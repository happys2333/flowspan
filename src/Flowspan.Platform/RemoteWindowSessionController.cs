using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Platform;

public interface IMirrorAuthorizationSource
{
    public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId);
}

public interface IRemoteWindowCaptureGate
{
    public LocalBoundaryResult PauseNow(MirrorPauseReason reason);

    public LocalBoundaryResult ResumeNow();

    public LocalBoundaryResult EmergencyStopNow();

    public LocalBoundaryResult StopNow();
}

public interface IRemoteWindowCaptureBoundary : IRemoteWindowCaptureGate
{
    public ValueTask<LocalBoundaryResult> StartAsync(
        ActivityId activityId,
        CancellationToken cancellationToken);
}

public interface IRemoteInputGate
{
    public LocalBoundaryResult PauseNow(MirrorPauseReason reason);

    public LocalBoundaryResult ResumeNow();

    public LocalBoundaryResult EmergencyStopNow();

    public LocalBoundaryResult StopNow();
}

public interface IRemoteInputBoundary : IRemoteInputGate
{
    public ValueTask<LocalBoundaryResult> InjectAsync(
        RemoteInputBatch batch,
        CancellationToken cancellationToken);
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
        ActivityKind? activityKind,
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

    public ActivityKind? ActivityKind { get; }

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

    public override string ToString()
    {
        string sourceKind = ActivityKind?.Value ?? "generic";
        return $"Remote Window {ActivityId} ({sourceKind}, {Lifecycle}, {CaptureState}, driver {CurrentDriverDeviceId}, epoch {DriverLeaseEpoch}, revision {Revision})";
    }
}

public sealed record RemoteWindowCommandResult
{
    internal RemoteWindowCommandResult(
        RemoteWindowCommandStatus status,
        string reasonCode,
        RemoteWindowSharingSnapshot snapshot,
        LocalBoundaryResult? boundary = null,
        LocalBoundaryResult? cleanupBoundary = null)
    {
        Status = status;
        ReasonCode = reasonCode;
        Snapshot = snapshot;
        Boundary = boundary;
        CleanupBoundary = cleanupBoundary;
    }

    public RemoteWindowCommandStatus Status { get; }

    public string ReasonCode { get; }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult? Boundary { get; }

