using System.Runtime.ExceptionServices;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSRemoteWindowCaptureBoundary :
    INativeRemoteWindowCaptureBoundary
{
    private readonly object gate = new();
    private readonly MacOSRemoteWindowSourceCatalog catalog;
    private readonly long ownerGeneration;
    private CaptureOperation? operation;
    private bool stopped;
    private bool disposed;

    public MacOSRemoteWindowCaptureBoundary(
        MacOSRemoteWindowSourceCatalog catalog,
        long ownerGeneration = 1)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        this.ownerGeneration = ownerGeneration;
    }

    // StopNow confirms only local delivery closure. The owner must join this
    // task or DisposeAsync before releasing the borrowed platform resources.
    public Task StopCompletion
    {
        get
        {
            lock (gate)
            {
                return operation?.StopCompletion ?? Task.CompletedTask;
            }
        }
    }

    internal Task DeliveryCompletion
    {
        get
        {
            lock (gate)
            {
                return operation?.DeliveryCompletion ?? Task.CompletedTask;
            }
        }
    }

    public async ValueTask<LocalBoundaryResult> StartAsync(
        NativeRemoteWindowSourceUse sourceUse,
        INativeRemoteWindowFrameSink frameSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);
        ArgumentNullException.ThrowIfNull(frameSink);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceUse.OwnerGeneration != ownerGeneration)
        {
            return LocalBoundaryResult.Failed("macos_capture_owner_mismatch");
        }

        if (!catalog.TryAcquireNativeBinding(sourceUse,
            out MacOSRemoteWindowSourceCatalog.NativeBinding? binding)
            || binding is null)
        {
            return LocalBoundaryResult.Failed("macos_capture_source_stale");
        }

        CaptureOperation active;
        lock (gate)
        {
            if (disposed || stopped || operation is not null)
            {
                binding.Dispose();
                return LocalBoundaryResult.Failed("macos_capture_owner_closed");
            }

            active = new CaptureOperation(catalog, binding, sourceUse, frameSink);
            operation = active;
        }

        active.BeginStart();
        try
        {
            bool started = await active.StartCompletion.WaitAsync(
                cancellationToken).ConfigureAwait(false);
            return started
                ? LocalBoundaryResult.Confirmed("macos_exact_window_capture_started")
                : LocalBoundaryResult.Failed(active.FailureReason);
        }
        catch
        {
            active.CloseNow();
            throw;
        }
    }

    public LocalBoundaryResult PauseNow(MirrorPauseReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return StopNow();
    }

    public LocalBoundaryResult ResumeNow() =>
        LocalBoundaryResult.Failed("macos_capture_restart_required");

    public LocalBoundaryResult EmergencyStopNow() => StopNow();

    public LocalBoundaryResult StopNow()
    {
        CaptureOperation? active;
        lock (gate)
        {
            stopped = true;
            active = operation;
        }

        active?.CloseNow();
        return LocalBoundaryResult.Confirmed("macos_capture_delivery_closed");
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposed = true;
        }

        StopNow();
        Task completion = StopCompletion;
        // An active source/frame ancestor cannot synchronously join itself.
        // The isolated cleanup owner still performs the full drain; an external
        // disposer always observes that same immutable completion.
        return NativeRemoteWindowDrainActivityScope.HasActiveAncestry()
            && !completion.IsCompleted
                ? ValueTask.CompletedTask
                : new ValueTask(completion);
    }

    private sealed class CaptureOperation : IAsyncDisposable
    {
        private readonly object gate = new();
        private readonly MacOSRemoteWindowSourceCatalog catalog;
        private readonly MacOSRemoteWindowSourceCatalog.NativeBinding binding;
        private readonly NativeRemoteWindowSourceUse sourceUse;
        private readonly INativeRemoteWindowFrameSink destination;
        private readonly SemaphoreSlim sampleReady = new(0, 1);
        private readonly TaskCompletionSource<bool> startCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task worker;
        private NativeRemoteWindowSourceInvalidationRegistration? invalidation;
        private IMacOSRemoteWindowNativeCapture? nativeCapture;
        private IMacOSRemoteWindowNativeSample? pendingSample;
        private Task? stopCompletion;
        private Task? knownPendingCleanupRecovery;
        private Task? sourceRetirement;
        private Task? rejectedSampleRelease;
        private MacOSRemoteWindowSampleRetention? retainedFailedSamples;
        private bool sampleCleanupFailed;
        private Exception? fatalFailure;
        private string failureReason = "macos_capture_start_failed";
        private bool deliveryOpen;
        private bool closed;
        private bool signalPending;
        private long sequence;

        internal CaptureOperation(
            MacOSRemoteWindowSourceCatalog catalog,
            MacOSRemoteWindowSourceCatalog.NativeBinding binding,
            NativeRemoteWindowSourceUse sourceUse,
            INativeRemoteWindowFrameSink destination)
        {
            this.catalog = catalog;
            this.binding = binding;
            this.sourceUse = sourceUse;
            this.destination = destination;
            worker = RunIsolated(DeliverAsync);
        }

        public Task<bool> StartCompletion => startCompletion.Task;

        public Task DeliveryCompletion => worker;

        public string FailureReason => Volatile.Read(ref failureReason);

        public Task StopCompletion
        {
            get
            {
                lock (gate)
                {
                    return stopCompletion ?? Task.CompletedTask;
                }
            }
        }

        public void BeginStart() => _ = StartCoreAsync();

        public ValueTask DisposeAsync()
        {
            CloseNow();
            return new ValueTask(StopCompletion);
        }

        public void CloseNow()
        {
            IMacOSRemoteWindowNativeSample? rejected;
            lock (gate)
            {
                if (closed)
                {
                    return;
                }

                closed = true;
                deliveryOpen = false;
                rejected = pendingSample;
                pendingSample = null;
                if (rejected is not null)
                {
                    rejectedSampleRelease = RunIsolated(() =>
                    {
                        ReleaseSample(rejected);
                        return Task.CompletedTask;
                    });
                }

                SignalUnderGate();
                stopCompletion ??= RunIsolated(CleanupAsync);
            }
        }

        private async Task StartCoreAsync()
        {
            bool started = false;
            try
            {
                lock (gate)
                {
                    if (closed)
                    {
                        return;
                    }
                }

                if (!binding.IsCurrent
                    || !binding.Lease.TryRegisterInvalidationCallback(
                        CloseNow, out invalidation)
                    || !binding.Lease.TryAcquireUseScope(
                        sourceUse.SourceGeneration,
                        sourceUse.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? useScope)
                    || useScope is null)
                {
                    Fail("macos_capture_source_stale");
                    return;
                }

                using (useScope)
                {
                    if (!ReadCurrentNativeSource())
                    {
                        Fail("macos_capture_source_unavailable");
                        return;
                    }

                    lock (gate)
                    {
                        if (closed)
                        {
                            return;
                        }
                    }

                    nativeCapture = catalog.NativeApi.CreateCapture(
                        binding.Native,
                        TakeSampleOwnership,
                        SourceUnavailable);
                    bool nativeStarted = await nativeCapture.StartAsync()
                        .ConfigureAwait(false);
                    if (!nativeStarted || !ReadCurrentNativeSource())
                    {
                        Fail("macos_capture_start_unconfirmed");
                        return;
                    }

                    lock (gate)
                    {
                        if (!closed)
                        {
                            deliveryOpen = true;
                            started = true;
                            if (pendingSample is not null)
                            {
                                SignalUnderGate();
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                if (nativeCapture is null)
                {
                    try
                    {
                        if (catalog.NativeApi.TryTakeFailedCapture(exception,
                            out IMacOSRemoteWindowNativeCapture? failedCapture))
                        {
                            nativeCapture = failedCapture;
                        }
                    }
                    catch (Exception handoffFailure)
                    {
                        RecordFailure(handoffFailure);
                    }
                }
            }
            finally
            {
                if (fatalFailure is not null)
                {
                    startCompletion.TrySetException(fatalFailure);
                }
                else
                {
                    startCompletion.TrySetResult(started);
                }

                if (!started)
                {
                    CloseNow();
                }
            }
        }

        private bool ReadCurrentNativeSource() => binding.IsCurrent
            && catalog.NativeApi.IsSupported
            && catalog.NativeApi.PreflightCaptureAccess()
            && catalog.NativeApi.IsCurrent(binding.Native);

        private void TakeSampleOwnership(IMacOSRemoteWindowNativeSample sample)
        {
            ArgumentNullException.ThrowIfNull(sample);
            bool transferred = false;
            IMacOSRemoteWindowNativeSample? replaced = null;
            try
            {
                lock (gate)
                {
                    if (closed)
                    {
                        return;
                    }
                }

                if (!binding.IsCurrent
                    || !binding.Lease.TryAcquireUseScope(
                        sourceUse.SourceGeneration,
                        sourceUse.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? useScope)
                    || useScope is null)
                {
                    SourceUnavailable();
                    return;
                }

                using (useScope)
                {
                    if (!ReadCurrentNativeSource())
                    {
                        SourceUnavailable();
                        return;
                    }

                    lock (gate)
                    {
                        if (!closed)
                        {
                            replaced = pendingSample;
                            pendingSample = sample;
                            transferred = true;
                            // A static window may produce its only Complete
                            // sample before native Start settles. Retain the
                            // bounded latest slot, but grant no copy/delivery
                            // until Start succeeds and revalidates the source.
                            if (deliveryOpen)
                            {
                                SignalUnderGate();
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                CloseNow();
            }
            finally
            {
                ReleaseSample(replaced);
                if (!transferred)
                {
                    ReleaseSample(sample);
                }
            }
        }

        private async Task DeliverAsync()
        {
            while (true)
            {
                await sampleReady.WaitAsync().ConfigureAwait(false);
                IMacOSRemoteWindowNativeSample? sample;
                lock (gate)
                {
                    signalPending = false;
                    sample = pendingSample;
                    pendingSample = null;
                    if (sample is null && closed)
                    {
                        return;
                    }
                }

                if (sample is null)
                {
                    continue;
                }

                try
                {
                    using NativeRemoteWindowDrainActivityScope activity =
                        NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                    if (!binding.Lease.TryAcquireUseScope(
                        sourceUse.SourceGeneration,
                        sourceUse.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? useScope)
                        || useScope is null)
                    {
                        SourceUnavailable();
                        continue;
                    }

                    using (useScope)
                    {
                        if (!ReadCurrentNativeSource())
                        {
                            SourceUnavailable();
                            continue;
                        }

                        DeliverSample(sample);
                    }
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    CloseNow();
                }
                finally
                {
                    ReleaseSample(sample);
                }
            }
        }

        private void DeliverSample(IMacOSRemoteWindowNativeSample sample)
        {
            if (!sample.TryCopyPixels(out MacOSRemoteWindowPixelBuffer? pixels)
                || pixels is null)
            {
                return;
            }

            NativeRemoteWindowFrame? frame = null;
            try
            {
                frame = NativeRemoteWindowFrame.TakeOwnership(
                    pixels.Owner, pixels.Length, pixels.Width, pixels.Height,
                    pixels.Stride, NativeRemoteWindowPixelFormat.Bgra8888,
                    sourceUse.OwnerGeneration, sourceUse.SessionGeneration,
                    sourceUse.SourceGeneration, sourceUse.GeometryRevision,
                    checked(++sequence));
            }
            catch
            {
                pixels.Owner.Dispose();
                throw;
            }

            lock (gate)
            {
                if (closed || !deliveryOpen)
                {
                    frame.Dispose();
                    return;
                }
            }

            // Ownership transfers on entry even when the destination throws.
            destination.TakeOwnership(sourceUse, frame);
        }

        private void SourceUnavailable()
        {
            Fail("macos_capture_source_unavailable");
            lock (gate)
            {
                sourceRetirement ??= RunIsolated(() =>
                {
                    catalog.InvalidateBinding(binding);
                    return Task.CompletedTask;
                });
            }

            CloseNow();
        }

        private async Task CleanupAsync()
        {
            bool nativeDrained = false;
            try
            {
                try
                {
                    await startCompletion.Task.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }

                nativeDrained = nativeCapture is null
                    || await nativeCapture.StopAndDrainAsync().ConfigureAwait(false)
                    || nativeCapture.IsDrained;
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                nativeDrained = nativeCapture?.IsDrained ?? true;
            }

            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                nativeDrained = false;
            }
            if (rejectedSampleRelease is not null)
            {
                try
                {
                    await rejectedSampleRelease.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    nativeDrained = false;
                }
            }

            Task? retirement;
            lock (gate)
            {
                retirement = sourceRetirement;
            }

            if (retirement is not null)
            {
                try
                {
                    await retirement.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    nativeDrained = false;
                }
            }

            if (nativeDrained && !sampleCleanupFailed)
            {
                try
                {
                    using NativeRemoteWindowDrainActivityScope activity =
                        NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                    invalidation?.Dispose();
                    try
                    {
                        nativeCapture?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        RecordFailure(exception);
                        // A contained diagnostic does not erase complete native
                        // cleanup. Unknown/legacy owners provide no such proof,
                        // so their binding remains held without guessed retry.
                        if (nativeCapture?.IsCleanupConfirmed != true)
                        {
                            StartKnownPendingCleanupRecovery();
                            throw;
                        }
                    }
                    binding.Dispose();
                    sampleReady.Dispose();
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    nativeDrained = false;
                }
            }

            if (fatalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(fatalFailure).Throw();
            }

            if (!nativeDrained)
            {
                // Keep this operation, its native owner, source borrow, and
                // callback registration reachable through the boundary.
                throw new InvalidOperationException("macos_capture_cleanup_unconfirmed");
            }

            if (sampleCleanupFailed)
            {
                throw new InvalidOperationException("macos_capture_sample_cleanup_unconfirmed");
            }
        }

        private void SignalUnderGate()
        {
            if (!signalPending)
            {
                signalPending = true;
                sampleReady.Release();
            }
        }

        private void StartKnownPendingCleanupRecovery()
        {
            try
            {
                if (nativeCapture is null || !nativeCapture.TryGetKnownPendingCleanupJoin(out Task? join) || join is null)
                {
                    return;
                }

                lock (gate)
                {
                    knownPendingCleanupRecovery ??= RunIsolated(() => RecoverKnownPendingCleanupAsync(join));
                }
            }
            catch (Exception exception)
            {
                // Qualification/join/registration can fail before recovery
                // owns any work. Preserve the original owner and diagnosis.
                RecordFailure(exception);
            }
        }

        private async Task RecoverKnownPendingCleanupAsync(Task lifetimeJoin)
        {
            try
            {
                Task originalStop;
                lock (gate) { originalStop = stopCompletion!; }
                try { await originalStop.ConfigureAwait(false); }
                catch (Exception) { }

                // Original StopCompletion remains the same failed receipt.
                // CleanupAsync never waits on this independent recovery.
                await lifetimeJoin.ConfigureAwait(false);
                using NativeRemoteWindowDrainActivityScope activity =
                    NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                try { nativeCapture!.Dispose(); }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    if (!nativeCapture!.IsCleanupConfirmed) { throw; }
                }

                if (!nativeCapture!.IsCleanupConfirmed)
                {
                    throw new InvalidOperationException("macos_capture_cleanup_unconfirmed");
                }

                binding.Dispose();
                sampleReady.Dispose();
                if (fatalFailure is not null) { ExceptionDispatchInfo.Capture(fatalFailure).Throw(); }
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                throw;
            }
        }

        private void ReleaseSample(IMacOSRemoteWindowNativeSample? sample)
        {
            try
            {
                using NativeRemoteWindowDrainActivityScope activity =
                    NativeRemoteWindowDrainActivityScope.Enter(this, new object());
                sample?.Dispose();
            }
            catch (Exception exception)
            {
                // Keep the first fatal diagnostic and link the sample's
                // preallocated node. No secondary ledger allocation can lose
                // this or an earlier unconfirmed native sample owner.
                RecordFailure(exception);
                if (sample is not null)
                {
                    lock (gate)
                    {
                        sampleCleanupFailed = true;
                        MacOSRemoteWindowSampleRetention retention = sample.Retention;
                        if (!retention.IsLinked)
                        {
                            retention.Sample = sample;
                            retention.Next = retainedFailedSamples;
                            retention.IsLinked = true;
                            retainedFailedSamples = retention;
                        }
                    }
                }

                CloseNow();
            }
        }

        private void Fail(string reason) => Volatile.Write(ref failureReason, reason);

        private void RecordFailure(Exception exception)
        {
            OutOfMemoryException? fatal = MacOSRemoteWindowFailure.FindFatal(exception);
            if (fatal is not null)
            {
                Interlocked.CompareExchange(ref fatalFailure, fatal, null);
            }

            Fail("macos_capture_native_failed");
        }

        private static Task RunIsolated(Func<Task> action)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return Task.Run(action);
            }

            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(action);
            }
        }
    }
}
