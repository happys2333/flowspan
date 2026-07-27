using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Desktop;

public interface IDesktopSceneRepositoryService : IAsyncDisposable
{
    public bool IsSceneRepositoryReady { get; }

    public ValueTask InitializeAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<ImmutableArray<SceneRepositoryEntry>> ListScenesAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<SceneRepositoryEntry> SaveSceneAsync(
        ScenePlan scene,
        CancellationToken cancellationToken = default);

    public ValueTask<bool> DeleteSceneAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default);

    public ValueTask<DesktopSceneExportResult?> ExportSceneAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default);
}

public sealed record DesktopSceneExportResult(
    string FullPath,
    string RedactedContent);

public sealed record DesktopSceneRepositoryPlanItem(
    string ItemLabel,
    string ActivityId,
    string Destination,
    string SourceDisposition,
    string ConflictPolicy);

public sealed class SceneRepositoryItemViewModel
{
    internal SceneRepositoryItemViewModel(SceneRepositoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        ScenePlan scene = entry.Scene;
        Name = scene.Name;
        SceneId = scene.Id.ToString();
        Summary =
            $"{scene.Activities.Length} ordered Activities; revision {scene.Revision}; format {scene.FormatVersion}";
        GroupBinding = scene.GroupBinding is null
            ? "No Group binding."
            : $"Group {scene.GroupBinding.GroupId} revision {scene.GroupBinding.GroupRevision}";
        SceneDigest = entry.SceneDigest;
        SavedAt = entry.SavedAt.ToString("O");
        AutomationName =
            $"Stored Scene {scene.Id} revision {scene.Revision} with {scene.Activities.Length} Activities";
    }

    internal SceneRepositoryEntry Entry { get; }

    public string AutomationName { get; }

    public string GroupBinding { get; }

    public string Name { get; }

    public string SavedAt { get; }

    public string SceneDigest { get; }

    public string SceneId { get; }

    public string Summary { get; }
}

