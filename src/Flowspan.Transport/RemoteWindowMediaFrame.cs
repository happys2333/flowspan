using System.Buffers.Binary;
using Flowspan.Domain;

namespace Flowspan.Transport;

public enum RemoteWindowMediaKind : byte
{
    Video = 1,
    Audio = 2,
    Cursor = 3,
}

public sealed class RemoteWindowMediaFrame
{
    public const int MaximumPayloadBytes = 64 * 1024;
    public const ushort MaximumVideoChunks = 16;
    private readonly byte[] payload;

    private RemoteWindowMediaFrame(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        RemoteWindowMediaKind kind,
        ulong sequence,
        ushort chunkIndex,
        ushort chunkCount,
        byte[] payload)
    {
        SessionId = sessionId;
        ActivityId = activityId;
        Kind = kind;
        Sequence = sequence;
        ChunkIndex = chunkIndex;
        ChunkCount = chunkCount;
        this.payload = payload;
    }

    public ActivityId ActivityId { get; }

    public ushort ChunkCount { get; }

    public ushort ChunkIndex { get; }

    public RemoteWindowMediaKind Kind { get; }

    public int PayloadLength => payload.Length;

    public ulong Sequence { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowMediaFrame Create(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        RemoteWindowMediaKind kind,
        ulong sequence,
        ushort chunkIndex,
        ushort chunkCount,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfZero(sequence);
        if (payload.Length > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"A Remote Window media payload cannot exceed {MaximumPayloadBytes} bytes.");
        }

        ValidateChunkShape(kind, chunkIndex, chunkCount);
        return new RemoteWindowMediaFrame(
            sessionId,
            activityId,
            kind,
            sequence,
            chunkIndex,
            chunkCount,
            payload.ToArray());
    }

    public byte[] ExportPayload() => payload.ToArray();

    public override string ToString() =>
        $"{nameof(RemoteWindowMediaFrame)} {{ Kind = {Kind}, "
        + $"Sequence = {Sequence}, PayloadLength = {PayloadLength} }}";

    internal void CopyPayloadTo(Span<byte> destination) => payload.CopyTo(destination);

    internal RemoteWindowMediaFrame Clone() => new(
        SessionId,
        ActivityId,
        Kind,
        Sequence,
        ChunkIndex,
        ChunkCount,
        payload.ToArray());

    private static void ValidateChunkShape(
        RemoteWindowMediaKind kind,
        ushort chunkIndex,
        ushort chunkCount)
    {
        if (kind == RemoteWindowMediaKind.Video)
        {
            if (chunkCount is < 1 or > MaximumVideoChunks || chunkIndex >= chunkCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkCount),
                    "A video frame must contain 1 to 16 zero-based chunks.");
            }

            return;
        }

        if (chunkIndex != 0 || chunkCount != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkCount),
                "Audio and cursor frames must contain exactly one chunk.");
        }
    }
}

public static class RemoteWindowMediaFrameCodec
{
    public const int HeaderBytes = 56;
    private const byte FormatVersion = 1;
    private const int ActivityIdOffset = 24;
    private const int ChunkCountOffset = 50;
    private const int ChunkIndexOffset = 48;
    private const int FlagsOffset = 6;
    private const int FormatOffset = 4;
    private const int KindOffset = 5;
    private const int PayloadLengthOffset = 52;
    private const int ReservedOffset = 7;
    private const int SequenceOffset = 40;
    private const int SessionIdOffset = 8;
    private const int IdentifierBytes = 16;
    private static ReadOnlySpan<byte> Magic => "FSRM"u8;

