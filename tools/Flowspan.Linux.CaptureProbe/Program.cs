using System.Runtime.InteropServices;

namespace Flowspan.Linux.CaptureProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Flowspan Linux thread-loop ABI smoke probe (not capture or a product adapter)");
            Console.WriteLine("Usage: [--self-test | --help]");
            Console.WriteLine("Default: Linux ordinary x64/arm64 PipeWire thread-loop create/start/lock/time/stop/destroy only.");
            Console.WriteLine("No context/core, daemon, portal/D-Bus, stream, hardware, window, permission, or pixels.");
            Console.WriteLine("Missing library/symbol or other hosts: explicit Skip, not native proof. Native worker join budget: 15 seconds.");
            return 0;
        }

        if (args is ["--self-test"])
        {
            return ProbeSelfTests.Run();
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine("probe=fail mode=thread_loop_abi reason=unsupported_arguments");
            return 2;
        }

        if (!OperatingSystem.IsLinux()
            || IntPtr.Size != 8
            || RuntimeInformation.ProcessArchitecture is not Architecture.X64 and not Architecture.Arm64)
        {
            Console.WriteLine(ProbeOutput.Format(new(ProbeStatus.Skip, ProbeReason.UnsupportedHost)));
            return 0;
        }

        ProbeResult result;
        try
        {
            result = NativeWorker.Run(RunNative, NativeWorker.MaximumBudget);
        }
        catch (Exception)
        {
            result = new(ProbeStatus.Fail, ProbeReason.ManagedFailure, OwnersReleased: false);
        }

        if (result.Status == ProbeStatus.Fail)
        {
            Console.Error.WriteLine(ProbeOutput.Format(result));
            return 1;
        }

        Console.WriteLine(ProbeOutput.Format(result));
        return 0;
    }

    private static ProbeResult RunNative()
    {
        NativePipeWireApi? api = NativePipeWireApi.TryCreate(out ProbeReason reason);
        if (api is null)
        {
            return new(ProbeStatus.Skip, reason);
        }

        ProbeResult result = ThreadLoopProbe.Run(api);
        if (result.OwnersReleased)
        {
            api.Dispose();
        }

        // Unconfirmed native owners retain their library handle until process exit.
        return result;
    }
}
