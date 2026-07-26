using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Desktop;

public interface IDesktopSceneApplyService
{
    public bool IsSceneApplyReady { get; }

    public ValueTask<SceneApplyPreview> PreviewSceneAsync(
        ScenePlan scene,
        IEnumerable<SceneSourceSelection> selectedSources,
        long? observedGroupRevision,
        CancellationToken cancellationToken = default);

    public ValueTask<SceneApplyExecutionResult> ApplySceneAsync(
        ScenePlan scene,
        SceneApplyPreview preview,
        SceneApplyApproval approval,
        CancellationToken cancellationToken = default);

    public ValueTask<SceneCompensationResult> CompensateSceneAsync(
        SceneApplyResult applyResult,
        CancellationToken cancellationToken = default);
}

public sealed record DesktopSceneSourceOption
{
    internal DesktopSceneSourceOption(SceneSourceSelection selection)
    {
        Selection = selection;
        Description =
            $"Device {selection.DeviceId}; revision {selection.Revision}; kind {selection.Kind}; slot {selection.Placement.Slot}";
    }

    internal SceneSourceSelection Selection { get; }

    public string Description { get; }

    public override string ToString() => Description;
}

public sealed class DesktopSceneApplyItemViewModel : INotifyPropertyChanged
{
    private readonly Action selectionChanged;
    private DesktopSceneSourceOption? selectedSource;
    private bool isReplaceConfirmed;

