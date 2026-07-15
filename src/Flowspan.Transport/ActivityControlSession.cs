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

internal sealed class ActivityControlSession : IActivityChannel, IAsyncDisposable
{
    private readonly IActivityControlConnection connection;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IActivityPeer localPeer;
    private readonly ConcurrentDictionary<CorrelationId, PendingTransfer> pending = new();
    private readonly TimeProvider timeProvider;
    private int disposed;
    private int running;
    private int stopped;

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
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

        this.connection = connection;
        this.localPeer = localPeer;
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
                    case ControlMessageType.OperationReceipt:
                        HandleReceipt(message);
                        break;
                    default:
                        throw new InvalidDataException(
                            "The Activity session received an unsupported control message.");
                }
            }
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
        if (!pending.TryAdd(offer.Context.CorrelationId, transfer))
        {
            throw new InvalidOperationException(
                "An Activity transfer with this correlation ID is already pending.");
        }

        bool sent = false;
        try
        {
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

        transfer.Completion.TrySetResult(
            ActivityDeliveryResult.Acknowledged(receipt));
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
                transfer.Completion.TrySetResult(
                    ActivityDeliveryResult.AcknowledgementLost);
            }
        }
    }

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
}

public sealed class AuthenticatedActivitySessionHandler :
    IAuthenticatedControlSessionHandler,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<DeviceId, Registration> sessions = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IActivityPeer localPeer;
    private readonly TimeProvider timeProvider;
    private int disposed;

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(localPeer);
        this.localPeer = localPeer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

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

    public async ValueTask RunAsync(
        AuthenticatedTcpControlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var adapter = new AuthenticatedConnectionAdapter(connection);
        var session = new ActivityControlSession(adapter, localPeer, timeProvider);
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
