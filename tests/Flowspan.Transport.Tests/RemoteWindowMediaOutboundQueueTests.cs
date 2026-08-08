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
            RemoteWindowMediaFrame frame = CreateFrame(sequence);
            firstSubmitted ??= frame;
            accepted.Add(queue.TryEnqueue(frame));
        }

        await sink.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowMediaEnqueueResult backpressured =
            queue.TryEnqueue(CreateFrame(sequence: 9));
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
            firstQueue.TryEnqueue(CreateFrame(sequence: 1));
        RemoteWindowMediaEnqueueResult second =
            secondQueue.TryEnqueue(CreateFrame(sequence: 2));

        RemoteWindowMediaEnqueueResult rejected =
            firstQueue.TryEnqueue(CreateFrame(sequence: 3));

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
            queue.TryEnqueue(CreateFrame(sequence: 1)),
            queue.TryEnqueue(CreateFrame(sequence: 2)),
            queue.TryEnqueue(CreateFrame(sequence: 3)),
        ];
        await sink.SendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        sink.Fail();
        RemoteWindowMediaDeliveryOutcome[] outcomes = await Task.WhenAll(
            accepted.Select(result => result.Completion!));
        RemoteWindowMediaEnqueueResult rejected =
            queue.TryEnqueue(CreateFrame(sequence: 4));
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
            queue.TryEnqueue(CreateFrame(sequence: 1)),
            queue.TryEnqueue(CreateFrame(sequence: 2)),
            queue.TryEnqueue(CreateFrame(sequence: 3)),
        ];
        await sink.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await queue.DisposeAsync();
        RemoteWindowMediaDeliveryOutcome[] outcomes = await Task.WhenAll(
            accepted.Select(result => result.Completion!));
        RemoteWindowMediaEnqueueResult rejected =
            queue.TryEnqueue(CreateFrame(sequence: 4));

        Assert.All(outcomes, outcome =>
            Assert.Equal(RemoteWindowMediaDeliveryOutcome.Cancelled, outcome));
        Assert.Equal(RemoteWindowMediaEnqueueStatus.Closed, rejected.Status);
        Assert.True(sink.IsDisposed);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task ConcurrentEnqueueNeverOverbooksPeerBudget()
    {
        var budget = new RemoteWindowMediaSessionBudget();
        var sink = new BlockingMediaSink();
        var queue = new RemoteWindowMediaOutboundQueue(budget, PeerId, sink);
        Task<RemoteWindowMediaEnqueueResult>[] attempts = Enumerable.Range(1, 64)
            .Select(index => Task.Run(() => queue.TryEnqueue(
                CreateFrame(checked((ulong)index)))))
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

    private static RemoteWindowMediaFrame CreateFrame(ulong sequence) =>
        RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence,
            chunkIndex: 0,
            chunkCount: 1,
            [checked((byte)sequence)]);

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
}
