using System.Security.Cryptography;
using Flowspan.Domain;

namespace Flowspan.Transport;

public sealed class RemoteWindowVideoFrameAssembly : IDisposable
{
    private byte[]? payload;

    internal RemoteWindowVideoFrameAssembly(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        ulong lastSequence,
        byte[] payload)
    {
        SessionId = sessionId;
        ActivityId = activityId;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
        this.payload = payload;
    }

    public ActivityId ActivityId { get; }

    public ulong FirstSequence { get; }

    public ulong LastSequence { get; }

    public ReadOnlyMemory<byte> Payload => Volatile.Read(ref payload)
        ?? throw new ObjectDisposedException(nameof(RemoteWindowVideoFrameAssembly));

    public int PayloadLength => Payload.Length;

    public RemoteWindowSessionId SessionId { get; }

    public byte[] ExportPayload() => Payload.ToArray();

    public void Dispose()
    {
        byte[]? owned = Interlocked.Exchange(ref payload, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() =>
        $"{nameof(RemoteWindowVideoFrameAssembly)} {{ PayloadLength = {PayloadLength}, FirstSequence = {FirstSequence}, LastSequence = {LastSequence} }}";
}

public sealed class RemoteWindowVideoFrameAssembler : IDisposable
{
    private readonly ActivityId activityId;
    private readonly IRemoteWindowVideoBufferOperations buffers;
    private readonly Lock gate = new();
    private readonly RemoteWindowSessionId sessionId;
    private readonly List<byte[]> chunks = [];
    private ushort expectedChunkCount;
    private ushort nextChunkIndex;
    private ulong firstSequence;
    private ulong lastSequence;
    private int payloadBytes;
    private bool disposed;

    public RemoteWindowVideoFrameAssembler(
        RemoteWindowSessionId sessionId,
        ActivityId activityId) : this(
        sessionId,
        activityId,
        RemoteWindowVideoBufferOperations.Instance)
    {
    }

    internal RemoteWindowVideoFrameAssembler(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        IRemoteWindowVideoBufferOperations buffers)
    {
        this.sessionId = sessionId
            ?? throw new ArgumentNullException(nameof(sessionId));
        this.activityId = activityId
            ?? throw new ArgumentNullException(nameof(activityId));
        this.buffers = buffers
            ?? throw new ArgumentNullException(nameof(buffers));
    }

    public RemoteWindowVideoFrameAssembly? Add(RemoteWindowMediaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            lock (gate)
            {
                try
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (frame.SessionId != sessionId
                    || frame.ActivityId != activityId
                    || frame.Kind != RemoteWindowMediaKind.Video
                    || frame.PayloadLength == 0)
                    {
                        return Reject("The Remote Window video chunk binding is invalid.");
                    }

                    if (frame.ChunkIndex == 0)
                    {
                        if (chunks.Count != 0)
                        {
                            return Reject(
                                "A new Remote Window video frame cannot replace an incomplete frame.");
                        }

                        expectedChunkCount = frame.ChunkCount;
                        firstSequence = frame.Sequence;
                        lastSequence = frame.Sequence;
                    }
                    else if (chunks.Count == 0)
                    {
                        return Reject(
                            "A Remote Window video frame must begin with chunk zero.");
                    }

                    if (frame.ChunkCount != expectedChunkCount
                        || frame.ChunkIndex != nextChunkIndex
                        || (frame.ChunkIndex != 0
                            && (lastSequence == ulong.MaxValue
                                || frame.Sequence != lastSequence + 1)))
                    {
                        return Reject(
                            "The Remote Window video chunks are not strictly continuous.");
                    }

                    int nextPayloadBytes;
                    try
                    {
                        nextPayloadBytes = checked(payloadBytes + frame.PayloadLength);
                    }
                    catch (OverflowException exception)
                    {
                        ClearPartial();
                        throw new InvalidDataException(
                            "The Remote Window video frame length overflowed.",
                            exception);
                    }

                    if (nextPayloadBytes > RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes)
                    {
                        return Reject(
                            "The Remote Window video frame exceeds the logical frame limit.");
                    }

                    byte[]? ownedChunk = frame.TakePayloadOwnership();
                    try
                    {
                        buffers.Add(chunks, ownedChunk);
                        ownedChunk = null;
                    }
                    catch
                    {
                        ClearPartial();
                        if (ownedChunk is not null)
                        {
                            CryptographicOperations.ZeroMemory(ownedChunk);
                        }

                        throw;
                    }

                    payloadBytes = nextPayloadBytes;
                    lastSequence = frame.Sequence;
                    nextChunkIndex++;
                    if (nextChunkIndex != expectedChunkCount)
                    {
                        return null;
                    }

                    byte[]? completed = null;
                    try
                    {
                        completed = buffers.Allocate(payloadBytes);
                        if (completed.Length != payloadBytes)
                        {
                            throw new InvalidOperationException(
                                "The Remote Window video allocator returned an invalid buffer.");
                        }

                        var offset = 0;
                        foreach (byte[] chunk in chunks)
                        {
                            buffers.Copy(
                                chunk,
                                completed.AsSpan(offset, chunk.Length));
                            offset = checked(offset + chunk.Length);
                        }

                        var assembly = new RemoteWindowVideoFrameAssembly(
                            sessionId,
                            activityId,
                            firstSequence,
                            lastSequence,
                            completed);
                        completed = null;
                        return assembly;
                    }
                    finally
                    {
                        if (completed is not null)
                        {
                            CryptographicOperations.ZeroMemory(completed);
                        }

                        ClearPartial();
                    }
                }
                catch
                {
                    ClearPartial();
                    throw;
                }
            }
        }
        finally
        {
            frame.Dispose();
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
            ClearPartial();
        }
    }

    private RemoteWindowVideoFrameAssembly? Reject(string message)
    {
        ClearPartial();
        throw new InvalidDataException(message);
    }

    private void ClearPartial()
    {
        for (var index = chunks.Count - 1; index >= 0; index--)
        {
            CryptographicOperations.ZeroMemory(chunks[index]);
        }

        chunks.Clear();
        expectedChunkCount = 0;
        nextChunkIndex = 0;
        firstSequence = 0;
        lastSequence = 0;
        payloadBytes = 0;
    }
}
