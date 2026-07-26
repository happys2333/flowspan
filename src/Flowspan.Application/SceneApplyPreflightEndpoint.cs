using System.Collections.Concurrent;
using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface ISceneApplyPreflightPeer
{
    public DeviceId DeviceId { get; }

    public ValueTask<SceneSourceLookup> LocateSourceAsync(
        DeviceId requestingDeviceId,
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken);

    public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        DeviceId requestingDeviceId,
        SceneActivityPlan item,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken);
}

public interface ISceneReplaceUndoAvailability
{
    public bool HasDurableUndoFor(ActivityInstance target);
}

public sealed class SceneApplyPreflightEndpoint : ISceneApplyPreflightPeer
{
    private readonly ActivityAdapterRegistry adapterRegistry;
    private readonly IClock clock;
    private readonly ConcurrentDictionary<DeviceId, CapabilityGrant> peerGrants =
        new();
    private readonly ISceneReplaceUndoAvailability replaceUndoAvailability;
    private readonly IActivitySnapshotSource snapshotSource;

    public SceneApplyPreflightEndpoint(
        DeviceId deviceId,
        IClock clock,
        IActivitySnapshotSource snapshotSource,
        ActivityAdapterRegistry adapterRegistry,
        ISceneReplaceUndoAvailability replaceUndoAvailability)
    {
        DeviceId = deviceId
            ?? throw new ArgumentNullException(nameof(deviceId));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.snapshotSource = snapshotSource
            ?? throw new ArgumentNullException(nameof(snapshotSource));
        this.adapterRegistry = adapterRegistry
            ?? throw new ArgumentNullException(nameof(adapterRegistry));
        this.replaceUndoAvailability = replaceUndoAvailability
            ?? throw new ArgumentNullException(
                nameof(replaceUndoAvailability));
    }

    public DeviceId DeviceId { get; }

    public void SetPeerGrant(DeviceId peerDeviceId, CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(grant);
        peerGrants[peerDeviceId] = grant;
    }

    public ValueTask<SceneSourceLookup> LocateSourceAsync(
        DeviceId requestingDeviceId,
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(childContext);
        cancellationToken.ThrowIfCancellationRequested();
        if (childContext.Deadline <= clock.UtcNow)
        {
            return ValueTask.FromResult(SceneSourceLookup.Unavailable(
                index,
                activityId,
                SceneApplyItemReason.SourceLookupUnavailable));
        }

        if (!AllowsSceneApply(requestingDeviceId))
        {
            return ValueTask.FromResult(SceneSourceLookup.Unavailable(
                index,
                activityId,
                SceneApplyItemReason.CapabilityDenied));
        }

        SceneSourceSelection[] sources = snapshotSource.GetSnapshot()
            .Where(activity =>
                activity.Descriptor.Id == activityId
                && activity.Placement.DeviceId == DeviceId
                && activity.Lifecycle == ActivityLifecycle.Active
                && activity.Descriptor.Sensitivity
                    == ActivitySensitivity.Normal)
            .Select(activity => SceneSourceSelection.Create(
                index,
                activity.Descriptor.Id,
                activity.Revision,
                activity.Descriptor.DescriptorDigest,
                activity.Descriptor.Kind,
                activity.Placement))
            .ToArray();
        return ValueTask.FromResult(SceneSourceLookup.FromObservation(
            index,
            activityId,
            sources,
            isComplete: true));
    }

