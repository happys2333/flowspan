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
    IAsyncDisposable
{
    [ThreadStatic]
    private static RemoteWindowControlSession? activeLifetimeCancellationOwner;

    public const int MaximumPendingCommands = 16;

    private readonly AsyncLocal<SessionCallScope?> activeLifetimeCancellationCall =
        new();
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
    private int running;
    private TaskCompletionSource? sendDrainCompletion;
    private int stopDispatchStarted;
    private int stopped;

    public RemoteWindowControlSession(
        IRemoteWindowControlConnection connection,
        IRemoteWindowControlPeer? peer = null,
        TimeProvider? timeProvider = null)
    {
        this.connection = connection
            ?? throw new ArgumentNullException(nameof(connection));
        this.peer = peer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (peer is not null && peer.HostDeviceId != connection.LocalDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window control peer must represent the authenticated local host.",
                nameof(peer));
        }
    }

    public DeviceId HostDeviceId => connection.PeerDeviceId;

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

        if (peer is not null)
        {
            try
            {
                using SessionCallLease sessionCall = EnterSessionCall(
                    activeStopDispatchCall);
                await peer.PeerDisconnectedAsync(
                    connection.PeerDeviceId,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
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
        if (!pending.TryAdd(binding.CorrelationId, pendingState))
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

    private async ValueTask HandleAdmissionAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
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

    private async ValueTask SendStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken)
    {
        ControlMessage response = RemoteWindowControlMessageCodec.CreateState(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            state,
            timeProvider.GetUtcNow());
        if (!await TrySendMessageAsync(response, cancellationToken)
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

    private async ValueTask<bool> TrySendMessageAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        SessionCallScope? inheritedScope = activeSendCall.Value;
        var currentScope = new SessionCallScope(this, inheritedScope);
        lock (sendAdmissionGate)
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return false;
            }

            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            activeSends++;
        }

        activeSendCall.Value = currentScope;
        try
        {
            await connection.SendAsync(message, linked.Token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            ExitSessionCall(activeSendCall, currentScope, inheritedScope);
            linked.Dispose();
            CompleteSend();
        }
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
