using System.Runtime.ExceptionServices;
using Flowspan.Domain;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSRemoteWindowSourceCatalog :
    INativeRemoteWindowSourceCatalog,
    IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<MacOSRemoteWindowNativeIdentity, SourceEntry>
        entries = [];
    private readonly IMacOSRemoteWindowNativeApi nativeApi;
    private readonly NativeRemoteWindowSourceRegistry registry;
    private readonly SemaphoreSlim refreshGate = new(1);
    private readonly TimeProvider timeProvider;
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
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        this.nativeApi = nativeApi;
        this.timeProvider = timeProvider ?? TimeProvider.System;
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

            IReadOnlyList<IMacOSRemoteWindowNativeSource> sources =
                await nativeApi.EnumerateAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                DisposeNativeSources(sources);
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
                throw;
            }

            if (sourceCount > NativeRemoteWindowSourceRegistry.MaximumSources)
            {
                DisposeNativeSources(sources);
                RetireAll();
                return LocalBoundaryResult.Failed("macos_source_catalog_overflow");
            }

            RefreshSources(sources);
            cancellationToken.ThrowIfCancellationRequested();
            return LocalBoundaryResult.Confirmed("macos_source_catalog_refreshed");
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
        ThrowIfDisposed();
        return registry.TryAcquire(token, sourceGeneration, out lease);
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
            if (disposed)
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

            entry.Retain();
            binding = new NativeBinding(entry, lease);
            return true;
        }
    }

    private void RefreshSources(
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources)
    {
        HashSet<IMacOSRemoteWindowNativeSource>? ownedCandidates = null;
        Exception? failure = null;
        try
        {
            ownedCandidates = new HashSet<IMacOSRemoteWindowNativeSource>(
                sources,
                ReferenceEqualityComparer.Instance);
            RefreshOwnedSources(sources, ownedCandidates);
        }
        catch (Exception exception)
        {
            failure = exception;
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
                    DisposeNativeSources(sources);
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
        HashSet<IMacOSRemoteWindowNativeSource> ownedCandidates)
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
            bool published = false;
            try
            {
                lock (gate)
                {
                    if (disposed)
                    {
                        continue;
                    }

                    var metadata = NativeRemoteWindowSourceMetadata.Create(
                        "Remote Window", "Application", source.Geometry,
                        supportsCapture: true, supportsInput: false,
                        new ProtectionSnapshot(ProtectionKind.Unknown,
                            timeProvider.GetUtcNow(), "macos_protection_unverified"));
                    registration = registry.RegisterGeneric(metadata);
                    entries.Add(identity, new SourceEntry(source, registration, identity));
                    ownedCandidates.Remove(source);
                    published = true;
                }
            }
            finally
            {
                if (!published)
                {
                    registration?.Dispose();
                }
            }
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
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources)
    {
        bool failed = false;
        for (int index = 0; index < sources.Count; index++)
        {
            IMacOSRemoteWindowNativeSource source = sources[index];
            bool duplicate = false;
            for (int previous = 0; previous < index; previous++)
            {
                duplicate |= ReferenceEquals(sources[previous], source);
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
                failed = true;
            }
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
            lock (gate)
            {
                entry.NextRetained = retainedFailedEntries;
                retainedFailedEntries = entry;
            }
            RecordCleanupFailure(exception);
        }
    }

    private void RecordCleanupFailure(Exception exception)
    {
        lock (gate)
        {
            cleanupFailed = true;
            fatalFailure ??= MacOSRemoteWindowFailure.FindFatal(exception);
        }
    }

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
        try
        {
            RetireAll();
            registry.Dispose();
        }
        finally
        {
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
            && entry.Current && Lease.IsCurrent;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Lease.Dispose();
                entry.Release();
            }
        }
    }

    internal sealed class SourceEntry
    {
        private int references = 1;
        private int current = 1;

        internal SourceEntry(
            IMacOSRemoteWindowNativeSource native,
            NativeRemoteWindowSourceRegistration registration,
            MacOSRemoteWindowNativeIdentity identity)
        {
            Native = native;
            Registration = registration;
            Identity = identity;
        }

        public bool Current
        {
            get => Volatile.Read(ref current) != 0;
            set => Volatile.Write(ref current, value ? 1 : 0);
        }

        public IMacOSRemoteWindowNativeSource Native { get; }

        public MacOSRemoteWindowNativeIdentity Identity { get; }

        public NativeRemoteWindowSourceRegistration Registration { get; }

        internal SourceEntry? NextRetained { get; set; }

        public void Retain() => Interlocked.Increment(ref references);

        public void Retire()
        {
            try
            {
                Registration.Dispose();
            }
            finally
            {
                Release();
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref references) == 0)
            {
                using NativeRemoteWindowDrainActivityScope activity =
                    NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                Native.Dispose();
            }
        }
    }
}
