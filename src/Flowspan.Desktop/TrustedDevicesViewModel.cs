using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop;

public sealed class TrustedDeviceItemViewModel
{
    internal TrustedDeviceItemViewModel(TrustedPeerSnapshot snapshot)
    {
        Snapshot = snapshot;
        DeviceId = snapshot.DeviceId.ToString();
        DisplayName = snapshot.DisplayName;
        Fingerprint = snapshot.Fingerprint;
        VerifiedAt = snapshot.VerifiedAt.ToString("u");
        CapabilitySummary = snapshot.GrantedCapabilities.Capabilities.Count == 0
            ? DesktopText.Get("TrustedDevices_NoCapabilities")
            : string.Join(
                DesktopText.Get("TrustedDevices_CapabilitySeparator"),
                snapshot.GrantedCapabilities.Capabilities
                    .Order()
                    .Select(FormatCapability));
    }

    internal TrustedPeerSnapshot Snapshot { get; }

    public string DeviceId { get; }

    public string CapabilitySummary { get; }

    public string DisplayName { get; }

    public string Fingerprint { get; }

    public string VerifiedAt { get; }

    private static string FormatCapability(Capability capability) => capability switch
    {
        Capability.ActivityOffer => "activity.offer",
        Capability.ActivityReceive => "activity.receive",
        Capability.ActivityReplace => "activity.replace",
        Capability.ActivitySwap => "activity.swap",
        Capability.MirrorView => "mirror.view",
        Capability.MirrorDrive => "mirror.drive",
        Capability.FileReceive => "file.receive",
        Capability.SceneApply => "scene.apply",
        _ => throw new ArgumentOutOfRangeException(
            nameof(capability),
            capability,
            "The Capability does not have a desktop label."),
    };
}

