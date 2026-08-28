using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
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

internal sealed partial class ActivityControlSession :
    IActivityChannel,
    IReplaceTargetInventoryChannel,
    IReplaceChannel,
    ISwapEndpointChannel,
    ISceneSourceLookupChannel,
    ISceneExactSlotChannel,
    ISceneChildOperationChannel,
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
    private readonly ConcurrentDictionary<CorrelationId, PendingSceneSourceLookup>
        pendingSceneSourceLookups = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingSceneExactSlot>
        pendingSceneExactSlots = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingSceneChild>
        pendingSceneChildren = new();
    private readonly ConcurrentDictionary<CorrelationId, PendingSceneUndoReplace>
        pendingSceneUndoReplaces = new();
    private readonly IReplaceTargetInventoryPeer? replaceInventoryPeer;
    private readonly IReplacePeer? replacePeer;
    private readonly ISceneControlPeer? scenePeer;
    private readonly TimeProvider timeProvider;
    private int disposed;
    private int lifetimeStopRequested;
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
            null,
            timeProvider)
    {
    }

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
        ISceneControlPeer scenePeer,
        TimeProvider? timeProvider = null) : this(
            connection,
            localPeer,
            null,
            null,
            null,
            timeProvider,
            scenePeer)
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
            null,
            timeProvider)
    {
    }

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        IReplaceTargetInventoryPeer? replaceInventoryPeer,
        TimeProvider? timeProvider = null) : this(
            connection,
            localPeer,
            replacePeer,
            replaceInventoryPeer,
            null,
            timeProvider)
    {
    }

    public ActivityControlSession(
        IActivityControlConnection connection,
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        IReplaceTargetInventoryPeer? replaceInventoryPeer,
        ISwapEndpointPeer? swapPeer,
        TimeProvider? timeProvider = null,
        ISceneControlPeer? scenePeer = null)
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

        if (swapPeer is not null && connection.LocalDeviceId != swapPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Swap peer must represent the authenticated local device.",
                nameof(swapPeer));
        }


        if (scenePeer is not null
            && connection.LocalDeviceId != scenePeer.DeviceId)
        {
            throw new ArgumentException(
                "The Scene peer must represent the authenticated local device.",
                nameof(scenePeer));
        }

        this.connection = connection;
        this.localPeer = localPeer;
        this.replacePeer = replacePeer;
        this.replaceInventoryPeer = replaceInventoryPeer;
        this.swapPeer = swapPeer;
        this.scenePeer = scenePeer;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DeviceId TargetDeviceId => connection.PeerDeviceId;

    internal CancellationToken LifetimeCancellationToken =>
        lifetimeCancellation.Token;

    public bool SupportsSwap =>
        Volatile.Read(ref disposed) == 0
        && Volatile.Read(ref stopped) == 0
        && ProtocolFeatures.SupportsActivitySwap(connection.ProtocolVersion);

    public bool SupportsSceneApply =>
        Volatile.Read(ref disposed) == 0
        && Volatile.Read(ref stopped) == 0
        && ProtocolFeatures.SupportsSceneApply(connection.ProtocolVersion);

    public void Cancel()
    {
        Interlocked.Exchange(ref stopped, 1);
        try
        {
            RequestLifetimeStop();
        }
        finally
        {
            CompletePendingAsUncertain();
        }
    }

    public async ValueTask RunAsync(
        CancellationToken cancellationToken = default)
    {
        StartDispatch();
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
                await DispatchAsync(message, linked.Token).ConfigureAwait(false);
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
            StopDispatch();
        }
    }

    internal async ValueTask DispatchAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message.Type)
        {
            case ControlMessageType.ActivityTransfer:
                await HandleTransferAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.ActivityReplaceInventory:
                await HandleReplaceInventoryAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.ActivityReplace:
                await HandleReplaceAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.ActivityReplaceInventoryResult:
                HandleReplaceInventoryResult(message);
                break;
            case ControlMessageType.ActivityReplaceResult:
                HandleReplaceResult(message);
                break;
            case ControlMessageType.ActivitySwapSnapshot:
                await HandleSwapSnapshotAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.ActivitySwapSnapshotResult:
                HandleSwapSnapshotResult(message);
                break;
            case ControlMessageType.ActivitySwapPrepare:
                await HandleSwapPrepareAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.ActivitySwapPrepareResult:
                HandleSwapPrepareResult(message);
                break;
            case ControlMessageType.ActivitySwapDecision:
                await HandleSwapDecisionAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.ActivitySwapDecisionResult:
                HandleSwapDecisionResult(message);
                break;
            case ControlMessageType.OperationReceipt:
                HandleReceipt(message);
                break;
            case ControlMessageType.SceneSourceLookup:
                await HandleSceneSourceLookupAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.SceneSourceLookupResult:
                HandleSceneSourceLookupResult(message);
                break;
            case ControlMessageType.SceneSlotInspection:
                await HandleSceneExactSlotAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.SceneSlotInspectionResult:
                HandleSceneExactSlotResult(message);
                break;
            case ControlMessageType.SceneChildOperation:
                await HandleSceneChildAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.SceneChildOperationResult:
                HandleSceneChildResult(message);
                break;
            case ControlMessageType.SceneUndoReplace:
                await HandleSceneUndoReplaceAsync(message, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControlMessageType.SceneUndoReplaceResult:
                HandleSceneUndoReplaceResult(message);
                break;
            default:
                throw new InvalidDataException(
                    "The Activity session received an unsupported control message.");
        }
    }

    internal void StartDispatch()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "An Activity control session can run only once.");
        }
    }

    internal void StopDispatch()
    {
        Volatile.Write(ref stopped, 1);
        CompletePendingAsUncertain();
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
            return ActivityDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pending.TryRemove(
                    new KeyValuePair<CorrelationId, PendingTransfer>(
                        offer.Context.CorrelationId,
                        transfer)))
            {
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
            return ReplaceDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pendingReplaces.TryRemove(
                    new KeyValuePair<CorrelationId, PendingReplace>(
                        command.Context.CorrelationId,
                        pendingReplace)))
            {
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
            return ReplaceTargetInventoryDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pendingInventories.TryRemove(
                    new KeyValuePair<CorrelationId, PendingReplaceInventory>(
                        query.CorrelationId,
                        pendingInventory)))
            {
                ReleaseCorrelation(query.CorrelationId);
            }
        }
    }

    public async ValueTask<SceneSourceLookupDeliveryResult> QuerySourceAsync(
        DeviceId requestingDeviceId,
        SceneSourceLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsSceneApply(connection.ProtocolVersion))
        {
            return SceneSourceLookupDeliveryResult.ProtocolUnsupported;
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SceneSourceLookupDeliveryResult.NotDelivered;
        }

        if (requestingDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A Scene source lookup requester must match the authenticated local Device.");
        }

        if (query.TargetDeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A Scene source lookup target must match the authenticated peer.");
        }

        var pendingLookup = new PendingSceneSourceLookup(query);
        ReserveCorrelation(query.Context.CorrelationId);
        if (!pendingSceneSourceLookups.TryAdd(
                query.Context.CorrelationId,
                pendingLookup))
        {
            ReleaseCorrelation(query.Context.CorrelationId);
            throw new InvalidOperationException(
                "The Scene source lookup could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SceneSourceLookupDeliveryResult.NotDelivered;
            }

            ControlMessage message = SceneControlMessageCodec.CreateSourceLookupQuery(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                query,
                timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            sent = true;
            try
            {
                return await pendingLookup.Completion.Task
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
            return SceneSourceLookupDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pendingSceneSourceLookups.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneSourceLookup>(
                        query.Context.CorrelationId,
                        pendingLookup)))
            {
                ReleaseCorrelation(query.Context.CorrelationId);
            }
        }
    }

    public async ValueTask<SceneExactSlotDeliveryResult> InspectSlotAsync(
        DeviceId requestingDeviceId,
        SceneExactSlotQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsSceneApply(connection.ProtocolVersion))
        {
            return SceneExactSlotDeliveryResult.ProtocolUnsupported;
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SceneExactSlotDeliveryResult.NotDelivered;
        }

        if (requestingDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A Scene exact-slot requester must match the authenticated local Device.");
        }

        if (query.TargetDeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A Scene exact-slot target must match the authenticated peer.");
        }

        var pendingInspection = new PendingSceneExactSlot(query);
        ReserveCorrelation(query.Context.CorrelationId);
        if (!pendingSceneExactSlots.TryAdd(
                query.Context.CorrelationId,
                pendingInspection))
        {
            ReleaseCorrelation(query.Context.CorrelationId);
            throw new InvalidOperationException(
                "The Scene exact-slot query could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SceneExactSlotDeliveryResult.NotDelivered;
            }

            ControlMessage message = SceneControlMessageCodec.CreateExactSlotQuery(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                query,
                timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            sent = true;
            try
            {
                return await pendingInspection.Completion.Task
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
            return SceneExactSlotDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pendingSceneExactSlots.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneExactSlot>(
                        query.Context.CorrelationId,
                        pendingInspection)))
            {
                ReleaseCorrelation(query.Context.CorrelationId);
            }
        }
    }

    public async ValueTask<SceneChildDeliveryResult> ExecuteChildAsync(
        DeviceId requestingDeviceId,
        SceneRemoteChildInstruction instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(instruction);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsSceneApply(connection.ProtocolVersion))
        {
            return SceneChildDeliveryResult.ProtocolUnsupported;
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SceneChildDeliveryResult.NotDelivered;
        }

        if (requestingDeviceId != connection.LocalDeviceId
            || instruction.CoordinatorDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A remote Scene child requester must match the authenticated local Device.");
        }

        if (instruction.SourceDeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A remote Scene child source must match the authenticated peer.");
        }

        var pendingChild = new PendingSceneChild(instruction);
        ReserveCorrelation(instruction.Item.ChildCorrelationId);
        if (!pendingSceneChildren.TryAdd(
                instruction.Item.ChildCorrelationId,
                pendingChild))
        {
            ReleaseCorrelation(instruction.Item.ChildCorrelationId);
            throw new InvalidOperationException(
                "The remote Scene child could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SceneChildDeliveryResult.NotDelivered;
            }

            ControlMessage message = SceneControlMessageCodec.CreateChildInstruction(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                instruction,
                timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            sent = true;
            try
            {
                return await pendingChild.Completion.Task
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
            return SceneChildDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pendingSceneChildren.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneChild>(
                        instruction.Item.ChildCorrelationId,
                        pendingChild)))
            {
                ReleaseCorrelation(instruction.Item.ChildCorrelationId);
            }
        }
    }

    public async ValueTask<SceneUndoReplaceDeliveryResult> UndoReplaceAsync(
        DeviceId requestingDeviceId,
        SceneUndoReplaceInstruction instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(instruction);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ProtocolFeatures.SupportsSceneApply(connection.ProtocolVersion))
        {
            return SceneUndoReplaceDeliveryResult.ProtocolUnsupported;
        }

        if (Volatile.Read(ref running) == 0 || Volatile.Read(ref stopped) != 0)
        {
            return SceneUndoReplaceDeliveryResult.NotDelivered;
        }

        if (requestingDeviceId != connection.LocalDeviceId
            || instruction.CoordinatorDeviceId != connection.LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A remote Scene undo requester must match the authenticated local Device.");
        }

        if (instruction.TargetDeviceId != TargetDeviceId)
        {
            throw new InvalidOperationException(
                "A remote Scene undo target must match the authenticated peer.");
        }

        var pendingUndo = new PendingSceneUndoReplace(instruction);
        ReserveCorrelation(instruction.Context.CorrelationId);
        if (!pendingSceneUndoReplaces.TryAdd(
                instruction.Context.CorrelationId,
                pendingUndo))
        {
            ReleaseCorrelation(instruction.Context.CorrelationId);
            throw new InvalidOperationException(
                "The remote Scene undo could not register its reserved correlation ID.");
        }

        bool sent = false;
        try
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return SceneUndoReplaceDeliveryResult.NotDelivered;
            }

            ControlMessage message =
                SceneControlMessageCodec.CreateUndoReplaceInstruction(
                    connection.ProtocolVersion,
                    connection.LocalDeviceId,
                    instruction,
                    timeProvider.GetUtcNow());
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            sent = true;
            try
            {
                return await pendingUndo.Completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                Cancel();
                throw;
            }
        }
        catch (Exception exception) when (
            !sent
            && exception is IOException or SocketException or TimeoutException)
        {
            return SceneUndoReplaceDeliveryResult.NotDelivered;
        }
        finally
        {
            if (!sent
                && pendingSceneUndoReplaces.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneUndoReplace>(
                        instruction.Context.CorrelationId,
                        pendingUndo)))
            {
                ReleaseCorrelation(instruction.Context.CorrelationId);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            Interlocked.Exchange(ref stopped, 1);
            try
            {
                RequestLifetimeStop();
            }
            finally
            {
                CompletePendingAsUncertain();
            }
        }

        return ValueTask.CompletedTask;
    }

    private void RequestLifetimeStop()
    {
        if (Interlocked.Exchange(ref lifetimeStopRequested, 1) == 0)
        {
            lifetimeCancellation.Cancel();
        }
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

    private void HandleSceneSourceLookupResult(ControlMessage message)
    {
        if (!pendingSceneSourceLookups.TryGetValue(
                message.CorrelationId,
                out PendingSceneSourceLookup? pendingLookup))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Scene source lookup result.");
        }

        SceneSourceLookup result = SceneControlMessageCodec.DecodeSourceLookupResult(
            message,
            connection.LocalDeviceId,
            pendingLookup.Query);
        if (!pendingSceneSourceLookups.TryRemove(
                new KeyValuePair<CorrelationId, PendingSceneSourceLookup>(
                    pendingLookup.Query.Context.CorrelationId,
                    pendingLookup)))
        {
            throw new InvalidDataException(
                "The Scene source lookup result raced with session shutdown.");
        }

        ReleaseCorrelation(pendingLookup.Query.Context.CorrelationId);
        pendingLookup.Completion.TrySetResult(
            SceneSourceLookupDeliveryResult.Acknowledged(result));
    }

    private async ValueTask HandleSceneSourceLookupAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        if (scenePeer is null)
        {
            throw new InvalidDataException(
                "The local Activity session does not expose Scene source lookup.");
        }

        SceneSourceLookupQuery query =
            SceneControlMessageCodec.DecodeSourceLookupQuery(
                message,
                connection.LocalDeviceId);
        SceneSourceLookup result = await scenePeer.LocateSourceAsync(
            connection.PeerDeviceId,
            query,
            cancellationToken).ConfigureAwait(false);
        ControlMessage response = SceneControlMessageCodec.CreateSourceLookupResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            connection.PeerDeviceId,
            query,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleSceneExactSlotResult(ControlMessage message)
    {
        if (!pendingSceneExactSlots.TryGetValue(
                message.CorrelationId,
                out PendingSceneExactSlot? pendingInspection))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited Scene exact-slot result.");
        }

        SceneExactSlotInspection result =
            SceneControlMessageCodec.DecodeExactSlotResult(
                message,
                connection.LocalDeviceId,
                pendingInspection.Query);
        if (!pendingSceneExactSlots.TryRemove(
                new KeyValuePair<CorrelationId, PendingSceneExactSlot>(
                    pendingInspection.Query.Context.CorrelationId,
                    pendingInspection)))
        {
            throw new InvalidDataException(
                "The Scene exact-slot result raced with session shutdown.");
        }

        ReleaseCorrelation(pendingInspection.Query.Context.CorrelationId);
        pendingInspection.Completion.TrySetResult(
            SceneExactSlotDeliveryResult.Acknowledged(result));
    }

    private async ValueTask HandleSceneExactSlotAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        if (scenePeer is null)
        {
            throw new InvalidDataException(
                "The local Activity session does not expose Scene exact-slot inspection.");
        }

        SceneExactSlotQuery query = SceneControlMessageCodec.DecodeExactSlotQuery(
            message,
            connection.LocalDeviceId);
        SceneExactSlotInspection result = await scenePeer.InspectExactSlotAsync(
            connection.PeerDeviceId,
            query,
            cancellationToken).ConfigureAwait(false);
        ControlMessage response = SceneControlMessageCodec.CreateExactSlotResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            connection.PeerDeviceId,
            query,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleSceneChildResult(ControlMessage message)
    {
        if (!pendingSceneChildren.TryGetValue(
                message.CorrelationId,
                out PendingSceneChild? pendingChild))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited remote Scene child result.");
        }

        SceneActivityOperationResult result =
            SceneControlMessageCodec.DecodeChildResult(
                message,
                connection.LocalDeviceId,
                pendingChild.Instruction);
        if (!pendingSceneChildren.TryRemove(
                new KeyValuePair<CorrelationId, PendingSceneChild>(
                    pendingChild.Instruction.Item.ChildCorrelationId,
                    pendingChild)))
        {
            throw new InvalidDataException(
                "The remote Scene child result raced with session shutdown.");
        }

        ReleaseCorrelation(pendingChild.Instruction.Item.ChildCorrelationId);
        pendingChild.Completion.TrySetResult(
            SceneChildDeliveryResult.Acknowledged(result));
    }

    private async ValueTask HandleSceneChildAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        if (scenePeer is null)
        {
            throw new InvalidDataException(
                "The local Activity session does not execute remote Scene children.");
        }

        SceneRemoteChildInstruction instruction =
            SceneControlMessageCodec.DecodeChildInstruction(
                message,
                connection.LocalDeviceId);
        SceneActivityOperationResult result = await scenePeer.ExecuteChildAsync(
            connection.PeerDeviceId,
            instruction,
            cancellationToken).ConfigureAwait(false);
        ControlMessage response = SceneControlMessageCodec.CreateChildResult(
            connection.ProtocolVersion,
            connection.LocalDeviceId,
            connection.PeerDeviceId,
            instruction,
            result,
            timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleSceneUndoReplaceResult(ControlMessage message)
    {
        if (!pendingSceneUndoReplaces.TryGetValue(
                message.CorrelationId,
                out PendingSceneUndoReplace? pendingUndo))
        {
            throw new InvalidDataException(
                "The Activity session received an unsolicited remote Scene undo result.");
        }

        UndoReplaceResult result =
            SceneControlMessageCodec.DecodeUndoReplaceResult(
                message,
                connection.LocalDeviceId,
                pendingUndo.Instruction);
        if (!pendingSceneUndoReplaces.TryRemove(
                new KeyValuePair<CorrelationId, PendingSceneUndoReplace>(
                    pendingUndo.Instruction.Context.CorrelationId,
                    pendingUndo)))
        {
            throw new InvalidDataException(
                "The remote Scene undo result raced with session shutdown.");
        }

        ReleaseCorrelation(pendingUndo.Instruction.Context.CorrelationId);
        pendingUndo.Completion.TrySetResult(
            SceneUndoReplaceDeliveryResult.Acknowledged(result));
    }

    private async ValueTask HandleSceneUndoReplaceAsync(
        ControlMessage message,
        CancellationToken cancellationToken)
    {
        if (scenePeer is null)
        {
            throw new InvalidDataException(
                "The local Activity session does not execute remote Scene undo.");
        }

        SceneUndoReplaceInstruction instruction =
            SceneControlMessageCodec.DecodeUndoReplaceInstruction(
                message,
                connection.LocalDeviceId);
        UndoReplaceResult result = await scenePeer.UndoReplaceAsync(
            connection.PeerDeviceId,
            instruction,
            cancellationToken).ConfigureAwait(false);
        ControlMessage response =
            SceneControlMessageCodec.CreateUndoReplaceResult(
                connection.ProtocolVersion,
                connection.LocalDeviceId,
                connection.PeerDeviceId,
                instruction,
                result,
                timeProvider.GetUtcNow());
        await connection.SendAsync(response, cancellationToken).ConfigureAwait(false);
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

        foreach ((CorrelationId correlationId, PendingSceneSourceLookup lookup)
                 in pendingSceneSourceLookups)
        {
            if (pendingSceneSourceLookups.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneSourceLookup>(
                        correlationId,
                        lookup)))
            {
                ReleaseCorrelation(correlationId);
                lookup.Completion.TrySetResult(
                    SceneSourceLookupDeliveryResult.AcknowledgementLost);
            }
        }

        foreach ((CorrelationId correlationId, PendingSceneExactSlot inspection)
                 in pendingSceneExactSlots)
        {
            if (pendingSceneExactSlots.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneExactSlot>(
                        correlationId,
                        inspection)))
            {
                ReleaseCorrelation(correlationId);
                inspection.Completion.TrySetResult(
                    SceneExactSlotDeliveryResult.AcknowledgementLost);
            }
        }

        foreach ((CorrelationId correlationId, PendingSceneChild child)
                 in pendingSceneChildren)
        {
            if (pendingSceneChildren.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneChild>(
                        correlationId,
                        child)))
            {
                ReleaseCorrelation(correlationId);
                child.Completion.TrySetResult(
                    SceneChildDeliveryResult.AcknowledgementLost);
            }
        }

        foreach ((CorrelationId correlationId, PendingSceneUndoReplace undo)
                 in pendingSceneUndoReplaces)
        {
            if (pendingSceneUndoReplaces.TryRemove(
                    new KeyValuePair<CorrelationId, PendingSceneUndoReplace>(
                        correlationId,
                        undo)))
            {
                ReleaseCorrelation(correlationId);
                undo.Completion.TrySetResult(
                    SceneUndoReplaceDeliveryResult.AcknowledgementLost);
            }
        }

        CompleteSwapPendingAsUncertain();
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

    private sealed class PendingSceneSourceLookup(SceneSourceLookupQuery query)
    {
        public SceneSourceLookupQuery Query { get; } = query;

        public TaskCompletionSource<SceneSourceLookupDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingSceneExactSlot(SceneExactSlotQuery query)
    {
        public SceneExactSlotQuery Query { get; } = query;

        public TaskCompletionSource<SceneExactSlotDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingSceneChild(SceneRemoteChildInstruction instruction)
    {
        public SceneRemoteChildInstruction Instruction { get; } = instruction;

        public TaskCompletionSource<SceneChildDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingSceneUndoReplace(
        SceneUndoReplaceInstruction instruction)
    {
        public SceneUndoReplaceInstruction Instruction { get; } = instruction;

        public TaskCompletionSource<SceneUndoReplaceDeliveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class AuthenticatedActivitySessionHandler :
    IAuthenticatedControlSessionHandler,
    ISceneOperationRouteDirectory,
    IAsyncDisposable
{
    private readonly HashSet<Registration> activeRegistrations = [];
    private readonly AsyncLocal<SessionCallScope?> activeSessionCall = new();
    private readonly ConcurrentDictionary<DeviceId, Registration> sessions = new();
    private readonly TaskCompletionSource disposalCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object lifecycleGate = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object revocationCallbackOwner = new();
    private readonly IActivityPeer localPeer;
    private readonly IReplaceTargetInventoryPeer? replaceInventoryPeer;
    private readonly IReplacePeer? replacePeer;
    private readonly AuthenticatedRemoteWindowMediaSessionDirectory?
        remoteWindowMediaSessions;
    private readonly IRemoteWindowControlPeer? remoteWindowPeer;
    private readonly IRemoteWindowPreparationPeer? remoteWindowPreparationPeer;
    private readonly ISceneControlPeer? scenePeer;
    private readonly ISwapEndpointPeer? swapPeer;
    private readonly TimeProvider timeProvider;
    private int disposalCleanupStarted;
    private int disposed;
    private long nextRemoteWindowConnectionGeneration;

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        TimeProvider? timeProvider = null,
        IRemoteWindowControlPeer? remoteWindowPeer = null,
        IRemoteWindowPreparationPeer? remoteWindowPreparationPeer = null) : this(
            localPeer,
            null,
            null,
            null,
            timeProvider,
            remoteWindowPeer: remoteWindowPeer,
            remoteWindowPreparationPeer: remoteWindowPreparationPeer)
    {
    }

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        ISceneControlPeer scenePeer,
        TimeProvider? timeProvider = null) : this(
            localPeer,
            null,
            null,
            null,
            timeProvider,
            scenePeer)
    {
    }

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        TimeProvider? timeProvider = null) : this(
            localPeer,
            replacePeer,
            null,
            null,
            timeProvider)
    {
    }

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        IReplaceTargetInventoryPeer? replaceInventoryPeer,
        TimeProvider? timeProvider = null) : this(
            localPeer,
            replacePeer,
            replaceInventoryPeer,
            null,
            timeProvider)
    {
    }

    public AuthenticatedActivitySessionHandler(
        IActivityPeer localPeer,
        IReplacePeer? replacePeer,
        IReplaceTargetInventoryPeer? replaceInventoryPeer,
        ISwapEndpointPeer? swapPeer,
        TimeProvider? timeProvider = null,
        ISceneControlPeer? scenePeer = null,
        IRemoteWindowControlPeer? remoteWindowPeer = null,
        AuthenticatedRemoteWindowMediaSessionDirectory?
            remoteWindowMediaSessions = null,
        IRemoteWindowPreparationPeer? remoteWindowPreparationPeer = null)
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

        if (swapPeer is not null && swapPeer.DeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity and Swap peers must represent the same local device.",
                nameof(swapPeer));
        }


        if (scenePeer is not null && scenePeer.DeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity and Scene peers must represent the same local device.",
                nameof(scenePeer));
        }

        if (remoteWindowPeer is not null
            && remoteWindowPeer.HostDeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity and Remote Window peers must represent the same local device.",
                nameof(remoteWindowPeer));
        }

        if (remoteWindowPreparationPeer is not null
            && remoteWindowPreparationPeer.ParticipantDeviceId != localPeer.DeviceId)
        {
            throw new ArgumentException(
                "The Activity and Remote Window preparation peers must represent the same local device.",
                nameof(remoteWindowPreparationPeer));
        }

        this.localPeer = localPeer;
        this.replacePeer = replacePeer;
        this.replaceInventoryPeer = replaceInventoryPeer;
        this.swapPeer = swapPeer;
        this.scenePeer = scenePeer;
        this.remoteWindowPeer = remoteWindowPeer;
        this.remoteWindowPreparationPeer = remoteWindowPreparationPeer;
        this.remoteWindowMediaSessions = remoteWindowMediaSessions;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public bool IsReplaceEndpointAvailable =>
        Volatile.Read(ref disposed) == 0 && replacePeer is not null;

    public bool IsSwapEndpointAvailable =>
        Volatile.Read(ref disposed) == 0 && swapPeer is not null;

    public bool IsSceneEndpointAvailable =>
        Volatile.Read(ref disposed) == 0 && scenePeer is not null;

    public IReadOnlyList<DeviceId> GetConnectedPeers()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return [];
        }

        return sessions
            .Where(static pair => pair.Value.IsReady)
            .Select(static pair => pair.Key)
            .OrderBy(static id => id.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryGetChannel(
        DeviceId peerDeviceId,
        out IActivityChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady)
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
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady)
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
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady)
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryGetRemoteWindowChannel(
        DeviceId peerDeviceId,
        out IRemoteWindowControlChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.RemoteWindowSession is not null)
        {
            channel = registration.RemoteWindowSession;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryGetRemoteWindowPreparationChannel(
        DeviceId peerDeviceId,
        out IRemoteWindowPreparationChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.RemoteWindowPreparationChannel is not null)
        {
            channel = registration.RemoteWindowPreparationChannel;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryAcquireRemoteWindowConnection(
        DeviceId peerDeviceId,
        out AuthenticatedRemoteWindowConnectionLease? lease)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.TryAcquireRemoteWindowConnection(out lease))
        {
            return true;
        }

        lease = null;
        return false;
    }

    public bool TryGetSwapChannel(
        DeviceId peerDeviceId,
        out ISwapEndpointChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.Session.SupportsSwap)
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public IReadOnlyList<DeviceId> GetSceneParticipantDeviceIds()
    {
        lock (lifecycleGate)
        {
            if (disposed != 0)
            {
                return [];
            }

            return sessions
                .Where(static pair =>
                    pair.Value.IsReady
                    && pair.Value.Session.SupportsSceneApply)
                .Select(static pair => pair.Key)
                .OrderBy(static deviceId => deviceId.Value)
                .ToArray();
        }
    }

    public bool TryGetSceneSourceLookupChannel(
        DeviceId peerDeviceId,
        out ISceneSourceLookupChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.Session.SupportsSceneApply)
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryGetSceneExactSlotChannel(
        DeviceId peerDeviceId,
        out ISceneExactSlotChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.Session.SupportsSceneApply)
        {
            channel = registration.Session;
            return true;
        }

        channel = null;
        return false;
    }

    public bool TryGetSceneChildOperationChannel(
        DeviceId peerDeviceId,
        out ISceneChildOperationChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (Volatile.Read(ref disposed) == 0
            && sessions.TryGetValue(peerDeviceId, out Registration? registration)
            && registration.IsReady
            && registration.Session.SupportsSceneApply)
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
        AuthenticatedRemoteWindowMediaSessionRegistration? mediaRegistration = null;
        if (remoteWindowMediaSessions is not null
            && ProtocolFeatures.SupportsRemoteWindowMediaRoute(
                connection.ProtocolVersion))
        {
            mediaRegistration = await remoteWindowMediaSessions
                .RegisterAsync(connection)
                .ConfigureAwait(false);
        }
        bool registrationTransferred = false;
        try
        {
            var dispatcher = new AuthenticatedControlSessionDispatcher(connection);
            await RunWithOwnedDispatcherAsync(
                dispatcher,
                () =>
                {
                    registrationTransferred = true;
                    return RunRegisteredSessionAsync(
                        connection.PeerIdentity.DeviceId,
                        dispatcher,
                        mediaRegistration,
                        cancellationToken);
                }).ConfigureAwait(false);
        }
        finally
        {
            if (!registrationTransferred && mediaRegistration is not null)
            {
                await mediaRegistration.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask RunRegisteredSessionAsync(
        DeviceId peerDeviceId,
        AuthenticatedControlSessionDispatcher dispatcher,
        AuthenticatedRemoteWindowMediaSessionRegistration? mediaRegistration,
        CancellationToken cancellationToken)
    {
        ActivityControlSession? constructedSession = null;
        RemoteWindowControlSession? constructedRemoteWindowSession = null;
        Registration? constructedRegistration = null;
        try
        {
            constructedSession = new ActivityControlSession(
                dispatcher.ActivityConnection,
                localPeer,
                replacePeer,
                replaceInventoryPeer,
                swapPeer,
                timeProvider,
                scenePeer);
            constructedRemoteWindowSession =
                dispatcher.RemoteWindowConnection is null
                    ? null
                    : new RemoteWindowControlSession(
                        dispatcher.RemoteWindowConnection,
                        remoteWindowPeer,
                        timeProvider,
                        remoteWindowPreparationPeer);
            constructedRegistration = new Registration(
                this,
                constructedSession,
                constructedRemoteWindowSession,
                mediaRegistration,
                ProtocolFeatures.SupportsRemoteWindowPreparation(
                    dispatcher.ActivityConnection.ProtocolVersion),
                GetNextRemoteWindowConnectionGeneration());
        }
        catch (Exception constructionFailure)
        {
            Exception? constructionCleanupFailure =
                await DisposeMediaRegistrationAsync(mediaRegistration)
                    .ConfigureAwait(false);
            if (constructedSession is not null)
            {
                constructionCleanupFailure = CombineFailures(
                    constructionCleanupFailure,
                    await DisposeSessionsAsync(
                        constructedSession,
                        constructedRemoteWindowSession).ConfigureAwait(false));
            }

            Exception combinedConstructionFailure = CombineFailures(
                constructionFailure,
                constructionCleanupFailure) ?? constructionFailure;
            ExceptionDispatchInfo.Capture(combinedConstructionFailure).Throw();
            throw;
        }

        ActivityControlSession session = constructedSession;
        RemoteWindowControlSession? remoteWindowSession =
            constructedRemoteWindowSession;
        Registration registration = constructedRegistration;
        bool handlerDisposed;
        bool registered;
        lock (lifecycleGate)
        {
            handlerDisposed = disposed != 0;
            registered = !handlerDisposed
                && sessions.TryAdd(
                    peerDeviceId,
                    registration);
            if (registered)
            {
                activeRegistrations.Add(registration);
            }
        }

        if (!registered)
        {
            Exception? rejectionCleanupFailure =
                await CaptureOwnedCleanupFailureAsync(registration)
                    .ConfigureAwait(false);
            rejectionCleanupFailure = CombineFailures(
                rejectionCleanupFailure,
                await DisposeSessionsAsync(
                    session,
                    remoteWindowSession).ConfigureAwait(false));
            if (rejectionCleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(rejectionCleanupFailure).Throw();
            }

            ObjectDisposedException.ThrowIf(handlerDisposed, this);
            throw new InvalidDataException(
                "A second authenticated Activity session for this peer was rejected.");
        }

        Exception? runFailure = null;
        try
        {
            using CancellationTokenSource linked = mediaRegistration is null
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token,
                    mediaRegistration.ControlStopToken);
            await dispatcher.RunAsync(
                session,
                remoteWindowSession,
                EnterSessionCall,
                onStarted: () => PublishStartedRegistration(
                    peerDeviceId,
                    registration),
                beginOwnedCleanup: mediaRegistration is null
                    ? null
                    : registration.BeginOwnedCleanupAsync,
                cancellationToken: linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            runFailure = exception;
        }

        Exception? cleanupFailure =
            await CaptureOwnedCleanupFailureAsync(registration)
                .ConfigureAwait(false);
        bool removed = sessions.TryRemove(
            new KeyValuePair<DeviceId, Registration>(
                peerDeviceId,
                registration));
        cleanupFailure = CombineFailures(
            cleanupFailure,
            await DisposeSessionsAsync(
                session,
                remoteWindowSession).ConfigureAwait(false));
        if (removed && registration.IsReady)
        {
            using (EnterSessionCall())
            {
                PublishChanged();
            }
        }

        lock (lifecycleGate)
        {
            activeRegistrations.Remove(registration);
        }

        if (cleanupFailure is null)
        {
            registration.Completion.TrySetResult();
        }
        else
        {
            registration.Completion.TrySetException(cleanupFailure);
        }

        Exception? failure = CombineFailures(runFailure, cleanupFailure);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static async ValueTask RunWithOwnedDispatcherAsync(
        AuthenticatedControlSessionDispatcher dispatcher,
        Func<ValueTask> run)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(run);
        Exception? failure = null;
        try
        {
            await run().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (!dispatcher.HasStartedStopping)
        {
            try
            {
                await dispatcher.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public ValueTask DisposeAsync()
    {
        bool calledFromActiveSession = IsActiveSessionCall();
        if (Interlocked.CompareExchange(ref disposalCleanupStarted, 1, 0) == 0)
        {
            Registration[] active;
            lock (lifecycleGate)
            {
                Volatile.Write(ref disposed, 1);
                active = [.. activeRegistrations];
            }

            _ = CompleteDisposalAsync(active);
        }

        return calledFromActiveSession
            ? ValueTask.CompletedTask
            : new ValueTask(disposalCompletion.Task);
    }

    private async Task CompleteDisposalAsync(Registration[] active)
    {
        var failures = new List<Exception>();
        var mediaCleanup = new List<Task>(active.Length);
        try
        {
            foreach (Registration registration in active)
            {
                try
                {
                    if (registration.MediaRegistration is not null)
                    {
                        mediaCleanup.Add(
                            registration.BeginOwnedCleanupAsync().AsTask());
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            CaptureFailure(failures, lifetimeCancellation.Cancel);
            foreach (Registration registration in active)
            {
                CaptureFailure(failures, registration.Session.Cancel);
                if (registration.RemoteWindowSession is not null)
                {
                    CaptureFailure(
                        failures,
                        registration.RemoteWindowSession.Cancel);
                }
            }

            failures.AddRange(await CollectCompletionFailuresAsync(
                active.Select(static item => item.Completion.Task))
                .ConfigureAwait(false));
            Exception[] mediaFailures = await CollectCompletionFailuresAsync(
                mediaCleanup).ConfigureAwait(false);
            foreach (Exception mediaFailure in mediaFailures)
            {
                if (!failures.Any(existing =>
                        ReferenceEquals(existing, mediaFailure)))
                {
                    failures.Add(mediaFailure);
                }
            }

            CaptureFailure(failures, lifetimeCancellation.Dispose);
            if (failures.Count == 0)
            {
                disposalCompletion.TrySetResult();
            }
            else
            {
                disposalCompletion.TrySetException(failures.Count == 1
                    ? failures[0]
                    : new AggregateException(
                        "Authenticated control session disposal failed.",
                        failures));
            }
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
        }
    }

    private static void CaptureFailure(
        List<Exception> failures,
        Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static Exception? CombineFailures(
        Exception? primary,
        Exception? secondary) => (primary, secondary) switch
        {
            (null, null) => null,
            (not null, null) => primary,
            (null, not null) => secondary,
            _ => new AggregateException(
                "The authenticated control session and its cleanup failed.",
                primary!,
                secondary!),
        };

    internal static async ValueTask<Exception[]> CollectCompletionFailuresAsync(
        IEnumerable<Task> completions)
    {
        Task all = Task.WhenAll(completions);
        try
        {
            await all.ConfigureAwait(false);
            return [];
        }
        catch (Exception exception)
        {
            return all.Exception?.Flatten().InnerExceptions.ToArray()
                ?? [exception];
        }
    }

    internal static async ValueTask<Exception?> DisposeSessionsAsync(
        ActivityControlSession session,
        RemoteWindowControlSession? remoteWindowSession)
    {
        var failures = new List<Exception>();
        if (remoteWindowSession is not null)
        {
            try
            {
                await remoteWindowSession.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                "Authenticated child session cleanup failed.",
                failures),
        };
    }

    private static async ValueTask<Exception?> DisposeMediaRegistrationAsync(
        AuthenticatedRemoteWindowMediaSessionRegistration? registration)
    {
        if (registration is null)
        {
            return null;
        }

        try
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async ValueTask<Exception?> CaptureOwnedCleanupFailureAsync(
        Registration registration)
    {
        try
        {
            await registration.BeginOwnedCleanupAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private long GetNextRemoteWindowConnectionGeneration()
    {
        long generation = Interlocked.Increment(
            ref nextRemoteWindowConnectionGeneration);
        if (generation <= 0)
        {
            throw new InvalidOperationException(
                "The Remote Window connection generation space was exhausted.");
        }

        return generation;
    }

    private IDisposable EnterSessionCall()
    {
        SessionCallScope? inheritedScope = activeSessionCall.Value;
        var currentScope = new SessionCallScope(this, inheritedScope);
        activeSessionCall.Value = currentScope;
        return new SessionCallLease(this, currentScope, inheritedScope);
    }

    private void ExitSessionCall(
        SessionCallScope currentScope,
        SessionCallScope? inheritedScope)
    {
        currentScope.Deactivate();
        activeSessionCall.Value = inheritedScope;
    }

    private bool IsActiveSessionCall()
    {
        for (SessionCallScope? scope = activeSessionCall.Value;
            scope is not null;
            scope = scope.Previous)
        {
            if (scope.IsActive && ReferenceEquals(scope.Owner, this))
            {
                return true;
            }
        }

        return RemoteWindowConnectionGeneration.IsActiveRevocationCallback(
            revocationCallbackOwner);
    }

    private void PublishStartedRegistration(
        DeviceId peerDeviceId,
        Registration registration)
    {
        bool publish;
        lock (lifecycleGate)
        {
            publish = disposed == 0
                && sessions.TryGetValue(
                    peerDeviceId,
                    out Registration? current)
                && ReferenceEquals(current, registration);
            if (publish)
            {
                registration.MarkReady();
            }
        }

        if (publish)
        {
            using (EnterSessionCall())
            {
                PublishChanged();
            }
        }
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

    private sealed class Registration(
        AuthenticatedActivitySessionHandler owner,
        ActivityControlSession session,
        RemoteWindowControlSession? remoteWindowSession,
        AuthenticatedRemoteWindowMediaSessionRegistration? mediaRegistration,
        bool supportsRemoteWindowPreparation,
        long remoteWindowConnectionGeneration)
    {
        private readonly object ownedCleanupGate = new();
        private readonly RemoteWindowConnectionGeneration? remoteWindowGeneration =
            supportsRemoteWindowPreparation
            && remoteWindowSession is not null
            && mediaRegistration is not null
                ? new RemoteWindowConnectionGeneration(
                    remoteWindowConnectionGeneration,
                    owner.revocationCallbackOwner)
                : null;
        private Task? ownedCleanup;
        private int ready;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsReady => Volatile.Read(ref ready) != 0;

        public ActivityControlSession Session { get; } = session;

        public AuthenticatedRemoteWindowMediaSessionRegistration? MediaRegistration { get; } =
            mediaRegistration;

        public RemoteWindowControlSession? RemoteWindowSession { get; } =
            remoteWindowSession;

        public IRemoteWindowPreparationChannel? RemoteWindowPreparationChannel { get; } =
            supportsRemoteWindowPreparation
                ? remoteWindowSession
                : null;

        public void MarkReady() => Volatile.Write(ref ready, 1);

        public ValueTask BeginOwnedCleanupAsync()
        {
            TaskCompletionSource completion;
            Task cleanup;
            lock (ownedCleanupGate)
            {
                if (ownedCleanup is not null)
                {
                    return new ValueTask(ownedCleanup);
                }

                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cleanup = completion.Task;
                ownedCleanup = cleanup;
            }

            _ = CompleteOwnedCleanupAsync(completion);
            return new ValueTask(cleanup);
        }

        public bool TryAcquireRemoteWindowConnection(
            out AuthenticatedRemoteWindowConnectionLease? lease)
        {
            IRemoteWindowPreparationChannel? channel =
                RemoteWindowPreparationChannel;
            RemoteWindowConnectionGeneration? generation =
                remoteWindowGeneration;
            AuthenticatedRemoteWindowMediaSessionRegistration? media =
                MediaRegistration;
            if (channel is not null
                && generation is not null
                && media is not null)
            {
                return generation.TryAcquire(channel, media.Session, out lease);
            }

            lease = null;
            return false;
        }

        private async Task CompleteOwnedCleanupAsync(
            TaskCompletionSource completion)
        {
            try
            {
                Exception? failure = null;
                try
                {
                    remoteWindowGeneration?.Dispose();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                Exception? mediaFailure = await DisposeMediaRegistrationAsync(
                        MediaRegistration)
                    .ConfigureAwait(false);
                Exception? combined = CombineFailures(failure, mediaFailure);
                if (combined is not null)
                {
                    ExceptionDispatchInfo.Capture(combined).Throw();
                }

                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private sealed class SessionCallLease(
        AuthenticatedActivitySessionHandler owner,
        SessionCallScope currentScope,
        SessionCallScope? inheritedScope) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.ExitSessionCall(currentScope, inheritedScope);
            }
        }
    }

    private sealed class SessionCallScope(
        AuthenticatedActivitySessionHandler owner,
        SessionCallScope? previous)
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public AuthenticatedActivitySessionHandler Owner { get; } = owner;

        public SessionCallScope? Previous { get; } = previous;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }
}
