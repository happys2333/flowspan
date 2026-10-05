using System.Runtime.ExceptionServices;
using Flowspan.Domain;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSRemoteWindowSourceCatalog :
    INativeRemoteWindowSourceCatalog,
    IAsyncDisposable,
    IMacOSRemoteWindowSourceCreationFailureSink
{
    private readonly object gate = new();
    private readonly Dictionary<MacOSRemoteWindowNativeIdentity, SourceEntry>
        entries = [];
    private readonly IMacOSRemoteWindowNativeApi nativeApi;
    private readonly NativeRemoteWindowSourceRegistry registry;
    private readonly SemaphoreSlim refreshGate = new(1);
    private readonly TimeProvider timeProvider;
    private readonly MacOSRemoteWindowSourceOwnershipPool ownershipPool;
    private MacOSRemoteWindowSourceOwnershipPool.CatalogRecord? ownership;
    private SourceEntry? retainedFailedEntries;
    private readonly InvalidOperationException cleanupFailure =
        new("macos_source_cleanup_failed");
    private OutOfMemoryException? fatalFailure;
    private bool cleanupFailed;
    private object? retainedFailedBatch;
    private Task? disposal;
    private bool disposed;

    public MacOSRemoteWindowSourceCatalog(DeviceId hostDeviceId)
        : this(hostDeviceId, MacOSRemoteWindowScreenCaptureKitApi.Instance)
    {
    }

    internal MacOSRemoteWindowSourceCatalog(
        DeviceId hostDeviceId,
        IMacOSRemoteWindowNativeApi nativeApi,
        TimeProvider? timeProvider = null,
        MacOSRemoteWindowSourceOwnershipPool? ownershipPool = null)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        this.nativeApi = nativeApi;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.ownershipPool = ownershipPool ?? MacOSRemoteWindowSourceOwnershipPool.Shared;
        registry = new NativeRemoteWindowSourceRegistry(hostDeviceId);
    }

    public ValueTask<LocalBoundaryResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<LocalBoundaryResult> ownedRefresh;
        if (ExecutionContext.IsFlowSuppressed())
        {
            ownedRefresh = Task.Run(() => RefreshCoreAsync(cancellationToken));
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                ownedRefresh = Task.Run(() => RefreshCoreAsync(cancellationToken));
            }
        }

        // Cancellation ends this caller's wait. The owned operation retains its
        // gate and native completion until every returned source is settled.
        return new ValueTask<LocalBoundaryResult>(
            ownedRefresh.WaitAsync(cancellationToken));
    }

    private async Task<LocalBoundaryResult> RefreshCoreAsync(
        CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowCleanupFailure();
            if (!nativeApi.IsSupported)
            {
                RetireAll();
                return LocalBoundaryResult.Failed("macos_capture_unsupported");
            }

            if (!nativeApi.PreflightCaptureAccess())
            {
                RetireAll();
                return LocalBoundaryResult.Failed("macos_capture_permission_absent");
            }

            if (ownership is null
                && !ownershipPool.TryReserveCatalog(this, out ownership))
            {
                return LocalBoundaryResult.Failed("macos_source_ownership_capacity_exhausted");
            }
            if (!ownershipPool.TryReserveBatch(ownership!, out var batch))
            {
                return LocalBoundaryResult.Failed("macos_source_ownership_capacity_exhausted");
            }
            MacOSRemoteWindowSourceCreationContext? context = null;
            try
            {
                // This is the admission check after permission/reservation work.
                // A previously admitted operation owns its batch through cleanup.
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    ThrowCleanupFailure();
                }
                context = new MacOSRemoteWindowSourceCreationContext(ownershipPool, batch!);
                IReadOnlyList<IMacOSRemoteWindowNativeSource> sources =
                    await nativeApi.EnumerateAsync(context).ConfigureAwait(false);
                ownershipPool.AttachBatch(batch!, sources);
                if (cancellationToken.IsCancellationRequested)
                {
                    DisposeNativeSources(sources, batch!);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                int sourceCount;
                try
                {
                    sourceCount = sources.Count;
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        retainedFailedBatch = sources;
                    }
                    RecordCleanupFailure(exception);
                    ownershipPool.FailBatch(batch!);
                    throw;
                }

                if (sourceCount > NativeRemoteWindowSourceRegistry.MaximumSources)
                {
                    DisposeNativeSources(sources, batch!, sourceCount);
                    RetireAll();
                    return LocalBoundaryResult.Failed("macos_source_catalog_overflow");
                }

                RefreshSources(sources, sourceCount, batch!);
                cancellationToken.ThrowIfCancellationRequested();
                return LocalBoundaryResult.Confirmed("macos_source_catalog_refreshed");
            }
            finally
            {
                ownershipPool.CompleteBatch(batch!, context);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            OutOfMemoryException? originalFatal =
                MacOSRemoteWindowFailure.FindFatal(exception);
            if (originalFatal is not null)
            {
                lock (gate)
                {
                    fatalFailure ??= originalFatal;
                }
            }
            try
            {
                RetireAll();
            }
            catch (Exception cleanupException)
            {
                originalFatal ??= MacOSRemoteWindowFailure.FindFatal(cleanupException);
            }

            if (originalFatal is not null)
            {
                ExceptionDispatchInfo.Capture(originalFatal).Throw();
            }

            return LocalBoundaryResult.Failed("macos_source_catalog_unavailable");
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public IReadOnlyList<NativeRemoteWindowSourceSnapshot> GetSnapshot()
    {
        ThrowIfDisposed();
        return registry.GetSnapshot();
    }

    public bool TryAcquire(
        NativeRemoteWindowSourceToken token,
        long sourceGeneration,
        out NativeRemoteWindowSourceLease? lease)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cleanupFailed || fatalFailure is not null)
            {
                lease = null;
                return false;
            }
            return registry.TryAcquire(token, sourceGeneration, out lease);
        }
    }

    public ValueTask DisposeAsync()
    {
        Task completion;
        lock (gate)
        {
            disposed = true;
            foreach (SourceEntry entry in entries.Values)
            {
                entry.Current = false;
            }
            if (disposal is null)
            {
                if (ExecutionContext.IsFlowSuppressed())
                {
                    disposal = Task.Run(DisposeCoreAsync);
                }
                else
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        disposal = Task.Run(DisposeCoreAsync);
                    }
                }
            }

            completion = disposal;
        }

        return NativeRemoteWindowDrainActivityScope.HasActiveAncestry()
            && !completion.IsCompleted
                ? ValueTask.CompletedTask : new ValueTask(completion);
    }

    internal IMacOSRemoteWindowNativeApi NativeApi => nativeApi;

    internal void InvalidateBinding(NativeBinding binding)
    {
        SourceEntry? retired = null;
        lock (gate)
        {
            if (entries.TryGetValue(binding.Native.Identity, out SourceEntry? entry)
                && ReferenceEquals(entry.Native, binding.Native))
            {
                entry.Current = false;
                entries.Remove(entry.Native.Identity);
                retired = entry;
            }
        }

        if (retired is not null)
        {
            RetireEntry(retired);
            ThrowCleanupFailure();
        }
    }

    internal bool TryAcquireNativeBinding(
        NativeRemoteWindowSourceUse sourceUse,
        out NativeBinding? binding)
    {
        lock (gate)
        {
            binding = null;
            if (disposed || cleanupFailed || fatalFailure is not null)
            {
                return false;
            }

            SourceEntry? entry = entries.Values.FirstOrDefault(candidate =>
                candidate.Registration.Snapshot.Token.Equals(sourceUse.Token));
            if (entry is null
                || !entry.Current
                || !sourceUse.Matches(
                    entry.Registration.Snapshot,
                    requireGeometryRevision: true)
                || !registry.TryAcquire(
                    sourceUse.Token,
                    sourceUse.SourceGeneration,
                    out NativeRemoteWindowSourceLease? lease)
                || lease is null)
            {
                return false;
            }

            binding = new NativeBinding(entry, lease);
            entry.Retain();
            return true;
        }
    }

    private void RefreshSources(
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources,
        int sourceCount,
        MacOSRemoteWindowSourceOwnershipPool.BatchRecord batch)
    {
        HashSet<IMacOSRemoteWindowNativeSource>? ownedCandidates = null;
        Exception? failure = null;
        bool readCompleted = false;
        try
        {
            // Retain each known object before the next fallible list access.
            // A failed index is never revisited by compensating cleanup.
            for (int index = 0; index < sourceCount; index++)
            {
                batch.KnownSources[index] = sources[index];
                batch.KnownSourceCount++;
            }
            readCompleted = true;
            sources = new ArraySegment<IMacOSRemoteWindowNativeSource>(
                batch.KnownSources, 0, sourceCount);
            ownedCandidates = new HashSet<IMacOSRemoteWindowNativeSource>(
                sources,
                ReferenceEqualityComparer.Instance);
            RefreshOwnedSources(sources, ownedCandidates, batch);
        }
        catch (Exception exception)
        {
            failure = exception;
            if (!readCompleted)
            {
                ownershipPool.FailBatch(batch);
                RecordCleanupFailure(exception);
            }
            lock (gate)
            {
                fatalFailure ??= MacOSRemoteWindowFailure.FindFatal(exception);
                retainedFailedBatch = sources;
            }
        }
        finally
        {
            if (ownedCandidates is null)
            {
                try
                {
                    DisposeNativeSources(batch.KnownSources, batch, batch.KnownSourceCount);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            else
            {
                foreach (IMacOSRemoteWindowNativeSource candidate in ownedCandidates)
                {
                    try
                    {
                        using NativeRemoteWindowDrainActivityScope activity =
                            NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                        candidate.Dispose();
                    }
                    catch (Exception exception)
                    {
                        lock (gate)
                        {
                            retainedFailedBatch = sources;
                        }
                        RecordCleanupFailure(exception);
                        ownershipPool.FailBatch(batch);
                        failure ??= exception;
                    }
                }
            }
        }

        ThrowCleanupFailure();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void RefreshOwnedSources(
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources,
        HashSet<IMacOSRemoteWindowNativeSource> ownedCandidates,
        MacOSRemoteWindowSourceOwnershipPool.BatchRecord batch)
    {
        var candidates = sources.GroupBy(static source => source.Identity)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single());
        SourceEntry[] retired;
        lock (gate)
        {
            // Finish every fallible selection/allocation before detaching any
            // owner. A failed selection leaves the dictionary as the ledger.
            SourceEntry[] observed = entries.Values.ToArray();
            retired = observed.Where(entry => disposed
                || !candidates.TryGetValue(entry.Identity,
                    out IMacOSRemoteWindowNativeSource? candidate)
                || candidate.Geometry != entry.Native.Geometry).ToArray();
            foreach (SourceEntry entry in retired)
            {
                entry.Current = false;
                entries.Remove(entry.Identity);
                ownedCandidates.Remove(entry.Native);
            }

            foreach (SourceEntry entry in observed)
            {
                if (entry.Current)
                {
                    IMacOSRemoteWindowNativeSource candidate = candidates[entry.Identity];
                    candidates.Remove(entry.Identity);
                    if (ReferenceEquals(candidate, entry.Native))
                    {
                        ownedCandidates.Remove(candidate);
                    }
                }
            }
        }

        foreach (SourceEntry entry in retired)
        {
            RetireEntry(entry);
        }

        ThrowCleanupFailure();

        foreach ((MacOSRemoteWindowNativeIdentity identity,
            IMacOSRemoteWindowNativeSource source) in candidates)
        {
            NativeRemoteWindowSourceRegistration? registration = null;
            SourceEntry? pendingEntry = null;
            bool published = false;
            Exception? publicationFailure = null;
            try
            {
                lock (gate)
                {
                    if (disposed || cleanupFailed || fatalFailure is not null)
                    {
                        continue;
                    }

                    var metadata = NativeRemoteWindowSourceMetadata.Create(
                        "Remote Window", "Application", source.Geometry,
                        supportsCapture: true, supportsInput: false,
                        new ProtectionSnapshot(ProtectionKind.Unknown,
                            timeProvider.GetUtcNow(), "macos_protection_unverified"));
                    registration = registry.RegisterGeneric(metadata);
                    var record = ownershipPool.PrepareEntry(batch, source, registration);
                    pendingEntry = new SourceEntry(this, source, registration, identity, record);
                    ownershipPool.AttachEntry(record, pendingEntry);
                    entries.Add(identity, pendingEntry);
                    ownedCandidates.Remove(source);
                    ownershipPool.PublishEntry(batch, record);
                    published = true;
                }
            }
            catch (Exception exception)
            {
                publicationFailure = exception;
            }
            finally
            {
                if (!published)
                {
                    try
                    {
                        registration?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        ownershipPool.FailBatch(batch);
                        RecordCleanupFailure(exception, pendingEntry);
                        publicationFailure = MacOSRemoteWindowFailure.FindFatal(publicationFailure ?? exception)
                            ?? MacOSRemoteWindowFailure.FindFatal(exception)
                            ?? publicationFailure ?? exception;
                    }
                }
            }
            if (publicationFailure is not null) { ExceptionDispatchInfo.Capture(publicationFailure).Throw(); }
        }
    }

    private void RetireAll()
    {
        SourceEntry[] retired;
        lock (gate)
        {
            foreach (SourceEntry entry in entries.Values)
            {
                entry.Current = false;
            }
            retired = entries.Values.ToArray();
            entries.Clear();
        }

        foreach (SourceEntry entry in retired)
        {
            RetireEntry(entry);
        }

        ThrowCleanupFailure();
    }

    private void DisposeNativeSources(
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources,
        MacOSRemoteWindowSourceOwnershipPool.BatchRecord batch,
        int? knownCount = null)
    {
        bool failed = false;
        try
        {
            int sourceCount = knownCount ?? sources.Count;
            for (int index = 0; index < sourceCount; index++)
            {
                IMacOSRemoteWindowNativeSource source = sources[index];
                bool duplicate = false;
                // Once an owner has been read, no fallible prior list access
                // may prevent its independent cleanup. The producer is bounded;
                // its already allocated buffer is the reference-identity ledger.
                for (int previous = 0; previous < Math.Min(index, batch.KnownSources.Length); previous++)
                {
                    duplicate |= ReferenceEquals(batch.KnownSources[previous], source);
                }
                if (index < batch.KnownSources.Length)
                {
                    batch.KnownSources[index] = source;
                    batch.KnownSourceCount = Math.Max(batch.KnownSourceCount, index + 1);
                }

                lock (gate)
                {
                    duplicate |= entries.Values.Any(entry => ReferenceEquals(entry.Native, source));
                }

                if (duplicate)
                {
                    continue;
                }

                try
                {
                    using NativeRemoteWindowDrainActivityScope activity =
                        NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                    source.Dispose();
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        retainedFailedBatch = sources;
                    }
                    RecordCleanupFailure(exception);
                    ownershipPool.FailBatch(batch);
                    failed = true;
                }
            }
        }
        catch (Exception exception)
        {
            ownershipPool.FailBatch(batch);
            RecordCleanupFailure(exception);
            throw;
        }

        if (failed)
        {
            ThrowCleanupFailure();
        }
    }

    private void RetireEntry(SourceEntry entry)
    {
        try
        {
            entry.Retire();
        }
        catch (Exception exception)
        {
            // The entry itself is already allocated and retains its native
            // owner even when no new failure-ledger allocation is possible.
            RecordCleanupFailure(exception, entry);
        }
    }

    private void RecordCleanupFailure(Exception exception, SourceEntry? entry = null)
    {
        lock (gate)
        {
            cleanupFailed = true;
            fatalFailure ??= MacOSRemoteWindowFailure.FindFatal(exception);
            if (entry is not null && !entry.IsRetained)
            {
                ownershipPool.FailEntry(entry.Ownership, entry);
                entry.IsRetained = true;
                entry.NextRetained = retainedFailedEntries;
                retainedFailedEntries = entry;
            }
        }
    }

    void IMacOSRemoteWindowSourceCreationFailureSink.RecordProducerFailure(Exception exception) =>
        RecordCleanupFailure(exception);

    private void ThrowCleanupFailure()
    {
        lock (gate)
        {
            if (fatalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(fatalFailure).Throw();
            }

            if (cleanupFailed)
            {
                throw cleanupFailure;
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        await refreshGate.WaitAsync().ConfigureAwait(false);
        bool registryConfirmed = false;
        Exception? failure = null;
        try
        {
            try { RetireAll(); }
            catch (Exception exception) { failure = exception; }
            try
            {
                registry.Dispose();
                registryConfirmed = true;
            }
            catch (Exception exception)
            {
                RecordCleanupFailure(exception);
                failure = MacOSRemoteWindowFailure.FindFatal(failure ?? exception)
                    ?? MacOSRemoteWindowFailure.FindFatal(exception)
                    ?? failure ?? exception;
            }
            if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
        }
        finally
        {
            if (ownership is not null) { ownershipPool.CloseCatalog(ownership, registryConfirmed); }
            refreshGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }

    internal sealed class NativeBinding : IDisposable
    {
        private readonly SourceEntry entry;
        private int disposed;

        internal NativeBinding(
            SourceEntry entry,
            NativeRemoteWindowSourceLease lease)
        {
            this.entry = entry;
            Lease = lease;
        }

        public NativeRemoteWindowSourceLease Lease { get; }

        public IMacOSRemoteWindowNativeSource Native => entry.Native;

        public bool IsCurrent => Volatile.Read(ref disposed) == 0
            && entry.Current && !Volatile.Read(ref entry.Owner.cleanupFailed)
            && Lease.IsCurrent;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Exception? failure = null;
                try
                {
                    Lease.Dispose();
                }
                catch (Exception exception)
                {
                    entry.Owner.RecordCleanupFailure(exception, entry);
                    failure = exception;
                }
                try
                {
                    entry.Release();
                }
                catch (Exception exception)
                {
                    failure = MacOSRemoteWindowFailure.FindFatal(failure ?? exception)
                        ?? MacOSRemoteWindowFailure.FindFatal(exception)
                        ?? failure ?? exception;
                }
                if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            }
        }
    }

    internal sealed class SourceEntry
    {
        private int references = 1;
        private int current = 1;

        internal SourceEntry(
            MacOSRemoteWindowSourceCatalog owner,
            IMacOSRemoteWindowNativeSource native,
            NativeRemoteWindowSourceRegistration registration,
            MacOSRemoteWindowNativeIdentity identity,
            MacOSRemoteWindowSourceOwnershipPool.SourceRecord ownership)
        {
            Owner = owner;
            Native = native;
            Registration = registration;
            Identity = identity;
            Ownership = ownership;
        }

        public bool Current
        {
            get => Volatile.Read(ref current) != 0;
            set => Volatile.Write(ref current, value ? 1 : 0);
        }

        public IMacOSRemoteWindowNativeSource Native { get; }

        public MacOSRemoteWindowNativeIdentity Identity { get; }

        public NativeRemoteWindowSourceRegistration Registration { get; }

        internal MacOSRemoteWindowSourceCatalog Owner { get; }

        internal MacOSRemoteWindowSourceOwnershipPool.SourceRecord Ownership { get; }

        internal bool IsRetained { get; set; }

        internal SourceEntry? NextRetained { get; set; }

        public void Retain() => Interlocked.Increment(ref references);

        public void Retire()
        {
            Exception? failure = null;
            try
            {
                Registration.Dispose();
            }
            catch (Exception exception)
            {
                Owner.RecordCleanupFailure(exception, this);
                failure = exception;
            }
            try
            {
                Release();
            }
            catch (Exception exception)
            {
                failure = MacOSRemoteWindowFailure.FindFatal(failure ?? exception)
                    ?? MacOSRemoteWindowFailure.FindFatal(exception)
                    ?? failure ?? exception;
            }
            if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref references) == 0)
            {
                try
                {
                    using (NativeRemoteWindowDrainActivityScope activity =
                        NativeRemoteWindowDrainActivityScope.Enter(this, new object()))
                    {
                        Native.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    Owner.RecordCleanupFailure(exception, this);
                    throw;
                }
                Owner.ownershipPool.CompleteEntry(Ownership, this);
            }
        }
    }
}
