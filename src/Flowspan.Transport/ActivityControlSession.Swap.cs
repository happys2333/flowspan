using System.Collections.Concurrent;
using System.Net.Sockets;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

internal sealed partial class ActivityControlSession
{
    private readonly ConcurrentDictionary<CorrelationId, PendingSwapDecision>
        pendingSwapDecisions = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingSwapPrepare>
        pendingSwapPrepares = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingSwapSnapshot>
        pendingSwapSnapshots = new();
    private readonly ISwapEndpointPeer? swapPeer;

    public async ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
        QueryActivityAsync(
            DeviceId requestingDeviceId,
            SwapActivitySnapshotQuery query,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SwapDelivery.NotDelivered<SwapActivitySnapshotResult>();
        }

        if (requestingDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A Swap snapshot requester must match the authenticated local Device.");
        }

        if (query.TargetDeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A Swap snapshot target must match the authenticated peer.");
        }

        var pendingSnapshot = new PendingSwapSnapshot(query);
        ReserveCorrelation(query.Context.CorrelationId);
        if (!pendingSwapSnapshots.TryAdd(
                query.Context.CorrelationId,
                pendingSnapshot))
        {
            ReleaseCorrelation(query.Context.CorrelationId);
            throw new InvalidOperationException(
                "The Swap snapshot could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SwapDelivery.NotDelivered<SwapActivitySnapshotResult>();
            }

            ControlMessage message = SwapControlMessageCodec.CreateSnapshotQuery(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                query,
                timeProvider.GetUtcNow());
            if (!await SendSwapWithinDeadlineAsync(
                    message,
                    query.Context.Deadline,
                    cancellationToken).ConfigureAwait(false))
            {
                return await ResolveFailedSwapSendAsync(
                    pendingSnapshot.Completion.Task).ConfigureAwait(false);
            }

            sent = true;
            return await WaitForSwapResponseAsync(
                pendingSnapshot.Completion.Task,
                query.Context.Deadline,
                () =>
                {
                    if (pendingSwapSnapshots.TryRemove(
                            new KeyValuePair<CorrelationId, PendingSwapSnapshot>(
                                query.Context.CorrelationId,
                                pendingSnapshot)))
                    {
                        ReleaseCorrelation(query.Context.CorrelationId);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            return await ResolveFailedSwapSendAsync(
                pendingSnapshot.Completion.Task).ConfigureAwait(false);
        }
        finally
        {
            if (!sent
                && pendingSwapSnapshots.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSwapSnapshot>(
                        query.Context.CorrelationId,
                        pendingSnapshot)))
            {
                ReleaseCorrelation(query.Context.CorrelationId);
            }
        }
    }

    public async ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
        DeviceId senderDeviceId,
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SwapDelivery.NotDelivered<SwapPrepareResult>();
        }

        if (senderDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A Swap Prepare sender must match the authenticated local Device.");
        }

        if (command.OriginalActivity.Placement.DeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A Swap Prepare target must match the authenticated peer.");
        }

        var pendingPrepare = new PendingSwapPrepare(command);
        ReserveCorrelation(command.CorrelationId);
        if (!pendingSwapPrepares.TryAdd(command.CorrelationId, pendingPrepare))
        {
            ReleaseCorrelation(command.CorrelationId);
            throw new InvalidOperationException(
                "The Swap Prepare could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SwapDelivery.NotDelivered<SwapPrepareResult>();
            }

            ControlMessage message = SwapControlMessageCodec.CreatePrepare(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                command,
                timeProvider.GetUtcNow());
            if (!await SendSwapWithinDeadlineAsync(
                    message,
                    command.ExpiresAt,
                    cancellationToken).ConfigureAwait(false))
            {
                return await ResolveFailedSwapSendAsync(
                    pendingPrepare.Completion.Task).ConfigureAwait(false);
            }

            sent = true;
            return await WaitForSwapResponseAsync(
                pendingPrepare.Completion.Task,
                command.ExpiresAt,
                () =>
                {
                    if (pendingSwapPrepares.TryRemove(
                            new KeyValuePair<CorrelationId, PendingSwapPrepare>(
                                command.CorrelationId,
                                pendingPrepare)))
                    {
                        ReleaseCorrelation(command.CorrelationId);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            return await ResolveFailedSwapSendAsync(
                pendingPrepare.Completion.Task).ConfigureAwait(false);
        }
        finally
        {
            if (!sent
                && pendingSwapPrepares.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSwapPrepare>(
                        command.CorrelationId,
                        pendingPrepare)))
            {
                ReleaseCorrelation(command.CorrelationId);
            }
        }
    }

    public async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
        DeviceId senderDeviceId,
        CorrelationId correlationId,
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(decision);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SwapDelivery.NotDelivered<SwapApplyResult>();
        }

