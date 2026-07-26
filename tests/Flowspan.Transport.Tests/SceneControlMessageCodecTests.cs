using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class SceneControlMessageCodecTests
{
    private static readonly ProtocolVersion Version =
        ProtocolFeatures.SceneApplyMinimumVersion;

    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId CoordinatorId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId SourceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DeviceId DestinationId =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly OperationContext ChildContext = OperationContext.Create(
        OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Now.AddSeconds(30));

    private static readonly OperationId ParentOperationId =
        OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static readonly CorrelationId ParentCorrelationId =
        CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void AllSixProtocolOnePointFourFramesAndHashesMatchFrozenFixture()
    {
        SceneSourceSelection source = CreateSourceSelection();
        SceneSourceLookupQuery sourceQuery = SceneSourceLookupQuery.Create(
            ChildContext,
            SourceId,
            ActivityId,
            index: 3);
        SceneSourceLookup sourceResult = SceneSourceLookup.FromObservation(
            index: 3,
            ActivityId,
            [source],
            isComplete: true);
        SceneExactSlotQuery slotQuery = CreateExactSlotQuery(source);
        SceneExactSlotInspection slotResult = SceneExactSlotInspection.Observed(
            SceneSlotOccupancy.Empty);
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        SceneActivityOperationResult childResult =
            CreateCommittedResult(instruction);
        (string Name, ControlMessage Message)[] messages =
        [
            ("source-lookup", Freeze(
                SceneControlMessageCodec.CreateSourceLookupQuery(
                    Version,
                    CoordinatorId,
                    sourceQuery,
                    Now),
                "01010101-0101-0101-0101-010101010101")),
            ("source-lookup-result", Freeze(
                SceneControlMessageCodec.CreateSourceLookupResult(
                    Version,
                    SourceId,
                    CoordinatorId,
                    sourceQuery,
                    sourceResult,
                    Now.AddSeconds(1)),
                "02020202-0202-0202-0202-020202020202")),
            ("slot-inspection", Freeze(
                SceneControlMessageCodec.CreateExactSlotQuery(
                    Version,
                    CoordinatorId,
                    slotQuery,
                    Now),
                "03030303-0303-0303-0303-030303030303")),
            ("slot-inspection-result", Freeze(
                SceneControlMessageCodec.CreateExactSlotResult(
                    Version,
                    DestinationId,
                    CoordinatorId,
                    slotQuery,
                    slotResult,
                    Now.AddSeconds(1)),
                "04040404-0404-0404-0404-040404040404")),
            ("child-operation", Freeze(
                SceneControlMessageCodec.CreateChildInstruction(
                    Version,
                    CoordinatorId,
                    instruction,
                    Now),
                "05050505-0505-0505-0505-050505050505")),
            ("child-operation-result", Freeze(
                SceneControlMessageCodec.CreateChildResult(
                    Version,
                    SourceId,
                    CoordinatorId,
                    instruction,
                    childResult,
                    Now.AddSeconds(2)),
                "06060606-0606-0606-0606-060606060606")),
        ];
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "scene-control-v1.4.json");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(Version.ToString(), root.GetProperty("protocol").GetString());
        JsonElement[] fixtures = root.GetProperty("fixtures")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(messages.Length, fixtures.Length);
        for (int index = 0; index < messages.Length; index++)
        {
            (string name, ControlMessage message) = messages[index];
            JsonElement fixture = fixtures[index];
            byte[] actualFrame = ControlMessageCodec.Encode(message);
            string expectedFrame = fixture.GetProperty("frame").GetString()
                ?? throw new InvalidDataException(
                    "A Scene fixture frame is null.");
            string expectedHash = fixture.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException(
                    "A Scene fixture hash is null.");

            Assert.Equal(name, fixture.GetProperty("name").GetString());
            Assert.Equal(expectedFrame, Encoding.UTF8.GetString(actualFrame));
            Assert.Equal(
                expectedHash,
                Convert.ToHexString(SHA256.HashData(actualFrame)));
            ControlMessage decoded = ControlMessageCodec.Decode(actualFrame);
            Assert.Equal(message.Type, decoded.Type);
            Assert.Equal(actualFrame, ControlMessageCodec.Encode(decoded));
        }
    }

    [Fact]
    public void SourceLookupQueryRoundTripsEveryBinding()
    {
        SceneSourceLookupQuery query = SceneSourceLookupQuery.Create(
            ChildContext,
            SourceId,
            ActivityId,
            index: 3);

        ControlMessage message = SceneControlMessageCodec.CreateSourceLookupQuery(
            Version,
            CoordinatorId,
            query,
            Now);
        SceneSourceLookupQuery decoded =
            SceneControlMessageCodec.DecodeSourceLookupQuery(message, SourceId);

        Assert.Equal(query, decoded);
        Assert.Equal(ControlMessageType.SceneSourceLookup, message.Type);
        Assert.Equal(CoordinatorId, message.SenderDeviceId);
        Assert.Equal(ChildContext.CorrelationId, message.CorrelationId);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceLookupResultRoundTripsExactCandidatesAndParticipants()
    {
        SceneSourceLookupQuery query = SceneSourceLookupQuery.Create(
            ChildContext,
            SourceId,
            ActivityId,
            index: 3);
        SceneSourceSelection candidate = SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        SceneSourceLookup expected = SceneSourceLookup.FromObservation(
            index: 3,
            ActivityId,
            [candidate],
            isComplete: true);

        ControlMessage message = SceneControlMessageCodec.CreateSourceLookupResult(
            Version,
            SourceId,
            CoordinatorId,
            query,
            expected,
            Now.AddSeconds(1));
        SceneSourceLookup decoded =
            SceneControlMessageCodec.DecodeSourceLookupResult(
                message,
                CoordinatorId,
                query);

        Assert.Equal(expected, decoded);
        Assert.Equal(ControlMessageType.SceneSourceLookupResult, message.Type);
        Assert.Equal(SourceId, message.SenderDeviceId);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactSlotQueryRoundTripsSourceDestinationAndPolicyBindings()
    {
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        SceneActivityPlan item = SceneActivityPlan.Place(
            ActivityId,
            ActivityPlacement.On(DestinationId, "focus"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        SceneExactSlotQuery query = SceneExactSlotQuery.Create(
            ChildContext,
            item,
            source);

        ControlMessage message = SceneControlMessageCodec.CreateExactSlotQuery(
            Version,
            CoordinatorId,
            query,
            Now);
        SceneExactSlotQuery decoded = SceneControlMessageCodec.DecodeExactSlotQuery(
            message,
            DestinationId);

        Assert.Equal(query, decoded);
        Assert.Equal(ControlMessageType.SceneSlotInspection, message.Type);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactSlotResultRoundTripsEligibleConflictWithoutPayload()
    {
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        SceneExactSlotQuery query = SceneExactSlotQuery.Create(
            ChildContext,
            SceneActivityPlan.Place(
                ActivityId,
                ActivityPlacement.On(DestinationId, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.ReplaceWithUndo),
            source);
        SceneReplaceTargetSnapshot target = SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            revision: 11,
            descriptorDigest: new string('B', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(DestinationId, "focus"));
        SceneExactSlotInspection expected = SceneExactSlotInspection.Observed(
            SceneSlotOccupancy.EligibleConflict(
                target,
                hasDurableUndoAvailability: true));

        ControlMessage message = SceneControlMessageCodec.CreateExactSlotResult(
            Version,
            DestinationId,
            CoordinatorId,
            query,
            expected,
            Now.AddSeconds(1));
        SceneExactSlotInspection decoded =
            SceneControlMessageCodec.DecodeExactSlotResult(
                message,
                CoordinatorId,
                query);

        Assert.Equal(expected, decoded);
        Assert.Equal(ControlMessageType.SceneSlotInspectionResult, message.Type);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactSlotResultRoundTripsEmptyWithoutTargetEvidence()
    {
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        SceneExactSlotQuery query = SceneExactSlotQuery.Create(
            ChildContext,
            SceneActivityPlan.Place(
                ActivityId,
                ActivityPlacement.On(DestinationId, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty),
            source);
        SceneExactSlotInspection expected = SceneExactSlotInspection.Observed(
            SceneSlotOccupancy.Empty);

        ControlMessage message = SceneControlMessageCodec.CreateExactSlotResult(
            Version,
            DestinationId,
            CoordinatorId,
            query,
            expected,
            Now.AddSeconds(1));
        SceneExactSlotInspection decoded =
            SceneControlMessageCodec.DecodeExactSlotResult(
                message,
                CoordinatorId,
                query);

        Assert.Equal(expected, decoded);
        Assert.Equal(JsonValueKind.Null, message.Body
            .GetProperty("occupancy")
            .GetProperty("target")
            .ValueKind);
    }

    [Fact]
    public void RemoteChildInstructionRoundTripsEveryPayloadFreeBinding()
    {
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        SceneActivityPlan plan = SceneActivityPlan.Place(
            ActivityId,
            ActivityPlacement.On(DestinationId, "focus"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        SceneApplyItemPreview item = SceneApplyItemPreview.TransferToEmpty(
            plan,
            source,
            ChildContext.OperationId,
            ChildContext.CorrelationId);
        SceneRemoteChildInstruction instruction =
            SceneRemoteChildInstruction.Create(
                CoordinatorId,
                SceneId.Parse("abababab-abab-abab-abab-abababababab"),
                sceneRevision: 5,
                sceneDigest: new string('C', 64),
                previewFingerprint: new string('D', 64),
                ParentOperationId,
                ParentCorrelationId,
                acceptedAt: Now,
                item);

        ControlMessage message = SceneControlMessageCodec.CreateChildInstruction(
            Version,
            CoordinatorId,
            instruction,
            Now);
        SceneRemoteChildInstruction decoded =
            SceneControlMessageCodec.DecodeChildInstruction(message, SourceId);

        Assert.Equal(instruction, decoded);
        Assert.Equal(ControlMessageType.SceneChildOperation, message.Type);
        Assert.Equal(ChildContext.CorrelationId, message.CorrelationId);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteChildResultRoundTripsExactTerminalReceipt()
    {
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        SceneActivityOperationResult expected = SceneActivityOperationResult.Create(
            OperationReceipt.FromRecordedResult(
                instruction.Item.ChildOperationId,
                instruction.Item.ChildCorrelationId,
                OperationKind.Handoff,
                OperationStatus.Committed,
                SourceId,
                DestinationId,
                ActivityId,
                instruction.Item.Source!.Kind,
                instruction.Item.Source.DescriptorDigest,
                Now.AddSeconds(2),
                FailureCode.None),
            undoCapsule: null);

        ControlMessage message = SceneControlMessageCodec.CreateChildResult(
            Version,
            SourceId,
            CoordinatorId,
            instruction,
            expected,
            Now.AddSeconds(2));
        SceneActivityOperationResult decoded =
            SceneControlMessageCodec.DecodeChildResult(
                message,
                CoordinatorId,
                instruction);

        Assert.Equal(expected, decoded);
        Assert.Equal(ControlMessageType.SceneChildOperationResult, message.Type);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteChildInstructionRejectsWrongAuthenticatedSource()
    {
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        ControlMessage message = SceneControlMessageCodec.CreateChildInstruction(
            Version,
            CoordinatorId,
            instruction,
            Now);

        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildInstruction(
                message,
                DestinationId));

        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["sourceDeviceId"] = DestinationId.ToString();
        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildInstruction(
                WithBody(message, body),
                SourceId));
    }

    [Fact]
    public void RemoteChildResultRejectsChangedFrozenBinding()
    {
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        SceneActivityOperationResult result = CreateCommittedResult(instruction);
        ControlMessage message = SceneControlMessageCodec.CreateChildResult(
            Version,
            SourceId,
            CoordinatorId,
            instruction,
            result,
            Now.AddSeconds(2));
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["previewFingerprint"] = new string('E', 64);

        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildResult(
                WithBody(message, body),
                CoordinatorId,
                instruction));
        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildResult(
                message,
                DestinationId,
                instruction));
    }

    [Theory]
    [InlineData("payloadJson")]
    [InlineData("title")]
    [InlineData("unknownField")]
    public void RemoteChildInstructionRejectsPayloadLikeOrUnknownFields(
        string field)
    {
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        ControlMessage message = SceneControlMessageCodec.CreateChildInstruction(
            Version,
            CoordinatorId,
            instruction,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body[field] = "secret-canary";

        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildInstruction(
                WithBody(message, body),
                SourceId));
    }

    [Fact]
    public void RemoteChildInstructionRejectsNestedPayloadField()
    {
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        ControlMessage message = SceneControlMessageCodec.CreateChildInstruction(
            Version,
            CoordinatorId,
            instruction,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["source"]!.AsObject()["payload"] = "secret-canary";

        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildInstruction(
                WithBody(message, body),
                SourceId));
    }

    [Fact]
    public void SceneRequestsRejectExpiredDeadlines()
    {
        SceneSourceLookupQuery lookup = SceneSourceLookupQuery.Create(
            ChildContext,
            SourceId,
            ActivityId,
            index: 3);
        ControlMessage lookupMessage =
            SceneControlMessageCodec.CreateSourceLookupQuery(
                Version,
                CoordinatorId,
                lookup,
                Now);
        JsonObject lookupBody = JsonNode.Parse(
            lookupMessage.Body.GetRawText())!.AsObject();
        lookupBody["deadline"] = Now;
        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeSourceLookupQuery(
                WithBody(lookupMessage, lookupBody),
                SourceId));

        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        ControlMessage childMessage =
            SceneControlMessageCodec.CreateChildInstruction(
                Version,
                CoordinatorId,
                instruction,
                Now);
        JsonObject childBody = JsonNode.Parse(
            childMessage.Body.GetRawText())!.AsObject();
        childBody["deadline"] = Now;
        Assert.Throws<InvalidDataException>(() =>
            SceneControlMessageCodec.DecodeChildInstruction(
                WithBody(childMessage, childBody),
                SourceId));
    }

    [Fact]
    public void SceneEnvelopeRejectsDuplicateBodyFields()
    {
        SceneRemoteChildInstruction instruction = CreateHandoffInstruction();
        ControlMessage message = SceneControlMessageCodec.CreateChildInstruction(
            Version,
            CoordinatorId,
            instruction,
            Now);
        string duplicate = message.Body.GetRawText().Replace(
            "\"action\":\"handoff\"",
            "\"action\":\"handoff\",\"action\":\"handoff\"",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => ControlMessage.Create(
            message.Version,
            message.Type,
            message.MessageId,
            message.CorrelationId,
            message.SenderDeviceId,
            message.SentAt,
            TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
            duplicate));
    }

    private static SceneRemoteChildInstruction CreateHandoffInstruction()
    {
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        SceneApplyItemPreview item = SceneApplyItemPreview.TransferToEmpty(
            SceneActivityPlan.Place(
                ActivityId,
                ActivityPlacement.On(DestinationId, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty),
            source,
            ChildContext.OperationId,
            ChildContext.CorrelationId);
        return SceneRemoteChildInstruction.Create(
            CoordinatorId,
            SceneId.Parse("abababab-abab-abab-abab-abababababab"),
            sceneRevision: 5,
            sceneDigest: new string('C', 64),
            previewFingerprint: new string('D', 64),
            ParentOperationId,
            ParentCorrelationId,
            acceptedAt: Now,
            item);
    }

    private static SceneSourceSelection CreateSourceSelection() =>
        SceneSourceSelection.Create(
            index: 3,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));

    private static SceneExactSlotQuery CreateExactSlotQuery(
        SceneSourceSelection source) => SceneExactSlotQuery.Create(
        ChildContext,
        SceneActivityPlan.Place(
            ActivityId,
            ActivityPlacement.On(DestinationId, "focus"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty),
        source);

    private static SceneActivityOperationResult CreateCommittedResult(
        SceneRemoteChildInstruction instruction) =>
        SceneActivityOperationResult.Create(
            OperationReceipt.FromRecordedResult(
                instruction.Item.ChildOperationId,
                instruction.Item.ChildCorrelationId,
                OperationKind.Handoff,
                OperationStatus.Committed,
                SourceId,
                DestinationId,
                ActivityId,
                instruction.Item.Source!.Kind,
                instruction.Item.Source.DescriptorDigest,
                Now.AddSeconds(2),
                FailureCode.None),
            undoCapsule: null);

    private static ControlMessage WithBody(
        ControlMessage message,
        JsonObject body) => ControlMessage.Create(
        message.Version,
        message.Type,
        message.MessageId,
        message.CorrelationId,
        message.SenderDeviceId,
        message.SentAt,
        TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
        body.ToJsonString());

    private static ControlMessage Freeze(
        ControlMessage message,
        string messageId) => ControlMessage.Create(
        message.Version,
        message.Type,
        Guid.Parse(messageId),
        message.CorrelationId,
        message.SenderDeviceId,
        message.SentAt,
        TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
        message.Body.GetRawText());
}
