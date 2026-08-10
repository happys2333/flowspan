using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Desktop;

public sealed record DesktopActivitySnapshot(
    ActivityId ActivityId,
    string Title,
    string Kind,
    ActivitySensitivity Sensitivity,
    ActivityLifecycle Lifecycle);

public sealed record DesktopActivityTargetSnapshot(
    DeviceId DeviceId,
    string DisplayName);

public sealed record DesktopReplaceTargetSnapshot(
    DeviceId DeviceId,
    ActivityId ActivityId,
    string Title,
    string Kind,
    long Revision,
    string DescriptorDigest,
    string PlacementSlot);

public sealed record DesktopReplaceTargetInventoryResult(
    FailureCode FailureCode,
    bool IsTruncated,
    DateTimeOffset? CapturedAt,
    ImmutableArray<DesktopReplaceTargetSnapshot> Targets)
{
    public bool IsSuccess => FailureCode == FailureCode.None;

    public static DesktopReplaceTargetInventoryResult Failed(
        FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A failed desktop Replace inventory must have a failure code.",
                nameof(failureCode));
        }

        return new DesktopReplaceTargetInventoryResult(
            failureCode,
            false,
            null,
            []);
    }
}

public sealed record DesktopReplaceOperationResult(
    OperationId? OperationId,
    CorrelationId? CorrelationId,
    ActivityDeliveryStatus DeliveryStatus,
    FailureCode FailureCode,
    DateTimeOffset OccurredAt,
    OperationReceipt? Receipt,
    UndoCapsuleReference? UndoCapsule)
{
    public bool IsSuccess =>
        DeliveryStatus == ActivityDeliveryStatus.Acknowledged
        && Receipt?.Status == OperationStatus.Committed
        && UndoCapsule is not null;

    public OperationStatus? Status => Receipt?.Status;

    internal static DesktopReplaceOperationResult NotDelivered(
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        if (failureCode == FailureCode.None
            || failureCode == FailureCode.AcknowledgementLost)
        {
            throw new ArgumentException(
                "A Replace operation that was not delivered needs an exact preflight or delivery failure.",
                nameof(failureCode));
        }

        return new DesktopReplaceOperationResult(
            null,
            null,
            ActivityDeliveryStatus.NotDelivered,
            failureCode,
            occurredAt,
            null,
            null);
    }

    internal static DesktopReplaceOperationResult NotDelivered(
        OperationContext context,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        DesktopReplaceOperationResult result = NotDelivered(
            failureCode,
            occurredAt);
        return result with
        {
            OperationId = context.OperationId,
            CorrelationId = context.CorrelationId,
        };
    }

    internal static DesktopReplaceOperationResult AcknowledgementLost(
        OperationContext context,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new DesktopReplaceOperationResult(
            context.OperationId,
            context.CorrelationId,
            ActivityDeliveryStatus.AcknowledgementLost,
            FailureCode.AcknowledgementLost,
            occurredAt,
            null,
            null);
    }

    internal static DesktopReplaceOperationResult Acknowledged(
        ReplaceOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new DesktopReplaceOperationResult(
            result.Receipt.OperationId,
            result.Receipt.CorrelationId,
            ActivityDeliveryStatus.Acknowledged,
            result.Receipt.FailureCode,
            result.Receipt.OccurredAt,
            result.Receipt,
            result.UndoCapsule);
    }
}

public sealed record DesktopReplaceRecoveryResult(
    bool IsAvailable,
    DateTimeOffset? CapturedAt,
    bool IsTruncated,
    ImmutableArray<ReplaceRecoveryRecord> Records,
    ImmutableArray<UndoCapsuleId> UndoableCapsuleIds)
{
    public static DesktopReplaceRecoveryResult Available(
        ReplaceRecoverySnapshot snapshot)
        => Available(snapshot, []);

    public static DesktopReplaceRecoveryResult Available(
        ReplaceRecoverySnapshot snapshot,
        ImmutableArray<UndoCapsuleId> undoableCapsuleIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new DesktopReplaceRecoveryResult(
            true,
            snapshot.CapturedAt,
            snapshot.IsTruncated,
            snapshot.Records,
            undoableCapsuleIds);
    }

    public static DesktopReplaceRecoveryResult Unavailable { get; } =
        new(false, null, false, [], []);
}

public sealed record DesktopReplaceRecoveryItem(
    string Kind,
    string State,
    string Reason,
    string OperationId,
    string CorrelationId,
    string Participants,
    string Activities,
    string Capsule,
    string Timestamp,
    string Undo,
    bool IsRecoveryRequired,
    UndoCapsuleId? UndoCapsuleId,
    ActivityId? TargetActivityId,
    ActivityId? IncomingActivityId,
    DateTimeOffset? UndoExpiresAt,
    bool CanUndo);

public enum DesktopSemanticResumeAvailability
{
    Unknown,
    Unavailable,
    Available,
}

public interface IDesktopActivityService : IAsyncDisposable
{
    public event Action? Changed;

    public bool IsReady => true;

    public bool IsDestructiveReplaceAvailable => false;

    public bool SupportsSemanticResume(string activityKind);

    public DesktopActivitySnapshot CreateWorkspaceNote(
        string title,
        string text,
        ActivitySensitivity sensitivity);

    public ImmutableArray<DesktopActivitySnapshot> GetActivities();

    public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets();

    public ImmutableArray<DesktopActivityTargetSnapshot> GetRemoteWindowTargets(
        MirrorParticipantRole role)
    {
        if (role is not MirrorParticipantRole.ViewOnly
            and not MirrorParticipantRole.DriverEligible)
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return [];
    }

    public DesktopReplaceRecoveryResult GetReplaceRecoveryState() =>
        DesktopReplaceRecoveryResult.Unavailable;

    public ValueTask InitializeAsync(
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default);

    public ValueTask<OperationReceipt> MoveAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<OperationReceipt>(
            new PlatformNotSupportedException(
                "Semantic Move is not configured by this Activity service."));

