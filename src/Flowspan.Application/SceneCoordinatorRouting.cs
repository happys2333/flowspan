using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class RoutedSceneApplyPreflightPort : ISceneApplyPreflightPort
{
    private readonly DeviceId coordinatorDeviceId;
    private readonly ISceneApplyPreflightPeer localPeer;
    private readonly ISceneOperationRouteDirectory routes;

    public RoutedSceneApplyPreflightPort(
        DeviceId coordinatorDeviceId,
        ISceneApplyPreflightPeer localPeer,
        ISceneOperationRouteDirectory routes)
    {
        this.coordinatorDeviceId = coordinatorDeviceId
            ?? throw new ArgumentNullException(nameof(coordinatorDeviceId));
        this.localPeer = localPeer
            ?? throw new ArgumentNullException(nameof(localPeer));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        if (localPeer.DeviceId != coordinatorDeviceId)
        {
            throw new ArgumentException(
                "A routed Scene preflight local peer must belong to the coordinator.",
                nameof(localPeer));
        }
    }

    public async ValueTask<SceneSourceLookup> LocateSourcesAsync(
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(childContext);
        IReadOnlyList<DeviceId> remoteDeviceIds =
            routes.GetSceneParticipantDeviceIds();
        if (remoteDeviceIds.Count >= ScenePlan.MaximumActivities
            || remoteDeviceIds.Any(static deviceId => deviceId is null)
            || remoteDeviceIds.Distinct().Count() != remoteDeviceIds.Count
            || remoteDeviceIds.Contains(coordinatorDeviceId))
        {
            return Unavailable(
                activityId,
                index,
                SceneApplyItemReason.SourceLookupUnavailable);
        }

        var candidates = ImmutableArray.CreateBuilder<SceneSourceSelection>();
        SceneSourceLookup local;
        try
        {
            local = await localPeer.LocateSourceAsync(
                coordinatorDeviceId,
                activityId,
                index,
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
            return Unavailable(
                activityId,
                index,
                SceneApplyItemReason.SourceLookupUnavailable);
        }

        if (!TryAppendLookup(
                local,
                coordinatorDeviceId,
                activityId,
                index,
                candidates,
                out SceneApplyItemReason localReason))
        {
            return Unavailable(activityId, index, localReason);
        }

        foreach (DeviceId remoteDeviceId in remoteDeviceIds.OrderBy(
                     static deviceId => deviceId.Value))
        {
            if (!routes.TryGetSceneSourceLookupChannel(
                    remoteDeviceId,
                    out ISceneSourceLookupChannel? channel)
                || channel is null
                || channel.TargetDeviceId != remoteDeviceId)
            {
                return Unavailable(
                    activityId,
                    index,
                    SceneApplyItemReason.SourceLookupUnavailable);
            }

            SceneSourceLookupQuery query = SceneSourceLookupQuery.Create(
                childContext,
                remoteDeviceId,
                activityId,
                index);
            SceneSourceLookupDeliveryResult delivery;
            try
            {
                delivery = await channel.QuerySourceAsync(
                    coordinatorDeviceId,
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
                return Unavailable(
                    activityId,
                    index,
                    SceneApplyItemReason.SourceLookupUnavailable);
            }

            if (delivery.Status != SceneControlDeliveryStatus.Acknowledged
                || delivery.Result is null)
            {
                return Unavailable(
                    activityId,
                    index,
                    delivery.Status
                        == SceneControlDeliveryStatus.ProtocolUnsupported
                        ? SceneApplyItemReason.ProtocolUnsupported
                        : SceneApplyItemReason.SourceLookupUnavailable);
            }

            if (!TryAppendLookup(
                    delivery.Result,
                    remoteDeviceId,
                    activityId,
                    index,
                    candidates,
                    out SceneApplyItemReason remoteReason))
            {
                return Unavailable(activityId, index, remoteReason);
            }
        }

        return SceneSourceLookup.FromObservation(
            index,
            activityId,
            candidates.ToImmutable(),
            isComplete: true);
    }

    public async ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        SceneActivityPlan item,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(childContext);
        if (item.Placement.DeviceId == coordinatorDeviceId)
        {
            return await localPeer.InspectExactSlotAsync(
                coordinatorDeviceId,
                item,
                source,
                childContext,
                cancellationToken).ConfigureAwait(false);
        }

        if (!routes.TryGetSceneExactSlotChannel(
                item.Placement.DeviceId,
                out ISceneExactSlotChannel? channel)
            || channel is null
            || channel.TargetDeviceId != item.Placement.DeviceId)
        {
            return SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable);
        }

        SceneExactSlotDeliveryResult delivery;
        try
        {
            delivery = await channel.InspectSlotAsync(
                coordinatorDeviceId,
                SceneExactSlotQuery.Create(childContext, item, source),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable);
        }

        if (delivery.Status == SceneControlDeliveryStatus.ProtocolUnsupported)
        {
            return SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.ProtocolUnsupported);
        }

        return delivery.Status == SceneControlDeliveryStatus.Acknowledged
            && delivery.Result is not null
            ? delivery.Result
            : SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable);
    }

    private static bool TryAppendLookup(
        SceneSourceLookup lookup,
        DeviceId expectedDeviceId,
        ActivityId activityId,
        int index,
        ImmutableArray<SceneSourceSelection>.Builder candidates,
        out SceneApplyItemReason reason)
    {
        if (lookup.Status == SceneSourceLookupStatus.Unavailable)
        {
            reason = lookup.Reason;
            return false;
        }

        if (lookup.Index != index
            || lookup.ActivityId != activityId
            || lookup.Status == SceneSourceLookupStatus.SelectionRequired
            || lookup.Candidates.Length > 1
            || lookup.Candidates.Any(candidate =>
                candidate.DeviceId != expectedDeviceId))
        {
            reason = SceneApplyItemReason.SourceLookupUnavailable;
            return false;
        }

        if (lookup.UniqueSource is not null)
        {
            candidates.Add(lookup.UniqueSource);
        }

        reason = SceneApplyItemReason.None;
        return true;
    }

    private static SceneSourceLookup Unavailable(
        ActivityId activityId,
        int index,
        SceneApplyItemReason reason) =>
        SceneSourceLookup.Unavailable(index, activityId, reason);
}

