using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Diagnostics;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class PersistentOperationHistoryTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 28, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public async Task AppendDeleteClearAndReopenPreserveDurableOrder()
    {
        var store = new HistoryPayloadStore();
        Guid firstId;
        using (PersistentOperationHistory history =
               await PersistentOperationHistory.OpenAsync(store))
        {
            OperationHistoryEntry first = await history.AppendAsync(
                CreateReceipt(OperationStatus.Committed, FailureCode.None));
            OperationHistoryEntry second = await history.AppendAsync(
                CreateReceipt(
                    OperationStatus.Failed,
                    FailureCode.PeerUnavailable,
                    OccurredAt.AddSeconds(1)));
            firstId = first.EntryId;
            Assert.Equal([1, 2], history.Snapshot().Select(static entry => entry.Sequence));
            Assert.Equal(second.EntryId, history.Snapshot()[1].EntryId);
            Assert.True(await history.DeleteAsync(first.EntryId));
            int savesAfterDelete = store.SaveCount;
            Assert.False(await history.DeleteAsync(first.EntryId));
            Assert.Equal(savesAfterDelete, store.SaveCount);
        }

        using PersistentOperationHistory reopened =
            await PersistentOperationHistory.OpenAsync(store);
        Assert.DoesNotContain(reopened.Snapshot(), entry => entry.EntryId == firstId);
        Assert.True(await reopened.ClearAsync());
        int savesAfterClear = store.SaveCount;
        Assert.False(await reopened.ClearAsync());
        Assert.Equal(savesAfterClear, store.SaveCount);
        Assert.Empty(reopened.Snapshot());
    }

    [Fact]
    public async Task AppendPastBoundEvictsOldestReceiptAtomically()
    {
        var store = new HistoryPayloadStore();
        using PersistentOperationHistory history =
            await PersistentOperationHistory.OpenAsync(store);
        for (int index = 0;
             index <= OperationHistoryStorageLimits.MaximumEntryCount;
             index++)
        {
            await history.AppendAsync(CreateReceipt(
                OperationStatus.Committed,
                FailureCode.None,
                OccurredAt.AddSeconds(index)));
        }

        OperationHistoryEntry[] entries = history.Snapshot().ToArray();
        Assert.Equal(
            OperationHistoryStorageLimits.MaximumEntryCount,
            entries.Length);
        Assert.Equal(2, entries[0].Sequence);
        Assert.Equal(
            OperationHistoryStorageLimits.MaximumEntryCount + 1,
            entries[^1].Sequence);

        using PersistentOperationHistory reopened =
            await PersistentOperationHistory.OpenAsync(store);
        Assert.Equal(
            entries.Select(static entry => entry.Sequence),
            reopened.Snapshot().Select(static entry => entry.Sequence));
    }

    [Theory]
    [InlineData("append", false)]
    [InlineData("append", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("clear", false)]
    [InlineData("clear", true)]
    public async Task EveryMutationSaveBoundaryPoisonsWithoutPublishing(
        string mutation,
        bool afterWrite)
    {
        await VerifySaveBoundaryAsync(mutation, afterWrite);
    }

    [Fact]
    public async Task FailedAppendDoesNotPublishAndPoisonsUntilReopen()
    {
        var store = new HistoryPayloadStore();
        using PersistentOperationHistory history =
            await PersistentOperationHistory.OpenAsync(store);
        store.FailNextSave = true;

        await Assert.ThrowsAsync<OperationHistoryPersistenceException>(async () =>
            await history.AppendAsync(CreateReceipt(
                OperationStatus.Committed,
                FailureCode.None)));
        Assert.Empty(history.Snapshot());
        OperationHistoryPersistenceException poisoned =
            await Assert.ThrowsAsync<OperationHistoryPersistenceException>(async () =>
                await history.AppendAsync(CreateReceipt(
                    OperationStatus.Committed,
                    FailureCode.None)));
        Assert.Contains("reopened", poisoned.Message, StringComparison.Ordinal);

        using PersistentOperationHistory reopened =
            await PersistentOperationHistory.OpenAsync(store);
        Assert.Empty(reopened.Snapshot());
        _ = await reopened.AppendAsync(CreateReceipt(
            OperationStatus.Committed,
            FailureCode.None));
        Assert.Single(reopened.Snapshot());
    }

    [Fact]
    public async Task AmbiguousDeleteDoesNotPublishButReopenUsesDurableTruth()
    {
        var store = new HistoryPayloadStore();
        using PersistentOperationHistory history =
            await PersistentOperationHistory.OpenAsync(store);
        OperationHistoryEntry entry = await history.AppendAsync(CreateReceipt(
            OperationStatus.Failed,
            FailureCode.InternalFailure));
        store.FailAfterNextWrite = true;

        await Assert.ThrowsAsync<OperationHistoryPersistenceException>(async () =>
            await history.DeleteAsync(entry.EntryId));
        Assert.Single(history.Snapshot());

        using PersistentOperationHistory reopened =
            await PersistentOperationHistory.OpenAsync(store);
        Assert.Empty(reopened.Snapshot());
    }

    [Fact]
    public async Task ReopenRejectsUnknownPropertiesAndStaleSequenceFrontier()
    {
        var store = new HistoryPayloadStore();
        using (PersistentOperationHistory history =
               await PersistentOperationHistory.OpenAsync(store))
        {
            _ = await history.AppendAsync(CreateReceipt(
                OperationStatus.Committed,
                FailureCode.None));
        }

        byte[] canonical = Assert.IsType<byte[]>(store.Payload).ToArray();
        JsonObject unknown = Assert.IsType<JsonObject>(
            JsonNode.Parse(canonical));
        unknown["unexpected"] = true;
        store.SetPayload(Encoding.UTF8.GetBytes(unknown.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentOperationHistory.OpenAsync(store));

        JsonObject stale = Assert.IsType<JsonObject>(
            JsonNode.Parse(canonical));
        stale["nextSequence"] = 1;
        store.SetPayload(Encoding.UTF8.GetBytes(stale.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentOperationHistory.OpenAsync(store));
    }

    [Fact]
    public async Task ReopenRejectsNonCanonicalIdsTimesEnumsAndPropertyOrder()
    {
        byte[] canonical = await CreateCanonicalPayloadAsync(1);
        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(canonical));

        JsonObject badId = Assert.IsType<JsonObject>(root.DeepClone());
        Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(badId["entries"])[0])
            ["entryId"] = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(badId.ToJsonString()));

        JsonObject badTime = Assert.IsType<JsonObject>(root.DeepClone());
        Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(badTime["entries"])[0])
            ["recordedAt"] = "2026-07-28T03:02:03.0000000+02:00";
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(badTime.ToJsonString()));

        JsonObject badEnum = Assert.IsType<JsonObject>(root.DeepClone());
        Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(badEnum["entries"])[0])["receipt"])
            ["kind"] = "futureKind";
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(badEnum.ToJsonString()));

        var reordered = new JsonObject
        {
            ["nextSequence"] = root["nextSequence"]!.DeepClone(),
            ["formatVersion"] = root["formatVersion"]!.DeepClone(),
            ["entries"] = root["entries"]!.DeepClone(),
        };
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(reordered.ToJsonString()));
    }

    [Fact]
    public async Task ReopenRejectsOrderingDuplicatesBoundsAndTrailingData()
    {
        byte[] canonical = await CreateCanonicalPayloadAsync(2);
        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(canonical));
        JsonArray entries = Assert.IsType<JsonArray>(root["entries"]);
        JsonObject first = Assert.IsType<JsonObject>(entries[0]);

        JsonObject duplicate = Assert.IsType<JsonObject>(root.DeepClone());
        JsonArray duplicateEntries = Assert.IsType<JsonArray>(
            duplicate["entries"]);
        Assert.IsType<JsonObject>(duplicateEntries[1])["entryId"] =
            first["entryId"]!.GetValue<string>();
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(duplicate.ToJsonString()));

        JsonObject misordered = Assert.IsType<JsonObject>(root.DeepClone());
        Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(misordered["entries"])[1])
            ["sequence"] = 1;
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(misordered.ToJsonString()));

        JsonObject overBound = Assert.IsType<JsonObject>(root.DeepClone());
        JsonArray overBoundEntries = Assert.IsType<JsonArray>(
            overBound["entries"]);
        JsonObject template = Assert.IsType<JsonObject>(overBoundEntries[0]);
        overBoundEntries.Clear();
        for (int index = 1;
             index <= OperationHistoryStorageLimits.MaximumEntryCount + 1;
             index++)
        {
            JsonObject added = Assert.IsType<JsonObject>(template.DeepClone());
            added["entryId"] = $"00000000-0000-0000-0000-{index:X12}";
            added["sequence"] = index;
            overBoundEntries.Add(added);
        }
        overBound["nextSequence"] = 258;
        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes(overBound.ToJsonString()));

        await AssertPayloadRejectedAsync(
            Encoding.UTF8.GetBytes($"{Encoding.UTF8.GetString(canonical)}{{}}"));
    }

    private static async Task<byte[]> CreateCanonicalPayloadAsync(int count)
    {
        var store = new HistoryPayloadStore();
        using PersistentOperationHistory history =
            await PersistentOperationHistory.OpenAsync(store);
        for (int index = 0; index < count; index++)
        {
            _ = await history.AppendAsync(CreateReceipt(
                OperationStatus.Committed,
                FailureCode.None,
                OccurredAt.AddSeconds(index)));
        }

        return Assert.IsType<byte[]>(store.Payload).ToArray();
    }

    private static async Task AssertPayloadRejectedAsync(byte[] payload)
    {
        var store = new HistoryPayloadStore();
        store.SetPayload(payload);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentOperationHistory.OpenAsync(store));
    }

    private static async Task VerifySaveBoundaryAsync(
        string mutation,
        bool afterWrite)
    {
        var store = new HistoryPayloadStore();
        using PersistentOperationHistory history =
            await PersistentOperationHistory.OpenAsync(store);
        OperationHistoryEntry first = await history.AppendAsync(CreateReceipt(
            OperationStatus.Committed,
            FailureCode.None));
        _ = await history.AppendAsync(CreateReceipt(
            OperationStatus.Failed,
            FailureCode.PeerUnavailable,
            OccurredAt.AddSeconds(1)));
        Guid[] published = history.Snapshot()
            .Select(static entry => entry.EntryId)
            .ToArray();
        store.FailAfterNextWrite = afterWrite;
        store.FailNextSave = !afterWrite;

        await Assert.ThrowsAsync<OperationHistoryPersistenceException>(
            mutation switch
            {
                "append" => async () => await history.AppendAsync(CreateReceipt(
                    OperationStatus.Committed,
                    FailureCode.None,
                    OccurredAt.AddSeconds(2))),
                "delete" => async () => await history.DeleteAsync(first.EntryId),
                "clear" => async () => await history.ClearAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
            });
        Assert.Equal(published, history.Snapshot()
            .Select(static entry => entry.EntryId));
        await Assert.ThrowsAsync<OperationHistoryPersistenceException>(async () =>
            await history.ClearAsync());

        using PersistentOperationHistory reopened =
            await PersistentOperationHistory.OpenAsync(store);
        int expectedCount = afterWrite
            ? mutation switch { "append" => 3, "delete" => 1, _ => 0 }
            : 2;
        Assert.Equal(expectedCount, reopened.Snapshot().Length);
        if (afterWrite && mutation == "delete")
        {
            Assert.DoesNotContain(
                reopened.Snapshot(), entry => entry.EntryId == first.EntryId);
        }
    }

    private static OperationReceipt CreateReceipt(
        OperationStatus status,
        FailureCode failureCode,
        DateTimeOffset? occurredAt = null)
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "HISTORY-TITLE-CANARY",
            "{\"text\":\"HISTORY-CONTENT-CANARY\"}",
            ActivitySensitivity.Sensitive);
        OperationId operationId = OperationId.From(Guid.NewGuid());
        CorrelationId correlationId = CorrelationId.From(Guid.NewGuid());
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        DateTimeOffset timestamp = occurredAt ?? OccurredAt;
        return status switch
        {
            OperationStatus.Committed => OperationReceipt.Committed(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                timestamp),
            OperationStatus.Failed => OperationReceipt.Failed(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                timestamp,
                failureCode),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private sealed class HistoryPayloadStore :
        IOperationHistoryStatePayloadStore
    {
        public bool FailAfterNextWrite { get; set; }
        public bool FailNextSave { get; set; }
        public byte[]? Payload { get; private set; }
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
            SaveCount++;
            if (FailNextSave)
            {
                FailNextSave = false;
                return ValueTask.FromException(
                    new IOException("fail-before-write-canary"));
            }

            Payload = payload.ToArray();
            if (FailAfterNextWrite)
            {
                FailAfterNextWrite = false;
                return ValueTask.FromException(
                    new IOException("fail-after-write-canary"));
            }

            return ValueTask.CompletedTask;
        }

        public void SetPayload(byte[] payload) => Payload = payload;
    }
}
