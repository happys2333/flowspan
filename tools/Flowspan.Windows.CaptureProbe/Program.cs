using System.Runtime.InteropServices;

namespace Flowspan.Windows.CaptureProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Flowspan Windows Capture ABI probe (not a product adapter)");
            Console.WriteLine("Usage: [--self-test | --wgc-self-test | --native-self-window | --help]");
            Console.WriteLine("Default: Windows x64 D3D11 WARP known-pixel readback; other hosts Skip.");
            Console.WriteLine("Explicit --native-self-window: capture only this probe's visible 64x64 HWND.");
            Console.WriteLine("No existing window enumeration/title reads, permission requests, or pixel files.");
            return 0;
        }

        if (args is ["--self-test"])
        {
            return ProbeSelfTests.Run();
        }

        if (args is ["--wgc-self-test"])
        {
            return WgcProbeSelfTests.Run();
        }

        if (args is ["--native-self-window"])
        {
            return RunNativeSelfWindow();
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine("probe=error reason=unsupported_arguments");
            return 2;
        }

        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Console.WriteLine("probe=skip mode=warp reason=requires_windows_x64 window_capture_executed=false permissions_requested=0 pixel_files_written=0");
            return 0;
        }

        WarpProbeResult? result = null;
        Exception? failure = null;
        Thread worker = new(() =>
        {
            try
            {
                result = WarpProbe.Run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Flowspan Windows WARP ABI probe",
        };
        worker.Start();
        if (!worker.Join(TimeSpan.FromSeconds(15)))
        {
            // Never release pointers still borrowed by a blocked native call.
            // This standalone process exits nonzero with its owners quarantined.
            Console.Error.WriteLine("probe=error mode=warp reason=native_worker_timeout");
            return 1;
        }

        if (failure is not null)
        {
            if (failure is ProbeFailureException nativeFailure)
            {
                Console.Error.WriteLine(
                    $"probe=error mode=warp reason={nativeFailure.Reason}"
                    + $" hresult=0x{unchecked((uint)nativeFailure.NativeResult):x8}");
            }
            else
            {
                Console.Error.WriteLine(
                    $"probe=error mode=warp reason=managed_failure type={failure.GetType().Name}");
            }

            return 1;
        }

        if (result is null)
        {
            Console.Error.WriteLine("probe=error mode=warp reason=missing_result");
            return 1;
        }

        Console.WriteLine(
            "probe=pass mode=warp api=D3D11CreateDevice driver=WARP apartment=MTA"
            + $" source=7x5 content=3x2 bytes=24 row_pitch={result.RowPitch}"
            + $" feature_level=0x{result.FeatureLevel:x4} map_attempts={result.MapAttempts}"
            + $" owned_refs={result.OwnedReferences} released_refs={result.ReleasedReferences}"
            + $" sha256={result.Sha256}"
            + " window_capture_executed=false permissions_requested=0 pixel_files_written=0");
        return 0;
    }

    private static int RunNativeSelfWindow()
    {
        const string safety = " existing_windows_enumerated=0 user_titles_read=0 permissions_requested=0 pixel_files_written=0 protection=unknown";
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Console.WriteLine("probe=skip mode=wgc_self_window reason=requires_windows_x64 window_capture_executed=false cleanup_confirmed=true" + safety);
            return 0;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            Console.WriteLine("probe=skip mode=wgc_self_window reason=requires_windows_build_19041 window_capture_executed=false cleanup_confirmed=true" + safety);
            return 0;
        }

        SelfWindow? window = null;
        WgcProbeState state = new();
        Exception? failure = null;
        WgcProbeResult? result = null;
        bool nativeWorkerStarted = false;
        bool workerJoined = false;
        bool startupTimedOut = false;
        try
        {
            if (!SelfWindow.HasInteractiveDesktop())
            {
                Console.WriteLine("probe=skip mode=wgc_self_window reason=interactive_composited_desktop_unavailable window_capture_executed=false cleanup_confirmed=true" + safety);
                return 0;
            }

            window = new SelfWindow();
            window.Start();
            Thread worker = new(() =>
            {
                try { result = WgcSelfWindowProbe.Run(window, state); }
                catch (Exception exception) { failure = exception; }
            })
            { IsBackground = true, Name = "Flowspan WGC native MTA owner" };
            worker.Start();
            nativeWorkerStarted = true;
            workerJoined = worker.Join(TimeSpan.FromSeconds(20));
            if (!workerJoined) { failure = new ProbeFailureException("native_worker_timeout"); }
        }
        catch (Exception exception)
        {
            failure = exception;
            startupTimedOut = exception is ProbeFailureException { Reason: "window_start_timeout" };
        }

        bool nativeCleanupConfirmed = (!nativeWorkerStarted
                && failure is not ProbeFailureException { Reason: "native_cleanup_unconfirmed" })
            || (workerJoined && state.NativeCleanupConfirmed);
        if (window is not null && nativeCleanupConfirmed && !startupTimedOut)
        {
            try { window.Finish(); }
            catch (Exception exception) { failure = exception; }
        }

        bool cleanupConfirmed = nativeCleanupConfirmed && !startupTimedOut
            && (window is null || window.CleanupConfirmed);
        if (failure is not null || !cleanupConfirmed || result is null)
        {
            string reason = failure is ProbeFailureException known ? known.Reason
                : failure is not null ? "managed_failure" : "missing_result";
            uint hresult = failure is ProbeFailureException native ? unchecked((uint)native.NativeResult) : 0;
            Console.Error.WriteLine($"probe=error mode=wgc_self_window reason={reason} hresult=0x{hresult:x8}"
                + $" capture_session_started={state.CaptureStarted.ToString().ToLowerInvariant()} frames_received={state.FramesReceived}"
                + $" window_capture_executed={(state.FramesReceived > 0).ToString().ToLowerInvariant()}"
                + $" cleanup_confirmed={cleanupConfirmed.ToString().ToLowerInvariant()}"
                + $" process_lifetime_quarantine={(!cleanupConfirmed).ToString().ToLowerInvariant()}" + safety);
            return 1;
        }

        string status = result.Passed ? "pass" : "skip";
        Console.WriteLine($"probe={status} mode=wgc_self_window reason={result.Reason} api=WindowsGraphicsCapture driver=WARP apartment=MTA"
            + $" content={result.Width}x{result.Height} frames={result.Frames} row_pitch={result.RowPitch}"
            + $" feature_level=0x{result.FeatureLevel:x4} owned_refs={result.OwnedReferences} released_refs={result.ReleasedReferences}"
            + $" secondary_windows_policy={result.SecondaryWindowsPolicy} hresult=0x{unchecked((uint)result.ApiResult):x8}"
            + $" marker_sha256={result.MarkerHash ?? "none"} frame_sha256={result.FrameHash ?? "none"}"
            + $" capture_session_started={result.CaptureStarted.ToString().ToLowerInvariant()}"
            + $" window_capture_executed={(result.Frames > 0).ToString().ToLowerInvariant()} cleanup_confirmed=true" + safety);
        return 0;
    }
}
