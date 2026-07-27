using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Security;

namespace Flowspan.Desktop;

public sealed class WorkspaceShellViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly Lock lifecycleGate = new();
    private readonly bool localPairingAvailable;
    private readonly IDesktopIdentityStartup startup;
    private readonly RelayCommand toggleIdentityDetailsCommand;
    private readonly AsyncRelayCommand retryIdentityCommand;
    private string deviceId = "Pending";
    private string deviceName = "Local device";
    private string fingerprint = "Pending";
    private string identityDetailsActionLabel = "Show identity details";
    private string identityProtection = "Pending";
    private bool isIdentityAvailable;
    private bool isIdentityDetailsVisible;
    private bool isInitializing = true;
    private bool isStartupBlocked;
    private bool isTestMode;
    private string recoveryAction = string.Empty;
    private string startupDescription =
        "Flowspan is opening without requesting capture or input access.";
    private string startupStatus = "INITIALIZING IDENTITY";
    private int activeInitializations;
    private Exception? disposalFailure;
    private Exception? disposalInitiationFailure;
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
        IDesktopSceneRepositoryService? sceneRepositoryService = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        this.startup = startup;
        localPairingAvailable = localPairingRuntime is not null;
        IDesktopUiDispatcher effectiveDispatcher =
            dispatcher ?? InlineDesktopUiDispatcher.Instance;
        Pairing = new PairingPromptViewModel(
            pairingDecisions ?? new DesktopPairingDecisionSource(),
            effectiveDispatcher);
        TrustedDevices = new TrustedDevicesViewModel(
            trustAuthority ?? new DesktopTrustAuthority(new InMemoryTrustStore()),
            localPairingRuntime is null
                ? null
                : token => localPairingRuntime
                    .RefreshTrustedPeersAsync(token)
                    .AsTask());
        LocalPairing = new LocalPairingViewModel(
            localPairingRuntime ?? new DesktopLocalPairingRuntime(
                UnavailableLocalPairingNetworkFactory.Instance),
            effectiveDispatcher,
            TrustedDevices.InitializeAsync,
            localNetworkPermissionGuide);
        IDesktopActivityService effectiveActivityService =
            activityService ?? UnavailableDesktopActivityService.Instance;
        Activities = new ActivityWorkspaceViewModel(
            effectiveActivityService,
            effectiveDispatcher);
        Scenes = new SceneApplyViewModel(
            sceneApplyService
                ?? effectiveActivityService as IDesktopSceneApplyService
                ?? UnavailableDesktopSceneApplyService.Instance);
        SceneRepository = new SceneRepositoryViewModel(
            sceneRepositoryService
                ?? UnavailableDesktopSceneRepositoryService.Instance,
            Scenes.SelectScene);
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

    public bool IsEmergencyStopAvailable { get; }

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
                    StartupStatus = "INITIALIZING IDENTITY";
                    StartupDescription =
                        "Flowspan is opening without requesting capture or input access.";
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
        bool startResourceDisposal;
        lock (lifecycleGate)
        {
            if (!disposed)
            {
                disposed = true;
                try
                {
                    lifetimeCancellation.Cancel();
                }
                catch (Exception exception)
                {
                    disposalInitiationFailure = exception;
                }
            }

            startResourceDisposal = TryStartResourceDisposal();
        }

        if (startResourceDisposal)
        {
            _ = DisposeResourcesAsync();
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
        if (disposalInitiationFailure is not null)
        {
            failures.Add(disposalInitiationFailure);
        }

        try
        {
            await LocalPairing.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

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

    private bool TryStartResourceDisposal()
    {
        if (!disposed || activeInitializations != 0 || resourcesDisposalStarted)
        {
            return false;
        }

        resourcesDisposalStarted = true;
        return true;
    }

    private void EndInitialization()
    {
        bool startResourceDisposal;
        lock (lifecycleGate)
        {
            activeInitializations--;
            startResourceDisposal = TryStartResourceDisposal();
        }

        if (startResourceDisposal)
        {
            _ = DisposeResourcesAsync();
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
            ? "LOCAL WORKSPACE READY — TEST MODE"
            : "LOCAL WORKSPACE READY";
        StartupDescription = snapshot.IsTestMode
            ? "The validation identity exists only for this process and is not trusted."
            : "The local identity is ready. Pairing and sharing remain inactive.";
    }

    private void ApplyFailure(DesktopStartupFailure failure)
    {
        IsIdentityAvailable = false;
        IsIdentityDetailsVisible = false;
        IsStartupBlocked = true;
        IsTestMode = false;
        LocalPairing.SetPrerequisitesAvailable(false);
        DeviceId = "Unavailable";
        Fingerprint = "Unavailable";
        IdentityProtection = failure.ReasonCode;
        StartupStatus = "IDENTITY UNAVAILABLE";
        StartupDescription = failure.Summary;
        RecoveryAction = failure.RecoveryAction;
        IdentityDetailsActionLabel = "Show identity details";
    }

    private void ToggleIdentityDetails()
    {
        IsIdentityDetailsVisible = !IsIdentityDetailsVisible;
        IdentityDetailsActionLabel = IsIdentityDetailsVisible
            ? "Hide identity details"
            : "Show identity details";
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
