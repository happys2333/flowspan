using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

public sealed class LocalPairingViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly RelayCommand cancelPairingCommand;
    private readonly AsyncRelayCommand disableCommand;
    private readonly AsyncRelayCommand enableCommand;
    private readonly AsyncRelayCommand pairDeviceCommand;
    private readonly Func<CancellationToken, Task> refreshTrust;
    private readonly DesktopLocalPairingRuntime runtime;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim pairingGate = new(1, 1);
    private readonly Lock pairingLifetimeGate = new();
    private readonly SemaphoreSlim trustRefreshGate = new(1, 1);
    private CancellationTokenSource? activePairingCancellation;
    private bool isEnabled;
    private bool isPairing;
    private string pairingStatus = string.Empty;
    private string recoveryAction = string.Empty;
    private bool prerequisitesAvailable;
    private string listenerStatus = "Listener inactive";
    private string status = "LOCAL PAIRING OFF";
    private string statusDescription =
        "No listener, discovery browser, or advertisement is active.";
    private LocalPairingCandidateItemViewModel? selectedCandidate;
    private int disposed;

    public LocalPairingViewModel(
        DesktopLocalPairingRuntime runtime,
        IDesktopUiDispatcher dispatcher,
        Func<CancellationToken, Task>? refreshTrust = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(dispatcher);
        this.runtime = runtime;
        this.dispatcher = dispatcher;
        this.refreshTrust = refreshTrust ?? (_ => Task.CompletedTask);
        runtime.Changed += OnRuntimeChanged;
        runtime.TrustChanged += OnRuntimeTrustChanged;
        enableCommand = new AsyncRelayCommand(
            () => EnableAsync(),
            () => prerequisitesAvailable && !IsEnabled && !IsPairing);
        disableCommand = new AsyncRelayCommand(
            DisableAsync,
            () => IsEnabled && !IsPairing);
        pairDeviceCommand = new AsyncRelayCommand(
            () => PairSelectedAsync(),
            () => IsEnabled
                && !IsPairing
                && SelectedCandidate?.CanPair == true);
        cancelPairingCommand = new RelayCommand(
            CancelPairing,
            () => IsPairing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LocalPairingCandidateItemViewModel> Candidates { get; } = [];

    public ObservableCollection<TrustedPeerConnectionItemViewModel>
        TrustedPeerConnections
    { get; } = [];

    public ICommand CancelPairingCommand => cancelPairingCommand;

    public ICommand DisableCommand => disableCommand;

    public ICommand EnableCommand => enableCommand;

    public bool HasSelection => SelectedCandidate is not null;

    public bool HasIdentityWarnings =>
        TrustedPeerConnections.Any(static connection => connection.HasIdentityWarning);

    public bool HasTrustedPeerConnections => TrustedPeerConnections.Count > 0;

    public bool IsEnabled
    {
        get => isEnabled;
        private set
        {
            if (SetProperty(ref isEnabled, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsPairing
    {
        get => isPairing;
        private set
        {
            if (SetProperty(ref isPairing, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public string ListenerStatus
    {
        get => listenerStatus;
        private set => SetProperty(ref listenerStatus, value);
    }

    public string PermissionEducation { get; } =
        "Enable only when you want this device discoverable on the current local network. "
        + "The operating system or firewall may request local-network access.";

    public string PairingStatus
    {
        get => pairingStatus;
        private set => SetProperty(ref pairingStatus, value);
    }

    public string RecoveryAction
    {
        get => recoveryAction;
        private set => SetProperty(ref recoveryAction, value);
    }

    public ICommand PairDeviceCommand => pairDeviceCommand;

    public LocalPairingCandidateItemViewModel? SelectedCandidate
    {
        get => selectedCandidate;
        set
        {
            if (SetProperty(ref selectedCandidate, value))
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(HasSelection)));
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

    public async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!prerequisitesAvailable)
        {
            return;
        }

        Status = "ENABLING LOCAL PAIRING";
        StatusDescription =
            "Opening one local listener and starting minimized discovery.";
        RecoveryAction = string.Empty;
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            await runtime.EnableAsync(linkedCancellation.Token).ConfigureAwait(true);
            RefreshFromRuntime();
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IsEnabled = false;
            Status = "LOCAL PAIRING UNAVAILABLE";
            StatusDescription =
                "Flowspan could not safely open local discovery and the listener.";
            RecoveryAction =
                "Check the local firewall or network permission, then retry.";
            ListenerStatus = "Listener inactive";
            NotifyCommandStates();
        }
    }

    public async Task DisableAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await runtime.DisableAsync().ConfigureAwait(true);
        IsEnabled = false;
        Status = "LOCAL PAIRING OFF";
        StatusDescription =
            "No listener, discovery browser, or advertisement is active.";
        ListenerStatus = "Listener inactive";
        RecoveryAction = string.Empty;
        Candidates.Clear();
        SelectedCandidate = null;
        TrustedPeerConnections.Clear();
        OnPropertyChanged(nameof(HasTrustedPeerConnections));
        OnPropertyChanged(nameof(HasIdentityWarnings));
    }

    public async Task PairSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        LocalPairingCandidateItemViewModel? selected = SelectedCandidate;
        if (!runtime.IsEnabled || selected is null || !selected.CanPair)
        {
            return;
        }

        await pairingGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        CancellationTokenSource? linked = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            lock (pairingLifetimeGate)
            {
                activePairingCancellation = linked;
            }

            IsPairing = true;
            PairingStatus = "PAIRING IN PROGRESS";
            PairingCeremonyResult result = await runtime.PairAsync(
                selected.Candidate,
                linked.Token).ConfigureAwait(true);
            if (result.Succeeded)
            {
                await refreshTrust(linked.Token).ConfigureAwait(true);
                PairingStatus = "DEVICE PAIRED";
                RefreshFromRuntime();
            }
            else
            {
                PairingStatus = result.Failure == PairingFailure.IdentityChanged
                    ? "IDENTITY CHANGED — BLOCKED"
                    : "PAIRING REJECTED";
            }
        }
        catch (OperationCanceledException) when (
            linked?.IsCancellationRequested == true)
        {
            PairingStatus = "PAIRING CANCELED";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PairingStatus = "PAIRING FAILED — RETRY";
        }
        finally
        {
            lock (pairingLifetimeGate)
            {
                if (ReferenceEquals(activePairingCancellation, linked))
                {
                    activePairingCancellation = null;
                }
            }

            linked?.Dispose();
            IsPairing = false;
            pairingGate.Release();
        }
    }

    public void CancelPairing()
    {
        lock (pairingLifetimeGate)
        {
            activePairingCancellation?.Cancel();
        }
    }

    public void SetPrerequisitesAvailable(bool available)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        prerequisitesAvailable = available;
        NotifyCommandStates();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        CancelPairing();
        runtime.Changed -= OnRuntimeChanged;
        runtime.TrustChanged -= OnRuntimeTrustChanged;
        await runtime.DisposeAsync().ConfigureAwait(false);
        await pairingGate.WaitAsync().ConfigureAwait(false);
        pairingGate.Release();
        await trustRefreshGate.WaitAsync().ConfigureAwait(false);
        trustRefreshGate.Release();
        pairingGate.Dispose();
        trustRefreshGate.Dispose();
        lifetimeCancellation.Dispose();
    }

    private void OnRuntimeChanged() => dispatcher.Post(RefreshFromRuntime);

    private void OnRuntimeTrustChanged() => dispatcher.Post(
        () => _ = RefreshTrustAfterInboundPairingAsync());

    private async Task RefreshTrustAfterInboundPairingAsync()
    {
        bool entered = false;
        try
        {
            await trustRefreshGate.WaitAsync(lifetimeCancellation.Token)
                .ConfigureAwait(true);
            entered = true;
            await refreshTrust(lifetimeCancellation.Token).ConfigureAwait(true);
            RefreshFromRuntime();
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            PairingStatus = "TRUST REFRESH FAILED — RETRY";
        }
        finally
        {
            if (entered)
            {
                trustRefreshGate.Release();
            }
        }
    }

    private void NotifyCommandStates()
    {
        enableCommand.NotifyCanExecuteChanged();
        disableCommand.NotifyCanExecuteChanged();
        pairDeviceCommand.NotifyCanExecuteChanged();
        cancelPairingCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromRuntime()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        IsEnabled = runtime.IsEnabled;
        if (runtime.Status == DesktopLocalPairingStatus.Enabled)
        {
            Status = "LOCAL PAIRING ENABLED";
            StatusDescription =
                "This device is discoverable for pairing. NOT SHARING remains active.";
            ListenerStatus = $"Listening on local TCP port {runtime.ListeningPort}";
        }
        else if (runtime.Status == DesktopLocalPairingStatus.Faulted)
        {
            Status = "LOCAL PAIRING UNAVAILABLE";
            StatusDescription =
                "Flowspan stopped local discovery because a background network service failed.";
            RecoveryAction =
                "Check the local firewall or network permission, then retry.";
            ListenerStatus = "Listener inactive";
        }

        Candidates.Clear();
        SelectedCandidate = null;
        foreach (UnverifiedPairingCandidate candidate in runtime.GetCandidates())
        {
            Candidates.Add(new LocalPairingCandidateItemViewModel(candidate));
        }

        TrustedPeerConnections.Clear();
        foreach (DesktopTrustedPeerConnectionSnapshot connection in
                 runtime.GetTrustedPeerConnections())
        {
            TrustedPeerConnections.Add(
                new TrustedPeerConnectionItemViewModel(connection));
        }

        OnPropertyChanged(nameof(HasTrustedPeerConnections));
        OnPropertyChanged(nameof(HasIdentityWarnings));
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

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TrustedPeerConnectionItemViewModel
{
    internal TrustedPeerConnectionItemViewModel(
        DesktopTrustedPeerConnectionSnapshot snapshot)
    {
        DeviceId = snapshot.DeviceId.ToString();
        DisplayName = snapshot.DisplayName;
        ExpectedFingerprint = snapshot.ExpectedFingerprint;
        ConflictingFingerprint = snapshot.ConflictingFingerprint
            ?? "Unavailable from authentication";
        HasIdentityWarning = snapshot.HasIdentityWarning;
        IdentityWarning = snapshot.IdentityWarning;
        Status = snapshot.StatusLabel;
        StatusDescription = snapshot.StatusDescription;
    }

    public string ConflictingFingerprint { get; }

    public string DeviceId { get; }

    public string DisplayName { get; }

    public string ExpectedFingerprint { get; }

    public bool HasIdentityWarning { get; }

    public string IdentityWarning { get; }

    public string Status { get; }

    public string StatusDescription { get; }
}

public sealed class LocalPairingCandidateItemViewModel
{
    internal LocalPairingCandidateItemViewModel(UnverifiedPairingCandidate candidate)
    {
        Candidate = candidate;
        DisplayName = candidate.Offer.DisplayName;
        DeviceId = candidate.Offer.DeviceId.ToString();
        Fingerprint = candidate.Offer.IdentityFingerprint;
        EndPoint = candidate.EndPoint.ToString();
        ExpiresAt = candidate.Offer.ExpiresAt.ToString("u");
        CanPair = candidate.TrustState
            == PairingCandidateTrustState.UnverifiedPairingRequired;
        Status = candidate.TrustState switch
        {
            PairingCandidateTrustState.UnverifiedPairingRequired =>
                "UNVERIFIED — PAIRING REQUIRED",
            PairingCandidateTrustState.AlreadyPaired => "ALREADY PAIRED",
            PairingCandidateTrustState.IdentityChangedBlocked =>
                "IDENTITY CHANGED — BLOCKED",
            _ => throw new InvalidOperationException(
                "The local pairing candidate Trust state is not supported."),
        };
    }

    internal UnverifiedPairingCandidate Candidate { get; }

    public bool CanPair { get; }

    public string DeviceId { get; }

    public string DisplayName { get; }

    public string EndPoint { get; }

    public string ExpiresAt { get; }

    public string Fingerprint { get; }

    public string Status { get; }
}
