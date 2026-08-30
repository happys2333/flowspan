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
        Assert.Equal(1, authorization.ReservationCount);
        Assert.Equal(
            connection.AuthenticatedPeerFingerprint,
            authorization.AuthenticatedFingerprint);
        Assert.False(authorization.CurrentReservation?.IsCurrent);
        Assert.Equal(1, authorization.CurrentReservation?.DisposeCount);
        Assert.Equal(1, permissions.PreparationReservationCount);
        Assert.False(permissions.CurrentPreparationRegistration?.IsCurrent);
        Assert.Equal(
            1,
            permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Equal(1, connection.ConnectionPreparationReservationCount);
        Assert.False(connection.CurrentConnectionPreparation?.IsCurrent);
        Assert.Equal(1, connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.Equal(1, protection.PreparationReservationCount);
        Assert.True(protection.CurrentPreparation?.IsCurrent);
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
            "protection.read",
            "protection.reserve",
            "connection.route",
            "connection.prepare",
            "connection.wait_media",
            "protection.subscribe",
            "protection.read",
            "emergency_stop.register",
            "protection.promote",
            "protection.capture_admit",
            "capture.start",
            "connection.publish",
            "connection.send_media");
        _ = await coordinator.StopAsync();
        Assert.False(protection.CurrentPreparation?.IsCurrent);
        Assert.Equal(1, protection.CurrentPreparation?.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => controlPeer.SessionId);
    }

    [Fact]
    public async Task AdmissionPublishThrowIsRedactedAndFailsClosed()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_ADMISSION_PUBLISH_CANARY");
        bool emergencyReleasedAfterStop = false;
        host.EmergencyStops.RegistrationDisposing = () =>
            emergencyReleasedAfterStop = host.Capture.StopCount > 0
                && host.Input.StopCount > 0;
        host.Connection.PublishFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("host_admission_publish_failed", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Null(failure.InnerException);
        Assert.Contains("connection.publish", host.Timeline);
        Assert.DoesNotContain("connection.send_media", host.Timeline);
        Assert.Empty(host.Connection.MediaFrames);
        Assert.Equal(1, host.Capture.StartCount);
        Assert.Equal(1, host.Capture.StopCount);
        Assert.Equal(1, host.Input.StopCount);
        Assert.True(emergencyReleasedAfterStop);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Throws<InvalidOperationException>(() => host.ControlPeer.SessionId);
    }

    [Fact]
    public async Task CallerCancellationAtAdmissionPublishPreservesExactToken()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Connection.Publishing = _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Contains("connection.publish", host.Timeline);
        Assert.DoesNotContain("connection.send_media", host.Timeline);
        Assert.Empty(host.Connection.MediaFrames);
        Assert.Equal(1, host.Capture.StartCount);
        Assert.Equal(1, host.Capture.StopCount);
        Assert.Equal(1, host.Input.StopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Throws<InvalidOperationException>(() => host.ControlPeer.SessionId);
    }

    [Fact]
    public async Task ForeignAdmissionCancellationIsRedactedWhenCallerAlsoCancels()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var callerCancellation = new CancellationTokenSource();
        using var foreignCancellation = new CancellationTokenSource();
        const string canary = "FLOWSPAN_FOREIGN_ADMISSION_CANCEL_CANARY";
        host.Connection.Publishing = _ =>
        {
            callerCancellation.Cancel();
            throw new OperationCanceledException(
                canary,
                innerException: null,
                foreignCancellation.Token);
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                callerCancellation.Token));

        Assert.Contains("host_admission_publish_failed", failure.Message);
        Assert.DoesNotContain(canary, failure.ToString());
        Assert.Null(failure.InnerException);
        Assert.Contains("connection.publish", host.Timeline);
        Assert.DoesNotContain("connection.send_media", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
    }

    [Fact]
    public async Task UnsafeProtectionRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var unsafeProtection = host.CreateProtection(
            new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-protection"));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, unsafeProtection)));

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.Equal(
            ["protection.read", "connection.dispose"],
            host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(unsafeProtection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionMutationDuringReservationRejectsBeforeObserverOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.PreparationReserved = () => host.Protection.Publish(
            UnsafeAt(Now));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.Equal(1, host.Protection.PreparationReservationCount);
        Assert.False(host.Protection.CurrentPreparation?.IsCurrent);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionMutationAfterRouteSelectionPreventsPrepareWireAndCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.RouteAdmitted = () => host.Protection.Publish(
            UnsafeAt(Now));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionMutationAfterPrepareSendPreventsReadyAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = () => host.Protection.Publish(
            UnsafeAt(Now));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionMutationBeforeCaptureStartAdmissionPreventsCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.CaptureStartAdmitting = () => host.Protection.Publish(
            UnsafeAt(Now));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.Contains("protection.promote", host.Timeline);
        Assert.Contains("protection.capture_admit", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.True(host.Capture.StopCount >= 1);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionMutationAfterCaptureStartAdmissionUsesStartingGate()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.CaptureStartAdmitted = () => host.Protection.Publish(
            UnsafeAt(Now));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("protection_blocked_during_start", failure.Message);
        Assert.Contains("protection.capture_admit", host.Timeline);
        Assert.Contains("capture.start", host.Timeline);
        Assert.DoesNotContain("connection.publish", host.Timeline);
        Assert.Equal(1, host.Capture.StartCount);
        Assert.True(host.Capture.StopCount + host.Capture.EmergencyStopCount >= 1);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task FormalProtectionNotificationsPreserveTransientUnsafeOrder()
    {
        using var clock = new BlockingClock(Now);
        using var host = new ReadyHostHarness(clock);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            registration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                host.Protection.CurrentPreparation);

        clock.BlockNextRead();
        registration.LatchLive(SafeAt(Now));
        Task firstNotify = Task.Run(registration.NotifyFormal);
        Assert.True(clock.Blocked.Wait(TimeSpan.FromSeconds(5)));

        registration.LatchLive(UnsafeAt(Now));
        Task unsafeNotify = Task.Run(registration.NotifyFormal);
        registration.LatchLive(SafeAt(Now));
        Task finalSafeNotify = Task.Run(registration.NotifyFormal);
        Assert.False(unsafeNotify.IsCompleted);
        Assert.False(finalSafeNotify.IsCompleted);

        clock.Release();
        await Task.WhenAll(firstNotify, unsafeNotify, finalSafeNotify)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, host.Capture.PauseCount);
        Assert.Equal(1, host.Capture.ResumeCount);
        Assert.Equal(1, host.Input.PauseCount);
        Assert.Equal(1, host.Input.ResumeCount);
        Assert.Equal(RemoteWindowLifecycle.Active, coordinator.Snapshot?.Lifecycle);
        _ = await coordinator.StopAsync();
    }

    [Fact]
    public async Task LiveProtectionLatchBlocksInputBeforeNotifyAndSafeReopens()
    {
        using var host = new ReadyHostHarness(
            role: MirrorParticipantRole.DriverEligible);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        RemoteWindowSharingSnapshot before = Assert.IsType<
            RemoteWindowSharingSnapshot>(coordinator.Snapshot);
        RemoteWindowParticipantState driver =
            await host.ControlPeer.RequestDriverAsync(
                RemoteWindowDriverRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    host.ControlPeer.SessionId,
                    before.ActivityId,
                    HostDeviceId,
                    ParticipantDeviceId,
                    Assert.IsType<long>(before.DriverLeaseEpoch),
                    TimeSpan.FromSeconds(5),
                    Now.AddSeconds(2)),
                CancellationToken.None);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            registration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                host.Protection.CurrentPreparation);

        RemoteWindowParticipantState initial =
            await host.ControlPeer.SendInputAsync(
                CreateInputRequest(driver, x: 0.25),
                CancellationToken.None);
        Assert.Equal(RemoteWindowControlOutcome.Applied, initial.Outcome);
        Assert.Equal(1, host.Input.InjectCount);

        registration.LatchLive(UnsafeAt(Now));
        RemoteWindowParticipantState blocked =
            await host.ControlPeer.SendInputAsync(
                CreateInputRequest(driver, x: 0.5),
                CancellationToken.None);

        Assert.Equal(RemoteWindowControlOutcome.Rejected, blocked.Outcome);
        Assert.Equal("protection_state_unknown", blocked.ReasonCode);
        Assert.Equal(1, host.Input.InjectCount);
        Assert.Equal(RemoteWindowLifecycle.Active, blocked.Lifecycle);
        registration.NotifyFormal();
        Assert.Equal(
            RemoteWindowLifecycle.ProtectionPaused,
            coordinator.Snapshot?.Lifecycle);

        registration.LatchLive(SafeAt(Now.AddTicks(1)));
        RemoteWindowParticipantState blockedSafe =
            await host.ControlPeer.SendInputAsync(
                CreateInputRequest(driver, x: 0.625),
                CancellationToken.None);
        Assert.Equal(RemoteWindowControlOutcome.Rejected, blockedSafe.Outcome);
        Assert.Equal("protection_state_unknown", blockedSafe.ReasonCode);
        Assert.Equal(1, host.Input.InjectCount);
        registration.NotifyFormal();
        RemoteWindowParticipantState resumed =
            await host.ControlPeer.SendInputAsync(
                CreateInputRequest(driver, x: 0.75),
                CancellationToken.None);

        Assert.Equal(RemoteWindowControlOutcome.Applied, resumed.Outcome);
        Assert.Equal(2, host.Input.InjectCount);
        Assert.Equal(RemoteWindowLifecycle.Active, resumed.Lifecycle);
        _ = await coordinator.StopAsync();

        RemoteWindowInputRequest CreateInputRequest(
            RemoteWindowParticipantState state,
            double x) => RemoteWindowInputRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            state.SessionId,
            state.ActivityId,
            HostDeviceId,
            ParticipantDeviceId,
            Assert.IsType<long>(state.DriverLeaseEpoch),
            RemoteInputBatch.Create([RemoteInputEvent.PointerMove(x, 0.5)]),
            Now.AddSeconds(2));
    }

    [Fact]
    public async Task FormalProtectionSourceLossWaitsForEarlierNotification()
    {
        using var clock = new BlockingClock(Now);
        using var host = new ReadyHostHarness(clock);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            registration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                host.Protection.CurrentPreparation);

        clock.BlockNextRead();
        registration.LatchLive(SafeAt(Now));
        Task firstNotify = Task.Run(registration.NotifyFormal);
        Assert.True(clock.Blocked.Wait(TimeSpan.FromSeconds(5)));

        registration.LatchSourceLoss();
        Task sourceLossNotify = Task.Run(registration.NotifyFormal);
        Assert.False(sourceLossNotify.IsCompleted);

        clock.Release();
        await Task.WhenAll(firstNotify, sourceLossNotify)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        await WaitForControlRouteClosedAsync(host.ControlPeer);
        await WaitForCoordinatorInactiveAsync(coordinator);
    }

    [Fact]
    public async Task FormalProtectionNotificationOutOfMemoryReplaysExactFailure()
    {
        using var clock = new BlockingClock(Now);
        using var host = new ReadyHostHarness(clock);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            registration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                host.Protection.CurrentPreparation);
        INativeRemoteWindowProtectionFormalSink sink = registration.FormalSink;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PROTECTION_NOTIFY_FATAL_CANARY");
