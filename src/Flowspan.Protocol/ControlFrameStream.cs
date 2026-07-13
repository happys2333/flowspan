using System.Buffers.Binary;

namespace Flowspan.Protocol;

public static class ControlFrameTransport
{
    private const int HeaderLength = sizeof(int);

    public static async ValueTask WriteAsync(
        Stream stream,
        ControlMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The stream must be writable.", nameof(stream));
        }

        byte[] frame = ControlMessageCodec.Encode(message);
        byte[] header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, frame.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<ControlMessage> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        byte[] header = new byte[HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int frameLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (frameLength is < 1 or > ControlMessageCodec.MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"A control frame length must be from 1 to {ControlMessageCodec.MaximumFrameBytes} bytes.");
        }

        byte[] frame = GC.AllocateUninitializedArray<byte>(frameLength);
        await stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
        return ControlMessageCodec.Decode(frame);
    }
}
