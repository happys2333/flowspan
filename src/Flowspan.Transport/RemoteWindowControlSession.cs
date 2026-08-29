using System.Collections.Concurrent;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public enum RemoteWindowControlDeliveryStatus
{
    Acknowledged,
    NotDelivered,
    AcknowledgementLost,
    ProtocolUnsupported,
}

public sealed record RemoteWindowControlDeliveryResult(
    RemoteWindowControlDeliveryStatus Status,
    RemoteWindowParticipantState? State)
{
    public static RemoteWindowControlDeliveryResult Acknowledged(
        RemoteWindowParticipantState state) => new(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            state ?? throw new ArgumentNullException(nameof(state)));

    public static RemoteWindowControlDeliveryResult NotDelivered { get; } =
        new(RemoteWindowControlDeliveryStatus.NotDelivered, null);

    public static RemoteWindowControlDeliveryResult AcknowledgementLost { get; } =
        new(RemoteWindowControlDeliveryStatus.AcknowledgementLost, null);

    public static RemoteWindowControlDeliveryResult ProtocolUnsupported { get; } =
        new(RemoteWindowControlDeliveryStatus.ProtocolUnsupported, null);
}

public sealed record RemoteWindowPreparationDeliveryResult(
    RemoteWindowControlDeliveryStatus Status,
    RemoteWindowPreparationResponse? Response)
{
    public static RemoteWindowPreparationDeliveryResult Acknowledged(
        RemoteWindowPreparationResponse response) => new(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            response ?? throw new ArgumentNullException(nameof(response)));

    public static RemoteWindowPreparationDeliveryResult NotDelivered { get; } =
        new(RemoteWindowControlDeliveryStatus.NotDelivered, null);

    public static RemoteWindowPreparationDeliveryResult AcknowledgementLost { get; } =
        new(RemoteWindowControlDeliveryStatus.AcknowledgementLost, null);

    public static RemoteWindowPreparationDeliveryResult ProtocolUnsupported { get; } =
        new(RemoteWindowControlDeliveryStatus.ProtocolUnsupported, null);
}

public interface IRemoteWindowControlChannel
{
    public event Action<RemoteWindowParticipantState>? StateChanged;

    public DeviceId HostDeviceId { get; }

    public ValueTask<RemoteWindowControlDeliveryResult> AdmitAsync(
        RemoteWindowAdmissionRequest request,
        CancellationToken cancellationToken);

    public ValueTask<RemoteWindowControlDeliveryResult> RequestDriverAsync(
        RemoteWindowDriverRequest request,
        CancellationToken cancellationToken);

    public ValueTask<RemoteWindowControlDeliveryResult> SendInputAsync(
        RemoteWindowInputRequest request,
        CancellationToken cancellationToken);

    public ValueTask<RemoteWindowControlDeliveryResult> DisconnectAsync(
        RemoteWindowDisconnectRequest request,
        CancellationToken cancellationToken);

    public ValueTask PublishStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken);
}

public interface IRemoteWindowPreparationChannel
{
    public DeviceId ParticipantDeviceId { get; }

    public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken);

    public ValueTask PublishAdmissionStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken);
}

internal interface IRemoteWindowHostPreparationAdmission
{
    public bool TryAdmitRouteSelection(DateTimeOffset now);

    public bool CompleteRouteSelection();

    public bool TryFailRouteSelection();

    public bool TryAdmitPrepareSend(
        RemoteWindowPreparationRequest request,
        DateTimeOffset now);
}

internal interface IReservedRemoteWindowPreparationChannel
{
    public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareReservedAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowHostPreparationAdmission admission,
        CancellationToken cancellationToken);
}

public interface IRemoteWindowPreparationPeer
{
    public DeviceId ParticipantDeviceId { get; }

    public ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken);

    public ValueTask CompletePreparationResponseAsync(
        RemoteWindowPreparationResponse response,
        bool responseCommitted) => ValueTask.CompletedTask;

    public ValueTask CompleteAdmissionAsync(
        RemoteWindowPreparationRequest request,
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken);

    public ValueTask PeerDisconnectedAsync(
        DeviceId hostDeviceId,
        CancellationToken cancellationToken);
}

public interface IRemoteWindowControlPeer
{
    public ActivityId ActivityId { get; }

    public DeviceId HostDeviceId { get; }

    public RemoteWindowSessionId SessionId { get; }

    public ValueTask<RemoteWindowParticipantState> AdmitAsync(
        RemoteWindowAdmissionRequest request,
        CancellationToken cancellationToken);

    public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
        RemoteWindowDriverRequest request,
        CancellationToken cancellationToken);

    public ValueTask<RemoteWindowParticipantState> SendInputAsync(
        RemoteWindowInputRequest request,
        CancellationToken cancellationToken);

    public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
        RemoteWindowDisconnectRequest request,
        CancellationToken cancellationToken);

    public ValueTask PeerDisconnectedAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken);
}

public sealed class RemoteWindowControllerControlPeer : IRemoteWindowControlPeer
{
    private readonly RemoteWindowSessionController controller;

    public RemoteWindowControllerControlPeer(
        RemoteWindowSessionId sessionId,
        RemoteWindowSessionController controller)
    {
        SessionId = sessionId
            ?? throw new ArgumentNullException(nameof(sessionId));
        this.controller = controller
            ?? throw new ArgumentNullException(nameof(controller));
        RemoteWindowSharingSnapshot snapshot = controller.Snapshot;
        ActivityId = snapshot.ActivityId;
        HostDeviceId = snapshot.HostDeviceId;
    }

    public ActivityId ActivityId { get; }

    public DeviceId HostDeviceId { get; }

    public RemoteWindowSessionId SessionId { get; }

    public async ValueTask<RemoteWindowParticipantState> AdmitAsync(
        RemoteWindowAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBinding(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId);
        RemoteWindowCommandResult result = await controller.AddParticipantAsync(
            request.ParticipantDeviceId,
            request.RequestedRole,
            cancellationToken).ConfigureAwait(false);
        return CreateState(
            request.CorrelationId,
            request.ParticipantDeviceId,
            RemoteWindowControlAction.Admission,
            ToOutcome(result.Status),
            result.ReasonCode,
            result.Snapshot);
    }

    public async ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
        RemoteWindowDriverRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBinding(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId);
        RemoteWindowSharingSnapshot before = controller.Snapshot;
        if (before.DriverLeaseEpoch != request.ExpectedEpoch)
        {
            return CreateState(
                request.CorrelationId,
                request.ParticipantDeviceId,
                RemoteWindowControlAction.Driver,
                RemoteWindowControlOutcome.Rejected,
                "driver_epoch_stale",
                before);
        }

