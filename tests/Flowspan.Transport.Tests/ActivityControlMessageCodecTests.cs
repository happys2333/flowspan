using System.Text.Json;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class ActivityControlMessageCodecTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 16, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId SourceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly ActivityDescriptor Descriptor = ActivityDescriptor.Create(
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        ActivityKind.Parse("workspace.note/v1"),
        SourceId,
        "Plan & notes",
        JsonSerializer.Serialize(new { text = "one-shot semantic payload" }),
        ActivitySensitivity.Sensitive);

    private static readonly ActivityDescriptor TargetDescriptor = ActivityDescriptor.Create(
        ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        ActivityKind.Parse("workspace.note/v1"),
        TargetId,
        "Current target",
        JsonSerializer.Serialize(new { text = "preserve this target state" }));

    private static readonly OperationContext Context = OperationContext.Create(
        OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Now.AddSeconds(30));

    [Fact]
    public void TransferRoundTripsEveryValidatedField()
    {
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            Context,
            Descriptor,
            ActivityPlacement.On(TargetId, "desktop-primary"));

        ControlMessage message = ActivityControlMessageCodec.CreateTransfer(
            new ProtocolVersion(1, 0),
            SourceId,
            offer,
            Now);
        ActivityTransferOffer decoded = ActivityControlMessageCodec.DecodeTransfer(
            message,
            TargetId);

        Assert.Equal(ControlMessageType.ActivityTransfer, message.Type);
        Assert.Equal(Context.CorrelationId, message.CorrelationId);
        Assert.Equal(SourceId, message.SenderDeviceId);
        Assert.Equal(Context.OperationId, decoded.Context.OperationId);
        Assert.Equal(Context.CorrelationId, decoded.Context.CorrelationId);
        Assert.Equal(Context.Deadline, decoded.Context.Deadline);
        Assert.Equal(OperationKind.Handoff, decoded.Kind);
        Assert.Equal(TargetId, decoded.TargetPlacement.DeviceId);
        Assert.Equal("desktop-primary", decoded.TargetPlacement.Slot);
        Assert.Equal(Descriptor, decoded.Descriptor);
        Assert.Equal(offer.RequestDigest, decoded.RequestDigest);
    }

    [Fact]
    public void TransferCreationRejectsDeadlineBeyondEnvelopeLimit()
    {
        var longContext = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            Now.AddMilliseconds(ControlMessage.MaximumTimeToLiveMilliseconds + 1));
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            longContext,
            Descriptor,
            ActivityPlacement.On(TargetId));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ActivityControlMessageCodec.CreateTransfer(
                new ProtocolVersion(1, 0),
                SourceId,
                offer,
                Now));
    }

    [Fact]
    public void ReplaceInventoryQueryRoundTripsPurposeAndDeadline()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);

        ControlMessage message =
            ActivityControlMessageCodec.CreateReplaceInventoryQuery(
                new ProtocolVersion(1, 0),
                SourceId,
                query,
                Now);
        ReplaceTargetInventoryQuery decoded =
            ActivityControlMessageCodec.DecodeReplaceInventoryQuery(
                message,
                TargetId);

        Assert.Equal(ControlMessageType.ActivityReplaceInventory, message.Type);
        Assert.Equal(Context.CorrelationId, message.CorrelationId);
        Assert.Equal(SourceId, message.SenderDeviceId);
        Assert.Equal(query, decoded);
    }

    [Fact]
    public void ReplaceInventoryQueryRejectsUnknownFields()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryQuery(
                new ProtocolVersion(1, 0),
                SourceId,
                query,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["unexpected"] = "must not be ignored";
        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryQuery(
                forged,
                TargetId));
    }

    [Fact]
    public void ReplaceInventoryQueryRejectsWrongTargetAndDeadlineOutsideEnvelope()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryQuery(
                new ProtocolVersion(1, 0),
                SourceId,
                query,
                Now);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryQuery(
                valid,
                SourceId));

        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["deadline"] = query.Deadline.AddMilliseconds(1);
        ControlMessage forged = WithBody(valid, body);
        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryQuery(
                forged,
                TargetId));
    }

    [Fact]
    public void ReplaceInventoryQueryCreationRejectsDeadlineBeyondEnvelopeLimit()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddMilliseconds(ControlMessage.MaximumTimeToLiveMilliseconds + 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ActivityControlMessageCodec.CreateReplaceInventoryQuery(
                new ProtocolVersion(1, 0),
                SourceId,
                query,
                Now));
    }

    [Fact]
    public void ReplaceInventoryResultRoundTripsBoundSnapshotsWithoutPayload()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            TargetDescriptor.Id,
            revision: 7,
            TargetDescriptor.DescriptorDigest,
            TargetDescriptor.Kind,
            TargetDescriptor.Title,
            "desktop-primary");
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Success(
            SourceId,
            query,
            Now.AddSeconds(1),
            [target],
            isTruncated: false);

        ControlMessage message =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now.AddSeconds(1));
        ReplaceTargetInventoryResult decoded =
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                message,
                SourceId,
                query);

        Assert.Equal(ControlMessageType.ActivityReplaceInventoryResult, message.Type);
        Assert.Equal(SourceId, decoded.RequestingDeviceId);
        Assert.Equal(TargetId, decoded.TargetDeviceId);
        Assert.Equal(query.Deadline, decoded.QueryDeadline);
        Assert.Equal(FailureCode.None, decoded.FailureCode);
        Assert.False(decoded.IsTruncated);
        Assert.Equal(target, Assert.Single(decoded.Targets));
        Assert.DoesNotContain(
            "preserve this target state",
            message.Body.GetRawText(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TargetDescriptor.PayloadDigest,
            message.Body.GetRawText(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("requestingDeviceId")]
    [InlineData("targetDeviceId")]
    [InlineData("incomingKind")]
    [InlineData("queryDeadline")]
    public void ReplaceInventoryResultRejectsForgedPurposeBinding(string field)
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Success(
            SourceId,
            query,
            Now,
            [],
            isTruncated: false);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body[field] = field switch
        {
            "requestingDeviceId" or "targetDeviceId" =>
                "33333333-3333-3333-3333-333333333333",
            "incomingKind" => "workspace.other/v1",
            "queryDeadline" => Now.AddSeconds(10),
            _ => throw new InvalidOperationException("Unexpected test field."),
        };
        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                forged,
                SourceId,
                query));
    }

    [Fact]
    public void ReplaceInventoryResultRejectsCaptureAfterAuthenticatedSentTime()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Success(
            SourceId,
            query,
            Now,
            [],
            isTruncated: false);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["capturedAt"] = Now.AddSeconds(1);
        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                forged,
                SourceId,
                query));
    }

    [Theory]
    [InlineData("AAAA")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void ReplaceInventoryResultRejectsMalformedDescriptorDigest(string digest)
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            TargetDescriptor.Id,
            revision: 7,
            TargetDescriptor.DescriptorDigest,
            TargetDescriptor.Kind,
            TargetDescriptor.Title,
            "desktop-primary");
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Success(
            SourceId,
            query,
            Now,
            [target],
            isTruncated: false);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["targets"]!.AsArray()[0]!.AsObject()["descriptorDigest"] = digest;
        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                forged,
                SourceId,
                query));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaceInventoryResultRejectsUnknownFields(bool nestedTarget)
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            TargetDescriptor.Id,
            revision: 7,
            TargetDescriptor.DescriptorDigest,
            TargetDescriptor.Kind,
            TargetDescriptor.Title,
            "desktop-primary");
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Success(
            SourceId,
            query,
            Now,
            [target],
            isTruncated: false);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        JsonObject mutated = nestedTarget
            ? body["targets"]!.AsArray()[0]!.AsObject()
            : body;
        mutated["unexpected"] = "must not be ignored";
        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                forged,
                SourceId,
                query));
    }

    [Fact]
    public void ReplaceInventoryResultRejectsOversizedTargetArrayBeforeProjection()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            TargetDescriptor.Id,
            revision: 7,
            TargetDescriptor.DescriptorDigest,
            TargetDescriptor.Kind,
            TargetDescriptor.Title,
            "desktop-primary");
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Success(
            SourceId,
            query,
            Now,
            [target],
            isTruncated: false);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        JsonArray targets = body["targets"]!.AsArray();
        JsonNode template = targets[0]!.DeepClone();
        while (targets.Count <= ReplaceTargetInventoryResult.MaximumTargets)
        {
            targets.Add(template.DeepClone());
        }

        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                forged,
                SourceId,
                query));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("truncation")]
    public void RejectedReplaceInventoryCannotDiscloseTargets(string mutation)
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            Context.CorrelationId,
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Context.Deadline);
        ReplaceTargetInventoryResult result = ReplaceTargetInventoryResult.Rejected(
            SourceId,
            query,
            Now,
            FailureCode.CapabilityDenied);
        ControlMessage valid =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                TargetId,
                result,
                Now);
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        if (mutation == "truncation")
        {
            body["isTruncated"] = true;
        }
        else
        {
            body["targets"]!.AsArray().Add(new JsonObject
            {
                ["activityId"] = TargetDescriptor.Id.ToString(),
                ["descriptorDigest"] = TargetDescriptor.DescriptorDigest,
                ["kind"] = TargetDescriptor.Kind.Value,
                ["placementSlot"] = "desktop-primary",
                ["revision"] = 7,
                ["title"] = TargetDescriptor.Title,
            });
        }

        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                forged,
                SourceId,
                query));
    }

    [Fact]
    public void ReplaceRoundTripsEveryBoundedField()
    {
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            Context,
            TargetDescriptor.Id,
            expectedTargetRevision: 7,
            TargetDescriptor.DescriptorDigest,
            Descriptor,
            ActivityPlacement.On(TargetId, "desktop-primary"),
            Now.AddMinutes(10));

        ControlMessage message = ActivityControlMessageCodec.CreateReplace(
            new ProtocolVersion(1, 0),
            SourceId,
            command,
            Now);
        ReplaceActivityCommand decoded = ActivityControlMessageCodec.DecodeReplace(
            message,
            TargetId);

        Assert.Equal(ControlMessageType.ActivityReplace, message.Type);
        Assert.Equal(Context.CorrelationId, message.CorrelationId);
        Assert.Equal(SourceId, message.SenderDeviceId);
        Assert.Equal(Context, decoded.Context);
        Assert.Equal(TargetDescriptor.Id, decoded.TargetActivityId);
        Assert.Equal(7, decoded.ExpectedTargetRevision);
        Assert.Equal(
            TargetDescriptor.DescriptorDigest,
            decoded.ExpectedTargetDescriptorDigest);
        Assert.Equal(Descriptor, decoded.IncomingDescriptor);
        Assert.Equal(TargetId, decoded.TargetPlacement.DeviceId);
        Assert.Equal("desktop-primary", decoded.TargetPlacement.Slot);
        Assert.Equal(Now.AddMinutes(10), decoded.UndoExpiresAt);
        Assert.Equal(command.RequestDigest, decoded.RequestDigest);
    }

    [Fact]
    public void ReplaceResultRoundTripsOnlyBoundCapsuleMetadata()
    {
        OperationReceipt receipt = OperationReceipt.Committed(
            Context.OperationId,
            Context.CorrelationId,
            OperationKind.Replace,
            SourceId,
            TargetId,
            Descriptor,
            Now.AddSeconds(1));
        var capsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Context.OperationId,
            Context.CorrelationId,
            TargetId,
            TargetDescriptor.Id,
            7,
            TargetDescriptor.DescriptorDigest,
            Descriptor.Id,
            Descriptor.DescriptorDigest,
            Now.AddMinutes(10));
        var result = new ReplaceOperationResult(receipt, capsule);

        ControlMessage message = ActivityControlMessageCodec.CreateReplaceResult(
            new ProtocolVersion(1, 0),
            TargetId,
            result,
            Now.AddSeconds(1));
        ReplaceOperationResult decoded =
            ActivityControlMessageCodec.DecodeReplaceResult(
                message,
                SourceId,
                Context.CorrelationId);

        Assert.Equal(ControlMessageType.ActivityReplaceResult, message.Type);
        Assert.Equal(result, decoded);
        Assert.DoesNotContain(
            "preserve this target state",
            message.Body.GetRawText(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TargetDescriptor.PayloadDigest,
            message.Body.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceResultRejectsNonUtcUndoCapsuleExpiry()
    {
        OperationReceipt receipt = OperationReceipt.Committed(
            Context.OperationId,
            Context.CorrelationId,
            OperationKind.Replace,
            SourceId,
            TargetId,
            Descriptor,
            Now.AddSeconds(1));
        var capsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Context.OperationId,
            Context.CorrelationId,
            TargetId,
            TargetDescriptor.Id,
            7,
            TargetDescriptor.DescriptorDigest,
            Descriptor.Id,
            Descriptor.DescriptorDigest,
            Now.AddMinutes(10));
        var result = new ReplaceOperationResult(receipt, capsule);
        ControlMessage valid = ActivityControlMessageCodec.CreateReplaceResult(
            new ProtocolVersion(1, 0),
            TargetId,
            result,
            Now.AddSeconds(1));
        JsonObject body = JsonNode.Parse(valid.Body.GetRawText())!.AsObject();
        body["undoCapsule"]!["expiresAt"] = "2026-07-14T18:10:00+02:00";

        ControlMessage forged = WithBody(valid, body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplaceResult(
                forged,
                SourceId,
                Context.CorrelationId));
    }

    [Fact]
    public void ReplaceRejectsTargetSnapshotTamperingEvenWithValidEnvelopeDigest()
    {
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            Context,
            TargetDescriptor.Id,
            expectedTargetRevision: 7,
            TargetDescriptor.DescriptorDigest,
            Descriptor,
            ActivityPlacement.On(TargetId, "desktop-primary"),
            Now.AddMinutes(10));
        ControlMessage valid = ActivityControlMessageCodec.CreateReplace(
            new ProtocolVersion(1, 0),
            SourceId,
            command,
            Now);
        string body = valid.Body.GetRawText().Replace(
            TargetDescriptor.DescriptorDigest,
            new string('A', 64),
            StringComparison.Ordinal);
        ControlMessage forged = ControlMessage.Create(
            valid.Version,
            valid.Type,
            valid.MessageId,
            valid.CorrelationId,
            valid.SenderDeviceId,
            valid.SentAt,
            TimeSpan.FromMilliseconds(valid.TimeToLiveMilliseconds),
            body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReplace(forged, TargetId));
    }

    [Fact]
    public void TransferRejectsWrongTargetAndUnverifiedDescriptorDigest()
    {
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            Context,
            Descriptor,
            ActivityPlacement.On(TargetId));
        ControlMessage valid = ActivityControlMessageCodec.CreateTransfer(
            new ProtocolVersion(1, 0),
            SourceId,
            offer,
            Now);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeTransfer(valid, SourceId));

        string body = valid.Body.GetRawText().Replace(
            Descriptor.DescriptorDigest,
            new string('A', 64),
            StringComparison.Ordinal);
        ControlMessage forged = ControlMessage.Create(
            valid.Version,
            valid.Type,
            valid.MessageId,
            valid.CorrelationId,
            valid.SenderDeviceId,
            valid.SentAt,
            TimeSpan.FromMilliseconds(valid.TimeToLiveMilliseconds),
            body);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeTransfer(forged, TargetId));
    }

    [Fact]
    public void ReceiptRoundTripsWithoutDescriptorPayload()
    {
        OperationReceipt receipt = OperationReceipt.Committed(
            Context.OperationId,
            Context.CorrelationId,
            OperationKind.Handoff,
            SourceId,
            TargetId,
            Descriptor,
            Now.AddSeconds(1));

        ControlMessage message = ActivityControlMessageCodec.CreateReceipt(
            new ProtocolVersion(1, 0),
            TargetId,
            receipt,
            Now.AddSeconds(1));
        OperationReceipt decoded = ActivityControlMessageCodec.DecodeReceipt(
            message,
            SourceId,
            Context.CorrelationId);

        Assert.Equal(receipt, decoded);
        Assert.Equal(ControlMessageType.OperationReceipt, message.Type);
        Assert.DoesNotContain(
            "one-shot semantic payload",
            message.Body.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptRejectsWrongRecipientAndCorrelationMismatch()
    {
        OperationReceipt receipt = OperationReceipt.Rejected(
            Context.OperationId,
            Context.CorrelationId,
            OperationKind.Handoff,
            SourceId,
            TargetId,
            Descriptor,
            Now,
            FailureCode.CapabilityDenied);
        ControlMessage valid = ActivityControlMessageCodec.CreateReceipt(
            new ProtocolVersion(1, 0),
            TargetId,
            receipt,
            Now);

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReceipt(
                valid,
                TargetId,
                Context.CorrelationId));

        ControlMessage wrongCorrelation = ControlMessage.Create(
            valid.Version,
            valid.Type,
            valid.MessageId,
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            valid.SenderDeviceId,
            valid.SentAt,
            TimeSpan.FromMilliseconds(valid.TimeToLiveMilliseconds),
            valid.Body.GetRawText());

        Assert.Throws<InvalidDataException>(() =>
            ActivityControlMessageCodec.DecodeReceipt(
                wrongCorrelation,
                SourceId,
                Context.CorrelationId));
    }

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
}