#pragma warning restore CA2201
        clock.FailNext(injected);
        sink.LatchNativeRemoteWindowProtectionObservationNow(
            host.Protection.CreateNextObservation(SafeAt(Now)));

        OutOfMemoryException first = Assert.Throws<OutOfMemoryException>(
            sink.NotifyNativeRemoteWindowProtectionChanged);
        OutOfMemoryException replay = Assert.Throws<OutOfMemoryException>(
            sink.NotifyNativeRemoteWindowProtectionChanged);

        Assert.Same(injected, first);
        Assert.Same(injected, replay);
        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        await WaitForControlRouteClosedAsync(host.ControlPeer);
    }

    [Fact]
    public async Task StaleCapturedProtectionNotifyContextMustJoinCurrentDrainer()
    {
        using var clock = new BlockingClock(Now);
        using var host = new ReadyHostHarness(clock);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            registration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                host.Protection.CurrentPreparation);
        var releaseStale = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? staleNotify = null;
        Task? currentNotify = null;
        try
        {
            clock.RunOnNextRead(() => staleNotify = Task.Factory.StartNew(
                () =>
                {
                    releaseStale.Task.GetAwaiter().GetResult();
                    staleEntered.TrySetResult();
                    registration.NotifyFormal();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning
                    | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default));
            registration.LatchLive(SafeAt(Now.AddTicks(1)));
            registration.NotifyFormal();
            Task capturedNotify = Assert.IsAssignableFrom<Task>(staleNotify);

            clock.BlockNextRead();
            registration.LatchLive(SafeAt(Now.AddTicks(2)));
            currentNotify = Task.Factory.StartNew(
                registration.NotifyFormal,
                CancellationToken.None,
                TaskCreationOptions.LongRunning
                    | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
            Assert.True(clock.Blocked.Wait(TimeSpan.FromSeconds(5)));
            registration.LatchLive(UnsafeAt(Now.AddTicks(3)));
            releaseStale.TrySetResult();
            await staleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(SpinWait.SpinUntil(
                () => coordinator.ActiveProtectionNotificationWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
            Assert.False(capturedNotify.IsCompleted);

            clock.Release();
            await Task.WhenAll(currentNotify, capturedNotify)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                RemoteWindowLifecycle.ProtectionPaused,
                coordinator.Snapshot?.Lifecycle);
            Assert.Equal(0, coordinator.ActiveProtectionNotificationWaiterCount);
            _ = await coordinator.StopAsync();
        }
        finally
        {
            releaseStale.TrySetResult();
            clock.Release();
            if (currentNotify is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await currentNotify.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (staleNotify is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await staleNotify.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
    }

    [Fact]
    public async Task NestedProtectionNotifyFindsActiveAncestorAcrossGenerations()
    {
        using var firstClock = new BlockingClock(Now);
        using var secondClock = new BlockingClock(Now);
        using var firstHost = new ReadyHostHarness(firstClock);
        using var secondHost = new ReadyHostHarness(secondClock);
        await using DesktopRemoteWindowHostCoordinator firstCoordinator =
            firstHost.Coordinator;
        await using DesktopRemoteWindowHostCoordinator secondCoordinator =
            secondHost.Coordinator;
        Assert.True((await firstHost.StartAsync()).Succeeded);
        Assert.True((await secondHost.StartAsync()).Succeeded);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            firstRegistration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                firstHost.Protection.CurrentPreparation);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            secondRegistration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                secondHost.Protection.CurrentPreparation);

        firstClock.RunOnNextRead(() =>
        {
            firstRegistration.LatchLive(SafeAt(Now.AddTicks(2)));
            secondRegistration.LatchLive(SafeAt(Now.AddTicks(1)));
            secondRegistration.NotifyFormal();
        });
        secondClock.RunOnNextRead(firstRegistration.NotifyFormal);
        firstRegistration.LatchLive(SafeAt(Now.AddTicks(1)));

        await Task.Run(firstRegistration.NotifyFormal)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RemoteWindowLifecycle.Active, firstCoordinator.Snapshot?.Lifecycle);
        Assert.Equal(
            RemoteWindowLifecycle.Active,
            secondCoordinator.Snapshot?.Lifecycle);
        Assert.Equal(0, firstCoordinator.ActiveProtectionNotificationWaiterCount);
        Assert.Equal(0, secondCoordinator.ActiveProtectionNotificationWaiterCount);
        _ = await firstCoordinator.StopAsync();
        _ = await secondCoordinator.StopAsync();
    }

    [Fact]
    public async Task SymmetricProtectionNotifiersDoNotWaitOnEachOther()
    {
        using var firstClock = new BlockingClock(Now);
        using var secondClock = new BlockingClock(Now);
        using var firstHost = new ReadyHostHarness(firstClock);
        using var secondHost = new ReadyHostHarness(secondClock);
        await using DesktopRemoteWindowHostCoordinator firstCoordinator =
            firstHost.Coordinator;
        await using DesktopRemoteWindowHostCoordinator secondCoordinator =
            secondHost.Coordinator;
        Assert.True((await firstHost.StartAsync()).Succeeded);
        Assert.True((await secondHost.StartAsync()).Succeeded);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            firstRegistration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                firstHost.Protection.CurrentPreparation);
        RecordingProtectionSource.RecordingProtectionPreparationRegistration
            secondRegistration = Assert.IsType<
                RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                secondHost.Protection.CurrentPreparation);
        using var callbacksEntered = new CountdownEvent(2);
        using var releaseCrossNotify = new ManualResetEventSlim(false);
        using var firstCrossReturned = new ManualResetEventSlim(false);
        using var secondCrossReturned = new ManualResetEventSlim(false);
        firstClock.RunOnNextRead(() =>
        {
            callbacksEntered.Signal();
            releaseCrossNotify.Wait();
            secondRegistration.NotifyFormal();
            firstCrossReturned.Set();
        });
        secondClock.RunOnNextRead(() =>
        {
            callbacksEntered.Signal();
            releaseCrossNotify.Wait();
            firstRegistration.NotifyFormal();
            secondCrossReturned.Set();
        });
        firstRegistration.LatchLive(SafeAt(Now.AddTicks(1)));
        secondRegistration.LatchLive(SafeAt(Now.AddTicks(1)));
        Task firstNotify = Task.Run(firstRegistration.NotifyFormal);
        Task secondNotify = Task.Run(secondRegistration.NotifyFormal);
        Assert.True(callbacksEntered.Wait(TimeSpan.FromSeconds(5)));

        releaseCrossNotify.Set();
        await Task.WhenAll(firstNotify, secondNotify)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(firstCrossReturned.IsSet);
        Assert.True(secondCrossReturned.IsSet);
        Assert.Equal(RemoteWindowLifecycle.Active, firstCoordinator.Snapshot?.Lifecycle);
        Assert.Equal(
            RemoteWindowLifecycle.Active,
            secondCoordinator.Snapshot?.Lifecycle);
        Assert.Equal(0, firstCoordinator.ActiveProtectionNotificationWaiterCount);
        Assert.Equal(0, secondCoordinator.ActiveProtectionNotificationWaiterCount);
        _ = await firstCoordinator.StopAsync();
        _ = await secondCoordinator.StopAsync();
    }

    [Fact]
    public async Task ProtectionPreparationConflictRejectsBeforeRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.PreparationStatus =
            NativeRemoteWindowProtectionPreparationReservationStatus
                .ReservationConflict;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.Equal(1, host.Protection.PreparationReservationCount);
        Assert.Null(host.Protection.CurrentPreparation);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionPreparationThrowIsRedactedBeforeRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_PROTECTION_RESERVE_CANARY");
        host.Protection.PreparationFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionPreparationOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PROTECTION_RESERVE_FATAL_CANARY");
