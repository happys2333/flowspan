using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Application;

public enum SceneApplyAction
{
    Blocked,
    NoChange,
    Handoff,
    Move,
    Replace,
}

public enum SceneApplyItemReason
{
    None,
    UnsafeMoveReplace,
}

public enum SceneSlotOccupancy
{
    NotInspected,
    Empty,
    EligibleConflict,
    Opaque,
    Ambiguous,
}

public sealed record SceneSourceSelection
{
    private SceneSourceSelection(
        int index,
        ActivityId activityId,
        long revision,
        string descriptorDigest,
        ActivityKind kind,
        ActivityPlacement placement)
    {
        Index = index;
        ActivityId = activityId;
        Revision = revision;
        DescriptorDigest = descriptorDigest;
        Kind = kind;
        Placement = placement;
    }

    public int Index { get; }

    public ActivityId ActivityId { get; }

    public DeviceId DeviceId => Placement.DeviceId;

    public long Revision { get; }

    public string DescriptorDigest { get; }

    public ActivityKind Kind { get; }

    public ActivityPlacement Placement { get; }

    public static SceneSourceSelection Create(
        int index,
        ActivityId activityId,
        long revision,
        string descriptorDigest,
        ActivityKind kind,
        ActivityPlacement placement)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            ScenePlan.MaximumActivities);

        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        string canonicalDigest = SceneApplyBinding.ValidateDigest(
            descriptorDigest,
            nameof(descriptorDigest));
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(placement);
        return new SceneSourceSelection(
            index,
            activityId,
            revision,
            canonicalDigest,
            kind,
            placement);
    }

    public override string ToString() =>
        $"Scene source selection {Index} ({Kind.Value}, revision {Revision})";
}

public sealed record SceneApplyItemPreview
{
    private SceneApplyItemPreview(
        int index,
        ActivityId activityId,
        ActivityPlacement destination,
        SceneSourceDisposition sourceDisposition,
        SceneConflictPolicy conflictPolicy,
        SceneSourceSelection source,
        SceneSlotOccupancy occupancy,
        SceneReplaceTargetSnapshot? replaceTarget,
        OperationId childOperationId,
        CorrelationId childCorrelationId,
        SceneApplyAction action,
        SceneApplyItemReason reason)
    {
        Index = index;
        ActivityId = activityId;
        Destination = destination;
        SourceDisposition = sourceDisposition;
        ConflictPolicy = conflictPolicy;
        Source = source;
        Occupancy = occupancy;
        ReplaceTarget = replaceTarget;
        ChildOperationId = childOperationId;
        ChildCorrelationId = childCorrelationId;
        Action = action;
        Reason = reason;
    }

    public int Index { get; }

    public ActivityId ActivityId { get; }

    public ActivityPlacement Destination { get; }

    public SceneSourceDisposition SourceDisposition { get; }

    public SceneConflictPolicy ConflictPolicy { get; }

    public SceneSourceSelection Source { get; }

    public SceneSlotOccupancy Occupancy { get; }

    public SceneReplaceTargetSnapshot? ReplaceTarget { get; }

    public OperationId ChildOperationId { get; }

    public CorrelationId ChildCorrelationId { get; }

    public SceneApplyAction Action { get; }

    public SceneApplyItemReason Reason { get; }

    public static SceneApplyItemPreview NoChange(
        SceneActivityPlan plan,
        SceneSourceSelection source,
        OperationId childOperationId,
        CorrelationId childCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(childOperationId);
        ArgumentNullException.ThrowIfNull(childCorrelationId);
        if (source.ActivityId != plan.ActivityId
            || source.Placement != plan.Placement)
        {
            throw new ArgumentException(
                "A No Change source must exactly match the Scene Activity and destination.",
                nameof(source));
        }

        return new SceneApplyItemPreview(
            source.Index,
            plan.ActivityId,
            plan.Placement,
            plan.SourceDisposition,
            plan.ConflictPolicy,
            source,
            SceneSlotOccupancy.NotInspected,
            null,
            childOperationId,
            childCorrelationId,
            SceneApplyAction.NoChange,
            SceneApplyItemReason.None);
    }

