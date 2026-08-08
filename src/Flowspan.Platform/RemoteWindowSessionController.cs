using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Platform;

public interface IMirrorAuthorizationSource
{
    public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId);
}

public interface IRemoteWindowCaptureBoundary
{
    public ValueTask<LocalBoundaryResult> StartAsync(
        ActivityId activityId,
        CancellationToken cancellationToken);

    public LocalBoundaryResult PauseNow(MirrorPauseReason reason);

    public LocalBoundaryResult ResumeNow();

    public LocalBoundaryResult EmergencyStopNow();

    public LocalBoundaryResult StopNow();
}

public interface IRemoteInputBoundary
{
    public ValueTask<LocalBoundaryResult> InjectAsync(
        RemoteInputBatch batch,
        CancellationToken cancellationToken);

    public LocalBoundaryResult PauseNow(MirrorPauseReason reason);

    public LocalBoundaryResult ResumeNow();

    public LocalBoundaryResult EmergencyStopNow();

    public LocalBoundaryResult StopNow();
}

public interface ILocalSharingSessionBoundary
{
    public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId);

    public LocalBoundaryResult DisconnectAllNow();
}

public enum LocalBoundaryStatus
{
    Confirmed,
    AlreadyApplied,
    Failed,
}

public sealed record LocalBoundaryResult
{
    private LocalBoundaryResult(LocalBoundaryStatus status, string reasonCode)
    {
        Status = status;
        ReasonCode = ValidateReasonCode(reasonCode);
    }

    public LocalBoundaryStatus Status { get; }

    public string ReasonCode { get; }

    public bool Succeeded => Status is LocalBoundaryStatus.Confirmed
        or LocalBoundaryStatus.AlreadyApplied;

    public static LocalBoundaryResult Confirmed(string reasonCode) =>
        new(LocalBoundaryStatus.Confirmed, reasonCode);

    public static LocalBoundaryResult AlreadyApplied(string reasonCode) =>
        new(LocalBoundaryStatus.AlreadyApplied, reasonCode);

    public static LocalBoundaryResult Failed(string reasonCode) =>
        new(LocalBoundaryStatus.Failed, reasonCode);

    private static string ValidateReasonCode(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        string normalized = reasonCode.Trim();
        if (normalized.Length > 80
            || !char.IsAsciiLetterLower(normalized[0])
            || normalized.Any(static character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not '.' and not '_'))
        {
            throw new ArgumentException(
                "A local boundary reason code must contain lowercase ASCII letters, digits, dots, or underscores.",
                nameof(reasonCode));
        }

        return normalized;
    }
}

public enum MirrorPauseReason
{
    ProtectionStateStale,
    ProtectionStateUnknown,
    SensitiveSurface,
}

public enum RemoteWindowLifecycle
{
    Idle,
    Starting,
    Active,
    ProtectionPaused,
    EmergencyStopped,
    Ended,
    Unavailable,
}

public enum RemoteWindowCaptureState
{
    Stopped,
    Starting,
    Capturing,
    Paused,
    Unconfirmed,
}

public enum RemoteWindowCommandStatus
{
    Applied,
    AlreadyApplied,
    CapabilityDenied,
    InvalidState,
    ParticipantLimitReached,
    ProtectionBlocked,
    BoundaryFailed,
    EmergencyStopped,
}

public sealed record RemoteWindowSharingSnapshot
{
    internal RemoteWindowSharingSnapshot(
        ActivityId activityId,
        ActivityKind activityKind,
        string activityTitle,
        DeviceId hostDeviceId,
        RemoteWindowLifecycle lifecycle,
        RemoteWindowCaptureState captureState,
        ImmutableDictionary<DeviceId, MirrorParticipantRole> participants,
        DeviceId? currentDriverDeviceId,
        long? driverLeaseEpoch,
        DateTimeOffset? driverLeaseExpiresAt,
        ProtectionKind protectionKind,
        long revision)
    {
        ActivityId = activityId;
        ActivityKind = activityKind;
        ActivityTitle = activityTitle;
        HostDeviceId = hostDeviceId;
        Lifecycle = lifecycle;
        CaptureState = captureState;
        Participants = participants;
        CurrentDriverDeviceId = currentDriverDeviceId;
        DriverLeaseEpoch = driverLeaseEpoch;
        DriverLeaseExpiresAt = driverLeaseExpiresAt;
        ProtectionKind = protectionKind;
        Revision = revision;
    }

    public ActivityId ActivityId { get; }

    public ActivityKind ActivityKind { get; }

    public string ActivityTitle { get; }

    public DeviceId HostDeviceId { get; }

    public RemoteWindowLifecycle Lifecycle { get; }

    public RemoteWindowCaptureState CaptureState { get; }

    public IReadOnlyDictionary<DeviceId, MirrorParticipantRole> Participants { get; }

    public DeviceId? CurrentDriverDeviceId { get; }

    public long? DriverLeaseEpoch { get; }

    public DateTimeOffset? DriverLeaseExpiresAt { get; }

    public ProtectionKind ProtectionKind { get; }

    public long Revision { get; }

    public bool IsSharing => Lifecycle is RemoteWindowLifecycle.Active
        or RemoteWindowLifecycle.ProtectionPaused;

    public override string ToString() =>
        $"Remote Window {ActivityId} ({ActivityKind}, {Lifecycle}, {CaptureState}, driver {CurrentDriverDeviceId}, epoch {DriverLeaseEpoch}, revision {Revision})";
}

public sealed record RemoteWindowCommandResult
{
    internal RemoteWindowCommandResult(
        RemoteWindowCommandStatus status,
        string reasonCode,
        RemoteWindowSharingSnapshot snapshot,
        LocalBoundaryResult? boundary = null)
    {
        Status = status;
        ReasonCode = reasonCode;
        Snapshot = snapshot;
        Boundary = boundary;
    }