    public LocalBoundaryResult? CleanupBoundary { get; }

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
        LocalBoundaryResult? inputBoundary = null,
        LocalBoundaryResult? sessionBoundary = null)
    {
        Status = status;
        Blocked = blocked;
        PauseReason = pauseReason;
        Snapshot = snapshot;
        CaptureBoundary = captureBoundary;
        InputBoundary = inputBoundary;
        SessionBoundary = sessionBoundary;
    }

    public RemoteWindowCommandStatus Status { get; }

    public bool Blocked { get; }

    public MirrorPauseReason? PauseReason { get; }

    public RemoteWindowSharingSnapshot Snapshot { get; }

    public LocalBoundaryResult? CaptureBoundary { get; }

    public LocalBoundaryResult? InputBoundary { get; }

    public LocalBoundaryResult? SessionBoundary { get; }

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
    private static readonly object ProtectionAdmissionActivityOwner = new();

    private readonly IMirrorAuthorizationSource authorization;
    private readonly IRemoteWindowCaptureGate capture;
    private readonly IClock clock;
    private readonly object disposalActivityToken = new();
    private readonly AsyncLocal<DisposalCallScope?> disposalCallScope = new();
    private readonly AsyncLocal<EmergencyStopCallScope?> emergencyStopCallScope = new();
    private readonly EmergencyStopLatch emergencyStop = new();
    private readonly DeviceId hostDeviceId;
    private readonly object lifetimeActivityToken = new();
    private readonly SemaphoreSlim normalOperationGate = new(1, 1);
    private readonly object operationLifetimeLock = new();
    private readonly Dictionary<long, int> emergencyStopAttemptsByGeneration = [];
    private readonly HashSet<DeviceId> pendingPeerDisconnects = [];
    private readonly HashSet<int> protectionBoundaryThreads = [];
    private readonly object stateLock = new();
    private readonly TimeSpan ownerLeaseDuration;
    private readonly IRemoteInputGate input;
    private readonly INativeRemoteWindowCaptureBoundary? nativeCapture;
    private readonly INativeRemoteWindowFrameSink? nativeFrameSink;
    private readonly INativeRemoteInputBoundary? nativeInput;
    private readonly long nativeOwnerGeneration;
    private readonly NativeRemoteWindowSourceInvalidationRegistration?
        nativeSourceInvalidationRegistration;
    private readonly NativeRemoteWindowSourceLease? nativeSourceLease;
    private readonly RemoteInputPolicy remoteInputPolicy;
    private readonly ILocalSharingSessionBoundary sessions;
    private readonly IRemoteWindowCaptureBoundary? semanticCapture;
    private readonly IRemoteInputBoundary? semanticInput;
    private readonly RemoteWindowSourceReference source;

    private RemoteWindowCaptureState captureState = RemoteWindowCaptureState.Stopped;
    private bool captureAdmissionConfirmed;
    private bool captureAdmissionInFlight;
    private long captureAdmissionSessionGeneration;
    private bool captureEmergencyConfirmed;
    private bool disposalFailCloseCompleted;
    private int disposed;
    private long emergencyConfirmationGeneration;
    private long emergencyStopGeneration;
    private long emergencyStopSessionGeneration;
    private bool inputEmergencyConfirmed;
    private RemoteWindowLifecycle lifecycle = RemoteWindowLifecycle.Idle;
    private MirrorSession? mirrorSession;
    private BoundedNativeRemoteWindowFrameSink? nativeBoundFrameSink;
    private bool nativeSourceInvalidated;
    private int activeProtectionAdmissionUses;
    private int protectionAdmissionDrainWaiters;
    private long? pendingProtectionAdmissionOpenEpoch;
    private ProtectionSnapshot? pendingProtectionAdmissionOpenObservation;
    private ProtectionSnapshot protection;
    private bool protectionAdmissionClosed;
    private long protectionAdmissionAppliedEpoch;
    private long protectionAdmissionEpoch;
    private long protectionRevision;
    private long revision;
    private int lifetimeDrainWaiters;
    private int lifetimeFinalizationState;
    private int lifetimeFinalizationWaiters;
    private int registeredOperations;
    private long sessionGeneration;
    private bool sessionEmergencyConfirmed;
    private bool terminalStopConfirmed = true;

    public RemoteWindowSessionController(
        DeviceId hostDeviceId,
        ActivityInstance activity,
        IClock clock,
        IMirrorAuthorizationSource authorization,
        IRemoteWindowCaptureBoundary capture,
        IRemoteInputBoundary input,
        ILocalSharingSessionBoundary sessions,
        TimeSpan ownerLeaseDuration) : this(
            CreateSemanticSource(hostDeviceId, activity),
            clock,
            authorization,
            capture,
            input,
            sessions,
            ownerLeaseDuration,
            capture,
            input,
            nativeCapture: null,
            nativeInput: null,
            nativeSourceLease: null,
            nativeOwnerGeneration: 0,
            nativeFrameSink: null)
    {
    }

    public RemoteWindowSessionController(
        NativeRemoteWindowSourceLease sourceLease,
        long ownerGeneration,
        IClock clock,
        IMirrorAuthorizationSource authorization,
        INativeRemoteWindowCaptureBoundary capture,
        INativeRemoteInputBoundary input,
        INativeRemoteWindowFrameSink frameSink,
        ILocalSharingSessionBoundary sessions,
        TimeSpan ownerLeaseDuration) : this(
            GetCurrentNativeSource(sourceLease),
            clock,
            authorization,
            capture,
            input,
            sessions,
            ownerLeaseDuration,
            semanticCapture: null,
            semanticInput: null,
            capture,
            input,
            sourceLease,
            ownerGeneration,
            frameSink)
    {
    }

    private RemoteWindowSessionController(
        RemoteWindowSourceReference source,
        IClock clock,
        IMirrorAuthorizationSource authorization,
        IRemoteWindowCaptureGate capture,
        IRemoteInputGate input,
        ILocalSharingSessionBoundary sessions,
        TimeSpan ownerLeaseDuration,
        IRemoteWindowCaptureBoundary? semanticCapture,
        IRemoteInputBoundary? semanticInput,
        INativeRemoteWindowCaptureBoundary? nativeCapture,
        INativeRemoteInputBoundary? nativeInput,
        NativeRemoteWindowSourceLease? nativeSourceLease,
        long nativeOwnerGeneration,
        INativeRemoteWindowFrameSink? nativeFrameSink)
    {
        this.source = source
            ?? throw new ArgumentNullException(nameof(source));
        hostDeviceId = source.HostDeviceId;
        this.clock = clock
            ?? throw new ArgumentNullException(nameof(clock));
        this.authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        this.capture = capture
            ?? throw new ArgumentNullException(nameof(capture));
        this.input = input
            ?? throw new ArgumentNullException(nameof(input));
        this.semanticCapture = semanticCapture;
        this.semanticInput = semanticInput;
        this.nativeCapture = nativeCapture;
        this.nativeInput = nativeInput;
        this.nativeOwnerGeneration = nativeOwnerGeneration;
        this.nativeFrameSink = nativeFrameSink;
        this.sessions = sessions
            ?? throw new ArgumentNullException(nameof(sessions));
        bool semanticPath = semanticCapture is not null && semanticInput is not null;
        bool nativePath = nativeCapture is not null
            && nativeInput is not null
            && nativeSourceLease is not null
            && nativeFrameSink is not null;
        if (semanticPath == nativePath)
        {
            throw new ArgumentException(
                "A Remote Window controller requires exactly one complete semantic or native boundary path.");
        }

        if (nativePath)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nativeOwnerGeneration, 1);
            if (!nativeSourceLease!.TryRetain(
                    out NativeRemoteWindowSourceLease? retainedSourceLease)
                || retainedSourceLease is null)
            {
                throw new ArgumentException(
                    "A native Remote Window controller requires a current source lease.",
                    nameof(nativeSourceLease));
            }

            this.nativeSourceLease = retainedSourceLease;
        }
        else
        {
            this.nativeSourceLease = null;
        }

        _ = DriverLease.IssueToOwner(
            source.ActivityId,
            hostDeviceId,
            clock.UtcNow,
            ownerLeaseDuration);
        this.ownerLeaseDuration = ownerLeaseDuration;
        remoteInputPolicy = new RemoteInputPolicy(emergencyStop);
        protection = new ProtectionSnapshot(
            ProtectionKind.Unknown,
            clock.UtcNow,
            "not_observed");
        if (this.nativeSourceLease is not null
            && (!this.nativeSourceLease.TryRegisterInvalidationCallback(
                    OnNativeSourceInvalidated,
                    out nativeSourceInvalidationRegistration)
                || nativeSourceInvalidationRegistration is null))
        {
            this.nativeSourceLease.Dispose();
            throw new ArgumentException(
                "A native Remote Window controller requires a current source lease.",
                nameof(nativeSourceLease));
        }
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

    internal int LifetimeDrainWaiterCount
    {
        get
        {
            lock (operationLifetimeLock)
            {
                return lifetimeDrainWaiters;
            }
        }
    }

    internal bool LifetimeFinalizationCompleted =>
        Volatile.Read(ref lifetimeFinalizationState)
            == (int)LifetimeFinalizationState.Completed;

    internal int LifetimeFinalizationWaiterCount
    {
        get
        {
            lock (operationLifetimeLock)
            {
                return lifetimeFinalizationWaiters;
            }
        }
    }

    public ValueTask<RemoteWindowCommandResult> StartAsync(
        ProtectionSnapshot initialProtection,
        CancellationToken cancellationToken = default) =>
        StartCoreAsync(
            initialProtection,
            captureStartAdmission: null,
            cancellationToken);

    internal ValueTask<RemoteWindowCommandResult> StartAsync(
        ProtectionSnapshot initialProtection,
        IRemoteWindowCaptureStartAdmission captureStartAdmission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureStartAdmission);
        return StartCoreAsync(
            initialProtection,
            captureStartAdmission,
            cancellationToken);
    }

    private async ValueTask<RemoteWindowCommandResult> StartCoreAsync(
        ProtectionSnapshot initialProtection,
        IRemoteWindowCaptureStartAdmission? captureStartAdmission,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(initialProtection);
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
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

            if (nativeSourceLease is not null
                && !nativeSourceLease.TryGetCurrentSnapshot(out _))
            {
                LocalBoundaryResult staleSource =
                    LocalBoundaryResult.Failed("native_source_stale");
                lock (stateLock)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        staleSource.ReasonCode,
                        staleSource);
                }
            }

            long admittedSessionGeneration;
            lock (stateLock)
            {
                if (lifecycle != RemoteWindowLifecycle.Idle)
                {
                    if (nativeSourceLease is not null
                        && !nativeSourceLease.IsCurrent)
                    {
                        LocalBoundaryResult staleSource =
                            LocalBoundaryResult.Failed("native_source_stale");
                        return Result(
                            RemoteWindowCommandStatus.BoundaryFailed,
                            staleSource.ReasonCode,
                            staleSource);
                    }

                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_idle");
                }

                protection = initialProtection;
                protectionAdmissionClosed = false;
                protectionAdmissionEpoch++;
                protectionAdmissionAppliedEpoch = protectionAdmissionEpoch;
                pendingProtectionAdmissionOpenEpoch = null;
                pendingProtectionAdmissionOpenObservation = null;
                protectionRevision = checked(protectionRevision + 1);
                sessionGeneration = checked(sessionGeneration + 1);
                terminalStopConfirmed = false;
                admittedSessionGeneration = sessionGeneration;
                captureAdmissionConfirmed = false;
                captureAdmissionInFlight = true;
                captureAdmissionSessionGeneration = admittedSessionGeneration;
                pendingPeerDisconnects.Clear();
                mirrorSession = MirrorSession.Start(
                    source.ActivityId,
                    hostDeviceId,
                    now,
                    ownerLeaseDuration);
                lifecycle = RemoteWindowLifecycle.Starting;
                captureState = RemoteWindowCaptureState.Starting;
                revision = checked(revision + 1);
            }

            LocalBoundaryResult boundary;
            NativeRemoteWindowSourceUse? sourceUse = null;
            bool admissionConfirmed = false;
            bool protectionAdmitted = true;
            if (captureStartAdmission is not null)
            {
                try
                {
                    DateTimeOffset captureAdmissionNow = clock.UtcNow;
                    protectionAdmitted =
                        captureStartAdmission.TryAdmitCaptureStart(
                            captureAdmissionNow);
                }
                catch (OutOfMemoryException)
                {
                    CompleteCaptureAdmission(
                        admittedSessionGeneration,
                        admissionConfirmed: false);
                    _ = CleanupFailedStart(
                        RemoteWindowLifecycle.Unavailable,
                        admittedSessionGeneration);
                    throw;
                }
                catch (Exception)
                {
                    protectionAdmitted = false;
                }
            }

            if (!protectionAdmitted)
            {
                boundary = LocalBoundaryResult.Failed(
                    "native_protection_not_safe");
                CompleteCaptureAdmission(
                    admittedSessionGeneration,
                    admissionConfirmed: false);
            }
            else if (nativeSourceLease is not null
                && !TryCreateNativeSourceUse(
                    admittedSessionGeneration,
                    out sourceUse))
            {
                boundary = LocalBoundaryResult.Failed("native_source_stale");
                CompleteCaptureAdmission(
                    admittedSessionGeneration,
                    admissionConfirmed: false);
            }
            else
            {
                try
                {
                    try
                    {
                        boundary = await StartCaptureAsync(
                                sourceUse,
                                cancellationToken)
                            .ConfigureAwait(false);
                        admissionConfirmed = boundary.Succeeded;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    finally
                    {
                        CompleteCaptureAdmission(
                            admittedSessionGeneration,
                            admissionConfirmed);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _ = CleanupFailedStart(
                        RemoteWindowLifecycle.Ended,
                        admittedSessionGeneration);
                    throw;
                }
                catch (Exception)
                {
                    boundary = LocalBoundaryResult.Failed("local_boundary_exception");
                }
            }

            if (boundary.Succeeded
                && sourceUse is not null
                && !IsNativeSourceUseCurrent(
                    sourceUse,
                    requireGeometryRevision: true,
                    out string staleReasonCode))
            {
                boundary = LocalBoundaryResult.Failed(staleReasonCode);
            }

            bool stoppedDuringStart;
            lock (stateLock)
            {
                stoppedDuringStart = emergencyStop.IsActive
                    || lifecycle == RemoteWindowLifecycle.EmergencyStopped;
            }

            if (stoppedDuringStart)
            {
                CloseNativeFrameSinkNow();
                LocalBoundaryResult lateStop =
                    CallBoundary(capture.EmergencyStopNow);
                lock (stateLock)
                {
                    if (IsCurrentEmergencyStop(admittedSessionGeneration))
                    {
                        captureEmergencyConfirmed |= lateStop.Succeeded;
                    }

                    captureState = captureEmergencyConfirmed
                        ? RemoteWindowCaptureState.Stopped
                        : RemoteWindowCaptureState.Unconfirmed;
                    revision = checked(revision + 1);
                    return Result(
                        RemoteWindowCommandStatus.EmergencyStopped,
                        "emergency_stop_won_start_race",
                        boundary,
                        lateStop);
                }
            }

            if (!boundary.Succeeded)
            {
                LocalBoundaryResult? cleanupBoundary = null;
                lock (stateLock)
                {
                    if (nativeSourceLease is not null
                        && !nativeSourceLease.IsCurrent
                        && sessionGeneration == admittedSessionGeneration
                        && lifecycle == RemoteWindowLifecycle.Unavailable
                        && terminalStopConfirmed)
                    {
                        cleanupBoundary = LocalBoundaryResult.Confirmed(
                            "native_source_invalidated_stopped");
                    }
                }

                cleanupBoundary ??= CleanupFailedStart(
                    RemoteWindowLifecycle.Unavailable,
                    admittedSessionGeneration);
                lock (stateLock)
                {
                    if (lifecycle == RemoteWindowLifecycle.EmergencyStopped)
                    {
                        return Result(
                            RemoteWindowCommandStatus.EmergencyStopped,
                            "emergency_stop_won_start_race",
                            boundary,
                            cleanupBoundary);
                    }

                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        boundary.ReasonCode,
                        boundary,
                        cleanupBoundary);
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
            ReleaseNormalOperation();
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

        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
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

                if (pendingPeerDisconnects.Contains(peerDeviceId))
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "peer_disconnect_pending");
                }

                if (mirrorSession.Participants.TryGetValue(
                        peerDeviceId,
                        out MirrorParticipantRole currentRole)
                    && currentRole == role)
                {
                    _ = pendingPeerDisconnects.Remove(peerDeviceId);
                    return Result(
                        RemoteWindowCommandStatus.AlreadyApplied,
                        "participant_role_current");
                }

                if (!mirrorSession.Participants.ContainsKey(peerDeviceId)
                    && mirrorSession.Participants.Count
                        + pendingPeerDisconnects.Count >= MaximumParticipants)
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
                _ = pendingPeerDisconnects.Remove(peerDeviceId);
                revision = checked(revision + 1);
                return Result(
                    RemoteWindowCommandStatus.Applied,
                    "participant_updated");
            }
        }
        finally
        {
            ReleaseNormalOperation();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> TransferDriverAsync(
        DeviceId peerDeviceId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
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

            DateTimeOffset now = clock.UtcNow;
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
            ReleaseNormalOperation();
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
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
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

            DateTimeOffset authorizationNow = clock.UtcNow;
            long inputSessionGeneration;
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

                RemoteInputDecision decision = EvaluateRemoteInputUnderLock(
                    mirrorSession,
                    peerDeviceId,
                    driverLeaseEpoch,
                    authorizationNow);
                if (decision != RemoteInputDecision.Allowed)
                {
                    return new RemoteInputAttemptResult(decision, CreateSnapshot());
                }

                inputSessionGeneration = sessionGeneration;
            }

            LocalBoundaryResult boundary;
            NativeRemoteWindowSourceUse? sourceUse = null;
            if (nativeSourceLease is not null
                && !TryCreateNativeSourceUse(inputSessionGeneration, out sourceUse))
            {
                boundary = LocalBoundaryResult.Failed("native_source_stale");
            }
            else
            {
                try
                {
                    boundary = await InjectInputBoundaryAsync(
                            sourceUse,
                            batch,
                            cancellationToken)
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
            }

            if (boundary.Succeeded
                && sourceUse is not null
                && !IsNativeSourceUseCurrent(
                    sourceUse,
                    requireGeometryRevision: true,
                    out string staleReasonCode))
            {
                boundary = LocalBoundaryResult.Failed(staleReasonCode);
            }

            DateTimeOffset postBoundaryNow = clock.UtcNow;
            lock (stateLock)
            {
                if (mirrorSession is not null)
                {
                    RemoteInputDecision postBoundaryDecision =
                        EvaluateRemoteInputUnderLock(
                            mirrorSession,
                            peerDeviceId,
                            driverLeaseEpoch,
                            postBoundaryNow);
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
            ReleaseNormalOperation();
        }
    }

    private RemoteInputDecision EvaluateRemoteInputUnderLock(
        MirrorSession session,
        DeviceId peerDeviceId,
        long driverLeaseEpoch,
        DateTimeOffset now)
    {
        if (protectionAdmissionClosed
            && (protectionAdmissionEpoch != protectionAdmissionAppliedEpoch
                || IsFreshSafe(protection, now)))
        {
            return RemoteInputDecision.ProtectionStateUnknown;
        }

        return remoteInputPolicy.Evaluate(
            session,
            peerDeviceId,
            driverLeaseEpoch,
            protection,
            now);
    }

    public async ValueTask<RemoteWindowCommandResult> ReconcilePeerCapabilitiesAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
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

            DateTimeOffset now = clock.UtcNow;
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
                    if (!pendingPeerDisconnects.Contains(peerDeviceId))
                    {
                        return Result(
                            RemoteWindowCommandStatus.AlreadyApplied,
                            "peer_not_participant");
                    }
                }
                else if (!canView)
                {
                    mirrorSession = mirrorSession.RemoveParticipant(
                        peerDeviceId,
                        now,
                        ownerLeaseDuration);
                    _ = pendingPeerDisconnects.Add(peerDeviceId);
                    revision = checked(revision + 1);
                }
                else if (!canDrive
                    && currentRole == MirrorParticipantRole.DriverEligible)
                {
                    if (mirrorSession.DriverLease.HolderDeviceId == peerDeviceId)
                    {
                        mirrorSession = mirrorSession.TransferDriver(
                            hostDeviceId,
                            now,
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
                if (disconnect.Succeeded)
                {
                    _ = pendingPeerDisconnects.Remove(peerDeviceId);
                }

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
            ReleaseNormalOperation();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> DisconnectParticipantAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = clock.UtcNow;
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
                    if (!pendingPeerDisconnects.Contains(peerDeviceId))
                    {
                        return Result(
                            RemoteWindowCommandStatus.AlreadyApplied,
                            "peer_not_participant");
                    }
                }
                else
                {
                    mirrorSession = mirrorSession.RemoveParticipant(
                        peerDeviceId,
                        now,
                        ownerLeaseDuration);
                    _ = pendingPeerDisconnects.Add(peerDeviceId);
                    revision = checked(revision + 1);
                }
            }

            LocalBoundaryResult disconnect =
                CallBoundary(() => sessions.DisconnectPeerNow(peerDeviceId));
            lock (stateLock)
            {
                if (disconnect.Succeeded)
                {
                    _ = pendingPeerDisconnects.Remove(peerDeviceId);
                }

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
            ReleaseNormalOperation();
        }
    }

    public async ValueTask<RemoteWindowCommandResult> RefreshExpiredLeaseAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = clock.UtcNow;
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
                    now,
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
            ReleaseNormalOperation();
        }
    }

    public RemoteWindowProtectionResult ApplyProtectionSnapshot(
        ProtectionSnapshot observation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(observation);
        return ApplyProtectionSnapshotCore(
            observation,
            CloseProtectionAdmissionNow());
    }

    internal RemoteWindowProtectionResult ApplyProtectionSnapshot(
        ProtectionSnapshot observation,
        long protectionAdmissionEpoch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(observation);
        return ApplyProtectionSnapshotCore(
            observation,
            protectionAdmissionEpoch);
    }

    internal long CloseProtectionAdmissionNow()
    {
        lock (stateLock)
        {
            protectionAdmissionClosed = true;
            protectionAdmissionEpoch++;
            pendingProtectionAdmissionOpenEpoch = null;
            pendingProtectionAdmissionOpenObservation = null;
            return protectionAdmissionEpoch;
        }
    }

    internal int ActiveProtectionAdmissionUseCount
    {
        get
        {
            lock (stateLock)
            {
                return activeProtectionAdmissionUses;
            }
        }
    }

    internal int ProtectionAdmissionDrainWaiterCount
    {
        get
        {
            lock (stateLock)
            {
                return protectionAdmissionDrainWaiters;
            }
        }
    }

    private ProtectionAdmissionUse? TryAcquireProtectionAdmissionUse()
    {
        lock (stateLock)
        {
            if (protectionAdmissionClosed
                || Volatile.Read(ref disposed) != 0)
            {
                return null;
            }

            activeProtectionAdmissionUses = checked(
                activeProtectionAdmissionUses + 1);
            NativeRemoteWindowDrainActivityScope activityScope =
                NativeRemoteWindowDrainActivityScope.Enter(
                    ProtectionAdmissionActivityOwner,
                    new object());
            return new ProtectionAdmissionUse(this, activityScope);
        }
    }

    private void ReleaseProtectionAdmissionUse()
    {
        lock (stateLock)
        {
            activeProtectionAdmissionUses--;
            if (activeProtectionAdmissionUses == 0)
            {
                TryCompletePendingProtectionAdmissionOpenUnderLock();
            }

            Monitor.PulseAll(stateLock);
        }
    }

    private void WaitForProtectionAdmissionUseDrain()
    {
        lock (stateLock)
        {
            while (activeProtectionAdmissionUses != 0)
            {
                if (NativeRemoteWindowDrainActivityScope.IsActiveForOwner(
                        ProtectionAdmissionActivityOwner))
                {
                    return;
                }

                protectionAdmissionDrainWaiters++;
                try
                {
                    Monitor.Wait(stateLock);
                }
                finally
                {
                    protectionAdmissionDrainWaiters--;
                }
            }
        }
    }

    private RemoteWindowProtectionResult ApplyProtectionSnapshotCore(
        ProtectionSnapshot observation,
        long acceptedProtectionAdmissionEpoch)
    {
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        int currentThreadId = Environment.CurrentManagedThreadId;
        long expectedSessionGeneration;
        bool ownsReconciliation;
        lock (stateLock)
        {
            if (!AllowsProtectionReconciliation(lifecycle)
                || mirrorSession is null)
            {
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.InvalidState,
                    blocked: true,
                    pauseReason: null,
                    CreateSnapshot());
            }

            expectedSessionGeneration = sessionGeneration;
            ownsReconciliation = protectionBoundaryThreads.Add(currentThreadId);
            protection = observation;
            protectionAdmissionAppliedEpoch =
                acceptedProtectionAdmissionEpoch;
            protectionRevision = checked(protectionRevision + 1);
        }

        try
        {
            DateTimeOffset now = clock.UtcNow;
            RemoteWindowProtectionResult? immediateResult = null;
            lock (stateLock)
            {
                if (!ownsReconciliation)
                {
                    return ApplyReentrantProtectionObservation(
                        observation,
                        now,
                        expectedSessionGeneration);
                }

                MirrorPauseReason? pauseReason =
                    ClassifyProtection(observation, now);
                if (!IsCurrentProtectionSession(expectedSessionGeneration))
                {
                    immediateResult = new RemoteWindowProtectionResult(
                        RemoteWindowCommandStatus.InvalidState,
                        blocked: true,
                        pauseReason,
                        CreateSnapshot());
                }
                else
                {
                    bool resuming = pauseReason is null
                        && lifecycle == RemoteWindowLifecycle.ProtectionPaused;
                    if (pauseReason is null && !resuming)
                    {
                        revision = checked(revision + 1);
                        immediateResult = new RemoteWindowProtectionResult(
                            RemoteWindowCommandStatus.AlreadyApplied,
                            blocked: false,
                            pauseReason: null,
                            CreateSnapshot());
                    }
                    else if (pauseReason is not null)
                    {
                        lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                        captureState = RemoteWindowCaptureState.Unconfirmed;
                        revision = checked(revision + 1);
                    }
                }
            }

            RemoteWindowProtectionResult result = immediateResult
                ?? ConvergeProtectionGatesCore(
                    activateStartingWithoutResume: false,
                    expectedSessionGeneration: expectedSessionGeneration);
            WaitForProtectionAdmissionUseDrain();
            TryOpenProtectionAdmission(result);
            return result;
        }
        finally
        {
            if (ownsReconciliation)
            {
                lock (stateLock)
                {
                    _ = protectionBoundaryThreads.Remove(currentThreadId);
                }
            }
        }
    }

    private void TryOpenProtectionAdmission(RemoteWindowProtectionResult result)
    {
        lock (stateLock)
        {
            if (!CanOpenProtectionAdmissionUnderLock(result))
            {
                return;
            }

            if (activeProtectionAdmissionUses == 0)
            {
                protectionAdmissionClosed = false;
                pendingProtectionAdmissionOpenEpoch = null;
                pendingProtectionAdmissionOpenObservation = null;
                return;
            }

            pendingProtectionAdmissionOpenEpoch = protectionAdmissionEpoch;
            pendingProtectionAdmissionOpenObservation = protection;
        }
    }

    private bool CanOpenProtectionAdmissionUnderLock(
        RemoteWindowProtectionResult result) =>
        protectionAdmissionEpoch == protectionAdmissionAppliedEpoch
        && protectionAdmissionClosed
        && result.Status is RemoteWindowCommandStatus.Applied
            or RemoteWindowCommandStatus.AlreadyApplied
        && !result.Blocked
        && lifecycle == RemoteWindowLifecycle.Active
        && captureState == RemoteWindowCaptureState.Capturing;

    private void TryCompletePendingProtectionAdmissionOpenUnderLock()
    {
        if (activeProtectionAdmissionUses != 0
            || pendingProtectionAdmissionOpenEpoch is not { } pendingEpoch
            || pendingEpoch != protectionAdmissionEpoch
            || pendingEpoch != protectionAdmissionAppliedEpoch
            || pendingProtectionAdmissionOpenObservation is not { } pending
            || !ReferenceEquals(protection, pending)
            || lifecycle != RemoteWindowLifecycle.Active
            || captureState != RemoteWindowCaptureState.Capturing)
        {
            return;
        }

        protectionAdmissionClosed = false;
        pendingProtectionAdmissionOpenEpoch = null;
        pendingProtectionAdmissionOpenObservation = null;
    }

    private RemoteWindowProtectionResult ApplyReentrantProtectionObservation(
        ProtectionSnapshot observation,
        DateTimeOffset now,
        long expectedSessionGeneration)
    {
        MirrorPauseReason? pauseReason =
            ClassifyProtection(observation, now);
        if (!IsCurrentProtectionSession(expectedSessionGeneration))
        {
            return new RemoteWindowProtectionResult(
                RemoteWindowCommandStatus.InvalidState,
                blocked: true,
                pauseReason,
                CreateSnapshot());
        }

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

        LocalBoundaryResult deferred = LocalBoundaryResult.Failed(
            "protection_reconciliation_in_progress");
        return new RemoteWindowProtectionResult(
            RemoteWindowCommandStatus.BoundaryFailed,
            blocked: true,
            pauseReason,
            CreateSnapshot(),
            deferred,
            deferred);
    }

    private RemoteWindowProtectionResult ConvergeProtectionGates(
        bool activateStartingWithoutResume = false)
    {
        DateTimeOffset now = clock.UtcNow;
        int currentThreadId = Environment.CurrentManagedThreadId;
        long expectedSessionGeneration;
        lock (stateLock)
        {
            if (!AllowsProtectionReconciliation(lifecycle))
            {
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.InvalidState,
                    blocked: true,
                    pauseReason: null,
                    CreateSnapshot());
            }

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
                    ClassifyProtection(protection, now),
                    CreateSnapshot(),
                    deferred,
                    deferred);
            }

            expectedSessionGeneration = sessionGeneration;
        }

        try
        {
            return ConvergeProtectionGatesCore(
                activateStartingWithoutResume,
                expectedSessionGeneration);
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
        bool activateStartingWithoutResume,
        long expectedSessionGeneration)
    {
        LocalBoundaryResult? captureBoundary = null;
        LocalBoundaryResult? inputBoundary = null;
        for (int attempt = 0;
             attempt < MaximumProtectionConvergenceAttempts;
             attempt++)
        {
            DateTimeOffset now = clock.UtcNow;
            long expectedProtectionRevision;
            MirrorPauseReason? pauseReason;
            bool terminalAtAttempt;
            lock (stateLock)
            {
                terminalAtAttempt =
                    !IsCurrentProtectionSession(expectedSessionGeneration);
                if (!terminalAtAttempt)
                {
                    expectedProtectionRevision = protectionRevision;
                    pauseReason = ClassifyProtection(protection, now);
                    terminalAtAttempt =
                        !IsCurrentProtectionSession(expectedSessionGeneration);
                    if (!terminalAtAttempt
                        && pauseReason is null
                        && lifecycle == RemoteWindowLifecycle.ProtectionPaused
                        && !IsCaptureAdmissionConfirmed(expectedSessionGeneration))
                    {
                        return new RemoteWindowProtectionResult(
                            RemoteWindowCommandStatus.ProtectionBlocked,
                            blocked: true,
                            pauseReason: null,
                            CreateSnapshot());
                    }

                    if (!terminalAtAttempt
                        && pauseReason is null
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

                    if (!terminalAtAttempt && pauseReason is not null)
                    {
                        lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                        captureState = RemoteWindowCaptureState.Unconfirmed;
                        revision = checked(revision + 1);
                    }
                }
                else
                {
                    expectedProtectionRevision = 0;
                    pauseReason = null;
                }
            }
            if (terminalAtAttempt)
            {
                return ReassertTerminalAfterProtectionBoundary(
                    pauseReason,
                    captureBoundary,
                    inputBoundary,
                    expectedSessionGeneration);
            }

            bool resuming = pauseReason is null;
            captureBoundary = resuming
                ? CallBoundary(capture.ResumeNow)
                : CallBoundary(() => capture.PauseNow(pauseReason!.Value));
            if (HasEmergencyStopped(expectedSessionGeneration))
            {
                return ReassertEmergencyAfterProtectionBoundary(
                    pauseReason,
                    expectedSessionGeneration);
            }
            if (HasTerminalProtectionLifecycle(expectedSessionGeneration))
            {
                return ReassertTerminalAfterProtectionBoundary(
                    pauseReason,
                    captureBoundary,
                    inputBoundary,
                    expectedSessionGeneration);
            }

            inputBoundary = resuming
                ? CallBoundary(input.ResumeNow)
                : CallBoundary(() => input.PauseNow(pauseReason!.Value));
            if (HasEmergencyStopped(expectedSessionGeneration))
            {
                return ReassertEmergencyAfterProtectionBoundary(
                    pauseReason,
                    expectedSessionGeneration);
            }
            if (HasTerminalProtectionLifecycle(expectedSessionGeneration))
            {
                return ReassertTerminalAfterProtectionBoundary(
                    pauseReason,
                    captureBoundary,
                    inputBoundary,
                    expectedSessionGeneration);
            }

            if (resuming
                && (!captureBoundary.Succeeded || !inputBoundary.Succeeded))
            {
                const MirrorPauseReason failedResume =
                    MirrorPauseReason.ProtectionStateUnknown;
                _ = CallBoundary(() => capture.PauseNow(failedResume));
                if (HasEmergencyStopped(expectedSessionGeneration))
                {
                    return ReassertEmergencyAfterProtectionBoundary(
                        pauseReason,
                        expectedSessionGeneration);
                }
                if (HasTerminalProtectionLifecycle(expectedSessionGeneration))
                {
                    return ReassertTerminalAfterProtectionBoundary(
                        pauseReason,
                        captureBoundary,
                        inputBoundary,
                        expectedSessionGeneration);
                }

                _ = CallBoundary(() => input.PauseNow(failedResume));
                if (HasEmergencyStopped(expectedSessionGeneration))
                {
                    return ReassertEmergencyAfterProtectionBoundary(
                        pauseReason,
                        expectedSessionGeneration);
                }
                if (HasTerminalProtectionLifecycle(expectedSessionGeneration))
                {
                    return ReassertTerminalAfterProtectionBoundary(
                        pauseReason,
                        captureBoundary,
                        inputBoundary,
                        expectedSessionGeneration);
                }
            }

            lock (stateLock)
            {
                if (!IsCurrentProtectionSession(expectedSessionGeneration))
                {
                    // Re-close outside the state lock after a terminal transition.
                }
                else if (expectedProtectionRevision != protectionRevision)
                {
                    continue;
                }
                else
                {
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

            return ReassertTerminalAfterProtectionBoundary(
                pauseReason,
                captureBoundary,
                inputBoundary,
                expectedSessionGeneration);
        }

        const MirrorPauseReason convergenceFailure =
            MirrorPauseReason.ProtectionStateUnknown;
        bool terminalAtExhaustion;
        lock (stateLock)
        {
            terminalAtExhaustion =
                !IsCurrentProtectionSession(expectedSessionGeneration);
            if (!terminalAtExhaustion)
            {
                lifecycle = RemoteWindowLifecycle.ProtectionPaused;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }
        }
        if (terminalAtExhaustion)
        {
            return ReassertTerminalAfterProtectionBoundary(
                convergenceFailure,
                captureBoundary,
                inputBoundary,
                expectedSessionGeneration);
        }

        captureBoundary =
            CallBoundary(() => capture.PauseNow(convergenceFailure));
        inputBoundary =
            CallBoundary(() => input.PauseNow(convergenceFailure));
        lock (stateLock)
        {
            if (IsCurrentProtectionSession(expectedSessionGeneration))
            {
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.BoundaryFailed,
                    blocked: true,
                    convergenceFailure,
                    CreateSnapshot(),
                    captureBoundary,
                    inputBoundary);
            }
        }

        return ReassertTerminalAfterProtectionBoundary(
            convergenceFailure,
            captureBoundary,
            inputBoundary,
            expectedSessionGeneration);
    }

    private bool HasEmergencyStopped(long expectedSessionGeneration)
    {
        lock (stateLock)
        {
            return sessionGeneration == expectedSessionGeneration
                && lifecycle == RemoteWindowLifecycle.EmergencyStopped;
        }
    }

    private bool HasTerminalProtectionLifecycle(long expectedSessionGeneration)
    {
        lock (stateLock)
        {
            return !IsCurrentProtectionSession(expectedSessionGeneration);
        }
    }

    private RemoteWindowProtectionResult ReassertTerminalAfterProtectionBoundary(
        MirrorPauseReason? pauseReason,
        LocalBoundaryResult? captureBoundary,
        LocalBoundaryResult? inputBoundary,
        long expectedSessionGeneration)
    {
        RemoteWindowLifecycle terminalLifecycle;
        long terminalProtectionRevision;
        lock (stateLock)
        {
            if (sessionGeneration != expectedSessionGeneration)
            {
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.InvalidState,
                    blocked: true,
                    pauseReason,
                    CreateSnapshot(),
                    captureBoundary,
                    inputBoundary);
            }

            if (AllowsProtectionReconciliation(lifecycle))
            {
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.InvalidState,
                    blocked: true,
                    pauseReason,
                    CreateSnapshot(),
                    captureBoundary,
                    inputBoundary);
            }

            terminalLifecycle = lifecycle;
            terminalProtectionRevision = protectionRevision;
        }
        if (terminalLifecycle == RemoteWindowLifecycle.EmergencyStopped)
        {
            return ReassertEmergencyAfterProtectionBoundary(
                pauseReason,
                expectedSessionGeneration);
        }

        CloseNativeFrameSinkNow();
        LocalBoundaryResult captureStop = CallBoundary(capture.StopNow);
        LocalBoundaryResult inputStop = CallBoundary(input.StopNow);
        LocalBoundaryResult sessionStop = CallBoundary(sessions.DisconnectAllNow);
        DisposeNativeFrameSink();
        lock (stateLock)
        {
            _ = protectionBoundaryThreads.Remove(
                Environment.CurrentManagedThreadId);
            bool sameTerminalGeneration = lifecycle == terminalLifecycle
                && sessionGeneration == expectedSessionGeneration
                && protectionRevision == terminalProtectionRevision
                && !AllowsProtectionReconciliation(lifecycle);
            bool isLastReconciliation = protectionBoundaryThreads.Count == 0;
            if (sameTerminalGeneration)
            {
                bool fullyStopped = captureStop.Succeeded
                    && inputStop.Succeeded
                    && sessionStop.Succeeded
                    && isLastReconciliation;
                captureState = captureStop.Succeeded && isLastReconciliation
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
                terminalStopConfirmed = fullyStopped;
                if (fullyStopped)
                {
                    mirrorSession = null;
                }

                revision = checked(revision + 1);
            }

            return new RemoteWindowProtectionResult(
                captureStop.Succeeded
                    && inputStop.Succeeded
                    && sessionStop.Succeeded
                    ? RemoteWindowCommandStatus.InvalidState
                    : RemoteWindowCommandStatus.BoundaryFailed,
                blocked: true,
                pauseReason,
                CreateSnapshot(),
                captureStop,
                inputStop,
                sessionStop);
        }
    }

    private RemoteWindowProtectionResult ReassertEmergencyAfterProtectionBoundary(
        MirrorPauseReason? pauseReason,
        long expectedSessionGeneration)
    {
        lock (stateLock)
        {
            if (sessionGeneration != expectedSessionGeneration
                || lifecycle != RemoteWindowLifecycle.EmergencyStopped)
            {
                return new RemoteWindowProtectionResult(
                    RemoteWindowCommandStatus.InvalidState,
                    blocked: true,
                    pauseReason,
                    CreateSnapshot());
            }

            captureEmergencyConfirmed = false;
            inputEmergencyConfirmed = false;
            captureState = RemoteWindowCaptureState.Unconfirmed;
            revision = checked(revision + 1);
        }

        RemoteWindowEmergencyStopResult reasserted = EmergencyStop();
        lock (stateLock)
        {
            _ = protectionBoundaryThreads.Remove(
                Environment.CurrentManagedThreadId);
            bool isLastReconciliation = protectionBoundaryThreads.Count == 0;
            if (isLastReconciliation)
            {
                captureEmergencyConfirmed =
                    reasserted.CaptureBoundary.Succeeded;
                inputEmergencyConfirmed =
                    reasserted.InputBoundary.Succeeded;
                captureState = reasserted.CaptureBoundary.Succeeded
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
            }
            else
            {
                captureEmergencyConfirmed = false;
                inputEmergencyConfirmed = false;
                captureState = RemoteWindowCaptureState.Unconfirmed;
            }

            revision = checked(revision + 1);
            return new RemoteWindowProtectionResult(
                reasserted.CaptureBoundary.Succeeded
                    && reasserted.InputBoundary.Succeeded
                    && reasserted.SessionBoundary.Succeeded
                    ? RemoteWindowCommandStatus.InvalidState
                    : RemoteWindowCommandStatus.BoundaryFailed,
                blocked: true,
                pauseReason,
                CreateSnapshot(),
                reasserted.CaptureBoundary,
                reasserted.InputBoundary,
                reasserted.SessionBoundary);
        }
    }

    public RemoteWindowEmergencyStopResult EmergencyStop()
    {
        EmergencyStopCallScope? inheritedScope = emergencyStopCallScope.Value;
        if (inheritedScope?.IsActive == true)
        {
            return CreateReentrantEmergencyStopResult();
        }

        using LifetimeOperationLease lifetimeOperation =
            EnterLifetimeOperation(allowNestedAfterDisposal: true);
        return InvokeEmergencyStopWithCallScope();
    }

    private RemoteWindowEmergencyStopResult InvokeEmergencyStopWithCallScope()
    {
        EmergencyStopCallScope? inheritedScope = emergencyStopCallScope.Value;
        if (inheritedScope?.IsActive == true)
        {
            return CreateReentrantEmergencyStopResult();
        }

        var currentScope = new EmergencyStopCallScope();
        emergencyStopCallScope.Value = currentScope;
        try
        {
            return EmergencyStopCore();
        }
        finally
        {
            currentScope.Deactivate();
            emergencyStopCallScope.Value = inheritedScope;
        }
    }

    private RemoteWindowEmergencyStopResult CreateReentrantEmergencyStopResult()
    {
        LocalBoundaryResult unconfirmed =
            LocalBoundaryResult.Failed("emergency_stop_reentrant");
        lock (stateLock)
        {
            return new RemoteWindowEmergencyStopResult(
                CreateSnapshot(),
                unconfirmed,
                unconfirmed,
                unconfirmed);
        }
    }

    private RemoteWindowEmergencyStopResult EmergencyStopCore()
    {
        DateTimeOffset now = ReadClockOrFailCloseTimestamp();
        long attemptSessionGeneration;
        long attemptStopGeneration;
        bool attemptingUnavailable;
        lock (stateLock)
        {
            emergencyStop.Activate();
            attemptingUnavailable = lifecycle == RemoteWindowLifecycle.Unavailable;
            if (attemptingUnavailable)
            {
                if (emergencyStopAttemptsByGeneration.GetValueOrDefault(
                        emergencyStopGeneration) == 0)
                {
                    emergencyStopGeneration = checked(emergencyStopGeneration + 1);
                    emergencyStopSessionGeneration = sessionGeneration;
                    emergencyConfirmationGeneration = emergencyStopGeneration;
                    captureEmergencyConfirmed = false;
                    inputEmergencyConfirmed = false;
                    sessionEmergencyConfirmed = false;
                }
            }
            else if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
            {
                emergencyStopGeneration = checked(emergencyStopGeneration + 1);
                emergencyStopSessionGeneration = sessionGeneration;
                emergencyConfirmationGeneration = emergencyStopGeneration;
                protectionRevision = checked(protectionRevision + 1);
                captureEmergencyConfirmed = false;
                inputEmergencyConfirmed = false;
                sessionEmergencyConfirmed = false;
                if (mirrorSession?.Status == MirrorSessionStatus.Active)
                {
                    mirrorSession = mirrorSession.EmergencyStop(now);
                }

                lifecycle = RemoteWindowLifecycle.EmergencyStopped;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }

            attemptSessionGeneration = sessionGeneration;
            attemptStopGeneration = emergencyStopGeneration;
            emergencyStopAttemptsByGeneration[attemptStopGeneration] =
                emergencyStopAttemptsByGeneration.GetValueOrDefault(
                    attemptStopGeneration) + 1;
        }

        CloseNativeFrameSinkNow();
        LocalBoundaryResult captureBoundary =
            CallBoundary(capture.EmergencyStopNow);
        LocalBoundaryResult inputBoundary =
            CallBoundary(input.EmergencyStopNow);
        LocalBoundaryResult sessionBoundary =
            CallBoundary(sessions.DisconnectAllNow);

        lock (stateLock)
        {
            LocalBoundaryResult resultCaptureBoundary = captureBoundary;
            LocalBoundaryResult resultInputBoundary = inputBoundary;
            LocalBoundaryResult resultSessionBoundary = sessionBoundary;
            int remainingAttempts = emergencyStopAttemptsByGeneration[
                attemptStopGeneration] - 1;
            if (remainingAttempts == 0)
            {
                _ = emergencyStopAttemptsByGeneration.Remove(
                    attemptStopGeneration);
            }
            else
            {
                emergencyStopAttemptsByGeneration[attemptStopGeneration] =
                    remainingAttempts;
            }

            bool belongsToCurrentStop =
                lifecycle == RemoteWindowLifecycle.EmergencyStopped
                && sessionGeneration == attemptSessionGeneration
                && emergencyStopSessionGeneration == attemptSessionGeneration
                && emergencyStopGeneration == attemptStopGeneration
                && emergencyConfirmationGeneration == attemptStopGeneration;
            if (belongsToCurrentStop)
            {
                captureEmergencyConfirmed |= captureBoundary.Succeeded;
                inputEmergencyConfirmed |= inputBoundary.Succeeded;
                sessionEmergencyConfirmed |= sessionBoundary.Succeeded;
                resultCaptureBoundary = ProjectEmergencyConfirmation(
                    captureBoundary,
                    captureEmergencyConfirmed,
                    "capture_emergency_already_confirmed");
                resultInputBoundary = ProjectEmergencyConfirmation(
                    inputBoundary,
                    inputEmergencyConfirmed,
                    "input_emergency_already_confirmed");
                resultSessionBoundary = ProjectEmergencyConfirmation(
                    sessionBoundary,
                    sessionEmergencyConfirmed,
                    "sessions_emergency_already_confirmed");
                captureState = captureEmergencyConfirmed
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }
            else if (attemptingUnavailable
                && lifecycle == RemoteWindowLifecycle.Unavailable
                && sessionGeneration == attemptSessionGeneration
                && emergencyStopSessionGeneration == attemptSessionGeneration
                && emergencyStopGeneration == attemptStopGeneration
                && emergencyConfirmationGeneration == attemptStopGeneration)
            {
                captureEmergencyConfirmed |= captureBoundary.Succeeded;
                inputEmergencyConfirmed |= inputBoundary.Succeeded;
                sessionEmergencyConfirmed |= sessionBoundary.Succeeded;
                resultCaptureBoundary = ProjectEmergencyConfirmation(
                    captureBoundary,
                    captureEmergencyConfirmed,
                    "capture_emergency_already_confirmed");
                resultInputBoundary = ProjectEmergencyConfirmation(
                    inputBoundary,
                    inputEmergencyConfirmed,
                    "input_emergency_already_confirmed");
                resultSessionBoundary = ProjectEmergencyConfirmation(
                    sessionBoundary,
                    sessionEmergencyConfirmed,
                    "sessions_emergency_already_confirmed");
                terminalStopConfirmed = captureEmergencyConfirmed
                    && inputEmergencyConfirmed
                    && sessionEmergencyConfirmed;
                captureState = captureEmergencyConfirmed
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
                if (terminalStopConfirmed)
                {
                    mirrorSession = null;
                }

                revision = checked(revision + 1);
            }

            return new RemoteWindowEmergencyStopResult(
                CreateSnapshot(),
                resultCaptureBoundary,
                resultInputBoundary,
                resultSessionBoundary);
        }
    }

    public async ValueTask<RemoteWindowCommandResult> ResetAfterLocalConfirmationAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool resettingUnavailable;
            long expectedEmergencyStopGeneration;
            long expectedSessionGeneration;
            lock (stateLock)
            {
                if (nativeSourceLease is not null
                    && (nativeSourceInvalidated || !nativeSourceLease.IsCurrent))
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "native_source_stale");
                }

                if ((lifecycle is RemoteWindowLifecycle.Unavailable
                    or RemoteWindowLifecycle.EmergencyStopped)
                    && protectionBoundaryThreads.Count > 0)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "protection_reconciliation_in_progress");
                }

                if (lifecycle == RemoteWindowLifecycle.Unavailable)
                {
                    if (emergencyStopAttemptsByGeneration.GetValueOrDefault(
                            emergencyStopGeneration) > 0)
                    {
                        return Result(
                            RemoteWindowCommandStatus.BoundaryFailed,
                            "emergency_stop_in_progress");
                    }

                    if (captureState != RemoteWindowCaptureState.Stopped
                        || mirrorSession?.Participants.Count > 0
                        || mirrorSession?.DriverLease.HolderDeviceId is not null)
                    {
                        return Result(
                            RemoteWindowCommandStatus.BoundaryFailed,
                            "unavailable_stop_unconfirmed");
                    }

                    resettingUnavailable = true;
                }
                else if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
                {
                    return Result(
                        RemoteWindowCommandStatus.InvalidState,
                        "session_not_emergency_stopped");
                }
                else if (emergencyStopAttemptsByGeneration.GetValueOrDefault(
                             emergencyStopGeneration) > 0)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "emergency_stop_in_progress");
                }
                else if (emergencyConfirmationGeneration != emergencyStopGeneration
                    || emergencyStopSessionGeneration != sessionGeneration
                    || !captureEmergencyConfirmed
                    || !inputEmergencyConfirmed
                    || !sessionEmergencyConfirmed)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "emergency_boundaries_unconfirmed");
                }

                resettingUnavailable = lifecycle ==
                    RemoteWindowLifecycle.Unavailable;
                expectedSessionGeneration = sessionGeneration;
                expectedEmergencyStopGeneration = emergencyStopGeneration;
            }

            if (!TryDisposeNativeFrameSinkWithoutWaiting())
            {
                lock (stateLock)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "native_frame_delivery_drain_pending");
                }
            }

            DateTimeOffset now = ReadClockOrFailCloseTimestamp();
            lock (stateLock)
            {
                if (nativeSourceLease is not null
                    && (nativeSourceInvalidated || !nativeSourceLease.IsCurrent))
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "native_source_stale");
                }

                if (sessionGeneration != expectedSessionGeneration
                    || protectionBoundaryThreads.Count > 0)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "reset_state_changed");
                }

                if (resettingUnavailable)
                {
                    if (lifecycle != RemoteWindowLifecycle.Unavailable
                        || captureState != RemoteWindowCaptureState.Stopped
                        || mirrorSession?.Participants.Count > 0
                        || mirrorSession?.DriverLease.HolderDeviceId is not null)
                    {
                        return Result(
                            RemoteWindowCommandStatus.BoundaryFailed,
                            "unavailable_stop_unconfirmed");
                    }
                }
                else if (lifecycle != RemoteWindowLifecycle.EmergencyStopped
                    || emergencyStopGeneration != expectedEmergencyStopGeneration
                    || emergencyStopAttemptsByGeneration.GetValueOrDefault(
                        emergencyStopGeneration) > 0
                    || emergencyConfirmationGeneration != emergencyStopGeneration
                    || emergencyStopSessionGeneration != sessionGeneration
                    || !captureEmergencyConfirmed
                    || !inputEmergencyConfirmed
                    || !sessionEmergencyConfirmed)
                {
                    return Result(
                        RemoteWindowCommandStatus.BoundaryFailed,
                        "emergency_boundaries_unconfirmed");
                }

                if (emergencyStop.IsActive)
                {
                    emergencyStop.ResetAfterLocalConfirmation();
                }

                mirrorSession = null;
                lifecycle = RemoteWindowLifecycle.Idle;
                captureState = RemoteWindowCaptureState.Stopped;
                protection = new ProtectionSnapshot(
                    ProtectionKind.Unknown,
                    now,
                    "not_observed");
                captureEmergencyConfirmed = false;
                inputEmergencyConfirmed = false;
                sessionEmergencyConfirmed = false;
                terminalStopConfirmed = true;
                protectionRevision = checked(protectionRevision + 1);
                revision = checked(revision + 1);
                return Result(
                    RemoteWindowCommandStatus.Applied,
                    resettingUnavailable
                        ? "unavailable_reset_locally"
                        : "emergency_stop_reset_locally");
            }
        }
        finally
        {
            ReleaseNormalOperation();
        }
    }

    public async ValueTask<RemoteWindowStopResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using LifetimeOperationLease lifetimeOperation = EnterLifetimeOperation();
        await AcquireNormalOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = ReadClockOrFailCloseTimestamp();
            bool wasAlreadyStopped;
            lock (stateLock)
            {
                wasAlreadyStopped = lifecycle is RemoteWindowLifecycle.Idle
                    or RemoteWindowLifecycle.Ended
                    or RemoteWindowLifecycle.Unavailable;
                if (lifecycle == RemoteWindowLifecycle.Unavailable)
                {
                    captureState = RemoteWindowCaptureState.Unconfirmed;
                    terminalStopConfirmed = false;
                    revision = checked(revision + 1);
                }
                else if (lifecycle != RemoteWindowLifecycle.EmergencyStopped)
                {
                    if (mirrorSession?.Status == MirrorSessionStatus.Active)
                    {
                        mirrorSession = mirrorSession.End(now);
                    }

                    lifecycle = RemoteWindowLifecycle.Ended;
                    captureState = RemoteWindowCaptureState.Unconfirmed;
                    terminalStopConfirmed = false;
                    revision = checked(revision + 1);
                }
            }

            CloseNativeFrameSinkNow();
            LocalBoundaryResult captureBoundary =
                CallBoundary(capture.StopNow);
            LocalBoundaryResult inputBoundary =
                CallBoundary(input.StopNow);
            LocalBoundaryResult sessionBoundary =
                CallBoundary(sessions.DisconnectAllNow);
            DisposeNativeFrameSink();

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
                terminalStopConfirmed = captureBoundary.Succeeded
                    && inputBoundary.Succeeded
                    && sessionBoundary.Succeeded;
                if (lifecycle == RemoteWindowLifecycle.Unavailable
                    && terminalStopConfirmed)
                {
                    mirrorSession = null;
                }

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
            ReleaseNormalOperation();
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        bool startedDisposal = Interlocked.Exchange(ref disposed, 1) == 0;
        if (startedDisposal)
        {
            RunInitialFailCloseForDisposal();
        }

        if (IsInLifetimeOperationAncestry()
            || IsInDisposalCallAncestry())
        {
            return;
        }

        if (!WaitForLifetimeDrain())
        {
            return;
        }

        FinalizeLifetime();
    }

    private void RunInitialFailCloseForDisposal()
    {
        DisposalCallScope? inheritedScope = disposalCallScope.Value;
        var currentScope = new DisposalCallScope(this);
        disposalCallScope.Value = currentScope;
        using NativeRemoteWindowDrainActivityScope activityScope =
            NativeRemoteWindowDrainActivityScope.Enter(
                this,
                disposalActivityToken);
        try
        {
            FailCloseForDisposal();
        }
        finally
        {
            currentScope.Deactivate();
            disposalCallScope.Value = inheritedScope;
            lock (operationLifetimeLock)
            {
                disposalFailCloseCompleted = true;
                Monitor.PulseAll(operationLifetimeLock);
            }
        }
    }

    private bool WaitForLifetimeDrain()
    {
        lock (operationLifetimeLock)
        {
            if (!disposalFailCloseCompleted || registeredOperations > 0)
            {
                if (NativeRemoteWindowDrainActivityScope.HasActiveAncestry())
                {
                    return false;
                }

                lifetimeDrainWaiters++;
                try
                {
                    while (!disposalFailCloseCompleted
                        || registeredOperations > 0)
                    {
                        Monitor.Wait(operationLifetimeLock);
                    }
                }
                finally
                {
                    lifetimeDrainWaiters--;
                }
            }

            return true;
        }
    }

    private void FailCloseForDisposal()
    {
        DisposalBoundaryAction action;
        lock (stateLock)
        {
            bool emergencyUnconfirmed =
                lifecycle == RemoteWindowLifecycle.EmergencyStopped
                && (!captureEmergencyConfirmed
                    || !inputEmergencyConfirmed
                    || !sessionEmergencyConfirmed)
                && emergencyStopAttemptsByGeneration.GetValueOrDefault(
                    emergencyStopGeneration) == 0;
            action = lifecycle is RemoteWindowLifecycle.Starting
                or RemoteWindowLifecycle.Active
                or RemoteWindowLifecycle.ProtectionPaused
                || emergencyUnconfirmed
                    ? DisposalBoundaryAction.EmergencyStop
                    : lifecycle is RemoteWindowLifecycle.Unavailable
                        or RemoteWindowLifecycle.Ended
                        && !terminalStopConfirmed
                            ? DisposalBoundaryAction.Stop
                            : DisposalBoundaryAction.None;
        }

        if (action == DisposalBoundaryAction.EmergencyStop)
        {
            _ = InvokeEmergencyStopWithCallScope();
        }
        else if (action == DisposalBoundaryAction.Stop)
        {
            RetryTerminalStopBoundariesForDisposal();
        }
    }

    private void RetryTerminalStopBoundariesForDisposal()
    {
        RemoteWindowLifecycle expectedLifecycle;
        long expectedSessionGeneration;
        lock (stateLock)
        {
            expectedLifecycle = lifecycle;
            expectedSessionGeneration = sessionGeneration;
            if (expectedLifecycle is not RemoteWindowLifecycle.Unavailable
                and not RemoteWindowLifecycle.Ended
                || terminalStopConfirmed)
            {
                return;
            }
        }

        CloseNativeFrameSinkNow();
        LocalBoundaryResult captureBoundary = CallBoundary(capture.StopNow);
        LocalBoundaryResult inputBoundary = CallBoundary(input.StopNow);
        LocalBoundaryResult sessionBoundary =
            CallBoundary(sessions.DisconnectAllNow);
        lock (stateLock)
        {
            if (lifecycle != expectedLifecycle
                || sessionGeneration != expectedSessionGeneration)
            {
                return;
            }

            bool fullyStopped = captureBoundary.Succeeded
                && inputBoundary.Succeeded
                && sessionBoundary.Succeeded;
            terminalStopConfirmed = fullyStopped;
            captureState = fullyStopped
                ? RemoteWindowCaptureState.Stopped
                : RemoteWindowCaptureState.Unconfirmed;
            if (fullyStopped)
            {
                mirrorSession = null;
            }

            revision = checked(revision + 1);
        }
    }

    private void FinalizeLifetime()
    {
        bool ownsFinalization;
        lock (operationLifetimeLock)
        {
            while (lifetimeFinalizationState
                == (int)LifetimeFinalizationState.InProgress)
            {
                if (IsInDisposalCallAncestry()
                    || NativeRemoteWindowDrainActivityScope.HasActiveAncestry())
                {
                    return;
                }

                lifetimeFinalizationWaiters++;
                try
                {
                    while (lifetimeFinalizationState
                        == (int)LifetimeFinalizationState.InProgress)
                    {
                        Monitor.Wait(operationLifetimeLock);
                    }
                }
                finally
                {
                    lifetimeFinalizationWaiters--;
                }
            }

            if (lifetimeFinalizationState
                == (int)LifetimeFinalizationState.Completed)
            {
                return;
            }

            ownsFinalization = TryClaimLifetimeFinalizationUnderLock();
        }

        if (!ownsFinalization)
        {
            return;
        }

        RunLifetimeFinalization();
    }

    private bool TryClaimLifetimeFinalizationUnderLock()
    {
        if (lifetimeFinalizationState
                != (int)LifetimeFinalizationState.NotStarted
            || Volatile.Read(ref disposed) == 0
            || !disposalFailCloseCompleted
            || registeredOperations > 0)
        {
            return false;
        }

        lifetimeFinalizationState =
            (int)LifetimeFinalizationState.InProgress;
        return true;
    }

    private void RunLifetimeFinalization()
    {
        DisposalCallScope? inheritedScope = disposalCallScope.Value;
        var currentScope = new DisposalCallScope(this);
        disposalCallScope.Value = currentScope;
        using NativeRemoteWindowDrainActivityScope activityScope =
            NativeRemoteWindowDrainActivityScope.Enter(
                this,
                disposalActivityToken);
        try
        {
            FailCloseForDisposal();
            nativeSourceInvalidationRegistration?.Dispose();
            DisposeNativeFrameSink();
            nativeSourceLease?.Dispose();
            normalOperationGate.Dispose();
        }
        finally
        {
            currentScope.Deactivate();
            disposalCallScope.Value = inheritedScope;
            lock (operationLifetimeLock)
            {
                lifetimeFinalizationState =
                    (int)LifetimeFinalizationState.Completed;
                Monitor.PulseAll(operationLifetimeLock);
            }
        }
    }

    private void OnNativeSourceInvalidated()
    {
        using LifetimeOperationLease? callbackOperation =
            TryEnterRegisteredCallbackOperation();
        if (callbackOperation is null)
        {
            return;
        }

        OnNativeSourceInvalidatedCore();
    }

    private void OnNativeSourceInvalidatedCore()
    {
        long invalidatedSessionGeneration;
        bool stopBoundaries;
        lock (stateLock)
        {
            DateTimeOffset now = GetFailCloseTimestampUnsafe();
            nativeSourceInvalidated = true;
            invalidatedSessionGeneration = sessionGeneration;
            stopBoundaries = lifecycle is RemoteWindowLifecycle.Starting
                or RemoteWindowLifecycle.Active
                or RemoteWindowLifecycle.ProtectionPaused
                or RemoteWindowLifecycle.EmergencyStopped
                || lifecycle is RemoteWindowLifecycle.Unavailable
                    or RemoteWindowLifecycle.Ended
                    && !terminalStopConfirmed;
            protection = new ProtectionSnapshot(
                ProtectionKind.Unknown,
                now,
                "native_source_invalidated");
            protectionRevision = checked(protectionRevision + 1);
            if (mirrorSession?.Status == MirrorSessionStatus.Active)
            {
                mirrorSession = mirrorSession.End(now);
            }

            lifecycle = RemoteWindowLifecycle.Unavailable;
            captureState = stopBoundaries
                ? RemoteWindowCaptureState.Unconfirmed
                : RemoteWindowCaptureState.Stopped;
            terminalStopConfirmed = !stopBoundaries;

            revision = checked(revision + 1);
        }

        CloseNativeFrameSinkNow();
        if (!stopBoundaries)
        {
            DisposeNativeFrameSink();
            return;
        }

        LocalBoundaryResult captureBoundary = CallBoundary(capture.StopNow);
        LocalBoundaryResult inputBoundary = CallBoundary(input.StopNow);
        LocalBoundaryResult sessionBoundary =
            CallBoundary(sessions.DisconnectAllNow);
        DisposeNativeFrameSink();
        lock (stateLock)
        {
            if (lifecycle != RemoteWindowLifecycle.Unavailable
                || sessionGeneration != invalidatedSessionGeneration)
            {
                return;
            }

            bool fullyStopped = captureBoundary.Succeeded
                && inputBoundary.Succeeded
                && sessionBoundary.Succeeded;
            captureState = captureBoundary.Succeeded
                ? RemoteWindowCaptureState.Stopped
                : RemoteWindowCaptureState.Unconfirmed;
            terminalStopConfirmed = fullyStopped;
            if (fullyStopped)
            {
                mirrorSession = null;
            }

            revision = checked(revision + 1);
        }
    }

    private void CloseNativeFrameSinkNow() =>
        Volatile.Read(ref nativeBoundFrameSink)?.CloseNow();

    private void DisposeNativeFrameSink()
    {
        BoundedNativeRemoteWindowFrameSink? frameSink = Interlocked.Exchange(
            ref nativeBoundFrameSink,
            null);
        frameSink?.Dispose();
    }

    private bool TryDisposeNativeFrameSinkWithoutWaiting()
    {
        BoundedNativeRemoteWindowFrameSink? frameSink = Volatile.Read(
            ref nativeBoundFrameSink);
        if (frameSink is null)
        {
            return true;
        }

        if (!frameSink.TryCloseAndConfirmDrained())
        {
            return false;
        }

        if (ReferenceEquals(
                Interlocked.CompareExchange(
                    ref nativeBoundFrameSink,
                    null,
                    frameSink),
                frameSink))
        {
            frameSink.Dispose();
        }

        return Volatile.Read(ref nativeBoundFrameSink) is null;
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

    private async ValueTask AcquireNormalOperationAsync(
        CancellationToken cancellationToken)
    {
        bool gateAcquired = false;
        try
        {
            await normalOperationGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            gateAcquired = true;
            ThrowIfDisposed();
        }
        catch
        {
            if (gateAcquired)
            {
                normalOperationGate.Release();
            }
            throw;
        }
    }

    private void ReleaseNormalOperation() => normalOperationGate.Release();

    private LifetimeOperationLease EnterLifetimeOperation(
        bool allowNestedAfterDisposal = false)
    {
        lock (operationLifetimeLock)
        {
            bool nestedAdmittedOperation = IsInLifetimeOperationAncestry();
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposed) != 0
                && !(allowNestedAfterDisposal && nestedAdmittedOperation),
                this);
            registeredOperations = checked(registeredOperations + 1);
        }

        NativeRemoteWindowDrainActivityScope activityScope =
            NativeRemoteWindowDrainActivityScope.Enter(
                this,
                lifetimeActivityToken);
        return new LifetimeOperationLease(
            this,
            activityScope);
    }

    private LifetimeOperationLease? TryEnterRegisteredCallbackOperation()
    {
        lock (operationLifetimeLock)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return null;
            }

            registeredOperations = checked(registeredOperations + 1);
        }

        NativeRemoteWindowDrainActivityScope activityScope =
            NativeRemoteWindowDrainActivityScope.Enter(
                this,
                lifetimeActivityToken);
        return new LifetimeOperationLease(
            this,
            activityScope);
    }

    private LifetimeOperationLease? TryEnterFrameDeliveryOperation()
    {
        lock (operationLifetimeLock)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return null;
            }

            registeredOperations = checked(registeredOperations + 1);
        }

        NativeRemoteWindowDrainActivityScope activityScope =
            NativeRemoteWindowDrainActivityScope.Enter(
                this,
                lifetimeActivityToken);
        return new LifetimeOperationLease(
            this,
            activityScope);
    }

    private void ExitLifetimeOperation(
        NativeRemoteWindowDrainActivityScope activityScope)
    {
        activityScope.Dispose();

        bool ownsFinalization;
        lock (operationLifetimeLock)
        {
            registeredOperations--;
            ownsFinalization = TryClaimLifetimeFinalizationUnderLock();
            if (registeredOperations == 0)
            {
                Monitor.PulseAll(operationLifetimeLock);
            }
        }

        if (ownsFinalization)
        {
            RunLifetimeFinalization();
        }
    }

    private bool IsInDisposalCallAncestry() =>
        disposalCallScope.Value is
        { IsActive: true, Owner: var owner }
        && ReferenceEquals(owner, this);

    private bool IsInLifetimeOperationAncestry() =>
        NativeRemoteWindowDrainActivityScope.IsActiveFor(
            this,
            lifetimeActivityToken);

    private static bool AllowsViewAndDrive(CapabilityGrant grant) =>
        grant.Allows(Capability.MirrorView)
        && grant.Allows(Capability.MirrorDrive);

    private static bool AllowsProtectionReconciliation(
        RemoteWindowLifecycle currentLifecycle) =>
        currentLifecycle is RemoteWindowLifecycle.Starting
            or RemoteWindowLifecycle.Active
            or RemoteWindowLifecycle.ProtectionPaused;

    private bool IsCurrentProtectionSession(long expectedSessionGeneration) =>
        sessionGeneration == expectedSessionGeneration
        && mirrorSession is not null
        && AllowsProtectionReconciliation(lifecycle);

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

    private DateTimeOffset ReadClockOrFailCloseTimestamp()
    {
        try
        {
            return clock.UtcNow;
        }
        catch (Exception)
        {
            lock (stateLock)
            {
                return GetFailCloseTimestampUnsafe();
            }
        }
    }

    private DateTimeOffset GetFailCloseTimestampUnsafe()
    {
        DateTimeOffset timestamp = protection.ObservedAt;
        if (mirrorSession is not null
            && mirrorSession.DriverLease.IssuedAt > timestamp)
        {
            timestamp = mirrorSession.DriverLease.IssuedAt;
        }

        return timestamp;
    }

    private static LocalBoundaryResult ProjectEmergencyConfirmation(
        LocalBoundaryResult attemptBoundary,
        bool generationConfirmed,
        string alreadyConfirmedReason) =>
        generationConfirmed && !attemptBoundary.Succeeded
            ? LocalBoundaryResult.AlreadyApplied(alreadyConfirmedReason)
            : attemptBoundary;

    private void CompleteCaptureAdmission(
        long admittedSessionGeneration,
        bool admissionConfirmed)
    {
        lock (stateLock)
        {
            if (!captureAdmissionInFlight
                || captureAdmissionSessionGeneration != admittedSessionGeneration)
            {
                return;
            }

            captureAdmissionInFlight = false;
            captureAdmissionConfirmed = admissionConfirmed;
            if (IsCurrentEmergencyStop(admittedSessionGeneration))
            {
                captureEmergencyConfirmed = false;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }
        }
    }

    private bool IsCurrentEmergencyStop(long expectedSessionGeneration) =>
        lifecycle == RemoteWindowLifecycle.EmergencyStopped
        && sessionGeneration == expectedSessionGeneration
        && emergencyStopSessionGeneration == expectedSessionGeneration
        && emergencyConfirmationGeneration == emergencyStopGeneration;

    private bool IsCaptureAdmissionConfirmed(long expectedSessionGeneration) =>
        sessionGeneration == expectedSessionGeneration
        && captureAdmissionSessionGeneration == expectedSessionGeneration
        && !captureAdmissionInFlight
        && captureAdmissionConfirmed;

    private LocalBoundaryResult CleanupFailedStart(
        RemoteWindowLifecycle terminalLifecycle,
        long admittedSessionGeneration)
    {
        DateTimeOffset now = clock.UtcNow;
        lock (stateLock)
        {
            protectionRevision = checked(protectionRevision + 1);
            if (!emergencyStop.IsActive
                && lifecycle != RemoteWindowLifecycle.EmergencyStopped)
            {
                if (mirrorSession?.Status == MirrorSessionStatus.Active)
                {
                    mirrorSession = mirrorSession.End(now);
                }

                lifecycle = terminalLifecycle;
                captureState = RemoteWindowCaptureState.Unconfirmed;
                terminalStopConfirmed = false;
                revision = checked(revision + 1);
            }
        }

        CloseNativeFrameSinkNow();
        LocalBoundaryResult cleanup = CallBoundary(capture.StopNow);
        DisposeNativeFrameSink();
        lock (stateLock)
        {
            if (lifecycle == terminalLifecycle)
            {
                bool hasProtectionReconciliation =
                    protectionBoundaryThreads.Count > 0;
                captureState = cleanup.Succeeded
                    && !hasProtectionReconciliation
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
                if (cleanup.Succeeded && !hasProtectionReconciliation)
                {
                    mirrorSession = null;
                }

                revision = checked(revision + 1);
            }
            else if (IsCurrentEmergencyStop(admittedSessionGeneration))
            {
                captureEmergencyConfirmed |= cleanup.Succeeded;
                captureState = captureEmergencyConfirmed
                    ? RemoteWindowCaptureState.Stopped
                    : RemoteWindowCaptureState.Unconfirmed;
                revision = checked(revision + 1);
            }
        }

        return cleanup;
    }

    private RemoteWindowCommandResult Result(
        RemoteWindowCommandStatus status,
        string reasonCode,
        LocalBoundaryResult? boundary = null,
        LocalBoundaryResult? cleanupBoundary = null) =>
        new(
            status,
            reasonCode,
            CreateSnapshot(),
            boundary,
            cleanupBoundary);

    private async ValueTask<LocalBoundaryResult> StartCaptureAsync(
        NativeRemoteWindowSourceUse? sourceUse,
        CancellationToken cancellationToken)
    {
        if (nativeCapture is not null)
        {
            NativeRemoteWindowSourceUse exactSourceUse = sourceUse
                ?? throw new InvalidOperationException(
                    "A native Remote Window capture requires an exact source use.");
            if (!TryDisposeNativeFrameSinkWithoutWaiting())
            {
                return LocalBoundaryResult.Failed(
                    "native_frame_delivery_drain_pending");
            }

            if (!TryAcquireNativeSourceUseScope(
                    exactSourceUse,
                    requireGeometryRevision: true,
                    out NativeRemoteWindowSourceUseScope? sourceScope,
                    out string reasonCode))
            {
                return LocalBoundaryResult.Failed(reasonCode);
            }

            using (sourceScope)
            {
                var boundFrameSink = new BoundedNativeRemoteWindowFrameSink(
                    exactSourceUse,
                    () => IsNativeFrameSourceUseCurrent(exactSourceUse),
                    () => CanDeliverNativeFrame(exactSourceUse),
                    nativeFrameSink
                        ?? throw new InvalidOperationException(
                            "A native Remote Window capture requires a frame destination."),
                    TryEnterFrameDeliveryOperation,
                    TryAcquireProtectionAdmissionUse,
                    fault => OnNativeFrameSinkFault(exactSourceUse, fault));
                BoundedNativeRemoteWindowFrameSink? previous =
                    Interlocked.Exchange(
                        ref nativeBoundFrameSink,
                        boundFrameSink);
                if (previous is not null)
                {
                    boundFrameSink.CloseNow();
                    previous.Dispose();
                    return LocalBoundaryResult.Failed(
                        "native_frame_sink_conflict");
                }

                return await nativeCapture.StartAsync(
                        exactSourceUse,
                        boundFrameSink,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await semanticCapture!
            .StartAsync(source.ActivityId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<LocalBoundaryResult> InjectInputBoundaryAsync(
        NativeRemoteWindowSourceUse? sourceUse,
        RemoteInputBatch batch,
        CancellationToken cancellationToken)
    {
        if (nativeInput is not null)
        {
            NativeRemoteWindowSourceUse exactSourceUse = sourceUse
                ?? throw new InvalidOperationException(
                    "Native Remote Window input requires an exact source use.");
            if (!TryAcquireNativeSourceUseScope(
                    exactSourceUse,
                    requireGeometryRevision: true,
                    out NativeRemoteWindowSourceUseScope? sourceScope,
                    out string reasonCode))
            {
                return LocalBoundaryResult.Failed(reasonCode);
            }

            using (sourceScope)
            {
                using ProtectionAdmissionUse? protectionUse =
                    TryAcquireProtectionAdmissionUse();
                if (protectionUse is null)
                {
                    return LocalBoundaryResult.Failed(
                        "native_protection_not_safe");
                }

                return await nativeInput.InjectAsync(
                        exactSourceUse,
                        batch,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        using ProtectionAdmissionUse? semanticProtectionUse =
            TryAcquireProtectionAdmissionUse();
        if (semanticProtectionUse is null)
        {
            return LocalBoundaryResult.Failed(
                "native_protection_not_safe");
        }

        return await semanticInput!
            .InjectAsync(batch, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryAcquireNativeSourceUseScope(
        NativeRemoteWindowSourceUse sourceUse,
        bool requireGeometryRevision,
        out NativeRemoteWindowSourceUseScope? sourceScope,
        out string reasonCode)
    {
        if (nativeSourceLease is not null
            && nativeSourceLease.TryAcquireUseScope(
                sourceUse.SourceGeneration,
                requireGeometryRevision
                    ? sourceUse.GeometryRevision
                    : null,
                out sourceScope)
            && sourceScope is not null)
        {
            reasonCode = "native_source_current";
            return true;
        }

        sourceScope = null;
        if (!IsNativeSourceUseCurrent(
                sourceUse,
                requireGeometryRevision,
                out reasonCode))
        {
            return false;
        }

        reasonCode = "native_source_transition";
        return false;
    }

    private bool IsNativeFrameSourceUseCurrent(
        NativeRemoteWindowSourceUse sourceUse)
    {
        if (Volatile.Read(ref disposed) != 0
            || !IsNativeSourceUseCurrent(
                sourceUse,
                requireGeometryRevision: true,
                out _))
        {
            return false;
        }

        lock (stateLock)
        {
            return sessionGeneration == sourceUse.SessionGeneration
                && lifecycle is RemoteWindowLifecycle.Starting
                    or RemoteWindowLifecycle.Active
                    or RemoteWindowLifecycle.ProtectionPaused;
        }
    }

    private bool CanDeliverNativeFrame(
        NativeRemoteWindowSourceUse sourceUse)
    {
        DateTimeOffset now = clock.UtcNow;
        lock (stateLock)
        {
            return sessionGeneration == sourceUse.SessionGeneration
                && lifecycle == RemoteWindowLifecycle.Active
                && captureState == RemoteWindowCaptureState.Capturing
                && !protectionAdmissionClosed
                && IsFreshSafe(protection, now);
        }
    }

    private void OnNativeFrameSinkFault(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrameSinkFault fault)
    {
        lock (stateLock)
        {
            DateTimeOffset now = GetFailCloseTimestampUnsafe();
            if (sessionGeneration != sourceUse.SessionGeneration
                || lifecycle is not RemoteWindowLifecycle.Starting
                    and not RemoteWindowLifecycle.Active
                    and not RemoteWindowLifecycle.ProtectionPaused)
            {
                return;
            }

            if (mirrorSession?.Status == MirrorSessionStatus.Active)
            {
                mirrorSession = mirrorSession.End(now);
            }

            protection = new ProtectionSnapshot(
                ProtectionKind.Unknown,
                now,
                fault switch
                {
                    NativeRemoteWindowFrameSinkFault.SourceBindingLost =>
                        "native_frame_source_binding_lost",
                    NativeRemoteWindowFrameSinkFault
                        .DeliveryPolicyUnavailable =>
                        "native_frame_delivery_policy_unavailable",
                    NativeRemoteWindowFrameSinkFault.DestinationFailed =>
                        "native_frame_destination_failed",
                    _ => "native_frame_sink_failed",
                });
            protectionRevision = checked(protectionRevision + 1);
            lifecycle = RemoteWindowLifecycle.Unavailable;
            captureState = RemoteWindowCaptureState.Unconfirmed;
            terminalStopConfirmed = false;
            revision = checked(revision + 1);
        }

        CloseNativeFrameSinkNow();
        LocalBoundaryResult captureBoundary = CallBoundary(capture.StopNow);
        LocalBoundaryResult inputBoundary = CallBoundary(input.StopNow);
        LocalBoundaryResult sessionBoundary =
            CallBoundary(sessions.DisconnectAllNow);
        DisposeNativeFrameSink();
        lock (stateLock)
        {
            if (sessionGeneration != sourceUse.SessionGeneration
                || lifecycle != RemoteWindowLifecycle.Unavailable)
            {
                return;
            }

            bool fullyStopped = captureBoundary.Succeeded
                && inputBoundary.Succeeded
                && sessionBoundary.Succeeded;
            captureState = captureBoundary.Succeeded
                ? RemoteWindowCaptureState.Stopped
                : RemoteWindowCaptureState.Unconfirmed;
            terminalStopConfirmed = fullyStopped;
            if (fullyStopped)
            {
                mirrorSession = null;
            }

            revision = checked(revision + 1);
        }
    }

    private bool TryCreateNativeSourceUse(
        long admittedSessionGeneration,
        out NativeRemoteWindowSourceUse? sourceUse)
    {
        if (nativeSourceLease is null
            || !nativeSourceLease.TryGetCurrentSnapshot(
                out NativeRemoteWindowSourceSnapshot? snapshot)
            || snapshot is null
            || snapshot.Source.ActivityId != source.ActivityId
            || snapshot.Source.HostDeviceId != source.HostDeviceId
            || snapshot.Source.SourceGeneration != source.SourceGeneration)
        {
            sourceUse = null;
            return false;
        }

        sourceUse = NativeRemoteWindowSourceUse.Create(
            snapshot,
            nativeOwnerGeneration,
            admittedSessionGeneration);
        return true;
    }

    private bool IsNativeSourceUseCurrent(
        NativeRemoteWindowSourceUse sourceUse,
        bool requireGeometryRevision,
        out string reasonCode)
    {
        if (nativeSourceLease is null
            || !nativeSourceLease.TryGetCurrentSnapshot(
                out NativeRemoteWindowSourceSnapshot? snapshot)
            || snapshot is null
            || !sourceUse.Matches(snapshot, requireGeometryRevision: false))
        {
            reasonCode = "native_source_stale";
            return false;
        }

        if (requireGeometryRevision && !sourceUse.Matches(snapshot, true))
        {
            reasonCode = "native_geometry_stale";
            return false;
        }

        reasonCode = "native_source_current";
        return true;
    }

    private RemoteWindowSharingSnapshot CreateSnapshot()
    {
        ImmutableDictionary<DeviceId, MirrorParticipantRole> participants =
            mirrorSession is null
                ? ImmutableDictionary<DeviceId, MirrorParticipantRole>.Empty
                : mirrorSession.Participants.ToImmutableDictionary();
        return new RemoteWindowSharingSnapshot(
            source.ActivityId,
            source.SemanticActivityKind,
            source.DisplayName,
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

    private static RemoteWindowSourceReference CreateSemanticSource(
        DeviceId hostDeviceId,
        ActivityInstance activity)
    {
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Placement.DeviceId != hostDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window controller requires an active Activity on its host Device.",
                nameof(activity));
        }

        return RemoteWindowSourceReference.FromActiveActivity(activity);
    }

    private static RemoteWindowSourceReference GetCurrentNativeSource(
        NativeRemoteWindowSourceLease sourceLease)
    {
        ArgumentNullException.ThrowIfNull(sourceLease);
        if (!sourceLease.TryGetCurrentSnapshot(
                out NativeRemoteWindowSourceSnapshot? snapshot)
            || snapshot is null)
        {
            throw new ArgumentException(
                "A native Remote Window controller requires a current source lease.",
                nameof(sourceLease));
        }

        if (snapshot.Source.IsSemanticActivity)
        {
            throw new ArgumentException(
                "The native Remote Window path requires a generic source without a semantic Activity kind.",
                nameof(sourceLease));
        }

        return snapshot.Source;
    }

    private sealed class EmergencyStopCallScope
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }

    private sealed class DisposalCallScope(RemoteWindowSessionController owner)
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public RemoteWindowSessionController Owner { get; } = owner;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }

    private sealed class LifetimeOperationLease(
        RemoteWindowSessionController owner,
        NativeRemoteWindowDrainActivityScope activityScope) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            owner.ExitLifetimeOperation(activityScope);
        }
    }

    private sealed class ProtectionAdmissionUse(
        RemoteWindowSessionController owner,
        NativeRemoteWindowDrainActivityScope activityScope) : IDisposable
    {
        private RemoteWindowSessionController? owner = owner;

        public void Dispose()
        {
            RemoteWindowSessionController? current = Interlocked.Exchange(
                ref owner,
                null);
            if (current is null)
            {
                return;
            }

            activityScope.Dispose();
            current.ReleaseProtectionAdmissionUse();
        }
    }

    private enum DisposalBoundaryAction
    {
        None,
        EmergencyStop,
        Stop,
    }

    private enum LifetimeFinalizationState
    {
        NotStarted,
        InProgress,
        Completed,
    }
}
