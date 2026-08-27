using Flowspan.Domain;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowVideoFrameAssemblerTests
{
    [Fact]
    public void AddingAChunkConsumesTheInputFrame()
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        RemoteWindowMediaFrame frame = CreateVideo(
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [1, 2, 3, 4]);

        using RemoteWindowVideoFrameAssembly completed = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(assembler.Add(frame));

        Assert.Equal([1, 2, 3, 4], completed.Payload.ToArray());
        Assert.Throws<ObjectDisposedException>(() => _ = frame.PayloadLength);
        Assert.Throws<ObjectDisposedException>(() => frame.ExportPayload());
    }

    [Fact]
    public void ListAddFailureZerosEveryOwnedChunkAndClearsPartialState()
    {
        var buffers = new FaultingBufferOperations
        {
            FailAddCall = 2,
        };
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId(),
            buffers);
        byte[] firstPayload = [1, 2, 3];
        byte[] secondPayload = [4, 5, 6];
        RemoteWindowMediaFrame first = TakeVideo(
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 3,
            firstPayload);
        RemoteWindowMediaFrame second = TakeVideo(
            sequence: 2,
            chunkIndex: 1,
            chunkCount: 3,
            secondPayload);
        Assert.Null(assembler.Add(first));

        Assert.Throws<InjectedBufferFailureException>(() => assembler.Add(second));

        Assert.All(firstPayload, static value => Assert.Equal(0, value));
        Assert.All(secondPayload, static value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = second.PayloadLength);
        using RemoteWindowVideoFrameAssembly recovered = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(
                assembler.Add(CreateVideo(20, 0, 1, [9])));
        Assert.Equal([9], recovered.Payload.ToArray());
    }

    [Fact]
    public void CompletionAllocationFailureZerosOwnedChunksAndClearsPartialState()
    {
        var buffers = new FaultingBufferOperations
        {
            FailAllocationCall = 1,
        };
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId(),
            buffers);
        byte[] payload = [1, 2, 3, 4];
        RemoteWindowMediaFrame frame = TakeVideo(1, 0, 1, payload);

        Assert.Throws<InjectedBufferFailureException>(() => assembler.Add(frame));

        Assert.All(payload, static value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = frame.PayloadLength);
        using RemoteWindowVideoFrameAssembly recovered = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(
                assembler.Add(CreateVideo(20, 0, 1, [9])));
        Assert.Equal([9], recovered.Payload.ToArray());
    }

    [Fact]
    public void CompletionCopyFailureZerosChunksAndCompletionBuffer()
    {
        var buffers = new FaultingBufferOperations
        {
            FailCopyCall = 2,
        };
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId(),
            buffers);
        byte[] firstPayload = [1, 2, 3];
        byte[] secondPayload = [4, 5, 6];
        Assert.Null(assembler.Add(TakeVideo(1, 0, 2, firstPayload)));

        Assert.Throws<InjectedBufferFailureException>(() =>
            assembler.Add(TakeVideo(2, 1, 2, secondPayload)));

        Assert.All(firstPayload, static value => Assert.Equal(0, value));
        Assert.All(secondPayload, static value => Assert.Equal(0, value));
        byte[] completed = Assert.Single(buffers.Allocations);
        Assert.All(completed, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void ConsumedInputFailureZerosExistingPartialState()
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        byte[] firstPayload = [1, 2, 3];
        Assert.Null(assembler.Add(TakeVideo(1, 0, 2, firstPayload)));
        RemoteWindowMediaFrame disposed = CreateVideo(2, 1, 2, [4, 5, 6]);
        disposed.Dispose();

        Assert.Throws<ObjectDisposedException>(() => assembler.Add(disposed));

        Assert.All(firstPayload, static value => Assert.Equal(0, value));
        using RemoteWindowVideoFrameAssembly recovered = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(
                assembler.Add(CreateVideo(20, 0, 1, [9])));
        Assert.Equal([9], recovered.Payload.ToArray());
    }

    [Fact]
    public void ContinuousBoundChunksCompleteOneOwnedLogicalFrame()
    {
        RemoteWindowSessionId sessionId = CreateSessionId();
        ActivityId activityId = CreateActivityId();
        byte[] jpeg = Enumerable.Range(
                0,
                RemoteWindowMediaFrame.MaximumPayloadBytes + 17)
            .Select(static index => checked((byte)(index % 251)))
            .ToArray();
        using RemoteWindowVideoFrameChunks chunks =
            RemoteWindowVideoFrameChunker.Chunk(
                sessionId,
                activityId,
                firstSequence: 91,
                jpeg);
        using var assembler = new RemoteWindowVideoFrameAssembler(
            sessionId,
            activityId);

        Assert.Null(assembler.Add(chunks.Take(0)));
        using RemoteWindowVideoFrameAssembly completed = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(assembler.Add(chunks.Take(1)));

        Assert.Same(sessionId, completed.SessionId);
        Assert.Same(activityId, completed.ActivityId);
        Assert.Equal<ulong>(91, completed.FirstSequence);
        Assert.Equal<ulong>(92, completed.LastSequence);
        Assert.Equal(jpeg.Length, completed.PayloadLength);
        Assert.Equal(jpeg, completed.Payload.ToArray());
        byte[] exported = completed.ExportPayload();
        exported[0] ^= 0xff;
        Assert.Equal(jpeg[0], completed.Payload.Span[0]);
    }

    [Fact]
    public void CompletedOwnerDisposalIsIdempotentAndZerosBorrowedMemory()
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        RemoteWindowMediaFrame frame = CreateVideo(
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [1, 2, 3, 4]);
        RemoteWindowVideoFrameAssembly completed = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(assembler.Add(frame));
        ReadOnlyMemory<byte> borrowed = completed.Payload;

        completed.Dispose();
        completed.Dispose();

        Assert.All(borrowed.ToArray(), static value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = completed.Payload);
        Assert.Throws<ObjectDisposedException>(() => completed.ExportPayload());
    }

    [Fact]
    public void NewFirstChunkRejectsAndClearsAnIncompleteFrame()
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        byte[] firstPayload = [1, 2, 3];
        byte[] replacementPayload = [4, 5];
        RemoteWindowMediaFrame first = TakeVideo(10, 0, 2, firstPayload);
        RemoteWindowMediaFrame replacement = TakeVideo(
            20,
            0,
            2,
            replacementPayload);
        Assert.Null(assembler.Add(first));

        Assert.Throws<InvalidDataException>(() => assembler.Add(replacement));

        Assert.All(firstPayload, static value => Assert.Equal(0, value));
        Assert.All(replacementPayload, static value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = first.PayloadLength);
        Assert.Throws<ObjectDisposedException>(() => _ = replacement.PayloadLength);
        using RemoteWindowVideoFrameAssembly recovered = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(
                assembler.Add(CreateVideo(30, 0, 1, [6, 7])));

        Assert.Equal<ulong>(30, recovered.FirstSequence);
        Assert.Equal<ulong>(30, recovered.LastSequence);
        Assert.Equal([6, 7], recovered.Payload.ToArray());
    }

    [Fact]
    public void WrongBindingAndNonVideoFramesFailClosed()
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        RemoteWindowMediaFrame wrongSession = RemoteWindowMediaFrame.Create(
            RemoteWindowSessionId.From(Guid.NewGuid()),
            CreateActivityId(),
            RemoteWindowMediaKind.Video,
            1,
            0,
            1,
            [1]);
        RemoteWindowMediaFrame wrongActivity = RemoteWindowMediaFrame.Create(
            CreateSessionId(),
            ActivityId.From(Guid.NewGuid()),
            RemoteWindowMediaKind.Video,
            1,
            0,
            1,
            [1]);
        RemoteWindowMediaFrame audio = RemoteWindowMediaFrame.Create(
            CreateSessionId(),
            CreateActivityId(),
            RemoteWindowMediaKind.Audio,
            1,
            0,
            1,
            [1]);

        Assert.Throws<InvalidDataException>(() => assembler.Add(wrongSession));
        Assert.Throws<InvalidDataException>(() => assembler.Add(wrongActivity));
        Assert.Throws<InvalidDataException>(() => assembler.Add(audio));
    }

    [Theory]
    [InlineData(3, 2, 3)]
    [InlineData(3, 1, 4)]
    [InlineData(4, 1, 3)]
    public void WrongIndexSequenceOrChunkCountFailsAndClearsPartial(
        ulong nextSequence,
        ushort nextIndex,
        ushort nextCount)
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        Assert.Null(assembler.Add(CreateVideo(1, 0, 3, [1])));

        Assert.Throws<InvalidDataException>(() =>
            assembler.Add(CreateVideo(nextSequence, nextIndex, nextCount, [2])));

        using RemoteWindowVideoFrameAssembly recovered = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(
                assembler.Add(CreateVideo(20, 0, 1, [9])));
        Assert.Equal([9], recovered.Payload.ToArray());
    }

    [Fact]
    public void NonInitialChunkWithoutPartialFailsClosed()
    {
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());

        Assert.Throws<InvalidDataException>(() =>
            assembler.Add(CreateVideo(2, 1, 2, [1])));
    }

    [Fact]
    public void AssemblerDisposalClearsPartialAndRejectsFurtherFrames()
    {
        var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        Assert.Null(assembler.Add(CreateVideo(1, 0, 2, [1, 2, 3])));

        assembler.Dispose();
        assembler.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            assembler.Add(CreateVideo(2, 1, 2, [4])));
    }

    [Fact]
    public void MaximumLogicalFrameReassemblesAtTheExactBoundary()
    {
        byte[] jpeg = new byte[
            RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes];
        jpeg[0] = 1;
        jpeg[^1] = 2;
        using RemoteWindowVideoFrameChunks chunks =
            RemoteWindowVideoFrameChunker.Chunk(
                CreateSessionId(),
                CreateActivityId(),
                firstSequence: 100,
                jpeg);
        using var assembler = new RemoteWindowVideoFrameAssembler(
            CreateSessionId(),
            CreateActivityId());
        RemoteWindowVideoFrameAssembly? completed = null;

        for (var index = 0; index < chunks.Count; index++)
        {
            completed = assembler.Add(chunks.Take(index));
        }

        using RemoteWindowVideoFrameAssembly assembly = Assert.IsType<
            RemoteWindowVideoFrameAssembly>(completed);
        Assert.Equal(jpeg.Length, assembly.PayloadLength);
        Assert.Equal(1, assembly.Payload.Span[0]);
        Assert.Equal(2, assembly.Payload.Span[^1]);
        Assert.Equal<ulong>(100, assembly.FirstSequence);
        Assert.Equal<ulong>(115, assembly.LastSequence);
    }

    private static RemoteWindowMediaFrame CreateVideo(
        ulong sequence,
        ushort chunkIndex,
        ushort chunkCount,
        ReadOnlySpan<byte> payload) => RemoteWindowMediaFrame.Create(
            CreateSessionId(),
            CreateActivityId(),
            RemoteWindowMediaKind.Video,
            sequence,
            chunkIndex,
            chunkCount,
            payload);

    private static RemoteWindowMediaFrame TakeVideo(
        ulong sequence,
        ushort chunkIndex,
        ushort chunkCount,
        byte[] ownedPayload) => RemoteWindowMediaFrame.TakeOwnership(
            CreateSessionId(),
            CreateActivityId(),
            RemoteWindowMediaKind.Video,
            sequence,
            chunkIndex,
            chunkCount,
            ownedPayload);

    private static ActivityId CreateActivityId() => ActivityId.From(
        Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static RemoteWindowSessionId CreateSessionId() => RemoteWindowSessionId.From(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private sealed class FaultingBufferOperations : IRemoteWindowVideoBufferOperations
    {
        private int addCalls;
        private int allocationCalls;
        private int copyCalls;

        public List<byte[]> Allocations { get; } = [];

        public int? FailAddCall { get; init; }

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

        public void Add(List<byte[]> destination, byte[] item)
        {
            addCalls++;
            destination.Add(item);
            if (addCalls == FailAddCall)
            {
                throw new InjectedBufferFailureException();
            }
        }

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
