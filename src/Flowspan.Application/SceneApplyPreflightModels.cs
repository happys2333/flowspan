using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SceneSourceLookupQuery
{
    private SceneSourceLookupQuery(
        OperationContext context,
        DeviceId targetDeviceId,
        ActivityId activityId,
        int index)
    {
        Context = context;
        TargetDeviceId = targetDeviceId;
        ActivityId = activityId;
        Index = index;
    }

    public OperationContext Context { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityId ActivityId { get; }

    public int Index { get; }

    public static SceneSourceLookupQuery Create(
        OperationContext context,
        DeviceId targetDeviceId,
        ActivityId activityId,
        int index)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            ScenePlan.MaximumActivities);
        return new SceneSourceLookupQuery(
            context,
            targetDeviceId,
            activityId,
            index);
    }
}

public sealed record SceneExactSlotQuery
{
    private SceneExactSlotQuery(
        OperationContext context,
        SceneActivityPlan item,
        SceneSourceSelection source)
    {
        Context = context;
        Item = item;
        Source = source;
    }

    public OperationContext Context { get; }

    public SceneActivityPlan Item { get; }

    public SceneSourceSelection Source { get; }

    public DeviceId TargetDeviceId => Item.Placement.DeviceId;

    public static SceneExactSlotQuery Create(
        OperationContext context,
        SceneActivityPlan item,
        SceneSourceSelection source)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        if (source.ActivityId != item.ActivityId)
        {
            throw new ArgumentException(
                "A Scene exact-slot query source must match its Activity plan.",
                nameof(source));
        }

        if (source.Placement == item.Placement)
        {
            throw new ArgumentException(
                "An exact-destination Scene source does not require a slot query.",
                nameof(source));
        }

        return new SceneExactSlotQuery(context, item, source);
    }
}

public enum SceneSourceLookupStatus
{
    NotFound,
    UniqueSource,
    SelectionRequired,
    Unavailable,
}

public sealed record SceneSourceLookup
{
    private SceneSourceLookup(
        int index,
        ActivityId activityId,
        SceneSourceLookupStatus status,
        SceneApplyItemReason reason,
        ImmutableArray<SceneSourceSelection> candidates)
    {
        Index = index;
        ActivityId = activityId;
        Status = status;
        Reason = reason;
        Candidates = candidates;
    }

    public int Index { get; }

    public ActivityId ActivityId { get; }

    public SceneSourceLookupStatus Status { get; }

    public SceneApplyItemReason Reason { get; }

    public ImmutableArray<SceneSourceSelection> Candidates { get; }

    public SceneSourceSelection? UniqueSource =>
        Status == SceneSourceLookupStatus.UniqueSource
            ? Candidates[0]
            : null;

    public static SceneSourceLookup FromObservation(
        int index,
        ActivityId activityId,
        IEnumerable<SceneSourceSelection> candidates,
        bool isComplete)
    {
        ValidateIdentity(index, activityId);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!isComplete)
        {
            return Unavailable(
                index,
                activityId,
                SceneApplyItemReason.SourceLookupUnavailable);
        }

        var bounded = ImmutableArray.CreateBuilder<SceneSourceSelection>(
            ScenePlan.MaximumActivities);
        var placements = new HashSet<(Guid DeviceId, string Slot)>();
        foreach (SceneSourceSelection candidate in candidates)
        {
            if (candidate is null)
            {
                throw new ArgumentException(
                    "Scene source candidates must be non-null.",
                    nameof(candidates));
            }

            if (bounded.Count == ScenePlan.MaximumActivities)
            {
                return Unavailable(
                    index,
                    activityId,
                    SceneApplyItemReason.SourceLookupUnavailable);
            }

            if (candidate.Index != index
                || candidate.ActivityId != activityId)
            {
                throw new ArgumentException(
                    "Every Scene source candidate must match the requested item and Activity.",
                    nameof(candidates));
            }

            if (!placements.Add((
                    candidate.DeviceId.Value,
                    candidate.Placement.Slot)))
            {
                throw new ArgumentException(
                    "Scene source candidates must identify unique active placements.",
                    nameof(candidates));
            }

            bounded.Add(candidate);
        }

