using System.Buffers;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowHostCoordinatorTests
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

    [Fact]
    public async Task StartKeepsFramesClosedUntilFinalAdmissionIsPublished()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var clock = new FixedClock(Now);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var authorization = new FixedAuthorizationSource(
            CapabilityGrant.Of(Capability.MirrorView));
        var capture = new RecordingCaptureBoundary(timeline);
        var input = new ConfirmingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var emergencyStops = new RecordingEmergencyStopRegistrar(timeline);
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId);
        connection.PrepareResponse = static request =>
            RemoteWindowPreparationDeliveryResult.Acknowledged(
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"));
        connection.Publishing = state =>
        {
            Assert.Equal(RemoteWindowControlOutcome.Applied, state.Outcome);
            Assert.Equal(MirrorParticipantRole.ViewOnly, state.EffectiveRole);
            Assert.True(permissions.SnapshotReadCount >= 3);
            Assert.True(authorization.ReadCount >= 4);
            Assert.True(protection.SnapshotReadCount >= 2);
            Assert.True(connection.CurrentReadCount >= 3);
            Assert.True(emergencyStops.CurrentRegistration?.IsCurrent);
            Assert.Equal(state.SessionId, controlPeer.SessionId);
            Assert.Equal(state.ActivityId, controlPeer.ActivityId);
            capture.EmitFrame(sequence: 2);
            Assert.Empty(connection.MediaFrames);
        };

        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            clock,
            permissions,
            authorization,
            capture,
            input,
            sessions,
            emergencyStops,
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        var request = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            connection,
            protection,
            MirrorParticipantRole.ViewOnly);

        RemoteWindowCommandResult result = await coordinator.StartAsync(request);

        Assert.Equal(RemoteWindowCommandStatus.Applied, result.Status);
        Assert.Empty(connection.MediaFrames);
        capture.EmitFrame(sequence: 3);
        await connection.WaitForMediaFrameCountAsync(1);
        RemoteWindowMediaFrameSnapshot sent = Assert.Single(
            connection.MediaFrames);
        Assert.Equal(RemoteWindowMediaKind.Video, sent.Kind);
        Assert.Equal<ulong>(1, sent.Sequence);
        Assert.Equal<ushort>(0, sent.ChunkIndex);
        Assert.Equal<ushort>(1, sent.ChunkCount);
        Assert.Equal(0xff, sent.Payload[0]);
        Assert.Equal(0xd8, sent.Payload[1]);
        Assert.True(emergencyStops.CurrentRegistration?.IsCurrent);
        Assert.True(permissions.SnapshotReadCount >= 2);
        AssertOrdered(
            timeline,
            "connection.route",
            "connection.prepare",
            "connection.wait_media",
            "protection.subscribe",
            "protection.read",
            "emergency_stop.register",
            "capture.start",
            "connection.publish",
            "connection.send_media");
        _ = await coordinator.StopAsync();
        Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
    }

    [Fact]
    public async Task ClosingFrameAdmissionDoesNotWaitForBlockedDestination()
    {
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var capture = new RecordingCaptureBoundary([]);
        var destination = new BlockingFrameDestination();
        using var admission = new DesktopRemoteWindowFrameAdmissionSink(destination);
        using var controller = new RemoteWindowSessionController(
            sourceLease,
            ownerGeneration: 1,
            new FixedClock(Now),
            new FixedAuthorizationSource(CapabilityGrant.None),
            capture,
            new ConfirmingInputBoundary(),
            admission,
            new RecordingSharingSessionBoundary(),
            TimeSpan.FromSeconds(10));
        Assert.True((await controller.StartAsync(SafeAt(Now))).Succeeded);
        Assert.True(admission.TryOpen());
        NativeRemoteWindowSourceUse sourceUse = capture.CurrentSource;
        Task delivering = Task.Run(() => admission.TakeOwnership(
            sourceUse,
            CreateFrame(sourceUse, sequence: 2)));
        await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var closeStarted = new ManualResetEventSlim();
        using var closeReturned = new ManualResetEventSlim();
        Task closing = Task.Run(() =>
        {
            closeStarted.Set();
            admission.CloseNow();
            closeReturned.Set();
        });
        Assert.True(closeStarted.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.True(closeReturned.Wait(TimeSpan.FromSeconds(5)));
            admission.TakeOwnership(
                sourceUse,
                CreateFrame(sourceUse, sequence: 3));
            Assert.Equal(1, destination.DeliveryCount);
        }
        finally
        {
            destination.Release();
        }

        await Task.WhenAll(delivering, closing).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, destination.DeliveryCount);
    }

    [Fact]
    public async Task MediaFailureFailsClosedOnceAndDrainsEveryHostOwner()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var capture = new RecordingCaptureBoundary(timeline);
        var input = new ConfirmingInputBoundary();
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")),
        };
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new FixedAuthorizationSource(
                CapabilityGrant.Of(Capability.MirrorView)),
            capture,
            input,
            new RecordingSharingSessionBoundary(),
            new RecordingEmergencyStopRegistrar(timeline),
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        var request = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            connection,
            protection,
            MirrorParticipantRole.ViewOnly);
        Assert.True((await coordinator.StartAsync(request)).Succeeded);
        RemoteWindowMediaSessionBudget budget = Assert.IsType<
            RemoteWindowMediaSessionBudget>(coordinator.ActiveMediaBudget);
        connection.SendFailure = new IOException("injected media failure");

        capture.EmitFrame(sequence: 9);

        await connection.WaitForDisposeAsync();
        Assert.Equal(1, connection.FailCloseCount);
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Equal(1, capture.EmergencyStopCount);
        Assert.Equal(1, input.EmergencyStopCount);
        Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
        Assert.Equal(1, connection.FailCloseCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
        Assert.Equal(0, permissions.ObserverCount);
        Assert.True(protection.IsDisposed);

        var replacementProtection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var replacementConnection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")),
        };
        var replacementRequest = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            replacementConnection,
            replacementProtection,
            MirrorParticipantRole.ViewOnly);

        Assert.True((await coordinator.StartAsync(replacementRequest)).Succeeded);
        capture.EmitFrame(sequence: 10);
        await replacementConnection.WaitForMediaFrameCountAsync(1);
        Assert.True((await coordinator.StopAsync()).FullyStopped);
        Assert.Single(replacementConnection.MediaFrames);
        Assert.Equal(1, replacementConnection.DisposeCount);
        Assert.True(replacementProtection.IsDisposed);
    }

    [Fact]
    public async Task CancelledStopStillDrainsTheDetachedHostGeneration()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var capture = new RecordingCaptureBoundary(timeline);
        var input = new BlockingInputBoundary();
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")),
        };
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new FixedAuthorizationSource(CapabilityGrant.Of(
                Capability.MirrorView,
                Capability.MirrorDrive)),
            capture,
            input,
            new RecordingSharingSessionBoundary(),
            new RecordingEmergencyStopRegistrar(timeline),
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        var request = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            connection,
            protection,
            MirrorParticipantRole.DriverEligible);
        Assert.True((await coordinator.StartAsync(request)).Succeeded);
        RemoteWindowSharingSnapshot before = Assert.IsType<
            RemoteWindowSharingSnapshot>(coordinator.Snapshot);
        RemoteWindowParticipantState driver = await controlPeer.RequestDriverAsync(
            RemoteWindowDriverRequest.Create(
                CorrelationId.From(Guid.NewGuid()),
                controlPeer.SessionId,
                before.ActivityId,
                HostDeviceId,
                ParticipantDeviceId,
                Assert.IsType<long>(before.DriverLeaseEpoch),
                TimeSpan.FromSeconds(5),
                Now.AddSeconds(2)),
            CancellationToken.None);
        Task<RemoteWindowParticipantState> injecting = controlPeer.SendInputAsync(
                RemoteWindowInputRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    driver.SessionId,
                    driver.ActivityId,
                    HostDeviceId,
                    ParticipantDeviceId,
                    Assert.IsType<long>(driver.DriverLeaseEpoch),
                    RemoteInputBatch.Create(
                        [RemoteInputEvent.PointerMove(0.25, 0.75)]),
                    Now.AddSeconds(2)),
                CancellationToken.None)
            .AsTask();
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();

        Task stopping = coordinator.StopAsync(cancellation.Token).AsTask();
        await WaitForControlRouteClosedAsync(controlPeer);
        cancellation.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        Assert.False(stopping.IsCompleted);

        input.Release.TrySetResult();
        Assert.Equal(
            RemoteWindowControlOutcome.Applied,
            (await injecting.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await stopping.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Null(coordinator.Snapshot);
        Assert.Equal(1, capture.StopCount);
        Assert.Equal(1, input.StopCount);
        Assert.Equal(1, connection.FailCloseCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(0, permissions.ObserverCount);
        Assert.True(protection.IsDisposed);
    }

    [Fact]
    public async Task DisposeReportsUnconfirmedStopAndStillDrainsEveryHostOwner()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var capture = new RecordingCaptureBoundary(timeline)
        {
            StopResult = LocalBoundaryResult.Failed(
                "native_capture_stop_unconfirmed"),
        };
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")),
        };
        var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new FixedAuthorizationSource(
                CapabilityGrant.Of(Capability.MirrorView)),
            capture,
            new ConfirmingInputBoundary(),
            new RecordingSharingSessionBoundary(),
            new RecordingEmergencyStopRegistrar(timeline),
            controlPeer,
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        Assert.True((await coordinator.StartAsync(
            new DesktopRemoteWindowHostStartRequest(
                sourceLease,
                ownerGeneration: 1,
                connection,
                protection,
                MirrorParticipantRole.ViewOnly))).Succeeded);

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.DisposeAsync());
        InvalidOperationException repeated = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.DisposeAsync());

        Assert.Same(failure, repeated);
        Assert.Contains("cleanup stop", failure.Message);
        Assert.Contains("capture=native_capture_stop_unconfirmed", failure.Message);
        Assert.Null(coordinator.Snapshot);
        Assert.True(capture.StopCount >= 1);
        Assert.Equal(1, connection.FailCloseCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(0, permissions.ObserverCount);
        Assert.True(protection.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
    }

    [Fact]
    public async Task ConnectionRevocationDetachesAndDrainsTheActiveGeneration()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        RemoteWindowMediaSessionBudget budget = Assert.IsType<
            RemoteWindowMediaSessionBudget>(coordinator.ActiveMediaBudget);

        host.Connection.Revoke();

        await host.Connection.WaitForDisposeAsync();
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => host.ControlPeer.SessionId);
    }

    [Fact]
    public async Task ConcurrentAndLaterDisposeCallsShareCleanupFailure()
    {
        using var host = new ReadyHostHarness();
        var injected = new IOException("injected connection dispose failure");
        host.Connection.BlockDisposal = true;
        host.Connection.DisposeFailure = injected;
        Assert.True((await host.StartAsync()).Succeeded);

        Task first = host.Coordinator.DisposeAsync().AsTask();
        await host.Connection.WaitForDisposeEnteredAsync();
        Task concurrent = host.Coordinator.DisposeAsync().AsTask();

        Assert.Same(first, concurrent);
        Assert.False(first.IsCompleted);
        Assert.False(concurrent.IsCompleted);
        host.Connection.ReleaseDisposal();
        IOException firstFailure = await Assert.ThrowsAsync<IOException>(
            async () => await first);
        IOException concurrentFailure = await Assert.ThrowsAsync<IOException>(
            async () => await concurrent);
        Task later = host.Coordinator.DisposeAsync().AsTask();
        IOException laterFailure = await Assert.ThrowsAsync<IOException>(
            async () => await later);

        Assert.Same(first, later);
        Assert.Same(injected, firstFailure);
        Assert.Same(injected, concurrentFailure);
        Assert.Same(injected, laterFailure);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
    }

    [Fact]
    public async Task RouteSideEffectFailureFailsClosedWithoutStartingCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("route selected then failed");
        host.Connection.RouteSelectionFailure = injected;

        IOException failure = await Assert.ThrowsAsync<IOException>(
            async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(coordinator.TerminalFailure);
    }

    [Fact]
    public async Task RevocationCleanupFailureBlocksRestartAndReachesDispose()
    {
        using var host = new ReadyHostHarness();
        var injected = new IOException("injected terminal cleanup failure");
        host.Connection.DisposeFailure = injected;
        Assert.True((await host.StartAsync()).Succeeded);

        host.Connection.Revoke();

        await WaitForTerminalFailureAsync(host.Coordinator);
        Assert.Null(host.Coordinator.Snapshot);
        Assert.Same(injected, host.Coordinator.TerminalFailure);
        var replacementConnection = new RecordingHostConnection(
            host.Timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = ReadyPreparation,
        };
        RecordingProtectionSource replacementProtection =
            host.CreateProtection();
        InvalidOperationException restartFailure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.Coordinator.StartAsync(
                host.CreateRequest(
                    replacementConnection,
                    replacementProtection)));
        IOException disposalFailure = await Assert.ThrowsAsync<IOException>(
            async () => await host.Coordinator.DisposeAsync());

        Assert.Contains("host_cleanup_unconfirmed", restartFailure.Message);
        Assert.Equal(1, replacementConnection.DisposeCount);
        Assert.True(replacementProtection.IsDisposed);
        Assert.Same(injected, disposalFailure);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
    }

    [Fact]
    public async Task UnconfirmedExplicitStopBlocksRestart()
    {
        using var host = new ReadyHostHarness();
        host.Capture.StopResult = LocalBoundaryResult.Failed(
            "native_capture_stop_unconfirmed");
        Assert.True((await host.StartAsync()).Succeeded);

        RemoteWindowStopResult stopped = await host.Coordinator.StopAsync();

        Assert.False(stopped.FullyStopped);
        Assert.NotNull(host.Coordinator.TerminalFailure);
        var replacementConnection = new RecordingHostConnection(
            host.Timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = ReadyPreparation,
        };
        RecordingProtectionSource replacementProtection =
            host.CreateProtection();
        InvalidOperationException restartFailure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.Coordinator.StartAsync(
                host.CreateRequest(
                    replacementConnection,
                    replacementProtection)));

        Assert.Contains("host_cleanup_unconfirmed", restartFailure.Message);
        Assert.Equal(1, replacementConnection.DisposeCount);
        Assert.True(replacementProtection.IsDisposed);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.Coordinator.DisposeAsync());
    }

    [Fact]
    public async Task StartRejectsPrePreparationProtocolBeforeSelectingRoute()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var capture = new RecordingCaptureBoundary(timeline);
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            ProtocolVersion = ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")),
        };
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new FixedAuthorizationSource(
                CapabilityGrant.Of(Capability.MirrorView)),
            capture,
            new ConfirmingInputBoundary(),
            new RecordingSharingSessionBoundary(),
            new RecordingEmergencyStopRegistrar(timeline),
            new DesktopRemoteWindowHostControlPeer(HostDeviceId),
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        var request = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            connection,
            protection,
            MirrorParticipantRole.ViewOnly);

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(request));

        Assert.Contains("remote_window_protocol_unsupported", failure.Message);
        Assert.DoesNotContain("connection.route", timeline);
        Assert.DoesNotContain("connection.prepare", timeline);
        Assert.Equal(0, capture.StartCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.True(protection.IsDisposed);
    }

    [Fact]
    public async Task ReadyRejectionUnwindsRouteWithoutStartingCaptureOrAdmission()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var capture = new RecordingCaptureBoundary(timeline);
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var emergencyStops = new RecordingEmergencyStopRegistrar(timeline);
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Rejected,
                        "participant_busy")),
        };
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            new FixedClock(Now),
            permissions,
            new FixedAuthorizationSource(
                CapabilityGrant.Of(Capability.MirrorView)),
            capture,
            new ConfirmingInputBoundary(),
            new RecordingSharingSessionBoundary(),
            emergencyStops,
            new DesktopRemoteWindowHostControlPeer(HostDeviceId),
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        var request = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            connection,
            protection,
            MirrorParticipantRole.ViewOnly);

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(request));

        Assert.Contains("participant_busy", failure.Message);
        Assert.Contains("connection.route", timeline);
        Assert.Contains("connection.prepare", timeline);
        Assert.DoesNotContain("connection.wait_media", timeline);
        Assert.DoesNotContain("protection.subscribe", timeline);
        Assert.DoesNotContain("emergency_stop.register", timeline);
        Assert.DoesNotContain("capture.start", timeline);
        Assert.DoesNotContain("connection.publish", timeline);
        Assert.Equal(0, capture.StartCount);
        Assert.Equal(1, connection.FailCloseCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(0, permissions.ObserverCount);
        Assert.True(protection.IsDisposed);
        Assert.Null(emergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ExpiredPreparationAfterMediaAttachmentNeverStartsCapture()
    {
        var timeline = new List<string>();
        using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            sources.RegisterGeneric(CreateMetadata());
        using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
            sources,
            registration.Snapshot);
        var clock = new MutableClock(Now);
        var permissions = new RecordingPermissionBoundary(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1));
        var capture = new RecordingCaptureBoundary(timeline);
        var protection = new RecordingProtectionSource(
            timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));
        var emergencyStops = new RecordingEmergencyStopRegistrar(timeline);
        var connection = new RecordingHostConnection(
            timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = static request =>
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")),
            WaitingForMedia = () => clock.UtcNow = Now.AddSeconds(5),
        };
        await using var coordinator = new DesktopRemoteWindowHostCoordinator(
            clock,
            permissions,
            new FixedAuthorizationSource(
                CapabilityGrant.Of(Capability.MirrorView)),
            capture,
            new ConfirmingInputBoundary(),
            new RecordingSharingSessionBoundary(),
            emergencyStops,
            new DesktopRemoteWindowHostControlPeer(HostDeviceId),
            ownerLeaseDuration: TimeSpan.FromSeconds(10),
            preparationLifetime: TimeSpan.FromSeconds(5));
        var request = new DesktopRemoteWindowHostStartRequest(
            sourceLease,
            ownerGeneration: 1,
            connection,
            protection,
            MirrorParticipantRole.ViewOnly);

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(request));

        Assert.Contains("preparation_expired", failure.Message);
        Assert.Contains("connection.wait_media", timeline);
        Assert.DoesNotContain("protection.subscribe", timeline);
        Assert.DoesNotContain("emergency_stop.register", timeline);
        Assert.DoesNotContain("capture.start", timeline);
        Assert.DoesNotContain("connection.publish", timeline);
        Assert.Equal(0, capture.StartCount);
        Assert.Equal(1, connection.FailCloseCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(0, permissions.ObserverCount);
        Assert.True(protection.IsDisposed);
        Assert.Null(emergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    private static NativeRemoteWindowSourceMetadata CreateMetadata() =>
        NativeRemoteWindowSourceMetadata.Create(
            "Test window",
            "Test application",
            NativeRemoteWindowGeometry.Create(0, 0, 640, 480, 1),
            supportsCapture: true,
            supportsInput: true,
            SafeAt(Now));

    private static ProtectionSnapshot SafeAt(DateTimeOffset observedAt) => new(
        ProtectionKind.Safe,
        observedAt,
        "test-protection");

    private static RemoteWindowPreparationDeliveryResult ReadyPreparation(
        RemoteWindowPreparationRequest request) =>
        RemoteWindowPreparationDeliveryResult.Acknowledged(
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"));

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

    private static void AssertOrdered(
        List<string> timeline,
        params string[] expected)
    {
        int previous = -1;
        foreach (string entry in expected)
        {
            int current = -1;
            for (int index = 0; index < timeline.Count; index++)
            {
                if (StringComparer.Ordinal.Equals(timeline[index], entry))
                {
                    current = index;
                    break;
                }
            }

            Assert.True(
                current > previous,
                $"Expected '{entry}' after index {previous}: {string.Join(", ", timeline)}");
            previous = current;
        }
    }

    private static async Task WaitForControlRouteClosedAsync(
        DesktopRemoteWindowHostControlPeer controlPeer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                _ = controlPeer.SessionId;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }
    }

    private static async Task WaitForTerminalFailureAsync(
        DesktopRemoteWindowHostCoordinator coordinator)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (coordinator.TerminalFailure is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }
    }

    private sealed class ReadyHostHarness : IDisposable
    {
        private readonly NativeRemoteWindowSourceRegistry sources =
            new(HostDeviceId);
        private readonly NativeRemoteWindowSourceRegistration registration;
        private readonly NativeRemoteWindowSourceLease sourceLease;

        public ReadyHostHarness()
        {
            registration = sources.RegisterGeneric(CreateMetadata());
            sourceLease = AcquireLease(sources, registration.Snapshot);
            Permissions = new RecordingPermissionBoundary(
                NativeRemoteWindowPermissionSnapshot.Create(
                    NativeRemoteWindowPermissionState.Granted,
                    NativeRemoteWindowPermissionState.Granted,
                    ownerGeneration: 1,
                    revision: 1));
            Capture = new RecordingCaptureBoundary(Timeline);
            Input = new ConfirmingInputBoundary();
            Protection = CreateProtection();
            EmergencyStops = new RecordingEmergencyStopRegistrar(Timeline);
            ControlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
            Connection = new RecordingHostConnection(
                Timeline,
                HostDeviceId,
                ParticipantDeviceId)
            {
                PrepareResponse = ReadyPreparation,
            };
            Coordinator = new DesktopRemoteWindowHostCoordinator(
                new FixedClock(Now),
                Permissions,
                new FixedAuthorizationSource(
                    CapabilityGrant.Of(Capability.MirrorView)),
                Capture,
                Input,
                new RecordingSharingSessionBoundary(),
                EmergencyStops,
                ControlPeer,
                ownerLeaseDuration: TimeSpan.FromSeconds(10),
                preparationLifetime: TimeSpan.FromSeconds(5));
        }

        public RecordingCaptureBoundary Capture { get; }

        public RecordingHostConnection Connection { get; }

        public DesktopRemoteWindowHostControlPeer ControlPeer { get; }

        public DesktopRemoteWindowHostCoordinator Coordinator { get; }

        public RecordingEmergencyStopRegistrar EmergencyStops { get; }

        public ConfirmingInputBoundary Input { get; }

        public RecordingPermissionBoundary Permissions { get; }

        public RecordingProtectionSource Protection { get; }

        public List<string> Timeline { get; } = [];

        public DesktopRemoteWindowHostStartRequest CreateRequest(
            RecordingHostConnection connection,
            RecordingProtectionSource protection) => new(
                sourceLease,
                ownerGeneration: 1,
                connection,
                protection,
                MirrorParticipantRole.ViewOnly);

        public RecordingProtectionSource CreateProtection() => new(
            Timeline,
            NativeRemoteWindowProtectionObservation.Create(
                SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));

        public ValueTask<RemoteWindowCommandResult> StartAsync() =>
            Coordinator.StartAsync(CreateRequest(Connection, Protection));

        public void Dispose()
        {
            sourceLease.Dispose();
            registration.Dispose();
            sources.Dispose();
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

    private sealed class FixedAuthorizationSource(CapabilityGrant grant) :
        IMirrorAuthorizationSource
    {
        public int ReadCount { get; private set; }

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId)
        {
            Assert.Equal(ParticipantDeviceId, peerDeviceId);
            ReadCount++;
            return grant;
        }
    }

    private sealed class RecordingPermissionBoundary(
        NativeRemoteWindowPermissionSnapshot snapshot) :
        INativeRemoteWindowPermissionBoundary
    {
        private readonly NativeRemoteWindowPermissionSnapshot snapshot = snapshot;
        private Action<NativeRemoteWindowPermissionSnapshot>? changed;

        public event Action<NativeRemoteWindowPermissionSnapshot>? Changed
        {
            add
            {
                changed += value;
                ObserverCount++;
            }
            remove
            {
                changed -= value;
                ObserverCount--;
            }
        }

        public int ObserverCount { get; private set; }

        public int SnapshotReadCount { get; private set; }

        public NativeRemoteWindowPermissionSnapshot GetSnapshot()
        {
            SnapshotReadCount++;
            return snapshot;
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask DisposeAsync()
        {
            changed = null;
            return ValueTask.CompletedTask;
        }

        public void Publish() => changed?.Invoke(snapshot);
    }

    private sealed class RecordingProtectionSource(
        List<string> timeline,
        NativeRemoteWindowProtectionObservation observation) :
        INativeProtectionSource
    {
        private Action<NativeRemoteWindowProtectionObservation>? changed;

        public bool IsDisposed { get; private set; }

        public int SnapshotReadCount { get; private set; }

        public event Action<NativeRemoteWindowProtectionObservation>? Changed
        {
            add
            {
                timeline.Add("protection.subscribe");
                changed += value;
            }
            remove => changed -= value;
        }

        public bool TryGetLatest(
            out NativeRemoteWindowProtectionObservation? latest)
        {
            timeline.Add("protection.read");
            SnapshotReadCount++;
            latest = observation;
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            changed = null;
        }
    }

    private sealed class RecordingEmergencyStopRegistrar(List<string> timeline) :
        ILocalEmergencyStopRegistrar
    {
        public RecordingEmergencyStopRegistration? CurrentRegistration
        {
            get;
            private set;
        }

        public LocalEmergencyStopRegistrationResult TryRegister(
            long ownerGeneration,
            long sessionGeneration,
            Action<LocalEmergencyStopActivation> callback)
        {
            timeline.Add("emergency_stop.register");
            CurrentRegistration = new RecordingEmergencyStopRegistration(
                ownerGeneration,
                sessionGeneration,
                callback);
            return LocalEmergencyStopRegistrationResult.Confirmed(
                CurrentRegistration,
                "emergency_stop_registered");
        }

        public void Dispose() => CurrentRegistration?.Dispose();
    }

    private sealed class RecordingEmergencyStopRegistration(
        long ownerGeneration,
        long sessionGeneration,
        Action<LocalEmergencyStopActivation> callback) :
        ILocalEmergencyStopRegistration
    {
        private Action<LocalEmergencyStopActivation>? callback = callback;

        public long OwnerGeneration { get; } = ownerGeneration;

        public long SessionGeneration { get; } = sessionGeneration;

        public bool IsCurrent => callback is not null;

        public void Dispose() => callback = null;
    }

    private sealed class RecordingCaptureBoundary(List<string> timeline) :
        INativeRemoteWindowCaptureBoundary
    {
        private INativeRemoteWindowFrameSink? frameSink;
        private NativeRemoteWindowSourceUse? sourceUse;

        public NativeRemoteWindowSourceUse CurrentSource => Assert.IsType<
            NativeRemoteWindowSourceUse>(sourceUse);

        public int StartCount { get; private set; }

        public int EmergencyStopCount { get; private set; }

        public int StopCount { get; private set; }

        public LocalBoundaryResult StopResult { get; set; } =
            LocalBoundaryResult.Confirmed("native_capture_stopped");

        public ValueTask<LocalBoundaryResult> StartAsync(
            NativeRemoteWindowSourceUse source,
            INativeRemoteWindowFrameSink sink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Add("capture.start");
            StartCount++;
            sourceUse = source;
            frameSink = sink;
            EmitFrame(sequence: 1);
            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("native_capture_started"));
        }

        public void EmitFrame(long sequence)
        {
            NativeRemoteWindowSourceUse currentSource = Assert.IsType<
                NativeRemoteWindowSourceUse>(sourceUse);
            INativeRemoteWindowFrameSink currentSink = Assert.IsAssignableFrom<
                INativeRemoteWindowFrameSink>(frameSink);
            currentSink.TakeOwnership(
                currentSource,
                CreateFrame(currentSource, sequence));
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_capture_resumed");

        public LocalBoundaryResult EmergencyStopNow()
        {
            EmergencyStopCount++;
            return LocalBoundaryResult.Confirmed(
                "native_capture_emergency_stopped");
        }

        public LocalBoundaryResult StopNow()
        {
            StopCount++;
            return StopResult;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ConfirmingInputBoundary : INativeRemoteInputBoundary
    {
        public int EmergencyStopCount { get; private set; }

        public int StopCount { get; private set; }

        public ValueTask<LocalBoundaryResult> InjectAsync(
            NativeRemoteWindowSourceUse sourceUse,
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("native_input_injected"));
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_input_resumed");

        public LocalBoundaryResult EmergencyStopNow()
        {
            EmergencyStopCount++;
            return LocalBoundaryResult.Confirmed(
                "native_input_emergency_stopped");
        }

        public LocalBoundaryResult StopNow()
        {
            StopCount++;
            return LocalBoundaryResult.Confirmed("native_input_stopped");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingInputBoundary : INativeRemoteInputBoundary
    {
        public TaskCompletionSource Entered { get; } = NewCompletion();

        public TaskCompletionSource Release { get; } = NewCompletion();

        public int StopCount { get; private set; }

        public async ValueTask<LocalBoundaryResult> InjectAsync(
            NativeRemoteWindowSourceUse sourceUse,
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sourceUse);
            ArgumentNullException.ThrowIfNull(batch);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return LocalBoundaryResult.Confirmed("native_input_injected");
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("native_input_emergency_stopped");

        public LocalBoundaryResult StopNow()
        {
            StopCount++;
            return LocalBoundaryResult.Confirmed("native_input_stopped");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSharingSessionBoundary :
        ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("all_peers_disconnected");
    }

    private sealed class RecordingFrameDestination(List<string> timeline) :
        INativeRemoteWindowFrameSink
    {
        public List<long> Sequences { get; } = [];

        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame)
        {
            timeline.Add("frame.deliver");
            Sequences.Add(frame.Sequence);
            frame.Dispose();
        }
    }

    private sealed class BlockingFrameDestination : INativeRemoteWindowFrameSink
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int deliveryCount;

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DeliveryCount => Volatile.Read(ref deliveryCount);

        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame)
        {
            Interlocked.Increment(ref deliveryCount);
            Entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            frame.Dispose();
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class RecordingHostConnection(
        List<string> timeline,
        DeviceId localDeviceId,
        DeviceId peerDeviceId) :
        IDesktopRemoteWindowHostConnection
    {
        private readonly TaskCompletionSource failClosed = NewCompletion();
        private readonly TaskCompletionSource disposeEntered = NewCompletion();
        private readonly TaskCompletionSource disposeRelease = NewCompletion();
        private readonly TaskCompletionSource disposed = NewCompletion();
        private readonly Lock mediaGate = new();
        private int disposeCount;
        private Action? revoked;
        private TaskCompletionSource mediaChanged = NewCompletion();

        public Func<RemoteWindowPreparationRequest,
            RemoteWindowPreparationDeliveryResult>? PrepareResponse
        {
            get;
            set;
        }

        public Action<RemoteWindowParticipantState>? Publishing { get; set; }

        public Action? WaitingForMedia { get; set; }

        public bool BlockDisposal { get; set; }

        public Exception? DisposeFailure { get; set; }

        public Exception? RouteSelectionFailure { get; set; }

        public ProtocolVersion ProtocolVersion { get; set; } =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public int CurrentReadCount { get; private set; }

        public bool IsCurrent
        {
            get
            {
                CurrentReadCount++;
                return isCurrent;
            }
        }

        private bool isCurrent = true;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public int FailCloseCount { get; private set; }

        public Exception? SendFailure { get; set; }

        public IReadOnlyList<RemoteWindowMediaFrameSnapshot> MediaFrames
        {
            get;
            private set;
        } = [];

        public IDisposable RegisterRevocationCallback(Action callback)
        {
            Volatile.Write(ref revoked, callback);
            return new CallbackRegistration(
                () => Interlocked.Exchange(ref revoked, null));
        }

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            TimeSpan lifetime)
        {
            timeline.Add("connection.route");
            if (RouteSelectionFailure is { } failure)
            {
                throw failure;
            }
        }

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Add("connection.prepare");
            return ValueTask.FromResult(PrepareResponse!(request));
        }

        public ValueTask WaitForMediaAttachmentAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Add("connection.wait_media");
            WaitingForMedia?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Add("connection.publish");
            Publishing?.Invoke(state);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            cancellationToken.ThrowIfCancellationRequested();
            if (SendFailure is { } failure)
            {
                return ValueTask.FromException(failure);
            }

            lock (mediaGate)
            {
                timeline.Add("connection.send_media");
                MediaFrames =
                [
                    .. MediaFrames,
                    new RemoteWindowMediaFrameSnapshot(
                        frame.Kind,
                        frame.Sequence,
                        frame.ChunkIndex,
                        frame.ChunkCount,
                        frame.ExportPayload()),
                ];
                TaskCompletionSource completed = mediaChanged;
                mediaChanged = NewCompletion();
                completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public async Task WaitForMediaFrameCountAsync(int expected)
        {
            while (true)
            {
                Task wait;
                lock (mediaGate)
                {
                    if (MediaFrames.Count >= expected)
                    {
                        return;
                    }

                    wait = mediaChanged.Task;
                }

                await wait.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public ValueTask FailCloseAsync()
        {
            timeline.Add("connection.fail_close");
            FailCloseCount++;
            isCurrent = false;
            failClosed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public Task WaitForFailCloseAsync() => failClosed.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        public Task WaitForDisposeAsync() => disposed.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        public Task WaitForDisposeEnteredAsync() => disposeEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        public async ValueTask DisposeAsync()
        {
            timeline.Add("connection.dispose");
            Interlocked.Increment(ref disposeCount);
            Interlocked.Exchange(ref revoked, null);
            disposeEntered.TrySetResult();
            try
            {
                if (BlockDisposal)
                {
                    await disposeRelease.Task.ConfigureAwait(false);
                }

                if (DisposeFailure is { } failure)
                {
                    throw failure;
                }
            }
            finally
            {
                disposed.TrySetResult();
            }
        }

        public void ReleaseDisposal() => disposeRelease.TrySetResult();

        public void Revoke()
        {
            isCurrent = false;
            Volatile.Read(ref revoked)?.Invoke();
        }
    }

    private sealed record RemoteWindowMediaFrameSnapshot(
        RemoteWindowMediaKind Kind,
        ulong Sequence,
        ushort ChunkIndex,
        ushort ChunkCount,
        byte[] Payload);

    private static TaskCompletionSource NewCompletion() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CallbackRegistration(Action dispose) : IDisposable
    {
        private Action? dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }

    private static NativeRemoteWindowFrame CreateFrame(
        NativeRemoteWindowSourceUse sourceUse,
        long sequence)
    {
        var owner = new TestMemoryOwner(4);
        return NativeRemoteWindowFrame.TakeOwnership(
            owner,
            payloadLength: 4,
            width: 1,
            height: 1,
            stride: 4,
            NativeRemoteWindowPixelFormat.Bgra8888,
            sourceUse.OwnerGeneration,
            sourceUse.SessionGeneration,
            sourceUse.SourceGeneration,
            sourceUse.GeometryRevision,
            sequence);
    }

    private sealed class TestMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private byte[]? buffer = new byte[length];

        public Memory<byte> Memory => buffer ?? throw new ObjectDisposedException(
            nameof(TestMemoryOwner));

        public void Dispose() => buffer = null;
    }
}