        if (senderDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A Swap decision sender must match the authenticated local Device.");
        }

        if (!decision.TryGetReservationToken(TargetDeviceId, out _))
        {
            throw new InvalidOperationException(
                "A Swap decision target must match the authenticated peer.");
        }

        ControlMessage message = SwapControlMessageCodec.CreateDecision(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            correlationId,
            TargetDeviceId,
            decision,
            timeProvider.GetUtcNow());
        DateTimeOffset acknowledgementDeadline = message.SentAt.Add(
            SwapControlMessageCodec.DecisionAcknowledgementTimeout);
        var pendingDecision = new PendingSwapDecision(
            correlationId,
            decision,
            acknowledgementDeadline);
        ReserveCorrelation(correlationId);
        if (!pendingSwapDecisions.TryAdd(correlationId, pendingDecision))
        {
            ReleaseCorrelation(correlationId);
            throw new InvalidOperationException(
                "The Swap decision could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SwapDelivery.NotDelivered<SwapApplyResult>();
            }

            if (!await SendSwapWithinDeadlineAsync(
                    message,
                    acknowledgementDeadline,
                    cancellationToken).ConfigureAwait(false))
            {
                return await ResolveFailedSwapSendAsync(
                    pendingDecision.Completion.Task).ConfigureAwait(false);
            }

            sent = true;
            return await WaitForSwapResponseAsync(
                pendingDecision.Completion.Task,
                acknowledgementDeadline,
                () =>
                {
                    if (pendingSwapDecisions.TryRemove(
                            new KeyValuePair<CorrelationId, PendingSwapDecision>(
                                correlationId,
                                pendingDecision)))
                    {
                        ReleaseCorrelation(correlationId);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            return await ResolveFailedSwapSendAsync(
                pendingDecision.Completion.Task).ConfigureAwait(false);
        }
        finally
        {
            if (!sent
                && pendingSwapDecisions.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSwapDecision>(
                        correlationId,
                        pendingDecision)))
            {
                ReleaseCorrelation(correlationId);
            }
        }
    }

    private async ValueTask HandleSwapSnapshotAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        EnsureSwapEnvelopeIsCurrent(message);
        ISwapEndpointPeer endpoint = swapPeer
            ?? throw new InvalidDataException(
                "The local Activity session does not expose a Swap endpoint.");
        SwapActivitySnapshotQuery query = SwapControlMessageCodec.DecodeSnapshotQuery(
            message,
            connection.LocalDeviceId);
        SwapActivitySnapshotResult result = await endpoint.QueryActivityAsync(
            connection.PeerDeviceId,
            query,
            cancellationToken).ConfigureAwait(false);
        if (result.OperationId != query.Context.OperationId
            || result.CorrelationId != query.Context.CorrelationId
            || result.RequestingDeviceId != connection.PeerDeviceId
            || result.TargetDeviceId != connection.LocalDeviceId
            || result.RequestedActivityId != query.ActivityId)
        {
            throw new InvalidDataException(
                "The local Swap endpoint returned a snapshot for another query.");
        }

        ControlMessage response = SwapControlMessageCodec.CreateSnapshotResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleSwapPrepareAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        EnsureSwapEnvelopeIsCurrent(message);
        ISwapEndpointPeer endpoint = swapPeer
            ?? throw new InvalidDataException(
                "The local Activity session does not expose a Swap endpoint.");
        SwapPrepareCommand command = SwapControlMessageCodec.DecodePrepare(
            message,
            connection.LocalDeviceId);
        SwapPrepareResult result = await endpoint.PrepareAsync(
            connection.PeerDeviceId,
            command,
            cancellationToken).ConfigureAwait(false);
        ControlMessage response = SwapControlMessageCodec.CreatePrepareResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            connection.PeerDeviceId,
            command,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleSwapDecisionAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        EnsureSwapEnvelopeIsCurrent(message);
        ISwapEndpointPeer endpoint = swapPeer
            ?? throw new InvalidDataException(
                "The local Activity session does not expose a Swap endpoint.");
        SwapDecision decision = SwapControlMessageCodec.DecodeDecision(
            message,
            connection.LocalDeviceId);
        SwapApplyResult result = await endpoint.ApplyDecisionAsync(
            connection.PeerDeviceId,
            message.CorrelationId,
            decision,
            cancellationToken).ConfigureAwait(false);
        ControlMessage response = SwapControlMessageCodec.CreateDecisionResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            connection.PeerDeviceId,
            message.CorrelationId,
            decision,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleSwapSnapshotResult(ControlMessage message)
    {
        if (!pendingSwapSnapshots.TryGetValue(
                message.CorrelationId,
                out PendingSwapSnapshot? pendingSnapshot))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Swap snapshot result.");
        }

        EnsureSwapEnvelopeIsCurrent(message);
        EnsureSwapResponseIsTimely(
            pendingSnapshot.Query.Context.Deadline,
            () =>
            {
                if (pendingSwapSnapshots.TryRemove(
                        new KeyValuePair<CorrelationId, PendingSwapSnapshot>(
                            message.CorrelationId,
                            pendingSnapshot)))
                {
                    ReleaseCorrelation(message.CorrelationId);
                    pendingSnapshot.Completion.TrySetResult(
                        SwapDelivery.AcknowledgementLost<SwapActivitySnapshotResult>());
                }
            },
            "snapshot");

        SwapActivitySnapshotResult result =
            SwapControlMessageCodec.DecodeSnapshotResult(
                message,
                connection.LocalDeviceId,
                pendingSnapshot.Query);
        if (!pendingSwapSnapshots.TryRemove(
                new KeyValuePair<CorrelationId, PendingSwapSnapshot>(
                    message.CorrelationId,
                    pendingSnapshot)))
        {
            throw new InvalidDataException(
                "The Swap snapshot result raced with session shutdown.");
        }

        ReleaseCorrelation(message.CorrelationId);
        pendingSnapshot.Completion.TrySetResult(SwapDelivery.Acknowledged(result));
    }

