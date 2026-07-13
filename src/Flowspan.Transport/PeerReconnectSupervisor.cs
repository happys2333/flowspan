using System.Net.NetworkInformation;

namespace Flowspan.Transport;

public enum PeerSessionAttemptStatus
{
    TransientFailure,
    AuthenticatedSessionEnded,
    PermanentRejection,
}

public enum PeerReconnectStopReason
{
    PeerNotTrusted,
    CandidateIdentityChanged,
    CapabilityDenied,
    ProtocolIncompatible,
    AuthenticationFailed,
}

public readonly record struct PeerSessionAttemptResult
{
    private PeerSessionAttemptResult(
        PeerSessionAttemptStatus status,
        PeerReconnectStopReason? stopReason)
    {
        Status = status;
        StopReason = stopReason;
    }

    public PeerSessionAttemptStatus Status { get; }

    public PeerReconnectStopReason? StopReason { get; }

    public static PeerSessionAttemptResult TransientFailure { get; } =
        new(PeerSessionAttemptStatus.TransientFailure, null);

    public static PeerSessionAttemptResult AuthenticatedSessionEnded { get; } =
        new(PeerSessionAttemptStatus.AuthenticatedSessionEnded, null);

    public static PeerSessionAttemptResult PermanentlyRejected(
        PeerReconnectStopReason stopReason)
    {
        if (!Enum.IsDefined(stopReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stopReason),
                stopReason,
                "Unknown peer reconnect stop reason.");
        }

        return new PeerSessionAttemptResult(
            PeerSessionAttemptStatus.PermanentRejection,
            stopReason);
    }
}

public interface IAuthenticatedPeerSessionAttempt
{
    public ValueTask<PeerSessionAttemptResult> RunAsync(
        CancellationToken cancellationToken = default);
}

public interface IReconnectDelay
{
    public ValueTask WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}

public sealed class SystemReconnectDelay : IReconnectDelay
{
    public ValueTask WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default) =>
        new(Task.Delay(delay, cancellationToken));
}

public interface INetworkChangeSource
{
    public IDisposable Subscribe(Action networkChanged);
}

public sealed class SystemNetworkChangeSource : INetworkChangeSource
{
    public IDisposable Subscribe(Action networkChanged)
    {
        ArgumentNullException.ThrowIfNull(networkChanged);
        return new Subscription(networkChanged);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly NetworkAddressChangedEventHandler handler;
        private int disposed;

        public Subscription(Action networkChanged)
        {
            handler = (_, _) => networkChanged();
            NetworkChange.NetworkAddressChanged += handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                NetworkChange.NetworkAddressChanged -= handler;
            }
        }
    }
}

public sealed class PeerReconnectSupervisor
{
    private readonly IAuthenticatedPeerSessionAttempt sessionAttempt;
    private readonly INetworkChangeSource networkChanges;
    private readonly IReconnectDelay delay;
    private readonly ReconnectBackoff backoff;
    private readonly Func<double> nextJitterSample;
    private int running;

    public PeerReconnectSupervisor(
        IAuthenticatedPeerSessionAttempt sessionAttempt,
        INetworkChangeSource networkChanges,
        IReconnectDelay delay,
        ReconnectBackoff backoff,
        Func<double> nextJitterSample)
    {
        ArgumentNullException.ThrowIfNull(sessionAttempt);
        ArgumentNullException.ThrowIfNull(networkChanges);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(backoff);
        ArgumentNullException.ThrowIfNull(nextJitterSample);
        this.sessionAttempt = sessionAttempt;
        this.networkChanges = networkChanges;
        this.delay = delay;
        this.backoff = backoff;
        this.nextJitterSample = nextJitterSample;
    }

