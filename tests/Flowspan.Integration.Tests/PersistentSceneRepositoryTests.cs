using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class PersistentSceneRepositoryTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OpenWithoutDurablePayloadStartsEmpty()
    {
        var payloadStore = new InMemoryPayloadStore();

        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);

        Assert.Equal(0, repository.SceneCount);
        Assert.Empty(repository.Snapshot());
        Assert.Equal(0, payloadStore.SaveCount);
    }

    [Fact]
    public async Task InsertPublishesEntryOnlyAfterDurableSave()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan scene = CreateScene("33333333-3333-3333-3333-333333333333");
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        Assert.Equal(0, repository.SceneCount);

        SceneRepositoryEntry saved = await repository.SaveAsync(
            scene,
            SavedAt,
            CancellationToken.None);

        Assert.Equal(1, payloadStore.SaveCount);
        SceneRepositoryEntry entry = Assert.Single(repository.Snapshot());
        Assert.Same(saved, entry);
        Assert.Equal(scene.Id, entry.Scene.Id);
        Assert.Equal(SavedAt, entry.SavedAt);
        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(ScenePlanCodec.Encode(scene))),
            entry.SceneDigest);
        Assert.Equal(1, repository.SceneCount);
    }

    [Fact]
    public async Task IdenticalResaveIsIdempotentWithoutAnotherDurableWrite()
    {
        var payloadStore = new InMemoryPayloadStore();
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        SceneRepositoryEntry first = await repository.SaveAsync(
            CreateScene("33333333-3333-3333-3333-333333333333"),
            SavedAt,
            CancellationToken.None);

        SceneRepositoryEntry second = await repository.SaveAsync(
            CreateScene("33333333-3333-3333-3333-333333333333"),
            SavedAt.AddMinutes(5),
            CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(SavedAt, second.SavedAt);
        Assert.Equal(1, payloadStore.SaveCount);
        Assert.Equal(1, repository.SceneCount);
    }

    [Fact]
    public async Task SameRevisionConflictAndLowerRevisionAreRejectedWithoutDurableWrite()
    {
        var payloadStore = new InMemoryPayloadStore();
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        await repository.SaveAsync(
            CreateScene("33333333-3333-3333-3333-333333333333"),
            SavedAt,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.SaveAsync(
                CreateScene(
                    "33333333-3333-3333-3333-333333333333",
                    "Different scene"),
                SavedAt,
                CancellationToken.None));
        Assert.Equal(1, payloadStore.SaveCount);

        await repository.SaveAsync(
            CreateRevisedScene(
                "33333333-3333-3333-3333-333333333333",
                3,
                "Third scene"),
            SavedAt,
            CancellationToken.None);
        Assert.Equal(2, payloadStore.SaveCount);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.SaveAsync(
                CreateRevisedScene(
                    "33333333-3333-3333-3333-333333333333",
                    2,
                    "Stale scene"),
                SavedAt,
                CancellationToken.None));
        Assert.Equal(2, payloadStore.SaveCount);
        Assert.Equal(3, Assert.Single(repository.Snapshot()).Scene.Revision);
    }

    [Fact]
    public async Task StrictlyGreaterRevisionReplacesStoredScene()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan revised = CreateRevisedScene(
            "33333333-3333-3333-3333-333333333333",
            2,
            "Renamed scene",
            "renamed-slot");
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        await repository.SaveAsync(
            CreateScene("33333333-3333-3333-3333-333333333333"),
            SavedAt,
            CancellationToken.None);

        await repository.SaveAsync(
            revised,
            SavedAt.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(2, payloadStore.SaveCount);
        SceneRepositoryEntry entry = Assert.Single(repository.Snapshot());
        Assert.Equal(2, entry.Scene.Revision);
        Assert.Equal("Renamed scene", entry.Scene.Name);
        Assert.Equal(SavedAt.AddMinutes(1), entry.SavedAt);
        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(ScenePlanCodec.Encode(revised))),
            entry.SceneDigest);
    }

    [Fact]
    public async Task NonUtcSaveTimestampIsRejectedBeforeAnyDurableWrite()
    {
        var payloadStore = new InMemoryPayloadStore();
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);

        ArgumentException exception =
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await repository.SaveAsync(
                    CreateScene("33333333-3333-3333-3333-333333333333"),
                    new DateTimeOffset(
                        2026,
                        7,
                        26,
                        10,
                        0,
                        0,
                        TimeSpan.FromHours(8)),
                    CancellationToken.None));

        Assert.Equal("savedAt", exception.ParamName);
        Assert.Equal(0, payloadStore.SaveCount);
        Assert.Empty(repository.Snapshot());
    }

    [Fact]
    public async Task DeleteRemovesScenesPersistsEmptyStateAndReportsAbsentIds()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan alpha = CreateScene(
            "aaaaaaaa-1111-1111-1111-111111111111",
            "Alpha scene",
            "alpha-slot");
        ScenePlan beta = CreateScene(
            "bbbbbbbb-2222-2222-2222-222222222222",
            "Beta scene",
            "beta-slot");
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(alpha, SavedAt, CancellationToken.None);
            await repository.SaveAsync(beta, SavedAt, CancellationToken.None);

            Assert.True(await repository.DeleteAsync(
                alpha.Id,
                CancellationToken.None));
            Assert.Equal(3, payloadStore.SaveCount);
            Assert.Equal(
                beta.Id,
                Assert.Single(repository.Snapshot()).Scene.Id);

            Assert.False(await repository.DeleteAsync(
                SceneId.Parse("99999999-9999-9999-9999-999999999999"),
                CancellationToken.None));
            Assert.Equal(3, payloadStore.SaveCount);

            Assert.True(await repository.DeleteAsync(
                beta.Id,
                CancellationToken.None));
            Assert.Equal(4, payloadStore.SaveCount);
            Assert.Equal(0, repository.SceneCount);
        }

        Assert.Equal(
            "{\"formatVersion\":1,\"scenes\":[]}",
            Encoding.UTF8.GetString(
                Assert.IsType<byte[]>(payloadStore.Payload)));
        using PersistentSceneRepository reopened =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        Assert.Equal(0, reopened.SceneCount);
        Assert.Empty(reopened.Snapshot());
    }

    [Fact]
    public async Task ReopenRestoresCanonicalOrderAndByteIdenticalScenes()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan gamma = CreateRevisedScene(
            "cccccccc-cccc-cccc-cccc-cccccccccccc",
            4,
            "Gamma scene",
            "gamma-slot");
        ScenePlan alpha = CreateRevisedScene(
            "11111111-2222-3333-4444-555555555555",
            7,
            "Alpha scene",
            "alpha-slot");
        ScenePlan beta = CreateRevisedScene(
            "aaaabbbb-cccc-dddd-eeee-ffff00001111",
            2,
            "Beta scene",
            "beta-slot");
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(gamma, SavedAt, CancellationToken.None);
            await repository.SaveAsync(
                alpha,
                SavedAt.AddMinutes(1),
                CancellationToken.None);
            await repository.SaveAsync(
                beta,
                SavedAt.AddMinutes(2),
                CancellationToken.None);
        }

        using PersistentSceneRepository reopened =
            await PersistentSceneRepository.OpenAsync(payloadStore);

        Assert.Equal(3, reopened.SceneCount);
        Assert.Collection(
            reopened.Snapshot(),
            first =>
            {
                Assert.Equal(alpha.Id, first.Scene.Id);
                Assert.Equal(SavedAt.AddMinutes(1), first.SavedAt);
                Assert.Equal(
                    ScenePlanCodec.Encode(alpha),
                    ScenePlanCodec.Encode(first.Scene));
            },
            second =>
            {
                Assert.Equal(beta.Id, second.Scene.Id);
                Assert.Equal(SavedAt.AddMinutes(2), second.SavedAt);
                Assert.Equal(
                    ScenePlanCodec.Encode(beta),
                    ScenePlanCodec.Encode(second.Scene));
            },
            third =>
            {
                Assert.Equal(gamma.Id, third.Scene.Id);
                Assert.Equal(SavedAt, third.SavedAt);
                Assert.Equal(
                    ScenePlanCodec.Encode(gamma),
                    ScenePlanCodec.Encode(third.Scene));
            });
    }

    [Fact]
    public async Task RepositoryStoresSixtyFourScenesAndRejectsTheSixtyFifth()
    {
        var payloadStore = new InMemoryPayloadStore();
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        for (int index = 1;
             index <= PersistentSceneRepository.MaximumSceneCount;
             index++)
        {
            await repository.SaveAsync(
                CreateScene($"00000000-0000-0000-0000-{index:000000000000}"),
                SavedAt,
                CancellationToken.None);
        }

        Assert.Equal(
            PersistentSceneRepository.MaximumSceneCount,
            repository.SceneCount);
        int saveCountAtCapacity = payloadStore.SaveCount;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.SaveAsync(
                CreateScene(
                    $"00000000-0000-0000-0000-{PersistentSceneRepository.MaximumSceneCount + 1:000000000000}"),
                SavedAt,
                CancellationToken.None));

        Assert.Equal(saveCountAtCapacity, payloadStore.SaveCount);
        Assert.Equal(
            PersistentSceneRepository.MaximumSceneCount,
            repository.SceneCount);
    }

    [Fact]
    public async Task FailedSaveBeforeWritePoisonsRepositoryUntilReopen()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan alpha = CreateScene(
            "aaaaaaaa-1111-1111-1111-111111111111",
            "Alpha scene",
            "alpha-slot");
        ScenePlan beta = CreateScene(
            "bbbbbbbb-2222-2222-2222-222222222222",
            "Beta scene",
            "beta-slot");
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(alpha, SavedAt, CancellationToken.None);
            payloadStore.FailNextSave = true;

            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.SaveAsync(
                    beta,
                    SavedAt,
                    CancellationToken.None));

            Assert.Equal(1, payloadStore.SaveCount);
            Assert.Equal(1, repository.SceneCount);
            Assert.Equal(
                alpha.Id,
                Assert.Single(repository.Snapshot()).Scene.Id);

            SceneRepositoryPersistenceException poisonedSave =
                await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                    async () => await repository.SaveAsync(
                        beta,
                        SavedAt,
                        CancellationToken.None));
            Assert.Contains(
                "reopened",
                poisonedSave.Message,
                StringComparison.Ordinal);
            SceneRepositoryPersistenceException poisonedDelete =
                await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                    async () => await repository.DeleteAsync(
                        alpha.Id,
                        CancellationToken.None));
            Assert.Contains(
                "reopened",
                poisonedDelete.Message,
                StringComparison.Ordinal);
            Assert.Equal(1, payloadStore.SaveCount);
            Assert.Equal(
                alpha.Id,
                Assert.Single(repository.Snapshot()).Scene.Id);
        }

        using PersistentSceneRepository reopened =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        Assert.Equal(alpha.Id, Assert.Single(reopened.Snapshot()).Scene.Id);
        await reopened.SaveAsync(beta, SavedAt, CancellationToken.None);
        Assert.Equal(2, reopened.SceneCount);
        Assert.Equal(2, payloadStore.SaveCount);
    }

    [Fact]
    public async Task AmbiguousSaveAfterWritePoisonsWithoutPublishingCandidate()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan alpha = CreateScene(
            "aaaaaaaa-1111-1111-1111-111111111111",
            "Alpha scene",
            "alpha-slot");
        ScenePlan beta = CreateScene(
            "bbbbbbbb-2222-2222-2222-222222222222",
            "Beta scene",
            "beta-slot");
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(alpha, SavedAt, CancellationToken.None);
            payloadStore.FailAfterNextWrite = true;

            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.SaveAsync(
                    beta,
                    SavedAt.AddMinutes(1),
                    CancellationToken.None));

            Assert.Equal(2, payloadStore.SaveCount);
            Assert.Equal(1, repository.SceneCount);
            Assert.Equal(
                alpha.Id,
                Assert.Single(repository.Snapshot()).Scene.Id);
            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.DeleteAsync(
                    alpha.Id,
                    CancellationToken.None));
            Assert.Equal(2, payloadStore.SaveCount);
        }

        using PersistentSceneRepository reopened =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        Assert.Equal(2, reopened.SceneCount);
        SceneRepositoryEntry durableBeta = Assert.Single(
            reopened.Snapshot(),
            entry => entry.Scene.Id == beta.Id);
        Assert.Equal(SavedAt.AddMinutes(1), durableBeta.SavedAt);
        Assert.Equal(
            ScenePlanCodec.Encode(beta),
            ScenePlanCodec.Encode(durableBeta.Scene));
    }

    [Fact]
    public async Task FailedDeleteBeforeWritePoisonsRepositoryUntilReopen()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan alpha = CreateScene(
            "aaaaaaaa-1111-1111-1111-111111111111");
        ScenePlan beta = CreateScene(
            "bbbbbbbb-2222-2222-2222-222222222222");
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(alpha, SavedAt, CancellationToken.None);
            payloadStore.FailNextSave = true;

            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.DeleteAsync(
                    alpha.Id,
                    CancellationToken.None));

            Assert.Equal(alpha.Id, Assert.Single(repository.Snapshot()).Scene.Id);
            SceneRepositoryPersistenceException poisoned =
                await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                    async () => await repository.SaveAsync(
                        beta,
                        SavedAt,
                        CancellationToken.None));
            Assert.Contains("reopened", poisoned.Message, StringComparison.Ordinal);
            Assert.Equal(1, payloadStore.SaveCount);
        }

        using PersistentSceneRepository reopened =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        Assert.Equal(alpha.Id, Assert.Single(reopened.Snapshot()).Scene.Id);
        await reopened.DeleteAsync(alpha.Id, CancellationToken.None);
        Assert.Empty(reopened.Snapshot());
    }

    [Fact]
    public async Task AmbiguousDeleteAfterWritePoisonsWithoutPublishingCandidate()
    {
        var payloadStore = new InMemoryPayloadStore();
        ScenePlan alpha = CreateScene(
            "aaaaaaaa-1111-1111-1111-111111111111");
        ScenePlan beta = CreateScene(
            "bbbbbbbb-2222-2222-2222-222222222222");
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(alpha, SavedAt, CancellationToken.None);
            payloadStore.FailAfterNextWrite = true;

            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.DeleteAsync(
                    alpha.Id,
                    CancellationToken.None));

            Assert.Equal(alpha.Id, Assert.Single(repository.Snapshot()).Scene.Id);
            SceneRepositoryPersistenceException poisoned =
                await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                    async () => await repository.SaveAsync(
                        beta,
                        SavedAt,
                        CancellationToken.None));
            Assert.Contains("reopened", poisoned.Message, StringComparison.Ordinal);
            Assert.Equal(2, payloadStore.SaveCount);
        }

        using PersistentSceneRepository reopened =
            await PersistentSceneRepository.OpenAsync(payloadStore);
        Assert.Empty(reopened.Snapshot());
        await reopened.SaveAsync(beta, SavedAt, CancellationToken.None);
        Assert.Equal(beta.Id, Assert.Single(reopened.Snapshot()).Scene.Id);
    }

    [Fact]
    public async Task StrictReopenRejectsTamperedEnvelopeEntriesAndTrailingData()
    {
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(
                CreateScene("aaaa1111-0000-0000-0000-000000000000"),
                SavedAt,
                CancellationToken.None);
        }

        string original = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        string[] tamperedPayloads =
        [
            ReplaceRequired(
                original,
                "{\"formatVersion\":1,\"scenes\"",
                "{\"formatVersion\":1,\"unexpected\":true,\"scenes\""),
            ReplaceRequired(
                original,
                "{\"formatVersion\":1,\"scenes\"",
                "{\"formatVersion\":1,\"formatVersion\":1,\"scenes\""),
            ReplaceRequired(
                original,
                "{\"formatVersion\":1,\"scenes\"",
                "{\"scenes\""),
            ReplaceRequired(
                original,
                "{\"formatVersion\":1,\"scenes\"",
                "{\"formatVersion\":2,\"scenes\""),
            "{\"formatVersion\":1,\"scenes\":{}}",
            ReplaceRequired(
                original,
                "{\"savedAt\":",
                "{\"unexpectedEntry\":true,\"savedAt\":"),
            ReplaceRequired(
                original,
                "{\"savedAt\":\"2026-07-26T02:00:00.0000000+00:00\",\"scene\":",
                "{\"scene\":"),
            ReplaceRequired(
                original,
                "\"savedAt\":\"2026-07-26T02:00:00.0000000+00:00\"",
                "\"savedAt\":\"2026-07-26T10:00:00.0000000+08:00\""),
            ReplaceRequired(
                original,
                "\"revision\":1,\"name\":\"Alpha scene\"",
                "\"name\":\"Alpha scene\",\"revision\":1"),
            ReplaceRequired(
                original,
                "\"sceneId\":\"aaaa1111-0000-0000-0000-000000000000\"",
                "\"sceneId\":\"AAAA1111-0000-0000-0000-000000000000\""),
            original + "{}",
        ];

        foreach (string tampered in tamperedPayloads)
        {
            payloadStore.ReplacePayload(Encoding.UTF8.GetBytes(tampered));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await PersistentSceneRepository.OpenAsync(payloadStore));
        }
    }

    [Fact]
    public async Task StrictReopenRejectsMisorderedAndDuplicatedSceneEntries()
    {
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneRepository repository =
               await PersistentSceneRepository.OpenAsync(payloadStore))
        {
            await repository.SaveAsync(
                CreateScene(
                    "aaaa2222-0000-0000-0000-000000000000",
                    "Alpha scene",
                    "alpha-slot"),
                SavedAt,
                CancellationToken.None);
            await repository.SaveAsync(
                CreateScene(
                    "bbbb2222-0000-0000-0000-000000000000",
                    "Beta scene",
                    "beta-slot"),
                SavedAt,
                CancellationToken.None);
        }

        byte[] durable = Assert.IsType<byte[]>(payloadStore.Payload);
        JsonObject misordered = Assert.IsType<JsonObject>(
            JsonNode.Parse(durable));
        JsonArray misorderedScenes = Assert.IsType<JsonArray>(
            misordered["scenes"]);
        Assert.Equal(2, misorderedScenes.Count);
        JsonNode first = Assert.IsType<JsonObject>(
            misorderedScenes[0]).DeepClone();
        JsonNode second = Assert.IsType<JsonObject>(
            misorderedScenes[1]).DeepClone();
        misorderedScenes.Clear();
        misorderedScenes.Add(second);
        misorderedScenes.Add(first);
        payloadStore.ReplacePayload(
            Encoding.UTF8.GetBytes(misordered.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSceneRepository.OpenAsync(payloadStore));

        JsonObject duplicated = Assert.IsType<JsonObject>(
            JsonNode.Parse(durable));
        JsonArray duplicatedScenes = Assert.IsType<JsonArray>(
            duplicated["scenes"]);
        JsonNode duplicateEntry = Assert.IsType<JsonObject>(
            duplicatedScenes[0]).DeepClone();
        duplicatedScenes.RemoveAt(1);
        duplicatedScenes.Add(duplicateEntry);
        payloadStore.ReplacePayload(
            Encoding.UTF8.GetBytes(duplicated.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSceneRepository.OpenAsync(payloadStore));
    }

    [Fact]
    public async Task RedactionKeepsSceneContentOutOfFailuresAndEntryText()
    {
        const string nameCanary = "FLOWSPAN-SCENE-REPO-NAME-CANARY";
        const string slotCanary = "flowspan-scene-repo-slot-canary";
        const string activityId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        ScenePlan scene = CreateScene(
            "33333333-3333-3333-3333-333333333333",
            nameCanary,
            slotCanary);
        var failingStore = new InMemoryPayloadStore
        {
            FailNextSave = true,
        };
        var ambiguousStore = new InMemoryPayloadStore
        {
            FailAfterNextWrite = true,
        };
        using PersistentSceneRepository repository =
            await PersistentSceneRepository.OpenAsync(failingStore);
        using PersistentSceneRepository ambiguousRepository =
            await PersistentSceneRepository.OpenAsync(ambiguousStore);

        SceneRepositoryPersistenceException beforeWrite =
            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.SaveAsync(
                    scene,
                    SavedAt,
                    CancellationToken.None));
        SceneRepositoryPersistenceException poisoned =
            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await repository.SaveAsync(
                    scene,
                    SavedAt,
                    CancellationToken.None));
        SceneRepositoryPersistenceException afterWrite =
            await Assert.ThrowsAsync<SceneRepositoryPersistenceException>(
                async () => await ambiguousRepository.SaveAsync(
                    scene,
                    SavedAt,
                    CancellationToken.None));

        string entryText =
            SceneRepositoryEntry.Create(scene, SavedAt).ToString();
        Assert.Contains(scene.Id.ToString(), entryText, StringComparison.Ordinal);
        string[] texts =
        [
            beforeWrite.ToString(),
            poisoned.ToString(),
            afterWrite.ToString(),
            entryText,
        ];
        foreach (string text in texts)
        {
            Assert.DoesNotContain(nameCanary, text, StringComparison.Ordinal);
            Assert.DoesNotContain(slotCanary, text, StringComparison.Ordinal);
            Assert.DoesNotContain(activityId, text, StringComparison.Ordinal);
        }
    }

    private static ScenePlan CreateScene(
        string sceneId,
        string name = "Alpha scene",
        string slot = "main") =>
        ScenePlan.Create(
            SceneId.Parse(sceneId),
            name,
            [CreateActivityPlan(slot)]);

    private static ScenePlan CreateRevisedScene(
        string sceneId,
        long revision,
        string name,
        string slot = "main") =>
        ScenePlan.Restore(
            SceneId.Parse(sceneId),
            revision,
            name,
            groupBinding: null,
            [CreateActivityPlan(slot)]);

    private static SceneActivityPlan CreateActivityPlan(string slot) =>
        SceneActivityPlan.Place(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityPlacement.On(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                slot),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);

    private static string ReplaceRequired(
        string value,
        string oldValue,
        string newValue)
    {
        string replaced = value.Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);
        Assert.NotEqual(value, replaced);
        return replaced;
    }

    private sealed class InMemoryPayloadStore : ISceneRepositoryStatePayloadStore
    {
        public byte[]? Payload { get; private set; }

        public bool FailNextSave { get; set; }

        public bool FailAfterNextWrite { get; set; }

        public int SaveCount { get; private set; }

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException(
                    "scene-repository-pre-write-exception-canary");
            }

            Payload = payload.ToArray();
            SaveCount++;
            if (FailAfterNextWrite)
            {
                FailAfterNextWrite = false;
                throw new IOException(
                    "scene-repository-post-write-exception-canary");
            }

            return ValueTask.CompletedTask;
        }

        public void ReplacePayload(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            Payload = payload.ToArray();
        }
    }
}
