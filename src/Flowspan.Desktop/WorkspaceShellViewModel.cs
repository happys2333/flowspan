using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Flowspan.Desktop;

public sealed class WorkspaceShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly Lock lifecycleGate = new();
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
    private bool disposed;
    private bool resourcesDisposed;

    public WorkspaceShellViewModel(IDesktopIdentityStartup startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        this.startup = startup;
        toggleIdentityDetailsCommand = new RelayCommand(
            ToggleIdentityDetails,
            () => IsIdentityAvailable);
        retryIdentityCommand = new AsyncRelayCommand(
            () => InitializeAsync(lifetimeCancellation.Token),
            () => IsStartupBlocked && !IsInitializing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
                if (IsIdentityAvailable && !IsStartupBlocked)
                {
                    return;
                }

                IsInitializing = true;
                IsStartupBlocked = false;
                StartupStatus = "INITIALIZING IDENTITY";
                StartupDescription =
                    "Flowspan is opening without requesting capture or input access.";
                RecoveryAction = string.Empty;

                try
                {
                    LocalIdentitySnapshot snapshot = await startup
                        .InitializeAsync(linkedCancellation.Token)
                        .ConfigureAwait(true);
                    ApplySnapshot(snapshot);
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

    public void Dispose()
    {
        bool disposeResources;
        lock (lifecycleGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetimeCancellation.Cancel();
            disposeResources = activeInitializations == 0;
        }

        if (disposeResources)
        {
            DisposeResources();
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

    private void DisposeResources()
    {
        lock (lifecycleGate)
        {
            if (resourcesDisposed)
            {
                return;
            }

            resourcesDisposed = true;
        }

        startup.Dispose();
        lifetimeCancellation.Dispose();
        initializationGate.Dispose();
    }

    private void EndInitialization()
    {
        bool disposeResources;
        lock (lifecycleGate)
        {
            activeInitializations--;
            disposeResources = disposed && activeInitializations == 0;
        }

        if (disposeResources)
        {
            DisposeResources();
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
}