    public static byte[] Encode(RemoteWindowMediaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        byte[] encoded = GC.AllocateUninitializedArray<byte>(
            checked(HeaderBytes + frame.PayloadLength));
        Magic.CopyTo(encoded);
        encoded[FormatOffset] = FormatVersion;
        encoded[KindOffset] = (byte)frame.Kind;
        encoded[FlagsOffset] = 0;
        encoded[ReservedOffset] = 0;
        WriteGuid(encoded.AsSpan(SessionIdOffset, IdentifierBytes), frame.SessionId.Value);
        WriteGuid(encoded.AsSpan(ActivityIdOffset, IdentifierBytes), frame.ActivityId.Value);
        BinaryPrimitives.WriteUInt64BigEndian(
            encoded.AsSpan(SequenceOffset, sizeof(ulong)),
            frame.Sequence);
        BinaryPrimitives.WriteUInt16BigEndian(
            encoded.AsSpan(ChunkIndexOffset, sizeof(ushort)),
            frame.ChunkIndex);
        BinaryPrimitives.WriteUInt16BigEndian(
            encoded.AsSpan(ChunkCountOffset, sizeof(ushort)),
            frame.ChunkCount);
        BinaryPrimitives.WriteUInt32BigEndian(
            encoded.AsSpan(PayloadLengthOffset, sizeof(uint)),
            checked((uint)frame.PayloadLength));
        frame.CopyPayloadTo(encoded.AsSpan(HeaderBytes));
        return encoded;
    }

    public static RemoteWindowMediaFrame Decode(
        ReadOnlySpan<byte> encoded,
        RemoteWindowSessionId expectedSessionId,
        ActivityId expectedActivityId)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        ArgumentNullException.ThrowIfNull(expectedActivityId);
        if (encoded.Length < HeaderBytes)
        {
            throw new InvalidDataException("The Remote Window media frame is truncated.");
        }

        if (!encoded[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The Remote Window media frame magic is invalid.");
        }

        if (encoded[FormatOffset] != FormatVersion)
        {
            throw new InvalidDataException("The Remote Window media frame format is unsupported.");
        }

        if (encoded[FlagsOffset] != 0 || encoded[ReservedOffset] != 0)
        {
            throw new InvalidDataException(
                "The Remote Window media frame contains unsupported flags.");
        }

        RemoteWindowMediaKind kind = (RemoteWindowMediaKind)encoded[KindOffset];
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("The Remote Window media kind is unsupported.");
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(
            encoded.Slice(PayloadLengthOffset, sizeof(uint)));
        if (payloadLength > RemoteWindowMediaFrame.MaximumPayloadBytes
            || encoded.Length != HeaderBytes + payloadLength)
        {
            throw new InvalidDataException(
                "The Remote Window media payload length is invalid.");
        }

        RemoteWindowSessionId sessionId;
        ActivityId activityId;
        try
        {
            sessionId = RemoteWindowSessionId.From(
                new Guid(encoded.Slice(SessionIdOffset, IdentifierBytes), bigEndian: true));
            activityId = ActivityId.From(
                new Guid(encoded.Slice(ActivityIdOffset, IdentifierBytes), bigEndian: true));
        }
        catch (ArgumentException failure)
        {
            throw new InvalidDataException(
                "The Remote Window media frame contains an invalid identifier.",
                failure);
        }

        if (sessionId != expectedSessionId || activityId != expectedActivityId)
        {
            throw new InvalidDataException(
                "The Remote Window media frame binding does not match this channel.");
        }

        ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(
            encoded.Slice(SequenceOffset, sizeof(ulong)));
        ushort chunkIndex = BinaryPrimitives.ReadUInt16BigEndian(
            encoded.Slice(ChunkIndexOffset, sizeof(ushort)));
        ushort chunkCount = BinaryPrimitives.ReadUInt16BigEndian(
            encoded.Slice(ChunkCountOffset, sizeof(ushort)));
        try
        {
            return RemoteWindowMediaFrame.Create(
                sessionId,
                activityId,
                kind,
                sequence,
                chunkIndex,
                chunkCount,
                encoded.Slice(HeaderBytes, checked((int)payloadLength)));
        }
        catch (ArgumentException failure)
        {
            throw new InvalidDataException(
                "The Remote Window media frame shape is invalid.",
                failure);
        }
    }

    private static void WriteGuid(Span<byte> destination, Guid value)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out int bytesWritten)
            || bytesWritten != IdentifierBytes)
        {
            throw new InvalidOperationException("A GUID could not be encoded.");
        }
    }
}