    public RemoteWindowCommandStatus Status { get; }

    public string ReasonCode { get; }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult? Boundary { get; }

    public bool Succeeded => Status is RemoteWindowCommandStatus.Applied
        or RemoteWindowCommandStatus.AlreadyApplied;
}

public enum RemoteInputEventKind
{
    HidKeyDown,
    HidKeyUp,
    PointerMove,
    PointerButtonDown,
    PointerButtonUp,
    Scroll,
}

public enum RemotePointerButton
{
    Primary,
    Secondary,
    Middle,
    Back,
    Forward,
}

public sealed record RemoteInputEvent
{
    public const int MaximumScrollDelta = 12_000;

    private RemoteInputEvent(
        RemoteInputEventKind kind,
        double normalizedX,
        double normalizedY,
        ushort hidUsagePage,
        ushort hidUsageId,
        RemotePointerButton? pointerButton,
        int horizontalScroll,
        int verticalScroll)
    {
        Kind = kind;
        NormalizedX = normalizedX;
        NormalizedY = normalizedY;
        HidUsagePage = hidUsagePage;
        HidUsageId = hidUsageId;
        PointerButton = pointerButton;
        HorizontalScroll = horizontalScroll;
        VerticalScroll = verticalScroll;
    }

    public RemoteInputEventKind Kind { get; }

    public double NormalizedX { get; }

    public double NormalizedY { get; }

    public ushort HidUsagePage { get; }

    public ushort HidUsageId { get; }

    public RemotePointerButton? PointerButton { get; }

    public int HorizontalScroll { get; }

    public int VerticalScroll { get; }

    public static RemoteInputEvent HidKeyDown(
        ushort usagePage,
        ushort usageId) =>
        HidKey(RemoteInputEventKind.HidKeyDown, usagePage, usageId);

    public static RemoteInputEvent HidKeyUp(
        ushort usagePage,
        ushort usageId) =>
        HidKey(RemoteInputEventKind.HidKeyUp, usagePage, usageId);

    public static RemoteInputEvent PointerMove(
        double normalizedX,
        double normalizedY)
    {
        if (!double.IsFinite(normalizedX)
            || !double.IsFinite(normalizedY)
            || normalizedX is < 0 or > 1
            || normalizedY is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedX),
                "Pointer coordinates must be finite normalized values from zero through one.");
        }

        return new RemoteInputEvent(
            RemoteInputEventKind.PointerMove,
            normalizedX,
            normalizedY,
            0,
            0,
            null,
            0,
            0);
    }

    public static RemoteInputEvent PointerButtonDown(
        RemotePointerButton button) =>
        PointerButtonEvent(RemoteInputEventKind.PointerButtonDown, button);

    public static RemoteInputEvent PointerButtonUp(
        RemotePointerButton button) =>
        PointerButtonEvent(RemoteInputEventKind.PointerButtonUp, button);

    public static RemoteInputEvent Scroll(
        int horizontalDelta,
        int verticalDelta)
    {
        if (horizontalDelta == 0 && verticalDelta == 0)
        {
            throw new ArgumentException(
                "A remote scroll event requires a non-zero axis.",
                nameof(horizontalDelta));
        }

        if (Math.Abs((long)horizontalDelta) > MaximumScrollDelta
            || Math.Abs((long)verticalDelta) > MaximumScrollDelta)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalDelta),
                $"A remote scroll delta cannot exceed {MaximumScrollDelta} per axis.");
        }

        return new RemoteInputEvent(
            RemoteInputEventKind.Scroll,
            0,
            0,
            0,
            0,
            null,
            horizontalDelta,
            verticalDelta);
    }

    public override string ToString() => $"Remote input event ({Kind})";

    private static RemoteInputEvent HidKey(
        RemoteInputEventKind kind,
        ushort usagePage,
        ushort usageId)
    {
        if (usagePage == 0 || usageId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usagePage),
                "A remote HID key requires non-zero usage page and usage ID values.");
        }

        return new RemoteInputEvent(
            kind,
            0,
            0,
            usagePage,
            usageId,
            null,
            0,
            0);
    }

    private static RemoteInputEvent PointerButtonEvent(
        RemoteInputEventKind kind,
        RemotePointerButton button)
    {
        if (!Enum.IsDefined(button))
        {
            throw new ArgumentOutOfRangeException(nameof(button));
        }

        return new RemoteInputEvent(
            kind,
            0,
            0,
            0,
            0,
            button,
            0,
            0);
    }
}

public sealed class RemoteInputBatch
{
    public const int MaximumEvents = 64;

    private readonly ImmutableArray<RemoteInputEvent> events;

    private RemoteInputBatch(ImmutableArray<RemoteInputEvent> events) =>
        this.events = events;

    public IReadOnlyList<RemoteInputEvent> Events => events;

    public static RemoteInputBatch Create(IEnumerable<RemoteInputEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        RemoteInputEvent[] materialized = events.ToArray();
        if (materialized.Length is < 1 or > MaximumEvents
            || materialized.Any(static input => input is null))
        {
            throw new ArgumentException(
                $"A remote input batch requires 1 to {MaximumEvents} events.",
                nameof(events));
        }

        return new RemoteInputBatch([.. materialized]);
    }

    public override string ToString() =>
        $"Remote input batch ({events.Length} {(events.Length == 1 ? "event" : "events")})";
}

public sealed record RemoteInputAttemptResult
{
    internal RemoteInputAttemptResult(
        RemoteInputDecision decision,
        RemoteWindowSharingSnapshot snapshot,
        LocalBoundaryResult? boundary = null)
    {
        Decision = decision;
        Snapshot = snapshot;
        Boundary = boundary;
    }

