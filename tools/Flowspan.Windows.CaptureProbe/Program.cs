using System.Runtime.InteropServices;

namespace Flowspan.Windows.CaptureProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Flowspan Windows Capture ABI probe (not a product adapter)");
            Console.WriteLine("Usage: [--self-test | --help]");
            Console.WriteLine("Default: Windows x64 D3D11 WARP known-pixel readback; other hosts Skip.");
            Console.WriteLine("No window enumeration, capture, permission requests, or pixel files.");
            return 0;
        }

        if (args is ["--self-test"])
        {
            return ProbeSelfTests.Run();
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
}
