using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowManagedTwoNodeTracerTests
{
    private static readonly DeviceId HostDeviceId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DeviceId ParticipantDeviceId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        28,
        10,
        0,
        0,
        TimeSpan.Zero);
    private static readonly ProtocolVersion Version =
        ProtocolFeatures.RemoteWindowPreparationMinimumVersion;

    [Fact]
    public async Task DriverEligibleWindowTraversesManagedTwoNodeProductionPathAndCleansUp()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.MirrorView,
            Capability.MirrorDrive);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.ActivityOffer);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);

        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        await using var preparationPeerOwner = preparationPeer;
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using var participantHandlerOwner = participantHandler;

        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        UnverifiedPairingCandidate signedHostCandidate = CreateCandidate(
            hostIdentity,
            endpoint);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(signedHostCandidate),
            FixedTimeProvider.Instance);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            timeProvider: FixedTimeProvider.Instance);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;

        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        using var emergencyStops = new RecordingEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        ObservingHostConnection? hostConnection = null;
        RemoteWindowMediaSessionBudget? activeBudget = null;

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            Assert.Equal(Version, participantConnection.ProtocolVersion);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            Assert.Null(hostLease.PeerConnectionCandidate);
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory);
            var request = new DesktopRemoteWindowHostStartRequest(
                sourceLease,
                ownerGeneration: 1,
                hostConnection,
                protection,
                MirrorParticipantRole.DriverEligible);

            RemoteWindowCommandResult started = await coordinator.StartAsync(
                request,
                deadline.Token);

            Assert.Equal(RemoteWindowCommandStatus.Applied, started.Status);
            Assert.True(hostConnection.ReadyObserved);
            Assert.True(hostConnection.AttachmentObserved);
            Assert.True(hostConnection.AdmissionPublished);
            Assert.Equal(1, capture.StartCount);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, hostConnection.MediaSentBeforeAdmissionCount);
            Assert.Equal(Version, hostConnection.ProtocolVersion);
            Assert.True(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? verifiedLease));
            await using (verifiedLease)
            {
                Assert.Equal(endpoint, Assert.IsType<VerifiedPeerConnectionCandidate>(
                    verifiedLease!.PeerConnectionCandidate).EndPoint);
            }

            activeBudget = Assert.IsType<RemoteWindowMediaSessionBudget>(
                coordinator.ActiveMediaBudget);
            Assert.Equal(
                new RemoteWindowMediaBudgetSnapshot(1, 0, 0),
                activeBudget.Snapshot);
            TrackingMemoryOwner renderedOwner = await capture.EmitFrameAsync(
                sequence: 2,
                deadline.Token);
            await renderer.Rendered.Task.WaitAsync(deadline.Token);

            Assert.Equal(1, renderedOwner.DisposeCount);
            Assert.Equal(1, renderer.RenderCount);
            Assert.Equal((2, 2), renderer.LastSize);
            Assert.Equal(NativeRemoteWindowPixelFormat.Bgra8888, renderer.LastFormat);
            AssertOpaqueRed(renderer.LastPixels);
            Assert.True(hostConnection.MediaSendCount >= 1);
            Assert.Equal(0, hostConnection.MediaSentBeforeAdmissionCount);

            IRemoteWindowControlChannel participantChannel =
                await WaitForRemoteWindowChannelAsync(
                    participantHandler,
                    HostDeviceId,
                    deadline.Token);
            RemoteWindowSharingSnapshot hostSnapshot = Assert.IsType<
                RemoteWindowSharingSnapshot>(coordinator.Snapshot);
            Assert.Equal(
                MirrorParticipantRole.DriverEligible,
                hostSnapshot.Participants[ParticipantDeviceId]);
            RemoteWindowControlDeliveryResult transferred =
                await participantChannel.RequestDriverAsync(
                    RemoteWindowDriverRequest.Create(
                        CorrelationId.From(Guid.NewGuid()),
                        controlPeer.SessionId,
                        controlPeer.ActivityId,
                        HostDeviceId,
                        ParticipantDeviceId,
                        Assert.IsType<long>(hostSnapshot.DriverLeaseEpoch),
                        TimeSpan.FromSeconds(10),
                        Now.AddSeconds(5)),
                    deadline.Token);
            RemoteWindowParticipantState driverState = Assert.IsType<
                RemoteWindowParticipantState>(transferred.State);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                transferred.Status);
            Assert.Equal(RemoteWindowControlOutcome.Applied, driverState.Outcome);
            Assert.Equal(ParticipantDeviceId, driverState.CurrentDriverDeviceId);

            RemoteInputBatch exactInput = RemoteInputBatch.Create(
                [
                    RemoteInputEvent.PointerMove(0.25, 0.75),
                    RemoteInputEvent.HidKeyDown(0x07, 0x04),
                ]);
            RemoteWindowControlDeliveryResult injected =
                await participantChannel.SendInputAsync(
                    RemoteWindowInputRequest.Create(
                        CorrelationId.From(Guid.NewGuid()),
                        controlPeer.SessionId,
                        controlPeer.ActivityId,
                        HostDeviceId,
                        ParticipantDeviceId,
                        Assert.IsType<long>(driverState.DriverLeaseEpoch),
                        exactInput,
                        Now.AddSeconds(2)),
                    deadline.Token);
            RemoteWindowParticipantState inputState = Assert.IsType<
                RemoteWindowParticipantState>(injected.State);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                injected.Status);
            Assert.Equal(RemoteWindowControlOutcome.Applied, inputState.Outcome);
            RemoteInputBatch deliveredInput = Assert.Single(input.Batches);
            Assert.Equal(2, deliveredInput.Events.Count);
            Assert.Equal(RemoteInputEventKind.PointerMove, deliveredInput.Events[0].Kind);
            Assert.Equal(0.25, deliveredInput.Events[0].NormalizedX);
            Assert.Equal(0.75, deliveredInput.Events[0].NormalizedY);
            Assert.Equal(RemoteInputEventKind.HidKeyDown, deliveredInput.Events[1].Kind);
            Assert.Equal(0x07, deliveredInput.Events[1].HidUsagePage);
            Assert.Equal(0x04, deliveredInput.Events[1].HidUsageId);
            Assert.Equal(sourceRegistration.Source.ActivityId, input.LastSourceActivityId);

            Assert.True(emergencyStops.Trigger());
            RemoteWindowSharingSnapshot emergency = Assert.IsType<
                RemoteWindowSharingSnapshot>(coordinator.Snapshot);
            Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, emergency.Lifecycle);
            Assert.Equal(RemoteWindowCaptureState.Stopped, emergency.CaptureState);
            Assert.Equal(1, capture.EmergencyStopCount);
            Assert.Equal(1, input.EmergencyStopCount);

            _ = await coordinator.StopAsync(deadline.Token);
            await participantConnection.DisposeAsync();
            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Null(coordinator.Snapshot);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(capture.HasCurrentCapture);
            Assert.True(capture.StopCount + capture.EmergencyStopCount >= 1);
            Assert.True(input.StopCount + input.EmergencyStopCount >= 1);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            if (participantConnection is not null)
            {
                await participantConnection.DisposeAsync();
            }

            listenerStop.Cancel();
            await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    [Fact]
    public async Task VerifiedFsm1AttachmentFailureAfterTcpAcceptRejectsWithoutAdmissionOrCapture()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.MirrorView,
            Capability.MirrorDrive);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.ActivityOffer);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        await using var preparationPeerOwner = preparationPeer;
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using var participantHandlerOwner = participantHandler;

        using var controlSocket = new TcpListener(IPAddress.Loopback, 0);
        controlSocket.Start(backlog: 8);
        var controlEndpoint = Assert.IsType<IPEndPoint>(controlSocket.LocalEndpoint);
        using var unavailableFsm1Socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);
        unavailableFsm1Socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        unavailableFsm1Socket.Listen(backlog: 1);
        using var unavailableFsm1Stop = new CancellationTokenSource();
        var fsm1ConnectionAccepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task rejectingFsm1Connection = AcceptAndResetFsm1ConnectionAsync(
            unavailableFsm1Socket,
            fsm1ConnectionAccepted,
            unavailableFsm1Stop.Token);
        var unavailableFsm1Endpoint = Assert.IsType<IPEndPoint>(
            unavailableFsm1Socket.LocalEndPoint);
        Assert.NotEqual(controlEndpoint.Port, unavailableFsm1Endpoint.Port);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(CreateCandidate(
                hostIdentity,
                unavailableFsm1Endpoint)),
            FixedTimeProvider.Instance);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            controlSocket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            timeProvider: FixedTimeProvider.Instance);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        using var emergencyStops = new RecordingEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        RejectedPreparationObservingHostConnection? hostConnection = null;

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    controlEndpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            hostConnection = new RejectedPreparationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                "media_attachment_failed");
            var request = new DesktopRemoteWindowHostStartRequest(
                sourceLease,
                ownerGeneration: 1,
                hostConnection,
                protection,
                MirrorParticipantRole.DriverEligible);

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await coordinator.StartAsync(request, deadline.Token));

            await fsm1ConnectionAccepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("media_attachment_failed", failure.Message);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.True(hostConnection.ResponseObservedBeforeFailClose);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(capture.HasCurrentCapture);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            try
            {
                if (participantConnection is not null)
                {
                    await participantConnection.DisposeAsync();
                }

                listenerStop.Cancel();
                await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
            }
            finally
            {
                unavailableFsm1Stop.Cancel();
                await rejectingFsm1Connection.WaitAsync(
                    TimeSpan.FromSeconds(5));
            }
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    [Theory]
    [InlineData(
        RendererPreparationFailure.Throw,
        RendererFailureBoundary.AfterBilateralAttachment)]
    [InlineData(
        RendererPreparationFailure.Missing,
        RendererFailureBoundary.AfterBilateralAttachment)]
    [InlineData(
        RendererPreparationFailure.ForeignCancellation,
        RendererFailureBoundary.AfterBilateralAttachment)]
    [InlineData(
        RendererPreparationFailure.Throw,
        RendererFailureBoundary.BeforeHostDirectoryPublication)]
    [InlineData(
        RendererPreparationFailure.Throw,
        RendererFailureBoundary.FailCloseBeforeHostDirectoryPublication)]
    public async Task VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose(
        RendererPreparationFailure rendererFailure,
        RendererFailureBoundary failureBoundary)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.MirrorView,
            Capability.MirrorDrive);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.ActivityOffer);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        BlockingMediaAttachmentHandler? hostMediaGate = failureBoundary is
            RendererFailureBoundary.BeforeHostDirectoryPublication or
            RendererFailureBoundary.FailCloseBeforeHostDirectoryPublication
                ? new BlockingMediaAttachmentHandler(hostMedia)
                : null;
        TaskCompletionSource? allowRejectedResponseReturn = failureBoundary is
            RendererFailureBoundary.BeforeHostDirectoryPublication
                ? new(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new FailingRendererFactory(
            rendererFailure,
            failureBoundary,
            renderer,
            hostMedia,
            participantMedia,
            hostMediaGate?.Entered.Task);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        await using var preparationPeerOwner = preparationPeer;
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using var participantHandlerOwner = participantHandler;

        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(CreateCandidate(hostIdentity, endpoint)),
            FixedTimeProvider.Instance);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            mediaHandler: hostMediaGate,
            timeProvider: FixedTimeProvider.Instance);
        TaskCompletionSource<InboundConnectionFailure>? lateMediaAttachmentFault =
            failureBoundary is
                RendererFailureBoundary.FailCloseBeforeHostDirectoryPublication
                    ? new(TaskCreationOptions.RunContinuationsAsynchronously)
                    : null;
        Action<InboundConnectionFailure>? lateMediaAttachmentFaultObserver = null;
        if (lateMediaAttachmentFault is not null)
        {
            lateMediaAttachmentFaultObserver = failure =>
            {
                if (failure.Stage is InboundConnectionFailureStage.MediaAttachment)
                {
                    lateMediaAttachmentFault.TrySetResult(failure);
                }
            };
            listener.ConnectionFaulted += lateMediaAttachmentFaultObserver;
        }

        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        using var emergencyStops = new RecordingEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        RejectedPreparationObservingHostConnection? hostConnection = null;
        Task<RemoteWindowCommandResult>? startTask = null;
        Exception? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            Assert.Equal(Version, participantConnection.ProtocolVersion);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            string expectedReasonCode = rendererFailure is
                RendererPreparationFailure.Missing
                    ? "renderer_unavailable"
                    : "renderer_start_failed";
            hostConnection = new RejectedPreparationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                expectedReasonCode,
                allowRejectedResponseReturn?.Task);
            var request = new DesktopRemoteWindowHostStartRequest(
                sourceLease,
                ownerGeneration: 1,
                hostConnection,
                protection,
                MirrorParticipantRole.DriverEligible);

            startTask = coordinator.StartAsync(request, deadline.Token).AsTask();
            InvalidOperationException failure;
            if (hostMediaGate is not null)
            {
                await hostMediaGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await rendererFactory.FailureInjected.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));
                await hostConnection.RejectedResponseObserved.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));
                Assert.False(hostMediaGate.IsReleased);
                Assert.Equal(1, hostMediaGate.CallCount);
                Assert.Equal(0, hostMediaGate.ForwardCount);
                Assert.True(hostConnection.ResponseObservedBeforeFailClose);
                Assert.True(rendererFactory.HostSessionObserved);
                Assert.True(rendererFactory.ParticipantSessionObserved);
                Assert.False(rendererFactory.HostSessionAttachedAtInjectedFailure);
                Assert.True(rendererFactory.ParticipantSessionAttachedAtInjectedFailure);
                if (failureBoundary is
                    RendererFailureBoundary.BeforeHostDirectoryPublication)
                {
                    hostMediaGate.Release();
                    Assert.True(hostMedia.TryGet(
                        ParticipantDeviceId,
                        out AuthenticatedRemoteWindowMediaSession?
                            observedHostSession));
                    AuthenticatedRemoteWindowMediaSession hostSession =
                        Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                            observedHostSession);
                    await hostSession.WaitForAttachmentAsync(deadline.Token);
                    Assert.True(hostSession.IsAttached);
                    Assert.Equal(1, hostMediaGate.ForwardCount);
                    Assert.Equal(0, hostConnection.FailCloseCount);
                    Assert.Equal(0, hostConnection.DisposeCount);
                    allowRejectedResponseReturn!.TrySetResult();
                    failure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await startTask);
                    await hostMediaGate.Exited.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));
                    Assert.Equal(1, hostMediaGate.ForwardCount);
                }
                else
                {
                    TaskCompletionSource<InboundConnectionFailure> fault =
                        Assert.IsType<
                            TaskCompletionSource<InboundConnectionFailure>>(
                            lateMediaAttachmentFault);
                    Assert.False(fault.Task.IsCompleted);
                    failure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await startTask.WaitAsync(
                            TimeSpan.FromSeconds(5)));
                    Assert.Contains(expectedReasonCode, failure.Message);

                    await ObserveSessionStopAsync(participantRun);
                    await WaitForCleanupAsync(
                        hostHandler,
                        participantHandler,
                        hostMedia,
                        participantMedia,
                        deadline.Token);

                    Assert.False(hostMediaGate.IsReleased);
                    Assert.Equal(1, hostMediaGate.CallCount);
                    Assert.Equal(0, hostMediaGate.ForwardCount);
                    Assert.False(fault.Task.IsCompleted);
                    Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
                    Assert.False(participantMedia.TryGet(HostDeviceId, out _));
                    Assert.Equal(0, hostMedia.Routes.Count);
                    Assert.Equal(0, participantMedia.Routes.Count);
                    Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                        ParticipantDeviceId,
                        out _));
                    Assert.False(
                        participantHandler.TryAcquireRemoteWindowPeerConnection(
                            HostDeviceId,
                            out _));
                    Assert.False(hostConnection.IsCurrent);
                    Assert.Equal(1, hostConnection.FailCloseCount);
                    Assert.Equal(1, hostConnection.DisposeCount);
                    Assert.Null(coordinator.Snapshot);
                    Assert.Null(coordinator.ActiveMediaBudget);
                    Assert.Null(coordinator.TerminalFailure);
                    Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
                    Assert.Equal(0, hostConnection.AdmissionPublishCount);
                    Assert.Equal(0, hostConnection.MediaSendCount);
                    Assert.Equal(0, capture.StartCount);
                    Assert.Equal(0, renderer.RenderCount);
                    Assert.True(protection.IsDisposed);
                    Assert.Equal(0, permissions.ObserverCount);
                    Assert.False(emergencyStops.HasCurrentRegistration);
                    Assert.False(capture.HasCurrentCapture);
                    Assert.Throws<InvalidOperationException>(
                        () => controlPeer.SessionId);

                    hostMediaGate.Release();
                    await hostMediaGate.Exited.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));
                    InboundConnectionFailure lateFailure =
                        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(
                        InboundConnectionFailureStage.MediaAttachment,
                        lateFailure.Stage);
                    InvalidDataException staleAttachment = Assert.IsType<
                        InvalidDataException>(lateFailure.Exception);
                    Assert.Contains(
                        "no live owning control connection",
                        staleAttachment.Message,
                        StringComparison.Ordinal);
                    Assert.Equal(1, hostMediaGate.ForwardCount);
                    await WaitForCleanupAsync(
                        hostHandler,
                        participantHandler,
                        hostMedia,
                        participantMedia,
                        deadline.Token);
                    Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
                    Assert.False(participantMedia.TryGet(HostDeviceId, out _));
                    Assert.Equal(0, hostMedia.Routes.Count);
                    Assert.Equal(0, participantMedia.Routes.Count);
                    Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                        ParticipantDeviceId,
                        out _));
                    Assert.False(
                        participantHandler.TryAcquireRemoteWindowPeerConnection(
                            HostDeviceId,
                            out _));
                }
            }
            else
            {
                failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await startTask);
            }

            Assert.Contains(expectedReasonCode, failure.Message);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.True(hostConnection.ResponseObservedBeforeFailClose);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(0, rendererFactory.RenderCountAtPrepare);
            Assert.True(rendererFactory.HostSessionObserved);
            Assert.True(rendererFactory.ParticipantSessionObserved);
            Assert.Equal(
                failureBoundary is RendererFailureBoundary.AfterBilateralAttachment,
                rendererFactory.AttachmentBarrierCompleted);
            Assert.Equal(
                failureBoundary is RendererFailureBoundary.AfterBilateralAttachment,
                rendererFactory.HostSessionAttachedAtInjectedFailure);
            Assert.True(rendererFactory.ParticipantSessionAttachedAtInjectedFailure);
            Assert.Equal(HostDeviceId, rendererFactory.HostLocalDeviceId);
            Assert.Equal(ParticipantDeviceId, rendererFactory.HostPeerDeviceId);
            Assert.Equal(
                ParticipantDeviceId,
                rendererFactory.ParticipantLocalDeviceId);
            Assert.Equal(HostDeviceId, rendererFactory.ParticipantPeerDeviceId);
            Assert.Equal(Version, rendererFactory.HostProtocolVersion);
            Assert.Equal(Version, rendererFactory.ParticipantProtocolVersion);
            RemoteWindowMediaRouteBinding observedBinding = Assert.IsType<
                RemoteWindowMediaRouteBinding>(rendererFactory.HostBinding);
            Assert.Equal(observedBinding, rendererFactory.ParticipantBinding);
            Assert.Equal(Version, observedBinding.ProtocolVersion);
            Assert.Equal(rendererFactory.RequestSessionId, observedBinding.SessionId);
            Assert.Equal(rendererFactory.RequestActivityId, observedBinding.ActivityId);
            Assert.Equal(
                ParticipantDeviceId,
                observedBinding.InitiatorDeviceId);
            Assert.Equal(HostDeviceId, observedBinding.ResponderDeviceId);
            if (hostMediaGate is not null)
            {
                Assert.Equal(observedBinding, hostMediaGate.Binding);
            }

            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(capture.HasCurrentCapture);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            if (lateMediaAttachmentFaultObserver is not null)
            {
                listener.ConnectionFaulted -= lateMediaAttachmentFaultObserver;
            }

            hostMediaGate?.Release();
            allowRejectedResponseReturn?.TrySetResult();
            if (startTask is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await startTask.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantConnection is not null)
            {
                Exception? participantDisposeFailure = await Record.ExceptionAsync(
                    async () => await participantConnection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
                if (participantDisposeFailure is not null)
                {
                    cleanupFailures.Add(participantDisposeFailure);
                }
            }

            if (participantRun is not null)
            {
                Exception? participantRunFailure = await Record.ExceptionAsync(
                    async () => await ObserveSessionStopAsync(participantRun));
                if (participantRunFailure is not null)
                {
                    cleanupFailures.Add(participantRunFailure);
                }
            }

            Exception? listenerCancelFailure = Record.Exception(
                listenerStop.Cancel);
            if (listenerCancelFailure is not null)
            {
                cleanupFailures.Add(listenerCancelFailure);
            }

            Exception? listenerFailure = await Record.ExceptionAsync(
                async () => await ObserveListenerStopAsync(
                    listenerRun,
                    listenerStop.Token));
            if (listenerFailure is not null)
            {
                cleanupFailures.Add(listenerFailure);
            }

            if (startTask is { IsCompleted: false })
            {
                _ = await Record.ExceptionAsync(async () =>
                    await startTask.WaitAsync(TimeSpan.FromSeconds(5)));
            }

        }

        if (primaryFailure is not null && cleanupFailures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (primaryFailure is not null)
        {
            cleanupFailures.Insert(0, primaryFailure);
            throw new AggregateException(
                "Managed renderer-failure tracer and cleanup both failed.",
                cleanupFailures);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Managed renderer-failure tracer cleanup failed.",
                cleanupFailures);
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    [Fact]
    public Task VerifiedFsm1AttachmentThenCallerCancellationFailsClosedBeforeAdmissionOrCapture() =>
        RunVerifiedFsm1AttachmentThenPreAdmissionFailureAsync(
            PreAdmissionFailureTrigger.CallerCancellation);

    [Fact]
    public Task VerifiedFsm1AttachmentThenPreparationExpiryFailsClosedBeforeAdmissionOrCapture() =>
        RunVerifiedFsm1AttachmentThenPreAdmissionFailureAsync(
            PreAdmissionFailureTrigger.HostDeadline);

    private static async Task RunVerifiedFsm1AttachmentThenPreAdmissionFailureAsync(
        PreAdmissionFailureTrigger trigger)
    {
        using var harnessDeadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(20));
        using var callerCancellation = new CancellationTokenSource();
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.MirrorView,
            Capability.MirrorDrive);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.ActivityOffer);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        await using var preparationPeerOwner = preparationPeer;
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using var participantHandlerOwner = participantHandler;

        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(CreateCandidate(hostIdentity, endpoint)),
            FixedTimeProvider.Instance);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            timeProvider: FixedTimeProvider.Instance);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        using var emergencyStops = new RecordingEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        var clock = new MutableClock(Now);
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            clock,
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        ObservingHostConnection? hostConnection = null;
        MediaAttachmentObservation? attachment = null;
        VerifiedPeerConnectionCandidate? signedCandidate = null;
        DateTimeOffset clockObservedBeforeTrigger = default;
        var clockWasBeforeDeadline = false;
        var participantLeaseObserved = false;

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: harnessDeadline.Token);
            Assert.Equal(new ProtocolVersion(1, 7), participantConnection.ProtocolVersion);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, harnessDeadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    harnessDeadline.Token);
            Assert.Null(hostLease.PeerConnectionCandidate);
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory,
                afterMediaAttachment: async request =>
                {
                    attachment = CaptureMediaAttachment(
                        request,
                        hostMedia,
                        participantMedia);
                    participantLeaseObserved = participantHandler
                        .TryAcquireRemoteWindowPeerConnection(
                            HostDeviceId,
                            out AuthenticatedRemoteWindowConnectionLease? lease);
                    if (lease is not null)
                    {
                        signedCandidate = lease.PeerConnectionCandidate as
                            VerifiedPeerConnectionCandidate;
                        await lease.DisposeAsync();
                    }

                    clockObservedBeforeTrigger = clock.UtcNow;
                    clockWasBeforeDeadline =
                        clockObservedBeforeTrigger < request.Deadline;
                    if (trigger == PreAdmissionFailureTrigger.HostDeadline)
                    {
                        clock.UtcNow = request.Deadline;
                    }
                    else
                    {
                        callerCancellation.Cancel();
                    }
                });
            var request = new DesktopRemoteWindowHostStartRequest(
                sourceLease,
                ownerGeneration: 1,
                hostConnection,
                protection,
                MirrorParticipantRole.DriverEligible);

            CancellationToken startCancellationToken = trigger is
                PreAdmissionFailureTrigger.CallerCancellation
                    ? callerCancellation.Token
                    : harnessDeadline.Token;
            if (trigger == PreAdmissionFailureTrigger.HostDeadline)
            {
                InvalidOperationException failure = await Assert.ThrowsAsync<
                    InvalidOperationException>(async () =>
                        await coordinator.StartAsync(
                            request,
                            startCancellationToken));
                Assert.Equal(
                    "Remote Window host start failed (preparation_expired).",
                    failure.Message);
                Assert.Null(failure.InnerException);
            }
            else
            {
                OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                    OperationCanceledException>(async () =>
                        await coordinator.StartAsync(
                            request,
                            startCancellationToken));
                Assert.Equal(
                    callerCancellation.Token,
                    failure.CancellationToken);
            }

            Assert.False(harnessDeadline.IsCancellationRequested);
            Assert.Equal(new ProtocolVersion(1, 7), hostConnection.ProtocolVersion);
            Assert.True(participantLeaseObserved);
            VerifiedPeerConnectionCandidate candidate = Assert.IsType<
                VerifiedPeerConnectionCandidate>(signedCandidate);
            Assert.Equal(endpoint, candidate.EndPoint);
            Assert.Equal(HostDeviceId, candidate.Offer.DeviceId);
            Assert.Equal(
                hostIdentity.PublicIdentity.Fingerprint,
                candidate.Offer.IdentityFingerprint);
            Assert.Contains(new ProtocolVersion(1, 7), candidate.Offer.ProtocolVersions);
            Assert.True(hostConnection.ReadyObserved);
            Assert.True(hostConnection.AttachmentObserved);
            Assert.Equal(1, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.False(hostConnection.AdmissionPublished);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, hostConnection.MediaSentBeforeAdmissionCount);
            Assert.Equal(0, capture.StartCount);
            Assert.False(capture.PreAdmissionFrameDisposed);

            MediaAttachmentObservation observed = Assert.IsType<
                MediaAttachmentObservation>(attachment);
            Assert.Equal(Now.AddSeconds(10), observed.Request.Deadline);
            Assert.True(clockWasBeforeDeadline);
            Assert.Equal(Now, clockObservedBeforeTrigger);
            if (trigger == PreAdmissionFailureTrigger.HostDeadline)
            {
                Assert.Equal(observed.Request.Deadline, clock.UtcNow);
                Assert.False(callerCancellation.IsCancellationRequested);
            }
            else
            {
                Assert.Equal(clockObservedBeforeTrigger, clock.UtcNow);
                Assert.True(callerCancellation.IsCancellationRequested);
            }

            Assert.Equal(HostDeviceId, observed.Request.HostDeviceId);
            Assert.Equal(ParticipantDeviceId, observed.Request.ParticipantDeviceId);
            Assert.Equal(
                sourceRegistration.Source.ActivityId,
                observed.Request.ActivityId);
            Assert.True(observed.HostSessionObserved);
            Assert.True(observed.ParticipantSessionObserved);
            Assert.True(observed.HostSessionAttached);
            Assert.True(observed.ParticipantSessionAttached);
            Assert.Equal(HostDeviceId, observed.HostLocalDeviceId);
            Assert.Equal(ParticipantDeviceId, observed.HostPeerDeviceId);
            Assert.Equal(ParticipantDeviceId, observed.ParticipantLocalDeviceId);
            Assert.Equal(HostDeviceId, observed.ParticipantPeerDeviceId);
            Assert.Equal(new ProtocolVersion(1, 7), observed.HostProtocolVersion);
            Assert.Equal(
                new ProtocolVersion(1, 7),
                observed.ParticipantProtocolVersion);
            RemoteWindowMediaRouteBinding binding = Assert.IsType<
                RemoteWindowMediaRouteBinding>(observed.HostBinding);
            Assert.Equal(binding, observed.ParticipantBinding);
            Assert.Equal(new ProtocolVersion(1, 7), binding.ProtocolVersion);
            Assert.Equal(observed.Request.SessionId, binding.SessionId);
            Assert.Equal(observed.Request.ActivityId, binding.ActivityId);
            Assert.Equal(ParticipantDeviceId, binding.InitiatorDeviceId);
            Assert.Equal(HostDeviceId, binding.ResponderDeviceId);

            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(0, capture.EmergencyStopCount);
            Assert.Equal(0, input.EmergencyStopCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                harnessDeadline.Token);

            Assert.True(renderer.IsDisposed);
            Assert.True(capture.StopCount >= 1);
            Assert.True(input.StopCount >= 1);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
            Assert.False(participantMedia.TryGet(HostDeviceId, out _));
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Empty(hostHandler.GetConnectedPeers());
            Assert.Empty(participantHandler.GetConnectedPeers());
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.False(participantHandler.TryGetRemoteWindowChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            if (participantConnection is not null)
            {
                await participantConnection.DisposeAsync();
            }

            listenerStop.Cancel();
            await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    private enum PreAdmissionFailureTrigger
    {
        HostDeadline,
        CallerCancellation,
    }

    private static async Task AcceptAndResetFsm1ConnectionAsync(
        Socket listener,
        TaskCompletionSource connectionAccepted,
        CancellationToken cancellationToken)
    {
        try
        {
            using Socket accepted = await listener.AcceptAsync(cancellationToken);
            connectionAccepted.TrySetResult();
            accepted.LingerState = new LingerOption(true, 0);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
    }

    [Theory]
    [InlineData(TerminalTrigger.AuthenticatedControlDisconnect)]
    [InlineData(TerminalTrigger.MirrorCapabilityRevocation)]
    [InlineData(TerminalTrigger.NativeCapturePermissionRevocation)]
    public async Task TerminalAuthorityOrSafetyLossTerminatesActiveHostSession(
        TerminalTrigger trigger)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.MirrorView);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.ActivityOffer);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        await using var preparationPeerOwner = preparationPeer;
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using var participantHandlerOwner = participantHandler;
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(CreateCandidate(hostIdentity, endpoint)),
            FixedTimeProvider.Instance);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            timeProvider: FixedTimeProvider.Instance);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        using var emergencyStops = new RecordingEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        ObservingHostConnection? hostConnection = null;
        RemoteWindowMediaSessionBudget? activeBudget = null;

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory);
            RemoteWindowCommandResult started = await coordinator.StartAsync(
                new DesktopRemoteWindowHostStartRequest(
                    sourceLease,
                    ownerGeneration: 1,
                    hostConnection,
                    protection,
                    MirrorParticipantRole.ViewOnly),
                deadline.Token);
            Assert.Equal(RemoteWindowCommandStatus.Applied, started.Status);
            activeBudget = Assert.IsType<RemoteWindowMediaSessionBudget>(
                coordinator.ActiveMediaBudget);
            _ = await capture.EmitFrameAsync(sequence: 2, deadline.Token);
            await renderer.Rendered.Task.WaitAsync(deadline.Token);
            Assert.Equal(1, renderer.RenderCount);
            Assert.NotNull(coordinator.Snapshot);
            Assert.True(hostConnection.IsCurrent);
            Assert.Equal(1, permissions.ObserverCount);

            if (trigger == TerminalTrigger.MirrorCapabilityRevocation)
            {
                TrustMutationResult mutation = await hostTrust.UpdateCapabilitiesAsync(
                    ParticipantDeviceId,
                    participantIdentity.PublicIdentity.Fingerprint,
                    CapabilityGrant.Of(Capability.ActivityOffer),
                    deadline.Token);
                Assert.Equal(TrustMutationResult.Applied, mutation);
            }
            else if (trigger == TerminalTrigger.NativeCapturePermissionRevocation)
            {
                permissions.Publish(
                    NativeRemoteWindowPermissionSnapshot.Create(
                        NativeRemoteWindowPermissionState.Denied,
                        NativeRemoteWindowPermissionState.Granted,
                        ownerGeneration: 1,
                        revision: 2));
            }
            else
            {
                await participantConnection.DisposeAsync();
            }

            await ObserveSessionStopAsync(participantRun);
            await WaitForTerminalCleanupAsync(
                coordinator,
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                activeBudget,
                capture,
                renderer,
                protection,
                permissions,
                emergencyStops,
                deadline.Token);

            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.TerminalFailure);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);
            Assert.Equal(1, capture.EmergencyStopCount);
            Assert.Equal(1, input.EmergencyStopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.False(capture.HasCurrentCapture);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            if (participantConnection is not null)
            {
                await participantConnection.DisposeAsync();
            }

            listenerStop.Cancel();
            await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    public enum TerminalTrigger
    {
        AuthenticatedControlDisconnect,
        MirrorCapabilityRevocation,
        NativeCapturePermissionRevocation,
    }

    [Fact]
    public async Task AuthenticatedControlDisconnectEmergencyStopRegistrationCleanupFaultDrainsAndRemainsObservable()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.MirrorView);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.ActivityOffer);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        await using var preparationPeerOwner = preparationPeer;
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using var participantHandlerOwner = participantHandler;
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(CreateCandidate(hostIdentity, endpoint)),
            FixedTimeProvider.Instance);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            timeProvider: FixedTimeProvider.Instance);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        var injected = new IOException(
            "injected emergency-stop registration cleanup failure");
        var emergencyStops = new RecordingEmergencyStopRegistrar(injected);
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        ObservingHostConnection? hostConnection = null;
        RemoteWindowMediaSessionBudget? activeBudget = null;
        Exception? primaryFailure = null;
        var cleanupFailures = new List<Exception>();
        var coordinatorDisposeObserved = false;

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            Assert.Equal(Version, participantConnection.ProtocolVersion);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory);
            RemoteWindowCommandResult started = await coordinator.StartAsync(
                new DesktopRemoteWindowHostStartRequest(
                    sourceLease,
                    ownerGeneration: 1,
                    hostConnection,
                    protection,
                    MirrorParticipantRole.ViewOnly),
                deadline.Token);
            Assert.Equal(RemoteWindowCommandStatus.Applied, started.Status);
            activeBudget = Assert.IsType<RemoteWindowMediaSessionBudget>(
                coordinator.ActiveMediaBudget);

            _ = await capture.EmitFrameAsync(sequence: 2, deadline.Token);
            await renderer.Rendered.Task.WaitAsync(deadline.Token);
            Assert.Equal(1, renderer.RenderCount);
            Assert.True(hostConnection.ReadyObserved);
            Assert.True(hostConnection.AttachmentObserved);
            Assert.Equal(1, hostConnection.WaitForMediaAttachmentCount);
            Assert.True(hostConnection.AdmissionPublished);
            Assert.Equal(1, hostConnection.AdmissionPublishCount);
            Assert.Equal(1, hostConnection.MediaSendCount);
            Assert.Equal(0, hostConnection.MediaSentBeforeAdmissionCount);
            Assert.Equal(1, capture.StartCount);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.NotNull(coordinator.Snapshot);
            Assert.True(hostConnection.IsCurrent);
            Assert.Equal(1, permissions.ObserverCount);
            Assert.True(emergencyStops.HasCurrentRegistration);
            Assert.True(controlPeer.HasRetainedGeneration);

            await participantConnection.DisposeAsync()
                .AsTask()
                .WaitAsync(deadline.Token);
            await ObserveSessionStopAsync(participantRun);
            while (coordinator.TerminalFailure is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), deadline.Token);
            }

            await WaitForTerminalCleanupAsync(
                coordinator,
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                activeBudget,
                capture,
                renderer,
                protection,
                permissions,
                emergencyStops,
                deadline.Token);

            Assert.Null(coordinator.Snapshot);
            Assert.Same(injected, coordinator.TerminalFailure);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);
            Assert.Equal(1, capture.EmergencyStopCount);
            Assert.Equal(1, input.EmergencyStopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.False(capture.HasCurrentCapture);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Equal(1, emergencyStops.RegistrationDisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
            Assert.False(participantMedia.TryGet(HostDeviceId, out _));
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Empty(hostHandler.GetConnectedPeers());
            Assert.Empty(participantHandler.GetConnectedPeers());
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.False(participantHandler.TryGetRemoteWindowChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
            Assert.False(controlPeer.HasRetainedGeneration);

            IOException disposalFailure = await Assert.ThrowsAsync<IOException>(
                async () => await coordinator.DisposeAsync());
            Assert.Same(injected, disposalFailure);
            coordinatorDisposeObserved = true;
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            Exception? coordinatorDisposeFailure = await Record.ExceptionAsync(
                async () => await coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));
            if (coordinatorDisposeFailure is not null
                && (!coordinatorDisposeObserved
                    || !ReferenceEquals(injected, coordinatorDisposeFailure)))
            {
                cleanupFailures.Add(coordinatorDisposeFailure);
            }

            Exception? emergencyStopRegistrarFailure = Record.Exception(
                emergencyStops.Dispose);
            if (emergencyStopRegistrarFailure is not null)
            {
                cleanupFailures.Add(emergencyStopRegistrarFailure);
            }

            if (participantConnection is not null)
            {
                Exception? participantDisposeFailure = await Record.ExceptionAsync(
                    async () => await participantConnection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
                if (participantDisposeFailure is not null)
                {
                    cleanupFailures.Add(participantDisposeFailure);
                }
            }

            if (participantRun is not null)
            {
                Exception? participantRunFailure = await Record.ExceptionAsync(
                    async () => await ObserveSessionStopAsync(participantRun));
                if (participantRunFailure is not null)
                {
                    cleanupFailures.Add(participantRunFailure);
                }
            }

            Exception? listenerCancelFailure = Record.Exception(listenerStop.Cancel);
            if (listenerCancelFailure is not null)
            {
                cleanupFailures.Add(listenerCancelFailure);
            }

            Exception? listenerFailure = await Record.ExceptionAsync(
                async () => await ObserveListenerStopAsync(
                    listenerRun,
                    listenerStop.Token));
            if (listenerFailure is not null)
            {
                cleanupFailures.Add(listenerFailure);
            }
        }

        if (primaryFailure is not null && cleanupFailures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (primaryFailure is not null)
        {
            cleanupFailures.Insert(0, primaryFailure);
            throw new AggregateException(
                "Managed disconnect cleanup-fault tracer and cleanup both failed.",
                cleanupFailures);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Managed disconnect cleanup-fault tracer cleanup failed.",
                cleanupFailures);
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    [Fact]
    public async Task ReverseOnlyMirrorGrantCannotPrepareOrStartCapture()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        CapabilityGrant hostToParticipant = CapabilityGrant.Of(
            Capability.ActivityOffer);
        CapabilityGrant participantToHost = CapabilityGrant.Of(
            Capability.MirrorView,
            Capability.MirrorDrive);
        await using TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 4);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            requiredCapabilities: CapabilityGrant.Of(Capability.ActivityOffer),
            timeProvider: FixedTimeProvider.Instance);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        await using var participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            timeProvider: FixedTimeProvider.Instance);
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        var capture = new RecordingCaptureBoundary();
        var renderer = new RecordingRenderer();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        using var emergencyStops = new RecordingEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceRegistration.Snapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            new RecordingInputBoundary(),
            new RecordingSharingSessionBoundary(),
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(5));

        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            participantRun = participantHandler.RunAsync(
                participantConnection,
                deadline.Token).AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            var connection = new AuthenticatedDesktopRemoteWindowHostConnection(
                hostLease);

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await coordinator.StartAsync(
                        new DesktopRemoteWindowHostStartRequest(
                            sourceLease,
                            ownerGeneration: 1,
                            connection,
                            protection,
                            MirrorParticipantRole.DriverEligible),
                        deadline.Token));

            Assert.Contains("mirror_capability_denied", failure.Message);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Null(coordinator.Snapshot);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            if (participantConnection is not null)
            {
                await participantConnection.DisposeAsync();
            }

            if (participantRun is not null)
            {
                await ObserveSessionStopAsync(participantRun);
            }

            listenerStop.Cancel();
            await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
        }
    }

    private static FlowspanTcpInboundListener CreateListener(
        TcpListener socket,
        DeviceIdentity identity,
        TrustSessionCoordinator trust,
        IAuthenticatedControlSessionHandler handler,
        AuthenticatedRemoteWindowMediaSessionDirectory media,
        CapabilityGrant? requiredCapabilities = null,
        TimeProvider? timeProvider = null,
        IRemoteWindowMediaAttachmentHandler? mediaHandler = null) => new(
        socket,
        identity,
        new PairingCeremonyProfile([Version], TimeSpan.FromSeconds(2)),
        new RejectingPairingDecisionSource(),
        trust,
        new FlowspanTcpInboundProfile(new AuthenticatedInboundSessionProfile(
            requiredCapabilities ?? CapabilityGrant.Of(Capability.MirrorView),
            [Version],
            maximumConcurrentSessions: 4,
            handshakeTimeout: TimeSpan.FromSeconds(2))),
        handler,
        media.Routes,
        mediaHandler ?? media,
        timeProvider);

    private static TrustSessionCoordinator CreateTrust(
        DeviceIdentity peer,
        CapabilityGrant grant)
    {
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            peer.PublicIdentity,
            Now,
            grant));
        return new TrustSessionCoordinator(store);
    }

    private static UnverifiedPairingCandidate CreateCandidate(
        DeviceIdentity host,
        IPEndPoint endpoint)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            host,
            endpoint.Port,
            [Version],
            Now.Subtract(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat((byte)0x5a, SignedDiscoveryOffer.NonceLength)
                .ToArray());
        return new UnverifiedPairingCandidate(
            $"flowspan-{host.DeviceId}",
            offer,
            endpoint,
            PairingCandidateTrustState.AlreadyPaired);
    }

    private static NativeRemoteWindowSourceMetadata CreateMetadata() =>
        NativeRemoteWindowSourceMetadata.Create(
            "Managed tracer window",
            "Flowspan tracer",
            NativeRemoteWindowGeometry.Create(0, 0, 2, 2, 1),
            supportsCapture: true,
            supportsInput: true,
            SafeNow());

    private static ProtectionSnapshot SafeNow() => new(
        ProtectionKind.Safe,
        Now,
        "managed-tracer");

    private static MediaAttachmentObservation CaptureMediaAttachment(
        RemoteWindowPreparationRequest request,
        AuthenticatedRemoteWindowMediaSessionDirectory hostMedia,
        AuthenticatedRemoteWindowMediaSessionDirectory participantMedia)
    {
        bool hostSessionObserved = hostMedia.TryGet(
            ParticipantDeviceId,
            out AuthenticatedRemoteWindowMediaSession? hostSession);
        bool participantSessionObserved = participantMedia.TryGet(
            HostDeviceId,
            out AuthenticatedRemoteWindowMediaSession? participantSession);
        return new MediaAttachmentObservation(
            request,
            hostSessionObserved,
            participantSessionObserved,
            hostSession?.IsAttached == true,
            participantSession?.IsAttached == true,
            hostSession?.LocalDeviceId,
            hostSession?.PeerDeviceId,
            participantSession?.LocalDeviceId,
            participantSession?.PeerDeviceId,
            hostSession?.ProtocolVersion,
            participantSession?.ProtocolVersion,
            hostSession?.Binding,
            participantSession?.Binding);
    }

    private static NativeRemoteWindowSourceLease AcquireLease(
        NativeRemoteWindowSourceRegistry registry,
        NativeRemoteWindowSourceSnapshot snapshot)
    {
        Assert.True(registry.TryAcquire(
            snapshot.Token,
            snapshot.Source.SourceGeneration,
            out NativeRemoteWindowSourceLease? lease));
        return Assert.IsType<NativeRemoteWindowSourceLease>(lease);
    }

    private static async Task<AuthenticatedRemoteWindowConnectionLease>
        WaitForConnectionLeaseAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId,
        bool requireVerifiedPeer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            AuthenticatedRemoteWindowConnectionLease? lease;
            bool acquired = requireVerifiedPeer
                ? handler.TryAcquireRemoteWindowPeerConnection(
                    peerDeviceId,
                    out lease)
                : handler.TryAcquireRemoteWindowConnection(peerDeviceId, out lease);
            if (acquired)
            {
                return Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(lease);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
    }

    private static async Task<IRemoteWindowControlChannel>
        WaitForRemoteWindowChannelAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        IRemoteWindowControlChannel? channel;
        while (!handler.TryGetRemoteWindowChannel(
            peerDeviceId,
            out channel))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }

        return Assert.IsAssignableFrom<IRemoteWindowControlChannel>(channel);
    }

    private static async Task WaitForCleanupAsync(
        AuthenticatedActivitySessionHandler hostHandler,
        AuthenticatedActivitySessionHandler participantHandler,
        AuthenticatedRemoteWindowMediaSessionDirectory hostMedia,
        AuthenticatedRemoteWindowMediaSessionDirectory participantMedia,
        CancellationToken cancellationToken)
    {
        while (hostMedia.TryGet(ParticipantDeviceId, out _)
            || participantMedia.TryGet(HostDeviceId, out _)
            || hostMedia.Routes.Count != 0
            || participantMedia.Routes.Count != 0
            || hostHandler.GetConnectedPeers().Count != 0
            || participantHandler.GetConnectedPeers().Count != 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
    }

    private static async Task WaitForTerminalCleanupAsync(
        DesktopRemoteWindowHostCoordinator coordinator,
        AuthenticatedActivitySessionHandler hostHandler,
        AuthenticatedActivitySessionHandler participantHandler,
        AuthenticatedRemoteWindowMediaSessionDirectory hostMedia,
        AuthenticatedRemoteWindowMediaSessionDirectory participantMedia,
        RemoteWindowMediaSessionBudget mediaBudget,
        RecordingCaptureBoundary capture,
        RecordingRenderer renderer,
        RecordingProtectionSource protection,
        RecordingPermissionBoundary permissions,
        RecordingEmergencyStopRegistrar emergencyStops,
        CancellationToken cancellationToken)
    {
        while (coordinator.Snapshot is not null
            || mediaBudget.Snapshot != RemoteWindowMediaBudgetSnapshot.Empty
            || !renderer.IsDisposed
            || !protection.IsDisposed
            || permissions.ObserverCount != 0
            || emergencyStops.HasCurrentRegistration
            || capture.HasCurrentCapture)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }

        await WaitForCleanupAsync(
            hostHandler,
            participantHandler,
            hostMedia,
            participantMedia,
            cancellationToken);
    }

    private static async Task ObserveSessionStopAsync(Task running)
    {
        try
        {
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (IsExpectedSessionStop(exception))
        {
        }
    }

    private static bool IsExpectedSessionStop(Exception exception) => exception switch
    {
        AggregateException aggregate =>
            aggregate.Flatten().InnerExceptions.Count > 0
            && aggregate.Flatten().InnerExceptions.All(IsExpectedSessionStop),
        OperationCanceledException or IOException or InvalidDataException
            or ObjectDisposedException => true,
        _ => false,
    };

    private static async Task ObserveListenerStopAsync(
        Task running,
        CancellationToken cancellationToken)
    {
        try
        {
            await running.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void AssertOpaqueRed(byte[] pixels)
    {
        Assert.Equal(16, pixels.Length);
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            Assert.InRange(pixels[offset], 0, 20);
            Assert.InRange(pixels[offset + 1], 0, 20);
            Assert.InRange(pixels[offset + 2], 235, 255);
            Assert.Equal(255, pixels[offset + 3]);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed record MediaAttachmentObservation(
        RemoteWindowPreparationRequest Request,
        bool HostSessionObserved,
        bool ParticipantSessionObserved,
        bool HostSessionAttached,
        bool ParticipantSessionAttached,
        DeviceId? HostLocalDeviceId,
        DeviceId? HostPeerDeviceId,
        DeviceId? ParticipantLocalDeviceId,
        DeviceId? ParticipantPeerDeviceId,
        ProtocolVersion? HostProtocolVersion,
        ProtocolVersion? ParticipantProtocolVersion,
        RemoteWindowMediaRouteBinding? HostBinding,
        RemoteWindowMediaRouteBinding? ParticipantBinding);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private FixedTimeProvider()
        {
        }

        public static FixedTimeProvider Instance { get; } = new();

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ObservingHostConnection(
        AuthenticatedDesktopRemoteWindowHostConnection inner,
        RecordingCaptureBoundary capture,
        RecordingRenderer renderer,
        RecordingRendererFactory rendererFactory,
        Func<RemoteWindowPreparationRequest, ValueTask>?
            afterMediaAttachment = null) :
        IDesktopRemoteWindowHostConnection
    {
        private int admissionPublishCount;
        private int admissionPublished;
        private int attachmentObserved;
        private int disposeCount;
        private int failCloseCount;
        private int mediaSendCount;
        private int mediaSentBeforeAdmissionCount;
        private RemoteWindowPreparationRequest? preparationRequest;
        private int readyObserved;
        private int waitForMediaAttachmentCount;

        public int AdmissionPublishCount => Volatile.Read(ref admissionPublishCount);

        public bool AdmissionPublished => Volatile.Read(ref admissionPublished) != 0;

        public bool AttachmentObserved => Volatile.Read(ref attachmentObserved) != 0;

        public bool IsCurrent => inner.IsCurrent;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public int FailCloseCount => Volatile.Read(ref failCloseCount);

        public DeviceId LocalDeviceId => inner.LocalDeviceId;

        public int MediaSendCount => Volatile.Read(ref mediaSendCount);

        public int MediaSentBeforeAdmissionCount =>
            Volatile.Read(ref mediaSentBeforeAdmissionCount);

        public DeviceId PeerDeviceId => inner.PeerDeviceId;

        public ProtocolVersion ProtocolVersion => inner.ProtocolVersion;

        public bool ReadyObserved => Volatile.Read(ref readyObserved) != 0;

        public int WaitForMediaAttachmentCount =>
            Volatile.Read(ref waitForMediaAttachmentCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCount);
            return inner.DisposeAsync();
        }

        public ValueTask FailCloseAsync()
        {
            Interlocked.Increment(ref failCloseCount);
            return inner.FailCloseAsync();
        }

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            TimeSpan lifetime) => inner.PrepareResponderRoute(
                sessionId,
                activityId,
                lifetime);

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            RemoteWindowPreparationDeliveryResult result = await inner.PrepareAsync(
                request,
                cancellationToken);
            Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
            Assert.Equal(
                RemoteWindowPreparationOutcome.Ready,
                Assert.IsType<RemoteWindowPreparationResponse>(result.Response).Outcome);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            Volatile.Write(ref preparationRequest, request);
            Volatile.Write(ref readyObserved, 1);
            return result;
        }

        public IDisposable RegisterRevocationCallback(Action callback) =>
            inner.RegisterRevocationCallback(callback);

        public async ValueTask WaitForMediaAttachmentAsync(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref waitForMediaAttachmentCount);
            await inner.WaitForMediaAttachmentAsync(cancellationToken);
            Assert.True(ReadyObserved);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            Volatile.Write(ref attachmentObserved, 1);
            RemoteWindowPreparationRequest request = Volatile.Read(
                    ref preparationRequest)
                ?? throw new InvalidOperationException(
                    "A preparation request was not observed before media attachment.");
            if (afterMediaAttachment is not null)
            {
                await afterMediaAttachment(request);
            }
        }

        public async ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref admissionPublishCount);
            Assert.True(AttachmentObserved);
            Assert.Equal(1, capture.StartCount);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, MediaSendCount);
            await inner.PublishAdmissionStateAsync(state, cancellationToken);
            Assert.Equal(0, renderer.RenderCount);
            Volatile.Write(ref admissionPublished, 1);
        }

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            if (!AdmissionPublished)
            {
                Interlocked.Increment(ref mediaSentBeforeAdmissionCount);
            }

            Interlocked.Increment(ref mediaSendCount);
            return inner.SendAsync(frame, cancellationToken);
        }
    }

    private sealed class RejectedPreparationObservingHostConnection(
        AuthenticatedDesktopRemoteWindowHostConnection inner,
        string expectedReasonCode,
        Task? allowResponseReturn = null) :
        IDesktopRemoteWindowHostConnection
    {
        private int admissionPublishCount;
        private int disposeCount;
        private int failCloseCount;
        private int mediaSendCount;
        private int prepareCount;
        private int prepareResponderRouteCount;
        private int responseObservedBeforeFailClose;
        private int waitForMediaAttachmentCount;

        public int AdmissionPublishCount => Volatile.Read(ref admissionPublishCount);

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public int FailCloseCount => Volatile.Read(ref failCloseCount);

        public bool IsCurrent => inner.IsCurrent;

        public DeviceId LocalDeviceId => inner.LocalDeviceId;

        public int MediaSendCount => Volatile.Read(ref mediaSendCount);

        public DeviceId PeerDeviceId => inner.PeerDeviceId;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public int PrepareResponderRouteCount =>
            Volatile.Read(ref prepareResponderRouteCount);

        public TaskCompletionSource RejectedResponseObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ResponseObservedBeforeFailClose =>
            Volatile.Read(ref responseObservedBeforeFailClose) != 0;

        public ProtocolVersion ProtocolVersion => inner.ProtocolVersion;

        public int WaitForMediaAttachmentCount =>
            Volatile.Read(ref waitForMediaAttachmentCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCount);
            return inner.DisposeAsync();
        }

        public ValueTask FailCloseAsync()
        {
            Interlocked.Increment(ref failCloseCount);
            return inner.FailCloseAsync();
        }

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            TimeSpan lifetime)
        {
            Interlocked.Increment(ref prepareResponderRouteCount);
            inner.PrepareResponderRoute(sessionId, activityId, lifetime);
        }

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            RemoteWindowPreparationDeliveryResult result = await inner.PrepareAsync(
                request,
                cancellationToken);
            Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
            RemoteWindowPreparationResponse response = Assert.IsType<
                RemoteWindowPreparationResponse>(result.Response);
            Assert.Equal(RemoteWindowPreparationOutcome.Rejected, response.Outcome);
            Assert.Equal(expectedReasonCode, response.ReasonCode);
            if (FailCloseCount == 0 && inner.IsCurrent)
            {
                Volatile.Write(ref responseObservedBeforeFailClose, 1);
            }

            RejectedResponseObserved.TrySetResult();
            if (allowResponseReturn is not null)
            {
                await allowResponseReturn.WaitAsync(cancellationToken);
            }

            return result;
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref admissionPublishCount);
            return inner.PublishAdmissionStateAsync(state, cancellationToken);
        }

        public IDisposable RegisterRevocationCallback(Action callback) =>
            inner.RegisterRevocationCallback(callback);

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref mediaSendCount);
            return inner.SendAsync(frame, cancellationToken);
        }

        public ValueTask WaitForMediaAttachmentAsync(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref waitForMediaAttachmentCount);
            return inner.WaitForMediaAttachmentAsync(cancellationToken);
        }
    }

    private sealed class RecordingCaptureBoundary :
        INativeRemoteWindowCaptureBoundary
    {
        private readonly object gate = new();
        private INativeRemoteWindowFrameSink? sink;
        private NativeRemoteWindowSourceUse? sourceUse;
        private int emergencyStopCount;
        private int preAdmissionFrameDisposed;
        private int startCount;
        private int stopCount;

        public int EmergencyStopCount => Volatile.Read(ref emergencyStopCount);

        public bool HasCurrentCapture
        {
            get
            {
                lock (gate)
                {
                    return sink is not null || sourceUse is not null;
                }
            }
        }

        public bool PreAdmissionFrameDisposed =>
            Volatile.Read(ref preAdmissionFrameDisposed) != 0;

        public int StartCount => Volatile.Read(ref startCount);

        public int StopCount => Volatile.Read(ref stopCount);

        public async ValueTask<LocalBoundaryResult> StartAsync(
            NativeRemoteWindowSourceUse source,
            INativeRemoteWindowFrameSink frameSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                sourceUse = source;
                sink = frameSink;
            }

            Interlocked.Increment(ref startCount);
            TrackingMemoryOwner owner = EmitFrame(sequence: 1);
            await owner.Disposed.Task.WaitAsync(cancellationToken);
            Volatile.Write(ref preAdmissionFrameDisposed, 1);
            return LocalBoundaryResult.Confirmed("native_capture_started");
        }

        public async Task<TrackingMemoryOwner> EmitFrameAsync(
            long sequence,
            CancellationToken cancellationToken)
        {
            TrackingMemoryOwner owner = EmitFrame(sequence);
            await owner.Disposed.Task.WaitAsync(cancellationToken);
            return owner;
        }

        public LocalBoundaryResult EmergencyStopNow()
        {
            Interlocked.Increment(ref emergencyStopCount);
            ClearCurrent();
            return LocalBoundaryResult.Confirmed("native_capture_emergency_stopped");
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_capture_resumed");

        public LocalBoundaryResult StopNow()
        {
            Interlocked.Increment(ref stopCount);
            ClearCurrent();
            return LocalBoundaryResult.Confirmed("native_capture_stopped");
        }

        public ValueTask DisposeAsync()
        {
            ClearCurrent();
            return ValueTask.CompletedTask;
        }

        private void ClearCurrent()
        {
            lock (gate)
            {
                sink = null;
                sourceUse = null;
            }
        }

        private TrackingMemoryOwner EmitFrame(long sequence)
        {
            NativeRemoteWindowSourceUse currentSource;
            INativeRemoteWindowFrameSink currentSink;
            lock (gate)
            {
                currentSource = sourceUse ?? throw new InvalidOperationException(
                    "No native capture is active.");
                currentSink = sink ?? throw new InvalidOperationException(
                    "No native capture sink is active.");
            }

            var owner = new TrackingMemoryOwner(16);
            Span<byte> pixels = owner.Memory.Span;
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = 255;
            }

            NativeRemoteWindowFrame frame = NativeRemoteWindowFrame.TakeOwnership(
                owner,
                payloadLength: 16,
                width: 2,
                height: 2,
                stride: 8,
                NativeRemoteWindowPixelFormat.Bgra8888,
                currentSource.OwnerGeneration,
                currentSource.SessionGeneration,
                currentSource.SourceGeneration,
                currentSource.GeometryRevision,
                sequence);
            Assert.True(currentSource.Matches(frame));
            currentSink.TakeOwnership(currentSource, frame);
            return owner;
        }
    }

    private sealed class RecordingInputBoundary : INativeRemoteInputBoundary
    {
        private readonly object gate = new();
        private readonly List<RemoteInputBatch> batches = [];
        private int emergencyStopCount;
        private int stopCount;

        public IReadOnlyList<RemoteInputBatch> Batches
        {
            get
            {
                lock (gate)
                {
                    return batches.ToArray();
                }
            }
        }

        public int EmergencyStopCount => Volatile.Read(ref emergencyStopCount);

        public ActivityId? LastSourceActivityId { get; private set; }

        public int StopCount => Volatile.Read(ref stopCount);

        public ValueTask<LocalBoundaryResult> InjectAsync(
            NativeRemoteWindowSourceUse sourceUse,
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                LastSourceActivityId = sourceUse.ActivityId;
                batches.Add(batch);
            }

            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("native_input_injected"));
        }

        public LocalBoundaryResult EmergencyStopNow()
        {
            Interlocked.Increment(ref emergencyStopCount);
            return LocalBoundaryResult.Confirmed("native_input_emergency_stopped");
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_input_resumed");

        public LocalBoundaryResult StopNow()
        {
            Interlocked.Increment(ref stopCount);
            return LocalBoundaryResult.Confirmed("native_input_stopped");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingRendererFactory(
        IDesktopRemoteWindowParticipantRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private int prepareCount;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref prepareCount);
            return ValueTask.FromResult<
                IDesktopRemoteWindowParticipantRenderer?>(renderer);
        }
    }

    private sealed class FailingRendererFactory(
        RendererPreparationFailure failure,
        RendererFailureBoundary failureBoundary,
        RecordingRenderer renderer,
        AuthenticatedRemoteWindowMediaSessionDirectory hostMedia,
        AuthenticatedRemoteWindowMediaSessionDirectory participantMedia,
        Task? hostPublicationBlocked) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private int attachmentBarrierCompleted;
        private int prepareCount;

        public bool AttachmentBarrierCompleted =>
            Volatile.Read(ref attachmentBarrierCompleted) != 0;

        public TaskCompletionSource FailureInjected { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowMediaRouteBinding? HostBinding { get; private set; }

        public DeviceId? HostLocalDeviceId { get; private set; }

        public DeviceId? HostPeerDeviceId { get; private set; }

        public ProtocolVersion? HostProtocolVersion { get; private set; }

        public bool HostSessionAttachedAtInjectedFailure { get; private set; }

        public bool HostSessionObserved { get; private set; }

        public RemoteWindowMediaRouteBinding? ParticipantBinding
        {
            get;
            private set;
        }

        public DeviceId? ParticipantLocalDeviceId { get; private set; }

        public DeviceId? ParticipantPeerDeviceId { get; private set; }

        public ProtocolVersion? ParticipantProtocolVersion { get; private set; }

        public bool ParticipantSessionAttachedAtInjectedFailure { get; private set; }

        public bool ParticipantSessionObserved { get; private set; }

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public int RenderCountAtPrepare { get; private set; }

        public ActivityId? RequestActivityId { get; private set; }

        public RemoteWindowSessionId? RequestSessionId { get; private set; }

        public async ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref prepareCount);
            RenderCountAtPrepare = renderer.RenderCount;
            RequestActivityId = request.ActivityId;
            RequestSessionId = request.SessionId;
            HostSessionObserved = hostMedia.TryGet(
                ParticipantDeviceId,
                out AuthenticatedRemoteWindowMediaSession? hostSession);
            ParticipantSessionObserved = participantMedia.TryGet(
                HostDeviceId,
                out AuthenticatedRemoteWindowMediaSession? participantSession);
            if (failureBoundary is
                RendererFailureBoundary.BeforeHostDirectoryPublication or
                RendererFailureBoundary.FailCloseBeforeHostDirectoryPublication)
            {
                await (hostPublicationBlocked
                    ?? throw new InvalidOperationException(
                        "The host publication gate is unavailable."))
                    .WaitAsync(cancellationToken);
            }

            // After-attachment rows own a bilateral wait. The pre-directory rows
            // instead freeze after responder ACK and route attachment, then sample
            // before the directory publishes the host session.
            if (hostSession is not null)
            {
                if (failureBoundary is RendererFailureBoundary.AfterBilateralAttachment)
                {
                    await hostSession.WaitForAttachmentAsync(cancellationToken);
                }

                HostSessionAttachedAtInjectedFailure = hostSession.IsAttached;
                HostLocalDeviceId = hostSession.LocalDeviceId;
                HostPeerDeviceId = hostSession.PeerDeviceId;
                HostProtocolVersion = hostSession.ProtocolVersion;
                HostBinding = hostSession.Binding;
            }

            if (participantSession is not null)
            {
                if (failureBoundary is RendererFailureBoundary.AfterBilateralAttachment)
                {
                    await participantSession.WaitForAttachmentAsync(cancellationToken);
                }

                ParticipantSessionAttachedAtInjectedFailure = participantSession.IsAttached;
                ParticipantLocalDeviceId = participantSession.LocalDeviceId;
                ParticipantPeerDeviceId = participantSession.PeerDeviceId;
                ParticipantProtocolVersion = participantSession.ProtocolVersion;
                ParticipantBinding = participantSession.Binding;
            }

            if (HostSessionAttachedAtInjectedFailure
                && ParticipantSessionAttachedAtInjectedFailure)
            {
                Volatile.Write(ref attachmentBarrierCompleted, 1);
            }

            FailureInjected.TrySetResult();
            return failure switch
            {
                RendererPreparationFailure.Missing => null,
                RendererPreparationFailure.ForeignCancellation =>
                    throw new OperationCanceledException(
                        "test managed foreign renderer cancellation"),
                _ => throw new InvalidOperationException(
                    "test managed renderer preparation failed"),
            };
        }
    }

    public enum RendererPreparationFailure
    {
        Throw,
        Missing,
        ForeignCancellation,
    }

    public enum RendererFailureBoundary
    {
        AfterBilateralAttachment,
        BeforeHostDirectoryPublication,
        FailCloseBeforeHostDirectoryPublication,
    }

    private sealed class BlockingMediaAttachmentHandler(
        IRemoteWindowMediaAttachmentHandler inner) :
        IRemoteWindowMediaAttachmentHandler
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;
        private int forwardCount;
        private int released;

        public RemoteWindowMediaRouteBinding? Binding { get; private set; }

        public int CallCount => Volatile.Read(ref callCount);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Exited { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ForwardCount => Volatile.Read(ref forwardCount);

        public bool IsReleased => Volatile.Read(ref released) != 0;

        public async ValueTask HandleAsync(
            RemoteWindowMediaAttachment attachment,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Binding = attachment.Binding;
            Entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref forwardCount);
            try
            {
                await inner.HandleAsync(attachment, cancellationToken);
            }
            finally
            {
                Exited.TrySetResult();
            }
        }

        public void Release()
        {
            Volatile.Write(ref released, 1);
            release.TrySetResult();
        }
    }

    private sealed class RecordingRenderer :
        IDesktopRemoteWindowParticipantRenderer
    {
        private int disposed;
        private int renderCount;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public NativeRemoteWindowPixelFormat LastFormat { get; private set; }

        public byte[] LastPixels { get; private set; } = [];

        public (int Width, int Height) LastSize { get; private set; }

        public int RenderCount => Volatile.Read(ref renderCount);

        public TaskCompletionSource Rendered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref disposed, 1);
            return ValueTask.CompletedTask;
        }

        public ValueTask RenderAsync(
            DesktopRemoteWindowBgraFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            LastSize = (frame.Width, frame.Height);
            LastFormat = frame.PixelFormat;
            LastPixels = frame.Pixels.ToArray();
            Interlocked.Increment(ref renderCount);
            Rendered.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPermissionBoundary(
        NativeRemoteWindowPermissionSnapshot snapshot) :
        INativeRemoteWindowPermissionBoundary
    {
        private Action<NativeRemoteWindowPermissionSnapshot>? changed;
        private NativeRemoteWindowPermissionSnapshot current = snapshot;
        private int observerCount;

        public event Action<NativeRemoteWindowPermissionSnapshot>? Changed
        {
            add
            {
                changed += value;
                Interlocked.Increment(ref observerCount);
            }
            remove
            {
                changed -= value;
                Interlocked.Decrement(ref observerCount);
            }
        }

        public int ObserverCount => Volatile.Read(ref observerCount);

        public ValueTask DisposeAsync()
        {
            changed = null;
            return ValueTask.CompletedTask;
        }

        public NativeRemoteWindowPermissionSnapshot GetSnapshot() => current;

        public void Publish(NativeRemoteWindowPermissionSnapshot updated)
        {
            current = updated;
            changed?.Invoke(updated);
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(current);
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(current);
        }
    }

    private sealed class RecordingProtectionSource(
        NativeRemoteWindowProtectionObservation observation) :
        INativeProtectionSource
    {
        private Action<NativeRemoteWindowProtectionObservation>? changed;
        private int disposed;

        public event Action<NativeRemoteWindowProtectionObservation>? Changed
        {
            add => changed += value;
            remove => changed -= value;
        }

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public void Dispose()
        {
            Volatile.Write(ref disposed, 1);
            changed = null;
        }

        public bool TryGetLatest(
            out NativeRemoteWindowProtectionObservation? latest)
        {
            latest = observation;
            return true;
        }
    }

    private sealed class RecordingEmergencyStopRegistrar(
        Exception? registrationDisposeFailure = null) :
        ILocalEmergencyStopRegistrar
    {
        private RecordingEmergencyStopRegistration? current;

        public bool HasCurrentRegistration => current?.IsCurrent == true;

        public int RegistrationDisposeCount => current?.DisposeCount ?? 0;

        public void Dispose() => current?.Dispose();

        public LocalEmergencyStopRegistrationResult TryRegister(
            long ownerGeneration,
            long sessionGeneration,
            Action<LocalEmergencyStopActivation> callback)
        {
            current = new RecordingEmergencyStopRegistration(
                ownerGeneration,
                sessionGeneration,
                callback,
                registrationDisposeFailure);
            return LocalEmergencyStopRegistrationResult.Confirmed(
                current,
                "emergency_stop_registered");
        }

        public bool Trigger()
        {
            RecordingEmergencyStopRegistration? registration = current;
            return registration is not null && registration.Trigger();
        }
    }

    private sealed class RecordingEmergencyStopRegistration(
        long ownerGeneration,
        long sessionGeneration,
        Action<LocalEmergencyStopActivation> callback,
        Exception? disposeFailure) :
        ILocalEmergencyStopRegistration
    {
        private Action<LocalEmergencyStopActivation>? callback = callback;
        private int disposeCount;

        public bool IsCurrent => Volatile.Read(ref callback) is not null;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public long OwnerGeneration { get; } = ownerGeneration;

        public long SessionGeneration { get; } = sessionGeneration;

        public void Dispose()
        {
            Action<LocalEmergencyStopActivation>? released =
                Interlocked.Exchange(ref callback, null);
            if (released is null)
            {
                return;
            }

            Interlocked.Increment(ref disposeCount);
            if (disposeFailure is not null)
            {
                ExceptionDispatchInfo.Capture(disposeFailure).Throw();
            }
        }

        public bool Trigger()
        {
            Action<LocalEmergencyStopActivation>? activation =
                Interlocked.Exchange(ref callback, null);
            if (activation is null)
            {
                return false;
            }

            activation(LocalEmergencyStopActivation.Create(
                OwnerGeneration,
                SessionGeneration,
                sequence: 1,
                LocalEmergencyStopCause.UserAction));
            return true;
        }
    }

    private sealed class RecordingSharingSessionBoundary :
        ILocalSharingSessionBoundary
    {
        private int disconnectAllCount;

        public int DisconnectAllCount => Volatile.Read(ref disconnectAllCount);

        public LocalBoundaryResult DisconnectAllNow()
        {
            Interlocked.Increment(ref disconnectAllCount);
            return LocalBoundaryResult.Confirmed("all_peers_disconnected");
        }

        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");
    }

    private sealed class TrustMirrorAuthorizationSource(
        TrustSessionCoordinator trust) : IMirrorAuthorizationSource
    {
        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            trust.TryGetCurrentTrust(peerDeviceId, out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
    }

    private sealed class TrackingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private byte[]? buffer = new byte[length];
        private int disposeCount;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public TaskCompletionSource Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Memory<byte> Memory => Volatile.Read(ref buffer)
            ?? throw new ObjectDisposedException(nameof(TrackingMemoryOwner));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref buffer, null) is not null)
            {
                Interlocked.Increment(ref disposeCount);
                Disposed.TrySetResult();
            }
        }
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException(
                    "No Activity transfer is expected in the Remote Window tracer."));
    }

    private sealed class RejectingPairingDecisionSource : IPairingDecisionSource
    {
        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingDecision>(
                new InvalidOperationException(
                    "No pairing ceremony is expected in the trusted tracer."));
    }
}
