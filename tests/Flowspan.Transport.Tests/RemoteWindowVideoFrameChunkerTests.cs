using Flowspan.Domain;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowVideoFrameChunkerTests
{
    [Fact]
    public void ChunkBatchDisposalZerosEveryOwnedPayload()
    {
        var buffers = new TrackingBufferOperations();
        RemoteWindowVideoFrameChunks chunks = RemoteWindowVideoFrameChunker.Chunk(
            CreateSessionId(),
            CreateActivityId(),
            firstSequence: 1,
            Enumerable.Repeat(
                    (byte)0x5a,
                    RemoteWindowMediaFrame.MaximumPayloadBytes + 1)
                .ToArray(),
            buffers);
        Assert.Equal(2, buffers.Allocations.Count);

        chunks.Dispose();
        chunks.Dispose();

        Assert.All(
            buffers.Allocations,
            static allocation => Assert.All(
                allocation,
                static value => Assert.Equal(0, value)));
        Assert.Throws<ObjectDisposedException>(() => _ = chunks[0]);
    }

    [Fact]
    public void AllocationFailureZerosChunksAlreadyOwnedByTheBatch()
    {
        var buffers = new TrackingBufferOperations
        {
            FailAllocationCall = 2,
        };
        byte[] jpeg = Enumerable.Repeat(
                (byte)0x5a,
                RemoteWindowMediaFrame.MaximumPayloadBytes + 1)
            .ToArray();

        Assert.Throws<InjectedBufferFailureException>(() =>
            RemoteWindowVideoFrameChunker.Chunk(
                CreateSessionId(),
                CreateActivityId(),
                firstSequence: 1,
                jpeg,
                buffers));

        byte[] firstChunk = Assert.Single(buffers.Allocations);
        Assert.All(firstChunk, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void CopyFailureZerosCurrentAndPreviouslyOwnedChunks()
    {
        var buffers = new TrackingBufferOperations
        {
            FailCopyCall = 2,
        };
        byte[] jpeg = Enumerable.Repeat(
                (byte)0x5a,
                RemoteWindowMediaFrame.MaximumPayloadBytes + 1)
            .ToArray();

        Assert.Throws<InjectedBufferFailureException>(() =>
            RemoteWindowVideoFrameChunker.Chunk(
                CreateSessionId(),
                CreateActivityId(),
                firstSequence: 1,
                jpeg,
                buffers));

        Assert.Equal(2, buffers.Allocations.Count);
        Assert.All(
            buffers.Allocations,
            static allocation => Assert.All(
                allocation,
                static value => Assert.Equal(0, value)));
    }

    [Fact]
    public void TakeTransfersOneChunkOutOfTheBatchUntilFrameDisposal()
    {
        var buffers = new TrackingBufferOperations();
        var chunks = RemoteWindowVideoFrameChunker.Chunk(
            CreateSessionId(),
            CreateActivityId(),
            firstSequence: 1,
            new byte[] { 1, 2, 3, 4 },
            buffers);
        byte[] ownedPayload = Assert.Single(buffers.Allocations);

        RemoteWindowMediaFrame frame = chunks.Take(0);
        Assert.Throws<InvalidOperationException>(() => chunks.Take(0));
        chunks.Dispose();

        Assert.Equal([1, 2, 3, 4], ownedPayload);
        frame.Dispose();
        Assert.All(ownedPayload, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void MaximumLogicalFrameUsesSixteenBoundedChunksAndContinuousSequences()
    {
        RemoteWindowSessionId sessionId = RemoteWindowSessionId.From(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        ActivityId activityId = ActivityId.From(
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        byte[] jpeg = Enumerable.Range(
                0,
                RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes)
            .Select(static index => checked((byte)(index % 251)))
            .ToArray();

        using RemoteWindowVideoFrameChunks chunks =
            RemoteWindowVideoFrameChunker.Chunk(
                sessionId,
                activityId,
                firstSequence: 41,
                jpeg);

        Assert.Equal(RemoteWindowMediaFrame.MaximumVideoChunks, chunks.Count);
        Assert.All(chunks, chunk =>
        {
            Assert.Same(sessionId, chunk.SessionId);
            Assert.Same(activityId, chunk.ActivityId);
            Assert.Equal(RemoteWindowMediaKind.Video, chunk.Kind);
            Assert.Equal(RemoteWindowMediaFrame.MaximumPayloadBytes, chunk.PayloadLength);
            Assert.Equal<ushort>(RemoteWindowMediaFrame.MaximumVideoChunks, chunk.ChunkCount);
        });
        Assert.Equal(
            Enumerable.Range(0, chunks.Count).Select(static index => (ushort)index),
            chunks.Select(static chunk => chunk.ChunkIndex));
        Assert.Equal(
            Enumerable.Range(41, chunks.Count).Select(static value => (ulong)value),
            chunks.Select(static chunk => chunk.Sequence));
        Assert.Equal(jpeg, chunks.SelectMany(static chunk => chunk.ExportPayload()));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(RemoteWindowMediaFrame.MaximumPayloadBytes, 1)]
    [InlineData(RemoteWindowMediaFrame.MaximumPayloadBytes + 1, 2)]
    public void LogicalFrameUsesTheMinimumChunkCount(int payloadBytes, int expectedChunks)
    {
        byte[] jpeg = new byte[payloadBytes];

        using RemoteWindowVideoFrameChunks chunks =
            RemoteWindowVideoFrameChunker.Chunk(
                CreateSessionId(),
                CreateActivityId(),
                firstSequence: 1,
                jpeg);

        Assert.Equal(expectedChunks, chunks.Count);
        Assert.Equal(payloadBytes, chunks.Sum(static chunk => chunk.PayloadLength));
    }

    [Fact]
    public void EmptyAndOversizedLogicalFramesAreRejected()
    {
        RemoteWindowSessionId sessionId = CreateSessionId();
        ActivityId activityId = CreateActivityId();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteWindowVideoFrameChunker.Chunk(
                sessionId,
                activityId,
                firstSequence: 1,
                ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteWindowVideoFrameChunker.Chunk(
                sessionId,
                activityId,
                firstSequence: 1,
                new byte[RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes + 1]));
    }

    [Fact]
    public void SequenceRangeMustFitWithoutWrapping()
    {
        byte[] twoChunks = new byte[RemoteWindowMediaFrame.MaximumPayloadBytes + 1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteWindowVideoFrameChunker.Chunk(
                CreateSessionId(),
                CreateActivityId(),
                ulong.MaxValue,
                twoChunks));
    }

    [Fact]
    public void ChunksOwnDefensivePayloadCopies()
    {
        byte[] jpeg = [1, 2, 3, 4];

        using RemoteWindowVideoFrameChunks chunks =
            RemoteWindowVideoFrameChunker.Chunk(
                CreateSessionId(),
                CreateActivityId(),
                firstSequence: 1,
                jpeg);
        jpeg.AsSpan().Clear();

        Assert.Equal([1, 2, 3, 4], chunks[0].ExportPayload());
    }

    private static ActivityId CreateActivityId() => ActivityId.From(
        Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static RemoteWindowSessionId CreateSessionId() => RemoteWindowSessionId.From(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private sealed class TrackingBufferOperations : IRemoteWindowVideoBufferOperations
    {
        private int allocationCalls;
        private int copyCalls;

        public List<byte[]> Allocations { get; } = [];

        public int? FailAllocationCall { get; init; }

        public int? FailCopyCall { get; init; }

        public byte[] Allocate(int length)
        {
            allocationCalls++;
            if (allocationCalls == FailAllocationCall)
            {
                throw new InjectedBufferFailureException();
            }

            byte[] allocation = GC.AllocateUninitializedArray<byte>(length);
            Allocations.Add(allocation);
            return allocation;
        }

        public void Add(List<byte[]> destination, byte[] item) => destination.Add(item);

        public void Copy(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            copyCalls++;
            source.CopyTo(destination);
            if (copyCalls == FailCopyCall)
            {
                throw new InjectedBufferFailureException();
            }
        }
    }

    private sealed class InjectedBufferFailureException : Exception;
}
