using Flowspan.Domain;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowMediaOutboundQueueTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task NinthPeerFrameIsBackpressuredUntilAcceptedFramesDrainInOrder()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingMediaSink();
        await using var queue = new RemoteWindowMediaOutboundQueue(
            budget,
            PeerId,
            sink);
        var accepted = new List<RemoteWindowMediaEnqueueResult>();
        RemoteWindowMediaFrame? firstSubmitted = null;
        for (ulong sequence = 1;
            sequence <= RemoteWindowMediaOutboundQueue.MaximumFrames;
            sequence++)
        {
            using RemoteWindowMediaFrame frame = CreateFrame(sequence);
            firstSubmitted ??= frame;
            accepted.Add(queue.TryEnqueue(frame));
        }

        await sink.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowMediaEnqueueResult backpressured =
            EnqueueFrame(queue, sequence: 9);
        RemoteWindowMediaBudgetSnapshot saturated = budget.Snapshot;

        Assert.All(accepted, result =>
            Assert.Equal(RemoteWindowMediaEnqueueStatus.Accepted, result.Status));
        Assert.Equal(
            RemoteWindowMediaEnqueueStatus.PeerBackpressure,
            backpressured.Status);
        Assert.Equal(RemoteWindowMediaOutboundQueue.MaximumFrames, saturated.Frames);
        Assert.Equal(RemoteWindowMediaOutboundQueue.MaximumFrames, saturated.Bytes);
        Assert.NotSame(firstSubmitted, sink.FirstFrame);

        sink.Release(RemoteWindowMediaOutboundQueue.MaximumFrames);
        RemoteWindowMediaDeliveryOutcome[] delivered = await Task.WhenAll(
            accepted.Select(result => result.Completion!));

        Assert.All(delivered, outcome =>
            Assert.Equal(RemoteWindowMediaDeliveryOutcome.Sent, outcome));
        Assert.Equal(
            Enumerable.Range(1, RemoteWindowMediaOutboundQueue.MaximumFrames)
                .Select(static value => (ulong)value),
            sink.Sequences);
        RemoteWindowMediaBudgetSnapshot drained = budget.Snapshot;
        Assert.Equal(0, drained.Frames);
        Assert.Equal(0, drained.Bytes);
        Assert.Equal(1, drained.Peers);
    }

    [Fact]
    public async Task SixteenthRemotePeerIsRejectedAndRegistrationsReleaseOnDispose()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var queues = new List<RemoteWindowMediaOutboundQueue>();
        try
        {
            for (int index = 1;
                index <= RemoteWindowMediaSessionBudget.MaximumPeers;
                index++)
            {
                DeviceId peerId = DeviceId.From(
                    Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"));
                queues.Add(new RemoteWindowMediaOutboundQueue(
                    budget,
                    peerId,
                    new ImmediateMediaSink()));
            }

            Assert.Equal(
                RemoteWindowMediaSessionBudget.MaximumPeers,
                budget.Snapshot.Peers);
            DeviceId rejectedPeer = DeviceId.Parse(
                "00000000-0000-0000-0000-000000000016");
            Assert.Throws<InvalidOperationException>(() =>
                new RemoteWindowMediaOutboundQueue(
                    budget,
                    rejectedPeer,
                    new ImmediateMediaSink()));
        }
        finally
        {
            foreach (RemoteWindowMediaOutboundQueue queue in queues)
            {
                await queue.DisposeAsync();
            }
        }

        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Theory]
    [InlineData(2, 1024)]
    [InlineData(8, 2)]
    public async Task SharedSessionCeilingsBackpressureBeforePeerCeilings(
        int maximumFrames,
        long maximumBytes)
    {
        var budget = new RemoteWindowMediaSessionBudget(
            maximumPeers: 2,
            maximumFrames,
            maximumBytes);
        var firstSink = new BlockingMediaSink();
        var secondSink = new BlockingMediaSink();
        await using var firstQueue = new RemoteWindowMediaOutboundQueue(
            budget,
            PeerId,
            firstSink);
        await using var secondQueue = new RemoteWindowMediaOutboundQueue(
            budget,
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            secondSink);
        RemoteWindowMediaEnqueueResult first =
            EnqueueFrame(firstQueue, sequence: 1);
        RemoteWindowMediaEnqueueResult second =
            EnqueueFrame(secondQueue, sequence: 2);

        RemoteWindowMediaEnqueueResult rejected =
            EnqueueFrame(firstQueue, sequence: 3);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(
            RemoteWindowMediaEnqueueStatus.SessionBackpressure,
            rejected.Status);
        Assert.Equal(2, budget.Snapshot.Frames);

        firstSink.Release(1);
        secondSink.Release(1);
        Assert.Equal(
            RemoteWindowMediaDeliveryOutcome.Sent,
            await first.Completion!);
        Assert.Equal(
            RemoteWindowMediaDeliveryOutcome.Sent,
            await second.Completion!);
    }

    [Fact]
    public async Task SinkFailureReleasesEveryReservationAndClosesQueue()
    {
        const string exceptionCanary = "FLOWSPAN-SINK-EXCEPTION-CANARY";
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new FailingMediaSink(exceptionCanary);
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);
        RemoteWindowMediaEnqueueResult[] accepted =
        [
            EnqueueFrame(queue, sequence: 1),
            EnqueueFrame(queue, sequence: 2),
            EnqueueFrame(queue, sequence: 3),
        ];
        await sink.SendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        sink.Fail();
        RemoteWindowMediaDeliveryOutcome[] outcomes = await Task.WhenAll(
            accepted.Select(result => result.Completion!));
        RemoteWindowMediaEnqueueResult rejected =
            EnqueueFrame(queue, sequence: 4);
        RemoteWindowMediaBudgetSnapshot drained = budget.Snapshot;

        Assert.All(outcomes, outcome =>
            Assert.Equal(RemoteWindowMediaDeliveryOutcome.Failed, outcome));
        Assert.Equal(RemoteWindowMediaEnqueueStatus.Closed, rejected.Status);
        Assert.Null(rejected.Completion);
        Assert.Equal(0, drained.Frames);
        Assert.Equal(0, drained.Bytes);
        Assert.DoesNotContain(
            exceptionCanary,
            rejected.ToString(),
            StringComparison.Ordinal);

        await queue.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task DisposeCancelsBlockedAndPendingFramesAndReleasesPeer()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingMediaSink();
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);
        RemoteWindowMediaEnqueueResult[] accepted =
        [
            EnqueueFrame(queue, sequence: 1),
            EnqueueFrame(queue, sequence: 2),
            EnqueueFrame(queue, sequence: 3),
        ];
        await sink.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await queue.DisposeAsync();
        RemoteWindowMediaDeliveryOutcome[] outcomes = await Task.WhenAll(
            accepted.Select(result => result.Completion!));
        RemoteWindowMediaEnqueueResult rejected =
            EnqueueFrame(queue, sequence: 4);

        Assert.All(outcomes, outcome =>
            Assert.Equal(RemoteWindowMediaDeliveryOutcome.Cancelled, outcome));
        Assert.Equal(RemoteWindowMediaEnqueueStatus.Closed, rejected.Status);
        Assert.True(sink.IsDisposed);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task ConcurrentDisposersJoinBlockedSinkCleanup()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingAsyncDisposableMediaSink();
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);

        Task firstDisposal = queue.DisposeAsync().AsTask();
        await sink.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Task secondDisposal = queue.DisposeAsync().AsTask();

        Assert.False(firstDisposal.IsCompleted);
        Assert.False(secondDisposal.IsCompleted);
        Assert.Equal(1, sink.DisposeCalls);

        sink.ReleaseDispose();
        await Task.WhenAll(firstDisposal, secondDisposal);

        Assert.Equal(1, sink.DisposeCalls);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task ConcurrentDisposersObserveTheSameCleanupFailure()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingAsyncDisposableMediaSink(
            new InvalidOperationException("FLOWSPAN-DISPOSAL-FAILURE-CANARY"));
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);

        Task firstDisposal = queue.DisposeAsync().AsTask();
        await sink.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Task secondDisposal = queue.DisposeAsync().AsTask();
        sink.ReleaseDispose();

        Exception firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => firstDisposal);
        Exception secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => secondDisposal);

        Assert.Same(firstFailure, secondFailure);
        Assert.Equal(1, sink.DisposeCalls);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task CancellationCallbackFailureCannotSkipQueueCleanup()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new ThrowingCancellationMediaSink();
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);
        RemoteWindowMediaEnqueueResult[] accepted =
        [
            EnqueueFrame(queue, sequence: 1),
            EnqueueFrame(queue, sequence: 2),
            EnqueueFrame(queue, sequence: 3),
        ];
        await sink.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<Task> invokingDisposal = Task.Factory.StartNew(
            () => queue.DisposeAsync().AsTask(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await sink.CancellationStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposing;
        try
        {
            disposing = await invokingDisposal.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            sink.ReleaseCancellation();
        }

        AggregateException failure =
            await Assert.ThrowsAsync<AggregateException>(async () =>
                await disposing);

        Assert.Equal(2, failure.Flatten().InnerExceptions.Count);
        Assert.Contains(
            failure.Flatten().InnerExceptions,
            cause => ReferenceEquals(cause, sink.CancellationFailure));
        Assert.Contains(
            failure.Flatten().InnerExceptions,
            cause => ReferenceEquals(cause, sink.CleanupFailure));
        Assert.All(
            await Task.WhenAll(accepted.Select(result => result.Completion!)),
            outcome => Assert.Equal(
                RemoteWindowMediaDeliveryOutcome.Cancelled,
                outcome));
        Assert.True(sink.IsDisposed);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);

        await using var replacement = new RemoteWindowMediaOutboundQueue(
            budget,
            PeerId,
            new ImmediateMediaSink());
    }

    [Fact]
    public async Task ConcurrentEnqueueNeverOverbooksPeerBudget()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingMediaSink();
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);
        Task<RemoteWindowMediaEnqueueResult>[] attempts = Enumerable.Range(1, 64)
            .Select(index => Task.Run(() => EnqueueFrame(
                queue,
                checked((ulong)index))))
            .ToArray();

        RemoteWindowMediaEnqueueResult[] results = await Task.WhenAll(attempts);
        await sink.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowMediaEnqueueResult[] accepted = results
            .Where(static result => result.Accepted)
            .ToArray();

        Assert.Equal(RemoteWindowMediaOutboundQueue.MaximumFrames, accepted.Length);
        Assert.Equal(
            64 - RemoteWindowMediaOutboundQueue.MaximumFrames,
            results.Count(result =>
                result.Status == RemoteWindowMediaEnqueueStatus.PeerBackpressure));
        Assert.Equal(
            RemoteWindowMediaOutboundQueue.MaximumFrames,
            budget.Snapshot.Frames);

        sink.Release(RemoteWindowMediaOutboundQueue.MaximumFrames);
        Assert.All(
            await Task.WhenAll(accepted.Select(result => result.Completion!)),
            outcome => Assert.Equal(RemoteWindowMediaDeliveryOutcome.Sent, outcome));
        await queue.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task SentQueueCloneIsDisposedBeforeDeliveryCompletes()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new CapturingMediaSink();
        await using var queue = new RemoteWindowMediaOutboundQueue(
            budget,
            PeerId,
            sink);
        using RemoteWindowMediaFrame submitted = CreateFrame(sequence: 1);

        RemoteWindowMediaEnqueueResult result = queue.TryEnqueue(submitted);

        Assert.Equal(
            RemoteWindowMediaDeliveryOutcome.Sent,
            await result.Completion!);
        Assert.NotNull(sink.Frame);
        Assert.Throws<ObjectDisposedException>(sink.Frame.ExportPayload);
        Assert.NotEmpty(submitted.ExportPayload());
    }

    private static RemoteWindowMediaFrame CreateFrame(ulong sequence) =>
        RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence,
            chunkIndex: 0,
            chunkCount: 1,
            [checked((byte)sequence)]);

    private static RemoteWindowMediaEnqueueResult EnqueueFrame(
        RemoteWindowMediaOutboundQueue queue,
        ulong sequence)
    {
        using RemoteWindowMediaFrame frame = CreateFrame(sequence);
        return queue.TryEnqueue(frame);
    }

    private sealed class CapturingMediaSink : IRemoteWindowMediaSink
    {
        public RemoteWindowMediaFrame? Frame { get; private set; }

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            Frame = frame;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingMediaSink : IRemoteWindowMediaSink, IDisposable
    {
        private readonly SemaphoreSlim permits = new(0);
        private readonly List<ulong> sequences = [];
        private readonly TaskCompletionSource firstSendStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstSendStarted => firstSendStarted.Task;

        public RemoteWindowMediaFrame? FirstFrame { get; private set; }

        public bool IsDisposed { get; private set; }

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

        public void Release(int count) => permits.Release(count);

        public void Dispose()
        {
            permits.Dispose();
            IsDisposed = true;
        }

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            FirstFrame ??= frame;
            firstSendStarted.TrySetResult();
            await permits.WaitAsync(cancellationToken);
            lock (sequences)
            {
                sequences.Add(frame.Sequence);
            }
        }
    }

    private sealed class ImmediateMediaSink : IRemoteWindowMediaSink
    {
        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BlockingAsyncDisposableMediaSink(Exception? failure = null) :
        IRemoteWindowMediaSink,
        IAsyncDisposable
    {
        private readonly TaskCompletionSource disposeStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseDispose = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposeCalls;

        public int DisposeCalls => Volatile.Read(ref disposeCalls);

        public Task DisposeStarted => disposeStarted.Task;

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCalls);
            disposeStarted.TrySetResult();
            await releaseDispose.Task;
            if (failure is not null)
            {
                throw failure;
            }
        }

        public void ReleaseDispose() => releaseDispose.TrySetResult();

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FailingMediaSink(string exceptionMessage)
        : IRemoteWindowMediaSink
    {
        private readonly TaskCompletionSource fail = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource sendStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendStarted => sendStarted.Task;

        public void Fail() => fail.TrySetResult();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            sendStarted.TrySetResult();
            await fail.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException(exceptionMessage);
        }
    }

    private sealed class ThrowingCancellationMediaSink :
        IRemoteWindowMediaSink,
        IAsyncDisposable
    {
        public Exception CancellationFailure { get; } =
            new InvalidOperationException("injected cancellation callback failure");

        public Exception CleanupFailure { get; } =
            new InvalidOperationException("injected sink cleanup failure");

        public bool IsDisposed { get; private set; }

        public Task CancellationStarted => cancellationStarted.Task;

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource cancellationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource releaseCancellation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource sendCancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.FromException(CleanupFailure);
        }

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
                    throw CancellationFailure;
                });
            SendStarted.TrySetResult();
            await sendCancelled.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void ReleaseCancellation() =>
            releaseCancellation.TrySetResult();
    }
}
