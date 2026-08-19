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
        Summary = DesktopText.Format(
            "SceneRepository_ItemSummaryFormat",
            scene.Activities.Length,
            scene.Revision,
            scene.FormatVersion);
        GroupBinding = scene.GroupBinding is null
            ? DesktopText.Get("SceneRepository_NoGroupBinding")
            : DesktopText.Format(
                "SceneRepository_GroupBindingFormat",
                scene.GroupBinding.GroupId,
                scene.GroupBinding.GroupRevision);
        SceneDigest = entry.SceneDigest;
        SavedAt = entry.SavedAt.ToString("O");
        AutomationName = DesktopText.Format(
            "SceneRepository_ItemAutomationNameFormat",
            scene.Id,
            scene.Revision,
            scene.Activities.Length);
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
    private string exportPath = DesktopText.Get(
        "SceneRepository_ExportPath_None");
    private string exportPreview =
        DesktopText.Get("SceneRepository_ExportPreview_Default");
    private string lifecycleDescription =
        DesktopText.Get("SceneRepository_LifecycleDescription_Default");
    private string lifecycleStatus = DesktopText.Get(
        "SceneRepository_LifecycleStatus_None");
    private string repositoryDescription =
        DesktopText.Get("SceneRepository_Description_Default");
    private string repositoryStatus = DesktopText.Get(
        "SceneRepository_Status_Unavailable");

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
            RepositoryStatus = DesktopText.Get(
                "SceneRepository_Status_Unavailable");
            RepositoryDescription =
                DesktopText.Get("SceneRepository_Description_OpenFailed");
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
            RepositoryStatus = DesktopText.Get(
                "SceneRepository_Status_Unavailable");
            RepositoryDescription =
                DesktopText.Get("SceneRepository_Description_NotConfigured");
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
                0 => DesktopText.Get("SceneRepository_Status_Empty"),
                1 => DesktopText.Get("SceneRepository_Status_OneStored"),
                _ => DesktopText.Format(
                    "SceneRepository_Status_MultipleStoredFormat",
                    entries.Length),
            };
            RepositoryDescription =
                DesktopText.Get("SceneRepository_Description_Default");
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            RepositoryStatus = DesktopText.Get(
                "SceneRepository_Status_RefreshCancelled");
        }
        catch (Exception)
        {
            IsRepositoryAvailable = false;
            RepositoryStatus = DesktopText.Get(
                "SceneRepository_Status_Unavailable");
            RepositoryDescription =
                DesktopText.Get("SceneRepository_Description_ReadFailed");
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
        LifecycleStatus = DesktopText.Get(
            "SceneRepository_LifecycleStatus_SelectedForApply");
        LifecycleDescription =
            DesktopText.Get(
                "SceneRepository_LifecycleDescription_SelectedForApply");
    }

    public void BeginDelete()
    {
        if (selectedScene is null)
        {
            return;
        }

        DeleteConfirmation = DesktopText.Format(
            "SceneRepository_DeleteConfirmationFormat",
            selectedScene.Name,
            selectedScene.SceneId,
            selectedScene.Entry.Scene.Revision);
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
                ? DesktopText.Get("SceneRepository_LifecycleStatus_Deleted")
                : DesktopText.Get("SceneRepository_LifecycleStatus_NotFound");
            LifecycleDescription = deleted
                ? DesktopText.Get(
                    "SceneRepository_LifecycleDescription_Deleted")
                : DesktopText.Get(
                    "SceneRepository_LifecycleDescription_NotFound");
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            LifecycleStatus = DesktopText.Get(
                "SceneRepository_LifecycleStatus_DeleteCancelled");
            LifecycleDescription =
                DesktopText.Get(
                    "SceneRepository_LifecycleDescription_DeleteCancelled");
        }
        catch (Exception)
        {
            LifecycleStatus = DesktopText.Get(
                "SceneRepository_LifecycleStatus_DeleteFailed");
            LifecycleDescription =
                DesktopText.Get(
                    "SceneRepository_LifecycleDescription_DeleteFailed");
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
                LifecycleStatus = DesktopText.Get(
                    "SceneRepository_LifecycleStatus_NotFound");
                LifecycleDescription =
                    DesktopText.Get(
                        "SceneRepository_LifecycleDescription_NotFound");
                return;
            }

            LifecycleStatus = DesktopText.Get(
                "SceneRepository_LifecycleStatus_ExportWritten");
            LifecycleDescription =
                DesktopText.Get(
                    "SceneRepository_LifecycleDescription_ExportWritten");
            ExportPath = export.FullPath;
            ExportPreview = export.RedactedContent;
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            LifecycleStatus = DesktopText.Get(
                "SceneRepository_LifecycleStatus_ExportCancelled");
            LifecycleDescription =
                DesktopText.Get(
                    "SceneRepository_LifecycleDescription_ExportCancelled");
        }
        catch (Exception)
        {
            LifecycleStatus = DesktopText.Get(
                "SceneRepository_LifecycleStatus_ExportFailed");
            LifecycleDescription =
                DesktopText.Get(
                    "SceneRepository_LifecycleDescription_ExportFailed");
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
                DesktopText.Format("Scene_ItemLabelFormat", index + 1),
                item.ActivityId.ToString(),
                DesktopText.Format(
                    "Scene_DestinationDescriptionFormat",
                    item.Placement.DeviceId,
                    item.Placement.Slot),
                item.SourceDisposition switch
                {
                    SceneSourceDisposition.PreserveSource => DesktopText.Get(
                        "Scene_SourceDisposition_PreserveSource"),
                    SceneSourceDisposition.MoveAfterAcknowledgement =>
                        DesktopText.Get(
                            "Scene_SourceDisposition_MoveAfterAcknowledgement"),
                    _ => DesktopText.Get(
                        "Scene_SourceDisposition_Unavailable"),
                },
                item.ConflictPolicy switch
                {
                    SceneConflictPolicy.RequireEmpty => DesktopText.Get(
                        "SceneRepository_ConflictPolicy_RequireEmpty"),
                    SceneConflictPolicy.ReplaceWithUndo => DesktopText.Get(
                        "SceneRepository_ConflictPolicy_ReplaceWithUndo"),
                    _ => DesktopText.Get(
                        "SceneRepository_ConflictPolicy_Unavailable"),
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