    public static SceneApplyItemPreview Replace(
        SceneActivityPlan plan,
        SceneSourceSelection source,
        SceneReplaceTargetSnapshot target,
        OperationId childOperationId,
        CorrelationId childCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(childOperationId);
        ArgumentNullException.ThrowIfNull(childCorrelationId);
        if (source.ActivityId != plan.ActivityId
            || source.Placement == plan.Placement
            || target.Placement != plan.Placement
            || target.ActivityId == source.ActivityId
            || target.Kind != source.Kind
            || plan.SourceDisposition != SceneSourceDisposition.PreserveSource
            || plan.ConflictPolicy != SceneConflictPolicy.ReplaceWithUndo)
        {
            throw new ArgumentException(
                "A Scene Replace preview requires an exact eligible target and Preserve Source policy.",
                nameof(target));
        }

        return new SceneApplyItemPreview(
            source.Index,
            plan.ActivityId,
            plan.Placement,
            plan.SourceDisposition,
            plan.ConflictPolicy,
            source,
            SceneSlotOccupancy.EligibleConflict,
            target,
            childOperationId,
            childCorrelationId,
            SceneApplyAction.Replace,
            SceneApplyItemReason.None);
    }

    public static SceneApplyItemPreview TransferToEmpty(
        SceneActivityPlan plan,
        SceneSourceSelection source,
        OperationId childOperationId,
        CorrelationId childCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(childOperationId);
        ArgumentNullException.ThrowIfNull(childCorrelationId);
        if (source.ActivityId != plan.ActivityId
            || source.Placement == plan.Placement)
        {
            throw new ArgumentException(
                "A Scene transfer source must match the Activity and differ from its destination.",
                nameof(source));
        }

        SceneApplyAction action = plan.SourceDisposition switch
        {
            SceneSourceDisposition.PreserveSource => SceneApplyAction.Handoff,
            SceneSourceDisposition.MoveAfterAcknowledgement =>
                SceneApplyAction.Move,
            _ => throw new ArgumentOutOfRangeException(nameof(plan)),
        };
        return new SceneApplyItemPreview(
            source.Index,
            plan.ActivityId,
            plan.Placement,
            plan.SourceDisposition,
            plan.ConflictPolicy,
            source,
            SceneSlotOccupancy.Empty,
            null,
            childOperationId,
            childCorrelationId,
            action,
            SceneApplyItemReason.None);
    }

    public static SceneApplyItemPreview Blocked(
        SceneActivityPlan plan,
        int index,
        SceneApplyItemReason reason,
        OperationId childOperationId,
        CorrelationId childCorrelationId,
        SceneSourceSelection source,
        SceneSlotOccupancy occupancy,
        SceneReplaceTargetSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            ScenePlan.MaximumActivities);
        ArgumentNullException.ThrowIfNull(childOperationId);
        ArgumentNullException.ThrowIfNull(childCorrelationId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        bool isUnsafeMoveReplace = reason == SceneApplyItemReason.UnsafeMoveReplace
            && source.Index == index
            && source.ActivityId == plan.ActivityId
            && source.Placement != plan.Placement
            && occupancy == SceneSlotOccupancy.EligibleConflict
            && target.Placement == plan.Placement
            && target.ActivityId != plan.ActivityId
            && target.Kind == source.Kind
            && plan.SourceDisposition
                == SceneSourceDisposition.MoveAfterAcknowledgement
            && plan.ConflictPolicy == SceneConflictPolicy.ReplaceWithUndo;
        if (!isUnsafeMoveReplace)
        {
            throw new ArgumentException(
                "The blocked Scene item evidence does not match its reason.",
                nameof(reason));
        }

        return new SceneApplyItemPreview(
            index,
            plan.ActivityId,
            plan.Placement,
            plan.SourceDisposition,
            plan.ConflictPolicy,
            source,
            occupancy,
            target,
            childOperationId,
            childCorrelationId,
            SceneApplyAction.Blocked,
            reason);
    }

