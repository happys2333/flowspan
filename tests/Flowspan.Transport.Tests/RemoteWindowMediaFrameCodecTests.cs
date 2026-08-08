using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowMediaFrameCodecTests
{
    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void VideoChunkRoundTripsExactBindingAndDefensivePayload()
    {
        byte[] source = [0x10, 0x20, 0x30, 0x40];
        RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 7,
            chunkIndex: 1,
            chunkCount: 2,
            source);
        source[0] = 0xff;

        byte[] encoded = RemoteWindowMediaFrameCodec.Encode(expected);
        RemoteWindowMediaFrame decoded = RemoteWindowMediaFrameCodec.Decode(
            encoded,
            SessionId,
            ActivityId);
        byte[] firstExport = decoded.ExportPayload();
        firstExport[1] = 0xff;

        Assert.Equal(RemoteWindowMediaFrameCodec.HeaderBytes + 4, encoded.Length);
        Assert.Equal(RemoteWindowMediaKind.Video, decoded.Kind);
        Assert.Equal<ulong>(7, decoded.Sequence);
        Assert.Equal<ushort>(1, decoded.ChunkIndex);
        Assert.Equal<ushort>(2, decoded.ChunkCount);
        Assert.Equal([0x10, 0x20, 0x30, 0x40], decoded.ExportPayload());
        Assert.DoesNotContain(
            Convert.ToHexString([0x10, 0x20, 0x30, 0x40]),
            decoded.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BinaryFrameHasFrozenHash()
    {
        RemoteWindowMediaFrame frame = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x01, 0x02, 0x03]);
        byte[] encoded = RemoteWindowMediaFrameCodec.Encode(frame);

        Assert.Equal(
            "B1A481AEFD4B67F0D178A8898AF5AF251A5144B8BF3692BED8DF157E19631362",
            Convert.ToHexString(SHA256.HashData(encoded)));
    }

    [Fact]
    public void OversizedPayloadIsRejectedBeforeEncoding()
    {
        byte[] payload = new byte[RemoteWindowMediaFrame.MaximumPayloadBytes + 1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Video,
                sequence: 1,
                chunkIndex: 0,
                chunkCount: 1,
                payload));
    }

    [Theory]
    [InlineData(RemoteWindowMediaKind.Video, 0, 0)]
    [InlineData(RemoteWindowMediaKind.Video, 0, 17)]
    [InlineData(RemoteWindowMediaKind.Video, 2, 2)]
    [InlineData(RemoteWindowMediaKind.Audio, 1, 1)]
    [InlineData(RemoteWindowMediaKind.Audio, 0, 2)]
    [InlineData(RemoteWindowMediaKind.Cursor, 1, 1)]
    public void InvalidChunkShapesAreRejected(
        RemoteWindowMediaKind kind,
        ushort chunkIndex,
        ushort chunkCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                kind,
                sequence: 1,
                chunkIndex,
                chunkCount,
                [0x01]));
    }

    [Theory]
    [InlineData(0, 0x58)]
    [InlineData(4, 0x02)]
    [InlineData(5, 0xff)]
    [InlineData(6, 0x01)]
    [InlineData(7, 0x01)]
    public void UnknownHeaderValuesAreRejected(int offset, byte value)
    {
        byte[] encoded = EncodeCursor();
        encoded[offset] = value;

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(encoded, SessionId, ActivityId));
    }

    [Fact]
    public void WrongSessionOrActivityBindingIsRejected()
    {
        byte[] encoded = EncodeCursor();
        RemoteWindowSessionId otherSession = RemoteWindowSessionId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        ActivityId otherActivity = ActivityId.Parse(
            "dddddddd-dddd-dddd-dddd-dddddddddddd");

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(
                encoded,
                otherSession,
                ActivityId));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(
                encoded,
                SessionId,
                otherActivity));
    }

    [Fact]
    public void TruncatedTrailingAndHostileLengthFramesAreRejected()
    {
        byte[] encoded = EncodeCursor();
        byte[] shortHeader = new byte[RemoteWindowMediaFrameCodec.HeaderBytes - 1];
        byte[] truncated = encoded[..^1];
        byte[] trailing = [.. encoded, 0x00];
        byte[] oversized = encoded.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(
            oversized.AsSpan(52, sizeof(uint)),
            RemoteWindowMediaFrame.MaximumPayloadBytes + 1);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(shortHeader, SessionId, ActivityId));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(truncated, SessionId, ActivityId));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(trailing, SessionId, ActivityId));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(oversized, SessionId, ActivityId));
    }

    [Fact]
    public void InvalidSequenceAndChunkShapeOnWireAreRejected()
    {
        byte[] zeroSequence = EncodeCursor();
        BinaryPrimitives.WriteUInt64BigEndian(
            zeroSequence.AsSpan(40, sizeof(ulong)),
            0);
        byte[] cursorWithSecondChunk = EncodeCursor();
        BinaryPrimitives.WriteUInt16BigEndian(
            cursorWithSecondChunk.AsSpan(48, sizeof(ushort)),
            1);
        byte[] tooManyVideoChunks = EncodeCursor();
        tooManyVideoChunks[5] = (byte)RemoteWindowMediaKind.Video;
        BinaryPrimitives.WriteUInt16BigEndian(
            tooManyVideoChunks.AsSpan(50, sizeof(ushort)),
            RemoteWindowMediaFrame.MaximumVideoChunks + 1);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(zeroSequence, SessionId, ActivityId));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(
                cursorWithSecondChunk,
                SessionId,
                ActivityId));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowMediaFrameCodec.Decode(
                tooManyVideoChunks,
                SessionId,
                ActivityId));
    }

    [Fact]
    public void DecodedPayloadIsOwnedAndDiagnosticsArePayloadFree()
    {
        const string canary = "FLOWSPAN-MEDIA-PAYLOAD-CANARY";
        byte[] payload = Encoding.ASCII.GetBytes(canary);
        byte[] encoded = RemoteWindowMediaFrameCodec.Encode(
            RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Audio,
                sequence: 2,
                chunkIndex: 0,
                chunkCount: 1,
                payload));

        RemoteWindowMediaFrame decoded = RemoteWindowMediaFrameCodec.Decode(
            encoded,
            SessionId,
            ActivityId);
        encoded[RemoteWindowMediaFrameCodec.HeaderBytes] = 0xff;
        string diagnostic = decoded.ToString();

        Assert.Equal(payload, decoded.ExportPayload());
        Assert.DoesNotContain(canary, diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Convert.ToBase64String(payload),
            diagnostic,
            StringComparison.Ordinal);
        Assert.DoesNotContain(SessionId.ToString(), diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(ActivityId.ToString(), diagnostic, StringComparison.Ordinal);
        Assert.Contains(
            $"PayloadLength = {payload.Length}",
            diagnostic,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RemoteWindowMediaFrame.MaximumPayloadBytes)]
    public void PayloadBoundaryValuesRoundTrip(int payloadLength)
    {
        byte[] payload = new byte[payloadLength];
        RemoteWindowMediaFrame frame = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Audio,
            sequence: 3,
            chunkIndex: 0,
            chunkCount: 1,
            payload);

        RemoteWindowMediaFrame decoded = RemoteWindowMediaFrameCodec.Decode(
            RemoteWindowMediaFrameCodec.Encode(frame),
            SessionId,
            ActivityId);

        Assert.Equal(payloadLength, decoded.PayloadLength);
        Assert.Equal(payload, decoded.ExportPayload());
    }

    private static byte[] EncodeCursor() => RemoteWindowMediaFrameCodec.Encode(
        RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x01, 0x02, 0x03]));
}
