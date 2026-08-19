using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        DesktopText.Get("Activity_ReplaceInventory_NotLoadedDescription");
    private string replaceInventoryCapturedAt = string.Empty;
    private string replaceInventoryCoverage = string.Empty;
    private string replaceInventoryStatus =
        DesktopText.Get("Activity_ReplaceInventory_NotLoadedStatus");
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
        DesktopText.Get("Activity_ReplaceRecovery_NotLoadedDescription");
    private string replaceRecoveryStatus =
        DesktopText.Get("Activity_ReplaceRecovery_NotLoadedStatus");
    private bool hasAcknowledgedTargetLocalUndo;
    private DesktopReplaceRecoveryItem? selectedReplaceRecoveryItem;
    private string targetLocalUndoDescription =
        DesktopText.Get("Activity_TargetLocalUndo_SelectCapsuleDescription");
    private string targetLocalUndoOccurredAt = string.Empty;
    private string targetLocalUndoReason = string.Empty;
    private string targetLocalUndoStatus =
        DesktopText.Get("Activity_TargetLocalUndo_SelectCapsuleStatus");
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
        DesktopText.Get("Activity_DataDisclosure");

    public string DegradationDescription { get; } =
        DesktopText.Format(
            "Activity_DegradationDescription",
            "workspace.note/v1");

    public string DegradationStatus { get; } =
        DesktopText.Get("Activity_DegradationStatus");

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
            ? DesktopText.Format(
                "Activity_TargetLocalUndo_ConfirmationDescriptionSelected",
                item.Capsule,
                item.Activities,
                ToInvariantRoundTrip(item.UndoExpiresAt))
            : DesktopText.Get(
                "Activity_TargetLocalUndo_ConfirmationDescriptionEmpty");

    public string TargetLocalUndoConfirmationAutomationName =>
        SelectedReplaceRecoveryItem is { } item
            ? DesktopText.Format(
                "Activity_TargetLocalUndo_ConfirmationAutomationNameSelected",
                item.Capsule)
            : DesktopText.Get(
                "Activity_TargetLocalUndo_ConfirmationAutomationNameEmpty");

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
        ? DesktopText.Format(
            "Activity_HandoffPreview_ReadyDescription",
            SelectedActivity!.Kind,
            SelectedTarget!.DisplayName,
            SelectedActivity.Sensitivity)
        : DesktopText.Get("Activity_HandoffPreview_NotReadyDescription");

    public string PreviewStatus => IsPreviewVisible
        ? DesktopText.Get("Activity_HandoffPreview_ReadyStatus")
        : DesktopText.Get("Activity_HandoffPreview_NotReadyStatus");

    public string MovePreviewDescription => IsMovePreviewVisible
        ? DesktopText.Format(
            "Activity_MovePreview_ReadyDescription",
            SelectedTarget!.DisplayName,
            SelectedActivity!.Kind,
            SelectedActivity.Sensitivity)
        : DesktopText.Get("Activity_MovePreview_NotReadyDescription");

    public string MovePreviewStatus => IsMovePreviewVisible
        ? DesktopText.Get("Activity_MovePreview_ReadyStatus")
        : DesktopText.Get("Activity_MovePreview_NotReadyStatus");

    public string ReplaceIncomingDescription => IsReplacePreviewVisible
        ? DesktopText.Format(
            "Activity_ReplacePreview_IncomingDescription",
            SelectedActivity!.Title,
            SelectedActivity.Kind)
        : DesktopText.Get("Activity_ReplacePreview_IncomingNotReadyDescription");

    public string ReplaceConfirmationAutomationName => IsReplacePreviewVisible
        ? DesktopText.Format(
            "Activity_ReplacePreview_ConfirmationAutomationName",
            SelectedReplaceTarget!.Title,
            SelectedTarget!.DisplayName,
            SelectedActivity!.Title)
        : DesktopText.Get(
            "Activity_ReplacePreview_ConfirmationAutomationNameNotReady");

    public string ReplaceConfirmationDescription => IsReplacePreviewVisible
        ? DesktopText.Format(
            "Activity_ReplacePreview_ConfirmationDescription",
            SelectedReplaceTarget!.Title,
            SelectedTarget!.DisplayName,
            SelectedActivity!.Title)
        : DesktopText.Get(
            "Activity_ReplacePreview_ConfirmationDescriptionNotReady");

    public string ReplacePreviewStatus => IsReplacePreviewVisible
        ? DesktopText.Get("Activity_ReplacePreview_ReadyStatus")
        : DesktopText.Get("Activity_ReplacePreview_NotReadyStatus");

    public string ReplaceActivationStatus => !IsReplacePreviewVisible
        ? DesktopText.Get("Activity_ReplaceActivation_PreviewLockedStatus")
        : HasAcknowledgedReplace
            ? IsDestructiveReplaceAvailable
                ? DesktopText.Get("Activity_ReplaceActivation_ReadyStatus")
                : DesktopText.Get("Activity_ReplaceActivation_NotActivatedStatus")
            : DesktopText.Get("Activity_ReplaceActivation_ConfirmationRequiredStatus");

    public string ReplaceTargetDescription => IsReplacePreviewVisible
        ? DesktopText.Format(
            "Activity_ReplacePreview_TargetDescription",
            SelectedReplaceTarget!.Title,
            SelectedReplaceTarget.Kind,
            SelectedTarget!.DisplayName,
            SelectedReplaceTarget.PlacementSlot,
            SelectedReplaceTarget.Revision,
            SelectedReplaceTarget.DescriptorDigest)
        : DesktopText.Get("Activity_ReplacePreview_TargetNotSelectedDescription");

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
            CreationStatus = DesktopText.Get("Activity_Note_ReadyStatus");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CreationStatus = DesktopText.Get("Activity_Note_CreateFailedStatus");
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
                DesktopText.Get("Activity_WorkspaceUnavailableStatus");
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
            ReceiptStatus = DesktopText.Get("Activity_Handoff_UnavailableStatus");
            ReceiptSummary =
                DesktopText.Format(
                    "Activity_Handoff_UnavailableDescription",
                    target.DisplayName);
            ReceiptReason = "peer-unavailable";
            ReceiptCorrelationId = string.Empty;
            ReceiptOccurredAt = string.Empty;
            UndoDescription =
                DesktopText.Get("Activity_Handoff_UnavailableUndoDescription");
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
            ReceiptStatus = DesktopText.Get("Activity_Move_UnavailableStatus");
            ReceiptSummary =
                DesktopText.Format(
                    "Activity_Move_UnavailableDescription",
                    target.DisplayName);
            ReceiptReason = "peer-unavailable";
            ReceiptCorrelationId = string.Empty;
            ReceiptOccurredAt = string.Empty;
            UndoDescription =
                DesktopText.Get("Activity_Move_UnavailableUndoDescription");
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
        ReplaceInventoryStatus = DesktopText.Get(
            "Activity_ReplaceInventory_LoadingStatus");
        ReplaceInventoryDescription =
            DesktopText.Format(
                "Activity_ReplaceInventory_LoadingDescription",
                activity.Kind,
                target.DisplayName);
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
                ReplaceInventoryStatus = DesktopText.Get(
                    "Activity_ReplaceInventory_UnavailableRetryStatus");
                ReplaceInventoryDescription =
                    DesktopText.Get(
                        "Activity_ReplaceInventory_VerifiedInventoryUnavailableDescription");
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
        ReplaceOperationStatus = DesktopText.Get(
            "Activity_ReplaceOperation_PendingStatus");
        ReplaceOperationDescription =
            DesktopText.Format(
                "Activity_ReplaceOperation_PendingDescription",
                target.Title,
                device.DisplayName);
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
                ReplaceInventoryStatus = DesktopText.Get(
                    "Activity_ReplaceInventory_CommittedRefreshRequiredStatus");
                ReplaceInventoryDescription =
                    DesktopText.Get(
                        "Activity_ReplaceInventory_CommittedRefreshRequiredDescription");
            }
            else if (result.FailureCode == FailureCode.RevisionConflict)
            {
                ClearReplaceInventorySnapshot();
                ReplaceInventoryStatus = DesktopText.Get(
                    "Activity_ReplaceInventory_TargetChangedRefreshRequiredStatus");
                ReplaceInventoryDescription =
                    DesktopText.Get(
                        "Activity_ReplaceInventory_TargetChangedRefreshRequiredDescription");
            }
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReplaceOperationStatus =
                DesktopText.Get(
                    "Activity_ReplaceOperation_OutcomeUnavailableStatus");
            ReplaceOperationDescription =
                DesktopText.Get(
                    "Activity_ReplaceOperation_OutcomeUnavailableDescription");
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
        TargetLocalUndoStatus = DesktopText.Get(
            "Activity_TargetLocalUndo_PendingStatus");
        TargetLocalUndoDescription =
            DesktopText.Format(
                "Activity_TargetLocalUndo_PendingDescription",
                capsuleId);
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
                DesktopText.Get(
                    "Activity_TargetLocalUndo_OutcomeUnavailableStatus");
            TargetLocalUndoDescription =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_OutcomeUnavailableDescription");
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
            ? DesktopText.Format(
                "Activity_ReplaceInventory_TruncatedCoverage",
                64)
            : DesktopText.Format(
                "Activity_ReplaceInventory_TargetCountCoverage",
                ReplaceTargets.Count);
        if (previousTarget is not null)
        {
            ReconcileRefreshedReplaceTarget(previousTarget);
        }
        else if (ReplaceTargets.Count == 0)
        {
            ReplaceInventoryStatus = DesktopText.Get(
                "Activity_ReplaceInventory_NoEligibleTargetsStatus");
            ReplaceInventoryDescription =
                DesktopText.Get(
                    "Activity_ReplaceInventory_NoEligibleTargetsDescription");
        }
        else
        {
            ReplaceInventoryStatus = DesktopText.Get(
                "Activity_ReplaceInventory_ReadyStatus");
            ReplaceInventoryDescription =
                DesktopText.Get("Activity_ReplaceInventory_ReadyDescription");
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
            ReplaceOperationStatus = DesktopText.Get(
                "Activity_ReplaceOperation_AcknowledgementLostStatus");
            ReplaceOperationDescription =
                DesktopText.Format(
                    "Activity_ReplaceOperation_AcknowledgementLostDescription",
                    targetDisplayName);
            return;
        }

        if (result.DeliveryStatus == ActivityDeliveryStatus.NotDelivered)
        {
            (ReplaceOperationStatus, ReplaceOperationDescription) =
                result.FailureCode switch
                {
                    FailureCode.RevisionConflict => (
                        DesktopText.Get(
                            "Activity_ReplaceOperation_NotSentTargetChangedStatus"),
                        DesktopText.Get(
                            "Activity_ReplaceOperation_NotSentTargetChangedDescription")),
                    FailureCode.CapabilityDenied => (
                        DesktopText.Get(
                            "Activity_ReplaceOperation_NotSentReviewTrustStatus"),
                        DesktopText.Format(
                            "Activity_ReplaceOperation_NotSentReviewTrustDescription",
                            "activity.receive",
                            "activity.replace")),
                    FailureCode.ActivityNotFound => (
                        DesktopText.Get(
                            "Activity_ReplaceOperation_NotSentActivityChangedStatus"),
                        DesktopText.Get(
                            "Activity_ReplaceOperation_NotSentActivityChangedDescription")),
                    FailureCode.UndoUnavailable => (
                        DesktopText.Get(
                            "Activity_ReplaceOperation_RecoveryUnavailableStatus"),
                        DesktopText.Get(
                            "Activity_ReplaceOperation_RecoveryUnavailableDescription")),
                    _ => (
                        DesktopText.Get(
                            "Activity_ReplaceOperation_NotDeliveredStatus"),
                        DesktopText.Format(
                            "Activity_ReplaceOperation_NotDeliveredDescription",
                            targetDisplayName)),
                };
            return;
        }

        OperationReceipt? receipt = result.Receipt;
        if (receipt is null)
        {
            ReplaceOperationStatus =
                DesktopText.Get("Activity_ReplaceOperation_ResultInvalidStatus");
            ReplaceOperationDescription =
                DesktopText.Get(
                    "Activity_ReplaceOperation_MissingReceiptDescription");
            return;
        }

        if (receipt.FailureCode == FailureCode.OperationInProgress)
        {
            ReplaceOperationStatus =
                DesktopText.Get(
                    "Activity_ReplaceOperation_BlockedByRecoveryStatus");
            ReplaceOperationDescription =
                DesktopText.Format(
                    "Activity_ReplaceOperation_BlockedByRecoveryDescription",
                    targetDisplayName);
            return;
        }

        if (receipt.Status == OperationStatus.Committed
            && result.UndoCapsule is null)
        {
            ReplaceOperationStatus =
                DesktopText.Get("Activity_ReplaceOperation_ResultInvalidStatus");
            ReplaceOperationDescription =
                DesktopText.Get(
                    "Activity_ReplaceOperation_MissingUndoCapsuleDescription");
            return;
        }

        (ReplaceOperationStatus, ReplaceOperationDescription) = receipt.Status switch
        {
            OperationStatus.Committed => (
                DesktopText.Get("Activity_ReplaceOperation_CommittedStatus"),
                DesktopText.Format(
                    "Activity_ReplaceOperation_CommittedDescription",
                    targetDisplayName)),
            OperationStatus.Recovering => (
                DesktopText.Get(
                    "Activity_ReplaceOperation_RecoveryRequiredStatus"),
                DesktopText.Format(
                    "Activity_ReplaceOperation_RecoveryRequiredDescription",
                    targetDisplayName)),
            OperationStatus.Rejected => (
                DesktopText.Get("Activity_ReplaceOperation_RejectedStatus"),
                DesktopText.Format(
                    "Activity_ReplaceOperation_RejectedDescription",
                    targetDisplayName)),
            _ => (
                DesktopText.Get("Activity_ReplaceOperation_FailedStatus"),
                DesktopText.Format(
                    "Activity_ReplaceOperation_FailedDescription",
                    targetDisplayName)),
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
            ReplaceRecoveryCoverage = DesktopText.Get(
                "Activity_ReplaceRecovery_CountUnavailableCoverage");
            ReplaceRecoveryStatus =
                DesktopText.Get("Activity_ReplaceRecovery_UnavailableStatus");
            ReplaceRecoveryDescription =
                DesktopText.Get("Activity_ReplaceRecovery_UnavailableDescription");
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
            ? DesktopText.Format(
                "Activity_ReplaceRecovery_TruncatedCoverage",
                64)
            : DesktopText.Format(
                "Activity_ReplaceRecovery_RecordCountCoverage",
                ReplaceRecoveryItems.Count);
        int recoveryRequired = result.Records.Count(
            static record => record.IsRecoveryRequired);
        if (recoveryRequired > 0)
        {
            ReplaceRecoveryStatus =
                DesktopText.Format(
                    "Activity_ReplaceRecovery_RequiredStatus",
                    recoveryRequired);
            ReplaceRecoveryDescription =
                DesktopText.Get("Activity_ReplaceRecovery_RequiredDescription");
        }
        else if (result.Records.IsEmpty)
        {
            ReplaceRecoveryStatus = DesktopText.Get(
                "Activity_ReplaceRecovery_NoHistoryStatus");
            ReplaceRecoveryDescription =
                DesktopText.Get("Activity_ReplaceRecovery_NoHistoryDescription");
        }
        else if (ReplaceRecoveryItems.Any(static item => item.CanUndo))
        {
            int available = ReplaceRecoveryItems.Count(static item => item.CanUndo);
            ReplaceRecoveryStatus =
                DesktopText.Format(
                    "Activity_ReplaceRecovery_UndoAvailableStatus",
                    available);
            ReplaceRecoveryDescription =
                DesktopText.Get(
                    "Activity_ReplaceRecovery_UndoAvailableDescription");
        }
        else
        {
            ReplaceRecoveryStatus =
                DesktopText.Get("Activity_ReplaceRecovery_NoUndoActionStatus");
            ReplaceRecoveryDescription =
                DesktopText.Get(
                    "Activity_ReplaceRecovery_NoUndoActionDescription");
        }
    }

    private static DesktopReplaceRecoveryItem CreateReplaceRecoveryItem(
        ReplaceRecoveryRecord record,
        bool canUndo)
    {
        string kind = record.Kind switch
        {
            ReplaceRecoveryOperationKind.Replace => DesktopText.Get(
                "Activity_RecoveryItem_ReplaceKind"),
            ReplaceRecoveryOperationKind.Undo => DesktopText.Get(
                "Activity_RecoveryItem_UndoKind"),
            _ => DesktopText.Get("Activity_RecoveryItem_OperationKind"),
        };
        string state = record.JournalState switch
        {
            ReplaceRecoveryJournalState.Pending =>
                DesktopText.Get("Activity_RecoveryItem_PendingState"),
            _ when record.Status == OperationStatus.Recovering =>
                DesktopText.Get("Activity_RecoveryItem_RecoveringState"),
            _ => record.Status switch
            {
                OperationStatus.Committed => DesktopText.Get(
                    "Activity_RecoveryItem_CommittedState"),
                OperationStatus.CommittedWithWarning => DesktopText.Get(
                    "Activity_RecoveryItem_CommittedWithWarningState"),
                OperationStatus.Rejected => DesktopText.Get(
                    "Activity_RecoveryItem_RejectedState"),
                OperationStatus.Failed => DesktopText.Get(
                    "Activity_RecoveryItem_FailedState"),
                _ => DesktopText.Get("Activity_RecoveryItem_OutcomeUnavailableState"),
            },
        };
        string participants = string.Join(
            DesktopText.Get("Activity_RecoveryItem_ParticipantsSeparator"),
            record.ReplaceSourceDeviceId is not null
                ? DesktopText.Format(
                    "Activity_RecoveryItem_SourceDevice",
                    record.ReplaceSourceDeviceId)
                : DesktopText.Get(
                    "Activity_RecoveryItem_SourceDeviceNotRecorded"),
            record.ReplaceTargetDeviceId is not null
                ? DesktopText.Format(
                    "Activity_RecoveryItem_TargetDevice",
                    record.ReplaceTargetDeviceId)
                : DesktopText.Get(
                    "Activity_RecoveryItem_TargetDeviceNotRecorded"));
        string activities = string.Join(
            DesktopText.Get("Activity_RecoveryItem_ActivitiesSeparator"),
            record.TargetActivityId is not null
                ? DesktopText.Format(
                    "Activity_RecoveryItem_TargetActivity",
                    record.TargetActivityId)
                : DesktopText.Get(
                    "Activity_RecoveryItem_TargetActivityNotRecorded"),
            record.IncomingActivityId is not null
                ? DesktopText.Format(
                    "Activity_RecoveryItem_IncomingActivity",
                    record.IncomingActivityId)
                : DesktopText.Get(
                    "Activity_RecoveryItem_IncomingActivityNotRecorded"));
        string timestamp = record.TimestampKind switch
        {
            ReplaceRecoveryTimestampKind.Outcome =>
                DesktopText.Format(
                    "Activity_RecoveryItem_OutcomeRecordedTimestamp",
                    ToInvariantRoundTrip(record.RecordedAt)),
            ReplaceRecoveryTimestampKind.CapsuleCaptured =>
                DesktopText.Format(
                    "Activity_RecoveryItem_CapsuleCapturedTimestamp",
                    ToInvariantRoundTrip(record.RecordedAt)),
            _ => DesktopText.Get(
                "Activity_RecoveryItem_TimeNotRecordedTimestamp"),
        };
        string undo = record.UndoAvailability switch
        {
            ReplaceUndoAvailability.Available when canUndo =>
                DesktopText.Format(
                    "Activity_RecoveryItem_UndoAvailable",
                    ToInvariantRoundTrip(record.UndoExpiresAt)),
            ReplaceUndoAvailability.Available =>
                DesktopText.Format(
                    "Activity_RecoveryItem_UndoLocked",
                    ToInvariantRoundTrip(record.UndoExpiresAt)),
            ReplaceUndoAvailability.Expired =>
                DesktopText.Format(
                    "Activity_RecoveryItem_UndoExpired",
                    ToInvariantRoundTrip(record.UndoExpiresAt)),
            ReplaceUndoAvailability.PendingOperation =>
                DesktopText.Format(
                    "Activity_RecoveryItem_UndoPending",
                    ToInvariantRoundTrip(record.UndoExpiresAt)),
            ReplaceUndoAvailability.Consumed => DesktopText.Get(
                "Activity_RecoveryItem_UndoConsumed"),
            _ when record.UndoExpiresAt is not null =>
                DesktopText.Format(
                    "Activity_RecoveryItem_UndoUnavailableWithExpiry",
                    ToInvariantRoundTrip(record.UndoExpiresAt)),
            _ => DesktopText.Get("Activity_RecoveryItem_UndoUnavailable"),
        };
        return new DesktopReplaceRecoveryItem(
            kind,
            state,
            ToReasonCode(record.FailureCode),
            record.OperationId.ToString(),
            record.CorrelationId?.ToString()
                ?? DesktopText.Get(
                    "Activity_RecoveryItem_CorrelationNotRecorded"),
            participants,
            activities,
            record.CapsuleId?.ToString()
                ?? DesktopText.Get("Activity_RecoveryItem_CapsuleNotRecorded"),
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
                DesktopText.Get(
                    "Activity_ReplaceInventory_TargetChangedReviewStatus");
            ReplaceInventoryDescription = refreshedTarget is null
                ? DesktopText.Get(
                    "Activity_ReplaceInventory_TargetNoLongerEligibleDescription")
                : DesktopText.Get(
                    "Activity_ReplaceInventory_TargetSnapshotChangedDescription");
            return;
        }

        SelectedReplaceTarget = refreshedTarget;
        ReplaceInventoryStatus = DesktopText.Get(
            "Activity_ReplaceInventory_RefreshedConfirmAgainStatus");
        ReplaceInventoryDescription =
            DesktopText.Get(
                "Activity_ReplaceInventory_RefreshedConfirmAgainDescription");
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
            OperationKind.Handoff => DesktopText.Get(
                "Activity_Receipt_HandoffOperation"),
            OperationKind.Move => DesktopText.Get("Activity_Receipt_MoveOperation"),
            _ => DesktopText.Get("Activity_Receipt_GenericOperation"),
        };
        string outcome = receipt.Status switch
        {
            OperationStatus.Committed => DesktopText.Get(
                "Activity_Receipt_CommittedOutcome"),
            OperationStatus.CommittedWithWarning => DesktopText.Get(
                "Activity_Receipt_CommittedWithWarningOutcome"),
            OperationStatus.Rejected => DesktopText.Get(
                "Activity_Receipt_RejectedOutcome"),
            OperationStatus.Failed => DesktopText.Get(
                "Activity_Receipt_FailedOutcome"),
            OperationStatus.Recovering => DesktopText.Get(
                "Activity_Receipt_UncertainOutcome"),
            _ => DesktopText.Get("Activity_Receipt_UnavailableOutcome"),
        };
        ReceiptStatus = DesktopText.Format(
            "Activity_Receipt_Status",
            operation,
            outcome);
        ReceiptSummary = (receipt.Kind, receipt.Status) switch
        {
            (OperationKind.Move, OperationStatus.Committed) =>
                DesktopText.Format(
                    "Activity_Receipt_MoveCommittedSummary",
                    targetDisplayName),
            (OperationKind.Move, OperationStatus.CommittedWithWarning) =>
                DesktopText.Get(
                    "Activity_Receipt_MoveCommittedWithWarningSummary"),
            (OperationKind.Handoff, OperationStatus.Committed or OperationStatus.CommittedWithWarning) =>
                DesktopText.Format(
                    "Activity_Receipt_HandoffCommittedSummary",
                    targetDisplayName),
            (OperationKind.Move, OperationStatus.Recovering) =>
                DesktopText.Format(
                    "Activity_Receipt_MoveRecoveringSummary",
                    targetDisplayName),
            (_, OperationStatus.Recovering) =>
                DesktopText.Format(
                    "Activity_Receipt_HandoffRecoveringSummary",
                    targetDisplayName),
            (OperationKind.Move, _) =>
                DesktopText.Format(
                    "Activity_Receipt_MoveNotAcceptedSummary",
                    targetDisplayName),
            _ =>
                DesktopText.Format(
                    "Activity_Receipt_HandoffNotAcceptedSummary",
                    targetDisplayName),
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
                DesktopText.Get(
                    "Activity_ReplaceInventory_BlockedReviewTrustStatus"),
                DesktopText.Format(
                    "Activity_ReplaceInventory_BlockedReviewTrustDescription",
                    "activity.receive",
                    "activity.replace")),
            FailureCode.ActivityNotFound => (
                DesktopText.Get(
                    "Activity_ReplaceInventory_IncomingActivityChangedStatus"),
                DesktopText.Get(
                    "Activity_ReplaceInventory_IncomingActivityChangedDescription")),
            FailureCode.AdapterUnavailable => (
                DesktopText.Get(
                    "Activity_ReplaceInventory_UnsupportedActivityStatus"),
                DesktopText.Get(
                    "Activity_ReplaceInventory_UnsupportedActivityDescription")),
            FailureCode.DeadlineExpired => (
                DesktopText.Get(
                    "Activity_ReplaceInventory_QueryExpiredStatus"),
                DesktopText.Get(
                    "Activity_ReplaceInventory_QueryExpiredDescription")),
            FailureCode.AcknowledgementLost => (
                DesktopText.Get(
                    "Activity_ReplaceInventory_UnconfirmedStatus"),
                DesktopText.Get(
                    "Activity_ReplaceInventory_UnconfirmedDescription")),
            _ => (
                DesktopText.Get(
                    "Activity_ReplaceInventory_UnavailableRetryStatus"),
                DesktopText.Get(
                    "Activity_ReplaceInventory_EligibleInventoryUnavailableDescription")),
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
                DesktopText.Get("Activity_TargetLocalUndo_SelectCapsuleStatus");
            TargetLocalUndoDescription =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_SelectCapsuleDescription");
        }
        else if (!SelectedReplaceRecoveryItem.CanUndo)
        {
            TargetLocalUndoStatus =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_RecordUnavailableStatus");
            TargetLocalUndoDescription =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_RecordUnavailableDescription");
        }
        else if (HasAcknowledgedTargetLocalUndo)
        {
            TargetLocalUndoStatus = DesktopText.Get(
                "Activity_TargetLocalUndo_ConfirmedReadyStatus");
            TargetLocalUndoDescription =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_ConfirmedReadyDescription");
        }
        else
        {
            TargetLocalUndoStatus =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_ConfirmationRequiredStatus");
            TargetLocalUndoDescription =
                DesktopText.Get(
                    "Activity_TargetLocalUndo_ConfirmationRequiredDescription");
        }
    }

    private void ApplyTargetLocalUndoResult(UndoReplaceResult result)
    {
        TargetLocalUndoStatus = result.Status switch
        {
            OperationStatus.Committed => DesktopText.Get(
                "Activity_TargetLocalUndo_CommittedStatus"),
            OperationStatus.Rejected => DesktopText.Get(
                "Activity_TargetLocalUndo_RejectedStatus"),
            OperationStatus.Failed => DesktopText.Get(
                "Activity_TargetLocalUndo_FailedStatus"),
            OperationStatus.Recovering =>
                DesktopText.Get("Activity_TargetLocalUndo_UncertainStatus"),
            _ => DesktopText.Get("Activity_TargetLocalUndo_UnavailableStatus"),
        };
        TargetLocalUndoDescription = (result.Status, result.FailureCode) switch
        {
            (OperationStatus.Committed, _) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_CommittedDescription"),
            (_, FailureCode.UndoCapsuleExpired) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_CapsuleExpiredDescription"),
            (_, FailureCode.UndoCapsuleConsumed) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_CapsuleConsumedDescription"),
            (_, FailureCode.RevisionConflict) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_RevisionConflictDescription"),
            (_, FailureCode.OperationInProgress) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_OperationInProgressDescription"),
            (OperationStatus.Recovering, _) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_RecoveringDescription"),
            (OperationStatus.Failed, _) =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_FailedDescription"),
            _ =>
                DesktopText.Get(
                    "Activity_TargetLocalUndo_NotCommittedDescription"),
        };
        TargetLocalUndoReason = ToReasonCode(result.FailureCode);
        TargetLocalUndoOccurredAt = result.OccurredAt.ToString("O");
    }

    private void InvalidateReplaceInventory()
    {
        Interlocked.Increment(ref replaceInventoryContextVersion);
        ClearReplaceInventorySnapshot();
        ReplaceInventoryStatus = DesktopText.Get(
            "Activity_ReplaceInventory_NotLoadedStatus");
        ReplaceInventoryDescription =
            DesktopText.Get("Activity_ReplaceInventory_NotLoadedDescription");
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

    private static string ToInvariantRoundTrip(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

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
                DesktopText.Get("Activity_Undo_HandoffPreservesSourceDescription"),
            (OperationKind.Move, OperationStatus.Committed) =>
                DesktopText.Get("Activity_Undo_MoveCommittedDescription"),
            (OperationKind.Move, OperationStatus.CommittedWithWarning) =>
                DesktopText.Get(
                    "Activity_Undo_MoveCommittedWithWarningDescription"),
            (OperationKind.Move, OperationStatus.Recovering) =>
                DesktopText.Get("Activity_Undo_MoveRecoveringDescription"),
            (OperationKind.Move, _) =>
                DesktopText.Get("Activity_Undo_MoveNotCommittedDescription"),
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
