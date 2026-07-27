using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Desktop;
using Flowspan.Domain;

namespace Flowspan.Desktop.Tests;

public sealed class SceneRepositoryViewModelTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnavailableServiceReportsRepositoryUnavailable()
    {
        using var viewModel = CreateViewModel(
            new TestSceneRepositoryService { IsSceneRepositoryReady = false },
            out _);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsRepositoryAvailable);
        Assert.Equal("SCENE REPOSITORY UNAVAILABLE", viewModel.RepositoryStatus);
        Assert.False(viewModel.CanRefresh);
        Assert.False(viewModel.CanSelectForApply);
        Assert.False(viewModel.CanBeginDelete);
        Assert.False(viewModel.CanExport);
    }

    [Fact]
    public async Task InitializeListsStoredScenesWithTruthfulCounts()
    {
        var service = new TestSceneRepositoryService();
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Morning desk"));
        using var viewModel = CreateViewModel(service, out _);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsRepositoryAvailable);
        Assert.Equal("1 SCENE STORED", viewModel.RepositoryStatus);
        SceneRepositoryItemViewModel item = Assert.Single(viewModel.Scenes);
        Assert.Equal("Morning desk", item.Name);
        Assert.Equal(
            "11111111-1111-1111-1111-111111111111",
            item.SceneId);
        Assert.Equal(64, item.SceneDigest.Length);
        Assert.Equal(SavedAt.ToString("O"), item.SavedAt);

        service.Entries.Add(CreateEntry(
            "22222222-2222-2222-2222-222222222222",
            "Evening desk"));
        await viewModel.RefreshAsync();
        Assert.Equal("2 SCENES STORED", viewModel.RepositoryStatus);

        service.Entries.Clear();
        await viewModel.RefreshAsync();
        Assert.Equal("SCENE REPOSITORY EMPTY", viewModel.RepositoryStatus);
    }

    [Fact]
    public async Task SelectionRendersOrderedInspectItemsWithoutMutation()
    {
        var service = new TestSceneRepositoryService();
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Inspect scene",
            activityCount: 3));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();

        viewModel.SelectedScene = viewModel.Scenes[0];

        Assert.Equal(3, viewModel.InspectItems.Count);
        Assert.Equal("ITEM 1", viewModel.InspectItems[0].ItemLabel);
        Assert.Equal("ITEM 3", viewModel.InspectItems[2].ItemLabel);
        Assert.Contains("slot-1", viewModel.InspectItems[0].Destination);
        Assert.Equal(
            "SOURCE STAYS OPEN",
            viewModel.InspectItems[0].SourceDisposition);
        Assert.Equal(
            "REQUIRE EMPTY DESTINATION",
            viewModel.InspectItems[0].ConflictPolicy);

        viewModel.SelectedScene = null;
        Assert.Empty(viewModel.InspectItems);
    }

    [Fact]
    public async Task SelectForApplyHandsTheExactStoredPlanToSelectScene()
    {
        var service = new TestSceneRepositoryService();
        SceneRepositoryEntry entry = CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Apply scene");
        service.Entries.Add(entry);
        using var viewModel = CreateViewModel(
            service,
            out List<(ScenePlan Scene, long? GroupRevision)> selected);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];

        viewModel.SelectForApply();

        (ScenePlan scene, long? groupRevision) = Assert.Single(selected);
        Assert.Same(entry.Scene, scene);
        Assert.Null(groupRevision);
        Assert.Equal("SCENE SELECTED FOR APPLY", viewModel.LifecycleStatus);
    }

    [Fact]
    public async Task DeleteRequiresTwoExplicitStepsAndNamesTheExactScene()
    {
        var service = new TestSceneRepositoryService();
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Doomed scene"));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];

        Assert.False(viewModel.IsDeleteConfirmationVisible);
        viewModel.BeginDelete();

        Assert.True(viewModel.IsDeleteConfirmationVisible);
        Assert.Contains("Doomed scene", viewModel.DeleteConfirmation);
        Assert.Contains(
            "11111111-1111-1111-1111-111111111111",
            viewModel.DeleteConfirmation);
        Assert.Contains("revision 1", viewModel.DeleteConfirmation);
        Assert.EndsWith(
            "This action has no undo.",
            viewModel.DeleteConfirmation);
        Assert.Null(service.LastDeletedSceneId);

        viewModel.CancelDelete();
        Assert.False(viewModel.IsDeleteConfirmationVisible);
        Assert.Null(service.LastDeletedSceneId);
    }

    [Fact]
    public async Task ConfirmedDeleteRemovesTheSceneAndRefreshesTheList()
    {
        var service = new TestSceneRepositoryService();
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Doomed scene"));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];
        viewModel.BeginDelete();

        await viewModel.ConfirmDeleteAsync();

        Assert.Equal(
            SceneId.Parse("11111111-1111-1111-1111-111111111111"),
            service.LastDeletedSceneId);
        Assert.Equal("SCENE DELETED", viewModel.LifecycleStatus);
        Assert.False(viewModel.IsDeleteConfirmationVisible);
        Assert.Empty(viewModel.Scenes);
        Assert.Equal("SCENE REPOSITORY EMPTY", viewModel.RepositoryStatus);
    }

    [Fact]
    public async Task DeleteOfMissingSceneReportsNotFound()
    {
        var service = new TestSceneRepositoryService { DeleteResult = false };
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Ghost scene"));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];
        viewModel.BeginDelete();

        await viewModel.ConfirmDeleteAsync();

        Assert.Equal(
            "SCENE NOT FOUND — REFRESH THE LIST",
            viewModel.LifecycleStatus);
    }

    [Fact]
    public async Task DeleteFailureShowsFixedRedactedStatus()
    {
        const string canary = "DELETE-EXCEPTION-CANARY";
        var service = new TestSceneRepositoryService
        {
            DeleteFailure = new IOException(canary),
        };
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Sticky scene"));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];
        viewModel.BeginDelete();

        await viewModel.ConfirmDeleteAsync();

        Assert.Equal("DELETE FAILED", viewModel.LifecycleStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.LifecycleStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.LifecycleDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportSurfacesTheExactPathAndRedactedContent()
    {
        var service = new TestSceneRepositoryService();
        SceneRepositoryEntry entry = CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Exported scene");
        service.Entries.Add(entry);
        service.ExportResult = new DesktopSceneExportResult(
            "/exports/scene-export-test.json",
            System.Text.Encoding.UTF8.GetString(
                SceneRepositoryExport.EncodeRedacted(entry, SavedAt)));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];

        await viewModel.ExportAsync();

        Assert.Equal(
            SceneId.Parse("11111111-1111-1111-1111-111111111111"),
            service.LastExportedSceneId);
        Assert.Equal("EXPORT WRITTEN", viewModel.LifecycleStatus);
        Assert.Equal(
            "/exports/scene-export-test.json",
            viewModel.ExportPath);
        Assert.Contains(
            SceneRepositoryExport.ExportKind,
            viewModel.ExportPreview,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Exported scene",
            viewModel.ExportPreview,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportOfMissingSceneReportsNotFound()
    {
        var service = new TestSceneRepositoryService { ExportResult = null };
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Ghost scene"));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];

        await viewModel.ExportAsync();

        Assert.Equal(
            "SCENE NOT FOUND — REFRESH THE LIST",
            viewModel.LifecycleStatus);
        Assert.Equal(
            "No Scene export has been written.",
            viewModel.ExportPath);
    }

    [Fact]
    public async Task ExportFailureShowsFixedRedactedStatus()
    {
        const string canary = "EXPORT-EXCEPTION-CANARY";
        var service = new TestSceneRepositoryService
        {
            ExportFailure = new IOException(canary),
        };
        service.Entries.Add(CreateEntry(
            "11111111-1111-1111-1111-111111111111",
            "Sticky scene"));
        using var viewModel = CreateViewModel(service, out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedScene = viewModel.Scenes[0];

        await viewModel.ExportAsync();

        Assert.Equal("EXPORT FAILED", viewModel.LifecycleStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.LifecycleDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListFailureDegradesToUnavailableWithoutExceptionContent()
    {
        const string canary = "LIST-EXCEPTION-CANARY";
        var service = new TestSceneRepositoryService
        {
            ListFailure = new IOException(canary),
        };
        using var viewModel = CreateViewModel(service, out _);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsRepositoryAvailable);
        Assert.Equal("SCENE REPOSITORY UNAVAILABLE", viewModel.RepositoryStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.RepositoryDescription,
            StringComparison.Ordinal);
    }

    private static SceneRepositoryViewModel CreateViewModel(
        TestSceneRepositoryService service,
        out List<(ScenePlan Scene, long? GroupRevision)> selected)
    {
        var selections = new List<(ScenePlan, long?)>();
        selected = selections;
        return new SceneRepositoryViewModel(
            service,
            (scene, groupRevision) => selections.Add((scene, groupRevision)));
    }

    private static SceneRepositoryEntry CreateEntry(
        string sceneId,
        string name,
        int activityCount = 1)
    {
        SceneActivityPlan[] activities = Enumerable
            .Range(1, activityCount)
            .Select(index => SceneActivityPlan.Place(
                ActivityId.From(new Guid(
                    index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1])),
                ActivityPlacement.On(
                    DeviceId.Parse("99999999-9999-9999-9999-999999999999"),
                    $"slot-{index}"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty))
            .ToArray();
        return SceneRepositoryEntry.Create(
            ScenePlan.Create(SceneId.Parse(sceneId), name, activities),
            SavedAt);
    }

    private sealed class TestSceneRepositoryService :
        IDesktopSceneRepositoryService
    {
        public List<SceneRepositoryEntry> Entries { get; } = [];

        public Exception? DeleteFailure { get; set; }

        public bool DeleteResult { get; set; } = true;

        public Exception? ExportFailure { get; set; }

        public DesktopSceneExportResult? ExportResult { get; set; }

        public bool IsSceneRepositoryReady { get; set; } = true;

        public SceneId? LastDeletedSceneId { get; private set; }

        public SceneId? LastExportedSceneId { get; private set; }

        public Exception? ListFailure { get; set; }

        public ValueTask InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<ImmutableArray<SceneRepositoryEntry>> ListScenesAsync(
            CancellationToken cancellationToken = default)
        {
            if (ListFailure is not null)
            {
                return ValueTask.FromException<
                    ImmutableArray<SceneRepositoryEntry>>(ListFailure);
            }

            return ValueTask.FromResult(Entries.ToImmutableArray());
        }

        public ValueTask<SceneRepositoryEntry> SaveSceneAsync(
            ScenePlan scene,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The view model tests do not save Scenes.");

        public ValueTask<bool> DeleteSceneAsync(
            SceneId sceneId,
            CancellationToken cancellationToken = default)
        {
            if (DeleteFailure is not null)
            {
                return ValueTask.FromException<bool>(DeleteFailure);
            }

            LastDeletedSceneId = sceneId;
            if (DeleteResult)
            {
                Entries.RemoveAll(entry => entry.Scene.Id == sceneId);
            }

            return ValueTask.FromResult(DeleteResult);
        }

        public ValueTask<DesktopSceneExportResult?> ExportSceneAsync(
            SceneId sceneId,
            CancellationToken cancellationToken = default)
        {
            if (ExportFailure is not null)
            {
                return ValueTask.FromException<DesktopSceneExportResult?>(
                    ExportFailure);
            }

            LastExportedSceneId = sceneId;
            return ValueTask.FromResult(ExportResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
