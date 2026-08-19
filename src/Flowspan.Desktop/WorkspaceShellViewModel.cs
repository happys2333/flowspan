using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop;

public sealed class WorkspaceShellViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource initializationDrainCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource remoteWindowProjectionsDrained = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly AsyncLocal<LifetimeCallbackScope?> lifetimeCallbackScope = new();
    private readonly AsyncLocal<RemoteWindowProjectionCallbackScope?>
        remoteWindowProjectionCallbackScope = new();
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly Lock lifecycleGate = new();
    private readonly Lock remoteWindowProjectionGate = new();
    private readonly bool localPairingAvailable;
    private readonly IDesktopIdentityStartup startup;
    private readonly RelayCommand toggleIdentityDetailsCommand;
    private readonly AsyncRelayCommand retryIdentityCommand;
    private string deviceId = DesktopText.Get("Shell_Pending");
    private string deviceName = DesktopText.Get("Shell_LocalDevice");
    private string fingerprint = DesktopText.Get("Shell_Pending");
    private string identityDetailsActionLabel = DesktopText.Get(
        "Shell_ShowIdentityDetails");
    private string identityProtection = DesktopText.Get("Shell_Pending");
    private bool isIdentityAvailable;
    private bool isIdentityDetailsVisible;
    private bool isInitializing = true;
    private bool isStartupBlocked;
    private bool isTestMode;
    private bool remoteWindowProjectionClosed;
    private string recoveryAction = string.Empty;
    private string startupDescription = DesktopText.Get(
        "Shell_InitializingDescription");
    private string startupStatus = DesktopText.Get("Shell_InitializingStatus");
    private int activeInitializations;
    private int activeRemoteWindowProjections;
    private Exception? disposalFailure;
    private bool disposed;
    private bool resourcesDisposalStarted;

    public WorkspaceShellViewModel(
        IDesktopIdentityStartup startup,
        DesktopPairingDecisionSource? pairingDecisions = null,
        IDesktopUiDispatcher? dispatcher = null,
        IDesktopTrustAuthority? trustAuthority = null,
        DesktopLocalPairingRuntime? localPairingRuntime = null,
        DesktopLocalNetworkPermissionGuide? localNetworkPermissionGuide = null,
        IDesktopActivityService? activityService = null,
        IDesktopSceneApplyService? sceneApplyService = null,
        IDesktopSceneRepositoryService? sceneRepositoryService = null,
        IDesktopLocalDataService? localDataService = null,
        IDesktopRemoteWindowService? remoteWindowService = null,
        IDesktopRemoteWindowPermissionService? remoteWindowPermissionService = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        this.startup = startup;
        localPairingAvailable = localPairingRuntime is not null;
        IDesktopUiDispatcher effectiveDispatcher =
            dispatcher ?? InlineDesktopUiDispatcher.Instance;
        Pairing = new PairingPromptViewModel(
            pairingDecisions ?? new DesktopPairingDecisionSource(),
            effectiveDispatcher);
        IDesktopLocalDataService effectiveLocalDataService =
            localDataService ?? UnavailableDesktopLocalDataService.Instance;
        TrustedDevices = new TrustedDevicesViewModel(
            trustAuthority ?? new DesktopTrustAuthority(new InMemoryTrustStore()),
            localPairingRuntime is null
                ? null
                : token => localPairingRuntime
                    .RefreshTrustedPeersAsync(token)
                    .AsTask(),
            effectiveLocalDataService);
        LocalPairing = new LocalPairingViewModel(
            localPairingRuntime ?? new DesktopLocalPairingRuntime(
                UnavailableLocalPairingNetworkFactory.Instance),
            effectiveDispatcher,
            TrustedDevices.InitializeAsync,
            localNetworkPermissionGuide);
        IDesktopActivityService effectiveActivityService =
            activityService ?? UnavailableDesktopActivityService.Instance;
        RemoteWindow = new RemoteWindowWorkspaceViewModel(
            remoteWindowService ?? UnavailableDesktopRemoteWindowService.Instance,
            effectiveDispatcher,
            remoteWindowPermissionService);
        Activities = new ActivityWorkspaceViewModel(
            effectiveActivityService,
            effectiveDispatcher);
        Activities.RemoteWindowTargetRole = GetRemoteWindowTargetRole();
        Activities.PropertyChanged += OnActivityWorkspacePropertyChanged;
        RemoteWindow.PropertyChanged += OnRemoteWindowPropertyChanged;
        UpdateRemoteWindowFallbackSelection();
        Scenes = new SceneApplyViewModel(
            sceneApplyService
                ?? effectiveActivityService as IDesktopSceneApplyService
                ?? UnavailableDesktopSceneApplyService.Instance);
        SceneRepository = new SceneRepositoryViewModel(
            sceneRepositoryService
                ?? UnavailableDesktopSceneRepositoryService.Instance,
            Scenes.SelectScene);
        LocalData = new LocalDataViewModel(effectiveLocalDataService);
        toggleIdentityDetailsCommand = new RelayCommand(
            ToggleIdentityDetails,
            () => IsIdentityAvailable);
        retryIdentityCommand = new AsyncRelayCommand(
            () => InitializeAsync(lifetimeCancellation.Token),
            () => IsStartupBlocked && !IsInitializing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PairingPromptViewModel Pairing { get; }

    public ActivityWorkspaceViewModel Activities { get; }

    public LocalPairingViewModel LocalPairing { get; }

    public LocalDataViewModel LocalData { get; }

    public RemoteWindowWorkspaceViewModel RemoteWindow { get; }

    public SceneApplyViewModel Scenes { get; }

    public SceneRepositoryViewModel SceneRepository { get; }

    public TrustedDevicesViewModel TrustedDevices { get; }

    public string DeviceId
    {
        get => deviceId;
        private set => SetProperty(ref deviceId, value);
    }

    public string DeviceName
    {
        get => deviceName;
        private set => SetProperty(ref deviceName, value);
    }

    public string Fingerprint
    {
        get => fingerprint;
        private set => SetProperty(ref fingerprint, value);
    }

    public string IdentityDetailsActionLabel
    {
        get => identityDetailsActionLabel;
        private set => SetProperty(ref identityDetailsActionLabel, value);
    }

    public string IdentityProtection
    {
        get => identityProtection;
        private set => SetProperty(ref identityProtection, value);
    }

    public bool IsEmergencyStopAvailable => RemoteWindow.IsEmergencyStopAvailable;

    public bool IsIdentityAvailable
    {
        get => isIdentityAvailable;
        private set
        {
            if (SetProperty(ref isIdentityAvailable, value))
            {
                toggleIdentityDetailsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsIdentityDetailsVisible
    {
        get => isIdentityDetailsVisible;
        private set => SetProperty(ref isIdentityDetailsVisible, value);
    }

    public bool IsInitializing
    {
        get => isInitializing;
        private set
        {
            if (SetProperty(ref isInitializing, value))
            {
                retryIdentityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsStartupBlocked
    {
        get => isStartupBlocked;
        private set
        {
            if (SetProperty(ref isStartupBlocked, value))
            {
                retryIdentityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTestMode
    {
        get => isTestMode;
        private set => SetProperty(ref isTestMode, value);
    }

    public string RecoveryAction
    {
        get => recoveryAction;
        private set => SetProperty(ref recoveryAction, value);
    }

    public ICommand RetryIdentityCommand => retryIdentityCommand;

    public string StartupDescription
    {
        get => startupDescription;
        private set => SetProperty(ref startupDescription, value);
    }

    public string StartupStatus
    {
        get => startupStatus;
        private set => SetProperty(ref startupStatus, value);
    }

    public ICommand ToggleIdentityDetailsCommand => toggleIdentityDetailsCommand;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        BeginInitialization();
        using LifetimeCallbackScopeLease callbackScope =
            EnterLifetimeCallbackScope();
        try
        {
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token);
            bool enteredInitializationGate = false;
            try
            {
                await initializationGate
                    .WaitAsync(linkedCancellation.Token)
                    .ConfigureAwait(true);
                enteredInitializationGate = true;
                if (IsIdentityAvailable
                    && !IsStartupBlocked
                    && TrustedDevices.IsTrustAvailable
                    && Activities.IsReady)
                {
                    return;
                }

                IsInitializing = true;
                if (!IsIdentityAvailable || IsStartupBlocked)
                {
                    IsStartupBlocked = false;
                    StartupStatus = DesktopText.Get("Shell_InitializingStatus");
                    StartupDescription = DesktopText.Get(
                        "Shell_InitializingDescription");
                    RecoveryAction = string.Empty;
                }

                try
                {
                    if (!IsIdentityAvailable || IsStartupBlocked)
                    {
                        LocalIdentitySnapshot snapshot = await startup
                            .InitializeAsync(linkedCancellation.Token)
                            .ConfigureAwait(true);
                        ApplySnapshot(snapshot);
                    }

                    if (IsIdentityAvailable)
                    {
                        if (!TrustedDevices.IsTrustAvailable)
                        {
                            await TrustedDevices
                                .InitializeAsync(linkedCancellation.Token)
                                .ConfigureAwait(true);
                        }

                        if (!LocalData.IsHistoryAvailable)
                        {
                            await LocalData
                                .InitializeAsync(linkedCancellation.Token)
                                .ConfigureAwait(true);
                        }

                        if (TrustedDevices.IsTrustAvailable && !Activities.IsReady)
                        {
                            await Activities
                                .InitializeAsync(linkedCancellation.Token)
                                .ConfigureAwait(true);
                        }

                        await SceneRepository
                            .InitializeAsync(linkedCancellation.Token)
                            .ConfigureAwait(true);
                        LocalPairing.SetPrerequisitesAvailable(
                            localPairingAvailable
                            && TrustedDevices.IsTrustAvailable);
                    }
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException)
                {
                    ApplyFailure(DesktopIdentityStartup.DescribeFailure(exception));
                }
                finally
                {
                    IsInitializing = false;
                }
            }
            catch (OperationCanceledException)
                when (linkedCancellation.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                if (enteredInitializationGate)
                {
                    initializationGate.Release();
                }
            }
        }
        finally
        {
            EndInitialization();
        }
    }

    public async ValueTask DisposeAsync()
    {
        bool isLifecycleCallback = IsLifetimeCallbackActive;
        bool isRemoteWindowProjectionCallback =
            IsRemoteWindowProjectionCallbackActive;
        lock (remoteWindowProjectionGate)
        {
            remoteWindowProjectionClosed = true;
            if (activeRemoteWindowProjections == 0)
            {
                remoteWindowProjectionsDrained.TrySetResult();
            }
        }

        bool startResourceDisposal;
        lock (lifecycleGate)
        {
            if (!disposed)
            {
                disposed = true;
                if (activeInitializations == 0)
                {
                    initializationDrainCompleted.TrySetResult();
                }
            }

            startResourceDisposal = TryStartResourceDisposal();
        }

        if (startResourceDisposal)
        {
            _ = DisposeResourcesAsync();
        }

        if (isLifecycleCallback || isRemoteWindowProjectionCallback)
        {
            return;
        }

        await disposalCompleted.Task.ConfigureAwait(false);
        if (disposalFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(disposalFailure)
                .Throw();
        }
    }

    private void BeginInitialization()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeInitializations++;
        }
    }

    private async Task DisposeResourcesAsync()
    {
        var failures = new List<Exception>();
        RemoteWindow.FailCloseForOwnerDisposal();
        try
        {
            Activities.PropertyChanged -= OnActivityWorkspacePropertyChanged;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            RemoteWindow.PropertyChanged -= OnRemoteWindowPropertyChanged;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        Task lifetimeCancellationTask = BeginCancellation(
            lifetimeCancellation,
            failures);
        Task remoteWindowDisposal = BeginDisposal(
            RemoteWindow.DisposeAsync,
            failures);
        Task localPairingDisposal = BeginDisposal(
            LocalPairing.DisposeAsync,
            failures);
        await CaptureFailureAsync(remoteWindowDisposal, failures)
            .ConfigureAwait(false);
        await CaptureFailureAsync(localPairingDisposal, failures)
            .ConfigureAwait(false);
        await remoteWindowProjectionsDrained.Task.ConfigureAwait(false);
        await CaptureFailureAsync(lifetimeCancellationTask, failures)
            .ConfigureAwait(false);

        await initializationDrainCompleted.Task.ConfigureAwait(false);

        try
        {
            Scenes.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await SceneRepository.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await Activities.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await LocalData.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            Pairing.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await TrustedDevices.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            startup.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            lifetimeCancellation.Dispose();
            initializationGate.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        disposalFailure = failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                "One or more desktop resources failed to close.",
                failures),
        };
        disposalCompleted.SetResult();
    }

    private static Task BeginCancellation(
        CancellationTokenSource source,
        List<Exception> failures)
    {
        try
        {
            return source.CancelAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return Task.CompletedTask;
        }
    }

    private static Task BeginDisposal(
        Func<ValueTask> dispose,
        List<Exception> failures)
    {
        try
        {
            return Task.Run(async () =>
                await dispose().ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return Task.CompletedTask;
        }
    }

    private static async Task CaptureFailureAsync(
        Task operation,
        List<Exception> failures)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void OnActivityWorkspacePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is null
            or nameof(ActivityWorkspaceViewModel.SelectedActivity)
            or nameof(ActivityWorkspaceViewModel.SelectedRemoteWindowTarget)
            or nameof(ActivityWorkspaceViewModel.RemoteWindowTargetRole)
            or nameof(ActivityWorkspaceViewModel.SelectedSemanticResumeAvailability))
        {
            UpdateRemoteWindowFallbackSelection();
        }
    }

    private void OnRemoteWindowPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not null
            and not nameof(RemoteWindowWorkspaceViewModel.IsRemoteDrivingEnabled))
        {
            return;
        }

        if (!TryAcquireRemoteWindowProjection())
        {
            return;
        }

        try
        {
            using RemoteWindowProjectionCallbackScopeLease callbackScope =
                EnterRemoteWindowProjectionCallbackScope();
            Activities.RemoteWindowTargetRole = GetRemoteWindowTargetRole();
        }
        catch (ObjectDisposedException) when (IsRemoteWindowProjectionClosed())
        {
        }
        finally
        {
            ReleaseRemoteWindowProjection();
        }
    }

    private MirrorParticipantRole GetRemoteWindowTargetRole() =>
        RemoteWindow.IsRemoteDrivingEnabled
            ? MirrorParticipantRole.DriverEligible
            : MirrorParticipantRole.ViewOnly;

    private void UpdateRemoteWindowFallbackSelection()
    {
        DesktopActivitySnapshot? activity = Activities.SelectedActivity;
        DesktopActivityTargetSnapshot? target =
            Activities.SelectedRemoteWindowTarget;
        DesktopSemanticResumeAvailability semanticResumeAvailability =
            Activities.SelectedSemanticResumeAvailability;
        if (!TryAcquireRemoteWindowProjection())
        {
            return;
        }

        try
        {
            using RemoteWindowProjectionCallbackScopeLease callbackScope =
                EnterRemoteWindowProjectionCallbackScope();
            RemoteWindow.SetFallbackSelection(
                activity,
                target,
                semanticResumeAvailability);
        }
        catch (ObjectDisposedException) when (IsRemoteWindowProjectionClosed())
        {
        }
        finally
        {
            ReleaseRemoteWindowProjection();
        }
    }

    private bool TryAcquireRemoteWindowProjection()
    {
        lock (remoteWindowProjectionGate)
        {
            if (remoteWindowProjectionClosed)
            {
                return false;
            }

            activeRemoteWindowProjections++;
            return true;
        }
    }

    private bool IsRemoteWindowProjectionClosed()
    {
        lock (remoteWindowProjectionGate)
        {
            return remoteWindowProjectionClosed;
        }
    }

    private void ReleaseRemoteWindowProjection()
    {
        lock (remoteWindowProjectionGate)
        {
            activeRemoteWindowProjections--;
            if (remoteWindowProjectionClosed
                && activeRemoteWindowProjections == 0)
            {
                remoteWindowProjectionsDrained.TrySetResult();
            }
        }
    }

    private bool TryStartResourceDisposal()
    {
        if (!disposed || resourcesDisposalStarted)
        {
            return false;
        }

        resourcesDisposalStarted = true;
        return true;
    }

    private void EndInitialization()
    {
        lock (lifecycleGate)
        {
            activeInitializations--;
            if (disposed && activeInitializations == 0)
            {
                initializationDrainCompleted.TrySetResult();
            }
        }
    }

    private void ApplySnapshot(LocalIdentitySnapshot snapshot)
    {
        DeviceName = snapshot.DisplayName;
        DeviceId = snapshot.DeviceId;
        Fingerprint = snapshot.Fingerprint;
        IdentityProtection = snapshot.ProtectionLabel;
        IsIdentityAvailable = true;
        IsStartupBlocked = false;
        IsTestMode = snapshot.IsTestMode;
        StartupStatus = snapshot.IsTestMode
            ? DesktopText.Get("Shell_ReadyTestModeStatus")
            : DesktopText.Get("Shell_ReadyStatus");
        StartupDescription = snapshot.IsTestMode
            ? DesktopText.Get("Shell_ReadyTestModeDescription")
            : DesktopText.Get("Shell_ReadyDescription");
    }

    private void ApplyFailure(DesktopStartupFailure failure)
    {
        IsIdentityAvailable = false;
        IsIdentityDetailsVisible = false;
        IsStartupBlocked = true;
        IsTestMode = false;
        LocalPairing.SetPrerequisitesAvailable(false);
        DeviceId = DesktopText.Get("Shell_Unavailable");
        Fingerprint = DesktopText.Get("Shell_Unavailable");
        IdentityProtection = failure.ReasonCode;
        StartupStatus = DesktopText.Get("Shell_UnavailableStatus");
        StartupDescription = failure.Summary;
        RecoveryAction = failure.RecoveryAction;
        IdentityDetailsActionLabel = DesktopText.Get(
            "Shell_ShowIdentityDetails");
    }

    private void ToggleIdentityDetails()
    {
        IsIdentityDetailsVisible = !IsIdentityDetailsVisible;
        IdentityDetailsActionLabel = IsIdentityDetailsVisible
            ? DesktopText.Get("Shell_HideIdentityDetails")
            : DesktopText.Get("Shell_ShowIdentityDetails");
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private bool IsLifetimeCallbackActive =>
        lifetimeCallbackScope.Value?.IsActive == true;

    private bool IsRemoteWindowProjectionCallbackActive =>
        remoteWindowProjectionCallbackScope.Value is
        { IsActive: true, Owner: var owner }
        && ReferenceEquals(owner, this);

    private LifetimeCallbackScopeLease EnterLifetimeCallbackScope() =>
        new(lifetimeCallbackScope);

    private RemoteWindowProjectionCallbackScopeLease
        EnterRemoteWindowProjectionCallbackScope() =>
        new(this, remoteWindowProjectionCallbackScope);

    private sealed class LifetimeCallbackScope
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }

    private sealed class LifetimeCallbackScopeLease : IDisposable
    {
        private readonly LifetimeCallbackScope current = new();
        private readonly AsyncLocal<LifetimeCallbackScope?> owner;
        private readonly LifetimeCallbackScope? previous;
        private int disposed;

        public LifetimeCallbackScopeLease(
            AsyncLocal<LifetimeCallbackScope?> owner)
        {
            this.owner = owner;
            previous = owner.Value;
            owner.Value = current;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            current.Deactivate();
            owner.Value = previous;
        }
    }

    private sealed class RemoteWindowProjectionCallbackScope(
        WorkspaceShellViewModel owner)
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public WorkspaceShellViewModel Owner { get; } = owner;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }

    private sealed class RemoteWindowProjectionCallbackScopeLease : IDisposable
    {
        private readonly RemoteWindowProjectionCallbackScope current;
        private readonly AsyncLocal<RemoteWindowProjectionCallbackScope?> owner;
        private readonly RemoteWindowProjectionCallbackScope? previous;
        private int disposed;

        public RemoteWindowProjectionCallbackScopeLease(
            WorkspaceShellViewModel shell,
            AsyncLocal<RemoteWindowProjectionCallbackScope?> owner)
        {
            this.owner = owner;
            previous = owner.Value;
            current = new RemoteWindowProjectionCallbackScope(shell);
            owner.Value = current;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            current.Deactivate();
            owner.Value = previous;
        }
    }

    private sealed class UnavailableLocalPairingNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public static UnavailableLocalPairingNetworkFactory Instance { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IDesktopLocalPairingNetworkSession>(
                new PlatformNotSupportedException(
                    "A production local-pairing runtime was not configured."));
    }
}
