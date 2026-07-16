using System.Collections.Concurrent;
using System.Net.Sockets;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

internal interface IActivityControlConnection
{
    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public ValueTask<ControlMessage> ReadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SendAsync(
        ControlMessage message,
        CancellationToken cancellationToken = default);
}

internal sealed class ActivityControlSession :
    IActivityChannel,
    IReplaceTargetInventoryChannel,
    IReplaceChannel,
    IAsyncDisposable
{
    private readonly IActivityControlConnection connection;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IActivityPeer localPeer;
    private readonly ConcurrentDictionary<CorrelationId, PendingTransfer> pending = new();
    private readonly ConcurrentDictionary<CorrelationId, byte> pendingCorrelations = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingReplaceInventory>
        pendingInventories = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingReplace> pendingReplaces = new();
    private readonly IReplaceTargetInventoryPeer? replaceInventoryPeer;
    private readonly IReplacePeer? replacePeer;
    private readonly TimeProvider timeProvider;
    private int disposed;
    private int running;
    private int stopped;

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
        TimeProvider? timeProvider = null) : this(
            connection,
            localPeer,
            null,
            null,
            timeProvider)
    {
    }

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        TimeProvider? timeProvider = null) : this(
            connection,
            localPeer,
            replacePeer,
            null,
            timeProvider)
    {
    }

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        IReplaceTargetInventoryPeer? replaceInventoryPeer,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(localPeer);
        if (connection.LocalDeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity peer must represent the authenticated local device.",
                nameof(localPeer));
        }

        if (replacePeer is not null
            && connection.LocalDeviceId != replacePeer.DeviceId)
        {
            throw new ArgumentException(
                "The Replace peer must represent the authenticated local device.",
                nameof(replacePeer));
        }

        if (replaceInventoryPeer is not null
            && connection.LocalDeviceId != replaceInventoryPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Replace inventory peer must represent the authenticated local device.",
                nameof(replaceInventoryPeer));
        }

        this.connection = connection;
        this.localPeer = localPeer;
        this.replacePeer = replacePeer;
        this.replaceInventoryPeer = replaceInventoryPeer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DeviceId TargetDeviceId => connection.PeerDeviceId;

    public void Cancel()
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            lifetimeCancellation.Cancel();
        }
    }

    public async ValueTask RunAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "An Activity control session can run only once.");
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            while (true)
            {
                ControlMessage message = await connection
                    .ReadAsync(linked.Token)
                    .ConfigureAwait(false);
                switch (message.Type)
                {
                    case ControlMessageType.ActivityTransfer:
                        await HandleTransferAsync(message, linked.Token)
                            .ConfigureAwait(false);
                        break;
                    case ControlMessageType.ActivityReplaceInventory:
                        await HandleReplaceInventoryAsync(message, linked.Token)
                            .ConfigureAwait(false);
                        break;
                    case ControlMessageType.ActivityReplace:
                        await HandleReplaceAsync(message, linked.Token)
                            .ConfigureAwait(false);
                        break;
                    case ControlMessageType.ActivityReplaceInventoryResult:
                        HandleReplaceInventoryResult(message);
                        break;
                    case ControlMessageType.ActivityReplaceResult:
                        HandleReplaceResult(message);
                        break;
                    case ControlMessageType.OperationReceipt:
                        HandleReceipt(message);
                        break;
                    default:
                        throw new InvalidDataException(
                            "The Activity session received an unsupported control message.");
                }
            }
        }
        catch (IOException exception) when (linked.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The Activity control session was stopped.",
                exception,
                linked.Token);
        }
        finally
        {
            Volatile.Write(ref stopped, 1);
            CompletePendingAsUncertain();
        }
    }

    public async ValueTask<ActivityDeliveryResult> SendAsync(
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(offer);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return ActivityDeliveryResult.NotDelivered;
        }

        if (senderDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "An Activity transfer sender must match the authenticated local device.");
        }

        if (offer.TargetPlacement.DeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "An Activity transfer target must match the authenticated peer.");
        }

        var transfer = new PendingTransfer(
            offer.Context.OperationId,
            offer.Context.CorrelationId,
            offer.Kind,
            offer.Descriptor.Id,
            offer.Descriptor.Kind,
            offer.Descriptor.DescriptorDigest);
        ReserveCorrelation(offer.Context.CorrelationId);
        if (!pending.TryAdd(offer.Context.CorrelationId, transfer))
        {
            ReleaseCorrelation(offer.Context.CorrelationId);
            throw new InvalidOperationException(
                "The Activity transfer could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return ActivityDeliveryResult.NotDelivered;
            }

            ControlMessage message = ActivityControlMessageCodec.CreateTransfer(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                offer,
                timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken)
                .ConfigureAwait(false);
            sent = true;
            try
            {
                return await transfer.Completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                throw;
            }
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            pending.TryRemove(
                new KeyValuePair<CorrelationId, PendingTransfer>(
                    offer.Context.CorrelationId,
                    transfer));
            return ActivityDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent)
            {
                pending.TryRemove(
                    new KeyValuePair<CorrelationId, PendingTransfer>(
                        offer.Context.CorrelationId,
                        transfer));
                ReleaseCorrelation(offer.Context.CorrelationId);
            }
        }
    }

    public async ValueTask<ReplaceDeliveryResult> SendAsync(
        DeviceId senderDeviceId,
        ReplaceActivityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return ReplaceDeliveryResult.NotDelivered;
        }

        if (senderDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "An Activity Replace sender must match the authenticated local device.");
        }

        if (command.TargetPlacement.DeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "An Activity Replace target must match the authenticated peer.");
        }

        var pendingReplace = new PendingReplace(command);
        ReserveCorrelation(command.Context.CorrelationId);
        if (!pendingReplaces.TryAdd(command.Context.CorrelationId, pendingReplace))
        {
            ReleaseCorrelation(command.Context.CorrelationId);
            throw new InvalidOperationException(
                "The Activity Replace could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return ReplaceDeliveryResult.NotDelivered;
            }

            ControlMessage message = ActivityControlMessageCodec.CreateReplace(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                command,
                timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            sent = true;
            try
            {
                return await pendingReplace.Completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                throw;
            }
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            pendingReplaces.TryRemove(
                new KeyValuePair<CorrelationId, PendingReplace>(
                    command.Context.CorrelationId,
                    pendingReplace));
            return ReplaceDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent)
            {
                pendingReplaces.TryRemove(
                    new KeyValuePair<CorrelationId, PendingReplace>(
                        command.Context.CorrelationId,
                        pendingReplace));
                ReleaseCorrelation(command.Context.CorrelationId);
            }
        }
    }

    public async ValueTask<ReplaceTargetInventoryDeliveryResult> QueryAsync(
        DeviceId requestingDeviceId,
        ReplaceTargetInventoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return ReplaceTargetInventoryDeliveryResult.NotDelivered;
        }

        if (requestingDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A Replace inventory requester must match the authenticated local device.");
        }

        if (query.TargetDeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A Replace inventory query target must match the authenticated peer.");
        }

        var pendingInventory = new PendingReplaceInventory(query);
        ReserveCorrelation(query.CorrelationId);
        if (!pendingInventories.TryAdd(query.CorrelationId, pendingInventory))
        {
            ReleaseCorrelation(query.CorrelationId);
            throw new InvalidOperationException(
                "The Replace inventory could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return ReplaceTargetInventoryDeliveryResult.NotDelivered;
            }

            ControlMessage message =
                ActivityControlMessageCodec.CreateReplaceInventoryQuery(
                    connection.ProtocolVersion,
                    connection.LocalDeviceId,
                    query,
                    timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            sent = true;
            try
            {
                return await pendingInventory.Completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                throw;
            }
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            pendingInventories.TryRemove(
                new KeyValuePair<CorrelationId, PendingReplaceInventory>(
                    query.CorrelationId,
                    pendingInventory));
            return ReplaceTargetInventoryDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent)
            {
                pendingInventories.TryRemove(
                    new KeyValuePair<CorrelationId, PendingReplaceInventory>(
                        query.CorrelationId,
                        pendingInventory));
                ReleaseCorrelation(query.CorrelationId);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            CompletePendingAsUncertain();
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask HandleTransferAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        ActivityTransferOffer offer = ActivityControlMessageCodec.DecodeTransfer(
            message,
            connection.LocalDeviceId);
        OperationReceipt receipt = await localPeer.ReceiveActivityAsync(
            connection.PeerDeviceId,
            offer,
            cancellationToken).ConfigureAwait(false);
        if (receipt.OperationId != offer.Context.OperationId
            || receipt.CorrelationId != offer.Context.CorrelationId
            || receipt.SourceDeviceId != connection.PeerDeviceId
            || receipt.TargetDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidDataException(
                "The local Activity peer returned a receipt for another operation.");
        }

        ControlMessage response = ActivityControlMessageCodec.CreateReceipt(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            receipt,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask HandleReplaceAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        if (replacePeer is null)
        {
            throw new InvalidDataException(
                "The local Activity session does not accept Replace operations.");
        }

        ReplaceActivityCommand command = ActivityControlMessageCodec.DecodeReplace(
            message,
            connection.LocalDeviceId);
        ReplaceOperationResult result = await replacePeer.ReplaceAsync(
            connection.PeerDeviceId,
            command,
            cancellationToken).ConfigureAwait(false);
        ValidateReplaceResult(
            command,
            result,
            connection.PeerDeviceId,
            connection.LocalDeviceId);
        ControlMessage response = ActivityControlMessageCodec.CreateReplaceResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleReplaceInventoryAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        if (replaceInventoryPeer is null)
        {
            throw new InvalidDataException(
                "The local Activity session does not expose Replace target inventory.");
        }

        ReplaceTargetInventoryQuery query =
            ActivityControlMessageCodec.DecodeReplaceInventoryQuery(
                message,
                connection.LocalDeviceId);
        ReplaceTargetInventoryResult result = await replaceInventoryPeer.QueryAsync(
            connection.PeerDeviceId,
            query,
            cancellationToken).ConfigureAwait(false);
        if (result.CorrelationId != query.CorrelationId
            || result.RequestingDeviceId != connection.PeerDeviceId
            || result.TargetDeviceId != connection.LocalDeviceId
            || result.IncomingKind != query.IncomingKind
            || result.QueryDeadline != query.Deadline)
        {
            throw new InvalidDataException(
                "The local Replace inventory peer returned a result for another query.");
        }

        ControlMessage response =
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                result,
                timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleReceipt(ControlMessage message)
    {
        if (!pending.TryGetValue(
                message.CorrelationId,
                out PendingTransfer? transfer))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited operation receipt.");
        }

        OperationReceipt receipt = ActivityControlMessageCodec.DecodeReceipt(
            message,
            connection.LocalDeviceId,
            transfer.CorrelationId);
        if (receipt.OperationId != transfer.OperationId
            || receipt.Kind != transfer.OperationKind
            || receipt.ActivityId != transfer.ActivityId
            || receipt.ActivityKind != transfer.ActivityKind
            || !StringComparer.OrdinalIgnoreCase.Equals(
                receipt.DescriptorDigest,
                transfer.DescriptorDigest))
        {
            throw new InvalidDataException(
                "The operation receipt does not match the pending Activity operation.");
        }

        if (!pending.TryRemove(
                new KeyValuePair<CorrelationId, PendingTransfer>(
                    transfer.CorrelationId,
                    transfer)))
        {
            throw new InvalidDataException(
                "The operation receipt raced with session shutdown.");
        }

        ReleaseCorrelation(transfer.CorrelationId);
        transfer.Completion.TrySetResult(
            ActivityDeliveryResult.Acknowledged(receipt));
    }

    private void HandleReplaceResult(ControlMessage message)
    {
        if (!pendingReplaces.TryGetValue(
                message.CorrelationId,
                out PendingReplace? pendingReplace))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Replace result.");
        }

        ReplaceOperationResult result = ActivityControlMessageCodec.DecodeReplaceResult(
            message,
            connection.LocalDeviceId,
            pendingReplace.Command.Context.CorrelationId);
        ValidateReplaceResult(
            pendingReplace.Command,
            result,
            connection.LocalDeviceId,
            connection.PeerDeviceId);
        if (!pendingReplaces.TryRemove(
                new KeyValuePair<CorrelationId, PendingReplace>(
                    pendingReplace.Command.Context.CorrelationId,
                    pendingReplace)))
        {
            throw new InvalidDataException(
                "The Activity Replace result raced with session shutdown.");
        }

        ReleaseCorrelation(pendingReplace.Command.Context.CorrelationId);
        pendingReplace.Completion.TrySetResult(
            ReplaceDeliveryResult.Acknowledged(result));
    }

    private void HandleReplaceInventoryResult(ControlMessage message)
    {
        if (!pendingInventories.TryGetValue(
                message.CorrelationId,
                out PendingReplaceInventory? pendingInventory))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Replace inventory result.");
        }

        ReplaceTargetInventoryResult result =
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                message,
                connection.LocalDeviceId,
                pendingInventory.Query);
        if (!pendingInventories.TryRemove(
                new KeyValuePair<CorrelationId, PendingReplaceInventory>(
                    pendingInventory.Query.CorrelationId,
                    pendingInventory)))
        {
            throw new InvalidDataException(
                "The Replace inventory result raced with session shutdown.");
        }

        ReleaseCorrelation(pendingInventory.Query.CorrelationId);
        pendingInventory.Completion.TrySetResult(
            ReplaceTargetInventoryDeliveryResult.Acknowledged(result));
    }

    private static void ValidateReplaceResult(
        ReplaceActivityCommand command,
        ReplaceOperationResult result,
        DeviceId expectedSourceDeviceId,
        DeviceId expectedTargetDeviceId)
    {
        OperationReceipt receipt = result.Receipt;
        if (receipt.OperationId != command.Context.OperationId
            || receipt.CorrelationId != command.Context.CorrelationId
            || receipt.Kind != OperationKind.Replace
            || receipt.SourceDeviceId != expectedSourceDeviceId
            || receipt.TargetDeviceId != expectedTargetDeviceId
            || receipt.ActivityId != command.IncomingDescriptor.Id
            || receipt.ActivityKind != command.IncomingDescriptor.Kind
            || !StringComparer.OrdinalIgnoreCase.Equals(
                receipt.DescriptorDigest,
                command.IncomingDescriptor.DescriptorDigest))
        {
            throw new InvalidDataException(
                "The Activity Replace result does not match the pending operation.");
        }

        if (result.UndoCapsule is UndoCapsuleReference capsule
            && (capsule.TargetActivityId != command.TargetActivityId
                || capsule.ExpectedTargetRevision != command.ExpectedTargetRevision
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    capsule.TargetDescriptorDigest,
                    command.ExpectedTargetDescriptorDigest)
                || capsule.IncomingActivityId != command.IncomingDescriptor.Id
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    capsule.IncomingDescriptorDigest,
                    command.IncomingDescriptor.DescriptorDigest)
                || capsule.ExpiresAt != command.UndoExpiresAt))
        {
            throw new InvalidDataException(
                "The Activity Replace undo metadata does not match the pending operation.");
        }
    }

    private void CompletePendingAsUncertain()
    {
        foreach ((CorrelationId correlationId, PendingTransfer transfer) in pending)
        {
            if (pending.TryRemove(
                    new KeyValuePair<CorrelationId, PendingTransfer>(
                        correlationId,
                        transfer)))
            {
                ReleaseCorrelation(correlationId);
                transfer.Completion.TrySetResult(
                    ActivityDeliveryResult.AcknowledgementLost);
            }
        }

        foreach ((CorrelationId correlationId, PendingReplace pendingReplace) in pendingReplaces)
        {
            if (pendingReplaces.TryRemove(
                    new KeyValuePair<CorrelationId, PendingReplace>(
                        correlationId,
                        pendingReplace)))
            {
                ReleaseCorrelation(correlationId);
                pendingReplace.Completion.TrySetResult(
                    ReplaceDeliveryResult.AcknowledgementLost);
            }
        }

        foreach ((CorrelationId correlationId, PendingReplaceInventory inventory)
                 in pendingInventories)
        {
            if (pendingInventories.TryRemove(
                    new KeyValuePair<CorrelationId, PendingReplaceInventory>(
                        correlationId,
                        inventory)))
            {
                ReleaseCorrelation(correlationId);
                inventory.Completion.TrySetResult(
                    ReplaceTargetInventoryDeliveryResult.AcknowledgementLost);
            }
        }
    }

    private void ReserveCorrelation(CorrelationId correlationId)
    {
        if (!pendingCorrelations.TryAdd(correlationId, 0))
        {
            throw new InvalidOperationException(
                "An Activity operation with this correlation ID is already pending.");
        }
    }

    private void ReleaseCorrelation(CorrelationId correlationId) =>
        pendingCorrelations.TryRemove(correlationId, out _);

    private sealed class PendingTransfer(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind operationKind,
        ActivityId activityId,
        ActivityKind activityKind,
        string descriptorDigest)
    {
        public ActivityId ActivityId { get; } = activityId;

        public ActivityKind ActivityKind { get; } = activityKind;

        public TaskCompletionSource<ActivityDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CorrelationId CorrelationId { get; } = correlationId;

        public string DescriptorDigest { get; } = descriptorDigest;

        public OperationId OperationId { get; } = operationId;

        public OperationKind OperationKind { get; } = operationKind;
    }

    private sealed class PendingReplace(ReplaceActivityCommand command)
    {
        public ReplaceActivityCommand Command { get; } = command;

        public TaskCompletionSource<ReplaceDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingReplaceInventory(ReplaceTargetInventoryQuery query)
    {
        public ReplaceTargetInventoryQuery Query { get; } = query;

        public TaskCompletionSource<ReplaceTargetInventoryDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class AuthenticatedActivitySessionHandler :
    IAuthenticatedControlSessionHandler,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<DeviceId, Registration> sessions = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IActivityPeer localPeer;
    private readonly IReplaceTargetInventoryPeer? replaceInventoryPeer;
    private readonly IReplacePeer? replacePeer;
    private readonly TimeProvider timeProvider;
    private int disposed;

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        TimeProvider? timeProvider = null) : this(
            localPeer,
            null,
            null,
            timeProvider)
    {
    }

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        TimeProvider? timeProvider = null) : this(
            localPeer,
            replacePeer,
            null,
            timeProvider)
    {
    }

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        IReplaceTargetInventoryPeer? replaceInventoryPeer,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(localPeer);
        if (replacePeer is not null && replacePeer.DeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity and Replace peers must represent the same local device.",
                nameof(replacePeer));
        }

        if (replaceInventoryPeer is not null
            && replaceInventoryPeer.DeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity and Replace inventory peers must represent the same local device.",
                nameof(replaceInventoryPeer));
        }

        this.localPeer = localPeer;
        this.replacePeer = replacePeer;
        this.replaceInventoryPeer = replaceInventoryPeer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public bool IsReplaceEndpointAvailable =>
        Volatile.Read(ref disposed) == 0 && replacePeer is not null;

    public IReadOnlyList<DeviceId> GetConnectedPeers()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return [];
        }

        return sessions.Keys
            .OrderBy(static id => id.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryGetChannel(
        DeviceId peerDeviceId,
        out IActivityChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration))
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryGetReplaceChannel(
        DeviceId peerDeviceId,
        out IReplaceChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration))
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryGetReplaceInventoryChannel(
        DeviceId peerDeviceId,
        out IReplaceTargetInventoryChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration))
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public async ValueTask RunAsync(
        AuthenticatedTcpControlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var adapter = new AuthenticatedConnectionAdapter(connection);
        var session = new ActivityControlSession(
            adapter,
            localPeer,
            replacePeer,
            replaceInventoryPeer,
            timeProvider);
        var registration = new Registration(session);
        if (!sessions.TryAdd(connection.PeerIdentity.DeviceId, registration))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new InvalidDataException(
                "A second authenticated Activity session for this peer was rejected.");
        }

        PublishChanged();
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            await session.RunAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            sessions.TryRemove(
                new KeyValuePair<DeviceId, Registration>(
                    connection.PeerIdentity.DeviceId,
                    registration));
            await session.DisposeAsync().ConfigureAwait(false);
            registration.Completion.TrySetResult();
            PublishChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        Registration[] active = sessions.Values.ToArray();
        foreach (Registration registration in active)
        {
            registration.Session.Cancel();
        }

        await Task.WhenAll(active.Select(static item => item.Completion.Task))
            .ConfigureAwait(false);
        lifetimeCancellation.Dispose();
    }

    private void PublishChanged()
    {
        foreach (Action subscriber in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                subscriber();
            }
            catch
            {
                // Observers cannot own an authenticated session lifetime.
            }
        }
    }

    private sealed class Registration(ActivityControlSession session)
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActivityControlSession Session { get; } = session;
    }

    private sealed class AuthenticatedConnectionAdapter(
        AuthenticatedTcpControlConnection connection) : IActivityControlConnection
    {
        public DeviceId LocalDeviceId => connection.LocalDeviceId;

        public DeviceId PeerDeviceId => connection.PeerIdentity.DeviceId;

        public ProtocolVersion ProtocolVersion => connection.ProtocolVersion;

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            connection.ReceiveAsync(cancellationToken);

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) =>
            connection.SendAsync(message, cancellationToken);
    }
}
