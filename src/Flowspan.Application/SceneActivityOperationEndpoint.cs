using System.Collections.Concurrent;
using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class SceneActivityOperationEndpoint
{
    private readonly IClock clock;
    private readonly FlowspanNode node;
    private readonly ConcurrentDictionary<DeviceId, CapabilityGrant> peerGrants =
        new();
    private readonly SceneApplyPreflightEndpoint preflight;
    private readonly ReplaceEndpoint? replaceEndpoint;

    public SceneActivityOperationEndpoint(
        FlowspanNode node,
        SceneApplyPreflightEndpoint preflight,
        ReplaceEndpoint? replaceEndpoint = null,
        IClock? clock = null)
    {
        this.node = node ?? throw new ArgumentNullException(nameof(node));
        this.preflight = preflight
            ?? throw new ArgumentNullException(nameof(preflight));
        this.replaceEndpoint = replaceEndpoint;
        this.clock = clock ?? SystemClock.Instance;
        if (preflight.DeviceId != node.DeviceId
            || (replaceEndpoint is not null
                && replaceEndpoint.DeviceId != node.DeviceId))
        {
            throw new ArgumentException(
                "A Scene operation endpoint must use boundaries owned by one device.",
                nameof(preflight));
        }
    }

    public DeviceId DeviceId => node.DeviceId;

    public void SetPeerGrant(DeviceId peerDeviceId, CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(grant);
        peerGrants[peerDeviceId] = grant;
        preflight.SetPeerGrant(peerDeviceId, grant);
        node.SetPeerGrant(peerDeviceId, grant);
        replaceEndpoint?.SetPeerGrant(peerDeviceId, grant);
    }

    internal bool Allows(DeviceId peerDeviceId, Capability capability) =>
        peerDeviceId == DeviceId
        || (peerGrants.TryGetValue(
                peerDeviceId,
                out CapabilityGrant? grant)
            && grant.Allows(capability));

    internal ValueTask<SceneSourceLookup> LocateSourceAsync(
        DeviceId coordinatorDeviceId,
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken) =>
        preflight.LocateSourceAsync(
            coordinatorDeviceId,
            activityId,
            index,
            childContext,
            cancellationToken);

    internal ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        DeviceId coordinatorDeviceId,
        SceneActivityPlan item,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken) =>
        preflight.InspectExactSlotAsync(
            coordinatorDeviceId,
            item,
            source,
            childContext,
            cancellationToken);

    internal FlowspanNode Node => node;

    internal ReplaceEndpoint? ReplaceEndpoint => replaceEndpoint;

    internal ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleReference capsule,
        OperationContext context,
        CancellationToken cancellationToken) =>
        replaceEndpoint is null
            ? ValueTask.FromResult(UndoReplaceResult.Failed(
                context,
                capsule.Id,
                FailureCode.UndoUnavailable,
                clock.UtcNow))
            : replaceEndpoint.UndoReplaceAsync(
                capsule,
                context,
                cancellationToken);
}

public interface ISceneOperationRouteDirectory
{
    public IReadOnlyList<DeviceId> GetSceneParticipantDeviceIds();

    public bool TryGetChannel(
        DeviceId peerDeviceId,
        out IActivityChannel? channel);

    public bool TryGetReplaceChannel(
        DeviceId peerDeviceId,
        out IReplaceChannel? channel);

    public bool TryGetSceneExactSlotChannel(
        DeviceId peerDeviceId,
        out ISceneExactSlotChannel? channel);

    public bool TryGetSceneSourceLookupChannel(
        DeviceId peerDeviceId,
        out ISceneSourceLookupChannel? channel);

    public bool TryGetSceneChildOperationChannel(
        DeviceId peerDeviceId,
        out ISceneChildOperationChannel? channel);
}

