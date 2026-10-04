namespace Flowspan.Linux.CaptureProbe;

internal static class ThreadLoopProbe
{
    public static ProbeResult Run(IPipeWireApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        bool initAttempted = false;
        bool initialized = false;
        bool allocationPending = false;
        bool lockMutationPending = false;
        int lockDepth = 0;
        nint loop = 0;
        string version = "unknown";
        ProbeResult result;
        try
        {
            initAttempted = true;
            api.Initialize();
            initialized = true;
            version = api.GetLibraryVersion();
            if (!ProbeOutput.IsValidVersion(version))
            {
                throw new ProbeFailureException(ProbeReason.InvalidVersion);
            }

            allocationPending = true;
            loop = api.CreateLoop();
            allocationPending = false;
            if (loop == 0)
            {
                result = new(ProbeStatus.Fail, ProbeReason.AllocationFailed, version);
            }
            else if (api.GetLoop(loop) == 0)
            {
                result = new(ProbeStatus.Fail, ProbeReason.UnderlyingLoopMissing, version);
            }
            else
            {
                int startResult = api.Start(loop);
                if (startResult != 0)
                {
                    result = new(ProbeStatus.Fail, ProbeReason.StartFailed, version, startResult);
                }
                else
                {
                    for (int index = 0; index < 2; index++)
                    {
                        lockMutationPending = true;
                        api.Lock(loop);
                        lockDepth++;
                        lockMutationPending = false;
                    }

                    byte inThread = api.InThread(loop);
                    int timeResult = api.GetTime(loop, out LinuxTimespec time);
                    result = inThread != 0
                        ? new(ProbeStatus.Fail, ProbeReason.InThreadInvalid, version)
                        : timeResult != 0
                            ? new(ProbeStatus.Fail, ProbeReason.TimeFailed, version, timeResult)
                            : time.Seconds < 0
                                || time.Nanoseconds is < 0 or >= 1_000_000_000
                                // Default-zero out storage does not prove native wrote time.
                                // This observation requirement is not a universal timespec rule.
                                || (time.Seconds == 0 && time.Nanoseconds == 0)
                                ? new(ProbeStatus.Fail, ProbeReason.TimeInvalid, version)
                                : new(ProbeStatus.Pass, ProbeReason.None, version);
                }
            }
        }
        catch (ProbeFailureException failure)
        {
            result = new(ProbeStatus.Fail, failure.Reason, version);
        }
        catch (Exception)
        {
            result = new(ProbeStatus.Fail, ProbeReason.ManagedFailure, version);
        }

        bool released = !(initAttempted && !initialized)
            && !allocationPending
            && !lockMutationPending;
        if (released)
        {
            try
            {
                if (loop != 0)
                {
                    while (lockDepth > 0)
                    {
                        api.Unlock(loop);
                        lockDepth--;
                    }

                    // Stop outside any loop lock, including after a negative Start result.
                    // A failed/unconfirmed Stop must never be followed by destruction.
                    api.Stop(loop);
                    api.Destroy(loop);
                }

                if (initialized)
                {
                    api.Deinitialize();
                }
            }
            catch (Exception)
            {
                released = false;
            }
        }

        return result with
        {
            Status = released ? result.Status : ProbeStatus.Fail,
            Reason = !released && result.Status == ProbeStatus.Pass
                ? ProbeReason.CleanupUnconfirmed
                : result.Reason,
            OwnersReleased = released,
            NativeCallsExecuted = initAttempted,
        };
    }
}

internal sealed class ProbeFailureException(ProbeReason reason) : Exception
{
    public ProbeReason Reason { get; } = reason;
}
