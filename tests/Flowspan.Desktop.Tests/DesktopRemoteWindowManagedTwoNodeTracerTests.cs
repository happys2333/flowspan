using System.Buffers;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
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

internal static class RemoteWindowSessionStopClassifier
{
    public static bool IsExpected(Exception exception) => exception switch
    {
        AggregateException aggregate => IsExpectedAggregate(aggregate),
        OperationCanceledException or IOException or InvalidDataException
            or ObjectDisposedException => true,
        _ => false,
    };

    private static bool IsExpectedAggregate(AggregateException aggregate)
    {
        ReadOnlyCollection<Exception> failures =
            aggregate.Flatten().InnerExceptions;
        return failures.Count > 0
            && (failures.All(IsExpected)
                || IsExpectedRetiredGenerationShutdown(failures));
    }

    private static bool IsExpectedRetiredGenerationShutdown(
        ReadOnlyCollection<Exception> failures)
    {
        const string retiredGeneration =
            "The authenticated Remote Window connection generation is no longer current.";
        int ioFailureCount = 0;
        int retiredGenerationCount = 0;
        foreach (Exception failure in failures)
        {
            if (failure is IOException)
            {
                ioFailureCount++;
            }
            else if (failure.GetType() == typeof(InvalidOperationException)
                && failure.InnerException is null
                && string.Equals(
                    failure.Message,
                    retiredGeneration,
                    StringComparison.Ordinal))
            {
                retiredGenerationCount++;
            }
            else
            {
                return false;
            }
        }

        return ioFailureCount == 1 && retiredGenerationCount > 0;
    }
}

