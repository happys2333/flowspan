using System.Buffers.Binary;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Protocol.Tests;

public sealed class ControlFrameTransportTests
{
    [Fact]
    public async Task PartialReadsReassembleOneBoundedFrame()
    {
        ControlMessage original = CreateMessage();
        await using var encoded = new MemoryStream();
        await ControlFrameTransport.WriteAsync(encoded, original);
        await using var chunks = new ChunkedReadStream(encoded.ToArray(), maximumChunkSize: 2);

        ControlMessage decoded = await ControlFrameTransport.ReadAsync(chunks);

        Assert.Equal(original.MessageId, decoded.MessageId);
        Assert.Equal(original.BodyDigest, decoded.BodyDigest);
    }

    [Fact]
    public async Task TruncatedFrameFailsInsteadOfReturningPartialMessage()
    {
        ControlMessage message = CreateMessage();
        await using var encoded = new MemoryStream();
        await ControlFrameTransport.WriteAsync(encoded, message);
        byte[] truncated = encoded.ToArray()[..^1];
        await using var input = new MemoryStream(truncated);

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await ControlFrameTransport.ReadAsync(input));
    }

    [Fact]
    public async Task OversizedLengthIsRejectedBeforeBodyAllocation()
    {
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(
            header,
            ControlMessageCodec.MaximumFrameBytes + 1);
        await using var input = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ControlFrameTransport.ReadAsync(input));
    }

    [Fact]
    public async Task WriterUsesFourByteBigEndianLength()
    {
        ControlMessage message = CreateMessage();
        await using var output = new MemoryStream();

        await ControlFrameTransport.WriteAsync(output, message);

        byte[] bytes = output.ToArray();
        int declaredLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, sizeof(int)));
        Assert.Equal(bytes.Length - sizeof(int), declaredLength);
    }

    private static ControlMessage CreateMessage() => ControlMessage.Create(
        new ProtocolVersion(1, 0),
        ControlMessageType.Hello,
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
        new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero),
        TimeSpan.FromSeconds(30),
        "{\"versions\":[\"1.0\"]}");

    private sealed class ChunkedReadStream(byte[] content, int maximumChunkSize) : Stream
    {
        private readonly MemoryStream inner = new(content, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maximumChunkSize));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, maximumChunkSize)], cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
