using System.Security.Cryptography;
using Flowspan.Domain;

namespace Flowspan.Transport;

public sealed class RemoteWindowLogicalVideoFrame : IDisposable
{
    private readonly Lock gate = new();
    private readonly int payloadLength;
    private byte[]? payload;

    private RemoteWindowLogicalVideoFrame(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        byte[] payload)
    {
        SessionId = sessionId;
        ActivityId = activityId;
        FirstSequence = firstSequence;
        payloadLength = payload.Length;
        this.payload = payload;
    }

    public ActivityId ActivityId { get; }

    public ulong FirstSequence { get; }

    public int PayloadLength => payloadLength;

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowLogicalVideoFrame Create(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        ReadOnlySpan<byte> payload)
    {
        Validate(sessionId, activityId, firstSequence, payload.Length, nameof(payload));
        byte[] ownedPayload = GC.AllocateUninitializedArray<byte>(payload.Length);
        try
        {
            payload.CopyTo(ownedPayload);
            return new RemoteWindowLogicalVideoFrame(
                sessionId,
                activityId,
                firstSequence,
                ownedPayload);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(ownedPayload);
            throw;
        }
    }

    public byte[] ExportPayload()
    {
        lock (gate)
        {
            byte[] current = RequirePayload();
            byte[] exported = GC.AllocateUninitializedArray<byte>(current.Length);
            try
            {
                current.CopyTo(exported, 0);
                return exported;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(exported);
                throw;
            }
        }
    }

    public void Dispose()
    {
        byte[]? released;
        lock (gate)
        {
            released = payload;
            payload = null;
        }

        if (released is not null)
        {
            CryptographicOperations.ZeroMemory(released);
        }
    }

    public override string ToString() =>
        $"{nameof(RemoteWindowLogicalVideoFrame)} {{ FirstSequence = "
        + $"{FirstSequence}, PayloadLength = {payloadLength} }}";

    internal static RemoteWindowLogicalVideoFrame TakeOwnership(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        byte[] ownedPayload)
    {
        ArgumentNullException.ThrowIfNull(ownedPayload);
        Validate(
            sessionId,
            activityId,
            firstSequence,
            ownedPayload.Length,
            nameof(ownedPayload));
        return new RemoteWindowLogicalVideoFrame(
            sessionId,
            activityId,
            firstSequence,
            ownedPayload);
    }

    internal RemoteWindowVideoFrameChunks CreateChunks(
        IRemoteWindowVideoBufferOperations buffers)
    {
        lock (gate)
        {
            return RemoteWindowVideoFrameChunker.Chunk(
                SessionId,
                ActivityId,
                FirstSequence,
                RequirePayload(),
                buffers);
        }
    }

    private static void Validate(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        ulong firstSequence,
        int payloadBytes,
        string payloadParameterName)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentOutOfRangeException.ThrowIfZero(firstSequence);
        if (payloadBytes is < 1
            or > RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes)
        {
            throw new ArgumentOutOfRangeException(
                payloadParameterName,
                $"A Remote Window logical video frame must contain 1 to "
                + $"{RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes} bytes.");
        }

        int chunks = checked(
            (payloadBytes + RemoteWindowMediaFrame.MaximumPayloadBytes - 1)
            / RemoteWindowMediaFrame.MaximumPayloadBytes);
        if (firstSequence > ulong.MaxValue - checked((ulong)(chunks - 1)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstSequence),
                "The Remote Window logical video frame sequence range is invalid.");
        }
    }

    private byte[] RequirePayload()
    {
        ObjectDisposedException.ThrowIf(payload is null, this);
        return payload;
    }
}

public enum RemoteWindowLogicalVideoFrameOutcome
{
    Sent,
    Replaced,
    Dropped,
    Failed,
    Cancelled,
}

public sealed class RemoteWindowLogicalVideoFrameSender : IAsyncDisposable
{
    public const int MaximumPendingFrames = 1;

