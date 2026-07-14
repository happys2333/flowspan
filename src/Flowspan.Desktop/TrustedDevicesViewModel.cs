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
            ? "No capabilities granted"
            : string.Join(
                " · ",
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
    private readonly RelayCommand beginRevokeCommand;
    private readonly RelayCommand cancelRevokeCommand;
    private readonly AsyncRelayCommand confirmRevokeCommand;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly AsyncRelayCommand saveCapabilitiesCommand;
    private TrustedDeviceItemViewModel? selectedDevice;
    private bool grantActivityOffer;
    private bool grantActivityReceive;
    private bool grantActivityReplace;
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
    private string protection = "Trust Store not loaded";
    private string recoveryAction = string.Empty;
    private string revokeConfirmation = string.Empty;
    private string status = "LOADING TRUST STORE";
    private string statusDescription =
        "Flowspan is loading paired devices from protected local storage.";
    private int disposed;

    public TrustedDevicesViewModel(IDesktopTrustAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        this.authority = authority;
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TrustedDeviceItemViewModel> Devices { get; } = [];

    public ICommand BeginRevokeCommand => beginRevokeCommand;

    public ICommand CancelRevokeCommand => cancelRevokeCommand;

    public ICommand ConfirmRevokeCommand => confirmRevokeCommand;

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
            MutationStatus = "TRUST UPDATE FAILED";
            MutationDescription =
                "The protected Trust Store kept its previous Capability grant. "
                + "Unlock the credential store and retry.";
            return;
        }

        ApplySnapshot(
            outcome.Snapshot,
            selected.Snapshot.DeviceId,
            selected.Snapshot.Fingerprint);
        MutationStatus = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied => "CAPABILITIES SAVED",
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                "CAPABILITIES SAVED — SESSION STOP UNCONFIRMED",
            DesktopTrustMutationStatus.IdentityChanged =>
                "IDENTITY CHANGED — REVIEW REQUIRED",
            DesktopTrustMutationStatus.PeerNotFound => "DEVICE NO LONGER PAIRED",
            _ => throw new InvalidOperationException(
                "The desktop Trust mutation status is not supported."),
        };
        MutationDescription = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied =>
                "The complete Capability grant is committed.",
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                "Authorization is removed, but one or more active sessions did not confirm shutdown.",
            DesktopTrustMutationStatus.IdentityChanged =>
                "No change was applied. Review the replacement identity before continuing.",
            DesktopTrustMutationStatus.PeerNotFound =>
                "No change was applied because the Trust Record no longer exists.",
            _ => string.Empty,
        };
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

        RevokeConfirmation =
            $"Revoke {selected.DisplayName} ({selected.DeviceId})? "
            + "New operations will be rejected immediately and active sharing "
            + "will be asked to stop. This action has no undo.";
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
            MutationStatus = "TRUST REVOKE FAILED";
            MutationDescription =
                "The protected Trust Store kept the existing Trust Record. "
                + "Unlock the credential store and retry.";
            return;
        }

        ApplySnapshot(outcome.Snapshot);
        CancelRevoke();
        MutationStatus = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied => "DEVICE REVOKED",
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                "DEVICE REVOKED — SESSION STOP UNCONFIRMED",
            DesktopTrustMutationStatus.IdentityChanged =>
                "IDENTITY CHANGED — REVIEW REQUIRED",
            DesktopTrustMutationStatus.PeerNotFound => "DEVICE NO LONGER PAIRED",
            _ => throw new InvalidOperationException(
                "The desktop Trust mutation status is not supported."),
        };
        MutationDescription = outcome.Status switch
        {
            DesktopTrustMutationStatus.Applied =>
                "The Trust Record is removed and new operations are blocked.",
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure =>
                "Authorization is removed, but one or more active sessions did not confirm shutdown.",
            DesktopTrustMutationStatus.IdentityChanged =>
                "No Trust Record was removed. Review the replacement identity.",
            DesktopTrustMutationStatus.PeerNotFound =>
                "The Trust Record had already been removed.",
            _ => string.Empty,
        };
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
        GrantMirrorView = capabilities.Allows(Capability.MirrorView);
        GrantMirrorDrive = capabilities.Allows(Capability.MirrorDrive);
        GrantFileReceive = capabilities.Allows(Capability.FileReceive);
        GrantSceneApply = capabilities.Allows(Capability.SceneApply);
        OnPropertyChanged(nameof(HasUnsavedChanges));
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
            0 => "NO PAIRED DEVICES",
            1 => "1 PAIRED DEVICE",
            _ => $"{Devices.Count} PAIRED DEVICES",
        };
        StatusDescription = Devices.Count == 0
            ? "No device has a persisted Trust Record. Pairing and discovery are separate."
            : "Select a paired device to inspect or edit its local Capability grants.";
        Protection = snapshot.Protection ==
            SecretStoreProtection.OperatingSystemProtected
            ? "Operating-system protected"
            : "TEST MODE — trust is not persisted";
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
        Status = "TRUST STORE UNAVAILABLE";
        StatusDescription =
            "Paired devices cannot be loaded safely, so Trust editing is disabled.";
        Protection = "Protection unavailable";
        RecoveryAction =
            "Unlock the operating-system credential store, verify this user can access it, and retry.";
        MutationStatus = string.Empty;
        MutationDescription = string.Empty;
    }

    private CapabilityGrant CreateDraftGrant()
    {
        var capabilities = new List<Capability>(7);
        AddIfGranted(GrantActivityOffer, Capability.ActivityOffer);
        AddIfGranted(GrantActivityReceive, Capability.ActivityReceive);
        AddIfGranted(GrantActivityReplace, Capability.ActivityReplace);
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