    public async ValueTask<PeerReconnectStopReason> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A peer reconnect supervisor can run only one loop at a time.");
        }

        try
        {
            var networkInterrupt = new NetworkChangeInterrupt();
            using IDisposable subscription = networkChanges.Subscribe(
                networkInterrupt.Signal);
            int failedAttempts = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long observedNetworkGeneration = networkInterrupt.Generation;
                PeerSessionAttemptResult outcome = default;
                bool networkChanged = await ExecuteInterruptiblyAsync(
                    async attemptCancellation =>
                    {
                        outcome = await sessionAttempt.RunAsync(attemptCancellation)
                            .ConfigureAwait(false);
                    },
                    networkInterrupt,
                    observedNetworkGeneration,
                    cancellationToken).ConfigureAwait(false);
                if (outcome.Status is PeerSessionAttemptStatus.PermanentRejection)
                {
                    return outcome.StopReason
                        ?? throw new InvalidOperationException(
                            "A permanent peer rejection must include a stop reason.");
                }

                if (networkChanged)
                {
                    failedAttempts = 0;
                    continue;
                }

                if (outcome.Status is PeerSessionAttemptStatus.AuthenticatedSessionEnded)
                {
                    failedAttempts = 0;
                }

                TimeSpan retryDelay = backoff.DelayForAttempt(
                    failedAttempts,
                    nextJitterSample());
                if (outcome.Status is PeerSessionAttemptStatus.TransientFailure
                    && failedAttempts < int.MaxValue)
                {
                    failedAttempts++;
                }

                networkChanged = await ExecuteInterruptiblyAsync(
                    retryCancellation => delay.WaitAsync(
                        retryDelay,
                        retryCancellation),
                    networkInterrupt,
                    observedNetworkGeneration,
                    cancellationToken).ConfigureAwait(false);
                if (networkChanged)
                {
                    failedAttempts = 0;
                }
            }
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    private static async ValueTask<bool> ExecuteInterruptiblyAsync(
        Func<CancellationToken, ValueTask> operation,
        NetworkChangeInterrupt networkInterrupt,
        long observedNetworkGeneration,
        CancellationToken cancellationToken)
    {
        using var operationCancellation = new CancellationTokenSource();
        using CancellationTokenRegistration callerCancellation =
            cancellationToken.Register(
                static state => NetworkChangeInterrupt.TryCancel(
                    (CancellationTokenSource)state!),
                operationCancellation);
        networkInterrupt.Attach(
            operationCancellation,
            observedNetworkGeneration);
        try
        {
            await operation(operationCancellation.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return networkInterrupt.Generation != observedNetworkGeneration;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && networkInterrupt.Generation != observedNetworkGeneration)
        {
            return true;
        }
        finally
        {
            networkInterrupt.Detach(operationCancellation);
        }
    }

    private sealed class NetworkChangeInterrupt
    {
        private readonly Lock gate = new();
        private CancellationTokenSource? activeOperation;
        private long generation;

        public long Generation
        {
            get
            {
                lock (gate)
                {
                    return generation;
                }
            }
        }

        public void Attach(
            CancellationTokenSource operationCancellation,
            long observedGeneration)
        {
            bool changed;
            lock (gate)
            {
                activeOperation = operationCancellation;
                changed = generation != observedGeneration;
            }

            if (changed)
            {
                TryCancel(operationCancellation);
            }
        }

        public void Detach(CancellationTokenSource operationCancellation)
        {
            lock (gate)
            {
                if (ReferenceEquals(activeOperation, operationCancellation))
                {
                    activeOperation = null;
                }
            }
        }

        public void Signal()
        {
            CancellationTokenSource? operationCancellation;
            lock (gate)
            {
                generation++;
                operationCancellation = activeOperation;
            }

            if (operationCancellation is not null)
            {
                TryCancel(operationCancellation);
            }
        }

        internal static void TryCancel(CancellationTokenSource cancellation)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A racing completed operation already detached and disposed it.
            }
            catch (AggregateException)
            {
                // A boundary callback failed, but cancellation still took effect.
            }
        }
    }
}
