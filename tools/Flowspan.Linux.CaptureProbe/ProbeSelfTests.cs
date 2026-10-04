using System.Runtime.InteropServices;

namespace Flowspan.Linux.CaptureProbe;

internal static class ProbeSelfTests
{
    public static int Run()
    {
        LinuxTimespec largeTime = new(4_294_967_296, 999_999_999);
        if (Marshal.SizeOf<LinuxTimespec>() != 16
            || Marshal.OffsetOf<LinuxTimespec>(nameof(LinuxTimespec.Seconds)) != 0
            || Marshal.OffsetOf<LinuxTimespec>(nameof(LinuxTimespec.Nanoseconds)) != 8
            || largeTime.Seconds != 4_294_967_296
            || largeTime.Nanoseconds != 999_999_999)
        {
            Console.Error.WriteLine("self_test=fail case=timespec_layout");
            return 1;
        }

        FakePipeWireApi api = new() { StartResult = -5 };
        ProbeResult result = ThreadLoopProbe.Run(api);
        if (result.Status != ProbeStatus.Fail
            || result.Reason != ProbeReason.StartFailed
            || result.NativeResult != -5
            || api.Initializations != 1
            || api.Stops != 1
            || api.Destroys != 1
            || api.Deinitializations != 1)
        {
            Console.Error.WriteLine("self_test=fail case=start_failure_releases_native_owners");
            return 1;
        }

        FakePipeWireApi successApi = new();
        ProbeResult success = ThreadLoopProbe.Run(successApi);
        if (success.Status != ProbeStatus.Pass
            || !success.OwnersReleased
            || successApi.Locks != 2
            || successApi.Unlocks != 2
            || successApi.TimeCalls != 1
            || successApi.InThreadCalls != 1
            || successApi.Stops != 1
            || successApi.Destroys != 1
            || successApi.Deinitializations != 1)
        {
            Console.Error.WriteLine("self_test=fail case=successful_roundtrip_observes_and_releases");
            return 1;
        }

        if (ProbeOutput.IsValidVersion("1.6.9; forged=pass\n")
            || ProbeOutput.IsValidVersion(new string('1', 65))
            || ProbeOutput.IsValidVersion(string.Empty)
            || !ProbeOutput.IsValidVersion("0.3.7")
            || !ProbeOutput.IsValidVersion("1.6.9-dev.1"))
        {
            Console.Error.WriteLine("self_test=fail case=version_output_is_bounded_and_injection_safe");
            return 1;
        }

        FakePipeWireApi versionApi = new() { LibraryVersion = "1.6.9; forged=pass\n" };
        ProbeResult invalidVersion = ThreadLoopProbe.Run(versionApi);
        if (invalidVersion.Status != ProbeStatus.Fail
            || invalidVersion.Reason != ProbeReason.InvalidVersion
            || versionApi.LoopAllocations != 0
            || versionApi.Deinitializations != 1
            || ProbeOutput.Format(invalidVersion).Contains("forged", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("self_test=fail case=invalid_version_fails_before_loop_allocation");
            return 1;
        }

        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim drained = new();
        FakePipeWireApi blockedApi = new()
        {
            StartEntered = entered,
            StartRelease = release,
            Deinitialized = drained,
        };
        try
        {
            ProbeResult timeout = NativeWorker.Run(
                () => ThreadLoopProbe.Run(blockedApi),
                TimeSpan.FromMilliseconds(10));
            bool started = entered.Wait(TimeSpan.FromSeconds(1));
            bool retained = timeout.Status == ProbeStatus.Fail
                && timeout.Reason == ProbeReason.WorkerTimeout
                && !timeout.OwnersReleased
                && started
                && blockedApi.Stops == 0
                && blockedApi.Destroys == 0
                && blockedApi.Deinitializations == 0;
            release.Set();
            bool eventuallyDrained = drained.Wait(TimeSpan.FromSeconds(1));
            bool joined = blockedApi.WorkerThread?.Join(TimeSpan.FromSeconds(1)) == true;
            if (!retained
                || !eventuallyDrained
                || !joined
                || blockedApi.Stops != 1
                || blockedApi.Destroys != 1
                || blockedApi.Deinitializations != 1
                || timeout.Reason != ProbeReason.WorkerTimeout
                || timeout.OwnersReleased)
            {
                Console.Error.WriteLine("self_test=fail case=worker_timeout_retains_borrowed_owner");
                return 1;
            }
        }
        finally
        {
            release.Set();
            blockedApi.WorkerThread?.Join(TimeSpan.FromSeconds(1));
        }

        if (!RunAdditionalFaultCases())
        {
            return 1;
        }

        string longestVersion = "999999999.999999999.999999999-" + new string('x', 32);
        string longestOutput = ProbeOutput.Format(new(
            ProbeStatus.Pass,
            ProbeReason.None,
            longestVersion,
            int.MinValue,
            NativeCallsExecuted: true));
        if (!ProbeOutput.IsValidVersion(longestVersion)
            || longestOutput.Length > 640
            || longestOutput.Contains('\n', StringComparison.Ordinal)
            || longestOutput.Contains('\r', StringComparison.Ordinal))
        {
            Console.Error.WriteLine("self_test=fail case=maximum_output_is_single_bounded_line");
            return 1;
        }

        Console.WriteLine("self_test=pass cases=17 native_calls=0");
        return 0;
    }

    private static bool RunAdditionalFaultCases()
    {
        (string Name, FakePipeWireApi Api, ProbeReason Reason, bool Released)[] cases =
        [
            ("allocation_failure", new() { LoopValue = 0 }, ProbeReason.AllocationFailed, true),
            ("underlying_loop_missing", new() { UnderlyingLoopValue = 0 }, ProbeReason.UnderlyingLoopMissing, true),
            ("time_failure", new() { TimeResult = -22 }, ProbeReason.TimeFailed, true),
            ("default_zero_time_is_not_observable", new() { Time = default }, ProbeReason.TimeInvalid, true),
            ("invalid_nanoseconds", new() { Time = new(1, 1_000_000_000) }, ProbeReason.TimeInvalid, true),
            ("outside_thread_fact", new() { InThreadValue = 2 }, ProbeReason.InThreadInvalid, true),
            ("stop_failure_retains_owner", new() { StopThrows = true }, ProbeReason.CleanupUnconfirmed, false),
            ("lock_failure_retains_owner", new() { LockThrows = true }, ProbeReason.ManagedFailure, false),
            ("unlock_failure_retains_owner", new() { UnlockThrows = true }, ProbeReason.CleanupUnconfirmed, false),
            ("initialization_failure_retains_owner", new() { InitializeThrows = true }, ProbeReason.ManagedFailure, false),
        ];
        foreach ((string name, FakePipeWireApi api, ProbeReason reason, bool released) in cases)
        {
            ProbeResult result = ThreadLoopProbe.Run(api);
            string output = ProbeOutput.Format(result);
            if (result.Status != ProbeStatus.Fail
                || result.Reason != reason
                || result.OwnersReleased != released
                || output.Length > 640
                || output.Contains('\n', StringComparison.Ordinal)
                || output.Contains('\r', StringComparison.Ordinal)
                || (released && api.Deinitializations != 1)
                || (!released && (api.Destroys != 0 || api.Deinitializations != 0)))
            {
                Console.Error.WriteLine($"self_test=fail case={name}");
                return false;
            }
        }

        return true;
    }

    private sealed class FakePipeWireApi : IPipeWireApi
    {
        public int StartResult { get; init; }
        public string LibraryVersion { get; init; } = "0.3.7";
        public nint LoopValue { get; init; } = 1;
        public nint UnderlyingLoopValue { get; init; } = 2;
        public byte InThreadValue { get; init; }
        public int TimeResult { get; init; }
        public LinuxTimespec Time { get; init; } = new(1, 2);
        public bool InitializeThrows { get; init; }
        public bool LockThrows { get; init; }
        public bool UnlockThrows { get; init; }
        public bool StopThrows { get; init; }
        public int LoopAllocations { get; private set; }
        public int Initializations { get; private set; }
        public int Stops { get; private set; }
        public int Destroys { get; private set; }
        public int Deinitializations { get; private set; }
        public int Locks { get; private set; }
        public int Unlocks { get; private set; }
        public int TimeCalls { get; private set; }
        public int InThreadCalls { get; private set; }
        public ManualResetEventSlim? StartEntered { get; init; }
        public ManualResetEventSlim? StartRelease { get; init; }
        public ManualResetEventSlim? Deinitialized { get; init; }
        public Thread? WorkerThread { get; private set; }

        public void Initialize()
        {
            Initializations++;
            if (InitializeThrows)
            {
                throw new InvalidOperationException();
            }
        }
        public string GetLibraryVersion() => LibraryVersion;
        public nint CreateLoop()
        {
            LoopAllocations++;
            return LoopValue;
        }
        public nint GetLoop(nint loop) => UnderlyingLoopValue;
        public int Start(nint loop)
        {
            WorkerThread = Thread.CurrentThread;
            StartEntered?.Set();
            StartRelease?.Wait();
            return StartResult;
        }
        public void Lock(nint loop)
        {
            if (LockThrows)
            {
                throw new InvalidOperationException();
            }

            Locks++;
        }
        public void Unlock(nint loop)
        {
            if (UnlockThrows)
            {
                throw new InvalidOperationException();
            }

            Unlocks++;
        }
        public byte InThread(nint loop)
        {
            InThreadCalls++;
            return InThreadValue;
        }
        public int GetTime(nint loop, out LinuxTimespec time)
        {
            time = Time;
            TimeCalls++;
            return TimeResult;
        }
        public void Stop(nint loop)
        {
            Stops++;
            if (StopThrows)
            {
                throw new InvalidOperationException();
            }
        }
        public void Destroy(nint loop) => Destroys++;
        public void Deinitialize()
        {
            Deinitializations++;
            Deinitialized?.Set();
        }
    }
}
