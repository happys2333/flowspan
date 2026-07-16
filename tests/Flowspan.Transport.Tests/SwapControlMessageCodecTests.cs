using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class SwapControlMessageCodecTests
{
    private static readonly ProtocolVersion Version =
        ProtocolFeatures.ActivitySwapMinimumVersion;

    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 6, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId SourceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly OperationContext Context = OperationContext.Create(
        OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Now.AddSeconds(30));

    private static readonly SwapReservationToken SourceToken =
        SwapReservationToken.From(
            Guid.Parse("12121212-1212-1212-1212-121212121212"));

    private static readonly SwapReservationToken TargetToken =
        SwapReservationToken.From(
            Guid.Parse("13131313-1313-1313-1313-131313131313"));

    private static readonly ActivityInstance SourceActivity = CreateActivity(
        SourceId,
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "Source note",
        "source secret");

    private static readonly ActivityInstance TargetActivity = CreateActivity(
        TargetId,
        "dddddddd-dddd-dddd-dddd-dddddddddddd",
        "Target note",
        "target secret");

    [Fact]
    public void AllSixProtocolOnePointOneFramesAndHashesMatchFrozenFixture()
    {
        SwapActivitySnapshotQuery query = CreateQuery();
        SwapPrepareCommand prepare = CreatePrepare();
        SwapDecision decision = CreateDecision(SwapDecisionOutcome.Commit);
        (string Name, ControlMessage Message)[] messages =
        [
            ("snapshot-query", Freeze(
                SwapControlMessageCodec.CreateSnapshotQuery(
                    Version, SourceId, query, Now),
                "01010101-0101-0101-0101-010101010101")),
            ("snapshot-result", Freeze(
                SwapControlMessageCodec.CreateSnapshotResult(
                    Version,
                    TargetId,
                    SwapActivitySnapshotResult.Success(SourceId, query, TargetActivity),
                    Now),
                "02020202-0202-0202-0202-020202020202")),
            ("prepare", Freeze(
                SwapControlMessageCodec.CreatePrepare(
                    Version, SourceId, prepare, Now),
                "03030303-0303-0303-0303-030303030303")),
            ("prepare-result", Freeze(
                SwapControlMessageCodec.CreatePrepareResult(
                    Version,
                    TargetId,
                    SourceId,
                    prepare,
                    SwapPrepareResult.Success(TargetToken),
                    Now),
                "04040404-0404-0404-0404-040404040404")),
            ("decision", Freeze(
                SwapControlMessageCodec.CreateDecision(
                    Version,
                    SourceId,
                    Context.CorrelationId,
                    TargetId,
                    decision,
                    Now),
                "05050505-0505-0505-0505-050505050505")),
            ("decision-result", Freeze(
                SwapControlMessageCodec.CreateDecisionResult(
                    Version,
                    TargetId,
                    SourceId,
                    Context.CorrelationId,
                    decision,
                    SwapApplyResult.Success(SwapReservationPhase.Committed),
                    Now),
                "06060606-0606-0606-0606-060606060606")),
        ];
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "swap-control-v1.1.json");
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
                ?? throw new InvalidDataException("A Swap fixture frame is null.");
            string expectedHash = fixture.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("A Swap fixture hash is null.");

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
    public void AllSwapEnvelopeTypesRoundTripThroughCanonicalControlCodec()
    {
        SwapActivitySnapshotQuery query = CreateQuery();
        SwapPrepareCommand prepare = CreatePrepare();
        SwapDecision decision = CreateDecision(SwapDecisionOutcome.Commit);
        ControlMessage[] messages =
        [
            SwapControlMessageCodec.CreateSnapshotQuery(
                Version, SourceId, query, Now),
            SwapControlMessageCodec.CreateSnapshotResult(
                Version,
                TargetId,
                SwapActivitySnapshotResult.Success(SourceId, query, TargetActivity),
                Now),
            SwapControlMessageCodec.CreatePrepare(
                Version, SourceId, prepare, Now),
            SwapControlMessageCodec.CreatePrepareResult(
                Version,
                TargetId,
                SourceId,
                prepare,
                SwapPrepareResult.Success(TargetToken),
                Now),
            SwapControlMessageCodec.CreateDecision(
                Version,
                SourceId,
                Context.CorrelationId,
                TargetId,
                decision,
                Now),
            SwapControlMessageCodec.CreateDecisionResult(
                Version,
                TargetId,
                SourceId,
                Context.CorrelationId,
                decision,
                SwapApplyResult.Success(SwapReservationPhase.Committed),
                Now),
        ];

        ControlMessageType[] expectedTypes =
        [
            ControlMessageType.ActivitySwapSnapshot,
            ControlMessageType.ActivitySwapSnapshotResult,
            ControlMessageType.ActivitySwapPrepare,
            ControlMessageType.ActivitySwapPrepareResult,
            ControlMessageType.ActivitySwapDecision,
            ControlMessageType.ActivitySwapDecisionResult,
        ];
        Assert.Equal(expectedTypes, messages.Select(static message => message.Type));
        foreach (ControlMessage message in messages)
        {
            byte[] encoded = ControlMessageCodec.Encode(message);
            ControlMessage decoded = ControlMessageCodec.Decode(encoded);
            Assert.Equal(message.Type, decoded.Type);
            Assert.Equal(message.CorrelationId, decoded.CorrelationId);
            Assert.Equal(message.BodyDigest, decoded.BodyDigest);
            Assert.Equal(encoded, ControlMessageCodec.Encode(decoded));
        }
    }

    [Fact]
    public void SnapshotQueryAndSuccessRoundTripExactActivity()
    {
        SwapActivitySnapshotQuery query = CreateQuery();
        ControlMessage request = SwapControlMessageCodec.CreateSnapshotQuery(
            Version,
            SourceId,
            query,
            Now);
        SwapActivitySnapshotQuery decodedQuery =
            SwapControlMessageCodec.DecodeSnapshotQuery(request, TargetId);
        SwapActivitySnapshotResult expected = SwapActivitySnapshotResult.Success(
            SourceId,
            query,
            TargetActivity);
        ControlMessage response = SwapControlMessageCodec.CreateSnapshotResult(
            Version,
            TargetId,
            expected,
            Now.AddSeconds(1));
        SwapActivitySnapshotResult decoded =
            SwapControlMessageCodec.DecodeSnapshotResult(
                response,
                SourceId,
                query);

        Assert.Equal(query, decodedQuery);
        Assert.Equal(expected, decoded);
        Assert.Contains("target secret", response.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotRejectionContainsNoActivityPayload()
    {
        SwapActivitySnapshotQuery query = CreateQuery();
        SwapActivitySnapshotResult rejected = SwapActivitySnapshotResult.Rejected(
            SourceId,
            query,
            FailureCode.CapabilityDenied);
        ControlMessage response = SwapControlMessageCodec.CreateSnapshotResult(
            Version,
            TargetId,
            rejected,
            Now);

        SwapActivitySnapshotResult decoded =
            SwapControlMessageCodec.DecodeSnapshotResult(response, SourceId, query);

        Assert.Equal(rejected, decoded);
        Assert.DoesNotContain("target secret", response.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Null(response.Body.GetProperty("activity").Deserialize<object>());
    }

    [Fact]
    public void PrepareAndPreparedResultRoundTripEveryBinding()
    {
        SwapPrepareCommand command = CreatePrepare();
        ControlMessage request = SwapControlMessageCodec.CreatePrepare(
            Version,
            SourceId,
            command,
            Now);
        SwapPrepareCommand decodedCommand = SwapControlMessageCodec.DecodePrepare(
            request,
            TargetId);
        SwapPrepareResult prepared = SwapPrepareResult.Success(TargetToken);
        ControlMessage response = SwapControlMessageCodec.CreatePrepareResult(
            Version,
            TargetId,
            SourceId,
            command,
            prepared,
            Now.AddSeconds(1));
        SwapPrepareResult decodedResult =
            SwapControlMessageCodec.DecodePrepareResult(
                response,
                SourceId,
                command);

        Assert.Equal(command, decodedCommand);
        Assert.Equal(prepared, decodedResult);
        Assert.Contains("source secret", request.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("target secret", request.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionAndResultRoundTripExactParticipantsAndDigest()
    {
        SwapDecision decision = CreateDecision(SwapDecisionOutcome.Commit);
        ControlMessage request = SwapControlMessageCodec.CreateDecision(
            Version,
            SourceId,
            Context.CorrelationId,
            TargetId,
            decision,
            Now);
        SwapDecision decodedDecision = SwapControlMessageCodec.DecodeDecision(
            request,
            TargetId);
        SwapApplyResult applied = SwapApplyResult.Success(
            SwapReservationPhase.Committed);
        ControlMessage response = SwapControlMessageCodec.CreateDecisionResult(
            Version,
            TargetId,
            SourceId,
            Context.CorrelationId,
            decision,
            applied,
            Now.AddSeconds(1));
        SwapApplyResult decodedResult =
            SwapControlMessageCodec.DecodeDecisionResult(
                response,
                SourceId,
                Context.CorrelationId,
                TargetId,
                decision);

        Assert.Equal(decision.OperationId, decodedDecision.OperationId);
        Assert.Equal(decision.Outcome, decodedDecision.Outcome);
        Assert.Equal(decision.DecidedAt, decodedDecision.DecidedAt);
        Assert.Equal(decision.FailureCode, decodedDecision.FailureCode);
        Assert.Equal(decision.Digest, decodedDecision.Digest);
        Assert.Equal(
            decision.Participants.ToArray(),
            decodedDecision.Participants.ToArray());
        Assert.Equal(applied, decodedResult);
        Assert.DoesNotContain("secret", request.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("prepare")]
    [InlineData("decision")]
    public void RequestsRejectUnknownFields(string kind)
    {
        ControlMessage valid = kind switch
        {
            "snapshot" => SwapControlMessageCodec.CreateSnapshotQuery(
                Version, SourceId, CreateQuery(), Now),
            "prepare" => SwapControlMessageCodec.CreatePrepare(
                Version, SourceId, CreatePrepare(), Now),
            "decision" => SwapControlMessageCodec.CreateDecision(
                Version,
                SourceId,
                Context.CorrelationId,
                TargetId,
                CreateDecision(SwapDecisionOutcome.Commit),
                Now),
            _ => throw new InvalidOperationException("Unexpected request kind."),
        };
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["unexpected"] = "must fail closed";
        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() => kind switch
        {
            "snapshot" => SwapControlMessageCodec.DecodeSnapshotQuery(forged, TargetId),
            "prepare" => SwapControlMessageCodec.DecodePrepare(forged, TargetId),
            "decision" => SwapControlMessageCodec.DecodeDecision(forged, TargetId),
            _ => throw new InvalidOperationException("Unexpected request kind."),
        });
    }

    [Theory]
    [InlineData("snapshot-result")]
    [InlineData("prepare-result")]
    [InlineData("decision-result")]
    public void ResultsRejectUnknownFields(string kind)
    {
        SwapActivitySnapshotQuery query = CreateQuery();
        SwapPrepareCommand prepare = CreatePrepare();
        SwapDecision decision = CreateDecision(SwapDecisionOutcome.Commit);
        ControlMessage valid = kind switch
        {
            "snapshot-result" => SwapControlMessageCodec.CreateSnapshotResult(
                Version,
                TargetId,
                SwapActivitySnapshotResult.Success(SourceId, query, TargetActivity),
                Now),
            "prepare-result" => SwapControlMessageCodec.CreatePrepareResult(
                Version,
                TargetId,
                SourceId,
                prepare,
                SwapPrepareResult.Success(TargetToken),
                Now),
            "decision-result" => SwapControlMessageCodec.CreateDecisionResult(
                Version,
                TargetId,
                SourceId,
                Context.CorrelationId,
                decision,
                SwapApplyResult.Success(SwapReservationPhase.Committed),
                Now),
            _ => throw new InvalidOperationException("Unexpected result kind."),
        };
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["unexpected"] = "must fail closed";
        ControlMessage forged = WithBody(valid, body);

        void Decode()
        {
            switch (kind)
            {
                case "snapshot-result":
                    _ = SwapControlMessageCodec.DecodeSnapshotResult(
                        forged,
                        SourceId,
                        query);
                    break;
                case "prepare-result":
                    _ = SwapControlMessageCodec.DecodePrepareResult(
                        forged,
                        SourceId,
                        prepare);
                    break;
                case "decision-result":
                    _ = SwapControlMessageCodec.DecodeDecisionResult(
                        forged,
                        SourceId,
                        Context.CorrelationId,
                        TargetId,
                        decision);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected result kind.");
            }
        }

        Assert.Throws<InvalidDataException>(Decode);
    }

    [Fact]
    public void SnapshotResultRejectsCrossOperationAndCorrelationReuse()
    {
        SwapActivitySnapshotQuery query = CreateQuery();
        ControlMessage valid = SwapControlMessageCodec.CreateSnapshotResult(
            Version,
            TargetId,
            SwapActivitySnapshotResult.Success(SourceId, query, TargetActivity),
            Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["operationId"] = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodeSnapshotResult(
                WithBody(valid, body),
                SourceId,
                query));

        ControlMessage wrongCorrelation = WithCorrelation(
            valid,
            CorrelationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodeSnapshotResult(
                wrongCorrelation,
                SourceId,
                query));
    }

    [Theory]
    [InlineData("payloadDigest")]
    [InlineData("descriptorDigest")]
    [InlineData("requestDigest")]
    public void PrepareRejectsTamperedActivityAndRequestDigests(string field)
    {
        ControlMessage valid = SwapControlMessageCodec.CreatePrepare(
            Version,
            SourceId,
            CreatePrepare(),
            Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        JsonObject target = field == "requestDigest"
            ? body
            : body["incomingActivity"]!.AsObject();
        target[field] = new string('A', 64);

        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodePrepare(WithBody(valid, body), TargetId));
    }

    [Fact]
    public void DecisionRejectsTamperedDigestAndParticipantToken()
    {
        ControlMessage valid = SwapControlMessageCodec.CreateDecision(
            Version,
            SourceId,
            Context.CorrelationId,
            TargetId,
            CreateDecision(SwapDecisionOutcome.Commit),
            Now);
        JsonObject digestBody = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        digestBody["decisionDigest"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodeDecision(
                WithBody(valid, digestBody),
                TargetId));

        JsonObject tokenBody = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        tokenBody["participants"]!.AsArray()[1]!.AsObject()["reservationToken"] =
            "14141414-1414-1414-1414-141414141414";
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodeDecision(
                WithBody(valid, tokenBody),
                TargetId));
    }

    [Fact]
    public void PrepareResultRejectsWrongRecipientTokenAndRequestDigest()
    {
        SwapPrepareCommand command = CreatePrepare();
        ControlMessage valid = SwapControlMessageCodec.CreatePrepareResult(
            Version,
            TargetId,
            SourceId,
            command,
            SwapPrepareResult.Success(TargetToken),
            Now);

        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodePrepareResult(valid, TargetId, command));

        JsonObject tokenBody = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        tokenBody["reservationToken"] =
            "14141414-1414-1414-1414-141414141414";
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodePrepareResult(
                WithBody(valid, tokenBody),
                SourceId,
                command));

        JsonObject digestBody = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        digestBody["requestDigest"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodePrepareResult(
                WithBody(valid, digestBody),
                SourceId,
                command));
    }

    [Fact]
    public void DecisionResultRejectsWrongPhaseAndDigest()
    {
        SwapDecision decision = CreateDecision(SwapDecisionOutcome.Commit);
        ControlMessage valid = SwapControlMessageCodec.CreateDecisionResult(
            Version,
            TargetId,
            SourceId,
            Context.CorrelationId,
            decision,
            SwapApplyResult.Success(SwapReservationPhase.Committed),
            Now);
        JsonObject phaseBody = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        phaseBody["phase"] = "aborted";
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodeDecisionResult(
                WithBody(valid, phaseBody),
                SourceId,
                Context.CorrelationId,
                TargetId,
                decision));

        JsonObject digestBody = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        digestBody["decisionDigest"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() =>
            SwapControlMessageCodec.DecodeDecisionResult(
                WithBody(valid, digestBody),
                SourceId,
                Context.CorrelationId,
                TargetId,
                decision));
    }

    [Fact]
    public void SnapshotAndPrepareRejectDeadlineBeyondEnvelopeLimit()
    {
        OperationContext longContext = OperationContext.Create(
            Context.OperationId,
            Context.CorrelationId,
            Now.AddMilliseconds(ControlMessage.MaximumTimeToLiveMilliseconds + 1));
        SwapActivitySnapshotQuery query = SwapActivitySnapshotQuery.Create(
            longContext,
            TargetId,
            TargetActivity.Descriptor.Id);
        var prepare = new SwapPrepareCommand(
            Context.OperationId,
            Context.CorrelationId,
            TargetToken,
            TargetActivity,
            SourceActivity,
            longContext.Deadline);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SwapControlMessageCodec.CreateSnapshotQuery(
                Version, SourceId, query, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SwapControlMessageCodec.CreatePrepare(
                Version, SourceId, prepare, Now));
    }

    private static SwapActivitySnapshotQuery CreateQuery() =>
        SwapActivitySnapshotQuery.Create(
            Context,
            TargetId,
            TargetActivity.Descriptor.Id);

    private static SwapPrepareCommand CreatePrepare() => new(
        Context.OperationId,
        Context.CorrelationId,
        TargetToken,
        TargetActivity,
        SourceActivity,
        Context.Deadline);

    private static SwapDecision CreateDecision(SwapDecisionOutcome outcome) =>
        SwapDecision.Create(
            Context.OperationId,
            outcome,
            Now.AddSeconds(1),
            [
                SwapDecisionParticipant.Create(SourceId, SourceToken),
                SwapDecisionParticipant.Create(TargetId, TargetToken),
            ],
            outcome == SwapDecisionOutcome.Commit
                ? FailureCode.None
                : FailureCode.PeerUnavailable);

    private static ActivityInstance CreateActivity(
        DeviceId deviceId,
        string activityId,
        string title,
        string text) => ActivityInstance.Active(
        ActivityDescriptor.Create(
            ActivityId.Parse(activityId),
            ActivityKind.Parse("workspace.note/v1"),
            deviceId,
            title,
            JsonSerializer.Serialize(new { text })),
        ActivityPlacement.On(deviceId, "desktop"),
        revision: 7);

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

    private static ControlMessage WithCorrelation(
        ControlMessage message,
        CorrelationId correlationId) => ControlMessage.Create(
        message.Version,
        message.Type,
        message.MessageId,
        correlationId,
        message.SenderDeviceId,
        message.SentAt,
        TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
        message.Body.GetRawText());
}