        ImmutableArray<SceneSourceSelection> ordered = bounded
            .ToImmutable()
            .OrderBy(static candidate => candidate.DeviceId.Value)
            .ThenBy(
                static candidate => candidate.Placement.Slot,
                StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Revision)
            .ThenBy(
                static candidate => candidate.DescriptorDigest,
                StringComparer.Ordinal)
            .ThenBy(
                static candidate => candidate.Kind.Value,
                StringComparer.Ordinal)
            .ToImmutableArray();
        return ordered.Length switch
        {
            0 => new SceneSourceLookup(
                index,
                activityId,
                SceneSourceLookupStatus.NotFound,
                SceneApplyItemReason.SourceNotFound,
                []),
            1 => new SceneSourceLookup(
                index,
                activityId,
                SceneSourceLookupStatus.UniqueSource,
                SceneApplyItemReason.None,
                ordered),
            _ => new SceneSourceLookup(
                index,
                activityId,
                SceneSourceLookupStatus.SelectionRequired,
                SceneApplyItemReason.SourceSelectionRequired,
                ordered),
        };
    }

    public static SceneSourceLookup Unavailable(
        int index,
        ActivityId activityId,
        SceneApplyItemReason reason)
    {
        ValidateIdentity(index, activityId);
        if (reason is not (
            SceneApplyItemReason.SourceLookupUnavailable
            or SceneApplyItemReason.CapabilityDenied
            or SceneApplyItemReason.ProtocolUnsupported))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new SceneSourceLookup(
            index,
            activityId,
            SceneSourceLookupStatus.Unavailable,
            reason,
            []);
    }

    public bool Equals(SceneSourceLookup? other) =>
        other is not null
        && Index == other.Index
        && ActivityId == other.ActivityId
        && Status == other.Status
        && Reason == other.Reason
        && Candidates.SequenceEqual(other.Candidates);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Index);
        hash.Add(ActivityId);
        hash.Add(Status);
        hash.Add(Reason);
        foreach (SceneSourceSelection candidate in Candidates)
        {
            hash.Add(candidate);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"Scene source lookup {Index} ({Status}, {Candidates.Length} candidates)";

    private static void ValidateIdentity(int index, ActivityId activityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            ScenePlan.MaximumActivities);
        ArgumentNullException.ThrowIfNull(activityId);
    }
}

public enum SceneSlotOccupancyKind
{
    NotInspected,
    Empty,
    EligibleConflict,
    Opaque,
    Ambiguous,
}

public sealed record SceneSlotOccupancy
{
    public static SceneSlotOccupancy NotInspected { get; } =
        new(SceneSlotOccupancyKind.NotInspected, null, false);

    public static SceneSlotOccupancy Empty { get; } =
        new(SceneSlotOccupancyKind.Empty, null, false);

    public static SceneSlotOccupancy Opaque { get; } =
        new(SceneSlotOccupancyKind.Opaque, null, false);

    public static SceneSlotOccupancy Ambiguous { get; } =
        new(SceneSlotOccupancyKind.Ambiguous, null, false);

    private SceneSlotOccupancy(
        SceneSlotOccupancyKind kind,
        SceneReplaceTargetSnapshot? target,
        bool hasDurableUndoAvailability)
    {
        Kind = kind;
        Target = target;
        HasDurableUndoAvailability = hasDurableUndoAvailability;
    }

    public SceneSlotOccupancyKind Kind { get; }

    public SceneReplaceTargetSnapshot? Target { get; }

    public bool HasDurableUndoAvailability { get; }

    public static SceneSlotOccupancy EligibleConflict(
        SceneReplaceTargetSnapshot target,
        bool hasDurableUndoAvailability = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new SceneSlotOccupancy(
            SceneSlotOccupancyKind.EligibleConflict,
            target,
            hasDurableUndoAvailability);
    }

    public override string ToString() =>
        $"Scene slot occupancy ({Kind})";
}

public sealed record SceneExactSlotInspection
{
    private SceneExactSlotInspection(
        SceneSlotOccupancy? occupancy,
        SceneApplyItemReason reason)
    {
        Occupancy = occupancy;
        Reason = reason;
    }

    public SceneSlotOccupancy? Occupancy { get; }

    public SceneApplyItemReason Reason { get; }

    public bool IsBlocked => Reason != SceneApplyItemReason.None;

    public static SceneExactSlotInspection Observed(
        SceneSlotOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(occupancy);
        if (occupancy.Kind == SceneSlotOccupancyKind.NotInspected)
        {
            throw new ArgumentException(
                "A completed exact-slot inspection requires an occupancy result.",
                nameof(occupancy));
        }

        return new SceneExactSlotInspection(
            occupancy,
            SceneApplyItemReason.None);
    }

    public static SceneExactSlotInspection Blocked(
        SceneApplyItemReason reason)
    {
        if (reason is not (
            SceneApplyItemReason.CapabilityDenied
            or SceneApplyItemReason.ProtocolUnsupported
            or SceneApplyItemReason.DestinationUnavailable))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new SceneExactSlotInspection(null, reason);
    }

