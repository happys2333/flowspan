using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Flowspan.Domain;

namespace Flowspan.Transport;

public interface IRemoteWindowMediaSink
{
    public ValueTask SendAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default);
}

public enum RemoteWindowMediaEnqueueStatus
{
    Accepted,
    PeerBackpressure,
    SessionBackpressure,
    Closed,
}

public enum RemoteWindowMediaDeliveryOutcome
{
    Sent,
    Failed,
    Cancelled,
}

public sealed class RemoteWindowMediaEnqueueResult
{
    private RemoteWindowMediaEnqueueResult(
        RemoteWindowMediaEnqueueStatus status,
        Task<RemoteWindowMediaDeliveryOutcome>? completion)
    {
        Status = status;
        Completion = completion;
    }

    public bool Accepted => Status == RemoteWindowMediaEnqueueStatus.Accepted;

    public Task<RemoteWindowMediaDeliveryOutcome>? Completion { get; }

    public RemoteWindowMediaEnqueueStatus Status { get; }

    public override string ToString() =>
        $"{nameof(RemoteWindowMediaEnqueueResult)} {{ Status = {Status} }}";

    internal static RemoteWindowMediaEnqueueResult CreateAccepted(
        Task<RemoteWindowMediaDeliveryOutcome> completion) =>
        new(RemoteWindowMediaEnqueueStatus.Accepted, completion);

    internal static RemoteWindowMediaEnqueueResult CreateRejected(
        RemoteWindowMediaEnqueueStatus status)
    {
        if (status == RemoteWindowMediaEnqueueStatus.Accepted)
        {
            throw new ArgumentException(
                "An accepted media enqueue result requires a completion task.",
                nameof(status));
        }

        return new RemoteWindowMediaEnqueueResult(status, completion: null);
    }
}

public readonly record struct RemoteWindowMediaBudgetSnapshot(
    int Peers,
    int Frames,
    long Bytes)
{
    public static RemoteWindowMediaBudgetSnapshot Empty { get; } = new(0, 0, 0);
}

public sealed class RemoteWindowMediaSessionBudget
{
    public const long MaximumBytes = 8L * 1024 * 1024;
    public const int MaximumFrames = 128;
    public const int MaximumPeers = 15;
    private readonly Lock gate = new();
    private readonly long maximumBytes;
    private readonly int maximumFrames;
    private readonly int maximumPeers;
    private readonly Dictionary<DeviceId, PeerReservation> peers = [];
    private long reservedBytes;
    private int reservedFrames;

    public RemoteWindowMediaSessionBudget() : this(
        MaximumPeers,
        MaximumFrames,
        MaximumBytes)
    {
    }

    internal RemoteWindowMediaSessionBudget(
        int maximumPeers,
        int maximumFrames,
        long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPeers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        this.maximumPeers = maximumPeers;
        this.maximumFrames = maximumFrames;
        this.maximumBytes = maximumBytes;
    }

    public RemoteWindowMediaBudgetSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return new RemoteWindowMediaBudgetSnapshot(
                    peers.Count,
                    reservedFrames,
                    reservedBytes);
            }
        }
    }

    internal void RegisterPeer(DeviceId peerId)
    {
        ArgumentNullException.ThrowIfNull(peerId);
        lock (gate)
        {
            if (peers.ContainsKey(peerId))
            {
                throw new InvalidOperationException(
                    "A Remote Window media queue already exists for this peer.");
            }

            if (peers.Count >= maximumPeers)
            {
                throw new InvalidOperationException(
                    $"A Remote Window media session cannot exceed {maximumPeers} remote peers.");
            }

            peers.Add(peerId, new PeerReservation());
        }
    }

    internal RemoteWindowMediaEnqueueStatus TryReserve(
        DeviceId peerId,
        int payloadBytes)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peerId, out PeerReservation? peer))
            {
                return RemoteWindowMediaEnqueueStatus.Closed;
            }

            if (peer.Frames >= RemoteWindowMediaOutboundQueue.MaximumFrames
                || peer.Bytes + payloadBytes
                    > RemoteWindowMediaOutboundQueue.MaximumBytes)
            {
                return RemoteWindowMediaEnqueueStatus.PeerBackpressure;
            }

            if (reservedFrames >= maximumFrames
                || reservedBytes + payloadBytes > maximumBytes)
            {
                return RemoteWindowMediaEnqueueStatus.SessionBackpressure;
            }

            peer.Frames++;
            peer.Bytes = checked(peer.Bytes + payloadBytes);
            reservedFrames++;
            reservedBytes = checked(reservedBytes + payloadBytes);
            return RemoteWindowMediaEnqueueStatus.Accepted;
        }
    }

    internal void Release(DeviceId peerId, int payloadBytes)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peerId, out PeerReservation? peer)
                || peer.Frames < 1
                || peer.Bytes < payloadBytes
                || reservedFrames < 1
                || reservedBytes < payloadBytes)
            {
                throw new InvalidOperationException(
                    "A Remote Window media reservation cannot be released twice.");
            }

            peer.Frames--;
            peer.Bytes -= payloadBytes;
            reservedFrames--;
            reservedBytes -= payloadBytes;
        }
    }

    internal void UnregisterPeer(DeviceId peerId)
    {
        lock (gate)
        {
            if (!peers.Remove(peerId, out PeerReservation? peer))
            {
                return;
            }

            if (peer.Frames != 0 || peer.Bytes != 0)
            {
                peers.Add(peerId, peer);
                throw new InvalidOperationException(
                    "A Remote Window media peer still owns reservations.");
            }
        }
    }

    private sealed class PeerReservation
    {
        public long Bytes { get; set; }

        public int Frames { get; set; }
    }
}

