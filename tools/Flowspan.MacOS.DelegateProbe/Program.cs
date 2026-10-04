using System.Runtime.InteropServices;

namespace Flowspan.MacOS.DelegateProbe;

internal static class Program
{
    private const string ExplicitSkip = "delegate_probe=skip; reason=explicit_run_required; native_calls=0; capture_executed=false";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine(ExplicitSkip);
            return 0;
        }

        if (args is not ["--run-synthetic"])
        {
            Console.Error.WriteLine("delegate_probe=fail; reason=unknown_arguments; native_calls=0; capture_executed=false");
            return 2;
        }

        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)
            || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            Console.WriteLine("delegate_probe=skip; reason=requires_macos_15_2_ordinary_arm64; native_calls=0; capture_executed=false");
            return 0;
        }

        try
        {
            NativeDelegateProbe.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"delegate_probe=fail; managed_exception={exception.GetType().Name}; capture_executed=false");
            return 1;
        }
    }
}
