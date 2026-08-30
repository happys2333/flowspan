using Flowspan.Platform;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal enum RemoteWindowHostPreparationFact
{
    Source,
    Permission,
    Authorization,
    Connection,
    EmergencyStop,
    Protection,
}

internal sealed class RemoteWindowHostPreparationFactEpoch
{
    internal RemoteWindowHostPreparationFactEpoch()
    {
    }

    public override string ToString() => "host-preparation-fact-epoch";
}

internal sealed class RemoteWindowHostPreparationEpochBundle
{
    private readonly RemoteWindowHostPreparationFactEpoch[] epochs;
    private int claimed;

    private RemoteWindowHostPreparationEpochBundle(
        RemoteWindowHostPreparationFactEpoch[] epochs) => this.epochs = epochs;

    public static RemoteWindowHostPreparationEpochBundle Create() => new(
        Enum.GetValues<RemoteWindowHostPreparationFact>()
            .Select(static _ => new RemoteWindowHostPreparationFactEpoch())
            .ToArray());

    public RemoteWindowHostPreparationFactEpoch Get(
        RemoteWindowHostPreparationFact fact)
    {
        if (!Enum.IsDefined(fact))
        {
            throw new ArgumentOutOfRangeException(nameof(fact));
        }

        return epochs[(int)fact];
    }

    internal bool Matches(
        RemoteWindowHostPreparationFact fact,
        RemoteWindowHostPreparationFactEpoch epoch) =>
        ReferenceEquals(Get(fact), epoch);

    internal bool TryClaim() => Interlocked.CompareExchange(
        ref claimed,
        1,
        0) == 0;

    public override string ToString() => "host-preparation-epoch-bundle";
}

internal enum RemoteWindowHostPreparationCleanupScope
{
    PreRoute,
    ConsumeConnection,
}

internal sealed record RemoteWindowHostPreparationTermination(
    string ReasonCode,
    RemoteWindowHostPreparationCleanupScope CleanupScope,
    RemoteWindowHostPreparationFact? Fact = null);

internal enum RemoteWindowHostPreparationPhase
{
    Collecting,
    Armed,
    RouteAdmitted,
    RouteSelected,
    PrepareSending,
    ReadyMatched,
    Promoted,
    Terminal,
}

internal sealed record RemoteWindowHostPreparationSnapshot(
    long HostGeneration,
    RemoteWindowHostPreparationPhase Phase,
    bool RouteMayBeOwned,
    bool PrepareSendAdmitted,
    RemoteWindowHostPreparationTermination? Termination);

