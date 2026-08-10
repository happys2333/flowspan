using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class RemoteWindowSessionControllerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId Host =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly ActivityId Activity =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DeviceId Peer =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task FreshSafeCaptureStartsBeforeActiveSharingIsPublished()
    {
        const string payloadCanary = "FLOWSPAN_REMOTE_WINDOW_PAYLOAD_CANARY";
        var capture = new RecordingCaptureBoundary();
        RemoteWindowSessionController controller = CreateController(
            capture,
            payloadCanary);

        RemoteWindowCommandResult result = await controller.StartAsync(
            SafeAt(Now));

        Assert.True(result.Succeeded);
        Assert.Equal(RemoteWindowCommandStatus.Applied, result.Status);
        Assert.Equal(RemoteWindowLifecycle.Active, result.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Capturing, result.Snapshot.CaptureState);
        Assert.Equal(Host, result.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(1, result.Snapshot.DriverLeaseEpoch);
        Assert.Equal(["capture.start"], capture.Events);
        Assert.Equal(
            RemoteWindowLifecycle.Starting,
            capture.LifecycleObservedAtStart);
        Assert.DoesNotContain(
            payloadCanary,
            result.Snapshot.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewOnlyJoinRechecksCurrentMirrorViewGrant()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization);
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowCommandResult denied = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.ViewOnly);
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        RemoteWindowCommandResult admitted = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.ViewOnly);

        Assert.Equal(RemoteWindowCommandStatus.CapabilityDenied, denied.Status);
        Assert.DoesNotContain(Peer, denied.Snapshot.Participants.Keys);
        Assert.True(admitted.Succeeded);
        Assert.Equal(
            MirrorParticipantRole.ViewOnly,
            admitted.Snapshot.Participants[Peer]);
    }

    [Fact]
    public async Task DriverTransferRechecksDriveGrantAndPublishesHigherEpoch()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));

        RemoteWindowCommandResult denied = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        RemoteWindowCommandResult transferred = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));

        Assert.Equal(RemoteWindowCommandStatus.CapabilityDenied, denied.Status);
        Assert.Equal(Host, denied.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(1, denied.Snapshot.DriverLeaseEpoch);
        Assert.True(transferred.Succeeded);
        Assert.Equal(Peer, transferred.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(2, transferred.Snapshot.DriverLeaseEpoch);
    }

    [Fact]
    public async Task CurrentDriverInjectsDefensivelyCopiedBatchThroughPublicBoundary()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);
        RemoteWindowCommandResult transferred = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));
        var source = new List<RemoteInputEvent>
        {
            RemoteInputEvent.PointerMove(0.25, 0.75),
        };
        RemoteInputBatch batch = RemoteInputBatch.Create(source);
        source.Add(RemoteInputEvent.PointerMove(0.5, 0.5));

        RemoteInputAttemptResult result = await controller.InjectInputAsync(
            Peer,
            transferred.Snapshot.DriverLeaseEpoch!.Value,
            batch);

        Assert.True(result.Injected);
        Assert.Equal(RemoteInputDecision.Allowed, result.Decision);
        Assert.Single(input.Batches);
        Assert.Single(input.Batches[0].Events);
        Assert.Equal("Remote input batch (1 event)", input.Batches[0].ToString());
    }

    [Fact]
    public async Task DriveRevocationDowngradesPeerAndReturnsLeaseToHost()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);
        RemoteWindowCommandResult transferred = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));

        RemoteWindowCommandResult reconciled =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);
        RemoteInputAttemptResult staleInput = await controller.InjectInputAsync(
            Peer,
            transferred.Snapshot.DriverLeaseEpoch!.Value,
            RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.1, 0.2)]));

        Assert.True(reconciled.Succeeded);
        Assert.Equal(MirrorParticipantRole.ViewOnly, reconciled.Snapshot.Participants[Peer]);
        Assert.Equal(Host, reconciled.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(3, reconciled.Snapshot.DriverLeaseEpoch);
        Assert.Equal(RemoteInputDecision.CapabilityDenied, staleInput.Decision);
        Assert.Empty(input.Batches);
    }

    [Fact]
    public async Task SecureInputPausesCaptureAndInputWithVisibleBlockedState()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowProtectionResult result = controller.ApplyProtectionSnapshot(
            new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-probe"));

        Assert.True(result.Blocked);
        Assert.True(result.LocalGatesConfirmed);
        Assert.Equal(MirrorPauseReason.SensitiveSurface, result.PauseReason);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, result.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Paused, result.Snapshot.CaptureState);
        Assert.Equal(["capture.start", "capture.pause"], capture.Events);
        Assert.Equal(["input.pause"], input.Events);
        Assert.Equal(
            RemoteWindowLifecycle.ProtectionPaused,
            capture.LifecycleObservedAtPause);
        Assert.Equal(
            RemoteWindowLifecycle.ProtectionPaused,
            input.LifecycleObservedAtPause);
    }

    [Fact]
    public async Task EmergencyStopRevokesLeaseBeforeEveryLocalBoundary()
    {
        var stopOrder = new List<string>();
        var observed = new List<RemoteWindowSharingSnapshot>();
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        capture.OnEmergencyStop = Observe("capture");
        input.OnEmergencyStop = Observe("input");
        sessions.OnDisconnectAll = Observe("sessions");
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowEmergencyStopResult result = controller.EmergencyStop();

        Assert.True(result.FullyStopped);
        Assert.Equal(["capture", "input", "sessions"], stopOrder);
        Assert.All(observed, snapshot =>
        {
            Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, snapshot.Lifecycle);
            Assert.Equal(RemoteWindowCaptureState.Unconfirmed, snapshot.CaptureState);
            Assert.Null(snapshot.CurrentDriverDeviceId);
            Assert.Equal(2, snapshot.DriverLeaseEpoch);
        });
        Assert.Equal(RemoteWindowCaptureState.Stopped, result.Snapshot.CaptureState);

        Action Observe(string boundary) => () =>
        {
            stopOrder.Add(boundary);
            observed.Add(controller.Snapshot);
        };
    }

    [Fact]
    public async Task ReentrantEmergencyStopDoesNotRecurseThroughLocalBoundaries()
    {
        var capture = new ReenteringEmergencyStopCaptureBoundary();
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        int inputStops = 0;
        input.OnEmergencyStop = () => inputStops++;
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        capture.Reenter = controller.EmergencyStop;

        RemoteWindowEmergencyStopResult outer = controller.EmergencyStop();

        RemoteWindowEmergencyStopResult nested = Assert.IsType<
            RemoteWindowEmergencyStopResult>(capture.ReentrantResult);
        Assert.False(nested.FullyStopped);
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, nested.Snapshot.Lifecycle);
        Assert.True(outer.FullyStopped);
        Assert.Equal(1, capture.EmergencyStopCallCount);
        Assert.Equal(1, capture.MaximumCallDepth);
        Assert.Equal(1, inputStops);
        Assert.Equal(1, sessions.DisconnectAllCallCount);
    }

    [Fact]
    public async Task CapturedEmergencyStopContextExpiresAfterOuterAttemptCompletes()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        var releaseDelayedStop = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RemoteWindowEmergencyStopResult>? delayedStop = null;
        int inputStops = 0;
        input.OnEmergencyStop = () => inputStops++;
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        capture.OnEmergencyStop = () =>
        {
            capture.OnEmergencyStop = null;
            delayedStop = Task.Run(async () =>
            {
                await releaseDelayedStop.Task;
                return controller.EmergencyStop();
            });
        };

        RemoteWindowEmergencyStopResult first = controller.EmergencyStop();
        releaseDelayedStop.TrySetResult();
        Assert.NotNull(delayedStop);
        RemoteWindowEmergencyStopResult retry = await delayedStop;

        Assert.True(first.FullyStopped);
        Assert.True(retry.FullyStopped);
        Assert.Equal(2, capture.EmergencyStopCallCount);
        Assert.Equal(2, inputStops);
        Assert.Equal(2, sessions.DisconnectAllCallCount);
    }

    [Fact]
    public async Task EmergencyStopWinsAgainstLateCaptureStartCompletion()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(capture);

        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;
        RemoteWindowEmergencyStopResult stopped = controller.EmergencyStop();
        capture.ReleaseStart();
        RemoteWindowCommandResult lateStart = await starting;

        Assert.True(stopped.FullyStopped);
        Assert.Equal(RemoteWindowCommandStatus.EmergencyStopped, lateStart.Status);
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Stopped, controller.Snapshot.CaptureState);
        Assert.Null(controller.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(2, controller.Snapshot.DriverLeaseEpoch);
    }

    [Fact]
    public async Task LateCaptureAdmissionInvalidatesEarlierEmergencyStopProof()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;

        RemoteWindowEmergencyStopResult initialStop = controller.EmergencyStop();
        capture.EmergencyFailure = new IOException("private late stop failure");
        capture.ReleaseStart();
        RemoteWindowCommandResult lateStart = await starting;
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.True(initialStop.FullyStopped);
        Assert.Equal(RemoteWindowCommandStatus.EmergencyStopped, lateStart.Status);
        Assert.False(lateStart.CleanupBoundary?.Succeeded);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            lateStart.Snapshot.CaptureState);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, reset.Status);
        Assert.Equal("emergency_boundaries_unconfirmed", reset.ReasonCode);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            reset.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task DriverDisconnectReturnsLeaseBeforeLocalPeerSessionCloses()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var sessions = new RecordingSharingSessionBoundary();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);
        _ = await controller.TransferDriverAsync(Peer, TimeSpan.FromSeconds(10));

        RemoteWindowCommandResult result =
            await controller.DisconnectParticipantAsync(Peer);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(Peer, result.Snapshot.Participants.Keys);
        Assert.Equal(Host, result.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(3, result.Snapshot.DriverLeaseEpoch);
        Assert.Equal([Peer], sessions.DisconnectedPeers);
        Assert.NotNull(sessions.SnapshotObservedAtPeerDisconnect);
        Assert.Equal(
            Host,
            sessions.SnapshotObservedAtPeerDisconnect.CurrentDriverDeviceId);
        Assert.DoesNotContain(
            Peer,
            sessions.SnapshotObservedAtPeerDisconnect.Participants.Keys);
    }

    [Fact]
    public async Task ParticipantDisconnectRetriesUnconfirmedLocalBoundary()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectPeerResult =
                LocalBoundaryResult.Failed("peer_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        _ = await controller.AddParticipantAsync(Peer, MirrorParticipantRole.ViewOnly);

        RemoteWindowCommandResult failed =
            await controller.DisconnectParticipantAsync(Peer);
        sessions.DisconnectPeerResult =
            LocalBoundaryResult.Confirmed("peer_disconnected");
        RemoteWindowCommandResult retried =
            await controller.DisconnectParticipantAsync(Peer);
        RemoteWindowCommandResult completed =
            await controller.DisconnectParticipantAsync(Peer);

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failed.Status);
        Assert.DoesNotContain(Peer, failed.Snapshot.Participants.Keys);
        Assert.Equal(RemoteWindowCommandStatus.Applied, retried.Status);
        Assert.Equal(RemoteWindowCommandStatus.AlreadyApplied, completed.Status);
        Assert.Equal([Peer, Peer], sessions.DisconnectedPeers);
    }

    [Fact]
    public async Task ExpiredDriverLeaseReturnsToHostAndRejectsOldEpoch()
    {
        var clock = new MutableClock(Now);
        var authorization = new MutableMirrorAuthorizationSource();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            input: input,
            clock: clock);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);
        RemoteWindowCommandResult transferred = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(1));
        clock.UtcNow = Now.AddSeconds(1);

        RemoteWindowCommandResult refreshed = await controller.RefreshExpiredLeaseAsync();
        _ = controller.ApplyProtectionSnapshot(SafeAt(clock.UtcNow));
        RemoteInputAttemptResult staleInput = await controller.InjectInputAsync(
            Peer,
            transferred.Snapshot.DriverLeaseEpoch!.Value,
            RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.1, 0.2)]));

        Assert.True(refreshed.Succeeded);
        Assert.Equal(Host, refreshed.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(3, refreshed.Snapshot.DriverLeaseEpoch);
        Assert.Equal(RemoteInputDecision.DriverLeaseDenied, staleInput.Decision);
        Assert.Empty(input.Batches);
    }

    [Fact]
    public void RemoteInputEventsHaveClosedShapesAndBoundedBatches()
    {
        RemoteInputEvent key = RemoteInputEvent.HidKeyDown(0x07, 0x04);
        RemoteInputEvent button = RemoteInputEvent.PointerButtonDown(
            RemotePointerButton.Primary);
        RemoteInputEvent scroll = RemoteInputEvent.Scroll(-120, 240);

        Assert.Equal(RemoteInputEventKind.HidKeyDown, key.Kind);
        Assert.Equal(0x07, key.HidUsagePage);
        Assert.Equal(0x04, key.HidUsageId);
        Assert.Equal(RemotePointerButton.Primary, button.PointerButton);
        Assert.Equal(-120, scroll.HorizontalScroll);
        Assert.Equal(240, scroll.VerticalScroll);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteInputEvent.HidKeyDown(0, 0x04));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteInputEvent.PointerMove(double.NaN, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteInputEvent.PointerButtonUp((RemotePointerButton)999));
        Assert.Throws<ArgumentException>(() => RemoteInputEvent.Scroll(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteInputEvent.Scroll(RemoteInputEvent.MaximumScrollDelta + 1, 0));

        RemoteInputEvent[] maximum = Enumerable
            .Repeat(RemoteInputEvent.PointerMove(0.5, 0.5), 64)
            .ToArray();
        Assert.Equal(64, RemoteInputBatch.Create(maximum).Events.Count);
        Assert.Throws<ArgumentException>(() =>
            RemoteInputBatch.Create(
                maximum.Append(RemoteInputEvent.PointerMove(0.5, 0.5))));
    }

    [Fact]
    public async Task OrdinaryStopRunsEveryLocalBoundaryAndReportsUnconfirmedCapture()
    {
        var stopOrder = new List<string>();
        var capture = new RecordingCaptureBoundary
        {
            OnStop = () => stopOrder.Add("capture"),
            StopFailure = new IOException("FLOWSPAN_PRIVATE_CAPTURE_EXCEPTION"),
        };
        var input = new RecordingInputBoundary
        {
            OnStop = () => stopOrder.Add("input"),
        };
        var sessions = new RecordingSharingSessionBoundary
        {
            OnDisconnectAll = () => stopOrder.Add("sessions"),
        };
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowStopResult result = await controller.StopAsync();

        Assert.False(result.FullyStopped);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, result.Status);
        Assert.Equal(["capture", "input", "sessions"], stopOrder);
        Assert.Equal("local_boundary_exception", result.CaptureBoundary.ReasonCode);
        Assert.DoesNotContain(
            "FLOWSPAN_PRIVATE_CAPTURE_EXCEPTION",
            result.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(RemoteWindowLifecycle.Ended, result.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Unconfirmed, result.Snapshot.CaptureState);
        Assert.Null(result.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(2, result.Snapshot.DriverLeaseEpoch);
    }

    [Fact]
    public async Task DisposingActiveControllerFailsClosedThroughEmergencyBoundaries()
    {
        int captureStops = 0;
        int inputStops = 0;
        int sessionStops = 0;
        var capture = new RecordingCaptureBoundary
        {
            OnEmergencyStop = () => captureStops++,
        };
        var input = new RecordingInputBoundary
        {
            OnEmergencyStop = () => inputStops++,
        };
        var sessions = new RecordingSharingSessionBoundary
        {
            OnDisconnectAll = () => sessionStops++,
        };
        RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));

        controller.Dispose();

        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Stopped, controller.Snapshot.CaptureState);
        Assert.Equal(1, captureStops);
        Assert.Equal(1, inputStops);
        Assert.Equal(1, sessionStops);
        controller.Dispose();
        Assert.Equal(1, captureStops);
        Assert.Equal(1, inputStops);
        Assert.Equal(1, sessionStops);
    }

    [Fact]
    public async Task EmergencyResetRequiresEveryLocalBoundaryConfirmation()
    {
        var capture = new RecordingCaptureBoundary
        {
            EmergencyFailure = new IOException("private capture failure"),
        };
        using RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowEmergencyStopResult failedStop = controller.EmergencyStop();
        RemoteWindowCommandResult deniedReset =
            await controller.ResetAfterLocalConfirmationAsync();
        capture.EmergencyFailure = null;
        RemoteWindowEmergencyStopResult retriedStop = controller.EmergencyStop();
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();
        RemoteWindowCommandResult restarted = await controller.StartAsync(SafeAt(Now));

        Assert.False(failedStop.FullyStopped);
        Assert.Equal("local_boundary_exception", failedStop.CaptureBoundary.ReasonCode);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, deniedReset.Status);
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, deniedReset.Snapshot.Lifecycle);
        Assert.Equal(2, deniedReset.Snapshot.DriverLeaseEpoch);
        Assert.True(retriedStop.FullyStopped);
        Assert.Equal(2, retriedStop.Snapshot.DriverLeaseEpoch);
        Assert.True(reset.Succeeded);
        Assert.Equal(RemoteWindowLifecycle.Idle, reset.Snapshot.Lifecycle);
        Assert.Empty(reset.Snapshot.Participants);
        Assert.Null(reset.Snapshot.CurrentDriverDeviceId);
        Assert.True(restarted.Succeeded);
        Assert.Equal(Host, restarted.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(1, restarted.Snapshot.DriverLeaseEpoch);
    }

    [Fact]
    public async Task EmergencyStopReturnsConfirmationsAccumulatedAcrossCurrentGeneration()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary
        {
            EmergencyStopResult = LocalBoundaryResult.Failed("input_stop_failed"),
        };
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectAllResult = LocalBoundaryResult.Failed(
                "sessions_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowEmergencyStopResult captureConfirmed =
            controller.EmergencyStop();
        capture.EmergencyFailure = new IOException("private capture failure");
        input.EmergencyStopResult =
            LocalBoundaryResult.Confirmed("input_emergency_stopped");
        RemoteWindowEmergencyStopResult inputConfirmed =
            controller.EmergencyStop();
        input.EmergencyStopResult = LocalBoundaryResult.Failed("input_stop_failed");
        sessions.DisconnectAllResult =
            LocalBoundaryResult.Confirmed("sessions_disconnected");

        RemoteWindowEmergencyStopResult allConfirmed = controller.EmergencyStop();
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.False(captureConfirmed.FullyStopped);
        Assert.False(inputConfirmed.FullyStopped);
        Assert.True(allConfirmed.FullyStopped);
        Assert.True(allConfirmed.CaptureBoundary.Succeeded);
        Assert.True(allConfirmed.InputBoundary.Succeeded);
        Assert.True(allConfirmed.SessionBoundary.Succeeded);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            allConfirmed.Snapshot.CaptureState);
        Assert.True(reset.Succeeded);
        Assert.Equal(RemoteWindowLifecycle.Idle, reset.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task ConcurrentEmergencyAttemptsMergeCurrentGenerationConfirmations()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary
        {
            EmergencyStopResult = LocalBoundaryResult.Failed("input_stop_failed"),
        };
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectAllResult = LocalBoundaryResult.Failed(
                "sessions_disconnect_failed"),
        };
        var firstDisconnectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDisconnect = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        sessions.OnDisconnectAll = () =>
        {
            if (sessions.DisconnectAllCallCount != 1)
            {
                return;
            }

            firstDisconnectEntered.TrySetResult();
            releaseFirstDisconnect.Task.GetAwaiter().GetResult();
        };
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));

        Task<RemoteWindowEmergencyStopResult> olderAttempt = Task.Run(
            controller.EmergencyStop);
        await firstDisconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        capture.EmergencyFailure = new IOException("private capture failure");
        input.EmergencyStopResult =
            LocalBoundaryResult.Confirmed("input_emergency_stopped");
        sessions.DisconnectAllResult =
            LocalBoundaryResult.Confirmed("sessions_disconnected");

        RemoteWindowEmergencyStopResult retry;
        RemoteWindowCommandResult prematureReset;
        try
        {
            retry = controller.EmergencyStop();
            prematureReset = await controller.ResetAfterLocalConfirmationAsync();
        }
        finally
        {
            releaseFirstDisconnect.TrySetResult();
        }

        RemoteWindowEmergencyStopResult completedOlderAttempt =
            await olderAttempt.WaitAsync(TimeSpan.FromSeconds(2));
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.False(retry.FullyStopped);
        Assert.Equal(
            RemoteWindowCommandStatus.BoundaryFailed,
            prematureReset.Status);
        Assert.Equal("emergency_stop_in_progress", prematureReset.ReasonCode);
        Assert.True(completedOlderAttempt.FullyStopped);
        Assert.True(completedOlderAttempt.CaptureBoundary.Succeeded);
        Assert.True(completedOlderAttempt.InputBoundary.Succeeded);
        Assert.True(completedOlderAttempt.SessionBoundary.Succeeded);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            completedOlderAttempt.Snapshot.CaptureState);
        Assert.True(reset.Succeeded);
    }

    [Fact]
    public async Task ProtectionBlockWinsAgainstLateCaptureStartCompletion()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);

        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;
        RemoteWindowProtectionResult blocked = controller.ApplyProtectionSnapshot(
            new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-probe"));
        capture.ReleaseStart();
        RemoteWindowCommandResult lateStart = await starting;

        Assert.True(blocked.Blocked);
        Assert.Equal(RemoteWindowCommandStatus.Applied, blocked.Status);
        Assert.Contains("capture.pause", capture.Events);
        Assert.Contains("input.pause", input.Events);
        Assert.Equal(RemoteWindowCommandStatus.ProtectionBlocked, lateStart.Status);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, controller.Snapshot.Lifecycle);
        Assert.NotEqual(RemoteWindowCaptureState.Capturing, controller.Snapshot.CaptureState);
    }

    [Fact]
    public async Task SafeProtectionCannotResumeBeforeCaptureAdmissionConfirms()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
        };
        var input = new RecordingInputBoundary();
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));

        RemoteWindowProtectionResult safeWhilePending =
            controller.ApplyProtectionSnapshot(SafeAt(Now));
        RemoteWindowSharingSnapshot pending = controller.Snapshot;
        capture.ReleaseStart();
        RemoteWindowCommandResult failedStart = await starting;

        Assert.True(safeWhilePending.Blocked);
        Assert.Equal(
            RemoteWindowLifecycle.ProtectionPaused,
            safeWhilePending.Snapshot.Lifecycle);
        Assert.NotEqual(
            RemoteWindowCaptureState.Capturing,
            safeWhilePending.Snapshot.CaptureState);
        Assert.DoesNotContain("capture.resume", capture.Events);
        Assert.False(input.IsAcceptingInput);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, pending.Lifecycle);
        Assert.Equal(RemoteWindowLifecycle.Unavailable, failedStart.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task NewerSafeProtectionWinsAgainstLateStartPauseCompletion()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.OnPause = () =>
        {
            capture.OnPause = null;
            _ = controller.ApplyProtectionSnapshot(SafeAt(Now));
        };

        capture.ReleaseStart();
        RemoteWindowCommandResult lateStart = await starting;

        Assert.Equal(RemoteWindowCommandStatus.Applied, lateStart.Status);
        Assert.Equal(RemoteWindowLifecycle.Active, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Capturing, controller.Snapshot.CaptureState);
        Assert.Equal(ProtectionKind.Safe, controller.Snapshot.ProtectionKind);
    }

    [Fact]
    public async Task FailedStartCleansUpAfterPreAdmissionResumeIsBlocked()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
        };
        var input = new RecordingInputBoundary();
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        RemoteWindowProtectionResult safeWhilePending =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        capture.ReleaseStart();
        RemoteWindowCommandResult failedStart = await starting;
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.True(safeWhilePending.Blocked);
        Assert.DoesNotContain("capture.resume", capture.Events);
        Assert.Equal(RemoteWindowLifecycle.Unavailable, failedStart.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            failedStart.Snapshot.CaptureState);
        Assert.False(capture.IsCapturing);
        Assert.False(input.IsAcceptingInput);
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(RemoteWindowCommandStatus.Applied, reset.Status);
        Assert.Equal(RemoteWindowLifecycle.Idle, reset.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task StaleProtectionCleanupRetainsPeersWhenSessionDisconnectFails()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        var capture = new RecordingCaptureBoundary();
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectAllResult = LocalBoundaryResult.Failed(
                "session_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            capture,
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.ViewOnly);
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.BlockResume();
        Task<RemoteWindowProtectionResult> staleResume = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        await capture.ResumeEntered.Task;

        RemoteWindowStopResult stopped = await controller.StopAsync();
        capture.ReleaseResume();
        RemoteWindowProtectionResult staleCleanup = await staleResume;

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, stopped.Status);
        Assert.Contains(Peer, stopped.Snapshot.Participants.Keys);
        Assert.Equal(
            RemoteWindowCommandStatus.BoundaryFailed,
            staleCleanup.Status);
        Assert.Equal(
            LocalBoundaryStatus.Failed,
            staleCleanup.SessionBoundary?.Status);
        Assert.Equal(
            "session_disconnect_failed",
            staleCleanup.SessionBoundary?.ReasonCode);
        Assert.Contains(Peer, staleCleanup.Snapshot.Participants.Keys);
        Assert.Contains(Peer, controller.Snapshot.Participants.Keys);
        Assert.Equal(2, sessions.DisconnectAllCallCount);
    }

    [Fact]
    public async Task EmergencyStopDefersResetUntilStaleProtectionResumeIsReclosed()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.BlockResume();
        Task<RemoteWindowProtectionResult> staleResume = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        await capture.ResumeEntered.Task;

        RemoteWindowEmergencyStopResult stopped = controller.EmergencyStop();
        RemoteWindowCommandResult prematureReset =
            await controller.ResetAfterLocalConfirmationAsync();
        RemoteWindowCommandResult prematureRetry =
            await controller.StartAsync(SafeAt(Now));
        capture.ReleaseResume();
        RemoteWindowProtectionResult staleResult = await staleResume;
        bool captureReclosed = !capture.IsCapturing;
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();
        RemoteWindowCommandResult restarted =
            await controller.StartAsync(SafeAt(Now));

        Assert.True(stopped.FullyStopped);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, prematureReset.Status);
        Assert.Equal(
            "protection_reconciliation_in_progress",
            prematureReset.ReasonCode);
        Assert.Equal(RemoteWindowCommandStatus.InvalidState, prematureRetry.Status);
        Assert.True(staleResult.Blocked);
        Assert.True(captureReclosed);
        Assert.False(input.IsAcceptingInput);
        Assert.Equal(2, capture.EmergencyStopCallCount);
        Assert.True(
            capture.BoundaryTimeline.LastIndexOf("capture.emergency_stop")
            > capture.BoundaryTimeline.LastIndexOf("capture.resume.return"));
        Assert.Equal(RemoteWindowCommandStatus.Applied, reset.Status);
        Assert.Equal(RemoteWindowLifecycle.Idle, reset.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCommandStatus.Applied, restarted.Status);
        Assert.Equal(RemoteWindowLifecycle.Active, restarted.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task EmergencyReassertionPreservesFailedSessionBoundaryAfterStaleResume()
    {
        var capture = new RecordingCaptureBoundary();
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectAllResult = LocalBoundaryResult.Failed(
                "session_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            capture,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.BlockResume();
        Task<RemoteWindowProtectionResult> staleResume = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        await capture.ResumeEntered.Task;

        RemoteWindowEmergencyStopResult stopped = controller.EmergencyStop();
        capture.ReleaseResume();
        RemoteWindowProtectionResult reasserted = await staleResume;

        Assert.False(stopped.FullyStopped);
        Assert.Equal(
            (
                RemoteWindowCommandStatus.BoundaryFailed,
                (LocalBoundaryStatus?)LocalBoundaryStatus.Failed,
                "session_disconnect_failed"),
            (
                reasserted.Status,
                reasserted.SessionBoundary?.Status,
                reasserted.SessionBoundary?.ReasonCode));
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            reasserted.Snapshot.Lifecycle);
        Assert.Equal(2, sessions.DisconnectAllCallCount);
    }

    [Fact]
    public async Task ProtectionObservationRegistersBeforeResetCanStartNewSession()
    {
        var clock = new MutableClock(Now);
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            clock: clock);
        _ = await controller.StartAsync(SafeAt(Now));
        RemoteWindowCommandResult? resetDuringObservation = null;
        RemoteWindowCommandResult? restarted = null;
        clock.OnRead = () =>
        {
            _ = controller.EmergencyStop();
            resetDuringObservation = controller
                .ResetAfterLocalConfirmationAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (resetDuringObservation.Succeeded)
            {
                restarted = controller
                    .StartAsync(SafeAt(Now))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        };

        RemoteWindowProtectionResult staleObservation =
            controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-probe"));

        Assert.NotNull(resetDuringObservation);
        Assert.Equal(
            RemoteWindowCommandStatus.BoundaryFailed,
            resetDuringObservation.Status);
        Assert.Equal(
            "protection_reconciliation_in_progress",
            resetDuringObservation.ReasonCode);
        Assert.Null(restarted);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            staleObservation.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task ProtectionClockReadDoesNotHoldStateLockAcrossReentrantStop()
    {
        var clock = new MutableClock(Now);
        var capture = new RecordingCaptureBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            clock: clock);
        _ = await controller.StartAsync(SafeAt(Now));
        Task<RemoteWindowEmergencyStopResult>? stopping = null;
        clock.OnRead = () =>
        {
            stopping = Task.Run(controller.EmergencyStop);
            capture.EmergencyStopEntered.Task.GetAwaiter().GetResult();
        };

        RemoteWindowProtectionResult observation = await Task.Run(() =>
                controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(stopping);
        RemoteWindowEmergencyStopResult stopped = await stopping;

        Assert.True(stopped.FullyStopped);
        Assert.True(observation.Blocked);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task EmergencyResetWaitsForEveryAttemptFromCurrentStopGeneration()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockEmergencyStopCall(1);
        using RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));

        Task<RemoteWindowEmergencyStopResult> olderStop = Task.Run(
            controller.EmergencyStop);
        await capture.EmergencyStopEntered.Task;
        RemoteWindowEmergencyStopResult retry = controller.EmergencyStop();
        RemoteWindowCommandResult prematureReset =
            await controller.ResetAfterLocalConfirmationAsync();

        capture.ReleaseEmergencyStop();
        RemoteWindowEmergencyStopResult completedOlderStop = await olderStop;
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();
        RemoteWindowCommandResult restarted =
            await controller.StartAsync(SafeAt(Now));

        Assert.True(retry.FullyStopped);
        Assert.Equal(
            RemoteWindowCommandStatus.BoundaryFailed,
            prematureReset.Status);
        Assert.Equal("emergency_stop_in_progress", prematureReset.ReasonCode);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            prematureReset.Snapshot.Lifecycle);
        Assert.True(completedOlderStop.FullyStopped);
        Assert.True(reset.Succeeded);
        Assert.Equal(RemoteWindowLifecycle.Active, restarted.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Capturing,
            restarted.Snapshot.CaptureState);
    }

    [Fact]
    public async Task EmergencyResetRequiresFreshConfirmationAfterStaleResume()
    {
        var capture = new RecordingCaptureBoundary();
        using RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.BlockResume();
        Task<RemoteWindowProtectionResult> staleResume = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        await capture.ResumeEntered.Task;

        RemoteWindowEmergencyStopResult stopped = controller.EmergencyStop();
        capture.EmergencyFailure = new IOException("private reclose failure");
        capture.ReleaseResume();
        RemoteWindowProtectionResult staleResult = await staleResume;
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.True(stopped.FullyStopped);
        Assert.True(staleResult.Blocked);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, reset.Status);
        Assert.Equal("emergency_boundaries_unconfirmed", reset.ReasonCode);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            reset.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            reset.Snapshot.CaptureState);
    }

    [Fact]
    public async Task ConcurrentSafeObservationsCannotResumeBeforeFailedAdmission()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
        };
        capture.BlockStart();
        using RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        Task<RemoteWindowProtectionResult> firstSafe = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        Task<RemoteWindowProtectionResult> secondSafe = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        RemoteWindowProtectionResult[] safeResults = await Task.WhenAll(
            firstSafe,
            secondSafe);

        capture.ReleaseStart();
        RemoteWindowCommandResult failedStart = await starting;
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.All(safeResults, static result => Assert.True(result.Blocked));
        Assert.DoesNotContain("capture.resume", capture.Events);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            failedStart.Snapshot.CaptureState);
        Assert.False(capture.IsCapturing);
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(RemoteWindowCommandStatus.Applied, reset.Status);
    }

    [Fact]
    public async Task DriverAdmissionUsesOneCurrentCapabilitySnapshot()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization);
        _ = await controller.StartAsync(SafeAt(Now));

        RemoteWindowCommandResult result = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);

        Assert.True(result.Succeeded);
        Assert.Equal(1, authorization.ReadCount);
    }

    [Fact]
    public async Task DisposePreemptsThenDrainsPendingStartWithoutTearingItsGate()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockStart();
        RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        await capture.StartEntered.Task;

        Task disposing = Task.Run(controller.Dispose);
        await capture.EmergencyStopEntered.Task;
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, controller.Snapshot.Lifecycle);
        capture.ReleaseStart();
        RemoteWindowCommandResult startResult = await starting;
        await disposing;

        Assert.Equal(RemoteWindowCommandStatus.EmergencyStopped, startResult.Status);
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task DisposeRejectsQueuedStartAdmissionBeforeAnyBoundary()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        authorization.BlockReads();
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        RemoteWindowSessionController controller = CreateController(
            capture,
            authorization: authorization,
            input: input,
            sessions: sessions);
        Task<RemoteWindowCommandResult> gateHolder = Task.Run(async () =>
            await controller.AddParticipantAsync(
                Peer,
                MirrorParticipantRole.ViewOnly));
        await authorization.ReadEntered.Task;
        Task<RemoteWindowCommandResult> queuedStart = controller
            .StartAsync(SafeAt(Now))
            .AsTask();
        Task disposing = Task.Run(controller.Dispose);
        Assert.True(SpinWait.SpinUntil(
            () => IsDisposed(controller),
            TimeSpan.FromSeconds(5)));

        authorization.ReleaseReads();
        _ = await gateHolder;
        Exception? startFailure = await Record.ExceptionAsync(async () =>
            await queuedStart);
        await disposing;

        Assert.IsType<ObjectDisposedException>(startFailure);
        Assert.Equal(
            RemoteWindowLifecycle.Idle,
            controller.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            controller.Snapshot.CaptureState);
        Assert.Empty(capture.Events);
        Assert.False(capture.IsCapturing);
        Assert.True(input.IsAcceptingInput);
        Assert.Equal(0, sessions.DisconnectAllCallCount);

        static bool IsDisposed(RemoteWindowSessionController candidate)
        {
            try
            {
                _ = candidate.ApplyProtectionSnapshot(SafeAt(Now));
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }

    [Fact]
    public async Task DisposeFromStartBoundaryReturnsAndDefersFinalCleanup()
    {
        var capture = new RecordingCaptureBoundary();
        RemoteWindowSessionController controller = CreateController(capture);
        bool disposeReturned = false;
        capture.OnStartReturning = () =>
        {
            controller.Dispose();
            disposeReturned = true;
        };

        RemoteWindowCommandResult result = await Task.Run(async () =>
                await controller.StartAsync(SafeAt(Now)))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(disposeReturned);
        Assert.Equal(RemoteWindowCommandStatus.EmergencyStopped, result.Status);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await controller.StartAsync(SafeAt(Now)));
    }

    [Fact]
    public async Task DisposeDrainsAdmittedProtectionReconciliation()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.BlockResume();
        Task<RemoteWindowProtectionResult> applying = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        await capture.ResumeEntered.Task;
        Task disposing = Task.Run(controller.Dispose);
        await capture.EmergencyStopEntered.Task;
        int returnedBeforeReconciliationReleased = 0;
        Task observeDisposal = disposing.ContinueWith(
            _ =>
            {
                if (!capture.ResumeReleased)
                {
                    Interlocked.Exchange(
                        ref returnedBeforeReconciliationReleased,
                        1);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        capture.ReleaseResume();
        RemoteWindowProtectionResult result = await applying;
        await Task.WhenAll(disposing, observeDisposal);

        Assert.Equal(0, Volatile.Read(ref returnedBeforeReconciliationReleased));
        Assert.True(result.Blocked);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task DisposeDrainsAdmittedEmergencyStopAttempt()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockEmergencyStopCall(1);
        RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));
        Task<RemoteWindowEmergencyStopResult> stopping = Task.Run(
            controller.EmergencyStop);
        await capture.EmergencyStopEntered.Task;
        Task disposing = Task.Run(controller.Dispose);
        int returnedBeforeStopReleased = 0;
        Task observeDisposal = disposing.ContinueWith(
            _ =>
            {
                if (!capture.EmergencyStopReleased)
                {
                    Interlocked.Exchange(ref returnedBeforeStopReleased, 1);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        capture.ReleaseEmergencyStop();
        RemoteWindowEmergencyStopResult result = await stopping;
        await Task.WhenAll(disposing, observeDisposal);

        Assert.Equal(0, Volatile.Read(ref returnedBeforeStopReleased));
        Assert.True(result.FullyStopped);
        Assert.Equal(1, capture.EmergencyStopCallCount);
    }

    [Fact]
    public async Task DisposeRetriesAllBoundariesForUnavailableUnconfirmedCapture()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
            StopResult = LocalBoundaryResult.Failed("capture_stop_failed"),
        };
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        int inputStops = 0;
        input.OnStop = () => inputStops++;
        RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        RemoteWindowCommandResult failedStart =
            await controller.StartAsync(SafeAt(Now));
        capture.StopResult = LocalBoundaryResult.Confirmed("capture_stopped");

        controller.Dispose();

        Assert.Equal(RemoteWindowLifecycle.Unavailable, failedStart.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            failedStart.Snapshot.CaptureState);
        Assert.Equal(2, capture.StopCallCount);
        Assert.Equal(1, inputStops);
        Assert.Equal(1, sessions.DisconnectAllCallCount);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            controller.Snapshot.CaptureState);
    }

    [Fact]
    public async Task DisposeRetriesAllBoundariesForEndedUnconfirmedCapture()
    {
        var capture = new RecordingCaptureBoundary
        {
            StopResult = LocalBoundaryResult.Failed("capture_stop_failed"),
        };
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        int inputStops = 0;
        input.OnStop = () => inputStops++;
        RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        RemoteWindowStopResult failedStop = await controller.StopAsync();
        capture.StopResult = LocalBoundaryResult.Confirmed("capture_stopped");

        controller.Dispose();

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failedStop.Status);
        Assert.Equal(RemoteWindowLifecycle.Ended, failedStop.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            failedStop.Snapshot.CaptureState);
        Assert.Equal(2, capture.StopCallCount);
        Assert.Equal(2, inputStops);
        Assert.Equal(2, sessions.DisconnectAllCallCount);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            controller.Snapshot.CaptureState);
    }

    [Fact]
    public async Task DisposeRetriesEndedInputAndSessionBoundaryFailures()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary
        {
            StopResult = LocalBoundaryResult.Failed("input_stop_failed"),
        };
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectAllResult = LocalBoundaryResult.Failed(
                "session_disconnect_failed"),
        };
        RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        RemoteWindowStopResult failedStop = await controller.StopAsync();
        input.StopResult = LocalBoundaryResult.Confirmed("input_stopped");
        sessions.DisconnectAllResult =
            LocalBoundaryResult.Confirmed("sessions_disconnected");

        controller.Dispose();

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failedStop.Status);
        Assert.Equal(RemoteWindowCaptureState.Stopped, failedStop.Snapshot.CaptureState);
        Assert.Equal(2, capture.StopCallCount);
        Assert.Equal(2, input.StopCallCount);
        Assert.Equal(2, sessions.DisconnectAllCallCount);
    }

    [Fact]
    public async Task DisposeRetriesAllUnconfirmedEmergencyBoundaries()
    {
        var capture = new RecordingCaptureBoundary
        {
            EmergencyFailure = new IOException("capture stop failed"),
        };
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        int inputStops = 0;
        input.OnEmergencyStop = () => inputStops++;
        RemoteWindowSessionController controller = CreateController(
            capture,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        RemoteWindowEmergencyStopResult failedStop = controller.EmergencyStop();
        capture.EmergencyFailure = null;

        controller.Dispose();

        Assert.False(failedStop.FullyStopped);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            failedStop.Snapshot.CaptureState);
        Assert.Equal(2, capture.EmergencyStopCallCount);
        Assert.Equal(2, inputStops);
        Assert.Equal(2, sessions.DisconnectAllCallCount);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            controller.Snapshot.CaptureState);
    }

    [Fact]
    public async Task DisposeFromEmergencyBoundaryReturnsWithoutSelfWaiting()
    {
        var capture = new RecordingCaptureBoundary();
        RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));
        bool disposeReturned = false;
        capture.OnEmergencyStop = () =>
        {
            controller.Dispose();
            disposeReturned = true;
        };

        RemoteWindowEmergencyStopResult result = await Task.Run(
                controller.EmergencyStop)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(disposeReturned);
        Assert.True(result.FullyStopped);
        Assert.Equal(1, capture.EmergencyStopCallCount);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task DisposalEmergencyStopRejectsBoundaryReentry()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        var sessions = new RecordingSharingSessionBoundary();
        int inputStops = 0;
        input.OnEmergencyStop = () => inputStops++;
        RemoteWindowSessionController controller = CreateController(
            capture,
            authorization: authorization,
            input: input,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        RemoteWindowEmergencyStopResult? nestedStop = null;
        capture.OnEmergencyStop = () =>
        {
            capture.OnEmergencyStop = null;
            nestedStop = controller.EmergencyStop();
        };
        authorization.OnRead = controller.Dispose;

        RemoteWindowCommandResult result = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.ViewOnly);

        Assert.Equal(RemoteWindowCommandStatus.InvalidState, result.Status);
        Assert.NotNull(nestedStop);
        Assert.False(nestedStop.FullyStopped);
        Assert.Equal(1, capture.EmergencyStopCallCount);
        Assert.Equal(1, inputStops);
        Assert.Equal(1, sessions.DisconnectAllCallCount);
    }

    [Fact]
    public async Task DisposeFromProtectionBoundaryReturnsWithoutSelfWaiting()
    {
        var capture = new RecordingCaptureBoundary();
        RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        bool disposeReturned = false;
        capture.OnResume = () =>
        {
            controller.Dispose();
            disposeReturned = true;
        };

        RemoteWindowProtectionResult result = await Task.Run(() =>
                controller.ApplyProtectionSnapshot(SafeAt(Now)))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(disposeReturned);
        Assert.True(result.Blocked);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
        Assert.False(capture.IsCapturing);
    }

    [Fact]
    public async Task EmergencyStopDuringProtectionResumeCannotBeReopenedByStaleInputGate()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        RemoteWindowEmergencyStopResult? stopped = null;
        capture.OnResume = () =>
        {
            capture.OnResume = null;
            stopped = controller.EmergencyStop();
        };

        RemoteWindowProtectionResult staleResume =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        Assert.NotNull(stopped);
        Assert.True(stopped.FullyStopped);
        Assert.True(staleResume.Blocked);
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, controller.Snapshot.Lifecycle);
        Assert.False(input.IsAcceptingInput);
    }

    [Fact]
    public async Task EmergencyStopPreemptsLateInputBoundarySuccess()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var input = new RecordingInputBoundary();
        input.BlockInjection();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.DriverEligible);
        RemoteWindowCommandResult transferred = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));

        Task<RemoteInputAttemptResult> injecting = controller
            .InjectInputAsync(
                Peer,
                transferred.Snapshot.DriverLeaseEpoch!.Value,
                RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.1, 0.2)]))
            .AsTask();
        await input.InjectionEntered.Task;
        _ = controller.EmergencyStop();
        input.ReleaseInjection();
        RemoteInputAttemptResult result = await injecting;

        Assert.False(result.Injected);
        Assert.Equal(RemoteInputDecision.EmergencyStopped, result.Decision);
        Assert.Equal(RemoteWindowLifecycle.EmergencyStopped, result.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task ParticipantLimitRejectsSeventeenthDeviceWithoutMutation()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization);
        _ = await controller.StartAsync(SafeAt(Now));
        DeviceId[] peers = Enumerable.Range(2, 16)
            .Select(index => DeviceId.Parse(
                $"00000000-0000-0000-0000-{index.ToString("x12", System.Globalization.CultureInfo.InvariantCulture)}"))
            .ToArray();
        foreach (DeviceId peer in peers)
        {
            authorization.SetGrant(peer, CapabilityGrant.Of(Capability.MirrorView));
        }

        foreach (DeviceId peer in peers[..15])
        {
            RemoteWindowCommandResult admitted = await controller.AddParticipantAsync(
                peer,
                MirrorParticipantRole.ViewOnly);
            Assert.True(admitted.Succeeded);
        }

        RemoteWindowCommandResult rejected = await controller.AddParticipantAsync(
            peers[^1],
            MirrorParticipantRole.ViewOnly);

        Assert.Equal(RemoteWindowCommandStatus.ParticipantLimitReached, rejected.Status);
        Assert.Equal(RemoteWindowSessionController.MaximumParticipants, rejected.Snapshot.Participants.Count);
        Assert.DoesNotContain(peers[^1], rejected.Snapshot.Participants.Keys);
    }

    [Fact]
    public async Task UnconfirmedPeerDisconnectsConsumeTheBoundedParticipantBudget()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectPeerResult =
                LocalBoundaryResult.Failed("peer_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        DeviceId[] peers = Enumerable.Range(2, 16)
            .Select(index => DeviceId.Parse(
                $"00000000-0000-0000-0000-{index.ToString("x12", System.Globalization.CultureInfo.InvariantCulture)}"))
            .ToArray();

        foreach (DeviceId peer in peers[..15])
        {
            authorization.SetGrant(peer, CapabilityGrant.Of(Capability.MirrorView));
            RemoteWindowCommandResult admitted = await controller.AddParticipantAsync(
                peer,
                MirrorParticipantRole.ViewOnly);
            authorization.SetGrant(peer, CapabilityGrant.None);
            RemoteWindowCommandResult revoked =
                await controller.ReconcilePeerCapabilitiesAsync(peer);

            Assert.True(admitted.Succeeded);
            Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, revoked.Status);
            Assert.DoesNotContain(peer, revoked.Snapshot.Participants.Keys);
        }

        authorization.SetGrant(peers[^1], CapabilityGrant.Of(Capability.MirrorView));
        RemoteWindowCommandResult exhausted = await controller.AddParticipantAsync(
            peers[^1],
            MirrorParticipantRole.ViewOnly);

        Assert.Equal(
            RemoteWindowCommandStatus.ParticipantLimitReached,
            exhausted.Status);
        Assert.Single(exhausted.Snapshot.Participants);
        Assert.DoesNotContain(peers[^1], exhausted.Snapshot.Participants.Keys);

        sessions.DisconnectPeerResult =
            LocalBoundaryResult.Confirmed("peer_disconnected");
        RemoteWindowCommandResult cleaned =
            await controller.DisconnectParticipantAsync(peers[0]);
        RemoteWindowCommandResult admittedAfterCleanup =
            await controller.AddParticipantAsync(
                peers[^1],
                MirrorParticipantRole.ViewOnly);

        Assert.True(cleaned.Succeeded);
        Assert.True(admittedAfterCleanup.Succeeded);
        Assert.Contains(peers[^1], admittedAfterCleanup.Snapshot.Participants.Keys);
    }

    [Fact]
    public async Task ViewRevocationRemovesPeerBeforeLocalDisconnect()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var sessions = new RecordingSharingSessionBoundary();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        _ = await controller.AddParticipantAsync(Peer, MirrorParticipantRole.ViewOnly);
        authorization.SetGrant(Peer, CapabilityGrant.None);

        RemoteWindowCommandResult result =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(Peer, result.Snapshot.Participants.Keys);
        Assert.Equal([Peer], sessions.DisconnectedPeers);
        Assert.NotNull(sessions.SnapshotObservedAtPeerDisconnect);
        Assert.DoesNotContain(
            Peer,
            sessions.SnapshotObservedAtPeerDisconnect.Participants.Keys);
    }

    [Fact]
    public async Task ViewRevocationRetriesUnconfirmedLocalDisconnect()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectPeerResult =
                LocalBoundaryResult.Failed("peer_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        _ = await controller.AddParticipantAsync(Peer, MirrorParticipantRole.ViewOnly);
        authorization.SetGrant(Peer, CapabilityGrant.None);

        RemoteWindowCommandResult failed =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);
        sessions.DisconnectPeerResult =
            LocalBoundaryResult.Confirmed("peer_disconnected");
        RemoteWindowCommandResult retried =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);
        RemoteWindowCommandResult completed =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failed.Status);
        Assert.DoesNotContain(Peer, failed.Snapshot.Participants.Keys);
        Assert.Equal(RemoteWindowCommandStatus.Applied, retried.Status);
        Assert.Equal(RemoteWindowCommandStatus.AlreadyApplied, completed.Status);
        Assert.Equal([Peer, Peer], sessions.DisconnectedPeers);
    }

    [Fact]
    public async Task PendingDisconnectBlocksRegrantUntilCleanupConfirms()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var sessions = new RecordingSharingSessionBoundary
        {
            DisconnectPeerResult =
                LocalBoundaryResult.Failed("peer_disconnect_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            sessions: sessions);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));
        _ = await controller.AddParticipantAsync(Peer, MirrorParticipantRole.ViewOnly);
        authorization.SetGrant(Peer, CapabilityGrant.None);
        RemoteWindowCommandResult failedCleanup =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);
        authorization.SetGrant(Peer, CapabilityGrant.Of(Capability.MirrorView));

        RemoteWindowCommandResult blocked = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.ViewOnly);

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failedCleanup.Status);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, blocked.Status);
        Assert.Equal("peer_disconnect_pending", blocked.ReasonCode);
        Assert.DoesNotContain(Peer, blocked.Snapshot.Participants.Keys);
        Assert.Equal([Peer], sessions.DisconnectedPeers);

        sessions.DisconnectPeerResult =
            LocalBoundaryResult.Confirmed("peer_disconnected");
        RemoteWindowCommandResult cleaned =
            await controller.ReconcilePeerCapabilitiesAsync(Peer);
        RemoteWindowCommandResult admitted = await controller.AddParticipantAsync(
            Peer,
            MirrorParticipantRole.ViewOnly);

        Assert.True(cleaned.Succeeded);
        Assert.True(admitted.Succeeded);
        Assert.Contains(Peer, admitted.Snapshot.Participants.Keys);
        Assert.Equal([Peer, Peer], sessions.DisconnectedPeers);
    }

    [Fact]
    public async Task SafeProtectionResumesOnlyAfterBothLocalGatesConfirm()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.ProtectedContent,
            Now,
            "test-probe"));
        input.ResumeFailure = new IOException("private input resume failure");

        RemoteWindowProtectionResult failed =
            controller.ApplyProtectionSnapshot(SafeAt(Now));
        input.ResumeFailure = null;
        RemoteWindowProtectionResult resumed =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        Assert.True(failed.Blocked);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failed.Status);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, failed.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Unconfirmed, failed.Snapshot.CaptureState);
        Assert.False(resumed.Blocked);
        Assert.True(resumed.LocalGatesConfirmed);
        Assert.Equal(RemoteWindowLifecycle.Active, resumed.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Capturing, resumed.Snapshot.CaptureState);
    }

    [Fact]
    public async Task PartialResumeFailureReclosesBothLocalGates()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary
        {
            ResumeFailure = new IOException("private input resume failure"),
        };
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.ProtectedContent,
            Now,
            "test-probe"));

        RemoteWindowProtectionResult failed =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        Assert.True(failed.Blocked);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failed.Status);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, controller.Snapshot.Lifecycle);
        Assert.False(capture.IsCapturing);
        Assert.False(input.IsAcceptingInput);
        Assert.Equal("capture.pause", capture.Events[^1]);
        Assert.Equal("input.pause", input.Events[^1]);
    }

    [Fact]
    public async Task SeededControlTransitionsNeverReauthorizeRetiredEpochs()
    {
        for (int seed = 0; seed < 16; seed++)
        {
            var random = new Random(seed);
            var clock = new MutableClock(Now);
            var authorization = new MutableMirrorAuthorizationSource();
            var input = new RecordingInputBoundary();
            using RemoteWindowSessionController controller = CreateController(
                new RecordingCaptureBoundary(),
                authorization: authorization,
                input: input,
                clock: clock);
            _ = await controller.StartAsync(SafeAt(clock.UtcNow));
            var retired = new HashSet<(DeviceId DeviceId, long Epoch)>();
            long previousRevision = controller.Snapshot.Revision;

            for (int eventIndex = 0; eventIndex < 48; eventIndex++)
            {
                RemoteWindowSharingSnapshot before = controller.Snapshot;
                (DeviceId? Holder, long? Epoch) previous = (
                    before.CurrentDriverDeviceId,
                    before.DriverLeaseEpoch);
                switch (random.Next(7))
                {
                    case 0:
                        authorization.SetGrant(
                            Peer,
                            CapabilityGrant.Of(
                                Capability.MirrorView,
                                Capability.MirrorDrive));
                        _ = await controller.AddParticipantAsync(
                            Peer,
                            MirrorParticipantRole.DriverEligible);
                        break;
                    case 1:
                        if (controller.Snapshot.Participants.TryGetValue(
                                Peer,
                                out MirrorParticipantRole role)
                            && role == MirrorParticipantRole.DriverEligible)
                        {
                            _ = await controller.TransferDriverAsync(
                                Peer,
                                TimeSpan.FromSeconds(random.Next(1, 5)));
                        }

                        break;
                    case 2:
                        _ = await controller.TransferDriverAsync(
                            Host,
                            TimeSpan.FromSeconds(random.Next(1, 5)));
                        break;
                    case 3:
                        authorization.SetGrant(
                            Peer,
                            CapabilityGrant.Of(Capability.MirrorView));
                        _ = await controller.ReconcilePeerCapabilitiesAsync(Peer);
                        break;
                    case 4:
                        authorization.SetGrant(Peer, CapabilityGrant.None);
                        _ = await controller.ReconcilePeerCapabilitiesAsync(Peer);
                        break;
                    case 5:
                        clock.UtcNow = clock.UtcNow.AddSeconds(random.Next(1, 5));
                        _ = await controller.RefreshExpiredLeaseAsync();
                        break;
                    default:
                        ProtectionKind kind = random.Next(2) == 0
                            ? ProtectionKind.Safe
                            : ProtectionKind.SecureInput;
                        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
                            kind,
                            clock.UtcNow,
                            "property-probe"));
                        break;
                }

                RemoteWindowSharingSnapshot after = controller.Snapshot;
                if (previous.Holder is not null
                    && previous.Epoch is not null
                    && (after.CurrentDriverDeviceId != previous.Holder
                        || after.DriverLeaseEpoch != previous.Epoch))
                {
                    retired.Add((previous.Holder, previous.Epoch.Value));
                }

                Assert.True(
                    after.Revision >= previousRevision,
                    $"seed={seed}, event={eventIndex}, revision regressed");
                previousRevision = after.Revision;
                Assert.InRange(
                    after.Participants.Count,
                    1,
                    RemoteWindowSessionController.MaximumParticipants);
                Assert.Equal(
                    MirrorParticipantRole.DriverEligible,
                    after.Participants[Host]);
                if (after.CurrentDriverDeviceId is not null)
                {
                    Assert.Equal(
                        MirrorParticipantRole.DriverEligible,
                        after.Participants[after.CurrentDriverDeviceId]);
                }

                _ = controller.ApplyProtectionSnapshot(SafeAt(clock.UtcNow));
                foreach ((DeviceId retiredDevice, long retiredEpoch) in retired)
                {
                    RemoteInputAttemptResult attempt = await controller.InjectInputAsync(
                        retiredDevice,
                        retiredEpoch,
                        RemoteInputBatch.Create([
                            RemoteInputEvent.PointerMove(0.5, 0.5),
                        ]));
                    Assert.False(
                        attempt.Injected,
                        $"seed={seed}, event={eventIndex}, retired={retiredDevice}/{retiredEpoch}");
                }
            }

            Assert.Empty(input.Batches);
        }
    }

    [Fact]
    public async Task UnsafeInitialProtectionNeverCallsCaptureOrPublishesSharing()
    {
        var capture = new RecordingCaptureBoundary();
        using RemoteWindowSessionController controller = CreateController(capture);

        RemoteWindowCommandResult result = await controller.StartAsync(
            new ProtectionSnapshot(
                ProtectionKind.Unknown,
                Now,
                "test-probe"));

        Assert.Equal(RemoteWindowCommandStatus.ProtectionBlocked, result.Status);
        Assert.Equal(RemoteWindowLifecycle.Idle, result.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Stopped, result.Snapshot.CaptureState);
        Assert.False(result.Snapshot.IsSharing);
        Assert.Empty(capture.Events);
    }

    [Fact]
    public async Task CaptureAndInputExceptionsBecomePayloadFreeBoundaryFailures()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartFailure = new IOException("FLOWSPAN_PRIVATE_START_FAILURE"),
        };
        using RemoteWindowSessionController unavailable = CreateController(capture);

        RemoteWindowCommandResult start = await unavailable.StartAsync(SafeAt(Now));

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, start.Status);
        Assert.Equal("local_boundary_exception", start.ReasonCode);
        Assert.Equal(RemoteWindowLifecycle.Unavailable, start.Snapshot.Lifecycle);
        Assert.False(start.Snapshot.IsSharing);
        Assert.DoesNotContain(
            "FLOWSPAN_PRIVATE_START_FAILURE",
            start.ToString(),
            StringComparison.Ordinal);

        var authorization = new MutableMirrorAuthorizationSource();
        var input = new RecordingInputBoundary
        {
            InjectionFailure = new IOException("FLOWSPAN_PRIVATE_INPUT_FAILURE"),
        };
        using RemoteWindowSessionController active = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            input: input);
        _ = await active.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await active.AddParticipantAsync(Peer, MirrorParticipantRole.DriverEligible);
        RemoteWindowCommandResult transferred = await active.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));

        RemoteInputAttemptResult attempt = await active.InjectInputAsync(
            Peer,
            transferred.Snapshot.DriverLeaseEpoch!.Value,
            RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.1, 0.2)]));

        Assert.Equal(RemoteInputDecision.BoundaryFailed, attempt.Decision);
        Assert.False(attempt.Injected);
        Assert.Equal("local_boundary_exception", attempt.Boundary?.ReasonCode);
        Assert.DoesNotContain(
            "FLOWSPAN_PRIVATE_INPUT_FAILURE",
            attempt.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedStartWithConfirmedCleanupCanBeLocallyResetBeforeRetry()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartFailure = new IOException("private initial failure"),
        };
        using RemoteWindowSessionController controller = CreateController(capture);
        RemoteWindowCommandResult failedStart =
            await controller.StartAsync(SafeAt(Now));
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();
        capture.StartFailure = null;
        RemoteWindowCommandResult retried =
            await controller.StartAsync(SafeAt(Now));

        Assert.Equal(RemoteWindowLifecycle.Unavailable, failedStart.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            failedStart.Snapshot.CaptureState);
        Assert.Empty(failedStart.Snapshot.Participants);
        Assert.Null(failedStart.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(RemoteWindowCommandStatus.Applied, reset.Status);
        Assert.Equal(RemoteWindowLifecycle.Idle, reset.Snapshot.Lifecycle);
        Assert.Empty(reset.Snapshot.Participants);
        Assert.Null(reset.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(RemoteWindowCommandStatus.Applied, retried.Status);
        Assert.Equal(RemoteWindowLifecycle.Active, retried.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task FailedStartWithUnconfirmedCleanupCannotBeLocallyReset()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
            StopResult = LocalBoundaryResult.Failed("capture_stop_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(capture);

        RemoteWindowCommandResult failedStart =
            await controller.StartAsync(SafeAt(Now));
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failedStart.Status);
        Assert.Equal("capture_start_failed", failedStart.ReasonCode);
        Assert.Equal("capture_start_failed", failedStart.Boundary?.ReasonCode);
        Assert.Equal(
            "capture_stop_failed",
            failedStart.CleanupBoundary?.ReasonCode);
        Assert.Equal(RemoteWindowLifecycle.Unavailable, failedStart.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            failedStart.Snapshot.CaptureState);
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, reset.Status);
        Assert.Equal("unavailable_stop_unconfirmed", reset.ReasonCode);
        Assert.Equal(RemoteWindowLifecycle.Unavailable, reset.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task ThrownStartAndCleanupFailuresHaveSeparatePayloadFreeResults()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartFailure = new IOException("FLOWSPAN_PRIVATE_START_FAILURE"),
            StopFailure = new IOException("FLOWSPAN_PRIVATE_CLEANUP_FAILURE"),
        };
        using RemoteWindowSessionController controller = CreateController(capture);

        RemoteWindowCommandResult failedStart =
            await controller.StartAsync(SafeAt(Now));

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, failedStart.Status);
        Assert.Equal("local_boundary_exception", failedStart.Boundary?.ReasonCode);
        Assert.Equal(
            "local_boundary_exception",
            failedStart.CleanupBoundary?.ReasonCode);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            failedStart.Snapshot.CaptureState);
        Assert.DoesNotContain(
            "FLOWSPAN_PRIVATE_START_FAILURE",
            failedStart.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FLOWSPAN_PRIVATE_CLEANUP_FAILURE",
            failedStart.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalDriverTransferWaitsForAdmittedInputToFinish()
    {
        var authorization = new MutableMirrorAuthorizationSource();
        var input = new RecordingInputBoundary();
        input.BlockInjection();
        using RemoteWindowSessionController controller = CreateController(
            new RecordingCaptureBoundary(),
            authorization: authorization,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        authorization.SetGrant(
            Peer,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        _ = await controller.AddParticipantAsync(Peer, MirrorParticipantRole.DriverEligible);
        RemoteWindowCommandResult peerDriver = await controller.TransferDriverAsync(
            Peer,
            TimeSpan.FromSeconds(10));
        Task<RemoteInputAttemptResult> injecting = controller.InjectInputAsync(
                Peer,
                peerDriver.Snapshot.DriverLeaseEpoch!.Value,
                RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.1, 0.2)]))
            .AsTask();
        await input.InjectionEntered.Task;

        Task<RemoteWindowCommandResult> transferring = controller
            .TransferDriverAsync(Host, TimeSpan.FromSeconds(10))
            .AsTask();

        Assert.False(transferring.IsCompleted);
        input.ReleaseInjection();
        Assert.True((await injecting).Injected);
        RemoteWindowCommandResult hostDriver = await transferring;
        Assert.Equal(Host, hostDriver.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(3, hostDriver.Snapshot.DriverLeaseEpoch);
    }

    [Fact]
    public async Task CancellingPendingCaptureStartAttemptsFailClosedCleanup()
    {
        var capture = new RecordingCaptureBoundary
        {
            StopResult = LocalBoundaryResult.Failed("capture_stop_failed"),
        };
        capture.BlockStart();
        using var cancellation = new CancellationTokenSource();
        using RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now), cancellation.Token)
            .AsTask();
        await capture.StartEntered.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await starting);
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(RemoteWindowLifecycle.Ended, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Unconfirmed, controller.Snapshot.CaptureState);
        Assert.False(controller.Snapshot.IsSharing);
    }

    [Fact]
    public async Task CancelledCaptureAdmissionCannotApplyIgnoredSuccessfulCancellation()
    {
        var capture = new RecordingCaptureBoundary();
        using var cancellation = new CancellationTokenSource();
        capture.OnStartReturning = cancellation.Cancel;
        using RemoteWindowSessionController controller = CreateController(capture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await controller.StartAsync(SafeAt(Now), cancellation.Token));

        Assert.Equal(1, capture.StopCallCount);
        Assert.False(capture.IsCapturing);
        Assert.Equal(RemoteWindowLifecycle.Ended, controller.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            controller.Snapshot.CaptureState);
        Assert.False(controller.Snapshot.IsSharing);
    }

    [Fact]
    public async Task CancelledCaptureAdmissionCannotReturnIgnoredBoundaryFailure()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
        };
        using var cancellation = new CancellationTokenSource();
        capture.OnStartReturning = cancellation.Cancel;
        using RemoteWindowSessionController controller = CreateController(capture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await controller.StartAsync(SafeAt(Now), cancellation.Token));

        Assert.Equal(1, capture.StopCallCount);
        Assert.False(capture.IsCapturing);
        Assert.Equal(RemoteWindowLifecycle.Ended, controller.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            controller.Snapshot.CaptureState);
        Assert.False(controller.Snapshot.IsSharing);
    }

    [Fact]
    public async Task CancelledCaptureAdmissionInvalidatesEarlierEmergencyStopProof()
    {
        var capture = new RecordingCaptureBoundary
        {
            StopResult = LocalBoundaryResult.Failed("capture_stop_failed"),
        };
        capture.BlockStart();
        using var cancellation = new CancellationTokenSource();
        using RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now), cancellation.Token)
            .AsTask();
        await capture.StartEntered.Task;
        RemoteWindowEmergencyStopResult initialStop = controller.EmergencyStop();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await starting);
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.True(initialStop.FullyStopped);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            controller.Snapshot.CaptureState);
        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, reset.Status);
        Assert.Equal("emergency_boundaries_unconfirmed", reset.ReasonCode);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            reset.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task SuccessfulCancellationCleanupReconfirmsCurrentStopGeneration()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockStart();
        using var cancellation = new CancellationTokenSource();
        using RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now), cancellation.Token)
            .AsTask();
        await capture.StartEntered.Task;
        RemoteWindowEmergencyStopResult initialStop = controller.EmergencyStop();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await starting);
        RemoteWindowCommandResult reset =
            await controller.ResetAfterLocalConfirmationAsync();

        Assert.True(initialStop.FullyStopped);
        Assert.Equal(RemoteWindowCommandStatus.Applied, reset.Status);
        Assert.Equal(RemoteWindowLifecycle.Idle, reset.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            reset.Snapshot.CaptureState);
    }

    [Fact]
    public async Task CancellingPendingCaptureStartPublishesConfirmedCleanup()
    {
        var capture = new RecordingCaptureBoundary();
        capture.BlockStart();
        using var cancellation = new CancellationTokenSource();
        using RemoteWindowSessionController controller = CreateController(capture);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now), cancellation.Token)
            .AsTask();
        await capture.StartEntered.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await starting);
        Assert.Equal(1, capture.StopCallCount);
        Assert.False(capture.IsCapturing);
        Assert.Equal(RemoteWindowLifecycle.Ended, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Stopped, controller.Snapshot.CaptureState);
        Assert.Empty(controller.Snapshot.Participants);
        Assert.Null(controller.Snapshot.CurrentDriverDeviceId);
    }

    [Fact]
    public async Task CancellationCleansUpAfterPreAdmissionResumeIsBlocked()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        capture.BlockStart();
        using var cancellation = new CancellationTokenSource();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        Task<RemoteWindowCommandResult> starting = controller
            .StartAsync(SafeAt(Now), cancellation.Token)
            .AsTask();
        await capture.StartEntered.Task;
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        RemoteWindowProtectionResult safeWhilePending =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await starting);
        RemoteWindowSharingSnapshot pending = controller.Snapshot;
        RemoteWindowCommandResult prematureRetry =
            await controller.StartAsync(SafeAt(Now));

        Assert.True(safeWhilePending.Blocked);
        Assert.DoesNotContain("capture.resume", capture.Events);
        Assert.Equal(RemoteWindowLifecycle.Ended, pending.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Stopped, pending.CaptureState);
        Assert.Equal(RemoteWindowCommandStatus.InvalidState, prematureRetry.Status);
        Assert.Empty(pending.Participants);
        Assert.Null(pending.CurrentDriverDeviceId);
        Assert.False(capture.IsCapturing);
        Assert.False(input.IsAcceptingInput);
        Assert.Equal(1, capture.StopCallCount);
    }

    [Fact]
    public async Task EmergencyStopDuringFailedStartCleanupRemainsTerminal()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
        };
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        capture.OnStop = () =>
        {
            capture.OnStop = null;
            _ = controller.EmergencyStop();
        };

        RemoteWindowCommandResult failedStart =
            await controller.StartAsync(SafeAt(Now));

        Assert.Equal(RemoteWindowCommandStatus.EmergencyStopped, failedStart.Status);
        Assert.Equal("capture_start_failed", failedStart.Boundary?.ReasonCode);
        Assert.True(failedStart.CleanupBoundary?.Succeeded);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            failedStart.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            failedStart.Snapshot.CaptureState);
        Assert.Null(failedStart.Snapshot.CurrentDriverDeviceId);
        Assert.False(capture.IsCapturing);
        Assert.False(input.IsAcceptingInput);
    }

    [Fact]
    public async Task EmergencyBeforeFailedStartPreservesAdmissionAndCleanupBoundaries()
    {
        var capture = new RecordingCaptureBoundary
        {
            StartResult = LocalBoundaryResult.Failed("capture_start_failed"),
        };
        using RemoteWindowSessionController controller = CreateController(capture);
        capture.OnStartReturning = () =>
        {
            capture.OnStartReturning = null;
            _ = controller.EmergencyStop();
        };

        RemoteWindowCommandResult failedStart =
            await controller.StartAsync(SafeAt(Now));

        Assert.Equal(RemoteWindowCommandStatus.EmergencyStopped, failedStart.Status);
        Assert.Equal("capture_start_failed", failedStart.Boundary?.ReasonCode);
        Assert.Equal(
            "capture_emergency_stopped",
            failedStart.CleanupBoundary?.ReasonCode);
        Assert.True(failedStart.CleanupBoundary?.Succeeded);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            failedStart.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            failedStart.Snapshot.CaptureState);
    }

    [Fact]
    public async Task NewerUnsafeProtectionCannotBeOverriddenByReentrantSafeResume()
    {
        var capture = new RecordingCaptureBoundary();
        using RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        RemoteWindowProtectionResult? newerUnsafe = null;
        capture.OnResume = () =>
        {
            capture.OnResume = null;
            newerUnsafe = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-probe"));
        };

        RemoteWindowProtectionResult olderSafe =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        Assert.NotNull(newerUnsafe);
        Assert.True(newerUnsafe.Blocked);
        Assert.True(olderSafe.Blocked);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Paused, controller.Snapshot.CaptureState);
        Assert.Equal(ProtectionKind.SecureInput, controller.Snapshot.ProtectionKind);
    }

    [Fact]
    public async Task ConcurrentNewerUnsafeProtectionWinsAgainstBlockedSafeResume()
    {
        var capture = new RecordingCaptureBoundary();
        var input = new RecordingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(
            capture,
            input: input);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        capture.BlockResume();
        Task<RemoteWindowProtectionResult> olderSafe = Task.Run(() =>
            controller.ApplyProtectionSnapshot(SafeAt(Now)));
        await capture.ResumeEntered.Task;

        RemoteWindowProtectionResult newerUnsafe =
            controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-probe"));
        capture.ReleaseResume();
        RemoteWindowProtectionResult completedOlderSafe = await olderSafe;

        Assert.True(newerUnsafe.Blocked);
        Assert.True(completedOlderSafe.Blocked);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, controller.Snapshot.Lifecycle);
        Assert.Equal(RemoteWindowCaptureState.Paused, controller.Snapshot.CaptureState);
        Assert.Equal(ProtectionKind.SecureInput, controller.Snapshot.ProtectionKind);
        Assert.False(input.IsAcceptingInput);
    }

    [Fact]
    public async Task ReentrantProtectionChurnIsBoundedAndFailsClosed()
    {
        var capture = new RecordingCaptureBoundary();
        using RemoteWindowSessionController controller = CreateController(capture);
        _ = await controller.StartAsync(SafeAt(Now));
        _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now,
            "test-probe"));
        int remainingTransitions = 64;
        capture.OnResume = () =>
        {
            if (remainingTransitions == 0)
            {
                return;
            }

            remainingTransitions--;
            _ = controller.ApplyProtectionSnapshot(new ProtectionSnapshot(
                ProtectionKind.SecureInput,
                Now,
                "test-probe"));
        };
        capture.OnPause = () =>
        {
            if (remainingTransitions == 0)
            {
                return;
            }

            remainingTransitions--;
            _ = controller.ApplyProtectionSnapshot(SafeAt(Now));
        };
        int callsBeforeChurn = capture.Events.Count;

        RemoteWindowProtectionResult result =
            controller.ApplyProtectionSnapshot(SafeAt(Now));

        Assert.Equal(RemoteWindowCommandStatus.BoundaryFailed, result.Status);
        Assert.True(result.Blocked);
        Assert.Equal(MirrorPauseReason.ProtectionStateUnknown, result.PauseReason);
        Assert.True(remainingTransitions > 0);
        Assert.InRange(capture.Events.Count - callsBeforeChurn, 1, 12);
        Assert.Equal(RemoteWindowLifecycle.ProtectionPaused, controller.Snapshot.Lifecycle);
        Assert.Equal(
            RemoteWindowCaptureState.Unconfirmed,
            controller.Snapshot.CaptureState);
    }

    private static RemoteWindowSessionController CreateController(
        IRemoteWindowCaptureBoundary capture,
        string payload = "test",
        MutableMirrorAuthorizationSource? authorization = null,
        RecordingInputBoundary? input = null,
        RecordingSharingSessionBoundary? sessions = null,
        MutableClock? clock = null)
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            Activity,
            ActivityKind.Parse("workspace.note/v1"),
            Host,
            "Test note",
            JsonSerializer.Serialize(new { text = payload }));
        ActivityInstance activity = ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(Host));
        clock ??= new MutableClock(Now);
        authorization ??= new MutableMirrorAuthorizationSource();
        input ??= new RecordingInputBoundary();
        sessions ??= new RecordingSharingSessionBoundary();
        RemoteWindowSessionController? controller = null;
        controller = new RemoteWindowSessionController(
            Host,
            activity,
            clock,
            authorization,
            capture,
            input,
            sessions,
            TimeSpan.FromSeconds(10));
        if (capture is RecordingCaptureBoundary recording)
        {
            recording.Snapshot = () => controller.Snapshot;
        }

        input.Snapshot = () => controller.Snapshot;
        sessions.Snapshot = () => controller.Snapshot;

        return controller;
    }

    private static ProtectionSnapshot SafeAt(DateTimeOffset observedAt) => new(
        ProtectionKind.Safe,
        observedAt,
        "test-probe");

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        private DateTimeOffset utcNow = utcNow;

        public Action? OnRead { get; set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Action? callback = OnRead;
                OnRead = null;
                callback?.Invoke();
                return utcNow;
            }

            set => utcNow = value;
        }
    }

    private sealed class MutableMirrorAuthorizationSource : IMirrorAuthorizationSource
    {
        private readonly Dictionary<DeviceId, CapabilityGrant> grants = [];
        private TaskCompletionSource? releaseReads;

        public int ReadCount { get; private set; }

        public Action? OnRead { get; set; }

        public TaskCompletionSource ReadEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId)
        {
            ReadCount++;
            Action? callback = OnRead;
            OnRead = null;
            callback?.Invoke();
            ReadEntered.TrySetResult();
            releaseReads?.Task.GetAwaiter().GetResult();
            return grants.TryGetValue(peerDeviceId, out CapabilityGrant? grant)
                ? grant
                : CapabilityGrant.None;
        }

        public void BlockReads() => releaseReads = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseReads() => releaseReads?.TrySetResult();

        public void SetGrant(DeviceId peerDeviceId, CapabilityGrant grant) =>
            grants[peerDeviceId] = grant;
    }

    private sealed class RecordingCaptureBoundary : IRemoteWindowCaptureBoundary
    {
        private int? blockedEmergencyStopCall;
        private TaskCompletionSource? releaseEmergencyStop;
        private TaskCompletionSource? releaseResume;
        private TaskCompletionSource? releaseStart;
        private int resumeCallCount;

        public List<string> Events { get; } = [];

        public List<string> BoundaryTimeline { get; } = [];

        public TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource EmergencyStopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ResumeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondResumeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool EmergencyStopReleased =>
            releaseEmergencyStop?.Task.IsCompleted ?? true;

        public bool ResumeReleased => releaseResume?.Task.IsCompleted ?? true;

        public Func<RemoteWindowSharingSnapshot>? Snapshot { get; set; }

        public Action? OnEmergencyStop { get; set; }

        public Action? OnPause { get; set; }

        public Action? OnStartReturning { get; set; }

        public Exception? EmergencyFailure { get; set; }

        public int EmergencyStopCallCount { get; private set; }

        public Exception? StartFailure { get; set; }

        public LocalBoundaryResult StartResult { get; set; } =
            LocalBoundaryResult.Confirmed("capture_started");

        public Action? OnStop { get; set; }

        public Action? OnResume { get; set; }

        public Exception? StopFailure { get; set; }

        public LocalBoundaryResult StopResult { get; set; } =
            LocalBoundaryResult.Confirmed("capture_stopped");

        public int StopCallCount { get; private set; }

        public bool IsCapturing { get; private set; }

        public RemoteWindowLifecycle? LifecycleObservedAtStart { get; private set; }

        public RemoteWindowLifecycle? LifecycleObservedAtPause { get; private set; }

        public async ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken)
        {
            Events.Add("capture.start");
            LifecycleObservedAtStart = Snapshot?.Invoke().Lifecycle;
            StartEntered.TrySetResult();
            if (releaseStart is not null)
            {
                await releaseStart.Task.WaitAsync(cancellationToken);
            }

            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            OnStartReturning?.Invoke();
            IsCapturing = StartResult.Succeeded;
            return StartResult;
        }

        public void BlockStart() => releaseStart = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseStart() => releaseStart?.TrySetResult();

        public void BlockResume() => releaseResume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseResume() => releaseResume?.TrySetResult();

        public void BlockEmergencyStopCall(int call)
        {
            blockedEmergencyStopCall = call;
            releaseEmergencyStop = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseEmergencyStop() => releaseEmergencyStop?.TrySetResult();

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason)
        {
            Events.Add("capture.pause");
            LifecycleObservedAtPause = Snapshot?.Invoke().Lifecycle;
            IsCapturing = false;
            OnPause?.Invoke();
            return LocalBoundaryResult.Confirmed("capture_paused");
        }

        public LocalBoundaryResult ResumeNow()
        {
            Events.Add("capture.resume");
            BoundaryTimeline.Add("capture.resume.enter");
            int currentResumeCall = Interlocked.Increment(ref resumeCallCount);
            ResumeEntered.TrySetResult();
            if (currentResumeCall >= 2)
            {
                SecondResumeEntered.TrySetResult();
            }

            releaseResume?.Task.GetAwaiter().GetResult();
            IsCapturing = true;
            BoundaryTimeline.Add("capture.resume.return");
            OnResume?.Invoke();
            return LocalBoundaryResult.Confirmed("capture_resumed");
        }

        public LocalBoundaryResult EmergencyStopNow()
        {
            EmergencyStopCallCount++;
            BoundaryTimeline.Add("capture.emergency_stop");
            EmergencyStopEntered.TrySetResult();
            if (EmergencyStopCallCount == blockedEmergencyStopCall)
            {
                releaseEmergencyStop?.Task.GetAwaiter().GetResult();
            }

            IsCapturing = false;
            OnEmergencyStop?.Invoke();
            if (EmergencyFailure is not null)
            {
                throw EmergencyFailure;
            }

            return LocalBoundaryResult.Confirmed("capture_emergency_stopped");
        }

        public LocalBoundaryResult StopNow()
        {
            StopCallCount++;
            BoundaryTimeline.Add("capture.stop");
            IsCapturing = false;
            OnStop?.Invoke();
            if (StopFailure is not null)
            {
                throw StopFailure;
            }

            return StopResult;
        }
    }

    private sealed class ReenteringEmergencyStopCaptureBoundary :
        IRemoteWindowCaptureBoundary
    {
        private int callDepth;

        public Func<RemoteWindowEmergencyStopResult>? Reenter { get; set; }

        public RemoteWindowEmergencyStopResult? ReentrantResult { get; private set; }

        public int EmergencyStopCallCount { get; private set; }

        public int MaximumCallDepth { get; private set; }

        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            LocalBoundaryResult.Confirmed("capture_started"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public LocalBoundaryResult EmergencyStopNow()
        {
            EmergencyStopCallCount++;
            callDepth++;
            MaximumCallDepth = Math.Max(MaximumCallDepth, callDepth);
            try
            {
                if (callDepth == 1)
                {
                    ReentrantResult = Reenter!();
                }

                return LocalBoundaryResult.Confirmed("capture_emergency_stopped");
            }
            finally
            {
                callDepth--;
            }
        }

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class RecordingInputBoundary : IRemoteInputBoundary
    {
        private TaskCompletionSource? releaseInjection;

        public List<RemoteInputBatch> Batches { get; } = [];

        public List<string> Events { get; } = [];

        public TaskCompletionSource InjectionEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<RemoteWindowSharingSnapshot>? Snapshot { get; set; }

        public Action? OnEmergencyStop { get; set; }

        public Action? OnStop { get; set; }

        public Exception? InjectionFailure { get; set; }

        public Exception? ResumeFailure { get; set; }

        public LocalBoundaryResult EmergencyStopResult { get; set; } =
            LocalBoundaryResult.Confirmed("input_emergency_stopped");

        public int StopCallCount { get; private set; }

        public LocalBoundaryResult StopResult { get; set; } =
            LocalBoundaryResult.Confirmed("input_stopped");

        public bool IsAcceptingInput { get; private set; } = true;

        public RemoteWindowLifecycle? LifecycleObservedAtPause { get; private set; }

        public async ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            InjectionEntered.TrySetResult();
            if (releaseInjection is not null)
            {
                await releaseInjection.Task.WaitAsync(cancellationToken);
            }
            if (InjectionFailure is not null)
            {
                throw InjectionFailure;
            }

            return LocalBoundaryResult.Confirmed("input_injected");
        }

        public void BlockInjection() => releaseInjection = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseInjection() => releaseInjection?.TrySetResult();

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason)
        {
            Events.Add("input.pause");
            LifecycleObservedAtPause = Snapshot?.Invoke().Lifecycle;
            IsAcceptingInput = false;
            return LocalBoundaryResult.Confirmed("input_paused");
        }

        public LocalBoundaryResult ResumeNow()
        {
            if (ResumeFailure is not null)
            {
                throw ResumeFailure;
            }

            IsAcceptingInput = true;
            return LocalBoundaryResult.Confirmed("input_resumed");
        }

        public LocalBoundaryResult EmergencyStopNow()
        {
            OnEmergencyStop?.Invoke();
            IsAcceptingInput = false;
            return EmergencyStopResult;
        }

        public LocalBoundaryResult StopNow()
        {
            StopCallCount++;
            OnStop?.Invoke();
            IsAcceptingInput = false;
            return StopResult;
        }
    }

    private sealed class RecordingSharingSessionBoundary : ILocalSharingSessionBoundary
    {
        public List<DeviceId> DisconnectedPeers { get; } = [];

        public Func<RemoteWindowSharingSnapshot>? Snapshot { get; set; }

        public RemoteWindowSharingSnapshot? SnapshotObservedAtPeerDisconnect { get; private set; }

        public Action? OnDisconnectAll { get; set; }

        public int DisconnectAllCallCount { get; private set; }

        public LocalBoundaryResult DisconnectAllResult { get; set; } =
            LocalBoundaryResult.Confirmed("sessions_disconnected");

        public LocalBoundaryResult DisconnectPeerResult { get; set; } =
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId)
        {
            DisconnectedPeers.Add(peerDeviceId);
            SnapshotObservedAtPeerDisconnect = Snapshot?.Invoke();
            return DisconnectPeerResult;
        }

        public LocalBoundaryResult DisconnectAllNow()
        {
            DisconnectAllCallCount++;
            LocalBoundaryResult result = DisconnectAllResult;
            OnDisconnectAll?.Invoke();
            return result;
        }
    }
}
