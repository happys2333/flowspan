using System.Globalization;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class RemoteWindowWorkspaceViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActiveSessionCanBeEmergencyStoppedFromPersistentWorkspaceState()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        _ = await service.Controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance,
            CreateGrantedCapturePermissionService());

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Contains(
            "Execution remains on this source Device",
            viewModel.SharingDescription,
            StringComparison.Ordinal);
        Assert.Equal("Release plan", viewModel.ActivityTitle);
        Assert.Equal("CAPTURE: Capturing", viewModel.CaptureStatus);
        Assert.Equal("PROTECTION: Safe", viewModel.ProtectionStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.Equal(
            "EMERGENCY STOP CONFIRMED",
            viewModel.EmergencyStopStatus);
        Assert.Contains("Release plan", viewModel.SharingDescription);
        Assert.Contains("Current Driver:", viewModel.SharingDescription);
        Assert.Contains(
            "Capture: confirmed. Input: confirmed. Sessions: confirmed.",
            viewModel.EmergencyStopDescription,
            StringComparison.Ordinal);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.False(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task ThrowingObserverCannotPreventLocalEmergencyStopBoundary()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        System.ComponentModel.PropertyChangedEventHandler observer = (_, _) =>
            throw new InvalidOperationException("persistent-observer-failure");
        viewModel.PropertyChanged += observer;

        Assert.Throws<InvalidOperationException>(
            () => viewModel.EmergencyStopCommand.Execute(null));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task AdmittedStartingRequestCanBeEmergencyStoppedBeforeSnapshotPublishes()
    {
        var capture = new BlockingCaptureBoundary();
        var service = new ControllerRemoteWindowService(
            CreateController(captureBoundary: capture));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        Task starting = viewModel.StartRemoteWindowAsync();
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            RemoteWindowLifecycle.Starting,
            service.Controller.Snapshot.Lifecycle);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        capture.Complete();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConfirmedLocalResetIsOperableAndDoesNotRestoreAuthority()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);

        Assert.True(viewModel.IsLocalResetAvailable);
        Assert.True(viewModel.ResetRemoteWindowCommand.CanExecute(null));
        Assert.Contains("does not restore", viewModel.LocalResetDescription);

        await viewModel.ResetRemoteWindowAsync();

        Assert.Equal(1, service.ResetCalls);
        Assert.Equal(RemoteWindowLifecycle.Idle, service.Controller.Snapshot.Lifecycle);
        Assert.Empty(service.Controller.Snapshot.Participants);
        Assert.Null(service.Controller.Snapshot.CurrentDriverDeviceId);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            service.Controller.Snapshot.CaptureState);
        Assert.False(viewModel.IsLocalResetAvailable);
        Assert.Equal("LOCAL RESET CONFIRMED", viewModel.LocalResetStatus);
    }

    [Fact]
    public async Task ConfirmedResetReenablesStopBeforeNextStartReturns()
    {
        using RemoteWindowSessionController controller = CreateController();
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        var service = new BlockingStartRemoteWindowService(controller);
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);
        await viewModel.ResetRemoteWindowAsync();
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
        viewModel.EmergencyStopCommand.Execute(null);
        Assert.Equal(2, service.EmergencyStopCalls);

        service.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ThrowingResetAdmissionObserverCannotLeaveResetBusy()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);
        bool observerFailed = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!observerFailed
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.IsLocalResetAvailable)
                && !viewModel.IsLocalResetAvailable)
            {
                observerFailed = true;
                throw new InvalidOperationException("reset-admission-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.ResetRemoteWindowAsync());
        viewModel.PropertyChanged -= observer;

        Assert.True(observerFailed);
        Assert.Equal(0, service.ResetCalls);
        Assert.True(viewModel.IsLocalResetAvailable);
        Assert.True(viewModel.ResetRemoteWindowCommand.CanExecute(null));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await viewModel.ResetRemoteWindowAsync(timeout.Token);
        Assert.Equal(1, service.ResetCalls);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task ActiveDriverStatusIncludesLeaseExpiry()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot snapshot = service.Controller.Snapshot;
        Assert.NotNull(snapshot.CurrentDriverDeviceId);
        Assert.NotNull(snapshot.DriverLeaseEpoch);
        Assert.NotNull(snapshot.DriverLeaseExpiresAt);
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());

        Assert.Equal(
            $"DRIVER: {snapshot.CurrentDriverDeviceId} / "
                + $"EPOCH {snapshot.DriverLeaseEpoch} / "
                + $"LEASE EXPIRES {snapshot.DriverLeaseExpiresAt:O}",
            viewModel.DriverStatus);
    }

    [Fact]
    public async Task DriverLeasePresentationRemainsInvariantAcrossDisplayCultures()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var service = new ControllerRemoteWindowService(CreateController());
            await service.StartAsync();
            RemoteWindowSharingSnapshot snapshot = service.Controller.Snapshot;
            Assert.NotNull(snapshot.CurrentDriverDeviceId);
            Assert.NotNull(snapshot.DriverLeaseEpoch);
            Assert.NotNull(snapshot.DriverLeaseExpiresAt);
            await using var viewModel = new RemoteWindowWorkspaceViewModel(
                service,
                permissionService: CreateGrantedCapturePermissionService());

            Assert.Equal(
                "DRIVER: "
                    + $"{snapshot.CurrentDriverDeviceId} / EPOCH "
                    + snapshot.DriverLeaseEpoch.Value.ToString(
                        CultureInfo.InvariantCulture)
                    + " / LEASE EXPIRES "
                    + snapshot.DriverLeaseExpiresAt.Value.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                viewModel.DriverStatus);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task ExplicitResetAndNewSessionReenableEmergencyStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);
        Assert.False(viewModel.IsEmergencyStopAvailable);

        await service.ResetAndStartAsync();

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmedInactiveBoundaryAllowsSameActivityRevisionReset()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);
        using RemoteWindowSessionController nextController = CreateController();
        _ = await nextController.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        Assert.True(
            nextController.Snapshot.Revision
            <= service.Controller.Snapshot.Revision);

        service.Publish(null);
        service.Publish(nextController.Snapshot);

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task NewControllerGenerationAcceptsSameActivityRevisionRestart()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);
        long oldRevision = service.Controller.Snapshot.Revision;
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);

        service.ReplaceController(CreateController());
        Assert.True(service.Controller.Snapshot.Revision < oldRevision);
        await service.StartAsync();
        Assert.True(service.Controller.Snapshot.Revision < oldRevision);

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Equal("EMERGENCY STOP NOT REQUIRED", viewModel.EmergencyStopStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task AcceptedIdleBoundaryAllowsSameActivityRevisionReset()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);
        long stoppedRevision = service.Controller.Snapshot.Revision;
        using RemoteWindowSessionController idleController = CreateController();
        _ = await idleController.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        _ = idleController.ApplyProtectionSnapshot(
            new ProtectionSnapshot(ProtectionKind.SecureInput, Now, "test"));
        _ = idleController.EmergencyStop();
        RemoteWindowSharingSnapshot idle = (
            await idleController.ResetAfterLocalConfirmationAsync()).Snapshot;
        Assert.True(idle.Revision > stoppedRevision);

        service.Publish(idle);

        Assert.Equal("NOT SHARING", viewModel.SharingStatus);
        using RemoteWindowSessionController nextController = CreateController();
        _ = await nextController.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        Assert.True(nextController.Snapshot.Revision <= stoppedRevision);
        service.Publish(nextController.Snapshot);

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task EmergencyStopShowsEachUnconfirmedLocalBoundary()
    {
        var service = new ControllerRemoteWindowService(CreateController(
            new StopResultCaptureBoundary(
                LocalBoundaryResult.Failed("capture_stop_unconfirmed")),
            new StopResultInputBoundary(
                LocalBoundaryResult.Confirmed("input_stopped")),
            new StopResultSessionBoundary(
                LocalBoundaryResult.Failed("sessions_stop_unconfirmed"))));
        await service.StartAsync();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.Equal(
            "EMERGENCY STOP PARTIALLY UNCONFIRMED",
            viewModel.EmergencyStopStatus);
        Assert.Equal(
            "Capture: unconfirmed (capture_stop_unconfirmed). "
                + "Input: confirmed. "
                + "Sessions: unconfirmed (sessions_stop_unconfirmed).",
            viewModel.EmergencyStopDescription);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.False(viewModel.EmergencyStopCommand.CanExecute(null));
        InvalidOperationException disposalFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => viewModel.DisposeAsync().AsTask());
        Assert.Equal(
            "Remote Window Emergency Stop was not fully confirmed during disposal.",
            disposalFailure.Message);
    }

    [Fact]
    public async Task EmergencyStopServiceFailureIsUnconfirmedWithoutPayload()
    {
        const string canary = "EMERGENCY-STOP-EXCEPTION-CANARY";
        var service = new ControllerRemoteWindowService(
            CreateController(),
            new IOException(canary));
        await service.StartAsync();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Equal("EMERGENCY STOP UNCONFIRMED", viewModel.EmergencyStopStatus);
        Assert.Contains(
            "Capture, input, and sessions are unconfirmed",
            viewModel.EmergencyStopDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.EmergencyStopDescription,
            StringComparison.Ordinal);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.False(viewModel.EmergencyStopCommand.CanExecute(null));

        _ = await Assert.ThrowsAsync<IOException>(
            () => viewModel.DisposeAsync().AsTask());
        Assert.Equal(2, service.EmergencyStopCalls);
    }

    [Fact]
    public async Task OlderSnapshotCannotRollPersistentSafetyStateBack()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot staleActive = service.Controller.Snapshot;
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        RemoteWindowProtectionResult paused = service.Controller.ApplyProtectionSnapshot(
            new ProtectionSnapshot(ProtectionKind.SecureInput, Now, "test"));

        service.Publish(paused.Snapshot);
        Assert.Equal("REMOTE WINDOW PAUSED", viewModel.SharingStatus);
        Assert.Equal("PROTECTION: SecureInput", viewModel.ProtectionStatus);

        service.Publish(staleActive);

        Assert.Equal("REMOTE WINDOW PAUSED", viewModel.SharingStatus);
        Assert.Equal("PROTECTION: SecureInput", viewModel.ProtectionStatus);
        Assert.Equal($"REVISION: {paused.Snapshot.Revision}", viewModel.RevisionStatus);
    }

    [Fact]
    public async Task OlderDriverSnapshotCannotStopCurrentViewOnlySession()
    {
        DeviceId peer = DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var authorization = new MutableAuthorizationSource(
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        RemoteWindowSessionController controller = CreateController(
            authorizationSource: authorization);
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        _ = await controller.AddParticipantAsync(
            peer,
            MirrorParticipantRole.DriverEligible);
        RemoteWindowSharingSnapshot staleDriverEligible = controller.Snapshot;
        authorization.SetGrant(CapabilityGrant.Of(Capability.MirrorView));
        RemoteWindowCommandResult downgraded =
            await controller.ReconcilePeerCapabilitiesAsync(peer);
        Assert.Equal(RemoteWindowCommandStatus.Applied, downgraded.Status);
        Assert.Equal(
            MirrorParticipantRole.ViewOnly,
            downgraded.Snapshot.Participants[peer]);
        Assert.True(downgraded.Snapshot.Revision > staleDriverEligible.Revision);

        var service = new ControllerRemoteWindowService(controller);
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Revoked));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        Assert.Equal(0, service.EmergencyStopCalls);

        service.Publish(staleDriverEligible);

        Assert.Equal(0, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.Active,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal(
            $"REVISION: {downgraded.Snapshot.Revision}",
            viewModel.RevisionStatus);
    }

    [Fact]
    public async Task ReversedUiCallbacksCannotRollAtomicSnapshotReducerBack()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot staleActive = service.Controller.Snapshot;
        var dispatcher = new ReverseQueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            CreateGrantedCapturePermissionService());
        RemoteWindowProtectionResult paused = service.Controller.ApplyProtectionSnapshot(
            new ProtectionSnapshot(ProtectionKind.SecureInput, Now, "test"));

        service.Publish(paused.Snapshot);
        service.Publish(staleActive);
        dispatcher.RunAllReverse();

        Assert.Equal("REMOTE WINDOW PAUSED", viewModel.SharingStatus);
        Assert.Equal("PROTECTION: SecureInput", viewModel.ProtectionStatus);
        Assert.Equal($"REVISION: {paused.Snapshot.Revision}", viewModel.RevisionStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LowerRevisionIdleCannotReplaceStoppableSessionWithoutInactiveProvenance(
        bool protectionPaused)
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot accepted = service.Controller.Snapshot;
        if (protectionPaused)
        {
            accepted = service.Controller.ApplyProtectionSnapshot(
                new ProtectionSnapshot(ProtectionKind.SecureInput, Now, "test"))
                .Snapshot;
            service.Publish(accepted);
        }

        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        using RemoteWindowSessionController nextController = CreateController();
        RemoteWindowSharingSnapshot staleIdle = nextController.Snapshot;
        Assert.True(staleIdle.Revision < accepted.Revision);

        service.Publish(staleIdle);

        Assert.Equal(
            protectionPaused ? "REMOTE WINDOW PAUSED" : "REMOTE WINDOW ACTIVE",
            viewModel.SharingStatus);
        Assert.Equal($"REVISION: {accepted.Revision}", viewModel.RevisionStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task UnavailableStateDoesNotAcceptStaleActiveSnapshot()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot staleActive = service.Controller.Snapshot;
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.EmergencyStopCommand.Execute(null);

        service.SetUnavailable();
        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);

        service.SetAvailable(staleActive);

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.False(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task EqualRevisionRecoveryRepaintsTransientUnavailableState()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot current = service.Controller.Snapshot;
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        service.SetUnavailable();
        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal("Release plan — LAST KNOWN", viewModel.ActivityTitle);

        service.SetAvailable(current);

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Equal("Release plan", viewModel.ActivityTitle);
        Assert.Equal($"REVISION: {current.Revision}", viewModel.RevisionStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task UnavailableReadKeepsStopForLastKnownActiveSession()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        RemoteWindowSharingSnapshot lastAccepted = service.Controller.Snapshot;
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());

        service.SetUnavailable();

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal("Release plan — LAST KNOWN", viewModel.ActivityTitle);
        Assert.Equal(
            $"CAPTURE (LAST KNOWN): {lastAccepted.CaptureState}",
            viewModel.CaptureStatus);
        Assert.Equal(
            $"PARTICIPANTS (LAST KNOWN): {lastAccepted.Participants.Count}",
            viewModel.ParticipantStatus);
        Assert.StartsWith("DRIVER (LAST KNOWN): ", viewModel.DriverStatus);
        Assert.Equal(
            $"PROTECTION (LAST KNOWN): {lastAccepted.ProtectionKind}",
            viewModel.ProtectionStatus);
        Assert.Equal(
            $"REVISION (LAST KNOWN): {lastAccepted.Revision}",
            viewModel.RevisionStatus);
        Assert.Contains("Last known", viewModel.SharingDescription);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOP CONFIRMED", viewModel.EmergencyStopStatus);
        Assert.False(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task SnapshotFailureIsUnavailableWithoutExceptionPayload()
    {
        const string canary = "REMOTE-WINDOW-SNAPSHOT-EXCEPTION-CANARY";
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ThrowingSnapshotService(canary));

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal(
            "Sharing state: Remote Window unavailable",
            viewModel.SharingAutomationName);
        Assert.Equal("Unknown Activity", viewModel.ActivityTitle);
        Assert.Equal("Unknown Activity", viewModel.ActivityId);
        Assert.Equal("CAPTURE: Unknown", viewModel.CaptureStatus);
        Assert.Equal("PARTICIPANTS: Unknown", viewModel.ParticipantStatus);
        Assert.Equal("DRIVER: Unknown", viewModel.DriverStatus);
        Assert.Equal("PROTECTION: Unknown", viewModel.ProtectionStatus);
        Assert.Equal("REVISION: Unknown", viewModel.RevisionStatus);
        Assert.Contains(
            "inspect local diagnostics",
            viewModel.SharingDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.SharingDescription,
            StringComparison.Ordinal);
        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task AvailabilityReadFailureIsUnavailableWithoutExceptionPayload()
    {
        const string canary = "REMOTE-WINDOW-AVAILABILITY-EXCEPTION-CANARY";
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ThrowingAvailabilityService(canary));

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal(
            "Sharing state: Remote Window unavailable",
            viewModel.SharingAutomationName);
        Assert.DoesNotContain(
            canary,
            viewModel.SharingDescription,
            StringComparison.Ordinal);
        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task PermissionStateReadFailureFailsClosedWithoutExceptionPayload()
    {
        const string canary = "PERMISSION-STATE-READ-EXCEPTION-CANARY";
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();

        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: new ThrowingPermissionSnapshotService(canary));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal(
            "CAPTURE PERMISSION UNAVAILABLE",
            viewModel.CapturePermissionStatus);
        Assert.Equal("INPUT PERMISSION UNAVAILABLE", viewModel.InputPermissionStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.CapturePermissionDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.InputPermissionDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndefinedPermissionStatesFailClosedAndStopSharing()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            (DesktopPermissionState)int.MaxValue,
            (DesktopPermissionState)(-1)));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal(
            "CAPTURE PERMISSION UNAVAILABLE",
            viewModel.CapturePermissionStatus);
        Assert.Equal("INPUT PERMISSION UNAVAILABLE", viewModel.InputPermissionStatus);
        Assert.False(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
    }

    [Fact]
    public async Task UnavailableReasonReadFailureUsesBoundedGenericPresentation()
    {
        const string canary = "REMOTE-WINDOW-REASON-EXCEPTION-CANARY";
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ThrowingUnavailableReasonService(canary));

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal(
            "Sharing state: Remote Window unavailable",
            viewModel.SharingAutomationName);
        Assert.Contains(
            "inspect local diagnostics",
            viewModel.SharingDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.SharingDescription,
            StringComparison.Ordinal);
        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task CaptureReviewPrecedesRequestAndInputWaitsForRemoteDriving()
    {
        var permissions = new RecordingPermissionService();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: permissions);

        Assert.True(viewModel.ReviewCapturePermissionCommand.CanExecute(null));
        Assert.False(viewModel.RequestCapturePermissionCommand.CanExecute(null));
        Assert.False(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsInputPermissionReviewVisible);
        await viewModel.RequestInputPermissionAsync();
        Assert.Equal(0, permissions.InputRequests);

        viewModel.ReviewCapturePermissionCommand.Execute(null);

        Assert.True(viewModel.IsCapturePermissionReviewVisible);
        Assert.Equal(0, permissions.CaptureRequests);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        Assert.True(viewModel.RequestCapturePermissionCommand.CanExecute(null));

        await viewModel.RequestCapturePermissionAsync();

        Assert.Equal(1, permissions.CaptureRequests);
        Assert.Equal("CAPTURE PERMISSION GRANTED", viewModel.CapturePermissionStatus);
        Assert.True(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsInputPermissionReviewVisible);

        viewModel.IsRemoteDrivingEnabled = true;

        Assert.True(viewModel.IsInputPermissionReviewVisible);
        Assert.Equal(0, permissions.InputRequests);
        Assert.False(viewModel.RequestInputPermissionCommand.CanExecute(null));
        viewModel.HasAcknowledgedInputPermissionReview = true;
        Assert.True(viewModel.RequestInputPermissionCommand.CanExecute(null));

        await viewModel.RequestInputPermissionAsync();

        Assert.Equal(1, permissions.InputRequests);
        Assert.Equal("INPUT PERMISSION GRANTED", viewModel.InputPermissionStatus);
        Assert.False(viewModel.IsInputPermissionReviewVisible);
    }

    [Fact]
    public async Task ThrowingPermissionObserverCannotBlockNextPermissionRequest()
    {
        var permissions = new RecordingPermissionService();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        bool observerFailed = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!observerFailed
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.IsPermissionBusy)
                && !viewModel.IsPermissionBusy)
            {
                observerFailed = true;
                throw new InvalidOperationException("permission-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.RequestCapturePermissionAsync());
        viewModel.PropertyChanged -= observer;
        Assert.True(observerFailed);
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined));
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await viewModel.RequestCapturePermissionAsync(timeout.Token);

        Assert.Equal(2, permissions.CaptureRequests);
        Assert.Equal(
            "CAPTURE PERMISSION GRANTED",
            viewModel.CapturePermissionStatus);
    }

    [Fact]
    public async Task ThrowingPermissionAdmissionObserverCannotLeaveRequestBusy()
    {
        var permissions = new RecordingPermissionService();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        bool observerFailed = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!observerFailed
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.IsPermissionBusy)
                && viewModel.IsPermissionBusy)
            {
                observerFailed = true;
                throw new InvalidOperationException(
                    "permission-admission-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.RequestCapturePermissionAsync());
        viewModel.PropertyChanged -= observer;

        Assert.True(observerFailed);
        Assert.Equal(0, permissions.CaptureRequests);
        Assert.True(viewModel.RequestCapturePermissionCommand.CanExecute(null));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await viewModel.RequestCapturePermissionAsync(timeout.Token);
        Assert.Equal(1, permissions.CaptureRequests);
        Assert.Equal(
            "CAPTURE PERMISSION GRANTED",
            viewModel.CapturePermissionStatus);
    }

    [Fact]
    public async Task PermissionBusyObserverCanSynchronouslyRequestDisposal()
    {
        var permissions = new RecordingPermissionService();
        var service = new ControllerRemoteWindowService(CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        Task? reentrantDisposal = null;
        bool reentrantDisposalCompleted = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (reentrantDisposal is null
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.IsPermissionBusy)
                && viewModel.IsPermissionBusy)
            {
                reentrantDisposal = viewModel.DisposeAsync().AsTask();
                reentrantDisposalCompleted =
                    reentrantDisposal.IsCompletedSuccessfully;
                if (reentrantDisposalCompleted)
                {
                    reentrantDisposal.GetAwaiter().GetResult();
                }
            }
        };
        viewModel.PropertyChanged += observer;

        await viewModel.RequestCapturePermissionAsync();
        viewModel.PropertyChanged -= observer;

        Assert.NotNull(reentrantDisposal);
        await reentrantDisposal;
        Assert.True(reentrantDisposalCompleted);
        await viewModel.DisposeAsync();
        Assert.True(service.Disposed);
        Assert.True(permissions.Disposed);
    }

    [Fact]
    public async Task SelectedActivityFallbackStartsViewOnlyRemoteWindow()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        var activity = new DesktopActivitySnapshot(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Release plan",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var target = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");

        viewModel.SetFallbackSelection(activity, target);

        Assert.Equal(
            "REMOTE WINDOW READY — EXECUTION STAYS ON SOURCE",
            viewModel.FallbackStatus);
        Assert.Contains("Release plan", viewModel.FallbackDescription);
        Assert.Contains("Peer desk", viewModel.FallbackDescription);
        Assert.True(viewModel.IsFallbackStartAvailable);
        Assert.True(viewModel.StartRemoteWindowCommand.CanExecute(null));

        await viewModel.StartRemoteWindowAsync();

        Assert.Equal(1, service.StartCalls);
        Assert.Equal(activity.ActivityId, service.RequestedActivityId);
        Assert.Equal(target.DeviceId, service.RequestedTargetDeviceId);
        Assert.Equal(MirrorParticipantRole.ViewOnly, service.RequestedRole);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.FallbackStatus);
    }

    [Fact]
    public async Task FallbackSelectionUpdateCannotOvertakeStartAdmissionFreeze()
    {
        var service = new BlockingStartRemoteWindowService(CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        var admittedActivity = new DesktopActivitySnapshot(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Release plan",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var admittedTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var nextActivity = new DesktopActivitySnapshot(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Incident review",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var nextTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Peer studio");
        viewModel.SetFallbackSelection(admittedActivity, admittedTarget);
        var admissionPresentationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseAdmission = new ManualResetEventSlim(initialState: false);
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.FallbackStatus)
                && viewModel.FallbackStatus == "REMOTE WINDOW STARTING")
            {
                admissionPresentationReached.TrySetResult();
                releaseAdmission.Wait(TimeSpan.FromSeconds(2));
            }
        };
        viewModel.PropertyChanged += observer;
        Task starting = Task.Run(() => viewModel.StartRemoteWindowAsync());
        await admissionPresentationReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var selectionAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task changingSelection = Task.Run(() =>
        {
            selectionAttempted.TrySetResult();
            viewModel.SetFallbackSelection(nextActivity, nextTarget);
        });

        try
        {
            await selectionAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            releaseAdmission.Set();
            viewModel.PropertyChanged -= observer;
        }

        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await changingSelection.WaitAsync(TimeSpan.FromSeconds(2));
        service.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(admittedActivity.ActivityId, service.RequestedActivityId);
        Assert.Equal(admittedTarget.DeviceId, service.RequestedTargetDeviceId);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task SemanticResumeAvailableDoesNotOfferRemoteWindowFallback()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"),
            DesktopSemanticResumeAvailability.Available);

        Assert.Equal(
            "REMOTE WINDOW NOT OFFERED — SEMANTIC RESUME AVAILABLE",
            viewModel.FallbackStatus);
        Assert.Contains("Handoff or Move", viewModel.FallbackDescription);
        Assert.False(viewModel.IsFallbackStartAvailable);
        Assert.False(viewModel.StartRemoteWindowCommand.CanExecute(null));

        await viewModel.StartRemoteWindowAsync();

        Assert.Equal(0, service.StartCalls);
    }

    [Fact]
    public async Task ThrowingPresentationObserverCannotBlockNextRemoteWindowStart()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        bool observerFailed = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!observerFailed
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.FallbackStatus)
                && viewModel.FallbackStatus == "REMOTE WINDOW ACTIVE")
            {
                observerFailed = true;
                throw new InvalidOperationException("presentation-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.StartRemoteWindowAsync());
        viewModel.PropertyChanged -= observer;
        Assert.True(observerFailed);
        viewModel.EmergencyStopCommand.Execute(null);
        await service.ResetAsync();
        Assert.True(viewModel.IsFallbackStartAvailable);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await viewModel.StartRemoteWindowAsync(timeout.Token);

        Assert.Equal(2, service.StartCalls);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.FallbackStatus);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task ThrowingStartAdmissionObserverCannotLeaveWorkspaceBusy()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        bool observerFailed = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!observerFailed
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.FallbackStatus)
                && viewModel.FallbackStatus == "REMOTE WINDOW STARTING")
            {
                observerFailed = true;
                throw new InvalidOperationException("admission-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.StartRemoteWindowAsync());
        viewModel.PropertyChanged -= observer;

        Assert.True(observerFailed);
        Assert.Equal(0, service.StartCalls);
        Assert.True(viewModel.IsFallbackStartAvailable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await viewModel.StartRemoteWindowAsync(timeout.Token);
        Assert.Equal(1, service.StartCalls);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task FailedStartAdmissionCannotLabelLaterSameActivitySession()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        bool observerFailed = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!observerFailed
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.FallbackStatus)
                && viewModel.FallbackStatus == "REMOTE WINDOW STARTING")
            {
                observerFailed = true;
                throw new InvalidOperationException("admission-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;
        _ = await Record.ExceptionAsync(() => viewModel.StartRemoteWindowAsync());
        viewModel.PropertyChanged -= observer;
        Assert.True(observerFailed);
        Assert.Equal(0, service.StartCalls);

        using RemoteWindowSessionController external = CreateController();
        _ = await external.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        service.Publish(external.Snapshot);

        Assert.Equal(
            "REMOTE WINDOW ACTIVE — TARGET CONTEXT UNAVAILABLE",
            viewModel.FallbackStatus);
        Assert.DoesNotContain("Peer desk", viewModel.FallbackDescription);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task SelectionChangeInvalidatesInFlightStartResultContext()
    {
        var service = new BlockingStartRemoteWindowService(
            CreateController(),
            new ProtectionSnapshot(ProtectionKind.Unknown, Now, "test"));
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        var firstActivity = new DesktopActivitySnapshot(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "First activity",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var firstTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "First target");
        var nextActivity = new DesktopActivitySnapshot(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Next activity",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var nextTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Next target");
        viewModel.SetFallbackSelection(firstActivity, firstTarget);

        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SetFallbackSelection(nextActivity, nextTarget);

        Assert.Equal("REMOTE WINDOW STARTING", viewModel.FallbackStatus);
        Assert.Contains("First activity", viewModel.FallbackDescription);
        Assert.Contains("First target", viewModel.FallbackDescription);
        Assert.DoesNotContain("Next activity", viewModel.FallbackDescription);
        service.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            "REMOTE WINDOW READY — EXECUTION STAYS ON SOURCE",
            viewModel.FallbackStatus);
        Assert.Contains("Next activity", viewModel.FallbackDescription);
        Assert.Contains("Next target", viewModel.FallbackDescription);
    }

    [Fact]
    public async Task ClearedSelectionCannotRelabelInFlightStart()
    {
        var service = new BlockingStartRemoteWindowService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SetFallbackSelection(null, null);

        Assert.Equal("REMOTE WINDOW STARTING", viewModel.FallbackStatus);
        Assert.Contains("Release plan", viewModel.FallbackDescription);
        Assert.Contains("Peer desk", viewModel.FallbackDescription);
        service.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.FallbackStatus);
        Assert.Contains("Release plan", viewModel.FallbackDescription);
        Assert.Contains("Peer desk", viewModel.FallbackDescription);
    }

    [Fact]
    public async Task TargetSelectionChangeCannotRelabelActiveSession()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        var activity = new DesktopActivitySnapshot(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Release plan",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var admittedTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        viewModel.SetFallbackSelection(activity, admittedTarget);
        await viewModel.StartRemoteWindowAsync();

        viewModel.SetFallbackSelection(
            activity,
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
                "Meeting room"));

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.FallbackStatus);
        Assert.Contains("Peer desk", viewModel.FallbackDescription);
        Assert.DoesNotContain("Meeting room", viewModel.FallbackDescription);
    }

    [Fact]
    public async Task DifferentActivitySnapshotCannotReusePriorOrPreviewTarget()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        await viewModel.StartRemoteWindowAsync();

        ActivityId nextActivityId =
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        using RemoteWindowSessionController nextController = CreateController(
            activityId: nextActivityId,
            activityTitle: "Second activity");
        _ = await nextController.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                nextActivityId,
                "Second activity",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
                "Meeting room"));

        service.Publish(nextController.Snapshot);

        Assert.Equal(
            "REMOTE WINDOW ACTIVE — TARGET CONTEXT UNAVAILABLE",
            viewModel.FallbackStatus);
        Assert.Contains("Second activity", viewModel.FallbackDescription);
        Assert.DoesNotContain("Peer desk", viewModel.FallbackDescription);
        Assert.DoesNotContain("Meeting room", viewModel.FallbackDescription);
    }

    [Fact]
    public async Task ThrowingStartBoundaryStopsAnyUnconfirmedActiveSession()
    {
        const string canary = "REMOTE-WINDOW-START-EXCEPTION-CANARY";
        var service = new ControllerRemoteWindowService(
            CreateController(),
            startFailure: new IOException(canary));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        await viewModel.StartRemoteWindowAsync();

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.EmergencyStopDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.FallbackDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledStartThatIgnoresTokenIsEmergencyStopped()
    {
        var service = new BlockingStartRemoteWindowService(
            CreateController(),
            ignoreCancellation: true);
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        using var cancellation = new CancellationTokenSource();
        Task starting = viewModel.StartRemoteWindowAsync(cancellation.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        service.CompleteStart();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task ThrowingObserverCannotBlockStopAfterUnconfirmedStartBoundary()
    {
        var service = new ControllerRemoteWindowService(
            CreateController(),
            startFailure: new IOException("start-boundary-failure"));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                == nameof(RemoteWindowWorkspaceViewModel.SharingStatus))
            {
                throw new InvalidOperationException("persistent-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        _ = await Record.ExceptionAsync(() => viewModel.StartRemoteWindowAsync());

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task OldControllerStartResultCannotOverwriteNewActiveSession()
    {
        var oldCapture = new BlockingFailingCaptureBoundary();
        var service = new GenerationSwitchingStartService(
            CreateController(oldCapture));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        Task starting = viewModel.StartRemoteWindowAsync();
        await oldCapture.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.ReplaceAndStartAsync(CreateController());
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        oldCapture.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            RemoteWindowLifecycle.Active,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.False(viewModel.IsLocalResetAvailable);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task FailedStartResultShowsBoundedFailureInsteadOfBusySession()
    {
        var service = new ControllerRemoteWindowService(CreateController(
            captureBoundary: new StartResultCaptureBoundary(
                LocalBoundaryResult.Failed("capture_start_unconfirmed"))));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        await viewModel.StartRemoteWindowAsync();

        Assert.Equal("REMOTE WINDOW COULD NOT START", viewModel.FallbackStatus);
        Assert.Contains(
            "did not confirm startup",
            viewModel.FallbackDescription,
            StringComparison.Ordinal);
        Assert.Contains("Release plan", viewModel.SharingDescription);
        Assert.Contains("Current Driver: None", viewModel.SharingDescription);
        Assert.False(viewModel.IsFallbackStartAvailable);
    }

    [Fact]
    public async Task UnavailableStartOffersLocalResetBeforeRetry()
    {
        var service = new ControllerRemoteWindowService(CreateController(
            captureBoundary: new StartResultCaptureBoundary(
                LocalBoundaryResult.Failed("capture_start_unconfirmed"))));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        await viewModel.StartRemoteWindowAsync();

        Assert.True(viewModel.IsLocalResetAvailable);
        Assert.True(viewModel.ResetRemoteWindowCommand.CanExecute(null));
        Assert.Equal("LOCAL RETRY RESET REQUIRED", viewModel.LocalResetStatus);

        await viewModel.ResetRemoteWindowAsync();

        Assert.Equal(1, service.ResetCalls);
        Assert.Equal(RemoteWindowLifecycle.Idle, service.Controller.Snapshot.Lifecycle);
        Assert.Equal("LOCAL RETRY RESET CONFIRMED", viewModel.LocalResetStatus);
        Assert.False(viewModel.IsLocalResetAvailable);
    }

    [Fact]
    public async Task UnavailableSnapshotWithoutObservedCleanupResultCannotOfferRetryReset()
    {
        using RemoteWindowSessionController controller = CreateController(
            captureBoundary: new StartResultCaptureBoundary(
                LocalBoundaryResult.Failed("capture_start_unconfirmed")));
        RemoteWindowCommandResult failedStart = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        Assert.True(failedStart.CleanupBoundary?.Succeeded);
        Assert.Equal(
            RemoteWindowLifecycle.Unavailable,
            failedStart.Snapshot.Lifecycle);
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(controller),
            permissionService: CreateGrantedCapturePermissionService());

        Assert.False(viewModel.IsLocalResetAvailable);
        Assert.False(viewModel.ResetRemoteWindowCommand.CanExecute(null));
        Assert.Equal("LOCAL RETRY RESET UNAVAILABLE", viewModel.LocalResetStatus);
    }

    [Fact]
    public async Task UnavailableSnapshotClaimCannotSuppressLaterPermissionStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        using RemoteWindowSessionController failedController = CreateController(
            captureBoundary: new StartResultCaptureBoundary(
                LocalBoundaryResult.Failed("capture_start_unconfirmed")));
        RemoteWindowCommandResult failedStart = await failedController.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        Assert.True(
            failedStart.Snapshot.Revision
            >= service.Controller.Snapshot.Revision);

        service.Publish(failedStart.Snapshot);
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.NotDetermined));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task AppliedStartResultPreservesStopWhenSnapshotReadFails()
    {
        var service = new StartThenThrowSnapshotService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        await viewModel.StartRemoteWindowAsync();

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal("Release plan — LAST KNOWN", viewModel.ActivityTitle);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        viewModel.EmergencyStopCommand.Execute(null);
        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task AppliedEmergencyStopResultReplacesActiveLastKnownSnapshot()
    {
        var service = new EmergencyStopThenThrowSnapshotService(CreateController());
        _ = await service.Controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal("REMOTE WINDOW UNAVAILABLE", viewModel.SharingStatus);
        Assert.Equal("EMERGENCY STOP CONFIRMED", viewModel.EmergencyStopStatus);
        Assert.Equal("Release plan — LAST KNOWN", viewModel.ActivityTitle);
        Assert.Equal(
            "CAPTURE (LAST KNOWN): Stopped",
            viewModel.CaptureStatus);
        Assert.Equal("DRIVER (LAST KNOWN): None", viewModel.DriverStatus);
        Assert.Equal("LOCAL RESET REQUIRED", viewModel.LocalResetStatus);
        Assert.False(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task InputRevocationStopsDriverEligibleRemoteWindow()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.IsRemoteDrivingEnabled = true;
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        await viewModel.StartRemoteWindowAsync();
        Assert.Equal(MirrorParticipantRole.DriverEligible, service.RequestedRole);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Revoked));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.Equal("INPUT PERMISSION REVOKED", viewModel.InputPermissionStatus);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
    }

    [Fact]
    public async Task ThrowingPermissionObserverCannotPreventRevocationSafetyStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.IsRemoteDrivingEnabled = true;
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        await viewModel.StartRemoteWindowAsync();
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName ==
                nameof(RemoteWindowWorkspaceViewModel.InputPermissionStatus))
            {
                throw new InvalidOperationException("persistent-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        Assert.Throws<InvalidOperationException>(() => permissions.Publish(
            new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Granted,
                DesktopPermissionState.Revoked)));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task BlockingSelectionObserverCannotDelayPermissionRevocationSafetyStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        using var observerEntered = new ManualResetEventSlim(initialState: false);
        using var releaseObserver = new ManualResetEventSlim(initialState: false);
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                == nameof(RemoteWindowWorkspaceViewModel.IsFallbackStartAvailable))
            {
                observerEntered.Set();
                releaseObserver.Wait();
            }
        };
        viewModel.PropertyChanged += observer;
        Task selecting = Task.Factory.StartNew(
            () => viewModel.SetFallbackSelection(
                new DesktopActivitySnapshot(
                    ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Release plan",
                    "workspace.note/v1",
                    ActivitySensitivity.Normal,
                    ActivityLifecycle.Active),
                new DesktopActivityTargetSnapshot(
                    DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                    "Peer desk")),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(observerEntered.Wait(TimeSpan.FromSeconds(2)));

        var revocationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task revoking = Task.Factory.StartNew(
            () =>
            {
                revocationStarted.TrySetResult();
                permissions.Publish(
                    new DesktopRemoteWindowPermissionSnapshot(
                        DesktopPermissionState.Revoked,
                        DesktopPermissionState.Granted));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await revocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await service.EmergencyStopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            releaseObserver.Set();
        }

        await Task.WhenAll(selecting, revoking).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task SynchronousStartChangeObserverCannotDelayPermissionRevocationSafetyStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        using var observerEntered = new ManualResetEventSlim(initialState: false);
        using var releaseObserver = new ManualResetEventSlim(initialState: false);
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                == nameof(RemoteWindowWorkspaceViewModel.SharingStatus))
            {
                observerEntered.Set();
                releaseObserver.Wait();
            }
        };
        viewModel.PropertyChanged += observer;
        Task starting = Task.Factory.StartNew(
            () => viewModel.StartRemoteWindowAsync(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        Assert.True(observerEntered.Wait(TimeSpan.FromSeconds(2)));

        var revocationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task revoking = Task.Factory.StartNew(
            () =>
            {
                revocationStarted.TrySetResult();
                permissions.Publish(
                    new DesktopRemoteWindowPermissionSnapshot(
                        DesktopPermissionState.Revoked,
                        DesktopPermissionState.Granted));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await revocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await service.EmergencyStopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            releaseObserver.Set();
        }

        await Task.WhenAll(starting, revoking).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task InputPermissionLossStopsAdmittedDriverEligibleSessionAfterPreviewChanges()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.IsRemoteDrivingEnabled = true;
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        await viewModel.StartRemoteWindowAsync();
        Assert.Equal(MirrorParticipantRole.DriverEligible, service.RequestedRole);

        viewModel.IsRemoteDrivingEnabled = false;
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.False(viewModel.IsRemoteDrivingEnabled);

        await service.ResetAsync();

        Assert.True(viewModel.IsFallbackStartAvailable);
        await viewModel.StartRemoteWindowAsync();
        Assert.Equal(2, service.StartCalls);
        Assert.Equal(MirrorParticipantRole.ViewOnly, service.RequestedRole);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
    }

    [Fact]
    public async Task InputPermissionLossStopsDriverSessionAndClearsDrivingIntent()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.IsRemoteDrivingEnabled = true;
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        await viewModel.StartRemoteWindowAsync();
        Assert.Equal(MirrorParticipantRole.DriverEligible, service.RequestedRole);
        Assert.True(viewModel.IsRemoteDrivingEnabled);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
        await service.ResetAsync();
        Assert.True(viewModel.CanEnableRemoteDriving);

        viewModel.IsRemoteDrivingEnabled = true;

        Assert.True(viewModel.IsInputPermissionReviewVisible);
        Assert.False(viewModel.IsFallbackStartAvailable);
    }

    [Fact]
    public async Task UngrantedInputStopsExistingDriverEligibleSessionOnInitialRefresh()
    {
        DeviceId peer = DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        RemoteWindowSessionController controller = CreateController(
            authorizationSource: new FixedAuthorizationSource(
                CapabilityGrant.Of(
                    Capability.MirrorView,
                    Capability.MirrorDrive)));
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        _ = await controller.AddParticipantAsync(
            peer,
            MirrorParticipantRole.DriverEligible);
        var service = new ControllerRemoteWindowService(controller);
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));

        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
    }

    [Fact]
    public async Task ThrowingSnapshotObserverCannotBlockRevokedPermissionStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.NotDetermined));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        _ = await service.Controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                == nameof(RemoteWindowWorkspaceViewModel.ActivityTitle))
            {
                throw new InvalidOperationException("persistent-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        _ = Record.Exception(service.PublishChanged);

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task DenialAndRevocationKeepRemoteDrivingDisabled()
    {
        var permissions = new RecordingPermissionService
        {
            NextCaptureState = DesktopPermissionState.Denied,
        };
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;

        await viewModel.RequestCapturePermissionAsync();

        Assert.Equal("CAPTURE PERMISSION DENIED", viewModel.CapturePermissionStatus);
        Assert.Contains(
            "privacy settings",
            viewModel.CapturePermissionRecoveryAction,
            StringComparison.Ordinal);
        Assert.False(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsRemoteDrivingEnabled);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        Assert.True(viewModel.CanEnableRemoteDriving);
        viewModel.IsRemoteDrivingEnabled = true;
        Assert.True(viewModel.IsRemoteDrivingEnabled);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Revoked));

        Assert.Equal("INPUT PERMISSION REVOKED", viewModel.InputPermissionStatus);
        Assert.Contains(
            "privacy settings",
            viewModel.InputPermissionRecoveryAction,
            StringComparison.Ordinal);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
        Assert.False(viewModel.CanEnableRemoteDriving);
    }

    [Fact]
    public async Task CaptureRevocationStopsActiveSharingLocally()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.Granted));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.Equal(
            "CAPTURE PERMISSION REVOKED",
            viewModel.CapturePermissionStatus);
        Assert.False(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
    }

    [Fact]
    public async Task BackgroundCaptureRevocationStopsBeforeQueuedPresentationRuns()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);

        await Task.Run(() => permissions.Publish(
            new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Revoked,
                DesktopPermissionState.Granted)));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);

        dispatcher.RunAll();
        Assert.Equal("CAPTURE PERMISSION REVOKED", viewModel.CapturePermissionStatus);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
    }

    [Fact]
    public async Task BackgroundRevocationStopsExternallyActiveUnpresentedSession()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        await service.StartAsync();
        Assert.Equal("NOT SHARING", viewModel.SharingStatus);

        await Task.Run(() => permissions.Publish(
            new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Revoked,
                DesktopPermissionState.Granted)));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        dispatcher.RunAll();
        Assert.Equal("CAPTURE PERMISSION REVOKED", viewModel.CapturePermissionStatus);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
    }

    [Fact]
    public async Task ExistingCaptureRevocationStopsExternallyPublishedSessionSynchronously()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);

        await service.StartAsync();

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("NOT SHARING", viewModel.SharingStatus);
    }

    [Fact]
    public async Task CachedPermissionRevocationStopsWithoutBlockingSnapshotRead()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new CachedChangedPermissionService();
        bool stopObservedBeforeGetter = false;
        service.OnEmergencyStop = () =>
        {
            stopObservedBeforeGetter =
                !permissions.GetterReadStarted.Task.IsCompleted;
        };
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);

        Task publishing = Task.Run(permissions.PublishRevokedAndBlockGetter);
        try
        {
            await service.EmergencyStopCalled.Task
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(stopObservedBeforeGetter);
            Assert.False(permissions.GetterReadStarted.Task.IsCompleted);
        }
        finally
        {
            permissions.ReleaseGetter();
        }

        await publishing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task ExistingInputRevocationStopsExternallyPublishedDriverSessionSynchronously()
    {
        DeviceId peer = DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var service = new ControllerRemoteWindowService(CreateController(
            authorizationSource: new FixedAuthorizationSource(
                CapabilityGrant.Of(
                    Capability.MirrorView,
                    Capability.MirrorDrive))));
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Revoked));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        await service.StartAsync();
        _ = await service.Controller.AddParticipantAsync(
            peer,
            MirrorParticipantRole.DriverEligible);

        service.PublishChanged();

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("NOT SHARING", viewModel.SharingStatus);
    }

    [Fact]
    public async Task ExternalSessionResetRearmsPermissionRevocationStopSynchronously()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        viewModel.EmergencyStopCommand.Execute(null);
        Assert.Equal(1, service.EmergencyStopCalls);

        await service.ResetAndStartAsync();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.Granted));

        Assert.Equal(2, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task QueuedOldEmergencyStopOutcomeCannotDisableNewSessionSafety()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);

        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.Granted));
        Assert.Equal(1, service.EmergencyStopCalls);
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await service.ResetAndStartAsync();
        Assert.Equal(
            RemoteWindowLifecycle.Active,
            service.Controller.Snapshot.Lifecycle);

        dispatcher.RunAll();

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Equal("EMERGENCY STOP NOT REQUIRED", viewModel.EmergencyStopStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task LateNullServiceObservationCannotClearNewActiveSafety()
    {
        var service = new OutOfOrderSnapshotRemoteWindowService(
            CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);

        Task publishingOldNull = service.PublishBlockingNull();
        await service.NullReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await service.StartAndPublishActiveAsync();
            Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
            Assert.True(viewModel.IsEmergencyStopAvailable);
        }
        finally
        {
            service.ReleaseNullRead();
        }

        await publishingOldNull.WaitAsync(TimeSpan.FromSeconds(2));
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.Granted));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task LateObservationCannotClearAcceptedStartResultSafety()
    {
        var service = new OutOfOrderSnapshotRemoteWindowService(
            CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        using var releaseStartProjection = new ManualResetEventSlim(false);
        var startResultReduced = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.SharingStatus)
                && viewModel.SharingStatus == "REMOTE WINDOW ACTIVE")
            {
                startResultReduced.TrySetResult();
                releaseStartProjection.Wait();
            }
        };
        viewModel.PropertyChanged += observer;
        Task? starting = null;
        try
        {
            Task publishingOldNull = service.PublishBlockingNull();
            await service.NullReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            starting = Task.Run(() => viewModel.StartRemoteWindowAsync());
            await startResultReduced.Task.WaitAsync(TimeSpan.FromSeconds(2));
            service.ReleaseNullRead();
            await publishingOldNull.WaitAsync(TimeSpan.FromSeconds(2));

            permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Revoked,
                DesktopPermissionState.Granted));

            Assert.Equal(1, service.EmergencyStopCalls);
        }
        finally
        {
            viewModel.PropertyChanged -= observer;
            service.ReleaseNullRead();
            releaseStartProjection.Set();
            if (starting is not null)
            {
                await starting.WaitAsync(TimeSpan.FromSeconds(2));
            }

            dispatcher.RunAll();
        }
    }

    [Fact]
    public async Task InactiveObservationBeforeSuccessfulStartResultFailsClosed()
    {
        var service = new DelayedSuccessfulStartResultService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        Task starting = viewModel.StartRemoteWindowAsync();
        await service.StartApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));

        service.PublishInactive();
        service.ReleaseResult();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
    }

    [Fact]
    public async Task OldSuccessfulStartResultCannotStopNewSameControllerSession()
    {
        var service = new DelayedSuccessfulStartResultService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        Task starting = viewModel.StartRemoteWindowAsync();
        await service.StartApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.PublishInactive();
        await service.StartReplacementSessionAndPublishAsync();
        long replacementRevision = service.Controller.Snapshot.Revision;

        service.ReleaseResult();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.Active,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal(replacementRevision, service.Controller.Snapshot.Revision);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task OldSuccessfulStartResultCannotStopEndedReplacementSession()
    {
        var service = new DelayedSuccessfulStartResultService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        Task starting = viewModel.StartRemoteWindowAsync();
        await service.StartApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.PublishInactive();
        await service.StartReplacementSessionAndPublishAsync();
        await service.EndReplacementSessionAndPublishAsync();
        long endedReplacementRevision = service.Controller.Snapshot.Revision;

        service.ReleaseResult();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.Idle,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal(endedReplacementRevision, service.Controller.Snapshot.Revision);
        Assert.Equal("NOT SHARING", viewModel.SharingStatus);
        Assert.False(viewModel.IsEmergencyStopAvailable);
    }

    [Fact]
    public async Task LateObservationCannotOverrideAcceptedEmergencyStopResult()
    {
        var service = new OutOfOrderSnapshotRemoteWindowService(
            CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        await service.StartAndPublishActiveAsync();
        dispatcher.RunAll();
        using var releaseStopProjection = new ManualResetEventSlim(false);
        var stopResultReduced = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.SharingStatus)
                && viewModel.SharingStatus == "EMERGENCY STOPPED")
            {
                stopResultReduced.TrySetResult();
                releaseStopProjection.Wait();
            }
        };
        viewModel.PropertyChanged += observer;
        Task? stopping = null;
        try
        {
            Task publishingOldNull = service.PublishBlockingNull();
            await service.NullReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            service.BlockNextAvailabilityRead();
            stopping = Task.Run(() =>
                viewModel.EmergencyStopCommand.Execute(null));
            await stopResultReduced.Task.WaitAsync(TimeSpan.FromSeconds(2));

            service.ReleaseNullRead();
            await publishingOldNull.WaitAsync(TimeSpan.FromSeconds(2));
            releaseStopProjection.Set();
            await service.AvailabilityReadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2));
            dispatcher.RunAll();

            Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        }
        finally
        {
            viewModel.PropertyChanged -= observer;
            service.ReleaseNullRead();
            releaseStopProjection.Set();
            service.ReleaseAvailabilityRead();
            if (stopping is not null)
            {
                await stopping.WaitAsync(TimeSpan.FromSeconds(2));
            }

            dispatcher.RunAll();
        }
    }

    [Fact]
    public async Task LateObservationCannotOverrideAcceptedResetResult()
    {
        var service = new OutOfOrderSnapshotRemoteWindowService(
            CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        await service.StartAndPublishActiveAsync();
        dispatcher.RunAll();
        viewModel.EmergencyStopCommand.Execute(null);
        dispatcher.RunAll();
        Assert.True(viewModel.IsLocalResetAvailable);
        using var releaseResetProjection = new ManualResetEventSlim(false);
        var resetResultReduced = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.SharingStatus)
                && viewModel.SharingStatus == "NOT SHARING"
                && viewModel.ActivityTitle != "No live Activity")
            {
                resetResultReduced.TrySetResult();
                releaseResetProjection.Wait();
            }
        };
        viewModel.PropertyChanged += observer;
        Task? resetting = null;
        try
        {
            Task publishingOldNull = service.PublishBlockingNull();
            await service.NullReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            service.BlockNextAvailabilityRead();
            resetting = Task.Run(() => viewModel.ResetRemoteWindowAsync());
            await resetResultReduced.Task.WaitAsync(TimeSpan.FromSeconds(2));

            service.ReleaseNullRead();
            await publishingOldNull.WaitAsync(TimeSpan.FromSeconds(2));
            releaseResetProjection.Set();
            await service.AvailabilityReadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2));
            dispatcher.RunAll();

            Assert.NotEqual("No live Activity", viewModel.ActivityTitle);
            Assert.Equal("NOT SHARING", viewModel.SharingStatus);
        }
        finally
        {
            viewModel.PropertyChanged -= observer;
            service.ReleaseNullRead();
            releaseResetProjection.Set();
            service.ReleaseAvailabilityRead();
            if (resetting is not null)
            {
                await resetting.WaitAsync(TimeSpan.FromSeconds(2));
            }

            dispatcher.RunAll();
        }
    }

    [Fact]
    public async Task StartedOldEmergencyStopOutcomeCannotDisableNewControllerSafety()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissions);
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.Granted));
        service.BlockNextAvailabilityRead();
        Task applyingOldOutcome = Task.Run(dispatcher.RunAll);
        await service.AvailabilityReadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        try
        {
            permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Granted,
                DesktopPermissionState.Granted));
            RemoteWindowSessionController replacement = CreateController(
                activityTitle: "New controller session");
            service.ReplaceController(replacement);
            await service.StartAsync();
            await Task.Run(dispatcher.RunAll).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
            Assert.True(viewModel.IsEmergencyStopAvailable);
        }
        finally
        {
            service.ReleaseAvailabilityRead();
        }

        await applyingOldOutcome.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.SharingStatus);
        Assert.Equal("EMERGENCY STOP NOT REQUIRED", viewModel.EmergencyStopStatus);
        Assert.True(viewModel.IsEmergencyStopAvailable);
        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task DisposeStopsBeforeBlockingStartCancellationCallbackRuns()
    {
        var service = new CancellationCallbackBlockingStartService(CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task disposing = Task.Factory.StartNew(
            async () =>
            {
                disposalStarted.TrySetResult();
                await viewModel.DisposeAsync();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await service.EmergencyStopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await service.CancellationCallbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(2));
        }
        finally
        {
            service.ReleaseCancellationCallback();
        }

        await Task.WhenAll(starting, disposing).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task DisposeRetriesPreviouslyUnconfirmedEmergencyStop()
    {
        var capture = new TransientEmergencyCaptureBoundary
        {
            EmergencyStopResult = LocalBoundaryResult.Failed(
                "capture_stop_failed"),
        };
        var service = new ControllerRemoteWindowService(
            CreateController(captureBoundary: capture));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        await service.StartAsync();
        viewModel.EmergencyStopCommand.Execute(null);
        capture.EmergencyStopResult =
            LocalBoundaryResult.Confirmed("capture_stopped");

        await viewModel.DisposeAsync();

        Assert.Equal(2, service.EmergencyStopCalls);
        Assert.Equal(2, capture.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowCaptureState.Stopped,
            service.Controller.Snapshot.CaptureState);
    }

    [Fact]
    public async Task DisposePropagatesUnconfirmedEmergencyStopRetryToAllCallers()
    {
        var capture = new TransientEmergencyCaptureBoundary
        {
            EmergencyStopResult = LocalBoundaryResult.Failed(
                "capture_stop_failed"),
        };
        var service = new ControllerRemoteWindowService(
            CreateController(captureBoundary: capture));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        await service.StartAsync();
        viewModel.EmergencyStopCommand.Execute(null);

        Task firstDisposal = viewModel.DisposeAsync().AsTask();
        Task concurrentDisposal = viewModel.DisposeAsync().AsTask();

        Assert.Same(firstDisposal, concurrentDisposal);
        InvalidOperationException firstFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => firstDisposal);
        InvalidOperationException concurrentFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => concurrentDisposal);
        Assert.Equal(
            "Remote Window Emergency Stop was not fully confirmed during disposal.",
            firstFailure.Message);
        Assert.Equal(firstFailure.Message, concurrentFailure.Message);
        Assert.Equal(2, service.EmergencyStopCalls);
        Assert.True(capture.EmergencyStopCalls >= 2);
    }

    [Fact]
    public async Task DisposeCancelsPermissionRequestBeforeDisposingBoundary()
    {
        var permissions = new BlockingPermissionService();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        Task requesting = viewModel.RequestCapturePermissionAsync();
        await permissions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task queued = viewModel.RequestCapturePermissionAsync();

        Task disposing = viewModel.DisposeAsync().AsTask();

        await Task.WhenAll(requesting, queued, disposing)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(permissions.CancellationObserved);
        Assert.True(permissions.Disposed);
        Assert.False(permissions.WasDisposedWhileRequestActive);
    }

    [Fact]
    public async Task DisposeStopsRemoteWindowBeforeCancellationIgnoringPermissionDrains()
    {
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var permissions = new CancellationIgnoringPermissionService(order);
        var service = new OrderedDisposeRemoteWindowService(order);
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        Task request = viewModel.RequestCapturePermissionAsync();
        await permissions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        await permissions.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(service.SharingActive);
        Assert.False(disposing.IsCompleted);
        permissions.Complete();
        await Task.WhenAll(request, disposing).WaitAsync(TimeSpan.FromSeconds(2));

        string[] events = order.ToArray();
        Assert.True(
            Array.IndexOf(events, "remote-disposed")
                < Array.IndexOf(events, "permission-completed"),
            string.Join(", ", events));
    }

    [Fact]
    public async Task ConcurrentDisposeCallersJoinTheSameCleanupCompletion()
    {
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var permissions = new CancellationIgnoringPermissionService(order);
        var service = new OrderedDisposeRemoteWindowService(order);
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        Task request = viewModel.RequestCapturePermissionAsync();
        await permissions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task firstDisposal = viewModel.DisposeAsync().AsTask();
        await permissions.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task concurrentDisposal = viewModel.DisposeAsync().AsTask();

        Assert.False(firstDisposal.IsCompleted);
        Assert.False(concurrentDisposal.IsCompleted);

        permissions.Complete();
        await Task.WhenAll(request, firstDisposal, concurrentDisposal)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LatePermissionChangeReadCannotStopDisposedRemoteWindowService()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        await service.StartAsync();
        var permissions = new BlockingChangedPermissionService();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);

        Task publishing = Task.Run(permissions.PublishRevokedAndBlockRead);
        await permissions.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = Task.Run(async () => await viewModel.DisposeAsync());
        await service.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        bool disposalCompletedBeforeReadReleased = disposing.IsCompleted;
        permissions.ReleaseRead();
        await Task.WhenAll(publishing, disposing).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(service.Disposed);
        Assert.False(disposalCompletedBeforeReadReleased);
        Assert.False(permissions.WasDisposedWhileReadActive);
        Assert.Equal(0, service.EmergencyStopAfterDisposeCalls);
    }

    [Fact]
    public async Task DispatchedProjectionDoesNotReadRemoteWindowServiceDuringDisposal()
    {
        var service = new BlockingRefreshRemoteWindowService();
        var dispatcher = new QueuedDispatcher();
        var viewModel = new RemoteWindowWorkspaceViewModel(service, dispatcher);
        service.PublishChanged();
        service.BlockNextAvailabilityRead();
        dispatcher.RunAll();

        Task disposing = Task.Run(async () => await viewModel.DisposeAsync());
        await service.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.ReleaseRead();
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(service.ReadStarted.Task.IsCompleted);
        Assert.False(service.WasDisposedWhileReadActive);
    }

    [Fact]
    public async Task DisposeFromServiceGetterReturnsWithoutSelfWaiting()
    {
        var service = new ReentrantDisposalRemoteWindowService();
        var viewModel = new RemoteWindowWorkspaceViewModel(service);
        bool callbackReturned = false;
        service.OnAvailabilityRead = () =>
        {
            viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            callbackReturned = true;
        };

        await Task.Run(service.PublishChanged)
            .WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(callbackReturned);
        Assert.True(service.Disposed);
    }

    [Fact]
    public async Task BlockingPostStartRefreshCannotDelayEmergencyStop()
    {
        var service = new BlockingPostStartRefreshRemoteWindowService(
            CreateController());
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        service.BlockNextAvailabilityRead();
        Task starting = Task.Run(() => viewModel.StartRemoteWindowAsync());
        await service.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsEmergencyStopAvailable);

        Task stopping = Task.Run(
            () => viewModel.EmergencyStopCommand.Execute(null));
        try
        {
            await service.EmergencyStopCalled.Task.WaitAsync(
                TimeSpan.FromSeconds(1));
        }
        finally
        {
            service.ReleaseRead();
        }

        await Task.WhenAll(starting, stopping).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsRemoteWindowStartBeforeDisposingBoundary()
    {
        var service = new BlockingStartRemoteWindowService(
            CreateController(),
            holdCancellationCompletion: true);
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: CreateGrantedCapturePermissionService());
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();

        await service.CancellationSignaled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        bool disposedBeforeStartDrained = service.Disposed;
        service.CompleteCancellation();
        await Task.WhenAll(starting, disposing).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(service.CancellationObserved);
        Assert.True(service.Disposed);
        Assert.False(disposedBeforeStartDrained);
        Assert.False(service.WasDisposedWhileStartActive);
    }

    [Fact]
    public async Task DisposeAttemptsEveryBoundaryAfterEventUnsubscribeFailure()
    {
        var remoteWindow = new ThrowingUnsubscribeRemoteWindowService();
        var permissions = new RecordingDisposePermissionService();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            remoteWindow,
            permissionService: permissions);

        Exception? failure = await Record.ExceptionAsync(
            () => viewModel.DisposeAsync().AsTask());

        Assert.NotNull(failure);
        Assert.True(remoteWindow.Disposed);
        Assert.True(permissions.Disposed);
    }

    [Fact]
    public async Task DisposeAttemptsEveryBoundaryAfterCancellationCallbackFailure()
    {
        var remoteWindow = new ControllerRemoteWindowService(CreateController());
        var permissions = new ThrowingCancellationPermissionService();
        var viewModel = new RemoteWindowWorkspaceViewModel(
            remoteWindow,
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        Task requesting = viewModel.RequestCapturePermissionAsync();
        await permissions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Exception? failure = await Record.ExceptionAsync(
            () => viewModel.DisposeAsync().AsTask());
        await requesting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(failure);
        Assert.True(remoteWindow.Disposed);
        Assert.True(permissions.CancellationObserved);
        Assert.True(permissions.Disposed);
        Assert.False(permissions.WasDisposedWhileRequestActive);
    }

    [Fact]
    public async Task ConcurrentCapturePermissionActivationCallsBoundaryOnce()
    {
        var permissions = new CompletablePermissionService();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;

        Task first = viewModel.RequestCapturePermissionAsync();
        await permissions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task second = viewModel.RequestCapturePermissionAsync();
        permissions.Complete();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, permissions.CaptureRequests);
        Assert.Equal("CAPTURE PERMISSION GRANTED", viewModel.CapturePermissionStatus);
    }

    [Fact]
    public async Task PermissionRequestCompletionCannotOverwriteNewerRevocation()
    {
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: new RevokedBeforeRequestReturnsPermissionService());
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;

        await viewModel.RequestCapturePermissionAsync();

        Assert.Equal(
            "CAPTURE PERMISSION REVOKED",
            viewModel.CapturePermissionStatus);
        Assert.False(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
    }

    [Fact]
    public async Task CaptureRevokedDuringAdmissionDoesNotCrossStartBoundary()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));
        bool revokedDuringAdmission = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (!revokedDuringAdmission
                && args.PropertyName
                    == nameof(RemoteWindowWorkspaceViewModel.IsFallbackStartAvailable)
                && !viewModel.IsFallbackStartAvailable)
            {
                revokedDuringAdmission = true;
                permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
                    DesktopPermissionState.Revoked,
                    DesktopPermissionState.NotDetermined));
            }
        };
        viewModel.PropertyChanged += observer;

        await viewModel.StartRemoteWindowAsync();

        viewModel.PropertyChanged -= observer;
        Assert.True(revokedDuringAdmission);
        Assert.Equal(0, service.StartCalls);
        Assert.Equal(
            "CAPTURE PERMISSION REVOKED",
            viewModel.CapturePermissionStatus);
    }

    [Fact]
    public async Task LaterPermissionStateWinsReorderedChangedCallbacksAtStartAdmission()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new ReorderedChangedPermissionService();
        var dispatcher = new QueuedDispatcher();
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            dispatcher,
            permissionService: permissions);
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        Task staleGranted = permissions.PublishStaleGrantedAndBlockRead();
        await permissions.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        permissions.PublishRevoked();
        permissions.ReleaseRead();
        await staleGranted.WaitAsync(TimeSpan.FromSeconds(2));

        await viewModel.StartRemoteWindowAsync();

        Assert.Equal(0, service.StartCalls);
        dispatcher.RunAll();
        Assert.Equal(
            "CAPTURE PERMISSION REVOKED",
            viewModel.CapturePermissionStatus);
    }

    [Fact]
    public async Task LatestConcurrentStartAloneCrossesAndOwnsSessionContext()
    {
        using RemoteWindowSessionController controller = CreateController();
        _ = await controller.StartAsync(
            new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
        var service = new BlockingResetRemoteWindowService(controller);
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.EmergencyStopCommand.Execute(null);
        Task resetting = viewModel.ResetRemoteWindowAsync();
        await service.ResetApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var firstActivity = new DesktopActivitySnapshot(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Release plan",
            "workspace.note/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);
        var firstTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        viewModel.SetFallbackSelection(firstActivity, firstTarget);
        Task first = viewModel.StartRemoteWindowAsync();

        var latestActivity = firstActivity with
        {
            Title = "Incident review",
        };
        var latestTarget = new DesktopActivityTargetSnapshot(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Peer studio");
        viewModel.IsRemoteDrivingEnabled = true;
        viewModel.SetFallbackSelection(latestActivity, latestTarget);
        Task latest = viewModel.StartRemoteWindowAsync();

        service.CompleteReset();
        await Task.WhenAll(resetting, first, latest)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.StartCalls);
        Assert.Equal(latestActivity.ActivityId, service.RequestedActivityId);
        Assert.Equal(latestTarget.DeviceId, service.RequestedTargetDeviceId);
        Assert.Equal(MirrorParticipantRole.DriverEligible, service.RequestedRole);
        Assert.Equal("REMOTE WINDOW ACTIVE", viewModel.FallbackStatus);
        Assert.Contains(
            latestTarget.DisplayName,
            viewModel.FallbackDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            firstTarget.DisplayName,
            viewModel.FallbackDescription,
            StringComparison.Ordinal);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task CaptureRevocationWinsConcurrentRemoteWindowStart()
    {
        var service = new BlockingStartRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Revoked,
            DesktopPermissionState.NotDetermined));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.Equal(
            "CAPTURE PERMISSION REVOKED",
            viewModel.CapturePermissionStatus);

        service.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task InputRevocationWinsConcurrentDriverEligibleStart()
    {
        var service = new BlockingStartRemoteWindowService(CreateController());
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted));
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.IsRemoteDrivingEnabled = true;
        viewModel.SetFallbackSelection(
            new DesktopActivitySnapshot(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Release plan",
                "workspace.note/v1",
                ActivitySensitivity.Normal,
                ActivityLifecycle.Active),
            new DesktopActivityTargetSnapshot(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk"));

        Task starting = viewModel.StartRemoteWindowAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MirrorParticipantRole.DriverEligible, service.RequestedRole);
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Revoked));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.Equal("INPUT PERMISSION REVOKED", viewModel.InputPermissionStatus);
        Assert.False(viewModel.IsRemoteDrivingEnabled);

        service.CompleteStart();
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
    }

    [Fact]
    public async Task PermissionRequestFailureDoesNotExposeExceptionPayload()
    {
        const string canary = "CAPTURE-PERMISSION-EXCEPTION-CANARY";
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            new ControllerRemoteWindowService(CreateController()),
            permissionService: new ThrowingPermissionService(canary));
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;

        await viewModel.RequestCapturePermissionAsync();

        Assert.Equal(
            "CAPTURE PERMISSION UNAVAILABLE",
            viewModel.CapturePermissionStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.CapturePermissionDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.CapturePermissionRecoveryAction,
            StringComparison.Ordinal);
        Assert.False(viewModel.CanEnableRemoteDriving);
        Assert.False(viewModel.IsRemoteDrivingEnabled);
    }

    [Fact]
    public async Task CapturePermissionRequestFailureStopsConcurrentSilentSession()
    {
        const string canary = "CAPTURE-PERMISSION-START-RACE-CANARY";
        var service = new ControllerRemoteWindowService(CreateController());
        await using var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: new StartThenThrowCapturePermissionService(
                service.Controller,
                canary));
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;

        await viewModel.RequestCapturePermissionAsync();

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        Assert.Equal(
            "CAPTURE PERMISSION UNAVAILABLE",
            viewModel.CapturePermissionStatus);
        Assert.Equal("EMERGENCY STOPPED", viewModel.SharingStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.CapturePermissionDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowingPermissionFailureObserverCannotBlockSilentSessionStop()
    {
        var service = new ControllerRemoteWindowService(CreateController());
        var permissions = new StartThenThrowCapturePermissionService(
            service.Controller,
            "permission-boundary-failure");
        var viewModel = new RemoteWindowWorkspaceViewModel(
            service,
            permissionService: permissions);
        viewModel.ReviewCapturePermissionCommand.Execute(null);
        viewModel.HasAcknowledgedCapturePermissionReview = true;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName
                == nameof(RemoteWindowWorkspaceViewModel.CapturePermissionStatus))
            {
                throw new InvalidOperationException("persistent-observer-failure");
            }
        };
        viewModel.PropertyChanged += observer;

        _ = await Record.ExceptionAsync(
            () => viewModel.RequestCapturePermissionAsync());

        Assert.Equal(1, service.EmergencyStopCalls);
        Assert.Equal(
            RemoteWindowLifecycle.EmergencyStopped,
            service.Controller.Snapshot.Lifecycle);
        viewModel.PropertyChanged -= observer;
        await viewModel.DisposeAsync();
    }

    private static RemoteWindowSessionController CreateController(
        IRemoteWindowCaptureBoundary? captureBoundary = null,
        IRemoteInputBoundary? inputBoundary = null,
        ILocalSharingSessionBoundary? sessionBoundary = null,
        IMirrorAuthorizationSource? authorizationSource = null,
        ActivityId? activityId = null,
        string activityTitle = "Release plan")
    {
        DeviceId hostId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        ActivityId effectiveActivityId = activityId ?? ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        return new RemoteWindowSessionController(
            hostId,
            ActivityInstance.Active(
                ActivityDescriptor.Create(
                    effectiveActivityId,
                    ActivityKind.Parse("workspace.note/v1"),
                    hostId,
                    activityTitle,
                    JsonSerializer.Serialize(new { text = "payload-canary" })),
                ActivityPlacement.On(hostId),
                revision: 1),
            new FixedClock(Now),
            authorizationSource ?? EmptyAuthorization.Instance,
            captureBoundary ?? new ConfirmingCaptureBoundary(),
            inputBoundary ?? new ConfirmingInputBoundary(),
            sessionBoundary ?? new ConfirmingSessionBoundary(),
            TimeSpan.FromMinutes(1));
    }

    private static RecordingPermissionService
        CreateGrantedCapturePermissionService()
    {
        var permissions = new RecordingPermissionService();
        permissions.Publish(new DesktopRemoteWindowPermissionSnapshot(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined));
        return permissions;
    }

    private sealed class ControllerRemoteWindowService(
        RemoteWindowSessionController controller,
        Exception? emergencyStopFailure = null,
        Exception? startFailure = null) : IDesktopRemoteWindowService
    {
        private readonly ManualResetEventSlim availabilityReadRelease =
            new(initialState: true);
        private int blockNextAvailabilityRead;
        public event Action? Changed;

        private RemoteWindowSessionController currentController = controller;

        private int isAvailable = 1;

        public RemoteWindowSessionController Controller => currentController;

        public long ControllerGeneration { get; private set; }

        public int EmergencyStopCalls { get; private set; }

        public int EmergencyStopAfterDisposeCalls { get; private set; }

        public Action? OnEmergencyStop { get; set; }

        public TaskCompletionSource EmergencyStopCalled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AvailabilityReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ResetCalls { get; private set; }

        public int StartCalls { get; private set; }

        public bool Disposed { get; private set; }

        public TaskCompletionSource DisposedSignal { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ActivityId? RequestedActivityId { get; private set; }

        public DeviceId? RequestedTargetDeviceId { get; private set; }

        public MirrorParticipantRole? RequestedRole { get; private set; }

        public bool IsAvailable
        {
            get
            {
                if (Interlocked.Exchange(ref blockNextAvailabilityRead, 0) != 0)
                {
                    AvailabilityReadStarted.TrySetResult();
                    availabilityReadRelease.Wait();
                }

                return Volatile.Read(ref isAvailable) != 0;
            }

            private set => Volatile.Write(ref isAvailable, value ? 1 : 0);
        }

        public string UnavailableReasonCode { get; private set; } = "none";

        private bool HasPublishedSnapshot { get; set; }

        private RemoteWindowSharingSnapshot? PublishedSnapshot { get; set; }

        public void BlockNextAvailabilityRead()
        {
            availabilityReadRelease.Reset();
            Volatile.Write(ref blockNextAvailabilityRead, 1);
        }

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            OnEmergencyStop?.Invoke();
            EmergencyStopCalled.TrySetResult();
            if (Disposed)
            {
                EmergencyStopAfterDisposeCalls++;
            }

            if (emergencyStopFailure is not null)
            {
                throw emergencyStopFailure;
            }

            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            Changed?.Invoke();
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() =>
            HasPublishedSnapshot ? PublishedSnapshot : Controller.Snapshot;

        public void Publish(RemoteWindowSharingSnapshot? snapshot)
        {
            HasPublishedSnapshot = true;
            PublishedSnapshot = snapshot;
            Changed?.Invoke();
        }

        public void PublishChanged() => Changed?.Invoke();

        public void ReleaseAvailabilityRead() => availabilityReadRelease.Set();

        public void ReplaceController(RemoteWindowSessionController replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            currentController.Dispose();
            currentController = replacement;
            ControllerGeneration = checked(ControllerGeneration + 1);
            HasPublishedSnapshot = false;
            PublishedSnapshot = null;
            Changed?.Invoke();
        }

        public void SetAvailable(RemoteWindowSharingSnapshot snapshot)
        {
            IsAvailable = true;
            UnavailableReasonCode = "none";
            Publish(snapshot);
        }

        public void SetUnavailable()
        {
            IsAvailable = false;
            UnavailableReasonCode = "state_unavailable";
            Changed?.Invoke();
        }

        public async Task ResetAndStartAsync()
        {
            _ = await ResetAfterLocalConfirmationAsync();
            await StartAsync();
        }

        public async Task ResetAsync()
        {
            _ = await ResetAfterLocalConfirmationAsync();
        }

        public async ValueTask<RemoteWindowCommandResult>
            ResetAfterLocalConfirmationAsync(
                CancellationToken cancellationToken = default)
        {
            ResetCalls++;
            RemoteWindowCommandResult result =
                await Controller.ResetAfterLocalConfirmationAsync(cancellationToken);
            Changed?.Invoke();
            return result;
        }

        public async Task StartAsync()
        {
            _ = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
            Changed?.Invoke();
        }

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            RequestedActivityId = activityId;
            RequestedTargetDeviceId = targetDeviceId;
            RequestedRole = role;
            RemoteWindowCommandResult result = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            if (startFailure is not null)
            {
                throw startFailure;
            }

            Changed?.Invoke();
            return result;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            Controller.Dispose();
            availabilityReadRelease.Dispose();
            DisposedSignal.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingResetRemoteWindowService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private readonly TaskCompletionSource releaseReset = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed;

        public RemoteWindowSessionController Controller { get; } = controller;

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public TaskCompletionSource ResetApplied { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCalls { get; private set; }

        public ActivityId? RequestedActivityId { get; private set; }

        public DeviceId? RequestedTargetDeviceId { get; private set; }

        public MirrorParticipantRole? RequestedRole { get; private set; }

        public void CompleteReset() => releaseReset.TrySetResult();

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            Changed?.Invoke();
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => Controller.Snapshot;

        public async ValueTask<RemoteWindowCommandResult>
            ResetAfterLocalConfirmationAsync(
                CancellationToken cancellationToken = default)
        {
            RemoteWindowCommandResult result =
                await Controller.ResetAfterLocalConfirmationAsync(cancellationToken);
            Changed?.Invoke();
            ResetApplied.TrySetResult();
            await releaseReset.Task.WaitAsync(cancellationToken);
            return result;
        }

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            RequestedActivityId = activityId;
            RequestedTargetDeviceId = targetDeviceId;
            RequestedRole = role;
            RemoteWindowCommandResult result = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            Changed?.Invoke();
            return result;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class EmptyAuthorization : IMirrorAuthorizationSource
    {
        public static EmptyAuthorization Instance { get; } = new();

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            CapabilityGrant.None;
    }

    private sealed class FixedAuthorizationSource(CapabilityGrant grant) :
        IMirrorAuthorizationSource
    {
        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) => grant;
    }

    private sealed class MutableAuthorizationSource(CapabilityGrant grant) :
        IMirrorAuthorizationSource
    {
        private CapabilityGrant currentGrant = grant;

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            Volatile.Read(ref currentGrant);

        public void SetGrant(CapabilityGrant next) =>
            Volatile.Write(ref currentGrant, next);
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
            LocalBoundaryResult.Confirmed("capture_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class BlockingCaptureBoundary : IRemoteWindowCaptureBoundary
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => release.TrySetResult();

        public async ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return LocalBoundaryResult.Confirmed("capture_started");
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class BlockingFailingCaptureBoundary :
        IRemoteWindowCaptureBoundary
    {
        private readonly TaskCompletionSource releaseStart = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void CompleteStart() => releaseStart.TrySetResult();

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("capture_emergency_stopped");

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public async ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await releaseStart.Task.WaitAsync(cancellationToken);
            return LocalBoundaryResult.Failed("capture_start_failed");
        }

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class StartResultCaptureBoundary(
        LocalBoundaryResult startResult) : IRemoteWindowCaptureBoundary
    {
        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(startResult);

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class ConfirmingInputBoundary : IRemoteInputBoundary
    {
        public ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("input_injected"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");
    }

    private sealed class TransientEmergencyCaptureBoundary :
        IRemoteWindowCaptureBoundary
    {
        public int EmergencyStopCalls { get; private set; }

        public LocalBoundaryResult EmergencyStopResult { get; set; } =
            LocalBoundaryResult.Confirmed("capture_stopped");

        public LocalBoundaryResult EmergencyStopNow()
        {
            EmergencyStopCalls++;
            return EmergencyStopResult;
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            LocalBoundaryResult.Confirmed("capture_started"));

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class ConfirmingSessionBoundary : ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_stopped");
    }

    private sealed class StopResultCaptureBoundary(
        LocalBoundaryResult emergencyStopResult) : IRemoteWindowCaptureBoundary
    {
        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("capture_started"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public LocalBoundaryResult EmergencyStopNow() => emergencyStopResult;

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class StopResultInputBoundary(
        LocalBoundaryResult emergencyStopResult) : IRemoteInputBoundary
    {
        public ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("input_injected"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("input_resumed");

        public LocalBoundaryResult EmergencyStopNow() => emergencyStopResult;

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");
    }

    private sealed class StopResultSessionBoundary(
        LocalBoundaryResult emergencyStopResult) : ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() => emergencyStopResult;
    }

    private sealed class ThrowingSnapshotService(string exceptionMessage) :
        IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() =>
            throw new IOException(exceptionMessage);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingRefreshRemoteWindowService :
        IDesktopRemoteWindowService
    {
        private readonly ManualResetEventSlim releaseRead = new(initialState: false);
        private int availabilityReads;
        private int blockReadNumber = int.MaxValue;
        private int readActive;

        public event Action? Changed;

        public TaskCompletionSource DisposeStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable
        {
            get
            {
                int readNumber = Interlocked.Increment(ref availabilityReads);
                if (readNumber == Volatile.Read(ref blockReadNumber))
                {
                    Volatile.Write(ref readActive, 1);
                    ReadStarted.TrySetResult();
                    releaseRead.Wait();
                    Volatile.Write(ref readActive, 0);
                }

                return true;
            }
        }

        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string UnavailableReasonCode => "none";

        public bool WasDisposedWhileReadActive { get; private set; }

        public void BlockNextAvailabilityRead() =>
            Volatile.Write(
                ref blockReadNumber,
                Volatile.Read(ref availabilityReads) + 1);

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public void PublishChanged() => Changed?.Invoke();

        public void ReleaseRead() => releaseRead.Set();

        public ValueTask DisposeAsync()
        {
            WasDisposedWhileReadActive = Volatile.Read(ref readActive) != 0;
            DisposeStarted.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OutOfOrderSnapshotRemoteWindowService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private readonly ManualResetEventSlim releaseAvailabilityRead =
            new(initialState: true);
        private readonly ManualResetEventSlim releaseNullRead =
            new(initialState: false);
        private int blockNextAvailabilityRead;
        private int blockNextNullRead;
        private RemoteWindowSharingSnapshot? snapshot;

        public event Action? Changed;

        public RemoteWindowSessionController Controller { get; } = controller;

        public int EmergencyStopCalls { get; private set; }

        public TaskCompletionSource AvailabilityReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable
        {
            get
            {
                if (Interlocked.Exchange(ref blockNextAvailabilityRead, 0) != 0)
                {
                    AvailabilityReadStarted.TrySetResult();
                    releaseAvailabilityRead.Wait();
                }

                return true;
            }
        }

        public TaskCompletionSource NullReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            Volatile.Write(ref snapshot, result.Snapshot);
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot()
        {
            RemoteWindowSharingSnapshot? captured = Volatile.Read(ref snapshot);
            if (Interlocked.Exchange(ref blockNextNullRead, 0) != 0)
            {
                NullReadStarted.TrySetResult();
                releaseNullRead.Wait();
            }

            return captured;
        }

        public Task PublishBlockingNull()
        {
            Volatile.Write(ref snapshot, null);
            Volatile.Write(ref blockNextNullRead, 1);
            return Task.Run(() => Changed?.Invoke());
        }

        public void BlockNextAvailabilityRead()
        {
            releaseAvailabilityRead.Reset();
            Volatile.Write(ref blockNextAvailabilityRead, 1);
        }

        public void ReleaseAvailabilityRead() => releaseAvailabilityRead.Set();

        public void ReleaseNullRead() => releaseNullRead.Set();

        public async ValueTask<RemoteWindowCommandResult>
            ResetAfterLocalConfirmationAsync(
                CancellationToken cancellationToken = default)
        {
            RemoteWindowCommandResult result =
                await Controller.ResetAfterLocalConfirmationAsync(cancellationToken);
            Volatile.Write(ref snapshot, result.Snapshot);
            return result;
        }

        public async Task StartAndPublishActiveAsync()
        {
            _ = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
            Volatile.Write(ref snapshot, Controller.Snapshot);
            Changed?.Invoke();
        }

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            RemoteWindowCommandResult result = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            Volatile.Write(ref snapshot, result.Snapshot);
            return result;
        }

        public ValueTask DisposeAsync()
        {
            Controller.Dispose();
            releaseAvailabilityRead.Dispose();
            releaseNullRead.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantDisposalRemoteWindowService :
        IDesktopRemoteWindowService
    {
        public event Action? Changed;

        public bool Disposed { get; private set; }

        public bool IsAvailable
        {
            get
            {
                Action? callback = OnAvailabilityRead;
                OnAvailabilityRead = null;
                callback?.Invoke();
                return true;
            }
        }

        public Action? OnAvailabilityRead { get; set; }

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public void PublishChanged() => Changed?.Invoke();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingPostStartRefreshRemoteWindowService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private readonly ManualResetEventSlim releaseRead = new(initialState: true);
        private int blockNextAvailabilityRead;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public RemoteWindowSessionController Controller { get; } = controller;

        public int EmergencyStopCalls { get; private set; }

        public TaskCompletionSource EmergencyStopCalled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable
        {
            get
            {
                if (Interlocked.Exchange(ref blockNextAvailabilityRead, 0) != 0)
                {
                    ReadStarted.TrySetResult();
                    releaseRead.Wait();
                }

                return true;
            }
        }

        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string UnavailableReasonCode => "none";

        public void BlockNextAvailabilityRead()
        {
            releaseRead.Reset();
            Volatile.Write(ref blockNextAvailabilityRead, 1);
        }

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            EmergencyStopCalled.TrySetResult();
            return Controller.EmergencyStop();
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => Controller.Snapshot;

        public void ReleaseRead() => releaseRead.Set();

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default) =>
            await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);

        public ValueTask DisposeAsync()
        {
            Controller.Dispose();
            releaseRead.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingStartRemoteWindowService(
        RemoteWindowSessionController controller,
        ProtectionSnapshot? startProtection = null,
        bool holdCancellationCompletion = false,
        bool ignoreCancellation = false) : IDesktopRemoteWindowService
    {
        private readonly TaskCompletionSource releaseCancellation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseStart = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed;

        public RemoteWindowSessionController Controller { get; } = controller;

        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource CancellationSignaled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public int EmergencyStopCalls { get; private set; }

        public bool IsAvailable => true;

        public ActivityId? RequestedActivityId { get; private set; }

        public MirrorParticipantRole? RequestedRole { get; private set; }

        public DeviceId? RequestedTargetDeviceId { get; private set; }

        public string UnavailableReasonCode => "none";

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposedWhileStartActive { get; private set; }

        private bool StartActive { get; set; }

        public void CompleteStart() => releaseStart.TrySetResult();

        public void CompleteCancellation() => releaseCancellation.TrySetResult();

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            Changed?.Invoke();
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => Controller.Snapshot;

        public async ValueTask<RemoteWindowCommandResult>
            ResetAfterLocalConfirmationAsync(
                CancellationToken cancellationToken = default)
        {
            RemoteWindowCommandResult result =
                await Controller.ResetAfterLocalConfirmationAsync(cancellationToken);
            Changed?.Invoke();
            return result;
        }

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            StartActive = true;
            RequestedActivityId = activityId;
            RequestedTargetDeviceId = targetDeviceId;
            RequestedRole = role;
            Started.TrySetResult();
            try
            {
                if (ignoreCancellation)
                {
                    await releaseStart.Task;
                }
                else
                {
                    await releaseStart.Task.WaitAsync(cancellationToken);
                }

                RemoteWindowCommandResult result = await Controller.StartAsync(
                    startProtection
                        ?? new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                    ignoreCancellation ? CancellationToken.None : cancellationToken);
                Changed?.Invoke();
                return result;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                CancellationSignaled.TrySetResult();
                if (holdCancellationCompletion)
                {
                    await releaseCancellation.Task;
                }

                throw;
            }
            finally
            {
                StartActive = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            WasDisposedWhileStartActive = StartActive;
            Disposed = true;
            Controller.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelayedSuccessfulStartResultService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private readonly TaskCompletionSource releaseResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private RemoteWindowSharingSnapshot? snapshot;

        public event Action? Changed;

        public RemoteWindowSessionController Controller { get; } = controller;

        public int EmergencyStopCalls { get; private set; }

        public bool IsAvailable => true;

        public TaskCompletionSource StartApplied { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            Volatile.Write(ref snapshot, result.Snapshot);
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() =>
            Volatile.Read(ref snapshot);

        public void PublishInactive()
        {
            Volatile.Write(ref snapshot, null);
            Changed?.Invoke();
        }

        public void ReleaseResult() => releaseResult.TrySetResult();

        public async Task StartReplacementSessionAndPublishAsync()
        {
            _ = Controller.EmergencyStop();
            _ = await Controller.ResetAfterLocalConfirmationAsync();
            _ = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
            Volatile.Write(ref snapshot, Controller.Snapshot);
            Changed?.Invoke();
        }

        public async Task EndReplacementSessionAndPublishAsync()
        {
            _ = Controller.EmergencyStop();
            _ = await Controller.ResetAfterLocalConfirmationAsync();
            Volatile.Write(ref snapshot, null);
            Changed?.Invoke();
        }

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            RemoteWindowCommandResult result = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            StartApplied.TrySetResult();
            await releaseResult.Task;
            return result;
        }

        public ValueTask DisposeAsync()
        {
            Controller.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationCallbackBlockingStartService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private readonly TaskCompletionSource cancellationCallbackCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim releaseCancellationCallback =
            new(initialState: false);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource CancellationCallbackEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteWindowSessionController Controller { get; } = controller;

        public TaskCompletionSource EmergencyStopCalled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int EmergencyStopCalls { get; private set; }

        public bool IsAvailable => true;

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            EmergencyStopCalled.TrySetResult();
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => Controller.Snapshot;

        public void ReleaseCancellationCallback() =>
            releaseCancellationCallback.Set();

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    CancellationCallbackEntered.TrySetResult();
                    releaseCancellationCallback.Wait();
                    cancellationCallbackCompleted.TrySetResult();
                });
            Started.TrySetResult();
            await cancellationCallbackCompleted.Task;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable after cancellation.");
        }

        public ValueTask DisposeAsync()
        {
            Controller.Dispose();
            releaseCancellationCallback.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GenerationSwitchingStartService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private readonly List<RemoteWindowSessionController> controllers =
            [controller];

        public event Action? Changed;

        public RemoteWindowSessionController Controller { get; private set; } =
            controller;

        public long ControllerGeneration { get; private set; }

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            Changed?.Invoke();
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => Controller.Snapshot;

        public async Task ReplaceAndStartAsync(
            RemoteWindowSessionController replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            controllers.Add(replacement);
            Controller = replacement;
            ControllerGeneration = checked(ControllerGeneration + 1);
            Changed?.Invoke();
            _ = await replacement.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"));
            Changed?.Invoke();
        }

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            RemoteWindowSessionController admittedController = Controller;
            RemoteWindowCommandResult result = await admittedController.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            Changed?.Invoke();
            return result;
        }

        public ValueTask DisposeAsync()
        {
            foreach (RemoteWindowSessionController ownedController in controllers)
            {
                ownedController.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAvailabilityService(string exceptionMessage) :
        IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsAvailable => throw new IOException(exceptionMessage);

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StartThenThrowSnapshotService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private bool throwOnSnapshotRead;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public RemoteWindowSessionController Controller { get; } = controller;

        public int EmergencyStopCalls { get; private set; }

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopCalls++;
            return Controller.EmergencyStop();
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => throwOnSnapshotRead
            ? throw new IOException("snapshot-read-failure")
            : Controller.Snapshot;

        public async ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            RemoteWindowCommandResult result = await Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            throwOnSnapshotRead = true;
            return result;
        }

        public ValueTask DisposeAsync()
        {
            Controller.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmergencyStopThenThrowSnapshotService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private bool throwOnSnapshotRead;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public RemoteWindowSessionController Controller { get; } = controller;

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            RemoteWindowEmergencyStopResult result = Controller.EmergencyStop();
            throwOnSnapshotRead = true;
            return result;
        }

        public RemoteWindowSharingSnapshot? GetSnapshot() => throwOnSnapshotRead
            ? throw new IOException("snapshot-read-failure")
            : Controller.Snapshot;

        public ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default) =>
            Controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);

        public ValueTask DisposeAsync()
        {
            Controller.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class QueuedDispatcher : IDesktopUiDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action>
            callbacks = new();

        public void Post(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            callbacks.Enqueue(callback);
        }

        public void RunAll()
        {
            while (callbacks.TryDequeue(out Action? callback))
            {
                callback();
            }
        }
    }

    private sealed class ReverseQueuedDispatcher : IDesktopUiDispatcher
    {
        private readonly List<Action> callbacks = [];
        private readonly object gate = new();

        public void Post(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            lock (gate)
            {
                callbacks.Add(callback);
            }
        }

        public void RunAllReverse()
        {
            while (true)
            {
                Action callback;
                lock (gate)
                {
                    if (callbacks.Count == 0)
                    {
                        return;
                    }

                    int lastIndex = callbacks.Count - 1;
                    callback = callbacks[lastIndex];
                    callbacks.RemoveAt(lastIndex);
                }

                callback();
            }
        }
    }

    private sealed class ThrowingUnavailableReasonService(string exceptionMessage) :
        IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsAvailable => false;

        public string UnavailableReasonCode => throw new IOException(exceptionMessage);

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingUnsubscribeRemoteWindowService :
        IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove => throw new IOException("unsubscribe-canary");
        }

        public bool Disposed { get; private set; }

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedDisposeRemoteWindowService(
        System.Collections.Concurrent.ConcurrentQueue<string> order) :
        IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsAvailable => true;

        public TaskCompletionSource DisposedSignal { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SharingActive { get; private set; } = true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask DisposeAsync()
        {
            SharingActive = false;
            order.Enqueue("remote-disposed");
            DisposedSignal.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationIgnoringPermissionService(
        System.Collections.Concurrent.ConcurrentQueue<string> order) :
        IDesktopRemoteWindowPermissionService
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => release.TrySetResult();

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => snapshot;

        public async ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => CancellationObserved.TrySetResult());
            Started.TrySetResult();
            await release.Task;
            order.Enqueue("permission-completed");
            snapshot = snapshot with
            {
                Capture = DesktopPermissionState.Granted,
            };
            return snapshot;
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            order.Enqueue("permission-disposed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDisposePermissionService :
        IDesktopRemoteWindowPermissionService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool Disposed { get; private set; }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => new(
            DesktopPermissionState.Unsupported,
            DesktopPermissionState.Unsupported);

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CachedChangedPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private readonly ManualResetEventSlim releaseGetter =
            new(initialState: false);
        private int blockGetter;
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted);

        public event Action? Changed;

        public TaskCompletionSource GetterReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot()
        {
            if (Volatile.Read(ref blockGetter) != 0)
            {
                GetterReadStarted.TrySetResult();
                releaseGetter.Wait();
            }

            return Volatile.Read(ref snapshot);
        }

        public void PublishRevokedAndBlockGetter()
        {
            Volatile.Write(
                ref snapshot,
                snapshot with
                {
                    Capture = DesktopPermissionState.Revoked,
                });
            Volatile.Write(ref blockGetter, 1);
            Changed?.Invoke();
        }

        public void ReleaseGetter() => releaseGetter.Set();

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public bool TryGetCachedSnapshot(
            out DesktopRemoteWindowPermissionSnapshot cachedSnapshot)
        {
            cachedSnapshot = Volatile.Read(ref snapshot);
            return true;
        }

        public ValueTask DisposeAsync()
        {
            releaseGetter.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingChangedPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private readonly ManualResetEventSlim releaseRead = new(initialState: false);
        private int blockReads;
        private int readActive;
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.Granted,
            DesktopPermissionState.Granted);

        public event Action? Changed;

        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot()
        {
            if (Volatile.Read(ref blockReads) != 0)
            {
                Volatile.Write(ref readActive, 1);
                try
                {
                    ReadStarted.TrySetResult();
                    releaseRead.Wait();
                }
                finally
                {
                    Volatile.Write(ref readActive, 0);
                }
            }

            return snapshot;
        }

        public void PublishRevokedAndBlockRead()
        {
            snapshot = snapshot with
            {
                Capture = DesktopPermissionState.Revoked,
            };
            Volatile.Write(ref blockReads, 1);
            Changed?.Invoke();
        }

        public void ReleaseRead() => releaseRead.Set();

        public bool WasDisposedWhileReadActive { get; private set; }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            WasDisposedWhileReadActive = Volatile.Read(ref readActive) != 0;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public event Action? Changed;

        public int CaptureRequests { get; private set; }

        public bool Disposed { get; private set; }

        public int InputRequests { get; private set; }

        public DesktopPermissionState NextCaptureState { get; set; } =
            DesktopPermissionState.Granted;

        public DesktopPermissionState NextInputState { get; set; } =
            DesktopPermissionState.Granted;

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => snapshot;

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureRequests++;
            snapshot = snapshot with
            {
                Capture = NextCaptureState,
            };
            Changed?.Invoke();
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputRequests++;
            snapshot = snapshot with
            {
                Input = NextInputState,
            };
            Changed?.Invoke();
            return ValueTask.FromResult(snapshot);
        }

        public void Publish(DesktopRemoteWindowPermissionSnapshot next)
        {
            snapshot = next;
            Changed?.Invoke();
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReorderedChangedPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private readonly ManualResetEventSlim releaseRead = new(initialState: false);
        private int blockNextRead;
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.Granted,
            DesktopPermissionState.NotDetermined);

        public event Action? Changed;

        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot()
        {
            DesktopRemoteWindowPermissionSnapshot observed = snapshot;
            if (Interlocked.Exchange(ref blockNextRead, 0) != 0)
            {
                ReadStarted.TrySetResult();
                releaseRead.Wait();
            }

            return observed;
        }

        public Task PublishStaleGrantedAndBlockRead()
        {
            Volatile.Write(ref blockNextRead, 1);
            return Task.Run(() => Changed?.Invoke());
        }

        public void PublishRevoked()
        {
            snapshot = snapshot with
            {
                Capture = DesktopPermissionState.Revoked,
            };
            Changed?.Invoke();
        }

        public void ReleaseRead() => releaseRead.Set();

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private bool requestActive;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool CancellationObserved { get; private set; }

        public bool Disposed { get; private set; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposedWhileRequestActive { get; private set; }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public async ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            requestActive = true;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The blocking permission request unexpectedly completed.");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
            finally
            {
                requestActive = false;
            }
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            WasDisposedWhileRequestActive = requestActive;
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingCancellationPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private readonly TaskCompletionSource cancellation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private bool requestActive;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool CancellationObserved { get; private set; }

        public bool Disposed { get; private set; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposedWhileRequestActive { get; private set; }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public async ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            requestActive = true;
            using CancellationTokenRegistration throwingRegistration =
                cancellationToken.Register(() =>
                {
                    CancellationObserved = true;
                    cancellation.TrySetCanceled(cancellationToken);
                    throw new IOException("cancellation-callback-canary");
                });
            Started.TrySetResult();
            try
            {
                await cancellation.Task;
                throw new InvalidOperationException(
                    "The throwing cancellation request unexpectedly completed.");
            }
            finally
            {
                requestActive = false;
            }
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            WasDisposedWhileRequestActive = requestActive;
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompletablePermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public event Action? Changed;

        public int CaptureRequests { get; private set; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => snapshot;

        public async ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            CaptureRequests++;
            Started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            snapshot = snapshot with
            {
                Capture = DesktopPermissionState.Granted,
            };
            Changed?.Invoke();
            return snapshot;
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public void Complete() => release.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingPermissionService(string exceptionMessage) :
        IDesktopRemoteWindowPermissionService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new IOException(exceptionMessage));
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingPermissionSnapshotService(string exceptionMessage) :
        IDesktopRemoteWindowPermissionService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() =>
            throw new IOException(exceptionMessage);

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StartThenThrowCapturePermissionService(
        RemoteWindowSessionController controller,
        string exceptionMessage) : IDesktopRemoteWindowPermissionService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public async ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            _ = await controller.StartAsync(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "test"),
                cancellationToken);
            throw new IOException(exceptionMessage);
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RevokedBeforeRequestReturnsPermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private DesktopRemoteWindowPermissionSnapshot snapshot = new(
            DesktopPermissionState.NotDetermined,
            DesktopPermissionState.NotDetermined);

        public event Action? Changed;

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => snapshot;

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DesktopRemoteWindowPermissionSnapshot staleGrant = snapshot with
            {
                Capture = DesktopPermissionState.Granted,
            };
            snapshot = snapshot with
            {
                Capture = DesktopPermissionState.Revoked,
            };
            Changed?.Invoke();
            return ValueTask.FromResult(staleGrant);
        }

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DesktopRemoteWindowPermissionSnapshot>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