public sealed class SceneRepositoryViewModel :
    INotifyPropertyChanged,
    IDisposable,
    IAsyncDisposable
{
    private readonly AsyncRelayCommand confirmDeleteCommand;
    private readonly AsyncRelayCommand exportCommand;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly AsyncRelayCommand refreshCommand;
    private readonly RelayCommand beginDeleteCommand;
    private readonly RelayCommand cancelDeleteCommand;
    private readonly RelayCommand selectForApplyCommand;
    private readonly Action<ScenePlan, long?> selectScene;
    private readonly IDesktopSceneRepositoryService service;
    private bool disposed;
    private bool isBusy;
    private bool isDeleteConfirmationVisible;
    private bool isRepositoryAvailable;
    private SceneRepositoryItemViewModel? selectedScene;
    private string deleteConfirmation = string.Empty;
    private string exportPath = "No Scene export has been written.";
    private string exportPreview =
        "Exports contain identifiers, revisions, digests, timestamps, and policies only.";
    private string lifecycleDescription =
        "Selection hands the stored Scene to the Scene apply preview workflow.";
    private string lifecycleStatus = "NO REPOSITORY ACTION";
    private string repositoryDescription =
        "Stored Scenes live in one protected local repository file.";
    private string repositoryStatus = "SCENE REPOSITORY UNAVAILABLE";

    public SceneRepositoryViewModel(
        IDesktopSceneRepositoryService service,
        Action<ScenePlan, long?> selectScene)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.selectScene = selectScene
            ?? throw new ArgumentNullException(nameof(selectScene));
        refreshCommand = new AsyncRelayCommand(RefreshAsync, () => CanRefresh);
        selectForApplyCommand = new RelayCommand(
            SelectForApply,
            () => CanSelectForApply);
        beginDeleteCommand = new RelayCommand(BeginDelete, () => CanBeginDelete);
        cancelDeleteCommand = new RelayCommand(
            CancelDelete,
            () => IsDeleteConfirmationVisible);
        confirmDeleteCommand = new AsyncRelayCommand(
            ConfirmDeleteAsync,
            () => CanConfirmDelete);
        exportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand BeginDeleteCommand => beginDeleteCommand;

    public ICommand CancelDeleteCommand => cancelDeleteCommand;

    public bool CanBeginDelete => !isBusy
        && selectedScene is not null
        && !IsDeleteConfirmationVisible;

    public bool CanConfirmDelete => !isBusy
        && selectedScene is not null
        && IsDeleteConfirmationVisible;

    public bool CanExport => !isBusy
        && selectedScene is not null
        && IsRepositoryAvailable;

    public bool CanRefresh => !isBusy && service.IsSceneRepositoryReady;

    public bool CanSelectForApply => !isBusy && selectedScene is not null;

    public ICommand ConfirmDeleteCommand => confirmDeleteCommand;

    public string DeleteConfirmation
    {
        get => deleteConfirmation;
        private set => SetProperty(ref deleteConfirmation, value);
    }

    public ICommand ExportCommand => exportCommand;

    public string ExportPath
    {
        get => exportPath;
        private set => SetProperty(ref exportPath, value);
    }

    public string ExportPreview
    {
        get => exportPreview;
        private set => SetProperty(ref exportPreview, value);
    }

    public ObservableCollection<DesktopSceneRepositoryPlanItem> InspectItems { get; } = [];

    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        private set
        {
            if (SetProperty(ref isDeleteConfirmationVisible, value))
            {
                NotifyCommandState();
            }
        }
    }

    public bool IsRepositoryAvailable
    {
        get => isRepositoryAvailable;
        private set
        {
            if (SetProperty(ref isRepositoryAvailable, value))
            {
                NotifyCommandState();
            }
        }
    }

    public string LifecycleDescription
    {
        get => lifecycleDescription;
        private set => SetProperty(ref lifecycleDescription, value);
    }

    public string LifecycleStatus
    {
        get => lifecycleStatus;
        private set => SetProperty(ref lifecycleStatus, value);
    }

    public ICommand RefreshCommand => refreshCommand;

    public string RepositoryDescription
    {
        get => repositoryDescription;
        private set => SetProperty(ref repositoryDescription, value);
    }

    public string RepositoryStatus
    {
        get => repositoryStatus;
        private set => SetProperty(ref repositoryStatus, value);
    }

    public ObservableCollection<SceneRepositoryItemViewModel> Scenes { get; } = [];

    public SceneRepositoryItemViewModel? SelectedScene
    {
        get => selectedScene;
        set
        {
            if (SetProperty(ref selectedScene, value))
            {
                CancelDelete();
                RenderInspectItems();
                NotifyCommandState();
            }
        }
    }

    public ICommand SelectForApplyCommand => selectForApplyCommand;

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRepositoryAvailable)
        {
            return;
        }

        try
        {
            await service.InitializeAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            RepositoryStatus = "SCENE REPOSITORY UNAVAILABLE";
            RepositoryDescription =
                "The protected Scene repository could not be opened. No exception content is displayed.";
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!service.IsSceneRepositoryReady)
        {
            IsRepositoryAvailable = false;
            RepositoryStatus = "SCENE REPOSITORY UNAVAILABLE";
            RepositoryDescription =
                "No protected Scene repository is configured for this desktop session.";
            return;
        }

        IsBusy = true;
        try
        {
            ImmutableArray<SceneRepositoryEntry> entries =
                await service.ListScenesAsync(lifetimeCancellation.Token)
                    .ConfigureAwait(true);
            Scenes.Clear();
            foreach (SceneRepositoryEntry entry in entries)
            {
                Scenes.Add(new SceneRepositoryItemViewModel(entry));
            }

            SelectedScene = null;
            IsRepositoryAvailable = true;
            RepositoryStatus = entries.Length switch
            {
                0 => "SCENE REPOSITORY EMPTY",
                1 => "1 SCENE STORED",
                _ => $"{entries.Length} SCENES STORED",
            };
            RepositoryDescription =
                "Stored Scenes live in one protected local repository file.";
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            RepositoryStatus = "SCENE REPOSITORY REFRESH CANCELLED";
        }
        catch (Exception)
        {
            IsRepositoryAvailable = false;
            RepositoryStatus = "SCENE REPOSITORY UNAVAILABLE";
            RepositoryDescription =
                "Stored Scenes could not be read. No exception content is displayed.";
        }
        finally
        {
            IsBusy = false;
            NotifyCommandState();
        }
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

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await service.DisposeAsync().ConfigureAwait(false);
    }

    private bool IsBusy
    {
        get => isBusy;
        set
        {
            if (isBusy != value)
            {
                isBusy = value;
                NotifyCommandState();
            }
        }
    }

    public void SelectForApply()
    {
        if (selectedScene is null)
        {
            return;
        }

        selectScene(selectedScene.Entry.Scene, null);
        LifecycleStatus = "SCENE SELECTED FOR APPLY";
        LifecycleDescription =
            "The Scene apply panel now binds this stored plan. No current Group revision source exists, so no stale-Group warning is observed at selection.";
    }

    public void BeginDelete()
    {
        if (selectedScene is null)
        {
            return;
        }

        DeleteConfirmation =
            $"Delete Scene \"{selectedScene.Name}\" ({selectedScene.SceneId} revision {selectedScene.Entry.Scene.Revision})? Only the stored plan is removed from this device; applied Activities are not affected. This action has no undo.";
        IsDeleteConfirmationVisible = true;
    }

    public void CancelDelete()
    {
        DeleteConfirmation = string.Empty;
        IsDeleteConfirmationVisible = false;
    }

    public async Task ConfirmDeleteAsync()
    {
        if (selectedScene is null || !IsDeleteConfirmationVisible)
        {
            return;
        }

        SceneId sceneId = selectedScene.Entry.Scene.Id;
        IsBusy = true;
        try
        {
            bool deleted = await service.DeleteSceneAsync(
                sceneId,
                lifetimeCancellation.Token).ConfigureAwait(true);
            LifecycleStatus = deleted
                ? "SCENE DELETED"
                : "SCENE NOT FOUND — REFRESH THE LIST";
            LifecycleDescription = deleted
                ? "The stored Scene plan was removed. Scene apply journals, Replace undo state, and applied Activities were not touched."
                : "The stored Scene no longer exists. Refresh the repository list.";
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            LifecycleStatus = "DELETE CANCELLED";
            LifecycleDescription =
                "The delete request was cancelled before a durable outcome was observed.";
        }
        catch (Exception)
        {
            LifecycleStatus = "DELETE FAILED";
            LifecycleDescription =
                "The stored Scene could not be deleted. No exception content is displayed.";
        }
        finally
        {
            IsBusy = false;
            CancelDelete();
            NotifyCommandState();
        }

        if (!disposed)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    public async Task ExportAsync()
    {
        if (selectedScene is null)
        {
            return;
        }

        SceneId sceneId = selectedScene.Entry.Scene.Id;
        IsBusy = true;
        try
        {
            DesktopSceneExportResult? export = await service.ExportSceneAsync(
                sceneId,
                lifetimeCancellation.Token).ConfigureAwait(true);
            if (export is null)
            {
                LifecycleStatus = "SCENE NOT FOUND — REFRESH THE LIST";
                LifecycleDescription =
                    "The stored Scene no longer exists. Refresh the repository list.";
                return;
            }

            LifecycleStatus = "EXPORT WRITTEN";
            LifecycleDescription =
                "The export is redacted: it contains no Scene name, Activity ID, Device ID, or placement slot.";
            ExportPath = export.FullPath;
            ExportPreview = export.RedactedContent;
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            LifecycleStatus = "EXPORT CANCELLED";
            LifecycleDescription =
                "The export request was cancelled before a file was reported.";
        }
        catch (Exception)
        {
            LifecycleStatus = "EXPORT FAILED";
            LifecycleDescription =
                "No export file can be reported as written. No exception content is displayed.";
        }
        finally
        {
            IsBusy = false;
            NotifyCommandState();
        }
    }

    private void RenderInspectItems()
    {
        InspectItems.Clear();
        if (selectedScene is null)
        {
            return;
        }

        ScenePlan scene = selectedScene.Entry.Scene;
        for (int index = 0; index < scene.Activities.Length; index++)
        {
            SceneActivityPlan item = scene.Activities[index];
            InspectItems.Add(new DesktopSceneRepositoryPlanItem(
                $"ITEM {index + 1}",
                item.ActivityId.ToString(),
                $"Device {item.Placement.DeviceId}; slot {item.Placement.Slot}",
                item.SourceDisposition switch
                {
                    SceneSourceDisposition.PreserveSource => "SOURCE STAYS OPEN",
                    SceneSourceDisposition.MoveAfterAcknowledgement =>
                        "SOURCE CLOSES ONLY AFTER ACKNOWLEDGEMENT",
                    _ => "SOURCE POLICY UNAVAILABLE",
                },
                item.ConflictPolicy switch
                {
                    SceneConflictPolicy.RequireEmpty => "REQUIRE EMPTY DESTINATION",
                    SceneConflictPolicy.ReplaceWithUndo => "REPLACE WITH UNDO",
                    _ => "CONFLICT POLICY UNAVAILABLE",
                }));
        }
    }

    private void NotifyCommandState()
    {
        refreshCommand.NotifyCanExecuteChanged();
        selectForApplyCommand.NotifyCanExecuteChanged();
        beginDeleteCommand.NotifyCanExecuteChanged();
        cancelDeleteCommand.NotifyCanExecuteChanged();
        confirmDeleteCommand.NotifyCanExecuteChanged();
        exportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanSelectForApply));
        OnPropertyChanged(nameof(CanBeginDelete));
        OnPropertyChanged(nameof(CanConfirmDelete));
        OnPropertyChanged(nameof(CanExport));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
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

internal sealed class UnavailableDesktopSceneRepositoryService :
    IDesktopSceneRepositoryService
{
    private UnavailableDesktopSceneRepositoryService()
    {
    }

    public static UnavailableDesktopSceneRepositoryService Instance { get; } = new();

    public bool IsSceneRepositoryReady => false;

    public ValueTask InitializeAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<ImmutableArray<SceneRepositoryEntry>> ListScenesAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<ImmutableArray<SceneRepositoryEntry>>(
            CreateException());

    public ValueTask<SceneRepositoryEntry> SaveSceneAsync(
        ScenePlan scene,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<SceneRepositoryEntry>(CreateException());

    public ValueTask<bool> DeleteSceneAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(CreateException());

    public ValueTask<DesktopSceneExportResult?> ExportSceneAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DesktopSceneExportResult?>(CreateException());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PlatformNotSupportedException CreateException() =>
        new("The Scene repository is not configured by this desktop service.");
}
