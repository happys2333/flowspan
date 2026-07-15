using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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

public interface IDesktopActivityService : IAsyncDisposable
{
    public event Action? Changed;

    public bool IsReady => true;

    public bool IsDestructiveReplaceAvailable => false;

    public DesktopActivitySnapshot CreateWorkspaceNote(
        string title,
        string text,
        ActivitySensitivity sensitivity);

    public ImmutableArray<DesktopActivitySnapshot> GetActivities();

    public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets();

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
    private string undoDescription = string.Empty;
    private DesktopActivitySnapshot? selectedActivity;
    private DesktopReplaceTargetSnapshot? selectedReplaceTarget;
    private DesktopActivityTargetSnapshot? selectedTarget;
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
        service.Changed += OnServiceChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesktopActivitySnapshot> Activities { get; } = [];

    public ObservableCollection<DesktopActivityTargetSnapshot> Targets { get; } = [];

    public ObservableCollection<DesktopReplaceTargetSnapshot> ReplaceTargets { get; } = [];

    public ICommand CreateWorkspaceNoteCommand => createWorkspaceNoteCommand;

    public ICommand HandoffCommand => handoffCommand;

    public ICommand MoveCommand => moveCommand;

    public ICommand RefreshReplaceTargetsCommand => refreshReplaceTargetsCommand;

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
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                createWorkspaceNoteCommand.NotifyCanExecuteChanged();
                handoffCommand.NotifyCanExecuteChanged();
                moveCommand.NotifyCanExecuteChanged();
                refreshReplaceTargetsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsHandoffAvailable => CanHandoff();

    public bool IsMoveAvailable => CanMove();

    public bool IsNoteCreationAvailable => CanCreateWorkspaceNote();

    public bool IsReady => service.IsReady;

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
        OnPropertyChanged(nameof(IsNoteCreationAvailable));
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
        && Targets.Contains(SelectedTarget);

    private bool CanMove() => CanHandoff();

    private bool CanRefreshReplaceTargets() => CanHandoff();

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
        OnPropertyChanged(nameof(IsHandoffAvailable));
        OnPropertyChanged(nameof(IsMoveAvailable));
        OnPropertyChanged(nameof(IsReplaceConfirmationAvailable));
        OnPropertyChanged(nameof(IsReplaceInventoryAvailable));
        handoffCommand.NotifyCanExecuteChanged();
        moveCommand.NotifyCanExecuteChanged();
        refreshReplaceTargetsCommand.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsDestructiveReplaceAvailable));
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                OnPropertyChanged(nameof(IsHandoffAvailable));
                OnPropertyChanged(nameof(IsMoveAvailable));
                moveCommand.NotifyCanExecuteChanged();
            }
        });
    }

    private void Refresh(
        ActivityId? preferredActivityId = null,
        DeviceId? preferredTargetId = null)
    {
        ActivityId? activityId = preferredActivityId ?? SelectedActivity?.ActivityId;
        DeviceId? targetId = preferredTargetId ?? SelectedTarget?.DeviceId;
        Replace(Activities, service.GetActivities());
        Replace(Targets, service.GetTargets());
        SelectedActivity = activityId is null
            ? null
            : Activities.FirstOrDefault(item => item.ActivityId == activityId);
        SelectedTarget = targetId is null
            ? null
            : Targets.FirstOrDefault(item => item.DeviceId == targetId);
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

    public DesktopActivitySnapshot CreateWorkspaceNote(
        string title,
        string text,
        ActivitySensitivity sensitivity) =>
        throw new PlatformNotSupportedException(
            "A production Activity runtime was not configured.");

    public ImmutableArray<DesktopActivitySnapshot> GetActivities() => [];

    public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() => [];

    public ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<OperationReceipt>(
            new PlatformNotSupportedException(
                "A production Activity runtime was not configured."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
