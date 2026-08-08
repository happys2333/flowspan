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
    public const int MaximumPendingCommands = 16;

    private readonly IRemoteWindowControlConnection connection;
    private readonly ConcurrentDictionary<SessionBinding, long> knownBindings = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingState> pending = new();
    private readonly IRemoteWindowControlPeer? peer;
    private readonly TimeProvider timeProvider;
    private int disposed;
    private int running;
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

    public event Action<RemoteWindowParticipantState>? StateChanged;

    public void Cancel()
    {
        Interlocked.Exchange(ref stopped, 1);
        lifetimeCancellation.Cancel();
        CompletePendingAsLost();
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
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
                switch (message.Type)
                {
                    case ControlMessageType.RemoteWindowAdmission:
                        await HandleAdmissionAsync(message, linked.Token)
                            .ConfigureAwait(false);
                        break;
                    case ControlMessageType.RemoteWindowDriver:
                        await HandleDriverAsync(message, linked.Token)
                            .ConfigureAwait(false);
                        break;
                    case ControlMessageType.RemoteWindowInput:
                        await HandleInputAsync(message, linked.Token)
                            .ConfigureAwait(false);
                        break;
                    case ControlMessageType.RemoteWindowDisconnect:
                        await HandleDisconnectAsync(message, linked.Token)
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
            Volatile.Write(ref stopped, 1);
            CompletePendingAsLost();
            if (peer is not null)
            {
                await peer.PeerDisconnectedAsync(
                    connection.PeerDeviceId,
                    CancellationToken.None).ConfigureAwait(false);
            }
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
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            Cancel();
            lifetimeCancellation.Dispose();
        }

        return ValueTask.CompletedTask;
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

        if (pending.Count >= MaximumPendingCommands)
        {
            return RemoteWindowControlDeliveryResult.NotDelivered;
        }

        var pendingState = new PendingState(
            binding.SessionId,
            binding.ActivityId,
            action);
        if (!pending.TryAdd(binding.CorrelationId, pendingState))
        {
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
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
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

            StateChanged?.Invoke(published);
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
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

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
}

public sealed class AuthenticatedRemoteWindowSessionHandler :
    IAuthenticatedControlSessionHandler,
    IAsyncDisposable
{
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IRemoteWindowControlPeer? peer;
    private readonly ConcurrentDictionary<DeviceId, Registration> sessions = new();
    private readonly TimeProvider timeProvider;
    private int disposed;

    public AuthenticatedRemoteWindowSessionHandler(
        TimeProvider? timeProvider = null) : this(null, timeProvider)
    {
    }

    public AuthenticatedRemoteWindowSessionHandler(
        IRemoteWindowControlPeer? peer,
        TimeProvider? timeProvider = null)
    {
        this.peer = peer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryGetChannel(
        DeviceId peerDeviceId,
        out IRemoteWindowControlChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration))
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public async ValueTask RunAsync(
        AuthenticatedTcpControlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsRemoteWindow(connection.ProtocolVersion))
        {
            throw new InvalidDataException(
                "The authenticated connection did not negotiate Remote Window protocol support.");
        }

        var session = new RemoteWindowControlSession(
            new AuthenticatedConnectionAdapter(connection),
            peer,
            timeProvider);
        var registration = new Registration(session);
        if (!sessions.TryAdd(connection.PeerIdentity.DeviceId, registration))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new InvalidDataException(
                "A second authenticated Remote Window session for this peer was rejected.");
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            await session.RunAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            sessions.TryRemove(
                new KeyValuePair<DeviceId, Registration>(
                    connection.PeerIdentity.DeviceId,
                    registration));
            await session.DisposeAsync().ConfigureAwait(false);
            registration.Completion.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        Registration[] active = sessions.Values.ToArray();
        foreach (Registration registration in active)
        {
            registration.Session.Cancel();
        }

        await Task.WhenAll(active.Select(static item => item.Completion.Task))
            .ConfigureAwait(false);
        lifetimeCancellation.Dispose();
    }

    private sealed class Registration(RemoteWindowControlSession session)
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowControlSession Session { get; } = session;
    }

    private sealed class AuthenticatedConnectionAdapter(
        AuthenticatedTcpControlConnection connection) : IRemoteWindowControlConnection
    {
        public DeviceId LocalDeviceId => connection.LocalDeviceId;

        public DeviceId PeerDeviceId => connection.PeerIdentity.DeviceId;

        public ProtocolVersion ProtocolVersion => connection.ProtocolVersion;

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            connection.ReceiveAsync(cancellationToken);

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) =>
            connection.SendAsync(message, cancellationToken);
    }
}
