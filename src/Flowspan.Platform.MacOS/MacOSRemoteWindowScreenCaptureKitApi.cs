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

    public async ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync()
    {
        if (!PreflightCaptureAccess() || !HasExistingApplication())
        {
            return [];
        }

        Runtime runtime = NativeRuntime.Value;
        var completion = new TaskCompletionSource<nint>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackExited = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OutOfMemoryException? callbackFatal = null;
        var failure = new InvalidOperationException("macOS source enumeration unavailable.");
        using var block = MacOSRemoteWindowBlock.Create((content, error) =>
        {
            if (error != 0 || content == 0)
            {
                completion.TrySetException(failure);
                return;
            }

            nint owner = N.objc_retain(content);
            if (!completion.TrySetResult(owner))
            {
                N.objc_release(owner);
            }
        }, exception =>
        {
            if (FindFatal(exception) is { } fatal)
            {
                Interlocked.CompareExchange(ref callbackFatal, fatal, null);
            }

            completion.TrySetException((Exception?)callbackFatal ?? failure);
        }, () => callbackExited.TrySetResult(true));

        nint pool = N.objc_autoreleasePoolPush();
        try
        {
            // This API can prompt when permission is absent; the prompt-free
            // preflight is repeated immediately before its sole invocation.
            if (!PreflightCaptureAccess())
            {
                return [];
            }

            N.Enumerate(runtime.ShareableClass,
                N.Sel("getShareableContentExcludingDesktopWindows:onScreenWindowsOnly:completionHandler:"),
                1, 1, block.Pointer);
        }
        finally
        {
            N.objc_autoreleasePoolPop(pool);
        }

        nint contentOwner = 0;
        List<IMacOSRemoteWindowNativeSource>? sources = null;
        pool = 0;
        try
        {
            contentOwner = await completion.Task.ConfigureAwait(false);
            await callbackExited.Task.ConfigureAwait(false);
            if (Volatile.Read(ref callbackFatal) is { } fatal)
            {
                ExceptionDispatchInfo.Capture(fatal).Throw();
            }

            pool = N.objc_autoreleasePoolPush();
            sources = [];
            nint windows = N.Send0(contentOwner, N.Sel("windows"));
            nuint count = Math.Min(N.GetNUInt(windows, N.Sel("count")), MaximumSources);
            for (nuint index = 0; index < count; index++)
            {
                nint window = N.SendIndex(windows, N.Sel("objectAtIndex:"), index);
                NativeSource? source = CreateSource(runtime, window);
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

            return sources;
        }
        catch (Exception exception)
        {
            await callbackExited.Task.ConfigureAwait(false);
            if (sources is not null)
            {
                foreach (IMacOSRemoteWindowNativeSource source in sources)
                {
                    source.Dispose();
                }
            }

            if (Volatile.Read(ref callbackFatal) is { } fatal)
            {
                ExceptionDispatchInfo.Capture(fatal).Throw();
            }

            if (FindFatal(exception) is { } capturedFatal)
            {
                ExceptionDispatchInfo.Capture(capturedFatal).Throw();
            }

            throw;
        }
        finally
        {
            if (contentOwner != 0)
            {
                N.objc_release(contentOwner);
            }
            if (pool != 0)
            {
                N.objc_autoreleasePoolPop(pool);
            }
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

    private NativeSource? CreateSource(Runtime runtime, nint window)
    {
        if (window == 0 || N.GetByte(window, N.Sel("isOnScreen")) == 0
            || N.GetNInt(window, N.Sel("windowLayer")) != 0)
        {
            return null;
        }

        uint windowId = N.GetUInt(window, N.Sel("windowID"));
        nint application = N.Send0(window, N.Sel("owningApplication"));
        int processId = N.GetInt(application, N.Sel("processID"));
        if (windowId == 0 || application == 0 || processId <= 0
            || (processId == Environment.ProcessId && !allowedOwnWindowIds.Contains(windowId))
            || !TryProcessIdentity(windowId, processId, out MacOSRemoteWindowNativeIdentity identity))
        {
            return null;
        }

        nint filter = N.Send1(N.Send0(runtime.FilterClass, N.Sel("alloc")),
            N.Sel("initWithDesktopIndependentWindow:"), window);
        if (filter == 0)
        {
            return null;
        }

        nint windowOwner = 0;
        try
        {
            MacOSCaptureRect frame = N.GetRect(window, N.Sel("frame"));
            float scale = N.GetFloat(filter, N.Sel("pointPixelScale"));
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
                || !CheckWindowInfo(runtime, identity, geometry))
            {
                return null;
            }

            windowOwner = N.objc_retain(window);
            var source = new NativeSource(identity, geometry, windowOwner, filter, sourceOperations);
            windowOwner = 0;
            filter = 0;
            return source;
        }
        finally
        {
            if (windowOwner != 0)
            {
                N.objc_release(windowOwner);
            }

            if (filter != 0)
            {
                N.objc_release(filter);
            }
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

    private sealed class NativeSource(
        MacOSRemoteWindowNativeIdentity identity,
        NativeRemoteWindowGeometry geometry,
        nint windowOwner,
        nint filterOwner, IMacOSRemoteWindowSourceOperations operations,
        NativeSource? acquisitionSource = null) : IMacOSRemoteWindowNativeSource
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
                    filterRetainConfirmed, ref filterReleaseAttempted)
                    & ReleaseOwner(ref window, windowRetainAttempted,
                        windowRetainConfirmed, ref windowReleaseAttempted);
                if (!released)
                {
                    if (Volatile.Read(ref ownerFatal) is { } fatal) { ExceptionDispatchInfo.Capture(fatal).Throw(); }
                    throw releaseUnconfirmed;
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

        private bool ReleaseOwner(ref nint owner, bool attempted, bool confirmed, ref bool releaseAttempted)
        {
            nint selected;
            lock (gate)
            {
                if (!confirmed) { return !attempted; }
                if (owner == 0) { return true; }
                if (releaseAttempted) { return false; }
                releaseAttempted = true;
                selected = owner;
            }
            try
            {
                operations.Release(selected);
                lock (gate) { owner = 0; }
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

    private sealed class NativeCaptureCompletion : IMacOSRemoteWindowCaptureCompletion
    {
        private readonly MacOSRemoteWindowBlock block;

        internal NativeCaptureCompletion(Action<nint> action, Action<Exception> failure, Action completed)
        {
            // The adapter itself exists before Block.Create acquires its +1.
            // Nothing is allocated between successful Create and assignment.
            block = MacOSRemoteWindowBlock.Create(action, failure, completed);
        }

        public nint Pointer => block.Pointer;
        public bool IsReleased => block.IsReleased;
        public Exception? FirstFailure => block.FirstFailure;
        public void Dispose() => block.Dispose();
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
            new NativeCaptureCompletion(action, failure, completed);

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
        private bool stopIssued;
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

                pool = operations.PushAutoreleasePool();
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
                factoryFailure = (Exception?)FindFatal(exception) ?? exception;
                unsafeFailure = true;
                Interlocked.Exchange(ref deliveryClosed, 1);
                if (!publicationAttempted)
                {
                    // No callback queue has ever been handed to native code.
                    // These absence facts permit one cleanup attempt; they
                    // do not prove a throwing native release had no effect.
                    startSettled = true;
                    stopSettled = true;
                    outputRemoved = true;
                    queueDrained = true;
                    Volatile.Write(ref nativeDrainConfirmed, 1);
                    if (rooted)
                    {
                        Roots.TryRemove(new KeyValuePair<nint, Capture>(output, this));
                    }

                    if (!ReleaseUnpublishedOwners())
                    {
                        failedFactoryCapture = this;
                    }
                }
                else
                {
                    failedFactoryCapture = this;
                    try
                    {
                        // A failed factory still has an independent owner.
                        // It can release only after confirmed absence/removal
                        // and the actual serial-queue barrier.
                        drain = RunIsolated(() => RollbackConstructionAsync(publicationRejected));
                    }
                    catch (Exception schedulingFailure)
                    {
                        RecordFatal(schedulingFailure);
                    }
                }

                // A tracked uncertain source acquisition or known-owner
                // release retains the complete shell independently of the
                // single failed-factory slot.
                ThrowPendingFatal();
                throw;
            }
            finally
            {
                if (pool != 0)
                {
                    operations.PopAutoreleasePool(pool);
                }
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
                    SetStartResult(error == 0);
                    if (error != 0)
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
                InvokeCompletion(startBlock.Pointer, isStart: true);
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
                    disposed = released;
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
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this))
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

            try
            {
                stopBlock = operations.CreateCompletion(error =>
                {
                    lock (gate)
                    {
                        stopSettled = error == 0;
                        unsafeFailure |= error != 0;
                    }

                    stopCompletion.TrySetResult(error == 0);
                }, exception =>
                {
                    RecordFatal(exception);
                    MarkUnsafeFailure();
                    stopCompletion.TrySetResult(false);
                }, () => stopCallbackExited.TrySetResult(true));
                InvokeCompletion(stopBlock.Pointer, isStart: false);
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
        }

        private void SetStartResult(bool success)
        {
            lock (gate)
            {
                startSettled = true;
            }

            if (Volatile.Read(ref fatalFailure) is { } fatal)
            {
                startCompletion.TrySetException(fatal);
            }
            else
            {
                startCompletion.TrySetResult(success);
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
            nint pool = operations.PushAutoreleasePool();
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
            finally
            {
                operations.PopAutoreleasePool(pool);
            }
        }

        private bool RemoveNativeOutput(nint stream, nint output)
        {
            nint pool = operations.PushAutoreleasePool();
            try
            {
                return operations.RemoveOutput(stream, output, out nint error) != 0 && error == 0;
            }
            finally
            {
                operations.PopAutoreleasePool(pool);
            }
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
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this))
            {
                throw new InvalidOperationException(
                    "macOS native callback cannot dispose its own cleanup owner.");
            }

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

                if (!IsDrainedCore())
                {
                    ThrowPendingFatal();
                    throw new InvalidOperationException("macOS capture cleanup remains unconfirmed.");
                }

                disposing = true;
            }

            try
            {
                bool released = ReleaseUnpublishedOwners();
                lock (gate)
                {
                    disposed = released;
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

                releasingOwners = true;
            }

            try
            {
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

        private bool ReleaseBlock(IMacOSRemoteWindowCaptureCompletion? block, ref bool attempted, ref bool confirmed)
        {
            lock (gate)
            {
                if (block is null) { return true; }
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
