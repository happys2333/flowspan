using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Desktop;
using Flowspan.Domain;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopSceneRepositoryRuntimeTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 26, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingPayloadStoreDegradesToNotReady()
    {
        await using var runtime = new DesktopSceneRepositoryRuntime(
            payloadStore: null);

        await runtime.InitializeAsync();

        Assert.False(runtime.IsSceneRepositoryReady);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            async () => await runtime.ListScenesAsync());
    }

    [Fact]
    public async Task FailingPayloadStoreDegradesInitializeWithoutCrashing()
    {
        var store = new MemorySceneRepositoryStatePayloadStore
        {
            FailLoads = true,
        };
        await using var runtime = new DesktopSceneRepositoryRuntime(store);

        await runtime.InitializeAsync();

        Assert.False(runtime.IsSceneRepositoryReady);
    }

    [Fact]
    public async Task SaveListDeleteRoundTripUsesTheInjectedClock()
    {
        var store = new MemorySceneRepositoryStatePayloadStore();
        await using var runtime = new DesktopSceneRepositoryRuntime(
            store,
            new FixedTimeProvider(FixedNow));
        await runtime.InitializeAsync();
        Assert.True(runtime.IsSceneRepositoryReady);

        ScenePlan scene = CreateScene(
            "11111111-1111-1111-1111-111111111111",
            "Round trip");
        SceneRepositoryEntry saved = await runtime.SaveSceneAsync(scene);
        Assert.Equal(FixedNow, saved.SavedAt);

        ImmutableArray<SceneRepositoryEntry> listed =
            await runtime.ListScenesAsync();
        SceneRepositoryEntry listedEntry = Assert.Single(listed);
        Assert.Equal(scene.Id, listedEntry.Scene.Id);

        Assert.True(await runtime.DeleteSceneAsync(scene.Id));
        Assert.Empty(await runtime.ListScenesAsync());
        Assert.False(await runtime.DeleteSceneAsync(scene.Id));
    }

    [Fact]
    public async Task ExportWritesARedactedFileAndReturnsItsExactPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-scene-export-{Guid.NewGuid():N}");
        try
        {
            var store = new MemorySceneRepositoryStatePayloadStore();
            await using var runtime = new DesktopSceneRepositoryRuntime(
                store,
                new FixedTimeProvider(FixedNow),
                directory);
            await runtime.InitializeAsync();
            ScenePlan scene = CreateScene(
                "11111111-1111-1111-1111-111111111111",
                "EXPORT-NAME-CANARY",
                slot: "EXPORT-SLOT-CANARY");
            await runtime.SaveSceneAsync(scene);

            DesktopSceneExportResult? export =
                await runtime.ExportSceneAsync(scene.Id);

            Assert.NotNull(export);
            Assert.StartsWith(
                Path.GetFullPath(directory),
                export.FullPath,
                StringComparison.Ordinal);
            Assert.True(File.Exists(export.FullPath));
            string written = File.ReadAllText(export.FullPath);
            Assert.Equal(export.RedactedContent, written);
            Assert.Contains(
                SceneRepositoryExport.ExportKind,
                written,
                StringComparison.Ordinal);
            Assert.Contains(
                scene.Id.ToString(),
                written,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "EXPORT-NAME-CANARY",
                written,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "EXPORT-SLOT-CANARY",
                written,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                scene.Activities[0].ActivityId.ToString(),
                written,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                scene.Activities[0].Placement.DeviceId.ToString(),
                written,
                StringComparison.Ordinal);

            DesktopSceneExportResult? second =
                await runtime.ExportSceneAsync(scene.Id);
            Assert.NotNull(second);
            Assert.NotEqual(export.FullPath, second.FullPath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExportOfAnUnknownSceneReturnsNull()
    {
        var store = new MemorySceneRepositoryStatePayloadStore();
        await using var runtime = new DesktopSceneRepositoryRuntime(
            store,
            new FixedTimeProvider(FixedNow),
            Path.Combine(
                Path.GetTempPath(),
                $"flowspan-scene-export-{Guid.NewGuid():N}"));
        await runtime.InitializeAsync();

        Assert.Null(await runtime.ExportSceneAsync(
            SceneId.Parse("11111111-1111-1111-1111-111111111111")));
    }

    [Fact]
    public async Task AmbiguousSaveFailureReopensFromDurableTruthOnNextOperation()
    {
        var store = new MemorySceneRepositoryStatePayloadStore();
        await using var runtime = new DesktopSceneRepositoryRuntime(
            store,
            new FixedTimeProvider(FixedNow));
        await runtime.InitializeAsync();
        ScenePlan first = CreateScene(
            "11111111-1111-1111-1111-111111111111",
            "Survivor");
        await runtime.SaveSceneAsync(first);

        store.FailAfterNextWrite = true;
        ScenePlan second = CreateScene(
            "22222222-2222-2222-2222-222222222222",
            "Ambiguous");
        await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
            async () => await runtime.SaveSceneAsync(second));

        // The ambiguous write persisted durably, so the reopened state on the
        // next operation must reflect the durable truth without retrying the
        // failed mutation.
        ImmutableArray<SceneRepositoryEntry> listed =
            await runtime.ListScenesAsync();
        Assert.Equal(2, listed.Length);
        Assert.Equal(2, store.LoadCount);
    }

    private static ScenePlan CreateScene(
        string sceneId,
        string name,
        string slot = "default")
    {
        return ScenePlan.Create(
            SceneId.Parse(sceneId),
            name,
            [
                SceneActivityPlan.Place(
                    ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    ActivityPlacement.On(
                        DeviceId.Parse("99999999-9999-9999-9999-999999999999"),
                        slot),
                    SceneSourceDisposition.PreserveSource,
                    SceneConflictPolicy.RequireEmpty),
            ]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemorySceneRepositoryStatePayloadStore :
        ISceneRepositoryStatePayloadStore
    {
        public bool FailAfterNextWrite { get; set; }

        public bool FailLoads { get; set; }

        public int LoadCount { get; private set; }

        public byte[]? Payload { get; private set; }

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            if (FailLoads)
            {
                return ValueTask.FromException<byte[]?>(
                    new IOException("Injected Scene repository load failure."));
            }

            return ValueTask.FromResult(Payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Payload = payload.ToArray();
            if (FailAfterNextWrite)
            {
                FailAfterNextWrite = false;
                return ValueTask.FromException(new IOException(
                    "Injected ambiguous post-write Scene repository failure."));
            }

            return ValueTask.CompletedTask;
        }
    }
}
