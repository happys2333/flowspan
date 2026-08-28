using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
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
    public async Task RealAuthenticatedPreparationBootstrapsBindingBeforePublishedState()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(hostId, "Host");
        using DeviceIdentity participantIdentity =
            DeviceIdentity.Generate(participantId, "Participant");
        var hostCatalog = new InMemoryActivityCatalog();
        var participantCatalog = new InMemoryActivityCatalog();
        FlowspanNode hostNode = CreateNode(hostId, "Host", hostCatalog);
        FlowspanNode participantNode = CreateNode(
            participantId,
            "Participant",
            participantCatalog);
        participantNode.SetPeerGrant(
            hostId,
            CapabilityGrant.Of(Capability.ActivityOffer));
        var authorization = new MutableAuthorization();
        authorization.SetGrant(
            participantId,
            CapabilityGrant.Of(Capability.MirrorView));
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
            new ConfirmingInputBoundary(),
            new ConfirmingSessionBoundary(),
            TimeSpan.FromMinutes(1));
        var hostPeer = new RemoteWindowControllerControlPeer(SessionId, controller);
        var participantPreparation = new BlockingPreparationPeer(participantId);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version = ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        CapabilityGrant connectionCapabilities = CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.ActivityReceive,
            Capability.MirrorView);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                hostIdentity,
                new TrustRecord(
                    participantIdentity.PublicIdentity,
                    Now,
                    connectionCapabilities),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    connectionCapabilities),
                [version]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        await using var participantHandler = new AuthenticatedActivitySessionHandler(
            participantNode,
            new FixedTimeProvider(Now),
            remoteWindowPreparationPeer: participantPreparation);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            hostNode,
            new FixedTimeProvider(Now),
            hostPeer);
        using var stop = new CancellationTokenSource();
        Task participantRun = participantHandler.RunAsync(
            participantConnection,
            stop.Token).AsTask();
        Task hostRun = hostHandler.RunAsync(hostConnection, stop.Token).AsTask();
        Assert.True(hostHandler.TryGetRemoteWindowPreparationChannel(
            participantId,
            out IRemoteWindowPreparationChannel? preparationChannel));
        Assert.NotNull(preparationChannel);
        Assert.True(participantHandler.TryGetRemoteWindowChannel(
            hostId,
            out IRemoteWindowControlChannel? participantChannel));
        Assert.NotNull(participantChannel);
        var admissionObserved = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        participantChannel.StateChanged += value =>
            admissionObserved.TrySetResult(value);
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            correlationId,
            SessionId,
            activityId,
            hostId,
            participantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));

        Task<RemoteWindowPreparationDeliveryResult> preparing =
            preparationChannel.PrepareAsync(request, CancellationToken.None).AsTask();
        await participantPreparation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(preparing.IsCompleted);
        Assert.Equal(RemoteWindowLifecycle.Idle, controller.Snapshot.Lifecycle);
        Assert.True(hostHandler.TryGetChannel(
            participantId,
            out IActivityChannel? activityChannel));
        Assert.NotNull(activityChannel);
        ActivityDescriptor transferred = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            hostId,
            "Concurrent handoff",
            JsonSerializer.Serialize(new { text = "dispatcher canary" }));
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Now.AddSeconds(5)),
            transferred,
            ActivityPlacement.On(participantId, "desktop"));

        ActivityDeliveryResult activityDelivery = await activityChannel.SendAsync(
                hostId,
                offer,
                CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, activityDelivery.Status);
        Assert.True(participantCatalog.TryGet(transferred.Id, out _));
        Assert.False(preparing.IsCompleted);

        participantPreparation.Release.TrySetResult();
        RemoteWindowPreparationDeliveryResult prepared = await preparing.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, prepared.Status);
        Assert.Equal(
            RemoteWindowPreparationOutcome.Ready,
            Assert.IsType<RemoteWindowPreparationResponse>(prepared.Response).Outcome);
        Assert.Equal(RemoteWindowLifecycle.Idle, controller.Snapshot.Lifecycle);

        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        RemoteWindowParticipantState admission = await hostPeer.AdmitAsync(
            RemoteWindowAdmissionRequest.Create(
                correlationId,
                SessionId,
                activityId,
                hostId,
                participantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(5)),
            CancellationToken.None);
        await preparationChannel.PublishAdmissionStateAsync(
            admission,
            CancellationToken.None);

        await participantPreparation.AdmissionStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        ActivityDescriptor afterAdmission = ActivityDescriptor.Create(
            ActivityId.Parse("14141414-1414-1414-1414-141414141414"),
            ActivityKind.Parse("workspace.note/v1"),
            hostId,
            "Finalization handoff",
            JsonSerializer.Serialize(new { text = "finalization canary" }));
        ActivityTransferOffer afterAdmissionOffer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.Parse("15151515-1515-1515-1515-151515151515"),
                CorrelationId.Parse("16161616-1616-1616-1616-161616161616"),
                Now.AddSeconds(5)),
            afterAdmission,
            ActivityPlacement.On(participantId, "secondary"));
        try
        {
            ActivityDeliveryResult duringFinalization = await activityChannel.SendAsync(
                    hostId,
                    afterAdmissionOffer,
                    CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(
                ActivityDeliveryStatus.Acknowledged,
                duringFinalization.Status);
            Assert.True(participantCatalog.TryGet(afterAdmission.Id, out _));
        }
        finally
        {
            participantPreparation.ReleaseAdmission.TrySetResult();
        }

        Assert.Equal(
            admission,
            await admissionObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, controller.Snapshot.Participants.Count);

        var changedObserved = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        participantChannel.StateChanged += value =>
        {
            if (value.Action is RemoteWindowControlAction.StateChanged)
            {
                changedObserved.TrySetResult(value);
            }
        };
        Assert.True(hostHandler.TryGetRemoteWindowChannel(
            participantId,
            out IRemoteWindowControlChannel? hostChannel));
        RemoteWindowParticipantState changed = RemoteWindowParticipantState.Create(
            CorrelationId.From(Guid.NewGuid()),
            admission.SessionId,
            admission.ActivityId,
            admission.HostDeviceId,
            admission.ParticipantDeviceId,
            RemoteWindowControlAction.StateChanged,
            RemoteWindowControlOutcome.Applied,
            "state_changed",
            admission.Lifecycle,
            admission.CaptureState,
            admission.ParticipantCount,
            admission.EffectiveRole,
            admission.CurrentDriverDeviceId,
            admission.DriverLeaseEpoch,
            admission.DriverLeaseExpiresAt,
            admission.ProtectionKind,
            admission.Revision + 1);
        await hostChannel!.PublishStateAsync(changed, CancellationToken.None);
        Assert.Equal(
            changed,
            await changedObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            participantRun.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            hostRun.WaitAsync(TimeSpan.FromSeconds(5)));
    }

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
            new AuthenticatedActivitySessionHandler(
                CreateNode(
                    participantId,
                    "Participant",
                    new InMemoryActivityCatalog()),
                new FixedTimeProvider(Now));
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            new FixedTimeProvider(Now),
            new RemoteWindowControllerControlPeer(SessionId, controller));
        using var stop = new CancellationTokenSource();
        Task participantRun = participantHandler.RunAsync(
            participantConnection,
            stop.Token).AsTask();
        Task hostRun = hostHandler.RunAsync(hostConnection, stop.Token).AsTask();
        Assert.True(participantHandler.TryGetRemoteWindowChannel(
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
        Assert.True(hostHandler.TryGetRemoteWindowChannel(
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
    public async Task AuthenticatedLoopbackCarriesActivityAndRemoteWindowOnOneConnection()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId remoteWindowActivityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(hostId, "Host");
        using DeviceIdentity participantIdentity =
            DeviceIdentity.Generate(participantId, "Participant");
        var hostCatalog = new InMemoryActivityCatalog();
        var participantCatalog = new InMemoryActivityCatalog();
        FlowspanNode hostNode = CreateNode(hostId, "Host", hostCatalog);
        FlowspanNode participantNode = CreateNode(
            participantId,
            "Participant",
            participantCatalog);
        hostNode.SetPeerGrant(
            participantId,
            CapabilityGrant.Of(Capability.ActivityOffer));

        ActivityDescriptor transferredDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            participantId,
            "Portable note",
            JsonSerializer.Serialize(new { text = "portable secret" }));
        Assert.True(participantNode.AddLocalActivity(ActivityInstance.Active(
            transferredDescriptor,
            ActivityPlacement.On(participantId))));

        var authorization = new MutableAuthorization();
        authorization.SetGrant(
            participantId,
            CapabilityGrant.Of(Capability.MirrorView));
        using var controller = new RemoteWindowSessionController(
            hostId,
            ActivityInstance.Active(
                ActivityDescriptor.Create(
                    remoteWindowActivityId,
                    ActivityKind.Parse("workspace.note/v1"),
                    hostId,
                    "Shared host note",
                    JsonSerializer.Serialize(new { text = "host secret" })),
                ActivityPlacement.On(hostId),
                revision: 1),
            new FixedClock(Now),
            authorization,
            new ConfirmingCaptureBoundary(),
            new ConfirmingInputBoundary(),
            new ConfirmingSessionBoundary(),
            TimeSpan.FromMinutes(1));
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        CapabilityGrant connectionCapabilities = CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.ActivityReceive,
            Capability.MirrorView);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                hostIdentity,
                new TrustRecord(
                    participantIdentity.PublicIdentity,
                    Now,
                    connectionCapabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    connectionCapabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        await using var participantHandler = new AuthenticatedActivitySessionHandler(
            participantNode,
            new FixedTimeProvider(Now));
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            hostNode,
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: new RemoteWindowControllerControlPeer(
                SessionId,
                controller));
        using var stop = new CancellationTokenSource();
        Task participantRun = participantHandler.RunAsync(
            participantConnection,
            stop.Token).AsTask();
        Task hostRun = hostHandler.RunAsync(hostConnection, stop.Token).AsTask();
        Assert.True(participantHandler.TryGetChannel(
            hostId,
            out IActivityChannel? activityChannel));
        Assert.NotNull(activityChannel);
        Assert.True(participantHandler.TryGetRemoteWindowChannel(
            hostId,
            out IRemoteWindowControlChannel? remoteWindowChannel));
        Assert.NotNull(remoteWindowChannel);

        OperationContext handoffContext = OperationContext.Create(
            OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Now.AddSeconds(30));
        OperationReceipt receipt = await participantNode.HandoffAsync(
            transferredDescriptor.Id,
            activityChannel,
            "desktop",
            handoffContext);
        RemoteWindowControlDeliveryResult admitted =
            await remoteWindowChannel.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    SessionId,
                    remoteWindowActivityId,
                    hostId,
                    participantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(5)),
                CancellationToken.None);

        Assert.True(receipt.IsSuccess);
        Assert.True(hostCatalog.TryGet(transferredDescriptor.Id, out _));
        Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, admitted.Status);
        Assert.Equal(
            MirrorParticipantRole.ViewOnly,
            Assert.IsType<RemoteWindowParticipantState>(admitted.State).EffectiveRole);
        Assert.Contains(participantId, controller.Snapshot.Participants.Keys);

        authorization.SetGrant(participantId, CapabilityGrant.None);
        RemoteWindowControlDeliveryResult denied =
            await remoteWindowChannel.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    CorrelationId.Parse("12121212-1212-1212-1212-121212121212"),
                    SessionId,
                    remoteWindowActivityId,
                    hostId,
                    participantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(5)),
                CancellationToken.None);
        RemoteWindowParticipantState deniedState =
            Assert.IsType<RemoteWindowParticipantState>(denied.State);
        Assert.Equal(RemoteWindowControlOutcome.Rejected, deniedState.Outcome);
        Assert.Equal("mirror_view_denied", deniedState.ReasonCode);
        Assert.True(participantHandler.TryGetChannel(hostId, out _));

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => participantRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hostRun);
    }

    [Fact]
    public async Task ActivityShapedRemoteWindowMessageIsFatalAndDrainsBothRoutes()
    {
        DeviceId localId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId peerId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(localId, "Local");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(peerId, "Peer");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.ActivityReceive,
            Capability.MirrorView);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                localIdentity,
                new TrustRecord(peerIdentity.PublicIdentity, Now, capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection peerConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                peerIdentity,
                new TrustRecord(localIdentity.PublicIdentity, Now, capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection localConnection = await accepting;
        var catalog = new InMemoryActivityCatalog();
        FlowspanNode localNode = CreateNode(localId, "Local", catalog);
        await using var handler = new AuthenticatedActivitySessionHandler(
            localNode,
            new FixedTimeProvider(Now));
        Task run = handler.RunAsync(localConnection).AsTask();
        Assert.True(handler.TryGetChannel(peerId, out IActivityChannel? _));
        Assert.True(handler.TryGetRemoteWindowChannel(
            peerId,
            out IRemoteWindowControlChannel? _));
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            peerId,
            "Cross-route canary",
            JsonSerializer.Serialize(new { text = "must-not-route-as-activity" }));
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Now.AddSeconds(30)),
            descriptor,
            ActivityPlacement.On(localId, "desktop"));
        ControlMessage activity = ActivityControlMessageCodec.CreateTransfer(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            peerId,
            offer,
            Now);
        ControlMessage crossRouted = ControlMessage.Create(
            activity.Version,
            ControlMessageType.RemoteWindowAdmission,
            Guid.Parse("abababab-abab-abab-abab-abababababab"),
            activity.CorrelationId,
            activity.SenderDeviceId,
            activity.SentAt,
            TimeSpan.FromMilliseconds(activity.TimeToLiveMilliseconds),
            activity.Body.GetRawText());

        await peerConnection.SendAsync(crossRouted);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(handler.TryGetChannel(peerId, out _));
        Assert.False(handler.TryGetRemoteWindowChannel(peerId, out _));
        Assert.False(catalog.TryGet(descriptor.Id, out _));
    }

    [Fact]
    public async Task PeerRevocationDrainsBothRoutesAndPendingRemoteWindowCommand()
    {
        DeviceId participantId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId hostId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using DeviceIdentity participantIdentity =
            DeviceIdentity.Generate(participantId, "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(hostId, "Host");
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            hostIdentity.PublicIdentity,
            Now,
            capabilities));
        await using var trust = new TrustSessionCoordinator(trustStore);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        var catalog = new InMemoryActivityCatalog();
        FlowspanNode participantNode = CreateNode(
            participantId,
            "Participant",
            catalog);
        await using var handler = new AuthenticatedActivitySessionHandler(
            participantNode,
            new FixedTimeProvider(Now));
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                hostId,
                capabilities,
                [ProtocolFeatures.RemoteWindowMinimumVersion],
                capabilityMatch: CapabilityRequirementMatch.Any),
            participantIdentity,
            trust,
            new FixedCandidateSource(CreateCandidate(hostIdentity, endpoint.Port)),
            new SystemAuthenticatedTcpConnector(),
            handler,
            new FixedTimeProvider(Now));

        Task<PeerSessionAttemptResult> running = attempt.RunAsync().AsTask();
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        IRemoteWindowControlChannel remoteWindowChannel =
            await WaitForRemoteWindowChannelAsync(handler, hostId);
        Assert.True(handler.TryGetChannel(hostId, out IActivityChannel? _));
        Task<RemoteWindowControlDeliveryResult> admission =
            remoteWindowChannel.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    SessionId,
                    activityId,
                    hostId,
                    participantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(5)),
                CancellationToken.None).AsTask();
        ControlMessage sent = await hostConnection.ReceiveAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ControlMessageType.RemoteWindowAdmission, sent.Type);

        bool revoked = await trust.RevokePeerAsync(hostId)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        PeerSessionAttemptResult result = await running.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.True(revoked);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admission);
        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result.StopReason);
        Assert.False(handler.TryGetChannel(hostId, out _));
        Assert.False(handler.TryGetRemoteWindowChannel(hostId, out _));
        Assert.False(trustStore.TryGet(hostId, out _));
    }

    [Fact]
    public async Task ReconnectReplacesBothRoutesWithoutRetainingParticipantAuthority()
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
        using var controller = new RemoteWindowSessionController(
            hostId,
            ActivityInstance.Active(
                ActivityDescriptor.Create(
                    activityId,
                    ActivityKind.Parse("workspace.note/v1"),
                    hostId,
                    "Reconnect source",
                    JsonSerializer.Serialize(new { text = "host secret" })),
                ActivityPlacement.On(hostId),
                revision: 1),
            new FixedClock(Now),
            authorization,
            new ConfirmingCaptureBoundary(),
            new ConfirmingInputBoundary(),
            new ConfirmingSessionBoundary(),
            TimeSpan.FromMinutes(1));
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        FlowspanNode hostNode = CreateNode(
            hostId,
            "Host",
            new InMemoryActivityCatalog());
        FlowspanNode participantNode = CreateNode(
            participantId,
            "Participant",
            new InMemoryActivityCatalog());
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            hostNode,
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: new RemoteWindowControllerControlPeer(
                SessionId,
                controller));
        await using var participantHandler = new AuthenticatedActivitySessionHandler(
            participantNode,
            new FixedTimeProvider(Now));
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.ActivityReceive,
            Capability.MirrorView);
        CorrelationId[] correlations =
        [
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        ];

        for (int generation = 0; generation < correlations.Length; generation++)
        {
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
                        capabilities),
                    [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
            await using AuthenticatedTcpControlConnection participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        capabilities),
                    [ProtocolFeatures.RemoteWindowMinimumVersion]);
            await using AuthenticatedTcpControlConnection hostConnection = await accepting;
            using var stop = new CancellationTokenSource();
            Task participantRun = participantHandler.RunAsync(
                participantConnection,
                stop.Token).AsTask();
            Task hostRun = hostHandler.RunAsync(
                hostConnection,
                stop.Token).AsTask();
            IRemoteWindowControlChannel channel =
                await WaitForRemoteWindowChannelAsync(participantHandler, hostId);
            Assert.True(participantHandler.TryGetChannel(
                hostId,
                out IActivityChannel? _));

            RemoteWindowControlDeliveryResult admitted = await channel.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    correlations[generation],
                    SessionId,
                    activityId,
                    hostId,
                    participantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(5)),
                CancellationToken.None);

            Assert.Equal(
                RemoteWindowControlOutcome.Applied,
                Assert.IsType<RemoteWindowParticipantState>(admitted.State).Outcome);
            Assert.Contains(participantId, controller.Snapshot.Participants.Keys);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => participantRun);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hostRun);
            Assert.DoesNotContain(participantId, controller.Snapshot.Participants.Keys);
            Assert.False(participantHandler.TryGetChannel(hostId, out _));
            Assert.False(participantHandler.TryGetRemoteWindowChannel(hostId, out _));
        }
    }

    [Fact]
    public async Task HandlerDisposalWaitsForRemoteWindowPeerDisconnectDrain()
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
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        var peer = new BlockingDisconnectRemoteWindowPeer(
            SessionId,
            activityId,
            hostId);
        var handler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: peer);
        Task run = handler.RunAsync(hostConnection).AsTask();
        _ = await WaitForRemoteWindowChannelAsync(handler, participantId);
        Assert.True(handler.TryGetChannel(participantId, out IActivityChannel? _));
        Assert.Contains(participantId, handler.GetSceneParticipantDeviceIds());

        Task disposing = handler.DisposeAsync().AsTask();
        DeviceId disconnectedPeer = await peer.DisconnectStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(participantId, disconnectedPeer);
        Assert.False(disposing.IsCompleted);
        Assert.False(handler.TryGetChannel(participantId, out _));
        Assert.False(handler.TryGetRemoteWindowChannel(participantId, out _));
        Assert.Empty(handler.GetSceneParticipantDeviceIds());

        peer.ReleaseDisconnect.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(handler.TryGetChannel(participantId, out _));
        Assert.False(handler.TryGetRemoteWindowChannel(participantId, out _));
        await handler.DisposeAsync();
    }

    [Fact]
    public async Task DisposalBeforeDispatchDoesNotNotifyRemoteWindowPeers()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var connection = new BlockingPendingRegistrationConnection(
            hostId,
            participantId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        var controlPeer = new SignalingDisconnectRemoteWindowPeer(
            SessionId,
            activityId,
            hostId);
        var preparationPeer = new BlockingPreparationPeer(hostId);
        await using var session = new RemoteWindowControlSession(
            connection,
            controlPeer,
            new FixedTimeProvider(Now),
            preparationPeer);

        await session.DisposeAsync();

        Assert.Equal(0, controlPeer.DisconnectCount);
        Assert.Equal(0, preparationPeer.DisconnectCount);
    }

    [Fact]
    public async Task StartedSessionDisposalNotifiesRemoteWindowPeersExactlyOnce()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var connection = new BlockingPendingRegistrationConnection(
            hostId,
            participantId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        var controlPeer = new SignalingDisconnectRemoteWindowPeer(
            SessionId,
            activityId,
            hostId);
        var preparationPeer = new BlockingPreparationPeer(hostId);
        await using var session = new RemoteWindowControlSession(
            connection,
            controlPeer,
            new FixedTimeProvider(Now),
            preparationPeer);
        session.StartDispatch();

        Task firstDisposal = session.DisposeAsync().AsTask();
        Task secondDisposal = session.DisposeAsync().AsTask();
        await Task.WhenAll(firstDisposal, secondDisposal);

        Assert.Equal(1, controlPeer.DisconnectCount);
        Assert.Equal(1, preparationPeer.DisconnectCount);
    }

    [Fact]
    public async Task RejectedDuplicateSessionDoesNotDisconnectActivePeer()
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
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
        var peer = new SignalingDisconnectRemoteWindowPeer(
            SessionId,
            activityId,
            hostId);
        await using var handler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: peer);
        (AuthenticatedTcpControlConnection firstParticipant,
            AuthenticatedTcpControlConnection firstHost) = await CreatePairAsync();
        (AuthenticatedTcpControlConnection secondParticipant,
            AuthenticatedTcpControlConnection secondHost) = await CreatePairAsync();
        await using (firstParticipant)
        await using (firstHost)
        await using (secondParticipant)
        await using (secondHost)
        {
            Task firstRun = handler.RunAsync(firstHost).AsTask();
            IRemoteWindowControlChannel activeChannel =
                await WaitForRemoteWindowChannelAsync(handler, participantId);
            Task secondRun = handler.RunAsync(secondHost).AsTask();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                secondRun.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, peer.DisconnectCount);
            Assert.True(handler.TryGetRemoteWindowChannel(
                participantId,
                out IRemoteWindowControlChannel? currentChannel));
            Assert.Same(activeChannel, currentChannel);

            await handler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                firstRun.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, peer.DisconnectCount);
        }

        async Task<(AuthenticatedTcpControlConnection Participant,
            AuthenticatedTcpControlConnection Host)> CreatePairAsync()
        {
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
                        capabilities),
                    [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
            AuthenticatedTcpControlConnection participant =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        capabilities),
                    [ProtocolFeatures.RemoteWindowMinimumVersion]);
            try
            {
                return (participant, await accepting);
            }
            catch
            {
                await participant.DisposeAsync();
                throw;
            }
        }
    }

    [Fact]
    public async Task HandlerDisposalWaitsForFinalRouteChangeNotification()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(hostId, "Host");
        using DeviceIdentity participantIdentity =
            DeviceIdentity.Generate(participantId, "Participant");
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        var handler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            new FixedTimeProvider(Now));
        var finalNotificationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalNotification = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int notificationCount = 0;
        handler.Changed += () =>
        {
            if (Interlocked.Increment(ref notificationCount) == 2)
            {
                finalNotificationStarted.TrySetResult();
                releaseFinalNotification.Task.GetAwaiter().GetResult();
            }
        };
        Task run = handler.RunAsync(hostConnection).AsTask();
        _ = await WaitForRemoteWindowChannelAsync(handler, participantId);

        Task disposing = handler.DisposeAsync().AsTask();
        await finalNotificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(disposing.IsCompleted);
        releaseFinalNotification.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
        await handler.DisposeAsync();
    }

    [Fact]
    public async Task ReentrantHandlerDisposalDoesNotWaitForItsOwnDisconnectCallback()
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
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        var peer = new ReentrantDisposalRemoteWindowPeer(
            SessionId,
            activityId,
            hostId);
        var handler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: peer);
        peer.Handler = handler;
        using var stop = new CancellationTokenSource();
        Task run = handler.RunAsync(hostConnection, stop.Token).AsTask();
        _ = await WaitForRemoteWindowChannelAsync(handler, participantId);

        stop.Cancel();
        await peer.DisconnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await peer.ReentrantDisposalReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task externalDisposal = handler.DisposeAsync().AsTask();

        Assert.False(externalDisposal.IsCompleted);
        peer.ReleaseDisconnect.TrySetResult();
        await externalDisposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
        await handler.DisposeAsync();
    }

    [Fact]
    public async Task HandlerDisposalRejectsAnAdmissionPausedDuringSessionConstruction()
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
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        var peer = new BlockingHostIdentityRemoteWindowPeer(
            SessionId,
            activityId,
            hostId);
        var handler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: peer);
        int changedCount = 0;
        handler.Changed += () => Interlocked.Increment(ref changedCount);
        peer.BlockNextHostDeviceIdRead();
        Task run = Task.Run(() => handler.RunAsync(hostConnection).AsTask());
        await peer.HostDeviceIdReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await handler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        peer.ReleaseHostDeviceIdRead.TrySetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref changedCount));
        Assert.Empty(handler.GetConnectedPeers());
        Assert.False(handler.TryGetChannel(participantId, out _));
        Assert.False(handler.TryGetRemoteWindowChannel(participantId, out _));
    }

    [Fact]
    public async Task CopiedDispatchContextDoesNotBypassExternalDisposalDrain()
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
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        var peer = new CopiedContextDisposalRemoteWindowPeer(
            SessionId,
            activityId,
            hostId,
            participantId);
        var handler = new AuthenticatedActivitySessionHandler(
            CreateNode(hostId, "Host", new InMemoryActivityCatalog()),
            timeProvider: new FixedTimeProvider(Now),
            remoteWindowPeer: peer);
        peer.Handler = handler;
        Task run = handler.RunAsync(hostConnection).AsTask();
        _ = await WaitForRemoteWindowChannelAsync(handler, participantId);
        RemoteWindowAdmissionRequest admission = RemoteWindowAdmissionRequest.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SessionId,
            activityId,
            hostId,
            participantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));

        await participantConnection.SendAsync(
            RemoteWindowControlMessageCodec.CreateAdmission(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                participantId,
                admission,
                Now));
        ControlMessage response = await participantConnection.ReceiveAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ControlMessageType.RemoteWindowState, response.Type);
        await peer.CopiedContextReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var driverRequest = RemoteWindowDriverRequest.Create(
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            SessionId,
            activityId,
            hostId,
            participantId,
            expectedEpoch: 1,
            TimeSpan.FromSeconds(30),
            Now.AddSeconds(5));
        await participantConnection.SendAsync(
            RemoteWindowControlMessageCodec.CreateDriverRequest(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                participantId,
                driverRequest,
                Now));
        await peer.NextDispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        peer.ReleaseCopiedContext.TrySetResult();

        bool completedSynchronously =
            await peer.DisposalCompletionState.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(completedSynchronously);
        await peer.DisconnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(peer.DisposalReturned.Task.IsCompleted);

        peer.ReleaseDisconnect.TrySetResult();
        await peer.DisposalReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
        await handler.DisposeAsync();
    }

    [Fact]
    public async Task HandlerDisposalCancelsRemoteWindowPendingBehindBlockedActivity()
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
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.MirrorView);
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
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]).AsTask();
        await using AuthenticatedTcpControlConnection participantConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                participantIdentity,
                new TrustRecord(
                    hostIdentity.PublicIdentity,
                    Now,
                    capabilities),
                [ProtocolFeatures.RemoteWindowMinimumVersion]);
        await using AuthenticatedTcpControlConnection hostConnection = await accepting;
        var activityPeer = new BlockingCancellationIgnoringActivityPeer(hostId);
        var handler = new AuthenticatedActivitySessionHandler(
            activityPeer,
            new FixedTimeProvider(Now));
        Task run = handler.RunAsync(hostConnection).AsTask();
        IRemoteWindowControlChannel remoteWindowChannel =
            await WaitForRemoteWindowChannelAsync(handler, participantId);
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            activityId,
            ActivityKind.Parse("workspace.note/v1"),
            participantId,
            "Blocked Activity",
            JsonSerializer.Serialize(new { text = "blocked" }));
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Now.AddSeconds(30)),
            descriptor,
            ActivityPlacement.On(hostId, "desktop"));
        await participantConnection.SendAsync(
            ActivityControlMessageCodec.CreateTransfer(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                participantId,
                offer,
                Now));
        await activityPeer.ReceiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<RemoteWindowControlDeliveryResult> admission =
            remoteWindowChannel.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    SessionId,
                    activityId,
                    participantId,
                    hostId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(10)),
                CancellationToken.None).AsTask();
        ControlMessage sent = await participantConnection.ReceiveAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ControlMessageType.RemoteWindowAdmission, sent.Type);

        Task disposing = handler.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                admission.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            activityPeer.ReleaseReceive.TrySetResult();
        }

        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task LaterActivityWaitsForEarlierRemoteWindowHostOperation()
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId participantId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId remoteWindowActivityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        RemoteWindowAdmissionRequest admission = RemoteWindowAdmissionRequest.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SessionId,
            remoteWindowActivityId,
            hostId,
            participantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            participantId,
            "Ordered Activity",
            JsonSerializer.Serialize(new { text = "must-wait" }));
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Now.AddSeconds(30)),
            descriptor,
            ActivityPlacement.On(hostId, "desktop"));
        var connection = new ScriptedDispatcherConnection(
            RemoteWindowControlMessageCodec.CreateAdmission(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                participantId,
                admission,
                Now),
            ActivityControlMessageCodec.CreateTransfer(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                participantId,
                offer,
                Now));
        await using var dispatcher = new AuthenticatedControlSessionDispatcher(
            hostId,
            participantId,
            ProtocolFeatures.RemoteWindowMinimumVersion,
            connection.ReceiveAsync,
            ScriptedDispatcherConnection.SendAsync);
        var hostCatalog = new InMemoryActivityCatalog();
        FlowspanNode hostNode = CreateNode(hostId, "Host", hostCatalog);
        hostNode.SetPeerGrant(
            participantId,
            CapabilityGrant.Of(Capability.ActivityOffer));
        var remotePeer = new BlockingFailAdmissionRemoteWindowPeer(
            SessionId,
            remoteWindowActivityId,
            hostId);
        await using var activitySession = new ActivityControlSession(
            dispatcher.ActivityConnection,
            hostNode,
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            new FixedTimeProvider(Now));
        await using var remoteWindowSession = new RemoteWindowControlSession(
            Assert.IsAssignableFrom<IRemoteWindowControlConnection>(
                dispatcher.RemoteWindowConnection),
            remotePeer,
            new FixedTimeProvider(Now));
        Task run = dispatcher.RunAsync(
            activitySession,
            remoteWindowSession,
            static () => NullDisposable.Instance).AsTask();
        await remotePeer.AdmissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, connection.ReadCount);
        Assert.False(hostCatalog.TryGet(descriptor.Id, out _));
        remotePeer.ReleaseAdmission.TrySetResult();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, connection.ReadCount);
        Assert.False(hostCatalog.TryGet(descriptor.Id, out _));
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

    [Fact]
    public async Task CommandPausedBeforePendingRegistrationCannotOutliveSessionStop()
    {
        DeviceId participantId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId hostId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var connection = new BlockingPendingRegistrationConnection(
            participantId,
            hostId);
        var peer = new SignalingDisconnectRemoteWindowPeer(
            SessionId,
            activityId,
            participantId);
        await using var session = new RemoteWindowControlSession(
            connection,
            peer,
            new FixedTimeProvider(Now));
        using var runCancellation = new CancellationTokenSource();
        using var commandCancellation = new CancellationTokenSource();
        Task run = session.RunAsync(runCancellation.Token).AsTask();
        await connection.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connection.BlockNextLocalDeviceIdRead();
        Task<RemoteWindowControlDeliveryResult> sending = Task.Run(async () =>
            await session.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    SessionId,
                    activityId,
                    hostId,
                    participantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(10)),
                commandCancellation.Token));
        await connection.LocalDeviceIdReadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        runCancellation.Cancel();
        await peer.DisconnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connection.ReleaseLocalDeviceIdRead.TrySetResult();

        try
        {
            RemoteWindowControlDeliveryResult result = await sending.WaitAsync(
                TimeSpan.FromSeconds(1));
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                result.Status);
            Assert.Equal(0, connection.SentCount);
        }
        finally
        {
            commandCancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CommandPausedAfterPendingRegistrationDoesNotSendAfterSessionStop()
    {
        DeviceId participantId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId hostId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var connection = new BlockingPendingRegistrationConnection(
            participantId,
            hostId);
        var peer = new SignalingDisconnectRemoteWindowPeer(
            SessionId,
            activityId,
            participantId);
        await using var session = new RemoteWindowControlSession(
            connection,
            peer,
            new FixedTimeProvider(Now));
        using var runCancellation = new CancellationTokenSource();
        using var commandCancellation = new CancellationTokenSource();
        Task run = session.RunAsync(runCancellation.Token).AsTask();
        await connection.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connection.BlockSecondLocalDeviceIdRead();
        Task<RemoteWindowControlDeliveryResult> sending = Task.Run(async () =>
            await session.AdmitAsync(
                RemoteWindowAdmissionRequest.Create(
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    SessionId,
                    activityId,
                    hostId,
                    participantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(10)),
                commandCancellation.Token));
        await connection.LocalDeviceIdReadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        runCancellation.Cancel();
        try
        {
            await peer.DisconnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection.ReleaseLocalDeviceIdRead.TrySetResult();

            RemoteWindowControlDeliveryResult result = await sending.WaitAsync(
                TimeSpan.FromSeconds(1));
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                result.Status);
            Assert.Equal(0, connection.SentCount);
        }
        finally
        {
            connection.ReleaseLocalDeviceIdRead.TrySetResult();
            commandCancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class BlockingPreparationPeer(DeviceId participantDeviceId) :
        IRemoteWindowPreparationPeer
    {
        private int disconnectCount;

        public TaskCompletionSource AdmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public int DisconnectCount => Volatile.Read(ref disconnectCount);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseAdmission { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");
        }

        public async ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            AdmissionStarted.TrySetResult();
            await ReleaseAdmission.Task.WaitAsync(cancellationToken);
        }

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref disconnectCount);
            return ValueTask.CompletedTask;
        }
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

    private static FlowspanNode CreateNode(
        DeviceId deviceId,
        string name,
        InMemoryActivityCatalog catalog) => new(
        deviceId,
        name,
        new FixedClock(Now),
        catalog,
        new InMemoryOperationJournal(),
        new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
        NullReceiptSink.Instance);

    private static VerifiedPeerConnectionCandidate CreateCandidate(
        DeviceIdentity identity,
        int port)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            identity,
            port,
            [ProtocolFeatures.RemoteWindowMinimumVersion],
            Now,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x11, SignedDiscoveryOffer.NonceLength)
                .ToArray());
        return VerifiedPeerConnectionCandidate.Create(
            new IPEndPoint(IPAddress.Loopback, port),
            offer,
            identity.PublicIdentity,
            Now);
    }

    private static async Task<IRemoteWindowControlChannel>
        WaitForRemoteWindowChannelAsync(
            AuthenticatedActivitySessionHandler handler,
            DeviceId peerDeviceId)
    {
        if (handler.TryGetRemoteWindowChannel(
            peerDeviceId,
            out IRemoteWindowControlChannel? existing))
        {
            return existing
                ?? throw new InvalidOperationException(
                    "The Remote Window channel disappeared during registration.");
        }

        var registered = new TaskCompletionSource<IRemoteWindowControlChannel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void CaptureRegistration()
        {
            if (handler.TryGetRemoteWindowChannel(
                    peerDeviceId,
                    out IRemoteWindowControlChannel? channel)
                && channel is not null)
            {
                registered.TrySetResult(channel);
            }
        }

        handler.Changed += CaptureRegistration;
        try
        {
            CaptureRegistration();
            return await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            handler.Changed -= CaptureRegistration;
        }
    }

    private sealed class FixedCandidateSource(
        VerifiedPeerConnectionCandidate candidate) : IPeerConnectionCandidateSource
    {
        public bool TryGet(
            DeviceId peerDeviceId,
            [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? result)
        {
            result = peerDeviceId == candidate.Offer.DeviceId ? candidate : null;
            return result is not null;
        }
    }

    private sealed class BlockingDisconnectRemoteWindowPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        public ActivityId ActivityId { get; } = activityId;

        public TaskCompletionSource<DeviceId> DisconnectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public TaskCompletionSource ReleaseDisconnect { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public async ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
        {
            DisconnectStarted.TrySetResult(peerDeviceId);
            await ReleaseDisconnect.Task.WaitAsync(cancellationToken);
        }

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException(
                    "No Remote Window command is expected during disposal."));
    }

    private sealed class BlockingPendingRegistrationConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId,
        ProtocolVersion? protocolVersion = null) : IRemoteWindowControlConnection
    {
        private int localDeviceIdReadsUntilBlock;
        private int sentCount;

        public DeviceId LocalDeviceId
        {
            get
            {
                int readsUntilBlock = Volatile.Read(
                    ref localDeviceIdReadsUntilBlock);
                if (readsUntilBlock > 0
                    && Interlocked.Decrement(
                        ref localDeviceIdReadsUntilBlock) == 0)
                {
                    LocalDeviceIdReadStarted.TrySetResult();
                    ReleaseLocalDeviceIdRead.Task.GetAwaiter().GetResult();
                }

                return localDeviceId;
            }
        }

        public TaskCompletionSource LocalDeviceIdReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = protocolVersion
            ?? ProtocolFeatures.RemoteWindowMinimumVersion;

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseLocalDeviceIdRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SentCount => Volatile.Read(ref sentCount);

        public void BlockNextLocalDeviceIdRead() =>
            Volatile.Write(ref localDeviceIdReadsUntilBlock, 1);

        public void BlockSecondLocalDeviceIdRead() =>
            Volatile.Write(ref localDeviceIdReadsUntilBlock, 2);

        public async ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking read unexpectedly returned.");
        }

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref sentCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingFailAdmissionRemoteWindowPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        public ActivityId ActivityId { get; } = activityId;

        public TaskCompletionSource AdmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public TaskCompletionSource ReleaseAdmission { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public async ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken)
        {
            AdmissionStarted.TrySetResult();
            await ReleaseAdmission.Task.WaitAsync(cancellationToken);
            throw new InvalidDataException(
                "The Remote Window host operation failed closed.");
        }

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException(
                    "Only admission is expected in the ordered dispatch test."));
    }

    private sealed class BlockingHostIdentityRemoteWindowPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        private int blockNextHostDeviceIdRead;

        public ActivityId ActivityId { get; } = activityId;

        public DeviceId HostDeviceId
        {
            get
            {
                if (Interlocked.Exchange(ref blockNextHostDeviceIdRead, 0) != 0)
                {
                    HostDeviceIdReadStarted.TrySetResult();
                    ReleaseHostDeviceIdRead.Task.GetAwaiter().GetResult();
                }

                return hostDeviceId;
            }
        }

        public TaskCompletionSource HostDeviceIdReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseHostDeviceIdRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public void BlockNextHostDeviceIdRead() =>
            Volatile.Write(ref blockNextHostDeviceIdRead, 1);

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException(
                    "No Remote Window command is expected during paused admission."));
    }

    private sealed class BlockingCancellationIgnoringActivityPeer(
        DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public TaskCompletionSource ReceiveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseReceive { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken)
        {
            ReceiveStarted.TrySetResult();
            await ReleaseReceive.Task;
            return OperationReceipt.Committed(
                offer.Context.OperationId,
                offer.Context.CorrelationId,
                offer.Kind,
                senderDeviceId,
                DeviceId,
                offer.Descriptor,
                Now);
        }
    }

    private sealed class CopiedContextDisposalRemoteWindowPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId) : IRemoteWindowControlPeer
    {
        public ActivityId ActivityId { get; } = activityId;

        public TaskCompletionSource CopiedContextReady { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisconnectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> DisposalCompletionState { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposalReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AuthenticatedActivitySessionHandler? Handler { get; set; }

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public TaskCompletionSource NextDispatchStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCopiedContext { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDisconnect { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken)
        {
            _ = DisposeFromCopiedContextAsync();
            return ValueTask.FromResult(RemoteWindowParticipantState.Create(
                request.CorrelationId,
                request.SessionId,
                request.ActivityId,
                request.HostDeviceId,
                request.ParticipantDeviceId,
                RemoteWindowControlAction.Admission,
                RemoteWindowControlOutcome.Applied,
                "participant_admitted",
                RemoteWindowLifecycle.Active,
                RemoteWindowCaptureState.Capturing,
                participantCount: 2,
                MirrorParticipantRole.ViewOnly,
                HostDeviceId,
                driverLeaseEpoch: 1,
                Now.AddMinutes(1),
                ProtectionKind.Safe,
                revision: 1));
        }

        public async ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken)
        {
            NextDispatchStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException(
                "The copied-context test's second dispatch must be cancelled.");
        }

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public async ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(participantDeviceId, peerDeviceId);
            DisconnectStarted.TrySetResult();
            await ReleaseDisconnect.Task.WaitAsync(cancellationToken);
        }

        private async Task DisposeFromCopiedContextAsync()
        {
            CopiedContextReady.TrySetResult();
            await ReleaseCopiedContext.Task;
            ValueTask disposal = (Handler ?? throw new InvalidOperationException(
                "The copied-context handler was not configured.")).DisposeAsync();
            DisposalCompletionState.TrySetResult(disposal.IsCompletedSuccessfully);
            await disposal;
            DisposalReturned.TrySetResult();
        }

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException(
                    "Only admission is expected in the copied-context test."));
    }

    private sealed class ReentrantDisposalRemoteWindowPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        public ActivityId ActivityId { get; } = activityId;

        public TaskCompletionSource DisconnectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AuthenticatedActivitySessionHandler? Handler { get; set; }

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public TaskCompletionSource ReentrantDisposalReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDisconnect { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public async ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
        {
            DisconnectStarted.TrySetResult();
            await (Handler ?? throw new InvalidOperationException(
                "The reentrant handler was not configured.")).DisposeAsync();
            ReentrantDisposalReturned.TrySetResult();
            await ReleaseDisconnect.Task.WaitAsync(cancellationToken);
        }

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException(
                    "No Remote Window command is expected during reentrant disposal."));
    }

    private sealed class SignalingDisconnectRemoteWindowPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        private int disconnectCount;

        public ActivityId ActivityId { get; } = activityId;

        public TaskCompletionSource DisconnectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisconnectCount => Volatile.Read(ref disconnectCount);

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref disconnectCount);
            DisconnectStarted.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException(
                    "No host command is expected while stopping the session."));
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

    private sealed class ScriptedDispatcherConnection(params ControlMessage[] messages)
    {
        private int readCount;

        public int ReadCount => Volatile.Read(ref readCount);

        public ValueTask<ControlMessage> ReceiveAsync(
            CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref readCount) - 1;
            return index < messages.Length
                ? ValueTask.FromResult(messages[index])
                : ValueTask.FromException<ControlMessage>(
                    new InvalidOperationException(
                        "The dispatcher read past the configured script."));
        }

        public static ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NullDisposable : IDisposable
    {
        private NullDisposable()
        {
        }

        public static NullDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
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