public sealed class RoutedSceneActivityOperationPort :
    ISceneActivityOperationPort
{
    private readonly IClock clock;
    private readonly DeviceId? coordinatorDeviceId;
    private readonly ISceneOperationRouteDirectory routes;
    private readonly SceneActivityOperationEndpoint sourceEndpoint;

    public RoutedSceneActivityOperationPort(
        IClock clock,
        DeviceId coordinatorDeviceId,
        SceneActivityOperationEndpoint sourceEndpoint,
        ISceneOperationRouteDirectory routes)
        : this(
            clock,
            sourceEndpoint,
            routes,
            coordinatorDeviceId
                ?? throw new ArgumentNullException(nameof(coordinatorDeviceId)))
    {
    }

    public RoutedSceneActivityOperationPort(
        IClock clock,
        SceneActivityOperationEndpoint sourceEndpoint,
        ISceneOperationRouteDirectory routes)
        : this(clock, sourceEndpoint, routes, coordinatorDeviceId: null)
    {
    }

    private RoutedSceneActivityOperationPort(
        IClock clock,
        SceneActivityOperationEndpoint sourceEndpoint,
        ISceneOperationRouteDirectory routes,
        DeviceId? coordinatorDeviceId)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.coordinatorDeviceId = coordinatorDeviceId;
        this.sourceEndpoint = sourceEndpoint
            ?? throw new ArgumentNullException(nameof(sourceEndpoint));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async ValueTask<SceneActivityOperationResult> ExecuteAsync(
        SceneActivityPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        SceneApplyItemPreview item = preparation.Item;
        SceneSourceSelection source = item.Source
            ?? throw new ArgumentException(
                "An executable Scene item requires an exact source.",
                nameof(preparation));
        OperationKind operationKind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentException(
                "A routed Scene operation requires an executable item.",
                nameof(preparation)),
        };
        if (source.DeviceId != sourceEndpoint.DeviceId)
        {
            throw new ArgumentException(
                "A routed Scene operation source must match its local endpoint.",
                nameof(preparation));
        }


        DeviceId requestingCoordinator =
            preparation.RemoteCoordinatorDeviceId
            ?? coordinatorDeviceId
            ?? throw new ArgumentException(
                "A routed Scene operation requires an authenticated coordinator.",
                nameof(preparation));
        if (coordinatorDeviceId is not null
            && preparation.RemoteCoordinatorDeviceId is not null
            && coordinatorDeviceId != preparation.RemoteCoordinatorDeviceId)
        {
            throw new ArgumentException(
                "A routed Scene operation coordinator changed after composition.",
                nameof(preparation));
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset deadline = StableDeadline(preparation.AcceptedAt);
        OperationContext childContext = OperationContext.Create(
            item.ChildOperationId,
            item.ChildCorrelationId,
            deadline);
        if (deadline <= clock.UtcNow)
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.DeadlineExpired);
        }

        SceneSourceLookup currentSource;
        try
        {
            currentSource = await sourceEndpoint.LocateSourceAsync(
                requestingCoordinator,
                item.ActivityId,
                item.Index,
                childContext,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        if (currentSource.Status == SceneSourceLookupStatus.Unavailable)
        {
            return Failed(
                StatusFor(currentSource.Reason),
                FailureFor(currentSource.Reason));
        }

        if (currentSource.UniqueSource is null)
        {
            return Failed(
                OperationStatus.Rejected,
                currentSource.Status == SceneSourceLookupStatus.NotFound
                    ? FailureCode.ActivityNotFound
                    : FailureCode.RevisionConflict);
        }

        if (currentSource.UniqueSource != source)
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!sourceEndpoint.Node.TryGetActivity(
                item.ActivityId,
                out ActivityInstance? liveSource))
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.ActivityNotFound);
        }

        if (!Matches(liveSource, source))
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!sourceEndpoint.Allows(
                item.Destination.DeviceId,
                Capability.ActivityReceive))
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.CapabilityDenied);
        }

        if (!routes.TryGetSceneExactSlotChannel(
                item.Destination.DeviceId,
                out ISceneExactSlotChannel? slotChannel)
            || slotChannel is null
            || slotChannel.TargetDeviceId != item.Destination.DeviceId)
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        SceneActivityPlan exactPlan = SceneActivityPlan.Place(
            item.ActivityId,
            item.Destination,
            item.SourceDisposition,
            item.ConflictPolicy);
        SceneExactSlotQuery query = SceneExactSlotQuery.Create(
            childContext,
            exactPlan,
            source);
        SceneExactSlotDeliveryResult slotDelivery;
        try
        {
            slotDelivery = await slotChannel.InspectSlotAsync(
                sourceEndpoint.DeviceId,
                query,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        if (slotDelivery.Status != SceneControlDeliveryStatus.Acknowledged
            || slotDelivery.Result is null)
        {
            return Failed(
                slotDelivery.Status
                    == SceneControlDeliveryStatus.ProtocolUnsupported
                    ? OperationStatus.Rejected
                    : OperationStatus.Failed,
                slotDelivery.Status switch
                {
                    SceneControlDeliveryStatus.ProtocolUnsupported =>
                        FailureCode.ProtocolIncompatible,
                    SceneControlDeliveryStatus.NotDelivered
                        or SceneControlDeliveryStatus.AcknowledgementLost =>
                        FailureCode.PeerUnavailable,
                    _ => FailureCode.InternalFailure,
                });
        }

        SceneExactSlotInspection currentSlot = slotDelivery.Result;
        if (currentSlot.IsBlocked)
        {
            return Failed(
                StatusFor(currentSlot.Reason),
                FailureFor(currentSlot.Reason));
        }

        if (item.Action == SceneApplyAction.Replace)
        {
            return await ExecuteReplaceAsync(
                item,
                source,
                liveSource,
                currentSlot,
                preparation.AcceptedAt,
                childContext,
                Failed,
                cancellationToken).ConfigureAwait(false);
        }

        if (currentSlot.Occupancy?.Kind != SceneSlotOccupancyKind.Empty)
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!routes.TryGetChannel(
                item.Destination.DeviceId,
                out IActivityChannel? channel)
            || channel is null
            || channel.TargetDeviceId != item.Destination.DeviceId)
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        try
        {
            OperationReceipt receipt = item.Action switch
            {
                SceneApplyAction.Handoff =>
                    await sourceEndpoint.Node.HandoffAsync(
                        item.ActivityId,
                        channel,
                        item.Destination.Slot,
                        childContext,
                        liveSource,
                        cancellationToken).ConfigureAwait(false),
                SceneApplyAction.Move =>
                    await sourceEndpoint.Node.MoveAsync(
                        item.ActivityId,
                        channel,
                        item.Destination.Slot,
                        childContext,
                        liveSource,
                        cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException(
                    "The Scene transfer action changed during execution."),
            };
            return SceneActivityOperationResult.Create(receipt, null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                OperationStatus.Recovering,
                FailureCode.AcknowledgementLost);
        }

        SceneActivityOperationResult Failed(
            OperationStatus status,
            FailureCode failureCode) =>
            SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    operationKind,
                    status,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    item.ActivityId,
                    source.Kind,
                    source.DescriptorDigest,
                    clock.UtcNow,
                    failureCode),
                null);
    }

    public ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleReference capsule,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        DeviceId requestingCoordinator = coordinatorDeviceId
            ?? sourceEndpoint.DeviceId;
        if (capsule.TargetDeviceId != sourceEndpoint.DeviceId)
        {
            return ValueTask.FromResult(UndoReplaceResult.Failed(
                context,
                capsule.Id,
                FailureCode.PeerUnavailable,
                clock.UtcNow));
        }

        if (!sourceEndpoint.Allows(
                requestingCoordinator,
                Capability.SceneApply))
        {
            return ValueTask.FromResult(UndoReplaceResult.Rejected(
                context,
                capsule.Id,
                FailureCode.CapabilityDenied,
                clock.UtcNow));
        }

        return sourceEndpoint.UndoReplaceAsync(
            capsule,
            context,
            cancellationToken);
    }

    private async ValueTask<SceneActivityOperationResult> ExecuteReplaceAsync(
        SceneApplyItemPreview item,
        SceneSourceSelection source,
        ActivityInstance liveSource,
        SceneExactSlotInspection currentSlot,
        DateTimeOffset acceptedAt,
        OperationContext childContext,
        Func<OperationStatus, FailureCode, SceneActivityOperationResult> failed,
        CancellationToken cancellationToken)
    {
        SceneSlotOccupancy? occupancy = currentSlot.Occupancy;
        SceneReplaceTargetSnapshot? currentTarget = occupancy?.Target;
        SceneReplaceTargetSnapshot? expectedTarget = item.ReplaceTarget;
        if (occupancy?.Kind != SceneSlotOccupancyKind.EligibleConflict
            || currentTarget is null
            || expectedTarget is null
            || currentTarget != expectedTarget)
        {
            return failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!occupancy.HasDurableUndoAvailability)
        {
            return failed(OperationStatus.Rejected, FailureCode.UndoUnavailable);
        }

        if (!routes.TryGetReplaceChannel(
                item.Destination.DeviceId,
                out IReplaceChannel? channel)
            || channel is null
            || channel.TargetDeviceId != item.Destination.DeviceId)
        {
            return failed(OperationStatus.Failed, FailureCode.PeerUnavailable);
        }

        DateTimeOffset undoExpiresAt = acceptedAt.ToUniversalTime()
            + ReplaceEndpoint.MaximumUndoRetention;
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            childContext,
            currentTarget.ActivityId,
            currentTarget.Revision,
            currentTarget.DescriptorDigest,
            liveSource.Descriptor,
            item.Destination,
            undoExpiresAt);
        ReplaceDeliveryResult delivery;
        try
        {
            delivery = await channel.SendAsync(
                source.DeviceId,
                command,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return failed(
                OperationStatus.Recovering,
                FailureCode.AcknowledgementLost);
        }

        if (delivery.Status != ActivityDeliveryStatus.Acknowledged
            || delivery.Result is null)
        {
            return failed(
                delivery.Status == ActivityDeliveryStatus.AcknowledgementLost
                    ? OperationStatus.Recovering
                    : OperationStatus.Failed,
                delivery.Status == ActivityDeliveryStatus.AcknowledgementLost
                    ? FailureCode.AcknowledgementLost
                    : FailureCode.PeerUnavailable);
        }

        return SceneActivityOperationResult.Create(
            delivery.Result.Receipt,
            delivery.Result.Receipt.IsSuccess
                ? delivery.Result.UndoCapsule
                : null);
    }

    private static DateTimeOffset StableDeadline(DateTimeOffset acceptedAt)
    {
        DateTimeOffset canonical = acceptedAt.ToUniversalTime();
        if (canonical
            > DateTimeOffset.MaxValue
                - DirectSceneActivityOperationPort.MaximumChildLifetime)
        {
            throw new InvalidOperationException(
                "The Scene acceptance time cannot represent a child deadline.");
        }

        return canonical + DirectSceneActivityOperationPort.MaximumChildLifetime;
    }

    private static bool Matches(
        ActivityInstance current,
        SceneSourceSelection expected) =>
        current.Descriptor.Id == expected.ActivityId
        && current.Revision == expected.Revision
        && string.Equals(
            current.Descriptor.DescriptorDigest,
            expected.DescriptorDigest,
            StringComparison.Ordinal)
        && current.Descriptor.Kind == expected.Kind
        && current.Placement == expected.Placement
        && current.Lifecycle == ActivityLifecycle.Active
        && current.Descriptor.Sensitivity == ActivitySensitivity.Normal;

    private static OperationStatus StatusFor(SceneApplyItemReason reason) =>
        reason switch
        {
            SceneApplyItemReason.CapabilityDenied
                or SceneApplyItemReason.ProtocolUnsupported =>
                OperationStatus.Rejected,
            SceneApplyItemReason.SourceLookupUnavailable
                or SceneApplyItemReason.DestinationUnavailable =>
                OperationStatus.Failed,
            _ => OperationStatus.Rejected,
        };

    private static FailureCode FailureFor(SceneApplyItemReason reason) =>
        reason switch
        {
            SceneApplyItemReason.CapabilityDenied =>
                FailureCode.CapabilityDenied,
            SceneApplyItemReason.ProtocolUnsupported =>
                FailureCode.ProtocolIncompatible,
            SceneApplyItemReason.SourceLookupUnavailable
                or SceneApplyItemReason.DestinationUnavailable =>
                FailureCode.PeerUnavailable,
            _ => FailureCode.InternalFailure,
        };
}

