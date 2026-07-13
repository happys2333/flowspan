using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Protocol.Tests;

public sealed class ControlMessageCodecTests
{
    private static readonly Guid MessageId =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly CorrelationId Correlation =
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly DeviceId Sender =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset SentAt =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MessageRoundTripsWithTypedMetadata()
    {
        ControlMessage message = Create("{\"features\":[\"activity.transfer\"]}");

        byte[] encoded = ControlMessageCodec.Encode(message);
        ControlMessage decoded = ControlMessageCodec.Decode(encoded);

        Assert.Equal(new ProtocolVersion(1, 0), decoded.Version);
        Assert.Equal(ControlMessageType.Hello, decoded.Type);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(Correlation, decoded.CorrelationId);
        Assert.Equal(Sender, decoded.SenderDeviceId);
        Assert.Equal(SentAt, decoded.SentAt);
        Assert.Equal(30_000, decoded.TimeToLiveMilliseconds);
        Assert.Equal(message.BodyDigest, decoded.BodyDigest);
        Assert.Equal(
            "activity.transfer",
            decoded.Body.GetProperty("features")[0].GetString());
    }

    [Fact]
    public void WriterAndReaderMatchCommittedVersionOneFixture()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "hello-v1.json");
        string fixture = File.ReadAllText(fixturePath).TrimEnd('\r', '\n');
        ControlMessage message = Create(
            "{\"versions\":[\"1.0\"],\"features\":[\"activity.transfer\"]}");

        string encoded = Encoding.UTF8.GetString(ControlMessageCodec.Encode(message));
        ControlMessage decoded = ControlMessageCodec.Decode(Encoding.UTF8.GetBytes(fixture));

        Assert.Equal(fixture, encoded);
        Assert.Equal(ControlMessageType.Hello, decoded.Type);
        Assert.Equal(message.BodyDigest, decoded.BodyDigest);
    }

    [Fact]
    public void ObjectPropertyOrderAndWhitespaceAreCanonicalized()
    {
        ControlMessage first = Create("{\"z\": 1, \"a\": {\"y\": true, \"x\": null}}");
        ControlMessage second = Create("{\"a\":{\"x\":null,\"y\":true},\"z\":1}");

        Assert.Equal(first.BodyDigest, second.BodyDigest);
        Assert.Equal(ControlMessageCodec.Encode(first), ControlMessageCodec.Encode(second));
    }

    [Fact]
    public void TamperedBodyIsRejectedInConstantTimeDigestPath()
    {
        ControlMessage message = Create("{\"value\":\"ORIGINAL\"}");
        string encoded = Encoding.UTF8.GetString(ControlMessageCodec.Encode(message));
        byte[] tampered = Encoding.UTF8.GetBytes(
            encoded.Replace("ORIGINAL", "TAMPERED", StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => ControlMessageCodec.Decode(tampered));
    }

    [Fact]
    public void UnknownMessageTypeIsRejected()
    {
        string encoded = Encoding.UTF8.GetString(ControlMessageCodec.Encode(Create("{}")));
        byte[] unknownType = Encoding.UTF8.GetBytes(
            encoded.Replace(
                "\"type\":\"hello\"",
                "\"type\":\"bogus\"",
                StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => ControlMessageCodec.Decode(unknownType));
    }

    [Fact]
    public void DuplicatePropertiesAreRejectedAtAnyDepth()
    {
        Assert.Throws<InvalidDataException>(() => Create("{\"outer\":{\"x\":1,\"x\":2}}"));
    }

    [Fact]
    public void DuplicateEnvelopePropertyIsRejectedBeforeUse()
    {
        byte[] duplicate = Encoding.UTF8.GetBytes("{\"magic\":\"FSPN\",\"magic\":\"FSPN\"}");

        Assert.Throws<InvalidDataException>(() => ControlMessageCodec.Decode(duplicate));
    }

    [Fact]
    public void OversizedFrameIsRejectedBeforeParsing()
    {
        byte[] oversized = new byte[ControlMessageCodec.MaximumFrameBytes + 1];

        Assert.Throws<InvalidDataException>(() => ControlMessageCodec.Decode(oversized));
    }

    [Fact]
    public void NonObjectBodyAndInvalidTimeToLiveAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Create("[]"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlMessage.Create(
            new ProtocolVersion(1, 0),
            ControlMessageType.Hello,
            MessageId,
            Correlation,
            Sender,
            SentAt,
            TimeSpan.Zero,
            "{}"));
    }

    [Fact]
    public void DefaultVersionAndUnknownTypeAreRejectedBeforeEncoding()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlMessage.Create(
            default,
            ControlMessageType.Hello,
            MessageId,
            Correlation,
            Sender,
            SentAt,
            TimeSpan.FromSeconds(30),
            "{}"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlMessage.Create(
            new ProtocolVersion(1, 0),
            (ControlMessageType)999,
            MessageId,
            Correlation,
            Sender,
            SentAt,
            TimeSpan.FromSeconds(30),
            "{}"));
    }

    private static ControlMessage Create(string bodyJson) => ControlMessage.Create(
        new ProtocolVersion(1, 0),
        ControlMessageType.Hello,
        MessageId,
        Correlation,
        Sender,
        SentAt,
        TimeSpan.FromSeconds(30),
        bodyJson);
}
