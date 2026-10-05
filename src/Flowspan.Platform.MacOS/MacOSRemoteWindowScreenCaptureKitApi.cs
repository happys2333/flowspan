using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using N = Flowspan.Platform.MacOS.MacOSRemoteWindowObjectiveCInterop;

namespace Flowspan.Platform.MacOS;

internal sealed class MacOSRemoteWindowScreenCaptureKitApi : IMacOSRemoteWindowNativeApi
{
    private const int MaximumSources = 128;
    private const int MaximumDimension = 16_384;
    private const long MaximumPixelBytes = 64 * 1024 * 1024;
    private readonly HashSet<uint> allowedOwnWindowIds;
    private readonly IMacOSRemoteWindowSourceOperations sourceOperations;
    private static readonly Lazy<Runtime> NativeRuntime = new(() => new Runtime());
    [ThreadStatic] private static Capture? failedFactoryCapture;

    internal static MacOSRemoteWindowScreenCaptureKitApi Instance { get; } = new();
    internal static int RetainedCaptureOwnerCount => Capture.RetainedOwnerCount;

    internal MacOSRemoteWindowScreenCaptureKitApi(IEnumerable<uint>? allowedOwnWindowIds = null)
    {
        // Internal harnesses may admit task-owned synthetic windows. Production
        // construction excludes this process without inspecting window names.
        this.allowedOwnWindowIds = allowedOwnWindowIds is null
            ? []
            : allowedOwnWindowIds.Take(MaximumSources).ToHashSet();
        sourceOperations = new NativeSourceOperations(this);
    }

    public bool IsSupported => OperatingSystem.IsMacOSVersionAtLeast(14, 2)
        && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    public bool PreflightCaptureAccess() => IsSupported
        && N.CGPreflightScreenCaptureAccess() != 0;

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync() =>
        WithDirectCreationContextAsync(MacOSRemoteWindowSourceOwnershipPool.Shared, EnumerateCoreAsync);

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync(
        MacOSRemoteWindowSourceCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return EnumerateCoreAsync(context);
    }

    private ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateCoreAsync(
        MacOSRemoteWindowSourceCreationContext context) =>
        EnumerateCoreAsync(context, new NativeEnumerationOperations(this));