#pragma warning restore CA2201
        host.Protection.PreparationFailure = injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionPreparationCommitThenCallerCancellationRetainsOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Protection.PreparationReserved = () =>
        {
            cancellation.Cancel();
            throw injected;
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(1, host.Protection.CurrentPreparation?.DisposeCount);
        Assert.False(host.Protection.CurrentPreparation?.IsCurrent);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionPromotionThrowIsRedactedAndDrained()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_PROTECTION_PROMOTE_CANARY");
        host.Protection.PromotionFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Protection.CurrentPreparation?.DisposeCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionPromotionOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PROTECTION_PROMOTE_FATAL_CANARY");
#pragma warning restore CA2201
        host.Protection.PromotionFailure = injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Protection.CurrentPreparation?.DisposeCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionPromotionCurrentnessThrowIsRedacted()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException(
            "FLOWSPAN_PROTECTION_PROMOTION_CURRENT_CANARY");
        host.Protection.CurrentReading = read => read == 1
            ? true
            : throw injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("protection.promote", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Protection.CurrentPreparation?.DisposeCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task UnavailableEmergencyStopRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.EmergencyStops.ReadinessResult = LocalBoundaryResult.Failed(
            "emergency_stop_unavailable");

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_unavailable", failure.Message);
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyReadinessLossBeforeRouteRejectsWithoutWireAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.EmergencyStops.ReadinessReserved = () => Assert.True(
            host.EmergencyStops.LoseReadiness());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_readiness_unavailable", failure.Message);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task AuthorizationReservationRejectionPreventsRouteAndPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Authorization.ReservationRejectionReason =
            "mirror_capability_denied";

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("mirror_capability_denied", failure.Message);
        Assert.Equal(1, host.Authorization.ReservationCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task MissingAuthenticatedFingerprintRejectsBeforeAuthorizationReservation()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.AuthenticatedPeerFingerprint = " ";

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Equal(0, host.Authorization.ReservationCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task AuthorizationReservationThrowIsRedactedAndOutOfMemoryEscapes()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("AUTHORIZATION_RESERVATION_CANARY");
        host.Authorization.ReservationFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("mirror_authorization_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);

#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var fatal = new OutOfMemoryException(
            "Injected authorization reservation exhaustion.");
#pragma warning restore CA2201
        using var fatalHost = new ReadyHostHarness();
        fatalHost.Authorization.ReservationFailure = fatal;
        await using DesktopRemoteWindowHostCoordinator fatalCoordinator =
            fatalHost.Coordinator;

        OutOfMemoryException fatalFailure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await fatalHost.StartAsync());

        Assert.Same(fatal, fatalFailure);
        Assert.DoesNotContain("connection.route", fatalHost.Timeline);
        Assert.Equal(1, fatalHost.Connection.DisposeCount);
    }

    [Fact]
    public async Task CallerCancellationAfterAuthorizationReservationPreservesToken()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Authorization.ReservationCommitted = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(1, host.Authorization.ReservationCount);
        Assert.Equal(1, host.Authorization.CurrentReservation?.DisposeCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task AuthorizationInvalidationBeforeRoutePreventsWireAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Authorization.ReservationCommitted = () => Assert.True(
            host.Authorization.InvalidateReservation());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("mirror_capability_denied", failure.Message);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterEmergencyReadinessReservationPreservesToken()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.EmergencyStops.ReadinessReserved = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessReservationCount);
        Assert.Equal(1, host.EmergencyStops.ReadinessReservationDisposeCount);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationBeforeEmergencyPromotionInstallsNoFormalOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Protection.Reading = () =>
        {
            if (host.Protection.SnapshotReadCount == 3)
            {
                cancellation.Cancel();
            }
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.EmergencyStops.ReadinessReservationCount);
        Assert.Equal(1, host.EmergencyStops.ReadinessReservationDisposeCount);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyStopReadinessThrowIsRedactedBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException(
            "FLOWSPAN_EMERGENCY_STOP_READINESS_CANARY");
        host.EmergencyStops.ReadinessFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_readiness_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ProtectionReadThrowIsRedactedBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_PROTECTION_READ_CANARY");
        host.Protection.ReadFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_protection_not_safe", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(
            ["protection.read", "connection.dispose"],
            host.Timeline);
        Assert.Equal(1, host.Protection.SnapshotReadCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterProtectionPreflightRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Protection.Reading = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(
            ["protection.read", "connection.dispose"],
            host.Timeline);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterEmergencyReadinessRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.EmergencyStops.CheckingReadiness = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionRevocationDuringProtectionPreflightRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.Reading = host.Connection.Revoke;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Equal(
            ["protection.read", "protection.reserve", "connection.dispose"],
            host.Timeline);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionMutationDuringReservationRejectsBeforeObserverOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.ConnectionPreparationReserved = () => Assert.True(
            host.Connection.InvalidateConnectionPreparation());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Equal(1, host.Connection.ConnectionPreparationReservationCount);
        Assert.False(host.Connection.CurrentConnectionPreparation?.IsCurrent);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionPreparationConflictRejectsBeforeObserverOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.ConnectionPreparationStatus =
            AuthenticatedRemoteWindowConnectionPreparationReservationStatus
                .ReservationConflict;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Equal(1, host.Connection.ConnectionPreparationReservationCount);
        Assert.Null(host.Connection.CurrentConnectionPreparation);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionPreparationThrowIsRedactedBeforeObserverOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_CONNECTION_RESERVE_CANARY");
        host.Connection.ConnectionPreparationFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Null(host.Connection.CurrentConnectionPreparation);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionPreparationOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_CONNECTION_RESERVE_FATAL_CANARY");
#pragma warning restore CA2201
        host.Connection.ConnectionPreparationFailure = injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Null(host.Connection.CurrentConnectionPreparation);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionPreparationCommitThenThrowRetainsCleanupOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException(
            "FLOWSPAN_CONNECTION_COMMITTED_RESERVE_CANARY");
        host.Connection.ConnectionPreparationReserved = () => throw injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.False(host.Connection.CurrentConnectionPreparation?.IsCurrent);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionPreparationCommitThenCallerCancellationRetainsOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Connection.ConnectionPreparationReserved = () =>
        {
            cancellation.Cancel();
            throw injected;
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.False(host.Connection.CurrentConnectionPreparation?.IsCurrent);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task InitialConnectionRegistrationForeignCancellationIsRedacted()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var callerCancellation = new CancellationTokenSource();
        using var foreignCancellation = new CancellationTokenSource();
        const string canary = "FLOWSPAN_CONNECTION_CURRENT_FOREIGN_CANARY";
        host.Connection.ConnectionPreparationCurrentReading = _ =>
        {
            callerCancellation.Cancel();
            throw new OperationCanceledException(
                canary,
                innerException: null,
                foreignCancellation.Token);
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                callerCancellation.Token));

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.DoesNotContain(canary, failure.ToString());
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task InitialConnectionRegistrationOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_CONNECTION_CURRENT_FATAL_CANARY");
#pragma warning restore CA2201
        host.Connection.ConnectionPreparationCurrentReading = _ => throw injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionConnectionRegistrationReadThrowIsRedactedAndDrained()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException(
            "FLOWSPAN_CONNECTION_PROMOTION_CURRENT_CANARY");
        host.Connection.ConnectionPreparationCurrentReading = read => read == 1
            ? true
            : throw injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionConnectionRegistrationPreservesExactCallerCancellation()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Connection.ConnectionPreparationCurrentReading = read =>
        {
            if (read == 1)
            {
                return true;
            }

            cancellation.Cancel();
            throw injected;
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionConnectionRegistrationOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_CONNECTION_PROMOTION_CURRENT_FATAL_CANARY");
#pragma warning restore CA2201
        host.Connection.ConnectionPreparationCurrentReading = read => read == 1
            ? true
            : throw injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(1, host.Connection.CurrentConnectionPreparation?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionRevocationDuringEmergencyReadinessRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.EmergencyStops.CheckingReadiness = host.Connection.Revoke;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterInitialHostFactsRejectsBeforeSafetyOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Authorization.Reading = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(["connection.dispose"], host.Timeline);
        Assert.Equal(0, host.Protection.SnapshotReadCount);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PreparationExpiryDuringEmergencyReadinessRejectsBeforeRouteOrPrepare()
    {
        var clock = new MutableClock(Now);
        using var host = new ReadyHostHarness(clock);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.EmergencyStops.CheckingReadiness = () =>
            clock.UtcNow = Now.AddSeconds(5);

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("preparation_expired", failure.Message);
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task SourceInvalidationDuringProtectionPreflightRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.Reading = host.InvalidateSource;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_source_stale", failure.Message);
        Assert.Equal(
            ["protection.read", "protection.reserve", "connection.dispose"],
            host.Timeline);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task SourceInvalidationAfterRouteAdmissionPreventsPrepareAndCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.RouteAdmitted = host.InvalidateSource;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_source_stale", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyReadinessLossAfterRouteAdmissionPreventsPrepareAndCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.RouteAdmitted = () =>
            Assert.True(host.EmergencyStops.LoseReadiness());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_readiness_unavailable", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task AuthorizationInvalidationAfterRouteAdmissionPreventsPrepareAndCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.RouteAdmitted = () => Assert.True(
            host.Authorization.InvalidateReservation());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("mirror_capability_denied", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionMutationAfterRouteSelectionPreventsPrepareWireAndCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.BeforePrepareSendAdmission = () => Assert.True(
            host.Connection.InvalidateConnectionPreparation());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionMutationAfterRouteSelectionPreventsPrepareWireAndCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.BeforePrepareSendAdmission = () =>
            host.Permissions.Publish(
                NativeRemoteWindowPermissionSnapshot.Create(
                    NativeRemoteWindowPermissionState.Revoked,
                    NativeRemoteWindowPermissionState.Granted,
                    ownerGeneration: 1,
                    revision: 2));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_denied", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task SourceInvalidationDuringRouteFailureReportsStableReason()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.RouteAdmitted = host.InvalidateSource;
        host.Connection.RouteSelectionFailure = new IOException(
            "route failed after claiming ownership");

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_source_stale", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task SourceInvalidationAfterPrepareSendPreventsReadyAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = host.InvalidateSource;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_source_stale", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyReadinessLossAfterPrepareSendPreventsReadyAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = () =>
            Assert.True(host.EmergencyStops.LoseReadiness());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_readiness_unavailable", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task AuthorizationInvalidationAfterPrepareSendPreventsReadyAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = () => Assert.True(
            host.Authorization.InvalidateReservation());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("mirror_capability_denied", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionMutationAfterPrepareSendPreventsReadyAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = () => host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_denied", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionMutationAfterPrepareSendPreventsReadyAuthority()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = () => Assert.True(
            host.Connection.InvalidateConnectionPreparation());

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyReadinessPromotionFailureAfterReadyIsRedactedAndFailsClosed()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("EMERGENCY_PROMOTION_CANARY");
        host.EmergencyStops.PromotionFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_registration_failed", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.Contains("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyPromotionSideEffectThenThrowRetainsCleanupOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("EMERGENCY_PROMOTION_COMMIT_CANARY");
        bool emergencyReleasedAfterStop = false;
        host.EmergencyStops.RegistrationDisposing = () =>
            emergencyReleasedAfterStop = host.Capture.StopCount > 0
                && host.Input.StopCount > 0;
        host.EmergencyStops.PromotionFailureAfterCommit = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_registration_failed", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Contains("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.True(emergencyReleasedAfterStop);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyActivationDuringPromotionCannotAuthorizeCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.EmergencyStops.PromotionCommitted = () => Assert.True(
            host.EmergencyStops.CurrentRegistration!.Trigger(
                LocalEmergencyStopCause.RegistrationLost));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_readiness_unavailable", failure.Message);
        Assert.Contains("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task EmergencyRegistrarDisposalAfterPromotionCannotAuthorizeCapture()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Authorization.Reading = () =>
        {
            if (host.Authorization.ReadCount == 5)
            {
                host.EmergencyStops.Dispose();
            }
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("emergency_stop_readiness_unavailable", failure.Message);
        Assert.Contains("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.True(host.EmergencyStops.CurrentRegistration?.IsCurrent is not true);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterEmergencyPromotionPreservesOwnerAndToken()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.EmergencyStops.PromotionCommitted = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Contains("emergency_stop.register", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task FormalEmergencyRegistrationRemainsUntilNativeBoundariesStop()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        bool disposedAfterBoundaries = false;
        host.EmergencyStops.RegistrationDisposing = () =>
            disposedAfterBoundaries = host.Capture.StopCount > 0
                && host.Input.StopCount > 0;
        Assert.True((await host.StartAsync()).Succeeded);

        RemoteWindowStopResult stopped = await coordinator.StopAsync();

        Assert.True(stopped.FullyStopped);
        Assert.True(disposedAfterBoundaries);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
    }

    [Fact]
    public async Task SourceInvalidationDuringPrepareFailureReportsStableReason()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Connection.PrepareSendAdmitted = host.InvalidateSource;
        host.Connection.PrepareResponse = _ => throw new IOException(
            "wire failed after admitting Prepare");

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_source_stale", failure.Message);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ExactCallerCancellationWinsConcurrentSourceInvalidation()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Connection.PrepareSendAdmitted = () =>
        {
            host.InvalidateSource();
            cancellation.Cancel();
        };
        host.Connection.PrepareResponse = _ => throw injected;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Contains("connection.route", host.Timeline);
        Assert.Contains("connection.prepare", host.Timeline);
        Assert.DoesNotContain("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CapturePermissionRevocationDuringProtectionPreflightRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Protection.Reading = () => host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_denied", failure.Message);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionMutationDuringReservationRejectsBeforeObserverRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Permissions.PreparationReserved = () => host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_denied", failure.Message);
        Assert.Equal(1, host.Permissions.PreparationReservationCount);
        Assert.False(host.Permissions.CurrentPreparationRegistration?.IsCurrent);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionSnapshotReplacementDuringReservationRejectsBeforeRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.Permissions.Preparing = () => host.Permissions.ReplaceCurrent(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_denied", failure.Message);
        Assert.Equal(1, host.Permissions.PreparationReservationCount);
        Assert.Null(host.Permissions.CurrentPreparationRegistration);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterPermissionReservationReleasesGuard()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Permissions.PreparationReserved = cancellation.Cancel;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(1, host.Permissions.PreparationReservationCount);
        Assert.False(host.Permissions.CurrentPreparationRegistration?.IsCurrent);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PreRoutePermissionRevocationWaitsForStartedFailCloseFailure()
    {
        using var host = new ReadyHostHarness();
        DesktopRemoteWindowHostCoordinator coordinator = host.Coordinator;
        var injected = new IOException("injected pre-route fail-close failure");
        host.Connection.BlockFailClose = true;
        host.Connection.FailCloseFailure = injected;
        host.Protection.Reading = () => host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        Task<RemoteWindowCommandResult> start = host.StartAsync().AsTask();
        await host.Connection.WaitForFailCloseEnteredAsync();
        bool waitedForFailClose = !start.IsCompleted;
        host.Connection.ReleaseFailClose();
        Exception? observed = await Record.ExceptionAsync(async () => await start);

        Assert.True(waitedForFailClose);
        AggregateException failure = Assert.IsType<AggregateException>(observed);
        Assert.Collection(
            failure.InnerExceptions,
            primary => Assert.Contains(
                "native_permission_denied",
                Assert.IsType<InvalidOperationException>(primary).Message),
            cleanup => Assert.Same(injected, cleanup));
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Same(injected, coordinator.TerminalFailure);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
        IOException terminal = await Assert.ThrowsAsync<IOException>(async () =>
            await coordinator.DisposeAsync());
        Assert.Same(injected, terminal);
    }

    [Fact]
    public async Task MirrorGrantRevocationDuringEmergencyReadinessRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        host.EmergencyStops.CheckingReadiness = () =>
            host.Authorization.CurrentGrant = CapabilityGrant.None;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("mirror_capability_denied", failure.Message);
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationDuringFinalHostFactCheckRejectsBeforeRouteOrPrepare()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Authorization.Reading = () =>
        {
            if (host.Authorization.ReadCount == 3)
            {
                cancellation.Cancel();
            }
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(3, host.Authorization.ReadCount);
        Assert.Equal(
            [
                "protection.read",
                "protection.reserve",
                "emergency_stop.readiness",
                "connection.dispose",
            ],
            host.Timeline);
        Assert.Equal(1, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationDuringFirstHostFactRevalidationRejectsBeforeEmergencyReadiness()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        host.Authorization.Reading = () =>
        {
            if (host.Authorization.ReadCount == 2)
            {
                cancellation.Cancel();
            }
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(2, host.Authorization.ReadCount);
        Assert.Equal(
            ["protection.read", "protection.reserve", "connection.dispose"],
            host.Timeline);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionReadThrowIsRedactedBeforeSafetyOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_PERMISSION_READ_CANARY");
        host.Permissions.SnapshotFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(["connection.dispose"], host.Timeline);
        Assert.Equal(1, host.Permissions.SnapshotReadCount);
        Assert.Equal(0, host.Protection.SnapshotReadCount);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionPreparationThrowIsRedactedBeforeObserverOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_PERMISSION_RESERVE_CANARY");
        host.Permissions.PreparationFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionPreparationCommitThenThrowRetainsCleanupOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException(
            "FLOWSPAN_PERMISSION_COMMITTED_RESERVE_CANARY");
        host.Permissions.PreparationReserved = () => throw injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.False(host.Permissions.CurrentPreparationRegistration?.IsCurrent);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionPreparationCommitThenCallerCancellationRetainsOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Permissions.PreparationReserved = () =>
        {
            cancellation.Cancel();
            throw injected;
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.False(host.Permissions.CurrentPreparationRegistration?.IsCurrent);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionPreparationCommitThenOutOfMemoryRetainsOwner()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PERMISSION_COMMITTED_FATAL_CANARY");
#pragma warning restore CA2201
        host.Permissions.PreparationReserved = () => throw injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.False(host.Permissions.CurrentPreparationRegistration?.IsCurrent);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task GrantedBoundaryWithoutPreparationContractFailsClosed()
    {
        var permissions = new GrantedPermissionBoundaryWithoutPreparation();
        using var host = new ReadyHostHarness(permissionOverride: permissions);
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.Equal(1, permissions.SnapshotReadCount);
        Assert.Equal(0, permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PermissionPreparationOutOfMemoryEscapesUnchangedAndStillCleansUp()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PERMISSION_RESERVE_FATAL_CANARY");
#pragma warning restore CA2201
        host.Permissions.PreparationFailure = injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.DoesNotContain("connection.prepare", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationDuringPermissionPreparationPreservesExactToken()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Permissions.Preparing = cancellation.Cancel;
        host.Permissions.PreparationFailure = injected;

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ForeignPermissionPreparationCancellationIsRedactedWhenCallerCancels()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var callerCancellation = new CancellationTokenSource();
        using var foreignCancellation = new CancellationTokenSource();
        const string canary = "FLOWSPAN_PERMISSION_FOREIGN_CANCEL_CANARY";
        host.Permissions.Preparing = callerCancellation.Cancel;
        host.Permissions.PreparationFailure = new OperationCanceledException(
            canary,
            innerException: null,
            foreignCancellation.Token);

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                callerCancellation.Token));

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(canary, failure.ToString());
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task InitialPermissionRegistrationReadThrowIsRedactedAndOwned()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_PERMISSION_CURRENT_CANARY");
        host.Permissions.PreparationCurrentReading = _ => throw injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task InitialPermissionRegistrationReadPreservesExactCallerCancellation()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Permissions.PreparationCurrentReading = _ =>
        {
            cancellation.Cancel();
            throw injected;
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task InitialPermissionRegistrationForeignCancellationIsRedacted()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var callerCancellation = new CancellationTokenSource();
        using var foreignCancellation = new CancellationTokenSource();
        const string canary = "FLOWSPAN_PERMISSION_CURRENT_FOREIGN_CANARY";
        host.Permissions.PreparationCurrentReading = _ =>
        {
            callerCancellation.Cancel();
            throw new OperationCanceledException(
                canary,
                innerException: null,
                foreignCancellation.Token);
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                callerCancellation.Token));

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(canary, failure.ToString());
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task InitialPermissionRegistrationOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PERMISSION_CURRENT_FATAL_CANARY");
#pragma warning restore CA2201
        host.Permissions.PreparationCurrentReading = _ => throw injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.DoesNotContain("connection.route", host.Timeline);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionPermissionRegistrationReadThrowIsRedactedAndDrained()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException(
            "FLOWSPAN_PERMISSION_PROMOTION_CURRENT_CANARY");
        host.Permissions.PreparationCurrentReading = read => read == 1
            ? true
            : throw injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionPermissionRegistrationForeignCancellationIsRedacted()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var callerCancellation = new CancellationTokenSource();
        using var foreignCancellation = new CancellationTokenSource();
        const string canary = "FLOWSPAN_PERMISSION_PROMOTION_FOREIGN_CANARY";
        host.Permissions.PreparationCurrentReading = read =>
        {
            if (read == 1)
            {
                return true;
            }

            callerCancellation.Cancel();
            throw new OperationCanceledException(
                canary,
                innerException: null,
                foreignCancellation.Token);
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                callerCancellation.Token));

        Assert.Contains("native_permission_unavailable", failure.Message);
        Assert.DoesNotContain(canary, failure.ToString());
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionPermissionRegistrationPreservesExactCallerCancellation()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        using var cancellation = new CancellationTokenSource();
        var injected = new OperationCanceledException(cancellation.Token);
        host.Permissions.PreparationCurrentReading = read =>
        {
            if (read == 1)
            {
                return true;
            }

            cancellation.Cancel();
            throw injected;
        };

        OperationCanceledException failure = await Assert.ThrowsAnyAsync<
            OperationCanceledException>(async () => await coordinator.StartAsync(
                host.CreateRequest(host.Connection, host.Protection),
                cancellation.Token));

        Assert.Same(injected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task PromotionPermissionRegistrationOutOfMemoryEscapesUnchanged()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "FLOWSPAN_PERMISSION_PROMOTION_CURRENT_FATAL_CANARY");
#pragma warning restore CA2201
        host.Permissions.PreparationCurrentReading = read => read == 1
            ? true
            : throw injected;

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await host.StartAsync());

        Assert.Same(injected, failure);
        Assert.Contains("connection.wait_media", host.Timeline);
        Assert.DoesNotContain("capture.start", host.Timeline);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(
            1,
            host.Permissions.CurrentPreparationRegistration?.DisposeCount);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task ConnectionCurrentReadThrowIsRedactedBeforeSafetyOrRoute()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        var injected = new IOException("FLOWSPAN_CONNECTION_READ_CANARY");
        host.Connection.CurrentReadFailure = injected;

        InvalidOperationException failure = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await host.StartAsync());

        Assert.Contains("authenticated_connection_stale", failure.Message);
        Assert.DoesNotContain(injected.Message, failure.ToString());
        Assert.Equal(["connection.dispose"], host.Timeline);
        Assert.Equal(1, host.Connection.CurrentReadCount);
        Assert.Equal(0, host.Permissions.SnapshotReadCount);
        Assert.Equal(0, host.Protection.SnapshotReadCount);
        Assert.Equal(0, host.EmergencyStops.ReadinessCheckCount);
        Assert.Equal(0, host.Capture.StartCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.Null(host.EmergencyStops.CurrentRegistration);
        Assert.Null(coordinator.Snapshot);
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
        var input = new BlockingInputBoundary();
        using var cancellation = new CancellationTokenSource();
        using var host = new ReadyHostHarness(
            role: MirrorParticipantRole.DriverEligible,
            inputOverride: input);
        Task<RemoteWindowParticipantState>? injecting = null;
        Task? stopping = null;
        try
        {
            Assert.True((await host.StartAsync()).Succeeded);
            RemoteWindowSharingSnapshot before = Assert.IsType<
                RemoteWindowSharingSnapshot>(host.Coordinator.Snapshot);
            RemoteWindowParticipantState driver =
                await host.ControlPeer.RequestDriverAsync(
                    RemoteWindowDriverRequest.Create(
                        CorrelationId.From(Guid.NewGuid()),
                        host.ControlPeer.SessionId,
                        before.ActivityId,
                        HostDeviceId,
                        ParticipantDeviceId,
                        Assert.IsType<long>(before.DriverLeaseEpoch),
                        TimeSpan.FromSeconds(5),
                        Now.AddSeconds(2)),
                    CancellationToken.None);
            injecting = host.ControlPeer.SendInputAsync(
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

            stopping = host.Coordinator.StopAsync(cancellation.Token).AsTask();
            await WaitForControlRouteClosedAsync(host.ControlPeer);
            cancellation.Cancel();
            Assert.False(stopping.IsCompleted);

            input.Release.TrySetResult();
            _ = await injecting.WaitAsync(TimeSpan.FromSeconds(5));
            OperationCanceledException stopFailure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Null(host.Coordinator.Snapshot);
            Assert.Equal(1, host.Capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.Equal(1, host.Connection.FailCloseCount);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.Equal(0, host.Permissions.ObserverCount);
            Assert.True(host.Protection.IsDisposed);
            OperationCanceledException disposalFailure =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await host.Coordinator.DisposeAsync());
            Assert.Same(stopFailure, disposalFailure);
        }
        finally
        {
            cancellation.Cancel();
            input.Release.TrySetResult();
            if (injecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await injecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (stopping is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            _ = await Record.ExceptionAsync(async () =>
                await host.Coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));
        }
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
    public async Task PermissionRevocationCleanupFailureRemainsObservableAfterDrain()
    {
        using var host = new ReadyHostHarness();
        var injected = new IOException("injected permission cleanup failure");
        host.Connection.DisposeFailure = injected;
        Assert.True((await host.StartAsync()).Succeeded);
        RemoteWindowMediaSessionBudget budget = Assert.IsType<
            RemoteWindowMediaSessionBudget>(host.Coordinator.ActiveMediaBudget);

        host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        await WaitForTerminalFailureAsync(host.Coordinator);
        Assert.Null(host.Coordinator.Snapshot);
        Assert.Same(injected, host.Coordinator.TerminalFailure);
        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
        Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => host.ControlPeer.SessionId);
        IOException disposalFailure = await Assert.ThrowsAsync<IOException>(
            async () => await host.Coordinator.DisposeAsync());
        Assert.Same(injected, disposalFailure);
    }

    [Fact]
    public async Task ChangedPermissionOwnerInvalidatesTheActiveGeneration()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);

        host.Permissions.Notify(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 2,
                revision: 1));
        Assert.NotNull(coordinator.Snapshot);
        Assert.Equal(0, host.Capture.EmergencyStopCount);
        Assert.Equal(0, host.Connection.FailCloseCount);

        host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 2,
                revision: 1));

        await host.Connection.WaitForDisposeAsync();
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
    }

    [Fact]
    public async Task StalePermissionRevisionCannotStopTheActiveGeneration()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 3));

        host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));

        Assert.NotNull(coordinator.Snapshot);
        Assert.Equal(0, host.Capture.EmergencyStopCount);
        Assert.Equal(0, host.Input.EmergencyStopCount);
        Assert.Equal(0, host.Connection.FailCloseCount);
        Assert.Equal(0, host.Connection.DisposeCount);

        host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 4));

        await host.Connection.WaitForDisposeAsync();
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
    }

    [Fact]
    public async Task CapturedPermissionCallbackFromStoppedGenerationCannotPoisonReplacement()
    {
        using var host = new ReadyHostHarness();
        await using DesktopRemoteWindowHostCoordinator coordinator =
            host.Coordinator;
        Assert.True((await host.StartAsync()).Succeeded);
        Action<NativeRemoteWindowPermissionSnapshot> staleCallback =
            host.Permissions.CaptureObservers();

        Assert.True((await coordinator.StopAsync()).FullyStopped);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        host.Permissions.ReplaceCurrent(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 3));
        var replacementConnection = new RecordingHostConnection(
            host.Timeline,
            HostDeviceId,
            ParticipantDeviceId)
        {
            PrepareResponse = ReadyPreparation,
        };
        RecordingProtectionSource replacementProtection =
            host.CreateProtection();
        Assert.True((await coordinator.StartAsync(host.CreateRequest(
            replacementConnection,
            replacementProtection))).Succeeded);

        staleCallback(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 4));

        Assert.NotNull(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Equal(0, replacementConnection.FailCloseCount);
        Assert.Equal(0, replacementConnection.DisposeCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);

        Assert.True((await coordinator.StopAsync()).FullyStopped);
        Assert.Equal(1, replacementConnection.FailCloseCount);
        Assert.Equal(1, replacementConnection.DisposeCount);
        Assert.True(replacementProtection.IsDisposed);
    }

    [Fact]
    public async Task GenerationCallbackCannotWaitForItsOwnStopOrDisposalDrain()
    {
        using var host = new ReadyHostHarness();
        DesktopRemoteWindowHostCoordinator coordinator = host.Coordinator;
        Exception? stopFailure = null;
        var callbackDisposalReturned = false;
        host.Capture.EmergencyStopping = () =>
        {
            try
            {
                _ = coordinator.StopAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                stopFailure = exception;
            }

            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            callbackDisposalReturned = true;
        };
        Assert.True((await host.StartAsync()).Succeeded);

        host.Permissions.Publish(
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 2));
        await coordinator.DisposeAsync();

        InvalidOperationException rejectedStop = Assert.IsType<
            InvalidOperationException>(stopFailure);
        Assert.Contains("generation callback", rejectedStop.Message);
        Assert.True(callbackDisposalReturned);
        Assert.Null(coordinator.Snapshot);
        Assert.Null(coordinator.TerminalFailure);
        Assert.Equal(1, host.Capture.EmergencyStopCount);
        Assert.Equal(1, host.Input.EmergencyStopCount);
        Assert.Equal(1, host.Connection.FailCloseCount);
        Assert.Equal(1, host.Connection.DisposeCount);
        Assert.Equal(0, host.Permissions.ObserverCount);
        Assert.True(host.Protection.IsDisposed);
    }

    [Fact]
    public async Task TerminalCleanupWatchdogTimeoutReleasesGateAndPermanentlyBlocksRestartUntilTrueDrain()
    {
        TimeSpan cleanupTimeout = TimeSpan.FromSeconds(10);
        var cleanupTimeProvider = new ManualTimeProvider(Now);
        using var host = new ReadyHostHarness(
            cleanupTimeProvider: cleanupTimeProvider,
            cleanupConfirmationTimeout: cleanupTimeout);
        host.Connection.BlockDisposal = true;
        Task<RemoteWindowCommandResult>? firstRestart = null;
        try
        {
            Assert.True((await host.StartAsync()).Succeeded);
            RemoteWindowMediaSessionBudget budget = Assert.IsType<
                RemoteWindowMediaSessionBudget>(
                    host.Coordinator.ActiveMediaBudget);
            host.Capture.EmitFrame(sequence: 2);
            await host.Connection.WaitForMediaFrameCountAsync(1);
            int admittedRouteCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "connection.route"));
            int admittedPrepareCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "connection.prepare"));
            int admittedPublishCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "connection.publish"));
            int admittedEmergencyRegistrationCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "emergency_stop.register"));
            int admittedCaptureCount = host.Capture.StartCount;
            int admittedInputCount = host.Input.InjectCount;
            int authorizationReservationCount =
                host.Authorization.ReservationCount;
            int permissionReservationCount =
                host.Permissions.PreparationReservationCount;
            int emergencyReadinessCount =
                host.EmergencyStops.ReadinessReservationCount;

            int revocationThreadId = Environment.CurrentManagedThreadId;
            host.Connection.Revoke();

            Assert.Null(host.Coordinator.Snapshot);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(
                revocationThreadId,
                cleanupTimeProvider.TimerCreateThreadId);
            await host.Connection.WaitForDisposeEnteredAsync();
            await WaitForCoordinatorInactiveAsync(host.Coordinator);
            await WaitForControlRouteClosedAsync(host.ControlPeer);
            Assert.Null(host.Coordinator.Snapshot);
            Assert.Null(host.Coordinator.TerminalFailure);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.False(host.Connection.DisposalCompleted);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            host.Capture.EmitFrame(sequence: 3);
            Assert.Single(host.Connection.MediaFrames);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);

            var firstReplacementConnection = new RecordingHostConnection(
                host.Timeline,
                HostDeviceId,
                ParticipantDeviceId)
            {
                PrepareResponse = ReadyPreparation,
            };
            RecordingProtectionSource firstReplacementProtection =
                host.CreateProtection();
            firstRestart = host.Coordinator
                .StartAsync(host.CreateRequest(
                    firstReplacementConnection,
                    firstReplacementProtection))
                .AsTask();

            cleanupTimeProvider.Advance(
                cleanupTimeout - TimeSpan.FromTicks(1));
            Assert.Null(host.Coordinator.TerminalFailure);
            Assert.False(firstRestart.IsCompleted);
            Assert.False(host.Connection.DisposalCompleted);

            cleanupTimeProvider.Advance(TimeSpan.FromTicks(1));
            await WaitForTerminalFailureAsync(host.Coordinator);
            Exception timeoutFailure = Assert.IsAssignableFrom<Exception>(
                host.Coordinator.TerminalFailure);
            Assert.Contains("host_cleanup_timeout", timeoutFailure.Message);
            Assert.Equal(Now.Add(cleanupTimeout), cleanupTimeProvider.GetUtcNow());
            Assert.False(host.Connection.DisposalCompleted);

            InvalidOperationException firstRestartFailure = await Assert
                .ThrowsAsync<InvalidOperationException>(async () =>
                    await firstRestart.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains(
                "host_cleanup_unconfirmed",
                firstRestartFailure.Message);
            Assert.Equal(admittedCaptureCount, host.Capture.StartCount);
            Assert.Equal(
                admittedRouteCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.route")));
            Assert.Equal(
                admittedPrepareCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.prepare")));
            Assert.Equal(
                admittedPublishCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.publish")));
            Assert.Equal(
                admittedEmergencyRegistrationCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "emergency_stop.register")));
            Assert.Equal(0, firstReplacementConnection.FailCloseCount);
            Assert.Equal(1, firstReplacementConnection.DisposeCount);
            Assert.Equal(
                0,
                firstReplacementConnection.ConnectionPreparationReservationCount);
            Assert.Empty(firstReplacementConnection.MediaFrames);
            Assert.Equal(
                0,
                firstReplacementProtection.PreparationReservationCount);
            Assert.Equal(
                authorizationReservationCount,
                host.Authorization.ReservationCount);
            Assert.Equal(
                permissionReservationCount,
                host.Permissions.PreparationReservationCount);
            Assert.Equal(
                emergencyReadinessCount,
                host.EmergencyStops.ReadinessReservationCount);
            Assert.Equal(admittedInputCount, host.Input.InjectCount);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.True(firstReplacementProtection.IsDisposed);
            Assert.Same(timeoutFailure, host.Coordinator.TerminalFailure);

            host.Connection.ReleaseDisposal();
            await host.Connection.WaitForDisposeAsync();
            await WaitForRetiringCleanupAsync(host.Coordinator);
            await WaitForControlRouteClosedAsync(host.ControlPeer);
            Assert.True(host.Connection.DisposalCompleted);
            Assert.False(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(0, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(1, host.Connection.FailCloseCount);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.Equal(0, host.Permissions.ObserverCount);
            Assert.True(host.Protection.IsDisposed);
            Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
            Assert.Null(host.Coordinator.Snapshot);

            var secondReplacementConnection = new RecordingHostConnection(
                host.Timeline,
                HostDeviceId,
                ParticipantDeviceId)
            {
                PrepareResponse = ReadyPreparation,
            };
            RecordingProtectionSource secondReplacementProtection =
                host.CreateProtection();
            InvalidOperationException secondRestartFailure = await Assert
                .ThrowsAsync<InvalidOperationException>(async () =>
                    await host.Coordinator.StartAsync(host.CreateRequest(
                            secondReplacementConnection,
                            secondReplacementProtection))
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains(
                "host_cleanup_unconfirmed",
                secondRestartFailure.Message);
            Assert.Equal(admittedCaptureCount, host.Capture.StartCount);
            Assert.Equal(
                admittedRouteCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.route")));
            Assert.Equal(
                admittedPrepareCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.prepare")));
            Assert.Equal(
                admittedPublishCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.publish")));
            Assert.Equal(
                admittedEmergencyRegistrationCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "emergency_stop.register")));
            Assert.Equal(0, secondReplacementConnection.FailCloseCount);
            Assert.Equal(1, secondReplacementConnection.DisposeCount);
            Assert.Equal(
                0,
                secondReplacementConnection.ConnectionPreparationReservationCount);
            Assert.Empty(secondReplacementConnection.MediaFrames);
            Assert.Equal(
                0,
                secondReplacementProtection.PreparationReservationCount);
            Assert.Equal(
                authorizationReservationCount,
                host.Authorization.ReservationCount);
            Assert.Equal(
                permissionReservationCount,
                host.Permissions.PreparationReservationCount);
            Assert.Equal(
                emergencyReadinessCount,
                host.EmergencyStops.ReadinessReservationCount);
            Assert.Equal(admittedInputCount, host.Input.InjectCount);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.True(secondReplacementProtection.IsDisposed);
            Assert.Same(timeoutFailure, host.Coordinator.TerminalFailure);
        }
        finally
        {
            if (firstRestart is { IsCompleted: false })
            {
                cleanupTimeProvider.Advance(
                    DesktopRemoteWindowHostCoordinator
                        .MaximumCleanupConfirmationTimeout);
            }

            host.Connection.ReleaseDisposal();
            if (firstRestart is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await firstRestart.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            Exception? coordinatorDisposalFailure = await Record.ExceptionAsync(
                async () => await host.Coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));
            Exception? connectionDrainFailure = host.Connection.DisposeCount == 0
                ? null
                : await Record.ExceptionAsync(
                    host.Connection.WaitForDisposeAsync);
            Exception? retiringDrainFailure = await Record.ExceptionAsync(
                async () => await WaitForRetiringCleanupAsync(host.Coordinator));

            Assert.Null(connectionDrainFailure);
            Assert.Null(retiringDrainFailure);
            if (host.Coordinator.TerminalFailure is { } terminalFailure)
            {
                Assert.Same(terminalFailure, coordinatorDisposalFailure);
            }
            else
            {
                Assert.Null(coordinatorDisposalFailure);
            }
        }
    }

    [Fact]
    public async Task DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain()
    {
        TimeSpan cleanupTimeout = TimeSpan.FromSeconds(10);
        var cleanupTimeProvider = new ManualTimeProvider(Now);
        using var host = new ReadyHostHarness(
            cleanupTimeProvider: cleanupTimeProvider,
            cleanupConfirmationTimeout: cleanupTimeout);
        host.Connection.BlockDisposal = true;
        Task? firstDisposal = null;
        Task? revocationTask = null;
        var disposalReturned = 0;
        var disposalReturnedBeforeTimer = -1;
        var retiringPublishedBeforeRevocation = false;
        var retiringAuthorityClosedBeforeRevocation = false;
        var controlRetainedBeforeRevocation = true;
        var timerHookCount = 0;
        TaskCompletionSource emergencyStopEntered = NewCompletion();
        host.Capture.EmergencyStopping = () => emergencyStopEntered.TrySetResult();

        try
        {
            Assert.True((await host.StartAsync()).Succeeded);
            RemoteWindowMediaSessionBudget budget = Assert.IsType<
                RemoteWindowMediaSessionBudget>(
                    host.Coordinator.ActiveMediaBudget);
            host.Capture.EmitFrame(sequence: 2);
            await host.Connection.WaitForMediaFrameCountAsync(1);
            int admittedRouteCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "connection.route"));
            int admittedPrepareCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "connection.prepare"));
            int admittedPublishCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "connection.publish"));
            int admittedCaptureCount = host.Capture.StartCount;
            int admittedInputCount = host.Input.InjectCount;
            int authorizationReservationCount =
                host.Authorization.ReservationCount;
            int permissionReservationCount =
                host.Permissions.PreparationReservationCount;
            int emergencyReadinessCount =
                host.EmergencyStops.ReadinessReservationCount;
            int emergencyRegistrationCount = host.Timeline.Count(entry =>
                StringComparer.Ordinal.Equals(entry, "emergency_stop.register"));
            cleanupTimeProvider.TimerCreated = () =>
            {
                Volatile.Write(
                    ref disposalReturnedBeforeTimer,
                    Volatile.Read(ref disposalReturned));
                retiringPublishedBeforeRevocation =
                    host.Coordinator.Snapshot is null
                    && host.Coordinator.ActiveMediaBudget is null
                    && host.Coordinator.HasRetiringGeneration;
                retiringAuthorityClosedBeforeRevocation =
                    host.Coordinator.IsRetiringAuthorityClosed;
                controlRetainedBeforeRevocation =
                    host.ControlPeer.HasRetainedGeneration;
                Interlocked.Increment(ref timerHookCount);
                Action revocation = host.Connection.CaptureRevocationCallback()
                    ?? throw new InvalidOperationException(
                        "The active host connection had no revocation callback.");
                revocationTask = Task.Run(revocation);
                emergencyStopEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .GetAwaiter()
                    .GetResult();
            };

            firstDisposal = host.Coordinator.DisposeAsync().AsTask();
            Volatile.Write(ref disposalReturned, 1);
            Task concurrentDisposal = host.Coordinator.DisposeAsync().AsTask();

            Assert.Same(firstDisposal, concurrentDisposal);
            Assert.Equal(0, Volatile.Read(ref disposalReturnedBeforeTimer));
            Assert.True(retiringPublishedBeforeRevocation);
            Assert.True(retiringAuthorityClosedBeforeRevocation);
            Assert.False(controlRetainedBeforeRevocation);
            Assert.Null(host.Coordinator.Snapshot);
            Assert.Null(host.Coordinator.ActiveMediaBudget);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(1, Volatile.Read(ref timerHookCount));
            await Assert.IsType<Task>(revocationTask).WaitAsync(
                TimeSpan.FromSeconds(5));
            Assert.Equal(1, host.Coordinator.TerminalCleanupAttachmentCount);

            ObjectDisposedException pendingStartFailure = await Assert.ThrowsAsync<
                ObjectDisposedException>(async () =>
                    await host.StartAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.NotNull(pendingStartFailure.ObjectName);
            await host.Connection.WaitForDisposeEnteredAsync();
            await WaitForControlRouteClosedAsync(host.ControlPeer);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.False(host.Connection.DisposalCompleted);
            Assert.False(firstDisposal.IsCompleted);
            Assert.Null(host.Coordinator.TerminalFailure);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
            Assert.Equal(1, host.Capture.EmergencyStopCount);
            Assert.Equal(1, host.Input.EmergencyStopCount);
            Assert.Equal(1, host.Capture.StopCount);
            Assert.Equal(1, host.Input.StopCount);
            Assert.Equal(1, host.Connection.FailCloseCount);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.Equal(0, host.Permissions.ObserverCount);
            Assert.True(host.Protection.IsDisposed);
            Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
            host.Capture.EmitFrame(sequence: 3);
            Assert.Single(host.Connection.MediaFrames);
            Assert.Equal(admittedCaptureCount, host.Capture.StartCount);
            Assert.Equal(admittedInputCount, host.Input.InjectCount);
            Assert.Equal(
                authorizationReservationCount,
                host.Authorization.ReservationCount);
            Assert.Equal(
                permissionReservationCount,
                host.Permissions.PreparationReservationCount);
            Assert.Equal(
                emergencyReadinessCount,
                host.EmergencyStops.ReadinessReservationCount);
            Assert.Equal(
                emergencyRegistrationCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "emergency_stop.register")));

            cleanupTimeProvider.Advance(
                cleanupTimeout - TimeSpan.FromTicks(1));

            Assert.False(firstDisposal.IsCompleted);
            Assert.False(concurrentDisposal.IsCompleted);
            Assert.Null(host.Coordinator.TerminalFailure);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.False(host.Connection.DisposalCompleted);

            cleanupTimeProvider.Advance(TimeSpan.FromTicks(1));

            InvalidOperationException firstFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await firstDisposal.WaitAsync(TimeSpan.FromSeconds(5)));
            InvalidOperationException concurrentFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await concurrentDisposal.WaitAsync(TimeSpan.FromSeconds(5)));
            Task laterDisposal = host.Coordinator.DisposeAsync().AsTask();
            InvalidOperationException laterFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await laterDisposal.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(firstDisposal, laterDisposal);
            Assert.Same(firstFailure, concurrentFailure);
            Assert.Same(firstFailure, laterFailure);
            Assert.Same(firstFailure, host.Coordinator.TerminalFailure);
            Assert.Equal(
                "Remote Window host cleanup confirmation failed "
                    + "(host_cleanup_timeout).",
                firstFailure.Message);
            Assert.Equal(Now.Add(cleanupTimeout), cleanupTimeProvider.GetUtcNow());
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.False(host.Connection.DisposalCompleted);

            host.Connection.ReleaseDisposal();
            await host.Connection.WaitForDisposeAsync();
            await WaitForRetiringCleanupAsync(host.Coordinator);

            Assert.False(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(0, cleanupTimeProvider.ActiveTimerCount);
            Assert.True(host.Connection.DisposalCompleted);
            Assert.Equal(1, host.Connection.FailCloseCount);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.Equal(0, host.Permissions.ObserverCount);
            Assert.True(host.Protection.IsDisposed);
            Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
            Assert.Same(firstFailure, host.Coordinator.TerminalFailure);

            Task postDrainDisposal = host.Coordinator.DisposeAsync().AsTask();
            InvalidOperationException postDrainFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await postDrainDisposal.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(firstDisposal, postDrainDisposal);
            Assert.Same(firstFailure, postDrainFailure);

            ObjectDisposedException startFailure = await Assert.ThrowsAsync<
                ObjectDisposedException>(async () => await host.StartAsync());

            Assert.NotNull(startFailure.ObjectName);
            Assert.Equal(
                admittedRouteCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.route")));
            Assert.Equal(
                admittedPrepareCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.prepare")));
            Assert.Equal(
                admittedPublishCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "connection.publish")));
            Assert.Equal(admittedCaptureCount, host.Capture.StartCount);
            Assert.Equal(admittedInputCount, host.Input.InjectCount);
            Assert.Equal(
                authorizationReservationCount,
                host.Authorization.ReservationCount);
            Assert.Equal(
                permissionReservationCount,
                host.Permissions.PreparationReservationCount);
            Assert.Equal(
                emergencyReadinessCount,
                host.EmergencyStops.ReadinessReservationCount);
            Assert.Equal(
                emergencyRegistrationCount,
                host.Timeline.Count(entry => StringComparer.Ordinal.Equals(
                    entry,
                    "emergency_stop.register")));
            Assert.Single(host.Connection.MediaFrames);
        }
        finally
        {
            if (firstDisposal is { IsCompleted: false }
                && cleanupTimeProvider.TimerCreateCount > 0)
            {
                cleanupTimeProvider.Advance(
                    DesktopRemoteWindowHostCoordinator
                        .MaximumCleanupConfirmationTimeout);
            }

            host.Connection.ReleaseDisposal();
            firstDisposal ??= host.Coordinator.DisposeAsync().AsTask();
            if (firstDisposal is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await firstDisposal.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (revocationTask is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await revocationTask.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (host.Connection.DisposeCount > 0)
            {
                _ = await Record.ExceptionAsync(host.Connection.WaitForDisposeAsync);
            }

            _ = await Record.ExceptionAsync(async () =>
                await WaitForRetiringCleanupAsync(host.Coordinator));
        }
    }

    [Fact]
    public async Task StopFirstCallerCancellationRunsOneFallbackAndPreservesTheExactToken()
    {
        TimeSpan cleanupTimeout = TimeSpan.FromSeconds(10);
        var cleanupTimeProvider = new ManualTimeProvider(Now);
        var input = new BlockingInputBoundary();
        using var callerCancellation = new CancellationTokenSource();
        using var host = new ReadyHostHarness(
            role: MirrorParticipantRole.DriverEligible,
            cleanupTimeProvider: cleanupTimeProvider,
            cleanupConfirmationTimeout: cleanupTimeout,
            inputOverride: input);
        Task<RemoteWindowParticipantState>? injecting = null;
        Task<RemoteWindowStopResult>? stopping = null;
        ControllerStopPublicationSnapshot? publication = null;
        var attemptsWhenTimerCreated = -1;
        try
        {
            Assert.True((await host.StartAsync()).Succeeded);
            RemoteWindowMediaSessionBudget budget = Assert.IsType<
                RemoteWindowMediaSessionBudget>(
                    host.Coordinator.ActiveMediaBudget);
            host.Capture.EmitFrame(sequence: 2);
            await host.Connection.WaitForMediaFrameCountAsync(1);
            RemoteWindowSharingSnapshot before = Assert.IsType<
                RemoteWindowSharingSnapshot>(host.Coordinator.Snapshot);
            RemoteWindowParticipantState driver =
                await host.ControlPeer.RequestDriverAsync(
                    RemoteWindowDriverRequest.Create(
                        CorrelationId.From(Guid.NewGuid()),
                        host.ControlPeer.SessionId,
                        before.ActivityId,
                        HostDeviceId,
                        ParticipantDeviceId,
                        Assert.IsType<long>(before.DriverLeaseEpoch),
                        TimeSpan.FromSeconds(5),
                        Now.AddSeconds(2)),
                    CancellationToken.None);
            injecting = host.ControlPeer.SendInputAsync(
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
            cleanupTimeProvider.TimerCreated = () =>
            {
                Volatile.Write(
                    ref attemptsWhenTimerCreated,
                    host.ControllerStops.AttemptCount);
                publication = new(
                    host.Coordinator.HasRetiringGeneration,
                    host.Coordinator.IsRetiringAuthorityClosed,
                    host.Coordinator.Snapshot is null,
                    host.Coordinator.ActiveMediaBudget is null,
                    cleanupTimeProvider.TimerCreateCount,
                    cleanupTimeProvider.ActiveTimerCount);
            };

            stopping = host.Coordinator.StopAsync(callerCancellation.Token)
                .AsTask();
            await host.ControllerStops.WaitForAttemptCountAsync(1);

            Assert.False(stopping.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref attemptsWhenTimerCreated));
            ControllerStopPublicationSnapshot observedPublication = Assert.IsType<
                ControllerStopPublicationSnapshot>(publication);
            Assert.True(observedPublication.HasRetiringGeneration);
            Assert.True(observedPublication.IsAuthorityClosed);
            Assert.True(observedPublication.SnapshotIsNull);
            Assert.True(observedPublication.ActiveMediaBudgetIsNull);
            Assert.Equal(1, observedPublication.TimerCreateCount);
            Assert.Equal(1, observedPublication.ActiveTimerCount);
            Assert.Equal(1, host.ControllerStops.AttemptCount);
            Assert.Equal(
                callerCancellation.Token,
                Assert.Single(host.ControllerStops.Tokens));
            Assert.Null(host.Coordinator.Snapshot);
            Assert.Null(host.Coordinator.ActiveMediaBudget);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.True(host.Coordinator.IsRetiringAuthorityClosed);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.True(host.ControlPeer.HasRetainedGeneration);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
            host.Capture.EmitFrame(sequence: 3);
            Assert.Single(host.Connection.MediaFrames);

            callerCancellation.Cancel();
            await host.ControllerStops.WaitForAttemptCountAsync(2);
            CancellationToken[] attempts =
                host.ControllerStops.Tokens;
            Assert.Equal(2, attempts.Length);
            Assert.Equal(callerCancellation.Token, attempts[0]);
            Assert.Equal(CancellationToken.None, attempts[1]);
            cleanupTimeProvider.Advance(
                cleanupTimeout - TimeSpan.FromTicks(1));

            Assert.False(stopping.IsCompleted);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(0, host.Capture.StopCount);
            Assert.Equal(0, input.StopCount);
            Assert.Equal(0, host.Connection.DisposeCount);

            input.Release.TrySetResult();
            _ = await injecting.WaitAsync(TimeSpan.FromSeconds(5));
            OperationCanceledException firstFailure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));
            OperationCanceledException repeatedFailure =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () =>
                        await stopping.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(firstFailure, repeatedFailure);
            Assert.Equal(callerCancellation.Token, firstFailure.CancellationToken);
            Assert.Same(firstFailure, host.Coordinator.TerminalFailure);
            await host.Connection.WaitForDisposeAsync();
            await WaitForRetiringCleanupAsync(host.Coordinator);
            Assert.False(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(0, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(
                Now.Add(cleanupTimeout).AddTicks(-1),
                cleanupTimeProvider.GetUtcNow());
            Assert.Equal(1, host.Capture.StopCount);
            Assert.Equal(1, input.StopCount);
            Assert.Equal(1, host.Connection.FailCloseCount);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.Equal(0, host.Permissions.ObserverCount);
            Assert.True(host.Protection.IsDisposed);
            Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.Equal(
                1,
                Assert.IsType<RecordingAuthorizationRegistration>(
                    host.Authorization.CurrentReservation).DisposeCount);
            Assert.Equal(
                1,
                Assert.IsType<RecordingPermissionBoundary
                    .RecordingPermissionPreparationRegistration>(
                        host.Permissions.CurrentPreparationRegistration)
                    .DisposeCount);
            Assert.Equal(
                1,
                Assert.IsType<RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                        host.Protection.CurrentPreparation).DisposeCount);
            Assert.Equal(
                1,
                Assert.IsType<RecordingHostConnection
                    .RecordingConnectionPreparationRegistration>(
                        host.Connection.CurrentConnectionPreparation)
                    .DisposeCount);
            Assert.Equal(
                0,
                host.EmergencyStops.ReadinessReservationDisposeCount);
            Assert.Equal(2, host.ControllerStops.Tokens.Length);

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
                InvalidOperationException>(async () =>
                    await host.Coordinator.StartAsync(host.CreateRequest(
                            replacementConnection,
                            replacementProtection))
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains("host_cleanup_unconfirmed", restartFailure.Message);
            Assert.Equal(1, replacementConnection.DisposeCount);
            Assert.True(replacementProtection.IsDisposed);
            OperationCanceledException disposalFailure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () =>
                    await host.Coordinator.DisposeAsync());
            Assert.Same(firstFailure, disposalFailure);
        }
        finally
        {
            callerCancellation.Cancel();
            input.Release.TrySetResult();
            if (stopping is { IsCompleted: false }
                && cleanupTimeProvider.TimerCreateCount > 0)
            {
                cleanupTimeProvider.Advance(
                    DesktopRemoteWindowHostCoordinator
                        .MaximumCleanupConfirmationTimeout);
            }

            if (injecting is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await injecting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (stopping is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (host.Connection.DisposeCount > 0)
            {
                _ = await Record.ExceptionAsync(host.Connection.WaitForDisposeAsync);
            }

            _ = await Record.ExceptionAsync(async () =>
                await host.Coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task StopFirstCleanupTimeoutIsStableWhileControllerStopBlocksAndAfterLateDrain()
    {
        TimeSpan cleanupTimeout = TimeSpan.FromSeconds(10);
        var cleanupTimeProvider = new ManualTimeProvider(Now);
        using var host = new ReadyHostHarness(
            cleanupTimeProvider: cleanupTimeProvider,
            cleanupConfirmationTimeout: cleanupTimeout);
        host.Capture.BlockStop = true;
        Task<RemoteWindowStopResult>? stopping = null;
        ControllerStopPublicationSnapshot? publication = null;
        var attemptsWhenTimerCreated = -1;
        try
        {
            Assert.True((await host.StartAsync()).Succeeded);
            RemoteWindowMediaSessionBudget budget = Assert.IsType<
                RemoteWindowMediaSessionBudget>(
                    host.Coordinator.ActiveMediaBudget);
            host.Capture.EmitFrame(sequence: 2);
            await host.Connection.WaitForMediaFrameCountAsync(1);
            cleanupTimeProvider.TimerCreated = () =>
            {
                Volatile.Write(
                    ref attemptsWhenTimerCreated,
                    host.ControllerStops.AttemptCount);
                publication = new(
                    host.Coordinator.HasRetiringGeneration,
                    host.Coordinator.IsRetiringAuthorityClosed,
                    host.Coordinator.Snapshot is null,
                    host.Coordinator.ActiveMediaBudget is null,
                    cleanupTimeProvider.TimerCreateCount,
                    cleanupTimeProvider.ActiveTimerCount);
            };

            stopping = Task.Run(async () =>
                await host.Coordinator.StopAsync());
            await host.ControllerStops.WaitForAttemptCountAsync(1);
            await host.Capture.WaitForStopEnteredAsync();

            Assert.False(stopping.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref attemptsWhenTimerCreated));
            ControllerStopPublicationSnapshot observedPublication = Assert.IsType<
                ControllerStopPublicationSnapshot>(publication);
            Assert.True(observedPublication.HasRetiringGeneration);
            Assert.True(observedPublication.IsAuthorityClosed);
            Assert.True(observedPublication.SnapshotIsNull);
            Assert.True(observedPublication.ActiveMediaBudgetIsNull);
            Assert.Equal(1, observedPublication.TimerCreateCount);
            Assert.Equal(1, observedPublication.ActiveTimerCount);
            Assert.Equal(1, host.ControllerStops.AttemptCount);
            Assert.Equal(
                CancellationToken.None,
                Assert.Single(host.ControllerStops.Tokens));
            Assert.Null(host.Coordinator.Snapshot);
            Assert.Null(host.Coordinator.ActiveMediaBudget);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.True(host.Coordinator.IsRetiringAuthorityClosed);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
            host.Capture.EmitFrame(sequence: 3);
            Assert.Single(host.Connection.MediaFrames);

            cleanupTimeProvider.Advance(
                cleanupTimeout - TimeSpan.FromTicks(1));

            Assert.False(stopping.IsCompleted);
            Assert.Null(host.Coordinator.TerminalFailure);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(Now.Add(cleanupTimeout).AddTicks(-1),
                cleanupTimeProvider.GetUtcNow());

            cleanupTimeProvider.Advance(TimeSpan.FromTicks(1));

            InvalidOperationException firstFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Equal(
                "Remote Window host cleanup confirmation failed "
                    + "(host_cleanup_timeout).",
                firstFailure.Message);
            Assert.Same(firstFailure, host.Coordinator.TerminalFailure);
            Assert.True(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(1, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(1, host.Capture.StopCount);
            Assert.Equal(0, host.Input.StopCount);
            Assert.Equal(0, host.Connection.DisposeCount);

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
                InvalidOperationException>(async () =>
                    await host.Coordinator.StartAsync(host.CreateRequest(
                            replacementConnection,
                            replacementProtection))
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains("host_cleanup_unconfirmed", restartFailure.Message);
            Assert.Equal(1, replacementConnection.DisposeCount);
            Assert.True(replacementProtection.IsDisposed);

            host.Capture.ReleaseStop();
            await host.Connection.WaitForDisposeAsync();
            await WaitForRetiringCleanupAsync(host.Coordinator);

            InvalidOperationException repeatedFailure = await Assert.ThrowsAsync<
                InvalidOperationException>(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(firstFailure, repeatedFailure);
            Assert.Same(firstFailure, host.Coordinator.TerminalFailure);
            Assert.False(host.Coordinator.HasRetiringGeneration);
            Assert.Equal(1, cleanupTimeProvider.TimerCreateCount);
            Assert.Equal(0, cleanupTimeProvider.ActiveTimerCount);
            Assert.Equal(1, host.Capture.StopCount);
            Assert.Equal(1, host.Input.StopCount);
            Assert.Equal(1, host.Connection.FailCloseCount);
            Assert.Equal(1, host.Connection.DisposeCount);
            Assert.Equal(0, host.Permissions.ObserverCount);
            Assert.True(host.Protection.IsDisposed);
            Assert.False(host.EmergencyStops.CurrentRegistration?.IsCurrent);
            Assert.False(host.ControlPeer.HasRetainedGeneration);
            Assert.Equal(
                1,
                Assert.IsType<RecordingAuthorizationRegistration>(
                    host.Authorization.CurrentReservation).DisposeCount);
            Assert.Equal(
                1,
                Assert.IsType<RecordingPermissionBoundary
                    .RecordingPermissionPreparationRegistration>(
                        host.Permissions.CurrentPreparationRegistration)
                    .DisposeCount);
            Assert.Equal(
                1,
                Assert.IsType<RecordingProtectionSource
                    .RecordingProtectionPreparationRegistration>(
                        host.Protection.CurrentPreparation).DisposeCount);
            Assert.Equal(
                1,
                Assert.IsType<RecordingHostConnection
                    .RecordingConnectionPreparationRegistration>(
                        host.Connection.CurrentConnectionPreparation)
                    .DisposeCount);
            Assert.Equal(
                0,
                host.EmergencyStops.ReadinessReservationDisposeCount);
            Assert.Single(host.ControllerStops.Tokens);
        }
        finally
        {
            host.Capture.ReleaseStop();
            if (stopping is { IsCompleted: false }
                && cleanupTimeProvider.TimerCreateCount > 0)
            {
                cleanupTimeProvider.Advance(
                    DesktopRemoteWindowHostCoordinator
                        .MaximumCleanupConfirmationTimeout);
            }

            if (stopping is not null)
            {
                _ = await Record.ExceptionAsync(async () =>
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            if (host.Connection.DisposeCount > 0)
            {
                _ = await Record.ExceptionAsync(host.Connection.WaitForDisposeAsync);
            }

            _ = await Record.ExceptionAsync(async () =>
                await host.Coordinator.DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)));
        }
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
        Assert.Equal(1, emergencyStops.ReadinessReservationCount);
        Assert.Equal(1, emergencyStops.ReadinessReservationDisposeCount);
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

    private static ProtectionSnapshot UnsafeAt(DateTimeOffset observedAt) => new(
        ProtectionKind.SecureInput,
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
            for (int index = previous + 1; index < timeline.Count; index++)
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

    private static async Task WaitForCoordinatorInactiveAsync(
        DesktopRemoteWindowHostCoordinator coordinator)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (coordinator.Snapshot is not null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }
    }

    private static async Task WaitForRetiringCleanupAsync(
        DesktopRemoteWindowHostCoordinator coordinator)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (coordinator.HasRetiringGeneration)
        {
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

        public ReadyHostHarness(
            IClock? clock = null,
            INativeRemoteWindowPermissionBoundary? permissionOverride = null,
            MirrorParticipantRole role = MirrorParticipantRole.ViewOnly,
            TimeProvider? cleanupTimeProvider = null,
            TimeSpan? cleanupConfirmationTimeout = null,
            INativeRemoteInputBoundary? inputOverride = null)
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
            ControllerStops = new RecordingControllerStopBoundary();
            Connection = new RecordingHostConnection(
                Timeline,
                HostDeviceId,
                ParticipantDeviceId)
            {
                PrepareResponse = ReadyPreparation,
            };
            Authorization = new FixedAuthorizationSource(role ==
                    MirrorParticipantRole.DriverEligible
                ? CapabilityGrant.Of(
                    Capability.MirrorView,
                    Capability.MirrorDrive)
                : CapabilityGrant.Of(Capability.MirrorView));
            Role = role;
            Coordinator = new DesktopRemoteWindowHostCoordinator(
                clock ?? new FixedClock(Now),
                permissionOverride ?? Permissions,
                Authorization,
                Capture,
                inputOverride ?? Input,
                new RecordingSharingSessionBoundary(),
                EmergencyStops,
                ControlPeer,
                ownerLeaseDuration: TimeSpan.FromSeconds(10),
                preparationLifetime: TimeSpan.FromSeconds(5),
                cleanupTimeProvider,
                cleanupConfirmationTimeout,
                ControllerStops);
        }

        public RecordingCaptureBoundary Capture { get; }

        public FixedAuthorizationSource Authorization { get; }

        public RecordingHostConnection Connection { get; }

        public DesktopRemoteWindowHostControlPeer ControlPeer { get; }

        public RecordingControllerStopBoundary ControllerStops { get; }

        public DesktopRemoteWindowHostCoordinator Coordinator { get; }

        public RecordingEmergencyStopRegistrar EmergencyStops { get; }

        public ConfirmingInputBoundary Input { get; }

        public RecordingPermissionBoundary Permissions { get; }

        public RecordingProtectionSource Protection { get; }

        public MirrorParticipantRole Role { get; }

        public List<string> Timeline { get; } = [];

        public DesktopRemoteWindowHostStartRequest CreateRequest(
            RecordingHostConnection connection,
            RecordingProtectionSource protection) => new(
                sourceLease,
                ownerGeneration: 1,
                connection,
                protection,
                Role);

        public RecordingProtectionSource CreateProtection(
            ProtectionSnapshot? protection = null) => new(
            Timeline,
            NativeRemoteWindowProtectionObservation.Create(
                protection ?? SafeAt(Now),
                ownerGeneration: 1,
                sessionGeneration: 1,
                registration.Source.SourceGeneration,
                revision: 1));

        public ValueTask<RemoteWindowCommandResult> StartAsync() =>
            Coordinator.StartAsync(CreateRequest(Connection, Protection));

        public void InvalidateSource() => registration.Dispose();

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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object gate = new();
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

        public Action? TimerCreated { get; set; }

        public int? TimerCreateThreadId
        {
            get
            {
                int threadId = Volatile.Read(ref timerCreateThreadId);
                return threadId == 0 ? null : threadId;
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates)
            {
                timer.FireIfDue(now);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(this, callback, state);
            if (!timer.Change(dueTime, period))
            {
                throw new InvalidOperationException(
                    "The manual cleanup timer could not be armed.");
            }

            lock (gate)
            {
                timers.Add(timer);
                _ = Interlocked.CompareExchange(
                    ref timerCreateThreadId,
                    Environment.CurrentManagedThreadId,
                    comparand: 0);
                Interlocked.Increment(ref timerCreateCount);
            }

            Action? timerCreated = TimerCreated;
            TimerCreated = null;
            timerCreated?.Invoke();

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
                    if (disposed)
                    {
                        return;
                    }

                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(DateTimeOffset now)
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
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class BlockingClock(DateTimeOffset utcNow) :
        IClock,
        IDisposable
    {
        private readonly ManualResetEventSlim release = new(false);
        private Action? nextReading;
        private Exception? nextFailure;
        private int blockNextRead;

        public ManualResetEventSlim Blocked { get; } = new(false);

        public DateTimeOffset UtcNow
        {
            get
            {
                Interlocked.Exchange(ref nextReading, null)?.Invoke();
                Exception? failure = Interlocked.Exchange(
                    ref nextFailure,
                    null);
                if (failure is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(failure)
                        .Throw();
                }

                if (Interlocked.Exchange(ref blockNextRead, 0) != 0)
                {
                    Blocked.Set();
                    release.Wait();
                }

                return utcNow;
            }
        }

        public void BlockNextRead()
        {
            Blocked.Reset();
            release.Reset();
            Volatile.Write(ref blockNextRead, 1);
        }

        public void FailNext(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            _ = Interlocked.Exchange(ref nextFailure, failure);
        }

        public void RunOnNextRead(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (Interlocked.CompareExchange(
                    ref nextReading,
                    callback,
                    null) is not null)
            {
                throw new InvalidOperationException(
                    "A test clock read callback is already pending.");
            }
        }

        public void Release() => release.Set();

        public void Dispose()
        {
            release.Set();
            release.Dispose();
            Blocked.Dispose();
        }
    }

    private sealed class FixedAuthorizationSource(CapabilityGrant grant) :
        IDesktopRemoteWindowHostAuthorizationSource
    {
        public string? AuthenticatedFingerprint { get; private set; }

        public CapabilityGrant CurrentGrant { get; set; } = grant;

        public RecordingAuthorizationRegistration? CurrentReservation
        {
            get;
            private set;
        }

        public Action? Reading { get; set; }

        public Action? ReservationCommitted { get; set; }

        public Exception? ReservationFailure { get; set; }

        public string? ReservationRejectionReason { get; set; }

        public MirrorParticipantRole? ReservedRole { get; private set; }

        public int ReservationCount { get; private set; }

        public int ReadCount { get; private set; }

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId)
        {
            Assert.Equal(ParticipantDeviceId, peerDeviceId);
            ReadCount++;
            Reading?.Invoke();
            return CurrentGrant;
        }

        public bool InvalidateReservation() =>
            CurrentReservation?.Invalidate() == true;

        public ValueTask<DesktopRemoteWindowHostAuthorizationReservationResult>
            TryReservePreparationAsync(
                DeviceId peerDeviceId,
                string authenticatedPeerFingerprint,
                MirrorParticipantRole role,
                IDesktopRemoteWindowHostAuthorizationInvalidationSink
                    invalidationSink,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ParticipantDeviceId, peerDeviceId);
            AuthenticatedFingerprint = authenticatedPeerFingerprint;
            ReservedRole = role;
            ReservationCount++;
            if (ReservationFailure is { } failure)
            {
                throw failure;
            }

            if (ReservationRejectionReason is { } reasonCode)
            {
                return ValueTask.FromResult(
                    DesktopRemoteWindowHostAuthorizationReservationResult
                        .Rejected(reasonCode));
            }

            CurrentReservation = new RecordingAuthorizationRegistration(
                invalidationSink);
            DesktopRemoteWindowHostAuthorizationReservationResult result =
                DesktopRemoteWindowHostAuthorizationReservationResult.Confirmed(
                    CurrentReservation);
            ReservationCommitted?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingAuthorizationRegistration(
        IDesktopRemoteWindowHostAuthorizationInvalidationSink invalidationSink) :
        IDesktopRemoteWindowHostAuthorizationRegistration
    {
        private IDesktopRemoteWindowHostAuthorizationInvalidationSink? sink =
            invalidationSink;

        public int DisposeCount { get; private set; }

        public bool IsCurrent => Volatile.Read(ref sink) is not null;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref sink, null) is not null)
            {
                DisposeCount++;
            }

            return ValueTask.CompletedTask;
        }

        public bool Invalidate()
        {
            IDesktopRemoteWindowHostAuthorizationInvalidationSink? target =
                Interlocked.Exchange(ref sink, null);
            if (target is null)
            {
                return false;
            }

            target.InvalidateAuthorizationPreparationNow();
            return true;
        }
    }

    private sealed class GrantedPermissionBoundaryWithoutPreparation :
        INativeRemoteWindowPermissionBoundary
    {
        private static readonly NativeRemoteWindowPermissionSnapshot Snapshot =
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Granted,
                NativeRemoteWindowPermissionState.Granted,
                ownerGeneration: 1,
                revision: 1);
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

        public ValueTask DisposeAsync()
        {
            changed = null;
            ObserverCount = 0;
            return ValueTask.CompletedTask;
        }

        public NativeRemoteWindowPermissionSnapshot GetSnapshot()
        {
            SnapshotReadCount++;
            return Snapshot;
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<NativeRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Snapshot);
        }
    }

    private sealed class RecordingPermissionBoundary(
        NativeRemoteWindowPermissionSnapshot snapshot) :
        INativeRemoteWindowPermissionBoundary,
        INativeRemoteWindowPermissionPreparationBoundary
    {
        private Action<NativeRemoteWindowPermissionSnapshot>? changed;
        private NativeRemoteWindowPermissionSnapshot current = snapshot;

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

        public RecordingPermissionPreparationRegistration?
            CurrentPreparationRegistration
        { get; private set; }

        public int PreparationReservationCount { get; private set; }

        public Action? PreparationReserved { get; set; }

        public Exception? PreparationFailure { get; set; }

        public Action? Preparing { get; set; }

        public Func<int, bool>? PreparationCurrentReading { get; set; }

        public Exception? SnapshotFailure { get; set; }

        public int SnapshotReadCount { get; private set; }

        public NativeRemoteWindowPermissionSnapshot GetSnapshot()
        {
            SnapshotReadCount++;
            if (SnapshotFailure is { } failure)
            {
                throw failure;
            }

            return current;
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
            Preparing?.Invoke();
            if (PreparationFailure is { } failure)
            {
                throw failure;
            }

            PreparationReservationCount++;
            if (expectedSnapshot != current)
            {
                return new(
                    NativeRemoteWindowPermissionPreparationReservationStatus
                        .SnapshotChanged,
                    Registration: null);
            }

            bool allowed = current.Capture
                    == NativeRemoteWindowPermissionState.Granted
                && (frozenRole != MirrorParticipantRole.DriverEligible
                    || current.Input
                        == NativeRemoteWindowPermissionState.Granted);
            if (!allowed)
            {
                return new(
                    NativeRemoteWindowPermissionPreparationReservationStatus
                        .PermissionDenied,
                    Registration: null);
            }

            CurrentPreparationRegistration = new(
                invalidationSink,
                PreparationCurrentReading);
            invalidationSink
                .OwnNativeRemoteWindowPermissionPreparationRegistration(
                    CurrentPreparationRegistration);
            PreparationReserved?.Invoke();
            return new(
                NativeRemoteWindowPermissionPreparationReservationStatus.Reserved,
                CurrentPreparationRegistration);
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

        public ValueTask DisposeAsync()
        {
            changed = null;
            return ValueTask.CompletedTask;
        }

        public void Publish(NativeRemoteWindowPermissionSnapshot snapshot)
        {
            current = snapshot;
            CurrentPreparationRegistration?.Invalidate();
            changed?.Invoke(snapshot);
        }

        public void Notify(NativeRemoteWindowPermissionSnapshot snapshot) =>
            changed?.Invoke(snapshot);

        public Action<NativeRemoteWindowPermissionSnapshot> CaptureObservers() =>
            changed ?? throw new InvalidOperationException(
                "No permission observer is registered.");

        public void ReplaceCurrent(
            NativeRemoteWindowPermissionSnapshot snapshot) => current = snapshot;

        public sealed class RecordingPermissionPreparationRegistration(
            INativeRemoteWindowPermissionPreparationInvalidationSink sink,
            Func<int, bool>? readingCurrent) :
            INativeRemoteWindowPermissionPreparationRegistration
        {
            private int disposed;
            private int currentReadCount;
            private INativeRemoteWindowPermissionPreparationInvalidationSink?
                sink = sink;

            public int DisposeCount { get; private set; }

            public int CurrentReadCount => Volatile.Read(ref currentReadCount);

            public bool IsCurrent
            {
                get
                {
                    int read = Interlocked.Increment(ref currentReadCount);
                    return readingCurrent?.Invoke(read)
                        ?? Volatile.Read(ref disposed) == 0;
                }
            }

            public void Dispose()
            {
                DisposeCount++;
                _ = Interlocked.Exchange(ref sink, null);
                _ = Interlocked.Exchange(ref disposed, 1);
            }

            public void Invalidate()
            {
                INativeRemoteWindowPermissionPreparationInvalidationSink? target =
                    Interlocked.Exchange(ref sink, null);
                _ = Interlocked.Exchange(ref disposed, 1);
                target?.InvalidateNativeRemoteWindowPermissionPreparationNow();
            }
        }
    }

    private sealed class RecordingProtectionSource(
        List<string> timeline,
        NativeRemoteWindowProtectionObservation initialObservation) :
        INativeProtectionSource,
        INativeRemoteWindowProtectionPreparationBoundary
    {
        private Action<NativeRemoteWindowProtectionObservation>? changed;
        private NativeRemoteWindowProtectionObservation observation =
            initialObservation;
        private long nextObservationRevision = initialObservation.Revision;
        private readonly long observationOwnerGeneration =
            initialObservation.OwnerGeneration;
        private readonly long observationSessionGeneration =
            initialObservation.SessionGeneration;
        private readonly long observationSourceGeneration =
            initialObservation.SourceGeneration;
        private readonly List<string> timeline = timeline;

        public Action? CaptureStartAdmitted { get; set; }

        public Action? CaptureStartAdmitting { get; set; }

        public RecordingProtectionPreparationRegistration? CurrentPreparation
        { get; private set; }

        public Func<int, bool>? CurrentReading { get; set; }

        public bool IsDisposed { get; private set; }

        public Exception? PreparationFailure { get; set; }

        public Action? PreparationReserved { get; set; }

        public int PreparationReservationCount { get; private set; }

        public NativeRemoteWindowProtectionPreparationReservationStatus?
            PreparationStatus
        { get; set; }

        public Action? PromotionCommitted { get; set; }

        public Exception? PromotionFailure { get; set; }

        public Exception? PromotionFailureAfterCommit { get; set; }

        public bool PromotionResult { get; set; } = true;

        public Exception? ReadFailure { get; set; }

        public Action? Reading { get; set; }

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
            Reading?.Invoke();
            if (ReadFailure is { } failure)
            {
                throw failure;
            }

            latest = Volatile.Read(ref observation);
            return true;
        }

        public NativeRemoteWindowProtectionObservation CreateNextObservation(
            ProtectionSnapshot protection)
        {
            ArgumentNullException.ThrowIfNull(protection);
            NativeRemoteWindowProtectionObservation next =
                NativeRemoteWindowProtectionObservation.Create(
                    protection,
                    observationOwnerGeneration,
                    observationSessionGeneration,
                    observationSourceGeneration,
                    Interlocked.Increment(ref nextObservationRevision));
            Volatile.Write(ref observation, next);
            return next;
        }

        public void Publish(ProtectionSnapshot protection)
        {
            NativeRemoteWindowProtectionObservation published =
                CreateNextObservation(protection);
            RecordingProtectionPreparationRegistration? registration =
                CurrentPreparation;
            registration?.Publish(published);
            changed?.Invoke(published);
        }

        public void Lose()
        {
            CurrentPreparation?.Lose();
            changed = null;
        }

        NativeRemoteWindowProtectionPreparationReservationResult
            INativeRemoteWindowProtectionPreparationBoundary
                .TryReservePreparation(
                    NativeRemoteWindowProtectionObservation expectedObservation,
                    DateTimeOffset now,
                    INativeRemoteWindowProtectionPreparationInvalidationSink
                        invalidationSink)
        {
            timeline.Add("protection.reserve");
            if (PreparationFailure is { } failure)
            {
                throw failure;
            }

            PreparationReservationCount++;
            if (PreparationStatus is { } status)
            {
                return new(status, Registration: null);
            }

            NativeRemoteWindowProtectionObservation current =
                Volatile.Read(ref observation);
            if (expectedObservation != current
                || current.Protection.Kind != ProtectionKind.Safe
                || current.Protection.ObservedAt >
                    now.Add(RemoteInputPolicy.MaximumFutureClockSkew)
                || now - current.Protection.ObservedAt >
                    RemoteInputPolicy.MaximumProtectionAge)
            {
                return new(
                    NativeRemoteWindowProtectionPreparationReservationStatus
                        .ObservationChanged,
                    Registration: null);
            }

            if (CurrentPreparation?.IsActive == true)
            {
                return new(
                    NativeRemoteWindowProtectionPreparationReservationStatus
                        .ReservationConflict,
                    Registration: null);
            }

            CurrentPreparation = new(
                this,
                invalidationSink,
                CurrentReading);
            invalidationSink
                .OwnNativeRemoteWindowProtectionPreparationRegistration(
                    CurrentPreparation);
            PreparationReserved?.Invoke();
            return new(
                NativeRemoteWindowProtectionPreparationReservationStatus.Reserved,
                CurrentPreparation);
        }

        public void Dispose()
        {
            IsDisposed = true;
            Lose();
            changed = null;
        }

        public sealed class RecordingProtectionPreparationRegistration(
            RecordingProtectionSource owner,
            INativeRemoteWindowProtectionPreparationInvalidationSink sink,
            Func<int, bool>? currentReading) :
            INativeRemoteWindowProtectionPreparationRegistration
        {
            private INativeRemoteWindowProtectionFormalSink? formalSink;
            private int currentReadCount;
            private int disposeCount;
            private int state = 1;

            public int DisposeCount => Volatile.Read(ref disposeCount);

            public bool IsActive => Volatile.Read(ref state) != 0;

            public bool IsCurrent
            {
                get
                {
                    int read = Interlocked.Increment(ref currentReadCount);
                    return currentReading?.Invoke(read) ?? IsActive;
                }
            }

            public long RegistrationId => 1;

            public INativeRemoteWindowProtectionFormalSink FormalSink =>
                Volatile.Read(ref formalSink)
                ?? throw new InvalidOperationException(
                    "The test protection registration has not been promoted.");

            public bool TryPromote(
                DateTimeOffset now,
                INativeRemoteWindowProtectionFormalSink promotedSink)
            {
                _ = now;
                owner.timeline.Add("protection.promote");
                if (owner.PromotionFailure is { } failure)
                {
                    throw failure;
                }

                if (!owner.PromotionResult
                    || Interlocked.CompareExchange(ref state, 2, 1) != 1)
                {
                    return false;
                }

                formalSink = promotedSink;
                owner.PromotionCommitted?.Invoke();
                if (owner.PromotionFailureAfterCommit is { } committedFailure)
                {
                    throw committedFailure;
                }

                return true;
            }

            public bool TryAdmitCaptureStart(DateTimeOffset now)
            {
                _ = now;
                owner.timeline.Add("protection.capture_admit");
                owner.CaptureStartAdmitting?.Invoke();
                if (Interlocked.CompareExchange(ref state, 3, 2) != 2)
                {
                    return false;
                }

                owner.CaptureStartAdmitted?.Invoke();
                return true;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref state, 0) != 0)
                {
                    Interlocked.Increment(ref disposeCount);
                    formalSink = null;
                }
            }

            public void LatchLive(ProtectionSnapshot protection)
            {
                if (Volatile.Read(ref state) != 3)
                {
                    throw new InvalidOperationException(
                        "The test protection registration is not live.");
                }

                FormalSink.LatchNativeRemoteWindowProtectionObservationNow(
                    owner.CreateNextObservation(protection));
            }

            public void LatchSourceLoss()
            {
                if (Interlocked.Exchange(ref state, 0) != 3)
                {
                    throw new InvalidOperationException(
                        "The test protection registration is not live.");
                }

                FormalSink.LatchNativeRemoteWindowProtectionObservationNow(null);
            }

            public void NotifyFormal() =>
                FormalSink.NotifyNativeRemoteWindowProtectionChanged();

            public void Publish(
                NativeRemoteWindowProtectionObservation published)
            {
                int current = Volatile.Read(ref state);
                if (current == 1 && Interlocked.CompareExchange(
                        ref state,
                        0,
                        1) == 1)
                {
                    sink.InvalidateNativeRemoteWindowProtectionPreparationNow();
                    return;
                }

                if (current == 2 && Interlocked.CompareExchange(
                        ref state,
                        0,
                        2) == 2)
                {
                    formalSink?
                        .InvalidateNativeRemoteWindowProtectionBeforeCaptureNow();
                    return;
                }

                if (current == 3 && formalSink is { } liveSink)
                {
                    liveSink.LatchNativeRemoteWindowProtectionObservationNow(
                        published);
                    liveSink.NotifyNativeRemoteWindowProtectionChanged();
                }
            }

            public void Lose()
            {
                int current = Interlocked.Exchange(ref state, 0);
                if (current == 1)
                {
                    sink.InvalidateNativeRemoteWindowProtectionPreparationNow();
                }
                else if (current == 2)
                {
                    formalSink?
                        .InvalidateNativeRemoteWindowProtectionBeforeCaptureNow();
                }
                else if (current == 3 && formalSink is { } liveSink)
                {
                    liveSink.LatchNativeRemoteWindowProtectionObservationNow(null);
                    liveSink.NotifyNativeRemoteWindowProtectionChanged();
                }
            }
        }
    }

    private sealed class RecordingEmergencyStopRegistrar(List<string> timeline) :
        ILocalEmergencyStopRegistrar
    {
        public LocalBoundaryResult ReadinessResult { get; set; } =
            LocalBoundaryResult.Confirmed("emergency_stop_ready");

        public Exception? ReadinessFailure { get; set; }

        public Action? CheckingReadiness { get; set; }

        public int ReadinessCheckCount { get; private set; }

        public int ReadinessReservationDisposeCount { get; private set; }

        public int ReadinessReservationCount { get; private set; }

        public Action? ReadinessReserved { get; set; }

        public Action? PromotionCommitted { get; set; }

        public Exception? PromotionFailure { get; set; }

        public Exception? PromotionFailureAfterCommit { get; set; }

        public LocalBoundaryResult PromotionResult { get; set; } =
            LocalBoundaryResult.Confirmed("emergency_stop_registered");

        public Action? RegistrationDisposing { get; set; }

        public RecordingEmergencyStopRegistration? CurrentRegistration
        {
            get;
            private set;
        }

        public RecordingEmergencyStopReadinessReservation?
            CurrentReadinessReservation
        {
            get;
            private set;
        }

        public LocalBoundaryResult CheckReadiness()
        {
            timeline.Add("emergency_stop.readiness");
            ReadinessCheckCount++;
            CheckingReadiness?.Invoke();
            if (ReadinessFailure is { } failure)
            {
                throw failure;
            }

            return ReadinessResult;
        }

        public LocalEmergencyStopReadinessReservationResult TryReserveReadiness(
            long ownerGeneration,
            long sessionGeneration,
            ILocalEmergencyStopReadinessInvalidationSink invalidationSink)
        {
            LocalBoundaryResult readiness = CheckReadiness();
            if (!readiness.Succeeded)
            {
                return LocalEmergencyStopReadinessReservationResult.Rejected(
                    readiness.ReasonCode);
            }

            if (CurrentReadinessReservation?.IsCurrent == true
                || CurrentRegistration?.IsCurrent == true)
            {
                return LocalEmergencyStopReadinessReservationResult.Rejected(
                    "emergency_stop_registration_conflict");
            }

            ReadinessReservationCount++;
            CurrentReadinessReservation =
                new RecordingEmergencyStopReadinessReservation(
                    this,
                    ownerGeneration,
                    sessionGeneration,
                    invalidationSink);
            LocalEmergencyStopReadinessReservationResult result =
                LocalEmergencyStopReadinessReservationResult.Confirmed(
                CurrentReadinessReservation,
                "emergency_stop_readiness_reserved");
            ReadinessReserved?.Invoke();
            return result;
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
                callback,
                RegistrationDisposing);
            return LocalEmergencyStopRegistrationResult.Confirmed(
                CurrentRegistration,
                "emergency_stop_registered");
        }

        public bool LoseReadiness() =>
            CurrentReadinessReservation?.Invalidate() == true;

        public void Dispose()
        {
            CurrentReadinessReservation?.Dispose();
            if (CurrentRegistration?.IsCurrent == true)
            {
                _ = CurrentRegistration.Trigger(
                    LocalEmergencyStopCause.RegistrationLost);
            }

            CurrentRegistration?.Dispose();
        }

        internal void Release(
            RecordingEmergencyStopReadinessReservation reservation)
        {
            if (ReferenceEquals(CurrentReadinessReservation, reservation))
            {
                CurrentReadinessReservation = null;
            }

            ReadinessReservationDisposeCount++;
        }

        internal LocalEmergencyStopRegistrationResult Promote(
            RecordingEmergencyStopReadinessReservation reservation,
            Action<LocalEmergencyStopActivation> callback)
        {
            timeline.Add("emergency_stop.register");
            if (PromotionFailure is { } failure)
            {
                throw failure;
            }

            if (!ReferenceEquals(CurrentReadinessReservation, reservation)
                || !reservation.IsCurrent)
            {
                return LocalEmergencyStopRegistrationResult.Rejected(
                    "emergency_stop_readiness_stale");
            }

            if (!PromotionResult.Succeeded)
            {
                return LocalEmergencyStopRegistrationResult.Rejected(
                    PromotionResult.ReasonCode);
            }

            CurrentRegistration = new RecordingEmergencyStopRegistration(
                reservation.OwnerGeneration,
                reservation.SessionGeneration,
                callback,
                RegistrationDisposing);
            reservation.CommitPromotion(CurrentRegistration);
            CurrentReadinessReservation = null;
            LocalEmergencyStopRegistrationResult result =
                LocalEmergencyStopRegistrationResult.Confirmed(
                    CurrentRegistration,
                    PromotionResult.ReasonCode);
            PromotionCommitted?.Invoke();
            if (PromotionFailureAfterCommit is { } committedFailure)
            {
                throw committedFailure;
            }

            return result;
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
        private RecordingEmergencyStopRegistration? promotedRegistration;

        public bool IsCurrent => Volatile.Read(ref invalidationSink) is not null;

        public long OwnerGeneration { get; } = ownerGeneration;

        public long SessionGeneration { get; } = sessionGeneration;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref invalidationSink, null) is not null)
            {
                registrar.Release(this);
                return;
            }

            Interlocked.Exchange(ref promotedRegistration, null)?.Dispose();
        }

        public LocalEmergencyStopRegistrationResult TryPromote(
            Action<LocalEmergencyStopActivation> callback) =>
            registrar.Promote(this, callback);

        public void CommitPromotion(
            RecordingEmergencyStopRegistration registration)
        {
            promotedRegistration = registration;
            _ = Interlocked.Exchange(ref invalidationSink, null);
        }

        public bool Invalidate()
        {
            ILocalEmergencyStopReadinessInvalidationSink? sink =
                Interlocked.Exchange(ref invalidationSink, null);
            if (sink is null)
            {
                return false;
            }

            registrar.Release(this);
            sink.InvalidateEmergencyStopReadinessNow();
            return true;
        }
    }

    private sealed class RecordingEmergencyStopRegistration(
        long ownerGeneration,
        long sessionGeneration,
        Action<LocalEmergencyStopActivation> callback,
        Action? disposing = null) :
        ILocalEmergencyStopRegistration
    {
        private Action<LocalEmergencyStopActivation>? callback = callback;

        public long OwnerGeneration { get; } = ownerGeneration;

        public long SessionGeneration { get; } = sessionGeneration;

        public bool IsCurrent => callback is not null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref callback, null) is not null)
            {
                disposing?.Invoke();
            }
        }

        public bool Trigger(LocalEmergencyStopCause cause)
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
                cause));
            return true;
        }
    }

    private sealed class RecordingCaptureBoundary(List<string> timeline) :
        INativeRemoteWindowCaptureBoundary
    {
        private INativeRemoteWindowFrameSink? frameSink;
        private NativeRemoteWindowSourceUse? sourceUse;
        private readonly TaskCompletionSource stopEntered = NewCompletion();
        private readonly TaskCompletionSource stopRelease = NewCompletion();

        public NativeRemoteWindowSourceUse CurrentSource => Assert.IsType<
            NativeRemoteWindowSourceUse>(sourceUse);

        public int StartCount { get; private set; }

        public int EmergencyStopCount { get; private set; }

        public Action? EmergencyStopping { get; set; }

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public int StopCount { get; private set; }

        public bool BlockStop { get; set; }

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

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason)
        {
            _ = reason;
            PauseCount++;
            return LocalBoundaryResult.Confirmed("native_capture_paused");
        }

        public LocalBoundaryResult ResumeNow()
        {
            ResumeCount++;
            return LocalBoundaryResult.Confirmed("native_capture_resumed");
        }

        public LocalBoundaryResult EmergencyStopNow()
        {
            EmergencyStopping?.Invoke();
            EmergencyStopCount++;
            return LocalBoundaryResult.Confirmed(
                "native_capture_emergency_stopped");
        }

        public LocalBoundaryResult StopNow()
        {
            StopCount++;
            if (BlockStop)
            {
                stopEntered.TrySetResult();
                stopRelease.Task.GetAwaiter().GetResult();
            }

            return StopResult;
        }

        public Task WaitForStopEnteredAsync() => stopEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        public void ReleaseStop() => stopRelease.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ConfirmingInputBoundary : INativeRemoteInputBoundary
    {
        public int EmergencyStopCount { get; private set; }

        public int InjectCount { get; private set; }

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public int StopCount { get; private set; }

        public ValueTask<LocalBoundaryResult> InjectAsync(
            NativeRemoteWindowSourceUse sourceUse,
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InjectCount++;
            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("native_input_injected"));
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason)
        {
            _ = reason;
            PauseCount++;
            return LocalBoundaryResult.Confirmed("native_input_paused");
        }

        public LocalBoundaryResult ResumeNow()
        {
            ResumeCount++;
            return LocalBoundaryResult.Confirmed("native_input_resumed");
        }

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

    private sealed class RecordingControllerStopBoundary :
        IDesktopRemoteWindowControllerStopBoundary
    {
        private readonly object gate = new();
        private readonly List<CancellationToken> tokens = [];
        private TaskCompletionSource attemptsChanged = NewCompletion();
        private int attemptCount;

        public int AttemptCount => Volatile.Read(ref attemptCount);

        public CancellationToken[] Tokens
        {
            get
            {
                lock (gate)
                {
                    return tokens.ToArray();
                }
            }
        }

        public async Task WaitForAttemptCountAsync(int expected)
        {
            while (true)
            {
                Task wait;
                lock (gate)
                {
                    if (tokens.Count >= expected)
                    {
                        return;
                    }

                    wait = attemptsChanged.Task;
                }

                await wait.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public ValueTask<RemoteWindowStopResult> StopAsync(
            RemoteWindowSessionController controller,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(controller);
            Interlocked.Increment(ref attemptCount);
            lock (gate)
            {
                tokens.Add(cancellationToken);
                TaskCompletionSource completed = attemptsChanged;
                attemptsChanged = NewCompletion();
                completed.TrySetResult();
            }

            return controller.StopAsync(cancellationToken);
        }
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
        private readonly TaskCompletionSource failCloseEntered = NewCompletion();
        private readonly TaskCompletionSource failCloseRelease = NewCompletion();
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

        public Exception? PublishFailure { get; set; }

        public Action? WaitingForMedia { get; set; }

        public bool BlockDisposal { get; set; }

        public bool BlockFailClose { get; set; }

        public Exception? DisposeFailure { get; set; }

        public Exception? FailCloseFailure { get; set; }

        public Exception? RouteSelectionFailure { get; set; }

        public Action? RouteAdmitted { get; set; }

        public Action? BeforePrepareSendAdmission { get; set; }

        public Action? ConnectionPreparationReserved { get; set; }

        public Func<int, bool>? ConnectionPreparationCurrentReading { get; set; }

        public Exception? ConnectionPreparationFailure { get; set; }

        public Action? ConnectionPreparationReserving { get; set; }

        public AuthenticatedRemoteWindowConnectionPreparationReservationStatus?
            ConnectionPreparationStatus
        { get; set; }

        public Action? PrepareSendAdmitted { get; set; }

        public ProtocolVersion ProtocolVersion { get; set; } =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public int CurrentReadCount { get; private set; }

        public int ConnectionPreparationReservationCount { get; private set; }

        public RecordingConnectionPreparationRegistration?
            CurrentConnectionPreparation
        { get; private set; }

        public Exception? CurrentReadFailure { get; set; }

        public string AuthenticatedPeerFingerprint { get; set; } =
            "test-authenticated-peer-fingerprint";

        public bool IsCurrent
        {
            get
            {
                CurrentReadCount++;
                if (CurrentReadFailure is { } failure)
                {
                    throw failure;
                }

                return isCurrent;
            }
        }

        private bool isCurrent = true;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public bool DisposalCompleted => disposed.Task.IsCompleted;

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

        public AuthenticatedRemoteWindowConnectionPreparationReservationResult
            TryReservePreparation(
                IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
                    invalidationSink)
        {
            ArgumentNullException.ThrowIfNull(invalidationSink);
            ConnectionPreparationReserving?.Invoke();
            if (ConnectionPreparationFailure is { } failure)
            {
                throw failure;
            }

            ConnectionPreparationReservationCount++;
            if (ConnectionPreparationStatus is { } status)
            {
                return new(status, Registration: null);
            }

            CurrentConnectionPreparation = new(
                invalidationSink,
                ConnectionPreparationCurrentReading);
            invalidationSink
                .OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(
                    CurrentConnectionPreparation);
            ConnectionPreparationReserved?.Invoke();
            return new(
                AuthenticatedRemoteWindowConnectionPreparationReservationStatus
                    .Reserved,
                CurrentConnectionPreparation);
        }

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            IRemoteWindowHostPreparationAdmission admission,
            TimeSpan lifetime)
        {
            if (!admission.TryAdmitRouteSelection(Now))
            {
                throw new InvalidOperationException(
                    "The test host Preparation reservation rejected route admission.");
            }

            try
            {
                timeline.Add("connection.route");
                RouteAdmitted?.Invoke();
                if (RouteSelectionFailure is { } failure)
                {
                    throw failure;
                }

                if (!admission.CompleteRouteSelection())
                {
                    throw new InvalidOperationException(
                        "The test host Preparation reservation became terminal during route selection.");
                }
            }
            catch
            {
                _ = admission.TryFailRouteSelection();
                throw;
            }
        }

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforePrepareSendAdmission?.Invoke();
            if (!admission.TryAdmitPrepareSend(request, Now))
            {
                return ValueTask.FromResult(
                    RemoteWindowPreparationDeliveryResult.NotDelivered);
            }

            timeline.Add("connection.prepare");
            PrepareSendAdmitted?.Invoke();
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
            if (PublishFailure is { } failure)
            {
                throw failure;
            }

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

        public async ValueTask FailCloseAsync()
        {
            timeline.Add("connection.fail_close");
            FailCloseCount++;
            isCurrent = false;
            failCloseEntered.TrySetResult();
            try
            {
                if (BlockFailClose)
                {
                    await failCloseRelease.Task.ConfigureAwait(false);
                }

                if (FailCloseFailure is { } failure)
                {
                    throw failure;
                }
            }
            finally
            {
                failClosed.TrySetResult();
            }
        }

        public Task WaitForFailCloseAsync() => failClosed.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        public Task WaitForFailCloseEnteredAsync() =>
            failCloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

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

        public sealed class RecordingConnectionPreparationRegistration(
            IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink sink,
            Func<int, bool>? readingCurrent) :
            IAuthenticatedRemoteWindowConnectionPreparationRegistration
        {
            private int active = 1;
            private int currentReadCount;
            private int disposeCount;
            private IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink?
                sink = sink;

            public int DisposeCount => Volatile.Read(ref disposeCount);

            public int CurrentReadCount => Volatile.Read(ref currentReadCount);

            public bool IsCurrent
            {
                get
                {
                    int read = Interlocked.Increment(ref currentReadCount);
                    return readingCurrent?.Invoke(read)
                        ?? Volatile.Read(ref active) != 0;
                }
            }

            public long RegistrationId => 1;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref active, 0) != 0)
                {
                    Interlocked.Increment(ref disposeCount);
                    _ = Interlocked.Exchange(ref sink, null);
                }
            }

            public bool Invalidate()
            {
                if (Interlocked.Exchange(ref active, 0) == 0)
                {
                    return false;
                }

                IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink?
                    target = Interlocked.Exchange(ref sink, null);
                target?.InvalidateAuthenticatedRemoteWindowConnectionPreparationNow();
                return true;
            }
        }

        public bool InvalidateConnectionPreparation()
        {
            isCurrent = false;
            return CurrentConnectionPreparation?.Invalidate() == true;
        }

        public void ReleaseDisposal() => disposeRelease.TrySetResult();

        public void ReleaseFailClose() => failCloseRelease.TrySetResult();

        public Action? CaptureRevocationCallback()
        {
            isCurrent = false;
            _ = CurrentConnectionPreparation?.Invalidate();
            return Volatile.Read(ref revoked);
        }

        public void Revoke() => CaptureRevocationCallback()?.Invoke();
    }

    private sealed record ControllerStopPublicationSnapshot(
        bool HasRetiringGeneration,
        bool IsAuthorityClosed,
        bool SnapshotIsNull,
        bool ActiveMediaBudgetIsNull,
        int TimerCreateCount,
        int ActiveTimerCount);

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