public sealed class DirectSceneActivityOperationPort :
    ISceneActivityOperationPort
{
    public static readonly TimeSpan MaximumChildLifetime =
        TimeSpan.FromMinutes(5);

    private readonly IClock clock;
    private readonly DeviceId coordinatorDeviceId;
    private readonly ImmutableDictionary<DeviceId, SceneActivityOperationEndpoint>
        endpoints;

    public DirectSceneActivityOperationPort(
        DeviceId coordinatorDeviceId,
        IEnumerable<SceneActivityOperationEndpoint> endpoints)
        : this(SystemClock.Instance, coordinatorDeviceId, endpoints)
    {
    }

    public DirectSceneActivityOperationPort(
        IClock clock,
        DeviceId coordinatorDeviceId,
        IEnumerable<SceneActivityOperationEndpoint> endpoints)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.coordinatorDeviceId = coordinatorDeviceId
            ?? throw new ArgumentNullException(nameof(coordinatorDeviceId));
        ArgumentNullException.ThrowIfNull(endpoints);
        SceneActivityOperationEndpoint[] bounded = endpoints.ToArray();
        if (bounded.Length is < 1 or > ScenePlan.MaximumActivities
            || bounded.Any(static endpoint => endpoint is null))
        {
            throw new ArgumentException(
                "A direct Scene operation port requires 1 through 64 device endpoints.",
                nameof(endpoints));
        }

        this.endpoints = bounded.ToImmutableDictionary(
            static endpoint => endpoint.DeviceId);
    }

    public async ValueTask<SceneActivityOperationResult> ExecuteAsync(
        SceneActivityPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        SceneApplyItemPreview item = preparation.Item;
        SceneSourceSelection source = item.Source
            ?? throw new ArgumentException(
                "An executable Scene item requires an exact source.",
                nameof(preparation));
        OperationKind operationKind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentException(
                "A direct Scene operation requires an executable item.",
                nameof(preparation)),
        };
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset deadline = StableDeadline(preparation.AcceptedAt);
        OperationContext childContext = OperationContext.Create(
            item.ChildOperationId,
            item.ChildCorrelationId,
            deadline);
        if (deadline <= clock.UtcNow)
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.DeadlineExpired);
        }

        if (!endpoints.TryGetValue(
                source.DeviceId,
                out SceneActivityOperationEndpoint? sourceEndpoint)
            || !endpoints.TryGetValue(
                item.Destination.DeviceId,
                out SceneActivityOperationEndpoint? targetEndpoint))
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        SceneSourceLookup currentSource;
        try
        {
            currentSource = await sourceEndpoint.LocateSourceAsync(
                coordinatorDeviceId,
                item.ActivityId,
                item.Index,
                childContext,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        if (currentSource.Status == SceneSourceLookupStatus.Unavailable)
        {
            return Failed(
                StatusFor(currentSource.Reason),
                FailureFor(currentSource.Reason));
        }

        if (currentSource.UniqueSource is null)
        {
            return Failed(
                OperationStatus.Rejected,
                currentSource.Status == SceneSourceLookupStatus.NotFound
                    ? FailureCode.ActivityNotFound
                    : FailureCode.RevisionConflict);
        }

        if (currentSource.UniqueSource != source)
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!sourceEndpoint.Node.TryGetActivity(
                item.ActivityId,
                out ActivityInstance? liveSource))
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.ActivityNotFound);
        }

        if (!Matches(liveSource, source))
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!sourceEndpoint.Allows(
                item.Destination.DeviceId,
                Capability.ActivityReceive))
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.CapabilityDenied);
        }

        SceneActivityPlan exactPlan = SceneActivityPlan.Place(
            item.ActivityId,
            item.Destination,
            item.SourceDisposition,
            item.ConflictPolicy);
        SceneExactSlotInspection currentSlot;
        try
        {
            currentSlot = await targetEndpoint.InspectExactSlotAsync(
                coordinatorDeviceId,
                exactPlan,
                source,
                childContext,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        if (currentSlot.IsBlocked)
        {
            return Failed(
                StatusFor(currentSlot.Reason),
                FailureFor(currentSlot.Reason));
        }

        if (item.Action == SceneApplyAction.Replace)
        {
            return await ExecuteReplaceAsync(
                targetEndpoint,
                item,
                source,
                liveSource,
                currentSlot,
                preparation.AcceptedAt,
                childContext,
                Failed,
                cancellationToken).ConfigureAwait(false);
        }

        if (currentSlot.Occupancy?.Kind != SceneSlotOccupancyKind.Empty)
        {
            return Failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        var channel = new DirectActivityChannel(targetEndpoint.Node);
        OperationReceipt receipt = item.Action switch
        {
            SceneApplyAction.Handoff =>
                await sourceEndpoint.Node.HandoffAsync(
                    item.ActivityId,
                    channel,
                    item.Destination.Slot,
                    childContext,
                    liveSource,
                    cancellationToken).ConfigureAwait(false),
            SceneApplyAction.Move =>
                await sourceEndpoint.Node.MoveAsync(
                    item.ActivityId,
                    channel,
                    item.Destination.Slot,
                    childContext,
                    liveSource,
                    cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "The Scene transfer action changed during execution."),
        };
        return SceneActivityOperationResult.Create(receipt, null);

        SceneActivityOperationResult Failed(
            OperationStatus status,
            FailureCode failureCode) =>
            SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    operationKind,
                    status,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    item.ActivityId,
                    source.Kind,
                    source.DescriptorDigest,
                    clock.UtcNow,
                    failureCode),
                null);
    }

    public ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleReference capsule,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!endpoints.TryGetValue(
                capsule.TargetDeviceId,
                out SceneActivityOperationEndpoint? targetEndpoint))
        {
            return ValueTask.FromResult(UndoReplaceResult.Failed(
                context,
                capsule.Id,
                FailureCode.PeerUnavailable,
                clock.UtcNow));
        }

        if (!targetEndpoint.Allows(
                coordinatorDeviceId,
                Capability.SceneApply))
        {
            return ValueTask.FromResult(UndoReplaceResult.Rejected(
                context,
                capsule.Id,
                FailureCode.CapabilityDenied,
                clock.UtcNow));
        }

        return targetEndpoint.UndoReplaceAsync(
            capsule,
            context,
            cancellationToken);
    }

    private static async ValueTask<SceneActivityOperationResult> ExecuteReplaceAsync(
        SceneActivityOperationEndpoint targetEndpoint,
        SceneApplyItemPreview item,
        SceneSourceSelection source,
        ActivityInstance liveSource,
        SceneExactSlotInspection currentSlot,
        DateTimeOffset acceptedAt,
        OperationContext childContext,
        Func<OperationStatus, FailureCode, SceneActivityOperationResult> failed,
        CancellationToken cancellationToken)
    {
        ReplaceEndpoint? replaceEndpoint = targetEndpoint.ReplaceEndpoint;
        if (replaceEndpoint is null)
        {
            return failed(OperationStatus.Failed, FailureCode.PeerUnavailable);
        }

        SceneSlotOccupancy? occupancy = currentSlot.Occupancy;
        SceneReplaceTargetSnapshot? currentTarget = occupancy?.Target;
        SceneReplaceTargetSnapshot? expectedTarget = item.ReplaceTarget;
        if (occupancy?.Kind != SceneSlotOccupancyKind.EligibleConflict
            || currentTarget is null
            || expectedTarget is null
            || currentTarget != expectedTarget)
        {
            return failed(
                OperationStatus.Rejected,
                FailureCode.RevisionConflict);
        }

        if (!occupancy.HasDurableUndoAvailability)
        {
            return failed(OperationStatus.Rejected, FailureCode.UndoUnavailable);
        }

        DateTimeOffset undoExpiresAt =
            acceptedAt.ToUniversalTime() + ReplaceEndpoint.MaximumUndoRetention;
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            childContext,
            currentTarget.ActivityId,
            currentTarget.Revision,
            currentTarget.DescriptorDigest,
            liveSource.Descriptor,
            item.Destination,
            undoExpiresAt);

        var channel = new DirectReplaceChannel(replaceEndpoint);
        ReplaceDeliveryResult delivery;
        try
        {
            delivery = await channel.SendAsync(
                source.DeviceId,
                command,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return failed(OperationStatus.Failed, FailureCode.PeerUnavailable);
        }

        if (delivery.Status != ActivityDeliveryStatus.Acknowledged
            || delivery.Result is null)
        {
            return failed(
                OperationStatus.Failed,
                delivery.Status == ActivityDeliveryStatus.AcknowledgementLost
                    ? FailureCode.AcknowledgementLost
                    : FailureCode.PeerUnavailable);
        }

        return SceneActivityOperationResult.Create(
            delivery.Result.Receipt,
            delivery.Result.Receipt.IsSuccess
                ? delivery.Result.UndoCapsule
                : null);
    }

    private static DateTimeOffset StableDeadline(DateTimeOffset acceptedAt)
    {
        DateTimeOffset canonical = acceptedAt.ToUniversalTime();
        if (canonical > DateTimeOffset.MaxValue - MaximumChildLifetime)
        {
            throw new InvalidOperationException(
                "The Scene acceptance time cannot represent a child deadline.");
        }

        return canonical + MaximumChildLifetime;
    }

    private static bool Matches(
        ActivityInstance current,
        SceneSourceSelection expected) =>
        current.Descriptor.Id == expected.ActivityId
        && current.Revision == expected.Revision
        && string.Equals(
            current.Descriptor.DescriptorDigest,
            expected.DescriptorDigest,
            StringComparison.Ordinal)
        && current.Descriptor.Kind == expected.Kind
        && current.Placement == expected.Placement
        && current.Lifecycle == ActivityLifecycle.Active
        && current.Descriptor.Sensitivity == ActivitySensitivity.Normal;

    private static OperationStatus StatusFor(SceneApplyItemReason reason) =>
        reason switch
        {
            SceneApplyItemReason.CapabilityDenied
                or SceneApplyItemReason.ProtocolUnsupported =>
                OperationStatus.Rejected,
            SceneApplyItemReason.SourceLookupUnavailable
                or SceneApplyItemReason.DestinationUnavailable =>
                OperationStatus.Failed,
            _ => OperationStatus.Rejected,
        };

    private static FailureCode FailureFor(SceneApplyItemReason reason) =>
        reason switch
        {
            SceneApplyItemReason.CapabilityDenied =>
                FailureCode.CapabilityDenied,
            SceneApplyItemReason.ProtocolUnsupported =>
                FailureCode.ProtocolIncompatible,
            SceneApplyItemReason.SourceLookupUnavailable
                or SceneApplyItemReason.DestinationUnavailable =>
                FailureCode.PeerUnavailable,
            _ => FailureCode.InternalFailure,
        };
}