public sealed class TrustedDevicesViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IDesktopTrustAuthority authority;
    private readonly Func<CancellationToken, Task> reconcileConnections;
    private readonly RelayCommand beginRevokeCommand;
    private readonly RelayCommand cancelRevokeCommand;
    private readonly AsyncRelayCommand confirmRevokeCommand;
    private readonly AsyncRelayCommand exportTrustCommand;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IDesktopLocalDataService? localDataService;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly AsyncRelayCommand saveCapabilitiesCommand;
    private TrustedDeviceItemViewModel? selectedDevice;
    private bool grantActivityOffer;
    private bool grantActivityReceive;
    private bool grantActivityReplace;
    private bool grantActivitySwap;
    private bool grantFileReceive;
    private bool grantMirrorDrive;
    private bool grantMirrorView;
    private bool grantSceneApply;
    private bool isEmpty = true;
    private bool isMutationInProgress;
    private bool isRevokeConfirmationVisible;
    private bool isTrustAvailable;
    private string mutationDescription = string.Empty;
    private string mutationStatus = string.Empty;
    private string trustExportPath = DesktopText.Get(
        "TrustedDevices_NoExportPath");
    private string trustExportPreview = DesktopText.Get(
        "TrustedDevices_ExportPrivacyDescription");
    private string protection = DesktopText.Get(
        "TrustedDevices_StoreNotLoaded");
    private string recoveryAction = string.Empty;
    private string revokeConfirmation = string.Empty;
    private string status = DesktopText.Get("TrustedDevices_LoadingStatus");
    private string statusDescription = DesktopText.Get(
        "TrustedDevices_LoadingDescription");
    private int disposed;

    public TrustedDevicesViewModel(
        IDesktopTrustAuthority authority,
        Func<CancellationToken, Task>? reconcileConnections = null,
        IDesktopLocalDataService? localDataService = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        this.authority = authority;
        this.localDataService = localDataService;
        this.reconcileConnections = reconcileConnections
            ?? (_ => Task.CompletedTask);
        beginRevokeCommand = new RelayCommand(BeginRevoke, CanBeginRevoke);
        cancelRevokeCommand = new RelayCommand(
            CancelRevoke,
            () => IsRevokeConfirmationVisible && !IsMutationInProgress);
        confirmRevokeCommand = new AsyncRelayCommand(
            () => ConfirmRevokeAsync(),
            () => IsRevokeConfirmationVisible
                && HasSelection
                && !IsMutationInProgress);
        saveCapabilitiesCommand = new AsyncRelayCommand(
            () => SaveCapabilitiesAsync(),
            () => IsTrustAvailable
                && HasSelection
                && HasUnsavedChanges
                && !IsMutationInProgress);
        exportTrustCommand = new AsyncRelayCommand(
            () => ExportTrustAsync(),
            () => IsTrustAvailable
                && this.localDataService is not null
                && !IsMutationInProgress);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TrustedDeviceItemViewModel> Devices { get; } = [];

    public ICommand BeginRevokeCommand => beginRevokeCommand;

    public ICommand CancelRevokeCommand => cancelRevokeCommand;

    public ICommand ConfirmRevokeCommand => confirmRevokeCommand;

    public ICommand ExportTrustCommand => exportTrustCommand;

    public bool GrantActivityOffer
    {
        get => grantActivityOffer;
        set => SetGrantProperty(ref grantActivityOffer, value);
    }

    public bool GrantActivityReceive
    {
        get => grantActivityReceive;
        set => SetGrantProperty(ref grantActivityReceive, value);
    }

    public bool GrantActivityReplace
    {
        get => grantActivityReplace;
        set => SetGrantProperty(ref grantActivityReplace, value);
    }

    public bool GrantActivitySwap
    {
        get => grantActivitySwap;
        set => SetGrantProperty(ref grantActivitySwap, value);
    }

    public bool GrantFileReceive
    {
        get => grantFileReceive;
        set => SetGrantProperty(ref grantFileReceive, value);
    }

    public bool GrantMirrorDrive
    {
        get => grantMirrorDrive;
        set => SetGrantProperty(ref grantMirrorDrive, value);
    }

    public bool GrantMirrorView
    {
        get => grantMirrorView;
        set => SetGrantProperty(ref grantMirrorView, value);
    }

    public bool GrantSceneApply
    {
        get => grantSceneApply;
        set => SetGrantProperty(ref grantSceneApply, value);
    }

    public bool HasUnsavedChanges => SelectedDevice is not null
        && !SelectedDevice.Snapshot.GrantedCapabilities.Capabilities.SetEquals(
            CreateDraftGrant().Capabilities);

    public bool HasSelection => SelectedDevice is not null;

    public bool CanEditSelectedPeer => IsTrustAvailable
        && HasSelection
        && !IsMutationInProgress;

    public bool IsEmpty
    {
        get => isEmpty;
        private set => SetProperty(ref isEmpty, value);
    }

    public bool IsTrustAvailable
    {
        get => isTrustAvailable;
        private set
        {
            if (SetProperty(ref isTrustAvailable, value))
            {
                OnPropertyChanged(nameof(CanEditSelectedPeer));
                NotifyCommandStates();
            }
        }
    }

    public bool IsRevokeConfirmationVisible
    {
        get => isRevokeConfirmationVisible;
        private set
        {
            if (SetProperty(ref isRevokeConfirmationVisible, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsMutationInProgress
    {
        get => isMutationInProgress;
        private set
        {
            if (SetProperty(ref isMutationInProgress, value))
            {
                OnPropertyChanged(nameof(CanEditSelectedPeer));
                NotifyCommandStates();
            }
        }
    }

    public string Protection
    {
        get => protection;
        private set => SetProperty(ref protection, value);
    }

    public string RevokeConfirmation
    {
        get => revokeConfirmation;
        private set => SetProperty(ref revokeConfirmation, value);
    }

    public string RecoveryAction
    {
        get => recoveryAction;
        private set => SetProperty(ref recoveryAction, value);
    }

    public string MutationStatus
    {
        get => mutationStatus;
        private set => SetProperty(ref mutationStatus, value);
    }

    public string MutationDescription
    {
        get => mutationDescription;
        private set => SetProperty(ref mutationDescription, value);
    }

    public string TrustExportPath
    {
        get => trustExportPath;
        private set => SetProperty(ref trustExportPath, value);
    }

    public string TrustExportPreview
    {
        get => trustExportPreview;
        private set => SetProperty(ref trustExportPreview, value);
    }

    public TrustedDeviceItemViewModel? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            if (SetProperty(ref selectedDevice, value))
            {
                ApplySelectedDevice(value);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanEditSelectedPeer));
                NotifyCommandStates();
            }
        }
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string StatusDescription
    {
        get => statusDescription;
        private set => SetProperty(ref statusDescription, value);
    }

    public ICommand SaveCapabilitiesCommand => saveCapabilitiesCommand;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        bool enteredOperationGate = false;
        try
        {
            await operationGate
                .WaitAsync(linkedCancellation.Token)
                .ConfigureAwait(true);
            enteredOperationGate = true;
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            DesktopTrustSnapshot snapshot = await authority
                .InitializeAsync(linkedCancellation.Token)
                .ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            ApplyLoadFailure();
        }
        finally
        {
            if (enteredOperationGate)
            {
                operationGate.Release();
            }
        }
    }

    public Task SaveCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(SaveCapabilitiesCoreAsync, cancellationToken);

    public Task ExportTrustAsync(
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(ExportTrustCoreAsync, cancellationToken);

    private async Task ExportTrustCoreAsync(CancellationToken cancellationToken)
    {
        if (localDataService is null)
        {
            return;
        }

        try
        {
            DesktopRedactedExportResult exported = await localDataService
                .ExportTrustAsync(cancellationToken)
                .ConfigureAwait(true);
            TrustExportPath = exported.FullPath;
            TrustExportPreview = exported.RedactedContent;
            MutationStatus = DesktopText.Get("TrustedDevices_ExportWrittenStatus");
            MutationDescription = DesktopText.Get(
                "TrustedDevices_ExportWrittenDescription");
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            MutationStatus = DesktopText.Get("TrustedDevices_ExportFailedStatus");
            MutationDescription = DesktopText.Get(
                "TrustedDevices_ExportFailedDescription");
        }
    }

    private async Task SaveCapabilitiesCoreAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        TrustedDeviceItemViewModel? selected = SelectedDevice;
        if (selected is null || !HasUnsavedChanges)
        {
            return;
        }

        DesktopTrustMutationOutcome outcome;
        try
        {
            outcome = await authority
                .UpdateCapabilitiesAsync(
                    selected.Snapshot.DeviceId,
                    selected.Snapshot.Fingerprint,
                    CreateDraftGrant(),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            DesktopTrustSnapshot refreshed = await authority
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);
            ApplySnapshot(
                refreshed,
                selected.Snapshot.DeviceId,
                selected.Snapshot.Fingerprint);
            MutationStatus = DesktopText.Get("TrustedDevices_UpdateFailedStatus");
            MutationDescription = DesktopText.Get(
                "TrustedDevices_UpdateFailedDescription");
            return;
        }

        ApplySnapshot(
            outcome.Snapshot,
            selected.Snapshot.DeviceId,
            selected.Snapshot.Fingerprint);
        MutationStatus = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied => DesktopText.Get(
                "TrustedDevices_CapabilitiesSavedStatus"),
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                DesktopText.Get("TrustedDevices_CapabilitiesSavedStopUnconfirmed"),
            DesktopTrustMutationStatus.IdentityChanged =>
                DesktopText.Get("TrustedDevices_IdentityChangedStatus"),
            DesktopTrustMutationStatus.PeerNotFound => DesktopText.Get(
                "TrustedDevices_NoLongerPairedStatus"),
            _ => throw new InvalidOperationException(
                "The desktop Trust mutation status is not supported."),
        };
        MutationDescription = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied =>
                DesktopText.Get("TrustedDevices_UpdateAppliedDescription"),
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                DesktopText.Get("TrustedDevices_StopUnconfirmedDescription"),
            DesktopTrustMutationStatus.IdentityChanged =>
                DesktopText.Get("TrustedDevices_UpdateIdentityChangedDescription"),
            DesktopTrustMutationStatus.PeerNotFound =>
                DesktopText.Get("TrustedDevices_UpdatePeerMissingDescription"),
            _ => string.Empty,
        };
        await ReconcileConnectionsAsync(cancellationToken).ConfigureAwait(true);
    }

    public void BeginRevoke()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        TrustedDeviceItemViewModel? selected = SelectedDevice;
        if (selected is null || IsMutationInProgress)
        {
            return;
        }

        RevokeConfirmation = DesktopText.Format(
            "TrustedDevices_RevokeConfirmation",
            selected.DisplayName,
            selected.DeviceId);
        IsRevokeConfirmationVisible = true;
    }

    public void CancelRevoke()
    {
        IsRevokeConfirmationVisible = false;
        RevokeConfirmation = string.Empty;
    }

    public Task ConfirmRevokeAsync(
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(ConfirmRevokeCoreAsync, cancellationToken);

    private async Task ConfirmRevokeCoreAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        TrustedDeviceItemViewModel? selected = SelectedDevice;
        if (selected is null || !IsRevokeConfirmationVisible)
        {
            return;
        }

        DesktopTrustMutationOutcome outcome;
        try
        {
            outcome = await authority
                .RevokeAsync(
                    selected.Snapshot.DeviceId,
                    selected.Snapshot.Fingerprint,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            DesktopTrustSnapshot refreshed = await authority
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);
            ApplySnapshot(
                refreshed,
                selected.Snapshot.DeviceId,
                selected.Snapshot.Fingerprint);
            CancelRevoke();
            MutationStatus = DesktopText.Get("TrustedDevices_RevokeFailedStatus");
            MutationDescription = DesktopText.Get(
                "TrustedDevices_RevokeFailedDescription");
            return;
        }

        ApplySnapshot(outcome.Snapshot);
        CancelRevoke();
        MutationStatus = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied => DesktopText.Get(
                "TrustedDevices_RevokedStatus"),
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                DesktopText.Get("TrustedDevices_RevokedStopUnconfirmed"),
            DesktopTrustMutationStatus.IdentityChanged =>
                DesktopText.Get("TrustedDevices_IdentityChangedStatus"),
            DesktopTrustMutationStatus.PeerNotFound => DesktopText.Get(
                "TrustedDevices_NoLongerPairedStatus"),
            _ => throw new InvalidOperationException(
                "The desktop Trust mutation status is not supported."),
        };
        MutationDescription = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied =>
                DesktopText.Get("TrustedDevices_RevokeAppliedDescription"),
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                DesktopText.Get("TrustedDevices_StopUnconfirmedDescription"),
            DesktopTrustMutationStatus.IdentityChanged =>
                DesktopText.Get("TrustedDevices_RevokeIdentityChangedDescription"),
            DesktopTrustMutationStatus.PeerNotFound =>
                DesktopText.Get("TrustedDevices_RevokePeerMissingDescription"),
            _ => string.Empty,
        };
        await ReconcileConnectionsAsync(cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        try
        {
            lifetimeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await authority.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            operationGate.Dispose();
            lifetimeCancellation.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "One or more trusted-device resources failed to close.",
                failures);
        }
    }

    private void ApplySelectedDevice(TrustedDeviceItemViewModel? device)
    {
        CancelRevoke();
        CapabilityGrant capabilities =
            device?.Snapshot.GrantedCapabilities ?? CapabilityGrant.None;
        GrantActivityOffer = capabilities.Allows(Capability.ActivityOffer);
        GrantActivityReceive = capabilities.Allows(Capability.ActivityReceive);
        GrantActivityReplace = capabilities.Allows(Capability.ActivityReplace);
        GrantActivitySwap = capabilities.Allows(Capability.ActivitySwap);
        GrantMirrorView = capabilities.Allows(Capability.MirrorView);
        GrantMirrorDrive = capabilities.Allows(Capability.MirrorDrive);
        GrantFileReceive = capabilities.Allows(Capability.FileReceive);
        GrantSceneApply = capabilities.Allows(Capability.SceneApply);
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private async Task ReconcileConnectionsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await reconcileConnections(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            MutationDescription = string.IsNullOrEmpty(MutationDescription)
                ? DesktopText.Get("TrustedDevices_ReconnectRefreshFailedDescription")
                : DesktopText.Format(
                    "TrustedDevices_ReconnectRefreshFailedAppend",
                    MutationDescription);
        }
    }

    private void ApplySnapshot(
        DesktopTrustSnapshot snapshot,
        DeviceId? preferredDeviceId = null,
        string? preferredFingerprint = null)
    {
        Devices.Clear();
        foreach (TrustedPeerSnapshot peer in snapshot.TrustedPeers)
        {
            Devices.Add(new TrustedDeviceItemViewModel(peer));
        }

        IsTrustAvailable = true;
        IsEmpty = Devices.Count == 0;
        Status = Devices.Count switch
        {
            0 => DesktopText.Get("TrustedDevices_EmptyStatus"),
            1 => DesktopText.Get("TrustedDevices_OneStatus"),
            _ => DesktopText.Format("TrustedDevices_ManyStatus", Devices.Count),
        };
        StatusDescription = Devices.Count == 0
            ? DesktopText.Get("TrustedDevices_EmptyDescription")
            : DesktopText.Get("TrustedDevices_AvailableDescription");
        Protection = snapshot.Protection ==
            SecretStoreProtection.OperatingSystemProtected
            ? DesktopText.Get("TrustedDevices_Protected")
            : DesktopText.Get("TrustedDevices_TestModeProtection");
        RecoveryAction = string.Empty;
        SelectedDevice = Devices.FirstOrDefault(device =>
                device.Snapshot.DeviceId == preferredDeviceId
                && StringComparer.Ordinal.Equals(
                    device.Snapshot.Fingerprint,
                    preferredFingerprint))
            ?? Devices.FirstOrDefault();
    }

    private void ApplyLoadFailure()
    {
        Devices.Clear();
        SelectedDevice = null;
        IsTrustAvailable = false;
        IsEmpty = true;
        Status = DesktopText.Get("TrustedDevices_UnavailableStatus");
        StatusDescription = DesktopText.Get(
            "TrustedDevices_UnavailableDescription");
        Protection = DesktopText.Get("TrustedDevices_ProtectionUnavailable");
        RecoveryAction = DesktopText.Get("TrustedDevices_RecoveryAction");
        MutationStatus = string.Empty;
        MutationDescription = string.Empty;
    }

    private CapabilityGrant CreateDraftGrant()
    {
        var capabilities = new List<Capability>(8);
        AddIfGranted(GrantActivityOffer, Capability.ActivityOffer);
        AddIfGranted(GrantActivityReceive, Capability.ActivityReceive);
        AddIfGranted(GrantActivityReplace, Capability.ActivityReplace);
        AddIfGranted(GrantActivitySwap, Capability.ActivitySwap);
        AddIfGranted(GrantMirrorView, Capability.MirrorView);
        AddIfGranted(GrantMirrorDrive, Capability.MirrorDrive);
        AddIfGranted(GrantFileReceive, Capability.FileReceive);
        AddIfGranted(GrantSceneApply, Capability.SceneApply);
        return CapabilityGrant.Of(capabilities.ToArray());

        void AddIfGranted(bool granted, Capability capability)
        {
            if (granted)
            {
                capabilities.Add(capability);
            }
        }
    }

    private void SetGrantProperty(ref bool field, bool value)
    {
        if (SetProperty(ref field, value))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            saveCapabilitiesCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanBeginRevoke() => IsTrustAvailable
        && HasSelection
        && !IsRevokeConfirmationVisible
        && !IsMutationInProgress;

    private async Task RunMutationAsync(
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        bool enteredOperationGate = false;
        try
        {
            await operationGate
                .WaitAsync(linkedCancellation.Token)
                .ConfigureAwait(true);
            enteredOperationGate = true;
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            IsMutationInProgress = true;
            await mutation(linkedCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (enteredOperationGate)
            {
                IsMutationInProgress = false;
                operationGate.Release();
            }
        }
    }

    private void NotifyCommandStates()
    {
        beginRevokeCommand.NotifyCanExecuteChanged();
        cancelRevokeCommand.NotifyCanExecuteChanged();
        confirmRevokeCommand.NotifyCanExecuteChanged();
        exportTrustCommand.NotifyCanExecuteChanged();
        saveCapabilitiesCommand.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
        OnPropertyChanged(propertyName!);
        return true;
    }
}
