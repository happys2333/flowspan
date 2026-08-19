using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

public sealed class LocalPairingViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly AsyncLocal<LifetimeCallbackScope?> lifetimeCallbackScope = new();
    private readonly RelayCommand cancelPairingCommand;
    private readonly RelayCommand cancelPermissionReviewCommand;
    private readonly AsyncRelayCommand disableCommand;
    private readonly AsyncRelayCommand enableCommand;
    private readonly AsyncRelayCommand pairDeviceCommand;
    private readonly RelayCommand reviewPermissionCommand;
    private readonly Func<CancellationToken, Task> refreshTrust;
    private readonly DesktopLocalPairingRuntime runtime;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim pairingGate = new(1, 1);
    private readonly Lock pairingLifetimeGate = new();
    private readonly Lock presentationGate = new();
    private readonly Lock runtimeReadGate = new();
    private readonly SemaphoreSlim trustRefreshGate = new(1, 1);
    private TaskCompletionSource presentationsDrained = CreateCompletedSignal();
    private TaskCompletionSource runtimeReadsDrained = CreateCompletedSignal();
    private CancellationTokenSource? activePairingCancellation;
    private bool hasAcknowledgedPermissionReview;
    private bool isEnabled;
    private bool isEnabling;
    private bool isPairing;
    private bool isPermissionReviewVisible;
    private string pairingStatus = string.Empty;
    private string recoveryAction = string.Empty;
    private bool prerequisitesAvailable;
    private string listenerStatus = DesktopText.Get("LocalPairing_ListenerInactive");
    private string status = DesktopText.Get("LocalPairing_OffStatus");
    private string statusDescription = DesktopText.Get(
        "LocalPairing_OffDescription");
    private LocalPairingCandidateItemViewModel? selectedCandidate;
    private Exception? disposalFailure;
    private int disposed;
    private int presentationsInFlight;
    private int runtimeReadsInFlight;

    public LocalPairingViewModel(
        DesktopLocalPairingRuntime runtime,
        IDesktopUiDispatcher dispatcher,
        Func<CancellationToken, Task>? refreshTrust = null,
        DesktopLocalNetworkPermissionGuide? permissionGuide = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(dispatcher);
        this.runtime = runtime;
        this.dispatcher = dispatcher;
        this.refreshTrust = refreshTrust ?? (_ => Task.CompletedTask);
        PermissionGuide = permissionGuide
            ?? DesktopLocalNetworkPermissionGuide.ForCurrentPlatform();
        runtime.Changed += OnRuntimeChanged;
        runtime.TrustChanged += OnRuntimeTrustChanged;
        reviewPermissionCommand = new RelayCommand(
            OpenPermissionReview,
            () => prerequisitesAvailable
                && !IsEnabled
                && !IsEnabling
                && !IsPairing
                && !IsPermissionReviewVisible);
        cancelPermissionReviewCommand = new RelayCommand(
            CancelPermissionReview,
            () => IsPermissionReviewVisible && !IsEnabling && !IsEnabled);
        enableCommand = new AsyncRelayCommand(
            () => EnableAsync(),
            () => prerequisitesAvailable
                && IsPermissionReviewVisible
                && HasAcknowledgedPermissionReview
                && !IsEnabled
                && !IsEnabling
                && !IsPairing
                && runtime.Status
                    != DesktopLocalPairingStatus.CleanupUnconfirmed);
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

    public ICommand CancelPermissionReviewCommand => cancelPermissionReviewCommand;

    public ICommand DisableCommand => disableCommand;

    public ICommand EnableCommand => enableCommand;

    public bool HasSelection => SelectedCandidate is not null;

    public bool HasIdentityWarnings =>
        TrustedPeerConnections.Any(static connection => connection.HasIdentityWarning);

    public bool HasAcknowledgedPermissionReview
    {
        get => hasAcknowledgedPermissionReview;
        set
        {
            if (SetProperty(ref hasAcknowledgedPermissionReview, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool HasTrustedPeerConnections => TrustedPeerConnections.Count > 0;

    public bool IsEnabled
    {
        get => isEnabled;
        private set
        {
            if (SetProperty(ref isEnabled, value))
            {
                OnPropertyChanged(nameof(IsPermissionReviewActionVisible));
                NotifyCommandStates();
            }
        }
    }

    public bool IsEnabling
    {
        get => isEnabling;
        private set
        {
            if (SetProperty(ref isEnabling, value))
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

    public bool IsPermissionReviewVisible
    {
        get => isPermissionReviewVisible;
        private set
        {
            if (SetProperty(ref isPermissionReviewVisible, value))
            {
                OnPropertyChanged(nameof(IsPermissionReviewActionVisible));
                NotifyCommandStates();
            }
        }
    }

    public bool IsPermissionReviewActionVisible =>
        !IsPermissionReviewVisible && !IsEnabled;

    public string ListenerStatus
    {
        get => listenerStatus;
        private set => SetProperty(ref listenerStatus, value);
    }

    public string PermissionDataExposure => PermissionGuide.DataExposure;

    public string PermissionEducation => PermissionGuide.Purpose;

    public DesktopLocalNetworkPermissionGuide PermissionGuide { get; }

    public string PermissionPlatformName => PermissionGuide.PlatformName;

    public string PermissionPromptExpectation => PermissionGuide.PromptExpectation;

    public string PermissionRevocationAction => PermissionGuide.RevocationAction;

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

    public ICommand ReviewPermissionCommand => reviewPermissionCommand;

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
        if (!prerequisitesAvailable
            || !IsPermissionReviewVisible
            || !HasAcknowledgedPermissionReview
            || IsEnabling)
        {
            return;
        }

        bool presentationAdmitted = TryBeginPresentation();
        ObjectDisposedException.ThrowIf(!presentationAdmitted, this);
        using LifetimeCallbackScopeLease callbackScope =
            EnterLifetimeCallbackScope();
        try
        {
            IsEnabling = true;
            Status = DesktopText.Get("LocalPairing_EnablingStatus");
            StatusDescription = DesktopText.Get(
                "LocalPairing_EnablingDescription");
            RecoveryAction = string.Empty;
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token);
            try
            {
                await runtime.EnableAsync(linkedCancellation.Token).ConfigureAwait(true);
                IsPermissionReviewVisible = false;
                RefreshFromRuntime();
            }
            catch (OperationCanceledException)
                when (lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RuntimeProjection? failureProjection = CaptureRuntimeProjection();
                bool cleanupUnconfirmed = failureProjection?.Status
                    == DesktopLocalPairingStatus.CleanupUnconfirmed;
                IsEnabled = false;
                Status = DesktopText.Get("LocalPairing_UnavailableStatus");
                StatusDescription = cleanupUnconfirmed
                    ? DesktopText.Get("LocalPairing_CleanupUnconfirmedDescription")
                    : DesktopText.Get("LocalPairing_OpenFailedDescription");
                RecoveryAction = DesktopText.Get("LocalPairing_RecoveryAction");
                ListenerStatus = cleanupUnconfirmed
                    ? FormatCleanupUnconfirmedListenerStatus(
                        failureProjection?.ListeningPort)
                    : DesktopText.Get("LocalPairing_ListenerInactive");
                NotifyCommandStates();
            }
            finally
            {
                IsEnabling = false;
            }
        }
        finally
        {
            EndPresentation();
        }
    }

    public async Task DisableAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        bool presentationAdmitted = TryBeginPresentation();
        ObjectDisposedException.ThrowIf(!presentationAdmitted, this);
        using LifetimeCallbackScopeLease callbackScope =
            EnterLifetimeCallbackScope();
        try
        {
            await runtime.DisableAsync().ConfigureAwait(true);
            IsEnabled = false;
            Status = DesktopText.Get("LocalPairing_OffStatus");
            StatusDescription = DesktopText.Get("LocalPairing_OffDescription");
            ListenerStatus = DesktopText.Get("LocalPairing_ListenerInactive");
            RecoveryAction = string.Empty;
            IsPermissionReviewVisible = false;
            HasAcknowledgedPermissionReview = false;
            Candidates.Clear();
            SelectedCandidate = null;
            TrustedPeerConnections.Clear();
            OnPropertyChanged(nameof(HasTrustedPeerConnections));
            OnPropertyChanged(nameof(HasIdentityWarnings));
        }
        finally
        {
            EndPresentation();
        }
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
        LifetimeCallbackScopeLease? callbackScope = null;
        bool presentationAdmitted = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            presentationAdmitted = TryBeginPresentation();
            if (!presentationAdmitted)
            {
                return;
            }

            callbackScope = EnterLifetimeCallbackScope();
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            lock (pairingLifetimeGate)
            {
                activePairingCancellation = linked;
            }

            IsPairing = true;
            PairingStatus = DesktopText.Get("LocalPairing_InProgressStatus");
            PairingCeremonyResult result = await runtime.PairAsync(
                selected.Candidate,
                linked.Token).ConfigureAwait(true);

            if (result.Succeeded)
            {
                await refreshTrust(linked.Token).ConfigureAwait(true);
                PairingStatus = DesktopText.Get("LocalPairing_PairedStatus");
                RefreshFromRuntime();
            }
            else
            {
                PairingStatus = result.Failure == PairingFailure.IdentityChanged
                    ? DesktopText.Get("LocalPairing_IdentityChangedStatus")
                    : DesktopText.Get("LocalPairing_RejectedStatus");
            }
        }
        catch (OperationCanceledException) when (
            linked?.IsCancellationRequested == true)
        {
            PairingStatus = DesktopText.Get("LocalPairing_CanceledStatus");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PairingStatus = DesktopText.Get("LocalPairing_FailedStatus");
        }
        finally
        {
            bool disposeLinked = false;
            lock (pairingLifetimeGate)
            {
                if (ReferenceEquals(activePairingCancellation, linked))
                {
                    activePairingCancellation = null;
                    disposeLinked = true;
                }
            }

            if (disposeLinked)
            {
                linked?.Dispose();
            }

            try
            {
                IsPairing = false;
            }
            finally
            {
                callbackScope?.Dispose();
                if (presentationAdmitted)
                {
                    EndPresentation();
                }

                pairingGate.Release();
            }
        }
    }

    public void CancelPairing()
    {
        CancellationTokenSource? activePairing;
        lock (pairingLifetimeGate)
        {
            activePairing = activePairingCancellation;
        }

        try
        {
            activePairing?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The pairing operation completed after ownership was sampled.
        }
    }

    public void SetPrerequisitesAvailable(bool available)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        prerequisitesAvailable = available;
        if (!available && !IsEnabled)
        {
            IsPermissionReviewVisible = false;
            HasAcknowledgedPermissionReview = false;
        }

        NotifyCommandStates();
    }

    public async ValueTask DisposeAsync()
    {
        bool isLifetimeCallback = IsLifetimeCallbackActive;

        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            _ = DisposeResourcesAsync();
        }

        if (isLifetimeCallback)
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

    private async Task DisposeResourcesAsync()
    {
        var failures = new List<Exception>();
        CancellationTokenSource? activePairing;
        lock (pairingLifetimeGate)
        {
            activePairing = activePairingCancellation;
            if (activePairing is not null)
            {
                activePairingCancellation = null;
            }
        }

        Task activePairingCancellationTask = BeginCancellation(
            activePairing,
            failures);
        Task lifetimeCancellationTask = BeginCancellation(
            lifetimeCancellation,
            failures);

        try
        {
            runtime.Changed -= OnRuntimeChanged;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            runtime.TrustChanged -= OnRuntimeTrustChanged;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        await GetRuntimeReadsDrainedTask().ConfigureAwait(false);

        try
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        await GetPresentationsDrainedTask().ConfigureAwait(false);

        await CaptureFailureAsync(activePairingCancellationTask, failures)
            .ConfigureAwait(false);
        await CaptureFailureAsync(lifetimeCancellationTask, failures)
            .ConfigureAwait(false);
        await DrainGateAsync(pairingGate, failures).ConfigureAwait(false);
        await DrainGateAsync(trustRefreshGate, failures).ConfigureAwait(false);

        try
        {
            activePairing?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            lifetimeCancellation.Dispose();
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
                "One or more local pairing view resources failed to close.",
                failures),
        };
        disposalCompleted.TrySetResult();
    }

    private static Task BeginCancellation(
        CancellationTokenSource? source,
        List<Exception> failures)
    {
        if (source is null)
        {
            return Task.CompletedTask;
        }

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

    private static async Task DrainGateAsync(
        SemaphoreSlim gate,
        List<Exception> failures)
    {
        bool entered = false;
        try
        {
            await gate.WaitAsync().ConfigureAwait(false);
            entered = true;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            if (entered)
            {
                gate.Release();
            }
        }
    }

    private bool TryBeginPresentation()
    {
        lock (presentationGate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            if (presentationsInFlight++ == 0)
            {
                presentationsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return true;
        }
    }

    private void EndPresentation()
    {
        TaskCompletionSource? drained = null;
        lock (presentationGate)
        {
            presentationsInFlight--;
            if (presentationsInFlight == 0)
            {
                drained = presentationsDrained;
            }
        }

        drained?.TrySetResult();
    }

    private Task GetPresentationsDrainedTask()
    {
        lock (presentationGate)
        {
            return presentationsDrained.Task;
        }
    }

    private bool TryBeginRuntimeRead()
    {
        lock (runtimeReadGate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            if (runtimeReadsInFlight++ == 0)
            {
                runtimeReadsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return true;
        }
    }

    private void EndRuntimeRead()
    {
        TaskCompletionSource? drained = null;
        lock (runtimeReadGate)
        {
            runtimeReadsInFlight--;
            if (runtimeReadsInFlight == 0)
            {
                drained = runtimeReadsDrained;
            }
        }

        drained?.TrySetResult();
    }

    private Task GetRuntimeReadsDrainedTask()
    {
        lock (runtimeReadGate)
        {
            return runtimeReadsDrained.Task;
        }
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }

    private void OnRuntimeChanged()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        dispatcher.Post(() =>
        {
            if (!TryBeginPresentation())
            {
                return;
            }

            using LifetimeCallbackScopeLease callbackScope =
                EnterLifetimeCallbackScope();
            try
            {
                RefreshFromRuntime();
            }
            catch (ObjectDisposedException)
                when (Volatile.Read(ref disposed) != 0)
            {
            }
            finally
            {
                EndPresentation();
            }
        });
    }

    private void OnRuntimeTrustChanged()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        dispatcher.Post(() =>
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                _ = RefreshTrustAfterInboundPairingAsync();
            }
        });
    }

    private async Task RefreshTrustAfterInboundPairingAsync()
    {
        if (!TryBeginPresentation())
        {
            return;
        }

        LifetimeCallbackScopeLease callbackScope =
            EnterLifetimeCallbackScope();
        bool entered = false;
        try
        {
            await trustRefreshGate.WaitAsync(lifetimeCancellation.Token)
                .ConfigureAwait(true);
            entered = true;
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            await refreshTrust(lifetimeCancellation.Token).ConfigureAwait(true);
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            RefreshFromRuntime();
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                PairingStatus = DesktopText.Get(
                    "LocalPairing_TrustRefreshFailedStatus");
            }
        }
        finally
        {
            try
            {
                if (entered)
                {
                    trustRefreshGate.Release();
                }
            }
            finally
            {
                callbackScope.Dispose();
                EndPresentation();
            }
        }
    }

    private void NotifyCommandStates()
    {
        reviewPermissionCommand.NotifyCanExecuteChanged();
        cancelPermissionReviewCommand.NotifyCanExecuteChanged();
        enableCommand.NotifyCanExecuteChanged();
        disableCommand.NotifyCanExecuteChanged();
        pairDeviceCommand.NotifyCanExecuteChanged();
        cancelPairingCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromRuntime()
    {
        RuntimeProjection? projection = CaptureRuntimeProjection();
        if (projection is null || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        ApplyRuntimeProjection(projection);
    }

    private RuntimeProjection? CaptureRuntimeProjection()
    {
        if (!TryBeginRuntimeRead())
        {
            return null;
        }

        try
        {
            DesktopLocalPairingStatus runtimeStatus = runtime.Status;
            return new RuntimeProjection(
                runtimeStatus == DesktopLocalPairingStatus.Enabled,
                runtime.ListeningPort,
                runtimeStatus,
                runtime.GetCandidates(),
                runtime.GetTrustedPeerConnections());
        }
        catch (ObjectDisposedException)
            when (Volatile.Read(ref disposed) != 0)
        {
            return null;
        }
        finally
        {
            EndRuntimeRead();
        }
    }

    private void ApplyRuntimeProjection(RuntimeProjection projection)
    {
        IsEnabled = projection.IsEnabled;
        if (projection.Status == DesktopLocalPairingStatus.Enabled)
        {
            Status = DesktopText.Get("LocalPairing_EnabledStatus");
            StatusDescription = DesktopText.Get("LocalPairing_EnabledDescription");
            ListenerStatus = DesktopText.Format(
                "LocalPairing_ListeningPort",
                projection.ListeningPort);
        }
        else if (projection.Status is DesktopLocalPairingStatus.Faulted
                 or DesktopLocalPairingStatus.CleanupUnconfirmed)
        {
            Status = DesktopText.Get("LocalPairing_UnavailableStatus");
            StatusDescription = projection.Status
                == DesktopLocalPairingStatus.CleanupUnconfirmed
                ? DesktopText.Get("LocalPairing_CleanupUnconfirmedDescription")
                : DesktopText.Get("LocalPairing_BackgroundFailedDescription");
            RecoveryAction = DesktopText.Get("LocalPairing_RecoveryAction");
            ListenerStatus = projection.Status
                == DesktopLocalPairingStatus.CleanupUnconfirmed
                ? FormatCleanupUnconfirmedListenerStatus(
                    projection.ListeningPort)
                : DesktopText.Get("LocalPairing_ListenerInactive");
            IsPermissionReviewVisible = true;
        }

        Candidates.Clear();
        SelectedCandidate = null;
        foreach (UnverifiedPairingCandidate candidate in projection.Candidates)
        {
            Candidates.Add(new LocalPairingCandidateItemViewModel(candidate));
        }

        TrustedPeerConnections.Clear();
        foreach (DesktopTrustedPeerConnectionSnapshot connection in
                 projection.TrustedPeerConnections)
        {
            TrustedPeerConnections.Add(
                new TrustedPeerConnectionItemViewModel(connection));
        }

        OnPropertyChanged(nameof(HasTrustedPeerConnections));
        OnPropertyChanged(nameof(HasIdentityWarnings));
    }

    private static string FormatCleanupUnconfirmedListenerStatus(
        int? listeningPort) =>
        listeningPort is { } port
            ? DesktopText.Format("LocalPairing_CleanupPort", port)
            : DesktopText.Get("LocalPairing_CleanupListener");

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

    private void OpenPermissionReview()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        IsPermissionReviewVisible = true;
    }

    private void CancelPermissionReview()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        HasAcknowledgedPermissionReview = false;
        IsPermissionReviewVisible = false;
    }

    private sealed record RuntimeProjection(
        bool IsEnabled,
        int? ListeningPort,
        DesktopLocalPairingStatus Status,
        ImmutableArray<UnverifiedPairingCandidate> Candidates,
        ImmutableArray<DesktopTrustedPeerConnectionSnapshot> TrustedPeerConnections);

    private bool IsLifetimeCallbackActive =>
        lifetimeCallbackScope.Value?.IsActive == true;

    private LifetimeCallbackScopeLease EnterLifetimeCallbackScope() =>
        new(lifetimeCallbackScope);

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
            ?? DesktopText.Get("LocalPairing_AuthenticationUnavailable");
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
                DesktopText.Get("LocalPairing_CandidateUnverified"),
            PairingCandidateTrustState.AlreadyPaired => DesktopText.Get(
                "LocalPairing_CandidatePaired"),
            PairingCandidateTrustState.IdentityChangedBlocked =>
                DesktopText.Get("LocalPairing_IdentityChangedStatus"),
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