    private readonly IRemoteWindowVideoBufferOperations buffers;
    private readonly TaskCompletionSource disposalCompletion = NewCompletion();
    private readonly Lock gate = new();
    private readonly RemoteWindowMediaOutboundQueue queue;
    private readonly Lazy<Task> queueDisposal;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task worker;
    private Submission? active;
    private bool closed;
    private int disposeStarted;
    private Submission? pending;
    private TaskCompletionSource pendingChanged = NewCompletion();
    private RemoteWindowLogicalVideoFrameOutcome terminalOutcome =
        RemoteWindowLogicalVideoFrameOutcome.Cancelled;

    public RemoteWindowLogicalVideoFrameSender(
        RemoteWindowMediaSessionBudget budget,
        DeviceId peerId,
        IRemoteWindowMediaSink sink) : this(
        budget,
        peerId,
        sink,
        RemoteWindowVideoBufferOperations.Instance)
    {
    }

    internal RemoteWindowLogicalVideoFrameSender(
        RemoteWindowMediaSessionBudget budget,
        DeviceId peerId,
        IRemoteWindowMediaSink sink,
        IRemoteWindowVideoBufferOperations buffers)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(peerId);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(buffers);
        this.buffers = buffers;
        queue = new RemoteWindowMediaOutboundQueue(budget, peerId, sink);
        queueDisposal = new Lazy<Task>(
            StartQueueDisposal,
            LazyThreadSafetyMode.ExecutionAndPublication);
        worker = ProcessAsync();
    }

    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    public Task<RemoteWindowLogicalVideoFrameOutcome> TakeOwnership(
        RemoteWindowLogicalVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var submission = new Submission(frame);
        Submission? replaced = null;
        RemoteWindowLogicalVideoFrameOutcome? rejected = null;
        lock (gate)
        {
            if (closed)
            {
                rejected = terminalOutcome;
            }
            else
            {
                replaced = pending;
                pending = submission;
                if (replaced is null)
                {
                    PulsePendingChanged();
                }
            }
        }

        replaced?.Settle(RemoteWindowLogicalVideoFrameOutcome.Replaced);
        if (rejected is not null)
        {
            submission.Settle(rejected.Value);
        }

        return submission.Completion;
    }

    public void StopNow() =>
        Close(RemoteWindowLogicalVideoFrameOutcome.Cancelled);

    public ValueTask DisposeAsync()
    {
        StopNow();
        if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
        {
            _ = DisposeAndCompleteAsync();
        }

        return new ValueTask(disposalCompletion.Task);
    }

    public override string ToString() => IsClosed
        ? "Remote Window logical video frame sender (closed)"
        : "Remote Window logical video frame sender (open)";

    private void Close(RemoteWindowLogicalVideoFrameOutcome outcome)
    {
        Submission? activeToCancel;
        Submission? pendingToSettle;
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            terminalOutcome = outcome;
            activeToCancel = active;
            pendingToSettle = pending;
            pending = null;
            PulsePendingChanged();
        }

        pendingToSettle?.Settle(outcome);
        activeToCancel?.CancelOwnedPayload();
        shutdown.Cancel();
        _ = queueDisposal.Value;
    }

    private async Task ProcessAsync()
    {
        Submission? current = null;
        try
        {
            while (true)
            {
                Task? waitForPending = null;
                lock (gate)
                {
                    if (closed)
                    {
                        return;
                    }

                    if (pending is null)
                    {
                        waitForPending = pendingChanged.Task;
                    }
                    else
                    {
                        current = pending;
                        pending = null;
                        active = current;
                    }
                }

                if (waitForPending is not null)
                {
                    await waitForPending.ConfigureAwait(false);
                    continue;
                }

                RemoteWindowLogicalVideoFrameOutcome outcome =
                    await SendAsync(current!).ConfigureAwait(false);
                if (outcome is RemoteWindowLogicalVideoFrameOutcome.Failed
                    or RemoteWindowLogicalVideoFrameOutcome.Cancelled)
                {
                    Close(outcome);
                }

                current!.Settle(outcome);
                ClearActive(current);
                current = null;
            }
        }
        catch
        {
            current?.Settle(RemoteWindowLogicalVideoFrameOutcome.Failed);
            Close(RemoteWindowLogicalVideoFrameOutcome.Failed);
            throw;
        }
        finally
        {
            if (current is not null)
            {
                ClearActive(current);
            }
        }
    }

    private async Task<RemoteWindowLogicalVideoFrameOutcome> SendAsync(
        Submission submission)
    {
        int chunkCount;
        try
        {
            chunkCount = submission.CreateChunks(buffers).Count;
        }
        catch (Exception) when (submission.IsCancellationRequested)
        {
            return RemoteWindowLogicalVideoFrameOutcome.Cancelled;
        }
        catch
        {
            return RemoteWindowLogicalVideoFrameOutcome.Failed;
        }

        for (var index = 0; index < chunkCount; index++)
        {
            if (submission.IsCancellationRequested)
            {
                return RemoteWindowLogicalVideoFrameOutcome.Cancelled;
            }

            RemoteWindowMediaEnqueueResult enqueue;
            RemoteWindowMediaFrame? chunk = null;
            try
            {
                chunk = submission.TakeChunk(index);
                enqueue = queue.TryEnqueue(chunk);
            }
            catch (Exception) when (submission.IsCancellationRequested)
            {
                return RemoteWindowLogicalVideoFrameOutcome.Cancelled;
            }
            catch
            {
                return RemoteWindowLogicalVideoFrameOutcome.Failed;
            }
            finally
            {
                if (chunk is not null)
                {
                    submission.ReleaseCurrentChunk(chunk);
                }
            }

            if (!enqueue.Accepted)
            {
                if (enqueue.Status is RemoteWindowMediaEnqueueStatus.PeerBackpressure
                    or RemoteWindowMediaEnqueueStatus.SessionBackpressure)
                {
                    return RemoteWindowLogicalVideoFrameOutcome.Dropped;
                }

                return ReadTerminalOutcome();
            }

            RemoteWindowMediaDeliveryOutcome delivery;
            try
            {
                delivery = await enqueue.Completion!.ConfigureAwait(false);
            }
            catch
            {
                return RemoteWindowLogicalVideoFrameOutcome.Failed;
            }

            if (delivery != RemoteWindowMediaDeliveryOutcome.Sent)
            {
                return delivery == RemoteWindowMediaDeliveryOutcome.Cancelled
                    ? ReadTerminalOutcome()
                    : RemoteWindowLogicalVideoFrameOutcome.Failed;
            }
        }

        return submission.IsCancellationRequested
            ? RemoteWindowLogicalVideoFrameOutcome.Cancelled
            : RemoteWindowLogicalVideoFrameOutcome.Sent;
    }

    private void ClearActive(Submission submission)
    {
        lock (gate)
        {
            if (ReferenceEquals(active, submission))
            {
                active = null;
            }
        }
    }

    private RemoteWindowLogicalVideoFrameOutcome ReadTerminalOutcome()
    {
        lock (gate)
        {
            return closed
                ? terminalOutcome
                : RemoteWindowLogicalVideoFrameOutcome.Failed;
        }
    }

    private void PulsePendingChanged()
    {
        TaskCompletionSource completed = pendingChanged;
        pendingChanged = NewCompletion();
        completed.TrySetResult();
    }

    private Task StartQueueDisposal()
    {
        try
        {
            return queue.DisposeAsync().AsTask();
        }
        catch (Exception failure)
        {
            return Task.FromException(failure);
        }
    }

    private async Task DisposeAndCompleteAsync()
    {
        var failures = new List<Exception>(capacity: 2);
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            await queueDisposal.Value.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        shutdown.Dispose();
        if (failures.Count == 0)
        {
            disposalCompletion.TrySetResult();
        }
        else
        {
            disposalCompletion.TrySetException(failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    "Remote Window logical video frame sender cleanup failed.",
                    failures));
        }
    }

    private static TaskCompletionSource NewCompletion() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Submission
    {
        private readonly TaskCompletionSource<RemoteWindowLogicalVideoFrameOutcome>
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock gate = new();
        private RemoteWindowVideoFrameChunks? chunks;
        private RemoteWindowMediaFrame? currentChunk;
        private RemoteWindowLogicalVideoFrame? frame;
        private bool cancellationRequested;
        private bool settled;

        public Submission(RemoteWindowLogicalVideoFrame frame)
        {
            this.frame = frame;
        }

        public Task<RemoteWindowLogicalVideoFrameOutcome> Completion => completion.Task;

        public bool IsCancellationRequested
        {
            get
            {
                lock (gate)
                {
                    return cancellationRequested;
                }
            }
        }

        public RemoteWindowVideoFrameChunks CreateChunks(
            IRemoteWindowVideoBufferOperations buffers)
        {
            lock (gate)
            {
                if (cancellationRequested || settled)
                {
                    throw new OperationCanceledException(
                        "The logical video frame submission was cancelled.");
                }

                RemoteWindowLogicalVideoFrame current = frame
                    ?? throw new InvalidOperationException(
                        "The logical video frame was already chunked.");
                try
                {
                    chunks = current.CreateChunks(buffers);
                    return chunks;
                }
                finally
                {
                    current.Dispose();
                    frame = null;
                }
            }
        }

        public RemoteWindowMediaFrame TakeChunk(int index)
        {
            lock (gate)
            {
                if (cancellationRequested || settled)
                {
                    throw new OperationCanceledException(
                        "The logical video frame submission was cancelled.");
                }

                RemoteWindowVideoFrameChunks current = chunks
                    ?? throw new InvalidOperationException(
                        "The logical video frame was not chunked.");
                currentChunk = current.Take(index);
                return currentChunk;
            }
        }

        public void ReleaseCurrentChunk(RemoteWindowMediaFrame released)
        {
            lock (gate)
            {
                if (ReferenceEquals(currentChunk, released))
                {
                    currentChunk = null;
                }
            }

            released.Dispose();
        }

        public void CancelOwnedPayload()
        {
            RemoteWindowLogicalVideoFrame? frameToDispose;
            RemoteWindowVideoFrameChunks? chunksToDispose;
            RemoteWindowMediaFrame? chunkToDispose;
            lock (gate)
            {
                cancellationRequested = true;
                frameToDispose = frame;
                frame = null;
                chunksToDispose = chunks;
                chunks = null;
                chunkToDispose = currentChunk;
                currentChunk = null;
            }

            chunkToDispose?.Dispose();
            chunksToDispose?.Dispose();
            frameToDispose?.Dispose();
        }

        public void Settle(RemoteWindowLogicalVideoFrameOutcome outcome)
        {
            RemoteWindowLogicalVideoFrame? frameToDispose;
            RemoteWindowVideoFrameChunks? chunksToDispose;
            RemoteWindowMediaFrame? chunkToDispose;
            lock (gate)
            {
                if (settled)
                {
                    return;
                }

                settled = true;
                cancellationRequested |=
                    outcome == RemoteWindowLogicalVideoFrameOutcome.Cancelled;
                frameToDispose = frame;
                frame = null;
                chunksToDispose = chunks;
                chunks = null;
                chunkToDispose = currentChunk;
                currentChunk = null;
            }

            chunkToDispose?.Dispose();
            chunksToDispose?.Dispose();
            frameToDispose?.Dispose();
            completion.TrySetResult(outcome);
        }
    }
}
