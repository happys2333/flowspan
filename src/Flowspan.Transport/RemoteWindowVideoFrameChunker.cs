using System.Collections;
using System.Security.Cryptography;
using Flowspan.Domain;

namespace Flowspan.Transport;

internal interface IRemoteWindowVideoBufferOperations
{
    public byte[] Allocate(int length);

    public void Add(List<byte[]> destination, byte[] item);

    public void Copy(ReadOnlySpan<byte> source, Span<byte> destination);
}

internal sealed class RemoteWindowVideoBufferOperations :
    IRemoteWindowVideoBufferOperations
{
    internal static RemoteWindowVideoBufferOperations Instance { get; } = new();

    private RemoteWindowVideoBufferOperations()
    {
    }

    public byte[] Allocate(int length) =>
        GC.AllocateUninitializedArray<byte>(length);

    public void Add(List<byte[]> destination, byte[] item) => destination.Add(item);

    public void Copy(ReadOnlySpan<byte> source, Span<byte> destination) =>
        source.CopyTo(destination);
}

public sealed class RemoteWindowVideoFrameChunks :
    IReadOnlyList<RemoteWindowMediaFrame>,
    IDisposable
{
    private readonly RemoteWindowMediaFrame?[] chunks;
    private readonly Lock gate = new();
    private bool disposed;

    internal RemoteWindowVideoFrameChunks(RemoteWindowMediaFrame?[] chunks)
    {
        this.chunks = chunks;
    }

    public int Count => chunks.Length;

    public RemoteWindowMediaFrame this[int index]
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return chunks[index]
                    ?? throw new InvalidOperationException(
                        "The Remote Window video chunk was already consumed.");
            }
        }
    }

    public RemoteWindowMediaFrame Take(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RemoteWindowMediaFrame? chunk = chunks[index];
            if (chunk is null)
            {
                throw new InvalidOperationException(
                    "The Remote Window video chunk was already consumed.");
            }

            chunks[index] = null;
            return chunk;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            for (var index = chunks.Length - 1; index >= 0; index--)
            {
                chunks[index]?.Dispose();
                chunks[index] = null;
            }
        }
    }

    public IEnumerator<RemoteWindowMediaFrame> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class RemoteWindowVideoFrameChunker
{
    public const int MaximumLogicalFrameBytes = 1024 * 1024;

    public static RemoteWindowVideoFrameChunks Chunk(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        ReadOnlySpan<byte> payload) => Chunk(
        sessionId,
        activityId,
        firstSequence,
        payload,
        RemoteWindowVideoBufferOperations.Instance);

    internal static RemoteWindowVideoFrameChunks Chunk(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        ReadOnlySpan<byte> payload,
        IRemoteWindowVideoBufferOperations buffers)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(buffers);
        ArgumentOutOfRangeException.ThrowIfZero(firstSequence);
        if (payload.Length is < 1 or > MaximumLogicalFrameBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"A Remote Window video frame must contain 1 to {MaximumLogicalFrameBytes} bytes.");
        }

        int chunkCount = checked(
            (payload.Length + RemoteWindowMediaFrame.MaximumPayloadBytes - 1)
            / RemoteWindowMediaFrame.MaximumPayloadBytes);
        if (chunkCount > RemoteWindowMediaFrame.MaximumVideoChunks
            || firstSequence > ulong.MaxValue - checked((ulong)(chunkCount - 1)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstSequence),
                "The Remote Window video chunk sequence range is invalid.");
        }

        var chunks = new RemoteWindowMediaFrame?[chunkCount];
        byte[]? ownedPayload = null;
        var completed = false;
        try
        {
            for (var index = 0; index < chunkCount; index++)
            {
                int offset = checked(index * RemoteWindowMediaFrame.MaximumPayloadBytes);
                int length = Math.Min(
                    RemoteWindowMediaFrame.MaximumPayloadBytes,
                    payload.Length - offset);
                ownedPayload = buffers.Allocate(length);
                if (ownedPayload.Length != length)
                {
                    throw new InvalidOperationException(
                        "The Remote Window video allocator returned an invalid buffer.");
                }

                buffers.Copy(
                    payload.Slice(offset, length),
                    ownedPayload);
                chunks[index] = RemoteWindowMediaFrame.TakeOwnership(
                    sessionId,
                    activityId,
                    RemoteWindowMediaKind.Video,
                    firstSequence + checked((ulong)index),
                    checked((ushort)index),
                    checked((ushort)chunkCount),
                    ownedPayload);
                ownedPayload = null;
            }

            var result = new RemoteWindowVideoFrameChunks(chunks);
            completed = true;
            return result;
        }
        finally
        {
            if (ownedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(ownedPayload);
            }

            if (!completed)
            {
                for (var index = chunks.Length - 1; index >= 0; index--)
                {
                    chunks[index]?.Dispose();
                }
            }
        }
    }
}
