using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Desktop;

public interface IDesktopRemoteWindowService : IAsyncDisposable
{
    public event Action? Changed;

    public bool IsAvailable { get; }

    /// <summary>
    /// Monotonically increases whenever this service replaces its underlying
    /// controller. The value must not decrease during the service lifetime.
    /// </summary>
    public long ControllerGeneration => 0;

    public string UnavailableReasonCode { get; }

    public RemoteWindowEmergencyStopResult EmergencyStop();

    public RemoteWindowSharingSnapshot? GetSnapshot();

    public ValueTask<RemoteWindowCommandResult> ResetAfterLocalConfirmationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RemoteWindowCommandResult>(
            new PlatformNotSupportedException(
                "Remote Window local reset is not configured by this Desktop service."));

    public ValueTask<RemoteWindowCommandResult> StartAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        MirrorParticipantRole role,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RemoteWindowCommandResult>(
            new PlatformNotSupportedException(
                "Remote Window start is not configured by this Desktop service."));
}

public sealed class RemoteWindowWorkspaceViewModel :
    INotifyPropertyChanged,
    IAsyncDisposable
{
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly RelayCommand cancelCapturePermissionReviewCommand;
    private readonly RelayCommand cancelInputPermissionReviewCommand;
    private readonly RelayCommand emergencyStopCommand;
    private readonly CancellationTokenSource permissionLifetimeCancellation = new();
    private readonly SemaphoreSlim permissionGate = new(1, 1);
    private readonly IDesktopRemoteWindowPermissionService permissionService;
    private readonly object fallbackAdmissionGate = new();
    private readonly SemaphoreSlim remoteWindowGate = new(1, 1);
    private readonly AsyncRelayCommand requestCapturePermissionCommand;
    private readonly AsyncRelayCommand requestInputPermissionCommand;
    private readonly AsyncRelayCommand resetRemoteWindowCommand;
    private readonly RelayCommand reviewCapturePermissionCommand;
    private readonly IDesktopRemoteWindowService service;
    private readonly AsyncLocal<ExternalBoundaryCallScope?> externalBoundaryCallScope =
        new();
    private readonly object presentationGate = new();
    private readonly object serviceBoundaryGate = new();
    private readonly AsyncRelayCommand startRemoteWindowCommand;
    private string activityId = "No live Activity";
    private string activityTitle = "No live Activity";
    private string captureStatus = "CAPTURE: Stopped";
    private DesktopPermissionState admissionCapturePermissionState =
        DesktopPermissionState.Unsupported;
    private DesktopPermissionState admissionInputPermissionState =
        DesktopPermissionState.Unsupported;
    private DesktopPermissionState capturePermissionState =
        DesktopPermissionState.Unsupported;
    private string capturePermissionDescription =
        "Native capture permission is unavailable in this platform build.";
    private string capturePermissionRecoveryAction =
        "Install a Flowspan build with the matching native capture adapter.";
    private string capturePermissionStatus = "CAPTURE PERMISSION UNAVAILABLE";
    private string driverStatus = "DRIVER: None";
    private int emergencyStopAttempted;
    private ActivityId? emergencyStoppedActivityId;
    private int emergencyStopPresentationResetRequired;
    private long? emergencyStoppedRevision;
    private string emergencyStopDescription =
        "No local Emergency Stop result has been recorded.";
    private bool emergencyStopFullyConfirmed;
    private string emergencyStopHelpText =
        "Unavailable while no Remote Window session is active.";
    private string emergencyStopStatus = "EMERGENCY STOP NOT REQUIRED";
    private string fallbackDescription =
        "Select one local Activity and one authenticated target to review Remote Window.";
    private RemoteWindowCommandStatus? fallbackFailureStatus;
    private ActivityId? retryResetCleanupActivityId;
    private long? retryResetCleanupRevision;
    private DesktopActivitySnapshot? fallbackSessionActivity;
    private ActivityId? fallbackSessionActivityId;
    private MirrorParticipantRole? fallbackSessionRole;
    private DesktopActivityTargetSnapshot? fallbackSessionTarget;
    private string fallbackStatus =
        "REMOTE WINDOW FALLBACK — SELECT ACTIVITY AND TARGET";
    private DesktopActivitySnapshot? fallbackActivity;
    private DesktopActivityTargetSnapshot? fallbackTarget;
    private DesktopActivitySnapshot? inFlightFallbackActivity;
    private MirrorParticipantRole? inFlightFallbackRole;
    private DesktopActivityTargetSnapshot? inFlightFallbackTarget;
    private bool isDetailVisible;
    private bool hasAcknowledgedCapturePermissionReview;
    private bool hasAcknowledgedInputPermissionReview;
    private DesktopPermissionState inputPermissionState =
        DesktopPermissionState.Unsupported;
    private string inputPermissionDescription =
        "Native input permission is unavailable in this platform build.";
    private string inputPermissionRecoveryAction =
        "Install a Flowspan build with the matching native input adapter.";
    private string inputPermissionStatus = "INPUT PERMISSION UNAVAILABLE";
    private bool isCapturePermissionReviewVisible;
    private bool isEmergencyStopAvailable;
    private bool isInputPermissionReviewVisible;
    private bool isFallbackBusy;
    private DesktopSemanticResumeAvailability fallbackSemanticResumeAvailability =
        DesktopSemanticResumeAvailability.Unavailable;
    private bool isLocalResetBusy;
    private bool isPermissionBusy;
    private bool isRemoteDrivingEnabled;
    private bool observedInactiveAfterEmergencyStop;
    private bool safetySessionMayExist;
    private MirrorParticipantRole? safetySessionRole;
    private bool safetyInactiveBoundaryObserved;
    private long safetyGeneration;
    private long safetyControllerGeneration = -1;
    private SnapshotReducerState snapshotReducer = new(
        Version: 0,
        ControllerGeneration: -1,
        ActivityId: null,
        Lifecycle: null,
        Revision: -1,
        InactiveRevisionResetActivityId: null,
        LastAcceptedSnapshot: null,
        IsServiceStateUnavailable: false);
    private long latestStartRequestTicket;
    private long latestServiceObservationTicket;
    private long permissionChangeGeneration;
    private int permissionSnapshotReadsInFlight;
    private TaskCompletionSource<bool> permissionSnapshotReadsDrained =
        CreateCompletedSignal();
    private int serviceOperationsInFlight;
    private TaskCompletionSource<bool> serviceOperationsDrained =
        CreateCompletedSignal();
    private string localResetDescription =
        "No local Remote Window reset is required.";
    private string localResetStatus = "LOCAL RESET NOT REQUIRED";
    private string participantStatus = "PARTICIPANTS: 0";
    private string protectionStatus = "PROTECTION: Unknown";
    private string revisionStatus = "REVISION: 0";
    private int remoteDrivingSafetyDisableRequired;
    private string sharingAutomationName = "Sharing state: Not sharing";
    private string sharingDescription = "No peer can view or drive this device.";
    private string sharingStatus = "NOT SHARING";
    private bool serviceAvailable;
    private readonly TaskCompletionSource<bool> disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> failCloseCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposalCleanupStarted;
    private int disposed;
    private Exception? failCloseFailure;

    private sealed record EmergencyStopBoundaryOutcome(
        RemoteWindowEmergencyStopResult? Result,
        ActivityId? ActivityAtInvocation,
        long SafetyGeneration,
        bool Invoked);

    private sealed record SnapshotReducerState(
        long Version,
        long ControllerGeneration,
        ActivityId? ActivityId,
        RemoteWindowLifecycle? Lifecycle,
        long Revision,
        ActivityId? InactiveRevisionResetActivityId,
        RemoteWindowSharingSnapshot? LastAcceptedSnapshot,
        bool IsServiceStateUnavailable);

    private sealed record SnapshotProjection(
        long Version,
        RemoteWindowSharingSnapshot? Snapshot,
        RemoteWindowLifecycle? PreviousLifecycle);

    private ActivityId? inactiveRevisionResetActivityId =>
        snapshotReducer.InactiveRevisionResetActivityId;

    private bool isServiceStateUnavailable =>
        snapshotReducer.IsServiceStateUnavailable;

    private RemoteWindowSharingSnapshot? lastAcceptedSnapshot =>
        snapshotReducer.LastAcceptedSnapshot;

    private ActivityId? observedActivityId => snapshotReducer.ActivityId;

    private long observedControllerGeneration =>
        snapshotReducer.ControllerGeneration;

    private RemoteWindowLifecycle? observedLifecycle => snapshotReducer.Lifecycle;

    private long observedRevision => snapshotReducer.Revision;

    public RemoteWindowWorkspaceViewModel(
        IDesktopRemoteWindowService service,
        IDesktopUiDispatcher? dispatcher = null,
        IDesktopRemoteWindowPermissionService? permissionService = null)
    {
        this.service = service
            ?? throw new ArgumentNullException(nameof(service));
        this.dispatcher = dispatcher ?? InlineDesktopUiDispatcher.Instance;
        this.permissionService = permissionService
            ?? UnavailableDesktopRemoteWindowPermissionService.Instance;
        emergencyStopCommand = new RelayCommand(
            EmergencyStop,
            () => IsEmergencyStopAvailable);
        reviewCapturePermissionCommand = new RelayCommand(
            OpenCapturePermissionReview,
            CanReviewCapturePermission);
        cancelCapturePermissionReviewCommand = new RelayCommand(
            CancelCapturePermissionReview,
            () => IsCapturePermissionReviewVisible && !IsPermissionBusy);
        requestCapturePermissionCommand = new AsyncRelayCommand(
            () => RequestCapturePermissionAsync(),
            CanRequestCapturePermission);
        cancelInputPermissionReviewCommand = new RelayCommand(
            CancelInputPermissionReview,
            () => IsInputPermissionReviewVisible && !IsPermissionBusy);
        requestInputPermissionCommand = new AsyncRelayCommand(
            () => RequestInputPermissionAsync(),
            CanRequestInputPermission);
        resetRemoteWindowCommand = new AsyncRelayCommand(
            () => ResetRemoteWindowAsync(),
            CanResetRemoteWindow);
        startRemoteWindowCommand = new AsyncRelayCommand(
            () => StartRemoteWindowAsync(),
            CanStartRemoteWindow);
        service.Changed += OnServiceChanged;
        this.permissionService.Changed += OnPermissionServiceChanged;
        RefreshPermissionState();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ActivityId
    {
        get => activityId;
        private set => SetProperty(ref activityId, value);
    }

    public string ActivityTitle
    {
        get => activityTitle;
        private set => SetProperty(ref activityTitle, value);
    }

    public string CaptureStatus
    {
        get => captureStatus;
        private set => SetProperty(ref captureStatus, value);
    }

    public bool CanEnableRemoteDriving =>
        capturePermissionState == DesktopPermissionState.Granted
        && inputPermissionState is not DesktopPermissionState.Unsupported
            and not DesktopPermissionState.Unavailable
            and not DesktopPermissionState.Denied
            and not DesktopPermissionState.Revoked
        && !IsPermissionBusy;

    public ICommand CancelCapturePermissionReviewCommand =>
        cancelCapturePermissionReviewCommand;

    public ICommand CancelInputPermissionReviewCommand =>
        cancelInputPermissionReviewCommand;

    public string CapturePermissionDescription
    {
        get => capturePermissionDescription;
        private set => SetProperty(ref capturePermissionDescription, value);
    }

    public string CapturePermissionRecoveryAction
    {
        get => capturePermissionRecoveryAction;
        private set => SetProperty(ref capturePermissionRecoveryAction, value);
    }

    public string CapturePermissionStatus
    {
        get => capturePermissionStatus;
        private set => SetProperty(ref capturePermissionStatus, value);
    }

    public string DriverStatus
    {
        get => driverStatus;
        private set => SetProperty(ref driverStatus, value);
    }

    public ICommand EmergencyStopCommand => emergencyStopCommand;

    public string EmergencyStopDescription
    {
        get => emergencyStopDescription;
        private set => SetProperty(ref emergencyStopDescription, value);
    }

    public string EmergencyStopHelpText
    {
        get => emergencyStopHelpText;
        private set => SetProperty(ref emergencyStopHelpText, value);
    }

    public string EmergencyStopStatus
    {
        get => emergencyStopStatus;
        private set => SetProperty(ref emergencyStopStatus, value);
    }

    public string FallbackDescription
    {
        get => fallbackDescription;
        private set => SetProperty(ref fallbackDescription, value);
    }

    public string FallbackStatus
    {
        get => fallbackStatus;
        private set => SetProperty(ref fallbackStatus, value);
    }

    public string FallbackStartAutomationName =>
        (isFallbackBusy ? inFlightFallbackRole : null)
            is MirrorParticipantRole.DriverEligible
        || !isFallbackBusy && IsRemoteDrivingEnabled
        ? "Start driver-enabled Remote Window for the selected Activity"
        : "Start view-only Remote Window for the selected Activity";

    public bool HasAcknowledgedCapturePermissionReview
    {
        get => hasAcknowledgedCapturePermissionReview;
        set
        {
            if (SetProperty(ref hasAcknowledgedCapturePermissionReview, value))
            {
                NotifyPermissionCommandStates();
            }
        }
    }

    public bool HasAcknowledgedInputPermissionReview
    {
        get => hasAcknowledgedInputPermissionReview;
        set
        {
            if (SetProperty(ref hasAcknowledgedInputPermissionReview, value))
            {
                NotifyPermissionCommandStates();
            }
        }
    }

    public string InputPermissionDescription
    {
        get => inputPermissionDescription;
        private set => SetProperty(ref inputPermissionDescription, value);
    }

    public string InputPermissionRecoveryAction
    {
        get => inputPermissionRecoveryAction;
        private set => SetProperty(ref inputPermissionRecoveryAction, value);
    }

    public string InputPermissionStatus
    {
        get => inputPermissionStatus;
        private set => SetProperty(ref inputPermissionStatus, value);
    }

    public bool IsCapturePermissionReviewVisible
    {
        get => isCapturePermissionReviewVisible;
        private set
        {
            if (SetProperty(ref isCapturePermissionReviewVisible, value))
            {
                OnPropertyChanged(nameof(IsCapturePermissionReviewActionVisible));
                NotifyPermissionCommandStates();
            }
        }
    }

    public bool IsCapturePermissionReviewActionVisible =>
        !IsCapturePermissionReviewVisible
        && capturePermissionState == DesktopPermissionState.NotDetermined;

    public bool IsDetailVisible
    {
        get => isDetailVisible;
        private set => SetProperty(ref isDetailVisible, value);
    }

    public bool IsEmergencyStopAvailable
    {
        get => isEmergencyStopAvailable;
        private set
        {
            if (SetProperty(ref isEmergencyStopAvailable, value))
            {
                emergencyStopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsInputPermissionReviewVisible
    {
        get => isInputPermissionReviewVisible;
        private set
        {
            if (SetProperty(ref isInputPermissionReviewVisible, value))
            {
                NotifyPermissionCommandStates();
            }
        }
    }

    public bool IsLocalResetAvailable => CanResetRemoteWindow();

    public string LocalResetDescription
    {
        get => localResetDescription;
        private set => SetProperty(ref localResetDescription, value);
    }

    public string LocalResetStatus
    {
        get => localResetStatus;
        private set => SetProperty(ref localResetStatus, value);
    }

    public bool IsFallbackStartAvailable => CanStartRemoteWindow();

    public bool IsPermissionBusy
    {
        get => isPermissionBusy;
        private set
        {
            if (SetProperty(ref isPermissionBusy, value))
            {
                OnPropertyChanged(nameof(CanEnableRemoteDriving));
                NotifyPermissionCommandStates();
                UpdateFallbackPresentation();
            }
        }
    }

    public bool IsRemoteDrivingEnabled
    {
        get => isRemoteDrivingEnabled;
        set
        {
            if (value && !CanEnableRemoteDriving)
            {
                return;
            }

            if (!SetProperty(ref isRemoteDrivingEnabled, value))
            {
                return;
            }

            if (value && inputPermissionState != DesktopPermissionState.Granted)
            {
                IsInputPermissionReviewVisible = true;
            }
            else if (!value)
            {
                HasAcknowledgedInputPermissionReview = false;
                IsInputPermissionReviewVisible = false;
            }

            NotifyPermissionCommandStates();
            fallbackFailureStatus = null;
            UpdateFallbackPresentation();
        }
    }

    public string ParticipantStatus
    {
        get => participantStatus;
        private set => SetProperty(ref participantStatus, value);
    }

    public ICommand RequestCapturePermissionCommand =>
        requestCapturePermissionCommand;

    public ICommand RequestInputPermissionCommand => requestInputPermissionCommand;

    public ICommand ResetRemoteWindowCommand => resetRemoteWindowCommand;

    public ICommand ReviewCapturePermissionCommand =>
        reviewCapturePermissionCommand;

    public ICommand StartRemoteWindowCommand => startRemoteWindowCommand;

    public string ProtectionStatus
    {
        get => protectionStatus;
        private set => SetProperty(ref protectionStatus, value);
    }

    public string RevisionStatus
    {
        get => revisionStatus;
        private set => SetProperty(ref revisionStatus, value);
    }

    public string SharingAutomationName
    {
        get => sharingAutomationName;
        private set => SetProperty(ref sharingAutomationName, value);
    }

    public string SharingDescription
    {
        get => sharingDescription;
        private set => SetProperty(ref sharingDescription, value);
    }

    public string SharingStatus
    {
        get => sharingStatus;
        private set => SetProperty(ref sharingStatus, value);
    }

    public void SetFallbackSelection(
        DesktopActivitySnapshot? activity,
        DesktopActivityTargetSnapshot? target,
        DesktopSemanticResumeAvailability semanticResumeAvailability =
            DesktopSemanticResumeAvailability.Unavailable)
    {
        lock (fallbackAdmissionGate)
        {
            lock (serviceBoundaryGate)
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref disposed) != 0,
                    this);
                if (Equals(fallbackActivity, activity)
                    && Equals(fallbackTarget, target)
                    && fallbackSemanticResumeAvailability
                        == semanticResumeAvailability)
                {
                    return;
                }

                fallbackActivity = activity;
                fallbackTarget = target;
                fallbackSemanticResumeAvailability = semanticResumeAvailability;
                fallbackFailureStatus = null;
            }

            UpdateFallbackPresentation();
        }
    }

    public ValueTask DisposeAsync()
    {
        bool calledFromExternalBoundary = externalBoundaryCallScope.Value is
        { IsActive: true, Owner: var owner }
        && ReferenceEquals(owner, this);
        FailCloseForOwnerDisposal();
        if (Interlocked.CompareExchange(ref disposalCleanupStarted, 1, 0) == 0)
        {
            _ = CompleteDisposalAsync();
        }

        return calledFromExternalBoundary
            ? ValueTask.CompletedTask
            : new ValueTask(disposalCompletion.Task);
    }

    internal void FailCloseForOwnerDisposal()
    {
        if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        try
        {
            StopRemoteWindowForDisposal(failures);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            failCloseFailure = failures.Count switch
            {
                0 => null,
                1 => failures[0],
                _ => new AggregateException(
                    "The Remote Window fail-close boundary reported multiple failures.",
                    failures),
            };
            failCloseCompleted.TrySetResult(true);
        }
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            await failCloseCompleted.Task.ConfigureAwait(false);
            await DisposeOwnedResourcesAsync().ConfigureAwait(false);
            disposalCompletion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
        }
    }

    private async Task DisposeOwnedResourcesAsync()
    {
        var failures = new List<Exception>();
        if (failCloseFailure is not null)
        {
            failures.Add(failCloseFailure);
        }

        CaptureFailure(failures, permissionLifetimeCancellation.Cancel);
        CaptureFailure(failures, () =>
        {
            using ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall();
            service.Changed -= OnServiceChanged;
        });
        CaptureFailure(
            failures,
            () =>
            {
                using ExternalBoundaryCallLease boundaryCall =
                    EnterExternalBoundaryCall();
                permissionService.Changed -= OnPermissionServiceChanged;
            });

        bool enteredRemoteWindowGate = false;
        try
        {
            await remoteWindowGate.WaitAsync().ConfigureAwait(false);
            enteredRemoteWindowGate = true;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        Task pendingServiceOperations;
        lock (serviceBoundaryGate)
        {
            pendingServiceOperations = serviceOperationsDrained.Task;
        }

        await CaptureFailureAsync(
            failures,
            () => new ValueTask(pendingServiceOperations))
            .ConfigureAwait(false);
        await CaptureFailureAsync(failures, DisposeServiceBoundaryAsync)
            .ConfigureAwait(false);

        bool enteredPermissionGate = false;
        try
        {
            await permissionGate.WaitAsync().ConfigureAwait(false);
            enteredPermissionGate = true;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        Task pendingPermissionSnapshotReads;
        lock (serviceBoundaryGate)
        {
            pendingPermissionSnapshotReads = permissionSnapshotReadsDrained.Task;
        }

        await CaptureFailureAsync(
            failures,
            () => new ValueTask(pendingPermissionSnapshotReads))
            .ConfigureAwait(false);
        await CaptureFailureAsync(failures, DisposePermissionBoundaryAsync)
            .ConfigureAwait(false);
        if (enteredPermissionGate)
        {
            CaptureFailure(failures, () =>
            {
                _ = permissionGate.Release();
            });
        }

        if (enteredRemoteWindowGate)
        {
            CaptureFailure(failures, () =>
            {
                _ = remoteWindowGate.Release();
            });
        }

        CaptureFailure(failures, remoteWindowGate.Dispose);
        CaptureFailure(failures, permissionGate.Dispose);
        CaptureFailure(failures, permissionLifetimeCancellation.Dispose);
        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private void StopRemoteWindowForDisposal(List<Exception> failures)
    {
        bool safetyStopRequired;
        lock (serviceBoundaryGate)
        {
            safetyStopRequired = safetySessionMayExist
                || observedLifecycle is
                    RemoteWindowLifecycle.Starting
                    or RemoteWindowLifecycle.Active
                    or RemoteWindowLifecycle.ProtectionPaused
                || isFallbackBusy;
            if (safetyStopRequired)
            {
                Volatile.Write(ref emergencyStopAttempted, 1);
            }
        }

        if (!safetyStopRequired)
        {
            return;
        }

        try
        {
            using ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall();
            RemoteWindowEmergencyStopResult result = service.EmergencyStop();
            if (!result.FullyStopped)
            {
                failures.Add(new InvalidOperationException(
                    "Remote Window Emergency Stop was not fully confirmed during disposal."));
            }
            else
            {
                lock (serviceBoundaryGate)
                {
                    ClearSafetySession();
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(exception);
        }
    }

    private static void CaptureFailure(
        List<Exception> failures,
        Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async ValueTask CaptureFailureAsync(
        List<Exception> failures,
        Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private async ValueTask DisposeServiceBoundaryAsync()
    {
        using ExternalBoundaryCallLease boundaryCall =
            EnterExternalBoundaryCall();
        await service.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask DisposePermissionBoundaryAsync()
    {
        using ExternalBoundaryCallLease boundaryCall =
            EnterExternalBoundaryCall();
        await permissionService.DisposeAsync().ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult(true);
        return signal;
    }

    private bool TryBeginServiceOperation()
    {
        lock (serviceBoundaryGate)
        {
            return TryBeginServiceOperationLocked();
        }
    }

    private bool TryBeginServiceObservation(out long observationTicket)
    {
        lock (serviceBoundaryGate)
        {
            observationTicket = 0;
            if (!TryBeginServiceOperationLocked())
            {
                return false;
            }

            observationTicket = checked(++latestServiceObservationTicket);
            return true;
        }
    }

    private void InvalidateEarlierServiceObservationsLocked() =>
        latestServiceObservationTicket = checked(latestServiceObservationTicket + 1);

    private bool TryBeginServiceOperationLocked()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return false;
        }

        if (serviceOperationsInFlight++ == 0)
        {
            serviceOperationsDrained = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return true;
    }

    private void CompleteServiceOperation()
    {
        lock (serviceBoundaryGate)
        {
            if (--serviceOperationsInFlight == 0)
            {
                serviceOperationsDrained.TrySetResult(true);
            }
        }
    }

    public Task RequestCapturePermissionAsync(
        CancellationToken cancellationToken = default) =>
        RequestPermissionAsync(capture: true, cancellationToken);

    public Task RequestInputPermissionAsync(
        CancellationToken cancellationToken = default) =>
        RequestPermissionAsync(capture: false, cancellationToken);

    public async Task ResetRemoteWindowAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!CanResetRemoteWindow())
        {
            return;
        }

        bool enteredRemoteWindowGate = false;
        bool resetBoundaryInvoked = false;
        bool resetBoundaryReturned = false;
        RemoteWindowLifecycle? resetLifecycle = observedLifecycle;
        try
        {
            SetLocalResetBusy(true);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    permissionLifetimeCancellation.Token);
            await remoteWindowGate.WaitAsync(linkedCancellation.Token)
                .ConfigureAwait(true);
            enteredRemoteWindowGate = true;
            resetLifecycle = observedLifecycle;
            if (!(resetLifecycle == RemoteWindowLifecycle.EmergencyStopped
                    && emergencyStopFullyConfirmed)
                && !IsUnavailableResetSafe())
            {
                return;
            }

            resetBoundaryInvoked = true;
            RemoteWindowCommandResult result;
            using (ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall())
            {
                result = await service.ResetAfterLocalConfirmationAsync(
                        linkedCancellation.Token)
                    .ConfigureAwait(true);
            }
            resetBoundaryReturned = true;
            lock (serviceBoundaryGate)
            {
                InvalidateEarlierServiceObservationsLocked();
            }

            ApplySnapshot(
                result.Snapshot,
                allowEqualRevision: true,
                allowLowerRevisionConfirmedIdle: result.Succeeded);
            Refresh();
            bool resetConfirmed = result.Succeeded
                && observedLifecycle is null or RemoteWindowLifecycle.Idle;
            if (resetConfirmed)
            {
                lock (serviceBoundaryGate)
                {
                    ClearSafetySession();
                    ClearRetryResetCleanupProof();
                }

                ResetEmergencyStopPresentationForNewSession();
                LocalResetStatus = resetLifecycle == RemoteWindowLifecycle.Unavailable
                    ? "LOCAL RETRY RESET CONFIRMED"
                    : "LOCAL RESET CONFIRMED";
                LocalResetDescription =
                    "The previous session is idle. Participants, capture, and the prior Driver were not restored.";
            }
            else
            {
                LocalResetStatus = "LOCAL RESET NOT CONFIRMED";
                LocalResetDescription = resetLifecycle
                        == RemoteWindowLifecycle.EmergencyStopped
                    && result.Status == RemoteWindowCommandStatus.BoundaryFailed
                    ? "One or more Emergency Stop boundaries remain unconfirmed. Close Flowspan and inspect local diagnostics before retrying."
                    : "The local reset boundary rejected the current session state. No participant, capture, or Driver authority was restored.";
            }
        }
        catch (OperationCanceledException)
            when (permissionLifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!resetBoundaryInvoked || resetBoundaryReturned)
            {
                throw;
            }

            LocalResetStatus = "LOCAL RESET UNCONFIRMED";
            LocalResetDescription =
                "The local reset boundary did not return a confirmation. No participant, capture, or Driver authority is claimed restored.";
        }
        finally
        {
            try
            {
                SetLocalResetBusy(false);
            }
            finally
            {
                if (enteredRemoteWindowGate)
                {
                    remoteWindowGate.Release();
                }
            }
        }
    }

    public async Task StartRemoteWindowAsync(
        CancellationToken cancellationToken = default)
    {
        DesktopActivitySnapshot activity;
        DesktopActivityTargetSnapshot target;
        MirrorParticipantRole role;
        long requestTicket;
        long admittedSafetyGeneration = -1;
        long admittedControllerGeneration = -1;
        lock (serviceBoundaryGate)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposed) != 0,
                this);
            if (!CanStartRemoteWindow())
            {
                return;
            }

            activity = fallbackActivity!;
            target = fallbackTarget!;
            role = IsRemoteDrivingEnabled
                ? MirrorParticipantRole.DriverEligible
                : MirrorParticipantRole.ViewOnly;
            requestTicket = ++latestStartRequestTicket;
        }

        bool enteredRemoteWindowGate = false;
        bool admissionPublished = false;
        bool startBoundaryInvoked = false;
        bool startBoundaryReturned = false;
        bool startApplied = false;
        bool returnedStartRequiresSafetyStop = false;
        try
        {
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    permissionLifetimeCancellation.Token);
            await remoteWindowGate.WaitAsync(linkedCancellation.Token)
                .ConfigureAwait(true);
            enteredRemoteWindowGate = true;
            if (!TryReadPermissionSnapshot(
                    recordsChange: false,
                    out DesktopRemoteWindowPermissionSnapshot
                        admissionPermissionSnapshot,
                    out long observedPermissionGeneration))
            {
                return;
            }

            lock (serviceBoundaryGate)
            {
                if (observedPermissionGeneration != permissionChangeGeneration)
                {
                    return;
                }

                UpdateAdmissionPermissionState(admissionPermissionSnapshot);
                if (requestTicket != latestStartRequestTicket
                    || !CanAdmitStart(activity, target, role))
                {
                    return;
                }

                fallbackFailureStatus = null;
                inFlightFallbackActivity = activity;
                inFlightFallbackTarget = target;
                inFlightFallbackRole = role;
                fallbackSessionActivity = activity;
                fallbackSessionActivityId = activity.ActivityId;
                fallbackSessionRole = role;
                fallbackSessionTarget = target;
                admissionPublished = true;
                MarkSafetySessionMayExist(role, beginsNewSession: true);
                admittedSafetyGeneration = safetyGeneration;
                admittedControllerGeneration = safetyControllerGeneration;
            }

            RemoteWindowCommandResult result;
            using (ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall())
            {
                ValueTask<RemoteWindowCommandResult> pendingStart;
                lock (fallbackAdmissionGate)
                {
                    SetFallbackBusy(true);
                    lock (serviceBoundaryGate)
                    {
                        if (observedPermissionGeneration != permissionChangeGeneration
                            || requestTicket != latestStartRequestTicket
                            || !CanAdmitStart(activity, target, role))
                        {
                            return;
                        }

                        startBoundaryInvoked = true;
                    }

                    pendingStart = service.StartAsync(
                        activity.ActivityId,
                        target.DeviceId,
                        role,
                        linkedCancellation.Token);
                }

                result = await pendingStart.ConfigureAwait(true);
            }

            startBoundaryReturned = true;
            startApplied = result.Succeeded;
            returnedStartRequiresSafetyStop =
                StartResultRequiresSafetyStop(result);
            linkedCancellation.Token.ThrowIfCancellationRequested();
            if (!RecordStartResult(
                    result,
                    admittedSafetyGeneration,
                    admittedControllerGeneration))
            {
                if (returnedStartRequiresSafetyStop)
                {
                    StopReturnedStartResult(
                        role,
                        admittedSafetyGeneration,
                        admittedControllerGeneration);
                }

                return;
            }

            ApplySnapshot(
                result.Snapshot,
                allowEqualRevision: true,
                controllerGeneration: admittedControllerGeneration,
                expectedSafetyGeneration: admittedSafetyGeneration);
            if (IsCurrentFallbackRequest(activity, target, role))
            {
                fallbackFailureStatus = result.Succeeded ? null : result.Status;
            }

            Refresh();
        }
        catch (OperationCanceledException)
            when (permissionLifetimeCancellation.IsCancellationRequested)
        {
            if (startBoundaryInvoked)
            {
                StopInterruptedStart(
                    startBoundaryReturned,
                    returnedStartRequiresSafetyStop,
                    role,
                    admittedSafetyGeneration,
                    admittedControllerGeneration);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (startBoundaryInvoked)
            {
                StopInterruptedStart(
                    startBoundaryReturned,
                    returnedStartRequiresSafetyStop,
                    role,
                    admittedSafetyGeneration,
                    admittedControllerGeneration);
            }

            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!startBoundaryInvoked)
            {
                throw;
            }

            if (IsCurrentFallbackRequest(activity, target, role))
            {
                fallbackFailureStatus = RemoteWindowCommandStatus.BoundaryFailed;
            }

            StopInterruptedStart(
                startBoundaryReturned,
                returnedStartRequiresSafetyStop,
                role,
                admittedSafetyGeneration,
                admittedControllerGeneration);
        }
        finally
        {
            try
            {
                if (admissionPublished)
                {
                    if (!startBoundaryInvoked
                        || startBoundaryReturned && !startApplied)
                    {
                        ClearFallbackSessionContext();
                    }

                    SetFallbackBusy(false);
                }
            }
            finally
            {
                if (admissionPublished)
                {
                    inFlightFallbackActivity = null;
                    inFlightFallbackTarget = null;
                    inFlightFallbackRole = null;
                }

                if (enteredRemoteWindowGate)
                {
                    remoteWindowGate.Release();
                }
            }
        }
    }

    private void StopInterruptedStart(
        bool startBoundaryReturned,
        bool returnedStartRequiresSafetyStop,
        MirrorParticipantRole role,
        long admittedSafetyGeneration,
        long admittedControllerGeneration)
    {
        if (startBoundaryReturned)
        {
            if (returnedStartRequiresSafetyStop)
            {
                StopReturnedStartResult(
                    role,
                    admittedSafetyGeneration,
                    admittedControllerGeneration);
            }

            return;
        }

        StopUnconfirmedStart(
            admittedSafetyGeneration,
            admittedControllerGeneration);
    }

    private void StopReturnedStartResult(
        MirrorParticipantRole role,
        long admittedSafetyGeneration,
        long admittedControllerGeneration)
    {
        lock (serviceBoundaryGate)
        {
            bool sameAdmittedSession =
                safetyGeneration == admittedSafetyGeneration;
            bool directInactiveBoundary = safetyInactiveBoundaryObserved
                && admittedSafetyGeneration < long.MaxValue
                && safetyGeneration == admittedSafetyGeneration + 1;
            if (Volatile.Read(ref disposed) != 0
                || admittedControllerGeneration != safetyControllerGeneration
                || !sameAdmittedSession && !directInactiveBoundary)
            {
                return;
            }

            MarkSafetySessionMayExist(role, beginsNewSession: true);
        }

        Volatile.Write(ref emergencyStopAttempted, 1);
        EmergencyStopBoundaryOutcome outcome = InvokeEmergencyStopBoundary();
        if (outcome.Invoked)
        {
            ApplyEmergencyStopOutcome(outcome);
        }
    }

    private void StopUnconfirmedStart(
        long admittedSafetyGeneration,
        long admittedControllerGeneration)
    {
        if (IsCurrentStartGeneration(
                admittedSafetyGeneration,
                admittedControllerGeneration))
        {
            EmergencyStop(allowAdmittedStart: true);
        }
    }

    private bool IsCurrentStartGeneration(
        long admittedSafetyGeneration,
        long admittedControllerGeneration)
    {
        lock (serviceBoundaryGate)
        {
            return admittedSafetyGeneration == safetyGeneration
                && admittedControllerGeneration == safetyControllerGeneration;
        }
    }

    private void EmergencyStop() => EmergencyStop(allowAdmittedStart: false);

    private void EmergencyStop(bool allowAdmittedStart)
    {
        if (!TryBeginEmergencyStop(allowAdmittedStart))
        {
            return;
        }

        EmergencyStopBoundaryOutcome outcome = InvokeEmergencyStopBoundary();
        if (outcome.Invoked)
        {
            ApplyEmergencyStopOutcome(outcome);
        }
    }

    private bool TryBeginEmergencyStop(bool allowAdmittedStart)
    {
        bool admittedSessionMayExist;
        lock (serviceBoundaryGate)
        {
            admittedSessionMayExist = safetySessionMayExist;
        }

        if (!IsEmergencyStopAvailable
            && !(allowAdmittedStart && admittedSessionMayExist))
        {
            return false;
        }

        return Interlocked.CompareExchange(ref emergencyStopAttempted, 1, 0) == 0;
    }

    private EmergencyStopBoundaryOutcome InvokeEmergencyStopBoundary()
    {
        ActivityId? activityAtInvocation;
        long generationAtInvocation;
        lock (serviceBoundaryGate)
        {
            activityAtInvocation = observedActivityId;
            generationAtInvocation = safetyGeneration;
            if (Volatile.Read(ref disposed) != 0)
            {
                return new EmergencyStopBoundaryOutcome(
                    null,
                    activityAtInvocation,
                    generationAtInvocation,
                    Invoked: false);
            }
        }

        if (!TryBeginServiceOperation())
        {
            return new EmergencyStopBoundaryOutcome(
                null,
                activityAtInvocation,
                generationAtInvocation,
                Invoked: false);
        }

        try
        {
            using ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall();
            RemoteWindowEmergencyStopResult result = service.EmergencyStop();
            lock (serviceBoundaryGate)
            {
                if (generationAtInvocation == safetyGeneration)
                {
                    InvalidateEarlierServiceObservationsLocked();
                    if (result.FullyStopped)
                    {
                        ClearSafetySession();
                    }
                }
            }

            return new EmergencyStopBoundaryOutcome(
                result,
                activityAtInvocation,
                generationAtInvocation,
                Invoked: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new EmergencyStopBoundaryOutcome(
                null,
                activityAtInvocation,
                generationAtInvocation,
                Invoked: true);
        }
        finally
        {
            CompleteServiceOperation();
        }
    }

    private void ApplyEmergencyStopOutcome(EmergencyStopBoundaryOutcome outcome)
    {
        lock (presentationGate)
        {
            lock (serviceBoundaryGate)
            {
                if (outcome.SafetyGeneration != safetyGeneration)
                {
                    return;
                }

                Volatile.Write(ref emergencyStopPresentationResetRequired, 0);
                emergencyStopFullyConfirmed = false;
                emergencyStoppedActivityId = outcome.ActivityAtInvocation;
                emergencyStoppedRevision = null;
                observedInactiveAfterEmergencyStop = false;
                isEmergencyStopAvailable = false;
                if (outcome.Result is { } result)
                {
                    emergencyStopFullyConfirmed = result.FullyStopped;
                    emergencyStoppedActivityId = result.Snapshot.ActivityId;
                    emergencyStoppedRevision = result.Snapshot.Revision;
                    emergencyStopStatus = result.FullyStopped
                        ? "EMERGENCY STOP CONFIRMED"
                        : "EMERGENCY STOP PARTIALLY UNCONFIRMED";
                    emergencyStopDescription =
                        $"Capture: {ToConfirmation(result.CaptureBoundary)}. "
                        + $"Input: {ToConfirmation(result.InputBoundary)}. "
                        + $"Sessions: {ToConfirmation(result.SessionBoundary)}.";
                }
                else
                {
                    emergencyStopStatus = "EMERGENCY STOP UNCONFIRMED";
                    emergencyStopDescription =
                        "The local stop boundary did not return a confirmation. "
                        + "Capture, input, and sessions are unconfirmed. Close Flowspan "
                        + "and inspect local diagnostics before any reset.";
                }
            }
        }

        try
        {
            if (outcome.Result is { } result)
            {
                ApplySnapshot(
                    result.Snapshot,
                    allowEqualRevision: true,
                    expectedSafetyGeneration: outcome.SafetyGeneration);
            }

            lock (serviceBoundaryGate)
            {
                if (outcome.SafetyGeneration != safetyGeneration)
                {
                    return;
                }
            }

            Refresh();
            lock (presentationGate)
            {
                lock (serviceBoundaryGate)
                {
                    if (outcome.SafetyGeneration != safetyGeneration)
                    {
                        return;
                    }
                }

                UpdateLocalResetPresentation(observedLifecycle, observedLifecycle);
            }
        }
        finally
        {
            lock (presentationGate)
            {
                bool currentGeneration;
                lock (serviceBoundaryGate)
                {
                    currentGeneration =
                        outcome.SafetyGeneration == safetyGeneration;
                    if (currentGeneration)
                    {
                        isEmergencyStopAvailable = false;
                        emergencyStopHelpText =
                            "Unavailable while no stoppable Remote Window session is active.";
                    }
                }

                if (currentGeneration)
                {
                    OnPropertyChanged(nameof(EmergencyStopStatus));
                    OnPropertyChanged(nameof(EmergencyStopDescription));
                    OnPropertyChanged(nameof(IsEmergencyStopAvailable));
                    OnPropertyChanged(nameof(EmergencyStopHelpText));
                    emergencyStopCommand.NotifyCanExecuteChanged();
                }
            }
        }
    }

    private void OnServiceChanged()
    {
        if (!TryBeginServiceObservation(out long observationTicket))
        {
            return;
        }

        bool available = false;
        string unavailableReasonCode = "service_state_unavailable";
        RemoteWindowSharingSnapshot? snapshot = null;
        long? controllerGeneration = null;
        try
        {
            using ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall();
            available = service.IsAvailable;
            controllerGeneration = service.ControllerGeneration;
            if (available)
            {
                snapshot = service.GetSnapshot();
            }
            else
            {
                unavailableReasonCode = service.UnavailableReasonCode;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            available = false;
            unavailableReasonCode = "service_state_unavailable";
        }
        finally
        {
            CompleteServiceOperation();
        }

        EmergencyStopBoundaryOutcome? stopOutcome = null;
        bool safetyStopRequired = false;
        SnapshotProjection? snapshotProjection = null;
        long? unavailableProjectionVersion = null;
        lock (serviceBoundaryGate)
        {
            if (Volatile.Read(ref disposed) != 0
                || observationTicket != latestServiceObservationTicket)
            {
                return;
            }

            if (controllerGeneration is { } generation
                && !TryObserveSafetyControllerGeneration(generation))
            {
                return;
            }

            serviceAvailable = available;
            snapshotProjection = available
                ? ReduceSnapshot(
                    snapshot,
                    allowEqualRevision: false,
                    allowLowerRevisionConfirmedIdle: false,
                    controllerGeneration,
                    expectedSafetyGeneration: null)
                : null;
            if (snapshotProjection is not null)
            {
                ObserveAuthoritativeSessionForEmergencyStop(snapshot);
                if (snapshot is not null)
                {
                    bool inputPermissionBlocksDriver =
                        admissionInputPermissionState
                            != DesktopPermissionState.Granted
                        && SafetyDriverEligibleSessionMayExist();
                    safetyStopRequired = admissionCapturePermissionState
                            != DesktopPermissionState.Granted
                        && safetySessionMayExist
                        || inputPermissionBlocksDriver;
                }
            }

            unavailableProjectionVersion = available
                ? null
                : ReduceUnavailable();
        }

        if (safetyStopRequired
            && TryBeginEmergencyStop(allowAdmittedStart: true))
        {
            EmergencyStopBoundaryOutcome outcome = InvokeEmergencyStopBoundary();
            if (outcome.Invoked)
            {
                stopOutcome = outcome;
            }
        }

        dispatcher.Post(() =>
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                if (stopOutcome is not null)
                {
                    ApplyEmergencyStopOutcome(stopOutcome);
                }

                if (snapshotProjection is not null)
                {
                    ProjectSnapshot(
                        snapshotProjection.Version,
                        snapshotProjection.Snapshot,
                        snapshotProjection.PreviousLifecycle);
                }
                else if (unavailableProjectionVersion is { } version)
                {
                    ProjectUnavailable(version, unavailableReasonCode);
                }
            }
        });
    }

    private void OnPermissionServiceChanged()
    {
        if (!TryReadPermissionSnapshot(
                recordsChange: true,
                out DesktopRemoteWindowPermissionSnapshot snapshot,
                out long changeGeneration,
                preferCachedSnapshot: true))
        {
            return;
        }

        EmergencyStopBoundaryOutcome? stopOutcome = null;
        bool safetyStopRequired;
        lock (serviceBoundaryGate)
        {
            if (Volatile.Read(ref disposed) != 0
                || changeGeneration != permissionChangeGeneration)
            {
                return;
            }

            DesktopPermissionState capture =
                NormalizePermissionState(snapshot.Capture);
            DesktopPermissionState input = NormalizePermissionState(snapshot.Input);
            UpdateAdmissionPermissionState(snapshot);
            if (capture != DesktopPermissionState.Granted
                || input != DesktopPermissionState.Granted
                    && SafetyDriverEligibleSessionMayExist())
            {
                Volatile.Write(ref remoteDrivingSafetyDisableRequired, 1);
            }

            safetyStopRequired = PermissionSnapshotRequiresSafetyStop(snapshot);
        }

        if (safetyStopRequired
            && TryBeginEmergencyStop(allowAdmittedStart: true))
        {
            EmergencyStopBoundaryOutcome outcome = InvokeEmergencyStopBoundary();
            if (outcome.Invoked)
            {
                stopOutcome = outcome;
            }
        }

        dispatcher.Post(() =>
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                if (stopOutcome is not null)
                {
                    ApplyEmergencyStopOutcome(stopOutcome);
                }

                if (changeGeneration
                    == Volatile.Read(ref permissionChangeGeneration))
                {
                    ApplyPermissionSnapshot(snapshot);
                }
            }
        });
    }

    private async Task RequestPermissionAsync(
        bool capture,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                permissionLifetimeCancellation.Token);
        bool enteredGate = false;
        bool markedBusy = false;
        bool permissionBoundaryInvoked = false;
        bool permissionBoundaryReturned = false;
        ExternalBoundaryCallLease? callbackDrainExclusion = null;
        try
        {
            await permissionGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            callbackDrainExclusion = EnterExternalBoundaryCall();
            if (capture ? !CanRequestCapturePermission() : !CanRequestInputPermission())
            {
                return;
            }

            markedBusy = true;
            IsPermissionBusy = true;
            permissionBoundaryInvoked = true;
            if (capture)
            {
                _ = await permissionService.RequestCapturePermissionAsync(
                    linkedCancellation.Token).ConfigureAwait(true);
            }
            else
            {
                _ = await permissionService.RequestInputPermissionAsync(
                    linkedCancellation.Token).ConfigureAwait(true);
            }

            permissionBoundaryReturned = true;
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            RefreshPermissionState();
            if (capture)
            {
                HasAcknowledgedCapturePermissionReview = false;
                IsCapturePermissionReviewVisible = false;
            }
            else
            {
                HasAcknowledgedInputPermissionReview = false;
                IsInputPermissionReviewVisible = false;
            }
        }
        catch (OperationCanceledException)
            when (permissionLifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
        }
        catch (Exception exception)
            when (Volatile.Read(ref disposed) != 0
                && exception is not OutOfMemoryException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!permissionBoundaryInvoked || permissionBoundaryReturned)
            {
                throw;
            }

            ApplyPermissionRequestFailure(capture);
        }
        finally
        {
            try
            {
                if (markedBusy)
                {
                    if (Volatile.Read(ref disposed) == 0)
                    {
                        IsPermissionBusy = false;
                    }
                    else
                    {
                        isPermissionBusy = false;
                    }
                }
            }
            finally
            {
                try
                {
                    if (enteredGate)
                    {
                        permissionGate.Release();
                    }
                }
                finally
                {
                    callbackDrainExclusion?.Dispose();
                }
            }
        }
    }

    private bool CanReviewCapturePermission() =>
        Volatile.Read(ref disposed) == 0
        && !IsPermissionBusy
        && !IsCapturePermissionReviewVisible
        && capturePermissionState == DesktopPermissionState.NotDetermined;

    private bool CanRequestCapturePermission() =>
        Volatile.Read(ref disposed) == 0
        && !IsPermissionBusy
        && IsCapturePermissionReviewVisible
        && HasAcknowledgedCapturePermissionReview
        && capturePermissionState == DesktopPermissionState.NotDetermined;

    private bool CanRequestInputPermission() =>
        Volatile.Read(ref disposed) == 0
        && !IsPermissionBusy
        && IsRemoteDrivingEnabled
        && IsInputPermissionReviewVisible
        && HasAcknowledgedInputPermissionReview
        && capturePermissionState == DesktopPermissionState.Granted
        && inputPermissionState == DesktopPermissionState.NotDetermined;

    private bool CanResetRemoteWindow() =>
        Volatile.Read(ref disposed) == 0
        && serviceAvailable
        && !isLocalResetBusy
        && !isFallbackBusy
        && (emergencyStopFullyConfirmed
                && observedLifecycle == RemoteWindowLifecycle.EmergencyStopped
            || IsUnavailableResetSafe());

    private bool IsUnavailableResetSafe() =>
        observedLifecycle == RemoteWindowLifecycle.Unavailable
        && lastAcceptedSnapshot is { } snapshot
        && IsRetryResetCleanupProof(snapshot)
        && IsStoppedUnavailableWithoutRemoteAuthority(snapshot);

    private bool CanStartRemoteWindow() =>
        Volatile.Read(ref disposed) == 0
        && serviceAvailable
        && !isFallbackBusy
        && fallbackSemanticResumeAvailability
            == DesktopSemanticResumeAvailability.Unavailable
        && !IsPermissionBusy
        && fallbackActivity is { Lifecycle: ActivityLifecycle.Active }
        && fallbackTarget is not null
        && capturePermissionState == DesktopPermissionState.Granted
        && (!IsRemoteDrivingEnabled
            || inputPermissionState == DesktopPermissionState.Granted)
        && observedLifecycle is null or RemoteWindowLifecycle.Idle;

    private void OpenCapturePermissionReview()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        HasAcknowledgedCapturePermissionReview = false;
        IsCapturePermissionReviewVisible = true;
    }

    private void CancelCapturePermissionReview()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        HasAcknowledgedCapturePermissionReview = false;
        IsCapturePermissionReviewVisible = false;
    }

    private void CancelInputPermissionReview()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        IsRemoteDrivingEnabled = false;
    }

    private void RefreshPermissionState()
    {
        if (!TryReadPermissionSnapshot(
                recordsChange: false,
                out DesktopRemoteWindowPermissionSnapshot snapshot,
                out long observedPermissionGeneration))
        {
            return;
        }

        lock (serviceBoundaryGate)
        {
            if (Volatile.Read(ref disposed) != 0
                || observedPermissionGeneration != permissionChangeGeneration)
            {
                return;
            }

            UpdateAdmissionPermissionState(snapshot);
        }

        if (Volatile.Read(ref disposed) == 0
            && observedPermissionGeneration
                == Volatile.Read(ref permissionChangeGeneration))
        {
            ApplyPermissionSnapshot(snapshot);
        }
    }

    private void UpdateAdmissionPermissionState(
        DesktopRemoteWindowPermissionSnapshot snapshot)
    {
        admissionCapturePermissionState =
            NormalizePermissionState(snapshot.Capture);
        admissionInputPermissionState = NormalizePermissionState(snapshot.Input);
    }

    private DesktopRemoteWindowPermissionSnapshot ReadPermissionSnapshot(
        bool preferCachedSnapshot)
    {
        try
        {
            if (preferCachedSnapshot
                && permissionService.TryGetCachedSnapshot(
                    out DesktopRemoteWindowPermissionSnapshot cachedSnapshot)
                && cachedSnapshot is not null)
            {
                return cachedSnapshot;
            }

            return permissionService.GetSnapshot();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new DesktopRemoteWindowPermissionSnapshot(
                DesktopPermissionState.Unavailable,
                DesktopPermissionState.Unavailable);
        }
    }

    private bool TryReadPermissionSnapshot(
        bool recordsChange,
        out DesktopRemoteWindowPermissionSnapshot snapshot,
        out long observedGeneration,
        bool preferCachedSnapshot = false)
    {
        lock (serviceBoundaryGate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                snapshot = default!;
                observedGeneration = 0;
                return false;
            }

            observedGeneration = recordsChange
                ? ++permissionChangeGeneration
                : permissionChangeGeneration;
            if (permissionSnapshotReadsInFlight++ == 0)
            {
                permissionSnapshotReadsDrained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            using ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall();
            snapshot = ReadPermissionSnapshot(preferCachedSnapshot);
            return true;
        }
        finally
        {
            lock (serviceBoundaryGate)
            {
                if (--permissionSnapshotReadsInFlight == 0)
                {
                    permissionSnapshotReadsDrained.TrySetResult(true);
                }
            }
        }
    }

    private bool PermissionSnapshotRequiresSafetyStop(
        DesktopRemoteWindowPermissionSnapshot snapshot)
    {
        DesktopPermissionState capture = NormalizePermissionState(snapshot.Capture);
        DesktopPermissionState input = NormalizePermissionState(snapshot.Input);
        return capture != DesktopPermissionState.Granted
                && safetySessionMayExist
            || input != DesktopPermissionState.Granted
                && SafetyDriverEligibleSessionMayExist();
    }

    private void ApplyPermissionSnapshot(
        DesktopRemoteWindowPermissionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        bool driverSessionCouldExist;
        lock (serviceBoundaryGate)
        {
            driverSessionCouldExist = SafetyDriverEligibleSessionMayExist();
        }

        capturePermissionState = NormalizePermissionState(snapshot.Capture);
        inputPermissionState = NormalizePermissionState(snapshot.Input);
        bool disableRemoteDriving =
            Interlocked.Exchange(ref remoteDrivingSafetyDisableRequired, 0) != 0
            || capturePermissionState != DesktopPermissionState.Granted
            || inputPermissionState is DesktopPermissionState.Denied
                or DesktopPermissionState.Revoked
                or DesktopPermissionState.Unsupported
                or DesktopPermissionState.Unavailable
            || driverSessionCouldExist
                && inputPermissionState != DesktopPermissionState.Granted;
        bool remoteDrivingChanged = disableRemoteDriving && isRemoteDrivingEnabled;
        if (disableRemoteDriving)
        {
            isRemoteDrivingEnabled = false;
            hasAcknowledgedInputPermissionReview = false;
            isInputPermissionReviewVisible = false;
            if (remoteDrivingChanged)
            {
                fallbackFailureStatus = null;
            }
        }

        bool clearCaptureReview =
            capturePermissionState == DesktopPermissionState.Granted;
        bool captureAcknowledgementChanged = clearCaptureReview
            && hasAcknowledgedCapturePermissionReview;
        bool captureReviewVisibilityChanged = clearCaptureReview
            && isCapturePermissionReviewVisible;
        if (clearCaptureReview)
        {
            hasAcknowledgedCapturePermissionReview = false;
            isCapturePermissionReviewVisible = false;
        }

        bool clearInputReview =
            inputPermissionState == DesktopPermissionState.Granted;
        bool inputAcknowledgementChanged = clearInputReview
            && hasAcknowledgedInputPermissionReview;
        bool inputReviewVisibilityChanged = clearInputReview
            && isInputPermissionReviewVisible;
        if (clearInputReview)
        {
            hasAcknowledgedInputPermissionReview = false;
            isInputPermissionReviewVisible = false;
        }

        // Permission loss must cross the local safety boundary before any
        // observer can interrupt presentation updates.
        EnforcePermissionSafetyStop();

        ApplyCapturePermissionPresentation();
        ApplyInputPermissionPresentation();
        if (remoteDrivingChanged)
        {
            OnPropertyChanged(nameof(IsRemoteDrivingEnabled));
        }

        if (captureAcknowledgementChanged)
        {
            OnPropertyChanged(nameof(HasAcknowledgedCapturePermissionReview));
        }

        if (captureReviewVisibilityChanged)
        {
            OnPropertyChanged(nameof(IsCapturePermissionReviewVisible));
        }

        if (inputAcknowledgementChanged)
        {
            OnPropertyChanged(nameof(HasAcknowledgedInputPermissionReview));
        }

        if (inputReviewVisibilityChanged)
        {
            OnPropertyChanged(nameof(IsInputPermissionReviewVisible));
        }

        OnPropertyChanged(nameof(CanEnableRemoteDriving));
        OnPropertyChanged(nameof(IsCapturePermissionReviewActionVisible));
        NotifyPermissionCommandStates();
        UpdateFallbackPresentation();
    }

    private void ApplyCapturePermissionPresentation()
    {
        (CapturePermissionStatus,
            CapturePermissionDescription,
            CapturePermissionRecoveryAction) = capturePermissionState switch
            {
                DesktopPermissionState.NotDetermined => (
                    "CAPTURE PERMISSION REVIEW REQUIRED",
                    "Review what Remote Window can expose before asking the operating system for screen-capture permission.",
                    "No permission is requested until the review is acknowledged."),
                DesktopPermissionState.Granted => (
                    "CAPTURE PERMISSION GRANTED",
                    "The operating system currently allows screen capture. Execution still remains on the source Device.",
                    "Stop sharing before changing capture access in system settings."),
                DesktopPermissionState.Denied => (
                    "CAPTURE PERMISSION DENIED",
                    "Remote Window cannot start capture. The source Activity remains unchanged.",
                    "Review screen-capture privacy settings, then return to Flowspan."),
                DesktopPermissionState.Revoked => (
                    "CAPTURE PERMISSION REVOKED",
                    "Capture access was removed. New sharing and remote driving remain disabled.",
                    "Review screen-capture privacy settings before retrying."),
                DesktopPermissionState.Unsupported => (
                    "CAPTURE PERMISSION UNAVAILABLE",
                    "Native capture permission is unavailable in this platform build.",
                    "Install a Flowspan build with the matching native capture adapter."),
                _ => (
                    "CAPTURE PERMISSION UNAVAILABLE",
                    "Capture permission state could not be read safely.",
                    "Inspect local diagnostics and platform privacy settings before retrying."),
            };
    }

    private void ApplyInputPermissionPresentation()
    {
        (InputPermissionStatus,
            InputPermissionDescription,
            InputPermissionRecoveryAction) = inputPermissionState switch
            {
                DesktopPermissionState.NotDetermined => (
                    "INPUT PERMISSION NOT REQUESTED",
                    "Remote driving is off. Input or accessibility permission is requested only after remote driving is explicitly enabled.",
                    "Viewing a Remote Window does not require input permission."),
                DesktopPermissionState.Granted => (
                    "INPUT PERMISSION GRANTED",
                    "The operating system currently allows Flowspan's local input boundary.",
                    "Driver authority still requires a current Capability and Driver Lease."),
                DesktopPermissionState.Denied => (
                    "INPUT PERMISSION DENIED",
                    "Remote viewing may continue, but remote driving remains disabled.",
                    "Review input or accessibility privacy settings, then return to Flowspan."),
                DesktopPermissionState.Revoked => (
                    "INPUT PERMISSION REVOKED",
                    "Remote driving was disabled because local input access was removed.",
                    "Review input or accessibility privacy settings before enabling driving again."),
                DesktopPermissionState.Unsupported => (
                    "INPUT PERMISSION UNAVAILABLE",
                    "Native input permission is unavailable in this platform build.",
                    "Install a Flowspan build with the matching native input adapter."),
                _ => (
                    "INPUT PERMISSION UNAVAILABLE",
                    "Input permission state could not be read safely. Remote driving remains disabled.",
                    "Inspect local diagnostics and platform privacy settings before retrying."),
            };
    }

    private void ApplyPermissionRequestFailure(bool capture)
    {
        bool remoteDrivingChanged = isRemoteDrivingEnabled;
        isRemoteDrivingEnabled = false;
        hasAcknowledgedInputPermissionReview = false;
        isInputPermissionReviewVisible = false;
        if (remoteDrivingChanged)
        {
            fallbackFailureStatus = null;
        }

        if (capture)
        {
            capturePermissionState = DesktopPermissionState.Unavailable;
        }
        else
        {
            inputPermissionState = DesktopPermissionState.Unavailable;
        }

        lock (serviceBoundaryGate)
        {
            if (capture)
            {
                admissionCapturePermissionState =
                    DesktopPermissionState.Unavailable;
            }
            else
            {
                admissionInputPermissionState =
                    DesktopPermissionState.Unavailable;
            }
        }

        EnforcePermissionSafetyStop();
        Refresh();
        EnforcePermissionSafetyStop();
        if (capture)
        {
            ApplyCapturePermissionPresentation();
        }
        else
        {
            ApplyInputPermissionPresentation();
        }

        if (remoteDrivingChanged)
        {
            OnPropertyChanged(nameof(IsRemoteDrivingEnabled));
        }

        OnPropertyChanged(nameof(CanEnableRemoteDriving));
        NotifyPermissionCommandStates();
        UpdateFallbackPresentation();
    }

    private static DesktopPermissionState NormalizePermissionState(
        DesktopPermissionState state) => Enum.IsDefined(state)
            ? state
            : DesktopPermissionState.Unavailable;

    private bool CanAdmitStart(
        DesktopActivitySnapshot activity,
        DesktopActivityTargetSnapshot target,
        MirrorParticipantRole role) =>
        Volatile.Read(ref disposed) == 0
        && serviceAvailable
        && fallbackSemanticResumeAvailability
            == DesktopSemanticResumeAvailability.Unavailable
        && !IsPermissionBusy
        && Equals(fallbackActivity, activity)
        && Equals(fallbackTarget, target)
        && activity.Lifecycle == ActivityLifecycle.Active
        && IsRemoteDrivingEnabled
            == (role == MirrorParticipantRole.DriverEligible)
        && admissionCapturePermissionState == DesktopPermissionState.Granted
        && (role != MirrorParticipantRole.DriverEligible
            || admissionInputPermissionState == DesktopPermissionState.Granted)
        && observedLifecycle is null or RemoteWindowLifecycle.Idle;

    private bool SafetyDriverEligibleSessionMayExist() =>
        safetySessionMayExist
        && safetySessionRole == MirrorParticipantRole.DriverEligible;

    private void MarkSafetySessionMayExist(
        MirrorParticipantRole role,
        bool beginsNewSession = false)
    {
        if (beginsNewSession || safetyInactiveBoundaryObserved)
        {
            safetyGeneration = checked(safetyGeneration + 1);
            safetyInactiveBoundaryObserved = false;
        }

        safetySessionMayExist = true;
        if (role == MirrorParticipantRole.DriverEligible
            || safetySessionRole is null)
        {
            safetySessionRole = role;
        }
    }

    private void ElevateSafetyFromSnapshot(
        RemoteWindowSharingSnapshot snapshot)
    {
        bool remoteAuthorityExists = snapshot.CurrentDriverDeviceId is { } driver
                && driver != snapshot.HostDeviceId
            || snapshot.Participants.Keys.Any(
                participant => participant != snapshot.HostDeviceId);
        bool activeLifecycle = snapshot.Lifecycle is
            RemoteWindowLifecycle.Starting
            or RemoteWindowLifecycle.Active
            or RemoteWindowLifecycle.ProtectionPaused;
        if (!activeLifecycle
            && snapshot.CaptureState == RemoteWindowCaptureState.Stopped
            && !remoteAuthorityExists)
        {
            return;
        }

        bool driverEligible = snapshot.CurrentDriverDeviceId is { } currentDriver
                && currentDriver != snapshot.HostDeviceId
            || snapshot.Participants.Any(participant =>
                participant.Key != snapshot.HostDeviceId
                && participant.Value == MirrorParticipantRole.DriverEligible);
        MarkSafetySessionMayExist(
            driverEligible
                ? MirrorParticipantRole.DriverEligible
                : MirrorParticipantRole.ViewOnly);
    }

    private void UpdateSafetyFromAcceptedSnapshot(
        RemoteWindowSharingSnapshot snapshot)
    {
        ElevateSafetyFromSnapshot(snapshot);
        bool remoteAuthorityExists = snapshot.CurrentDriverDeviceId is { } driver
                && driver != snapshot.HostDeviceId
            || snapshot.Participants.Keys.Any(
                participant => participant != snapshot.HostDeviceId);
        bool provesStoppedWithoutAuthority = snapshot.CaptureState
                == RemoteWindowCaptureState.Stopped
            && !remoteAuthorityExists
            && snapshot.Lifecycle == RemoteWindowLifecycle.Idle;
        if (provesStoppedWithoutAuthority)
        {
            ClearSafetySession();
            RecordAuthoritativeInactiveBoundary();
        }
    }

    private void RecordAuthoritativeInactiveBoundary()
    {
        if (safetyInactiveBoundaryObserved)
        {
            return;
        }

        safetyGeneration = checked(safetyGeneration + 1);
        safetyInactiveBoundaryObserved = true;
    }

    private bool TryObserveSafetyControllerGeneration(long generation)
    {
        if (generation < safetyControllerGeneration)
        {
            return false;
        }

        if (generation == safetyControllerGeneration)
        {
            return true;
        }

        safetyControllerGeneration = generation;
        safetyGeneration = checked(safetyGeneration + 1);
        safetyInactiveBoundaryObserved = false;
        ClearSafetySession();
        return true;
    }

    private void ClearSafetySession()
    {
        safetySessionMayExist = false;
        safetySessionRole = null;
    }

    private bool RecordStartResult(
        RemoteWindowCommandResult result,
        long admittedSafetyGeneration,
        long admittedControllerGeneration)
    {
        lock (serviceBoundaryGate)
        {
            if (admittedSafetyGeneration != safetyGeneration
                || admittedControllerGeneration != safetyControllerGeneration)
            {
                return false;
            }

            InvalidateEarlierServiceObservationsLocked();
            ClearRetryResetCleanupProof();
            if (StartResultRequiresSafetyStop(result))
            {
                return true;
            }

            retryResetCleanupActivityId = result.Snapshot.ActivityId;
            retryResetCleanupRevision = result.Snapshot.Revision;
            ClearSafetySession();
            return true;
        }
    }

    private static bool StartResultRequiresSafetyStop(
        RemoteWindowCommandResult result) =>
        result.Succeeded
        || result.CleanupBoundary is not { Succeeded: true }
        || !IsStoppedUnavailableWithoutRemoteAuthority(result.Snapshot);

    private bool IsRetryResetCleanupProof(
        RemoteWindowSharingSnapshot snapshot) =>
        retryResetCleanupActivityId == snapshot.ActivityId
        && retryResetCleanupRevision == snapshot.Revision;

    private void ClearRetryResetCleanupProof()
    {
        retryResetCleanupActivityId = null;
        retryResetCleanupRevision = null;
    }

    private static bool IsStoppedUnavailableWithoutRemoteAuthority(
        RemoteWindowSharingSnapshot snapshot) =>
        snapshot.Lifecycle == RemoteWindowLifecycle.Unavailable
        && snapshot.CaptureState == RemoteWindowCaptureState.Stopped
        && snapshot.CurrentDriverDeviceId is null
        && snapshot.Participants.Keys.All(participant =>
            participant == snapshot.HostDeviceId);

    private void ClearFallbackSessionContext()
    {
        fallbackSessionActivity = null;
        fallbackSessionActivityId = null;
        fallbackSessionRole = null;
        fallbackSessionTarget = null;
    }

    private bool IsCurrentFallbackRequest(
        DesktopActivitySnapshot activity,
        DesktopActivityTargetSnapshot target,
        MirrorParticipantRole role) =>
        Equals(fallbackActivity, activity)
        && Equals(fallbackTarget, target)
        && IsRemoteDrivingEnabled
            == (role == MirrorParticipantRole.DriverEligible);

    private void EnforcePermissionSafetyStop()
    {
        EmergencyStopBoundaryOutcome? stopOutcome = null;
        bool safetyStopRequired;
        lock (serviceBoundaryGate)
        {
            bool inputPermissionBlocksDriver =
                admissionInputPermissionState != DesktopPermissionState.Granted
                && SafetyDriverEligibleSessionMayExist();
            safetyStopRequired =
                admissionCapturePermissionState != DesktopPermissionState.Granted
                    && safetySessionMayExist
                || inputPermissionBlocksDriver;
        }

        if (safetyStopRequired
            && TryBeginEmergencyStop(allowAdmittedStart: true))
        {
            EmergencyStopBoundaryOutcome outcome = InvokeEmergencyStopBoundary();
            if (outcome.Invoked)
            {
                stopOutcome = outcome;
            }
        }

        if (stopOutcome is not null)
        {
            ApplyEmergencyStopOutcome(stopOutcome);
        }
    }

    private void SetFallbackBusy(bool value)
    {
        if (isFallbackBusy == value)
        {
            return;
        }

        isFallbackBusy = value;
        UpdateEmergencyStopAvailability(observedLifecycle);
        UpdateFallbackPresentation();
    }

    private void SetLocalResetBusy(bool value)
    {
        if (isLocalResetBusy == value)
        {
            return;
        }

        isLocalResetBusy = value;
        OnPropertyChanged(nameof(IsLocalResetAvailable));
        resetRemoteWindowCommand.NotifyCanExecuteChanged();
    }

    private void UpdateFallbackPresentation()
    {
        if (isFallbackBusy)
        {
            DesktopActivitySnapshot startingActivity = inFlightFallbackActivity!;
            DesktopActivityTargetSnapshot startingTarget = inFlightFallbackTarget!;
            FallbackStatus = "REMOTE WINDOW STARTING";
            FallbackDescription =
                $"Preparing {startingActivity.Title} for {startingTarget.DisplayName}. Execution remains on this source Device.";
        }
        else if (observedLifecycle is
            RemoteWindowLifecycle.Starting
            or RemoteWindowLifecycle.Active
            or RemoteWindowLifecycle.ProtectionPaused)
        {
            string lifecycleStatus = observedLifecycle switch
            {
                RemoteWindowLifecycle.Starting => "REMOTE WINDOW STARTING",
                RemoteWindowLifecycle.ProtectionPaused => "REMOTE WINDOW PAUSED",
                _ => "REMOTE WINDOW ACTIVE",
            };
            if (fallbackSessionActivity is { } sessionActivity
                && fallbackSessionTarget is { } sessionTarget
                && sessionActivity.ActivityId == observedActivityId)
            {
                FallbackStatus = lifecycleStatus;
                FallbackDescription =
                    $"Presenting {sessionActivity.Title} to {sessionTarget.DisplayName}. Execution remains on this source Device.";
            }
            else
            {
                string observedTitle = lastAcceptedSnapshot?.ActivityTitle
                    ?? "the current Activity";
                FallbackStatus =
                    $"{lifecycleStatus} — TARGET CONTEXT UNAVAILABLE";
                FallbackDescription =
                    $"Presenting {observedTitle}. The admitted target label is unavailable, so no selected preview target is attributed to this session. Execution remains on this source Device.";
            }
        }
        else if (fallbackActivity is null || fallbackTarget is null)
        {
            FallbackStatus =
                "REMOTE WINDOW FALLBACK — SELECT ACTIVITY AND TARGET";
            FallbackDescription =
                "Select one local Activity and one authenticated target to review Remote Window.";
        }
        else if (fallbackSemanticResumeAvailability
            == DesktopSemanticResumeAvailability.Unknown)
        {
            FallbackStatus =
                "REMOTE WINDOW UNAVAILABLE — SEMANTIC SUPPORT UNKNOWN";
            FallbackDescription =
                $"Semantic resume support for {fallbackActivity.Title} could not be confirmed. Handoff, Move, and Remote Window remain disabled until support is known.";
        }
        else if (fallbackSemanticResumeAvailability
            == DesktopSemanticResumeAvailability.Available)
        {
            FallbackStatus =
                "REMOTE WINDOW NOT OFFERED — SEMANTIC RESUME AVAILABLE";
            FallbackDescription =
                $"Use Handoff or Move for {fallbackActivity.Title} on {fallbackTarget.DisplayName}. Remote Window fallback is reserved for Activities without semantic resume.";
        }
        else if (!serviceAvailable)
        {
            FallbackStatus = "REMOTE WINDOW UNAVAILABLE";
            FallbackDescription =
                $"Remote Window cannot present {fallbackActivity.Title} to {fallbackTarget.DisplayName} in this platform build. The source Activity remains unchanged.";
        }
        else if (fallbackActivity.Lifecycle != ActivityLifecycle.Active)
        {
            FallbackStatus = "REMOTE WINDOW BLOCKED — SOURCE NOT ACTIVE";
            FallbackDescription =
                $"{fallbackActivity.Title} is not active on this source Device. Select an active local Activity.";
        }
        else if (observedLifecycle == RemoteWindowLifecycle.EmergencyStopped)
        {
            FallbackStatus = "REMOTE WINDOW STOPPED — LOCAL RESET REQUIRED";
            FallbackDescription =
                "Finish or locally reset the current Remote Window session before starting another one.";
        }
        else if (capturePermissionState != DesktopPermissionState.Granted)
        {
            FallbackStatus = "REMOTE WINDOW BLOCKED — CAPTURE PERMISSION REQUIRED";
            FallbackDescription =
                "Review and grant screen-capture permission before starting Remote Window. No sharing request was sent.";
        }
        else if (IsRemoteDrivingEnabled
            && inputPermissionState != DesktopPermissionState.Granted)
        {
            FallbackStatus = "REMOTE WINDOW BLOCKED — INPUT PERMISSION REQUIRED";
            FallbackDescription =
                "Remote driving was selected. Review and grant input or accessibility permission, or turn remote driving off to start view-only.";
        }
        else if (fallbackFailureStatus is { } failureStatus)
        {
            (FallbackStatus, FallbackDescription) = failureStatus switch
            {
                RemoteWindowCommandStatus.CapabilityDenied => (
                    "REMOTE WINDOW BLOCKED — REVIEW TRUST",
                    "The selected target lacks the required current Mirror capability. Review Trust and retry; the source Activity remains unchanged."),
                RemoteWindowCommandStatus.ProtectionBlocked => (
                    "REMOTE WINDOW BLOCKED — PROTECTED SOURCE",
                    "The source protection state is unsafe or uncertain. Capture and input remain blocked."),
                RemoteWindowCommandStatus.BoundaryFailed => (
                    "REMOTE WINDOW COULD NOT START",
                    "A local or authenticated sharing boundary did not confirm startup. Inspect local diagnostics before retrying."),
                _ => (
                    "REMOTE WINDOW NOT STARTED",
                    "The exact selected Remote Window request was not applied. Review the visible session, permission, and Trust state before retrying."),
            };
        }
        else if (observedLifecycle is not null and not RemoteWindowLifecycle.Idle)
        {
            FallbackStatus = "REMOTE WINDOW BUSY — ANOTHER SESSION EXISTS";
            FallbackDescription =
                "Finish or locally reset the current Remote Window session before starting another one.";
        }
        else
        {
            string role = IsRemoteDrivingEnabled
                ? "driver-eligible"
                : "view-only";
            FallbackStatus =
                "REMOTE WINDOW READY — EXECUTION STAYS ON SOURCE";
            FallbackDescription =
                $"Present {fallbackActivity.Title} on {fallbackTarget.DisplayName} as {role}. Execution remains on this source Device; application and process state do not move.";
        }

        OnPropertyChanged(nameof(IsFallbackStartAvailable));
        OnPropertyChanged(nameof(FallbackStartAutomationName));
        startRemoteWindowCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPermissionCommandStates()
    {
        reviewCapturePermissionCommand.NotifyCanExecuteChanged();
        cancelCapturePermissionReviewCommand.NotifyCanExecuteChanged();
        requestCapturePermissionCommand.NotifyCanExecuteChanged();
        cancelInputPermissionReviewCommand.NotifyCanExecuteChanged();
        requestInputPermissionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsFallbackStartAvailable));
        startRemoteWindowCommand.NotifyCanExecuteChanged();
    }

    private void Refresh()
    {
        if (!TryBeginServiceObservation(out long observationTicket))
        {
            return;
        }

        bool available = false;
        string unavailableReasonCode = "service_state_unavailable";
        RemoteWindowSharingSnapshot? snapshot = null;
        long? controllerGeneration = null;
        try
        {
            using ExternalBoundaryCallLease boundaryCall =
                EnterExternalBoundaryCall();
            available = service.IsAvailable;
            controllerGeneration = service.ControllerGeneration;
            if (available)
            {
                snapshot = service.GetSnapshot();
            }
            else
            {
                unavailableReasonCode = service.UnavailableReasonCode;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            available = false;
            unavailableReasonCode = "service_state_unavailable";
        }
        finally
        {
            CompleteServiceOperation();
        }

        SnapshotProjection? snapshotProjection = null;
        lock (serviceBoundaryGate)
        {
            if (Volatile.Read(ref disposed) != 0
                || observationTicket != latestServiceObservationTicket
                || controllerGeneration is { } generation
                    && !TryObserveSafetyControllerGeneration(generation))
            {
                return;
            }

            serviceAvailable = available;
            if (available)
            {
                snapshotProjection = ReduceSnapshot(
                    snapshot,
                    allowEqualRevision: false,
                    allowLowerRevisionConfirmedIdle: false,
                    controllerGeneration,
                    expectedSafetyGeneration: null);
            }
        }

        EnforcePermissionSafetyStop();
        if (!available)
        {
            long? unavailableProjectionVersion = null;
            lock (serviceBoundaryGate)
            {
                if (Volatile.Read(ref disposed) == 0
                    && observationTicket == latestServiceObservationTicket)
                {
                    unavailableProjectionVersion = ReduceUnavailable();
                }
            }

            if (unavailableProjectionVersion is { } version)
            {
                ProjectUnavailable(version, unavailableReasonCode);
            }

            return;
        }

        if (snapshotProjection is not null)
        {
            ProjectSnapshot(
                snapshotProjection.Version,
                snapshotProjection.Snapshot,
                snapshotProjection.PreviousLifecycle);
        }
    }

    private void ApplySnapshot(
        RemoteWindowSharingSnapshot? snapshot,
        bool allowEqualRevision = false,
        bool allowLowerRevisionConfirmedIdle = false,
        long? controllerGeneration = null,
        long? expectedSafetyGeneration = null)
    {
        SnapshotProjection? projection = ReduceSnapshot(
            snapshot,
            allowEqualRevision,
            allowLowerRevisionConfirmedIdle,
            controllerGeneration,
            expectedSafetyGeneration);
        EnforcePermissionSafetyStop();
        if (projection is not null)
        {
            ProjectSnapshot(
                projection.Version,
                projection.Snapshot,
                projection.PreviousLifecycle);
        }
    }

    private SnapshotProjection? ReduceSnapshot(
        RemoteWindowSharingSnapshot? snapshot,
        bool allowEqualRevision,
        bool allowLowerRevisionConfirmedIdle,
        long? controllerGeneration,
        long? expectedSafetyGeneration)
    {
        long projectionVersion;
        RemoteWindowLifecycle? previousLifecycle;
        bool rejected = false;
        lock (serviceBoundaryGate)
        {
            if (expectedSafetyGeneration is { } expected
                && expected != safetyGeneration)
            {
                return null;
            }

            SnapshotReducerState current = snapshotReducer;
            long generation = controllerGeneration
                ?? current.ControllerGeneration;
            if (generation < current.ControllerGeneration)
            {
                return null;
            }

            bool controllerChanged = generation > current.ControllerGeneration;
            previousLifecycle = current.Lifecycle;
            if (snapshot is null)
            {
                if (Volatile.Read(ref emergencyStopAttempted) != 0)
                {
                    observedInactiveAfterEmergencyStop = true;
                }

                ClearSafetySession();
                RecordAuthoritativeInactiveBoundary();
                ClearRetryResetCleanupProof();
                ClearFallbackSessionContext();
                snapshotReducer = new SnapshotReducerState(
                    Version: checked(current.Version + 1),
                    ControllerGeneration: generation,
                    ActivityId: null,
                    Lifecycle: null,
                    Revision: -1,
                    InactiveRevisionResetActivityId: null,
                    LastAcceptedSnapshot: null,
                    IsServiceStateUnavailable: false);
                projectionVersion = snapshotReducer.Version;
            }
            else
            {
                bool sameActivity = !controllerChanged
                    && current.ActivityId == snapshot.ActivityId;
                bool confirmedLowerRevisionIdle = sameActivity
                    && snapshot.Lifecycle == RemoteWindowLifecycle.Idle
                    && allowLowerRevisionConfirmedIdle;
                bool confirmedLowerRevisionNewSession = sameActivity
                    && snapshot.Lifecycle != RemoteWindowLifecycle.Idle
                    && current.InactiveRevisionResetActivityId
                        == snapshot.ActivityId;
                bool equalRevisionRecovery = sameActivity
                    && snapshot.Revision == current.Revision
                    && current.IsServiceStateUnavailable
                    && current.LastAcceptedSnapshot is { } lastKnown
                    && HasEquivalentSnapshotState(lastKnown, snapshot);
                rejected = sameActivity
                    && snapshot.Revision <= current.Revision
                    && !confirmedLowerRevisionIdle
                    && !confirmedLowerRevisionNewSession
                    && !equalRevisionRecovery
                    && !(allowEqualRevision
                        && snapshot.Revision == current.Revision);
                if (rejected)
                {
                    projectionVersion = current.Version;
                }
                else
                {
                    UpdateSafetyFromAcceptedSnapshot(snapshot);
                    if (!IsRetryResetCleanupProof(snapshot))
                    {
                        ClearRetryResetCleanupProof();
                    }

                    if (fallbackSessionActivity is { } admittedActivity
                        && admittedActivity.ActivityId != snapshot.ActivityId)
                    {
                        ClearFallbackSessionContext();
                    }

                    if (Volatile.Read(ref emergencyStopAttempted) != 0
                        && snapshot.Lifecycle == RemoteWindowLifecycle.Idle)
                    {
                        observedInactiveAfterEmergencyStop = true;
                    }

                    if (snapshot.Lifecycle == RemoteWindowLifecycle.Idle)
                    {
                        ClearFallbackSessionContext();
                    }

                    snapshotReducer = new SnapshotReducerState(
                        Version: checked(current.Version + 1),
                        ControllerGeneration: generation,
                        ActivityId: snapshot.ActivityId,
                        Lifecycle: snapshot.Lifecycle,
                        Revision: snapshot.Revision,
                        InactiveRevisionResetActivityId: snapshot.Lifecycle
                                == RemoteWindowLifecycle.Idle
                            ? snapshot.ActivityId
                            : null,
                        LastAcceptedSnapshot: snapshot,
                        IsServiceStateUnavailable: false);
                    projectionVersion = snapshotReducer.Version;
                }
            }
        }

        if (rejected)
        {
            return null;
        }

        return new SnapshotProjection(
            projectionVersion,
            snapshot,
            previousLifecycle);
    }

    private void ProjectSnapshot(
        long projectionVersion,
        RemoteWindowSharingSnapshot? snapshot,
        RemoteWindowLifecycle? previousLifecycle)
    {
        lock (presentationGate)
        {
            lock (serviceBoundaryGate)
            {
                if (snapshotReducer.Version != projectionVersion)
                {
                    return;
                }
            }

            if (snapshot is null)
            {
                ApplyInactive();
                return;
            }

            ActivityTitle = snapshot.ActivityTitle;
            ActivityId = snapshot.ActivityId.ToString();
            CaptureStatus = $"CAPTURE: {snapshot.CaptureState}";
            ParticipantStatus = $"PARTICIPANTS: {snapshot.Participants.Count}";
            DriverStatus = snapshot.CurrentDriverDeviceId is null
                ? "DRIVER: None"
                : $"DRIVER: {snapshot.CurrentDriverDeviceId} / "
                    + $"EPOCH {snapshot.DriverLeaseEpoch?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"} / "
                    + (snapshot.DriverLeaseExpiresAt is { } expiresAt
                        ? $"LEASE EXPIRES {expiresAt:O}"
                        : "LEASE EXPIRES Unknown");
            ProtectionStatus = $"PROTECTION: {snapshot.ProtectionKind}";
            RevisionStatus = $"REVISION: {snapshot.Revision}";
            IsDetailVisible = snapshot.Lifecycle != RemoteWindowLifecycle.Idle;
            string currentDriver =
                snapshot.CurrentDriverDeviceId?.ToString() ?? "None";
            SharingStatus = snapshot.Lifecycle switch
            {
                RemoteWindowLifecycle.Starting => "REMOTE WINDOW STARTING",
                RemoteWindowLifecycle.Active => "REMOTE WINDOW ACTIVE",
                RemoteWindowLifecycle.ProtectionPaused => "REMOTE WINDOW PAUSED",
                RemoteWindowLifecycle.EmergencyStopped => "EMERGENCY STOPPED",
                RemoteWindowLifecycle.Unavailable => "REMOTE WINDOW UNAVAILABLE",
                _ => "NOT SHARING",
            };
            SharingDescription = snapshot.Lifecycle switch
            {
                RemoteWindowLifecycle.Starting =>
                    $"Preparing {snapshot.ActivityTitle}. Current Driver: {currentDriver}. Execution remains on this source Device.",
                RemoteWindowLifecycle.Active =>
                    $"Presenting {snapshot.ActivityTitle}. Current Driver: {currentDriver}. Execution remains on this source Device.",
                RemoteWindowLifecycle.ProtectionPaused =>
                    $"{snapshot.ActivityTitle} remains on this source Device. Current Driver: {currentDriver}. Capture and input are blocked by protection state.",
                RemoteWindowLifecycle.EmergencyStopped =>
                    $"{snapshot.ActivityTitle} is Emergency Stopped. Current Driver: {currentDriver}. Local Driver authority is revoked; capture, input, and sharing boundary results are shown below.",
                RemoteWindowLifecycle.Unavailable =>
                    $"Remote Window state for {snapshot.ActivityTitle} is unavailable. Current Driver: {currentDriver}. The source Activity remains unchanged.",
                _ => "No peer can view or drive this device.",
            };
            SharingAutomationName = snapshot.Lifecycle
                    == RemoteWindowLifecycle.Idle
                ? $"Sharing state: {SharingStatus}"
                : $"Sharing state: {SharingStatus}; Activity: {snapshot.ActivityTitle}; Current Driver: {currentDriver}";
            if (snapshot.Lifecycle is
                    RemoteWindowLifecycle.Starting
                    or RemoteWindowLifecycle.Active
                    or RemoteWindowLifecycle.ProtectionPaused
                && (Volatile.Read(ref emergencyStopPresentationResetRequired) != 0
                    || Volatile.Read(ref emergencyStopAttempted) != 0
                        && (observedInactiveAfterEmergencyStop
                            || emergencyStoppedActivityId is { } stoppedActivityId
                                && stoppedActivityId != snapshot.ActivityId
                            || emergencyStoppedRevision is { } stoppedRevision
                                && snapshot.Revision > stoppedRevision)))
            {
                ResetEmergencyStopPresentationForNewSession();
            }

            UpdateEmergencyStopAvailability(snapshot.Lifecycle);
            UpdateLocalResetPresentation(snapshot.Lifecycle, previousLifecycle);
            UpdateFallbackPresentation();
        }
    }

    private void ApplyInactive()
    {
        ActivityTitle = "No live Activity";
        ActivityId = "No live Activity";
        CaptureStatus = "CAPTURE: Stopped";
        ParticipantStatus = "PARTICIPANTS: 0";
        DriverStatus = "DRIVER: None";
        ProtectionStatus = "PROTECTION: Unknown";
        RevisionStatus = "REVISION: 0";
        SharingStatus = "NOT SHARING";
        SharingDescription = "No peer can view or drive this device.";
        SharingAutomationName = "Sharing state: Not sharing";
        IsDetailVisible = false;
        UpdateEmergencyStopAvailability(null);
        UpdateLocalResetPresentation(null, null);
        UpdateFallbackPresentation();
    }

    private void ApplyUnavailable(string reasonCode)
    {
        long projectionVersion = ReduceUnavailable();
        ProjectUnavailable(projectionVersion, reasonCode);
    }

    private long ReduceUnavailable()
    {
        lock (serviceBoundaryGate)
        {
            SnapshotReducerState current = snapshotReducer;
            snapshotReducer = current with
            {
                Version = checked(current.Version + 1),
                IsServiceStateUnavailable = true,
            };
            return snapshotReducer.Version;
        }
    }

    private void ProjectUnavailable(long projectionVersion, string reasonCode)
    {
        lock (presentationGate)
        {
            lock (serviceBoundaryGate)
            {
                if (snapshotReducer.Version != projectionVersion)
                {
                    return;
                }
            }

            ApplyUnavailablePresentation(reasonCode);
        }
    }

    private void ApplyUnavailablePresentation(string reasonCode)
    {
        if (lastAcceptedSnapshot is { } lastKnown)
        {
            string currentDriver =
                lastKnown.CurrentDriverDeviceId?.ToString() ?? "None";
            ActivityTitle = $"{lastKnown.ActivityTitle} — LAST KNOWN";
            ActivityId = $"{lastKnown.ActivityId} — LAST KNOWN";
            CaptureStatus = $"CAPTURE (LAST KNOWN): {lastKnown.CaptureState}";
            ParticipantStatus =
                $"PARTICIPANTS (LAST KNOWN): {lastKnown.Participants.Count}";
            DriverStatus = lastKnown.CurrentDriverDeviceId is null
                ? "DRIVER (LAST KNOWN): None"
                : $"DRIVER (LAST KNOWN): {lastKnown.CurrentDriverDeviceId} / "
                    + $"EPOCH {lastKnown.DriverLeaseEpoch?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"} / "
                    + (lastKnown.DriverLeaseExpiresAt is { } expiresAt
                        ? $"LEASE EXPIRES {expiresAt:O}"
                        : "LEASE EXPIRES Unknown");
            ProtectionStatus =
                $"PROTECTION (LAST KNOWN): {lastKnown.ProtectionKind}";
            RevisionStatus = $"REVISION (LAST KNOWN): {lastKnown.Revision}";
            SharingAutomationName =
                "Sharing state: Remote Window unavailable; "
                + $"Last known Activity: {lastKnown.ActivityTitle}; "
                + $"Last known Driver: {currentDriver}";
            UpdateEmergencyStopAvailability(observedLifecycle);
            UpdateLocalResetPresentation(observedLifecycle, observedLifecycle);
            UpdateFallbackPresentation();
        }
        else
        {
            ActivityTitle = "Unknown Activity";
            ActivityId = "Unknown Activity";
            CaptureStatus = "CAPTURE: Unknown";
            ParticipantStatus = "PARTICIPANTS: Unknown";
            DriverStatus = "DRIVER: Unknown";
            ProtectionStatus = "PROTECTION: Unknown";
            RevisionStatus = "REVISION: Unknown";
            SharingAutomationName = "Sharing state: Remote Window unavailable";
            UpdateEmergencyStopAvailability(observedLifecycle);
            UpdateLocalResetPresentation(observedLifecycle, observedLifecycle);
            UpdateFallbackPresentation();
        }

        IsDetailVisible = true;
        SharingStatus = "REMOTE WINDOW UNAVAILABLE";
        SharingDescription = reasonCode switch
        {
            "native_adapters_unavailable" =>
                "Capture and input adapters are not available on this platform build. The source Activity remains unchanged.",
            _ when lastAcceptedSnapshot is { } accepted =>
                $"Remote Window state is unavailable. Last known Activity {accepted.ActivityTitle} at revision {accepted.Revision} reported capture {accepted.CaptureState} and Driver {accepted.CurrentDriverDeviceId?.ToString() ?? "None"}. Local capture and input are not claimed stopped; inspect local diagnostics before retrying.",
            _ =>
                "Remote Window state is unavailable and no Remote Window snapshot has been accepted. Capture, input, participants, and Driver are unknown; inspect local diagnostics before retrying.",
        };
    }

    private void UpdateEmergencyStopAvailability(
        RemoteWindowLifecycle? lifecycle = null)
    {
        bool lifecycleAllowsStop = lifecycle is
            RemoteWindowLifecycle.Starting
            or RemoteWindowLifecycle.Active
            or RemoteWindowLifecycle.ProtectionPaused;
        IsEmergencyStopAvailable = (lifecycleAllowsStop || isFallbackBusy)
            && Volatile.Read(ref emergencyStopAttempted) == 0;
        EmergencyStopHelpText = IsEmergencyStopAvailable
            ? "Stops local capture, input, and sharing sessions immediately without waiting for the peer."
            : "Unavailable while no stoppable Remote Window session is active.";
    }

    private void UpdateLocalResetPresentation(
        RemoteWindowLifecycle? lifecycle,
        RemoteWindowLifecycle? previousLifecycle)
    {
        if (!isLocalResetBusy)
        {
            if (lifecycle == RemoteWindowLifecycle.EmergencyStopped
                && emergencyStopFullyConfirmed)
            {
                LocalResetStatus = "LOCAL RESET REQUIRED";
                LocalResetDescription =
                    "Confirm a local reset before another session starts. Reset does not restore prior participants, capture, or Driver authority.";
            }
            else if (lifecycle == RemoteWindowLifecycle.EmergencyStopped)
            {
                LocalResetStatus = "LOCAL RESET UNAVAILABLE";
                LocalResetDescription =
                    "One or more local Emergency Stop boundaries are unconfirmed. Reset is unavailable; close Flowspan and inspect local diagnostics.";
            }
            else if (lifecycle == RemoteWindowLifecycle.Unavailable
                && serviceAvailable
                && IsUnavailableResetSafe())
            {
                LocalResetStatus = "LOCAL RETRY RESET REQUIRED";
                LocalResetDescription =
                    "The failed start is confirmed stopped with no remote Driver. Confirm a local retry reset before starting another session; no participant or authority is restored.";
            }
            else if (lifecycle == RemoteWindowLifecycle.Unavailable)
            {
                LocalResetStatus = "LOCAL RETRY RESET UNAVAILABLE";
                LocalResetDescription =
                    "Remote Window is unavailable, but a safely stopped local start state is not confirmed. Inspect local diagnostics before retrying.";
            }
            else if (lifecycle == RemoteWindowLifecycle.Idle
                && previousLifecycle is RemoteWindowLifecycle.EmergencyStopped
                    or RemoteWindowLifecycle.Unavailable)
            {
                LocalResetStatus = previousLifecycle
                    == RemoteWindowLifecycle.Unavailable
                    ? "LOCAL RETRY RESET CONFIRMED"
                    : "LOCAL RESET CONFIRMED";
                LocalResetDescription =
                    "The previous session is idle. Participants, capture, and the prior Driver were not restored.";
            }
            else
            {
                LocalResetStatus = "LOCAL RESET NOT REQUIRED";
                LocalResetDescription =
                    "No local Remote Window reset is required.";
            }
        }

        OnPropertyChanged(nameof(IsLocalResetAvailable));
        resetRemoteWindowCommand.NotifyCanExecuteChanged();
    }

    private void ResetEmergencyStopPresentationForNewSession()
    {
        Volatile.Write(ref emergencyStopAttempted, 0);
        Volatile.Write(ref emergencyStopPresentationResetRequired, 0);
        emergencyStopFullyConfirmed = false;
        emergencyStoppedActivityId = null;
        emergencyStoppedRevision = null;
        observedInactiveAfterEmergencyStop = false;
        EmergencyStopStatus = "EMERGENCY STOP NOT REQUIRED";
        EmergencyStopDescription =
            "No local Emergency Stop result has been recorded for this session.";
    }

    private void ObserveAuthoritativeSessionForEmergencyStop(
        RemoteWindowSharingSnapshot? snapshot)
    {
        if (Volatile.Read(ref emergencyStopAttempted) == 0)
        {
            return;
        }

        if (snapshot is null)
        {
            RecordAuthoritativeInactiveBoundary();
            observedInactiveAfterEmergencyStop = true;
            return;
        }

        if (snapshot.Lifecycle == RemoteWindowLifecycle.Idle)
        {
            bool staleSameActivityIdle = observedActivityId == snapshot.ActivityId
                && snapshot.Revision <= observedRevision;
            if (!staleSameActivityIdle)
            {
                RecordAuthoritativeInactiveBoundary();
                observedInactiveAfterEmergencyStop = true;
            }

            return;
        }

        bool stoppable = snapshot.Lifecycle is
            RemoteWindowLifecycle.Starting
            or RemoteWindowLifecycle.Active
            or RemoteWindowLifecycle.ProtectionPaused;
        bool newSession = observedInactiveAfterEmergencyStop
            || emergencyStoppedActivityId is { } stoppedActivityId
                && stoppedActivityId != snapshot.ActivityId
            || emergencyStoppedRevision is { } stoppedRevision
                && snapshot.Revision > stoppedRevision;
        if (!stoppable || !newSession)
        {
            return;
        }

        Volatile.Write(ref emergencyStopAttempted, 0);
        Volatile.Write(ref emergencyStopPresentationResetRequired, 1);
        emergencyStopFullyConfirmed = false;
        observedInactiveAfterEmergencyStop = false;
    }

    private static bool HasEquivalentSnapshotState(
        RemoteWindowSharingSnapshot left,
        RemoteWindowSharingSnapshot right) =>
        left.ActivityId == right.ActivityId
        && left.ActivityKind == right.ActivityKind
        && string.Equals(left.ActivityTitle, right.ActivityTitle, StringComparison.Ordinal)
        && left.HostDeviceId == right.HostDeviceId
        && left.Lifecycle == right.Lifecycle
        && left.CaptureState == right.CaptureState
        && left.CurrentDriverDeviceId == right.CurrentDriverDeviceId
        && left.DriverLeaseEpoch == right.DriverLeaseEpoch
        && left.DriverLeaseExpiresAt == right.DriverLeaseExpiresAt
        && left.ProtectionKind == right.ProtectionKind
        && left.Revision == right.Revision
        && left.Participants.Count == right.Participants.Count
        && left.Participants.All(participant =>
            right.Participants.TryGetValue(participant.Key, out MirrorParticipantRole role)
            && role == participant.Value);

    private static string ToConfirmation(LocalBoundaryResult boundary) =>
        boundary.Succeeded ? "confirmed" : $"unconfirmed ({boundary.ReasonCode})";

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private ExternalBoundaryCallLease EnterExternalBoundaryCall()
    {
        ExternalBoundaryCallScope? inheritedScope = externalBoundaryCallScope.Value;
        var currentScope = new ExternalBoundaryCallScope(this);
        externalBoundaryCallScope.Value = currentScope;
        return new ExternalBoundaryCallLease(
            this,
            currentScope,
            inheritedScope);
    }

    private void ExitExternalBoundaryCall(
        ExternalBoundaryCallScope currentScope,
        ExternalBoundaryCallScope? inheritedScope)
    {
        currentScope.Deactivate();
        externalBoundaryCallScope.Value = inheritedScope;
    }

    private sealed class ExternalBoundaryCallLease(
        RemoteWindowWorkspaceViewModel owner,
        ExternalBoundaryCallScope currentScope,
        ExternalBoundaryCallScope? inheritedScope) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            owner.ExitExternalBoundaryCall(currentScope, inheritedScope);
        }
    }

    private sealed class ExternalBoundaryCallScope(
        RemoteWindowWorkspaceViewModel owner)
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public RemoteWindowWorkspaceViewModel Owner { get; } = owner;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }
}

internal sealed class UnavailableDesktopRemoteWindowService :
    IDesktopRemoteWindowService
{
    private UnavailableDesktopRemoteWindowService()
    {
    }

    public static UnavailableDesktopRemoteWindowService Instance { get; } = new();

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public bool IsAvailable => false;

    public string UnavailableReasonCode => "native_adapters_unavailable";

    public RemoteWindowEmergencyStopResult EmergencyStop() =>
        throw new InvalidOperationException(
            "No Remote Window session is available to stop.");

    public RemoteWindowSharingSnapshot? GetSnapshot() => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
