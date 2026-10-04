namespace Flowspan.Linux.CaptureProbe;

internal enum ProbeStatus
{
    Pass,
    Skip,
    Fail,
}

internal enum ProbeReason
{
    None,
    UnsupportedHost,
    LibraryMissing,
    SymbolMissing,
    InvalidVersion,
    AllocationFailed,
    UnderlyingLoopMissing,
    StartFailed,
    InThreadInvalid,
    TimeFailed,
    TimeInvalid,
    ManagedFailure,
    CleanupUnconfirmed,
    WorkerTimeout,
}

internal sealed record ProbeResult(
    ProbeStatus Status,
    ProbeReason Reason,
    string LibraryVersion = "unknown",
    int NativeResult = 0,
    bool OwnersReleased = true,
    bool NativeCallsExecuted = false);