[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Outbound Queue is the frozen domain term for this bounded media worker.")]
public sealed class RemoteWindowMediaOutboundQueue : IAsyncDisposable
{
    public const long MaximumBytes = 512L * 1024;
    public const int MaximumFrames = 8;
    private readonly RemoteWindowMediaSessionBudget budget;
    private readonly Channel<QueuedFrame> entries;
    private readonly Lock gate = new();
    private readonly DeviceId peerId;
    private readonly CancellationTokenSource shutdown = new();
    private readonly IRemoteWindowMediaSink sink;
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task worker;
    private bool closed;
    private int disposeStarted;
    private int sinkDisposed;

    public RemoteWindowMediaOutboundQueue(
        RemoteWindowMediaSessionBudget budget,
        DeviceId peerId,
        IRemoteWindowMediaSink sink)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(peerId);
        ArgumentNullException.ThrowIfNull(sink);
        budget.RegisterPeer(peerId);
        this.budget = budget;
        this.peerId = peerId;
        this.sink = sink;
        entries = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(MaximumFrames)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        worker = ProcessAsync();
    }

    public RemoteWindowMediaEnqueueResult TryEnqueue(RemoteWindowMediaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (gate)
        {
            if (closed)
            {
                return RemoteWindowMediaEnqueueResult.CreateRejected(
                    RemoteWindowMediaEnqueueStatus.Closed);
            }

            int payloadBytes = frame.PayloadLength;
            RemoteWindowMediaEnqueueStatus reservation = budget.TryReserve(
                peerId,
                payloadBytes);
            if (reservation != RemoteWindowMediaEnqueueStatus.Accepted)
            {
                return RemoteWindowMediaEnqueueResult.CreateRejected(reservation);
            }

            RemoteWindowMediaFrame? ownedFrame = null;
            try
            {
                var completion = new TaskCompletionSource<RemoteWindowMediaDeliveryOutcome>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                ownedFrame = frame.Clone();
                var queued = new QueuedFrame(
                    ownedFrame,
                    payloadBytes,
                    completion);
                if (!entries.Writer.TryWrite(queued))
                {
                    throw new InvalidOperationException(
                        "The reserved Remote Window media queue rejected an entry.");
                }

                ownedFrame = null;
                return RemoteWindowMediaEnqueueResult.CreateAccepted(completion.Task);
            }
            catch
            {
                ownedFrame?.Dispose();
                budget.Release(peerId, payloadBytes);
                throw;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
        {
            _ = DisposeAndCompleteAsync();
        }

        return new ValueTask(disposalCompletion.Task);
    }

    private async Task DisposeAndCompleteAsync()
    {
        try
        {
            await DisposeResourcesAsync().ConfigureAwait(false);
            disposalCompletion.TrySetResult();
        }
        catch (Exception failure)
        {
            disposalCompletion.TrySetException(failure);
        }
    }

    private async Task DisposeResourcesAsync()
    {
        lock (gate)
        {
            closed = true;
            entries.Writer.TryComplete();
        }

        List<Exception>? cleanupFailures = null;
        try
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            cleanupFailures = [failure];
        }

        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            (cleanupFailures ??= []).Add(failure);
        }

        try
        {
            await DisposeSinkAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            (cleanupFailures ??= []).Add(failure);
        }

        try
        {
            budget.UnregisterPeer(peerId);
        }
        catch (Exception failure)
        {
            (cleanupFailures ??= []).Add(failure);
        }

        try
        {
            shutdown.Dispose();
        }
        catch (Exception failure)
        {
            (cleanupFailures ??= []).Add(failure);
        }

        if (cleanupFailures is { Count: 1 })
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                    cleanupFailures[0])
                .Throw();
        }

        if (cleanupFailures is { Count: > 1 })
        {
            throw new AggregateException(
                "Remote Window media queue cleanup failed.",
                cleanupFailures);
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (QueuedFrame queued in entries.Reader.ReadAllAsync(
                shutdown.Token).ConfigureAwait(false))
            {
                RemoteWindowMediaDeliveryOutcome outcome;
                try
                {
                    await sink.SendAsync(queued.Frame, shutdown.Token)
                        .ConfigureAwait(false);
                    outcome = RemoteWindowMediaDeliveryOutcome.Sent;
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    outcome = RemoteWindowMediaDeliveryOutcome.Cancelled;
                }
                catch
                {
                    outcome = RemoteWindowMediaDeliveryOutcome.Failed;
                }
                finally
                {
                    queued.Frame.Dispose();
                }

                budget.Release(peerId, queued.PayloadBytes);
                queued.Completion.TrySetResult(outcome);
                if (outcome != RemoteWindowMediaDeliveryOutcome.Sent)
                {
                    MarkClosed();
                    Drain(outcome);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            Drain(RemoteWindowMediaDeliveryOutcome.Cancelled);
        }
    }

    private void Drain(RemoteWindowMediaDeliveryOutcome outcome)
    {
        while (entries.Reader.TryRead(out QueuedFrame? queued))
        {
            queued.Frame.Dispose();
            budget.Release(peerId, queued.PayloadBytes);
            queued.Completion.TrySetResult(outcome);
        }
    }

    private async ValueTask DisposeSinkAsync()
    {
        if (Interlocked.Exchange(ref sinkDisposed, 1) != 0)
        {
            return;
        }

        if (sink is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (sink is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void MarkClosed()
    {
        lock (gate)
        {
            closed = true;
            entries.Writer.TryComplete();
        }
    }

    private sealed record QueuedFrame(
        RemoteWindowMediaFrame Frame,
        int PayloadBytes,
        TaskCompletionSource<RemoteWindowMediaDeliveryOutcome> Completion);
}