    public override string ToString() =>
        $"Scene apply item {Index} ({SceneApplyBinding.Format(Action)})";
}

public sealed record SceneReplaceTargetSnapshot
{
    private SceneReplaceTargetSnapshot(
        ActivityId activityId,
        long revision,
        string descriptorDigest,
        ActivityKind kind,
        ActivityPlacement placement)
    {
        ActivityId = activityId;
        Revision = revision;
        DescriptorDigest = descriptorDigest;
        Kind = kind;
        Placement = placement;
    }

    public ActivityId ActivityId { get; }

    public DeviceId DeviceId => Placement.DeviceId;

    public long Revision { get; }

    public string DescriptorDigest { get; }

    public ActivityKind Kind { get; }

    public ActivityPlacement Placement { get; }

    public static SceneReplaceTargetSnapshot Create(
        ActivityId activityId,
        long revision,
        string descriptorDigest,
        ActivityKind kind,
        ActivityPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        string canonicalDigest = SceneApplyBinding.ValidateDigest(
            descriptorDigest,
            nameof(descriptorDigest));
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(placement);
        return new SceneReplaceTargetSnapshot(
            activityId,
            revision,
            canonicalDigest,
            kind,
            placement);
    }

    public override string ToString() =>
        $"Scene Replace target ({Kind.Value}, revision {Revision})";
}

public sealed record SceneApplyPreview
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);

    private SceneApplyPreview(
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        SceneGroupRevisionWarning? groupRevisionWarning,
        ImmutableArray<SceneApplyItemPreview> items,
        string fingerprint,
        ImmutableArray<SceneReplaceConfirmation> requiredReplaceConfirmations)
    {
        SceneId = sceneId;
        SceneRevision = sceneRevision;
        SceneDigest = sceneDigest;
        ParentOperationId = parentOperationId;
        ParentCorrelationId = parentCorrelationId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        GroupRevisionWarning = groupRevisionWarning;
        Items = items;
        Fingerprint = fingerprint;
        RequiredReplaceConfirmations = requiredReplaceConfirmations;
    }

    public SceneId SceneId { get; }

    public long SceneRevision { get; }

    public string SceneDigest { get; }

    public OperationId ParentOperationId { get; }

    public CorrelationId ParentCorrelationId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public SceneGroupRevisionWarning? GroupRevisionWarning { get; }

    public ImmutableArray<SceneApplyItemPreview> Items { get; }

    public string Fingerprint { get; }

    public ImmutableArray<SceneReplaceConfirmation> RequiredReplaceConfirmations
    {
        get;
    }

    public static SceneApplyPreview Create(
        ScenePlan scene,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        IEnumerable<SceneApplyItemPreview> items,
        long? observedGroupRevision = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(parentOperationId);
        ArgumentNullException.ThrowIfNull(parentCorrelationId);
        ArgumentNullException.ThrowIfNull(items);
        DateTimeOffset canonicalCreatedAt = createdAt.ToUniversalTime();
        DateTimeOffset canonicalExpiresAt = expiresAt.ToUniversalTime();
        if (canonicalExpiresAt <= canonicalCreatedAt
            || canonicalExpiresAt - canonicalCreatedAt > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "A Scene apply preview must expire within five minutes of creation.");
        }

        SceneGroupRevisionWarning? groupRevisionWarning = null;
        if (observedGroupRevision is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                observedGroupRevision.Value,
                1);
            SceneGroupBinding binding = scene.GroupBinding
                ?? throw new ArgumentException(
                    "An observed Group revision requires a Group-derived Scene.",
                    nameof(observedGroupRevision));
            if (observedGroupRevision.Value != binding.GroupRevision)
            {
                groupRevisionWarning = new SceneGroupRevisionWarning(
                    binding.GroupId,
                    binding.GroupRevision,
                    observedGroupRevision.Value);
            }
        }

        ImmutableArray<SceneApplyItemPreview> ordered = items.ToImmutableArray();
        if (ordered.Length != scene.Activities.Length
            || ordered.Length is < 1 or > ScenePlan.MaximumActivities
            || ordered.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A Scene apply preview must contain one item for every Scene Activity.",
                nameof(items));
        }

        var operationIds = new HashSet<OperationId> { parentOperationId };
        var correlationIds = new HashSet<CorrelationId> { parentCorrelationId };
        for (int index = 0; index < ordered.Length; index++)
        {
            SceneApplyItemPreview item = ordered[index];
            SceneActivityPlan plan = scene.Activities[index];
            if (item.Index != index
                || item.ActivityId != plan.ActivityId
                || item.Destination != plan.Placement
                || item.SourceDisposition != plan.SourceDisposition
                || item.ConflictPolicy != plan.ConflictPolicy)
            {
                throw new ArgumentException(
                    "Scene apply preview items must exactly match saved Scene order and policy.",
                    nameof(items));
            }

            if (!operationIds.Add(item.ChildOperationId)
                || !correlationIds.Add(item.ChildCorrelationId))
            {
                throw new ArgumentException(
                    "Scene apply child operation and correlation IDs must be distinct.",
                    nameof(items));
            }
        }

        string sceneDigest = Convert.ToHexString(
            SHA256.HashData(ScenePlanCodec.Encode(scene)));
        string fingerprint = SceneApplyBinding.ComputePreviewFingerprint(
            scene,
            sceneDigest,
            parentOperationId,
            parentCorrelationId,
            canonicalCreatedAt,
            canonicalExpiresAt,
            groupRevisionWarning,
            ordered);
        ImmutableArray<SceneReplaceConfirmation> requiredReplaceConfirmations =
            ordered
                .Where(static item => item.Action == SceneApplyAction.Replace)
                .Select(item => SceneReplaceConfirmation.Create(
                    item.Index,
                    SceneApplyBinding.ComputeReplaceConfirmationFingerprint(
                        fingerprint,
                        item)))
                .ToImmutableArray();
        return new SceneApplyPreview(
            scene.Id,
            scene.Revision,
            sceneDigest,
            parentOperationId,
            parentCorrelationId,
            canonicalCreatedAt,
            canonicalExpiresAt,
            groupRevisionWarning,
            ordered,
            fingerprint,
            requiredReplaceConfirmations);
    }

    public override string ToString() =>
        $"Scene apply preview {SceneId} revision {SceneRevision} "
        + $"with {Items.Length} items (fingerprint {Fingerprint})";
}