public sealed class CoordinatorSceneActivityOperationPort :
    ISceneActivityOperationPort
{
    private readonly IClock clock;
    private readonly DeviceId coordinatorDeviceId;
    private readonly ISceneActivityOperationPort localPort;
    private readonly ISceneOperationRouteDirectory routes;

    public CoordinatorSceneActivityOperationPort(
        IClock clock,
        DeviceId coordinatorDeviceId,
        ISceneActivityOperationPort localPort,
        ISceneOperationRouteDirectory routes)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.coordinatorDeviceId = coordinatorDeviceId
            ?? throw new ArgumentNullException(nameof(coordinatorDeviceId));
        this.localPort = localPort
            ?? throw new ArgumentNullException(nameof(localPort));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async ValueTask<SceneActivityOperationResult> ExecuteAsync(
        SceneActivityPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.RemoteCoordinatorDeviceId is not null)
        {
            throw new ArgumentException(
                "A Scene coordinator operation cannot execute a forwarded preparation.",
                nameof(preparation));
        }

        SceneApplyItemPreview item = preparation.Item;
        SceneSourceSelection source = item.Source
            ?? throw new ArgumentException(
                "An executable Scene item requires an exact source.",
                nameof(preparation));
        if (source.DeviceId == coordinatorDeviceId)
        {
            return await localPort.ExecuteAsync(
                preparation,
                cancellationToken).ConfigureAwait(false);
        }

        if (!routes.TryGetSceneChildOperationChannel(
                source.DeviceId,
                out ISceneChildOperationChannel? channel)
            || channel is null
            || channel.TargetDeviceId != source.DeviceId)
        {
            return Failed(
                item,
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        SceneRemoteChildInstruction instruction =
            SceneRemoteChildInstruction.Create(
                coordinatorDeviceId,
                preparation.SceneId,
                preparation.SceneRevision,
                preparation.SceneDigest,
                preparation.PreviewFingerprint,
                preparation.ParentOperationId,
                preparation.ParentCorrelationId,
                preparation.AcceptedAt,
                item);
        SceneChildDeliveryResult delivery;
        try
        {
            delivery = await channel.ExecuteChildAsync(
                coordinatorDeviceId,
                instruction,
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
                item,
                OperationStatus.Recovering,
                FailureCode.AcknowledgementLost);
        }

        if (delivery.Status == SceneControlDeliveryStatus.ProtocolUnsupported)
        {
            return Failed(
                item,
                OperationStatus.Rejected,
                FailureCode.ProtocolIncompatible);
        }

        if (delivery.Status == SceneControlDeliveryStatus.NotDelivered)
        {
            return Failed(
                item,
                OperationStatus.Failed,
                FailureCode.PeerUnavailable);
        }

        if (delivery.Status == SceneControlDeliveryStatus.AcknowledgementLost)
        {
            return Failed(
                item,
                OperationStatus.Recovering,
                FailureCode.AcknowledgementLost);
        }

        SceneActivityOperationResult? result = delivery.Result;
        return result is not null && IsBoundResult(item, result)
            ? result
            : Failed(
                item,
                OperationStatus.Recovering,
                FailureCode.InternalFailure);
    }

    public async ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleReference capsule,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        ArgumentNullException.ThrowIfNull(context);
        if (capsule.TargetDeviceId == coordinatorDeviceId)
        {
            return await localPort.UndoReplaceAsync(
                capsule,
                context,
                cancellationToken).ConfigureAwait(false);
        }

        if (!routes.TryGetSceneChildOperationChannel(
                capsule.TargetDeviceId,
                out ISceneChildOperationChannel? channel)
            || channel is null
            || channel.TargetDeviceId != capsule.TargetDeviceId)
        {
            return UndoReplaceResult.Failed(
                context,
                capsule.Id,
                FailureCode.PeerUnavailable,
                clock.UtcNow);
        }

        SceneUndoReplaceInstruction instruction;
        try
        {
            instruction = SceneUndoReplaceInstruction.Create(
                coordinatorDeviceId,
                capsule,
                context);
        }
        catch (ArgumentException)
        {
            return UndoReplaceResult.Rejected(
                context,
                capsule.Id,
                FailureCode.UndoCapsuleInvalid,
                clock.UtcNow);
        }

        SceneUndoReplaceDeliveryResult delivery;
        try
        {
            delivery = await channel.UndoReplaceAsync(
                coordinatorDeviceId,
                instruction,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return UndoReplaceResult.Recovering(
                context,
                capsule.Id,
                FailureCode.AcknowledgementLost,
                clock.UtcNow);
        }

        if (delivery.Status == SceneControlDeliveryStatus.ProtocolUnsupported)
        {
            return UndoReplaceResult.Rejected(
                context,
                capsule.Id,
                FailureCode.ProtocolIncompatible,
                clock.UtcNow);
        }

        if (delivery.Status == SceneControlDeliveryStatus.NotDelivered)
        {
            return UndoReplaceResult.Failed(
                context,
                capsule.Id,
                FailureCode.PeerUnavailable,
                clock.UtcNow);
        }

        if (delivery.Status == SceneControlDeliveryStatus.AcknowledgementLost)
        {
            return UndoReplaceResult.Recovering(
                context,
                capsule.Id,
                FailureCode.AcknowledgementLost,
                clock.UtcNow);
        }

        UndoReplaceResult? result = delivery.Result;
        return result is not null
            && result.OperationId == context.OperationId
            && result.CorrelationId == context.CorrelationId
            && result.CapsuleId == capsule.Id
                ? result
                : UndoReplaceResult.Recovering(
                    context,
                    capsule.Id,
                    FailureCode.InternalFailure,
                    clock.UtcNow);
    }

    private SceneActivityOperationResult Failed(
        SceneApplyItemPreview item,
        OperationStatus status,
        FailureCode failureCode)
    {
        SceneSourceSelection source = item.Source!;
        OperationKind operationKind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        return SceneActivityOperationResult.Create(
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
            undoCapsule: null);
    }

    private static bool IsBoundResult(
        SceneApplyItemPreview item,
        SceneActivityOperationResult result)
    {
        SceneSourceSelection source = item.Source!;
        OperationReceipt receipt = result.Receipt;
        OperationKind operationKind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        if (receipt.OperationId != item.ChildOperationId
            || receipt.CorrelationId != item.ChildCorrelationId
            || receipt.Kind != operationKind
            || receipt.SourceDeviceId != source.DeviceId
            || receipt.TargetDeviceId != item.Destination.DeviceId
            || receipt.ActivityId != item.ActivityId
            || (receipt.ActivityKind is not null
                && receipt.ActivityKind != source.Kind)
            || (receipt.DescriptorDigest is not null
                && receipt.DescriptorDigest != source.DescriptorDigest))
        {
            return false;
        }

        UndoCapsuleReference? undo = result.UndoCapsule;
        if (item.Action != SceneApplyAction.Replace || !receipt.IsSuccess)
        {
            return undo is null;
        }

        SceneReplaceTargetSnapshot? target = item.ReplaceTarget;
        return target is not null
            && undo is not null
            && undo.OperationId == item.ChildOperationId
            && undo.CorrelationId == item.ChildCorrelationId
            && undo.TargetDeviceId == item.Destination.DeviceId
            && undo.TargetActivityId == target.ActivityId
            && undo.TargetDescriptorDigest == target.DescriptorDigest
            && undo.IncomingActivityId == item.ActivityId
            && undo.IncomingDescriptorDigest == source.DescriptorDigest;
    }
}
