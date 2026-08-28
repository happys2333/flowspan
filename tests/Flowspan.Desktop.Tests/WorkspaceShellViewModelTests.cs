using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class WorkspaceShellViewModelTests
{
    private static Task RunOnDedicatedThread(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static Task RunOnDedicatedThread(Func<Task> action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

    [Fact]
    public async Task InitializeAsyncExposesTruthfulSafeState()
    {
        var startup = new StubStartup(new LocalIdentitySnapshot(
            "Desk",
            "11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            "Operating-system protected",
            false));
        await using var viewModel = new WorkspaceShellViewModel(startup);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsIdentityAvailable);
        Assert.False(viewModel.IsStartupBlocked);
        Assert.False(viewModel.IsTestMode);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.Equal(
            "REMOTE WINDOW UNAVAILABLE",
            viewModel.RemoteWindow.SharingStatus);
        Assert.Equal("LOCAL WORKSPACE READY", viewModel.StartupStatus);
        Assert.Contains("sharing remain inactive", viewModel.StartupDescription);
        Assert.False(viewModel.LocalData.IsHistoryAvailable);
        Assert.Equal(
            "OPERATION HISTORY UNAVAILABLE",
            viewModel.LocalData.HistoryStatus);
    }

    [Fact]
    public async Task ToggleIdentityDetailsCommandChangesVisibleTextAndState()
    {
        await using var viewModel = CreateReadyViewModel();
        await viewModel.InitializeAsync();

        viewModel.ToggleIdentityDetailsCommand.Execute(null);

        Assert.True(viewModel.IsIdentityDetailsVisible);
        Assert.Equal("Hide identity details", viewModel.IdentityDetailsActionLabel);

        viewModel.ToggleIdentityDetailsCommand.Execute(null);

        Assert.False(viewModel.IsIdentityDetailsVisible);
        Assert.Equal("Show identity details", viewModel.IdentityDetailsActionLabel);
    }

    [Fact]
    public async Task InitializeAsyncBlocksWithoutLeakingStartupException()
    {
        const string canary = "CANARY_SECRET_STORE_DETAIL";
        await using var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new IOException(canary)));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsStartupBlocked);
        Assert.False(viewModel.IsIdentityAvailable);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.Equal("IDENTITY UNAVAILABLE", viewModel.StartupStatus);
        Assert.DoesNotContain(canary, viewModel.StartupDescription, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, viewModel.RecoveryAction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeDuringInitializationCancelsBeforeDisposingStartup()
    {
        var startup = new BlockingStartup();
        var viewModel = new WorkspaceShellViewModel(startup);
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        await Task.WhenAll(initialization, disposing)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(startup.Disposed);
        Assert.False(startup.WasDisposedWhileInitializing);
    }

    [Fact]
    public async Task DisposeStopsLiveSharingWhileCancellationIgnoringInitializationDrains()
    {
        var order = new List<string>();
        var startup = new CancellationIgnoringStartup(order);
        var authority = new OrderedTrustAuthority(order);
        var networkFactory = new OrderedNetworkFactory(order);
        var runtime = new DesktopLocalPairingRuntime(networkFactory);
        var remoteWindowService = new OrderedRemoteWindowService(order);
        var viewModel = new WorkspaceShellViewModel(
            startup,
            trustAuthority: authority,
            localPairingRuntime: runtime,
            remoteWindowService: remoteWindowService);
        await runtime.EnableAsync();
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(remoteWindowService.SharingActive);

        Task disposing = viewModel.DisposeAsync().AsTask();

        try
        {
            await Task.WhenAll(
                    remoteWindowService.SharingStopped.Task,
                    networkFactory.Session.Stopped.Task)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(
                ["network", "remote-window"],
                order.Order(StringComparer.Ordinal));
            Assert.False(remoteWindowService.SharingActive);
            Assert.False(disposing.IsCompleted);
            Assert.False(startup.Disposed);
            Assert.DoesNotContain("trust", order);
        }
        finally
        {
            startup.ReleaseInitialization();
            await Task.WhenAll(initialization, disposing)
                .WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.True(startup.Disposed);
        Assert.False(startup.WasDisposedWhileInitializing);
        AssertSafetyTeardownPrecedesDependencies(order);
    }

    [Fact]
    public async Task DisposeStartsPairingTeardownBeforeRemoteWindowTeardownCompletes()
    {
        var order = new List<string>();
        var remoteWindowService = new BlockingDisposeRemoteWindowService(order);
        var networkFactory = new OrderedNetworkFactory(order);
        var runtime = new DesktopLocalPairingRuntime(networkFactory);
        var viewModel = new WorkspaceShellViewModel(
            new OrderedStartup(order),
            trustAuthority: new OrderedTrustAuthority(order),
            localPairingRuntime: runtime,
            remoteWindowService: remoteWindowService);
        await runtime.EnableAsync();

        Task disposing = viewModel.DisposeAsync().AsTask();
        await remoteWindowService.DisposeEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await networkFactory.Session.Stopped.Task
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(networkFactory.Session.DisposeStartedOnThreadPool);
            Assert.False(disposing.IsCompleted);
            Assert.DoesNotContain("trust", order);
        }
        finally
        {
            remoteWindowService.ReleaseDispose();
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        }

        AssertSafetyTeardownPrecedesDependencies(order);
    }

    [Fact]
    public async Task DisposeStartsPairingTeardownWhenRemoteWindowDisposeBlocksSynchronously()
    {
        var order = new List<string>();
        var remoteWindowService = new SynchronouslyBlockingDisposeRemoteWindowService(
            order);
        var networkFactory = new OrderedNetworkFactory(order);
        var runtime = new DesktopLocalPairingRuntime(networkFactory);
        var viewModel = new WorkspaceShellViewModel(
            new OrderedStartup(order),
            trustAuthority: new OrderedTrustAuthority(order),
            localPairingRuntime: runtime,
            remoteWindowService: remoteWindowService);
        await runtime.EnableAsync();

        Task disposing = RunOnDedicatedThread(
            () => viewModel.DisposeAsync().AsTask());
        await remoteWindowService.DisposeEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(remoteWindowService.DisposeStartedOnThreadPool);

        try
        {
            await networkFactory.Session.Stopped.Task
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(disposing.IsCompleted);
            Assert.DoesNotContain("trust", order);
        }
        finally
        {
            remoteWindowService.ReleaseDispose();
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task DisposeStartsLifetimeCancellationWithBothSafetyTeardowns()
    {
        var order = new List<string>();
        var startup = new BlockingCancellationCallbackStartup(order);
        var remoteWindowService = new BlockingDisposeRemoteWindowService(order);
        var networkFactory = new BlockingDisposeNetworkFactory(order);
        var runtime = new DesktopLocalPairingRuntime(networkFactory);
        var viewModel = new WorkspaceShellViewModel(
            startup,
            trustAuthority: new OrderedTrustAuthority(order),
            localPairingRuntime: runtime,
            remoteWindowService: remoteWindowService);
        await runtime.EnableAsync();
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();

        try
        {
            await Task.WhenAll(
                    startup.CancellationCallbackEntered.Task,
                    remoteWindowService.DisposeEntered.Task,
                    networkFactory.Session.DisposeEntered.Task)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(disposing.IsCompleted);
            Assert.DoesNotContain("trust", order);
        }
        finally
        {
            remoteWindowService.ReleaseDispose();
            networkFactory.Session.ReleaseDispose();
            await startup.CancellationCallbackEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(2));
            startup.ReleaseCancellationCallback();
            startup.ReleaseInitialization();
            await Task.WhenAll(initialization, disposing)
                .WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task DisposeCrossesRemoteWindowEmergencyStopSynchronouslyBeforeCancellation()
    {
        var order = new List<string>();
        var startup = new BlockingCancellationCallbackStartup(order);
        var remoteWindowService = await ActiveRemoteWindowService.CreateAsync();
        var viewModel = new WorkspaceShellViewModel(
            startup,
            remoteWindowService: remoteWindowService,
            remoteWindowPermissionService: new GrantedCapturePermissionService(
                DesktopPermissionState.Granted));
        bool cancellationObservedEmergencyStop = false;
        startup.CancellationCallbackAction = () =>
        {
            cancellationObservedEmergencyStop =
                remoteWindowService.EmergencyStopCalls > 0;
        };
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task;
        var disposalStarted = new TaskCompletionSource<Task>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int disposalThreadId = 0;
        var disposalThread = new Thread(() =>
        {
            disposalThreadId = Environment.CurrentManagedThreadId;
            disposalStarted.TrySetResult(viewModel.DisposeAsync().AsTask());
        });

        disposalThread.Start();
        await remoteWindowService.EmergencyStopCalled.Task;
        Task disposing = await disposalStarted.Task;
        disposalThread.Join();

        try
        {
            await startup.CancellationCallbackActionCompleted.Task;
            Assert.Equal(
                disposalThreadId,
                remoteWindowService.EmergencyStopThreadId);
            Assert.True(cancellationObservedEmergencyStop);
        }
        finally
        {
            startup.ReleaseCancellationCallback();
            startup.ReleaseInitialization();
            await Task.WhenAll(initialization, disposing);
        }
    }

    [Fact]
    public async Task DisposeStopsLiveSharingBeforeBlockingCancellationCallbackReturns()
    {
        var order = new List<string>();
        var startup = new BlockingCancellationCallbackStartup(order);
        var networkFactory = new OrderedNetworkFactory(order);
        var runtime = new DesktopLocalPairingRuntime(networkFactory);
        var remoteWindowService = new OrderedRemoteWindowService(order);
        var viewModel = new WorkspaceShellViewModel(
            startup,
            trustAuthority: new OrderedTrustAuthority(order),
            localPairingRuntime: runtime,
            remoteWindowService: remoteWindowService);
        Task? reentrantDisposing = null;
        startup.CancellationCallbackAction = () =>
            reentrantDisposing = RunOnDedicatedThread(
                () => viewModel.DisposeAsync().AsTask());
        await runtime.EnableAsync();
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = RunOnDedicatedThread(
            () => viewModel.DisposeAsync().AsTask());
        await startup.CancellationCallbackEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        await startup.CancellationCallbackActionCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await Task.WhenAll(
                    remoteWindowService.SharingStopped.Task,
                    networkFactory.Session.Stopped.Task)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(remoteWindowService.SharingActive);
            Assert.False(disposing.IsCompleted);
            Assert.False(startup.Disposed);
            Assert.DoesNotContain("trust", order);
        }
        finally
        {
            startup.ReleaseCancellationCallback();
            startup.ReleaseInitialization();
            await Task.WhenAll(initialization, disposing, reentrantDisposing!)
                .WaitAsync(TimeSpan.FromSeconds(2));
        }

        AssertSafetyTeardownPrecedesDependencies(order);
    }

    [Fact]
    public async Task CancellationCallbackDisposeDoesNotWaitOnShellCleanup()
    {
        var order = new List<string>();
        var startup = new BlockingCancellationCallbackStartup(order);
        var viewModel = new WorkspaceShellViewModel(startup);
        bool reentrantDisposeCompletedSynchronously = false;
        startup.CancellationCallbackAction = () =>
        {
            ValueTask reentrantDispose = viewModel.DisposeAsync();
            reentrantDisposeCompletedSynchronously =
                reentrantDispose.IsCompletedSuccessfully;
        };
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        await startup.CancellationCallbackActionCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.True(reentrantDisposeCompletedSynchronously);
        }
        finally
        {
            startup.ReleaseCancellationCallback();
            startup.ReleaseInitialization();
            await Task.WhenAll(initialization, disposing)
                .WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task CapturedInitializationContextExpiresAfterInitializationReturns()
    {
        var order = new List<string>();
        var startup = new CapturedContextStartup();
        var remoteWindowService = new BlockingDisposeRemoteWindowService(order);
        var viewModel = new WorkspaceShellViewModel(
            startup,
            remoteWindowService: remoteWindowService);
        startup.DisposeShell = viewModel.DisposeAsync;
        await viewModel.InitializeAsync();

        startup.RunCapturedDispose();
        await remoteWindowService.DisposeEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.False(startup.CapturedDisposeCompleted.Task.IsCompleted);
        }
        finally
        {
            remoteWindowService.ReleaseDispose();
            await startup.CapturedDisposeCompleted.Task
                .WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task SecondInitializationRecoversAfterTransientFailure()
    {
        var startup = new RecoveringStartup();
        await using var viewModel = new WorkspaceShellViewModel(startup);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.IsStartupBlocked);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsIdentityAvailable);
        Assert.False(viewModel.IsStartupBlocked);
        Assert.Equal(2, startup.Attempts);
    }

    [Fact]
    public async Task ActivityWorkspaceInitializesAfterIdentityAndTrust()
    {
        var order = new List<string>();
        var activity = new OrderedActivityService(order);
        var localData = new FakeDesktopLocalDataService(order);
        await using var viewModel = new WorkspaceShellViewModel(
            new OrderedReadyStartup(order),
            trustAuthority: new OrderedReadyTrustAuthority(order),
            activityService: activity,
            localDataService: localData);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["identity-init", "trust-init", "local-data-init", "activity-init"],
            order);
        Assert.True(viewModel.Activities.IsReady);
    }

    [Fact]
    public async Task ActivityWorkspaceRetryKeepsReadyIdentityAndTrustOpen()
    {
        var order = new List<string>();
        var activity = new RecoveringOrderedActivityService(order);
        await using var viewModel = new WorkspaceShellViewModel(
            new OrderedReadyStartup(order),
            trustAuthority: new OrderedReadyTrustAuthority(order),
            activityService: activity);

        await viewModel.InitializeAsync();
        Assert.False(viewModel.Activities.IsReady);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["identity-init", "trust-init", "activity-init", "activity-init"],
            order);
        Assert.True(viewModel.Activities.IsReady);
        Assert.Equal(2, activity.Attempts);
    }

    [Fact]
    public async Task DisposeAsyncCancelsTrustMutationBeforeDisposingDependencies()
    {
        var startup = new TrackingStartup();
        var authority = new BlockingTrustAuthority();
        var viewModel = new WorkspaceShellViewModel(
            startup,
            trustAuthority: authority);
        await viewModel.InitializeAsync();
        viewModel.TrustedDevices.GrantActivityOffer = true;
        Task saving = viewModel.TrustedDevices.SaveCapabilitiesAsync();
        await authority.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();

        await Task.WhenAll(saving, disposing).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(authority.Disposed);
        Assert.False(authority.WasDisposedDuringMutation);
        Assert.True(startup.Disposed);
    }

    [Fact]
    public async Task DisposeStopsRemoteWindowNetworkAndActivityBeforeTrustAndIdentity()
    {
        var order = new List<string>();
        var startup = new OrderedStartup(order);
        var authority = new OrderedTrustAuthority(order);
        var runtime = new DesktopLocalPairingRuntime(
            new OrderedNetworkFactory(order));
        var localData = new FakeDesktopLocalDataService(order);
        var viewModel = new WorkspaceShellViewModel(
            startup,
            trustAuthority: authority,
            localPairingRuntime: runtime,
            activityService: new OrderedActivityService(order),
            localDataService: localData,
            remoteWindowService: new OrderedRemoteWindowService(order));
        await runtime.EnableAsync();

        await viewModel.DisposeAsync();

        Assert.True(order.IndexOf("remote-window") < order.IndexOf("activity"));
        Assert.True(order.IndexOf("network") < order.IndexOf("activity"));
        Assert.True(order.IndexOf("activity") < order.IndexOf("trust"));
        Assert.True(order.IndexOf("local-data") < order.IndexOf("trust"));
        Assert.True(order.IndexOf("trust") < order.IndexOf("identity"));
    }

    [Fact]
    public async Task SemanticResumeProbeFailureClearsRemoteWindowFallbackSelection()
    {
        var activityService = new SemanticResumeProbeActivityService();
        var remoteWindowService = new RecordingRemoteWindowService();
        await using var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false)),
            activityService: activityService,
            remoteWindowService: remoteWindowService,
            remoteWindowPermissionService: new GrantedCapturePermissionService());
        viewModel.Activities.SelectedActivity =
            Assert.Single(viewModel.Activities.Activities);
        viewModel.Activities.SelectedTarget =
            Assert.Single(viewModel.Activities.Targets);
        viewModel.Activities.SelectedRemoteWindowTarget =
            Assert.Single(viewModel.Activities.RemoteWindowTargets);
        Assert.True(viewModel.RemoteWindow.StartRemoteWindowCommand.CanExecute(null));

        activityService.SemanticResumeException = new InvalidOperationException(
            "Injected semantic resume probe failure.");
        activityService.PublishChanged();

        Assert.Equal(
            DesktopSemanticResumeAvailability.Unknown,
            viewModel.Activities.SelectedSemanticResumeAvailability);
        Assert.NotNull(viewModel.Activities.SelectedActivity);
        Assert.NotNull(viewModel.Activities.SelectedTarget);
        Assert.NotNull(viewModel.Activities.SelectedRemoteWindowTarget);
        Assert.False(viewModel.Activities.HandoffCommand.CanExecute(null));
        Assert.False(viewModel.Activities.MoveCommand.CanExecute(null));
        Assert.Equal(
            "REMOTE WINDOW UNAVAILABLE — SEMANTIC SUPPORT UNKNOWN",
            viewModel.RemoteWindow.FallbackStatus);
        Assert.False(viewModel.RemoteWindow.StartRemoteWindowCommand.CanExecute(null));

        await viewModel.RemoteWindow.StartRemoteWindowAsync();

        Assert.Equal(0, remoteWindowService.StartCalls);
    }

    [Fact]
    public async Task RemoteWindowProjectionUsesRoleScopedTargetsAndFailsClosed()
    {
        var activityService = new RoleScopedRemoteWindowActivityService();
        await using var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false)),
            activityService: activityService,
            remoteWindowService: new RecordingRemoteWindowService(),
            remoteWindowPermissionService: new GrantedCapturePermissionService(
                DesktopPermissionState.Granted));
        viewModel.Activities.SelectedActivity =
            Assert.Single(viewModel.Activities.Activities);

        DesktopActivityTargetSnapshot receiveOnlyTarget =
            Assert.Single(viewModel.Activities.Targets);
        Assert.DoesNotContain(
            viewModel.Activities.RemoteWindowTargets,
            target => target.DeviceId == receiveOnlyTarget.DeviceId);
        viewModel.Activities.SelectedRemoteWindowTarget = receiveOnlyTarget;
        Assert.Null(viewModel.Activities.SelectedRemoteWindowTarget);
        viewModel.Activities.SelectedRemoteWindowTarget =
            activityService.DriveWithoutViewTarget;
        Assert.Null(viewModel.Activities.SelectedRemoteWindowTarget);

        viewModel.Activities.SelectedRemoteWindowTarget =
            activityService.MirrorOnlyTarget;

        Assert.Equal(
            activityService.MirrorOnlyTarget,
            viewModel.Activities.SelectedRemoteWindowTarget);
        Assert.True(viewModel.RemoteWindow.IsFallbackStartAvailable);
        Assert.Contains(
            activityService.MirrorOnlyTarget.DisplayName,
            viewModel.RemoteWindow.FallbackDescription);

        viewModel.RemoteWindow.IsRemoteDrivingEnabled = true;

        Assert.Equal(
            MirrorParticipantRole.DriverEligible,
            viewModel.Activities.RemoteWindowTargetRole);
        Assert.Equal(
            activityService.DriverEligibleTarget,
            Assert.Single(viewModel.Activities.RemoteWindowTargets));
        Assert.Null(viewModel.Activities.SelectedRemoteWindowTarget);
        Assert.False(viewModel.RemoteWindow.IsFallbackStartAvailable);

        viewModel.Activities.SelectedRemoteWindowTarget =
            activityService.DriverEligibleTarget;

        Assert.True(viewModel.RemoteWindow.IsFallbackStartAvailable);
        Assert.Contains(
            activityService.DriverEligibleTarget.DisplayName,
            viewModel.RemoteWindow.FallbackDescription);

        activityService.Disconnect();

        Assert.Empty(viewModel.Activities.RemoteWindowTargets);
        Assert.Null(viewModel.Activities.SelectedRemoteWindowTarget);
        Assert.False(viewModel.RemoteWindow.IsFallbackStartAvailable);

        activityService.Reconnect();
        viewModel.Activities.SelectedRemoteWindowTarget =
            Assert.Single(viewModel.Activities.RemoteWindowTargets);
        Assert.True(viewModel.RemoteWindow.IsFallbackStartAvailable);

        activityService.RevokeMirrorGrant();

        Assert.Empty(viewModel.Activities.RemoteWindowTargets);
        Assert.Null(viewModel.Activities.SelectedRemoteWindowTarget);
        Assert.False(viewModel.RemoteWindow.IsFallbackStartAvailable);
    }

    [Fact]
    public async Task BlockedActivityProjectionReturnsQuietlyAfterShellDisposal()
    {
        var activityService = new SemanticResumeProbeActivityService();
        var order = new List<string>();
        var remoteWindowService = new OrderedRemoteWindowService(order);
        var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false)),
            activityService: activityService,
            remoteWindowService: remoteWindowService);
        viewModel.Activities.SelectedActivity =
            Assert.Single(viewModel.Activities.Activities);
        viewModel.Activities.SelectedRemoteWindowTarget =
            Assert.Single(viewModel.Activities.RemoteWindowTargets);
        activityService.BlockSemanticResumeProbe();

        Task publishing = RunOnDedicatedThread(activityService.PublishChanged);
        await activityService.SemanticResumeProbeEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        Task disposing = viewModel.DisposeAsync().AsTask();

        try
        {
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(remoteWindowService.SharingActive);
            Assert.False(publishing.IsCompleted);
        }
        finally
        {
            activityService.ReleaseSemanticResumeProbe();
        }

        await publishing.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisposeClosesProjectionWhileFallbackObserverIsBlocked()
    {
        var activityService = new SemanticResumeProbeActivityService();
        var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false)),
            activityService: activityService,
            remoteWindowService: new RecordingRemoteWindowService());
        viewModel.Activities.SelectedRemoteWindowTarget =
            Assert.Single(viewModel.Activities.RemoteWindowTargets);
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observerRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCallReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.RemoteWindow.PropertyChanged += OnRemoteWindowPropertyChanged;

        Task projecting = RunOnDedicatedThread(() =>
            viewModel.Activities.SelectedActivity =
                Assert.Single(viewModel.Activities.Activities));
        await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task disposing = RunOnDedicatedThread(async () =>
        {
            ValueTask pending = viewModel.DisposeAsync();
            disposeCallReturned.TrySetResult();
            await pending;
        });

        try
        {
            await disposeCallReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            observerRelease.TrySetResult();
        }

        await Task.WhenAll(projecting, disposing)
            .WaitAsync(TimeSpan.FromSeconds(2));

        void OnRemoteWindowPropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(
                    RemoteWindowWorkspaceViewModel.FallbackStatus))
            {
                observerEntered.TrySetResult();
                observerRelease.Task.GetAwaiter().GetResult();
            }
        }
    }

    [Fact]
    public async Task ProjectionObserverDisposeDoesNotWaitOnItsOwnProjection()
    {
        var activityService = new SemanticResumeProbeActivityService();
        var order = new List<string>();
        var remoteWindowService = new BlockingDisposeRemoteWindowService(order);
        var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false)),
            activityService: activityService,
            remoteWindowService: remoteWindowService);
        viewModel.Activities.SelectedRemoteWindowTarget =
            Assert.Single(viewModel.Activities.RemoteWindowTargets);
        bool reentrantDisposeCompleted = false;
        int observerInvocations = 0;
        viewModel.RemoteWindow.PropertyChanged += OnRemoteWindowPropertyChanged;

        Task projecting = RunOnDedicatedThread(() =>
            viewModel.Activities.SelectedActivity =
                Assert.Single(viewModel.Activities.Activities));
        await projecting.WaitAsync(TimeSpan.FromSeconds(2));
        await remoteWindowService.DisposeEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        Task externalDisposal = viewModel.DisposeAsync().AsTask();

        try
        {
            Assert.True(reentrantDisposeCompleted);
            Assert.False(externalDisposal.IsCompleted);
        }
        finally
        {
            remoteWindowService.ReleaseDispose();
            await externalDisposal.WaitAsync(TimeSpan.FromSeconds(2));
        }

        void OnRemoteWindowPropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(
                    RemoteWindowWorkspaceViewModel.FallbackStatus)
                && Interlocked.Exchange(ref observerInvocations, 1) == 0)
            {
                ValueTask reentrantDisposal = viewModel.DisposeAsync();
                reentrantDisposeCompleted = reentrantDisposal.IsCompleted;
                Assert.True(reentrantDisposeCompleted);
                reentrantDisposal.GetAwaiter().GetResult();
            }
        }
    }

    private static WorkspaceShellViewModel CreateReadyViewModel() => new(
        new StubStartup(new LocalIdentitySnapshot(
            "Desk",
            "11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            "Operating-system protected",
            false)));

    private static void AssertSafetyTeardownPrecedesDependencies(
        List<string> order)
    {
        Assert.Equal(4, order.Count);
        Assert.True(order.IndexOf("remote-window") < order.IndexOf("trust"));
        Assert.True(order.IndexOf("network") < order.IndexOf("trust"));
        Assert.True(order.IndexOf("trust") < order.IndexOf("identity"));
    }

    private sealed class StubStartup : IDesktopIdentityStartup
    {
        private readonly Exception? failure;
        private readonly LocalIdentitySnapshot? snapshot;

        public StubStartup(LocalIdentitySnapshot snapshot) => this.snapshot = snapshot;

        public StubStartup(Exception failure) => this.failure = failure;

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null
                ? ValueTask.FromResult(snapshot!)
                : ValueTask.FromException<LocalIdentitySnapshot>(failure);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingStartup : IDesktopIdentityStartup
    {
        public bool Disposed { get; private set; }

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class OrderedReadyStartup(List<string> order) :
        IDesktopIdentityStartup
    {
        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            order.Add("identity-init");
            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose()
        {
        }
    }

    private sealed class OrderedReadyTrustAuthority(List<string> order) :
        IDesktopTrustAuthority
    {
        public ValueTask<DesktopTrustSnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            order.Add("trust-init");
            return ValueTask.FromResult(new DesktopTrustSnapshot(
                SecretStoreProtection.OperatingSystemProtected,
                []));
        }

        public ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DesktopTrustMutationOutcome>(
                new NotSupportedException());

        public ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DesktopTrustMutationOutcome>(
                new NotSupportedException());

        public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
            DeviceId peerDeviceId,
            CapabilityGrant requiredCapabilities,
            IRevocablePeerSession session,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TrustSessionRegistration?>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class OrderedActivityService(List<string> order) :
        IDesktopActivityService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsReady { get; private set; }

        public bool SupportsSemanticResume(string activityKind) => false;

        public DesktopActivitySnapshot CreateWorkspaceNote(
            string title,
            string text,
            ActivitySensitivity sensitivity) => throw new NotSupportedException();

        public ImmutableArray<DesktopActivitySnapshot> GetActivities() => [];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() => [];

        public ValueTask<OperationReceipt> HandoffAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<OperationReceipt>(new NotSupportedException());

        public ValueTask InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            order.Add("activity-init");
            IsReady = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            order.Add("activity");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedRemoteWindowService(List<string> order) :
        IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsAvailable => true;

        public bool SharingActive { get; private set; } = true;

        public TaskCompletionSource SharingStopped { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask DisposeAsync()
        {
            lock (order)
            {
                order.Add("remote-window");
            }

            SharingActive = false;
            SharingStopped.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDisposeRemoteWindowService(List<string> order) :
        IDesktopRemoteWindowService
    {
        private readonly TaskCompletionSource disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public async ValueTask DisposeAsync()
        {
            lock (order)
            {
                order.Add("remote-window");
            }

            DisposeEntered.TrySetResult();
            await disposeRelease.Task.ConfigureAwait(false);
        }

        public void ReleaseDispose() => disposeRelease.TrySetResult();
    }

    private sealed class SynchronouslyBlockingDisposeRemoteWindowService(
        List<string> order) : IDesktopRemoteWindowService
    {
        private readonly ManualResetEventSlim disposeRelease = new(false);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => true;

        public bool DisposeStartedOnThreadPool { get; private set; }

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask DisposeAsync()
        {
            DisposeStartedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            lock (order)
            {
                order.Add("remote-window");
            }

            DisposeEntered.TrySetResult();
            disposeRelease.Wait();
            return ValueTask.CompletedTask;
        }

        public void ReleaseDispose() => disposeRelease.Set();
    }

    private sealed class SemanticResumeProbeActivityService :
        IDesktopActivityService
    {
        private static readonly DesktopActivitySnapshot Activity = new(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Release plan",
            "unsupported.activity/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);

        private static readonly DesktopActivityTargetSnapshot Target = new(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");

        private readonly TaskCompletionSource semanticResumeProbeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int blockSemanticResumeProbe;

        public event Action? Changed;

        public bool IsReady => true;

        public Exception? SemanticResumeException { get; set; }

        public TaskCompletionSource SemanticResumeProbeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void BlockSemanticResumeProbe() =>
            Volatile.Write(ref blockSemanticResumeProbe, 1);

        public bool SupportsSemanticResume(string activityKind)
        {
            if (Volatile.Read(ref blockSemanticResumeProbe) != 0)
            {
                SemanticResumeProbeEntered.TrySetResult();
                semanticResumeProbeRelease.Task.GetAwaiter().GetResult();
            }

            if (SemanticResumeException is not null)
            {
                throw SemanticResumeException;
            }

            return false;
        }

        public DesktopActivitySnapshot CreateWorkspaceNote(
            string title,
            string text,
            ActivitySensitivity sensitivity) => throw new NotSupportedException();

        public ImmutableArray<DesktopActivitySnapshot> GetActivities() => [Activity];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() => [Target];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetRemoteWindowTargets(
            MirrorParticipantRole role) => role switch
            {
                MirrorParticipantRole.ViewOnly => [Target],
                MirrorParticipantRole.DriverEligible => [Target],
                _ => throw new ArgumentOutOfRangeException(nameof(role)),
            };

        public ValueTask<OperationReceipt> HandoffAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<OperationReceipt>(new NotSupportedException());

        public void PublishChanged() => Changed?.Invoke();

        public void ReleaseSemanticResumeProbe() =>
            semanticResumeProbeRelease.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RoleScopedRemoteWindowActivityService :
        IDesktopActivityService
    {
        private static readonly DesktopActivitySnapshot Activity = new(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Release plan",
            "unsupported.activity/v1",
            ActivitySensitivity.Normal,
            ActivityLifecycle.Active);

        private bool connected = true;
        private bool mirrorGranted = true;

        public event Action? Changed;

        public bool IsReady => true;

        public DesktopActivityTargetSnapshot ReceiveOnlyTarget { get; } = new(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Semantic receive-only peer");

        public DesktopActivityTargetSnapshot MirrorOnlyTarget { get; } = new(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Mirror-only peer");

        public DesktopActivityTargetSnapshot DriverEligibleTarget { get; } = new(
            DeviceId.Parse("44444444-4444-4444-4444-444444444444"),
            "Mirror driver peer");

        public DesktopActivityTargetSnapshot DriveWithoutViewTarget { get; } = new(
            DeviceId.Parse("55555555-5555-5555-5555-555555555555"),
            "Drive-without-view peer");

        public bool SupportsSemanticResume(string activityKind) => false;

        public DesktopActivitySnapshot CreateWorkspaceNote(
            string title,
            string text,
            ActivitySensitivity sensitivity) => throw new NotSupportedException();

        public ImmutableArray<DesktopActivitySnapshot> GetActivities() => [Activity];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() =>
            [ReceiveOnlyTarget];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetRemoteWindowTargets(
            MirrorParticipantRole role) => !connected || !mirrorGranted
            ? []
            : role switch
            {
                MirrorParticipantRole.ViewOnly =>
                    [MirrorOnlyTarget, DriverEligibleTarget],
                MirrorParticipantRole.DriverEligible => [DriverEligibleTarget],
                _ => throw new ArgumentOutOfRangeException(nameof(role)),
            };

        public ValueTask<OperationReceipt> HandoffAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<OperationReceipt>(new NotSupportedException());

        public void Disconnect()
        {
            connected = false;
            Changed?.Invoke();
        }

        public void Reconnect()
        {
            connected = true;
            Changed?.Invoke();
        }

        public void RevokeMirrorGrant()
        {
            mirrorGranted = false;
            Changed?.Invoke();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingRemoteWindowService : IDesktopRemoteWindowService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int StartCalls { get; private set; }

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public RemoteWindowEmergencyStopResult EmergencyStop() =>
            throw new NotSupportedException();

        public RemoteWindowSharingSnapshot? GetSnapshot() => null;

        public ValueTask<RemoteWindowCommandResult> StartAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            MirrorParticipantRole role,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return ValueTask.FromException<RemoteWindowCommandResult>(
                new NotSupportedException());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ActiveRemoteWindowService(
        RemoteWindowSessionController controller) : IDesktopRemoteWindowService
    {
        private int emergencyStopCalls;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int EmergencyStopCalls => Volatile.Read(ref emergencyStopCalls);

        public int EmergencyStopThreadId { get; private set; }

        public TaskCompletionSource EmergencyStopCalled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => true;

        public string UnavailableReasonCode => "none";

        public static async Task<ActiveRemoteWindowService> CreateAsync()
        {
            DeviceId hostDeviceId = DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111");
            var clock = new FixedClock(
                new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero));
            var boundaries = new ConfirmingRemoteWindowBoundaries();
            var controller = new RemoteWindowSessionController(
                hostDeviceId,
                ActivityInstance.Active(
                    ActivityDescriptor.Create(
                        ActivityId.Parse(
                            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        ActivityKind.Parse("workspace.note/v1"),
                        hostDeviceId,
                        "Release plan",
                        "{}"),
                    ActivityPlacement.On(hostDeviceId),
                    revision: 1),
                clock,
                DenyMirrorAuthorization.Instance,
                boundaries,
                boundaries,
                boundaries,
                TimeSpan.FromMinutes(1));
            RemoteWindowCommandResult started = await controller.StartAsync(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    clock.UtcNow,
                    "test-probe"));
            Assert.True(started.Succeeded);
            return new ActiveRemoteWindowService(controller);
        }

        public RemoteWindowEmergencyStopResult EmergencyStop()
        {
            EmergencyStopThreadId = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref emergencyStopCalls);
            EmergencyStopCalled.TrySetResult();
            return controller.EmergencyStop();
        }

        public RemoteWindowSharingSnapshot GetSnapshot() => controller.Snapshot;

        public ValueTask DisposeAsync()
        {
            controller.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DenyMirrorAuthorization : IMirrorAuthorizationSource
    {
        public static DenyMirrorAuthorization Instance { get; } = new();

        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            CapabilityGrant.None;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class ConfirmingRemoteWindowBoundaries :
        IRemoteWindowCaptureBoundary,
        IRemoteInputBoundary,
        ILocalSharingSessionBoundary
    {
        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("capture_started"));

        public ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("input_injected"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("boundary_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("boundary_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("boundary_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("boundary_stopped");

        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");
    }

    private sealed class GrantedCapturePermissionService :
        IDesktopRemoteWindowPermissionService
    {
        private readonly DesktopRemoteWindowPermissionSnapshot snapshot;

        public GrantedCapturePermissionService(
            DesktopPermissionState inputPermissionState =
                DesktopPermissionState.NotDetermined) =>
            snapshot = new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Granted,
                inputPermissionState);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => snapshot;

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestCapturePermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);

        public ValueTask<DesktopRemoteWindowPermissionSnapshot>
            RequestInputPermissionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecoveringOrderedActivityService(List<string> order) :
        IDesktopActivityService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int Attempts { get; private set; }

        public bool IsReady { get; private set; }

        public bool SupportsSemanticResume(string activityKind) => false;

        public DesktopActivitySnapshot CreateWorkspaceNote(
            string title,
            string text,
            ActivitySensitivity sensitivity) => throw new NotSupportedException();

        public ImmutableArray<DesktopActivitySnapshot> GetActivities() => [];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() => [];

        public ValueTask<OperationReceipt> HandoffAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<OperationReceipt>(new NotSupportedException());

        public ValueTask InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            order.Add("activity-init");
            if (Attempts == 1)
            {
                return ValueTask.FromException(
                    new IOException("Injected Activity startup failure."));
            }

            IsReady = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingTrustAuthority : IDesktopTrustAuthority
    {
        private readonly DeviceId peerDeviceId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        private bool mutationActive;

        public bool Disposed { get; private set; }

        public TaskCompletionSource MutationStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposedDuringMutation { get; private set; }

        public ValueTask<DesktopTrustSnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DesktopTrustSnapshot(
                SecretStoreProtection.OperatingSystemProtected,
                [new TrustedPeerSnapshot(
                    peerDeviceId,
                    "Peer desk",
                    new string('B', 64),
                    DateTimeOffset.UnixEpoch,
                    CapabilityGrant.None)]));
        }

        public async ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default)
        {
            mutationActive = true;
            MutationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The blocking Trust mutation unexpectedly completed.");
            }
            finally
            {
                mutationActive = false;
            }
        }

        public ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DesktopTrustMutationOutcome>(
                new NotSupportedException());

        public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
            DeviceId peerDeviceId,
            CapabilityGrant requiredCapabilities,
            IRevocablePeerSession session,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TrustSessionRegistration?>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            WasDisposedDuringMutation = mutationActive;
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingStartup : IDesktopIdentityStartup
    {
        private bool initializing;

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public bool WasDisposedWhileInitializing { get; private set; }

        public async ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            initializing = true;
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking startup unexpectedly completed.");
            }
            finally
            {
                initializing = false;
            }
        }

        public void Dispose()
        {
            WasDisposedWhileInitializing = initializing;
            Disposed = true;
        }
    }

    private sealed class CapturedContextStartup : IDesktopIdentityStartup
    {
        private readonly TaskCompletionSource runCapturedDispose = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CapturedDisposeCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ValueTask>? DisposeShell { get; set; }

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            _ = Task.Run(
                async () =>
                {
                    await runCapturedDispose.Task.ConfigureAwait(false);
                    await DisposeShell!().ConfigureAwait(false);
                    CapturedDisposeCompleted.TrySetResult();
                },
                CancellationToken.None);
            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose()
        {
        }

        public void RunCapturedDispose() => runCapturedDispose.TrySetResult();
    }

    private sealed class CancellationIgnoringStartup(List<string> order) :
        IDesktopIdentityStartup
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private bool initializing;

        public bool Disposed { get; private set; }

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposedWhileInitializing { get; private set; }

        public async ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            initializing = true;
            Entered.TrySetResult();
            try
            {
                await release.Task.ConfigureAwait(false);
                return new LocalIdentitySnapshot(
                    "Desk",
                    "11111111-1111-1111-1111-111111111111",
                    new string('A', 64),
                    "Operating-system protected",
                    false);
            }
            finally
            {
                initializing = false;
            }
        }

        public void Dispose()
        {
            WasDisposedWhileInitializing = initializing;
            Disposed = true;
            order.Add("identity");
        }

        public void ReleaseInitialization() => release.TrySetResult();
    }

    private sealed class BlockingCancellationCallbackStartup(List<string> order) :
        IDesktopIdentityStartup
    {
        private readonly TaskCompletionSource cancellationCallbackRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource initializationRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationCallbackEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Action? CancellationCallbackAction { get; set; }

        public TaskCompletionSource CancellationCallbackActionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    CancellationCallbackEntered.TrySetResult();
                    CancellationCallbackAction?.Invoke();
                    CancellationCallbackActionCompleted.TrySetResult();
                    cancellationCallbackRelease.Task.GetAwaiter().GetResult();
                });
            Entered.TrySetResult();
            await initializationRelease.Task.ConfigureAwait(false);
            return new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false);
        }

        public void Dispose()
        {
            Disposed = true;
            order.Add("identity");
        }

        public void ReleaseCancellationCallback() =>
            cancellationCallbackRelease.TrySetResult();

        public void ReleaseInitialization() => initializationRelease.TrySetResult();
    }

    private sealed class OrderedStartup(List<string> order) : IDesktopIdentityStartup
    {
        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<LocalIdentitySnapshot>(new NotSupportedException());

        public void Dispose() => order.Add("identity");
    }

    private sealed class OrderedTrustAuthority(List<string> order) :
        IDesktopTrustAuthority
    {
        public ValueTask<DesktopTrustSnapshot> InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DesktopTrustSnapshot(
                SecretStoreProtection.OperatingSystemProtected,
                []));

        public ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DesktopTrustMutationOutcome>(
                new NotSupportedException());

        public ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DesktopTrustMutationOutcome>(
                new NotSupportedException());

        public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
            DeviceId peerDeviceId,
            CapabilityGrant requiredCapabilities,
            IRevocablePeerSession session,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TrustSessionRegistration?>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            order.Add("trust");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public OrderedNetworkFactory(List<string> order) =>
            Session = new OrderedNetworkSession(order);

        public OrderedNetworkSession Session { get; }

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(
                Session);
    }

    private sealed class OrderedNetworkSession(List<string> order) :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public bool DisposeStartedOnThreadPool { get; private set; }

        public TaskCompletionSource Stopped { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeStartedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            lock (order)
            {
                order.Add("network");
            }

            Stopped.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDisposeNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public BlockingDisposeNetworkFactory(List<string> order) =>
            Session = new BlockingDisposeNetworkSession(order);

        public BlockingDisposeNetworkSession Session { get; }

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class BlockingDisposeNetworkSession(List<string> order) :
        IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public async ValueTask DisposeAsync()
        {
            lock (order)
            {
                order.Add("network");
            }

            DisposeEntered.TrySetResult();
            await disposeRelease.Task.ConfigureAwait(false);
        }

        public void ReleaseDispose() => disposeRelease.TrySetResult();
    }

    private sealed class RecoveringStartup : IDesktopIdentityStartup
    {
        public int Attempts { get; private set; }

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            if (Attempts == 1)
            {
                return ValueTask.FromException<LocalIdentitySnapshot>(
                    new IOException("Transient credential-store failure."));
            }

            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose()
        {
        }
    }
}