    public RemoteInputDecision Decision { get; }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult? Boundary { get; }

    public bool Injected => Decision == RemoteInputDecision.Allowed
        && Boundary?.Succeeded == true;
}

public sealed record RemoteWindowProtectionResult
{
    internal RemoteWindowProtectionResult(
        RemoteWindowCommandStatus status,
        bool blocked,
        MirrorPauseReason? pauseReason,
        RemoteWindowSharingSnapshot snapshot,
        LocalBoundaryResult? captureBoundary = null,
        LocalBoundaryResult? inputBoundary = null)
    {
        Status = status;
        Blocked = blocked;
        PauseReason = pauseReason;
        Snapshot = snapshot;
        CaptureBoundary = captureBoundary;
        InputBoundary = inputBoundary;
    }

    public RemoteWindowCommandStatus Status { get; }

    public bool Blocked { get; }

    public MirrorPauseReason? PauseReason { get; }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult? CaptureBoundary { get; }

    public LocalBoundaryResult? InputBoundary { get; }

    public bool LocalGatesConfirmed =>
        (CaptureBoundary is null || CaptureBoundary.Succeeded)
        && (InputBoundary is null || InputBoundary.Succeeded);
}

public sealed record RemoteWindowEmergencyStopResult
{
    internal RemoteWindowEmergencyStopResult(
        RemoteWindowSharingSnapshot snapshot,
        LocalBoundaryResult captureBoundary,
        LocalBoundaryResult inputBoundary,
        LocalBoundaryResult sessionBoundary)
    {
        Snapshot = snapshot;
        CaptureBoundary = captureBoundary;
        InputBoundary = inputBoundary;
        SessionBoundary = sessionBoundary;
    }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult CaptureBoundary { get; }

    public LocalBoundaryResult InputBoundary { get; }

    public LocalBoundaryResult SessionBoundary { get; }

    public bool FullyStopped => CaptureBoundary.Succeeded
        && InputBoundary.Succeeded
        && SessionBoundary.Succeeded;
}

public sealed record RemoteWindowStopResult
{
    internal RemoteWindowStopResult(
        RemoteWindowCommandStatus status,
        RemoteWindowSharingSnapshot snapshot,
        LocalBoundaryResult captureBoundary,
        LocalBoundaryResult inputBoundary,
        LocalBoundaryResult sessionBoundary)
    {
        Status = status;
        Snapshot = snapshot;
        CaptureBoundary = captureBoundary;
        InputBoundary = inputBoundary;
        SessionBoundary = sessionBoundary;
    }

    public RemoteWindowCommandStatus Status { get; }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult CaptureBoundary { get; }

    public LocalBoundaryResult InputBoundary { get; }

    public LocalBoundaryResult SessionBoundary { get; }

    public bool FullyStopped => CaptureBoundary.Succeeded
        && InputBoundary.Succeeded
        && SessionBoundary.Succeeded;
}

public sealed class RemoteWindowSessionController : IDisposable
{
    public const int MaximumParticipants = 16;

    private const int MaximumProtectionConvergenceAttempts = 8;

    private readonly ActivityInstance activity;
    private readonly IMirrorAuthorizationSource authorization;
    private readonly IRemoteWindowCaptureBoundary capture;
    private readonly IClock clock;
    private readonly EmergencyStopLatch emergencyStop = new();
    private readonly DeviceId hostDeviceId;
    private readonly SemaphoreSlim normalOperationGate = new(1, 1);
    private readonly HashSet<int> protectionBoundaryThreads = [];
    private readonly object stateLock = new();
    private readonly TimeSpan ownerLeaseDuration;
    private readonly IRemoteInputBoundary input;
    private readonly RemoteInputPolicy remoteInputPolicy;
    private readonly ILocalSharingSessionBoundary sessions;

    private RemoteWindowCaptureState captureState = RemoteWindowCaptureState.Stopped;
    private bool captureEmergencyConfirmed;
    private int disposed;
    private bool inputEmergencyConfirmed;
    private RemoteWindowLifecycle lifecycle = RemoteWindowLifecycle.Idle;
    private MirrorSession? mirrorSession;
    private ProtectionSnapshot protection;
    private long protectionRevision;
    private long revision;
    private bool sessionEmergencyConfirmed;

    public RemoteWindowSessionController(
        DeviceId hostDeviceId,
        ActivityInstance activity,
        IClock clock,
        IMirrorAuthorizationSource authorization,
        IRemoteWindowCaptureBoundary capture,
        IRemoteInputBoundary input,
        ILocalSharingSessionBoundary sessions,
        TimeSpan ownerLeaseDuration)
    {
        this.hostDeviceId = hostDeviceId
            ?? throw new ArgumentNullException(nameof(hostDeviceId));
        this.activity = activity
            ?? throw new ArgumentNullException(nameof(activity));
        this.clock = clock
            ?? throw new ArgumentNullException(nameof(clock));
        this.authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        this.capture = capture
            ?? throw new ArgumentNullException(nameof(capture));
        this.input = input
            ?? throw new ArgumentNullException(nameof(input));
        this.sessions = sessions
            ?? throw new ArgumentNullException(nameof(sessions));
        if (activity.Placement.DeviceId != hostDeviceId
            || activity.Lifecycle != ActivityLifecycle.Active)
        {
            throw new ArgumentException(
                "A Remote Window controller requires an active Activity on its host Device.",
                nameof(activity));
        }

        _ = DriverLease.IssueToOwner(
            activity.Descriptor.Id,
            hostDeviceId,
            clock.UtcNow,
            ownerLeaseDuration);
        this.ownerLeaseDuration = ownerLeaseDuration;
        remoteInputPolicy = new RemoteInputPolicy(emergencyStop);
        protection = new ProtectionSnapshot(
            ProtectionKind.Unknown,
            clock.UtcNow,
            "not_observed");
    }

