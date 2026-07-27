using System.Text;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneRepositoryExportTests
{
    private const string NameCanary = "FLOWSPAN-EXPORT-NAME-CANARY";

    private const string GroupedExportJson =
        "{\"formatVersion\":1,\"exportKind\":\"flowspan.scene-export.redacted/v1\",\"exportedAt\":\"2026-07-26T02:00:00.0000000+00:00\",\"sceneId\":\"33333333-3333-3333-3333-333333333333\",\"sceneRevision\":5,\"sceneFormatVersion\":1,\"sceneDigest\":\"A25112BCA107C28E3178822453BF6B3EA8A8EF95AA551D4BEE7BA68B643A2024\",\"savedAt\":\"2026-07-26T01:30:00.0000000+00:00\",\"group\":{\"groupId\":\"44444444-4444-4444-4444-444444444444\",\"revision\":3},\"activityCount\":2,\"activities\":[{\"index\":0,\"sourceDisposition\":\"preserve-source\",\"conflictPolicy\":\"require-empty\"},{\"index\":1,\"sourceDisposition\":\"move-after-acknowledgement\",\"conflictPolicy\":\"replace-with-undo\"}]}";

    private const string UngroupedExportJson =
        "{\"formatVersion\":1,\"exportKind\":\"flowspan.scene-export.redacted/v1\",\"exportedAt\":\"2026-07-26T02:00:00.0000000+00:00\",\"sceneId\":\"55555555-5555-5555-5555-555555555555\",\"sceneRevision\":2,\"sceneFormatVersion\":1,\"sceneDigest\":\"25F5A623F918620DDE71CAB2F9964D447BA7AE7347237AAD0CBA82CA497D13E8\",\"savedAt\":\"2026-07-26T01:30:00.0000000+00:00\",\"group\":null,\"activityCount\":1,\"activities\":[{\"index\":0,\"sourceDisposition\":\"preserve-source\",\"conflictPolicy\":\"require-empty\"}]}";

    private static readonly DateTimeOffset EntrySavedAt =
        new(2026, 7, 26, 1, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExportedAt =
        new(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GroupBoundEntryExportsFrozenRedactedDocument()
    {
        SceneRepositoryEntry entry = CreateGroupedEntry();

        byte[] exported = SceneRepositoryExport.EncodeRedacted(
            entry,
            ExportedAt);

        Assert.Equal(GroupedExportJson, Encoding.UTF8.GetString(exported));
    }

    [Fact]
    public void UngroupedEntryExportsFrozenDocumentWithNullGroup()
    {
        SceneRepositoryEntry entry = CreateUngroupedEntry();

        byte[] exported = SceneRepositoryExport.EncodeRedacted(
            entry,
            ExportedAt);

        Assert.Equal(UngroupedExportJson, Encoding.UTF8.GetString(exported));
    }

    [Fact]
    public void ExportOmitsNamesSlotsActivityAndDeviceIdentifiers()
    {
        SceneRepositoryEntry entry = CreateGroupedEntry();

        string exported = Encoding.UTF8.GetString(
            SceneRepositoryExport.EncodeRedacted(entry, ExportedAt));

        Assert.DoesNotContain(NameCanary, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export-slot-canary",
            exported,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            exported,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            exported,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "22222222-2222-2222-2222-222222222222",
            exported,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "11111111-1111-1111-1111-111111111111",
            exported,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NonUtcExportTimestampIsRejected()
    {
        SceneRepositoryEntry entry = CreateGroupedEntry();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SceneRepositoryExport.EncodeRedacted(
                entry,
                new DateTimeOffset(
                    2026,
                    7,
                    26,
                    10,
                    0,
                    0,
                    TimeSpan.FromHours(8))));

        Assert.Equal("exportedAt", exception.ParamName);
    }

    [Fact]
    public void ActivityIndexesCountUpwardFromZeroInPlanOrder()
    {
        SceneActivityPlan[] plans = Enumerable.Range(1, 5)
            .Select(index => SceneActivityPlan.Place(
                ActivityId.Parse(
                    $"00000000-0000-0000-0000-{index:000000000000}"),
                ActivityPlacement.On(
                    DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
                    $"slot-{index}"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty))
            .ToArray();
        SceneRepositoryEntry entry = SceneRepositoryEntry.Create(
            ScenePlan.Create(
                SceneId.Parse("66666666-6666-6666-6666-666666666666"),
                "Indexed scene",
                plans),
            EntrySavedAt);

        byte[] exported = SceneRepositoryExport.EncodeRedacted(
            entry,
            ExportedAt);

        using JsonDocument document = JsonDocument.Parse(exported);
        JsonElement root = document.RootElement;
        Assert.Equal(5, root.GetProperty("activityCount").GetInt32());
        JsonElement activities = root.GetProperty("activities");
        Assert.Equal(5, activities.GetArrayLength());
        int expectedIndex = 0;
        foreach (JsonElement activity in activities.EnumerateArray())
        {
            Assert.Equal(
                expectedIndex,
                activity.GetProperty("index").GetInt32());
            expectedIndex++;
        }

        Assert.Equal(5, expectedIndex);
    }

    private static SceneRepositoryEntry CreateGroupedEntry() =>
        SceneRepositoryEntry.Create(
            ScenePlan.Restore(
                SceneId.Parse("33333333-3333-3333-3333-333333333333"),
                5,
                NameCanary,
                SceneGroupBinding.Create(
                    GroupId.Parse("44444444-4444-4444-4444-444444444444"),
                    3),
                [
                    SceneActivityPlan.Place(
                        ActivityId.Parse(
                            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        ActivityPlacement.On(
                            DeviceId.Parse(
                                "22222222-2222-2222-2222-222222222222"),
                            "export-slot-canary"),
                        SceneSourceDisposition.PreserveSource,
                        SceneConflictPolicy.RequireEmpty),
                    SceneActivityPlan.Place(
                        ActivityId.Parse(
                            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        ActivityPlacement.On(
                            DeviceId.Parse(
                                "11111111-1111-1111-1111-111111111111"),
                            "export-slot-canary-two"),
                        SceneSourceDisposition.MoveAfterAcknowledgement,
                        SceneConflictPolicy.ReplaceWithUndo),
                ]),
            EntrySavedAt);

    private static SceneRepositoryEntry CreateUngroupedEntry() =>
        SceneRepositoryEntry.Create(
            ScenePlan.Restore(
                SceneId.Parse("55555555-5555-5555-5555-555555555555"),
                2,
                NameCanary,
                groupBinding: null,
                [
                    SceneActivityPlan.Place(
                        ActivityId.Parse(
                            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        ActivityPlacement.On(
                            DeviceId.Parse(
                                "22222222-2222-2222-2222-222222222222"),
                            "export-slot-canary"),
                        SceneSourceDisposition.PreserveSource,
                        SceneConflictPolicy.RequireEmpty),
                ]),
            EntrySavedAt);
}
