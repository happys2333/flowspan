using System.Text.Json;
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
}
