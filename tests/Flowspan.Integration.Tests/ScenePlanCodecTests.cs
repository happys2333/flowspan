using System.Security.Cryptography;
using System.Text;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class ScenePlanCodecTests
{
    private const string CanonicalJson =
        "{\"formatVersion\":1,\"sceneId\":\"33333333-3333-3333-3333-333333333333\",\"revision\":2,\"name\":\"Focus layout\",\"group\":{\"groupId\":\"44444444-4444-4444-4444-444444444444\",\"revision\":3},\"activities\":[{\"activityId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"deviceId\":\"22222222-2222-2222-2222-222222222222\",\"slot\":\"main\",\"sourceDisposition\":\"preserve-source\",\"conflictPolicy\":\"require-empty\"},{\"activityId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"slot\":\"side\",\"sourceDisposition\":\"move-after-acknowledgement\",\"conflictPolicy\":\"replace-with-undo\"}]}";

    [Fact]
    public void CanonicalGroupSceneHasFrozenBytesAndHash()
    {
        ScenePlan scene = CreateCanonicalScene();

        byte[] encoded = ScenePlanCodec.Encode(scene);

        Assert.Equal(CanonicalJson, Encoding.UTF8.GetString(encoded));
        Assert.Equal(
            "1BD613EBA1866B9D6AD1533CF052261DFF91A71D525A68339B51B83DCC0AE0D3",
            Convert.ToHexString(SHA256.HashData(encoded)));
    }

    [Fact]
    public void DecodePreservesCanonicalSceneAndReencodesExactly()
    {
        ScenePlan scene = ScenePlanCodec.Decode(
            Encoding.UTF8.GetBytes(CanonicalJson));

        Assert.Equal(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            scene.Id);
        Assert.Equal(2, scene.Revision);
        Assert.Equal("Focus layout", scene.Name);
        Assert.NotNull(scene.GroupBinding);
        Assert.Equal(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            scene.GroupBinding.GroupId);
        Assert.Equal(3, scene.GroupBinding.GroupRevision);
        Assert.Collection(
            scene.Activities,
            first =>
            {
                Assert.Equal(
                    ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    first.ActivityId);
                Assert.Equal("main", first.Placement.Slot);
                Assert.Equal(
                    SceneSourceDisposition.PreserveSource,
                    first.SourceDisposition);
                Assert.Equal(
                    SceneConflictPolicy.RequireEmpty,
                    first.ConflictPolicy);
            },
            second =>
            {
                Assert.Equal(
                    ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    second.ActivityId);
                Assert.Equal("side", second.Placement.Slot);
                Assert.Equal(
                    SceneSourceDisposition.MoveAfterAcknowledgement,
                    second.SourceDisposition);
                Assert.Equal(
                    SceneConflictPolicy.ReplaceWithUndo,
                    second.ConflictPolicy);
            });
        Assert.Equal(CanonicalJson, Encoding.UTF8.GetString(
            ScenePlanCodec.Encode(scene)));
    }

    [Fact]
    public void UnknownSecretAndDuplicatePropertiesAreRejectedAtEveryLevel()
    {
        string[] malformed =
        [
            CanonicalJson.Replace(
                "{\"formatVersion\"",
                "{\"payload\":\"secret\",\"formatVersion\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "{\"groupId\"",
                "{\"trafficKey\":\"secret\",\"groupId\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "{\"activityId\"",
                "{\"sessionId\":\"secret\",\"activityId\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"formatVersion\":1,",
                "\"formatVersion\":1,\"formatVersion\":1,",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"groupId\":\"44444444-4444-4444-4444-444444444444\",",
                "\"groupId\":\"44444444-4444-4444-4444-444444444444\",\"groupId\":\"44444444-4444-4444-4444-444444444444\",",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"activityId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",",
                "\"activityId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"activityId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",",
                StringComparison.Ordinal),
        ];

        foreach (string json in malformed)
        {
            Assert.Throws<InvalidDataException>(() =>
                ScenePlanCodec.Decode(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Fact]
    public void RejectedUnknownFieldDoesNotEchoUntrustedNameOrValue()
    {
        const string canary = "FLOWSPAN_CODEC_SECRET_CANARY";
        string json = CanonicalJson.Replace(
            "{\"formatVersion\"",
            $"{{\"{canary}\":\"{canary}\",\"formatVersion\"",
            StringComparison.Ordinal);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ScenePlanCodec.Decode(Encoding.UTF8.GetBytes(json)));

        Assert.DoesNotContain(
            canary,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedRequiredValuesAreRejected()
    {
        string[] malformed =
        [
            CanonicalJson.Replace(
                "\"formatVersion\":1",
                "\"formatVersion\":2",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"formatVersion\":1",
                "\"formatVersion\":\"1\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"formatVersion\":1",
                "\"formatVersion\":1.0",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "33333333-3333-3333-3333-333333333333",
                "ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"revision\":2",
                "\"revision\":0",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"revision\":2",
                "\"revision\":2e0",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"name\":\"Focus layout\"",
                "\"name\":123",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"name\":\"Focus layout\"",
                "\"name\":\"Invalid \\uD800\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"name\":\"Focus layout\"",
                "\"name\":\"\\tFocus layout\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"revision\":3",
                "\"revision\":0",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"revision\":3",
                "\"revision\":3.0",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"activities\":[",
                "\"activities\":{\"items\":[",
                StringComparison.Ordinal) + "}",
            CanonicalJson.Replace(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "not-an-activity-id",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "22222222-2222-2222-2222-222222222222",
                "ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"slot\":\"main\"",
                "\"slot\":\" \"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"slot\":\"main\"",
                "\"slot\":\"invalid-\\uD800-slot\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"slot\":\"main\"",
                "\"slot\":\"\\tmain\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"sourceDisposition\":\"preserve-source\"",
                "\"sourceDisposition\":\"mirror\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "\"conflictPolicy\":\"require-empty\"",
                "\"conflictPolicy\":\"overwrite\"",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                StringComparison.Ordinal),
            CanonicalJson.Replace(
                ",\"name\":\"Focus layout\"",
                string.Empty,
                StringComparison.Ordinal),
        ];

        foreach (string json in malformed)
        {
            Assert.Throws<InvalidDataException>(() =>
                ScenePlanCodec.Decode(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Fact]
    public void UngroupedSceneWritesNullGroupAndRoundTrips()
    {
        ScenePlan original = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Individual",
            [SceneActivityPlan.Place(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityPlacement.On(
                    DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                    "main"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty)]);

        byte[] encoded = ScenePlanCodec.Encode(original);
        ScenePlan decoded = ScenePlanCodec.Decode(encoded);

        Assert.Contains(
            "\"group\":null",
            Encoding.UTF8.GetString(encoded),
            StringComparison.Ordinal);
        Assert.Null(decoded.GroupBinding);
        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Revision, decoded.Revision);
        Assert.Equal(original.Name, decoded.Name);
        Assert.Equal(encoded, ScenePlanCodec.Encode(decoded));
    }

    [Fact]
    public void EncodedSizeDepthAndFramingBoundsAreEnforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScenePlanCodec.Decode([]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScenePlanCodec.Decode(
                new byte[ScenePlanCodec.MaximumEncodedBytes + 1]));

        string[] malformed =
        [
            new string('[', 9) + "0" + new string(']', 9),
            CanonicalJson + "{}",
            CanonicalJson.Replace(
                "{\"formatVersion\"",
                "{/*comment*/\"formatVersion\"",
                StringComparison.Ordinal),
            CanonicalJson[..^1] + ",}",
        ];
        foreach (string json in malformed)
        {
            Assert.Throws<InvalidDataException>(() =>
                ScenePlanCodec.Decode(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Fact]
    public void ActivityAndStringBoundsAreEnforcedThroughTheCodec()
    {
        string maximum = CreateUngroupedJson(ScenePlan.MaximumActivities);
        ScenePlan decoded = ScenePlanCodec.Decode(
            Encoding.UTF8.GetBytes(maximum));

        Assert.Equal(ScenePlan.MaximumActivities, decoded.Activities.Length);
        Assert.True(ScenePlanCodec.Encode(decoded).Length
            <= ScenePlanCodec.MaximumEncodedBytes);

        string overActivityBound = CreateUngroupedJson(
            ScenePlan.MaximumActivities + 1);
        Assert.True(Encoding.UTF8.GetByteCount(overActivityBound)
            < ScenePlanCodec.MaximumEncodedBytes);
        Assert.Throws<InvalidDataException>(() => ScenePlanCodec.Decode(
            Encoding.UTF8.GetBytes(overActivityBound)));
        Assert.Throws<InvalidDataException>(() => ScenePlanCodec.Decode(
            Encoding.UTF8.GetBytes(CreateUngroupedJson(activityCount: 0))));
        Assert.Throws<InvalidDataException>(() => ScenePlanCodec.Decode(
            Encoding.UTF8.GetBytes(CreateUngroupedJson(activityCount: 1)
                .Replace(
                    "\"name\":\"Bounded\"",
                    $"\"name\":\"{new string('n', ScenePlan.MaximumNameCharacters + 1)}\"",
                    StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => ScenePlanCodec.Decode(
            Encoding.UTF8.GetBytes(CreateUngroupedJson(activityCount: 1)
                .Replace(
                    "\"slot\":\"slot-1\"",
                    $"\"slot\":\"{new string('s', 81)}\"",
                    StringComparison.Ordinal))));
    }

    [Fact]
    public void MaximumUnicodeSceneRemainsWithinItsOwnDecodeBound()
    {
        string name = new string('界', ScenePlan.MaximumNameCharacters - 2)
            + "\U0001F680";
        string slot = new string('界', 78) + "\U0001F680";
        SceneActivityPlan[] activities = Enumerable.Range(
                1,
                ScenePlan.MaximumActivities)
            .Select(index => SceneActivityPlan.Place(
                ActivityId.From(Guid.Parse(
                    $"00000000-0000-0000-0000-{index:000000000000}")),
                ActivityPlacement.On(
                    DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
                    slot),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty))
            .ToArray();
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            name,
            activities);

        byte[] encoded = ScenePlanCodec.Encode(scene);

        Assert.True(encoded.Length <= ScenePlanCodec.MaximumEncodedBytes);
        ScenePlan decoded = ScenePlanCodec.Decode(encoded);
        Assert.Equal(name, decoded.Name);
        Assert.Equal(ScenePlan.MaximumActivities, decoded.Activities.Length);
        Assert.All(decoded.Activities, activity =>
            Assert.Equal(slot, activity.Placement.Slot));
    }

    private static ScenePlan CreateCanonicalScene()
    {
        ActivityId first =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityId second =
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        DeviceId laptop =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId desktop =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            "Focus group",
            [first, second]);
        group = group.Revise(group.Name, group.Activities);
        group = group.Revise(group.Name, group.Activities);
        SceneActivityPlan firstPlan = SceneActivityPlan.Place(
            first,
            ActivityPlacement.On(desktop, "main"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        SceneActivityPlan secondPlan = SceneActivityPlan.Place(
            second,
            ActivityPlacement.On(laptop, "side"),
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.ReplaceWithUndo);
        ScenePlan scene = ScenePlan.CreateFromGroup(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            group,
            [firstPlan, secondPlan]);
        return scene.ReviseFromGroup(
            scene.Name,
            group,
            scene.Activities);
    }

    private static string CreateUngroupedJson(int activityCount)
    {
        string activities = string.Join(
            ',',
            Enumerable.Range(1, activityCount).Select(index =>
                $"{{\"activityId\":\"00000000-0000-0000-0000-{index:000000000000}\",\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"slot\":\"slot-{index}\",\"sourceDisposition\":\"preserve-source\",\"conflictPolicy\":\"require-empty\"}}"));
        return $"{{\"formatVersion\":1,\"sceneId\":\"33333333-3333-3333-3333-333333333333\",\"revision\":1,\"name\":\"Bounded\",\"group\":null,\"activities\":[{activities}]}}";
    }
}
