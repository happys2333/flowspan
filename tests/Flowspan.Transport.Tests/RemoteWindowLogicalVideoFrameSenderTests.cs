using Flowspan.Domain;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowLogicalVideoFrameSenderTests
{
    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DeviceId PeerId =
        DeviceId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void LogicalFrameCreateDefensivelyCopiesAndOwnedDisposalZerosPayload()
    {
        byte[] callerPayload = [1, 2, 3, 4];
        using RemoteWindowLogicalVideoFrame copied =
            RemoteWindowLogicalVideoFrame.Create(
                SessionId,
                ActivityId,
                firstSequence: 1,
                callerPayload);
        callerPayload.AsSpan().Clear();

        Assert.Equal([1, 2, 3, 4], copied.ExportPayload());

        byte[] ownedPayload = [5, 6, 7, 8];
        var owned = RemoteWindowLogicalVideoFrame.TakeOwnership(
            SessionId,
            ActivityId,
            firstSequence: 2,
            ownedPayload);
        owned.Dispose();
        owned.Dispose();

        Assert.All(ownedPayload, static value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(owned.ExportPayload);
    }

    [Fact]
    public async Task MaximumLogicalFrameSendsSixteenChunksWithOneWireFrameOutstanding()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new SteppedMediaSink(budget);
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerId,
            sink);
        (RemoteWindowLogicalVideoFrame frame, byte[] ownedPayload) = CreateOwnedFrame(
            firstSequence: 41,
            RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes,
            fill: 0x5a);

        Task<RemoteWindowLogicalVideoFrameOutcome> completion =
            sender.TakeOwnership(frame);
        for (var expectedCount = 1;
            expectedCount <= RemoteWindowMediaFrame.MaximumVideoChunks;
            expectedCount++)
        {
            await sink.WaitForSendCountAsync(expectedCount);
            RemoteWindowMediaBudgetSnapshot snapshot = budget.Snapshot;
            Assert.Equal(1, snapshot.Frames);
            Assert.Equal(RemoteWindowMediaFrame.MaximumPayloadBytes, snapshot.Bytes);
            sink.ReleaseOne();
        }

        Assert.Equal(RemoteWindowLogicalVideoFrameOutcome.Sent, await completion);
        Assert.Equal(1, sink.MaximumObservedFrames);
        Assert.Equal(
            Enumerable.Range(41, RemoteWindowMediaFrame.MaximumVideoChunks)
                .Select(static value => checked((ulong)value)),
            sink.Sends.Select(static send => send.Sequence));
        Assert.Equal(
            Enumerable.Range(0, RemoteWindowMediaFrame.MaximumVideoChunks)
                .Select(static value => checked((ushort)value)),
            sink.Sends.Select(static send => send.ChunkIndex));
        Assert.All(
            sink.Sends,
            static send => Assert.Equal<ushort>(
                RemoteWindowMediaFrame.MaximumVideoChunks,
                send.ChunkCount));
        Assert.All(ownedPayload, static value => Assert.Equal(0, value));

        await sender.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task PendingFrameReplacementIsLatestWinsAndZerosReplacedOwnerImmediately()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new SteppedMediaSink(budget);
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerId,
            sink);
        (RemoteWindowLogicalVideoFrame first, byte[] firstPayload) =
            CreateOwnedFrame(firstSequence: 1, payloadBytes: 1, fill: 0x11);
        (RemoteWindowLogicalVideoFrame replaced, byte[] replacedPayload) =
            CreateOwnedFrame(firstSequence: 2, payloadBytes: 1, fill: 0x22);
        (RemoteWindowLogicalVideoFrame latest, byte[] latestPayload) =
            CreateOwnedFrame(firstSequence: 3, payloadBytes: 1, fill: 0x33);

        Task<RemoteWindowLogicalVideoFrameOutcome> firstCompletion =
            sender.TakeOwnership(first);
        await sink.WaitForSendCountAsync(1);
        Task<RemoteWindowLogicalVideoFrameOutcome> replacedCompletion =
            sender.TakeOwnership(replaced);

        Task<RemoteWindowLogicalVideoFrameOutcome> latestCompletion =
            sender.TakeOwnership(latest);

        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Replaced,
            await replacedCompletion);
        Assert.All(replacedPayload, static value => Assert.Equal(0, value));
        Assert.Contains(latestPayload, static value => value != 0);

        sink.ReleaseOne();
        await sink.WaitForSendCountAsync(2);
        Assert.Equal<ulong>(3, sink.Sends[1].Sequence);
        sink.ReleaseOne();

        Assert.Equal(RemoteWindowLogicalVideoFrameOutcome.Sent, await firstCompletion);
        Assert.Equal(RemoteWindowLogicalVideoFrameOutcome.Sent, await latestCompletion);
        Assert.All(firstPayload, static value => Assert.Equal(0, value));
        Assert.All(latestPayload, static value => Assert.Equal(0, value));

        await sender.DisposeAsync();
    }

    [Fact]
    public async Task MidFrameSinkFailureZerosAllOwnersAndFailsPendingFrame()
    {
        const int failingChunk = 8;
        var buffers = new TrackingBufferOperations();
        var sink = new ControlledFailureMediaSink(failingChunk);
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerId,
            sink,
            buffers);
        (RemoteWindowLogicalVideoFrame current, byte[] currentPayload) =
            CreateOwnedFrame(
                firstSequence: 1,
                RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes,
                fill: 0x44);
        (RemoteWindowLogicalVideoFrame pending, byte[] pendingPayload) =
            CreateOwnedFrame(firstSequence: 100, payloadBytes: 1, fill: 0x55);

        Task<RemoteWindowLogicalVideoFrameOutcome> currentCompletion =
            sender.TakeOwnership(current);
        await sink.FailingSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Task<RemoteWindowLogicalVideoFrameOutcome> pendingCompletion =
            sender.TakeOwnership(pending);
        sink.FailNow();

        Assert.Equal(RemoteWindowLogicalVideoFrameOutcome.Failed, await currentCompletion);
        Assert.Equal(RemoteWindowLogicalVideoFrameOutcome.Failed, await pendingCompletion);
        Assert.True(sender.IsClosed);
        Assert.All(currentPayload, static value => Assert.Equal(0, value));
        Assert.All(pendingPayload, static value => Assert.Equal(0, value));
        Assert.Equal(RemoteWindowMediaFrame.MaximumVideoChunks, buffers.Allocations.Count);
        Assert.All(
            buffers.Allocations,
            static allocation => Assert.All(
                allocation,
                static value => Assert.Equal(0, value)));
        Assert.All(
            sink.Frames,
            static frame => Assert.Throws<ObjectDisposedException>(frame.ExportPayload));

        await sender.DisposeAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    public async Task StopNowAtAnyChunkReturnsBeforeNonCooperativeSendAndClearsOwnedData(
        int blockedChunk)
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var buffers = new TrackingBufferOperations();
        var sink = new NonCooperativeMediaSink(blockedChunk);
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerId,
            sink,
            buffers);
        (RemoteWindowLogicalVideoFrame current, byte[] currentPayload) =
            CreateOwnedFrame(
                firstSequence: 1,
                RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes,
                fill: 0x66);
        (RemoteWindowLogicalVideoFrame pending, byte[] pendingPayload) =
            CreateOwnedFrame(firstSequence: 100, payloadBytes: 1, fill: 0x77);
        Task<RemoteWindowLogicalVideoFrameOutcome> currentCompletion =
            sender.TakeOwnership(current);
        await sink.BlockedSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Task<RemoteWindowLogicalVideoFrameOutcome> pendingCompletion =
            sender.TakeOwnership(pending);

        await Task.Run(sender.StopNow).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sender.IsClosed);
        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Cancelled,
            await pendingCompletion);
        Assert.All(currentPayload, static value => Assert.Equal(0, value));
        Assert.All(pendingPayload, static value => Assert.Equal(0, value));
        Assert.All(
            buffers.Allocations,
            static allocation => Assert.All(
                allocation,
                static value => Assert.Equal(0, value)));
        Assert.Equal(sink.BorrowedPayload, sink.BorrowedFrame!.ExportPayload());

        Task disposal = sender.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        Assert.False(currentCompletion.IsCompleted);
        sink.Release();

        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Cancelled,
            await currentCompletion);
        await disposal;
        Assert.True(sink.PayloadWasStableUntilCompletion);
        Assert.Throws<ObjectDisposedException>(sink.BorrowedFrame.ExportPayload);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task StopNowReturnsBeforeBlockingSinkCancellationCallback()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingCancellationCallbackMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerId,
            sink);
        (RemoteWindowLogicalVideoFrame frame, byte[] ownedPayload) =
            CreateOwnedFrame(firstSequence: 1, payloadBytes: 1, fill: 0x68);
        Task<RemoteWindowLogicalVideoFrameOutcome> completion =
            sender.TakeOwnership(frame);
        await sink.SendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Task stopping = Task.Factory.StartNew(
            sender.StopNow,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await sink.CancellationStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(sender.IsClosed);
        Assert.All(ownedPayload, static value => Assert.Equal(0, value));
        Task disposal = sender.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        sink.ReleaseCancellation();

        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Cancelled,
            await completion);
        await disposal;
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task SessionBackpressureDropsOneLogicalFrameWithoutClosingSender()
    {
        var budget = new RemoteWindowMediaSessionBudget(
            maximumPeers: 2,
            maximumFrames: 1,
            maximumBytes: RemoteWindowMediaFrame.MaximumPayloadBytes);
        var occupyingSink = new SteppedMediaSink(budget);
        var occupyingQueue = new RemoteWindowMediaOutboundQueue(
            budget,
            DeviceId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            occupyingSink);
        using RemoteWindowMediaFrame occupyingFrame = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x01]);
        RemoteWindowMediaEnqueueResult occupying =
            occupyingQueue.TryEnqueue(occupyingFrame);
        await occupyingSink.WaitForSendCountAsync(1);
        var sink = new ImmediateMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerId,
            sink);
        (RemoteWindowLogicalVideoFrame dropped, byte[] droppedPayload) =
            CreateOwnedFrame(firstSequence: 2, payloadBytes: 1, fill: 0x22);

        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Dropped,
            await sender.TakeOwnership(dropped));
        Assert.False(sender.IsClosed);
        Assert.All(droppedPayload, static value => Assert.Equal(0, value));

        occupyingSink.ReleaseOne();
        Assert.Equal(RemoteWindowMediaDeliveryOutcome.Sent, await occupying.Completion!);
        (RemoteWindowLogicalVideoFrame delivered, byte[] deliveredPayload) =
            CreateOwnedFrame(firstSequence: 3, payloadBytes: 1, fill: 0x33);
        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Sent,
            await sender.TakeOwnership(delivered));
        Assert.Equal([3UL], sink.Sequences);
        Assert.All(deliveredPayload, static value => Assert.Equal(0, value));

        await sender.DisposeAsync();
        await occupyingQueue.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task ChunkAllocationFailureClosesSenderAndZerosEveryOwner()
    {
        var buffers = new TrackingBufferOperations
        {
            FailAllocationCall = 5,
        };
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerId,
            new ImmediateMediaSink(),
            buffers);
        (RemoteWindowLogicalVideoFrame frame, byte[] ownedPayload) = CreateOwnedFrame(
            firstSequence: 1,
            RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes,
            fill: 0x33);

        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Failed,
            await sender.TakeOwnership(frame));

        Assert.True(sender.IsClosed);
        Assert.All(ownedPayload, static value => Assert.Equal(0, value));
        Assert.All(
            buffers.Allocations,
            static allocation => Assert.All(
                allocation,
                static value => Assert.Equal(0, value)));
        (RemoteWindowLogicalVideoFrame late, byte[] latePayload) =
            CreateOwnedFrame(firstSequence: 20, payloadBytes: 1, fill: 0x44);
        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Failed,
            await sender.TakeOwnership(late));
        Assert.All(latePayload, static value => Assert.Equal(0, value));

        await sender.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentDisposersShareCompletionAndCleanupFailure()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingDisposalMediaSink(
            new InvalidOperationException("FLOWSPAN-LOGICAL-SENDER-DISPOSAL-CANARY"));
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerId,
            sink);

        Task firstDisposal = sender.DisposeAsync().AsTask();
        await sink.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Task secondDisposal = sender.DisposeAsync().AsTask();

        Assert.Same(firstDisposal, secondDisposal);
        Assert.False(firstDisposal.IsCompleted);
        Assert.Equal(1, sink.DisposeCalls);
        sink.ReleaseDispose();

        Exception firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => firstDisposal);
        Exception secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => secondDisposal);
        Assert.Same(firstFailure, secondFailure);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task SubmissionAfterStopIsConsumedClearedAndCancelled()
    {
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerId,
            new ImmediateMediaSink());
        sender.StopNow();
        (RemoteWindowLogicalVideoFrame frame, byte[] ownedPayload) =
            CreateOwnedFrame(firstSequence: 1, payloadBytes: 4, fill: 0x5a);

        Assert.Equal(
            RemoteWindowLogicalVideoFrameOutcome.Cancelled,
            await sender.TakeOwnership(frame));
        Assert.All(ownedPayload, static value => Assert.Equal(0, value));

        await sender.DisposeAsync();
    }

    private static (RemoteWindowLogicalVideoFrame Frame, byte[] OwnedPayload)
        CreateOwnedFrame(ulong firstSequence, int payloadBytes, byte fill)
    {
        byte[] payload = Enumerable.Repeat(fill, payloadBytes).ToArray();
        return (
            RemoteWindowLogicalVideoFrame.TakeOwnership(
                SessionId,
                ActivityId,
                firstSequence,
                payload),
            payload);
    }

    private sealed record CapturedSend(
        ulong Sequence,
        ushort ChunkIndex,
        ushort ChunkCount,
        byte[] Payload);

    private sealed class SteppedMediaSink(RemoteWindowMediaSessionBudget budget) :
        IRemoteWindowMediaSink,
        IDisposable
    {
        private readonly Lock gate = new();
        private readonly SemaphoreSlim releases = new(0);
        private TaskCompletionSource changed = NewCompletion();
        private int maximumObservedFrames;

        public int MaximumObservedFrames => Volatile.Read(ref maximumObservedFrames);

        public IReadOnlyList<CapturedSend> Sends { get; private set; } = [];

        public void ReleaseOne() => releases.Release();

        public void Dispose() => releases.Dispose();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            RemoteWindowMediaBudgetSnapshot snapshot = budget.Snapshot;
            int observed;
            do
            {
                observed = Volatile.Read(ref maximumObservedFrames);
            }
            while (snapshot.Frames > observed
                && Interlocked.CompareExchange(
                    ref maximumObservedFrames,
                    snapshot.Frames,
                    observed) != observed);

            lock (gate)
            {
                Sends =
                [
                    .. Sends,
                    new CapturedSend(
                        frame.Sequence,
                        frame.ChunkIndex,
                        frame.ChunkCount,
                        frame.ExportPayload()),
                ];
                TaskCompletionSource completed = changed;
                changed = NewCompletion();
                completed.TrySetResult();
            }

            await releases.WaitAsync(cancellationToken);
        }

        public async Task WaitForSendCountAsync(int expected)
        {
            while (true)
            {
                Task wait;
                lock (gate)
                {
                    if (Sends.Count >= expected)
                    {
                        return;
                    }

                    wait = changed.Task;
                }

                await wait.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class ImmediateMediaSink : IRemoteWindowMediaSink
    {
        private readonly List<ulong> sequences = [];

        public IReadOnlyList<ulong> Sequences
        {
            get
            {
                lock (sequences)
                {
                    return sequences.ToArray();
                }
            }
        }

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            lock (sequences)
            {
                sequences.Add(frame.Sequence);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledFailureMediaSink(int failingSend) :
        IRemoteWindowMediaSink
    {
        private readonly TaskCompletionSource fail = NewCompletion();
        private readonly TaskCompletionSource failingSendStarted = NewCompletion();
        private int sends;

        public Task FailingSendStarted => failingSendStarted.Task;

        public IReadOnlyList<RemoteWindowMediaFrame> Frames { get; private set; } = [];

        public void FailNow() => fail.TrySetResult();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            Frames = [.. Frames, frame];
            if (Interlocked.Increment(ref sends) != failingSend)
            {
                return;
            }

            failingSendStarted.TrySetResult();
            await fail.Task;
            throw new InjectedBufferFailureException();
        }
    }

    private sealed class NonCooperativeMediaSink(int blockedSend) :
        IRemoteWindowMediaSink
    {
        private readonly TaskCompletionSource blockedSendStarted = NewCompletion();
        private readonly TaskCompletionSource release = NewCompletion();
        private int sends;

        public Task BlockedSendStarted => blockedSendStarted.Task;

        public RemoteWindowMediaFrame? BorrowedFrame { get; private set; }

        public byte[]? BorrowedPayload { get; private set; }

        public bool PayloadWasStableUntilCompletion { get; private set; }

        public void Release() => release.TrySetResult();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref sends) != blockedSend)
            {
                return;
            }

            BorrowedFrame = frame;
            BorrowedPayload = frame.ExportPayload();
            blockedSendStarted.TrySetResult();
            await release.Task;
            PayloadWasStableUntilCompletion =
                BorrowedPayload.SequenceEqual(frame.ExportPayload());
        }
    }

    private sealed class BlockingDisposalMediaSink(Exception failure) :
        IRemoteWindowMediaSink,
        IAsyncDisposable
    {
        private readonly TaskCompletionSource disposeStarted = NewCompletion();
        private readonly TaskCompletionSource releaseDispose = NewCompletion();
        private int disposeCalls;

        public int DisposeCalls => Volatile.Read(ref disposeCalls);

        public Task DisposeStarted => disposeStarted.Task;

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCalls);
            disposeStarted.TrySetResult();
            await releaseDispose.Task;
            throw failure;
        }

        public void ReleaseDispose() => releaseDispose.TrySetResult();

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BlockingCancellationCallbackMediaSink :
        IRemoteWindowMediaSink
    {
        private readonly TaskCompletionSource cancellationStarted = NewCompletion();
        private readonly TaskCompletionSource releaseCancellation = NewCompletion();
        private readonly TaskCompletionSource sendCancelled = NewCompletion();
        private readonly TaskCompletionSource sendStarted = NewCompletion();

        public Task CancellationStarted => cancellationStarted.Task;

        public Task SendStarted => sendStarted.Task;

        public void ReleaseCancellation() => releaseCancellation.TrySetResult();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenRegistration callback =
                cancellationToken.Register(() =>
                {
                    cancellationStarted.TrySetResult();
                    releaseCancellation.Task.GetAwaiter().GetResult();
                    sendCancelled.TrySetResult();
                });
            sendStarted.TrySetResult();
            await sendCancelled.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class TrackingBufferOperations :
        IRemoteWindowVideoBufferOperations
    {
        private int allocationCalls;

        public List<byte[]> Allocations { get; } = [];

        public int? FailAllocationCall { get; init; }

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

        public void Copy(ReadOnlySpan<byte> source, Span<byte> destination) =>
            source.CopyTo(destination);
    }

    private sealed class InjectedBufferFailureException : Exception;

    private static TaskCompletionSource NewCompletion() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);
}
