using System.Collections.Immutable;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal sealed class DesktopActivityRuntime : IDesktopActivityService
{
    private static readonly TimeSpan OperationLifetime = TimeSpan.FromSeconds(30);
    private readonly Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity;
    private readonly Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IReplaceStatePayloadStore? replaceStatePayloadStore;
    private readonly TimeProvider timeProvider;
    private InMemoryActivityCatalog? catalog;
    private AuthenticatedActivitySessionHandler? handler;
    private FlowspanNode? node;
    private ReplaceEndpoint? replaceEndpoint;
    private PersistentReplaceStateStore? replaceState;
    private TrustSessionCoordinator? trust;
    private int disposed;

    public DesktopActivityRuntime(
        Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity,
        Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust,
        TimeProvider? timeProvider = null,
        IReplaceStatePayloadStore? replaceStatePayloadStore = null)
    {
        ArgumentNullException.ThrowIfNull(getIdentity);
        ArgumentNullException.ThrowIfNull(getTrust);
        this.getIdentity = getIdentity;
        this.getTrust = getTrust;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.replaceStatePayloadStore = replaceStatePayloadStore;
    }

    public event Action? Changed;

    public bool IsReady => Volatile.Read(ref node) is not null;

    public DesktopActivitySnapshot CreateWorkspaceNote(
        string title,
        string text,
        ActivitySensitivity sensitivity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!Enum.IsDefined(sensitivity))
        {
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        }

        if (text.Length is < 1 or > WorkspaceNoteAdapter.MaximumTextCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"A portable note must contain 1 to {WorkspaceNoteAdapter.MaximumTextCharacters} characters.");
        }

        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            WorkspaceNoteKind,
            currentNode.DeviceId,
            title,
            JsonSerializer.Serialize(new { text }),
            sensitivity);
        var activity = ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(currentNode.DeviceId, "desktop"));
        if (!currentNode.AddLocalActivity(activity))
        {
            throw new InvalidOperationException(
                "The generated Activity ID already exists.");
        }

        DesktopActivitySnapshot snapshot = CreateSnapshot(activity);
        PublishChanged();
        return snapshot;
    }

    public ImmutableArray<DesktopActivitySnapshot> GetActivities()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return catalog?.Snapshot()
            .Where(static activity => activity.Lifecycle == ActivityLifecycle.Active)
            .Select(CreateSnapshot)
            .ToImmutableArray() ?? [];
    }

    public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        AuthenticatedActivitySessionHandler? currentHandler = handler;
        TrustSessionCoordinator? currentTrust = trust;
        if (currentHandler is null || currentTrust is null)
        {
            return [];
        }

        var targets = ImmutableArray.CreateBuilder<DesktopActivityTargetSnapshot>();
        foreach (DeviceId peerDeviceId in currentHandler.GetConnectedPeers())
        {
            if (currentTrust.TryGetCurrentTrust(
                    peerDeviceId,
                    out TrustRecord? record)
                && record.GrantedCapabilities.Allows(Capability.ActivityReceive))
            {
                targets.Add(new DesktopActivityTargetSnapshot(
                    peerDeviceId,
                    record.PeerIdentity.DisplayName));
            }
        }

        return targets.ToImmutable();
    }

    public DesktopReplaceRecoveryResult GetReplaceRecoveryState()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        PersistentReplaceStateStore? current = replaceState;
        if (current is null)
        {
            return DesktopReplaceRecoveryResult.Unavailable;
        }

        try
        {
            FlowspanNode? currentNode = node;
            InMemoryActivityCatalog? currentCatalog = catalog;
            if (currentNode is null || currentCatalog is null)
            {
                return DesktopReplaceRecoveryResult.Unavailable;
            }

            ReplaceRecoverySnapshot snapshot =
                current.GetRecoverySnapshot(timeProvider.GetUtcNow());
            HashSet<UndoCapsuleId> availableCapsules = snapshot.Records
                .Where(static record =>
                    record.Kind == ReplaceRecoveryOperationKind.Replace
                    && record.UndoAvailability == ReplaceUndoAvailability.Available
                    && record.CapsuleId is not null)
                .Select(static record => record.CapsuleId!)
                .ToHashSet();
            ReplaceRestartRecoveryPlan plan = current.GetRestartRecoveryPlan(
                currentNode.DeviceId);
            ImmutableArray<UndoCapsuleId> undoable = plan.UndoCandidates
                .Where(candidate => availableCapsules.Contains(candidate.CapsuleId))
                .Where(candidate => currentCatalog.TryGet(
                    candidate.ExactReplacement.Descriptor.Id,
                    out ActivityInstance? exact)
                    && exact == candidate.ExactReplacement)
                .Select(static candidate => candidate.CapsuleId)
                .OrderBy(static capsuleId => capsuleId.ToString(), StringComparer.Ordinal)
                .ToImmutableArray();
            return DesktopReplaceRecoveryResult.Available(
                snapshot,
                undoable);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DesktopReplaceRecoveryResult.Unavailable;
        }
    }

    public async ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OutboundOperationPreparation preparation = PrepareOutboundOperation(
            activityId,
            targetDeviceId,
            OperationKind.Handoff);
        if (preparation.FailureReceipt is not null)
        {
            return preparation.FailureReceipt;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        return await preparation.Node.HandoffAsync(
            activityId,
            preparation.Channel!,
            "desktop",
            preparation.Context,
            linked.Token).ConfigureAwait(false);
    }

    public async ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleId capsuleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capsuleId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OperationContext context = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            timeProvider.GetUtcNow().Add(OperationLifetime));
        ReplaceEndpoint? current = replaceEndpoint;
        if (current is null || !CanAttemptTargetLocalUndo(capsuleId))
        {
            return UndoReplaceResult.Failed(
                context,
                capsuleId,
                FailureCode.UndoUnavailable,
                timeProvider.GetUtcNow());
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            return await current.UndoReplaceAsync(
                capsuleId,
                context,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            PublishChanged();
        }
    }

    private bool CanAttemptTargetLocalUndo(UndoCapsuleId capsuleId)
    {
        PersistentReplaceStateStore? currentState = replaceState;
        InMemoryActivityCatalog? currentCatalog = catalog;
        FlowspanNode? currentNode = node;
        if (currentState is null || currentCatalog is null || currentNode is null)
        {
            return false;
        }

        try
        {
            DesktopReplaceRecoveryResult recovery = GetReplaceRecoveryState();
            if (!recovery.IsAvailable)
            {
                return false;
            }

            if (recovery.UndoableCapsuleIds.Contains(capsuleId))
            {
                return true;
            }

            ReplaceRestartRecoveryPlan plan = currentState.GetRestartRecoveryPlan(
                currentNode.DeviceId);
            if (plan.IsBlockedByUnresolvedOperation
                || !currentState.TryGet(capsuleId, out UndoCapsule? capsule)
                || capsule is null)
            {
                return false;
            }

            if (capsule.ExpiresAt <= timeProvider.GetUtcNow())
            {
                return true;
            }

            return !currentCatalog.TryGet(
                    capsule.ReplacementActivity.Descriptor.Id,
                    out ActivityInstance? exact)
                || exact != capsule.ReplacementActivity;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public async ValueTask<OperationReceipt> MoveAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OutboundOperationPreparation preparation = PrepareOutboundOperation(
            activityId,
            targetDeviceId,
            OperationKind.Move);
        if (preparation.FailureReceipt is not null)
        {
            return preparation.FailureReceipt;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        OperationReceipt receipt = await preparation.Node.MoveAsync(
            activityId,
            preparation.Channel!,
            "desktop",
            preparation.Context,
            linked.Token).ConfigureAwait(false);
        if (receipt.Status == OperationStatus.Committed)
        {
            PublishChanged();
        }

        return receipt;
    }

    public async ValueTask<DesktopReplaceTargetInventoryResult> GetReplaceTargetsAsync(
        ActivityId incomingActivityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incomingActivityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        if (!currentNode.TryGetActivity(
                incomingActivityId,
                out ActivityInstance? incoming)
            || incoming.Lifecycle != ActivityLifecycle.Active)
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                FailureCode.ActivityNotFound);
        }

        TrustSessionCoordinator? currentTrust = trust;
        if (currentTrust is null
            || !currentTrust.TryGetCurrentTrust(
                targetDeviceId,
                out TrustRecord? record)
            || !record.GrantedCapabilities.Allows(Capability.ActivityReceive))
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                FailureCode.CapabilityDenied);
        }

        if (handler is null
            || !handler.TryGetReplaceInventoryChannel(
                targetDeviceId,
                out IReplaceTargetInventoryChannel? channel)
            || channel is null)
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                FailureCode.PeerUnavailable);
        }

        var query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.From(Guid.NewGuid()),
            targetDeviceId,
            incoming.Descriptor.Kind,
            timeProvider.GetUtcNow().Add(OperationLifetime));
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        ReplaceTargetInventoryDeliveryResult delivered = await channel.QueryAsync(
            currentNode.DeviceId,
            query,
            linked.Token).ConfigureAwait(false);
        if (delivered.Status != ActivityDeliveryStatus.Acknowledged
            || delivered.Result is not ReplaceTargetInventoryResult result)
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                delivered.Status == ActivityDeliveryStatus.AcknowledgementLost
                    ? FailureCode.AcknowledgementLost
                    : FailureCode.PeerUnavailable);
        }

        if (!result.IsSuccess)
        {
            return DesktopReplaceTargetInventoryResult.Failed(result.FailureCode);
        }

        return new DesktopReplaceTargetInventoryResult(
            FailureCode.None,
            result.IsTruncated,
            result.CapturedAt,
            result.Targets
                .Select(target => new DesktopReplaceTargetSnapshot(
                    targetDeviceId,
                    target.ActivityId,
                    target.Title,
                    target.Kind.Value,
                    target.Revision,
                    target.DescriptorDigest,
                    target.PlacementSlot))
                .ToImmutableArray());
    }

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        await initializationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (node is not null)
            {
                return;
            }

            DeviceIdentity identity = await getIdentity(linked.Token)
                .ConfigureAwait(false);
            TrustSessionCoordinator coordinator = await getTrust(linked.Token)
                .ConfigureAwait(false);
            var newCatalog = new InMemoryActivityCatalog();
            var workspaceNoteAdapter = new WorkspaceNoteAdapter();
            var adapterRegistry = new ActivityAdapterRegistry(
                [workspaceNoteAdapter]);
            var newNode = new FlowspanNode(
                identity.DeviceId,
                identity.DisplayName,
                new TimeProviderClock(timeProvider),
                newCatalog,
                new InMemoryOperationJournal(),
                adapterRegistry,
                NullReceiptSink.Instance);
            var authorizedPeer = new TrustBoundActivityPeer(
                newNode,
                coordinator,
                PublishChanged);
            var inventoryEndpoint = new ReplaceTargetInventoryEndpoint(
                identity.DeviceId,
                new TimeProviderClock(timeProvider),
                newCatalog,
                adapterRegistry);
            var authorizedInventoryPeer = new TrustBoundReplaceInventoryPeer(
                inventoryEndpoint,
                coordinator);
            PersistentReplaceStateStore? newReplaceState = null;
            ReplaceEndpoint? newReplaceEndpoint = null;
            if (replaceStatePayloadStore is not null)
            {
                try
                {
                    newReplaceState = await PersistentReplaceStateStore.OpenAsync(
                        replaceStatePayloadStore,
                        linked.Token).ConfigureAwait(false);
                    ReplaceRestartRecoveryPlan recoveryPlan =
                        newReplaceState.GetRestartRecoveryPlan(identity.DeviceId);
                    foreach (ActivityInstance activity in recoveryPlan.CurrentActivities)
                    {
                        if (activity.Descriptor.Kind != workspaceNoteAdapter.Kind)
                        {
                            throw new InvalidDataException(
                                "The protected Replace state contains an unsupported restart Activity kind.");
                        }

                        ResumeActivityResult resumed = await workspaceNoteAdapter
                            .ResumeAsync(
                                activity.Descriptor,
                                activity.Placement,
                                linked.Token)
                            .ConfigureAwait(false);
                        if (!resumed.Succeeded || !newCatalog.TryAdd(activity))
                        {
                            throw new InvalidDataException(
                                "The protected Replace state could not reconstruct an exact semantic Activity frontier.");
                        }
                    }

                    newReplaceEndpoint = new ReplaceEndpoint(
                        identity.DeviceId,
                        new TimeProviderClock(timeProvider),
                        newCatalog,
                        newReplaceState,
                        adapterRegistry,
                        newReplaceState,
                        new CryptographicUndoCapsuleIdSource(),
                        NullReceiptSink.Instance);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    newReplaceEndpoint?.Dispose();
                    newReplaceState?.Dispose();
                    newReplaceEndpoint = null;
                    newReplaceState = null;
                }
            }

            AuthenticatedActivitySessionHandler newHandler;
            try
            {
                newHandler = new AuthenticatedActivitySessionHandler(
                    authorizedPeer,
                    replacePeer: null,
                    authorizedInventoryPeer,
                    timeProvider);
            }
            catch
            {
                newReplaceEndpoint?.Dispose();
                newReplaceState?.Dispose();
                throw;
            }
            newHandler.Changed += OnHandlerChanged;
            catalog = newCatalog;
            trust = coordinator;
            handler = newHandler;
            replaceEndpoint = newReplaceEndpoint;
            replaceState = newReplaceState;
            Volatile.Write(ref node, newNode);
        }
        finally
        {
            initializationGate.Release();
        }

        PublishChanged();
    }

    internal async ValueTask<AuthenticatedActivitySessionHandler>
        GetSessionHandlerAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return handler
            ?? throw new InvalidOperationException(
                "The Activity session handler was not initialized.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        await initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            AuthenticatedActivitySessionHandler? current = handler;
            handler = null;
            if (current is not null)
            {
                current.Changed -= OnHandlerChanged;
                await current.DisposeAsync().ConfigureAwait(false);
            }

            Volatile.Write(ref node, null);
            ReplaceEndpoint? currentReplaceEndpoint = replaceEndpoint;
            replaceEndpoint = null;
            currentReplaceEndpoint?.Dispose();
            PersistentReplaceStateStore? currentReplaceState = replaceState;
            replaceState = null;
            currentReplaceState?.Dispose();
            catalog = null;
            trust = null;
        }
        finally
        {
            initializationGate.Release();
            initializationGate.Dispose();
            lifetimeCancellation.Dispose();
        }
    }

    private static ActivityKind WorkspaceNoteKind { get; } =
        ActivityKind.Parse("workspace.note/v1");

    private OutboundOperationPreparation PrepareOutboundOperation(
        ActivityId activityId,
        DeviceId targetDeviceId,
        OperationKind kind)
    {
        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        OperationContext context = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            timeProvider.GetUtcNow().Add(OperationLifetime));
        if (!currentNode.TryGetActivity(activityId, out ActivityInstance? activity))
        {
            return new OutboundOperationPreparation(
                currentNode,
                context,
                null,
                OperationReceipt.RejectedMissingActivity(
                    context.OperationId,
                    context.CorrelationId,
                    kind,
                    currentNode.DeviceId,
                    targetDeviceId,
                    activityId,
                    timeProvider.GetUtcNow()));
        }

        TrustSessionCoordinator? currentTrust = trust;
        if (currentTrust is null
            || !currentTrust.TryGetCurrentTrust(
                targetDeviceId,
                out TrustRecord? record)
            || !record.GrantedCapabilities.Allows(Capability.ActivityReceive))
        {
            return new OutboundOperationPreparation(
                currentNode,
                context,
                null,
                OperationReceipt.Rejected(
                    context.OperationId,
                    context.CorrelationId,
                    kind,
                    currentNode.DeviceId,
                    targetDeviceId,
                    activity.Descriptor,
                    timeProvider.GetUtcNow(),
                    FailureCode.CapabilityDenied));
        }

        if (handler is null
            || !handler.TryGetChannel(
                targetDeviceId,
                out IActivityChannel? channel)
            || channel is null)
        {
            return new OutboundOperationPreparation(
                currentNode,
                context,
                null,
                OperationReceipt.Failed(
                    context.OperationId,
                    context.CorrelationId,
                    kind,
                    currentNode.DeviceId,
                    targetDeviceId,
                    activity.Descriptor,
                    timeProvider.GetUtcNow(),
                    FailureCode.PeerUnavailable));
        }

        return new OutboundOperationPreparation(
            currentNode,
            context,
            channel,
            null);
    }

    private static DesktopActivitySnapshot CreateSnapshot(ActivityInstance activity) => new(
        activity.Descriptor.Id,
        activity.Descriptor.Title,
        activity.Descriptor.Kind.Value,
        activity.Descriptor.Sensitivity,
        activity.Lifecycle);

    private void OnHandlerChanged() => PublishChanged();

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
                // Presentation observers cannot own Activity/session lifetime.
            }
        }
    }

    private sealed class TimeProviderClock(TimeProvider timeProvider) : IClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }

    private sealed record OutboundOperationPreparation(
        FlowspanNode Node,
        OperationContext Context,
        IActivityChannel? Channel,
        OperationReceipt? FailureReceipt);

    private sealed class TrustBoundActivityPeer(
        FlowspanNode node,
        TrustSessionCoordinator trust,
        Action activityChanged) : IActivityPeer
    {
        public DeviceId DeviceId => node.DeviceId;

        public async ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken)
        {
            CapabilityGrant grant = trust.TryGetCurrentTrust(
                senderDeviceId,
                out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
            node.SetPeerGrant(senderDeviceId, grant);
            OperationReceipt receipt = await node.ReceiveActivityAsync(
                senderDeviceId,
                offer,
                cancellationToken).ConfigureAwait(false);
            if (receipt.IsSuccess)
            {
                activityChanged();
            }

            return receipt;
        }
    }

    private sealed class TrustBoundReplaceInventoryPeer(
        ReplaceTargetInventoryEndpoint endpoint,
        TrustSessionCoordinator trust) : IReplaceTargetInventoryPeer
    {
        public DeviceId DeviceId => endpoint.DeviceId;

        public ValueTask<ReplaceTargetInventoryResult> QueryAsync(
            DeviceId requestingDeviceId,
            ReplaceTargetInventoryQuery query,
            CancellationToken cancellationToken)
        {
            CapabilityGrant grant = trust.TryGetCurrentTrust(
                requestingDeviceId,
                out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
            endpoint.SetPeerGrant(requestingDeviceId, grant);
            return endpoint.QueryAsync(
                requestingDeviceId,
                query,
                cancellationToken);
        }
    }
}
