using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowControlSessionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task RealAuthenticatedLoopbackAdmitsCurrentAuthorizedParticipant()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(hostId, "Host");
        using DeviceIdentity participantIdentity =
            DeviceIdentity.Generate(participantId, "Participant");
        var authorization = new MutableAuthorization();
        authorization.SetGrant(
            participantId,
            CapabilityGrant.Of(Capability.MirrorView));
        var inputBoundary = new ConfirmingInputBoundary();
        using var controller = new RemoteWindowSessionController(
            hostId,
            ActivityInstance.Active(
                ActivityDescriptor.Create(
                    activityId,
                    ActivityKind.Parse("workspace.note/v1"),
                    hostId,
                    "title-canary",
                    JsonSerializer.Serialize(new { text = "payload-canary" })),
                ActivityPlacement.On(hostId),
                revision: 1),
            new FixedClock(Now),
            authorization,
            new ConfirmingCaptureBoundary(),
            inputBoundary,
            new ConfirmingSessionBoundary(),
            TimeSpan.FromMinutes(1));
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                hostIdentity,
                new TrustRecord(
                    participantIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.MirrorView)),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.MirrorView)),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        await using var participantHandler =
            new AuthenticatedRemoteWindowSessionHandler(
                new FixedTimeProvider(Now));
        await using var hostHandler = new AuthenticatedRemoteWindowSessionHandler(
            new RemoteWindowControllerControlPeer(SessionId, controller),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task participantRun = participantHandler.RunAsync(
            participantConnection,
            stop.Token).AsTask();
        Task hostRun = hostHandler.RunAsync(hostConnection, stop.Token).AsTask();
        Assert.True(participantHandler.TryGetChannel(
            hostId,
            out IRemoteWindowControlChannel? channel));
        Assert.NotNull(channel);
        RemoteWindowAdmissionRequest request = RemoteWindowAdmissionRequest.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SessionId,
            activityId,
            hostId,
            participantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));

        RemoteWindowControlDeliveryResult delivered = await channel.AdmitAsync(
            request,
            CancellationToken.None);

        Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, delivered.Status);
        RemoteWindowParticipantState state = Assert.IsType<RemoteWindowParticipantState>(
            delivered.State);
        Assert.Equal(RemoteWindowControlOutcome.Applied, state.Outcome);
        Assert.Equal(MirrorParticipantRole.ViewOnly, state.EffectiveRole);
        Assert.Equal(2, state.ParticipantCount);
        Assert.Equal(RemoteWindowLifecycle.Active, state.Lifecycle);
        Assert.Equal(2, controller.Snapshot.Participants.Count);

        var stateChanged = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        channel.StateChanged += value => stateChanged.TrySetResult(value);
        Assert.True(hostHandler.TryGetChannel(
            participantId,
            out IRemoteWindowControlChannel? hostChannel));
        Assert.NotNull(hostChannel);
        RemoteWindowParticipantState published = RemoteWindowParticipantState.Create(
            CorrelationId.Parse("abababab-abab-abab-abab-abababababab"),
            state.SessionId,
            state.ActivityId,
            state.HostDeviceId,
            state.ParticipantDeviceId,
            RemoteWindowControlAction.StateChanged,
            RemoteWindowControlOutcome.Applied,
            "state_changed",
            state.Lifecycle,
            state.CaptureState,
            state.ParticipantCount,
            state.EffectiveRole,
            state.CurrentDriverDeviceId,
            state.DriverLeaseEpoch,
            state.DriverLeaseExpiresAt,
            state.ProtectionKind,
            state.Revision + 1);

        await hostChannel.PublishStateAsync(published, CancellationToken.None);
        RemoteWindowParticipantState observed = await stateChanged.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.Equal(published, observed);

        authorization.SetGrant(
            participantId,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        RemoteWindowControlDeliveryResult upgraded = await channel.AdmitAsync(
            RemoteWindowAdmissionRequest.Create(
                CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                SessionId,
                activityId,
                hostId,
                participantId,
                MirrorParticipantRole.DriverEligible,
                Now.AddSeconds(5)),
            CancellationToken.None);
        Assert.Equal(
            MirrorParticipantRole.DriverEligible,
            Assert.IsType<RemoteWindowParticipantState>(upgraded.State).EffectiveRole);

        RemoteWindowControlDeliveryResult transferred =
            await channel.RequestDriverAsync(
                RemoteWindowDriverRequest.Create(
                    CorrelationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    SessionId,
                    activityId,
                    hostId,
                    participantId,
                    expectedEpoch: 1,
                    TimeSpan.FromSeconds(30),
                    Now.AddSeconds(5)),
                CancellationToken.None);
        RemoteWindowParticipantState driverState =
            Assert.IsType<RemoteWindowParticipantState>(transferred.State);
        Assert.Equal(RemoteWindowControlOutcome.Applied, driverState.Outcome);
        Assert.Equal(participantId, driverState.CurrentDriverDeviceId);
        Assert.Equal(2, driverState.DriverLeaseEpoch);

        RemoteInputBatch input = RemoteInputBatch.Create(
            [RemoteInputEvent.PointerMove(0.25, 0.75)]);
        RemoteWindowControlDeliveryResult injected = await channel.SendInputAsync(
            RemoteWindowInputRequest.Create(
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                SessionId,
                activityId,
                hostId,
                participantId,
                leaseEpoch: 2,
                input,
                Now.AddSeconds(2)),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlOutcome.Applied,
            Assert.IsType<RemoteWindowParticipantState>(injected.State).Outcome);
        RemoteInputBatch injectedBatch = Assert.Single(inputBoundary.Batches);
        RemoteInputEvent injectedEvent = Assert.Single(injectedBatch.Events);
        Assert.Equal(RemoteInputEventKind.PointerMove, injectedEvent.Kind);
        Assert.Equal(0.25, injectedEvent.NormalizedX);
        Assert.Equal(0.75, injectedEvent.NormalizedY);

        _ = controller.ApplyProtectionSnapshot(
            new ProtectionSnapshot(ProtectionKind.SecureInput, Now, "test"));
        RemoteWindowControlDeliveryResult blocked = await channel.SendInputAsync(
            RemoteWindowInputRequest.Create(
                CorrelationId.Parse("12121212-1212-1212-1212-121212121212"),
                SessionId,
                activityId,
                hostId,
                participantId,
                leaseEpoch: 2,
                RemoteInputBatch.Create([RemoteInputEvent.HidKeyDown(0x07, 0x04)]),
                Now.AddSeconds(2)),
            CancellationToken.None);
        RemoteWindowParticipantState blockedState =
            Assert.IsType<RemoteWindowParticipantState>(blocked.State);
        Assert.Equal(RemoteWindowControlOutcome.Rejected, blockedState.Outcome);
        Assert.Equal("sensitive_surface", blockedState.ReasonCode);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, blockedState.Lifecycle);
        Assert.Single(inputBoundary.Batches);

        RemoteWindowControlDeliveryResult disconnected = await channel.DisconnectAsync(
            RemoteWindowDisconnectRequest.Create(
                CorrelationId.Parse("13131313-1313-1313-1313-131313131313"),
                SessionId,
                activityId,
                hostId,
                participantId,
                controller.Snapshot.Revision,
                "participant_closed",
                Now.AddSeconds(5)),
            CancellationToken.None);
        RemoteWindowParticipantState disconnectedState =
            Assert.IsType<RemoteWindowParticipantState>(disconnected.State);
        Assert.Equal(RemoteWindowControlOutcome.Applied, disconnectedState.Outcome);
        Assert.Null(disconnectedState.EffectiveRole);
        Assert.DoesNotContain(participantId, controller.Snapshot.Participants.Keys);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => participantRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hostRun);
    }

    [Fact]
    public async Task PublishedStateForUnknownSessionIsRejected()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        RemoteWindowParticipantState state = RemoteWindowParticipantState.Create(
            CorrelationId.Parse("abababab-abab-abab-abab-abababababab"),
            SessionId,
            activityId,
            hostId,
            participantId,
            RemoteWindowControlAction.StateChanged,
            RemoteWindowControlOutcome.Applied,
            "state_changed",
            RemoteWindowLifecycle.Active,
            RemoteWindowCaptureState.Capturing,
            participantCount: 2,
            MirrorParticipantRole.ViewOnly,
            hostId,
            driverLeaseEpoch: 1,
            Now.AddMinutes(1),
            ProtectionKind.Safe,
            revision: 1);
        ControlMessage message = RemoteWindowControlMessageCodec.CreateState(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            hostId,
            state,
            Now);
        var connection = new SingleMessageControlConnection(
            participantId,
            hostId,
            message);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            session.RunAsync().AsTask());
    }

    private sealed class MutableAuthorization : IMirrorAuthorizationSource
    {
        private readonly Dictionary<DeviceId, CapabilityGrant> grants = [];

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            grants.TryGetValue(peerDeviceId, out CapabilityGrant? grant)
                ? grant
                : CapabilityGrant.None;

        public void SetGrant(DeviceId peerDeviceId, CapabilityGrant grant) =>
            grants[peerDeviceId] = grant;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SingleMessageControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId,
        ControlMessage message) : IRemoteWindowControlConnection
    {
        private int read;

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowMinimumVersion;

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref read, 1) == 0
                ? ValueTask.FromResult(message)
                : ValueTask.FromException<ControlMessage>(
                    new InvalidOperationException("Only one message was configured."));

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class ConfirmingCaptureBoundary : IRemoteWindowCaptureBoundary
    {
        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("capture_started"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("capture_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class ConfirmingInputBoundary : IRemoteInputBoundary
    {
        public List<RemoteInputBatch> Batches { get; } = [];

        public ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("input_injected"));
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("input_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");
    }

    private sealed class ConfirmingSessionBoundary : ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");
    }
}