    public RemoteWindowSharingSnapshot Snapshot
    {
        get
        {
            lock (stateLock)
            {
                return CreateSnapshot();
            }
        }
    }

    public async ValueTask<RemoteWindowCommandResult> StartAsync(
        ProtectionSnapshot initialProtection,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(initialProtection);
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = clock.UtcNow;
            if (!IsFreshSafe(initialProtection, now))
            {
                lock (stateLock)
                {
                    protection = initialProtection;
                    protectionRevision = checked(protectionRevision + 1);
                    return Result(
                        RemoteWindowCommandStatus.ProtectionBlocked,
                        "protection_blocked");
                }
            }

            lock (stateLock)
            {
                if (lifecycle != RemoteWindowLifecycle.Idle)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_idle");
                }

                protection = initialProtection;
                protectionRevision = checked(protectionRevision + 1);
                mirrorSession = MirrorSession.Start(
                    activity.Descriptor.Id,
                    hostDeviceId,
                    now,
                    ownerLeaseDuration);
                lifecycle = RemoteWindowLifecycle.Starting;
                captureState = RemoteWindowCaptureState.Starting;
                revision = checked(revision + 1);
            }

            LocalBoundaryResult boundary;
            try
            {
                boundary = await capture
                    .StartAsync(activity.Descriptor.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (stateLock)
                {
                    mirrorSession = mirrorSession!.End(clock.UtcNow);
                    lifecycle = RemoteWindowLifecycle.Ended;
                    captureState = RemoteWindowCaptureState.Unconfirmed;
                    revision = checked(revision + 1);
                }

                throw;
            }
            catch (Exception)
            {
                boundary = LocalBoundaryResult.Failed("local_boundary_exception");
            }

            bool stoppedDuringStart;
            lock (stateLock)
            {
                stoppedDuringStart = emergencyStop.IsActive
                    || lifecycle == RemoteWindowLifecycle.EmergencyStopped;
            }

            if (stoppedDuringStart)
            {
                LocalBoundaryResult lateStop =
                    CallBoundary(capture.EmergencyStopNow);
                lock (stateLock)
                {
                    captureState = lateStop.Succeeded
                        ? RemoteWindowCaptureState.Stopped
                        : RemoteWindowCaptureState.Unconfirmed;
                    revision = checked(revision + 1);
                    return Result(
                        RemoteWindowCommandStatus.EmergencyStopped,
                        "emergency_stop_won_start_race",
                        lateStop);
                }
            }

            lock (stateLock)
            {
                if (!boundary.Succeeded)
                {
                    mirrorSession = mirrorSession!.End(clock.UtcNow);
                    lifecycle = RemoteWindowLifecycle.Unavailable;
                    captureState = RemoteWindowCaptureState.Stopped;
                    revision = checked(revision + 1);
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        boundary.ReasonCode,
                        boundary);
                }
            }

            RemoteWindowProtectionResult protectionResult =
                ConvergeProtectionGates(activateStartingWithoutResume: true);
            lock (stateLock)
            {
                if (lifecycle == RemoteWindowLifecycle.EmergencyStopped)
                {
                    return Result(
                        RemoteWindowCommandStatus.EmergencyStopped,
                        "emergency_stop_won_start_race",
                        protectionResult.CaptureBoundary);
                }

                if (protectionResult.Status == RemoteWindowCommandStatus.BoundaryFailed)
                {
                    LocalBoundaryResult? failedBoundary =
                        protectionResult.CaptureBoundary is { Succeeded: false }
                            ? protectionResult.CaptureBoundary
                            : protectionResult.InputBoundary;
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "protection_pause_unconfirmed",
                        failedBoundary);
                }

                if (protectionResult.Blocked)
                {
                    return Result(
                        RemoteWindowCommandStatus.ProtectionBlocked,
                        "protection_blocked_during_start",
                        protectionResult.CaptureBoundary);
                }