internal sealed class RemoteWindowHostPreparationReservation :
    IDisposable,
    IDesktopRemoteWindowHostAuthorizationInvalidationSink,
    ILocalEmergencyStopReadinessInvalidationSink,
    INativeRemoteWindowSourcePreparationReservation,
    IRemoteWindowHostPreparationAdmission
{
    private readonly RemoteWindowHostPreparationEpochBundle epochs;
    private readonly object gate = new();
    private readonly long hostGeneration;
    private readonly RemoteWindowPreparationRequest request;
    private readonly TaskCompletionSource<RemoteWindowHostPreparationTermination>
        terminalCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    private bool prepareSendAdmitted;
    private RemoteWindowHostPreparationPhase phase =
        RemoteWindowHostPreparationPhase.Collecting;
    private DateTimeOffset? protectionNotBefore;
    private DateTimeOffset? protectionValidThrough;
    private bool routeMayBeOwned;
    private RemoteWindowHostPreparationTermination? termination;

    public RemoteWindowHostPreparationReservation(
        long hostGeneration,
        RemoteWindowPreparationRequest request,
        RemoteWindowHostPreparationEpochBundle epochs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hostGeneration, 1);
        this.hostGeneration = hostGeneration;
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
        if (!this.epochs.TryClaim())
        {
            throw new ArgumentException(
                "A host Preparation epoch bundle can bind only one reservation generation.",
                nameof(epochs));
        }
    }

    public RemoteWindowHostPreparationSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return new RemoteWindowHostPreparationSnapshot(
                    hostGeneration,
                    phase,
                    routeMayBeOwned,
                    prepareSendAdmitted,
                    termination);
            }
        }
    }

    public Task<RemoteWindowHostPreparationTermination> Terminal =>
        terminalCompletion.Task;

    public bool TryBindProtectionObservation(DateTimeOffset observedAt)
    {
        DateTimeOffset notBefore;
        DateTimeOffset validThrough;
        try
        {
            notBefore = observedAt.Subtract(
                RemoteInputPolicy.MaximumFutureClockSkew);
            validThrough = observedAt.Add(
                RemoteInputPolicy.MaximumProtectionAge);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return TryBindProtectionInterval(notBefore, validThrough);
    }

    private bool TryBindProtectionInterval(
        DateTimeOffset protectionNotBefore,
        DateTimeOffset protectionValidThrough)
    {
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.Collecting)
            {
                return false;
            }

            if (this.protectionNotBefore is { } boundNotBefore
                && this.protectionValidThrough is { } boundValidThrough)
            {
                return boundNotBefore == protectionNotBefore
                    && boundValidThrough == protectionValidThrough;
            }

            this.protectionNotBefore = protectionNotBefore;
            this.protectionValidThrough = protectionValidThrough;
            return true;
        }
    }

    public bool TryArm(DateTimeOffset now)
    {
        RemoteWindowHostPreparationTermination? expired = null;
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.Collecting)
            {
                return false;
            }

            expired = GetTimeTermination(now);
            if (expired is null)
            {
                phase = RemoteWindowHostPreparationPhase.Armed;
                return true;
            }
            termination = expired;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(expired);
        return false;
    }

    public bool TryAdmitRouteSelection(DateTimeOffset now)
    {
        RemoteWindowHostPreparationTermination? expired = null;
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.Armed)
            {
                return false;
            }

            expired = GetTimeTermination(now);
            if (expired is null)
            {
                routeMayBeOwned = true;
                phase = RemoteWindowHostPreparationPhase.RouteAdmitted;
                return true;
            }
            termination = expired;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(expired);
        return false;
    }

    public bool CompleteRouteSelection()
    {
        lock (gate)
        {
            if (!routeMayBeOwned)
            {
                throw new InvalidOperationException(
                    "A host Preparation route cannot complete before admission.");
            }

            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.RouteAdmitted)
            {
                return false;
            }

            phase = RemoteWindowHostPreparationPhase.RouteSelected;
            return true;
        }
    }

    public bool TryFailRouteSelection()
    {
        RemoteWindowHostPreparationTermination? completed = null;
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.RouteAdmitted)
            {
                return false;
            }

            completed = new RemoteWindowHostPreparationTermination(
                "responder_route_failed",
                RemoteWindowHostPreparationCleanupScope.ConsumeConnection);
            termination = completed;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(completed);
        return true;
    }

    public bool TryAdmitPrepareSend(
        RemoteWindowPreparationRequest candidate,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        RemoteWindowHostPreparationTermination? expired = null;
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.RouteSelected
                || prepareSendAdmitted
                || candidate != request)
            {
                return false;
            }

            expired = GetTimeTermination(now);
            if (expired is null)
            {
                prepareSendAdmitted = true;
                phase = RemoteWindowHostPreparationPhase.PrepareSending;
                return true;
            }
            termination = expired;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(expired);
        return false;
    }

    public bool TryMatchReady(
        RemoteWindowPreparationResponse response,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        RemoteWindowHostPreparationTermination? failed = null;
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.PrepareSending)
            {
                return false;
            }

            if (response.Request != request)
            {
                failed = new RemoteWindowHostPreparationTermination(
                    "remote_window_ready_mismatch",
                    GetCleanupScope());
            }
            else
            {
                failed = GetTimeTermination(now);
            }

            if (failed is null
                && response.Outcome == RemoteWindowPreparationOutcome.Ready)
            {
                phase = RemoteWindowHostPreparationPhase.ReadyMatched;
                return true;
            }

            failed ??= new RemoteWindowHostPreparationTermination(
                response.ReasonCode,
                GetCleanupScope());
            termination = failed;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(failed);
        return false;
    }

    public bool TryPromote(DateTimeOffset now)
    {
        RemoteWindowHostPreparationTermination? expired = null;
        lock (gate)
        {
            if (termination is not null
                || phase != RemoteWindowHostPreparationPhase.ReadyMatched)
            {
                return false;
            }

            expired = GetTimeTermination(now);
            if (expired is null)
            {
                phase = RemoteWindowHostPreparationPhase.Promoted;
                return true;
            }
            termination = expired;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(expired);
        return false;
    }

    public bool TryInvalidate(
        long expectedHostGeneration,
        RemoteWindowHostPreparationFact fact,
        RemoteWindowHostPreparationFactEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        RemoteWindowHostPreparationTermination? completed = null;
        lock (gate)
        {
            if (expectedHostGeneration != hostGeneration
                || termination is not null
                || phase == RemoteWindowHostPreparationPhase.Promoted)
            {
                return false;
            }

            if (!epochs.Matches(fact, epoch))
            {
                return false;
            }

            completed = new RemoteWindowHostPreparationTermination(
                GetInvalidationReason(fact),
                GetCleanupScope(),
                fact);
            termination = completed;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(completed);
        return true;
    }

    internal bool TryInvalidate(RemoteWindowHostPreparationFact fact) =>
        TryInvalidate(hostGeneration, fact, epochs.Get(fact));

    void INativeRemoteWindowSourcePreparationReservation
        .InvalidateSourcePreparationNow() =>
        _ = TryInvalidate(RemoteWindowHostPreparationFact.Source);

    void ILocalEmergencyStopReadinessInvalidationSink
        .InvalidateEmergencyStopReadinessNow() =>
        _ = TryInvalidate(RemoteWindowHostPreparationFact.EmergencyStop);

    void IDesktopRemoteWindowHostAuthorizationInvalidationSink
        .InvalidateAuthorizationPreparationNow() =>
        _ = TryInvalidate(RemoteWindowHostPreparationFact.Authorization);

    private static string GetInvalidationReason(
        RemoteWindowHostPreparationFact fact) => fact switch
        {
            RemoteWindowHostPreparationFact.Source => "native_source_stale",
            RemoteWindowHostPreparationFact.Permission =>
                "native_permission_denied",
            RemoteWindowHostPreparationFact.Authorization =>
                "mirror_capability_denied",
            RemoteWindowHostPreparationFact.Connection =>
                "authenticated_connection_stale",
            RemoteWindowHostPreparationFact.EmergencyStop =>
                "emergency_stop_readiness_unavailable",
            RemoteWindowHostPreparationFact.Protection =>
                "native_protection_not_safe",
            _ => throw new ArgumentOutOfRangeException(nameof(fact)),
        };

    private RemoteWindowHostPreparationTermination? GetTimeTermination(
        DateTimeOffset now)
    {
        if (now >= request.Deadline)
        {
            return new RemoteWindowHostPreparationTermination(
                "preparation_expired",
                GetCleanupScope());
        }

        return protectionNotBefore is null
            || protectionValidThrough is null
            || now < protectionNotBefore.Value
            || now > protectionValidThrough.Value
            ? new RemoteWindowHostPreparationTermination(
                "native_protection_not_safe",
                GetCleanupScope(),
                RemoteWindowHostPreparationFact.Protection)
            : null;
    }

    private RemoteWindowHostPreparationCleanupScope GetCleanupScope() =>
        routeMayBeOwned
            ? RemoteWindowHostPreparationCleanupScope.ConsumeConnection
            : RemoteWindowHostPreparationCleanupScope.PreRoute;

    public void Dispose()
    {
        RemoteWindowHostPreparationTermination? completed = null;
        lock (gate)
        {
            if (termination is not null
                || phase == RemoteWindowHostPreparationPhase.Promoted)
            {
                return;
            }

            completed = new RemoteWindowHostPreparationTermination(
                "host_preparation_disposed",
                GetCleanupScope());
            termination = completed;
            phase = RemoteWindowHostPreparationPhase.Terminal;
        }

        terminalCompletion.TrySetResult(completed);
    }
}
