using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class PeerReconnectSupervisorTests
{
    [Fact]
    public async Task PermanentRejectionReasonIsReturnedToCaller()
    {
        var supervisor = new PeerReconnectSupervisor(
            new IdentityChangedSessionAttempt(),
            new SilentNetworkChangeSource(),
            new RecordingDelay(),
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);

        PeerReconnectStopReason result = await supervisor.RunAsync();

        Assert.Equal(PeerReconnectStopReason.CandidateIdentityChanged, result);
    }

    [Fact]
    public async Task TransientFailuresBackOffUntilPermanentRejection()
    {
        var attempt = new SequenceSessionAttempt(
        [
            PeerSessionAttemptResult.TransientFailure,
            PeerSessionAttemptResult.TransientFailure,
            PeerSessionAttemptResult.TransientFailure,
            RejectedAsUntrusted(),
        ]);
        var delay = new RecordingDelay();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            new SilentNetworkChangeSource(),
            delay,
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);

        PeerReconnectStopReason result = await supervisor.RunAsync();

        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result);
        Assert.Equal(4, attempt.Count);
        Assert.Equal<TimeSpan>(
        [
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
        ], delay.Requests);
    }

    [Fact]
    public async Task NetworkChangeInterruptsBackoffAndRetriesImmediately()
    {
        var attempt = new SequenceSessionAttempt(
        [
            PeerSessionAttemptResult.TransientFailure,
            RejectedAsUntrusted(),
        ]);
        var networkChanges = new TestNetworkChangeSource();
        var delay = new CancellableDelay();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            networkChanges,
            delay,
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);
        using var stop = new CancellationTokenSource();
        Task<PeerReconnectStopReason> running = supervisor.RunAsync(stop.Token).AsTask();

        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        networkChanges.Signal();

        try
        {
            PeerReconnectStopReason result = await running.WaitAsync(
                TimeSpan.FromSeconds(1));
            Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result);
            Assert.Equal(2, attempt.Count);
        }
        finally
        {
            stop.Cancel();
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public async Task AuthenticatedSessionEndResetsFailureBackoff()
    {
        var attempt = new SequenceSessionAttempt(
        [
            PeerSessionAttemptResult.TransientFailure,
            PeerSessionAttemptResult.TransientFailure,
            PeerSessionAttemptResult.AuthenticatedSessionEnded,
            PeerSessionAttemptResult.TransientFailure,
            RejectedAsUntrusted(),
        ]);
        var delay = new RecordingDelay();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            new SilentNetworkChangeSource(),
            delay,
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);

        PeerReconnectStopReason result = await supervisor.RunAsync();

        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result);
        Assert.Equal<TimeSpan>(
        [
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(250),
        ], delay.Requests);
    }

    [Fact]
    public async Task SystemReconnectDelayHonorsCancellation()
    {
        var delay = new SystemReconnectDelay();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await delay.WaitAsync(TimeSpan.FromHours(1), cancellation.Token));
    }

    [Fact]
    public void SystemNetworkChangeSubscriptionDisposesIdempotently()
    {
        var source = new SystemNetworkChangeSource();
        IDisposable subscription = source.Subscribe(static () => { });

        subscription.Dispose();
        subscription.Dispose();

        Assert.Throws<ArgumentNullException>(() =>
        {
            source.Subscribe(null!);
        });
    }

    [Fact]
    public async Task ConcurrentRunIsRejectedWithoutStartingAnotherAttempt()
    {
        var attempt = new BlockingFirstSessionAttempt();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            new SilentNetworkChangeSource(),
            new RecordingDelay(),
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);
        using var stop = new CancellationTokenSource();
        Task<PeerReconnectStopReason> firstRun = supervisor.RunAsync(stop.Token).AsTask();
        await attempt.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await supervisor.RunAsync());
            Assert.Equal(1, attempt.Count);
        }
        finally
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await firstRun);
        }
    }

    [Fact]
    public async Task RapidNetworkChangesDrainTheOldAttemptBeforeRetrying()
    {
        var attempt = new DelayedDrainSessionAttempt();
        var networkChanges = new TestNetworkChangeSource();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            networkChanges,
            new RecordingDelay(),
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);

        Task<PeerReconnectStopReason> running = supervisor.RunAsync().AsTask();
        await attempt.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        networkChanges.Signal();
        networkChanges.Signal();
        attempt.AllowDrain.TrySetResult();

        PeerReconnectStopReason result = await running.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result);
        Assert.Equal(2, attempt.Count);
        Assert.Equal(1, attempt.MaximumConcurrency);
    }

    [Fact]
    public async Task CallerCancellationDrainsAttemptAndRemovesSubscription()
    {
        var attempt = new BlockingFirstSessionAttempt();
        var networkChanges = new TestNetworkChangeSource();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            networkChanges,
            new RecordingDelay(),
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);
        using var stop = new CancellationTokenSource();
        Task<PeerReconnectStopReason> running = supervisor.RunAsync(stop.Token).AsTask();
        await attempt.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(networkChanges.HasSubscriber);

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await running);
        Assert.False(networkChanges.HasSubscriber);
        Assert.Equal(1, attempt.Count);
        Assert.Equal(1, attempt.MaximumConcurrency);
    }

    [Fact]
    public async Task ThrowingCancellationCallbackCannotEscapeNetworkSignal()
    {
        var attempt = new ThrowingCancellationSessionAttempt();
        var networkChanges = new TestNetworkChangeSource();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            networkChanges,
            new RecordingDelay(),
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);
        Task<PeerReconnectStopReason> running = supervisor.RunAsync().AsTask();
        await attempt.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Exception? signalFailure = Record.Exception(networkChanges.Signal);
        PeerReconnectStopReason result = await running.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.Null(signalFailure);
        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result);
        Assert.Equal(2, attempt.Count);
    }

    [Fact]
    public async Task PermanentRejectionWinsRaceWithNetworkChange()
    {
        var attempt = new PermanentAfterCancellationSessionAttempt();
        var networkChanges = new TestNetworkChangeSource();
        var supervisor = new PeerReconnectSupervisor(
            attempt,
            networkChanges,
            new RecordingDelay(),
            new ReconnectBackoff(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1),
                jitterFraction: 0),
            static () => 0.5);
        Task<PeerReconnectStopReason> running = supervisor.RunAsync().AsTask();
        await attempt.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        networkChanges.Signal();
        PeerReconnectStopReason result = await running.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result);
        Assert.Equal(1, attempt.Count);
    }

    private static PeerSessionAttemptResult RejectedAsUntrusted() =>
        PeerSessionAttemptResult.PermanentlyRejected(
            PeerReconnectStopReason.PeerNotTrusted);

    private sealed class SequenceSessionAttempt(
        IEnumerable<PeerSessionAttemptResult> outcomes) : IAuthenticatedPeerSessionAttempt
    {
        private readonly Queue<PeerSessionAttemptResult> outcomes = new(outcomes);

        public int Count { get; private set; }

        public ValueTask<PeerSessionAttemptResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return ValueTask.FromResult(outcomes.Dequeue());
        }
    }

    private sealed class IdentityChangedSessionAttempt :
        IAuthenticatedPeerSessionAttempt
    {
        public ValueTask<PeerSessionAttemptResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                PeerSessionAttemptResult.PermanentlyRejected(
                    PeerReconnectStopReason.CandidateIdentityChanged));
        }
    }

    private sealed class RecordingDelay : IReconnectDelay
    {
        public List<TimeSpan> Requests { get; } = [];

        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellableDelay : IReconnectDelay
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        }
    }

    private sealed class BlockingFirstSessionAttempt : IAuthenticatedPeerSessionAttempt
    {
        private int active;
        private int count;
        private int maximumConcurrency;

        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count => Volatile.Read(ref count);

        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public async ValueTask<PeerSessionAttemptResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            int invocation = Interlocked.Increment(ref count);
            int concurrency = Interlocked.Increment(ref active);
            UpdateMaximumConcurrency(concurrency);
            try
            {
                if (invocation > 1)
                {
                    return RejectedAsUntrusted();
                }

                FirstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PeerSessionAttemptResult.AuthenticatedSessionEnded;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        private void UpdateMaximumConcurrency(int value)
        {
            int current = Volatile.Read(ref maximumConcurrency);
            while (value > current)
            {
                int observed = Interlocked.CompareExchange(
                    ref maximumConcurrency,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class ThrowingCancellationSessionAttempt : IAuthenticatedPeerSessionAttempt
    {
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count { get; private set; }

        public async ValueTask<PeerSessionAttemptResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            Count++;
            if (Count > 1)
            {
                return RejectedAsUntrusted();
            }

            using CancellationTokenRegistration registration =
                cancellationToken.Register(static () =>
                    throw new InvalidOperationException("Injected cancellation failure."));
            FirstStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PeerSessionAttemptResult.AuthenticatedSessionEnded;
        }
    }

    private sealed class DelayedDrainSessionAttempt :
        IAuthenticatedPeerSessionAttempt
    {
        private int active;
        private int count;
        private int maximumConcurrency;

        public TaskCompletionSource AllowDrain { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count => Volatile.Read(ref count);

        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public async ValueTask<PeerSessionAttemptResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            int invocation = Interlocked.Increment(ref count);
            int concurrency = Interlocked.Increment(ref active);
            UpdateMaximumConcurrency(concurrency);
            try
            {
                if (invocation > 1)
                {
                    return RejectedAsUntrusted();
                }

                FirstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                }

                await AllowDrain.Task;
                return PeerSessionAttemptResult.AuthenticatedSessionEnded;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        private void UpdateMaximumConcurrency(int value)
        {
            int current = Volatile.Read(ref maximumConcurrency);
            while (value > current)
            {
                int observed = Interlocked.CompareExchange(
                    ref maximumConcurrency,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class PermanentAfterCancellationSessionAttempt :
        IAuthenticatedPeerSessionAttempt
    {
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count { get; private set; }

        public async ValueTask<PeerSessionAttemptResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            Count++;
            if (Count == 1)
            {
                FirstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                }
            }

            return RejectedAsUntrusted();
        }
    }

    private sealed class TestNetworkChangeSource : INetworkChangeSource
    {
        private Action? networkChanged;

        public bool HasSubscriber => networkChanged is not null;

        public IDisposable Subscribe(Action networkChanged)
        {
            ArgumentNullException.ThrowIfNull(networkChanged);
            Assert.Null(this.networkChanged);
            this.networkChanged = networkChanged;
            return new CallbackSubscription(() => this.networkChanged = null);
        }

        public void Signal()
        {
            Assert.NotNull(networkChanged);
            networkChanged();
        }
    }

    private sealed class SilentNetworkChangeSource : INetworkChangeSource
    {
        public IDisposable Subscribe(Action networkChanged)
        {
            ArgumentNullException.ThrowIfNull(networkChanged);
            return new NoopSubscription();
        }
    }

    private sealed class NoopSubscription : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class CallbackSubscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