    internal DesktopSceneApplyItemViewModel(
        SceneApplyItemPreview item,
        SceneReplaceConfirmation? replaceConfirmation,
        Action stateChanged)
    {
        ArgumentNullException.ThrowIfNull(item);
        selectionChanged = stateChanged
            ?? throw new ArgumentNullException(nameof(stateChanged));
        Index = item.Index;
        ActivityId = item.ActivityId.ToString();
        Action = FormatAction(item.Action);
        Reason = FormatReason(item.Reason);
        SourceDisposition = item.SourceDisposition switch
        {
            SceneSourceDisposition.PreserveSource => "SOURCE STAYS OPEN",
            SceneSourceDisposition.MoveAfterAcknowledgement =>
                "SOURCE CLOSES ONLY AFTER ACKNOWLEDGEMENT",
            _ => "SOURCE POLICY UNAVAILABLE",
        };
        SourceDescription = item.Source is { } source
            ? $"Device {source.DeviceId}; revision {source.Revision}; kind {source.Kind}; slot {source.Placement.Slot}"
            : item.Reason == SceneApplyItemReason.SourceSelectionRequired
                ? "Select one exact source and regenerate the complete preview."
                : "No exact source metadata was published.";
        DestinationDescription =
            $"Device {item.Destination.DeviceId}; slot {item.Destination.Slot}";
        ReplaceTargetDescription = item.Action == SceneApplyAction.Replace
            && item.ReplaceTarget is { } target
            ? $"Activity {target.ActivityId}; Device {target.DeviceId}; revision {target.Revision}; kind {target.Kind}; slot {target.Placement.Slot}; digest {target.DescriptorDigest}"
            : item.Occupancy.Kind switch
            {
                SceneSlotOccupancyKind.Opaque =>
                    "Protected or ineligible occupant; exact metadata withheld.",
                SceneSlotOccupancyKind.Ambiguous =>
                    "Ambiguous occupants; exact metadata withheld.",
                _ => "No Activity will be replaced.",
            };
        IsReplace = item.Action == SceneApplyAction.Replace;
        ReplaceConfirmation = replaceConfirmation;
        ReplaceConfirmationAutomationName = item.ReplaceTarget is { } exact
            ? $"Confirm Scene item {item.Index + 1} replacement of Activity {exact.ActivityId} on Device {exact.DeviceId} revision {exact.Revision}"
            : "No destructive Scene replacement confirmation";
        SourceOptions = item.SourceLookup?.Candidates
            .Select(static candidate => new DesktopSceneSourceOption(candidate))
            .ToArray() ?? [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Action { get; }

    public string ActivityId { get; }

    public string DestinationDescription { get; }

    public int Index { get; }

    public string ItemLabel => $"ITEM {Index + 1}";

    public bool CanSelectSource => SourceOptions.Count > 1;

    public bool IsReplace { get; }

    public bool IsReplaceConfirmed
    {
        get => isReplaceConfirmed;
        set
        {
            if (SetProperty(ref isReplaceConfirmed, value))
            {
                selectionChanged();
            }
        }
    }

    public string Reason { get; }

    public string ReplaceConfirmationAutomationName { get; }

    internal SceneReplaceConfirmation? ReplaceConfirmation { get; }

    public string ReplaceTargetDescription { get; }

    public DesktopSceneSourceOption? SelectedSource
    {
        get => selectedSource;
        set
        {
            if (value is not null && !SourceOptions.Contains(value))
            {
                throw new ArgumentException(
                    "A Scene source selection must come from the exact preview candidates.",
                    nameof(value));
            }

            if (SetProperty(ref selectedSource, value))
            {
                selectionChanged();
            }
        }
    }

    public string SourceDescription { get; }

    public string SourceDisposition { get; }

    public IReadOnlyList<DesktopSceneSourceOption> SourceOptions { get; }

    private static string FormatAction(SceneApplyAction action) => action switch
    {
        SceneApplyAction.Blocked => "BLOCKED",
        SceneApplyAction.NoChange => "NO CHANGE",
        SceneApplyAction.Handoff => "HANDOFF",
        SceneApplyAction.Move => "MOVE",
        SceneApplyAction.Replace => "REPLACE WITH UNDO",
        _ => "UNKNOWN ACTION",
    };

    private static string FormatReason(SceneApplyItemReason reason) => reason switch
    {
        SceneApplyItemReason.None => "Ready",
        SceneApplyItemReason.SourceNotFound => "Source Activity not found",
        SceneApplyItemReason.SourceSelectionRequired =>
            "Multiple exact sources require explicit selection",
        SceneApplyItemReason.SourceLookupUnavailable => "Source lookup unavailable",
        SceneApplyItemReason.CapabilityDenied => "Scene capability denied",
        SceneApplyItemReason.ProtocolUnsupported => "Peer protocol unsupported",
        SceneApplyItemReason.DestinationUnavailable => "Destination unavailable",
        SceneApplyItemReason.DestinationOccupied => "Destination must be empty",
        SceneApplyItemReason.OpaqueOccupancy =>
            "Destination occupancy is protected or ineligible",
        SceneApplyItemReason.AmbiguousOccupancy =>
            "Destination occupancy is ambiguous",
        SceneApplyItemReason.UndoUnavailable => "Durable undo unavailable",
        SceneApplyItemReason.UnsafeMoveReplace =>
            "Move plus Replace is unsafe and blocked",
        SceneApplyItemReason.Cancelled => "Cancelled",
        SceneApplyItemReason.NotAttemptedAfterRecovering =>
            "Not attempted after uncertain outcome",
        _ => "Unavailable",
    };

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed record DesktopSceneApplyResultItem(
    string ItemLabel,
    string ActivityId,
    string Action,
    string Outcome,
    string Reason,
    string OperationId,
    string CorrelationId,
    string OccurredAt,
    string UndoCapsule);

public sealed record DesktopSceneCompensationItem(
    string ItemLabel,
    string TargetDeviceId,
    string CapsuleId,
    string Outcome,
    string Reason,
    string OperationId,
    string CorrelationId,
    string OccurredAt);

public sealed class SceneApplyViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AsyncRelayCommand applyCommand;
    private readonly AsyncRelayCommand compensateCommand;
    private readonly IDesktopSceneApplyService service;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly AsyncRelayCommand previewCommand;
    private readonly AsyncRelayCommand repreviewCommand;
    private readonly TimeProvider timeProvider;
    private bool disposed;
    private bool hasAcknowledgedApply;
    private bool hasAcknowledgedCompensation;
    private bool isBusy;
    private SceneApplyPreview? preview;
    private SceneApplyResult? result;
    private ScenePlan? scene;
    private long? observedGroupRevision;
    private string compensationDescription =
        "No Scene compensation has been requested.";
    private string compensationStatus = "NO COMPENSATION RESULT";
    private string previewDescription =
        "Select a Scene through the Scene repository workflow, then preview current state.";
    private string previewExpiry = "No preview expiry.";
    private string previewStatus = "NO SCENE SELECTED";
    private string resultDescription = "No Scene apply has been attempted.";
    private string resultStatus = "NO APPLY RESULT";
    private string staleGroupWarning = "No stale Group warning.";

    public SceneApplyViewModel(
        IDesktopSceneApplyService service,
        TimeProvider? timeProvider = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        previewCommand = new AsyncRelayCommand(
            PreviewAsync,
            () => CanPreview);
        repreviewCommand = new AsyncRelayCommand(
            RepreviewAsync,
            () => CanRepreview);
        applyCommand = new AsyncRelayCommand(
            ApplyAsync,
            () => CanApply);
        compensateCommand = new AsyncRelayCommand(
            CompensateAsync,
            () => CanCompensate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ApplyCommand => applyCommand;

    public bool CanApply => !isBusy
        && scene is not null
        && preview is not null
        && preview.ExpiresAt > timeProvider.GetUtcNow()
        && HasAcknowledgedApply
        && PreviewItems.Where(static item => item.IsReplace)
            .All(static item => item.IsReplaceConfirmed);

    public bool CanCompensate => !isBusy
        && result is not null
        && result.Items.Any(static item => item.UndoCapsule is not null)
        && HasAcknowledgedCompensation;

    public bool CanPreview => !isBusy
        && scene is not null
        && service.IsSceneApplyReady;

    public bool CanRepreview => CanPreview
        && PreviewItems.Any(static item =>
            item.CanSelectSource && item.SelectedSource is not null);

    public string CompensationDescription
    {
        get => compensationDescription;
        private set => SetProperty(ref compensationDescription, value);
    }

    public ObservableCollection<DesktopSceneCompensationItem> CompensationItems { get; } = [];

    public string CompensationStatus
    {
        get => compensationStatus;
        private set => SetProperty(ref compensationStatus, value);
    }

    public ICommand CompensateCommand => compensateCommand;

    public bool HasAcknowledgedApply
    {
        get => hasAcknowledgedApply;
        set
        {
            if (SetProperty(ref hasAcknowledgedApply, value))
            {
                NotifyCommandState();
            }
        }
    }

    public bool HasAcknowledgedCompensation
    {
        get => hasAcknowledgedCompensation;
        set
        {
            if (SetProperty(ref hasAcknowledgedCompensation, value))
            {
                NotifyCommandState();
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
                NotifyCommandState();
            }
        }
    }

    public string PreviewDescription
    {
        get => previewDescription;
        private set => SetProperty(ref previewDescription, value);
    }

    public string PreviewExpiry
    {
        get => previewExpiry;
        private set => SetProperty(ref previewExpiry, value);
    }

    public ObservableCollection<DesktopSceneApplyItemViewModel> PreviewItems { get; } = [];

    public ICommand PreviewCommand => previewCommand;

    public string PreviewStatus
    {
        get => previewStatus;
        private set => SetProperty(ref previewStatus, value);
    }

    public ICommand RepreviewCommand => repreviewCommand;

    public string ResultDescription
    {
        get => resultDescription;
        private set => SetProperty(ref resultDescription, value);
    }

    public ObservableCollection<DesktopSceneApplyResultItem> ResultItems { get; } = [];

    public string ResultStatus
    {
        get => resultStatus;
        private set => SetProperty(ref resultStatus, value);
    }

    public string SceneDescription => scene is null
        ? "No Scene is selected. Scene repository lifecycle is handled separately."
        : $"{scene.Activities.Length} ordered Activities; revision {scene.Revision}; format {scene.FormatVersion}.";

    public string SceneName => scene?.Name ?? "No Scene selected";

    public string StaleGroupWarning
    {
        get => staleGroupWarning;
        private set => SetProperty(ref staleGroupWarning, value);
    }

    public void SelectScene(ScenePlan selectedScene, long? currentGroupRevision = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        scene = selectedScene ?? throw new ArgumentNullException(nameof(selectedScene));
        observedGroupRevision = currentGroupRevision;
        ClearWorkflow();
        PreviewStatus = "SCENE SELECTED — PREVIEW REQUIRED";
        PreviewDescription =
            "Preview reads exact current sources and destination occupancy without mutation.";
        OnPropertyChanged(nameof(SceneName));
        OnPropertyChanged(nameof(SceneDescription));
        NotifyCommandState();
    }

    public async Task PreviewAsync()
    {
        await PreviewCoreAsync([]).ConfigureAwait(true);
    }

    public async Task RepreviewAsync()
    {
        SceneSourceSelection[] selections = PreviewItems
            .Where(static item => item.SelectedSource is not null)
            .Select(static item => item.SelectedSource!.Selection)
            .ToArray();
        await PreviewCoreAsync(selections).ConfigureAwait(true);
    }

    public async Task ApplyAsync()
    {
        if (!CanApply || scene is null || preview is null)
        {
            // Re-render the expiry state so a rejected attempt (for example on
            // an expired preview) is truthfully presented instead of leaving a
            // dead enabled-looking control.
            RefreshExpiryState();
            return;
        }

        IsBusy = true;
        try
        {
            SceneReplaceConfirmation[] confirmations = PreviewItems
                .Where(static item => item.IsReplace && item.IsReplaceConfirmed)
                .Select(static item => item.ReplaceConfirmation
                    ?? throw new InvalidOperationException(
                        "A Scene Replace row requires its exact confirmation."))
                .ToArray();
            SceneApplyExecutionResult execution = await service.ApplySceneAsync(
                scene,
                preview,
                SceneApplyApproval.Create(preview.Fingerprint, confirmations),
                lifetimeCancellation.Token).ConfigureAwait(true);
            if (execution.Result is null)
            {
                result = null;
                ResultItems.Clear();
                ResultStatus = $"APPLY REJECTED — {FormatApprovalStatus(execution.ApprovalStatus)}";
                ResultDescription =
                    "No new Scene mutation was authorized. Generate a fresh preview before retrying.";
                return;
            }

            result = execution.Result;
            RenderResult(result);
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            result = null;
            ResultItems.Clear();
            ResultStatus = "APPLY CANCELLED";
            ResultDescription =
                "Cancellation does not imply rollback; inspect every recorded item outcome.";
        }
        catch (Exception)
        {
            result = null;
            ResultItems.Clear();
            ResultStatus = "APPLY RECOVERY REQUIRED";
            ResultDescription =
                "The presentation could not prove a terminal outcome. No exception or Activity content is displayed.";
        }
        finally
        {
            IsBusy = false;
            HasAcknowledgedApply = false;
            NotifyCommandState();
        }
    }

    public async Task CompensateAsync()
    {
        if (!CanCompensate || result is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            SceneCompensationResult compensation =
                await service.CompensateSceneAsync(
                    result,
                    lifetimeCancellation.Token).ConfigureAwait(true);
            CompensationItems.Clear();
            foreach (SceneCompensationItemResult item in compensation.Items)
            {
                CompensationItems.Add(new DesktopSceneCompensationItem(
                    $"ITEM {item.SceneIndex + 1}",
                    item.TargetDeviceId.ToString(),
                    item.CapsuleId.ToString(),
                    FormatCompensationOutcome(item.Outcome),
                    item.FailureCode.ToString(),
                    item.OperationId.ToString(),
                    item.CorrelationId.ToString(),
                    item.OccurredAt.ToString("O")));
            }

            CompensationStatus = compensation.Status switch
            {
                SceneCompensationStatus.NothingToUndo => "NOTHING ELIGIBLE TO UNDO",
                SceneCompensationStatus.Completed => "COMPENSATION COMPLETED",
                SceneCompensationStatus.PartiallyCompleted =>
                    "COMPENSATION PARTIALLY COMPLETED",
                SceneCompensationStatus.Recovering =>
                    "COMPENSATION RECOVERY REQUIRED",
                SceneCompensationStatus.Cancelled => "COMPENSATION CANCELLED",
                _ => "COMPENSATION STATUS UNAVAILABLE",
            };
            CompensationDescription =
                "Only eligible committed Preserve-Source Replace items were attempted in reverse Scene order. Handoff and Move were not reversed.";
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            CompensationStatus = "COMPENSATION CANCELLED";
            CompensationDescription =
                "Cancellation is not represented as whole-Scene rollback.";
        }
        catch (Exception)
        {
            CompensationStatus = "COMPENSATION RECOVERY REQUIRED";
            CompensationDescription =
                "The presentation could not prove a terminal undo outcome. No exception or Activity content is displayed.";
        }
        finally
        {
            IsBusy = false;
            HasAcknowledgedCompensation = false;
            NotifyCommandState();
        }
    }

    public void RefreshExpiryState()
    {
        if (preview is null)
        {
            return;
        }

        RenderExpiry(preview);
        NotifyCommandState();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    private async Task PreviewCoreAsync(
        IEnumerable<SceneSourceSelection> selections)
    {
        if (!CanPreview || scene is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            SceneApplyPreview next = await service.PreviewSceneAsync(
                scene,
                selections,
                observedGroupRevision,
                lifetimeCancellation.Token).ConfigureAwait(true);
            preview = next;
            result = null;
            PreviewItems.Clear();
            foreach (SceneApplyItemPreview item in next.Items)
            {
                SceneReplaceConfirmation? confirmation =
                    next.RequiredReplaceConfirmations.SingleOrDefault(
                        candidate => candidate.Index == item.Index);
                PreviewItems.Add(new DesktopSceneApplyItemViewModel(
                    item,
                    confirmation,
                    NotifyCommandState));
            }

            ResultItems.Clear();
            CompensationItems.Clear();
            HasAcknowledgedApply = false;
            HasAcknowledgedCompensation = false;
            ResultStatus = "NO APPLY RESULT";
            ResultDescription = "No Scene apply has been attempted for this preview.";
            CompensationStatus = "NO COMPENSATION RESULT";
            CompensationDescription =
                "No Scene compensation has been requested.";
            PreviewStatus = next.Items.Any(static item =>
                item.Action == SceneApplyAction.Blocked)
                ? "PREVIEW READY — BLOCKERS PRESENT"
                : "PREVIEW READY";
            PreviewDescription =
                "Items remain in saved Scene order. Every action and blocker is bound to this expiring preview.";
            StaleGroupWarning = next.GroupRevisionWarning is null
                ? "No stale Group warning."
                : $"STALE GROUP — saved revision {next.GroupRevisionWarning.BoundRevision}; observed revision {next.GroupRevisionWarning.ObservedRevision}. Saved Scene item order remains authoritative.";
            RenderExpiry(next);
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            PreviewStatus = "PREVIEW CANCELLED";
            PreviewDescription = "No mutation authority was acquired.";
        }
        catch (Exception)
        {
            preview = null;
            PreviewItems.Clear();
            PreviewStatus = "PREVIEW UNAVAILABLE";
            PreviewDescription =
                "Current-state evidence could not be completed. No exception or Activity content is displayed.";
            PreviewExpiry = "Generate a complete fresh preview before applying.";
        }
        finally
        {
            IsBusy = false;
            NotifyCommandState();
        }
    }

    private void RenderExpiry(SceneApplyPreview value)
    {
        bool expired = value.ExpiresAt <= timeProvider.GetUtcNow();
        PreviewExpiry = expired
            ? $"EXPIRED AT {value.ExpiresAt:O} — generate a fresh complete preview."
            : $"Expires at {value.ExpiresAt:O}.";
        if (expired)
        {
            PreviewStatus = "PREVIEW EXPIRED";
        }
    }

    private void RenderResult(SceneApplyResult applyResult)
    {
        ResultItems.Clear();
        foreach (SceneApplyItemResult item in applyResult.Items)
        {
            ResultItems.Add(new DesktopSceneApplyResultItem(
                $"ITEM {item.Index + 1}",
                item.ActivityId.ToString(),
                item.Action.ToString(),
                item.Outcome.ToString(),
                item.Reason != SceneApplyItemReason.None
                    ? item.Reason.ToString()
                    : item.FailureCode.ToString(),
                item.ChildOperationId.ToString(),
                item.ChildCorrelationId.ToString(),
                item.OccurredAt.ToString("O"),
                item.UndoCapsule?.Id.ToString() ?? "None"));
        }

        ResultStatus = applyResult.Status switch
        {
            SceneApplyOverallStatus.Completed => "SCENE COMPLETED",
            SceneApplyOverallStatus.CompletedWithWarnings =>
                "SCENE COMPLETED WITH WARNINGS",
            SceneApplyOverallStatus.PartiallyCompleted =>
                "SCENE PARTIALLY COMPLETED",
            SceneApplyOverallStatus.Blocked => "SCENE BLOCKED",
            SceneApplyOverallStatus.Recovering => "SCENE RECOVERY REQUIRED",
            SceneApplyOverallStatus.Cancelled => "SCENE CANCELLED",
            _ => "SCENE RESULT UNAVAILABLE",
        };
        ResultDescription =
            "This is a per-item non-atomic result. Terminal failures may coexist with committed work; Recovering never means rollback.";
        OnPropertyChanged(nameof(CanCompensate));
    }

    private void ClearWorkflow()
    {
        preview = null;
        result = null;
        PreviewItems.Clear();
        ResultItems.Clear();
        CompensationItems.Clear();
        HasAcknowledgedApply = false;
        HasAcknowledgedCompensation = false;
        PreviewExpiry = "No preview expiry.";
        StaleGroupWarning = "No stale Group warning.";
        ResultStatus = "NO APPLY RESULT";
        ResultDescription = "No Scene apply has been attempted.";
        CompensationStatus = "NO COMPENSATION RESULT";
        CompensationDescription = "No Scene compensation has been requested.";
    }

    private void NotifyCommandState()
    {
        previewCommand.NotifyCanExecuteChanged();
        repreviewCommand.NotifyCanExecuteChanged();
        applyCommand.NotifyCanExecuteChanged();
        compensateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanRepreview));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCompensate));
    }

    private static string FormatApprovalStatus(SceneApplyApprovalStatus status) =>
        status switch
        {
            SceneApplyApprovalStatus.SceneChanged => "SCENE CHANGED",
            SceneApplyApprovalStatus.PreviewMismatch => "PREVIEW CHANGED",
            SceneApplyApprovalStatus.Expired => "PREVIEW EXPIRED",
            SceneApplyApprovalStatus.ReplaceConfirmationMismatch =>
                "REPLACE CONFIRMATION MISMATCH",
            SceneApplyApprovalStatus.Valid => "VALID",
            _ => "UNAVAILABLE",
        };

    private static string FormatCompensationOutcome(
        SceneCompensationItemOutcome outcome) => outcome switch
        {
            SceneCompensationItemOutcome.Committed => "COMMITTED",
            SceneCompensationItemOutcome.Rejected => "REJECTED",
            SceneCompensationItemOutcome.Failed => "FAILED",
            SceneCompensationItemOutcome.Recovering => "RECOVERING",
            SceneCompensationItemOutcome.Cancelled => "CANCELLED",
            _ => "UNAVAILABLE",
        };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

internal sealed class UnavailableDesktopSceneApplyService : IDesktopSceneApplyService
{
    private UnavailableDesktopSceneApplyService()
    {
    }

    public static UnavailableDesktopSceneApplyService Instance { get; } = new();

    public bool IsSceneApplyReady => false;

    public ValueTask<SceneApplyPreview> PreviewSceneAsync(
        ScenePlan scene,
        IEnumerable<SceneSourceSelection> selectedSources,
        long? observedGroupRevision,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<SceneApplyPreview>(CreateException());

    public ValueTask<SceneApplyExecutionResult> ApplySceneAsync(
        ScenePlan scene,
        SceneApplyPreview preview,
        SceneApplyApproval approval,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<SceneApplyExecutionResult>(CreateException());

    public ValueTask<SceneCompensationResult> CompensateSceneAsync(
        SceneApplyResult applyResult,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<SceneCompensationResult>(CreateException());

    private static PlatformNotSupportedException CreateException() =>
        new("Scene Apply is not configured by this desktop service.");
}
