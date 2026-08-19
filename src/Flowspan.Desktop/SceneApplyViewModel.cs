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
        Description = DesktopText.Format(
            "Scene_SourceDescriptionFormat",
            selection.DeviceId,
            selection.Revision,
            selection.Kind,
            selection.Placement.Slot);
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
            SceneSourceDisposition.PreserveSource => DesktopText.Get(
                "Scene_SourceDisposition_PreserveSource"),
            SceneSourceDisposition.MoveAfterAcknowledgement =>
                DesktopText.Get(
                    "Scene_SourceDisposition_MoveAfterAcknowledgement"),
            _ => DesktopText.Get("Scene_SourceDisposition_Unavailable"),
        };
        SourceDescription = item.Source is { } source
            ? DesktopText.Format(
                "Scene_SourceDescriptionFormat",
                source.DeviceId,
                source.Revision,
                source.Kind,
                source.Placement.Slot)
            : item.Reason == SceneApplyItemReason.SourceSelectionRequired
                ? DesktopText.Get("Scene_SourceSelectionRequiredDescription")
                : DesktopText.Get("Scene_SourceMetadataUnavailable");
        DestinationDescription = DesktopText.Format(
            "Scene_DestinationDescriptionFormat",
            item.Destination.DeviceId,
            item.Destination.Slot);
        ReplaceTargetDescription = item.Action == SceneApplyAction.Replace
            && item.ReplaceTarget is { } target
            ? DesktopText.Format(
                "Scene_ReplaceTargetDescriptionFormat",
                target.ActivityId,
                target.DeviceId,
                target.Revision,
                target.Kind,
                target.Placement.Slot,
                target.DescriptorDigest)
            : item.Occupancy.Kind switch
            {
                SceneSlotOccupancyKind.Opaque =>
                    DesktopText.Get("Scene_Occupancy_Opaque"),
                SceneSlotOccupancyKind.Ambiguous =>
                    DesktopText.Get("Scene_Occupancy_Ambiguous"),
                _ => DesktopText.Get("Scene_NoReplacement"),
            };
        IsReplace = item.Action == SceneApplyAction.Replace;
        ReplaceConfirmation = replaceConfirmation;
        ReplaceConfirmationAutomationName = item.ReplaceTarget is { } exact
            ? DesktopText.Format(
                "Scene_ReplaceConfirmationAutomationNameFormat",
                item.Index + 1,
                exact.ActivityId,
                exact.DeviceId,
                exact.Revision)
            : DesktopText.Get("Scene_NoReplaceConfirmationAutomationName");
        SourceOptions = item.SourceLookup?.Candidates
            .Select(static candidate => new DesktopSceneSourceOption(candidate))
            .ToArray() ?? [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Action { get; }

    public string ActivityId { get; }

    public string DestinationDescription { get; }

    public int Index { get; }

    public string ItemLabel => DesktopText.Format(
        "Scene_ItemLabelFormat",
        Index + 1);

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
        SceneApplyAction.Blocked => DesktopText.Get("Scene_Action_Blocked"),
        SceneApplyAction.NoChange => DesktopText.Get("Scene_Action_NoChange"),
        SceneApplyAction.Handoff => DesktopText.Get("Scene_Action_Handoff"),
        SceneApplyAction.Move => DesktopText.Get("Scene_Action_Move"),
        SceneApplyAction.Replace => DesktopText.Get("Scene_Action_Replace"),
        _ => DesktopText.Get("Scene_Action_Unknown"),
    };

    private static string FormatReason(SceneApplyItemReason reason) => reason switch
    {
        SceneApplyItemReason.None => DesktopText.Get("Scene_Reason_None"),
        SceneApplyItemReason.SourceNotFound => DesktopText.Get(
            "Scene_Reason_SourceNotFound"),
        SceneApplyItemReason.SourceSelectionRequired =>
            DesktopText.Get("Scene_Reason_SourceSelectionRequired"),
        SceneApplyItemReason.SourceLookupUnavailable => DesktopText.Get(
            "Scene_Reason_SourceLookupUnavailable"),
        SceneApplyItemReason.CapabilityDenied => DesktopText.Get(
            "Scene_Reason_CapabilityDenied"),
        SceneApplyItemReason.ProtocolUnsupported => DesktopText.Get(
            "Scene_Reason_ProtocolUnsupported"),
        SceneApplyItemReason.DestinationUnavailable => DesktopText.Get(
            "Scene_Reason_DestinationUnavailable"),
        SceneApplyItemReason.DestinationOccupied => DesktopText.Get(
            "Scene_Reason_DestinationOccupied"),
        SceneApplyItemReason.OpaqueOccupancy =>
            DesktopText.Get("Scene_Reason_OpaqueOccupancy"),
        SceneApplyItemReason.AmbiguousOccupancy =>
            DesktopText.Get("Scene_Reason_AmbiguousOccupancy"),
        SceneApplyItemReason.UndoUnavailable => DesktopText.Get(
            "Scene_Reason_UndoUnavailable"),
        SceneApplyItemReason.UnsafeMoveReplace =>
            DesktopText.Get("Scene_Reason_UnsafeMoveReplace"),
        SceneApplyItemReason.Cancelled => DesktopText.Get(
            "Scene_Reason_Cancelled"),
        SceneApplyItemReason.NotAttemptedAfterRecovering =>
            DesktopText.Get("Scene_Reason_NotAttemptedAfterRecovering"),
        _ => DesktopText.Get("Scene_Reason_Unavailable"),
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
        DesktopText.Get("Scene_CompensationNotRequested");
    private string compensationStatus = DesktopText.Get(
        "Scene_CompensationStatus_None");
    private string previewDescription =
        DesktopText.Get("Scene_PreviewDescription_SelectFromRepository");
    private string previewExpiry = DesktopText.Get("Scene_PreviewExpiry_None");
    private string previewStatus = DesktopText.Get("Scene_PreviewStatus_NoSelection");
    private string resultDescription = DesktopText.Get(
        "Scene_ResultDescription_NotAttempted");
    private string resultStatus = DesktopText.Get("Scene_ResultStatus_None");
    private string staleGroupWarning = DesktopText.Get(
        "Scene_StaleGroupWarning_None");

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
        ? DesktopText.Get("Scene_Description_NoSelection")
        : DesktopText.Format(
            "Scene_Description_SelectedFormat",
            scene.Activities.Length,
            scene.Revision,
            scene.FormatVersion);

    public string SceneName => scene?.Name
        ?? DesktopText.Get("Scene_Name_NoSelection");

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
        PreviewStatus = DesktopText.Get("Scene_PreviewStatus_Selected");
        PreviewDescription =
            DesktopText.Get("Scene_PreviewDescription_Selected");
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
                ResultStatus = DesktopText.Format(
                    "Scene_ResultStatus_RejectedFormat",
                    FormatApprovalStatus(execution.ApprovalStatus));
                ResultDescription =
                    DesktopText.Get("Scene_ResultDescription_Rejected");
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
            ResultStatus = DesktopText.Get("Scene_ResultStatus_ApplyCancelled");
            ResultDescription =
                DesktopText.Get("Scene_ResultDescription_ApplyCancelled");
        }
        catch (Exception)
        {
            result = null;
            ResultItems.Clear();
            ResultStatus = DesktopText.Get("Scene_ResultStatus_ApplyRecovering");
            ResultDescription =
                DesktopText.Get("Scene_ResultDescription_ApplyRecovering");
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
                    DesktopText.Format(
                        "Scene_ItemLabelFormat",
                        item.SceneIndex + 1),
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
                SceneCompensationStatus.NothingToUndo => DesktopText.Get(
                    "Scene_CompensationStatus_NothingToUndo"),
                SceneCompensationStatus.Completed => DesktopText.Get(
                    "Scene_CompensationStatus_Completed"),
                SceneCompensationStatus.PartiallyCompleted =>
                    DesktopText.Get(
                        "Scene_CompensationStatus_PartiallyCompleted"),
                SceneCompensationStatus.Recovering =>
                    DesktopText.Get("Scene_CompensationStatus_Recovering"),
                SceneCompensationStatus.Cancelled => DesktopText.Get(
                    "Scene_CompensationStatus_Cancelled"),
                _ => DesktopText.Get("Scene_CompensationStatus_Unavailable"),
            };
            CompensationDescription =
                DesktopText.Get("Scene_CompensationDescription_Completed");
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            CompensationStatus = DesktopText.Get(
                "Scene_CompensationStatus_Cancelled");
            CompensationDescription =
                DesktopText.Get("Scene_CompensationDescription_Cancelled");
        }
        catch (Exception)
        {
            CompensationStatus = DesktopText.Get(
                "Scene_CompensationStatus_Recovering");
            CompensationDescription =
                DesktopText.Get("Scene_CompensationDescription_Recovering");
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
            ResultStatus = DesktopText.Get("Scene_ResultStatus_None");
            ResultDescription = DesktopText.Get(
                "Scene_ResultDescription_NotAttemptedForPreview");
            CompensationStatus = DesktopText.Get(
                "Scene_CompensationStatus_None");
            CompensationDescription =
                DesktopText.Get("Scene_CompensationNotRequested");
            PreviewStatus = next.Items.Any(static item =>
                item.Action == SceneApplyAction.Blocked)
                ? DesktopText.Get("Scene_PreviewStatus_ReadyWithBlockers")
                : DesktopText.Get("Scene_PreviewStatus_Ready");
            PreviewDescription =
                DesktopText.Get("Scene_PreviewDescription_Ready");
            StaleGroupWarning = next.GroupRevisionWarning is null
                ? DesktopText.Get("Scene_StaleGroupWarning_None")
                : DesktopText.Format(
                    "Scene_StaleGroupWarning_Format",
                    next.GroupRevisionWarning.BoundRevision,
                    next.GroupRevisionWarning.ObservedRevision);
            RenderExpiry(next);
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            PreviewStatus = DesktopText.Get("Scene_PreviewStatus_Cancelled");
            PreviewDescription = DesktopText.Get(
                "Scene_PreviewDescription_Cancelled");
        }
        catch (Exception)
        {
            preview = null;
            PreviewItems.Clear();
            PreviewStatus = DesktopText.Get("Scene_PreviewStatus_Unavailable");
            PreviewDescription =
                DesktopText.Get("Scene_PreviewDescription_Unavailable");
            PreviewExpiry = DesktopText.Get(
                "Scene_PreviewExpiry_FreshPreviewRequired");
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
            ? DesktopText.Format(
                "Scene_PreviewExpiry_ExpiredAtFormat",
                value.ExpiresAt.ToString("O"))
            : DesktopText.Format(
                "Scene_PreviewExpiry_ExpiresAtFormat",
                value.ExpiresAt.ToString("O"));
        if (expired)
        {
            PreviewStatus = DesktopText.Get("Scene_PreviewStatus_Expired");
        }
    }

    private void RenderResult(SceneApplyResult applyResult)
    {
        ResultItems.Clear();
        foreach (SceneApplyItemResult item in applyResult.Items)
        {
            ResultItems.Add(new DesktopSceneApplyResultItem(
                DesktopText.Format("Scene_ItemLabelFormat", item.Index + 1),
                item.ActivityId.ToString(),
                FormatResultAction(item.Action),
                FormatResultOutcome(item.Outcome),
                item.Reason != SceneApplyItemReason.None
                    ? item.Reason.ToString()
                    : item.FailureCode.ToString(),
                item.ChildOperationId.ToString(),
                item.ChildCorrelationId.ToString(),
                item.OccurredAt.ToString("O"),
                item.UndoCapsule?.Id.ToString()
                    ?? DesktopText.Get("Scene_UndoCapsule_None")));
        }

        ResultStatus = applyResult.Status switch
        {
            SceneApplyOverallStatus.Completed => DesktopText.Get(
                "Scene_ResultStatus_Completed"),
            SceneApplyOverallStatus.CompletedWithWarnings =>
                DesktopText.Get("Scene_ResultStatus_CompletedWithWarnings"),
            SceneApplyOverallStatus.PartiallyCompleted =>
                DesktopText.Get("Scene_ResultStatus_PartiallyCompleted"),
            SceneApplyOverallStatus.Blocked => DesktopText.Get(
                "Scene_ResultStatus_Blocked"),
            SceneApplyOverallStatus.Recovering => DesktopText.Get(
                "Scene_ResultStatus_Recovering"),
            SceneApplyOverallStatus.Cancelled => DesktopText.Get(
                "Scene_ResultStatus_Cancelled"),
            _ => DesktopText.Get("Scene_ResultStatus_Unavailable"),
        };
        ResultDescription =
            DesktopText.Get("Scene_ResultDescription_Completed");
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
        PreviewExpiry = DesktopText.Get("Scene_PreviewExpiry_None");
        StaleGroupWarning = DesktopText.Get("Scene_StaleGroupWarning_None");
        ResultStatus = DesktopText.Get("Scene_ResultStatus_None");
        ResultDescription = DesktopText.Get("Scene_ResultDescription_NotAttempted");
        CompensationStatus = DesktopText.Get("Scene_CompensationStatus_None");
        CompensationDescription = DesktopText.Get(
            "Scene_CompensationNotRequested");
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
            SceneApplyApprovalStatus.SceneChanged => DesktopText.Get(
                "Scene_ApprovalStatus_SceneChanged"),
            SceneApplyApprovalStatus.PreviewMismatch => DesktopText.Get(
                "Scene_ApprovalStatus_PreviewMismatch"),
            SceneApplyApprovalStatus.Expired => DesktopText.Get(
                "Scene_ApprovalStatus_Expired"),
            SceneApplyApprovalStatus.ReplaceConfirmationMismatch =>
                DesktopText.Get(
                    "Scene_ApprovalStatus_ReplaceConfirmationMismatch"),
            SceneApplyApprovalStatus.Valid => DesktopText.Get(
                "Scene_ApprovalStatus_Valid"),
            _ => DesktopText.Get("Scene_ApprovalStatus_Unavailable"),
        };

    private static string FormatCompensationOutcome(
        SceneCompensationItemOutcome outcome) => outcome switch
        {
            SceneCompensationItemOutcome.Committed => DesktopText.Get(
                "Scene_CompensationOutcome_Committed"),
            SceneCompensationItemOutcome.Rejected => DesktopText.Get(
                "Scene_CompensationOutcome_Rejected"),
            SceneCompensationItemOutcome.Failed => DesktopText.Get(
                "Scene_CompensationOutcome_Failed"),
            SceneCompensationItemOutcome.Recovering => DesktopText.Get(
                "Scene_CompensationOutcome_Recovering"),
            SceneCompensationItemOutcome.Cancelled => DesktopText.Get(
                "Scene_CompensationOutcome_Cancelled"),
            _ => DesktopText.Get("Scene_CompensationOutcome_Unavailable"),
        };

    private static string FormatResultAction(SceneApplyAction action) => action switch
    {
        SceneApplyAction.Blocked => DesktopText.Get("Scene_ResultAction_Blocked"),
        SceneApplyAction.NoChange => DesktopText.Get("Scene_ResultAction_NoChange"),
        SceneApplyAction.Handoff => DesktopText.Get("Scene_ResultAction_Handoff"),
        SceneApplyAction.Move => DesktopText.Get("Scene_ResultAction_Move"),
        SceneApplyAction.Replace => DesktopText.Get("Scene_ResultAction_Replace"),
        _ => DesktopText.Get("Scene_ResultAction_Unknown"),
    };

    private static string FormatResultOutcome(SceneApplyItemOutcome outcome) =>
        outcome switch
        {
            SceneApplyItemOutcome.Blocked => DesktopText.Get(
                "Scene_ResultOutcome_Blocked"),
            SceneApplyItemOutcome.NoChange => DesktopText.Get(
                "Scene_ResultOutcome_NoChange"),
            SceneApplyItemOutcome.Committed => DesktopText.Get(
                "Scene_ResultOutcome_Committed"),
            SceneApplyItemOutcome.CommittedWithWarning => DesktopText.Get(
                "Scene_ResultOutcome_CommittedWithWarning"),
            SceneApplyItemOutcome.Rejected => DesktopText.Get(
                "Scene_ResultOutcome_Rejected"),
            SceneApplyItemOutcome.Failed => DesktopText.Get(
                "Scene_ResultOutcome_Failed"),
            SceneApplyItemOutcome.Recovering => DesktopText.Get(
                "Scene_ResultOutcome_Recovering"),
            SceneApplyItemOutcome.NotAttempted => DesktopText.Get(
                "Scene_ResultOutcome_NotAttempted"),
            _ => DesktopText.Get("Scene_ResultOutcome_Unavailable"),
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