    public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        DeviceId requestingDeviceId,
        SceneActivityPlan item,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(childContext);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Placement.DeviceId != DeviceId)
        {
            throw new ArgumentException(
                "An exact-slot query targets another device.",
                nameof(item));
        }

        if (source.ActivityId != item.ActivityId
            || source.Placement == item.Placement)
        {
            throw new ArgumentException(
                "An exact-slot query requires the selected remote source.",
                nameof(source));
        }

        if (childContext.Deadline <= clock.UtcNow)
        {
            return ValueTask.FromResult(SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable));
        }

        if (!AllowsSceneApply(requestingDeviceId))
        {
            return ValueTask.FromResult(SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.CapabilityDenied));
        }

        ActivityInstance? target = null;
        foreach (ActivityInstance activity in snapshotSource.GetSnapshot())
        {
            if (activity.Placement != item.Placement
                || activity.Lifecycle != ActivityLifecycle.Active)
            {
                continue;
            }

            if (target is not null)
            {
                return ValueTask.FromResult(SceneExactSlotInspection.Observed(
                    SceneSlotOccupancy.Ambiguous));
            }

            target = activity;
        }

        if (target is null)
        {
            return ValueTask.FromResult(SceneExactSlotInspection.Observed(
                SceneSlotOccupancy.Empty));
        }

        if (target.Descriptor.Id == source.ActivityId
            || target.Descriptor.Sensitivity != ActivitySensitivity.Normal
            || target.Descriptor.Kind != source.Kind
            || !adapterRegistry.TryFind(
                target.Descriptor.Kind,
                out IActivityAdapter? adapter)
            || adapter is not IReplaceActivityAdapter)
        {
            return ValueTask.FromResult(SceneExactSlotInspection.Observed(
                SceneSlotOccupancy.Opaque));
        }

        bool hasDurableUndo;
        try
        {
            hasDurableUndo =
                replaceUndoAvailability.HasDurableUndoFor(target);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(SceneExactSlotInspection.Observed(
                SceneSlotOccupancy.Opaque));
        }

        SceneReplaceTargetSnapshot snapshot =
            SceneReplaceTargetSnapshot.Create(
                target.Descriptor.Id,
                target.Revision,
                target.Descriptor.DescriptorDigest,
                target.Descriptor.Kind,
                target.Placement);
        return ValueTask.FromResult(SceneExactSlotInspection.Observed(
            SceneSlotOccupancy.EligibleConflict(
                snapshot,
                hasDurableUndo)));
    }

    private bool AllowsSceneApply(DeviceId requestingDeviceId) =>
        requestingDeviceId == DeviceId
        || (peerGrants.TryGetValue(
                requestingDeviceId,
                out CapabilityGrant? grant)
            && grant.Allows(Capability.SceneApply));
}

public sealed class DirectSceneApplyPreflightPort :
    ISceneApplyPreflightPort
{
    public const int MaximumParticipants = ScenePlan.MaximumActivities;

    private readonly DeviceId coordinatorDeviceId;
    private readonly ImmutableDictionary<DeviceId, ISceneApplyPreflightPeer>
        peers;

    public DirectSceneApplyPreflightPort(
        DeviceId coordinatorDeviceId,
        IEnumerable<ISceneApplyPreflightPeer> peers)
    {
        this.coordinatorDeviceId = coordinatorDeviceId
            ?? throw new ArgumentNullException(nameof(coordinatorDeviceId));
        ArgumentNullException.ThrowIfNull(peers);
        ISceneApplyPreflightPeer[] bounded = peers.ToArray();
        if (bounded.Length is < 1 or > MaximumParticipants
            || bounded.Any(static peer => peer is null))
        {
            throw new ArgumentException(
                $"A direct Scene preflight requires 1 to {MaximumParticipants} peers.",
                nameof(peers));
        }

        this.peers = bounded.ToImmutableDictionary(
            static peer => peer.DeviceId);
    }

    public async ValueTask<SceneSourceLookup> LocateSourcesAsync(
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(childContext);
        var candidates = ImmutableArray.CreateBuilder<SceneSourceSelection>();
        foreach (ISceneApplyPreflightPeer peer in peers.Values.OrderBy(
                     static peer => peer.DeviceId.Value))
        {
            SceneSourceLookup local;
            try
            {
                local = await peer.LocateSourceAsync(
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
                return SceneSourceLookup.Unavailable(
                    index,
                    activityId,
                    SceneApplyItemReason.SourceLookupUnavailable);
            }

            if (local.Status == SceneSourceLookupStatus.Unavailable)
            {
                return SceneSourceLookup.Unavailable(
                    index,
                    activityId,
                    local.Reason);
            }

            if (local.Index != index
                || local.ActivityId != activityId
                || local.Status == SceneSourceLookupStatus.SelectionRequired
                || local.Candidates.Length > 1
                || local.Candidates.Any(candidate =>
                    candidate.DeviceId != peer.DeviceId))
            {
                return SceneSourceLookup.Unavailable(
                    index,
                    activityId,
                    SceneApplyItemReason.SourceLookupUnavailable);
            }

            if (local.UniqueSource is not null)
            {
                if (candidates.Count == ScenePlan.MaximumActivities)
                {
                    return SceneSourceLookup.Unavailable(
                        index,
                        activityId,
                        SceneApplyItemReason.SourceLookupUnavailable);
                }

                candidates.Add(local.UniqueSource);
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
        if (!peers.TryGetValue(
                item.Placement.DeviceId,
                out ISceneApplyPreflightPeer? target))
        {
            return SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable);
        }

        try
        {
            return await target.InspectExactSlotAsync(
                coordinatorDeviceId,
                item,
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
            return SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable);
        }
    }
}