public sealed record SceneGroupRevisionWarning
{
    internal SceneGroupRevisionWarning(
        GroupId groupId,
        long boundRevision,
        long observedRevision)
    {
        GroupId = groupId;
        BoundRevision = boundRevision;
        ObservedRevision = observedRevision;
    }

    public GroupId GroupId { get; }

    public long BoundRevision { get; }

    public long ObservedRevision { get; }

    public override string ToString() =>
        $"Scene Group revision warning ({BoundRevision} -> {ObservedRevision})";
}

public sealed record SceneReplaceConfirmation
{
    private SceneReplaceConfirmation(int index, string fingerprint)
    {
        Index = index;
        Fingerprint = fingerprint;
    }

    public int Index { get; }

    public string Fingerprint { get; }

    public static SceneReplaceConfirmation Create(int index, string fingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            ScenePlan.MaximumActivities);
        return new SceneReplaceConfirmation(
            index,
            SceneApplyBinding.ValidateDigest(fingerprint, nameof(fingerprint)));
    }

    public override string ToString() =>
        $"Scene Replace confirmation {Index} (fingerprint {Fingerprint})";
}

public sealed record SceneApplyApproval
{
    private SceneApplyApproval(
        string previewFingerprint,
        ImmutableArray<SceneReplaceConfirmation> replaceConfirmations)
    {
        PreviewFingerprint = previewFingerprint;
        ReplaceConfirmations = replaceConfirmations;
    }

    public string PreviewFingerprint { get; }

    public ImmutableArray<SceneReplaceConfirmation> ReplaceConfirmations { get; }

