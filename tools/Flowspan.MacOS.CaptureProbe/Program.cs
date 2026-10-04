// STANDALONE ABI PROBE. Not referenced by the solution or shipped product.
// No permission request or existing-user-window capture. Native capture opt-in only.
// Question: can C# own public ObjC protocol callbacks, copied Blocks, and retained
// synthetic CoreMedia samples without a Swift shim on the current arm64 macOS?
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class Program
{
    private static readonly ConcurrentDictionary<nint, OutputState> Outputs = new();
    private const uint Bgra = 0x42475241;

    private static int retainedCaptureOwners;

    private static int Main(string[] args)
    {
        bool nativeSelfWindow = args.Length == 1 && args[0] == "--native-self-window";
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: dotnet run --project tools/Flowspan.MacOS.CaptureProbe [-- --native-self-window]");
            Console.WriteLine("Default: synthetic ABI tests only. Native mode: prompt-free, exact tool-owned window only.");
            return 0;
        }
        if (args.Length != 0 && !nativeSelfWindow)
        {
            Console.Error.WriteLine("probe=fail; reason=unknown_arguments; use=--help");
            return 2;
        }
        if (!OperatingSystem.IsMacOSVersionAtLeast(14, 4) || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            Console.WriteLine("probe=skip; reason=requires_macos_14_4_or_later_ordinary_arm64; native_capture_executed=false");
            return 0;
        }

        NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        NativeLibrary.Load("/System/Library/Frameworks/ScreenCaptureKit.framework/ScreenCaptureKit");
        nint pool = N.objc_autoreleasePoolPush();
        try
        {
            Console.WriteLine($"runtime={RuntimeInformation.FrameworkDescription}; arch={RuntimeInformation.ProcessArchitecture}; mode={(nativeSelfWindow ? "native-self-window" : "synthetic-only")}");
            PrintSizes();
            VerifyConfiguration();
            VerifyCopiedBlocks();
            VerifyOutputAndSampleLifetime();
            if (nativeSelfWindow)
            {
                bool granted = N.CGPreflightScreenCaptureAccess() != 0;
                Console.WriteLine($"capture_preflight_granted={granted}");
                if (granted)
                {
                    BootstrapAppKit();
                    VerifyOwnWindowCapture();
                }
                else
                    Console.WriteLine("own_window_capture=skip; reason=permission_not_granted; native_capture_executed=false; requests=0");
            }
            else
                Console.WriteLine("own_window_capture=skip; reason=explicit_native_self_window_flag_required; preflight_called=false; AppKit_initialized=false; native_capture_executed=false");
            Check(retainedCaptureOwners == 0, "uncertain capture owners retained until process exit");
            Check(Volatile.Read(ref BlockState.LiveContexts) == 0, "all native completion/block GCHandles drained");
            Console.WriteLine("user_existing_window_capture_calls=0; window_title_reads=0; permissions_requested=0; pixel_files_written=0; all_block_GCHandle_contexts=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"probe=fail; managed_exception={exception.GetType().Name}; detail={exception.Message}; retained_capture_owners={retainedCaptureOwners}; exit_code=1");
            return 1;
        }
        finally
        {
            N.objc_autoreleasePoolPop(pool);
        }
    }

    private static void VerifyConfiguration()
    {
        nint config = N.Send0(N.objc_getClass("SCStreamConfiguration"), Sel("new"));
        Check(config != 0, "configuration allocation");
        try
        {
            N.SendNUInt(config, Sel("setWidth:"), 2);
            N.SendNUInt(config, Sel("setHeight:"), 2);
            N.SendUInt(config, Sel("setPixelFormat:"), Bgra);
            N.SendNInt(config, Sel("setQueueDepth:"), 3);
            N.SendByte(config, Sel("setCapturesAudio:"), 0);
            N.SendByte(config, Sel("setShowsCursor:"), 0);
            var interval = new CmTime { Value = 1, Timescale = 30, Flags = 1 };
            N.SendTime(config, Sel("setMinimumFrameInterval:"), interval);
            var rect = new Rect { X = 1, Y = 2, Width = 3, Height = 4 };
            N.SendRect(config, Sel("setSourceRect:"), rect);
            Check(N.GetNUInt(config, Sel("width")) == 2 && N.GetNUInt(config, Sel("height")) == 2, "dimensions round-trip");
            Check(N.GetUInt(config, Sel("pixelFormat")) == Bgra, "pixel format round-trip");
            Check(N.GetNInt(config, Sel("queueDepth")) == 3, "queue depth round-trip");
            CmTime actualTime = N.GetTime(config, Sel("minimumFrameInterval"));
            Rect actualRect = N.GetRect(config, Sel("sourceRect"));
            Check(actualTime.Value == 1 && actualTime.Timescale == 30 && actualTime.Flags == 1, "CMTime by-value ABI");
            Check(actualRect.X == 1 && actualRect.Y == 2 && actualRect.Width == 3 && actualRect.Height == 4, "CGRect by-value ABI");
            Console.WriteLine("stream_configuration=pass; width=2; height=2; format=BGRA; queueDepth=3; CMTime_roundtrip=pass; CGRect_roundtrip=pass");
        }
        finally { N.objc_release(config); }
    }

    private static unsafe void PrintSizes() => Console.WriteLine($"sizes: block={sizeof(BlockLiteral)}; descriptor={sizeof(BlockDescriptor)}; CGRect={sizeof(Rect)}; CMTime={sizeof(CmTime)}; timing={sizeof(SampleTiming)}");

    private static unsafe void VerifyCopiedBlocks()
    {
        nint queue = N.dispatch_queue_create("flowspan.synthetic.abi.probe", 0);
        Check(queue != 0, "dispatch queue allocation");
        bool suspended = false;
        try
        {
            int invoked = 0;
            List<BlockState> states = [];
            N.dispatch_suspend(queue);
            suspended = true;
            for (int i = 0; i < 1000; i++)
            {
                using var block = BlockOwner.Create(() => Interlocked.Increment(ref invoked));
                states.Add(block.State);
                N.dispatch_async(queue, block.Pointer);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check(invoked == 0, "suspended queue must not invoke blocks");
            N.dispatch_resume(queue);
            suspended = false;
            N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            Check(invoked == 1000, "native async invocation count");
            Check(states.All(s => s.References == 0 && s.CopyCount == 1 && s.DisposeCount == 1 && s.Failure is null), "native copied-block lifetime balance");
            Console.WriteLine("copied_blocks=pass; cycles=1000; callbacks=1000; copy_helpers=1000; dispose_helpers=1000; live_contexts=0; forced_GC_before_invoke=true");
        }
        finally
        {
            if (suspended) N.dispatch_resume(queue);
            N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            N.dispatch_release(queue);
        }
    }

    private static unsafe void VerifyOutputAndSampleLifetime()
    {
        Check(N.objc_getClass("SCStream") != 0, "SCStream class");
        _ = N.Send0(N.objc_getClass("SCStream"), Sel("class"));
        nint protocol = N.objc_getProtocol("SCStreamOutput");
        bool protocolWasRegistered = protocol != 0;
        nint selector = Sel("stream:didOutputSampleBuffer:ofType:");
        if (protocol == 0)
        {
            protocol = N.objc_allocateProtocol("SCStreamOutput");
            Check(protocol != 0, "public optional protocol allocation");
            nint baseProtocol = N.objc_getProtocol("NSObject");
            if (baseProtocol != 0) N.protocol_addProtocol(protocol, baseProtocol);
            // From the documented SCStreamOutput method declaration. Independently
            // confirmed by clang SDK compilation on ordinary arm64 and x86_64.
            nint types = Marshal.StringToCoTaskMemUTF8("v40@0:8@16^{opaqueCMSampleBuffer=}24q32");
            try { N.protocol_addMethodDescription(protocol, selector, types, 0, 1); }
            finally { Marshal.FreeCoTaskMem(types); }
            N.objc_registerProtocol(protocol);
        }
        Check(protocol != 0, "SCStreamOutput protocol");
        MethodDescription description = N.protocol_getMethodDescription(protocol, selector, 0, 1);
        Check(description.Types != 0, "optional output protocol method encoding");
        string encoding = Marshal.PtrToStringUTF8(description.Types)!;
        nint cls = N.objc_allocateClassPair(N.objc_getClass("NSObject"), "FlowspanCaptureProbeSyntheticOutput", 0);
        Check(cls != 0, "dynamic class allocation");
        Check(N.class_addProtocol(cls, protocol) != 0, "public protocol registration");
        Check(N.class_addMethod(cls, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&DidOutput, description.Types) != 0, "public method registration");
        N.objc_registerClassPair(cls);
        nint output = N.Send0(cls, Sel("new"));
        Check(output != 0, "synthetic output allocation");
        nint queue = 0;
        var state = new OutputState();
        try
        {
            queue = N.dispatch_queue_create("flowspan.synthetic.output.probe", 0);
            Check(queue != 0, "synthetic output queue allocation");
            Check(Outputs.TryAdd(output, state), "managed callback owner registration");
            Check(N.GetByte(output, Sel("conformsToProtocol:"), protocol) != 0, "native protocol conformance");
            for (int i = 0; i < 1000; i++)
            {
                nint pixel = 0, format = 0, sample = 0;
                try
                {
                    Check(N.CVPixelBufferCreate(0, 2, 2, Bgra, 0, out pixel) == 0, "synthetic pixel create");
                    Check(N.CVPixelBufferLockBaseAddress(pixel, 0) == 0, "synthetic write lock");
                    try
                    {
                        nint address = N.CVPixelBufferGetBaseAddress(pixel);
                        nuint stride = N.CVPixelBufferGetBytesPerRow(pixel);
                        Check(address != 0 && stride >= 8, "synthetic row geometry");
                        for (int row = 0; row < 2; row++)
                            new Span<byte>((void*)(address + (nint)(stride * (nuint)row)), 8).Fill(0x5a);
                    }
                    finally { Check(N.CVPixelBufferUnlockBaseAddress(pixel, 0) == 0, "synthetic write unlock"); }
                    Check(N.CMVideoFormatDescriptionCreateForImageBuffer(0, pixel, out format) == 0, "format create");
                    var timing = new SampleTiming
                    {
                        Duration = new CmTime { Value = 1, Timescale = 30, Flags = 1 },
                        Presentation = new CmTime { Value = i, Timescale = 30, Flags = 1 },
                    };
                    Check(N.CMSampleBufferCreateReadyWithImageBuffer(0, pixel, format, in timing, out sample) == 0, "sample create");
                    nint callbackSample = sample;
                    using var callbackBlock = BlockOwner.Create(() => N.SendOutput(output, selector, 0, callbackSample, 0));
                    N.dispatch_async(queue, callbackBlock.Pointer);
                    N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
                    Check(state.RetainedSample != 0 && state.Failure is null, "reverse native output callback");
                }
                finally
                {
                    if (sample != 0) N.CFRelease(sample);
                    if (format != 0) N.CFRelease(format);
                    if (pixel != 0) N.CFRelease(pixel);
                }
                nint retained = state.Take();
                try
                {
                    nint image = N.CMSampleBufferGetImageBuffer(retained);
                    Check(image != 0 && N.CVPixelBufferGetWidth(image) == 2 && N.CVPixelBufferGetHeight(image) == 2, "sample survives producer release");
                    Check(N.CVPixelBufferLockBaseAddress(image, 1) == 0, "retained read lock");
                    try
                    {
                        nint address = N.CVPixelBufferGetBaseAddress(image);
                        nuint stride = N.CVPixelBufferGetBytesPerRow(image);
                        Check(stride >= 8, "bounded read stride");
                        byte[] bytes = new byte[16];
                        for (int row = 0; row < 2; row++)
                            new ReadOnlySpan<byte>((void*)(address + (nint)(stride * (nuint)row)), 8).CopyTo(bytes.AsSpan(row * 8, 8));
                        Check(bytes.All(b => b == 0x5a), "bounded synthetic copy");
                    }
                    finally { Check(N.CVPixelBufferUnlockBaseAddress(image, 1) == 0, "retained read unlock"); }
                }
                finally { N.CFRelease(retained); }
            }
            Check(state.CallbackCount == 1000 && state.RetainedSample == 0, "output lifetime drain");
            Console.WriteLine($"objc_output_callback=pass; native_dispatch_thread=true; protocol_pre_registered={protocolWasRegistered}; protocol_encoding={encoding}; synthetic_sample_cycles=1000; retained_after_producer_release=pass; bounded_row_copy=pass; live_retained_samples=0");
        }
        finally
        {
            if (queue != 0) N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            Outputs.TryRemove(output, out _);
            if (state.RetainedSample != 0) N.CFRelease(state.Take());
            if (queue != 0) N.dispatch_release(queue);
            N.objc_release(output);
        }
    }

    private static void BootstrapAppKit()
    {
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        Check(N.NSApplicationLoad() != 0, "documented AppKit initialization");
        nint applicationOwner = N.Send0(N.objc_getClass("NSApplication"), Sel("sharedApplication"));
        Check(applicationOwner != 0, "AppKit main application bootstrap");
        _ = N.GetByte(applicationOwner, Sel("setActivationPolicy:"), 2);
        Console.WriteLine("AppKit_bootstrap=pass; activation_policy=prohibited; runloop=pumped");
    }

    private static nint EnumerateForOwnWindow()
    {
        var completion = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var block = BlockOwner.Create((content, error) =>
        {
            if (error != 0 || content == 0)
                completion.TrySetException(new InvalidOperationException("Native own-window enumeration failed; native text suppressed."));
            else
            {
                N.objc_retain(content);
                if (!completion.TrySetResult(content)) N.objc_release(content);
            }
        });
        try
        {
            // macOS 14.4's documented redacted current-process API does not
            // require user consent via TCC; never enumerate global user content.
            N.SendVoidPointer(N.objc_getClass("SCShareableContent"), Sel("getCurrentProcessShareableContentWithCompletionHandler:"), block.Pointer);
            return AwaitWithRunloop(completion.Task, TimeSpan.FromSeconds(15));
        }
        catch
        {
            // The timeout/result race has exactly one releaser. A completion
            // after cancellation releases its own retain in the callback.
            if (!completion.TrySetCanceled() && completion.Task.IsCompletedSuccessfully)
                N.objc_release(completion.Task.Result);
            throw;
        }
    }

    private static unsafe void VerifyOwnWindowCapture()
    {
        nint window = 0, view = 0, content = 0, ownWindow = 0, filter = 0, config = 0, output = 0, stream = 0, queue = 0;
        bool outputAdded = false, startAttempted = false, startCompletionUnconfirmed = false, stopCompletionUnconfirmed = false, stopped = false;
        var capture = new CaptureState();
        var outputState = new OutputState { SampleObserver = sample => ObserveOwnFrame(capture, sample) };
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
            window = N.InitWindow(N.Send0(N.objc_getClass("NSWindow"), Sel("alloc")), Sel("initWithContentRect:styleMask:backing:defer:"),
                new Rect { X = 100, Y = 100, Width = 64, Height = 64 }, 0, 2, 0);
            Check(window != 0, "synthetic NSWindow allocation");
            N.SendByte(window, Sel("setReleasedWhenClosed:"), 0);
            N.SendByte(window, Sel("setIgnoresMouseEvents:"), 1);
            view = N.InitView(N.Send0(N.objc_getClass("NSView"), Sel("alloc")), Sel("initWithFrame:"), new Rect { Width = 64, Height = 64 });
            Check(view != 0, "synthetic NSView allocation");
            N.SendByte(view, Sel("setWantsLayer:"), 1);
            nint rootLayer = N.Send0(view, Sel("layer"));
            Check(rootLayer != 0, "synthetic layer-backed view");
            nint srgb = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics"), "kCGColorSpaceSRGB"));
            nint srgbSpace = N.CGColorSpaceCreateWithName(srgb);
            Check(srgbSpace != 0, "synthetic sRGB color space");
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    nint layer = N.Send0(N.objc_getClass("CALayer"), Sel("new"));
                    nint color = 0;
                    try
                    {
                        Check(layer != 0, "synthetic CALayer allocation");
                        var components = new Rgba { Red = i == 0 || i == 3 ? 1 : 0, Green = i == 1 || i == 3 ? 1 : 0, Blue = i == 2 || i == 3 ? 1 : 0, Alpha = 1 };
                        color = N.CGColorCreate(srgbSpace, in components);
                        Check(color != 0, "synthetic CGColor allocation");
                        N.SendRect(layer, Sel("setFrame:"), new Rect { X = i % 2 * 32, Y = i / 2 * 32, Width = 32, Height = 32 });
                        _ = N.Send1(layer, Sel("setBackgroundColor:"), color);
                        _ = N.Send1(rootLayer, Sel("addSublayer:"), layer);
                    }
                    finally
                    {
                        if (color != 0) N.CFRelease(color);
                        if (layer != 0) N.objc_release(layer);
                    }
                }
            }
            finally { N.CFRelease(srgbSpace); }
            _ = N.Send1(window, Sel("setContentView:"), view);
            N.objc_release(view);
            view = 0;
            _ = N.Send1(window, Sel("orderFront:"), 0);
            _ = N.Send0(window, Sel("displayIfNeeded"));
            Pump(TimeSpan.FromMilliseconds(350));
            nint number = N.GetNInt(window, Sel("windowNumber"));
            Check(number > 0 && number <= uint.MaxValue, "synthetic exact window number");
            content = EnumerateForOwnWindow();
            nint windows = N.Send0(content, Sel("windows"));
            nuint count = N.GetNUInt(windows, Sel("count"));
            for (nuint i = 0; i < count && i < 128; i++)
            {
                nint candidate = N.SendIndex(windows, Sel("objectAtIndex:"), i);
                if (N.GetUInt(candidate, Sel("windowID")) != (uint)number) continue;
                nint app = N.Send0(candidate, Sel("owningApplication"));
                Check(app != 0 && N.GetInt(app, Sel("processID")) == Environment.ProcessId, "exact source belongs to probe PID");
                ownWindow = N.objc_retain(candidate);
                break;
            }
            Check(ownWindow != 0, "own synthetic window present");
            filter = N.Send1(N.Send0(N.objc_getClass("SCContentFilter"), Sel("alloc")), Sel("initWithDesktopIndependentWindow:"), ownWindow);
            Check(filter != 0, "own exact-window filter");
            config = N.Send0(N.objc_getClass("SCStreamConfiguration"), Sel("new"));
            Check(config != 0, "capture configuration allocation");
            N.SendNUInt(config, Sel("setWidth:"), 64);
            N.SendNUInt(config, Sel("setHeight:"), 64);
            N.SendUInt(config, Sel("setPixelFormat:"), Bgra);
            N.SendNInt(config, Sel("setQueueDepth:"), 3);
            N.SendByte(config, Sel("setCapturesAudio:"), 0);
            N.SendByte(config, Sel("setShowsCursor:"), 0);
            N.SendByte(config, Sel("setScalesToFit:"), 1);
            N.SendByte(config, Sel("setIgnoreShadowsSingleWindow:"), 1);
            N.SendByte(config, Sel("setIncludeChildWindows:"), 0);
            N.SendTime(config, Sel("setMinimumFrameInterval:"), new CmTime { Value = 1, Timescale = 30, Flags = 1 });
            _ = N.Send1(config, Sel("setColorSpaceName:"), srgb);
            output = N.Send0(N.objc_getClass("FlowspanCaptureProbeSyntheticOutput"), Sel("new"));
            Check(output != 0, "capture output allocation");
            Check(Outputs.TryAdd(output, outputState), "capture callback owner root");
            queue = N.dispatch_queue_create("flowspan.own-window.capture.probe", 0);
            Check(queue != 0, "capture queue allocation");
            stream = N.InitStream(N.Send0(N.objc_getClass("SCStream"), Sel("alloc")), Sel("initWithFilter:configuration:delegate:"), filter, config, 0);
            Check(stream != 0, "own-window SCStream allocation");
            Check(N.AddOutput(stream, Sel("addStreamOutput:type:sampleHandlerQueue:error:"), output, 0, queue, out nint addError) != 0 && addError == 0, "native output registration");
            outputAdded = true;
            var start = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var startBlock = BlockOwner.Create(error => start.TrySetResult(error == 0 ? 0 : N.GetNInt(error, Sel("code")))))
            {
                startAttempted = true;
                startCompletionUnconfirmed = true;
                _ = N.Send1(stream, Sel("startCaptureWithCompletionHandler:"), startBlock.Pointer);
                nint startError = AwaitWithRunloop(start.Task, TimeSpan.FromSeconds(15));
                startCompletionUnconfirmed = false;
                Check(startError == 0, "own-window capture start");
            }
            long deadline = Environment.TickCount64 + 10000;
            while (Volatile.Read(ref capture.MatchedFrames) < 1 && Volatile.Read(ref capture.Failure) is null && Environment.TickCount64 < deadline)
                Pump(TimeSpan.FromMilliseconds(25));
            Check(capture.Failure is null && outputState.Failure is null, $"own-frame callback validation: frame={capture.Failure}; managed_type={outputState.Failure}");
            Check(Volatile.Read(ref capture.MatchedFrames) >= 1, "own-window marker pattern frame");
            Volatile.Write(ref capture.AdmissionOpen, 0);
            stopCompletionUnconfirmed = true;
            StopStream(stream);
            stopCompletionUnconfirmed = false;
            stopped = true;
            N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            Check(N.RemoveOutput(stream, Sel("removeStreamOutput:type:error:"), output, 0, out nint removeError) != 0 && removeError == 0, "native output removal");
            outputAdded = false;
            N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            int callbacksAtDrain = Volatile.Read(ref capture.Callbacks);
            long observationStart = Environment.TickCount64;
            Pump(TimeSpan.FromMilliseconds(500));
            long observationMilliseconds = Environment.TickCount64 - observationStart;
            Check(observationMilliseconds >= 500, "late callback observation duration");
            N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            Check(Volatile.Read(ref capture.Callbacks) == callbacksAtDrain, "no callback after stop/remove/drain observation");
            Console.WriteLine($"own_window_capture=pass; source_pid_and_window_number_matched=true; dimensions=64x64; matched_frames={capture.MatchedFrames}; callbacks={capture.Callbacks}; complete_frames={capture.CompleteFrames}; marker_hash={capture.PatternHash:x16}; full_frame_hash={capture.FullFrameHash:x16}; stop_completion=pass; output_removed=true; queue_drained=true; late_callbacks=0; late_observation_ms={observationMilliseconds}; bounded_buffer_bytes=16384; marker_bytes=4096; retained_native_samples=0");
        }
        finally
        {
            Volatile.Write(ref capture.AdmissionOpen, 0);
            bool confirmed = (!startAttempted || stopped) && !startCompletionUnconfirmed && !stopCompletionUnconfirmed;
            try
            {
                if (startAttempted && !stopped && stream != 0)
                {
                    StopStream(stream);
                    // A separate Stop completion cannot prove ordering against
                    // a Start whose completion was never observed.
                    confirmed = !startCompletionUnconfirmed && !stopCompletionUnconfirmed;
                }
                if (outputAdded && stream != 0 && output != 0)
                {
                    Check(N.RemoveOutput(stream, Sel("removeStreamOutput:type:error:"), output, 0, out nint error) != 0 && error == 0, "cleanup native output removal");
                    outputAdded = false;
                }
                if (queue != 0)
                    N.dispatch_sync_f(queue, 0, (nint)(delegate* unmanaged[Cdecl]<nint, void>)&NoOp);
            }
            catch (Exception exception)
            {
                confirmed = false;
                Console.Error.WriteLine($"native_cleanup=unconfirmed; managed_exception={exception.GetType().Name}; frame_admission_closed=true; retained_until_process_exit=true");
            }

            if (confirmed && !outputAdded)
            {
                if (stream != 0) N.objc_release(stream);
                if (output != 0) { Outputs.TryRemove(output, out _); N.objc_release(output); }
                if (queue != 0) N.dispatch_release(queue);
                if (config != 0) N.objc_release(config);
                if (filter != 0) N.objc_release(filter);
                if (ownWindow != 0) N.objc_release(ownWindow);
                if (content != 0) N.objc_release(content);
                if (view != 0) N.objc_release(view);
                if (window != 0) { _ = N.Send0(window, Sel("close")); N.objc_release(window); }
            }
            else
            {
                // This process is a short-lived probe, not a recoverable service.
                // Keep native resources and the rooted callback state alive.
                // The OS reclaims them after Main returns a nonzero exit code.
                Interlocked.Increment(ref retainedCaptureOwners);
                if (window != 0) _ = N.Send0(window, Sel("close"));
            }
        }
    }

    private static void StopStream(nint stream)
    {
        var stop = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopBlock = BlockOwner.Create(error => stop.TrySetResult(error == 0 ? 0 : N.GetNInt(error, Sel("code"))));
        _ = N.Send1(stream, Sel("stopCaptureWithCompletionHandler:"), stopBlock.Pointer);
        Check(AwaitWithRunloop(stop.Task, TimeSpan.FromSeconds(15)) == 0, "capture stop completion");
    }

    private static unsafe void ObserveOwnFrame(CaptureState state, nint sample)
    {
        Interlocked.Increment(ref state.Callbacks);
        if (Volatile.Read(ref state.AdmissionOpen) == 0) return;
        if (N.CMSampleBufferIsValid(sample) == 0 || N.CMSampleBufferDataIsReady(sample) == 0) return;
        nint attachments = N.CMSampleBufferGetSampleAttachmentsArray(sample, 0);
        if (attachments == 0 || N.CFArrayGetCount(attachments) < 1) return;
        nint dictionary = N.CFArrayGetValueAtIndex(attachments, 0);
        nint statusKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load("/System/Library/Frameworks/ScreenCaptureKit.framework/ScreenCaptureKit"), "SCStreamFrameInfoStatus"));
        nint status = N.CFDictionaryGetValue(dictionary, statusKey);
        if (status == 0 || N.GetNInt(status, Sel("integerValue")) != 0) return;
        Interlocked.Increment(ref state.CompleteFrames);
        nint image = N.CMSampleBufferGetImageBuffer(sample);
        if (image == 0 || N.CVPixelBufferGetWidth(image) != 64 || N.CVPixelBufferGetHeight(image) != 64 || N.CVPixelBufferGetPixelFormatType(image) != Bgra)
        { Volatile.Write(ref state.Failure, "dimensions_or_format"); return; }
        if (N.CVPixelBufferLockBaseAddress(image, 1) != 0) { Volatile.Write(ref state.Failure, "read_lock"); return; }
        try
        {
            nint address = N.CVPixelBufferGetBaseAddress(image);
            nuint stride = N.CVPixelBufferGetBytesPerRow(image);
            if (address == 0 || stride < 256 || stride > 16384) { Volatile.Write(ref state.Failure, "stride_bound"); return; }
            byte[] bytes = new byte[16384];
            for (int row = 0; row < 64; row++)
                new ReadOnlySpan<byte>((void*)(address + (nint)(stride * (nuint)row)), 256).CopyTo(bytes.AsSpan(row * 256, 256));
            state.FullFrameHash = Hash(bytes);
            // Native scaling can interpolate quadrant boundaries. Compare fixed
            // interior markers, whose expected byte values are exact and known.
            ulong hash = Hash(ExtractMarkers(bytes));
            ulong normal = PatternHash(false), flipped = PatternHash(true);
            if (hash == normal || hash == flipped)
            {
                state.PatternHash = hash;
                Interlocked.Increment(ref state.MatchedFrames);
            }
            else
            {
                // Only task-owned pixels; output bounded center values to explain
                // legitimate color/orientation differences in this native probe.
                state.MarkerCenters = string.Join("/", new[] { (16, 16), (48, 16), (16, 48), (48, 48) }.Select(p => Convert.ToHexString(bytes.AsSpan((p.Item2 * 64 + p.Item1) * 4, 4))));
                Volatile.Write(ref state.Failure, $"pattern_mismatch_{hash:x16}_centers_{state.MarkerCenters}");
            }
        }
        finally { if (N.CVPixelBufferUnlockBaseAddress(image, 1) != 0) Volatile.Write(ref state.Failure, "read_unlock"); }
    }

    private static ulong PatternHash(bool flipped)
    {
        byte[] bytes = new byte[16384];
        for (int row = 0; row < 64; row++) for (int col = 0; col < 64; col++)
        {
            int quadrant = col / 32 + ((flipped ? 63 - row : row) / 32) * 2;
            int index = (row * 64 + col) * 4;
            bytes[index] = quadrant == 2 || quadrant == 3 ? (byte)255 : (byte)0;
            bytes[index + 1] = quadrant == 1 || quadrant == 3 ? (byte)255 : (byte)0;
            bytes[index + 2] = quadrant == 0 || quadrant == 3 ? (byte)255 : (byte)0;
            bytes[index + 3] = 255;
        }
        return Hash(ExtractMarkers(bytes));
    }

    private static byte[] ExtractMarkers(byte[] bytes)
    {
        byte[] markers = new byte[4096];
        for (int quadrant = 0; quadrant < 4; quadrant++) for (int row = 0; row < 16; row++)
        {
            int x = quadrant % 2 * 32 + 8;
            int y = quadrant / 2 * 32 + 8 + row;
            bytes.AsSpan((y * 64 + x) * 4, 64).CopyTo(markers.AsSpan((quadrant * 16 + row) * 64, 64));
        }
        return markers;
    }

    private static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        ulong value = 14695981039346656037;
        foreach (byte b in bytes) value = unchecked((value ^ b) * 1099511628211);
        return value;
    }

    private static void Pump(TimeSpan duration)
    {
        nint mode = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation"), "kCFRunLoopDefaultMode"));
        long deadline = Environment.TickCount64 + (long)Math.Ceiling(duration.TotalMilliseconds);
        while (Environment.TickCount64 < deadline)
            _ = N.CFRunLoopRunInMode(mode, Math.Min(0.02, (deadline - Environment.TickCount64) / 1000.0), 0);
    }

    private static T AwaitWithRunloop<T>(Task<T> task, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!task.IsCompleted && Environment.TickCount64 < deadline) Pump(TimeSpan.FromMilliseconds(20));
        if (!task.IsCompleted) throw new TimeoutException("Native operation timed out.");
        return task.GetAwaiter().GetResult();
    }

    private sealed class CaptureState
    {
        public int AdmissionOpen = 1, Callbacks, CompleteFrames, MatchedFrames;
        public ulong PatternHash, FullFrameHash;
        public string? Failure, MarkerCenters;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidOutput(nint self, nint selector, nint stream, nint sample, nint kind)
    {
        OutputState? state = null;
        try
        {
            if (!Outputs.TryGetValue(self, out state)) return;
            if (sample == 0 || kind != 0) throw new InvalidOperationException("Invalid output callback.");
            if (state.SampleObserver is not null) { state.SampleObserver(sample); return; }
            if (state.RetainedSample != 0) throw new InvalidOperationException("Invalid synthetic callback.");
            state.RetainedSample = N.CFRetain(sample);
            state.CallbackCount++;
        }
        catch
        {
            if (state is null) Environment.FailFast("Probe callback registry invariant failed.");
            state.Failure = "managed_output_callback_failed";
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void NoOp(nint context) { }
    private static nint Sel(string name) => N.sel_registerName(name);
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private sealed class OutputState
    {
        public nint RetainedSample;
        public int CallbackCount;
        public string? Failure;
        public Action<nint>? SampleObserver;
        public nint Take() => Interlocked.Exchange(ref RetainedSample, 0);
    }
}

[StructLayout(LayoutKind.Sequential)] internal struct Rect { public double X, Y, Width, Height; }
[StructLayout(LayoutKind.Sequential)] internal struct Rgba { public double Red, Green, Blue, Alpha; }
[StructLayout(LayoutKind.Sequential, Pack = 4)] internal struct CmTime { public long Value; public int Timescale; public uint Flags; public long Epoch; }
[StructLayout(LayoutKind.Sequential, Pack = 4)] internal struct SampleTiming { public CmTime Duration, Presentation, Decode; }
[StructLayout(LayoutKind.Sequential)] internal struct MethodDescription { public nint Name, Types; }
[StructLayout(LayoutKind.Sequential)] internal struct BlockLiteral { public nint Isa; public int Flags, Reserved; public nint Invoke, Descriptor, Context; }
[StructLayout(LayoutKind.Sequential)] internal struct BlockDescriptor { public nuint Reserved, Size; public nint Copy, Dispose, Signature; }

internal sealed class BlockState
{
    public static int LiveContexts;
    public int References = 1, CopyCount, DisposeCount;
    public nint Handle;
    public Action? ZeroArguments;
    public Action<nint, nint>? TwoArguments;
    public Action<nint>? OneArgument;
    public string? Failure;
    public void Release()
    {
        if (Interlocked.Decrement(ref References) == 0)
        {
            GCHandle.FromIntPtr(Handle).Free();
            Interlocked.Decrement(ref LiveContexts);
        }
    }
}

internal sealed unsafe class BlockOwner : IDisposable
{
    private static readonly nint StackIsa = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteStackBlock");
    private static readonly nint Descriptor0 = CreateDescriptor("v8@?0");
    private static readonly nint Descriptor2 = CreateDescriptor("v24@?0@8@16");
    private static readonly nint Descriptor1 = CreateDescriptor("v16@?0@8");
    private nint pointer;
    public nint Pointer => pointer;
    public BlockState State { get; }
    private BlockOwner(BlockState state, int argumentCount)
    {
        State = state;
        state.Handle = GCHandle.ToIntPtr(GCHandle.Alloc(state));
        Interlocked.Increment(ref BlockState.LiveContexts);
        BlockLiteral* source = null;
        try
        {
            source = (BlockLiteral*)NativeMemory.Alloc((nuint)sizeof(BlockLiteral));
            if (source == null) throw new OutOfMemoryException();
            *source = new BlockLiteral
            {
                Isa = StackIsa,
                Flags = (1 << 25) | (1 << 30),
                Invoke = argumentCount == 2 ? (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Invoke2 : argumentCount == 1 ? (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&Invoke1 : (nint)(delegate* unmanaged[Cdecl]<nint, void>)&Invoke0,
                Descriptor = argumentCount == 2 ? Descriptor2 : argumentCount == 1 ? Descriptor1 : Descriptor0,
                Context = state.Handle,
            };
            pointer = N._Block_copy((nint)source);
        }
        finally
        {
            NativeMemory.Free(source);
            state.Release();
        }
        if (pointer == 0) throw new OutOfMemoryException();
    }
    public static BlockOwner Create(Action action) => new(new BlockState { ZeroArguments = action }, 0);
    public static BlockOwner Create(Action<nint> action) => new(new BlockState { OneArgument = action }, 1);
    public static BlockOwner Create(Action<nint, nint> action) => new(new BlockState { TwoArguments = action }, 2);
    public void Dispose() { nint p = Interlocked.Exchange(ref pointer, 0); if (p != 0) N._Block_release(p); }
    private static nint CreateDescriptor(string signature)
    {
        var descriptor = (BlockDescriptor*)NativeMemory.Alloc((nuint)sizeof(BlockDescriptor));
        if (descriptor == null) throw new OutOfMemoryException();
        try
        {
            *descriptor = new BlockDescriptor
            {
                Size = (nuint)sizeof(BlockLiteral),
                Copy = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&Copy,
                Dispose = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&Release,
                Signature = Marshal.StringToCoTaskMemUTF8(signature),
            };
            return (nint)descriptor;
        }
        catch
        {
            NativeMemory.Free(descriptor);
            throw;
        }
    }
    private static BlockState StateOf(nint block) => (BlockState)GCHandle.FromIntPtr(((BlockLiteral*)block)->Context).Target!;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Copy(nint destination, nint source)
    {
        try
        {
            BlockState state = StateOf(source);
            Interlocked.Increment(ref state.References);
            Interlocked.Increment(ref state.CopyCount);
        }
        catch { Environment.FailFast("Probe copied Block owner invariant failed."); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(nint block)
    {
        try
        {
            BlockState state = StateOf(block);
            Interlocked.Increment(ref state.DisposeCount);
            state.Release();
        }
        catch { Environment.FailFast("Probe released Block owner invariant failed."); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Invoke0(nint block)
    {
        BlockState? state = null;
        try { state = StateOf(block); state.ZeroArguments!(); }
        catch
        {
            if (state is null) Environment.FailFast("Probe Block context invariant failed.");
            state.Failure = "managed_block_callback_failed";
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Invoke2(nint block, nint argument0, nint argument1)
    {
        BlockState? state = null;
        try { state = StateOf(block); state.TwoArguments!(argument0, argument1); }
        catch
        {
            if (state is null) Environment.FailFast("Probe Block context invariant failed.");
            state.Failure = "managed_block_callback_failed";
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Invoke1(nint block, nint argument)
    {
        BlockState? state = null;
        try { state = StateOf(block); state.OneArgument!(argument); }
        catch
        {
            if (state is null) Environment.FailFast("Probe Block context invariant failed.");
            state.Failure = "managed_block_callback_failed";
        }
    }
}

internal static partial class N
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string System = "/usr/lib/libSystem.B.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    [LibraryImport("/System/Library/Frameworks/AppKit.framework/AppKit")] public static partial byte NSApplicationLoad();
    [LibraryImport(CoreFoundation)] public static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);
    [LibraryImport(ObjC)] public static partial nint objc_autoreleasePoolPush();
    [LibraryImport(ObjC)] public static partial void objc_autoreleasePoolPop(nint pool);
    [LibraryImport(ObjC)] public static partial nint objc_retain(nint value);
    [LibraryImport(ObjC)] public static partial void objc_release(nint value);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_getClass(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_getProtocol(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_allocateProtocol(string name);
    [LibraryImport(ObjC)] public static partial void objc_registerProtocol(nint protocol);
    [LibraryImport(ObjC)] public static partial void protocol_addProtocol(nint protocol, nint adoptedProtocol);
    [LibraryImport(ObjC)] public static partial void protocol_addMethodDescription(nint protocol, nint selector, nint types, byte required, byte instance);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint sel_registerName(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);
    [LibraryImport(ObjC)] public static partial void objc_registerClassPair(nint cls);
    [LibraryImport(ObjC)] public static partial byte class_addProtocol(nint cls, nint protocol);
    [LibraryImport(ObjC)] public static partial byte class_addMethod(nint cls, nint selector, nint implementation, nint types);
    [LibraryImport(ObjC)] public static partial MethodDescription protocol_getMethodDescription(nint protocol, nint selector, byte required, byte instance);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint Send0(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint Send1(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint SendIndex(nint receiver, nint selector, nuint index);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendNUInt(nint receiver, nint selector, nuint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendNInt(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendUInt(nint receiver, nint selector, uint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendByte(nint receiver, nint selector, byte value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendTime(nint receiver, nint selector, CmTime value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendRect(nint receiver, nint selector, Rect value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nuint GetNUInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint GetNInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial uint GetUInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial int GetInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial byte GetByte(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial byte GetByte(nint receiver, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial CmTime GetTime(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial Rect GetRect(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendOutput(nint receiver, nint selector, nint stream, nint sample, nint kind);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial void SendVoidPointer(nint receiver, nint selector, nint block);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint InitWindow(nint receiver, nint selector, Rect rect, nuint style, nuint backing, byte defer);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint InitView(nint receiver, nint selector, Rect rect);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial nint InitStream(nint receiver, nint selector, nint filter, nint config, nint callbackDelegate);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial byte AddOutput(nint receiver, nint selector, nint output, nint type, nint queue, out nint error);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial byte RemoveOutput(nint receiver, nint selector, nint output, nint type, out nint error);
    [LibraryImport(System)] public static partial nint _Block_copy(nint block);
    [LibraryImport(System)] public static partial void _Block_release(nint block);
    [LibraryImport(System, StringMarshalling = StringMarshalling.Utf8)] public static partial nint dispatch_queue_create(string label, nint attribute);
    [LibraryImport(System)] public static partial void dispatch_async(nint queue, nint block);
    [LibraryImport(System)] public static partial void dispatch_sync_f(nint queue, nint context, nint function);
    [LibraryImport(System)] public static partial void dispatch_suspend(nint queue);
    [LibraryImport(System)] public static partial void dispatch_resume(nint queue);
    [LibraryImport(System)] public static partial void dispatch_release(nint queue);
    [LibraryImport(CoreGraphics)] public static partial byte CGPreflightScreenCaptureAccess();
    [LibraryImport(CoreGraphics)] public static partial nint CGColorSpaceCreateWithName(nint name);
    [LibraryImport(CoreGraphics)] public static partial nint CGColorCreate(nint space, in Rgba components);
    [LibraryImport(CoreFoundation)] public static partial nint CFRetain(nint value);
    [LibraryImport(CoreFoundation)] public static partial void CFRelease(nint value);
    [LibraryImport(CoreFoundation)] public static partial nint CFArrayGetCount(nint array);
    [LibraryImport(CoreFoundation)] public static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(CoreFoundation)] public static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(CoreVideo)] public static partial int CVPixelBufferCreate(nint allocator, nuint width, nuint height, uint pixelFormat, nint attributes, out nint pixel);
    [LibraryImport(CoreVideo)] public static partial int CVPixelBufferLockBaseAddress(nint pixel, nuint flags);
    [LibraryImport(CoreVideo)] public static partial int CVPixelBufferUnlockBaseAddress(nint pixel, nuint flags);
    [LibraryImport(CoreVideo)] public static partial nint CVPixelBufferGetBaseAddress(nint pixel);
    [LibraryImport(CoreVideo)] public static partial nuint CVPixelBufferGetBytesPerRow(nint pixel);
    [LibraryImport(CoreVideo)] public static partial nuint CVPixelBufferGetWidth(nint pixel);
    [LibraryImport(CoreVideo)] public static partial nuint CVPixelBufferGetHeight(nint pixel);
    [LibraryImport(CoreVideo)] public static partial uint CVPixelBufferGetPixelFormatType(nint pixel);
    [LibraryImport(CoreMedia)] public static partial int CMVideoFormatDescriptionCreateForImageBuffer(nint allocator, nint pixel, out nint format);
    [LibraryImport(CoreMedia)] public static partial int CMSampleBufferCreateReadyWithImageBuffer(nint allocator, nint pixel, nint format, in SampleTiming timing, out nint sample);
    [LibraryImport(CoreMedia)] public static partial nint CMSampleBufferGetImageBuffer(nint sample);
    [LibraryImport(CoreMedia)] public static partial byte CMSampleBufferIsValid(nint sample);
    [LibraryImport(CoreMedia)] public static partial byte CMSampleBufferDataIsReady(nint sample);
    [LibraryImport(CoreMedia)] public static partial nint CMSampleBufferGetSampleAttachmentsArray(nint sample, byte createIfNecessary);
}