    private void HandleSwapPrepareResult(ControlMessage message)
    {
        if (!pendingSwapPrepares.TryGetValue(
                message.CorrelationId,
                out PendingSwapPrepare? pendingPrepare))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Swap Prepare result.");
        }

        EnsureSwapEnvelopeIsCurrent(message);
        EnsureSwapResponseIsTimely(
            pendingPrepare.Command.ExpiresAt,
            () =>
            {
                if (pendingSwapPrepares.TryRemove(
                        new KeyValuePair<CorrelationId, PendingSwapPrepare>(
                            message.CorrelationId,
                            pendingPrepare)))
                {
                    ReleaseCorrelation(message.CorrelationId);
                    pendingPrepare.Completion.TrySetResult(
                        SwapDelivery.AcknowledgementLost<SwapPrepareResult>());
                }
            },
            "Prepare");

        SwapPrepareResult result = SwapControlMessageCodec.DecodePrepareResult(
            message,
            connection.LocalDeviceId,
            pendingPrepare.Command);
        if (!pendingSwapPrepares.TryRemove(
                new KeyValuePair<CorrelationId, PendingSwapPrepare>(
                    message.CorrelationId,
                    pendingPrepare)))
        {
            throw new InvalidDataException(
                "The Swap Prepare result raced with session shutdown.");
        }