    public static SceneApplyApproval Create(
        string previewFingerprint,
        IEnumerable<SceneReplaceConfirmation> replaceConfirmations)
    {
        string canonicalPreviewFingerprint = SceneApplyBinding.ValidateDigest(
            previewFingerprint,
            nameof(previewFingerprint));
        ArgumentNullException.ThrowIfNull(replaceConfirmations);
        ImmutableArray<SceneReplaceConfirmation> ordered =
            replaceConfirmations.ToImmutableArray();
        if (ordered.Length > ScenePlan.MaximumActivities
            || ordered.Any(static confirmation => confirmation is null))
        {
            throw new ArgumentException(
                "Scene Replace confirmations must be bounded and non-null.",
                nameof(replaceConfirmations));
        }

        int previousIndex = -1;
        foreach (SceneReplaceConfirmation confirmation in ordered)
        {
            if (confirmation.Index <= previousIndex)
            {
                throw new ArgumentException(
                    "Scene Replace confirmations must be unique and in Scene order.",
                    nameof(replaceConfirmations));
            }

            previousIndex = confirmation.Index;
        }

        return new SceneApplyApproval(
            canonicalPreviewFingerprint,
            ordered);
    }

    public override string ToString() =>
        $"Scene apply approval for preview {PreviewFingerprint} "
        + $"with {ReplaceConfirmations.Length} Replace confirmations";
}

public enum SceneApplyApprovalStatus
{
    Valid,
    SceneChanged,
    PreviewMismatch,
    Expired,
    ReplaceConfirmationMismatch,
}

public static class SceneApplyApprovalVerifier
{
    public static SceneApplyApprovalStatus Validate(
        ScenePlan scene,
        SceneApplyPreview preview,
        SceneApplyApproval approval,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(approval);
        string currentSceneDigest = Convert.ToHexString(
            SHA256.HashData(ScenePlanCodec.Encode(scene)));
        if (scene.Id != preview.SceneId
            || scene.Revision != preview.SceneRevision
            || !string.Equals(
                currentSceneDigest,
                preview.SceneDigest,
                StringComparison.Ordinal))
        {
            return SceneApplyApprovalStatus.SceneChanged;
        }

        if (!string.Equals(
                approval.PreviewFingerprint,
                preview.Fingerprint,
                StringComparison.Ordinal))
        {
            return SceneApplyApprovalStatus.PreviewMismatch;
        }

        if (now.ToUniversalTime() >= preview.ExpiresAt)
        {
            return SceneApplyApprovalStatus.Expired;
        }

        return approval.ReplaceConfirmations.SequenceEqual(
            preview.RequiredReplaceConfirmations)
            ? SceneApplyApprovalStatus.Valid
            : SceneApplyApprovalStatus.ReplaceConfirmationMismatch;
    }
}

internal static class SceneApplyBinding
{
    private const string PreviewDomain = "flowspan.scene-apply-preview/v1";
    private const string ReplaceConfirmationDomain =
        "flowspan.scene-apply-replace-confirmation/v1";