    public override string ToString() =>
        IsBlocked
            ? $"Scene exact-slot inspection blocked ({Reason})"
            : $"Scene exact-slot inspection observed ({Occupancy!.Kind})";
}

public static class SceneApplyItemResolver
{
    public static SceneApplyItemPreview Resolve(
        SceneActivityPlan plan,
        SceneSourceLookup sourceLookup,
        SceneSourceSelection? explicitSelection,
        SceneSlotOccupancy? occupancy,
        OperationId childOperationId,
        CorrelationId childCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceLookup);
        ArgumentNullException.ThrowIfNull(childOperationId);
        ArgumentNullException.ThrowIfNull(childCorrelationId);
        if (sourceLookup.ActivityId != plan.ActivityId)
        {
            throw new ArgumentException(
                "The Scene source lookup must match the Scene Activity.",
                nameof(sourceLookup));
        }

        SceneSourceSelection? source = ResolveSource(
            sourceLookup,
            explicitSelection,
            occupancy);
        if (source is null)
        {
            return SceneApplyItemPreview.BlockedBySourceLookup(
                plan,
                sourceLookup,
                childOperationId,
                childCorrelationId);
        }

        if (source.Placement == plan.Placement)
        {
            if (occupancy is not null
                && occupancy.Kind != SceneSlotOccupancyKind.NotInspected)
            {
                throw new ArgumentException(
                    "An exact-destination source must resolve without a slot query.",
                    nameof(occupancy));
            }

            return SceneApplyItemPreview.NoChange(
                plan,
                source,
                childOperationId,
                childCorrelationId);
        }

        if (occupancy is null
            || occupancy.Kind == SceneSlotOccupancyKind.NotInspected)
        {
            throw new ArgumentException(
                "A remote Scene source requires an exact-slot observation.",
                nameof(occupancy));
        }

        if (occupancy.Kind == SceneSlotOccupancyKind.Empty)
        {
            return SceneApplyItemPreview.TransferToEmpty(
                plan,
                source,
                childOperationId,
                childCorrelationId);
        }

        if (occupancy.Kind == SceneSlotOccupancyKind.EligibleConflict
            && plan.SourceDisposition == SceneSourceDisposition.PreserveSource
            && plan.ConflictPolicy == SceneConflictPolicy.ReplaceWithUndo)
        {
            if (!occupancy.HasDurableUndoAvailability)
            {
                return SceneApplyItemPreview.BlockedByOccupancy(
                    plan,
                    source,
                    occupancy,
                    childOperationId,
                    childCorrelationId);
            }

            SceneReplaceTargetSnapshot target = occupancy.Target
                ?? throw new ArgumentException(
                    "An eligible conflict requires exact target evidence.",
                    nameof(occupancy));
            return SceneApplyItemPreview.Replace(
                plan,
                source,
                target,
                childOperationId,
                childCorrelationId);
        }

        return SceneApplyItemPreview.BlockedByOccupancy(
            plan,
            source,
            occupancy,
            childOperationId,
            childCorrelationId);
    }

    internal static SceneSourceSelection? ResolveSource(
        SceneSourceLookup lookup,
        SceneSourceSelection? explicitSelection,
        SceneSlotOccupancy? occupancy)
    {
        switch (lookup.Status)
        {
            case SceneSourceLookupStatus.NotFound:
            case SceneSourceLookupStatus.Unavailable:
                if (explicitSelection is not null || occupancy is not null)
                {
                    throw new ArgumentException(
                        "A blocked source lookup cannot carry selected-source or slot evidence.",
                        nameof(explicitSelection));
                }

                return null;
            case SceneSourceLookupStatus.UniqueSource:
                {
                    SceneSourceSelection unique = lookup.UniqueSource
                        ?? throw new InvalidOperationException(
                            "A unique Scene source lookup requires one candidate.");
                    if (explicitSelection is not null
                        && explicitSelection != unique)
                    {
                        throw new ArgumentException(
                            "The selected Scene source snapshot is not the current unique source.",
                            nameof(explicitSelection));
                    }

                    return unique;
                }
            case SceneSourceLookupStatus.SelectionRequired:
                if (explicitSelection is null)
                {
                    if (occupancy is not null)
                    {
                        throw new ArgumentException(
                            "Slot evidence cannot precede an exact Scene source selection.",
                            nameof(occupancy));
                    }

                    return null;
                }

                if (!lookup.Candidates.Contains(explicitSelection))
                {
                    throw new ArgumentException(
                        "The selected Scene source snapshot is absent from the complete current lookup.",
                        nameof(explicitSelection));
                }

                return explicitSelection;
            default:
                throw new ArgumentOutOfRangeException(nameof(lookup));
        }
    }
}
