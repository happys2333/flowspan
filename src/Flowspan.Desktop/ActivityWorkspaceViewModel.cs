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

public interface IDesktopActivityService : IAsyncDisposable
{
    public event Action? Changed;

    public bool IsReady => true;

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
}

public sealed class ActivityWorkspaceViewModel :
    INotifyPropertyChanged,
    IDisposable,
    IAsyncDisposable
{
    private readonly AsyncRelayCommand handoffCommand;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly AsyncRelayCommand moveCommand;
    private readonly IDesktopActivityService service;
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly RelayCommand createWorkspaceNoteCommand;
    private string creationStatus = string.Empty;
    private string draftText = string.Empty;
    private string draftTitle = string.Empty;
    private bool isBusy;
    private string receiptCorrelationId = string.Empty;
    private string receiptOccurredAt = string.Empty;
    private string receiptReason = string.Empty;
    private string receiptStatus = string.Empty;
    private string receiptSummary = string.Empty;
    private string undoDescription = string.Empty;
    private DesktopActivitySnapshot? selectedActivity;
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
        service.Changed += OnServiceChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DesktopActivitySnapshot> Activities { get; } = [];

    public ObservableCollection<DesktopActivityTargetSnapshot> Targets { get; } = [];

    public ICommand CreateWorkspaceNoteCommand => createWorkspaceNoteCommand;

    public ICommand HandoffCommand => handoffCommand;

    public ICommand MoveCommand => moveCommand;

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
                OnPropertyChanged(nameof(IsNoteCreationAvailable));
                createWorkspaceNoteCommand.NotifyCanExecuteChanged();
                handoffCommand.NotifyCanExecuteChanged();
                moveCommand.NotifyCanExecuteChanged();
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

    public DesktopActivitySnapshot? SelectedActivity
    {
        get => selectedActivity;
        set
        {
            if (SetProperty(ref selectedActivity, value))
            {
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
                OnPreviewChanged();
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
        handoffCommand.NotifyCanExecuteChanged();
        moveCommand.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(IsReady));
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