        RemoteWindowCommandResult result = await controller.TransferDriverAsync(
            request.ParticipantDeviceId,
            request.LeaseDuration,
            cancellationToken).ConfigureAwait(false);
        return CreateState(
            request.CorrelationId,
            request.ParticipantDeviceId,
            RemoteWindowControlAction.Driver,
            ToOutcome(result.Status),
            result.ReasonCode,
            result.Snapshot);
    }

    public async ValueTask<RemoteWindowParticipantState> SendInputAsync(
        RemoteWindowInputRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBinding(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId);
        RemoteInputAttemptResult result = await controller.InjectInputAsync(
            request.ParticipantDeviceId,
            request.LeaseEpoch,
            request.Batch,
            cancellationToken).ConfigureAwait(false);
        return CreateState(
            request.CorrelationId,
            request.ParticipantDeviceId,
            RemoteWindowControlAction.Input,
            result.Injected
                ? RemoteWindowControlOutcome.Applied
                : RemoteWindowControlOutcome.Rejected,
            ToReasonCode(result.Decision),
            result.Snapshot);
    }

    public async ValueTask<RemoteWindowParticipantState> DisconnectAsync(
        RemoteWindowDisconnectRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBinding(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId);
        RemoteWindowSharingSnapshot before = controller.Snapshot;
        if (request.LastKnownRevision > before.Revision)
        {
            return CreateState(
                request.CorrelationId,
                request.ParticipantDeviceId,
                RemoteWindowControlAction.Disconnect,
                RemoteWindowControlOutcome.Rejected,
                "state_revision_future",
                before);
        }

        RemoteWindowCommandResult result =
            await controller.DisconnectParticipantAsync(
                request.ParticipantDeviceId,
                cancellationToken).ConfigureAwait(false);
        return CreateState(
            request.CorrelationId,
            request.ParticipantDeviceId,
            RemoteWindowControlAction.Disconnect,
            ToOutcome(result.Status),
            result.ReasonCode,
            result.Snapshot);
    }

    public async ValueTask PeerDisconnectedAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        RemoteWindowSharingSnapshot snapshot = controller.Snapshot;
        if (snapshot.Participants.ContainsKey(peerDeviceId))
        {
            _ = await controller.DisconnectParticipantAsync(
                peerDeviceId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void ValidateBinding(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId)
    {
        if (sessionId != SessionId
            || activityId != ActivityId
            || hostDeviceId != HostDeviceId)
        {
            throw new InvalidDataException(
                "The Remote Window command does not match the live controller session.");
        }
    }

    private RemoteWindowParticipantState CreateState(
        CorrelationId correlationId,
        DeviceId participantDeviceId,
        RemoteWindowControlAction action,
        RemoteWindowControlOutcome outcome,
        string reasonCode,
        RemoteWindowSharingSnapshot snapshot)
    {
        snapshot.Participants.TryGetValue(
            participantDeviceId,
            out MirrorParticipantRole effectiveRole);
        MirrorParticipantRole? participantRole =
            snapshot.Participants.ContainsKey(participantDeviceId)
                ? effectiveRole
                : null;
        return RemoteWindowParticipantState.Create(
            correlationId,
            SessionId,
            ActivityId,
            HostDeviceId,
            participantDeviceId,
            action,
            outcome,
            reasonCode,
            snapshot.Lifecycle,
            snapshot.CaptureState,
            snapshot.Participants.Count,
            participantRole,
            snapshot.CurrentDriverDeviceId,
            snapshot.DriverLeaseEpoch,
            snapshot.DriverLeaseExpiresAt?.ToUniversalTime(),
            snapshot.ProtectionKind,
            snapshot.Revision);
    }

    private static RemoteWindowControlOutcome ToOutcome(
        RemoteWindowCommandStatus status) => status switch
        {
            RemoteWindowCommandStatus.Applied => RemoteWindowControlOutcome.Applied,
            RemoteWindowCommandStatus.AlreadyApplied =>
                RemoteWindowControlOutcome.AlreadyApplied,
            _ => RemoteWindowControlOutcome.Rejected,
        };

    private static string ToReasonCode(RemoteInputDecision decision) => decision switch
    {
        RemoteInputDecision.Allowed => "input_injected",
        RemoteInputDecision.EmergencyStopped => "emergency_stopped",
        RemoteInputDecision.SessionInactive => "session_inactive",
        RemoteInputDecision.NotParticipant => "participant_missing",
        RemoteInputDecision.ViewOnly => "participant_view_only",
        RemoteInputDecision.ProtectionStateStale => "protection_state_stale",
        RemoteInputDecision.ProtectionStateUnknown => "protection_state_unknown",
        RemoteInputDecision.SensitiveSurface => "sensitive_surface",
        RemoteInputDecision.DriverLeaseDenied => "driver_lease_denied",
        RemoteInputDecision.CapabilityDenied => "capability_denied",
        RemoteInputDecision.BoundaryFailed => "input_boundary_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };
}

internal interface IRemoteWindowControlConnection
{
    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public ValueTask<ControlMessage> ReadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SendAsync(
        ControlMessage message,
        CancellationToken cancellationToken = default);
}

internal sealed class RemoteWindowControlSession :
    IRemoteWindowControlChannel,
    IRemoteWindowPreparationChannel,
    IReservedRemoteWindowPreparationChannel,
    IAsyncDisposable
{
    [ThreadStatic]
    private static RemoteWindowControlSession? activeLifetimeCancellationOwner;

    public const int MaximumPendingCommands = 16;

    private readonly AsyncLocal<SessionCallScope?> activeLifetimeCancellationCall =
        new();
    private readonly AsyncLocal<SessionCallScope?> activePreparationCall = new();
    private readonly AsyncLocal<SessionCallScope?> activeSendCall = new();
    private readonly AsyncLocal<SessionCallScope?> activeStopDispatchCall = new();
    private readonly IRemoteWindowControlConnection connection;
    private readonly TaskCompletionSource disposalCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<SessionBinding, long> knownBindings = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly TaskCompletionSource lifetimeCancellationDisposalCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object lifetimeCancellationGate = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingState> pending = new();
    private readonly IRemoteWindowControlPeer? peer;
    private readonly TaskCompletionSource peerDisconnectCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object preparationGate = new();
    private readonly IRemoteWindowPreparationPeer? preparationPeer;
    private readonly TaskCompletionSource preparationPeerDisconnectCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object sendAdmissionGate = new();
    private readonly TimeProvider timeProvider;
    private readonly TaskCompletionSource stopDispatchCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int activeSends;
    private int disposed;
    private bool lifetimeCancellationDisposalRequested;
    private bool lifetimeCancellationDisposed;
    private int lifetimeCancellationUsers;
    private int lifetimeStopRequested;
    private int pendingCommandCount;
    private int peerDisconnectStarted;
    private int preparationPeerDisconnectStarted;
    private InboundPreparation? inboundPreparation;
    private OutboundPreparation? outboundPreparation;
    private int running;
    private TaskCompletionSource? sendDrainCompletion;
    private int stopDispatchStarted;
    private int stopped;

    public RemoteWindowControlSession(
        IRemoteWindowControlConnection connection,
        IRemoteWindowControlPeer? peer = null,
        TimeProvider? timeProvider = null,
        IRemoteWindowPreparationPeer? preparationPeer = null)
    {
        this.connection = connection
            ?? throw new ArgumentNullException(nameof(connection));
        this.peer = peer;
        this.preparationPeer = preparationPeer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (peer is not null && peer.HostDeviceId != connection.LocalDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window control peer must represent the authenticated local host.",
                nameof(peer));
        }


        if (preparationPeer is not null
            && preparationPeer.ParticipantDeviceId != connection.LocalDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window preparation peer must represent the authenticated local participant.",
                nameof(preparationPeer));
        }
    }

    public DeviceId HostDeviceId => connection.PeerDeviceId;

    public DeviceId ParticipantDeviceId => connection.PeerDeviceId;

    internal CancellationToken LifetimeCancellationToken
    {
        get
        {
            lock (lifetimeCancellationGate)
            {
                ObjectDisposedException.ThrowIf(
                    lifetimeCancellationDisposed,
                    this);
                return lifetimeCancellation.Token;
            }
        }
    }

    internal CancellationTokenRegistration RegisterLifetimeCancellationCallback(
        Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        CancellationToken token = LifetimeCancellationToken;
        return token.UnsafeRegister(
            static state =>
            {
                var registration =
                    (LifetimeCancellationCallbackRegistration)state!;
                using SessionCallLease callbackCall =
                    registration.Owner.EnterSessionCall(
                        registration.Owner.activeLifetimeCancellationCall);
                registration.Callback();
            },
            new LifetimeCancellationCallbackRegistration(this, callback));
    }

    public event Action<RemoteWindowParticipantState>? StateChanged;

    public void Cancel()
    {
        _ = CloseSendAdmission();

        try
        {
            RequestLifetimeStop();
        }
        finally
        {
            CompletePendingAsLost();
        }
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        StartDispatch();
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            while (true)
            {
                ControlMessage message = await connection.ReadAsync(linked.Token)
                    .ConfigureAwait(false);
                await DispatchAsync(message, linked.Token).ConfigureAwait(false);
            }
        }
        catch (IOException exception) when (linked.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The Remote Window control session was stopped.",
                exception,
                linked.Token);
        }
        finally
        {
            await StopDispatchAsync().ConfigureAwait(false);
        }
    }

    internal async ValueTask DispatchAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message.Type)
        {
            case ControlMessageType.RemoteWindowPrepare:
                await HandlePrepareAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.RemoteWindowReady:
                HandleReady(message);
                break;
            case ControlMessageType.RemoteWindowAdmission:
                await HandleAdmissionAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.RemoteWindowDriver:
                await HandleDriverAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.RemoteWindowInput:
                await HandleInputAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.RemoteWindowDisconnect:
                await HandleDisconnectAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.RemoteWindowState:
                HandleState(message);
                break;
            default:
                throw new InvalidDataException(
                    "The Remote Window session received an unsupported control message.");
        }
    }

    internal void StartDispatch()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsRemoteWindow(connection.ProtocolVersion))
        {
            throw new InvalidOperationException(
                "A Remote Window control session requires protocol 1.5 or later.");
        }

        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A Remote Window control session can run only once.");
        }
    }

    internal ValueTask StopDispatchAsync()
    {
        bool calledFromActiveSessionCall = IsActiveSessionCall(activeSendCall)
            || IsActiveSessionCall(activePreparationCall)
            || IsActiveSessionCall(activeStopDispatchCall)
            || IsActiveSessionCall(activeLifetimeCancellationCall)
            || ReferenceEquals(activeLifetimeCancellationOwner, this);
        if (Interlocked.CompareExchange(ref stopDispatchStarted, 1, 0) == 0)
        {
            _ = CompleteStopDispatchAsync();
        }

        return calledFromActiveSessionCall
            ? ValueTask.CompletedTask
            : new ValueTask(stopDispatchCompletion.Task);
    }

    private async Task CompleteStopDispatchAsync()
    {
        try
        {
            await StopDispatchCoreAsync().ConfigureAwait(false);
            stopDispatchCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            stopDispatchCompletion.TrySetException(exception);
        }
    }

    private async ValueTask StopDispatchCoreAsync()
    {
        Task? sendDrain = CloseSendAdmission();
        Exception? failure = null;
        try
        {
            RequestLifetimeStop();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        CompletePendingAsLost();
        Task preparationPeerDisconnect =
            NotifyPreparationPeerDisconnectedAsync().AsTask();

        if (sendDrain is not null)
        {
            try
            {
                await sendDrain.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        InboundPreparation? inbound;
        OutboundPreparation? outbound;
        lock (preparationGate)
        {
            inbound = inboundPreparation;
            outbound = outboundPreparation;
        }

        if (inbound is not null)
        {
            try
            {
                await inbound.Completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                Volatile.Read(ref lifetimeStopRequested) != 0)
            {
                // The connection stop owns this preparation cancellation.
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        if (outbound is not null)
        {
            try
            {
                await outbound.WatchdogCompletion.Task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        try
        {
            await preparationPeerDisconnect.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        try
        {
            await NotifyControlPeerDisconnectedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }
    }

    public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken) => PrepareCoreAsync(
            request,
            admission: null,
            cancellationToken);

    public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareReservedAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowHostPreparationAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return PrepareCoreAsync(request, admission, cancellationToken);
    }

    private async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareCoreAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowHostPreparationAdmission? admission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsRemoteWindowPreparation(
                connection.ProtocolVersion))
        {
            return RemoteWindowPreparationDeliveryResult.ProtocolUnsupported;
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return RemoteWindowPreparationDeliveryResult.NotDelivered;
        }

        if (request.HostDeviceId != connection.LocalDeviceId
            || request.ParticipantDeviceId != connection.PeerDeviceId)
        {
            throw new InvalidOperationException(
                "A Remote Window preparation must match the authenticated host and participant.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            request,
            now);
        var preparation = new OutboundPreparation(
            request,
            CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token));
        bool reserved = false;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                DateTimeOffset reservationTime = timeProvider.GetUtcNow();
                if (Volatile.Read(ref stopped) == 0
                    && !preparation.WatchdogCancellation.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested
                    && reservationTime < request.Deadline)
                {
                    if (outboundPreparation is not null
                        || inboundPreparation is not null
                        || pending.ContainsKey(request.CorrelationId))
                    {
                        preparation.WatchdogCancellation.Dispose();
                        throw new InvalidOperationException(
                            "An authenticated Remote Window connection can prepare only one session.");
                    }

                    outboundPreparation = preparation;
                    reserved = true;
                }
            }
        }

        if (!reserved)
        {
            preparation.WatchdogCancellation.Dispose();
            if (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return RemoteWindowPreparationDeliveryResult.NotDelivered;
        }

        _ = MonitorOutboundPreparationAsync(preparation);

        try
        {
            if (!await TrySendPrepareMessageAsync(
                    message,
                    preparation,
                    admission,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return RemoteWindowPreparationDeliveryResult.NotDelivered;
            }

            bool sendCommitted;
            RemoteWindowPreparationResponse? bufferedResponse;
            lock (sendAdmissionGate)
            {
                lock (preparationGate)
                {
                    bufferedResponse = preparation.State is (
                            OutboundPreparationState.ReadyBuffered
                            or OutboundPreparationState.ReadyAcknowledged)
                        ? preparation.Response
                        : null;
                    bool rejected = bufferedResponse?.Outcome
                        is RemoteWindowPreparationOutcome.Rejected;
                    DateTimeOffset commitTime = rejected
                        ? default
                        : timeProvider.GetUtcNow();
                    sendCommitted = rejected
                        || Volatile.Read(ref stopped) == 0
                        && !cancellationToken.IsCancellationRequested
                        && commitTime < request.Deadline
                        && ReferenceEquals(outboundPreparation, preparation)
                        && preparation.State is (
                            OutboundPreparationState.PrepareSending
                            or OutboundPreparationState.ReadyBuffered
                            or OutboundPreparationState.ReadyAcknowledged);
                    if (sendCommitted
                        && preparation.State == OutboundPreparationState.PrepareSending)
                    {
                        preparation.State = OutboundPreparationState.PrepareSent;
                    }

                    if (sendCommitted
                        && preparation.State == OutboundPreparationState.ReadyBuffered)
                    {
                        preparation.State = OutboundPreparationState.ReadyAcknowledged;
                        preparation.Completion.TrySetResult(bufferedResponse!);
                    }
                }
            }

            if (!sendCommitted)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return RemoteWindowPreparationDeliveryResult.AcknowledgementLost;
            }

            if (TryGetAcknowledgedPreparationResponse(
                    preparation,
                    out RemoteWindowPreparationResponse committedResponse))
            {
                return RemoteWindowPreparationDeliveryResult.Acknowledged(
                    committedResponse);
            }

            TimeSpan remaining = request.Deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                Cancel();
                if (TryGetAcknowledgedPreparationResponse(
                        preparation,
                        out committedResponse))
                {
                    return RemoteWindowPreparationDeliveryResult.Acknowledged(
                        committedResponse);
                }

                return RemoteWindowPreparationDeliveryResult.AcknowledgementLost;
            }

            try
            {
                RemoteWindowPreparationResponse response =
                    await preparation.Completion.Task
                        .WaitAsync(remaining, timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                return RemoteWindowPreparationDeliveryResult.Acknowledged(response);
            }
            catch (TimeoutException)
            {
                Cancel();
                if (TryGetAcknowledgedPreparationResponse(
                        preparation,
                        out committedResponse))
                {
                    return RemoteWindowPreparationDeliveryResult.Acknowledged(
                        committedResponse);
                }

                return RemoteWindowPreparationDeliveryResult.AcknowledgementLost;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return RemoteWindowPreparationDeliveryResult.AcknowledgementLost;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Cancel();
            throw;
        }
        catch (OperationCanceledException)
        {
            if (TryGetAcknowledgedPreparationResponse(preparation, out var response))
            {
                return RemoteWindowPreparationDeliveryResult.Acknowledged(response);
            }

            Cancel();
            return RemoteWindowPreparationDeliveryResult.AcknowledgementLost;
        }
        catch (Exception exception) when (exception is
            IOException or SocketException or TimeoutException)
        {
            if (TryGetAcknowledgedPreparationResponse(preparation, out var response))
            {
                return RemoteWindowPreparationDeliveryResult.Acknowledged(response);
            }

            Cancel();
            return RemoteWindowPreparationDeliveryResult.NotDelivered;
        }
    }

    public async ValueTask PublishAdmissionStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OutboundPreparation preparation;
        bool expired;
        bool inactive;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                preparation = outboundPreparation
                    ?? throw new InvalidOperationException(
                        "A Remote Window admission cannot precede preparation.");
                RemoteWindowPreparationRequest request = preparation.Request;
                bool applied = state.Outcome is
                    RemoteWindowControlOutcome.Applied
                    or RemoteWindowControlOutcome.AlreadyApplied;
                DateTimeOffset reservationTime = timeProvider.GetUtcNow();
                expired = reservationTime >= request.Deadline;
                inactive = Volatile.Read(ref running) == 0
                    || Volatile.Read(ref stopped) != 0
                    || preparation.WatchdogCancellation.IsCancellationRequested
                    || cancellationToken.IsCancellationRequested;
                if (!expired
                    && (preparation.State != OutboundPreparationState.ReadyAcknowledged
                    || preparation.Response?.Outcome is not RemoteWindowPreparationOutcome.Ready
                    || state.Action != RemoteWindowControlAction.Admission
                    || state.CorrelationId != request.CorrelationId
                    || state.SessionId != request.SessionId
                    || state.ActivityId != request.ActivityId
                    || state.HostDeviceId != request.HostDeviceId
                    || state.ParticipantDeviceId != request.ParticipantDeviceId
                    || applied && state.EffectiveRole != request.RequestedRole
                    || !applied && state.Outcome != RemoteWindowControlOutcome.Rejected))
                {
                    throw new InvalidOperationException(
                        "A Remote Window admission state must exactly finalize its ready preparation.");
                }

                if (!expired && !inactive)
                {
                    preparation.State = OutboundPreparationState.AdmissionSending;
                }
            }
        }

        if (expired)
        {
            Cancel();
            throw new InvalidOperationException(
                "A Remote Window admission cannot outlive its preparation deadline.");
        }

        if (inactive)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException(
                "A Remote Window admission cannot be published on an inactive session.");
        }

        try
        {
            await SendStateAsync(
                    state,
                    cancellationToken,
                    preparation.Request.Deadline)
                .ConfigureAwait(false);
            bool publicationCommitted;
            lock (sendAdmissionGate)
            {
                lock (preparationGate)
                {
                    DateTimeOffset commitTime = timeProvider.GetUtcNow();
                    publicationCommitted = Volatile.Read(ref stopped) == 0
                        && !cancellationToken.IsCancellationRequested
                        && commitTime < preparation.Request.Deadline
                        && ReferenceEquals(outboundPreparation, preparation)
                        && preparation.State
                            == OutboundPreparationState.AdmissionSending;
                    if (publicationCommitted)
                    {
                        preparation.State = OutboundPreparationState.AdmissionSent;
                        preparation.WatchdogCancellation.Cancel();
                    }
                }
            }

            if (!publicationCommitted)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "The Remote Window admission publication raced its deadline or connection stop.");
            }

            if (state.Outcome is RemoteWindowControlOutcome.Rejected)
            {
                Cancel();
            }
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    public ValueTask<RemoteWindowControlDeliveryResult> AdmitAsync(
        RemoteWindowAdmissionRequest request,
        CancellationToken cancellationToken) => SendAsync(
            request,
            RemoteWindowControlAction.Admission,
            static (version, localDeviceId, value, sentAt) =>
                RemoteWindowControlMessageCodec.CreateAdmission(
                    version,
                    localDeviceId,
                    value,
                    sentAt),
            cancellationToken);

    public ValueTask<RemoteWindowControlDeliveryResult> RequestDriverAsync(
        RemoteWindowDriverRequest request,
        CancellationToken cancellationToken) => SendAsync(
            request,
            RemoteWindowControlAction.Driver,
            static (version, localDeviceId, value, sentAt) =>
                RemoteWindowControlMessageCodec.CreateDriverRequest(
                    version,
                    localDeviceId,
                    value,
                    sentAt),
            cancellationToken);

    public ValueTask<RemoteWindowControlDeliveryResult> SendInputAsync(
        RemoteWindowInputRequest request,
        CancellationToken cancellationToken) => SendAsync(
            request,
            RemoteWindowControlAction.Input,
            static (version, localDeviceId, value, sentAt) =>
                RemoteWindowControlMessageCodec.CreateInputRequest(
                    version,
                    localDeviceId,
                    value,
                    sentAt),
            cancellationToken);

    public ValueTask<RemoteWindowControlDeliveryResult> DisconnectAsync(
        RemoteWindowDisconnectRequest request,
        CancellationToken cancellationToken) => SendAsync(
            request,
            RemoteWindowControlAction.Disconnect,
            static (version, localDeviceId, value, sentAt) =>
                RemoteWindowControlMessageCodec.CreateDisconnect(
                    version,
                    localDeviceId,
                    value,
                    sentAt),
            cancellationToken);

    public async ValueTask PublishStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (peer is null
            || state.Action != RemoteWindowControlAction.StateChanged
            || state.SessionId != peer.SessionId
            || state.ActivityId != peer.ActivityId
            || state.HostDeviceId != connection.LocalDeviceId
            || state.ParticipantDeviceId != connection.PeerDeviceId)
        {
            throw new InvalidOperationException(
                "A published Remote Window state must match this hosted live session.");
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            throw new InvalidOperationException(
                "A Remote Window state cannot be published on an inactive session.");
        }

        await SendStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        bool calledFromActiveSessionCall = IsActiveSessionCall(activeSendCall)
            || IsActiveSessionCall(activePreparationCall)
            || IsActiveSessionCall(activeStopDispatchCall)
            || IsActiveSessionCall(activeLifetimeCancellationCall)
            || ReferenceEquals(activeLifetimeCancellationOwner, this);
        if (Interlocked.CompareExchange(ref disposed, 1, 0) == 0)
        {
            _ = CompleteDisposalAsync();
        }

        return calledFromActiveSessionCall
            ? ValueTask.CompletedTask
            : new ValueTask(disposalCompletion.Task);
    }

    private async Task CompleteDisposalAsync()
    {
        Exception? failure = null;
        Task? sendDrain = CloseSendAdmission();
        try
        {
            RequestLifetimeStop();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            CompletePendingAsLost();
        }

        Task preparationPeerDisconnect =
            NotifyPreparationPeerDisconnectedAsync().AsTask();

        if (sendDrain is not null)
        {
            try
            {
                await sendDrain.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        InboundPreparation? preparation;
        OutboundPreparation? outbound;
        lock (preparationGate)
        {
            preparation = inboundPreparation;
            outbound = outboundPreparation;
        }

        if (preparation is not null)
        {
            try
            {
                await preparation.Completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                Volatile.Read(ref lifetimeStopRequested) != 0)
            {
                // The connection stop owns this preparation cancellation.
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        if (outbound is not null)
        {
            try
            {
                await outbound.WatchdogCompletion.Task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        try
        {
            await preparationPeerDisconnect.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        try
        {
            await NotifyControlPeerDisconnectedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        try
        {
            await RequestLifetimeCancellationDisposalAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        if (failure is null)
        {
            disposalCompletion.TrySetResult();
        }
        else
        {
            disposalCompletion.TrySetException(failure);
        }
    }

    private async ValueTask<RemoteWindowControlDeliveryResult> SendAsync<TRequest>(
        TRequest request,
        RemoteWindowControlAction action,
        Func<ProtocolVersion, DeviceId, TRequest, DateTimeOffset, ControlMessage> create,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsRemoteWindow(connection.ProtocolVersion))
        {
            return RemoteWindowControlDeliveryResult.ProtocolUnsupported;
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return RemoteWindowControlDeliveryResult.NotDelivered;
        }

        RequestBinding binding = GetBinding(request);
        if (binding.ParticipantDeviceId != connection.LocalDeviceId
            || binding.HostDeviceId != connection.PeerDeviceId)
        {
            throw new InvalidOperationException(
                "A Remote Window request must match the authenticated connection participants.");
        }

        if (!TryReservePendingSlot())
        {
            return RemoteWindowControlDeliveryResult.NotDelivered;
        }

        var pendingState = new PendingState(
            binding.SessionId,
            binding.ActivityId,
            action);
        bool registered;
        lock (preparationGate)
        {
            if (inboundPreparation?.Request.CorrelationId == binding.CorrelationId
                || outboundPreparation?.Request.CorrelationId == binding.CorrelationId)
            {
                ReleasePendingSlot();
                throw new InvalidOperationException(
                    "A Remote Window command cannot reuse its connection's preparation correlation ID.");
            }

            registered = pending.TryAdd(binding.CorrelationId, pendingState);
        }

        if (!registered)
        {
            ReleasePendingSlot();
            throw new InvalidOperationException(
                "A Remote Window command with this correlation ID is already pending.");
        }

        bool sent = false;
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            ControlMessage message = create(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                request,
                now);
            if (!await TrySendMessageAsync(message, cancellationToken)
                .ConfigureAwait(false))
            {
                return RemoteWindowControlDeliveryResult.NotDelivered;
            }

            sent = true;
            TimeSpan remaining = binding.Deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                Cancel();
                return RemoteWindowControlDeliveryResult.AcknowledgementLost;
            }

            try
            {
                RemoteWindowParticipantState state = await pendingState.Completion.Task
                    .WaitAsync(remaining, timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                return RemoteWindowControlDeliveryResult.Acknowledged(state);
            }
            catch (TimeoutException)
            {
                Cancel();
                return RemoteWindowControlDeliveryResult.AcknowledgementLost;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                throw;
            }
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            return RemoteWindowControlDeliveryResult.NotDelivered;
        }
        finally
        {
            pending.TryRemove(
                new KeyValuePair<CorrelationId, PendingState>(
                    binding.CorrelationId,
                    pendingState));
            ReleasePendingSlot();
        }
    }

    private ValueTask HandlePrepareAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        IRemoteWindowPreparationPeer target = preparationPeer
            ?? throw new InvalidDataException(
                "This authenticated Device has no Remote Window preparation endpoint.");
        RemoteWindowPreparationRequest request =
            RemoteWindowControlMessageCodec.DecodePrepare(
                message,
                connection.LocalDeviceId,
                connection.ProtocolVersion);
        ValidateIncoming(request.Deadline);
        TimeSpan remaining = request.Deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "The Remote Window preparation deadline expired before it could be reserved.");
        }

        var deadlineCancellation = new CancellationTokenSource(
            remaining,
            timeProvider);
        CancellationTokenSource cancellation;
        try
        {
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token,
                deadlineCancellation.Token);
        }
        catch
        {
            deadlineCancellation.Dispose();
            throw;
        }

        var preparation = new InboundPreparation(
            request,
            cancellation,
            deadlineCancellation);
        bool reserved = false;
        bool deadlineExpired = false;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                DateTimeOffset reservationTime = timeProvider.GetUtcNow();
                deadlineExpired = reservationTime >= request.Deadline;
                if (Volatile.Read(ref stopped) == 0
                    && !preparation.Cancellation.IsCancellationRequested
                    && !deadlineExpired)
                {
                    if (inboundPreparation is not null
                        || outboundPreparation is not null
                        || pending.ContainsKey(request.CorrelationId))
                    {
                        preparation.Cancellation.Dispose();
                        preparation.DeadlineCancellation.Dispose();
                        throw new InvalidDataException(
                            "An authenticated Remote Window connection received a conflicting preparation.");
                    }

                    inboundPreparation = preparation;
                    reserved = true;
                }
            }
        }

        if (!reserved)
        {
            preparation.Cancellation.Dispose();
            preparation.DeadlineCancellation.Dispose();
            if (deadlineExpired)
            {
                throw new InvalidDataException(
                    "The Remote Window preparation deadline expired before it could be reserved.");
            }

            throw new OperationCanceledException(
                "The Remote Window preparation raced the connection stop.");
        }

        _ = Task.Run(
            () => CompleteInboundPreparationAsync(target, preparation),
            CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    private async Task CompleteInboundPreparationAsync(
        IRemoteWindowPreparationPeer target,
        InboundPreparation preparation)
    {
        Exception? failure = null;
        bool cancelled = false;
        try
        {
            RemoteWindowPreparationResponse response;
            using (SessionCallLease sessionCall = EnterSessionCall(
                       activePreparationCall))
            {
                bool boundaryStarted;
                bool deadlineExpired;
                lock (sendAdmissionGate)
                {
                    lock (preparationGate)
                    {
                        DateTimeOffset boundaryStartTime = timeProvider.GetUtcNow();
                        deadlineExpired = boundaryStartTime
                            >= preparation.Request.Deadline;
                        if (!ReferenceEquals(inboundPreparation, preparation)
                            || preparation.State != InboundPreparationState.Reserved)
                        {
                            throw new InvalidDataException(
                                "The Remote Window participant boundary raced a terminal preparation.");
                        }

                        boundaryStarted = Volatile.Read(ref stopped) == 0
                            && !preparation.Cancellation.IsCancellationRequested
                            && !deadlineExpired;
                        if (boundaryStarted)
                        {
                            preparation.State = InboundPreparationState.Preparing;
                        }
                    }
                }

                if (!boundaryStarted)
                {
                    if (deadlineExpired)
                    {
                        throw new InvalidDataException(
                            "The Remote Window preparation deadline expired before its participant boundary started.");
                    }

                    throw new OperationCanceledException(
                        "The Remote Window preparation stopped before its participant boundary started.",
                        preparation.Cancellation.Token);
                }

                response = await target.PrepareAsync(
                    preparation.Request,
                    preparation.Cancellation.Token).ConfigureAwait(false);
            }
            if (response.Request != preparation.Request)
            {
                throw new InvalidDataException(
                    "The local Remote Window preparation endpoint changed its binding.");
            }

            Exception? responseDeliveryFailure = null;
            bool responseCommitted = false;
            try
            {
                ValidateIncoming(preparation.Request.Deadline);
                lock (preparationGate)
                {
                    if (!ReferenceEquals(inboundPreparation, preparation)
                        || preparation.State != InboundPreparationState.Preparing)
                    {
                        throw new InvalidDataException(
                            "The Remote Window preparation raced a terminal state.");
                    }

                    preparation.Response = response;
                    if (response.Outcome is RemoteWindowPreparationOutcome.Rejected)
                    {
                        preparation.State = InboundPreparationState.Rejected;
                    }
                }

                ControlMessage ready = RemoteWindowControlMessageCodec.CreateReady(
                    connection.ProtocolVersion,
                    connection.LocalDeviceId,
                    response,
                    timeProvider.GetUtcNow());
                responseCommitted = response.Outcome is
                    RemoteWindowPreparationOutcome.Ready
                    ? await TrySendReadyMessageAsync(ready, preparation)
                        .ConfigureAwait(false)
                    : await TrySendMessageAsync(
                            ready,
                            preparation.Cancellation.Token,
                            preparation.Request.Deadline)
                        .ConfigureAwait(false);
                if (!responseCommitted)
                {
                    throw new OperationCanceledException(
                        "The Remote Window readiness result could not be sent.");
                }
            }
            catch (Exception exception)
            {
                responseDeliveryFailure = exception;
            }

            try
            {
                using SessionCallLease sessionCall = EnterSessionCall(
                    activePreparationCall);
                await target.CompletePreparationResponseAsync(
                        response,
                        responseCommitted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                responseDeliveryFailure = CombineResponseCompletionFailures(
                    responseDeliveryFailure,
                    exception);
            }

            if (responseDeliveryFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(responseDeliveryFailure)
                    .Throw();
            }

            if (response.Outcome is RemoteWindowPreparationOutcome.Ready)
            {
                RemoteWindowParticipantState? bufferedAdmission;
                bool readyCommitted;
                lock (sendAdmissionGate)
                {
                    lock (preparationGate)
                    {
                        DateTimeOffset commitTime = timeProvider.GetUtcNow();
                        readyCommitted = Volatile.Read(ref stopped) == 0
                            && !preparation.Cancellation.IsCancellationRequested
                            && commitTime < preparation.Request.Deadline
                            && ReferenceEquals(inboundPreparation, preparation)
                            && preparation.State == InboundPreparationState.ReadySending;
                        bufferedAdmission = readyCommitted
                            ? preparation.BufferedAdmissionState
                            : null;
                        if (readyCommitted)
                        {
                            preparation.State = bufferedAdmission is null
                                ? InboundPreparationState.AwaitingAdmissionState
                                : InboundPreparationState.AdmissionPendingBoundary;
                        }
                    }
                }

                if (!readyCommitted)
                {
                    Cancel();
                    throw new OperationCanceledException(
                        "The Remote Window readiness send raced its deadline or connection stop.",
                        preparation.Cancellation.Token);
                }

                if (bufferedAdmission is not null
                    && !preparation.AdmissionCompletion.TrySetResult(bufferedAdmission))
                {
                    throw new InvalidDataException(
                        "The buffered Remote Window admission raced another terminal result.");
                }

                TimeSpan remaining = preparation.Request.Deadline
                    - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        "The Remote Window admission state missed its preparation deadline.");
                }

                RemoteWindowParticipantState admission =
                    await preparation.AdmissionCompletion.Task
                    .WaitAsync(
                        remaining,
                        timeProvider,
                        preparation.Cancellation.Token)
                    .ConfigureAwait(false);
                await CompletePreparedAdmissionAsync(
                    target,
                    preparation,
                    admission).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        preparation.Cancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            preparation.Cancellation.IsCancellationRequested)
        {
            cancelled = true;
            Cancel();
        }
        catch (Exception exception)
        {
            failure = exception;
            Cancel();
        }
        finally
        {
            preparation.Cancellation.Dispose();
            preparation.DeadlineCancellation.Dispose();
            if (failure is not null)
            {
                preparation.Completion.TrySetException(failure);
            }
            else if (cancelled)
            {
                preparation.Completion.TrySetCanceled();
            }
            else
            {
                preparation.Completion.TrySetResult();
            }
        }
    }

    private async Task CompletePreparedAdmissionAsync(
        IRemoteWindowPreparationPeer target,
        InboundPreparation preparation,
        RemoteWindowParticipantState state)
    {
        RemoteWindowPreparationRequest request = preparation.Request;
        bool applied = state.Outcome is
            RemoteWindowControlOutcome.Applied
            or RemoteWindowControlOutcome.AlreadyApplied;
        var binding = new SessionBinding(state.SessionId, state.ActivityId);
        CancellationToken preparationCancellation =
            preparation.Cancellation.Token;
        using SessionCallLease sessionCall = EnterSessionCall(
            activePreparationCall);
        bool boundaryStarted;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                DateTimeOffset boundaryStartTime = timeProvider.GetUtcNow();
                if (!ReferenceEquals(inboundPreparation, preparation)
                    || preparation.State
                        != InboundPreparationState.AdmissionPendingBoundary)
                {
                    throw new InvalidDataException(
                        "The Remote Window admission boundary raced a terminal preparation.");
                }

                boundaryStarted = Volatile.Read(ref stopped) == 0
                    && !preparationCancellation.IsCancellationRequested
                    && boundaryStartTime < request.Deadline;
                if (boundaryStarted)
                {
                    preparation.State = InboundPreparationState.FinalizingAdmission;
                }
            }
        }

        if (!boundaryStarted)
        {
            Cancel();
            throw new OperationCanceledException(
                "The Remote Window admission boundary raced its deadline or connection stop.",
                preparationCancellation);
        }

        await target.CompleteAdmissionAsync(
                request,
                state,
                preparationCancellation)
            .ConfigureAwait(false);

        bool completionCommitted;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                completionCommitted = Volatile.Read(ref stopped) == 0
                    && !preparationCancellation.IsCancellationRequested
                    && now < request.Deadline;
                if (completionCommitted)
                {
                    if (!ReferenceEquals(inboundPreparation, preparation)
                        || preparation.State != InboundPreparationState.FinalizingAdmission)
                    {
                        throw new InvalidDataException(
                            "The Remote Window admission completion raced a terminal preparation.");
                    }

                    if (applied)
                    {
                        if (!knownBindings.IsEmpty
                            && !knownBindings.ContainsKey(binding))
                        {
                            throw new InvalidDataException(
                                "The authenticated connection already owns another Remote Window binding.");
                        }

                        knownBindings.AddOrUpdate(
                            binding,
                            state.Revision,
                            (_, revision) => Math.Max(revision, state.Revision));
                        preparation.State = InboundPreparationState.Admitted;
                    }
                    else
                    {
                        preparation.State = InboundPreparationState.Rejected;
                    }
                }
            }
        }

        if (!completionCommitted)
        {
            Cancel();
            throw new OperationCanceledException(
                "The Remote Window admission completion raced its deadline or connection stop.",
                preparationCancellation);
        }

        PublishStateChanged(state);
        if (!applied)
        {
            Cancel();
        }
    }

    private async Task MonitorOutboundPreparationAsync(
        OutboundPreparation preparation)
    {
        Exception? failure = null;
        try
        {
            TimeSpan remaining = preparation.Request.Deadline
                - timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(
                    remaining,
                    timeProvider,
                    preparation.WatchdogCancellation.Token).ConfigureAwait(false);
            }

            DateTimeOffset expiryCheckTime = timeProvider.GetUtcNow();
            bool expired;
            lock (preparationGate)
            {
                expired = ReferenceEquals(outboundPreparation, preparation)
                    && preparation.State != OutboundPreparationState.AdmissionSent
                    && expiryCheckTime >= preparation.Request.Deadline;
            }

            if (expired)
            {
                Cancel();
            }
        }
        catch (OperationCanceledException) when (
            preparation.WatchdogCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
            try
            {
                Cancel();
            }
            catch (Exception cancellationFailure)
            {
                failure = CombineFailures(failure, cancellationFailure);
            }
        }
        finally
        {
            preparation.WatchdogCancellation.Dispose();
            if (failure is null)
            {
                preparation.WatchdogCompletion.TrySetResult();
            }
            else
            {
                preparation.WatchdogCompletion.TrySetException(failure);
            }
        }
    }

    private void HandleReady(ControlMessage message)
    {
        OutboundPreparation preparation;
        lock (preparationGate)
        {
            preparation = outboundPreparation
                ?? throw new InvalidDataException(
                    "An unsolicited Remote Window readiness result was rejected.");
            if (preparation.IsTerminal)
            {
                throw new InvalidDataException(
                    "A delayed Remote Window readiness result was rejected by the terminal preparation tombstone.");
            }

            if (preparation.State is not (
                    OutboundPreparationState.PrepareSending
                    or OutboundPreparationState.PrepareSent))
            {
                throw new InvalidDataException(
                    "A duplicate Remote Window readiness result was rejected.");
            }
        }

        RemoteWindowPreparationResponse response =
            RemoteWindowControlMessageCodec.DecodeReady(
                message,
                connection.LocalDeviceId,
                connection.ProtocolVersion,
                preparation.Request);
        ValidateIncoming(response.Request.Deadline);
        bool commitResponse;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                if (!ReferenceEquals(outboundPreparation, preparation)
                    || preparation.IsTerminal
                    || preparation.State is not (
                        OutboundPreparationState.PrepareSending
                        or OutboundPreparationState.PrepareSent))
                {
                    throw new InvalidDataException(
                        "The Remote Window readiness result raced a terminal state.");
                }

                DateTimeOffset commitTime = response.Outcome
                        is RemoteWindowPreparationOutcome.Rejected
                    ? default
                    : timeProvider.GetUtcNow();
                commitResponse = Volatile.Read(ref stopped) == 0
                    && !preparation.WatchdogCancellation.IsCancellationRequested
                    && (response.Outcome is RemoteWindowPreparationOutcome.Rejected
                        || commitTime < preparation.Request.Deadline);
                if (commitResponse)
                {
                    preparation.Response = response;
                    bool acknowledged = preparation.State
                            == OutboundPreparationState.PrepareSent
                        || response.Outcome is RemoteWindowPreparationOutcome.Rejected;
                    preparation.State = acknowledged
                        ? OutboundPreparationState.ReadyAcknowledged
                        : OutboundPreparationState.ReadyBuffered;
                    if (acknowledged)
                    {
                        preparation.Completion.TrySetResult(response);
                    }
                }
            }
        }

        if (!commitResponse)
        {
            Cancel();
            throw new InvalidDataException(
                "The Remote Window readiness result raced its deadline or connection stop.");
        }

    }

    private bool TryGetAcknowledgedPreparationResponse(
        OutboundPreparation preparation,
        out RemoteWindowPreparationResponse response)
    {
        lock (preparationGate)
        {
            if (ReferenceEquals(outboundPreparation, preparation)
                && preparation.State == OutboundPreparationState.ReadyAcknowledged
                && preparation.Response is { } acknowledged)
            {
                response = acknowledged;
                return true;
            }
        }

        response = null!;
        return false;
    }

    private async ValueTask HandleAdmissionAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        RejectPreparationCorrelation(message.CorrelationId);
        IRemoteWindowControlPeer target = RequirePeer();
        RemoteWindowAdmissionRequest request =
            RemoteWindowControlMessageCodec.DecodeAdmission(
                message,
                connection.LocalDeviceId);
        ValidateIncoming(request.Deadline);
        RemoteWindowParticipantState state = await target.AdmitAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        await SendStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleDriverAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        RejectPreparationCorrelation(message.CorrelationId);
        IRemoteWindowControlPeer target = RequirePeer();
        RemoteWindowDriverRequest request =
            RemoteWindowControlMessageCodec.DecodeDriverRequest(
                message,
                connection.LocalDeviceId);
        ValidateIncoming(request.Deadline);
        RemoteWindowParticipantState state = await target.RequestDriverAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        await SendStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleInputAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        RejectPreparationCorrelation(message.CorrelationId);
        IRemoteWindowControlPeer target = RequirePeer();
        RemoteWindowInputRequest request =
            RemoteWindowControlMessageCodec.DecodeInputRequest(
                message,
                connection.LocalDeviceId);
        ValidateIncoming(request.Deadline);
        RemoteWindowParticipantState state = await target.SendInputAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        await SendStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleDisconnectAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        RejectPreparationCorrelation(message.CorrelationId);
        IRemoteWindowControlPeer target = RequirePeer();
        RemoteWindowDisconnectRequest request =
            RemoteWindowControlMessageCodec.DecodeDisconnect(
                message,
                connection.LocalDeviceId);
        ValidateIncoming(request.Deadline);
        RemoteWindowParticipantState state = await target.DisconnectAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        await SendStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private void HandleState(ControlMessage message)
    {
        DateTimeOffset expiresAt = message.SentAt.ToUniversalTime()
            .AddMilliseconds(message.TimeToLiveMilliseconds);
        if (timeProvider.GetUtcNow() >= expiresAt)
        {
            throw new InvalidDataException("The Remote Window state has expired.");
        }

        if (TryHandlePreparedAdmissionState(message))
        {
            return;
        }

        if (!pending.TryGetValue(
                message.CorrelationId,
                out PendingState? pendingState))
        {
            RemoteWindowParticipantState published =
                RemoteWindowControlMessageCodec.DecodePublishedState(
                    message,
                    connection.LocalDeviceId);
            var binding = new SessionBinding(
                published.SessionId,
                published.ActivityId);
            if (!knownBindings.TryGetValue(binding, out long lastRevision))
            {
                throw new InvalidDataException(
                    "An unsolicited Remote Window state for an unknown live session was rejected.");
            }

            if (published.Revision <= lastRevision
                || !knownBindings.TryUpdate(
                    binding,
                    published.Revision,
                    lastRevision))
            {
                throw new InvalidDataException(
                    "An unsolicited Remote Window state did not advance its live-session revision.");
            }

            PublishStateChanged(published);
            return;
        }

        RemoteWindowParticipantState state =
            RemoteWindowControlMessageCodec.DecodeState(
                message,
                connection.LocalDeviceId,
                pendingState.SessionId,
                pendingState.ActivityId);
        if (state.HostDeviceId != connection.PeerDeviceId
            || state.Action != pendingState.Action)
        {
            throw new InvalidDataException(
                "The Remote Window state does not match its pending command.");
        }

        if (!pending.TryRemove(
                new KeyValuePair<CorrelationId, PendingState>(
                    message.CorrelationId,
                    pendingState)))
        {
            throw new InvalidDataException(
                "The Remote Window state raced another terminal result.");
        }

        RecordAcknowledgedState(state);
        pendingState.Completion.TrySetResult(state);
    }

    private bool TryHandlePreparedAdmissionState(ControlMessage message)
    {
        InboundPreparation? preparation;
        lock (preparationGate)
        {
            preparation = inboundPreparation;
            if (preparation is null
                || message.CorrelationId != preparation.Request.CorrelationId)
            {
                return false;
            }

            if (preparation.State is not (
                    InboundPreparationState.ReadySending
                    or InboundPreparationState.AwaitingAdmissionState)
                || preparation.BufferedAdmissionState is not null)
            {
                throw new InvalidDataException(
                    "The Remote Window admission state arrived outside its preparation phase.");
            }
        }

        RemoteWindowPreparationRequest request = preparation.Request;
        if (timeProvider.GetUtcNow() >= request.Deadline)
        {
            throw new InvalidDataException(
                "The Remote Window admission state missed its preparation deadline.");
        }

        RemoteWindowParticipantState state =
            RemoteWindowControlMessageCodec.DecodeState(
                message,
                connection.LocalDeviceId,
                request.SessionId,
                request.ActivityId);
        bool applied = state.Outcome is
            RemoteWindowControlOutcome.Applied
            or RemoteWindowControlOutcome.AlreadyApplied;
        if (state.HostDeviceId != request.HostDeviceId
            || state.ParticipantDeviceId != request.ParticipantDeviceId
            || state.Action != RemoteWindowControlAction.Admission
            || applied && state.EffectiveRole != request.RequestedRole
            || !applied && state.Outcome != RemoteWindowControlOutcome.Rejected)
        {
            throw new InvalidDataException(
                "The Remote Window admission state does not finalize its preparation.");
        }

        var binding = new SessionBinding(state.SessionId, state.ActivityId);
        bool completeAdmission;
        bool commitAdmission;
        lock (sendAdmissionGate)
        {
            lock (preparationGate)
            {
                DateTimeOffset commitTime = timeProvider.GetUtcNow();
                if (!ReferenceEquals(inboundPreparation, preparation)
                    || preparation.State is not (
                        InboundPreparationState.ReadySending
                        or InboundPreparationState.AwaitingAdmissionState)
                    || preparation.BufferedAdmissionState is not null)
                {
                    throw new InvalidDataException(
                        "The Remote Window admission state raced a terminal preparation.");
                }

                commitAdmission = Volatile.Read(ref stopped) == 0
                    && !preparation.Cancellation.IsCancellationRequested
                    && commitTime < request.Deadline;
                if (commitAdmission)
                {
                    if (applied
                        && !knownBindings.IsEmpty
                        && !knownBindings.ContainsKey(binding))
                    {
                        throw new InvalidDataException(
                            "The authenticated connection already owns another Remote Window binding.");
                    }

                    completeAdmission = preparation.State
                        == InboundPreparationState.AwaitingAdmissionState;
                    if (completeAdmission)
                    {
                        preparation.State =
                            InboundPreparationState.AdmissionPendingBoundary;
                    }
                    else
                    {
                        preparation.BufferedAdmissionState = state;
                    }
                }
                else
                {
                    completeAdmission = false;
                }
            }
        }

        if (!commitAdmission)
        {
            Cancel();
            throw new InvalidDataException(
                "The Remote Window admission state raced its deadline or connection stop.");
        }

        if (completeAdmission
            && !preparation.AdmissionCompletion.TrySetResult(state))
        {
            throw new InvalidDataException(
                "The Remote Window admission state raced another terminal result.");
        }

        return true;
    }

    private async ValueTask SendStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken,
        DateTimeOffset? sendDeadline = null)
    {
        ControlMessage response = RemoteWindowControlMessageCodec.CreateState(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            state,
            timeProvider.GetUtcNow());
        if (!await TrySendMessageAsync(
                response,
                cancellationToken,
                sendDeadline)
            .ConfigureAwait(false))
        {
            throw new OperationCanceledException(
                "The Remote Window control session was stopped.");
        }
    }

    private Task? CloseSendAdmission()
    {
        lock (sendAdmissionGate)
        {
            Volatile.Write(ref stopped, 1);
            if (activeSends == 0)
            {
                return null;
            }

            sendDrainCompletion ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return sendDrainCompletion.Task;
        }
    }

    private ValueTask<bool> TrySendPrepareMessageAsync(
        ControlMessage message,
        OutboundPreparation preparation,
        IRemoteWindowHostPreparationAdmission? admission,
        CancellationToken cancellationToken) => TrySendMessageAsync(
            message,
            cancellationToken,
            preparation.Request.Deadline,
            admissionTime => AdmitPrepareSend(
                preparation,
                admission,
                admissionTime));

    private bool AdmitPrepareSend(
        OutboundPreparation preparation,
        IRemoteWindowHostPreparationAdmission? admission,
        DateTimeOffset admissionTime)
    {
        lock (preparationGate)
        {
            if (!ReferenceEquals(outboundPreparation, preparation)
                || preparation.State != OutboundPreparationState.Created)
            {
                throw new InvalidOperationException(
                    "The Remote Window preparation send raced a terminal state.");
            }

            if (admission is not null
                && !admission.TryAdmitPrepareSend(
                    preparation.Request,
                    admissionTime))
            {
                return false;
            }

            preparation.State = OutboundPreparationState.PrepareSending;
            return true;
        }
    }

    private ValueTask<bool> TrySendReadyMessageAsync(
        ControlMessage message,
        InboundPreparation preparation) => TrySendMessageAsync(
            message,
            preparation.Cancellation.Token,
            preparation.Request.Deadline,
            _ =>
            {
                AdmitReadySend(preparation);
                return true;
            });

    private void AdmitReadySend(InboundPreparation preparation)
    {
        lock (preparationGate)
        {
            if (!ReferenceEquals(inboundPreparation, preparation)
                || preparation.State != InboundPreparationState.Preparing
                || preparation.Response?.Outcome is not RemoteWindowPreparationOutcome.Ready)
            {
                throw new InvalidDataException(
                    "The Remote Window readiness send raced a terminal preparation.");
            }

            preparation.State = InboundPreparationState.ReadySending;
        }
    }

    private async ValueTask<bool> TrySendMessageAsync(
        ControlMessage message,
        CancellationToken cancellationToken,
        DateTimeOffset? sendDeadline = null,
        Func<DateTimeOffset, bool>? admitSend = null)
    {
        CancellationTokenSource linked;
        SessionCallScope? inheritedScope = activeSendCall.Value;
        var currentScope = new SessionCallScope(this, inheritedScope);
        lock (sendAdmissionGate)
        {
            DateTimeOffset admissionTime = sendDeadline.HasValue
                ? timeProvider.GetUtcNow()
                : default;
            if (Volatile.Read(ref stopped) != 0
                || cancellationToken.IsCancellationRequested
                || sendDeadline.HasValue && admissionTime >= sendDeadline.Value)
            {
                return false;
            }

            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            try
            {
                if (admitSend is not null && !admitSend(admissionTime))
                {
                    linked.Dispose();
                    return false;
                }

                activeSends++;
            }
            catch
            {
                linked.Dispose();
                throw;
            }
        }

        activeSendCall.Value = currentScope;
        try
        {
            try
            {
                await connection.SendAsync(message, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                cancellationToken.IsCancellationRequested
                && exception.CancellationToken == linked.Token)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            return true;
        }
        finally
        {
            ExitSessionCall(activeSendCall, currentScope, inheritedScope);
            linked.Dispose();
            CompleteSend();
        }
    }

    private async ValueTask NotifyPreparationPeerDisconnectedAsync()
    {
        if (Volatile.Read(ref running) == 0 || preparationPeer is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(
                ref preparationPeerDisconnectStarted,
                1,
                0) == 0)
        {
            try
            {
                using SessionCallLease sessionCall = EnterSessionCall(
                    activeStopDispatchCall);
                await preparationPeer.PeerDisconnectedAsync(
                    connection.PeerDeviceId,
                    CancellationToken.None).ConfigureAwait(false);
                preparationPeerDisconnectCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                preparationPeerDisconnectCompletion.TrySetException(exception);
            }
        }

        await preparationPeerDisconnectCompletion.Task.ConfigureAwait(false);
    }

    private async ValueTask NotifyControlPeerDisconnectedAsync()
    {
        if (Volatile.Read(ref running) == 0 || peer is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref peerDisconnectStarted, 1, 0) == 0)
        {
            try
            {
                using SessionCallLease sessionCall = EnterSessionCall(
                    activeStopDispatchCall);
                await peer.PeerDisconnectedAsync(
                    connection.PeerDeviceId,
                    CancellationToken.None).ConfigureAwait(false);
                peerDisconnectCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                peerDisconnectCompletion.TrySetException(exception);
            }
        }

        await peerDisconnectCompletion.Task.ConfigureAwait(false);
    }

    private void CompleteSend()
    {
        TaskCompletionSource? completed = null;
        lock (sendAdmissionGate)
        {
            activeSends--;
            if (activeSends == 0)
            {
                completed = sendDrainCompletion;
            }
        }

        completed?.TrySetResult();
    }

    private bool TryReservePendingSlot()
    {
        while (true)
        {
            int current = Volatile.Read(ref pendingCommandCount);
            if (current >= MaximumPendingCommands)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref pendingCommandCount,
                    current + 1,
                    current) == current)
            {
                return true;
            }
        }
    }

    private void ReleasePendingSlot() =>
        Interlocked.Decrement(ref pendingCommandCount);

    private void PublishStateChanged(RemoteWindowParticipantState state)
    {
        Action<RemoteWindowParticipantState>? observers = StateChanged;
        if (observers is null)
        {
            return;
        }

        foreach (Action<RemoteWindowParticipantState> observer in
                 observers.GetInvocationList())
        {
            try
            {
                observer(state);
            }
            catch
            {
                // Presentation observers cannot determine protocol validity.
            }
        }
    }

    private static Exception CombineFailures(
        Exception? first,
        Exception second) => first is null
            ? second
            : new AggregateException(
                "Remote Window session shutdown failed.",
                first,
                second);

    private static Exception CombineResponseCompletionFailures(
        Exception? deliveryFailure,
        Exception completionFailure) => deliveryFailure is null
            ? completionFailure
            : new AggregateException(
                "The Remote Window preparation response delivery and completion hook both failed.",
                deliveryFailure,
                completionFailure);

    private IRemoteWindowControlPeer RequirePeer() => peer
        ?? throw new InvalidDataException(
            "This authenticated Device does not host a Remote Window endpoint.");

    private void ValidateIncoming(DateTimeOffset deadline)
    {
        if (timeProvider.GetUtcNow() >= deadline)
        {
            throw new InvalidDataException(
                "The Remote Window command deadline has expired.");
        }
    }

    private void RejectPreparationCorrelation(CorrelationId correlationId)
    {
        lock (preparationGate)
        {
            if (inboundPreparation?.Request.CorrelationId == correlationId
                || outboundPreparation?.Request.CorrelationId == correlationId)
            {
                throw new InvalidDataException(
                    "A Remote Window command reused its connection's preparation correlation ID.");
            }
        }
    }

    private void CompletePendingAsLost()
    {
        foreach ((CorrelationId correlationId, PendingState pendingState) in pending)
        {
            if (pending.TryRemove(
                    new KeyValuePair<CorrelationId, PendingState>(
                        correlationId,
                        pendingState)))
            {
                pendingState.Completion.TrySetCanceled();
            }
        }

        lock (preparationGate)
        {
            if (outboundPreparation is { } preparation)
            {
                preparation.IsTerminal = true;
                preparation.Completion.TrySetCanceled();
            }
        }
    }

    private void RequestLifetimeStop()
    {
        bool entered;
        lock (lifetimeCancellationGate)
        {
            entered = lifetimeStopRequested == 0
                && !lifetimeCancellationDisposalRequested;
            if (!entered)
            {
                return;
            }

            lifetimeStopRequested = 1;
            lifetimeCancellationUsers++;
        }

        try
        {
            RemoteWindowControlSession? inheritedOwner =
                activeLifetimeCancellationOwner;
            activeLifetimeCancellationOwner = this;
            try
            {
                lifetimeCancellation.Cancel();
            }
            finally
            {
                activeLifetimeCancellationOwner = inheritedOwner;
            }
        }
        finally
        {
            ReleaseLifetimeCancellationUser();
        }
    }

    private void ReleaseLifetimeCancellationUser()
    {
        bool disposeCancellation;
        lock (lifetimeCancellationGate)
        {
            lifetimeCancellationUsers--;
            disposeCancellation = TryClaimLifetimeCancellationDisposal();
        }

        if (disposeCancellation)
        {
            DisposeLifetimeCancellation();
        }
    }

    private Task RequestLifetimeCancellationDisposalAsync()
    {
        bool disposeCancellation;
        lock (lifetimeCancellationGate)
        {
            lifetimeCancellationDisposalRequested = true;
            disposeCancellation = TryClaimLifetimeCancellationDisposal();
        }

        if (disposeCancellation)
        {
            DisposeLifetimeCancellation();
        }

        return lifetimeCancellationDisposalCompletion.Task;
    }

    private void DisposeLifetimeCancellation()
    {
        try
        {
            lifetimeCancellation.Dispose();
            lifetimeCancellationDisposalCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            lifetimeCancellationDisposalCompletion.TrySetException(exception);
        }
    }

    private bool TryClaimLifetimeCancellationDisposal()
    {
        if (!lifetimeCancellationDisposalRequested
            || lifetimeCancellationDisposed
            || lifetimeCancellationUsers != 0)
        {
            return false;
        }

        lifetimeCancellationDisposed = true;
        return true;
    }

    private SessionCallLease EnterSessionCall(
        AsyncLocal<SessionCallScope?> activeCall)
    {
        SessionCallScope? inheritedScope = activeCall.Value;
        var currentScope = new SessionCallScope(this, inheritedScope);
        activeCall.Value = currentScope;
        return new SessionCallLease(
            activeCall,
            currentScope,
            inheritedScope);
    }

    private static void ExitSessionCall(
        AsyncLocal<SessionCallScope?> activeCall,
        SessionCallScope currentScope,
        SessionCallScope? inheritedScope)
    {
        currentScope.Deactivate();
        activeCall.Value = inheritedScope;
    }

    private bool IsActiveSessionCall(
        AsyncLocal<SessionCallScope?> activeCall)
    {
        for (SessionCallScope? scope = activeCall.Value;
            scope is not null;
            scope = scope.Previous)
        {
            if (scope.IsActive && ReferenceEquals(scope.Owner, this))
            {
                return true;
            }
        }

        return false;
    }

    private void RecordAcknowledgedState(RemoteWindowParticipantState state)
    {
        var binding = new SessionBinding(state.SessionId, state.ActivityId);
        if (state.Action == RemoteWindowControlAction.Admission
            && state.Outcome != RemoteWindowControlOutcome.Rejected
            && state.EffectiveRole is not null)
        {
            knownBindings.AddOrUpdate(
                binding,
                state.Revision,
                (_, revision) => Math.Max(revision, state.Revision));
            return;
        }

        if (!knownBindings.TryGetValue(binding, out long lastRevision))
        {
            return;
        }

        if (state.Action == RemoteWindowControlAction.Disconnect
            && state.Outcome != RemoteWindowControlOutcome.Rejected
            && state.EffectiveRole is null)
        {
            knownBindings.TryRemove(
                new KeyValuePair<SessionBinding, long>(binding, lastRevision));
            return;
        }

        if (state.Revision > lastRevision)
        {
            knownBindings.TryUpdate(binding, state.Revision, lastRevision);
        }
    }

    private static RequestBinding GetBinding<TRequest>(TRequest request) => request switch
    {
        RemoteWindowAdmissionRequest value => new RequestBinding(
            value.CorrelationId,
            value.SessionId,
            value.ActivityId,
            value.HostDeviceId,
            value.ParticipantDeviceId,
            value.Deadline),
        RemoteWindowDriverRequest value => new RequestBinding(
            value.CorrelationId,
            value.SessionId,
            value.ActivityId,
            value.HostDeviceId,
            value.ParticipantDeviceId,
            value.Deadline),
        RemoteWindowInputRequest value => new RequestBinding(
            value.CorrelationId,
            value.SessionId,
            value.ActivityId,
            value.HostDeviceId,
            value.ParticipantDeviceId,
            value.Deadline),
        RemoteWindowDisconnectRequest value => new RequestBinding(
            value.CorrelationId,
            value.SessionId,
            value.ActivityId,
            value.HostDeviceId,
            value.ParticipantDeviceId,
            value.Deadline),
        _ => throw new ArgumentException(
            "The Remote Window request type is unsupported.",
            nameof(request)),
    };

    private enum InboundPreparationState
    {
        Reserved,
        Preparing,
        ReadySending,
        AwaitingAdmissionState,
        AdmissionPendingBoundary,
        FinalizingAdmission,
        Rejected,
        Admitted,
    }

    private sealed class InboundPreparation(
        RemoteWindowPreparationRequest request,
        CancellationTokenSource cancellation,
        CancellationTokenSource deadlineCancellation)
    {
        public TaskCompletionSource<RemoteWindowParticipantState>
            AdmissionCompletion
        { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowParticipantState? BufferedAdmissionState { get; set; }

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public CancellationTokenSource DeadlineCancellation { get; } =
            deadlineCancellation;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowPreparationRequest Request { get; } = request;

        public RemoteWindowPreparationResponse? Response { get; set; }

        public InboundPreparationState State { get; set; } =
            InboundPreparationState.Reserved;
    }

    private enum OutboundPreparationState
    {
        Created,
        PrepareSending,
        PrepareSent,
        ReadyBuffered,
        ReadyAcknowledged,
        AdmissionSending,
        AdmissionSent,
    }

    private sealed class OutboundPreparation(
        RemoteWindowPreparationRequest request,
        CancellationTokenSource watchdogCancellation)
    {
        public TaskCompletionSource<RemoteWindowPreparationResponse> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowPreparationRequest Request { get; } = request;

        public RemoteWindowPreparationResponse? Response { get; set; }

        public bool IsTerminal { get; set; }

        public OutboundPreparationState State { get; set; } =
            OutboundPreparationState.Created;

        public CancellationTokenSource WatchdogCancellation { get; } =
            watchdogCancellation;

        public TaskCompletionSource WatchdogCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingState(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        RemoteWindowControlAction action)
    {
        public RemoteWindowControlAction Action { get; } = action;

        public ActivityId ActivityId { get; } = activityId;

        public TaskCompletionSource<RemoteWindowParticipantState> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionId SessionId { get; } = sessionId;
    }

    private sealed record LifetimeCancellationCallbackRegistration(
        RemoteWindowControlSession Owner,
        Action Callback);

    private sealed record RequestBinding(
        CorrelationId CorrelationId,
        RemoteWindowSessionId SessionId,
        ActivityId ActivityId,
        DeviceId HostDeviceId,
        DeviceId ParticipantDeviceId,
        DateTimeOffset Deadline);

    private readonly record struct SessionBinding(
        RemoteWindowSessionId SessionId,
        ActivityId ActivityId);

    private sealed class SessionCallLease(
        AsyncLocal<SessionCallScope?> activeCall,
        SessionCallScope currentScope,
        SessionCallScope? inheritedScope) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                ExitSessionCall(activeCall, currentScope, inheritedScope);
            }
        }
    }

    private sealed class SessionCallScope(
        RemoteWindowControlSession owner,
        SessionCallScope? previous)
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public RemoteWindowControlSession Owner { get; } = owner;

        public SessionCallScope? Previous { get; } = previous;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }
}