    public static string ValidateDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != SHA256.HashSizeInBytes * 2
            || !value.All(char.IsAsciiHexDigit)
            || !string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A Scene apply digest must be canonical uppercase SHA-256 hexadecimal.",
                parameterName);
        }

        return value;
    }

    public static string ComputePreviewFingerprint(
        ScenePlan scene,
        string sceneDigest,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        SceneGroupRevisionWarning? groupRevisionWarning,
        ImmutableArray<SceneApplyItemPreview> items)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, PreviewDomain);
        Append(hash, scene.Id.ToString());
        Append(hash, Format(scene.Revision));
        Append(hash, sceneDigest);
        Append(hash, parentOperationId.ToString());
        Append(hash, parentCorrelationId.ToString());
        Append(hash, createdAt.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, expiresAt.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, scene.GroupBinding is null ? "none" : "some");
        if (scene.GroupBinding is not null)
        {
            Append(hash, scene.GroupBinding.GroupId.ToString());
            Append(hash, Format(scene.GroupBinding.GroupRevision));
        }

        Append(hash, groupRevisionWarning is null ? "none" : "some");
        if (groupRevisionWarning is not null)
        {
            Append(hash, groupRevisionWarning.GroupId.ToString());
            Append(hash, Format(groupRevisionWarning.BoundRevision));
            Append(hash, Format(groupRevisionWarning.ObservedRevision));
        }

        Append(hash, Format(items.Length));
        foreach (SceneApplyItemPreview item in items)
        {
            Append(hash, Format(item.Index));
            Append(hash, item.ActivityId.ToString());
            Append(hash, item.Destination.DeviceId.ToString());
            Append(hash, item.Destination.Slot);
            Append(hash, Format(item.SourceDisposition));
            Append(hash, Format(item.ConflictPolicy));
            Append(hash, item.ChildOperationId.ToString());
            Append(hash, item.ChildCorrelationId.ToString());
            Append(hash, "some");
            Append(hash, item.Source.DeviceId.ToString());
            Append(hash, Format(item.Source.Revision));
            Append(hash, item.Source.DescriptorDigest);
            Append(hash, item.Source.Kind.Value);
            Append(hash, item.Source.Placement.Slot);
            Append(hash, Format(item.Action));
            Append(hash, Format(item.Reason));
            Append(hash, Format(item.Occupancy));
            Append(hash, item.ReplaceTarget is null ? "none" : "some");
            if (item.ReplaceTarget is not null)
            {
                Append(hash, item.ReplaceTarget.DeviceId.ToString());
                Append(hash, item.ReplaceTarget.ActivityId.ToString());
                Append(hash, Format(item.ReplaceTarget.Revision));
                Append(hash, item.ReplaceTarget.DescriptorDigest);
                Append(hash, item.ReplaceTarget.Kind.Value);
                Append(hash, item.ReplaceTarget.Placement.Slot);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string ComputeReplaceConfirmationFingerprint(
        string previewFingerprint,
        SceneApplyItemPreview item)
    {
        ArgumentNullException.ThrowIfNull(item);
        SceneReplaceTargetSnapshot target = item.ReplaceTarget
            ?? throw new ArgumentException(
                "A Scene Replace confirmation requires an exact target.",
                nameof(item));
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        Append(hash, ReplaceConfirmationDomain);
        Append(hash, previewFingerprint);
        Append(hash, Format(item.Index));
        Append(hash, item.ActivityId.ToString());
        Append(hash, target.DeviceId.ToString());
        Append(hash, target.ActivityId.ToString());
        Append(hash, Format(target.Revision));
        Append(hash, target.DescriptorDigest);
        Append(hash, target.Kind.Value);
        Append(hash, target.Placement.Slot);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string Format(SceneApplyAction action) => action switch
    {
        SceneApplyAction.Blocked => "blocked",
        SceneApplyAction.NoChange => "no-change",
        SceneApplyAction.Handoff => "handoff",
        SceneApplyAction.Move => "move",
        SceneApplyAction.Replace => "replace",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static string Format(SceneApplyItemReason reason) => reason switch
    {
        SceneApplyItemReason.None => "none",
        SceneApplyItemReason.UnsafeMoveReplace => "unsafe-move-replace",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static string Format(SceneSlotOccupancy occupancy) => occupancy switch
    {
        SceneSlotOccupancy.NotInspected => "not-inspected",
        SceneSlotOccupancy.Empty => "empty",
        SceneSlotOccupancy.EligibleConflict => "eligible-conflict",
        SceneSlotOccupancy.Opaque => "opaque",
        SceneSlotOccupancy.Ambiguous => "ambiguous",
        _ => throw new ArgumentOutOfRangeException(nameof(occupancy)),
    };

    private static string Format(SceneSourceDisposition disposition) =>
        disposition switch
        {
            SceneSourceDisposition.PreserveSource => "preserve-source",
            SceneSourceDisposition.MoveAfterAcknowledgement =>
                "move-after-acknowledgement",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    private static string Format(SceneConflictPolicy policy) => policy switch
    {
        SceneConflictPolicy.RequireEmpty => "require-empty",
        SceneConflictPolicy.ReplaceWithUndo => "replace-with-undo",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    private static string Format(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, encoded.Length);
        hash.AppendData(length);
        hash.AppendData(encoded);
    }
}