public sealed class RemoteWindowSessionStopClassifierTests
{
    [Fact]
    public void AcceptsOnlyExactRetiredGenerationShutdownAggregate()
    {
        const string retiredGeneration =
            "The authenticated Remote Window connection generation is no longer current.";
        var expected = new AggregateException(
            "The authenticated control session and its cleanup failed.",
            new AggregateException(
                "The authenticated control session and its cleanup failed.",
                new EndOfStreamException("test authenticated control EOF"),
                new InvalidOperationException(retiredGeneration)),
            new InvalidOperationException(retiredGeneration));
        var windowsOperationAborted = new AggregateException(
            "The authenticated control session and its cleanup failed.",
            new IOException(
                "test authenticated control operation aborted",
                new SocketException((int)SocketError.OperationAborted)),
            new InvalidOperationException(retiredGeneration),
            new InvalidOperationException(retiredGeneration));
        var wrongMessage = new AggregateException(
            new EndOfStreamException("test authenticated control EOF"),
            new InvalidOperationException($"{retiredGeneration} unexpected"));
        var standalone = new InvalidOperationException(retiredGeneration);

        Assert.True(RemoteWindowSessionStopClassifier.IsExpected(expected));
        Assert.True(RemoteWindowSessionStopClassifier.IsExpected(
            windowsOperationAborted));
        Assert.False(RemoteWindowSessionStopClassifier.IsExpected(wrongMessage));
        Assert.False(RemoteWindowSessionStopClassifier.IsExpected(standalone));
    }
}

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
            Assert.Equal(1, permissions.PreparationReservationCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
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
    public Task AdFinalAdmissionSideEffectThenThrowFailsClosedAndDrainsBothNodes() =>
        RunAdFinalAdmissionBoundaryScenarioAsync(
            FinalAdmissionBoundaryTrigger.SideEffectThenThrow);

    [Fact]
    public Task AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes() =>
        RunAdFinalAdmissionBoundaryScenarioAsync(
            FinalAdmissionBoundaryTrigger.AuthorityRevoke);

    [Fact]
    public Task AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes() =>
        RunAdFinalAdmissionBoundaryScenarioAsync(
            FinalAdmissionBoundaryTrigger.AuthenticatedDisconnect);

    private static async Task RunAdFinalAdmissionBoundaryScenarioAsync(
        FinalAdmissionBoundaryTrigger trigger)
    {
        bool revokeAuthority =
            trigger is FinalAdmissionBoundaryTrigger.AuthorityRevoke;
        bool disconnectTransport =
            trigger is FinalAdmissionBoundaryTrigger.AuthenticatedDisconnect;
        bool failAfterPublication =
            trigger is FinalAdmissionBoundaryTrigger.SideEffectThenThrow;
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
        Task? disconnecting = null;
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
        var participantAdmission =
            new TaskCompletionSource<RemoteWindowParticipantState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? injected = failAfterPublication
            ? new IOException("FLOWSPAN_FINAL_ADMISSION_SIDE_EFFECT_CANARY")
            : null;
        RemoteWindowParticipantState? admissionAtBoundary = null;
        TrustMutationResult? authorityMutation = null;
        bool connectionCurrentAtBoundary = false;
        bool connectionCurrentAfterInvalidation = true;
        bool hostGenerationReacquiredAfterInvalidation = false;
        long authenticatedGeneration = 0;
        long? reacquiredGenerationAfterInvalidation = null;
        int captureStartCountAtBoundary = -1;
        bool preAdmissionFrameDisposedAtBoundary = false;
        int boundaryFrameDisposeCount = -1;
        int renderCountAtBoundary = -1;
        int mediaSendCountAtBoundary = -1;

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
            authenticatedGeneration = hostLease.Generation;
            IRemoteWindowControlChannel participantChannel =
                await WaitForRemoteWindowChannelAsync(
                    participantHandler,
                    HostDeviceId,
                    deadline.Token);
            participantChannel.StateChanged += state =>
            {
                if (state.Action is RemoteWindowControlAction.Admission)
                {
                    participantAdmission.TrySetResult(state);
                }
            };
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory,
                afterAdmissionPublication: AfterAdmissionPublicationAsync,
                injectedAdmissionPublicationFailure: injected);

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token));
            RemoteWindowParticipantState admitted = await participantAdmission.Task
                .WaitAsync(deadline.Token);

            Assert.Equal(admitted, admissionAtBoundary);
            Assert.Equal(1, captureStartCountAtBoundary);
            Assert.True(preAdmissionFrameDisposedAtBoundary);
            Assert.Equal(1, boundaryFrameDisposeCount);
            Assert.Equal(0, renderCountAtBoundary);
            Assert.Equal(0, mediaSendCountAtBoundary);
            Assert.True(connectionCurrentAtBoundary);
            if (revokeAuthority || disconnectTransport)
            {
                Assert.Equal(
                    "Remote Window host start failed (authenticated_connection_stale).",
                    failure.Message);
                Assert.False(connectionCurrentAfterInvalidation);
                Assert.False(
                    hostGenerationReacquiredAfterInvalidation,
                    $"Connection generation {reacquiredGenerationAfterInvalidation} "
                    + $"was reacquired after invalidating {authenticatedGeneration}.");
                Assert.DoesNotContain(
                    participantIdentity.PublicIdentity.Fingerprint,
                    failure.ToString(),
                    StringComparison.Ordinal);
                if (revokeAuthority)
                {
                    Assert.Equal(TrustMutationResult.Applied, authorityMutation);
                    Assert.True(hostTrust.TryGetCurrentTrust(
                        ParticipantDeviceId,
                        out TrustRecord? reducedTrust));
                    TrustRecord currentTrust = Assert.IsType<TrustRecord>(
                        reducedTrust);
                    Assert.False(currentTrust.GrantedCapabilities.Allows(
                        Capability.MirrorView));
                    Assert.Empty(currentTrust.GrantedCapabilities.Capabilities);
                }
                else
                {
                    Assert.True(hostTrust.TryGetCurrentTrust(
                        ParticipantDeviceId,
                        out TrustRecord? unchangedTrust));
                    TrustRecord currentTrust = Assert.IsType<TrustRecord>(
                        unchangedTrust);
                    Assert.Equal(
                        participantIdentity.PublicIdentity.Fingerprint,
                        currentTrust.PeerIdentity.Fingerprint);
                    Assert.True(currentTrust.GrantedCapabilities.Allows(
                        Capability.MirrorView));
                    Assert.Single(currentTrust.GrantedCapabilities.Capabilities);
                }
            }
            else
            {
                Assert.Contains("host_admission_publish_failed", failure.Message);
                Assert.DoesNotContain(injected!.Message, failure.ToString());
            }

            Assert.Null(failure.InnerException);
            RemoteWindowPreparationRequest prepared = Assert.IsType<
                RemoteWindowPreparationRequest>(rendererFactory.Request);
            Assert.Equal(RemoteWindowControlAction.Admission, admitted.Action);
            Assert.True(admitted.Outcome is RemoteWindowControlOutcome.Applied
                or RemoteWindowControlOutcome.AlreadyApplied);
            Assert.Equal(MirrorParticipantRole.ViewOnly, admitted.EffectiveRole);
            Assert.Equal(prepared.CorrelationId, admitted.CorrelationId);
            Assert.Equal(prepared.SessionId, admitted.SessionId);
            Assert.Equal(prepared.ActivityId, admitted.ActivityId);
            Assert.Equal(prepared.HostDeviceId, admitted.HostDeviceId);
            Assert.Equal(
                prepared.ParticipantDeviceId,
                admitted.ParticipantDeviceId);
            Assert.True(hostConnection.ReadyObserved);
            Assert.True(hostConnection.AttachmentObserved);
            Assert.True(hostConnection.AdmissionPublished);
            Assert.Equal(1, hostConnection.AdmissionPublishCount);
            Assert.Equal(1, capture.StartCount);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, hostConnection.MediaSentBeforeAdmissionCount);
            Assert.Empty(input.Batches);

            if (disconnecting is not null)
            {
                await disconnecting.WaitAsync(deadline.Token);
            }

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(capture.HasCurrentCapture);
            if (revokeAuthority || disconnectTransport)
            {
                Assert.Equal(1, capture.EmergencyStopCount);
                Assert.Equal(1, input.EmergencyStopCount);
            }
            else
            {
                Assert.Equal(1, capture.StopCount);
                Assert.Equal(1, input.StopCount);
            }

            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.Empty(input.Batches);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            if (revokeAuthority)
            {
                Assert.True(hostTrust.TryGetCurrentTrust(
                    ParticipantDeviceId,
                    out TrustRecord? finalTrust));
                Assert.False(Assert.IsType<TrustRecord>(finalTrust)
                    .GrantedCapabilities.Allows(Capability.MirrorView));
            }
            else if (disconnectTransport)
            {
                Assert.True(hostTrust.TryGetCurrentTrust(
                    ParticipantDeviceId,
                    out TrustRecord? finalTrust));
                TrustRecord retainedTrust = Assert.IsType<TrustRecord>(finalTrust);
                Assert.Equal(
                    participantIdentity.PublicIdentity.Fingerprint,
                    retainedTrust.PeerIdentity.Fingerprint);
                Assert.True(retainedTrust.GrantedCapabilities.Allows(
                    Capability.MirrorView));
                Assert.Single(retainedTrust.GrantedCapabilities.Capabilities);
            }

            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            if (disconnecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await disconnecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

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

        async ValueTask AfterAdmissionPublicationAsync()
        {
            admissionAtBoundary = await participantAdmission.Task.WaitAsync(
                deadline.Token);
            captureStartCountAtBoundary = capture.StartCount;
            preAdmissionFrameDisposedAtBoundary =
                capture.PreAdmissionFrameDisposed;
            TrackingMemoryOwner boundaryFrame = await capture.EmitFrameAsync(
                sequence: 2,
                deadline.Token);
            boundaryFrameDisposeCount = boundaryFrame.DisposeCount;
            renderCountAtBoundary = renderer.RenderCount;
            mediaSendCountAtBoundary = hostConnection!.MediaSendCount;
            connectionCurrentAtBoundary = hostConnection.IsCurrent;
            if (failAfterPublication)
            {
                return;
            }

            if (revokeAuthority)
            {
                authorityMutation = await hostTrust.UpdateCapabilitiesAsync(
                    ParticipantDeviceId,
                    participantIdentity.PublicIdentity.Fingerprint,
                    CapabilityGrant.None,
                    deadline.Token);
            }
            else
            {
                disconnecting = participantConnection!.DisposeAsync().AsTask();
                await hostConnection.ConnectionRevoked.Task.WaitAsync(
                    deadline.Token);
            }

            connectionCurrentAfterInvalidation = hostConnection.IsCurrent;
            hostGenerationReacquiredAfterInvalidation =
                hostHandler.TryAcquireRemoteWindowConnection(
                    ParticipantDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? reacquired);
            if (reacquired is not null)
            {
                reacquiredGenerationAfterInvalidation = reacquired.Generation;
                await reacquired.DisposeAsync();
            }
        }
    }

    private enum FinalAdmissionBoundaryTrigger
    {
        SideEffectThenThrow,
        AuthorityRevoke,
        AuthenticatedDisconnect,
    }

    [Fact]
    public Task HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes() =>
        RunHcCaptureStartTerminationScenarioAsync(
            HcCaptureStartTerminationTrigger.AuthenticatedDisconnect);

    [Fact]
    public Task HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes() =>
        RunHcCaptureStartTerminationScenarioAsync(
            HcCaptureStartTerminationTrigger.AuthorityRevoke);

    [Fact]
    public Task HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes() =>
        RunHcCaptureStartTerminationScenarioAsync(
            HcCaptureStartTerminationTrigger.CallerCancellation);

    [Fact]
    public Task HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes() =>
        RunHcCaptureStartTerminationScenarioAsync(
            HcCaptureStartTerminationTrigger.CaptureReject);

    private static async Task RunHcCaptureStartTerminationScenarioAsync(
        HcCaptureStartTerminationTrigger trigger)
    {
        bool revokeAuthority =
            trigger is HcCaptureStartTerminationTrigger.AuthorityRevoke;
        bool cancelCaller =
            trigger is HcCaptureStartTerminationTrigger.CallerCancellation;
        bool rejectCapture =
            trigger is HcCaptureStartTerminationTrigger.CaptureReject;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var callerCancellation = new CancellationTokenSource();
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
        Task? disconnecting = null;
        ObservingHostConnection? hostConnection = null;
        bool readyAtHook = false;
        bool attachmentAtHook = false;
        bool bilateralFsm1AtHook = false;
        bool connectionCurrentAtHook = false;
        bool connectionCurrentAfterHook = true;
        bool generationAcquiredAfterHook = false;
        TrustMutationResult? authorityMutation = null;
        long authenticatedGeneration = 0;
        long? generationAfterHook = null;
        int captureStartCountAtHook = -1;
        int preAdmissionFrameDisposeCountAtHook = -1;
        int admissionPublishCountAtHook = -1;
        int mediaSendCountAtHook = -1;
        int renderCountAtHook = -1;
        var capture = new RecordingCaptureBoundary(
            AfterPreAdmissionFrameDisposedAsync,
            rejectCapture
                ? LocalBoundaryResult.Failed("capture_start_failed")
                : null);
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
            authenticatedGeneration = hostLease.Generation;
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory);

            var startRequest = new DesktopRemoteWindowHostStartRequest(
                sourceLease,
                ownerGeneration: 1,
                hostConnection,
                protection,
                MirrorParticipantRole.ViewOnly);
            Exception failure = cancelCaller
                ? await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await coordinator.StartAsync(
                        startRequest,
                        callerCancellation.Token))
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await coordinator.StartAsync(startRequest, deadline.Token));

            if (revokeAuthority)
            {
                Assert.Equal(TrustMutationResult.Applied, authorityMutation);
            }
            else if (!cancelCaller && !rejectCapture)
            {
                Assert.NotNull(disconnecting);
                await disconnecting.WaitAsync(deadline.Token);
            }

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.True(readyAtHook);
            Assert.True(attachmentAtHook);
            Assert.True(bilateralFsm1AtHook);
            Assert.True(connectionCurrentAtHook);
            bool connectionShouldRemainCurrentAtHook =
                cancelCaller || rejectCapture;
            Assert.Equal(
                connectionShouldRemainCurrentAtHook,
                connectionCurrentAfterHook);
            Assert.Equal(
                connectionShouldRemainCurrentAtHook,
                generationAcquiredAfterHook);
            if (connectionShouldRemainCurrentAtHook)
            {
                Assert.Equal(authenticatedGeneration, generationAfterHook);
            }
            Assert.Equal(1, captureStartCountAtHook);
            Assert.Equal(1, preAdmissionFrameDisposeCountAtHook);
            Assert.Equal(0, admissionPublishCountAtHook);
            Assert.Equal(0, mediaSendCountAtHook);
            Assert.Equal(0, renderCountAtHook);
            Assert.True(hostTrust.TryGetCurrentTrust(
                ParticipantDeviceId,
                out TrustRecord? retained));
            TrustRecord retainedTrust = Assert.IsType<TrustRecord>(retained);
            Assert.Equal(
                participantIdentity.PublicIdentity.Fingerprint,
                retainedTrust.PeerIdentity.Fingerprint);
            if (revokeAuthority)
            {
                Assert.False(retainedTrust.GrantedCapabilities.Allows(
                    Capability.MirrorView));
                Assert.Empty(retainedTrust.GrantedCapabilities.Capabilities);
            }
            else
            {
                Assert.True(retainedTrust.GrantedCapabilities.Allows(
                    Capability.MirrorView));
                Assert.Single(retainedTrust.GrantedCapabilities.Capabilities);
            }
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(
                participantIdentity.PublicIdentity.Fingerprint,
                failure.ToString(),
                StringComparison.Ordinal);
            Assert.True(hostConnection.ReadyObserved);
            Assert.True(hostConnection.AttachmentObserved);
            Assert.Equal(1, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(1, capture.StartCount);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.Equal(1, capture.PreAdmissionFrameDisposeCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Empty(input.Batches);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(capture.HasCurrentCapture);
            if (cancelCaller || rejectCapture)
            {
                Assert.True(capture.StopCount >= 1);
                Assert.True(input.StopCount >= 1);
                Assert.Equal(0, capture.EmergencyStopCount);
                Assert.Equal(0, input.EmergencyStopCount);
            }
            else
            {
                Assert.True(capture.EmergencyStopCount >= 1);
                Assert.True(input.EmergencyStopCount >= 1);
            }
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);

            if (cancelCaller)
            {
                OperationCanceledException canceled = Assert.IsAssignableFrom<
                    OperationCanceledException>(failure);
                Assert.Equal(
                    callerCancellation.Token,
                    canceled.CancellationToken);
            }
            else if (rejectCapture)
            {
                Assert.Equal(
                    "Remote Window host start failed (capture_start_failed).",
                    failure.Message);
            }
            else
            {
                Assert.Equal(
                    "Remote Window host start failed (authenticated_connection_stale).",
                    failure.Message);
            }
        }
        finally
        {
            if (disconnecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await disconnecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantConnection is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await participantConnection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantRun is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await ObserveSessionStopAsync(participantRun));
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

        async ValueTask AfterPreAdmissionFrameDisposedAsync(
            RecordingCaptureBoundary captureAtHook)
        {
            readyAtHook = hostConnection!.ReadyObserved;
            attachmentAtHook = hostConnection.AttachmentObserved;
            bilateralFsm1AtHook = hostMedia.TryGet(
                    ParticipantDeviceId,
                    out AuthenticatedRemoteWindowMediaSession? hostSession)
                && participantMedia.TryGet(
                    HostDeviceId,
                    out AuthenticatedRemoteWindowMediaSession? participantSession)
                && hostSession is { IsAttached: true }
                && participantSession is { IsAttached: true }
                && hostSession.Binding == participantSession.Binding
                && hostSession.ProtocolVersion == Version
                && participantSession.ProtocolVersion == Version;
            connectionCurrentAtHook = hostConnection.IsCurrent;
            captureStartCountAtHook = captureAtHook.StartCount;
            preAdmissionFrameDisposeCountAtHook =
                captureAtHook.PreAdmissionFrameDisposeCount;
            admissionPublishCountAtHook = hostConnection.AdmissionPublishCount;
            mediaSendCountAtHook = hostConnection.MediaSendCount;
            renderCountAtHook = renderer.RenderCount;
            if (revokeAuthority)
            {
                authorityMutation = await hostTrust.UpdateCapabilitiesAsync(
                    ParticipantDeviceId,
                    participantIdentity.PublicIdentity.Fingerprint,
                    CapabilityGrant.None,
                    deadline.Token);
            }
            else if (cancelCaller)
            {
                callerCancellation.Cancel();
            }
            else if (!rejectCapture)
            {
                disconnecting = participantConnection!.DisposeAsync().AsTask();
            }

            if (!cancelCaller && !rejectCapture)
            {
                await hostConnection.ConnectionRevoked.Task.WaitAsync(deadline.Token);
            }

            connectionCurrentAfterHook = hostConnection.IsCurrent;
            generationAcquiredAfterHook =
                hostHandler.TryAcquireRemoteWindowConnection(
                    ParticipantDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? generationProbe);
            if (generationProbe is not null)
            {
                generationAfterHook = generationProbe.Generation;
                await generationProbe.DisposeAsync();
            }
        }
    }

    private enum HcCaptureStartTerminationTrigger
    {
        AuthenticatedDisconnect,
        AuthorityRevoke,
        CallerCancellation,
        CaptureReject,
    }

    [Fact]
    public async Task H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes()
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
        Task? disconnecting = null;
        Task<RemoteWindowCommandResult>? starting = null;
        SourceInvalidationObservingHostConnection? hostConnection = null;
        IDesktopRemoteWindowHostAuthorizationRegistration?
            authorizationRegistration = null;
        var authorization = new BlockingHostAuthorizationSource(
            new TrustMirrorAuthorizationSource(hostTrust));
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
            authorization,
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        long authenticatedGeneration = 0;

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
            authenticatedGeneration = hostLease.Generation;
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();

            DesktopRemoteWindowHostAuthorizationReservationResult reserved =
                await authorization.ReservationAcquired.Task.WaitAsync(
                    deadline.Token);
            Assert.True(reserved.Reserved);
            authorizationRegistration = Assert.IsAssignableFrom<
                IDesktopRemoteWindowHostAuthorizationRegistration>(
                reserved.Registration);
            Assert.True(authorizationRegistration.IsCurrent);
            Assert.False(starting.IsCompleted);
            Assert.True(hostConnection.IsCurrent);
            Assert.True(hostConnection.RevocationCallbackRegistered);
            IAuthenticatedRemoteWindowConnectionPreparationRegistration
                connectionPreparation = Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    hostConnection.ConnectionPreparationRegistration);
            Assert.True(connectionPreparation.IsCurrent);
            Assert.Equal(1, permissions.CurrentPreparationReservationCount);
            Assert.Equal(0, protection.PreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentReadiness);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Equal(0, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, hostConnection.PrepareCount);
            Assert.Equal(0, hostConnection.PrepareSendAdmissionCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Empty(input.Batches);
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);

            disconnecting = participantConnection.DisposeAsync().AsTask();
            await hostConnection.ConnectionRevoked.Task.WaitAsync(deadline.Token);

            Assert.False(hostConnection.IsCurrent);
            Assert.False(connectionPreparation.IsCurrent);
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? reacquired));
            Assert.Null(reacquired);
            Assert.True(authorizationRegistration.IsCurrent);
            Assert.True(hostTrust.TryGetCurrentTrust(
                ParticipantDeviceId,
                out TrustRecord? retained));
            TrustRecord retainedTrust = Assert.IsType<TrustRecord>(retained);
            Assert.Equal(
                participantIdentity.PublicIdentity.Fingerprint,
                retainedTrust.PeerIdentity.Fingerprint);
            Assert.True(retainedTrust.GrantedCapabilities.Allows(
                Capability.MirrorView));
            Assert.Single(retainedTrust.GrantedCapabilities.Capabilities);
            Assert.Equal(0, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, hostConnection.PrepareCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Empty(input.Batches);

            authorization.Release.TrySetResult();
            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);
            await disconnecting.WaitAsync(deadline.Token);
            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(
                "Remote Window host start failed (authenticated_connection_stale).",
                failure.Message);
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(
                participantIdentity.PublicIdentity.Fingerprint,
                failure.ToString(),
                StringComparison.Ordinal);
            Assert.False(authorizationRegistration.IsCurrent);
            Assert.False(connectionPreparation.IsCurrent);
            Assert.Equal(0, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, hostConnection.PrepareCount);
            Assert.Equal(0, hostConnection.PrepareSendAdmissionCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Empty(input.Batches);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentReadiness);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Equal(0, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.True(protection.IsDisposed);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
            Assert.True(hostTrust.TryGetCurrentTrust(
                ParticipantDeviceId,
                out TrustRecord? finalTrust));
            Assert.Equal(
                participantIdentity.PublicIdentity.Fingerprint,
                Assert.IsType<TrustRecord>(finalTrust).PeerIdentity.Fingerprint);
            Assert.True(authenticatedGeneration > 0);
        }
        finally
        {
            authorization.Release.TrySetResult();
            if (disconnecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await disconnecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantConnection is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await participantConnection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (starting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await starting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantRun is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await ObserveSessionStopAsync(participantRun));
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
    public async Task H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes()
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
        Task? disconnecting = null;
        Task<RemoteWindowCommandResult>? starting = null;
        SourceInvalidationObservingHostConnection? hostConnection = null;
        IAuthenticatedRemoteWindowConnectionPreparationRegistration?
            connectionPreparation = null;
        bool routeHookEntered = false;
        bool connectionCurrentAtRouteHook = false;
        bool connectionCurrentAfterDisconnect = true;
        bool connectionPreparationCurrentAtRouteHook = false;
        bool protectionReservedAtRouteHook = false;
        bool emergencyReadinessAtRouteHook = false;
        bool oldGenerationReacquired = false;
        int routeCountAtHook = -1;
        int prepareCountAtHook = -1;
        int hostRouteDirectoryCountAtHook = -1;
        long authenticatedGeneration = 0;
        long? reacquiredGeneration = null;
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
            authenticatedGeneration = hostLease.Generation;
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                AfterRouteSelection,
                blockPrepareForward: false);
            starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);
            Assert.NotNull(disconnecting);
            await disconnecting.WaitAsync(deadline.Token);
            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.True(routeHookEntered);
            Assert.True(connectionCurrentAtRouteHook);
            Assert.True(connectionPreparationCurrentAtRouteHook);
            Assert.True(protectionReservedAtRouteHook);
            Assert.True(emergencyReadinessAtRouteHook);
            Assert.Equal(1, routeCountAtHook);
            Assert.Equal(0, prepareCountAtHook);
            Assert.Equal(1, hostRouteDirectoryCountAtHook);
            Assert.False(connectionCurrentAfterDisconnect);
            Assert.False(
                oldGenerationReacquired,
                $"Connection generation {reacquiredGeneration} was reacquired "
                + $"after disconnecting {authenticatedGeneration}.");
            Assert.Equal(
                "Remote Window host start failed (authenticated_connection_stale).",
                failure.Message);
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(
                participantIdentity.PublicIdentity.Fingerprint,
                failure.ToString(),
                StringComparison.Ordinal);
            Assert.True(hostTrust.TryGetCurrentTrust(
                ParticipantDeviceId,
                out TrustRecord? retained));
            TrustRecord retainedTrust = Assert.IsType<TrustRecord>(retained);
            Assert.Equal(
                participantIdentity.PublicIdentity.Fingerprint,
                retainedTrust.PeerIdentity.Fingerprint);
            Assert.True(retainedTrust.GrantedCapabilities.Allows(
                Capability.MirrorView));
            Assert.Single(retainedTrust.GrantedCapabilities.Capabilities);
            Assert.NotNull(connectionPreparation);
            Assert.False(connectionPreparation.IsCurrent);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, hostConnection.PrepareSendAdmissionCount);
            Assert.Null(hostConnection.PreparationStatus);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Empty(input.Batches);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentReadiness);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.True(protection.IsDisposed);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);

            Assert.Equal(0, hostConnection.PrepareCount);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
            if (disconnecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await disconnecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantConnection is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await participantConnection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (starting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await starting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantRun is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await ObserveSessionStopAsync(participantRun));
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

        void AfterRouteSelection()
        {
            routeHookEntered = true;
            routeCountAtHook = hostConnection!.PrepareResponderRouteCount;
            prepareCountAtHook = hostConnection.PrepareCount;
            hostRouteDirectoryCountAtHook = hostMedia.Routes.Count;
            connectionCurrentAtRouteHook = hostConnection.IsCurrent;
            connectionPreparation = hostConnection.ConnectionPreparationRegistration;
            connectionPreparationCurrentAtRouteHook =
                connectionPreparation?.IsCurrent == true;
            protectionReservedAtRouteHook =
                protection.PreparationReservationCount == 1;
            emergencyReadinessAtRouteHook =
                emergencyStops.HasCurrentReadiness;
            disconnecting = participantConnection!.DisposeAsync().AsTask();
            hostConnection.ConnectionRevoked.Task
                .WaitAsync(deadline.Token)
                .GetAwaiter()
                .GetResult();
            connectionCurrentAfterDisconnect = hostConnection.IsCurrent;
            oldGenerationReacquired =
                hostHandler.TryAcquireRemoteWindowConnection(
                    ParticipantDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? reacquired);
            if (reacquired is not null)
            {
                reacquiredGeneration = reacquired.Generation;
                reacquired.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }

    [Fact]
    public async Task SourceInvalidationAfterReservedRoutePreventsPrepareWireAndDrains()
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
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
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
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
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
        SourceInvalidationObservingHostConnection? hostConnection = null;

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
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Task<RemoteWindowCommandResult> starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);

            Assert.Equal(
                RemoteWindowHostPreparationPhase.RouteSelected,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);

            try
            {
                sourceRegistration.Dispose();
                RemoteWindowHostPreparationTermination termination =
                    await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal("native_source_stale", termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Source,
                    termination.Fact);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.False(reservation.Snapshot.PrepareSendAdmitted);
                Assert.False(sources.TryAcquire(
                    sourceSnapshot.Token,
                    sourceSnapshot.Source.SourceGeneration,
                    out NativeRemoteWindowSourceLease? staleLease));
                Assert.Null(staleLease);
            }
            finally
            {
                hostConnection.ReleasePrepareForward.TrySetResult();
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            Assert.Equal(
                "Remote Window host start failed (native_source_stale).",
                failure.Message);
            Assert.Equal(1, hostConnection.PrepareSendAdmissionCount);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                hostConnection.PreparationStatus);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
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
    public async Task AuthenticatedControlDisconnectAfterReservedRoutePreventsPrepareWireAndDrains()
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
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
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
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
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
        SourceInvalidationObservingHostConnection? hostConnection = null;

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
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Task<RemoteWindowCommandResult> starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);

            Assert.Equal(
                RemoteWindowHostPreparationPhase.RouteSelected,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);

            try
            {
                await participantConnection.DisposeAsync();
                RemoteWindowHostPreparationTermination termination =
                    await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal(
                    "authenticated_connection_stale",
                    termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Connection,
                    termination.Fact);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.False(reservation.Snapshot.PrepareSendAdmitted);
                Assert.True(sourceLease.IsCurrent);
            }
            finally
            {
                hostConnection.ReleasePrepareForward.TrySetResult();
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            Assert.Equal(
                "Remote Window host start failed (authenticated_connection_stale).",
                failure.Message);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(0, hostConnection.PrepareSendAdmissionCount);
            Assert.Null(hostConnection.PreparationStatus);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
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
    public Task TxP0P2ExactDeadlineWhileRendererPreparationIsBlockedFailsClosedAndDrains() =>
        RunTxP0P2BlockedRendererTerminationScenarioAsync(
            BlockedRendererTerminationTrigger.ExactDeadline);

    [Fact]
    public Task TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains() =>
        RunTxP0P2BlockedRendererTerminationScenarioAsync(
            BlockedRendererTerminationTrigger.AuthenticatedControlDisconnect);

    [Fact]
    public Task P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains() =>
        RunTxP0P2BlockedRendererTerminationScenarioAsync(
            BlockedRendererTerminationTrigger.ParticipantTrustRevoke);

    private static async Task RunTxP0P2BlockedRendererTerminationScenarioAsync(
        BlockedRendererTerminationTrigger trigger)
    {
        bool expireDeadline = trigger is BlockedRendererTerminationTrigger.ExactDeadline;
        bool revokeParticipantTrust =
            trigger is BlockedRendererTerminationTrigger.ParticipantTrustRevoke;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hostTimeProvider = new ManualTimeProvider(Now);
        var participantTimeProvider = new ManualTimeProvider(Now);
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
            hostTimeProvider,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new NonCooperativeBlockingRendererFactory(renderer);
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
            rendererFactory,
            participantTimeProvider);
        await using var preparationPeerOwner = preparationPeer;
        var observingPreparationPeer =
            new DisconnectObservingPreparationPeer(preparationPeer);
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            participantTimeProvider,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: observingPreparationPeer);
        await using var participantHandlerOwner = participantHandler;
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        UnverifiedPairingCandidate hostCandidate = CreateCandidate(
            hostIdentity,
            endpoint);
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            participantTrust,
            () => ImmutableArray.Create(hostCandidate),
            participantTimeProvider);
        var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
            participantHandler,
            resolver);
        var listener = CreateListener(
            socket,
            hostIdentity,
            hostTrust,
            hostHandler,
            hostMedia,
            timeProvider: hostTimeProvider);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        Task<PeerSessionAttemptResult>? participantAttempt = null;
        Task<bool>? revokingParticipantTrust = null;
        AuthenticatedRemoteWindowConnectionLease? revokedParticipantLease = null;
        Task? disconnecting = null;
        Task<RemoteWindowCommandResult>? starting = null;
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
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
                revision: 1));
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            hostTimeProvider,
            permissions,
            new TrustMirrorAuthorizationSource(hostTrust),
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(30),
            preparationLifetime: TimeSpan.FromSeconds(10));
        SourceInvalidationObservingHostConnection? hostConnection = null;

        try
        {
            if (revokeParticipantTrust)
            {
                VerifiedPeerConnectionCandidate verifiedHostCandidate =
                    VerifiedPeerConnectionCandidate.Create(
                        hostCandidate.EndPoint,
                        hostCandidate.Offer,
                        hostIdentity.PublicIdentity,
                        participantTimeProvider.GetUtcNow());
                var attempt = new AuthenticatedTcpPeerSessionAttempt(
                    new AuthenticatedPeerSessionProfile(
                        HostDeviceId,
                        participantToHost,
                        [Version]),
                    participantIdentity,
                    participantTrust,
                    new SinglePeerConnectionCandidateSource(
                        verifiedHostCandidate),
                    new SystemAuthenticatedTcpConnector(),
                    participantSessionHandler,
                    participantTimeProvider);
                participantAttempt = attempt.RunAsync(deadline.Token).AsTask();
                participantRun = participantAttempt;
            }
            else
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
            }

            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Assert.Equal(Version, hostConnection.ProtocolVersion);
            starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);
            hostConnection.ReleasePrepareForward.TrySetResult();

            await rendererFactory.Entered.Task.WaitAsync(deadline.Token);
            AuthenticatedRemoteWindowMediaSession hostSession =
                await WaitForAttachedMediaSessionOnChangeAsync(
                    hostMedia,
                    ParticipantDeviceId,
                    deadline.Token);
            AuthenticatedRemoteWindowMediaSession participantSession =
                await WaitForAttachedMediaSessionOnChangeAsync(
                    participantMedia,
                    HostDeviceId,
                    deadline.Token);
            if (revokeParticipantTrust)
            {
                revokedParticipantLease = await WaitForConnectionLeaseAsync(
                    participantHandler,
                    HostDeviceId,
                    requireVerifiedPeer: true,
                    deadline.Token);
            }

            Assert.Equal(
                RemoteWindowHostPreparationPhase.PrepareSending,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.True(reservation.Snapshot.PrepareSendAdmitted);
            Assert.False(reservation.Terminal.IsCompleted);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostConnection.PrepareSendAdmissionCount);
            Assert.Null(hostConnection.PreparationStatus);
            Assert.Equal(1, receivePolicy.EvaluationCount);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.False(
                observingPreparationPeer.PreparationCompleted.Task.IsCompleted);
            Assert.False(rendererFactory.Returned.Task.IsCompleted);
            Assert.False(renderer.IsDisposed);
            Assert.True(hostSession.IsAttached);
            Assert.True(participantSession.IsAttached);
            Assert.Equal(Version, hostSession.ProtocolVersion);
            Assert.Equal(Version, participantSession.ProtocolVersion);
            Assert.Equal(HostDeviceId, hostSession.LocalDeviceId);
            Assert.Equal(ParticipantDeviceId, hostSession.PeerDeviceId);
            Assert.Equal(ParticipantDeviceId, participantSession.LocalDeviceId);
            Assert.Equal(HostDeviceId, participantSession.PeerDeviceId);
            Assert.Equal(hostSession.Binding, participantSession.Binding);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Empty(input.Batches);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, renderer.RenderCount);
            if (revokeParticipantTrust)
            {
                Assert.True(participantTrust.TryGetCurrentTrust(
                    HostDeviceId,
                    out _));
                Assert.False(participantAttempt!.IsCompleted);
                Assert.False(
                    observingPreparationPeer.PeerDisconnectEntered.Task.IsCompleted);
                Assert.True(revokedParticipantLease!.IsCurrent);
            }

            RemoteWindowPreparationRequest preparationRequest =
                rendererFactory.Request;
            Assert.Equal(Now.AddSeconds(10), preparationRequest.Deadline);
            Assert.True(
                hostTimeProvider.GetUtcNow() < preparationRequest.Deadline);
            Assert.True(
                participantTimeProvider.GetUtcNow() < preparationRequest.Deadline);
            RemoteWindowHostPreparationTermination? termination = null;
            if (expireDeadline)
            {
                participantTimeProvider.Advance(
                    preparationRequest.Deadline
                    - participantTimeProvider.GetUtcNow());
                Assert.Equal(
                    preparationRequest.Deadline,
                    participantTimeProvider.GetUtcNow());
                Assert.True(
                    hostTimeProvider.GetUtcNow() < preparationRequest.Deadline);
                await rendererFactory.CancellationObserved.Task.WaitAsync(
                    deadline.Token);
                Assert.False(
                    observingPreparationPeer.PeerDisconnectEntered.Task.IsCompleted);
                Assert.False(reservation.Terminal.IsCompleted);
                Assert.Equal(
                    RemoteWindowHostPreparationPhase.PrepareSending,
                    reservation.Snapshot.Phase);
            }
            else if (!revokeParticipantTrust)
            {
                Assert.NotNull(participantConnection);
                disconnecting = participantConnection.DisposeAsync().AsTask();
                await observingPreparationPeer.PeerDisconnectEntered.Task.WaitAsync(
                    deadline.Token);
                await rendererFactory.CancellationObserved.Task.WaitAsync(
                    deadline.Token);
                termination = await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Connection,
                    termination.Fact);
                Assert.Equal(
                    "authenticated_connection_stale",
                    termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.Equal(
                    RemoteWindowHostPreparationPhase.Terminal,
                    reservation.Snapshot.Phase);
            }
            else
            {
                Assert.NotNull(participantAttempt);
                Assert.NotNull(revokedParticipantLease);
                revokingParticipantTrust = participantTrust
                    .RevokePeerAsync(HostDeviceId, deadline.Token)
                    .AsTask();
                await observingPreparationPeer.PeerDisconnectEntered.Task.WaitAsync(
                    deadline.Token);
                await rendererFactory.CancellationObserved.Task.WaitAsync(
                    deadline.Token);
                termination = await reservation.Terminal.WaitAsync(deadline.Token);

                Assert.False(participantTrust.TryGetCurrentTrust(
                    HostDeviceId,
                    out _));
                Assert.False(revokingParticipantTrust.IsCompleted);
                Assert.False(participantAttempt.IsCompleted);
                Assert.False(revokedParticipantLease.IsCurrent);
                Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                    HostDeviceId,
                    out _));
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Connection,
                    termination.Fact);
                Assert.Equal(
                    "authenticated_connection_stale",
                    termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.Equal(
                    RemoteWindowHostPreparationPhase.Terminal,
                    reservation.Snapshot.Phase);

                await revokedParticipantLease.DisposeAsync();
                revokedParticipantLease = null;
            }

            Assert.True(reservation.Snapshot.PrepareSendAdmitted);
            Assert.False(
                observingPreparationPeer.PeerDisconnectCompleted.Task.IsCompleted);
            Assert.False(
                observingPreparationPeer.PreparationCompleted.Task.IsCompleted);
            Assert.False(rendererFactory.Returned.Task.IsCompleted);
            Assert.False(renderer.IsDisposed);
            Assert.False(participantRun.IsCompleted);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Empty(input.Batches);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, renderer.RenderCount);

            rendererFactory.Release();
            await rendererFactory.Returned.Task.WaitAsync(deadline.Token);
            RemoteWindowPreparationResponse participantResponse =
                await observingPreparationPeer.PreparationCompleted.Task.WaitAsync(
                    deadline.Token);
            Assert.Equal(
                RemoteWindowPreparationOutcome.Rejected,
                participantResponse.Outcome);
            Assert.Equal(
                expireDeadline
                    ? "preparation_expired"
                    : "preparation_cancelled",
                participantResponse.ReasonCode);
            Assert.True(
                hostTimeProvider.GetUtcNow() < preparationRequest.Deadline);
            if (expireDeadline)
            {
                await observingPreparationPeer.PeerDisconnectEntered.Task.WaitAsync(
                    deadline.Token);
            }

            await observingPreparationPeer.PeerDisconnectCompleted.Task.WaitAsync(
                deadline.Token);
            if (disconnecting is not null)
            {
                await disconnecting.WaitAsync(deadline.Token);
            }

            if (revokingParticipantTrust is not null)
            {
                Assert.True(await revokingParticipantTrust.WaitAsync(
                    deadline.Token));
                PeerSessionAttemptResult attemptResult = await participantAttempt!
                    .WaitAsync(deadline.Token);
                Assert.Equal(
                    PeerSessionAttemptStatus.PermanentRejection,
                    attemptResult.Status);
                Assert.Equal(
                    PeerReconnectStopReason.PeerNotTrusted,
                    attemptResult.StopReason);
            }

            termination ??= await reservation.Terminal.WaitAsync(deadline.Token);
            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            if (expireDeadline)
            {
                Assert.True(
                    (termination, failure.Message) is
                    (
                    {
                        Fact: RemoteWindowHostPreparationFact.Connection,
                        ReasonCode: "authenticated_connection_stale",
                    },
                        "Remote Window host start failed (authenticated_connection_stale)."
                    )
                    or
                    (
                    {
                        Fact: null,
                        ReasonCode: "host_preparation_disposed",
                    },
                        "Remote Window host start failed (remote_window_prepare_not_acknowledged)."
                    ),
                    $"Unexpected host deadline outcome: {termination}; {failure.Message}");
            }
            else
            {
                Assert.Equal(
                    "Remote Window host start failed (authenticated_connection_stale).",
                    failure.Message);
            }

            Assert.Equal(
                RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                termination.CleanupScope);
            Assert.Equal(
                RemoteWindowHostPreparationPhase.Terminal,
                reservation.Snapshot.Phase);
            Assert.Null(failure.InnerException);
            Assert.NotEqual(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                hostConnection.PreparationStatus);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Empty(input.Batches);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, renderer.RenderCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.Empty(input.Batches);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            if (revokeParticipantTrust)
            {
                Assert.False(participantTrust.TryGetCurrentTrust(
                    HostDeviceId,
                    out _));
            }

            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
            rendererFactory.Release();
            if (revokedParticipantLease is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await revokedParticipantLease.DisposeAsync());
            }

            if (disconnecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await disconnecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else if (participantConnection is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await participantConnection.DisposeAsync());
            }

            if (starting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await starting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (participantRun is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await ObserveSessionStopAsync(participantRun));
            }

            if (revokingParticipantTrust is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await revokingParticipantTrust.WaitAsync(
                        TimeSpan.FromSeconds(5)));
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

    private enum BlockedRendererTerminationTrigger
    {
        ExactDeadline,
        AuthenticatedControlDisconnect,
        ParticipantTrustRevoke,
    }

    [Fact]
    public async Task PermissionRevisionAfterReservedRoutePreventsPrepareWireAndDrains()
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
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
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
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
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
        SourceInvalidationObservingHostConnection? hostConnection = null;

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
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Task<RemoteWindowCommandResult> starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);

            Assert.Equal(
                RemoteWindowHostPreparationPhase.RouteSelected,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(1, permissions.PreparationReservationCount);
            Assert.Equal(1, permissions.CurrentPreparationReservationCount);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);

            try
            {
                permissions.Publish(
                    NativeRemoteWindowPermissionSnapshot.Create(
                        NativeRemoteWindowPermissionState.Revoked,
                        NativeRemoteWindowPermissionState.Granted,
                        ownerGeneration: 1,
                        revision: 2));
                RemoteWindowHostPreparationTermination termination =
                    await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal("native_permission_denied", termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Permission,
                    termination.Fact);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.False(reservation.Snapshot.PrepareSendAdmitted);
                Assert.Equal(0, permissions.CurrentPreparationReservationCount);

                permissions.Publish(
                    NativeRemoteWindowPermissionSnapshot.Create(
                        NativeRemoteWindowPermissionState.Granted,
                        NativeRemoteWindowPermissionState.Granted,
                        ownerGeneration: 1,
                        revision: 3));
                Assert.Equal(
                    RemoteWindowHostPreparationPhase.Terminal,
                    reservation.Snapshot.Phase);
                Assert.Equal(
                    "native_permission_denied",
                    reservation.Snapshot.Termination?.ReasonCode);
                Assert.True(sourceLease.IsCurrent);
            }
            finally
            {
                hostConnection.ReleasePrepareForward.TrySetResult();
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            Assert.Equal(
                "Remote Window host start failed (native_permission_denied).",
                failure.Message);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(0, hostConnection.PrepareSendAdmissionCount);
            Assert.Null(hostConnection.PreparationStatus);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
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

    [Theory]
    [InlineData(ProtectionKind.SecureInput)]
    [InlineData(ProtectionKind.Unknown)]
    public async Task ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains(
        ProtectionKind replacementKind)
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
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
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
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
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
        SourceInvalidationObservingHostConnection? hostConnection = null;

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
            Assert.Equal(
                new ProtocolVersion(1, 7),
                participantConnection.ProtocolVersion);
            participantRun = participantSessionHandler
                .RunAsync(participantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Assert.Equal(new ProtocolVersion(1, 7), hostConnection.ProtocolVersion);
            Assert.Equal(HostDeviceId, hostConnection.LocalDeviceId);
            Assert.Equal(ParticipantDeviceId, hostConnection.PeerDeviceId);
            Assert.Equal(
                participantIdentity.PublicIdentity.Fingerprint,
                hostConnection.AuthenticatedPeerFingerprint);
            Task<RemoteWindowCommandResult> starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);

            Assert.Equal(
                RemoteWindowHostPreparationPhase.RouteSelected,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.False(reservation.Terminal.IsCompleted);
            Assert.Equal(1, protection.PreparationReservationCount);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Empty(input.Batches);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            try
            {
                Assert.True(protection.Publish(
                    new ProtectionSnapshot(
                        replacementKind,
                        Now,
                        "managed-loopback-protection-mutation")));
                RemoteWindowHostPreparationTermination termination =
                    await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Protection,
                    termination.Fact);
                Assert.Equal(
                    "native_protection_not_safe",
                    termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.False(reservation.Snapshot.PrepareSendAdmitted);
                Assert.True(sourceLease.IsCurrent);
                Assert.True(protection.TryGetLatest(
                    out NativeRemoteWindowProtectionObservation? latest));
                Assert.Equal(replacementKind, latest?.Protection.Kind);

                Assert.True(protection.Publish(SafeNow()));
                Assert.Equal(
                    RemoteWindowHostPreparationPhase.Terminal,
                    reservation.Snapshot.Phase);
                Assert.Equal(
                    "native_protection_not_safe",
                    reservation.Snapshot.Termination?.ReasonCode);
            }
            finally
            {
                hostConnection.ReleasePrepareForward.TrySetResult();
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            Assert.Equal(
                "Remote Window host start failed (native_protection_not_safe).",
                failure.Message);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostConnection.PrepareSendAdmissionCount);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                hostConnection.PreparationStatus);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Empty(input.Batches);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.Empty(input.Batches);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
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
    public async Task AppliedSameMirrorGrantAfterReservedRoutePreventsPrepareWireAndDrains()
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
        var trustChanged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
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
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
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
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
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
        SourceInvalidationObservingHostConnection? hostConnection = null;
        hostTrust.Changed += OnTrustChanged;

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
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            long authenticatedGeneration = hostLease.Generation;
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Assert.Equal(
                participantIdentity.PublicIdentity.Fingerprint,
                hostConnection.AuthenticatedPeerFingerprint);
            Task<RemoteWindowCommandResult> starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);

            Assert.Equal(
                RemoteWindowHostPreparationPhase.RouteSelected,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);

            try
            {
                TrustMutationResult mutation =
                    await hostTrust.UpdateCapabilitiesAsync(
                        ParticipantDeviceId,
                        participantIdentity.PublicIdentity.Fingerprint,
                        hostToParticipant,
                        deadline.Token);
                Assert.Equal(TrustMutationResult.Applied, mutation);
                await trustChanged.Task.WaitAsync(deadline.Token);
                RemoteWindowHostPreparationTermination termination =
                    await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal("mirror_capability_denied", termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.Authorization,
                    termination.Fact);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.False(reservation.Snapshot.PrepareSendAdmitted);
                Assert.True(hostConnection.IsCurrent);
                Assert.True(hostHandler.TryAcquireRemoteWindowConnection(
                    ParticipantDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? hostProbe));
                await using (AuthenticatedRemoteWindowConnectionLease currentProbe =
                    Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                        hostProbe))
                {
                    Assert.True(currentProbe.IsCurrent);
                    Assert.Equal(authenticatedGeneration, currentProbe.Generation);
                    Assert.Equal(
                        participantIdentity.PublicIdentity.Fingerprint,
                        currentProbe.AuthenticatedPeerFingerprint);
                }

                Assert.True(sourceLease.IsCurrent);
                Assert.True(sources.TryAcquire(
                    sourceSnapshot.Token,
                    sourceSnapshot.Source.SourceGeneration,
                    out NativeRemoteWindowSourceLease? currentSourceLease));
                using NativeRemoteWindowSourceLease currentSource = Assert.IsType<
                    NativeRemoteWindowSourceLease>(currentSourceLease);
                Assert.True(currentSource.IsCurrent);
            }
            finally
            {
                hostConnection.ReleasePrepareForward.TrySetResult();
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            Assert.Equal(
                "Remote Window host start failed (mirror_capability_denied).",
                failure.Message);
            Assert.Equal(1, hostConnection.PrepareSendAdmissionCount);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                hostConnection.PreparationStatus);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostTrust.Changed -= OnTrustChanged;
            hostConnection?.ReleasePrepareForward.TrySetResult();
            if (participantConnection is not null)
            {
                await participantConnection.DisposeAsync();
            }

            listenerStop.Cancel();
            await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
        }

        void OnTrustChanged() => trustChanged.TrySetResult();

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    [Fact]
    public async Task EmergencyStopReadinessLossAfterReservedRoutePreventsPrepareWireAndDrains()
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
        var receivePolicy = new CountingAllowReceivePolicy();
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            receivePolicy,
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
        using var emergencyStops = new InMemoryLocalEmergencyStopRegistrar();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            sources.RegisterGeneric(CreateMetadata());
        NativeRemoteWindowSourceSnapshot sourceSnapshot =
            sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            sourceSnapshot);
        var protection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceSnapshot.Source.SourceGeneration,
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
        SourceInvalidationObservingHostConnection? hostConnection = null;

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
                await WaitForConnectionLeaseOnChangeAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    deadline.Token);
            hostConnection = new SourceInvalidationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease));
            Task<RemoteWindowCommandResult> starting = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();
            RemoteWindowHostPreparationReservation reservation =
                await hostConnection.BeforePrepareForward.Task.WaitAsync(
                    deadline.Token);

            Assert.Equal(
                RemoteWindowHostPreparationPhase.RouteSelected,
                reservation.Snapshot.Phase);
            Assert.True(reservation.Snapshot.RouteMayBeOwned);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);

            try
            {
                Assert.True(emergencyStops.LoseRegistration());
                RemoteWindowHostPreparationTermination termination =
                    await reservation.Terminal.WaitAsync(deadline.Token);
                Assert.Equal(
                    "emergency_stop_readiness_unavailable",
                    termination.ReasonCode);
                Assert.Equal(
                    RemoteWindowHostPreparationFact.EmergencyStop,
                    termination.Fact);
                Assert.Equal(
                    RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
                    termination.CleanupScope);
                Assert.False(reservation.Snapshot.PrepareSendAdmitted);
                Assert.True(sourceLease.IsCurrent);
                Assert.True(sources.TryAcquire(
                    sourceSnapshot.Token,
                    sourceSnapshot.Source.SourceGeneration,
                    out NativeRemoteWindowSourceLease? currentSourceLease));
                Assert.NotNull(currentSourceLease);
                currentSourceLease.Dispose();
            }
            finally
            {
                hostConnection.ReleasePrepareForward.TrySetResult();
            }

            InvalidOperationException failure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () => await starting);

            Assert.Equal(
                "Remote Window host start failed (emergency_stop_readiness_unavailable).",
                failure.Message);
            Assert.Equal(1, hostConnection.PrepareSendAdmissionCount);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                hostConnection.PreparationStatus);
            Assert.False(reservation.Snapshot.PrepareSendAdmitted);
            Assert.Equal(0, receivePolicy.EvaluationCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, hostConnection.MediaSendCount);
            Assert.Equal(0, rendererFactory.PrepareCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, hostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, hostConnection.AdmissionPublishCount);

            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupOnChangeAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.False(capture.HasCurrentCapture);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            LocalBoundaryResult emergencyStopReadiness =
                emergencyStops.CheckReadiness();
            Assert.True(emergencyStopReadiness.Succeeded);
            Assert.Equal(
                "emergency_stop_ready",
                emergencyStopReadiness.ReasonCode);
            Assert.True(sourceLease.IsCurrent);
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
            Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(
                HostDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        }
        finally
        {
            hostConnection?.ReleasePrepareForward.TrySetResult();
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
    public async Task RendererFailureLateAttachmentCannotRetargetReplacementDesktopGeneration()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
        TrustSessionCoordinator hostTrust = CreateTrust(
            participantIdentity,
            hostToParticipant);
        TrustSessionCoordinator participantTrust = CreateTrust(
            hostIdentity,
            participantToHost);
        var hostMedia = new AuthenticatedRemoteWindowMediaSessionDirectory();
        var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        var mediaHandler = new SequencedMediaAttachmentHandler(hostMedia);
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowPeer: controlPeer,
            remoteWindowMediaSessions: hostMedia);
        var renderer = new RecordingRenderer();
        var rendererFactory = new SequencedRendererFactory(
            renderer,
            mediaHandler);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            TryAcquireParticipantConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            FixedTimeProvider.Instance);
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: FixedTimeProvider.Instance,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);

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
            timeProvider: FixedTimeProvider.Instance,
            mediaHandler: mediaHandler);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();

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
        var oldProtection = new RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation.Create(
                SafeNow(),
                ownerGeneration: 1,
                sessionGeneration: 1,
                sourceRegistration.Source.SourceGeneration,
                revision: 1));
        RecordingProtectionSource? replacementProtection = null;
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

        AuthenticatedTcpControlConnection? oldParticipantConnection = null;
        AuthenticatedTcpControlConnection? replacementParticipantConnection = null;
        Task? oldParticipantRun = null;
        Task? replacementParticipantRun = null;
        Task<RemoteWindowCommandResult>? oldStart = null;
        Task<RemoteWindowCommandResult>? replacementStart = null;
        RejectedPreparationObservingHostConnection? oldHostConnection = null;
        AbaObservingHostConnection? replacementHostConnection = null;
        RemoteWindowMediaSessionBudget? replacementBudget = null;
        bool oldStartObserved = false;
        bool replacementStartObserved = false;
        Exception? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            oldParticipantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            oldParticipantRun = participantSessionHandler
                .RunAsync(oldParticipantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease oldParticipantProbe =
                await WaitForConnectionLeaseAsync(
                    participantHandler,
                    HostDeviceId,
                    requireVerifiedPeer: true,
                    deadline.Token);
            long oldParticipantGeneration = oldParticipantProbe.Generation;
            await oldParticipantProbe.DisposeAsync()
                .AsTask()
                .WaitAsync(deadline.Token);
            AuthenticatedRemoteWindowConnectionLease oldHostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            long oldHostGeneration = oldHostLease.Generation;
            oldHostConnection = new RejectedPreparationObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(oldHostLease),
                "renderer_start_failed");
            oldStart = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        oldHostConnection,
                        oldProtection,
                        MirrorParticipantRole.DriverEligible),
                    deadline.Token)
                .AsTask();

            await mediaHandler.First.Entered.Task.WaitAsync(deadline.Token);
            await rendererFactory.FirstFailureInjected.Task.WaitAsync(
                deadline.Token);
            await oldHostConnection.RejectedResponseObserved.Task.WaitAsync(
                deadline.Token);
            InvalidOperationException oldFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await oldStart.WaitAsync(deadline.Token));
            oldStartObserved = true;
            Assert.Contains("renderer_start_failed", oldFailure.Message);
            await ObserveSessionStopAsync(oldParticipantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.True(oldHostConnection.ResponseObservedBeforeFailClose);
            Assert.Equal(1, oldHostConnection.PrepareCount);
            Assert.Equal(1, oldHostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, oldHostConnection.FailCloseCount);
            Assert.Equal(1, oldHostConnection.DisposeCount);
            Assert.False(oldHostConnection.IsCurrent);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(1, mediaHandler.CallCount);
            Assert.Equal(0, mediaHandler.First.ForwardCount);
            Assert.False(mediaHandler.First.IsReleased);
            Assert.False(mediaHandler.First.Completion.Task.IsCompleted);
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
            Assert.False(participantMedia.TryGet(HostDeviceId, out _));
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
            Assert.True(oldProtection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(capture.HasCurrentCapture);
            Assert.False(controlPeer.HasRetainedGeneration);

            replacementParticipantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(
                        hostIdentity.PublicIdentity,
                        Now,
                        participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
            replacementParticipantRun = participantSessionHandler
                .RunAsync(replacementParticipantConnection, deadline.Token)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease replacementParticipantProbe =
                await WaitForConnectionLeaseAsync(
                    participantHandler,
                    HostDeviceId,
                    requireVerifiedPeer: true,
                    deadline.Token);
            long replacementParticipantGeneration =
                replacementParticipantProbe.Generation;
            await replacementParticipantProbe.DisposeAsync()
                .AsTask()
                .WaitAsync(deadline.Token);
            AuthenticatedRemoteWindowConnectionLease replacementHostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            long replacementHostGeneration = replacementHostLease.Generation;
            replacementHostConnection = new AbaObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(
                    replacementHostLease));
            Assert.True(replacementParticipantGeneration > oldParticipantGeneration);
            Assert.True(replacementHostGeneration > oldHostGeneration);
            replacementProtection = new RecordingProtectionSource(
                NativeRemoteWindowProtectionObservation.Create(
                    SafeNow(),
                    ownerGeneration: 1,
                    sessionGeneration: 1,
                    sourceRegistration.Source.SourceGeneration,
                    revision: 2));
            replacementStart = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        replacementHostConnection,
                        replacementProtection,
                        MirrorParticipantRole.DriverEligible),
                    deadline.Token)
                .AsTask();

            await mediaHandler.Second.Entered.Task.WaitAsync(deadline.Token);
            await rendererFactory.SecondRendererReturned.Task.WaitAsync(
                deadline.Token);
            await WaitForConditionAsync(
                () => replacementHostConnection.WaitForMediaAttachmentCount >= 1,
                deadline.Token);

            RemoteWindowPreparationRequest oldRequest = Assert.IsType<
                RemoteWindowPreparationRequest>(rendererFactory.FirstRequest);
            RemoteWindowPreparationRequest replacementRequest = Assert.IsType<
                RemoteWindowPreparationRequest>(rendererFactory.SecondRequest);
            RemoteWindowMediaRouteBinding oldBinding = Assert.IsType<
                RemoteWindowMediaRouteBinding>(mediaHandler.First.Binding);
            RemoteWindowMediaRouteBinding replacementBinding = Assert.IsType<
                RemoteWindowMediaRouteBinding>(mediaHandler.Second.Binding);
            Assert.Equal(oldRequest.SessionId, oldBinding.SessionId);
            Assert.Equal(replacementRequest.SessionId, replacementBinding.SessionId);
            Assert.NotEqual(oldRequest.CorrelationId, replacementRequest.CorrelationId);
            Assert.NotEqual(oldRequest.SessionId, replacementRequest.SessionId);
            Assert.NotEqual(oldBinding.RouteId, replacementBinding.RouteId);
            Assert.NotEqual(oldBinding, replacementBinding);
            Assert.Equal(oldRequest.ActivityId, replacementRequest.ActivityId);
            Assert.Equal(oldBinding.ActivityId, replacementBinding.ActivityId);
            Assert.Equal(Version, oldBinding.ProtocolVersion);
            Assert.Equal(Version, replacementBinding.ProtocolVersion);
            Assert.Equal(
                oldBinding.InitiatorDeviceId,
                replacementBinding.InitiatorDeviceId);
            Assert.Equal(
                oldBinding.ResponderDeviceId,
                replacementBinding.ResponderDeviceId);
            Assert.Equal(2, mediaHandler.CallCount);
            Assert.Equal(2, rendererFactory.PrepareCount);
            Assert.Equal(1, replacementHostConnection.PrepareCount);
            Assert.Equal(1, replacementHostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, replacementHostConnection.WaitForMediaAttachmentCount);
            Assert.Equal(0, mediaHandler.Second.ForwardCount);
            Assert.False(mediaHandler.Second.IsReleased);
            Assert.True(hostMedia.TryGet(
                ParticipantDeviceId,
                out AuthenticatedRemoteWindowMediaSession?
                    replacementHostSession));
            AuthenticatedRemoteWindowMediaSession currentHostSession =
                Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                    replacementHostSession);
            Assert.True(currentHostSession.IsCurrent);
            Assert.False(currentHostSession.IsAttached);
            Assert.Equal(replacementBinding, currentHostSession.Binding);
            Assert.True(participantMedia.TryGet(
                HostDeviceId,
                out AuthenticatedRemoteWindowMediaSession?
                    replacementParticipantSession));
            AuthenticatedRemoteWindowMediaSession currentParticipantSession =
                Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                    replacementParticipantSession);
            Assert.True(currentParticipantSession.IsCurrent);
            Assert.True(currentParticipantSession.IsAttached);
            Assert.Equal(replacementBinding, currentParticipantSession.Binding);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.False(replacementStart.IsCompleted);
            Assert.Equal(0, replacementHostConnection.AdmissionPublishCount);
            Assert.Equal(0, replacementHostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);

            mediaHandler.First.Release();
            Assert.True(mediaHandler.First.IsReleased);
            Assert.False(mediaHandler.Second.IsReleased);
            Exception oldAttachmentFailure = Assert.IsType<InvalidDataException>(
                await mediaHandler.First.Completion.Task.WaitAsync(deadline.Token));
            Assert.Contains(
                "no live owning control connection",
                oldAttachmentFailure.Message,
                StringComparison.Ordinal);
            Assert.Equal(1, mediaHandler.First.ForwardCount);
            await mediaHandler.First.Exited.Task.WaitAsync(deadline.Token);
            Assert.True(mediaHandler.First.Exited.Task.IsCompletedSuccessfully);
            Assert.Equal(0, mediaHandler.Second.ForwardCount);
            Assert.False(replacementStart.IsCompleted);
            Assert.True(replacementHostConnection.IsCurrent);
            Assert.Equal(0, replacementHostConnection.FailCloseCount);
            Assert.Equal(0, replacementHostConnection.DisposeCount);
            Assert.Equal(0, replacementHostConnection.AdmissionPublishCount);
            Assert.Equal(0, replacementHostConnection.MediaSendCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.False(currentHostSession.IsAttached);
            Assert.True(currentParticipantSession.IsAttached);
            Assert.False(currentHostSession.ControlStopToken.IsCancellationRequested);
            Assert.False(
                currentParticipantSession.ControlStopToken.IsCancellationRequested);
            Assert.Equal(1, hostMedia.Routes.Count);
            Assert.True(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? hostProbe));
            await using (AuthenticatedRemoteWindowConnectionLease currentProbe =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(hostProbe))
            {
                Assert.Equal(replacementHostGeneration, currentProbe.Generation);
            }

            Assert.True(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? participantProbe));
            await using (AuthenticatedRemoteWindowConnectionLease currentProbe =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                    participantProbe))
            {
                Assert.Equal(
                    replacementParticipantGeneration,
                    currentProbe.Generation);
            }

            mediaHandler.Second.Release();
            RemoteWindowCommandResult started = await replacementStart.WaitAsync(
                deadline.Token);
            replacementStartObserved = true;
            Assert.Equal(RemoteWindowCommandStatus.Applied, started.Status);
            Assert.True(replacementHostConnection.AttachmentObserved);
            Assert.Equal(1, replacementHostConnection.AdmissionPublishCount);
            Assert.Equal(1, capture.StartCount);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.Equal(0, renderer.RenderCount);
            Assert.Equal(0, replacementHostConnection.MediaSendCount);
            Assert.True(controlPeer.HasRetainedGeneration);
            Assert.Equal(replacementRequest.SessionId, controlPeer.SessionId);
            Assert.Equal(replacementRequest.ActivityId, controlPeer.ActivityId);
            Assert.True(currentHostSession.IsAttached);
            Assert.True(currentParticipantSession.IsAttached);
            Assert.Equal(1, mediaHandler.Second.ForwardCount);
            Assert.False(mediaHandler.Second.Completion.Task.IsCompleted);
            replacementBudget = Assert.IsType<RemoteWindowMediaSessionBudget>(
                coordinator.ActiveMediaBudget);
            Assert.Equal(
                new RemoteWindowMediaBudgetSnapshot(1, 0, 0),
                replacementBudget.Snapshot);

            TrackingMemoryOwner renderedOwner = await capture.EmitFrameAsync(
                sequence: 2,
                deadline.Token);
            await renderer.Rendered.Task.WaitAsync(deadline.Token);
            Assert.Equal(1, renderedOwner.DisposeCount);
            Assert.Equal(1, renderer.RenderCount);
            Assert.Equal((2, 2), renderer.LastSize);
            Assert.Equal(
                NativeRemoteWindowPixelFormat.Bgra8888,
                renderer.LastFormat);
            AssertOpaqueRed(renderer.LastPixels);
            Assert.True(replacementHostConnection.MediaSendCount >= 1);

            RemoteWindowStopResult stopped = await coordinator.StopAsync(
                deadline.Token);
            Assert.True(stopped.FullyStopped);
            await ObserveSessionStopAsync(replacementParticipantRun);
            Assert.Null(await mediaHandler.Second.Completion.Task.WaitAsync(
                deadline.Token));
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);
            await WaitForConditionAsync(
                () => renderer.IsDisposed,
                deadline.Token);

            Assert.Equal(1, replacementHostConnection.FailCloseCount);
            Assert.Equal(1, replacementHostConnection.DisposeCount);
            Assert.False(replacementHostConnection.IsCurrent);
            Assert.True(replacementProtection.IsDisposed);
            Assert.True(renderer.IsDisposed);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, replacementBudget.Snapshot);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(capture.HasCurrentCapture);
            Assert.True(capture.StopCount >= 1);
            Assert.True(input.StopCount >= 1);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
            Assert.False(participantMedia.TryGet(HostDeviceId, out _));
            Assert.False(hostHandler.TryAcquireRemoteWindowConnection(
                ParticipantDeviceId,
                out _));
            Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(
                HostDeviceId,
                out _));
            Assert.Equal(2, mediaHandler.CallCount);
            Assert.Equal(2, rendererFactory.PrepareCount);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Null(coordinator.TerminalFailure);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            mediaHandler.First.Release();
            mediaHandler.Second.Release();
            if (!oldStartObserved)
            {
                await ObserveOldStartCleanupAsync(oldStart);
            }

            if (!replacementStartObserved)
            {
                await ObserveCleanupTaskAsync(
                    replacementStart,
                    allowRendererStartFailure: false);
            }

            await DisposeOwnerAsync(coordinator);
            await DisposeCurrentHostConnectionAsync(replacementHostConnection);
            await DisposeCurrentHostConnectionAsync(oldHostConnection);
            await DisposeConnectionAsync(replacementParticipantConnection);
            await DisposeConnectionAsync(oldParticipantConnection);
            Exception? listenerCancelFailure = Record.Exception(
                listenerStop.Cancel);
            if (listenerCancelFailure is not null)
            {
                cleanupFailures.Add(listenerCancelFailure);
            }

            Exception? socketStopFailure = Record.Exception(socket.Stop);
            if (socketStopFailure is not null)
            {
                cleanupFailures.Add(socketStopFailure);
            }

            await ObserveCleanupTaskAsync(
                replacementParticipantRun,
                allowRendererStartFailure: false);
            await ObserveCleanupTaskAsync(
                oldParticipantRun,
                allowRendererStartFailure: false);
            Exception? listenerFailure = await Record.ExceptionAsync(
                async () => await ObserveListenerStopAsync(
                    listenerRun,
                    listenerStop.Token));
            if (listenerFailure is not null)
            {
                cleanupFailures.Add(listenerFailure);
            }

            await DisposeOwnerAsync(preparationPeer);
            await DisposeOwnerAsync(participantHandler);
            await DisposeOwnerAsync(hostHandler);
            await DisposeOwnerAsync(participantMedia);
            await DisposeOwnerAsync(hostMedia);
            await DisposeOwnerAsync(participantTrust);
            await DisposeOwnerAsync(hostTrust);
            oldProtection.Dispose();
            replacementProtection?.Dispose();
        }

        if (primaryFailure is not null && cleanupFailures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (primaryFailure is not null)
        {
            cleanupFailures.Insert(0, primaryFailure);
            throw new AggregateException(
                "Managed renderer replacement ABA tracer and cleanup both failed.",
                cleanupFailures);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Managed renderer replacement ABA tracer cleanup failed.",
                cleanupFailures);
        }

        bool TryAcquireParticipantConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);

        async Task WaitForConditionAsync(
            Func<bool> condition,
            CancellationToken cancellationToken)
        {
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
            }
        }

        async Task ObserveOldStartCleanupAsync(
            Task<RemoteWindowCommandResult>? running)
        {
            if (running is null)
            {
                return;
            }

            Exception? failure = await Record.ExceptionAsync(async () =>
                await running.WaitAsync(TimeSpan.FromSeconds(5)));
            if (failure is InvalidOperationException invalidOperation
                && invalidOperation.Message.Contains(
                    "renderer_start_failed",
                    StringComparison.Ordinal))
            {
                return;
            }

            if (failure is not null)
            {
                cleanupFailures.Add(failure);
            }
        }

        async Task ObserveCleanupTaskAsync(
            Task? running,
            bool allowRendererStartFailure)
        {
            if (running is null)
            {
                return;
            }

            Exception? failure = await Record.ExceptionAsync(async () =>
                await running.WaitAsync(TimeSpan.FromSeconds(5)));
            if (failure is null
                || RemoteWindowSessionStopClassifier.IsExpected(failure))
            {
                return;
            }

            if (allowRendererStartFailure
                && failure is InvalidOperationException invalidOperation
                && invalidOperation.Message.Contains(
                    "renderer_start_failed",
                    StringComparison.Ordinal))
            {
                return;
            }

            cleanupFailures.Add(failure);
        }

        async Task DisposeConnectionAsync(
            AuthenticatedTcpControlConnection? connection)
        {
            if (connection is not null)
            {
                await CaptureCleanupAsync(async () =>
                    await connection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }

        async Task DisposeCurrentHostConnectionAsync(
            IDesktopRemoteWindowHostConnection? connection)
        {
            if (connection is not null)
            {
                await CaptureCleanupAsync(async () =>
                    await connection.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }

        async Task DisposeOwnerAsync(IAsyncDisposable owner) =>
            await CaptureCleanupAsync(async () =>
                await owner.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));

        async Task CaptureCleanupAsync(Func<Task> cleanup)
        {
            Exception? failure = await Record.ExceptionAsync(cleanup);
            if (failure is not null)
            {
                cleanupFailures.Add(failure);
            }
        }
    }

    [Fact]
    public Task VerifiedFsm1AttachmentThenCallerCancellationFailsClosedBeforeAdmissionOrCapture() =>
        RunVerifiedFsm1AttachmentThenPreAdmissionFailureAsync(
            PreAdmissionFailureTrigger.CallerCancellation);

    [Fact]
    public Task VerifiedFsm1AttachmentThenPreparationExpiryFailsClosedBeforeAdmissionOrCapture() =>
        RunVerifiedFsm1AttachmentThenPreAdmissionFailureAsync(
            PreAdmissionFailureTrigger.HostDeadline);

    [Fact]
    public Task MediaMutationAfterPreparationPromotionTriggersLiveCallbackBeforeCapture() =>
        RunVerifiedFsm1AttachmentThenPreAdmissionFailureAsync(
            PreAdmissionFailureTrigger.MediaMutationAtPromotion);

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
        var connectionGenerationRevokedAtMediaMutationReturn = true;
        var emergencyStopCountAtMediaMutationReturn = -1;

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
                    else if (trigger ==
                        PreAdmissionFailureTrigger.CallerCancellation)
                    {
                        callerCancellation.Cancel();
                    }
                },
                beforeConnectionPreparationRelease: trigger ==
                    PreAdmissionFailureTrigger.MediaMutationAtPromotion
                        ? () =>
                        {
                            Assert.True(hostMedia.TryGet(
                                ParticipantDeviceId,
                                out AuthenticatedRemoteWindowMediaSession?
                                    hostSession));
                            Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                                    hostSession)
                                .RequestControlStop();
                            connectionGenerationRevokedAtMediaMutationReturn =
                                hostLease.IsRevoked;
                            emergencyStopCountAtMediaMutationReturn =
                                capture.EmergencyStopCount;
                        }
            : null);
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
            else if (trigger == PreAdmissionFailureTrigger.CallerCancellation)
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
            else
            {
                InvalidOperationException failure = await Assert.ThrowsAsync<
                    InvalidOperationException>(async () =>
                        await coordinator.StartAsync(
                            request,
                            startCancellationToken));
                Assert.Equal(
                    "Remote Window host start failed (authenticated_connection_stale).",
                    failure.Message);
                Assert.Null(failure.InnerException);
            }

            Assert.False(harnessDeadline.IsCancellationRequested);
            if (trigger == PreAdmissionFailureTrigger.MediaMutationAtPromotion)
            {
                Assert.False(connectionGenerationRevokedAtMediaMutationReturn);
                Assert.Equal(1, emergencyStopCountAtMediaMutationReturn);
            }

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
                Assert.Equal(
                    trigger == PreAdmissionFailureTrigger.CallerCancellation,
                    callerCancellation.IsCancellationRequested);
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
            Assert.Equal(
                trigger == PreAdmissionFailureTrigger.MediaMutationAtPromotion
                    ? 1
                    : 0,
                capture.EmergencyStopCount);
            Assert.Equal(
                trigger == PreAdmissionFailureTrigger.MediaMutationAtPromotion
                    ? 1
                    : 0,
                input.EmergencyStopCount);
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
        MediaMutationAtPromotion,
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

    public enum TerminalCleanupFault
    {
        EmergencyStopRegistration,
        CaptureEmergencyStop,
        InputEmergencyStop,
        HostFailClose,
        HostConnectionDispose,
        EmergencyStopRegistrationAndHostConnectionDispose,
        CaptureEmergencyStopAndEmergencyStopRegistration,
    }

    [Theory]
    [InlineData(TerminalCleanupFault.EmergencyStopRegistration)]
    [InlineData(TerminalCleanupFault.CaptureEmergencyStop)]
    [InlineData(TerminalCleanupFault.InputEmergencyStop)]
    [InlineData(TerminalCleanupFault.HostFailClose)]
    [InlineData(TerminalCleanupFault.HostConnectionDispose)]
    [InlineData(
        TerminalCleanupFault.EmergencyStopRegistrationAndHostConnectionDispose)]
    [InlineData(
        TerminalCleanupFault.CaptureEmergencyStopAndEmergencyStopRegistration)]
    public async Task AuthenticatedControlDisconnectCleanupFaultDrainsAndRemainsObservable(
        TerminalCleanupFault cleanupFault)
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
        var captureInjected = new IOException(
            "injected capture Emergency Stop cleanup failure");
        var inputInjected = new IOException(
            "injected input Emergency Stop cleanup failure");
        var failCloseInjected = new IOException(
            "injected host fail-close cleanup failure");
        var connectionDisposeInjected = new IOException(
            "injected host connection disposal cleanup failure");
        var registrationInjected = new IOException(
            "injected Emergency Stop registration cleanup failure");
        bool injectCapture = cleanupFault is
            TerminalCleanupFault.CaptureEmergencyStop
            or TerminalCleanupFault.CaptureEmergencyStopAndEmergencyStopRegistration;
        bool injectInput = cleanupFault == TerminalCleanupFault.InputEmergencyStop;
        bool injectFailClose = cleanupFault == TerminalCleanupFault.HostFailClose;
        bool injectConnectionDispose = cleanupFault is
            TerminalCleanupFault.HostConnectionDispose
            or TerminalCleanupFault
                .EmergencyStopRegistrationAndHostConnectionDispose;
        bool injectRegistration = cleanupFault is
            TerminalCleanupFault.EmergencyStopRegistration
            or TerminalCleanupFault
                .EmergencyStopRegistrationAndHostConnectionDispose
            or TerminalCleanupFault.CaptureEmergencyStopAndEmergencyStopRegistration;
        var capture = new RecordingCaptureBoundary
        {
            EmergencyStopFailure = injectCapture ? captureInjected : null,
        };
        var input = new RecordingInputBoundary
        {
            EmergencyStopFailure = injectInput ? inputInjected : null,
        };
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        var emergencyStops = new RecordingEmergencyStopRegistrar(
            injectRegistration ? registrationInjected : null);
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
        Exception? expectedTerminalFailure = null;
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
                rendererFactory,
                injectedFailCloseFailure: injectFailClose
                    ? failCloseInjected
                    : null,
                injectedDisposeFailure: injectConnectionDispose
                    ? connectionDisposeInjected
                    : null);
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
            if (cleanupFault
                == TerminalCleanupFault
                    .CaptureEmergencyStopAndEmergencyStopRegistration)
            {
                while (coordinator.TerminalFailure is not AggregateException
                    { InnerExceptions.Count: 2 })
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1), deadline.Token);
                }
            }

            Assert.Null(coordinator.Snapshot);
            expectedTerminalFailure = Assert.IsAssignableFrom<Exception>(
                coordinator.TerminalFailure);
            if (cleanupFault == TerminalCleanupFault.EmergencyStopRegistration)
            {
                Assert.Same(registrationInjected, expectedTerminalFailure);
            }
            else if (cleanupFault == TerminalCleanupFault.CaptureEmergencyStop)
            {
                AssertCaptureProjection(expectedTerminalFailure, captureInjected);
            }
            else if (cleanupFault == TerminalCleanupFault.InputEmergencyStop)
            {
                AssertInputProjection(expectedTerminalFailure, inputInjected);
            }
            else if (cleanupFault == TerminalCleanupFault.HostFailClose)
            {
                Assert.Same(failCloseInjected, expectedTerminalFailure);
            }
            else if (cleanupFault == TerminalCleanupFault.HostConnectionDispose)
            {
                Assert.Same(connectionDisposeInjected, expectedTerminalFailure);
            }
            else if (cleanupFault
                == TerminalCleanupFault
                    .EmergencyStopRegistrationAndHostConnectionDispose)
            {
                AggregateException combinedFailure = Assert.IsType<
                    AggregateException>(expectedTerminalFailure);
                Assert.Equal(2, combinedFailure.InnerExceptions.Count);
                Assert.Same(
                    registrationInjected,
                    combinedFailure.InnerExceptions[0]);
                Assert.Same(
                    connectionDisposeInjected,
                    combinedFailure.InnerExceptions[1]);
            }
            else
            {
                AggregateException combinedFailure = Assert.IsType<
                    AggregateException>(expectedTerminalFailure);
                Assert.Equal(2, combinedFailure.InnerExceptions.Count);
                AssertCaptureProjection(
                    combinedFailure.InnerExceptions[0],
                    captureInjected);
                Assert.Same(
                    registrationInjected,
                    combinedFailure.InnerExceptions[1]);
                Assert.DoesNotContain(
                    captureInjected.Message,
                    combinedFailure.ToString(),
                    StringComparison.Ordinal);
            }
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);
            bool injectEmergencyBoundary = injectCapture || injectInput;
            int expectedEmergencyStopCount = injectEmergencyBoundary ? 2 : 1;
            Assert.Equal(expectedEmergencyStopCount, capture.EmergencyStopCount);
            Assert.Equal(
                injectCapture ? 1 : 0,
                capture.EmergencyStopFailureCount);
            Assert.Equal(expectedEmergencyStopCount, input.EmergencyStopCount);
            Assert.Equal(
                injectInput ? 1 : 0,
                input.EmergencyStopFailureCount);
            Assert.Equal(
                injectInput ? 1 : 0,
                input.EmergencyStopAppliedBeforeFailureCount);
            Assert.Equal(
                injectEmergencyBoundary ? 3 : 2,
                sessions.DisconnectAllCount);
            Assert.Equal(1, capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.False(capture.HasCurrentCapture);
            Assert.True(input.IsEmergencyStopped);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.Equal(1, emergencyStops.RegistrationDisposeCount);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(
                injectFailClose ? 1 : 0,
                hostConnection.FailCloseFailureCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.Equal(
                injectConnectionDispose ? 1 : 0,
                hostConnection.DisposeFailureCount);
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

            Exception? disposalFailure = await Record.ExceptionAsync(
                async () => await coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(deadline.Token));
            Assert.Same(expectedTerminalFailure, disposalFailure);
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
                    || !ReferenceEquals(
                        expectedTerminalFailure,
                        coordinatorDisposeFailure)))
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

        static void AssertCaptureProjection(
            Exception projected,
            Exception captureFailure)
        {
            InvalidOperationException projectedFailure = Assert.IsType<
                InvalidOperationException>(projected);
            Assert.Equal(
                "Remote Window host emergency stop was not fully confirmed "
                    + "(capture=local_boundary_exception, "
                    + "input=native_input_emergency_stopped, "
                    + "sessions=all_peers_disconnected).",
                projectedFailure.Message,
                ignoreCase: false,
                ignoreLineEndingDifferences: false,
                ignoreWhiteSpaceDifferences: false);
            Assert.Null(projectedFailure.InnerException);
            Assert.DoesNotContain(
                captureFailure.Message,
                projectedFailure.ToString(),
                StringComparison.Ordinal);
        }

        static void AssertInputProjection(
            Exception projected,
            Exception inputFailure)
        {
            InvalidOperationException projectedFailure = Assert.IsType<
                InvalidOperationException>(projected);
            Assert.Equal(
                "Remote Window host emergency stop was not fully confirmed "
                    + "(capture=native_capture_emergency_stopped, "
                    + "input=local_boundary_exception, "
                    + "sessions=all_peers_disconnected).",
                projectedFailure.Message,
                ignoreCase: false,
                ignoreLineEndingDifferences: false,
                ignoreWhiteSpaceDifferences: false);
            Assert.Null(projectedFailure.InnerException);
            Assert.DoesNotContain(
                inputFailure.Message,
                projectedFailure.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AuthenticatedControlDisconnectBlockedHostDisposeTimesOutAndPermanentlyBlocksRestart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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

        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var sessions = new RecordingSharingSessionBoundary();
        var emergencyStops = new RecordingEmergencyStopRegistrar();
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
        var cleanupTimeProvider = new ManualTimeProvider(Now);
        TimeSpan cleanupTimeout = TimeSpan.FromSeconds(10);
        var originalDisposeRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timerCreateCountAtRevocationCallbackReturn = -1;
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
            preparationLifetime: TimeSpan.FromSeconds(10),
            cleanupTimeProvider,
            cleanupTimeout);

        AuthenticatedTcpControlConnection? participantConnection = null;
        Task? participantRun = null;
        ObservingHostConnection? hostConnection = null;
        ObservingHostConnection? replacementHostConnection = null;
        RecordingProtectionSource? replacementProtection = null;
        RecordingProtectionSource? secondReplacementProtection = null;
        RemoteWindowMediaSessionBudget? activeBudget = null;
        Task<RemoteWindowCommandResult>? firstRestart = null;
        Exception? timeoutFailure = null;
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
            Assert.Equal(new ProtocolVersion(1, 7), Version);
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
            long originalHostConnectionGeneration = hostLease.Generation;
            hostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                capture,
                renderer,
                rendererFactory,
                disposeRelease: originalDisposeRelease.Task,
                afterRevocationCallback: () => Volatile.Write(
                    ref timerCreateCountAtRevocationCallbackReturn,
                    cleanupTimeProvider.TimerCreateCount));
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
            TrackingMemoryOwner renderedOwner = await capture.EmitFrameAsync(
                sequence: 2,
                deadline.Token);
            await renderer.Rendered.Task.WaitAsync(deadline.Token);

            Assert.Equal(1, renderedOwner.DisposeCount);
            Assert.Equal(Version, hostConnection.ProtocolVersion);
            Assert.True(hostConnection.ReadyObserved);
            Assert.True(hostConnection.AttachmentObserved);
            Assert.Equal(1, hostConnection.WaitForMediaAttachmentCount);
            Assert.True(hostConnection.AdmissionPublished);
            Assert.Equal(1, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(1, hostConnection.PrepareCount);
            Assert.Equal(1, hostConnection.AdmissionPublishCount);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Equal(1, capture.StartCount);
            Assert.Equal(1, renderer.RenderCount);
            Assert.Equal(1, permissions.PreparationReservationCount);
            Assert.Equal(1, protection.PreparationReservationCount);
            Assert.Equal(1, emergencyStops.ReadinessReservationCount);
            Assert.Equal(1, emergencyStops.RegistrationCount);
            Assert.NotNull(coordinator.Snapshot);
            Assert.True(controlPeer.HasRetainedGeneration);
            Assert.True(hostMedia.TryGet(
                ParticipantDeviceId,
                out AuthenticatedRemoteWindowMediaSession? hostSession));
            AuthenticatedRemoteWindowMediaSession attachedHostSession =
                Assert.IsType<AuthenticatedRemoteWindowMediaSession>(hostSession);
            Assert.True(attachedHostSession.IsAttached);
            Assert.Equal(
                new ProtocolVersion(1, 7),
                attachedHostSession.ProtocolVersion);
            Assert.Equal(
                new ProtocolVersion(1, 7),
                Assert.IsType<RemoteWindowMediaRouteBinding>(
                    attachedHostSession.Binding).ProtocolVersion);
            Assert.True(participantMedia.TryGet(
                HostDeviceId,
                out AuthenticatedRemoteWindowMediaSession? participantSession));
            Assert.True(Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                participantSession).IsAttached);
            Assert.Equal(1, hostMedia.Routes.Count);

            AuthenticatedRemoteWindowConnectionLease replacementHostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    ParticipantDeviceId,
                    requireVerifiedPeer: false,
                    deadline.Token);
            Assert.Equal(
                originalHostConnectionGeneration,
                replacementHostLease.Generation);
            replacementHostConnection = new ObservingHostConnection(
                new AuthenticatedDesktopRemoteWindowHostConnection(
                    replacementHostLease),
                capture,
                renderer,
                rendererFactory);

            int admittedRouteCount = hostConnection.PrepareResponderRouteCount;
            int admittedPrepareCount = hostConnection.PrepareCount;
            int admittedAdmissionCount = hostConnection.AdmissionPublishCount;
            int admittedCaptureCount = capture.StartCount;
            int admittedRendererPrepareCount = rendererFactory.PrepareCount;
            int admittedPermissionReservationCount =
                permissions.PreparationReservationCount;
            int admittedEmergencyReadinessCount =
                emergencyStops.ReadinessReservationCount;
            int admittedEmergencyRegistrationCount =
                emergencyStops.RegistrationCount;

            await participantConnection.DisposeAsync()
                .AsTask()
                .WaitAsync(deadline.Token);
            await hostConnection.ConnectionRevoked.Task.WaitAsync(deadline.Token);
            await hostConnection.DisposeEntered.Task.WaitAsync(deadline.Token);
            await ObserveSessionStopAsync(participantRun);
            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.Null(coordinator.Snapshot);
            Assert.True(coordinator.HasRetiringGeneration);
            Assert.Null(coordinator.TerminalFailure);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(
                1,
                Volatile.Read(ref timerCreateCountAtRevocationCallbackReturn));
            Assert.Equal(
                hostConnection.RevocationCallbackThreadId,
                cleanupTimeProvider.TimerCreateThreadId);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.False(hostConnection.DisposalCompleted.Task.IsCompleted);
            Assert.False(hostConnection.IsCurrent);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);

            replacementProtection = new RecordingProtectionSource(
                NativeRemoteWindowProtectionObservation.Create(
                    SafeNow(),
                    ownerGeneration: 1,
                    sessionGeneration: 1,
                    sourceRegistration.Source.SourceGeneration,
                    revision: 2));
            firstRestart = coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        replacementHostConnection,
                        replacementProtection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token)
                .AsTask();

            cleanupTimeProvider.Advance(
                cleanupTimeout - TimeSpan.FromTicks(1));

            Assert.False(firstRestart.IsCompleted);
            Assert.Null(coordinator.TerminalFailure);
            Assert.True(coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.False(hostConnection.DisposalCompleted.Task.IsCompleted);
            Assert.Equal(0, replacementHostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, replacementHostConnection.PrepareCount);
            Assert.Equal(0, replacementHostConnection.AdmissionPublishCount);
            Assert.Equal(0, replacementHostConnection.DisposeCount);
            Assert.Equal(0, replacementProtection.PreparationReservationCount);
            Assert.Equal(admittedCaptureCount, capture.StartCount);
            Assert.Equal(admittedRendererPrepareCount, rendererFactory.PrepareCount);
            Assert.Equal(
                admittedPermissionReservationCount,
                permissions.PreparationReservationCount);
            Assert.Equal(
                admittedEmergencyReadinessCount,
                emergencyStops.ReadinessReservationCount);
            Assert.Equal(
                admittedEmergencyRegistrationCount,
                emergencyStops.RegistrationCount);

            cleanupTimeProvider.Advance(TimeSpan.FromTicks(1));
            while (coordinator.TerminalFailure is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), deadline.Token);
            }

            timeoutFailure = Assert.IsType<InvalidOperationException>(
                coordinator.TerminalFailure);
            Assert.Equal(
                "Remote Window host cleanup confirmation failed "
                    + "(host_cleanup_timeout).",
                timeoutFailure.Message);
            Assert.Equal(Now.Add(cleanupTimeout), cleanupTimeProvider.UtcNow);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.True(coordinator.HasRetiringGeneration);
            Assert.False(hostConnection.DisposalCompleted.Task.IsCompleted);
            InvalidOperationException firstRestartFailure = await Assert
                .ThrowsAsync<InvalidOperationException>(async () =>
                    await firstRestart.WaitAsync(deadline.Token));
            Assert.Contains(
                "host_cleanup_unconfirmed",
                firstRestartFailure.Message,
                StringComparison.Ordinal);
            await replacementHostConnection.DisposalCompleted.Task.WaitAsync(
                deadline.Token);

            Assert.Equal(0, replacementHostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, replacementHostConnection.PrepareCount);
            Assert.Equal(0, replacementHostConnection.AdmissionPublishCount);
            Assert.Equal(0, replacementHostConnection.FailCloseCount);
            Assert.Equal(1, replacementHostConnection.DisposeCount);
            Assert.Equal(0, replacementHostConnection.MediaSendCount);
            Assert.Equal(0, replacementProtection.PreparationReservationCount);
            Assert.True(replacementProtection.IsDisposed);
            Assert.Equal(admittedRouteCount, hostConnection.PrepareResponderRouteCount);
            Assert.Equal(admittedPrepareCount, hostConnection.PrepareCount);
            Assert.Equal(admittedAdmissionCount, hostConnection.AdmissionPublishCount);
            Assert.Equal(admittedCaptureCount, capture.StartCount);
            Assert.Equal(admittedRendererPrepareCount, rendererFactory.PrepareCount);
            Assert.Equal(
                admittedPermissionReservationCount,
                permissions.PreparationReservationCount);
            Assert.Equal(
                admittedEmergencyReadinessCount,
                emergencyStops.ReadinessReservationCount);
            Assert.Equal(
                admittedEmergencyRegistrationCount,
                emergencyStops.RegistrationCount);
            Assert.Same(timeoutFailure, coordinator.TerminalFailure);
            Assert.False(hostConnection.DisposalCompleted.Task.IsCompleted);

            originalDisposeRelease.TrySetResult();
            await hostConnection.DisposalCompleted.Task.WaitAsync(deadline.Token);
            while (coordinator.HasRetiringGeneration)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), deadline.Token);
            }

            await WaitForCleanupAsync(
                hostHandler,
                participantHandler,
                hostMedia,
                participantMedia,
                deadline.Token);

            Assert.False(coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(0, cleanupTimeProvider.ActiveTimerCount);
            Assert.Null(coordinator.Snapshot);
            Assert.Null(coordinator.ActiveMediaBudget);
            Assert.Same(timeoutFailure, coordinator.TerminalFailure);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);
            Assert.Equal(1, hostConnection.FailCloseCount);
            Assert.Equal(1, hostConnection.DisposeCount);
            Assert.True(hostConnection.DisposalCompleted.Task.IsCompletedSuccessfully);
            Assert.False(replacementHostConnection.IsCurrent);
            Assert.True(
                replacementHostConnection.DisposalCompleted.Task
                    .IsCompletedSuccessfully);
            Assert.True(renderer.IsDisposed);
            Assert.True(protection.IsDisposed);
            Assert.False(capture.HasCurrentCapture);
            Assert.True(capture.EmergencyStopCount >= 1);
            Assert.True(input.EmergencyStopCount >= 1);
            Assert.True(sessions.DisconnectAllCount >= 1);
            Assert.Equal(0, permissions.ObserverCount);
            Assert.Equal(0, permissions.CurrentPreparationReservationCount);
            Assert.False(emergencyStops.HasCurrentReadiness);
            Assert.False(emergencyStops.HasCurrentRegistration);
            Assert.False(controlPeer.HasRetainedGeneration);
            Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
            Assert.Equal(0, hostMedia.Routes.Count);
            Assert.Equal(0, participantMedia.Routes.Count);
            Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
            Assert.False(participantMedia.TryGet(HostDeviceId, out _));
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

            secondReplacementProtection = new RecordingProtectionSource(
                NativeRemoteWindowProtectionObservation.Create(
                    SafeNow(),
                    ownerGeneration: 1,
                    sessionGeneration: 1,
                    sourceRegistration.Source.SourceGeneration,
                    revision: 3));
            InvalidOperationException secondRestartFailure = await Assert
                .ThrowsAsync<InvalidOperationException>(async () =>
                    await coordinator.StartAsync(
                        new DesktopRemoteWindowHostStartRequest(
                            sourceLease,
                            ownerGeneration: 1,
                            replacementHostConnection,
                            secondReplacementProtection,
                            MirrorParticipantRole.ViewOnly),
                        deadline.Token));
            Assert.Contains(
                "host_cleanup_unconfirmed",
                secondRestartFailure.Message,
                StringComparison.Ordinal);
            Assert.True(secondReplacementProtection.IsDisposed);
            Assert.Equal(
                0,
                secondReplacementProtection.PreparationReservationCount);
            Assert.Equal(0, replacementHostConnection.PrepareResponderRouteCount);
            Assert.Equal(0, replacementHostConnection.PrepareCount);
            Assert.Equal(0, replacementHostConnection.AdmissionPublishCount);
            Assert.Equal(0, replacementHostConnection.FailCloseCount);
            Assert.Equal(2, replacementHostConnection.DisposeCount);
            Assert.Equal(admittedCaptureCount, capture.StartCount);
            Assert.Equal(admittedRendererPrepareCount, rendererFactory.PrepareCount);
            Assert.Equal(
                admittedPermissionReservationCount,
                permissions.PreparationReservationCount);
            Assert.Equal(
                admittedEmergencyReadinessCount,
                emergencyStops.ReadinessReservationCount);
            Assert.Equal(
                admittedEmergencyRegistrationCount,
                emergencyStops.RegistrationCount);
            Assert.Same(timeoutFailure, coordinator.TerminalFailure);

            Exception? coordinatorDisposeFailure = await Record.ExceptionAsync(
                async () => await coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(deadline.Token));
            Assert.Same(timeoutFailure, coordinatorDisposeFailure);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            originalDisposeRelease.TrySetResult();
            if (firstRestart is { IsCompleted: false })
            {
                cleanupTimeProvider.Advance(
                    DesktopRemoteWindowHostCoordinator
                        .MaximumCleanupConfirmationTimeout);
            }

            if (firstRestart is not null)
            {
                Exception? restartFailure = await Record.ExceptionAsync(
                    async () => await firstRestart.WaitAsync(
                        TimeSpan.FromSeconds(5)));
                if (restartFailure is TimeoutException)
                {
                    cleanupFailures.Add(restartFailure);
                }
            }

            Exception? coordinatorCleanupFailure = await Record.ExceptionAsync(
                async () => await coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));
            if (coordinatorCleanupFailure is not null
                && !ReferenceEquals(timeoutFailure, coordinatorCleanupFailure))
            {
                cleanupFailures.Add(coordinatorCleanupFailure);
            }

            if (hostConnection is not null)
            {
                Exception? hostConnectionDrainFailure = await Record
                    .ExceptionAsync(async () =>
                        await hostConnection.DisposalCompleted.Task.WaitAsync(
                            TimeSpan.FromSeconds(5)));
                if (hostConnectionDrainFailure is not null)
                {
                    cleanupFailures.Add(hostConnectionDrainFailure);
                }
            }

            Exception? retiringDrainFailure = await Record.ExceptionAsync(
                async () =>
                {
                    using var drainDeadline = new CancellationTokenSource(
                        TimeSpan.FromSeconds(5));
                    while (coordinator.HasRetiringGeneration)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(1),
                            drainDeadline.Token);
                    }
                });
            if (retiringDrainFailure is not null)
            {
                cleanupFailures.Add(retiringDrainFailure);
            }

            if (replacementHostConnection is { DisposeCount: 0 })
            {
                Exception? replacementHostCleanupFailure = await Record
                    .ExceptionAsync(async () =>
                        await replacementHostConnection.DisposeAsync()
                            .AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(5)));
                if (replacementHostCleanupFailure is not null)
                {
                    cleanupFailures.Add(replacementHostCleanupFailure);
                }
            }

            replacementProtection?.Dispose();
            secondReplacementProtection?.Dispose();
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
                "Managed cleanup-timeout tracer and cleanup both failed.",
                cleanupFailures);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Managed cleanup-timeout tracer cleanup failed.",
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

    private sealed class SinglePeerConnectionCandidateSource(
        VerifiedPeerConnectionCandidate candidate) :
        IPeerConnectionCandidateSource
    {
        private readonly VerifiedPeerConnectionCandidate candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));

        public bool TryGet(
            DeviceId peerDeviceId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out VerifiedPeerConnectionCandidate? resolved)
        {
            ArgumentNullException.ThrowIfNull(peerDeviceId);
            if (peerDeviceId == candidate.CandidateIdentity.DeviceId)
            {
                resolved = candidate;
                return true;
            }

            resolved = null;
            return false;
        }
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

    private static async Task<AuthenticatedRemoteWindowConnectionLease>
        WaitForConnectionLeaseOnChangeAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => changed.TrySetResult();
            handler.Changed += OnChanged;
            try
            {
                if (handler.TryAcquireRemoteWindowConnection(
                        peerDeviceId,
                        out AuthenticatedRemoteWindowConnectionLease? lease))
                {
                    return Assert.IsType<
                        AuthenticatedRemoteWindowConnectionLease>(lease);
                }

                await changed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                handler.Changed -= OnChanged;
            }
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

    private static async Task<AuthenticatedRemoteWindowMediaSession>
        WaitForAttachedMediaSessionOnChangeAsync(
        AuthenticatedRemoteWindowMediaSessionDirectory directory,
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => changed.TrySetResult();
            directory.Changed += OnChanged;
            try
            {
                if (directory.TryGet(
                        peerDeviceId,
                        out AuthenticatedRemoteWindowMediaSession? found))
                {
                    AuthenticatedRemoteWindowMediaSession session = Assert.IsType<
                        AuthenticatedRemoteWindowMediaSession>(found);
                    await session.WaitForAttachmentAsync(cancellationToken);
                    return session;
                }

                await changed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                directory.Changed -= OnChanged;
            }
        }
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

    private static async Task WaitForCleanupOnChangeAsync(
        AuthenticatedActivitySessionHandler hostHandler,
        AuthenticatedActivitySessionHandler participantHandler,
        AuthenticatedRemoteWindowMediaSessionDirectory hostMedia,
        AuthenticatedRemoteWindowMediaSessionDirectory participantMedia,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => changed.TrySetResult();
            hostHandler.Changed += OnChanged;
            participantHandler.Changed += OnChanged;
            hostMedia.Changed += OnChanged;
            participantMedia.Changed += OnChanged;
            try
            {
                if (!hostMedia.TryGet(ParticipantDeviceId, out _)
                    && !participantMedia.TryGet(HostDeviceId, out _)
                    && hostMedia.Routes.Count == 0
                    && participantMedia.Routes.Count == 0
                    && hostHandler.GetConnectedPeers().Count == 0
                    && participantHandler.GetConnectedPeers().Count == 0)
                {
                    return;
                }

                await changed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                hostHandler.Changed -= OnChanged;
                participantHandler.Changed -= OnChanged;
                hostMedia.Changed -= OnChanged;
                participantMedia.Changed -= OnChanged;
            }
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
        catch (Exception exception) when (
            RemoteWindowSessionStopClassifier.IsExpected(exception))
        {
        }
    }

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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) :
        TimeProvider,
        IClock
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private int timerCreateCount;
        private int timerCreateThreadId;
        private DateTimeOffset utcNow = utcNow;

        public int ActiveTimerCount
        {
            get
            {
                lock (gate)
                {
                    return timers.Count;
                }
            }
        }

        public int TimerCreateCount => Volatile.Read(ref timerCreateCount);

        public int TimerCreateThreadId => Volatile.Read(ref timerCreateThreadId);

        public DateTimeOffset UtcNow => GetUtcNow();

        public void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates.Where(timer => timer.IsDue(now)))
            {
                timer.Fire(now);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            Interlocked.Increment(ref timerCreateCount);
            _ = Interlocked.CompareExchange(
                ref timerCreateThreadId,
                Environment.CurrentManagedThreadId,
                comparand: 0);
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    period = newPeriod;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > now)
                    {
                        return;
                    }

                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : now.Add(period);
                }

                callback(state);
            }

            public bool IsDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueAt <= now;
                }
            }
        }
    }

    private sealed class ObservingHostConnection(
        AuthenticatedDesktopRemoteWindowHostConnection inner,
        RecordingCaptureBoundary capture,
        RecordingRenderer renderer,
        RecordingRendererFactory rendererFactory,
        Func<RemoteWindowPreparationRequest, ValueTask>?
            afterMediaAttachment = null,
        Exception? injectedFailCloseFailure = null,
        Exception? injectedDisposeFailure = null,
        Func<ValueTask>? afterAdmissionPublication = null,
        Exception? injectedAdmissionPublicationFailure = null,
        Action? beforeConnectionPreparationRelease = null,
        Task? disposeRelease = null,
        Action? afterRevocationCallback = null) :
        IDesktopRemoteWindowHostConnection
    {
        private int admissionPublishCount;
        private int admissionPublished;
        private int attachmentObserved;
        private int disposeCount;
        private readonly Task? disposeRelease = disposeRelease;
        private Exception? disposeFailure = injectedDisposeFailure;
        private int disposeFailureCount;
        private int failCloseCount;
        private Exception? failCloseFailure = injectedFailCloseFailure;
        private int failCloseFailureCount;
        private int mediaSendCount;
        private int mediaSentBeforeAdmissionCount;
        private int prepareCount;
        private int prepareResponderRouteCount;
        private RemoteWindowPreparationRequest? preparationRequest;
        private int readyObserved;
        private int revocationCallbackThreadId;
        private int waitForMediaAttachmentCount;

        public int AdmissionPublishCount => Volatile.Read(ref admissionPublishCount);

        public bool AdmissionPublished => Volatile.Read(ref admissionPublished) != 0;

        public bool AttachmentObserved => Volatile.Read(ref attachmentObserved) != 0;

        public TaskCompletionSource ConnectionRevoked { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposalCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string AuthenticatedPeerFingerprint =>
            inner.AuthenticatedPeerFingerprint;

        public bool IsCurrent => inner.IsCurrent;

        public AuthenticatedRemoteWindowConnectionPreparationReservationResult
            TryReservePreparation(
                IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
                    sink)
        {
            if (beforeConnectionPreparationRelease is null)
            {
                return inner.TryReservePreparation(sink);
            }

            var observingSink = new ReleaseObservingConnectionPreparationSink(
                sink,
                beforeConnectionPreparationRelease);
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                inner.TryReservePreparation(observingSink);
            return result.Registration is null
                ? result
                : new(result.Status, observingSink.RequireRegistration());
        }

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public int DisposeFailureCount => Volatile.Read(ref disposeFailureCount);

        public int FailCloseCount => Volatile.Read(ref failCloseCount);

        public int FailCloseFailureCount =>
            Volatile.Read(ref failCloseFailureCount);

        public DeviceId LocalDeviceId => inner.LocalDeviceId;

        public int MediaSendCount => Volatile.Read(ref mediaSendCount);

        public int MediaSentBeforeAdmissionCount =>
            Volatile.Read(ref mediaSentBeforeAdmissionCount);

        public DeviceId PeerDeviceId => inner.PeerDeviceId;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public int PrepareResponderRouteCount =>
            Volatile.Read(ref prepareResponderRouteCount);

        public ProtocolVersion ProtocolVersion => inner.ProtocolVersion;

        public bool ReadyObserved => Volatile.Read(ref readyObserved) != 0;

        public int RevocationCallbackThreadId =>
            Volatile.Read(ref revocationCallbackThreadId);

        public int WaitForMediaAttachmentCount =>
            Volatile.Read(ref waitForMediaAttachmentCount);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCount);
            DisposeEntered.TrySetResult();
            try
            {
                if (disposeRelease is not null)
                {
                    await disposeRelease.ConfigureAwait(false);
                }

                await inner.DisposeAsync().ConfigureAwait(false);
                Exception? failure = Interlocked.Exchange(ref disposeFailure, null);
                if (failure is not null)
                {
                    Interlocked.Increment(ref disposeFailureCount);
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
            finally
            {
                DisposalCompleted.TrySetResult();
            }
        }

        public async ValueTask FailCloseAsync()
        {
            Interlocked.Increment(ref failCloseCount);
            await inner.FailCloseAsync().ConfigureAwait(false);
            Exception? failure = Interlocked.Exchange(ref failCloseFailure, null);
            if (failure is not null)
            {
                Interlocked.Increment(ref failCloseFailureCount);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            IRemoteWindowHostPreparationAdmission admission,
            TimeSpan lifetime)
        {
            Interlocked.Increment(ref prepareResponderRouteCount);
            inner.PrepareResponderRoute(
                sessionId,
                activityId,
                admission,
                lifetime);
        }

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            Assert.Equal(0, capture.StartCount);
            Assert.Equal(0, renderer.RenderCount);
            RemoteWindowPreparationDeliveryResult result = await inner.PrepareAsync(
                request,
                admission,
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

        public IDisposable RegisterRevocationCallback(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return inner.RegisterRevocationCallback(() =>
            {
                Volatile.Write(
                    ref revocationCallbackThreadId,
                    Environment.CurrentManagedThreadId);
                callback();
                afterRevocationCallback?.Invoke();
                ConnectionRevoked.TrySetResult();
            });
        }

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
            if (afterAdmissionPublication is not null)
            {
                await afterAdmissionPublication();
            }

            if (injectedAdmissionPublicationFailure is not null)
            {
                ExceptionDispatchInfo.Capture(injectedAdmissionPublicationFailure)
                    .Throw();
            }
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

        private sealed class ReleaseObservingConnectionPreparationSink(
            IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink inner,
            Action beforeRelease) :
            IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
        {
            private ReleaseObservingConnectionPreparationRegistration?
                registration;

            public void
                OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration
                        owned)
            {
                var observing =
                    new ReleaseObservingConnectionPreparationRegistration(
                        owned,
                        beforeRelease);
                inner.OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(
                    observing);
                registration = observing;
            }

            public void
                InvalidateAuthenticatedRemoteWindowConnectionPreparationNow() =>
                inner.InvalidateAuthenticatedRemoteWindowConnectionPreparationNow();

            public ReleaseObservingConnectionPreparationRegistration
                RequireRegistration() => registration
                ?? throw new InvalidOperationException(
                    "The exact Connection Preparation registration was not synchronously owned.");
        }

        private sealed class ReleaseObservingConnectionPreparationRegistration(
            IAuthenticatedRemoteWindowConnectionPreparationRegistration inner,
            Action beforeRelease) :
            IAuthenticatedRemoteWindowConnectionPreparationRegistration
        {
            private int disposed;

            public bool IsCurrent => inner.IsCurrent;

            public long RegistrationId => inner.RegistrationId;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                bool wasCurrent = inner.IsCurrent;
                try
                {
                    if (wasCurrent)
                    {
                        beforeRelease();
                    }
                }
                finally
                {
                    inner.Dispose();
                }
            }
        }
    }

    private sealed class BlockingHostAuthorizationSource(
        IDesktopRemoteWindowHostAuthorizationSource inner) :
        IDesktopRemoteWindowHostAuthorizationSource
    {
        private readonly IDesktopRemoteWindowHostAuthorizationSource inner = inner
            ?? throw new ArgumentNullException(nameof(inner));

        public TaskCompletionSource<
            DesktopRemoteWindowHostAuthorizationReservationResult>
            ReservationAcquired
        { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            inner.GetCurrentGrant(peerDeviceId);

        public async ValueTask<
            DesktopRemoteWindowHostAuthorizationReservationResult>
            TryReservePreparationAsync(
                DeviceId peerDeviceId,
                string authenticatedPeerFingerprint,
                MirrorParticipantRole role,
                IDesktopRemoteWindowHostAuthorizationInvalidationSink
                    invalidationSink,
                CancellationToken cancellationToken)
        {
            DesktopRemoteWindowHostAuthorizationReservationResult? result = null;
            try
            {
                result = await inner.TryReservePreparationAsync(
                            peerDeviceId,
                            authenticatedPeerFingerprint,
                            role,
                            invalidationSink,
                            cancellationToken)
                        .ConfigureAwait(false);
                ReservationAcquired.TrySetResult(result);
                await Release.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                ReservationAcquired.TrySetException(exception);
                if (result?.Registration is { } registration)
                {
                    Exception? cleanupFailure = null;
                    try
                    {
                        await registration.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailure = failure;
                    }

                    if (exception is OutOfMemoryException)
                    {
                        ExceptionDispatchInfo.Capture(exception).Throw();
                    }

                    if (cleanupFailure is OutOfMemoryException)
                    {
                        ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                    }

                    if (cleanupFailure is not null)
                    {
                        throw new AggregateException(
                            "Authorization reservation barrier and cleanup failed.",
                            exception,
                            cleanupFailure);
                    }
                }

                ExceptionDispatchInfo.Capture(exception).Throw();
                throw;
            }
        }
    }

    private sealed class SourceInvalidationObservingHostConnection(
        AuthenticatedDesktopRemoteWindowHostConnection inner,
        Action? afterRouteSelection = null,
        bool blockPrepareForward = true) :
        IDesktopRemoteWindowHostConnection
    {
        private int admissionPublishCount;
        private int disposeCount;
        private int failCloseCount;
        private int mediaSendCount;
        private int prepareCount;
        private int prepareResponderRouteCount;
        private int prepareSendAdmissionCount;
        private int waitForMediaAttachmentCount;

        public int AdmissionPublishCount => Volatile.Read(ref admissionPublishCount);

        public IAuthenticatedRemoteWindowConnectionPreparationRegistration?
            ConnectionPreparationRegistration
        { get; private set; }

        public TaskCompletionSource ConnectionRevoked { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<RemoteWindowHostPreparationReservation>
            BeforePrepareForward
        { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public int FailCloseCount => Volatile.Read(ref failCloseCount);

        public string AuthenticatedPeerFingerprint =>
            inner.AuthenticatedPeerFingerprint;

        public bool IsCurrent => inner.IsCurrent;

        public bool RevocationCallbackRegistered { get; private set; }

        public AuthenticatedRemoteWindowConnectionPreparationReservationResult
            TryReservePreparation(
                IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
                    sink)
        {
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                inner.TryReservePreparation(sink);
            ConnectionPreparationRegistration = result.Registration;
            return result;
        }

        public DeviceId LocalDeviceId => inner.LocalDeviceId;

        public int MediaSendCount => Volatile.Read(ref mediaSendCount);

        public DeviceId PeerDeviceId => inner.PeerDeviceId;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public int PrepareResponderRouteCount =>
            Volatile.Read(ref prepareResponderRouteCount);

        public int PrepareSendAdmissionCount =>
            Volatile.Read(ref prepareSendAdmissionCount);

        public RemoteWindowControlDeliveryStatus? PreparationStatus { get; private set; }

        public ProtocolVersion ProtocolVersion => inner.ProtocolVersion;

        public TaskCompletionSource ReleasePrepareForward { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int WaitForMediaAttachmentCount =>
            Volatile.Read(ref waitForMediaAttachmentCount);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCount);
            await inner.DisposeAsync().ConfigureAwait(false);
        }

        public async ValueTask FailCloseAsync()
        {
            Interlocked.Increment(ref failCloseCount);
            await inner.FailCloseAsync().ConfigureAwait(false);
        }

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            IRemoteWindowHostPreparationAdmission admission,
            TimeSpan lifetime)
        {
            Interlocked.Increment(ref prepareResponderRouteCount);
            inner.PrepareResponderRoute(
                sessionId,
                activityId,
                admission,
                lifetime);
            afterRouteSelection?.Invoke();
        }

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            RemoteWindowHostPreparationReservation reservation = Assert.IsType<
                RemoteWindowHostPreparationReservation>(admission);
            BeforePrepareForward.TrySetResult(reservation);
            if (blockPrepareForward)
            {
                await ReleasePrepareForward.Task.WaitAsync(cancellationToken);
            }

            var observedAdmission = new CountingPrepareSendAdmission(
                admission,
                () => Interlocked.Increment(ref prepareSendAdmissionCount));
            RemoteWindowPreparationDeliveryResult result = await inner.PrepareAsync(
                    request,
                    observedAdmission,
                    cancellationToken)
                .ConfigureAwait(false);
            PreparationStatus = result.Status;
            return result;
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref admissionPublishCount);
            return inner.PublishAdmissionStateAsync(state, cancellationToken);
        }

        public IDisposable RegisterRevocationCallback(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            IDisposable registration = inner.RegisterRevocationCallback(() =>
            {
                callback();
                ConnectionRevoked.TrySetResult();
            });
            RevocationCallbackRegistered = true;
            return registration;
        }

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref mediaSendCount);
            return inner.SendAsync(frame, cancellationToken);
        }

        public async ValueTask WaitForMediaAttachmentAsync(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref waitForMediaAttachmentCount);
            await inner.WaitForMediaAttachmentAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class CountingPrepareSendAdmission(
        IRemoteWindowHostPreparationAdmission inner,
        Action admitted) : IRemoteWindowHostPreparationAdmission
    {
        public bool CompleteRouteSelection() => inner.CompleteRouteSelection();

        public bool TryAdmitPrepareSend(
            RemoteWindowPreparationRequest request,
            DateTimeOffset now)
        {
            admitted();
            return inner.TryAdmitPrepareSend(request, now);
        }

        public bool TryAdmitRouteSelection(DateTimeOffset now) =>
            inner.TryAdmitRouteSelection(now);

        public bool TryFailRouteSelection() => inner.TryFailRouteSelection();
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

        public string AuthenticatedPeerFingerprint =>
            inner.AuthenticatedPeerFingerprint;

        public bool IsCurrent => inner.IsCurrent;

        public AuthenticatedRemoteWindowConnectionPreparationReservationResult
            TryReservePreparation(
                IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
                    sink) => inner.TryReservePreparation(sink);

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
            IRemoteWindowHostPreparationAdmission admission,
            TimeSpan lifetime)
        {
            Interlocked.Increment(ref prepareResponderRouteCount);
            inner.PrepareResponderRoute(
                sessionId,
                activityId,
                admission,
                lifetime);
        }

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            RemoteWindowPreparationDeliveryResult result = await inner.PrepareAsync(
                request,
                admission,
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

    private sealed class RecordingCaptureBoundary(
        Func<RecordingCaptureBoundary, ValueTask>?
            afterPreAdmissionFrameDisposed = null,
        LocalBoundaryResult? startResult = null) :
        INativeRemoteWindowCaptureBoundary
    {
        private Func<RecordingCaptureBoundary, ValueTask>?
            afterPreAdmissionFrameDisposed = afterPreAdmissionFrameDisposed;
        private readonly object gate = new();
        private readonly LocalBoundaryResult startResult = startResult
            ?? LocalBoundaryResult.Confirmed("native_capture_started");
        private Exception? emergencyStopFailure;
        private INativeRemoteWindowFrameSink? sink;
        private NativeRemoteWindowSourceUse? sourceUse;
        private int emergencyStopCount;
        private int emergencyStopFailureCount;
        private TrackingMemoryOwner? preAdmissionFrameOwner;
        private int preAdmissionFrameDisposed;
        private int startCount;
        private int stopCount;

        public int EmergencyStopCount => Volatile.Read(ref emergencyStopCount);

        public Exception? EmergencyStopFailure
        {
            get => Volatile.Read(ref emergencyStopFailure);
            init => emergencyStopFailure = value;
        }

        public int EmergencyStopFailureCount =>
            Volatile.Read(ref emergencyStopFailureCount);

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

        public int PreAdmissionFrameDisposeCount =>
            Volatile.Read(ref preAdmissionFrameOwner)?.DisposeCount ?? 0;

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
            Volatile.Write(ref preAdmissionFrameOwner, owner);
            await owner.Disposed.Task.WaitAsync(cancellationToken);
            Volatile.Write(ref preAdmissionFrameDisposed, 1);
            Func<RecordingCaptureBoundary, ValueTask>? hook = Interlocked.Exchange(
                ref afterPreAdmissionFrameDisposed,
                null);
            if (hook is not null)
            {
                await hook(this).ConfigureAwait(false);
            }

            return startResult;
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
            Exception? failure = Interlocked.Exchange(
                ref emergencyStopFailure,
                null);
            if (failure is not null)
            {
                Interlocked.Increment(ref emergencyStopFailureCount);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

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
        private Exception? emergencyStopFailure;
        private int emergencyStopApplied;
        private int emergencyStopAppliedBeforeFailureCount;
        private int emergencyStopCount;
        private int emergencyStopFailureCount;
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

        public Exception? EmergencyStopFailure
        {
            get => Volatile.Read(ref emergencyStopFailure);
            init => emergencyStopFailure = value;
        }

        public int EmergencyStopFailureCount =>
            Volatile.Read(ref emergencyStopFailureCount);

        public int EmergencyStopAppliedBeforeFailureCount =>
            Volatile.Read(ref emergencyStopAppliedBeforeFailureCount);

        public bool IsEmergencyStopped =>
            Volatile.Read(ref emergencyStopApplied) != 0;

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
            Volatile.Write(ref emergencyStopApplied, 1);
            Exception? failure = Interlocked.Exchange(
                ref emergencyStopFailure,
                null);
            if (failure is not null)
            {
                if (Volatile.Read(ref emergencyStopApplied) != 0)
                {
                    Interlocked.Increment(
                        ref emergencyStopAppliedBeforeFailureCount);
                }

                Interlocked.Increment(ref emergencyStopFailureCount);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

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

    private sealed class CountingAllowReceivePolicy :
        IDesktopRemoteWindowReceivePolicy
    {
        private int evaluationCount;

        public int EvaluationCount => Volatile.Read(ref evaluationCount);

        public string? GetRejectionReason(RemoteWindowPreparationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Interlocked.Increment(ref evaluationCount);
            return null;
        }
    }

    private sealed class RecordingRendererFactory(
        IDesktopRemoteWindowParticipantRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private int prepareCount;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public RemoteWindowPreparationRequest? Request { get; private set; }

        public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref prepareCount);
            Request = request;
            return ValueTask.FromResult<
                IDesktopRemoteWindowParticipantRenderer?>(renderer);
        }
    }

    private sealed class DisconnectObservingPreparationPeer(
        IRemoteWindowPreparationPeer inner) : IRemoteWindowPreparationPeer
    {
        private readonly IRemoteWindowPreparationPeer inner = inner
            ?? throw new ArgumentNullException(nameof(inner));

        public TaskCompletionSource<RemoteWindowPreparationResponse>
            PreparationCompleted
        { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PeerDisconnectCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PeerDisconnectEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId ParticipantDeviceId => inner.ParticipantDeviceId;

        public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                RemoteWindowPreparationResponse response = await inner.PrepareAsync(
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
                PreparationCompleted.TrySetResult(response);
                return response;
            }
            catch (Exception exception)
            {
                PreparationCompleted.TrySetException(exception);
                throw;
            }
        }

        public ValueTask CompletePreparationResponseAsync(
            RemoteWindowPreparationResponse response,
            bool responseCommitted) => inner.CompletePreparationResponseAsync(
            response,
            responseCommitted);

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => inner.CompleteAdmissionAsync(
            request,
            state,
            cancellationToken);

        public async ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken)
        {
            PeerDisconnectEntered.TrySetResult();
            try
            {
                await inner.PeerDisconnectedAsync(
                        hostDeviceId,
                        cancellationToken)
                    .ConfigureAwait(false);
                PeerDisconnectCompleted.TrySetResult();
            }
            catch (Exception exception)
            {
                PeerDisconnectCompleted.TrySetException(exception);
                throw;
            }
        }
    }

    private sealed class NonCooperativeBlockingRendererFactory(
        IDesktopRemoteWindowParticipantRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int prepareCount;

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public RemoteWindowPreparationRequest Request { get; private set; } = null!;

        public TaskCompletionSource Returned { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Assert.Equal(1, Interlocked.Increment(ref prepareCount));
            Request = request;
            using CancellationTokenRegistration registration =
                cancellationToken.UnsafeRegister(
                    static state => ((TaskCompletionSource)state!).TrySetResult(),
                    CancellationObserved);
            Entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            Returned.TrySetResult();
            return renderer;
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class SequencedRendererFactory(
        IDesktopRemoteWindowParticipantRenderer renderer,
        SequencedMediaAttachmentHandler mediaHandler) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private int prepareCount;

        public TaskCompletionSource FirstFailureInjected { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowPreparationRequest? FirstRequest { get; private set; }

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public TaskCompletionSource SecondRendererReturned { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowPreparationRequest? SecondRequest { get; private set; }

        public async ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int ordinal = Interlocked.Increment(ref prepareCount);
            switch (ordinal)
            {
                case 1:
                    FirstRequest = request;
                    await mediaHandler.First.Entered.Task.WaitAsync(
                        cancellationToken);
                    FirstFailureInjected.TrySetResult();
                    throw new InvalidOperationException(
                        "test managed first-generation renderer preparation failed");
                case 2:
                    SecondRequest = request;
                    await mediaHandler.Second.Entered.Task.WaitAsync(
                        cancellationToken);
                    SecondRendererReturned.TrySetResult();
                    return renderer;
                default:
                    throw new InvalidOperationException(
                        "The managed ABA tracer expected exactly two renderer preparations.");
            }
        }
    }

    private sealed class AbaObservingHostConnection(
        AuthenticatedDesktopRemoteWindowHostConnection inner) :
        IDesktopRemoteWindowHostConnection
    {
        private int admissionPublishCount;
        private int attachmentObserved;
        private int disposeCount;
        private int failCloseCount;
        private int mediaSendCount;
        private int prepareCount;
        private int prepareResponderRouteCount;
        private int waitForMediaAttachmentCount;

        public int AdmissionPublishCount => Volatile.Read(ref admissionPublishCount);

        public bool AttachmentObserved => Volatile.Read(ref attachmentObserved) != 0;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public int FailCloseCount => Volatile.Read(ref failCloseCount);

        public string AuthenticatedPeerFingerprint =>
            inner.AuthenticatedPeerFingerprint;

        public bool IsCurrent => inner.IsCurrent;

        public AuthenticatedRemoteWindowConnectionPreparationReservationResult
            TryReservePreparation(
                IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
                    sink) => inner.TryReservePreparation(sink);

        public DeviceId LocalDeviceId => inner.LocalDeviceId;

        public int MediaSendCount => Volatile.Read(ref mediaSendCount);

        public DeviceId PeerDeviceId => inner.PeerDeviceId;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public int PrepareResponderRouteCount =>
            Volatile.Read(ref prepareResponderRouteCount);

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
            IRemoteWindowHostPreparationAdmission admission,
            TimeSpan lifetime)
        {
            Interlocked.Increment(ref prepareResponderRouteCount);
            inner.PrepareResponderRoute(
                sessionId,
                activityId,
                admission,
                lifetime);
        }

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            return inner.PrepareAsync(request, admission, cancellationToken);
        }

        public async ValueTask WaitForMediaAttachmentAsync(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref waitForMediaAttachmentCount);
            await inner.WaitForMediaAttachmentAsync(cancellationToken);
            Volatile.Write(ref attachmentObserved, 1);
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

    private sealed class SequencedMediaAttachmentHandler :
        IRemoteWindowMediaAttachmentHandler
    {
        private readonly IRemoteWindowMediaAttachmentHandler inner;
        private int callCount;

        public SequencedMediaAttachmentHandler(
            IRemoteWindowMediaAttachmentHandler inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            First = new SequencedMediaAttachmentStep(new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously));
            Second = new SequencedMediaAttachmentStep(new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously));
        }

        public int CallCount => Volatile.Read(ref callCount);

        public SequencedMediaAttachmentStep First { get; }

        public SequencedMediaAttachmentStep Second { get; }

        public async ValueTask HandleAsync(
            RemoteWindowMediaAttachment attachment,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            int ordinal = Interlocked.Increment(ref callCount);
            SequencedMediaAttachmentStep step = ordinal switch
            {
                1 => First,
                2 => Second,
                _ => throw new InvalidOperationException(
                    "The managed ABA tracer expected exactly two media attachments."),
            };
            step.Binding = attachment.Binding;
            step.Entered.TrySetResult();
            await step.ReleaseTask.WaitAsync(cancellationToken);
            Interlocked.Increment(ref step.ForwardCountStorage);
            Exception? failure = null;
            try
            {
                await inner.HandleAsync(attachment, cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                step.Completion.TrySetResult(failure);
                step.Exited.TrySetResult();
            }
        }

        public sealed class SequencedMediaAttachmentStep(
            TaskCompletionSource release)
        {
            internal int ForwardCountStorage;

            public RemoteWindowMediaRouteBinding? Binding { get; internal set; }

            public TaskCompletionSource<Exception?> Completion { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Entered { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Exited { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public int ForwardCount => Volatile.Read(ref ForwardCountStorage);

            public bool IsReleased => release.Task.IsCompleted;

            internal Task ReleaseTask => release.Task;

            public void Release() => release.TrySetResult();
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
        INativeRemoteWindowPermissionBoundary,
        INativeRemoteWindowPermissionPreparationBoundary
    {
        private readonly object gate = new();
        private readonly List<RecordingPermissionPreparationRegistration>
            preparations = [];
        private Action<NativeRemoteWindowPermissionSnapshot>? changed;
        private NativeRemoteWindowPermissionSnapshot current = snapshot;
        private int observerCount;
        private int preparationReservationCount;

        public event Action<NativeRemoteWindowPermissionSnapshot>? Changed
        {
            add
            {
                lock (gate)
                {
                    changed += value;
                    Interlocked.Increment(ref observerCount);
                }
            }
            remove
            {
                lock (gate)
                {
                    changed -= value;
                    Interlocked.Decrement(ref observerCount);
                }
            }
        }

        public int ObserverCount => Volatile.Read(ref observerCount);

        public int PreparationReservationCount =>
            Volatile.Read(ref preparationReservationCount);

        public int CurrentPreparationReservationCount
        {
            get
            {
                lock (gate)
                {
                    return preparations.Count;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (gate)
            {
                foreach (RecordingPermissionPreparationRegistration preparation in
                    preparations.ToArray())
                {
                    preparation.InvalidateUnderGate();
                }

                preparations.Clear();
                changed = null;
                Volatile.Write(ref observerCount, 0);
            }

            return ValueTask.CompletedTask;
        }

        public NativeRemoteWindowPermissionSnapshot GetSnapshot()
        {
            lock (gate)
            {
                return current;
            }
        }

        public NativeRemoteWindowPermissionPreparationReservationResult
            TryReservePreparation(
                NativeRemoteWindowPermissionSnapshot expectedSnapshot,
                MirrorParticipantRole frozenRole,
                INativeRemoteWindowPermissionPreparationInvalidationSink
                    invalidationSink)
        {
            ArgumentNullException.ThrowIfNull(expectedSnapshot);
            ArgumentNullException.ThrowIfNull(invalidationSink);
            if (!Enum.IsDefined(frozenRole))
            {
                throw new ArgumentOutOfRangeException(nameof(frozenRole));
            }

            lock (gate)
            {
                if (expectedSnapshot != current)
                {
                    return new(
                        NativeRemoteWindowPermissionPreparationReservationStatus
                            .SnapshotChanged,
                        Registration: null);
                }

                NativeRemoteWindowPermissionState[] required =
                    frozenRole == MirrorParticipantRole.DriverEligible
                        ? [current.Capture, current.Input]
                        : [current.Capture];
                if (required.Any(static state => state is
                    NativeRemoteWindowPermissionState.Unsupported or
                    NativeRemoteWindowPermissionState.Unavailable))
                {
                    return new(
                        NativeRemoteWindowPermissionPreparationReservationStatus
                            .BoundaryUnavailable,
                        Registration: null);
                }

                if (required.Any(static state =>
                    state != NativeRemoteWindowPermissionState.Granted))
                {
                    return new(
                        NativeRemoteWindowPermissionPreparationReservationStatus
                            .PermissionDenied,
                        Registration: null);
                }

                var registration =
                    new RecordingPermissionPreparationRegistration(
                        this,
                        invalidationSink);
                preparations.Add(registration);
                invalidationSink
                    .OwnNativeRemoteWindowPermissionPreparationRegistration(
                        registration);
                Interlocked.Increment(ref preparationReservationCount);
                return new(
                    NativeRemoteWindowPermissionPreparationReservationStatus
                        .Reserved,
                    registration);
            }
        }

        public void Publish(NativeRemoteWindowPermissionSnapshot updated)
        {
            Action<NativeRemoteWindowPermissionSnapshot>? observers;
            lock (gate)
            {
                if (updated == current)
                {
                    return;
                }

                current = updated;
                foreach (RecordingPermissionPreparationRegistration preparation in
                    preparations.ToArray())
                {
                    preparation.InvalidateUnderGate();
                }

                preparations.Clear();
                observers = changed;
            }

            observers?.Invoke(updated);
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GetSnapshot());
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GetSnapshot());
        }

        private void Release(
            RecordingPermissionPreparationRegistration registration)
        {
            lock (gate)
            {
                _ = preparations.Remove(registration);
                registration.ReleaseUnderGate();
            }
        }

        private sealed class RecordingPermissionPreparationRegistration(
            RecordingPermissionBoundary owner,
            INativeRemoteWindowPermissionPreparationInvalidationSink sink) :
            INativeRemoteWindowPermissionPreparationRegistration
        {
            private INativeRemoteWindowPermissionPreparationInvalidationSink?
                sink = sink;

            public bool IsCurrent => Volatile.Read(ref sink) is not null;

            public void Dispose() => owner.Release(this);

            internal void InvalidateUnderGate()
            {
                INativeRemoteWindowPermissionPreparationInvalidationSink? target =
                    Interlocked.Exchange(ref sink, null);
                target?.InvalidateNativeRemoteWindowPermissionPreparationNow();
            }

            internal void ReleaseUnderGate() =>
                _ = Interlocked.Exchange(ref sink, null);
        }
    }

    private sealed class RecordingProtectionSource :
        INativeProtectionSource,
        INativeRemoteWindowProtectionPreparationBoundary
    {
        private readonly InMemoryNativeProtectionSource inner;
        private int disposed;
        private int preparationReservationCount;

        public RecordingProtectionSource(
            NativeRemoteWindowProtectionObservation observation)
        {
            ArgumentNullException.ThrowIfNull(observation);
            inner = new InMemoryNativeProtectionSource(
                observation.OwnerGeneration,
                observation.SessionGeneration,
                observation.SourceGeneration);
            for (long revision = 0;
                revision < observation.Revision;
                revision++)
            {
                if (!inner.TryPublish(observation.Protection))
                {
                    throw new InvalidOperationException(
                        "The recording protection source could not publish its initial observation.");
                }
            }
        }

        public event Action<NativeRemoteWindowProtectionObservation>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public int PreparationReservationCount =>
            Volatile.Read(ref preparationReservationCount);

        public void Dispose()
        {
            Volatile.Write(ref disposed, 1);
            inner.Dispose();
        }

        public bool Publish(ProtectionSnapshot protection) =>
            inner.TryPublish(protection);

        public bool TryGetLatest(
            out NativeRemoteWindowProtectionObservation? latest) =>
            inner.TryGetLatest(out latest);

        NativeRemoteWindowProtectionPreparationReservationResult
            INativeRemoteWindowProtectionPreparationBoundary
            .TryReservePreparation(
                NativeRemoteWindowProtectionObservation expectedObservation,
                DateTimeOffset now,
                INativeRemoteWindowProtectionPreparationInvalidationSink
                    invalidationSink)
        {
            NativeRemoteWindowProtectionPreparationReservationResult result =
                ((INativeRemoteWindowProtectionPreparationBoundary)inner)
                .TryReservePreparation(
                    expectedObservation,
                    now,
                    invalidationSink);
            if (result.Status ==
                NativeRemoteWindowProtectionPreparationReservationStatus.Reserved)
            {
                Interlocked.Increment(ref preparationReservationCount);
            }

            return result;
        }
    }

    private sealed class RecordingEmergencyStopRegistrar(
        Exception? registrationDisposeFailure = null) :
        ILocalEmergencyStopRegistrar
    {
        private RecordingEmergencyStopRegistration? current;
        private int readinessReservationCount;
        private int registrationCount;
        private RecordingEmergencyStopReadinessReservation? readiness;

        public bool HasCurrentRegistration => current?.IsCurrent == true;

        public bool HasCurrentReadiness => readiness?.IsCurrent == true;

        public int RegistrationDisposeCount => current?.DisposeCount ?? 0;

        public int ReadinessReservationCount =>
            Volatile.Read(ref readinessReservationCount);

        public int RegistrationCount => Volatile.Read(ref registrationCount);

        public LocalBoundaryResult CheckReadiness() =>
            LocalBoundaryResult.Confirmed("emergency_stop_ready");

        public LocalEmergencyStopReadinessReservationResult TryReserveReadiness(
            long ownerGeneration,
            long sessionGeneration,
            ILocalEmergencyStopReadinessInvalidationSink invalidationSink)
        {
            Interlocked.Increment(ref readinessReservationCount);
            if (current?.IsCurrent == true || readiness?.IsCurrent == true)
            {
                return LocalEmergencyStopReadinessReservationResult.Rejected(
                    "emergency_stop_registration_conflict");
            }

            readiness = new RecordingEmergencyStopReadinessReservation(
                this,
                ownerGeneration,
                sessionGeneration,
                invalidationSink);
            return LocalEmergencyStopReadinessReservationResult.Confirmed(
                readiness,
                "emergency_stop_readiness_reserved");
        }

        public void Dispose()
        {
            readiness?.Dispose();
            current?.Dispose();
        }

        public LocalEmergencyStopRegistrationResult TryRegister(
            long ownerGeneration,
            long sessionGeneration,
            Action<LocalEmergencyStopActivation> callback)
        {
            Interlocked.Increment(ref registrationCount);
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

        internal void Release(
            RecordingEmergencyStopReadinessReservation reservation)
        {
            if (ReferenceEquals(readiness, reservation))
            {
                readiness = null;
            }
        }

        internal LocalEmergencyStopRegistrationResult Promote(
            RecordingEmergencyStopReadinessReservation reservation,
            Action<LocalEmergencyStopActivation> callback)
        {
            Interlocked.Increment(ref registrationCount);
            if (!ReferenceEquals(readiness, reservation)
                || !reservation.IsCurrent)
            {
                return LocalEmergencyStopRegistrationResult.Rejected(
                    "emergency_stop_readiness_stale");
            }

            reservation.CommitPromotion();
            readiness = null;
            current = new RecordingEmergencyStopRegistration(
                reservation.OwnerGeneration,
                reservation.SessionGeneration,
                callback,
                registrationDisposeFailure);
            return LocalEmergencyStopRegistrationResult.Confirmed(
                current,
                "emergency_stop_registered");
        }
    }

    private sealed class RecordingEmergencyStopReadinessReservation(
        RecordingEmergencyStopRegistrar registrar,
        long ownerGeneration,
        long sessionGeneration,
        ILocalEmergencyStopReadinessInvalidationSink invalidationSink) :
        ILocalEmergencyStopReadinessReservation
    {
        private ILocalEmergencyStopReadinessInvalidationSink? invalidationSink =
            invalidationSink;

        public bool IsCurrent => Volatile.Read(ref invalidationSink) is not null;

        public long OwnerGeneration { get; } = ownerGeneration;

        public long SessionGeneration { get; } = sessionGeneration;

        public void CommitPromotion() =>
            _ = Interlocked.Exchange(ref invalidationSink, null);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref invalidationSink, null) is not null)
            {
                registrar.Release(this);
            }
        }

        public LocalEmergencyStopRegistrationResult TryPromote(
            Action<LocalEmergencyStopActivation> callback) =>
            registrar.Promote(this, callback);
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