    internal static ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateWithOperations(
        MacOSRemoteWindowSourceCreationContext context,
        IMacOSRemoteWindowEnumerationOperations operations)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        return EnumerateCoreAsync(context, operations);
    }

    internal static ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateDirectWithOperations(
        MacOSRemoteWindowSourceOwnershipPool ownershipPool,
        IMacOSRemoteWindowEnumerationOperations operations)
    {
        ArgumentNullException.ThrowIfNull(ownershipPool);
        ArgumentNullException.ThrowIfNull(operations);
        return WithDirectCreationContextAsync(ownershipPool,
            context => EnumerateCoreAsync(context, operations));
    }

    private static async ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateCoreAsync(
        MacOSRemoteWindowSourceCreationContext context,
        IMacOSRemoteWindowEnumerationOperations operations)
    {
        if (!operations.PreflightCaptureAccess() || !operations.HasExistingApplication())
        {
            return [];
        }

        var ownership = new MacOSRemoteWindowEnumerationOwnershipLedger(context, operations);
        context.Pool.PrepareEnumeration(ownership);
        operations.InitializeRuntime();
        var completion = new TaskCompletionSource<nint>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OutOfMemoryException? callbackFatal = null;
        OutOfMemoryException? firstFatal = null;
        var failure = new InvalidOperationException("macOS source enumeration unavailable.");
        void NotifyEnumerationFailure(Exception exception)
        {
            try { context.FailEnumeration(ownership, exception); }
            catch (Exception notificationFailure)
            {
                // The durable failure facts precede this external observer.
                // Its own fault cannot skip a result or independent cleanup.
                RecordFirstEnumerationFatal(ref firstFatal, notificationFailure);
                ownership.RecordFailure(notificationFailure);
            }
        }
        var block = operations.PrepareCompletion((content, error) =>
        {
            ownership.EnterInvocation();
            if (!ownership.TryAdmitCallback()) { return; }
            if (error != 0 || content == 0)
            {
                completion.TrySetException(failure);
                return;
            }

            ownership.BorrowedContent = content;
            ownership.ContentRetainAttempted = true;
            nint owner = operations.RetainContent(content);
            if (owner == 0 || owner != content)
            {
                throw failure;
            }
            ownership.ContentOwner = owner;
            ownership.ContentRetainConfirmed = true;
            // A concurrent invocation fault can win result completion while
            // this retain is in flight. The ledger still owns the confirmed
            // reference; only joined orchestration cleanup may release it.
            completion.TrySetResult(owner);
        }, exception =>
        {
            RecordFirstEnumerationFatal(ref firstFatal, exception);
            ownership.RecordFailure(exception);
            NotifyEnumerationFailure(exception);
            if (FindFatal(exception) is { } fatal)
            {
                Interlocked.CompareExchange(ref callbackFatal, fatal, null);
            }

            completion.TrySetException((Exception?)callbackFatal ?? failure);
        }, ownership.ExitInvocation);
        ownership.Completion = block;

        Exception? bodyFailure = null;
        Exception? poolCleanupFailure = null;
        bool accessRevoked = false;
        nint pool = 0;
        try
        {
            block.AcquireCopy();
            ownership.FirstPoolAcquireAttempted = true;
            pool = operations.PushAutoreleasePool();
            ownership.FirstPoolOwner = pool;
            ownership.FirstPoolAcquireConfirmed = true;
            // This API can prompt when permission is absent; the prompt-free
            // preflight is repeated immediately before its sole invocation.
            if (!operations.PreflightCaptureAccess())
            {
                accessRevoked = true;
            }
            else
            {
                ownership.DispatchAttempted = true;
                operations.Dispatch(block.Pointer);
                ownership.DispatchConfirmed = true;
            }
        }
        catch (Exception exception)
        {
            RecordFirstEnumerationFatal(ref firstFatal, exception);
            bodyFailure = exception;
            ownership.CloseCallbackAdmission();
            ownership.RecordFailure(exception);
            NotifyEnumerationFailure(exception);
        }
        finally
        {
            if (pool != 0)
            {
                ownership.FirstPoolReleaseAttempted = true;
                try
                {
                    operations.PopAutoreleasePool(pool);
                    ownership.FirstPoolReleaseConfirmed = true;
                }
                catch (Exception exception)
                {
                    RecordFirstEnumerationFatal(ref firstFatal, exception);
                    bodyFailure ??= exception;
                    poolCleanupFailure = exception;
                    ownership.CloseCallbackAdmission();
                    ownership.RecordFailure(exception);
                    NotifyEnumerationFailure(exception);
                }
            }
        }

        nint contentOwner = 0;
        List<IMacOSRemoteWindowNativeSource>? sources = null;
        Exception? contentCleanupFailure = null;
        Exception? completionCleanupFailure = null;
        Exception? sourceCleanupFailure = null;
        pool = 0;
        try
        {
            if (bodyFailure is not null)
            {
                ExceptionDispatchInfo.Capture(bodyFailure).Throw();
            }
            if (accessRevoked)
            {
                ownership.CloseCallbackAdmission();
                sources = [];
            }
            else
            {
                await Task.WhenAny(completion.Task, ownership.FailureObserved).ConfigureAwait(false);
                if (!completion.Task.IsCompleted && ownership.Failure is { } resultFailure)
                {
                    ExceptionDispatchInfo.Capture(resultFailure).Throw();
                }
                contentOwner = await completion.Task.ConfigureAwait(false);
                await ownership.InvocationsExited.ConfigureAwait(false);
                if (Volatile.Read(ref callbackFatal) is { } fatal)
                {
                    ExceptionDispatchInfo.Capture(fatal).Throw();
                }

                ownership.SecondPoolAcquireAttempted = true;
                pool = operations.PushAutoreleasePool();
                ownership.SecondPoolOwner = pool;
                ownership.SecondPoolAcquireConfirmed = true;
                sources = [];
                nint windows = operations.GetWindows(contentOwner);
                nuint count = Math.Min(operations.GetWindowCount(windows), MaximumSources);
                for (nuint index = 0; index < count; index++)
                {
                    nint window = operations.GetWindow(windows, index);
                    NativeSource? source = CreateSourceCore(window, operations.SourceCreationOperations, context);
                    if (source is not null)
                    {
                        try
                        {
                            sources.Add(source);
                        }
                        catch
                        {
                            source.Dispose();
                            throw;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            RecordFirstEnumerationFatal(ref firstFatal, exception);
            bodyFailure = exception;
            ownership.CloseCallbackAdmission();
            context.Close();
            if (ownership.SecondPoolAcquireAttempted && !ownership.SecondPoolAcquireConfirmed)
            {
                ownership.RecordFailure(exception);
                NotifyEnumerationFailure(exception);
            }
            if (ownership.HasAdmittedCallback)
            {
                await ownership.InvocationsExited.ConfigureAwait(false);
            }
            if (ownership.ContentRetainConfirmed)
            {
                contentOwner = ownership.ContentOwner;
            }

            if (Volatile.Read(ref callbackFatal) is { } fatal)
            {
                bodyFailure = fatal;
            }
            else if (FindFatal(exception) is { } capturedFatal)
            {
                bodyFailure = capturedFatal;
            }
        }
        finally
        {
            if (contentOwner != 0)
            {
                ownership.ContentReleaseAttempted = true;
                try
                {
                    operations.ReleaseContent(contentOwner);
                    ownership.ContentReleaseConfirmed = true;
                }
                catch (Exception exception)
                {
                    RecordFirstEnumerationFatal(ref firstFatal, exception);
                    ownership.RecordFailure(exception);
                    NotifyEnumerationFailure(exception);
                    contentCleanupFailure = exception;
                }
            }
            if (pool != 0)
            {
                ownership.SecondPoolReleaseAttempted = true;
                try
                {
                    operations.PopAutoreleasePool(pool);
                    ownership.SecondPoolReleaseConfirmed = true;
                }
                catch (Exception exception)
                {
                    RecordFirstEnumerationFatal(ref firstFatal, exception);
                    ownership.RecordFailure(exception);
                    NotifyEnumerationFailure(exception);
                    poolCleanupFailure = exception;
                }
            }
        }
        ownership.CompletionReleaseAttempted = true;
        try
        {
            block.Dispose();
            if (!block.IsReleased) { throw failure; }
            ownership.CompletionReleaseConfirmed = true;
            // Native helper faults may be contained by the ABI rather than
            // thrown by Dispose. A confirmed caller reference release remains
            // a separate fact from the completion's retained diagnosis.
            if (block.FirstFailure is { } reportedFailure)
            {
                RecordFirstEnumerationFatal(ref firstFatal, reportedFailure);
                ownership.CloseCallbackAdmission();
                ownership.RecordFailure(reportedFailure);
                NotifyEnumerationFailure(reportedFailure);
                completionCleanupFailure = reportedFailure;
            }
        }
        catch (Exception exception)
        {
            RecordFirstEnumerationFatal(ref firstFatal, exception);
            ownership.CloseCallbackAdmission();
            ownership.RecordFailure(exception);
            NotifyEnumerationFailure(exception);
            completionCleanupFailure = exception;
        }
        bool sourceCleanupSelected = false;
        void CleanupConfirmedSources()
        {
            if (sourceCleanupSelected || sources is null) { return; }
            sourceCleanupSelected = true;
            foreach (IMacOSRemoteWindowNativeSource source in sources)
            {
                try { source.Dispose(); }
                catch (Exception exception)
                {
                    RecordFirstEnumerationFatal(ref firstFatal, exception);
                    sourceCleanupFailure ??= exception;
                    ownership.RecordFailure(exception);
                    NotifyEnumerationFailure(exception);
                }
            }
        }
        if (bodyFailure is not null || contentCleanupFailure is not null || poolCleanupFailure is not null
            || completionCleanupFailure is not null)
        {
            // Known independent source obligations cannot wait for a native
            // API's last retained completion copy to return.
            CleanupConfirmedSources();
        }
        if (ownership.CompletionReleaseConfirmed && ownership.Failure is null)
        {
            // Caller ownership must be consumed first: otherwise its own +1
            // would prevent the last physical capture from ever retiring.
            await Task.WhenAny(block.NativeCaptureRetirement, ownership.FailureObserved).ConfigureAwait(false);
            if (ownership.Failure is null)
            {
                await Task.WhenAny(block.ManagedInvocationDrain, ownership.FailureObserved).ConfigureAwait(false);
            }
        }
        if ((block.FirstFailure ?? ownership.Failure) is { } lifetimeFailure)
        {
            RecordFirstEnumerationFatal(ref firstFatal, lifetimeFailure);
            ownership.CloseCallbackAdmission();
            ownership.RecordFailure(lifetimeFailure);
            NotifyEnumerationFailure(lifetimeFailure);
            completionCleanupFailure ??= lifetimeFailure;
        }
        if ((bodyFailure is not null || contentCleanupFailure is not null || poolCleanupFailure is not null
            || completionCleanupFailure is not null)
            && sources is not null)
        {
            CleanupConfirmedSources();
        }
        if (ownership.Failure is null && ownership.CompletionReleaseConfirmed
            && block.NativeCaptureRetirement.IsCompletedSuccessfully
            && block.ManagedInvocationDrain.IsCompletedSuccessfully)
        {
            ownership.CloseCallbackAdmission();
            ownership.EndEnumerationLifecycle();
        }
        OutOfMemoryException? selectedFatal = Volatile.Read(ref firstFatal);
        if (selectedFatal is not null)
        {
            ExceptionDispatchInfo.Capture(selectedFatal).Throw();
        }
        if (bodyFailure is not null || contentCleanupFailure is not null
            || poolCleanupFailure is not null || completionCleanupFailure is not null
            || sourceCleanupFailure is not null)
        {
            throw failure;
        }
        return sources ?? throw failure;
    }

    private static void RecordFirstEnumerationFatal(
        ref OutOfMemoryException? firstFatal, Exception exception)
    {
        if (FindFatal(exception) is { } fatal)
        {
            Interlocked.CompareExchange(ref firstFatal, fatal, null);
        }
    }

    public bool IsCurrent(IMacOSRemoteWindowNativeSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not NativeSource nativeSource)
        {
            return false;
        }

        return nativeSource.CheckCurrent();
    }

    public IMacOSRemoteWindowNativeCapture CreateCapture(
        IMacOSRemoteWindowNativeSource source,
        Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
        Action sourceUnavailable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(takeSampleOwnership);
        ArgumentNullException.ThrowIfNull(sourceUnavailable);
        if (!IsCurrent(source) || source is not NativeSource nativeSource)
        {
            throw new InvalidOperationException("macOS exact window unavailable.");
        }

        return new Capture(this, new NativeCaptureOperations(NativeRuntime.Value),
            new NativeCaptureSource(this, nativeSource),
            takeSampleOwnership, sourceUnavailable);
    }

    internal IMacOSRemoteWindowNativeCapture CreateCaptureWithOperations(
        IMacOSRemoteWindowNativeSource source,
        IMacOSRemoteWindowCaptureOperations operations,
        Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
        Action sourceUnavailable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(takeSampleOwnership);
        ArgumentNullException.ThrowIfNull(sourceUnavailable);
        if (!IsCurrent(source) || source is not NativeSource nativeSource)
        {
            throw new InvalidOperationException("macOS exact window unavailable.");
        }

        return new Capture(this, operations, new NativeCaptureSource(this, nativeSource),
            takeSampleOwnership, sourceUnavailable);
    }

    internal IMacOSRemoteWindowNativeCapture CreateCaptureWithOperations(
        IMacOSRemoteWindowCaptureSource source,
        IMacOSRemoteWindowCaptureOperations operations,
        Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
        Action sourceUnavailable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(takeSampleOwnership);
        ArgumentNullException.ThrowIfNull(sourceUnavailable);
        if (!source.IsCurrent())
        {
            throw new InvalidOperationException("macOS exact window unavailable.");
        }

        return new Capture(this, operations, source, takeSampleOwnership, sourceUnavailable);
    }

    internal IMacOSRemoteWindowCaptureSource CreateCaptureSourceWithOperations(
        MacOSRemoteWindowNativeIdentity identity, NativeRemoteWindowGeometry geometry,
        nint windowOwner, nint filterOwner, IMacOSRemoteWindowSourceOperations operations)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(operations);
        return new NativeCaptureSource(this,
            new NativeSource(identity, geometry, windowOwner, filterOwner, operations));
    }

    internal static void DeliverCaptureSample(nint output, nint stream, nint sample, nint kind) =>
        Capture.ProcessOutput(output, stream, sample, kind);

    internal static IMacOSRemoteWindowNativeSource CreateNativeSourceWithOperations(
        MacOSRemoteWindowNativeIdentity identity, NativeRemoteWindowGeometry geometry,
        nint windowOwner, nint filterOwner, IMacOSRemoteWindowSourceOperations operations)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(operations);
        return new NativeSource(identity, geometry, windowOwner, filterOwner, operations);
    }

    public bool TryTakeFailedCapture(Exception failure,
        out IMacOSRemoteWindowNativeCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Capture? pending = failedFactoryCapture;
        capture = null;
        if (pending is null || !ReferenceEquals(pending.Api, this)
            || !pending.MatchesFactoryFailure(failure))
        {
            return false;
        }

        failedFactoryCapture = null;
        capture = pending;
        return true;
    }

    private static bool HasExistingApplication()
    {
        // Loading the framework does not create NSApplication, run its loop,
        // call NSApplicationLoad, or alter the host's activation policy.
        nint appKit = NativeLibrary.Load(N.AppKit);
        return NativeLibrary.TryGetExport(appKit, "NSApp", out nint pointer)
            && Marshal.ReadIntPtr(pointer) != 0;
    }

    internal static IMacOSRemoteWindowNativeSource? CreateSourceWithOperations(
        MacOSRemoteWindowSourceCreationContext context, nint window,
        IMacOSRemoteWindowSourceCreationOperations operations)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        return CreateSourceCore(window, operations, context);
    }

    internal static ValueTask<IMacOSRemoteWindowNativeSource?> CreateDirectSourceWithOperations(
        MacOSRemoteWindowSourceOwnershipPool ownershipPool, nint window,
        IMacOSRemoteWindowSourceCreationOperations operations)
    {
        ArgumentNullException.ThrowIfNull(ownershipPool);
        ArgumentNullException.ThrowIfNull(operations);
        return WithDirectCreationContextAsync(ownershipPool, context =>
            ValueTask.FromResult<IMacOSRemoteWindowNativeSource?>(CreateSourceCore(window, operations, context)));
    }

    private static async ValueTask<T> WithDirectCreationContextAsync<T>(
        MacOSRemoteWindowSourceOwnershipPool ownershipPool,
        Func<MacOSRemoteWindowSourceCreationContext, ValueTask<T>> body)
    {
        var lifetime = new DirectSourceLifetime();
        var unavailable = new InvalidOperationException("macos_source_producer_unavailable");
        if (!ownershipPool.TryReserveCatalog(lifetime, out var owner))
        {
            throw new InvalidOperationException("macos_source_ownership_capacity_exhausted");
        }
        MacOSRemoteWindowSourceOwnershipPool.BatchRecord? batch = null;
        MacOSRemoteWindowSourceCreationContext? context = null;
        try
        {
            if (!ownershipPool.TryReserveBatch(owner!, out batch))
            {
                throw new InvalidOperationException("macos_source_ownership_capacity_exhausted");
            }
            context = new(ownershipPool, batch!);
            try
            {
                return await body(context).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (FindFatal(exception) is { } fatal) { ExceptionDispatchInfo.Capture(fatal).Throw(); }
                throw unavailable;
            }
        }
        finally
        {
            if (batch is not null) { ownershipPool.CompleteBatch(batch, context); }
            ownershipPool.CloseCatalog(owner!, registryConfirmed: true);
        }
    }

    // The pool owns the direct lifetime and all its debt. Context.Fail already
    // closes admission and marks the exact source/batch; no catalog registry or
    // fallible/native work is needed for this stable sink.
    private sealed class DirectSourceLifetime : IMacOSRemoteWindowSourceCreationFailureSink
    {
        public void RecordProducerFailure(Exception failure) { }
    }

    private static NativeSource? CreateSourceCore(nint window,
        IMacOSRemoteWindowSourceCreationOperations operations,
        MacOSRemoteWindowSourceCreationContext context)
    {
        if (!operations.TryGetIdentity(window, out MacOSRemoteWindowNativeIdentity identity))
        {
            return null;
        }

        var ledger = new MacOSRemoteWindowSourceCreationLedger(context, window, operations);
        ledger.Record = context.Pool.PrepareCreation(context.Batch, ledger);
        nint filter = 0;
        nint windowOwner = 0;
        OutOfMemoryException? originalFatal = null;
        bool handedOff = false;
        try
        {
            ledger.AllocationAttempted = true;
            nint allocatedFilter = operations.AllocateFilter();
            ledger.AllocatedFilter = allocatedFilter;
            ledger.AllocationConfirmed = true;
            if (allocatedFilter == 0) { return null; }
            ledger.InitializationAttempted = true;
            filter = operations.InitializeFilter(allocatedFilter, window);
            ledger.Filter = filter;
            ledger.InitializationConfirmed = true;
            if (filter == 0) { return null; }
            var frame = operations.GetFrame(window);
            float scale = operations.GetScale(filter);
            NativeRemoteWindowGeometry geometry;
            try
            {
                geometry = NativeRemoteWindowGeometry.Create(
                    frame.X, frame.Y, frame.Width, frame.Height, scale);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }

            if (!TryDimensions(geometry, out _, out _)
                || !operations.ValidateWindow(identity, geometry))
            {
                return null;
            }

            ledger.WindowRetainAttempted = true;
            nint retainedWindow = operations.Retain(window);
            ledger.Window = retainedWindow;
            ledger.WindowRetainConfirmed = retainedWindow == window && retainedWindow != 0;
            if (retainedWindow != window || retainedWindow == 0)
            {
                throw new InvalidOperationException("macOS native window retain unconfirmed.");
            }
            windowOwner = retainedWindow;
            var source = new NativeSource(identity, geometry, windowOwner, filter, operations,
                creationLedger: ledger);
            context.Pool.AttachCreatedSource(ledger, source);
            windowOwner = 0;
            filter = 0;
            handedOff = true;
            return source;
        }
        catch (Exception exception)
        {
            originalFatal = FindFatal(exception);
            ledger.Failure = exception;
            if (ledger.HasUnconfirmedAcquisition)
            {
                try { context.Fail(ledger, exception); }
                catch (Exception notificationFailure)
                {
                    originalFatal ??= FindFatal(notificationFailure);
                }
            }
            if (originalFatal is not null)
            {
                ExceptionDispatchInfo.Capture(originalFatal).Throw();
            }
            throw;
        }
        finally
        {
            Exception? cleanupFailure = null;
            OutOfMemoryException? cleanupFatal = null;
            ReleaseCreatedOwner(windowOwner, isFilter: false, operations, ledger,
                ref cleanupFailure, ref cleanupFatal);
            ReleaseCreatedOwner(filter, isFilter: true, operations, ledger,
                ref cleanupFailure, ref cleanupFatal);
            if (cleanupFailure is not null)
            {
                Exception selected = (Exception?)originalFatal ?? cleanupFatal ?? cleanupFailure;
                ledger.Failure ??= cleanupFailure;
                try { context.Fail(ledger, selected); }
                catch (Exception notificationFailure)
                {
                    if (FindFatal(selected) is null && FindFatal(notificationFailure) is { } notificationFatal)
                    {
                        selected = notificationFatal;
                    }
                }
                ExceptionDispatchInfo.Capture(selected).Throw();
            }
            if (!handedOff && ledger.InitialOwnersCleaned)
            {
                context.Pool.CompleteCreatedSource(ledger, native: null);
            }
        }
    }

    private static void ReleaseCreatedOwner(nint owner, bool isFilter,
        IMacOSRemoteWindowSourceCreationOperations operations,
        MacOSRemoteWindowSourceCreationLedger ledger,
        ref Exception? cleanupFailure, ref OutOfMemoryException? cleanupFatal)
    {
        if (owner == 0) { return; }
        if (isFilter) { ledger.FilterReleaseAttempted = true; }
        else { ledger.WindowReleaseAttempted = true; }
        try
        {
            operations.Release(owner);
            if (isFilter) { ledger.FilterReleaseConfirmed = true; }
            else { ledger.WindowReleaseConfirmed = true; }
        }
        catch (Exception exception)
        {
            cleanupFailure ??= exception;
            cleanupFatal ??= FindFatal(exception);
        }
    }

    private static bool TryDimensions(NativeRemoteWindowGeometry geometry,
        out int width, out int height)
    {
        double pixelWidth = Math.Ceiling(geometry.Width * geometry.ScaleFactor);
        double pixelHeight = Math.Ceiling(geometry.Height * geometry.ScaleFactor);
        width = 0;
        height = 0;
        if (!double.IsFinite(pixelWidth) || !double.IsFinite(pixelHeight)
            || pixelWidth is < 1 or > MaximumDimension
            || pixelHeight is < 1 or > MaximumDimension
            || pixelWidth * pixelHeight * 4 > MaximumPixelBytes)
        {
            return false;
        }

        width = (int)pixelWidth;
        height = (int)pixelHeight;
        return true;
    }

    private static bool TryProcessIdentity(uint windowId, int processId,
        out MacOSRemoteWindowNativeIdentity identity)
    {
        identity = default;
        const int size = 136;
        if (N.proc_pidinfo(processId, 3, 0, out MacOSProcessInstanceInfo info, size) != size
            || info.ProcessId != (uint)processId || info.StartSeconds == 0
            || info.StartMicroseconds >= 1_000_000)
        {
            return false;
        }

        identity = new(windowId, processId, info.StartSeconds, info.StartMicroseconds);
        return true;
    }

    private static bool CheckWindowInfo(Runtime runtime,
        MacOSRemoteWindowNativeIdentity identity, NativeRemoteWindowGeometry geometry)
    {
        if (!TryProcessIdentity(identity.WindowId, identity.ProcessId, out var current)
            || current != identity)
        {
            return false;
        }

        nint info = N.CGWindowListCopyWindowInfo(1 << 3, identity.WindowId);
        if (info == 0)
        {
            return false;
        }

        try
        {
            nint count = N.CFArrayGetCount(info);
            if (count != 1)
            {
                return false;
            }

            nint dictionary = N.CFArrayGetValueAtIndex(info, 0);
            nint onScreen = N.CFDictionaryGetValue(dictionary, runtime.WindowOnScreenKey);
            nint bounds = N.CFDictionaryGetValue(dictionary, runtime.WindowBoundsKey);
            return ReadNumber(dictionary, runtime.WindowNumberKey, out long number)
                && number == identity.WindowId
                && ReadNumber(dictionary, runtime.WindowProcessKey, out long processId)
                && processId == identity.ProcessId
                && ReadNumber(dictionary, runtime.WindowLayerKey, out long layer) && layer == 0
                && onScreen != 0 && N.CFBooleanGetValue(onScreen) != 0
                && bounds != 0 && N.CGRectMakeWithDictionaryRepresentation(bounds, out var frame) != 0
                && frame.X == geometry.X && frame.Y == geometry.Y
                && frame.Width == geometry.Width && frame.Height == geometry.Height
                && TryProcessIdentity(identity.WindowId, identity.ProcessId, out current)
                && current == identity;
        }
        finally
        {
            N.CFRelease(info);
        }
    }

    private static bool ReadNumber(nint dictionary, nint key, out long number)
    {
        nint value = N.CFDictionaryGetValue(dictionary, key);
        number = 0;
        return value != 0 && N.CFNumberGetValue(value, 4, out number) != 0;
    }

    private static OutOfMemoryException? FindFatal(Exception exception)
    {
        if (exception is OutOfMemoryException fatal)
        {
            return fatal;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                if (FindFatal(inner) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }

        return exception.InnerException is { } cause ? FindFatal(cause) : null;
    }

    private sealed class NativeSourceOperations(MacOSRemoteWindowScreenCaptureKitApi api)
        : IMacOSRemoteWindowSourceOperations
    {
        public nint Retain(nint owner) => N.objc_retain(owner);
        public void Release(nint owner) => N.objc_release(owner);
        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => api.PreflightCaptureAccess()
                && N.GetFloat(filter, N.Sel("pointPixelScale")) == geometry.ScaleFactor
                && CheckWindowInfo(NativeRuntime.Value, identity, geometry);
    }

    private sealed class NativeEnumerationOperations(MacOSRemoteWindowScreenCaptureKitApi api)
        : IMacOSRemoteWindowEnumerationOperations
    {
        private Runtime? runtime;
        public bool PreflightCaptureAccess() => api.PreflightCaptureAccess();
        public bool HasExistingApplication() => MacOSRemoteWindowScreenCaptureKitApi.HasExistingApplication();
        public void InitializeRuntime() => runtime = NativeRuntime.Value;
        public IMacOSRemoteWindowEnumerationCompletion PrepareCompletion(
            Action<nint, nint> action, Action<Exception> failure, Action completed) =>
            MacOSRemoteWindowEnumerationCompletion.Prepare(action, failure, completed);
        public nint PushAutoreleasePool() => N.objc_autoreleasePoolPush();
        public void PopAutoreleasePool(nint pool) => N.objc_autoreleasePoolPop(pool);
        public void Dispatch(nint completion) => N.Enumerate(runtime!.ShareableClass,
            N.Sel("getShareableContentExcludingDesktopWindows:onScreenWindowsOnly:completionHandler:"),
            1, 1, completion);
        public nint RetainContent(nint content) => N.objc_retain(content);
        public void ReleaseContent(nint content) => N.objc_release(content);
        public nint GetWindows(nint content) => N.Send0(content, N.Sel("windows"));
        public nuint GetWindowCount(nint windows) => N.GetNUInt(windows, N.Sel("count"));
        public nint GetWindow(nint windows, nuint index) =>
            N.SendIndex(windows, N.Sel("objectAtIndex:"), index);
        public IMacOSRemoteWindowSourceCreationOperations SourceCreationOperations =>
            new NativeSourceCreationOperations(api, runtime!);
    }

    private sealed class NativeSourceCreationOperations(
        MacOSRemoteWindowScreenCaptureKitApi api, Runtime runtime)
        : IMacOSRemoteWindowSourceCreationOperations
    {
        public bool TryGetIdentity(nint window, out MacOSRemoteWindowNativeIdentity identity)
        {
            identity = default;
            if (window == 0 || N.GetByte(window, N.Sel("isOnScreen")) == 0
                || N.GetNInt(window, N.Sel("windowLayer")) != 0) { return false; }
            uint windowId = N.GetUInt(window, N.Sel("windowID"));
            nint application = N.Send0(window, N.Sel("owningApplication"));
            int processId = N.GetInt(application, N.Sel("processID"));
            return windowId != 0 && application != 0 && processId > 0
                && (processId != Environment.ProcessId || api.allowedOwnWindowIds.Contains(windowId))
                && TryProcessIdentity(windowId, processId, out identity);
        }
        public nint AllocateFilter() => N.Send0(runtime.FilterClass, N.Sel("alloc"));
        public nint InitializeFilter(nint allocatedFilter, nint window) =>
            N.Send1(allocatedFilter, N.Sel("initWithDesktopIndependentWindow:"), window);
        public (double X, double Y, double Width, double Height) GetFrame(nint window)
        {
            MacOSCaptureRect frame = N.GetRect(window, N.Sel("frame"));
            return (frame.X, frame.Y, frame.Width, frame.Height);
        }
        public float GetScale(nint filter) => N.GetFloat(filter, N.Sel("pointPixelScale"));
        public bool ValidateWindow(MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => CheckWindowInfo(runtime, identity, geometry);
        public nint Retain(nint owner) => N.objc_retain(owner);
        public void Release(nint owner) => N.objc_release(owner);
        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => api.sourceOperations.CheckCurrent(filter, identity, geometry);
    }

    private sealed class NativeSource(
        MacOSRemoteWindowNativeIdentity identity,
        NativeRemoteWindowGeometry geometry,
        nint windowOwner,
        nint filterOwner, IMacOSRemoteWindowSourceOperations operations,
        NativeSource? acquisitionSource = null,
        MacOSRemoteWindowSourceCreationLedger? creationLedger = null) : IMacOSRemoteWindowNativeSource
    {
        private readonly object gate = new();
        private readonly NativeSource? parent = acquisitionSource;
        private nint window = windowOwner;
        private nint filter = filterOwner;
        private nint windowRetainResult;
        private nint filterRetainResult;
        private bool windowRetainAttempted;
        private bool filterRetainAttempted;
        // A base source arrives with its caller-owned native references.
        // Prepared tokens only borrow them until each retain is confirmed.
        private bool windowRetainConfirmed = acquisitionSource is null;
        private bool filterRetainConfirmed = acquisitionSource is null;
        private bool windowReleaseAttempted;
        private bool filterReleaseAttempted;
        private bool acquisitionStarted;
        private bool closing;
        private bool disposing;
        private int activeUses;
        private readonly TaskCompletionSource<bool> usesExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? ownerFailure;
        private OutOfMemoryException? ownerFatal;
        private readonly InvalidOperationException releaseUnconfirmed =
            new("macOS native source release unconfirmed.");
        public MacOSRemoteWindowNativeIdentity Identity { get; } = identity;
        public NativeRemoteWindowGeometry Geometry { get; } = geometry;

        internal nint Filter
        {
            get
            {
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(closing || filter == 0, this);
                    if (parent is not null && (!windowRetainConfirmed || !filterRetainConfirmed))
                    {
                        throw new InvalidOperationException("macOS native source acquisition unconfirmed.");
                    }
                    return filter;
                }
            }
        }

        internal NativeSource PrepareOwner()
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closing || window == 0 || filter == 0
                    || (parent is not null && (!windowRetainConfirmed || !filterRetainConfirmed)), this);
                return new(Identity, Geometry, window, filter, operations, this);
            }
        }

        internal void AcquireOwner()
        {
            if (parent is null) { throw new InvalidOperationException("macOS native source acquisition unavailable."); }
            ObjectDisposedException.ThrowIf(
                !parent.TryEnterUse(out nint selectedWindow, out nint selectedFilter), parent);
            bool ownerUseEntered = false;
            try
            {
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(closing, this);
                    if (acquisitionStarted) { throw new InvalidOperationException("macOS native source acquisition already attempted."); }
                    acquisitionStarted = true;
                    activeUses++;
                    ownerUseEntered = true;
                }

                // Both borrowed addresses and the staged ledger stay pinned.
                // Managed scope setup precedes the first native-attempt fact.
                using var parentActivity = NativeRemoteWindowDrainActivityScope.Enter(parent, this);
                using var ownerActivity = NativeRemoteWindowDrainActivityScope.Enter(this, this);
                try
                {
                    lock (gate) { windowRetainAttempted = true; }
                    nint retainedWindow = operations.Retain(selectedWindow);
                    lock (gate)
                    {
                        windowRetainResult = retainedWindow;
                        windowRetainConfirmed = retainedWindow != 0 && retainedWindow == selectedWindow;
                    }
                    if (!windowRetainConfirmed) { throw new InvalidOperationException("macOS native window retain unconfirmed."); }
                    lock (gate) { filterRetainAttempted = true; }
                    nint retainedFilter = operations.Retain(selectedFilter);
                    lock (gate)
                    {
                        filterRetainResult = retainedFilter;
                        filterRetainConfirmed = retainedFilter != 0 && retainedFilter == selectedFilter;
                    }
                    if (!filterRetainConfirmed) { throw new InvalidOperationException("macOS native filter retain unconfirmed."); }
                }
                catch (Exception exception)
                {
                    RecordOwnerFailure(exception);
                    throw;
                }

                lock (gate) { ObjectDisposedException.ThrowIf(closing, this); }
            }
            finally
            {
                if (ownerUseEntered) { ExitUse(); }
                parent.ExitUse();
            }
        }

        internal bool CheckCurrent()
        {
            if (!TryEnterUse(out _, out nint selectedFilter)) { return false; }
            try
            {
                using var activity = NativeRemoteWindowDrainActivityScope.Enter(this, this);
                bool current = operations.CheckCurrent(selectedFilter, Identity, Geometry);
                lock (gate) { return !closing && current; }
            }
            finally
            {
                ExitUse();
            }
        }

        public void Dispose()
        {
            bool selfJoin = NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this);
            Task wait;
            lock (gate)
            {
                closing = true;
                if (selfJoin)
                {
                    throw new InvalidOperationException("macOS native source use cannot join its own cleanup.");
                }
                if (disposing) { throw releaseUnconfirmed; }
                disposing = true;
                wait = activeUses == 0 ? Task.CompletedTask : usesExited.Task;
            }
            try
            {
                // Closing rejects new uses. Only already-admitted uses can
                // delay cleanup, and their exit is joined outside the gate.
                wait.GetAwaiter().GetResult();
                bool released = ReleaseOwner(ref filter, filterRetainAttempted,
                    filterRetainConfirmed, ref filterReleaseAttempted, isFilter: true)
                    & ReleaseOwner(ref window, windowRetainAttempted,
                        windowRetainConfirmed, ref windowReleaseAttempted, isFilter: false);
                if (!released)
                {
                    if (Volatile.Read(ref ownerFatal) is { } fatal) { ExceptionDispatchInfo.Capture(fatal).Throw(); }
                    throw releaseUnconfirmed;
                }
                if (creationLedger is not null)
                {
                    creationLedger.Context.Pool.CompleteCreatedSource(creationLedger, this);
                }
            }
            finally
            {
                lock (gate) { disposing = false; }
            }
        }

        private bool TryEnterUse(out nint selectedWindow, out nint selectedFilter)
        {
            lock (gate)
            {
                selectedWindow = 0;
                selectedFilter = 0;
                if (closing || window == 0 || filter == 0
                    || (parent is not null && (!windowRetainConfirmed || !filterRetainConfirmed)))
                {
                    return false;
                }
                activeUses++;
                selectedWindow = window;
                selectedFilter = filter;
                return true;
            }
        }

        private void ExitUse()
        {
            bool notify;
            lock (gate)
            {
                activeUses--;
                notify = closing && activeUses == 0;
            }
            if (notify) { usesExited.TrySetResult(true); }
        }

        private bool ReleaseOwner(ref nint owner, bool attempted, bool confirmed,
            ref bool releaseAttempted, bool isFilter)
        {
            nint selected;
            lock (gate)
            {
                if (!confirmed) { return !attempted; }
                if (owner == 0) { return true; }
                if (releaseAttempted) { return false; }
                releaseAttempted = true;
                selected = owner;
                if (creationLedger is not null)
                {
                    if (isFilter) { creationLedger.FilterReleaseAttempted = true; }
                    else { creationLedger.WindowReleaseAttempted = true; }
                }
            }
            try
            {
                operations.Release(selected);
                lock (gate)
                {
                    owner = 0;
                    if (creationLedger is not null)
                    {
                        if (isFilter) { creationLedger.FilterReleaseConfirmed = true; }
                        else { creationLedger.WindowReleaseConfirmed = true; }
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                RecordOwnerFailure(exception);
                return false;
            }
        }

        private void RecordOwnerFailure(Exception exception)
        {
            Interlocked.CompareExchange(ref ownerFailure, exception, null);
            if (FindFatal(exception) is { } fatal) { Interlocked.CompareExchange(ref ownerFatal, fatal, null); }
            if (creationLedger is not null)
            {
                creationLedger.Failure ??= exception;
                try { creationLedger.Context.Fail(creationLedger, exception, this); }
                catch (Exception notificationFailure)
                {
                    // The original owner fault is selected before notification.
                    // Containment lets the independent second owner release run.
                    if (FindFatal(notificationFailure) is { } notificationFatal)
                    {
                        Interlocked.CompareExchange(ref ownerFatal, notificationFatal, null);
                    }
                }
            }
        }
    }

    private sealed unsafe class Runtime
    {
        internal Runtime()
        {
            NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
            NativeLibrary.Load(N.ScreenCaptureKit);
            ShareableClass = RequireClass("SCShareableContent");
            FilterClass = RequireClass("SCContentFilter");
            ConfigurationClass = RequireClass("SCStreamConfiguration");
            StreamClass = RequireClass("SCStream");
            SrgbName = N.ExportedObject("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", "kCGColorSpaceSRGB");
            WindowNumberKey = WindowKey("kCGWindowNumber");
            WindowProcessKey = WindowKey("kCGWindowOwnerPID");
            WindowLayerKey = WindowKey("kCGWindowLayer");
            WindowOnScreenKey = WindowKey("kCGWindowIsOnscreen");
            WindowBoundsKey = WindowKey("kCGWindowBounds");
            OutputClass = CreateOutputClass();
        }

        internal nint ShareableClass { get; }
        internal nint FilterClass { get; }
        internal nint ConfigurationClass { get; }
        internal nint StreamClass { get; }
        internal nint OutputClass { get; }
        internal nint SrgbName { get; }
        internal nint WindowNumberKey { get; }
        internal nint WindowProcessKey { get; }
        internal nint WindowLayerKey { get; }
        internal nint WindowOnScreenKey { get; }
        internal nint WindowBoundsKey { get; }

        private static nint WindowKey(string name) => N.ExportedObject(
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", name);

        private static nint RequireClass(string name)
        {
            nint value = N.objc_getClass(name);
            return value != 0 ? value : throw new InvalidOperationException("macOS capture runtime unavailable.");
        }

        private static nint CreateOutputClass()
        {
            const string encoding = "v40@0:8@16^{opaqueCMSampleBuffer=}24q32";
            nint selector = N.Sel("stream:didOutputSampleBuffer:ofType:");
            nint protocol = N.objc_getProtocol("SCStreamOutput");
            if (protocol == 0)
            {
                protocol = N.objc_allocateProtocol("SCStreamOutput");
                if (protocol == 0)
                {
                    throw new InvalidOperationException("macOS output protocol unavailable.");
                }

                nint baseProtocol = N.objc_getProtocol("NSObject");
                if (baseProtocol != 0)
                {
                    N.protocol_addProtocol(protocol, baseProtocol);
                }

                nint types = Marshal.StringToCoTaskMemUTF8(encoding);
                try
                {
                    N.protocol_addMethodDescription(protocol, selector, types, 0, 1);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(types);
                }

                N.objc_registerProtocol(protocol);
            }

            MacOSObjectiveCMethodDescription description =
                N.protocol_getMethodDescription(protocol, selector, 0, 1);
            if (description.Types == 0 || Marshal.PtrToStringUTF8(description.Types) != encoding)
            {
                throw new InvalidOperationException("macOS output protocol ABI unavailable.");
            }

            nint cls = N.objc_allocateClassPair(RequireClass("NSObject"),
                "FlowspanRemoteWindowScreenOutputV1", 0);
            if (cls == 0 || N.class_addProtocol(cls, protocol) == 0
                || N.class_addMethod(cls, selector,
                    (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&Capture.DidOutput,
                    description.Types) == 0)
            {
                throw new InvalidOperationException("macOS output callback unavailable.");
            }

            N.objc_registerClassPair(cls);
            return cls;
        }
    }

    private sealed class NativeCaptureSource : IMacOSRemoteWindowStagedCaptureSource
    {
        private readonly MacOSRemoteWindowScreenCaptureKitApi api;
        private readonly NativeSource source;

        internal NativeCaptureSource(MacOSRemoteWindowScreenCaptureKitApi api, NativeSource source)
        {
            this.api = api;
            this.source = source;
        }

        public NativeRemoteWindowGeometry Geometry => source.Geometry;
        public nint Filter => source.Filter;
        public IMacOSRemoteWindowCaptureSource RetainOwner()
        {
            // No unrooted one-step path may lose a partially acquired token.
            throw new InvalidOperationException("macOS native source requires staged acquisition.");
        }

        public IMacOSRemoteWindowStagedCaptureSource PrepareOwner() =>
            new NativeCaptureSource(api, source.PrepareOwner());
        public void AcquireOwner() => source.AcquireOwner();

        public bool IsCurrent() => source.CheckCurrent();
        public void Dispose() => source.Dispose();
    }

    private sealed class NativeCaptureOperations(Runtime runtime) : IMacOSRemoteWindowCaptureOperations
    {
        public nint PushAutoreleasePool() => N.objc_autoreleasePoolPush();
        public void PopAutoreleasePool(nint pool) => N.objc_autoreleasePoolPop(pool);
        public nint AllocateConfiguration() => N.Send0(runtime.ConfigurationClass, N.Sel("new"));
        public nint AllocateOutput() => N.Send0(runtime.OutputClass, N.Sel("new"));
        public nint CreateSampleQueue() => N.dispatch_queue_create("flowspan.remote-window.samples", 0);
        public nint AllocateStream() => N.Send0(runtime.StreamClass, N.Sel("alloc"));
        public nint InitializeStream(nint allocatedStream, nint filter, nint configuration, nint streamDelegate) =>
            N.InitStream(allocatedStream, N.Sel("initWithFilter:configuration:delegate:"),
                filter, configuration, streamDelegate);

        public void Configure(nint configuration, int width, int height)
        {
            N.SendNUInt(configuration, N.Sel("setWidth:"), (nuint)width);
            N.SendNUInt(configuration, N.Sel("setHeight:"), (nuint)height);
            N.SendUInt(configuration, N.Sel("setPixelFormat:"), N.Bgra);
            N.SendNInt(configuration, N.Sel("setQueueDepth:"), 3);
            N.SendByte(configuration, N.Sel("setCapturesAudio:"), 0);
            N.SendByte(configuration, N.Sel("setShowsCursor:"), 0);
            N.SendByte(configuration, N.Sel("setScalesToFit:"), 1);
            N.SendByte(configuration, N.Sel("setIgnoreShadowsSingleWindow:"), 1);
            N.SendByte(configuration, N.Sel("setIncludeChildWindows:"), 0);
            N.SendTime(configuration, N.Sel("setMinimumFrameInterval:"),
                new MacOSCaptureTime { Value = 1, Timescale = 30, Flags = 1 });
            _ = N.Send1(configuration, N.Sel("setColorSpaceName:"), runtime.SrgbName);
        }

        public byte AddOutput(nint stream, nint output, nint queue, out nint error) =>
            N.AddOutput(stream, N.Sel("addStreamOutput:type:sampleHandlerQueue:error:"),
                output, 0, queue, out error);

        public byte RemoveOutput(nint stream, nint output, out nint error) =>
            N.RemoveOutput(stream, N.Sel("removeStreamOutput:type:error:"), output, 0, out error);

        public IMacOSRemoteWindowCaptureCompletion CreateCompletion(
            Action<nint> action, Action<Exception> failure, Action completed) =>
            MacOSRemoteWindowCaptureCompletion.Create(action, failure, completed);

        public nint GetCompletionSelector(bool isStart) => N.Sel(isStart
            ? "startCaptureWithCompletionHandler:" : "stopCaptureWithCompletionHandler:");
        public void InvokeCompletion(nint stream, nint selector, nint completion) =>
            _ = N.Send1(stream, selector, completion);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void QueueBarrier(nint context) { }

        public unsafe void DrainSampleQueue(nint queue) => N.dispatch_sync_f(queue, 0,
            (nint)(delegate* unmanaged[Cdecl]<nint, void>)&QueueBarrier);
        public void ReleaseObject(nint owner) => N.objc_release(owner);
        public void ReleaseQueue(nint queue) => N.dispatch_release(queue);

        public nint? GetSampleFrameStatus(nint sample)
        {
            nint attachments = N.CMSampleBufferGetSampleAttachmentsArray(sample, 0);
            if (attachments == 0 || N.CFArrayGetCount(attachments) < 1)
            {
                return null;
            }

            nint dictionary = N.CFArrayGetValueAtIndex(attachments, 0);
            nint status = N.CFDictionaryGetValue(dictionary, N.FrameStatusKey);
            return status == 0 ? null : N.GetNInt(status, N.Sel("integerValue"));
        }

        public bool IsSampleReady(nint sample) => N.CMSampleBufferIsValid(sample) != 0
            && N.CMSampleBufferDataIsReady(sample) != 0;

        public IMacOSRemoteWindowNativeSample RetainSample(nint sample, int width, int height)
        {
            nint retained = N.CFRetain(sample);
            try
            {
                return new MacOSRemoteWindowNativeSample(retained, width, height);
            }
            catch
            {
                N.CFRelease(retained);
                throw;
            }
        }
    }

    private sealed class Capture : IMacOSRemoteWindowNativeCapture
    {
        private static readonly ConcurrentDictionary<nint, Capture> Roots = new();
        private static int retainedOwners;
        internal static int RetainedOwnerCount => Volatile.Read(ref retainedOwners);
        [ThreadStatic] private static CallbackScope? callbackScope;
        private readonly object gate = new();
        private readonly MacOSRemoteWindowScreenCaptureKitApi api;
        private readonly IMacOSRemoteWindowCaptureOperations operations;
        private readonly IMacOSRemoteWindowCaptureSource source;
        private readonly Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership;
        private readonly Action sourceUnavailable;
        private readonly TaskCompletionSource<bool> startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> startCallbackExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> stopCallbackExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int width;
        private readonly int height;
        private nint stream;
        private nint output;
        private nint queue;
        private nint configuration;
        private GCHandle callbackRoot;
        private IMacOSRemoteWindowCaptureCompletion? startBlock;
        private IMacOSRemoteWindowCaptureCompletion? stopBlock;
        private Task<bool>? drain;
        private bool startRequested;
        private bool startIssued;
        private bool startInvocationReturned;
        private bool startInvocationBorrowExited;
        private bool stopIssued;
        private bool stopInvocationReturned;
        private bool stopInvocationBorrowExited;
        private bool stopSetupSettledUnissued;
        private bool startSettled;
        private bool stopSettled;
        private bool outputRemoved;
        private bool queueDrained;
        private bool unsafeFailure;
        private OutOfMemoryException? fatalFailure;
        private bool disposed;
        private bool disposing;
        private bool releasingOwners;
        private bool streamReleaseAttempted;
        private bool outputReleaseAttempted;
        private bool configurationReleaseAttempted;
        private bool queueReleaseAttempted;
        private bool startBlockReleaseAttempted;
        private bool startBlockReleaseConfirmed;
        private bool stopBlockReleaseAttempted;
        private bool stopBlockReleaseConfirmed;
        private bool sourceReleaseAttempted;
        private bool sourceReleaseConfirmed;
        private readonly bool sourceRetainAttempted;
        private readonly bool sourceCleanupOwned;
        private readonly Exception? factoryFailure;
        private readonly bool constructorPoolPushAttempted;
        private readonly bool constructorPoolPushConfirmed;
        private readonly bool constructorPoolPopAttempted;
        private readonly bool constructorPoolPopConfirmed;
        private bool startPoolPushAttempted;
        private bool startPoolPushConfirmed;
        private bool startPoolPopAttempted;
        private bool startPoolPopConfirmed;
        private bool stopPoolPushAttempted;
        private bool stopPoolPushConfirmed;
        private bool stopPoolPopAttempted;
        private bool stopPoolPopConfirmed;
        private bool removePoolPushAttempted;
        private bool removePoolPushConfirmed;
        private bool removePoolPopAttempted;
        private bool removePoolPopConfirmed;
        private Exception? disposalFailure;
        private bool rootCounted;
        private readonly InvalidOperationException releaseUnconfirmed =
            new("macOS capture release unconfirmed.");
        private int deliveryClosed;
        private int unavailableNotified;
        private int nativeDrainConfirmed;

        internal MacOSRemoteWindowScreenCaptureKitApi Api => api;

        internal bool MatchesFactoryFailure(Exception failure) =>
            ReferenceEquals(failure, factoryFailure)
            || (FindFatal(failure) is { } fatal && ReferenceEquals(fatal, fatalFailure));

        // Physical drain is monotonic. Reading its published proof must not
        // acquire the release gate from a joined failure-observer child task.
        public bool IsDrained => Volatile.Read(ref nativeDrainConfirmed) != 0;

        // Existing monotonic proof: independent owners/pools, both terminal
        // lifetimes and shell-root/accounting cleanup have all confirmed.
        public bool IsCleanupConfirmed { get { lock (gate) { return disposed; } } }

        public bool TryGetKnownPendingCleanupJoin(out Task? join)
        {
            join = null;
            IMacOSRemoteWindowStagedCaptureCompletion start;
            IMacOSRemoteWindowStagedCaptureCompletion stop;
            lock (gate)
            {
                // A recorded disposal fault may include shell-root free with
                // unknown effect. This narrow recovery must not retry it.
                if (disposed || disposing || releasingOwners || disposalFailure is not null || !callbackRoot.IsAllocated || !rootCounted
                    || !IsDrainedCore() || HasActiveNativeInvocationBorrowCore()
                    || stream != 0 || output != 0 || queue != 0 || configuration != 0
                    || !sourceReleaseConfirmed || !startBlockReleaseConfirmed || !stopBlockReleaseConfirmed
                    || (constructorPoolPushAttempted && (!constructorPoolPushConfirmed || !constructorPoolPopAttempted || !constructorPoolPopConfirmed))
                    || (startPoolPushAttempted && (!startPoolPushConfirmed || !startPoolPopAttempted || !startPoolPopConfirmed))
                    || (stopPoolPushAttempted && (!stopPoolPushConfirmed || !stopPoolPopAttempted || !stopPoolPopConfirmed))
                    || (removePoolPushAttempted && (!removePoolPushConfirmed || !removePoolPopAttempted || !removePoolPopConfirmed))
                    || startBlock is not IMacOSRemoteWindowStagedCaptureCompletion actualStart
                    || stopBlock is not IMacOSRemoteWindowStagedCaptureCompletion actualStop)
                {
                    return false;
                }

                start = actualStart;
                stop = actualStop;
            }

            // Owner qualification/getters and join allocation stay outside
            // Capture's gate. Defaults decline opaque or unknown primitives.
            if (!start.TryGetKnownLifetimeJoin(out Task? startJoin) || startJoin is null
                || !stop.TryGetKnownLifetimeJoin(out Task? stopJoin) || stopJoin is null)
            {
                return false;
            }

            join = Task.WhenAll(startJoin, stopJoin);
            return true;
        }

        private bool IsDrainedCore() => startSettled && stopSettled
            && outputRemoved && queueDrained
            && (!startRequested || startCallbackExited.Task.IsCompletedSuccessfully)
            && (!stopIssued || stopCallbackExited.Task.IsCompletedSuccessfully);

        internal Capture(MacOSRemoteWindowScreenCaptureKitApi api, IMacOSRemoteWindowCaptureOperations operations,
            IMacOSRemoteWindowCaptureSource source, Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
            Action sourceUnavailable)
        {
            this.api = api;
            this.operations = operations;
            // The shell holds a borrowed reference until RetainOwner returns.
            // A throwing retain must never authorize disposing that reference.
            this.source = source;
            this.takeSampleOwnership = takeSampleOwnership;
            this.sourceUnavailable = sourceUnavailable;

            nint pool = 0;
            bool rooted = false;
            bool publicationAttempted = false;
            bool publicationRejected = false;
            Exception? constructionFailure = null;
            try
            {
                // Keep the complete owner reachable before any per-Capture
                // native allocation can need an uncertain cleanup attempt.
                callbackRoot = GCHandle.Alloc(this);
                Interlocked.Increment(ref retainedOwners);
                rootCounted = true;
                if (source is IMacOSRemoteWindowStagedCaptureSource staged)
                {
                    IMacOSRemoteWindowStagedCaptureSource owner = staged.PrepareOwner();
                    this.source = owner;
                    sourceCleanupOwned = true;
                    sourceRetainAttempted = true;
                    owner.AcquireOwner();
                }
                else
                {
                    sourceRetainAttempted = true;
                    this.source = source.RetainOwner();
                    sourceCleanupOwned = true;
                }
                if (!TryDimensions(this.source.Geometry, out width, out height))
                {
                    throw new InvalidOperationException("macOS window pixel bounds unavailable.");
                }

                constructorPoolPushAttempted = true;
                pool = operations.PushAutoreleasePool();
                constructorPoolPushConfirmed = pool != 0;
                if (!constructorPoolPushConfirmed)
                {
                    throw new InvalidOperationException("macOS capture autorelease pool unavailable.");
                }
                configuration = operations.AllocateConfiguration();
                output = operations.AllocateOutput();
                queue = operations.CreateSampleQueue();
                if (configuration == 0 || output == 0 || queue == 0)
                {
                    throw new InvalidOperationException("macOS capture allocation unavailable.");
                }

                operations.Configure(configuration, width, height);

                if (!Roots.TryAdd(output, this))
                {
                    throw new InvalidOperationException("macOS callback registration unavailable.");
                }

                rooted = true;
                // SCStreamDelegate is deliberately absent: only the sample
                // queue has a proven drain barrier in this implementation.
                stream = operations.InitializeStream(operations.AllocateStream(),
                    this.source.Filter, configuration, 0);
                if (stream == 0)
                {
                    throw new InvalidOperationException("macOS capture stream unavailable.");
                }

                publicationAttempted = true;
                byte registered = operations.AddOutput(stream, output, queue, out nint error);
                publicationRejected = registered == 0;
                if (publicationRejected || error != 0)
                {
                    throw new InvalidOperationException("macOS output registration unavailable.");
                }
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                constructionFailure = exception;
                unsafeFailure = true;
                Interlocked.Exchange(ref deliveryClosed, 1);
            }
            finally
            {
                if (constructorPoolPushConfirmed)
                {
                    // This fixed synchronous scope pops once on its creating
                    // thread. A throwing pop may already have consumed it;
                    // never transfer or retry that unknown obligation.
                    constructorPoolPopAttempted = true;
                    try
                    {
                        operations.PopAutoreleasePool(pool);
                        constructorPoolPopConfirmed = true;
                    }
                    catch (Exception exception)
                    {
                        RecordFatal(exception);
                        constructionFailure ??= exception;
                        unsafeFailure = true;
                        Interlocked.Exchange(ref deliveryClosed, 1);
                    }
                }
            }

            if (constructionFailure is not null)
            {
                // Pool unwind and primary/fatal selection precede the exact
                // failed-shell handoff and every independent cleanup attempt.
                // The replaceable slot is not the shell's durable GC root.
                factoryFailure = (Exception?)Volatile.Read(ref fatalFailure) ?? constructionFailure;
                failedFactoryCapture = this;
                if (!publicationAttempted)
                {
                    startSettled = true;
                    stopSettled = true;
                    outputRemoved = true;
                    queueDrained = true;
                    Volatile.Write(ref nativeDrainConfirmed, 1);
                    if (rooted)
                    {
                        Roots.TryRemove(new KeyValuePair<nint, Capture>(output, this));
                    }

                    if (ReleaseUnpublishedOwners() && ReferenceEquals(failedFactoryCapture, this))
                    {
                        failedFactoryCapture = null;
                    }
                }
                else
                {
                    try
                    {
                        drain = RunIsolated(() => RollbackConstructionAsync(publicationRejected));
                    }
                    catch (Exception schedulingFailure)
                    {
                        RecordFatal(schedulingFailure);
                    }
                }

                ThrowPendingFatal();
                ExceptionDispatchInfo.Capture(constructionFailure).Throw();
            }
        }

        public ValueTask<bool> StartAsync()
        {
            if (CallbackScope.Contains(this)
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this))
            {
                return ValueTask.FromResult(false);
            }

            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (Volatile.Read(ref disposalFailure) is not null)
                {
                    return ValueTask.FromResult(false);
                }

                if (startRequested)
                {
                    ThrowPendingFatal();
                    return new(startCompletion.Task);
                }

                if (drain is not null || Volatile.Read(ref deliveryClosed) != 0)
                {
                    return ValueTask.FromResult(false);
                }

                startRequested = true;
            }

            try
            {
                if (!source.IsCurrent())
                {
                    SetStartResult(false);
                    NotifyUnavailable();
                    startCallbackExited.TrySetResult(true);
                    return new(startCompletion.Task);
                }

                startBlock = operations.CreateCompletion(error =>
                {
                    if (SetStartResult(error == 0) && error != 0)
                    {
                        NotifyUnavailable();
                    }
                }, exception =>
                {
                    RecordFatal(exception);
                    MarkUnsafeFailure();
                    SetStartResult(false);
                    NotifyUnavailable();
                }, () => startCallbackExited.TrySetResult(true));
                if (startBlock is IMacOSRemoteWindowStagedCaptureCompletion stagedStart)
                {
                    stagedStart.AcquireCopy();
                }
                InvokeCompletion(startBlock.Pointer, isStart: true);
                lock (gate)
                {
                    startInvocationReturned = true;
                }
                // A synchronous callback can settle success and then record a
                // fatal. Preserve the real handoff/result facts, but never let
                // that cached result hide the already recorded local failure.
                ThrowPendingFatal();
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                MarkUnsafeFailure();
                bool issued;
                lock (gate)
                {
                    issued = startIssued;
                }

                if (issued)
                {
                    startCompletion.TrySetException((Exception?)FindFatal(exception)
                        ?? new InvalidOperationException("macOS capture start unconfirmed."));
                }
                else
                {
                    SetStartResult(false);
                }

                NotifyUnavailable();
                if (!issued)
                {
                    startCallbackExited.TrySetResult(true);
                }
                ThrowPendingFatal();
                throw;
            }

            return new(startCompletion.Task);
        }

        private Task<bool> RollbackConstructionAsync(bool publicationRejected)
        {
            try
            {
                // No Start has been requested on a factory that never returns.
                // A settled BOOL=false confirms output was not registered.
                if (!publicationRejected && !RemoveNativeOutput(stream, output))
                {
                    return Task.FromResult(false);
                }

                lock (gate)
                {
                    startSettled = true;
                    stopSettled = true;
                    outputRemoved = true;
                }

                operations.DrainSampleQueue(queue);
                lock (gate)
                {
                    queueDrained = true;
                    if (IsDrainedCore())
                    {
                        Volatile.Write(ref nativeDrainConfirmed, 1);
                    }
                }

                bool released = ReleaseUnpublishedOwners();
                lock (gate)
                {
                    disposed |= released;
                }

                ThrowDisposalFailure();
                return Task.FromResult(released);
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                MarkUnsafeFailure();
                ThrowPendingFatal();
                return Task.FromResult(false);
            }
        }

        public ValueTask<bool> StopAndDrainAsync()
        {
            // A release fault observer can synchronously wait for a child task.
            // Reject same-owner callback ancestry before acquiring the gate:
            // its parent may be holding that gate while releasing a block.
            if (CallbackScope.Contains(this)
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this)
                || HasActiveCompletionResourceUseAncestry())
            {
                Interlocked.Exchange(ref deliveryClosed, 1);
                return ValueTask.FromException<bool>(new InvalidOperationException(
                    "macOS native callback cannot join its own cleanup."));
            }

            lock (gate)
            {
                if (factoryFailure is not null && drain is not null)
                {
                    return new(drain);
                }

                if (factoryFailure is not null && IsDrainedCore())
                {
                    return ValueTask.FromResult(true);
                }

                ObjectDisposedException.ThrowIf(disposed, this);
                Interlocked.Exchange(ref deliveryClosed, 1);
                drain ??= RunIsolated(DrainAsync);
                return new(drain);
            }
        }

        private async Task<bool> DrainAsync()
        {
            bool requested;
            lock (gate)
            {
                requested = startRequested;
                if (!requested)
                {
                    startSettled = true;
                }
            }

            if (requested)
            {
                try
                {
                    _ = await startCompletion.Task.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    RecordFatal(exception);
                    MarkUnsafeFailure();
                }

                await startCallbackExited.Task.ConfigureAwait(false);
            }

            try
            {
                bool issued;
                lock (gate)
                {
                    issued = startIssued;
                }

                if (!await ConfirmStopAsync(issued).ConfigureAwait(false))
                {
                    return false;
                }

                if (!RemoveNativeOutput(stream, output))
                {
                    MarkUnsafeFailure();
                    return false;
                }

                lock (gate)
                {
                    outputRemoved = true;
                }

                // DrainAsync always starts on the thread pool. Never execute a
                // dispatch_sync barrier inline on the sample callback queue.
                operations.DrainSampleQueue(queue);
                lock (gate)
                {
                    queueDrained = true;
                    if (IsDrainedCore())
                    {
                        Volatile.Write(ref nativeDrainConfirmed, 1);
                    }
                }

                // Result/exit notification and the physical sample barrier do
                // not join an admitted completion resource-use region. Close
                // both owners before awaiting their fixed joins outside gates.
                // This is pre-release use drain, not native-copy retirement or
                // ManagedInvocationDrain, which depend on later owner releases.
                Task startResourceUse = startBlock is IMacOSRemoteWindowStagedCaptureCompletion stagedStart
                    ? stagedStart.CloseResourceUse() : Task.CompletedTask;
                Task stopResourceUse = stopBlock is IMacOSRemoteWindowStagedCaptureCompletion stagedStop
                    ? stagedStop.CloseResourceUse() : Task.CompletedTask;
                await startResourceUse.ConfigureAwait(false);
                await stopResourceUse.ConfigureAwait(false);
                lock (gate)
                {
                    return startSettled && stopSettled && outputRemoved && !unsafeFailure;
                }
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                MarkUnsafeFailure();
                return false;
            }
            finally
            {
                ThrowPendingFatal();
            }
        }

        private async Task<bool> ConfirmStopAsync(bool captureStartIssued)
        {
            if (!captureStartIssued)
            {
                lock (gate)
                {
                    stopSettled = true;
                }

                return true;
            }

            bool acquisitionReturnedConfirmed = false;
            try
            {
                stopBlock = operations.CreateCompletion(error =>
                {
                    lock (gate)
                    {
                        // Result admission and its corresponding Stop facts
                        // have one winner, for both success and rejection.
                        if (!stopCompletion.TrySetResult(error == 0)) { return; }
                        stopSettled = error == 0;
                        unsafeFailure |= error != 0;
                    }
                }, exception =>
                {
                    RecordFatal(exception);
                    MarkUnsafeFailure();
                    stopCompletion.TrySetResult(false);
                }, () => stopCallbackExited.TrySetResult(true));
                if (stopBlock is IMacOSRemoteWindowStagedCaptureCompletion stagedStop)
                {
                    stagedStop.AcquireCopy();
                    acquisitionReturnedConfirmed = true;
                }
                InvokeCompletion(stopBlock.Pointer, isStart: false);
                lock (gate) { stopInvocationReturned = true; }
                bool stopped = await stopCompletion.Task.ConfigureAwait(false);
                await stopCallbackExited.Task.ConfigureAwait(false);
                return stopped;
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                MarkUnsafeFailure();
                bool issued;
                lock (gate)
                {
                    issued = stopIssued;
                }

                if (issued)
                {
                    // An invocation fault cannot synthesize callback exit.
                    await stopCallbackExited.Task.ConfigureAwait(false);
                }

                lock (gate)
                {
                    // A real successful stop, received after the invocation
                    // fault, still permits independent removal and drain.
                    return stopSettled;
                }
            }
            finally
            {
                if (acquisitionReturnedConfirmed)
                {
                    // This unique setup flow has ended and cannot resume a
                    // handoff. A transient !stopIssued during acquisition is
                    // never sufficient to release the attached caller copy.
                    lock (gate)
                    {
                        if (!stopIssued) { stopSetupSettledUnissued = true; }
                    }
                }
            }
        }

        private bool SetStartResult(bool success)
        {
            lock (gate)
            {
                // A real late callback still confirms settlement after an
                // issued handoff fault has already faulted the result task.
                startSettled = true;
                return Volatile.Read(ref fatalFailure) is { } fatal
                    ? startCompletion.TrySetException(fatal)
                    : startCompletion.TrySetResult(success);
            }
        }

        private void MarkUnsafeFailure()
        {
            lock (gate)
            {
                unsafeFailure = true;
            }
        }

        private void RecordFatal(Exception exception)
        {
            if (FindFatal(exception) is { } fatal)
            {
                Interlocked.CompareExchange(ref fatalFailure, fatal, null);
            }
        }

        private void ThrowPendingFatal()
        {
            if (Volatile.Read(ref fatalFailure) is { } fatal)
            {
                ExceptionDispatchInfo.Capture(fatal).Throw();
            }
        }

        private void NotifyUnavailable()
        {
            Interlocked.Exchange(ref deliveryClosed, 1);
            if (Interlocked.Exchange(ref unavailableNotified, 1) != 0)
            {
                return;
            }

            try
            {
                using var sharedAncestry = NativeRemoteWindowDrainActivityScope.Enter(this, this);
                using var ancestry = new CallbackScope(this);
                sourceUnavailable();
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                MarkUnsafeFailure();
                // Caller faults cannot cross a native callback boundary.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        internal static void DidOutput(nint self, nint selector, nint stream, nint sample, nint kind) =>
            ProcessOutput(self, stream, sample, kind);

        internal static void ProcessOutput(nint self, nint stream, nint sample, nint kind)
        {
            Capture? capture = null;
            CallbackScope? ancestry = null;
            NativeRemoteWindowDrainActivityScope? sharedAncestry = null;
            try
            {
                if (!Roots.TryGetValue(self, out capture)
                    || Volatile.Read(ref capture.deliveryClosed) != 0
                    || !Volatile.Read(ref capture.startIssued))
                {
                    return;
                }

                ancestry = new CallbackScope(capture);
                sharedAncestry = NativeRemoteWindowDrainActivityScope.Enter(capture, capture);

                if (stream != capture.stream || sample == 0 || kind != 0)
                {
                    capture.NotifyUnavailable();
                    return;
                }

                if (!capture.source.IsCurrent())
                {
                    capture.NotifyUnavailable();
                    return;
                }

                nint? frameStatus = capture.operations.GetSampleFrameStatus(sample);
                if (frameStatus is null)
                {
                    return;
                }

                if (frameStatus == 5)
                {
                    capture.NotifyUnavailable();
                    return;
                }

                if (frameStatus != 0 || !capture.operations.IsSampleReady(sample))
                {
                    return;
                }

                IMacOSRemoteWindowNativeSample owner = capture.operations.RetainSample(
                    sample, capture.width, capture.height);

                // Invocation transfers ownership, including reject/throw paths.
                capture.takeSampleOwnership(owner);
            }
            catch (Exception exception)
            {
                capture?.RecordFatal(exception);
                capture?.MarkUnsafeFailure();
                capture?.NotifyUnavailable();
            }
            finally
            {
                try
                {
                    try
                    {
                        sharedAncestry?.Dispose();
                    }
                    finally
                    {
                        // Restore the thread-local chain even if restoring the
                        // ExecutionContext scope itself fails.
                        ancestry?.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    capture?.RecordFatal(exception);
                    capture?.MarkUnsafeFailure();
                    capture?.NotifyUnavailable();
                }
            }
        }

        private void InvokeCompletion(nint block, bool isStart)
        {
            if (isStart)
            {
                lock (gate) { startPoolPushAttempted = true; }
            }
            else
            {
                lock (gate) { stopPoolPushAttempted = true; }
            }

            try
            {
                nint pool = operations.PushAutoreleasePool();
                if (isStart)
                {
                    lock (gate) { startPoolPushConfirmed = pool != 0; }
                }
                else
                {
                    lock (gate) { stopPoolPushConfirmed = pool != 0; }
                }

                if (pool == 0)
                {
                    throw new InvalidOperationException("macOS capture autorelease pool unavailable.");
                }

                try
                {
                    nint selectorPointer = operations.GetCompletionSelector(isStart);
                    lock (gate)
                    {
                        if (isStart)
                        {
                            startIssued = true;
                        }
                        else
                        {
                            stopIssued = true;
                        }
                    }

                    operations.InvokeCompletion(stream, selectorPointer, block);
                }
                catch (Exception exception)
                {
                    // Preserve the original body fatal before the known
                    // pool unwinds; a later pop fault must not replace it.
                    RecordFatal(exception);
                    throw;
                }
                finally
                {
                    if (isStart)
                    {
                        if (pool != 0)
                        {
                            // This synchronous scope owns one pop attempt on
                            // its creating thread, never a retry after a fault.
                            lock (gate) { startPoolPopAttempted = true; }
                            operations.PopAutoreleasePool(pool);
                            lock (gate) { startPoolPopConfirmed = true; }
                        }
                    }
                    else
                    {
                        if (pool != 0)
                        {
                            lock (gate) { stopPoolPopAttempted = true; }
                            operations.PopAutoreleasePool(pool);
                            lock (gate) { stopPoolPopConfirmed = true; }
                        }
                    }
                }
            }
            finally
            {
                // Callback result/exit and resource-use idle do not end this
                // synchronous borrow. Publish its fixed exit even when push,
                // selector, native invocation or original-thread pop throws.
                lock (gate)
                {
                    if (isStart) { startInvocationBorrowExited = true; }
                    else { stopInvocationBorrowExited = true; }
                }
            }
        }

        private bool RemoveNativeOutput(nint stream, nint output)
        {
            lock (gate) { removePoolPushAttempted = true; }
            nint pool = operations.PushAutoreleasePool();
            lock (gate) { removePoolPushConfirmed = pool != 0; }
            if (pool == 0)
            {
                throw new InvalidOperationException("macOS capture autorelease pool unavailable.");
            }
            bool removed;
            try
            {
                removed = operations.RemoveOutput(stream, output, out nint error) != 0 && error == 0;
            }
            catch (Exception exception)
            {
                RecordFatal(exception);
                throw;
            }
            finally
            {
                if (pool != 0)
                {
                    lock (gate) { removePoolPopAttempted = true; }
                    try
                    {
                        operations.PopAutoreleasePool(pool);
                        lock (gate) { removePoolPopConfirmed = true; }
                    }
                    catch (Exception exception)
                    {
                        // Pop failure does not erase an independently
                        // confirmed BOOL/error removal result. Keep cleanup
                        // possible, but never confirm or retry this pool.
                        RecordFatal(exception);
                        MarkUnsafeFailure();
                    }
                }
            }

            return removed;
        }

        private static Task<bool> RunIsolated(Func<Task<bool>> operation)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return Task.Run(operation);
            }

            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(operation);
            }
        }

        private sealed class CallbackScope : IDisposable
        {
            private readonly Capture owner;
            private readonly CallbackScope? previous;
            private bool active = true;

            internal CallbackScope(Capture owner)
            {
                this.owner = owner;
                previous = callbackScope;
                callbackScope = this;
            }

            internal static bool Contains(Capture owner)
            {
                for (CallbackScope? scope = callbackScope; scope is not null; scope = scope.previous)
                {
                    if (scope.active && ReferenceEquals(scope.owner, owner))
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Dispose()
            {
                active = false;
                if (ReferenceEquals(callbackScope, this))
                {
                    callbackScope = previous;
                }
            }
        }

        public void Dispose()
        {
            if (CallbackScope.Contains(this)
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this)
                || HasActiveCompletionResourceUseAncestry())
            {
                throw new InvalidOperationException(
                    "macOS native callback cannot dispose its own cleanup owner.");
            }

            bool drained;
            bool releaseCompletedStartCaller;
            bool releaseCompletedStopCaller;
            lock (gate)
            {
                if (disposed)
                {
                    ThrowDisposalFailure();
                    return;
                }

                if (disposing)
                {
                    throw releaseUnconfirmed;
                }

                drained = IsDrainedCore();
                releaseCompletedStartCaller = !drained && startInvocationReturned
                    && startCompletion.Task.IsCompleted
                    && startCallbackExited.Task.IsCompletedSuccessfully
                    && startBlock is IMacOSRemoteWindowStagedCaptureCompletion;
                releaseCompletedStopCaller = !drained && (stopSetupSettledUnissued
                    || (stopInvocationReturned && stopCompletion.Task.IsCompleted
                        && stopCallbackExited.Task.IsCompletedSuccessfully))
                    && stopBlock is IMacOSRemoteWindowStagedCaptureCompletion;
                disposing = true;
            }

            try
            {
                if (!drained)
                {
                    // A confirmed +1 is independent after unissued setup ends,
                    // or handoff returns and completion notification is observed.
                    // ReleaseBlock additionally closes and joins all admitted
                    // action/failure/completed resource users. This is not
                    // terminal managed drain or final ABI return.
                    // This releases no stream/output/source owner, confirms no
                    // Stop/drain and cannot return the Capture root/accounting.
                    if (releaseCompletedStartCaller)
                    {
                        _ = ReleaseBlock(startBlock, ref startBlockReleaseAttempted, ref startBlockReleaseConfirmed);
                    }
                    if (releaseCompletedStopCaller)
                    {
                        _ = ReleaseBlock(stopBlock, ref stopBlockReleaseAttempted, ref stopBlockReleaseConfirmed);
                    }

                    ThrowDisposalFailure();
                    ThrowPendingFatal();
                    throw new InvalidOperationException("macOS capture cleanup remains unconfirmed.");
                }

                bool released = ReleaseUnpublishedOwners();
                lock (gate)
                {
                    disposed |= released;
                }

                ThrowDisposalFailure();
                if (!released)
                {
                    ThrowPendingFatal();
                    throw releaseUnconfirmed;
                }
            }
            finally
            {
                lock (gate)
                {
                    disposing = false;
                }
            }
        }

        private bool ReleaseUnpublishedOwners()
        {
            lock (gate)
            {
                if (releasingOwners)
                {
                    return false;
                }

                if (HasActiveNativeInvocationBorrowCore()
                    || (startBlock is not null && !startCompletion.Task.IsCompleted)
                    || (stopBlock is not null && !stopSetupSettledUnissued && !stopCompletion.Task.IsCompleted))
                {
                    return false;
                }

                releasingOwners = true;
            }

            try
            {
                // A real settled result (or completed, unissued setup) is the
                // eligibility for closure. Never close a still-pending issued
                // callback and strand the result/exit this flow must await.
                // Join is independent of native-copy retirement and performs
                // no blocking wait under a gate or before stream release.
                if (!CloseCompletionResourceUse(startBlock) | !CloseCompletionResourceUse(stopBlock))
                {
                    return false;
                }

                bool released = ReleaseBlock(startBlock, ref startBlockReleaseAttempted, ref startBlockReleaseConfirmed)
                    & ReleaseBlock(stopBlock, ref stopBlockReleaseAttempted, ref stopBlockReleaseConfirmed);
                released &= ReleaseObject(ref stream, ref streamReleaseAttempted);
                nint outputKey = output;
                if (outputKey != 0)
                {
                    // Remove the address lookup before native deallocation so
                    // an immediately reused address cannot hit this owner.
                    Roots.TryRemove(new KeyValuePair<nint, Capture>(outputKey, this));
                    // An uncertain release may already have deallocated this
                    // address. Never republish its lookup; callbackRoot keeps
                    // the full owner reachable independently of that address.
                    released &= ReleaseObject(ref output, ref outputReleaseAttempted);
                }

                released &= ReleaseQueue();

                released &= ReleaseObject(ref configuration, ref configurationReleaseAttempted);
                released &= ReleaseSource();

                // Caller ownership and independent native owners must release
                // first: SCStream may hold a completion copy until its release.
                // A confirmed caller release is not last-copy retirement or
                // terminal managed drain. Observe late failure on every pass,
                // even when the caller's single-attempt result was cached.
                released &= IsCompletionLifetimeConfirmed(startBlock)
                    & IsCompletionLifetimeConfirmed(stopBlock);
                released &= !constructorPoolPushAttempted || (constructorPoolPushConfirmed
                    && constructorPoolPopAttempted && constructorPoolPopConfirmed);
                lock (gate)
                {
                    released &= !startPoolPushAttempted || (startPoolPushConfirmed
                        && startPoolPopAttempted && startPoolPopConfirmed);
                    released &= !stopPoolPushAttempted || (stopPoolPushConfirmed
                        && stopPoolPopAttempted && stopPoolPopConfirmed);
                    released &= !removePoolPushAttempted || (removePoolPushConfirmed
                        && removePoolPopAttempted && removePoolPopConfirmed);
                }

                if (released && callbackRoot.IsAllocated)
                {
                    try
                    {
                        callbackRoot.Free();
                        if (rootCounted)
                        {
                            rootCounted = false;
                            Interlocked.Decrement(ref retainedOwners);
                        }
                    }
                    catch (Exception exception)
                    {
                        RecordDisposalFailure(exception);
                        released = false;
                    }
                }

                return released;
            }
            finally
            {
                lock (gate)
                {
                    releasingOwners = false;
                }
            }
        }

        private bool IsCompletionLifetimeConfirmed(IMacOSRemoteWindowCaptureCompletion? block)
        {
            if (block is not IMacOSRemoteWindowStagedCaptureCompletion staged)
            {
                return true;
            }

            bool lifetimeConfirmed = staged.NativeCaptureRetirement.IsCompletedSuccessfully
                && staged.ManagedInvocationDrain.IsCompletedSuccessfully;
            // Observe failure freshly after terminal lifetime observation;
            // the pre-release sample cannot authorize shell/root return.
            if (staged.FirstFailure is { } failure)
            {
                RecordDisposalFailure(failure);
                // Keep the original failure reportable. A contained managed
                // callback fault is not unknown ownership once independent
                // releases and both terminal lifetime facts are confirmed.
            }

            return lifetimeConfirmed;
        }

        private bool HasActiveCompletionResourceUseAncestry() =>
            (startBlock is IMacOSRemoteWindowStagedCaptureCompletion start && start.HasActiveResourceUseAncestry)
            || (stopBlock is IMacOSRemoteWindowStagedCaptureCompletion stop && stop.HasActiveResourceUseAncestry);

        private static bool CloseCompletionResourceUse(IMacOSRemoteWindowCaptureCompletion? block) =>
            block is not IMacOSRemoteWindowStagedCaptureCompletion staged
            || staged.CloseResourceUse().IsCompletedSuccessfully;

        // Read only under the Capture gate. Each fixed invocation has one
        // synchronous scope, whose exit is distinct from normal handoff return.
        private bool HasActiveNativeInvocationBorrowCore() =>
            (startPoolPushAttempted && !startInvocationBorrowExited)
            || (stopPoolPushAttempted && !stopInvocationBorrowExited);

        private bool ReleaseBlock(IMacOSRemoteWindowCaptureCompletion? block, ref bool attempted, ref bool confirmed)
        {
            // Closing admission with active users is pending, not a native
            // release attempt. A later pass can observe the immutable join.
            if (!CloseCompletionResourceUse(block)) { return false; }

            lock (gate)
            {
                if (block is null) { return true; }
                if ((ReferenceEquals(block, startBlock) && startPoolPushAttempted && !startInvocationBorrowExited)
                    || (ReferenceEquals(block, stopBlock) && stopPoolPushAttempted && !stopInvocationBorrowExited))
                {
                    return false;
                }
                if (attempted) { return confirmed; }
                attempted = true;
            }

            bool released = false;
            try
            {
                Exception? before = block.FirstFailure;
                block.Dispose();
                released = block.IsReleased;
                if (block.FirstFailure is { } after && !ReferenceEquals(before, after))
                {
                    RecordDisposalFailure(after);
                }

                if (!released)
                {
                    RecordDisposalFailure(block.FirstFailure ?? releaseUnconfirmed);
                }

                return released;
            }
            catch (Exception exception)
            {
                RecordDisposalFailure(exception);
                return false;
            }
            finally
            {
                lock (gate) { confirmed = released; }
            }
        }

        private bool ReleaseObject(ref nint owner, ref bool attempted)
        {
            nint selected;
            lock (gate)
            {
                if (owner == 0)
                {
                    return true;
                }

                if (attempted)
                {
                    return false;
                }

                attempted = true;
                selected = owner;
            }

            try
            {
                operations.ReleaseObject(selected);
                lock (gate)
                {
                    owner = 0;
                }
                return true;
            }
            catch (Exception exception)
            {
                RecordDisposalFailure(exception);
                return false;
            }
        }

        private bool ReleaseQueue()
        {
            nint selected;
            lock (gate)
            {
                if (queue == 0) { return true; }
                if (queueReleaseAttempted) { return false; }
                queueReleaseAttempted = true;
                selected = queue;
            }

            try
            {
                operations.ReleaseQueue(selected);
                lock (gate) { queue = 0; }
                return true;
            }
            catch (Exception exception)
            {
                RecordDisposalFailure(exception);
                return false;
            }
        }

        private bool ReleaseSource()
        {
            lock (gate)
            {
                if (!sourceCleanupOwned)
                {
                    // A throwing retain may have acquired opaque native refs.
                    // Preserve the shell, but do not release a borrowed source.
                    return !sourceRetainAttempted;
                }
                if (sourceReleaseAttempted) { return sourceReleaseConfirmed; }
                sourceReleaseAttempted = true;
            }

            try
            {
                source.Dispose();
                lock (gate) { sourceReleaseConfirmed = true; }
                return true;
            }
            catch (Exception exception)
            {
                RecordDisposalFailure(exception);
                return false;
            }
        }

        private void RecordDisposalFailure(Exception exception)
        {
            Interlocked.Exchange(ref deliveryClosed, 1);
            MarkUnsafeFailure();
            RecordFatal(exception);
            Interlocked.CompareExchange(ref disposalFailure, exception, null);
        }

        private void ThrowDisposalFailure()
        {
            if (Volatile.Read(ref disposalFailure) is not null)
            {
                ThrowPendingFatal();
                throw releaseUnconfirmed;
            }
        }
    }
}