        ReleaseCorrelation(message.CorrelationId);
        pendingPrepare.Completion.TrySetResult(SwapDelivery.Acknowledged(result));
    }

    private void HandleSwapDecisionResult(ControlMessage message)
    {
        if (!pendingSwapDecisions.TryGetValue(
                message.CorrelationId,
                out PendingSwapDecision? pendingDecision))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Swap decision result.");
        }

        EnsureSwapEnvelopeIsCurrent(message);
        EnsureSwapResponseIsTimely(
            pendingDecision.AcknowledgementDeadline,
            () =>
            {
                if (pendingSwapDecisions.TryRemove(
                        new KeyValuePair<CorrelationId, PendingSwapDecision>(
                            message.CorrelationId,
                            pendingDecision)))
                {
                    ReleaseCorrelation(message.CorrelationId);
                    pendingDecision.Completion.TrySetResult(
                        SwapDelivery.AcknowledgementLost<SwapApplyResult>());
                }
            },
            "decision");

        SwapApplyResult result = SwapControlMessageCodec.DecodeDecisionResult(
            message,
            connection.LocalDeviceId,
            pendingDecision.CorrelationId,
            connection.PeerDeviceId,
            pendingDecision.Decision);
        if (!pendingSwapDecisions.TryRemove(
                new KeyValuePair<CorrelationId, PendingSwapDecision>(
                    message.CorrelationId,
                    pendingDecision)))
        {
            throw new InvalidDataException(
                "The Swap decision result raced with session shutdown.");
        }

        ReleaseCorrelation(message.CorrelationId);
        pendingDecision.Completion.TrySetResult(SwapDelivery.Acknowledged(result));
    }

    private void CompleteSwapPendingAsUncertain()
    {
        foreach ((CorrelationId correlationId, PendingSwapSnapshot snapshot)
                 in pendingSwapSnapshots)
        {
            if (pendingSwapSnapshots.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSwapSnapshot>(
                        correlationId,
                        snapshot)))
            {
                ReleaseCorrelation(correlationId);
                snapshot.Completion.TrySetResult(
                    SwapDelivery.AcknowledgementLost<SwapActivitySnapshotResult>());
            }
        }

        foreach ((CorrelationId correlationId, PendingSwapPrepare prepare)
                 in pendingSwapPrepares)
        {
            if (pendingSwapPrepares.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSwapPrepare>(
                        correlationId,
                        prepare)))
            {
                ReleaseCorrelation(correlationId);
                prepare.Completion.TrySetResult(
                    SwapDelivery.AcknowledgementLost<SwapPrepareResult>());
            }
        }

        foreach ((CorrelationId correlationId, PendingSwapDecision decision)
                 in pendingSwapDecisions)
        {
            if (pendingSwapDecisions.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSwapDecision>(
                        correlationId,
                        decision)))
            {
                ReleaseCorrelation(correlationId);
                decision.Completion.TrySetResult(
                    SwapDelivery.AcknowledgementLost<SwapApplyResult>());
            }
        }
    }

    private async ValueTask<SwapDeliveryResult<T>> WaitForSwapResponseAsync<T>(
        Task<SwapDeliveryResult<T>> completion,
        DateTimeOffset deadline,
        Action removePending,
        CancellationToken cancellationToken)
        where T : class
    {
        TimeSpan remaining = deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            removePending();
            Cancel();
            return SwapDelivery.AcknowledgementLost<T>();
        }

        try
        {
            return await completion.WaitAsync(
                remaining,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            removePending();
            Cancel();
            return SwapDelivery.AcknowledgementLost<T>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Cancel();
            throw;
        }
    }

    private async ValueTask<bool> SendSwapWithinDeadlineAsync(
        ControlMessage message,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            Cancel();
            return false;
        }

        CancellationTokenSource sendCancellation;
        try
        {
            sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        }
        catch (ObjectDisposedException) when (
            Volatile.Read(ref disposed) != 0
            || Volatile.Read(ref stopped) != 0)
        {
            return false;
        }

        using (sendCancellation)
        {
            return await SendSwapWithinDeadlineCoreAsync(
                message,
                deadline,
                remaining,
                sendCancellation,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> SendSwapWithinDeadlineCoreAsync(
        ControlMessage message,
        DateTimeOffset deadline,
        TimeSpan remaining,
        CancellationTokenSource sendCancellation,
        CancellationToken cancellationToken)
    {
        ValueTask sending = connection.SendAsync(message, sendCancellation.Token);
        if (sending.IsCompletedSuccessfully)
        {
            await sending.ConfigureAwait(false);
            if (timeProvider.GetUtcNow() < deadline)
            {
                return true;
            }

            Cancel();
            return false;
        }

        Task sendingTask = sending.AsTask();
        try
        {
            await sendingTask.WaitAsync(
                remaining,
                timeProvider,
                sendCancellation.Token).ConfigureAwait(false);
            if (timeProvider.GetUtcNow() < deadline)
            {
                return true;
            }

            Cancel();
            return false;
        }
        catch (TimeoutException)
        {
            sendCancellation.Cancel();
            ObserveAbandonedSend(sendingTask);
            Cancel();
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveAbandonedSend(sendingTask);
            Cancel();
            throw;
        }
        catch (OperationCanceledException) when (
            lifetimeCancellation.IsCancellationRequested)
        {
            ObserveAbandonedSend(sendingTask);
            Cancel();
            return false;
        }
    }

    private static void ObserveAbandonedSend(Task sendingTask)
    {
        _ = sendingTask.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
            | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private async ValueTask<SwapDeliveryResult<T>> ResolveFailedSwapSendAsync<T>(
        Task<SwapDeliveryResult<T>> completion)
        where T : class
    {
        SwapDeliveryResult<T> result = completion.IsCompletedSuccessfully
            ? await completion.ConfigureAwait(false)
            : SwapDelivery.AcknowledgementLost<T>();
        Cancel();
        return result;
    }

    private void EnsureSwapResponseIsTimely(
        DateTimeOffset deadline,
        Action expirePending,
        string operation)
    {
        if (timeProvider.GetUtcNow() < deadline)
        {
            return;
        }

        expirePending();
        Cancel();
        throw new InvalidDataException(
            $"The Swap {operation} result arrived after its deadline.");
    }

    private void EnsureSwapEnvelopeIsCurrent(ControlMessage message)
    {
        DateTimeOffset expiresAt = message.SentAt.AddMilliseconds(
            message.TimeToLiveMilliseconds);
        if (timeProvider.GetUtcNow() < expiresAt)
        {
            return;
        }

        Cancel();
        throw new InvalidDataException(
            "The Swap control message arrived after its envelope lifetime.");
    }

    private sealed class PendingSwapSnapshot(SwapActivitySnapshotQuery query)
    {
        public SwapActivitySnapshotQuery Query { get; } = query;

        public TaskCompletionSource<SwapDeliveryResult<SwapActivitySnapshotResult>>
            Completion
        { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingSwapPrepare(SwapPrepareCommand command)
    {
        public SwapPrepareCommand Command { get; } = command;

        public TaskCompletionSource<SwapDeliveryResult<SwapPrepareResult>> Completion
        { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingSwapDecision(
        CorrelationId correlationId,
        SwapDecision decision,
        DateTimeOffset acknowledgementDeadline)
    {
        public DateTimeOffset AcknowledgementDeadline { get; } =
            acknowledgementDeadline;

        public CorrelationId CorrelationId { get; } = correlationId;

        public SwapDecision Decision { get; } = decision;

        public TaskCompletionSource<SwapDeliveryResult<SwapApplyResult>> Completion
        { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