    public ValueTask<DesktopReplaceTargetInventoryResult> GetReplaceTargetsAsync(
        ActivityId incomingActivityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DesktopReplaceTargetInventoryResult>(
            new PlatformNotSupportedException(
                "Replace target inventory is not configured by this Activity service."));

    public ValueTask<DesktopReplaceOperationResult> ReplaceAsync(
        ActivityId incomingActivityId,
        DesktopReplaceTargetSnapshot selectedTarget,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DesktopReplaceOperationResult>(
            new PlatformNotSupportedException(
                "Destructive Replace is not configured by this Activity service."));

    public ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleId capsuleId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<UndoReplaceResult>(
            new PlatformNotSupportedException(
                "Target-local Replace undo is not configured by this Activity service."));
}

public sealed class ActivityWorkspaceViewModel :
    INotifyPropertyChanged,
    IDisposable,
    IAsyncDisposable
{
    private readonly AsyncRelayCommand handoffCommand;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly AsyncRelayCommand moveCommand;
    private readonly AsyncRelayCommand refreshReplaceTargetsCommand;
    private readonly AsyncRelayCommand replaceCommand;
    private readonly AsyncRelayCommand targetLocalUndoCommand;
    private readonly IDesktopActivityService service;
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly RelayCommand createWorkspaceNoteCommand;
    private string creationStatus = string.Empty;
    private string draftText = string.Empty;
    private string draftTitle = string.Empty;
    private bool hasAcknowledgedReplace;
    private bool isBusy;
    private string receiptCorrelationId = string.Empty;
    private string receiptOccurredAt = string.Empty;
    private string receiptReason = string.Empty;
    private string receiptStatus = string.Empty;
    private string receiptSummary = string.Empty;
    private string replaceInventoryDescription =
        "Select an incoming Activity and authenticated target, then load purpose-scoped Replace targets.";
    private string replaceInventoryCapturedAt = string.Empty;
    private string replaceInventoryCoverage = string.Empty;
    private string replaceInventoryStatus = "REPLACE TARGETS NOT LOADED";
    private int replaceInventoryContextVersion;
    private string replaceOperationCapsule = string.Empty;
    private string replaceOperationCorrelationId = string.Empty;
    private string replaceOperationDescription = string.Empty;
    private string replaceOperationId = string.Empty;
    private string replaceOperationOccurredAt = string.Empty;
    private string replaceOperationReason = string.Empty;
    private string replaceOperationStatus = string.Empty;
    private string replaceOperationUndoExpiry = string.Empty;
    private string replaceRecoveryCapturedAt = string.Empty;
    private string replaceRecoveryCoverage = string.Empty;
    private string replaceRecoveryDescription =
        "Protected target-local Replace state has not been loaded.";
    private string replaceRecoveryStatus = "REPLACE RECOVERY STATE NOT LOADED";
    private bool hasAcknowledgedTargetLocalUndo;
    private DesktopReplaceRecoveryItem? selectedReplaceRecoveryItem;
    private string targetLocalUndoDescription =
        "Select an available committed Replace record and review its exact capsule binding.";
    private string targetLocalUndoOccurredAt = string.Empty;
    private string targetLocalUndoReason = string.Empty;
    private string targetLocalUndoStatus =
        "TARGET-LOCAL UNDO — SELECT AN AVAILABLE CAPSULE";
    private string undoDescription = string.Empty;
    private DesktopActivitySnapshot? selectedActivity;
    private DesktopActivityTargetSnapshot? selectedRemoteWindowTarget;
    private DesktopReplaceTargetSnapshot? selectedReplaceTarget;
    private DesktopActivityTargetSnapshot? selectedTarget;
    private MirrorParticipantRole remoteWindowTargetRole =
        MirrorParticipantRole.ViewOnly;
    private int disposed;
    private int serviceDisposed;

    internal ActivityWorkspaceViewModel(
        IDesktopActivityService service,
        IDesktopUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(dispatcher);
        this.service = service;
        this.dispatcher = dispatcher;
        createWorkspaceNoteCommand = new RelayCommand(
            CreateWorkspaceNote,
            CanCreateWorkspaceNote);
        handoffCommand = new AsyncRelayCommand(
            HandoffAsync,
            CanHandoff);
        moveCommand = new AsyncRelayCommand(
            MoveAsync,
            CanMove);
        refreshReplaceTargetsCommand = new AsyncRelayCommand(
            RefreshReplaceTargetsAsync,
            CanRefreshReplaceTargets);
        replaceCommand = new AsyncRelayCommand(
            ReplaceAsync,
            CanReplace);
        targetLocalUndoCommand = new AsyncRelayCommand(
            UndoReplaceAsync,
            CanUndoReplace);
        service.Changed += OnServiceChanged;
        Refresh();
        RefreshReplaceRecovery();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesktopActivitySnapshot> Activities { get; } = [];

    public ObservableCollection<DesktopActivityTargetSnapshot> Targets { get; } = [];

    public ObservableCollection<DesktopActivityTargetSnapshot> RemoteWindowTargets
    { get; } = [];

    public ObservableCollection<DesktopReplaceTargetSnapshot> ReplaceTargets { get; } = [];

    public ObservableCollection<DesktopReplaceRecoveryItem> ReplaceRecoveryItems { get; } = [];

    public ICommand CreateWorkspaceNoteCommand => createWorkspaceNoteCommand;

    public ICommand HandoffCommand => handoffCommand;

    public ICommand MoveCommand => moveCommand;

    public ICommand RefreshReplaceTargetsCommand => refreshReplaceTargetsCommand;

    public ICommand ReplaceCommand => replaceCommand;

    public ICommand TargetLocalUndoCommand => targetLocalUndoCommand;

    public string CreationStatus
    {
        get => creationStatus;
        private set => SetProperty(ref creationStatus, value);
    }

    public string DataDisclosure { get; } =
        "Sends the note title, sensitivity, and bounded plain-text note over the end-to-end encrypted control channel.";

    public string DegradationDescription { get; } =
        "Only workspace.note/v1 resumes semantically here. Flowspan does not transfer process memory, unsaved application internals, credentials, or unsupported app state.";

    public string DegradationStatus { get; } =
        "REMOTE WINDOW NOT AVAILABLE IN THIS BUILD";

    public string DraftText
    {
        get => draftText;
        set
        {
            if (SetProperty(ref draftText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                createWorkspaceNoteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DraftTitle
    {
        get => draftTitle;
        set
        {
            if (SetProperty(ref draftTitle, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                createWorkspaceNoteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsHandoffAvailable));
                OnPropertyChanged(nameof(IsMoveAvailable));
                OnPropertyChanged(nameof(IsReplaceConfirmationAvailable));
                OnPropertyChanged(nameof(IsReplaceInventoryAvailable));
                OnPropertyChanged(nameof(IsDestructiveReplaceAvailable));
                OnPropertyChanged(nameof(IsTargetLocalUndoConfirmationAvailable));
                OnPropertyChanged(nameof(IsTargetLocalUndoAvailable));
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                createWorkspaceNoteCommand.NotifyCanExecuteChanged();
                handoffCommand.NotifyCanExecuteChanged();
                moveCommand.NotifyCanExecuteChanged();
                refreshReplaceTargetsCommand.NotifyCanExecuteChanged();
                replaceCommand.NotifyCanExecuteChanged();
                targetLocalUndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsHandoffAvailable => CanHandoff();

    public bool IsMoveAvailable => CanMove();

    public bool IsNoteCreationAvailable => CanCreateWorkspaceNote();

    public bool IsReady => service.IsReady;

    public DesktopSemanticResumeAvailability SelectedSemanticResumeAvailability =>
        SelectedActivity is { } activity
            ? GetSemanticResumeAvailability(activity)
            : DesktopSemanticResumeAvailability.Unavailable;

    public bool IsSelectedSemanticResumeAvailable =>
        SelectedSemanticResumeAvailability
        == DesktopSemanticResumeAvailability.Available;

    public bool IsPreviewVisible =>
        SelectedActivity is not null && SelectedTarget is not null;

    public bool IsMovePreviewVisible => IsPreviewVisible;

    public bool IsReplacePreviewVisible =>
        SelectedActivity is not null
        && SelectedTarget is not null
        && SelectedReplaceTarget is not null
        && Activities.Contains(SelectedActivity)
        && Targets.Contains(SelectedTarget)
        && ReplaceTargets.Contains(SelectedReplaceTarget)
        && SelectedReplaceTarget.DeviceId == SelectedTarget.DeviceId;

    public bool IsReplaceConfirmationAvailable => IsReplacePreviewVisible && !IsBusy;

    public bool IsReplaceInventoryAvailable => CanRefreshReplaceTargets();

    public bool IsDestructiveReplaceAvailable =>
        service.IsDestructiveReplaceAvailable
        && HasAcknowledgedReplace
        && IsReplaceConfirmationAvailable;

    public bool IsTargetLocalUndoConfirmationAvailable =>
        SelectedReplaceRecoveryItem is { CanUndo: true }
        && ReplaceRecoveryItems.Contains(SelectedReplaceRecoveryItem)
        && !IsBusy;

    public bool IsTargetLocalUndoAvailable =>
        HasAcknowledgedTargetLocalUndo
        && IsTargetLocalUndoConfirmationAvailable;

    public bool HasAcknowledgedTargetLocalUndo
    {
        get => hasAcknowledgedTargetLocalUndo;
        set
        {
            bool accepted = value && IsTargetLocalUndoConfirmationAvailable;
            if (SetProperty(ref hasAcknowledgedTargetLocalUndo, accepted))
            {
                if (!IsBusy)
                {
                    ApplyTargetLocalUndoSelectionState();
                }

                OnPropertyChanged(nameof(IsTargetLocalUndoAvailable));
                OnPropertyChanged(nameof(TargetLocalUndoStatus));
                targetLocalUndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DesktopReplaceRecoveryItem? SelectedReplaceRecoveryItem
    {
        get => selectedReplaceRecoveryItem;
        set
        {
            if (SetProperty(ref selectedReplaceRecoveryItem, value))
            {
                HasAcknowledgedTargetLocalUndo = false;
                OnTargetLocalUndoSelectionChanged();
            }
        }
    }

    public string TargetLocalUndoConfirmationDescription =>
        SelectedReplaceRecoveryItem is { } item
            ? $"Undo capsule {item.Capsule}. {item.Activities}. Exact expiry: {item.UndoExpiresAt:O}. Restore is allowed only while the incoming Activity is still the exact current replacement."
            : "Select one exact available target-local Replace record before confirming undo.";

    public string TargetLocalUndoConfirmationAutomationName =>
        SelectedReplaceRecoveryItem is { } item
            ? $"Confirm target-local undo for capsule {item.Capsule}"
            : "Confirm the selected target-local undo capsule";

    public string TargetLocalUndoDescription
    {
        get => targetLocalUndoDescription;
        private set => SetProperty(ref targetLocalUndoDescription, value);
    }

    public string TargetLocalUndoOccurredAt
    {
        get => targetLocalUndoOccurredAt;
        private set => SetProperty(ref targetLocalUndoOccurredAt, value);
    }

    public string TargetLocalUndoReason
    {
        get => targetLocalUndoReason;
        private set => SetProperty(ref targetLocalUndoReason, value);
    }

    public string TargetLocalUndoStatus
    {
        get => targetLocalUndoStatus;
        private set => SetProperty(ref targetLocalUndoStatus, value);
    }

    public bool IsReceiptVisible => ReceiptStatus.Length > 0;

    public string PreviewDescription => IsPreviewVisible
        ? $"Create a native {SelectedActivity!.Kind} copy on {SelectedTarget!.DisplayName}. The source Activity remains active on this device. Sensitivity: {SelectedActivity.Sensitivity}."
        : "Select one local Activity and one authenticated target to review a handoff.";

    public string PreviewStatus => IsPreviewVisible
        ? "SEMANTIC HANDOFF — SOURCE STAYS OPEN"
        : "HANDOFF PREVIEW NOT READY";

    public string MovePreviewDescription => IsMovePreviewVisible
        ? $"The target {SelectedTarget!.DisplayName} resumes {SelectedActivity!.Kind} first. Flowspan closes the source only after a verified target acknowledgement; it remains active after rejection, failure, or an uncertain outcome. Sensitivity: {SelectedActivity.Sensitivity}."
        : "Select one local Activity and one authenticated target to review a move.";

    public string MovePreviewStatus => IsMovePreviewVisible
        ? "SEMANTIC MOVE — SOURCE CLOSES AFTER TARGET ACKNOWLEDGEMENT"
        : "MOVE PREVIEW NOT READY";

    public string ReplaceIncomingDescription => IsReplacePreviewVisible
        ? $"Incoming: {SelectedActivity!.Title} ({SelectedActivity.Kind}). The source Activity remains active on this device."
        : "Select an incoming local Activity, an authenticated device, and one Replace target.";

    public string ReplaceConfirmationAutomationName => IsReplacePreviewVisible
        ? $"Confirm replacing {SelectedReplaceTarget!.Title} on {SelectedTarget!.DisplayName} with {SelectedActivity!.Title}"
        : "Confirm the exact Replace preview";

    public string ReplaceConfirmationDescription => IsReplacePreviewVisible
        ? $"I understand that {SelectedReplaceTarget!.Title} on {SelectedTarget!.DisplayName} would be replaced by {SelectedActivity!.Title}, and that activation must remain blocked unless the target first stores a 15-minute undo capsule."
        : "Load and select an exact target snapshot before confirming.";

    public string ReplacePreviewStatus => IsReplacePreviewVisible
        ? "REPLACE PREVIEW — CONFIRMATION REQUIRED"
        : "REPLACE PREVIEW NOT READY";

    public string ReplaceActivationStatus => !IsReplacePreviewVisible
        ? "REPLACE ACTIVATION LOCKED — LOAD AND SELECT AN EXACT TARGET SNAPSHOT"
        : HasAcknowledgedReplace
            ? IsDestructiveReplaceAvailable
                ? "PREVIEW CONFIRMED — DESTRUCTIVE REPLACE READY"
                : "PREVIEW CONFIRMED — DESTRUCTIVE REPLACE NOT ACTIVATED"
            : "CONFIRMATION REQUIRED — REVIEW THE EXACT TARGET SNAPSHOT";

    public string ReplaceTargetDescription => IsReplacePreviewVisible
        ? $"Replaced target: {SelectedReplaceTarget!.Title} ({SelectedReplaceTarget.Kind}) on {SelectedTarget!.DisplayName}, placement {SelectedReplaceTarget.PlacementSlot}, revision {SelectedReplaceTarget.Revision}, descriptor digest {SelectedReplaceTarget.DescriptorDigest}."
        : "No exact target snapshot is selected.";

    public bool HasAcknowledgedReplace
    {
        get => hasAcknowledgedReplace;
        set
        {
            bool accepted = value && IsReplaceConfirmationAvailable;
            if (SetProperty(ref hasAcknowledgedReplace, accepted))
            {
                OnPropertyChanged(nameof(ReplaceActivationStatus));
                OnPropertyChanged(nameof(IsDestructiveReplaceAvailable));
                replaceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ReceiptCorrelationId
    {
        get => receiptCorrelationId;
        private set => SetProperty(ref receiptCorrelationId, value);
    }

    public string ReceiptOccurredAt
    {
        get => receiptOccurredAt;
        private set => SetProperty(ref receiptOccurredAt, value);
    }

    public string ReceiptReason
    {
        get => receiptReason;
        private set => SetProperty(ref receiptReason, value);
    }

    public string ReceiptStatus
    {
        get => receiptStatus;
        private set
        {
            if (SetProperty(ref receiptStatus, value))
            {
                OnPropertyChanged(nameof(IsReceiptVisible));
            }
        }
    }

    public string ReceiptSummary
    {
        get => receiptSummary;
        private set => SetProperty(ref receiptSummary, value);
    }

    public string ReplaceInventoryDescription
    {
        get => replaceInventoryDescription;
        private set => SetProperty(ref replaceInventoryDescription, value);
    }

    public string ReplaceInventoryCapturedAt
    {
        get => replaceInventoryCapturedAt;
        private set => SetProperty(ref replaceInventoryCapturedAt, value);
    }

    public string ReplaceInventoryCoverage
    {
        get => replaceInventoryCoverage;
        private set => SetProperty(ref replaceInventoryCoverage, value);
    }

    public string ReplaceInventoryStatus
    {
        get => replaceInventoryStatus;
        private set => SetProperty(ref replaceInventoryStatus, value);
    }

    public bool IsReplaceOperationResultVisible => ReplaceOperationStatus.Length > 0;

    public string ReplaceOperationCapsule
    {
        get => replaceOperationCapsule;
        private set => SetProperty(ref replaceOperationCapsule, value);
    }

    public string ReplaceOperationCorrelationId
    {
        get => replaceOperationCorrelationId;
        private set => SetProperty(ref replaceOperationCorrelationId, value);
    }

    public string ReplaceOperationDescription
    {
        get => replaceOperationDescription;
        private set => SetProperty(ref replaceOperationDescription, value);
    }

    public string ReplaceOperationId
    {
        get => replaceOperationId;
        private set => SetProperty(ref replaceOperationId, value);
    }

    public string ReplaceOperationOccurredAt
    {
        get => replaceOperationOccurredAt;
        private set => SetProperty(ref replaceOperationOccurredAt, value);
    }

    public string ReplaceOperationReason
    {
        get => replaceOperationReason;
        private set => SetProperty(ref replaceOperationReason, value);
    }

    public string ReplaceOperationStatus
    {
        get => replaceOperationStatus;
        private set
        {
            if (SetProperty(ref replaceOperationStatus, value))
            {
                OnPropertyChanged(nameof(IsReplaceOperationResultVisible));
            }
        }
    }

    public string ReplaceOperationUndoExpiry
    {
        get => replaceOperationUndoExpiry;
        private set => SetProperty(ref replaceOperationUndoExpiry, value);
    }

    public string ReplaceRecoveryCapturedAt
    {
        get => replaceRecoveryCapturedAt;
        private set => SetProperty(ref replaceRecoveryCapturedAt, value);
    }

    public string ReplaceRecoveryCoverage
    {
        get => replaceRecoveryCoverage;
        private set => SetProperty(ref replaceRecoveryCoverage, value);
    }

    public string ReplaceRecoveryDescription
    {
        get => replaceRecoveryDescription;
        private set => SetProperty(ref replaceRecoveryDescription, value);
    }

    public string ReplaceRecoveryStatus
    {
        get => replaceRecoveryStatus;
        private set => SetProperty(ref replaceRecoveryStatus, value);
    }

    public DesktopActivitySnapshot? SelectedActivity
    {
        get => selectedActivity;
        set
        {
            if (SetProperty(ref selectedActivity, value))
            {
                InvalidateReplaceInventory();
                OnPreviewChanged();
            }
        }
    }

    public DesktopActivityTargetSnapshot? SelectedTarget
    {
        get => selectedTarget;
        set
        {
            if (SetProperty(ref selectedTarget, value))
            {
                InvalidateReplaceInventory();
                OnPreviewChanged();
            }
        }
    }

    public DesktopActivityTargetSnapshot? SelectedRemoteWindowTarget
    {
        get => selectedRemoteWindowTarget;
        set
        {
            DesktopActivityTargetSnapshot? accepted = value is null
                ? null
                : RemoteWindowTargets.FirstOrDefault(
                    target => target.DeviceId == value.DeviceId);
            SetProperty(ref selectedRemoteWindowTarget, accepted);
        }
    }

    public MirrorParticipantRole RemoteWindowTargetRole
    {
        get => remoteWindowTargetRole;
        set
        {
            if (value is not MirrorParticipantRole.ViewOnly
                and not MirrorParticipantRole.DriverEligible)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (remoteWindowTargetRole == value)
            {
                return;
            }

            DeviceId? selectedTargetId = SelectedRemoteWindowTarget?.DeviceId;
            remoteWindowTargetRole = value;
            RefreshRemoteWindowTargets(selectedTargetId);
            OnPropertyChanged();
        }
    }

    public DesktopReplaceTargetSnapshot? SelectedReplaceTarget
    {
        get => selectedReplaceTarget;
        set
        {
            if (SetProperty(ref selectedReplaceTarget, value))
            {
                HasAcknowledgedReplace = false;
                OnReplacePreviewChanged();
            }
        }
    }

    public string UndoDescription
    {
        get => undoDescription;
        private set => SetProperty(ref undoDescription, value);
    }

    public void CreateWorkspaceNote()
    {
        if (!CanCreateWorkspaceNote())
        {
            return;
        }

        try
        {
            DesktopActivitySnapshot created = service.CreateWorkspaceNote(
                DraftTitle,
                DraftText,
                ActivitySensitivity.Normal);
            Refresh(created.ActivityId, SelectedTarget?.DeviceId);
            SelectedActivity = Activities.FirstOrDefault(
                item => item.ActivityId == created.ActivityId);
            DraftTitle = string.Empty;
            DraftText = string.Empty;
            CreationStatus = "PORTABLE NOTE READY";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CreationStatus = "NOTE COULD NOT BE CREATED — check the title and the 16 KiB plain-text limit.";
        }
    }

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await service.InitializeAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CreationStatus =
                "ACTIVITY WORKSPACE UNAVAILABLE — protected identity or Trust is not ready.";
        }

        Refresh();
        RefreshReplaceRecovery();
        OnPropertyChanged(nameof(IsNoteCreationAvailable));
        OnPropertyChanged(nameof(SelectedSemanticResumeAvailability));
        OnPropertyChanged(nameof(IsSelectedSemanticResumeAvailable));
        OnPropertyChanged(nameof(IsHandoffAvailable));
        OnPropertyChanged(nameof(IsMoveAvailable));
        createWorkspaceNoteCommand.NotifyCanExecuteChanged();
        handoffCommand.NotifyCanExecuteChanged();
        moveCommand.NotifyCanExecuteChanged();
    }

    public async Task HandoffAsync()
    {
        if (!CanHandoff())
        {
            return;
        }

        DesktopActivitySnapshot activity = SelectedActivity!;
        DesktopActivityTargetSnapshot target = SelectedTarget!;
        IsBusy = true;
        ClearReceipt();
        try
        {
            OperationReceipt receipt = await service.HandoffAsync(
                activity.ActivityId,
                target.DeviceId,
                lifetimeCancellation.Token).ConfigureAwait(true);
            ApplyReceipt(receipt, target.DisplayName);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReceiptStatus = "HANDOFF UNAVAILABLE";
            ReceiptSummary =
                $"{target.DisplayName} did not return a verified receipt. The source remains available; retry after the authenticated local connection recovers.";
            ReceiptReason = "peer-unavailable";
            ReceiptCorrelationId = string.Empty;
            ReceiptOccurredAt = string.Empty;
            UndoDescription =
                "NO UNDO REQUIRED — the handoff did not change the source Activity.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MoveAsync()
    {
        if (!CanMove())
        {
            return;
        }

        DesktopActivitySnapshot activity = SelectedActivity!;
        DesktopActivityTargetSnapshot target = SelectedTarget!;
        IsBusy = true;
        ClearReceipt();
        try
        {
            OperationReceipt receipt = await service.MoveAsync(
                activity.ActivityId,
                target.DeviceId,
                lifetimeCancellation.Token).ConfigureAwait(true);
            ApplyReceipt(receipt, target.DisplayName);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReceiptStatus = "MOVE UNAVAILABLE";
            ReceiptSummary =
                $"{target.DisplayName} did not return a verified receipt. The source remains active; retry after the authenticated local connection recovers.";
            ReceiptReason = "peer-unavailable";
            ReceiptCorrelationId = string.Empty;
            ReceiptOccurredAt = string.Empty;
            UndoDescription =
                "NO UNDO REQUIRED — the move did not close the source Activity.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshReplaceTargetsAsync()
    {
        if (!CanHandoff())
        {
            return;
        }

        DesktopActivitySnapshot activity = SelectedActivity!;
        DesktopActivityTargetSnapshot target = SelectedTarget!;
        int contextVersion = Volatile.Read(ref replaceInventoryContextVersion);
        DesktopReplaceTargetSnapshot? previousTarget = SelectedReplaceTarget;
        ClearReplaceInventorySnapshot();
        ReplaceInventoryStatus = "LOADING REPLACE TARGETS";
        ReplaceInventoryDescription =
            $"Requesting payload-free {activity.Kind} choices from {target.DisplayName}. No Replace request is being sent.";
        IsBusy = true;
        try
        {
            DesktopReplaceTargetInventoryResult result =
                await service.GetReplaceTargetsAsync(
                    activity.ActivityId,
                    target.DeviceId,
                    lifetimeCancellation.Token).ConfigureAwait(true);
            if (contextVersion != Volatile.Read(ref replaceInventoryContextVersion))
            {
                return;
            }

            ApplyReplaceInventoryResult(result, previousTarget);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (contextVersion == Volatile.Read(ref replaceInventoryContextVersion))
            {
                ClearReplaceInventorySnapshot();
                ReplaceInventoryStatus = "REPLACE TARGETS UNAVAILABLE — RETRY";
                ReplaceInventoryDescription =
                    "The authenticated local connection did not return a verified target inventory. Reconnect and retry. No Replace request was sent.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReplaceAsync()
    {
        if (!CanReplace())
        {
            return;
        }

        DesktopActivitySnapshot activity = SelectedActivity!;
        DesktopActivityTargetSnapshot device = SelectedTarget!;
        DesktopReplaceTargetSnapshot target = SelectedReplaceTarget!;
        IsBusy = true;
        ReplaceOperationStatus = "REPLACE PENDING — DUPLICATE DISABLED";
        ReplaceOperationDescription =
            $"Revalidating the exact snapshot for {target.Title} on {device.DisplayName}, then waiting for one authenticated receipt and undo capsule. The source remains active.";
        ReplaceOperationReason = "operation-in-progress";
        ReplaceOperationId = string.Empty;
        ReplaceOperationCorrelationId = string.Empty;
        ReplaceOperationOccurredAt = string.Empty;
        ReplaceOperationCapsule = string.Empty;
        ReplaceOperationUndoExpiry = string.Empty;
        try
        {
            DesktopReplaceOperationResult result = await service.ReplaceAsync(
                activity.ActivityId,
                target,
                lifetimeCancellation.Token).ConfigureAwait(true);
            ApplyReplaceOperationResult(result, device.DisplayName);
            if (result.IsSuccess)
            {
                ClearReplaceInventorySnapshot();
                ReplaceInventoryStatus = "REPLACE COMMITTED — REFRESH REQUIRED";
                ReplaceInventoryDescription =
                    "The selected target was replaced. Load a fresh payload-free inventory before preparing another Replace.";
            }
            else if (result.FailureCode == FailureCode.RevisionConflict)
            {
                ClearReplaceInventorySnapshot();
                ReplaceInventoryStatus = "TARGET CHANGED — REFRESH REQUIRED";
                ReplaceInventoryDescription =
                    "The exact target ID, revision, descriptor digest, kind, or placement changed at send time. Load fresh inventory, select again, and confirm again. No destructive request was sent.";
            }
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReplaceOperationStatus =
                "REPLACE OUTCOME UNAVAILABLE — INSPECT TARGET RECOVERY";
            ReplaceOperationDescription =
                "The destructive application port did not return a verified outcome. The target may have crossed the commit boundary; the source remains active. Inspect target recovery before any new operation. Flowspan will not guess or automatically retry.";
            ReplaceOperationReason = "internal-failure";
            ReplaceOperationId = string.Empty;
            ReplaceOperationCorrelationId = string.Empty;
            ReplaceOperationOccurredAt = string.Empty;
            ReplaceOperationCapsule = string.Empty;
            ReplaceOperationUndoExpiry = string.Empty;
        }
        finally
        {
            HasAcknowledgedReplace = false;
            IsBusy = false;
        }
    }

    public async Task UndoReplaceAsync()
    {
        if (!CanUndoReplace())
        {
            return;
        }

        UndoCapsuleId capsuleId = SelectedReplaceRecoveryItem!.UndoCapsuleId!;
        IsBusy = true;
        TargetLocalUndoStatus = "TARGET-LOCAL UNDO PENDING — DO NOT RETRY";
        TargetLocalUndoDescription =
            $"The protected journal reserved capsule {capsuleId}. Waiting for one exact local restore outcome; duplicate action is disabled.";
        TargetLocalUndoReason = "operation-in-progress";
        TargetLocalUndoOccurredAt = string.Empty;
        try
        {
            UndoReplaceResult result = await service.UndoReplaceAsync(
                capsuleId,
                lifetimeCancellation.Token).ConfigureAwait(true);
            RefreshReplaceRecovery();
            ApplyTargetLocalUndoResult(result);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RefreshReplaceRecovery();
            TargetLocalUndoStatus =
                "TARGET-LOCAL UNDO OUTCOME UNAVAILABLE — INSPECT RECOVERY";
            TargetLocalUndoDescription =
                "The application port did not return a verified outcome. Inspect the protected pending/terminal recovery record before any further action; Flowspan will not guess or repeat Adapter work.";
            TargetLocalUndoReason = "undo-unavailable";
            TargetLocalUndoOccurredAt = string.Empty;
        }
        finally
        {
            HasAcknowledgedTargetLocalUndo = false;
            IsBusy = false;
        }
    }

    private void ApplyReplaceInventoryResult(
        DesktopReplaceTargetInventoryResult result,
        DesktopReplaceTargetSnapshot? previousTarget)
    {
        Replace(ReplaceTargets, result.IsSuccess ? result.Targets : []);
        if (!result.IsSuccess)
        {
            ApplyReplaceInventoryFailure(result.FailureCode);
            return;
        }

        ReplaceInventoryCapturedAt = result.CapturedAt?.ToString("O")
            ?? string.Empty;
        ReplaceInventoryCoverage = result.IsTruncated
            ? "SHOWING FIRST 64 ELIGIBLE TARGETS — INVENTORY TRUNCATED"
            : $"{ReplaceTargets.Count} ELIGIBLE REPLACE TARGETS";
        if (previousTarget is not null)
        {
            ReconcileRefreshedReplaceTarget(previousTarget);
        }
        else if (ReplaceTargets.Count == 0)
        {
            ReplaceInventoryStatus = "NO ELIGIBLE REPLACE TARGETS";
            ReplaceInventoryDescription =
                "The peer returned no active same-kind target that can be preserved for undo. No Replace request was sent.";
        }
        else
        {
            ReplaceInventoryStatus = "REPLACE TARGETS READY — SELECT ONE";
            ReplaceInventoryDescription =
                "Choose one exact target snapshot to build the destructive preview. No Replace request has been sent.";
        }
    }

    private void ApplyReplaceOperationResult(
        DesktopReplaceOperationResult result,
        string targetDisplayName)
    {
        ReplaceOperationId = result.OperationId?.ToString() ?? string.Empty;
        ReplaceOperationCorrelationId = result.CorrelationId?.ToString()
            ?? string.Empty;
        ReplaceOperationOccurredAt = result.OccurredAt.ToString("O");
        ReplaceOperationReason = ToReasonCode(result.FailureCode);
        ReplaceOperationCapsule = result.UndoCapsule?.Id.ToString() ?? string.Empty;
        ReplaceOperationUndoExpiry = result.UndoCapsule?.ExpiresAt.ToString("O")
            ?? string.Empty;
        if (result.DeliveryStatus == ActivityDeliveryStatus.AcknowledgementLost)
        {
            ReplaceOperationStatus = "REPLACE OUTCOME UNCERTAIN — DO NOT RETRY";
            ReplaceOperationDescription =
                $"{targetDisplayName} may have committed Replace and stored an undo capsule, but its authenticated acknowledgement was lost. The source remains active. Inspect target recovery before any new operation; Flowspan will not invent a new Operation ID or automatically retry.";
            return;
        }

        if (result.DeliveryStatus == ActivityDeliveryStatus.NotDelivered)
        {
            (ReplaceOperationStatus, ReplaceOperationDescription) =
                result.FailureCode switch
                {
                    FailureCode.RevisionConflict => (
                        "REPLACE NOT SENT — TARGET CHANGED",
                        "Send-time revalidation did not match the confirmed target snapshot. The source remains active and the target was not mutated. Load fresh inventory, select again, and confirm again."),
                    FailureCode.CapabilityDenied => (
                        "REPLACE NOT SENT — REVIEW TRUST",
                        "The current activity.receive or activity.replace grant is unavailable. The source remains active and no destructive request was sent. Review Trust on both devices, then refresh."),
                    FailureCode.ActivityNotFound => (
                        "REPLACE NOT SENT — INCOMING ACTIVITY CHANGED",
                        "The incoming Activity is no longer the exact active source. No destructive request was sent; select an active Activity and load fresh inventory."),
                    FailureCode.UndoUnavailable => (
                        "REPLACE LOCKED — PROTECTED RECOVERY UNAVAILABLE",
                        "Protected Replace recovery is unavailable or unresolved on this device. The source remains active and no destructive request was sent."),
                    _ => (
                        "REPLACE NOT DELIVERED — REFRESH AND RETRY",
                        $"No destructive Replace request was delivered to {targetDisplayName}. The source remains active. Recover the authenticated connection and load fresh inventory before trying again."),
                };
            return;
        }

        OperationReceipt? receipt = result.Receipt;
        if (receipt is null)
        {
            ReplaceOperationStatus =
                "REPLACE RESULT INVALID — INSPECT TARGET RECOVERY";
            ReplaceOperationDescription =
                "The acknowledged response contained no verified receipt. The source remains active; inspect target recovery and do not retry.";
            return;
        }

        if (receipt.FailureCode == FailureCode.OperationInProgress)
        {
            ReplaceOperationStatus =
                "REPLACE BLOCKED BY TARGET RECOVERY — DO NOT RETRY";
            ReplaceOperationDescription =
                $"{targetDisplayName} already has an unresolved protected Replace or undo boundary. This request did not mutate the target. Inspect target recovery and resolve the existing boundary before any new operation.";
            return;
        }

        if (receipt.Status == OperationStatus.Committed
            && result.UndoCapsule is null)
        {
            ReplaceOperationStatus =
                "REPLACE RESULT INVALID — INSPECT TARGET RECOVERY";
            ReplaceOperationDescription =
                "The acknowledged committed response contained no verified undo capsule. The source remains active; inspect target recovery and do not retry.";
            return;
        }

        (ReplaceOperationStatus, ReplaceOperationDescription) = receipt.Status switch
        {
            OperationStatus.Committed => (
                "REPLACE COMMITTED",
                $"{targetDisplayName} stored the exact undo capsule before installing the incoming Activity. The source remains active. The capsule and expiry below are also visible in target recovery."),
            OperationStatus.Recovering => (
                "REPLACE OUTCOME REQUIRES TARGET RECOVERY — DO NOT RETRY",
                $"{targetDisplayName} returned a recovery-required receipt. The source remains active. Inspect target recovery before any new operation; Flowspan will not retry Adapter work."),
            OperationStatus.Rejected => (
                "REPLACE REJECTED",
                $"{targetDisplayName} returned a verified rejection before commit. The source remains active; review the named reason and refresh the exact preview."),
            _ => (
                "REPLACE FAILED",
                $"{targetDisplayName} returned a verified failure. The source remains active; inspect target recovery when indicated and refresh before another attempt."),
        };
    }

    private void RefreshReplaceRecovery()
    {
        DesktopReplaceRecoveryResult result;
        try
        {
            result = service.GetReplaceRecoveryState();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = DesktopReplaceRecoveryResult.Unavailable;
        }

        if (!result.IsAvailable)
        {
            SelectedReplaceRecoveryItem = null;
            ReplaceRecoveryItems.Clear();
            ReplaceRecoveryCapturedAt = string.Empty;
            ReplaceRecoveryCoverage = "RECOVERY RECORD COUNT UNAVAILABLE";
            ReplaceRecoveryStatus =
                "REPLACE RECOVERY STATE UNAVAILABLE — REPLACE LOCKED";
            ReplaceRecoveryDescription =
                "The protected target-local Replace store could not be opened. Handoff and Move remain available, but do not activate Replace. Check the current-user credential store and local Flowspan data permissions, then restart.";
            return;
        }

        HashSet<UndoCapsuleId> undoableCapsules = result.UndoableCapsuleIds.ToHashSet();
        SelectedReplaceRecoveryItem = null;
        Replace(
            ReplaceRecoveryItems,
            result.Records
                .Select(record => CreateReplaceRecoveryItem(
                    record,
                    record.CapsuleId is not null
                    && undoableCapsules.Contains(record.CapsuleId)))
                .ToImmutableArray());
        ReplaceRecoveryCapturedAt = result.CapturedAt?.ToString("O")
            ?? string.Empty;
        ReplaceRecoveryCoverage = result.IsTruncated
            ? "SHOWING FIRST 64 RECORDS — UNRESOLVED STATE PRIORITIZED — HISTORY TRUNCATED"
            : $"{ReplaceRecoveryItems.Count} TARGET-LOCAL REPLACE / UNDO RECORDS";
        int recoveryRequired = result.Records.Count(
            static record => record.IsRecoveryRequired);
        if (recoveryRequired > 0)
        {
            ReplaceRecoveryStatus =
                $"REPLACE RECOVERY REQUIRED — {recoveryRequired} UNRESOLVED";
            ReplaceRecoveryDescription =
                "Inspect the opaque IDs and both devices before retrying. This read-only surface does not repeat Replace or undo Adapter work.";
        }
        else if (result.Records.IsEmpty)
        {
            ReplaceRecoveryStatus = "NO TARGET-LOCAL REPLACE HISTORY";
            ReplaceRecoveryDescription =
                "No protected Replace or undo records are stored on this device. Replace still requires an exact peer inventory, explicit confirmation, send-time revalidation, and current Trust on both devices.";
        }
        else if (ReplaceRecoveryItems.Any(static item => item.CanUndo))
        {
            int available = ReplaceRecoveryItems.Count(static item => item.CanUndo);
            ReplaceRecoveryStatus =
                $"TARGET-LOCAL UNDO AVAILABLE — {available} EXACT CAPSULES";
            ReplaceRecoveryDescription =
                "Select one exact committed Replace record, review both opaque Activity IDs and its expiry, then confirm one target-local semantic restore. A new Replace remains independently gated by fresh inventory and Trust.";
        }
        else
        {
            ReplaceRecoveryStatus =
                "TARGET-LOCAL REPLACE HISTORY — NO UNDO ACTION";
            ReplaceRecoveryDescription =
                "Recorded outcomes and capsule state are shown without Activity content. No record is both unattempted and the exact current unexpired replacement; a new Replace still requires a fresh exact preview.";
        }
    }

    private static DesktopReplaceRecoveryItem CreateReplaceRecoveryItem(
        ReplaceRecoveryRecord record,
        bool canUndo)
    {
        string kind = record.Kind switch
        {
            ReplaceRecoveryOperationKind.Replace => "TARGET-LOCAL REPLACE",
            ReplaceRecoveryOperationKind.Undo => "TARGET-LOCAL UNDO",
            _ => "TARGET-LOCAL OPERATION",
        };
        string state = record.JournalState switch
        {
            ReplaceRecoveryJournalState.Pending =>
                "PENDING — RECOVERY REQUIRED",
            _ when record.Status == OperationStatus.Recovering =>
                "RECORDED RECOVERING — OUTCOME UNCERTAIN",
            _ => record.Status switch
            {
                OperationStatus.Committed => "COMMITTED",
                OperationStatus.CommittedWithWarning => "COMMITTED WITH WARNING",
                OperationStatus.Rejected => "REJECTED",
                OperationStatus.Failed => "FAILED",
                _ => "OUTCOME UNAVAILABLE",
            },
        };
        string participants = string.Join(
            " → ",
            record.ReplaceSourceDeviceId is not null
                ? $"Replace source device {record.ReplaceSourceDeviceId}"
                : "SOURCE DEVICE NOT RECORDED",
            record.ReplaceTargetDeviceId is not null
                ? $"target device {record.ReplaceTargetDeviceId}"
                : "TARGET DEVICE NOT RECORDED");
        string activities = string.Join(
            " ← ",
            record.TargetActivityId is not null
                ? $"Target Activity {record.TargetActivityId}"
                : "TARGET ACTIVITY NOT RECORDED",
            record.IncomingActivityId is not null
                ? $"incoming Activity {record.IncomingActivityId}"
                : "INCOMING ACTIVITY NOT RECORDED");
        string timestamp = record.TimestampKind switch
        {
            ReplaceRecoveryTimestampKind.Outcome =>
                $"Outcome recorded: {record.RecordedAt:O}",
            ReplaceRecoveryTimestampKind.CapsuleCaptured =>
                $"Undo capsule captured: {record.RecordedAt:O}",
            _ => "TIME NOT RECORDED — pre-capture pending boundary.",
        };
        string undo = record.UndoAvailability switch
        {
            ReplaceUndoAvailability.Available when canUndo =>
                $"UNDO AVAILABLE — EXPIRES {record.UndoExpiresAt:O} — SELECT AND CONFIRM THIS EXACT CAPSULE",
            ReplaceUndoAvailability.Available =>
                $"CAPSULE UNCONSUMED AT SNAPSHOT — EXPIRES {record.UndoExpiresAt:O} — LOCAL UNDO LOCKED: EXACT CURRENT REPLACEMENT NOT PROVEN",
            ReplaceUndoAvailability.Expired =>
                $"UNDO EXPIRED AT {record.UndoExpiresAt:O}",
            ReplaceUndoAvailability.PendingOperation =>
                $"UNDO / REPLACE OUTCOME PENDING — EXPIRY {record.UndoExpiresAt:O}",
            ReplaceUndoAvailability.Consumed => "UNDO ALREADY CONSUMED",
            _ when record.UndoExpiresAt is not null =>
                $"UNDO NOT AVAILABLE FOR THIS RECORD — CAPSULE EXPIRY {record.UndoExpiresAt:O}",
            _ => "UNDO NOT AVAILABLE FOR THIS RECORD",
        };
        return new DesktopReplaceRecoveryItem(
            kind,
            state,
            ToReasonCode(record.FailureCode),
            record.OperationId.ToString(),
            record.CorrelationId?.ToString()
                ?? "NOT RECORDED — NO VALUE IN PROTECTED STATE",
            participants,
            activities,
            record.CapsuleId?.ToString()
                ?? "NO CAPSULE ID RECORDED",
            timestamp,
            undo,
            record.IsRecoveryRequired,
            record.CapsuleId,
            record.TargetActivityId,
            record.IncomingActivityId,
            record.UndoExpiresAt,
            canUndo);
    }

    private void ReconcileRefreshedReplaceTarget(
        DesktopReplaceTargetSnapshot previousTarget)
    {
        DesktopReplaceTargetSnapshot? refreshedTarget = ReplaceTargets
            .FirstOrDefault(item => item.ActivityId == previousTarget.ActivityId);
        if (refreshedTarget is null
            || refreshedTarget.Revision != previousTarget.Revision
            || !StringComparer.Ordinal.Equals(
                refreshedTarget.DescriptorDigest,
                previousTarget.DescriptorDigest))
        {
            ReplaceInventoryStatus =
                "TARGET CHANGED — REVIEW REFRESHED INVENTORY";
            ReplaceInventoryDescription = refreshedTarget is null
                ? "The previously selected target is no longer eligible. Select a fresh target snapshot and confirm again. No Replace request was sent."
                : "The selected target revision or descriptor digest changed. Select the refreshed snapshot and confirm again. No Replace request was sent.";
            return;
        }

        SelectedReplaceTarget = refreshedTarget;
        ReplaceInventoryStatus = "TARGETS REFRESHED — CONFIRM AGAIN";
        ReplaceInventoryDescription =
            "The exact target snapshot is still present. Review it and confirm again; no Replace request was sent.";
    }

    private void ClearReplaceInventorySnapshot()
    {
        SelectedReplaceTarget = null;
        ReplaceTargets.Clear();
        ReplaceInventoryCapturedAt = string.Empty;
        ReplaceInventoryCoverage = string.Empty;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        service.Changed -= OnServiceChanged;
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (Interlocked.Exchange(ref serviceDisposed, 1) == 0)
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ApplyReceipt(OperationReceipt receipt, string targetDisplayName)
    {
        string operation = receipt.Kind switch
        {
            OperationKind.Handoff => "HANDOFF",
            OperationKind.Move => "MOVE",
            _ => "OPERATION",
        };
        string outcome = receipt.Status switch
        {
            OperationStatus.Committed => "COMMITTED",
            OperationStatus.CommittedWithWarning => "COMMITTED WITH WARNING",
            OperationStatus.Rejected => "REJECTED",
            OperationStatus.Failed => "FAILED",
            OperationStatus.Recovering => "OUTCOME UNCERTAIN",
            _ => "RESULT UNAVAILABLE",
        };
        ReceiptStatus = $"{operation} {outcome}";
        ReceiptSummary = (receipt.Kind, receipt.Status) switch
        {
            (OperationKind.Move, OperationStatus.Committed) =>
                $"{targetDisplayName} acknowledged the semantic resume; the source closed only after that verified receipt.",
            (OperationKind.Move, OperationStatus.CommittedWithWarning) =>
                $"The target committed the semantic resume, but source cleanup failed. The source remains active, so two active copies may exist.",
            (OperationKind.Handoff, OperationStatus.Committed or OperationStatus.CommittedWithWarning) =>
                $"{targetDisplayName} acknowledged a semantic copy; the source remains available on this device.",
            (OperationKind.Move, OperationStatus.Recovering) =>
                $"{targetDisplayName} may have accepted the semantic resume, but the verified acknowledgement is unavailable. The source remains available and unchanged; inspect both devices before retrying.",
            (_, OperationStatus.Recovering) =>
                $"{targetDisplayName} may have accepted a semantic copy, but the verified outcome is unavailable. The source remains available and unchanged.",
            (OperationKind.Move, _) =>
                $"{targetDisplayName} did not accept the semantic resume; the source remains available and unchanged.",
            _ =>
                $"{targetDisplayName} did not accept a semantic copy; the source remains available and unchanged.",
        };
        ReceiptCorrelationId = receipt.CorrelationId.ToString();
        ReceiptOccurredAt = receipt.OccurredAt.ToString("O");
        ReceiptReason = ToReasonCode(receipt.FailureCode);
        UndoDescription = ToUndoDescription(receipt.Kind, receipt.Status);
    }

    private void ApplyReplaceInventoryFailure(FailureCode failureCode)
    {
        (ReplaceInventoryStatus, ReplaceInventoryDescription) = failureCode switch
        {
            FailureCode.CapabilityDenied => (
                "REPLACE TARGETS BLOCKED — REVIEW TRUST",
                "The current activity.receive or activity.replace permission is unavailable. Review Trust on both devices and retry. No Replace request was sent."),
            FailureCode.ActivityNotFound => (
                "INCOMING ACTIVITY CHANGED — SELECT AGAIN",
                "The incoming Activity is no longer active. Select it again or choose another source. No Replace request was sent."),
            FailureCode.AdapterUnavailable => (
                "REPLACE UNSUPPORTED FOR THIS ACTIVITY",
                "No Replace-capable semantic adapter is available for this Activity kind. No Replace request was sent."),
            FailureCode.DeadlineExpired => (
                "REPLACE TARGET QUERY EXPIRED — RETRY",
                "The bounded inventory query expired. Refresh the target list. No Replace request was sent."),
            FailureCode.AcknowledgementLost => (
                "REPLACE TARGETS UNCONFIRMED — RETRY",
                "The inventory acknowledgement was lost. Refresh after the authenticated connection recovers. No Replace request was sent."),
            _ => (
                "REPLACE TARGETS UNAVAILABLE — RETRY",
                "The authenticated local connection did not return an eligible inventory. Reconnect and retry. No Replace request was sent."),
        };
    }

    private bool CanCreateWorkspaceNote() =>
        Volatile.Read(ref disposed) == 0
        && service.IsReady
        && !IsBusy
        && !string.IsNullOrWhiteSpace(DraftTitle)
        && DraftTitle.Trim().Length <= ActivityDescriptor.MaximumTitleCharacters
        && DraftText.Length is > 0 and <= 16 * 1024;

    private bool CanHandoff() =>
        Volatile.Read(ref disposed) == 0
        && !IsBusy
        && SelectedActivity is not null
        && SelectedTarget is not null
        && Activities.Contains(SelectedActivity)
        && Targets.Contains(SelectedTarget)
        && SelectedSemanticResumeAvailability
            == DesktopSemanticResumeAvailability.Available;

    private bool CanMove() => CanHandoff();

    private bool CanRefreshReplaceTargets() => CanHandoff();

    private bool CanReplace() =>
        Volatile.Read(ref disposed) == 0
        && IsDestructiveReplaceAvailable;

    private bool CanUndoReplace() =>
        Volatile.Read(ref disposed) == 0
        && !IsBusy
        && HasAcknowledgedTargetLocalUndo
        && SelectedReplaceRecoveryItem?.UndoCapsuleId is not null
        && IsTargetLocalUndoConfirmationAvailable;

    private void ClearReceipt()
    {
        ReceiptStatus = string.Empty;
        ReceiptSummary = string.Empty;
        ReceiptCorrelationId = string.Empty;
        ReceiptOccurredAt = string.Empty;
        ReceiptReason = string.Empty;
        UndoDescription = string.Empty;
    }

    private void OnPreviewChanged()
    {
        OnPropertyChanged(nameof(IsPreviewVisible));
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDescription));
        OnPropertyChanged(nameof(IsMovePreviewVisible));
        OnPropertyChanged(nameof(MovePreviewStatus));
        OnPropertyChanged(nameof(MovePreviewDescription));
        OnPropertyChanged(nameof(SelectedSemanticResumeAvailability));
        OnPropertyChanged(nameof(IsSelectedSemanticResumeAvailable));
        OnPropertyChanged(nameof(IsHandoffAvailable));
        OnPropertyChanged(nameof(IsMoveAvailable));
        OnPropertyChanged(nameof(IsReplaceConfirmationAvailable));
        OnPropertyChanged(nameof(IsReplaceInventoryAvailable));
        handoffCommand.NotifyCanExecuteChanged();
        moveCommand.NotifyCanExecuteChanged();
        refreshReplaceTargetsCommand.NotifyCanExecuteChanged();
        replaceCommand.NotifyCanExecuteChanged();
    }

    private void OnReplacePreviewChanged()
    {
        OnPropertyChanged(nameof(IsReplacePreviewVisible));
        OnPropertyChanged(nameof(ReplacePreviewStatus));
        OnPropertyChanged(nameof(ReplaceIncomingDescription));
        OnPropertyChanged(nameof(ReplaceTargetDescription));
        OnPropertyChanged(nameof(ReplaceConfirmationAutomationName));
        OnPropertyChanged(nameof(ReplaceConfirmationDescription));
        OnPropertyChanged(nameof(IsReplaceConfirmationAvailable));
        OnPropertyChanged(nameof(ReplaceActivationStatus));
        OnPropertyChanged(nameof(IsDestructiveReplaceAvailable));
        replaceCommand.NotifyCanExecuteChanged();
    }

    private void OnTargetLocalUndoSelectionChanged()
    {
        if (!IsBusy)
        {
            ApplyTargetLocalUndoSelectionState();
        }

        OnPropertyChanged(nameof(IsTargetLocalUndoConfirmationAvailable));
        OnPropertyChanged(nameof(IsTargetLocalUndoAvailable));
        OnPropertyChanged(nameof(TargetLocalUndoConfirmationDescription));
        OnPropertyChanged(nameof(TargetLocalUndoConfirmationAutomationName));
        OnPropertyChanged(nameof(TargetLocalUndoStatus));
        targetLocalUndoCommand.NotifyCanExecuteChanged();
    }

    private void ApplyTargetLocalUndoSelectionState()
    {
        TargetLocalUndoReason = string.Empty;
        TargetLocalUndoOccurredAt = string.Empty;
        if (SelectedReplaceRecoveryItem is null)
        {
            TargetLocalUndoStatus =
                "TARGET-LOCAL UNDO — SELECT AN AVAILABLE CAPSULE";
            TargetLocalUndoDescription =
                "Select an available committed Replace record and review its exact capsule binding.";
        }
        else if (!SelectedReplaceRecoveryItem.CanUndo)
        {
            TargetLocalUndoStatus =
                "TARGET-LOCAL UNDO NOT AVAILABLE FOR THIS RECORD";
            TargetLocalUndoDescription =
                "This record is pending, expired, consumed, already attempted, unsupported, or no longer the exact current replacement. No action is available.";
        }
        else if (HasAcknowledgedTargetLocalUndo)
        {
            TargetLocalUndoStatus = "TARGET-LOCAL UNDO CONFIRMED — READY";
            TargetLocalUndoDescription =
                "The exact capsule binding is confirmed. Activating undo will reserve one durable operation before Adapter restore.";
        }
        else
        {
            TargetLocalUndoStatus =
                "TARGET-LOCAL UNDO — EXACT CONFIRMATION REQUIRED";
            TargetLocalUndoDescription =
                "Review the capsule, both opaque Activity IDs, and exact expiry, then confirm this one target-local action.";
        }
    }

    private void ApplyTargetLocalUndoResult(UndoReplaceResult result)
    {
        TargetLocalUndoStatus = result.Status switch
        {
            OperationStatus.Committed => "TARGET-LOCAL UNDO COMMITTED",
            OperationStatus.Rejected => "TARGET-LOCAL UNDO REJECTED",
            OperationStatus.Failed => "TARGET-LOCAL UNDO FAILED",
            OperationStatus.Recovering =>
                "TARGET-LOCAL UNDO OUTCOME UNCERTAIN — DUPLICATE DISABLED",
            _ => "TARGET-LOCAL UNDO OUTCOME UNAVAILABLE",
        };
        TargetLocalUndoDescription = (result.Status, result.FailureCode) switch
        {
            (OperationStatus.Committed, _) =>
                "The preserved semantic Activity was restored at a new revision and the protected capsule is recorded as consumed.",
            (_, FailureCode.UndoCapsuleExpired) =>
                "The exact capsule expired before restore began. No Adapter restore was performed.",
            (_, FailureCode.UndoCapsuleConsumed) =>
                "The exact capsule was already consumed. No duplicate Adapter restore was performed.",
            (_, FailureCode.RevisionConflict) =>
                "The replacement is no longer the exact current Activity revision. Flowspan did not overwrite newer state.",
            (_, FailureCode.OperationInProgress) =>
                "A protected pending boundary already owns this capsule. Inspect recovery; Adapter work was not repeated.",
            (OperationStatus.Recovering, _) =>
                "The durable terminal write or destructive boundary is uncertain. The pending record blocks duplicate restore until explicit recovery exists.",
            (OperationStatus.Failed, _) =>
                "The Adapter did not complete the semantic restore. The terminal failure is recorded and this UI does not silently retry it.",
            _ =>
                "The protected undo request was not committed. The recorded reason is shown; no outcome is inferred.",
        };
        TargetLocalUndoReason = ToReasonCode(result.FailureCode);
        TargetLocalUndoOccurredAt = result.OccurredAt.ToString("O");
    }

    private void InvalidateReplaceInventory()
    {
        Interlocked.Increment(ref replaceInventoryContextVersion);
        ClearReplaceInventorySnapshot();
        ReplaceInventoryStatus = "REPLACE TARGETS NOT LOADED";
        ReplaceInventoryDescription =
            "Select an incoming Activity and authenticated target, then load purpose-scoped Replace targets.";
    }

    private void OnServiceChanged()
    {
        dispatcher.Post(() =>
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                Refresh(
                    SelectedActivity?.ActivityId,
                    SelectedTarget?.DeviceId);
                InvalidateReplaceInventory();
                RefreshReplaceRecovery();
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsDestructiveReplaceAvailable));
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                OnPropertyChanged(nameof(SelectedSemanticResumeAvailability));
                OnPropertyChanged(nameof(IsSelectedSemanticResumeAvailable));
                OnPropertyChanged(nameof(IsHandoffAvailable));
                OnPropertyChanged(nameof(IsMoveAvailable));
                handoffCommand.NotifyCanExecuteChanged();
                moveCommand.NotifyCanExecuteChanged();
                refreshReplaceTargetsCommand.NotifyCanExecuteChanged();
            }
        });
    }

    private DesktopSemanticResumeAvailability GetSemanticResumeAvailability(
        DesktopActivitySnapshot activity)
    {
        try
        {
            return service.SupportsSemanticResume(activity.Kind)
                ? DesktopSemanticResumeAvailability.Available
                : DesktopSemanticResumeAvailability.Unavailable;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DesktopSemanticResumeAvailability.Unknown;
        }
    }

    private void Refresh(
        ActivityId? preferredActivityId = null,
        DeviceId? preferredTargetId = null)
    {
        ActivityId? activityId = preferredActivityId ?? SelectedActivity?.ActivityId;
        DeviceId? targetId = preferredTargetId ?? SelectedTarget?.DeviceId;
        DeviceId? remoteWindowTargetId = SelectedRemoteWindowTarget?.DeviceId;
        Replace(Activities, service.GetActivities());
        Replace(Targets, service.GetTargets());
        RefreshRemoteWindowTargets(remoteWindowTargetId);
        SelectedActivity = activityId is null
            ? null
            : Activities.FirstOrDefault(item => item.ActivityId == activityId);
        SelectedTarget = targetId is null
            ? null
            : Targets.FirstOrDefault(item => item.DeviceId == targetId);
    }

    private void RefreshRemoteWindowTargets(DeviceId? preferredTargetId)
    {
        ImmutableArray<DesktopActivityTargetSnapshot> targets;
        try
        {
            targets = service.GetRemoteWindowTargets(RemoteWindowTargetRole);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            targets = [];
        }

        Replace(RemoteWindowTargets, targets);
        SelectedRemoteWindowTarget = preferredTargetId is null
            ? null
            : RemoteWindowTargets.FirstOrDefault(
                target => target.DeviceId == preferredTargetId);
    }

    private static void Replace<T>(
        ObservableCollection<T> destination,
        ImmutableArray<T> source)
    {
        destination.Clear();
        foreach (T item in source)
        {
            destination.Add(item);
        }
    }

    private static string ToReasonCode(FailureCode code)
    {
        if (code == FailureCode.None)
        {
            return "none";
        }

        var characters = new List<char>();
        foreach (char character in code.ToString())
        {
            if (char.IsAsciiLetterUpper(character) && characters.Count > 0)
            {
                characters.Add('-');
            }

            characters.Add(char.ToLowerInvariant(character));
        }

        return new string([.. characters]);
    }

    private static string ToUndoDescription(
        OperationKind kind,
        OperationStatus status) => (kind, status) switch
        {
            (OperationKind.Handoff, _) =>
                "NO UNDO REQUIRED — handoff preserves the source. Each device owns its resulting copy and can delete it locally.",
            (OperationKind.Move, OperationStatus.Committed) =>
                "NO AUTOMATIC UNDO — the source closed after verified target acknowledgement. Start a new move to move it back.",
            (OperationKind.Move, OperationStatus.CommittedWithWarning) =>
                "NO AUTOMATIC UNDO — target resume is committed and source cleanup failed. Resolve the two active copies explicitly.",
            (OperationKind.Move, OperationStatus.Recovering) =>
                "NO AUTOMATIC UNDO — the source remains active, but target acceptance is uncertain. Inspect both devices before retrying.",
            (OperationKind.Move, _) =>
                "NO UNDO REQUIRED — the move did not close the source Activity.",
            _ => string.Empty,
        };

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
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class UnavailableDesktopActivityService : IDesktopActivityService
{
    private UnavailableDesktopActivityService()
    {
    }

    public static UnavailableDesktopActivityService Instance { get; } = new();

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public bool IsReady => false;

    public bool SupportsSemanticResume(string activityKind) => false;

    public DesktopActivitySnapshot CreateWorkspaceNote(
        string title,
        string text,
        ActivitySensitivity sensitivity) =>
        throw new PlatformNotSupportedException(
            "A production Activity runtime was not configured.");

    public ImmutableArray<DesktopActivitySnapshot> GetActivities() => [];

    public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() => [];

    public ImmutableArray<DesktopActivityTargetSnapshot> GetRemoteWindowTargets(
        MirrorParticipantRole role) => [];

    public ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<OperationReceipt>(
            new PlatformNotSupportedException(
                "A production Activity runtime was not configured."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