                return Result(
                    RemoteWindowCommandStatus.Applied,
                    boundary.ReasonCode,
                    boundary);
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> AddParticipantAsync(
        DeviceId peerDeviceId,
        MirrorParticipantRole role,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool canView;
            bool canDrive;
            try
            {
                CapabilityGrant grant = authorization.GetCurrentGrant(peerDeviceId);
                canView = grant.Allows(Capability.MirrorView);
                canDrive = role != MirrorParticipantRole.DriverEligible
                    || grant.Allows(Capability.MirrorDrive);
            }
            catch (Exception)
            {
                canView = false;
                canDrive = false;
            }

            lock (stateLock)
            {
                if (lifecycle is not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused
                    || mirrorSession is null)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_active");
                }

                if (peerDeviceId == hostDeviceId)
                {
                    return role == MirrorParticipantRole.DriverEligible
                        ? Result(
                            RemoteWindowCommandStatus.AlreadyApplied,
                            "host_already_participant")
                        : Result(
                            RemoteWindowCommandStatus.InvalidState,
                            "host_must_remain_driver_eligible");
                }

                if (!canView || !canDrive)
                {
                    return Result(
                        RemoteWindowCommandStatus.CapabilityDenied,
                        !canView
                            ? "mirror_view_denied"
                            : "mirror_drive_denied");
                }

                if (mirrorSession.Participants.TryGetValue(
                        peerDeviceId,
                        out MirrorParticipantRole currentRole)
                    && currentRole == role)
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "participant_role_current");
                }

                if (!mirrorSession.Participants.ContainsKey(peerDeviceId)
                    && mirrorSession.Participants.Count >= MaximumParticipants)
                {
                    return Result(
                        RemoteWindowCommandStatus.ParticipantLimitReached,
                        "participant_limit_reached");
                }

                if (mirrorSession.DriverLease.HolderDeviceId == peerDeviceId
                    && role == MirrorParticipantRole.ViewOnly)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "current_driver_cannot_be_downgraded");
                }

                mirrorSession = mirrorSession.AddParticipant(peerDeviceId, role);
                revision = checked(revision + 1);
                return Result(
                    RemoteWindowCommandStatus.Applied,
                    "participant_updated");
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> TransferDriverAsync(
        DeviceId peerDeviceId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool authorized;
            try
            {
                authorized = peerDeviceId == hostDeviceId
                    || AllowsViewAndDrive(
                        authorization.GetCurrentGrant(peerDeviceId));
            }
            catch (Exception)
            {
                authorized = false;
            }

            lock (stateLock)
            {
                if (lifecycle is not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused
                    || mirrorSession is null)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_active");
                }

                if (!authorized)
                {
                    return Result(
                        RemoteWindowCommandStatus.CapabilityDenied,
                        "mirror_drive_denied");
                }

                if (!mirrorSession.Participants.TryGetValue(
                        peerDeviceId,
                        out MirrorParticipantRole role)
                    || role != MirrorParticipantRole.DriverEligible)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "participant_not_driver_eligible");
                }

                DateTimeOffset now = clock.UtcNow;
                if (mirrorSession.DriverLease.Authorizes(
                        peerDeviceId,
                        mirrorSession.DriverLease.Epoch,
                        now))
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "driver_lease_current");
                }

                mirrorSession = mirrorSession.TransferDriver(
                    peerDeviceId,
                    now,
                    leaseDuration);
                revision = checked(revision + 1);
                return Result(
                    RemoteWindowCommandStatus.Applied,
                    "driver_transferred");
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteInputAttemptResult> InjectInputAsync(
        DeviceId peerDeviceId,
        long driverLeaseEpoch,
        RemoteInputBatch batch,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(batch);
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool authorized;
            try
            {
                authorized = peerDeviceId == hostDeviceId
                    || AllowsViewAndDrive(
                        authorization.GetCurrentGrant(peerDeviceId));
            }
            catch (Exception)
            {
                authorized = false;
            }

            lock (stateLock)
            {
                if (!authorized)
                {
                    return new RemoteInputAttemptResult(
                        RemoteInputDecision.CapabilityDenied,
                        CreateSnapshot());
                }

                if (lifecycle is not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused
                    || mirrorSession is null)
                {
                    return new RemoteInputAttemptResult(
                        RemoteInputDecision.SessionInactive,
                        CreateSnapshot());
                }

                RemoteInputDecision decision = remoteInputPolicy.Evaluate(
                    mirrorSession,
                    peerDeviceId,
                    driverLeaseEpoch,
                    protection,
                    clock.UtcNow);
                if (decision != RemoteInputDecision.Allowed)
                {
                    return new RemoteInputAttemptResult(decision, CreateSnapshot());
                }
            }

            LocalBoundaryResult boundary;
            try
            {
                boundary = await input
                    .InjectAsync(batch, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                boundary = LocalBoundaryResult.Failed("local_boundary_exception");
            }

            lock (stateLock)
            {
                if (mirrorSession is not null)
                {
                    RemoteInputDecision postBoundaryDecision =
                        remoteInputPolicy.Evaluate(
                            mirrorSession,
                            peerDeviceId,
                            driverLeaseEpoch,
                            protection,
                            clock.UtcNow);
                    if (postBoundaryDecision != RemoteInputDecision.Allowed)
                    {
                        return new RemoteInputAttemptResult(
                            postBoundaryDecision,
                            CreateSnapshot(),
                            boundary);
                    }
                }

                return new RemoteInputAttemptResult(
                    boundary.Succeeded
                        ? RemoteInputDecision.Allowed
                        : RemoteInputDecision.BoundaryFailed,
                    CreateSnapshot(),
                    boundary);
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> ReconcilePeerCapabilitiesAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (peerDeviceId == hostDeviceId)
            {
                lock (stateLock)
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "host_authority_local");
                }
            }

            bool canView;
            bool canDrive;
            try
            {
                CapabilityGrant grant = authorization.GetCurrentGrant(peerDeviceId);
                canView = grant.Allows(Capability.MirrorView);
                canDrive = canView && grant.Allows(Capability.MirrorDrive);
            }
            catch (Exception)
            {
                canView = false;
                canDrive = false;
            }

            lock (stateLock)
            {
                if (lifecycle is not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused
                    || mirrorSession is null)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_active");
                }

                if (!mirrorSession.Participants.TryGetValue(
                        peerDeviceId,
                        out MirrorParticipantRole currentRole))
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "peer_not_participant");
                }

                if (!canView)
                {
                    mirrorSession = mirrorSession.RemoveParticipant(
                        peerDeviceId,
                        clock.UtcNow,
                        ownerLeaseDuration);
                    revision = checked(revision + 1);
                }
                else if (!canDrive
                    && currentRole == MirrorParticipantRole.DriverEligible)
                {
                    if (mirrorSession.DriverLease.HolderDeviceId == peerDeviceId)
                    {
                        mirrorSession = mirrorSession.TransferDriver(
                            hostDeviceId,
                            clock.UtcNow,
                            ownerLeaseDuration);
                    }

                    mirrorSession = mirrorSession.AddParticipant(
                        peerDeviceId,
                        MirrorParticipantRole.ViewOnly);
                    revision = checked(revision + 1);
                    return Result(
                        RemoteWindowCommandStatus.Applied,
                        "peer_downgraded_to_view_only");
                }
                else
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "peer_capabilities_current");
                }
            }

            LocalBoundaryResult disconnect;
            try
            {
                disconnect = sessions.DisconnectPeerNow(peerDeviceId);
            }
            catch (Exception)
            {
                disconnect = LocalBoundaryResult.Failed("local_boundary_exception");
            }

            lock (stateLock)
            {
                return Result(
                    disconnect.Succeeded
                        ? RemoteWindowCommandStatus.Applied
                        : RemoteWindowCommandStatus.BoundaryFailed,
                    disconnect.Succeeded
                        ? "peer_removed"
                        : disconnect.ReasonCode,
                    disconnect);
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> DisconnectParticipantAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                if (lifecycle is not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused
                    || mirrorSession is null)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_active");
                }

                if (peerDeviceId == hostDeviceId)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "host_cannot_disconnect_from_own_session");
                }

                if (!mirrorSession.Participants.ContainsKey(peerDeviceId))
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "peer_not_participant");
                }

                mirrorSession = mirrorSession.RemoveParticipant(
                    peerDeviceId,
                    clock.UtcNow,
                    ownerLeaseDuration);
                revision = checked(revision + 1);
            }

            LocalBoundaryResult disconnect =
                CallBoundary(() => sessions.DisconnectPeerNow(peerDeviceId));
            lock (stateLock)
            {
                return Result(
                    disconnect.Succeeded
                        ? RemoteWindowCommandStatus.Applied
                        : RemoteWindowCommandStatus.BoundaryFailed,
                    disconnect.ReasonCode,
                    disconnect);
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> RefreshExpiredLeaseAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                if (lifecycle is not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused
                    || mirrorSession is null)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_active");
                }

                MirrorSession refreshed = mirrorSession.RefreshExpiredLease(
                    clock.UtcNow,
                    ownerLeaseDuration);
                if (refreshed.DriverLease == mirrorSession.DriverLease)
                {
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "driver_lease_not_expired");
                }

                mirrorSession = refreshed;
                revision = checked(revision + 1);
                return Result(
                    RemoteWindowCommandStatus.Applied,
                    "expired_lease_returned_to_host");
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public RemoteWindowProtectionResult ApplyProtectionSnapshot(
        ProtectionSnapshot observation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(observation);
        lock (stateLock)
        {
            if (lifecycle is not RemoteWindowLifecycle.Starting
                and not RemoteWindowLifecycle.Active
                and not RemoteWindowLifecycle.ProtectionPaused
                || mirrorSession is null)
            {
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.InvalidState,
                    blocked: true,
                    pauseReason: null,
                    CreateSnapshot());
            }

            protection = observation;
            protectionRevision = checked(protectionRevision + 1);
            MirrorPauseReason? pauseReason =
                ClassifyProtection(observation, clock.UtcNow);
            bool resuming = pauseReason is null
                && lifecycle == RemoteWindowLifecycle.ProtectionPaused;
            if (pauseReason is null && !resuming)
            {
                revision = checked(revision + 1);
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.AlreadyApplied,
                    blocked: false,
                    pauseReason: null,
                    CreateSnapshot());
            }

            if (pauseReason is not null)
            {
                lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }
        }

        return ConvergeProtectionGates();
    }

    private RemoteWindowProtectionResult ConvergeProtectionGates(
        bool activateStartingWithoutResume = false)
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        lock (stateLock)
        {
            if (!protectionBoundaryThreads.Add(currentThreadId))
            {
                lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
                LocalBoundaryResult deferred = LocalBoundaryResult.Failed(
                    "protection_reconciliation_in_progress");
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.BoundaryFailed,
                    blocked: true,
                    ClassifyProtection(protection, clock.UtcNow),
                    CreateSnapshot(),
                    deferred,
                    deferred);
            }
        }

        try
        {
            return ConvergeProtectionGatesCore(activateStartingWithoutResume);
        }
        finally
        {
            lock (stateLock)
            {
                _ = protectionBoundaryThreads.Remove(currentThreadId);
            }
        }
    }

    private RemoteWindowProtectionResult ConvergeProtectionGatesCore(
        bool activateStartingWithoutResume)
    {
        LocalBoundaryResult? captureBoundary = null;
        LocalBoundaryResult? inputBoundary = null;
        for (int attempt = 0;
             attempt < MaximumProtectionConvergenceAttempts;
             attempt++)
        {
            long expectedProtectionRevision;
            MirrorPauseReason? pauseReason;
            lock (stateLock)
            {
                if (lifecycle == RemoteWindowLifecycle.EmergencyStopped)
                {
                    return new RemoteWindowProtectionResult(
                        RemoteWindowCommandStatus.InvalidState,
                        blocked: true,
                        pauseReason: null,
                        CreateSnapshot(),
                        captureBoundary,
                        inputBoundary);
                }

                expectedProtectionRevision = protectionRevision;
                pauseReason = ClassifyProtection(protection, clock.UtcNow);
                if (pauseReason is null
                    && activateStartingWithoutResume
                    && lifecycle == RemoteWindowLifecycle.Starting)
                {
                    lifecycle = RemoteWindowLifecycle.Active;
                    captureState = RemoteWindowCaptureState.Capturing;
                    revision = checked(revision + 1);
                    return new RemoteWindowProtectionResult(
                        RemoteWindowCommandStatus.Applied,
                        blocked: false,
                        pauseReason: null,
                        CreateSnapshot());
                }

                if (pauseReason is not null)
                {
                    lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                    captureState = RemoteWindowCaptureState.Unconfirmed;
                    revision = checked(revision + 1);
                }
            }

            bool resuming = pauseReason is null;
            captureBoundary = resuming
                ? CallBoundary(capture.ResumeNow)
                : CallBoundary(() => capture.PauseNow(pauseReason!.Value));
            if (HasEmergencyStopped())
            {
                return ReassertEmergencyAfterProtectionBoundary(
                    pauseReason,
                    captureBoundary,
                    inputBoundary);
            }

            inputBoundary = resuming
                ? CallBoundary(input.ResumeNow)
                : CallBoundary(() => input.PauseNow(pauseReason!.Value));
            if (HasEmergencyStopped())
            {
                return ReassertEmergencyAfterProtectionBoundary(
                    pauseReason,
                    captureBoundary,
                    inputBoundary);
            }

            if (resuming
                && (!captureBoundary.Succeeded || !inputBoundary.Succeeded))
            {
                const MirrorPauseReason failedResume =
                    MirrorPauseReason.ProtectionStateUnknown;
                _ = CallBoundary(() => capture.PauseNow(failedResume));
                if (HasEmergencyStopped())
                {
                    return ReassertEmergencyAfterProtectionBoundary(
                        pauseReason,
                        captureBoundary,
                        inputBoundary);
                }

                _ = CallBoundary(() => input.PauseNow(failedResume));
                if (HasEmergencyStopped())
                {
                    return ReassertEmergencyAfterProtectionBoundary(
                        pauseReason,
                        captureBoundary,
                        inputBoundary);
                }
            }

            lock (stateLock)
            {
                if (lifecycle == RemoteWindowLifecycle.EmergencyStopped)
                {
                    return new RemoteWindowProtectionResult(
                        RemoteWindowCommandStatus.InvalidState,
                        blocked: true,
                        pauseReason,
                        CreateSnapshot(),
                        captureBoundary,
                        inputBoundary);
                }

                if (expectedProtectionRevision != protectionRevision)
                {
                    continue;
                }

                if (captureBoundary.Succeeded && inputBoundary.Succeeded)
                {
                    lifecycle = resuming
                        ? RemoteWindowLifecycle.Active
                        : RemoteWindowLifecycle.ProtectionPaused;
                    captureState = resuming
                        ? RemoteWindowCaptureState.Capturing
                        : RemoteWindowCaptureState.Paused;
                }
                else
                {
                    lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                    captureState = RemoteWindowCaptureState.Unconfirmed;
                }

                revision = checked(revision + 1);
                return new RemoteWindowProtectionResult(
                    captureBoundary.Succeeded && inputBoundary.Succeeded
                        ? RemoteWindowCommandStatus.Applied
                        : RemoteWindowCommandStatus.BoundaryFailed,
                    blocked: !resuming
                        || !captureBoundary.Succeeded
                        || !inputBoundary.Succeeded,
                    pauseReason,
                    CreateSnapshot(),
                    captureBoundary,
                    inputBoundary);
            }
        }

        const MirrorPauseReason convergenceFailure =
            MirrorPauseReason.ProtectionStateUnknown;
        lock (stateLock)
        {
            lifecycle = RemoteWindowLifecycle.ProtectionPaused;
            captureState = RemoteWindowCaptureState.Unconfirmed;
            revision = checked(revision + 1);
        }

        captureBoundary =
            CallBoundary(() => capture.PauseNow(convergenceFailure));
        inputBoundary =
            CallBoundary(() => input.PauseNow(convergenceFailure));
        lock (stateLock)
        {
            if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
            {
                captureState = captureBoundary.Succeeded && inputBoundary.Succeeded
                    ? RemoteWindowCaptureState.Paused
                    : RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }

            return new RemoteWindowProtectionResult(
                RemoteWindowCommandStatus.BoundaryFailed,
                blocked: true,
                convergenceFailure,
                CreateSnapshot(),
                captureBoundary,
                inputBoundary);
        }
    }

    private bool HasEmergencyStopped()
    {
        lock (stateLock)
        {
            return lifecycle == RemoteWindowLifecycle.EmergencyStopped;
        }
    }

    private RemoteWindowProtectionResult ReassertEmergencyAfterProtectionBoundary(
        MirrorPauseReason? pauseReason,
        LocalBoundaryResult? captureBoundary,
        LocalBoundaryResult? inputBoundary)
    {
        _ = EmergencyStop();
        lock (stateLock)
        {
            return new RemoteWindowProtectionResult(
                RemoteWindowCommandStatus.InvalidState,
                blocked: true,
                pauseReason,
                CreateSnapshot(),
                captureBoundary,
                inputBoundary);
        }
    }

    public RemoteWindowEmergencyStopResult EmergencyStop()
    {
        lock (stateLock)
        {
            emergencyStop.Activate();
            if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
            {
                captureEmergencyConfirmed = false;
                inputEmergencyConfirmed = false;
                sessionEmergencyConfirmed = false;
                if (mirrorSession?.Status == MirrorSessionStatus.Active)
                {
                    mirrorSession = mirrorSession.EmergencyStop(clock.UtcNow);
                }

                lifecycle = RemoteWindowLifecycle.EmergencyStopped;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }
        }

        LocalBoundaryResult captureBoundary =
            CallBoundary(capture.EmergencyStopNow);
        LocalBoundaryResult inputBoundary =
            CallBoundary(input.EmergencyStopNow);
        LocalBoundaryResult sessionBoundary =
            CallBoundary(sessions.DisconnectAllNow);

        lock (stateLock)
        {
            captureEmergencyConfirmed |= captureBoundary.Succeeded;
            inputEmergencyConfirmed |= inputBoundary.Succeeded;
            sessionEmergencyConfirmed |= sessionBoundary.Succeeded;
            captureState = captureBoundary.Succeeded
                ? RemoteWindowCaptureState.Stopped
                : RemoteWindowCaptureState.Unconfirmed;
            revision = checked(revision + 1);
            return new RemoteWindowEmergencyStopResult(
                CreateSnapshot(),
                captureBoundary,
                inputBoundary,
                sessionBoundary);
        }
    }

    public async ValueTask<RemoteWindowCommandResult> ResetAfterLocalConfirmationAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_emergency_stopped");
                }

                if (!captureEmergencyConfirmed
                    || !inputEmergencyConfirmed
                    || !sessionEmergencyConfirmed)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "emergency_boundaries_unconfirmed");
                }

                emergencyStop.ResetAfterLocalConfirmation();
                mirrorSession = null;
                lifecycle = RemoteWindowLifecycle.Idle;
                captureState = RemoteWindowCaptureState.Stopped;
                protection = new ProtectionSnapshot(
                    ProtectionKind.Unknown,
                    clock.UtcNow,
                    "not_observed");
                captureEmergencyConfirmed = false;
                inputEmergencyConfirmed = false;
                sessionEmergencyConfirmed = false;
                revision = checked(revision + 1);
                return Result(
                    RemoteWindowCommandStatus.Applied,
                    "emergency_stop_reset_locally");
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public async ValueTask<RemoteWindowStopResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await normalOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool wasAlreadyStopped;
            lock (stateLock)
            {
                wasAlreadyStopped = lifecycle is RemoteWindowLifecycle.Idle
                    or RemoteWindowLifecycle.Ended
                    or RemoteWindowLifecycle.Unavailable;
                if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
                {
                    if (mirrorSession?.Status == MirrorSessionStatus.Active)
                    {
                        mirrorSession = mirrorSession.End(clock.UtcNow);
                    }

                    lifecycle = RemoteWindowLifecycle.Ended;
                    captureState = RemoteWindowCaptureState.Unconfirmed;
                    revision = checked(revision + 1);
                }
            }

            LocalBoundaryResult captureBoundary =
                CallBoundary(capture.StopNow);
            LocalBoundaryResult inputBoundary =
                CallBoundary(input.StopNow);
            LocalBoundaryResult sessionBoundary =
                CallBoundary(sessions.DisconnectAllNow);

            lock (stateLock)
            {
                captureState = captureBoundary.Succeeded
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
                RemoteWindowCommandStatus status =
                    captureBoundary.Succeeded
                    && inputBoundary.Succeeded
                    && sessionBoundary.Succeeded
                        ? wasAlreadyStopped
                            ? RemoteWindowCommandStatus.AlreadyApplied
                            : RemoteWindowCommandStatus.Applied
                        : RemoteWindowCommandStatus.BoundaryFailed;
                return new RemoteWindowStopResult(
                    status,
                    CreateSnapshot(),
                    captureBoundary,
                    inputBoundary,
                    sessionBoundary);
            }
        }
        finally
        {
            normalOperationGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        bool failClosed;
        lock (stateLock)
        {
            failClosed = lifecycle is RemoteWindowLifecycle.Starting
                or RemoteWindowLifecycle.Active
                or RemoteWindowLifecycle.ProtectionPaused;
        }

        if (failClosed)
        {
            _ = EmergencyStop();
        }

        normalOperationGate.Wait();
        normalOperationGate.Release();
        normalOperationGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static bool IsFreshSafe(
        ProtectionSnapshot snapshot,
        DateTimeOffset now) =>
        snapshot.Kind == ProtectionKind.Safe
        && snapshot.ObservedAt <= now.Add(RemoteInputPolicy.MaximumFutureClockSkew)
        && now - snapshot.ObservedAt <= RemoteInputPolicy.MaximumProtectionAge;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    private static bool AllowsViewAndDrive(CapabilityGrant grant) =>
        grant.Allows(Capability.MirrorView)
        && grant.Allows(Capability.MirrorDrive);

    private static MirrorPauseReason? ClassifyProtection(
        ProtectionSnapshot snapshot,
        DateTimeOffset now)
    {
        if (snapshot.ObservedAt > now.Add(RemoteInputPolicy.MaximumFutureClockSkew)
            || now - snapshot.ObservedAt > RemoteInputPolicy.MaximumProtectionAge)
        {
            return MirrorPauseReason.ProtectionStateStale;
        }

        if (snapshot.Kind == ProtectionKind.Unknown)
        {
            return MirrorPauseReason.ProtectionStateUnknown;
        }

        return snapshot.Kind == ProtectionKind.Safe
            ? null
            : MirrorPauseReason.SensitiveSurface;
    }

    private static LocalBoundaryResult CallBoundary(
        Func<LocalBoundaryResult> boundary)
    {
        try
        {
            return boundary();
        }
        catch (Exception)
        {
            return LocalBoundaryResult.Failed("local_boundary_exception");
        }
    }

    private RemoteWindowCommandResult Result(
        RemoteWindowCommandStatus status,
        string reasonCode,
        LocalBoundaryResult? boundary = null) =>
        new(status, reasonCode, CreateSnapshot(), boundary);

    private RemoteWindowSharingSnapshot CreateSnapshot()
    {
        ImmutableDictionary<DeviceId, MirrorParticipantRole> participants =
            mirrorSession is null
                ? ImmutableDictionary<DeviceId, MirrorParticipantRole>.Empty
                : mirrorSession.Participants.ToImmutableDictionary();
        return new RemoteWindowSharingSnapshot(
            activity.Descriptor.Id,
            activity.Descriptor.Kind,
            activity.Descriptor.Title,
            hostDeviceId,
            lifecycle,
            captureState,
            participants,
            mirrorSession?.DriverLease.HolderDeviceId,
            mirrorSession?.DriverLease.Epoch,
            mirrorSession?.DriverLease.ExpiresAt,
            protection.Kind,
            revision);
    }
}
