using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public enum SceneApplyItemOutcome
{
    Blocked,
    NoChange,
    Committed,
    CommittedWithWarning,
    Rejected,
    Failed,
    Recovering,
    NotAttempted,
}

public enum SceneApplyOverallStatus
{
    Completed,
    CompletedWithWarnings,
    PartiallyCompleted,
    Blocked,
    Recovering,
    Cancelled,
}

public sealed record SceneApplyItemResult
{
    private SceneApplyItemResult(
        int index,
        ActivityId activityId,
        SceneSourceDisposition requestedSourceDisposition,
        SceneConflictPolicy requestedConflictPolicy,
        SceneApplyAction action,
        OperationId childOperationId,
        CorrelationId childCorrelationId,
        SceneApplyItemOutcome outcome,
        SceneApplyItemReason reason,
        FailureCode failureCode,
        DateTimeOffset occurredAt,
        UndoCapsuleReference? undoCapsule)
    {
        Index = index;
        ActivityId = activityId;
        RequestedSourceDisposition = requestedSourceDisposition;
        RequestedConflictPolicy = requestedConflictPolicy;
        Action = action;
        ChildOperationId = childOperationId;
        ChildCorrelationId = childCorrelationId;
        Outcome = outcome;
        Reason = reason;
        FailureCode = failureCode;
        OccurredAt = occurredAt;
        UndoCapsule = undoCapsule;
    }

    public int Index { get; }

    public ActivityId ActivityId { get; }

    public SceneSourceDisposition RequestedSourceDisposition { get; }

    public SceneConflictPolicy RequestedConflictPolicy { get; }

    public SceneApplyAction Action { get; }

    public OperationId ChildOperationId { get; }

    public CorrelationId ChildCorrelationId { get; }

    public SceneApplyItemOutcome Outcome { get; }

    public SceneApplyItemReason Reason { get; }

    public FailureCode FailureCode { get; }

    public DateTimeOffset OccurredAt { get; }

    public UndoCapsuleReference? UndoCapsule { get; }

    public bool IsSatisfied => Outcome is SceneApplyItemOutcome.NoChange
        or SceneApplyItemOutcome.Committed
        or SceneApplyItemOutcome.CommittedWithWarning;

    public static SceneApplyItemResult FromPreviewOnly(
        SceneApplyItemPreview item,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        (SceneApplyItemOutcome outcome, SceneApplyItemReason reason) =
            item.Action switch
            {
                SceneApplyAction.Blocked => (
                    SceneApplyItemOutcome.Blocked,
                    item.Reason),
                SceneApplyAction.NoChange => (
                    SceneApplyItemOutcome.NoChange,
                    SceneApplyItemReason.None),
                _ => throw new ArgumentException(
                    "Only blocked and No Change Scene items have preview-only results.",
                    nameof(item)),
            };
        return Create(
            item,
            outcome,
            reason,
            FailureCode.None,
            occurredAt,
            null);
    }

    public static SceneApplyItemResult FromOperation(
        SceneApplyItemPreview item,
        OperationReceipt receipt,
        UndoCapsuleReference? undoCapsule)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(receipt);
        SceneSourceSelection source = item.Source
            ?? throw new ArgumentException(
                "An executed Scene item requires an exact selected source.",
                nameof(item));
        OperationKind expectedKind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentException(
                "A blocked or No Change Scene item cannot carry an operation receipt.",
                nameof(item)),
        };
        if (receipt.OperationId != item.ChildOperationId
            || receipt.CorrelationId != item.ChildCorrelationId
            || receipt.Kind != expectedKind
            || receipt.SourceDeviceId != source.DeviceId
            || receipt.TargetDeviceId != item.Destination.DeviceId
            || receipt.ActivityId != item.ActivityId
            || (receipt.ActivityKind is not null
                && receipt.ActivityKind != source.Kind)
            || (receipt.DescriptorDigest is not null
                && !string.Equals(
                    receipt.DescriptorDigest,
                    source.DescriptorDigest,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The child operation receipt does not match the exact Scene item binding.",
                nameof(receipt));
        }

        SceneApplyItemOutcome outcome = receipt.Status switch
        {
            OperationStatus.Committed => SceneApplyItemOutcome.Committed,
            OperationStatus.CommittedWithWarning =>
                SceneApplyItemOutcome.CommittedWithWarning,
            OperationStatus.Rejected => SceneApplyItemOutcome.Rejected,
            OperationStatus.Failed => SceneApplyItemOutcome.Failed,
            OperationStatus.Recovering => SceneApplyItemOutcome.Recovering,
            _ => throw new ArgumentOutOfRangeException(nameof(receipt)),
        };
        bool committedReplace = item.Action == SceneApplyAction.Replace
            && outcome is SceneApplyItemOutcome.Committed
                or SceneApplyItemOutcome.CommittedWithWarning;
        if (committedReplace)
        {
            ValidateUndoReference(item, source, receipt.OccurredAt, undoCapsule);
        }
        else if (undoCapsule is not null)
        {
            throw new ArgumentException(
                "Only a committed Scene Replace may carry an Undo Capsule reference.",
                nameof(undoCapsule));
        }

        return Create(
            item,
            outcome,
            SceneApplyItemReason.None,
            receipt.FailureCode,
            receipt.OccurredAt,
            undoCapsule);
    }

    public static SceneApplyItemResult NotAttempted(
        SceneApplyItemPreview item,
        SceneApplyItemReason reason,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (reason is not (
            SceneApplyItemReason.Cancelled
            or SceneApplyItemReason.NotAttemptedAfterRecovering))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return Create(
            item,
            SceneApplyItemOutcome.NotAttempted,
            reason,
            FailureCode.None,
            occurredAt,
            null);
    }

    public static SceneApplyItemResult RecoveringUnknown(
        SceneApplyItemPreview item,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Action is SceneApplyAction.Blocked
            or SceneApplyAction.NoChange)
        {
            throw new ArgumentException(
                "Only an executable Scene item can have an unknown operation outcome.",
                nameof(item));
        }

        if (failureCode == FailureCode.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        }

        return Create(
            item,
            SceneApplyItemOutcome.Recovering,
            SceneApplyItemReason.None,
            failureCode,
            occurredAt,
            null);
    }

    public override string ToString() =>
        $"Scene apply item result {Index} ({Outcome})";

    private static SceneApplyItemResult Create(
        SceneApplyItemPreview item,
        SceneApplyItemOutcome outcome,
        SceneApplyItemReason reason,
        FailureCode failureCode,
        DateTimeOffset occurredAt,
        UndoCapsuleReference? undoCapsule) =>
        new(
            item.Index,
            item.ActivityId,
            item.SourceDisposition,
            item.ConflictPolicy,
            item.Action,
            item.ChildOperationId,
            item.ChildCorrelationId,
            outcome,
            reason,
            failureCode,
            occurredAt.ToUniversalTime(),
            undoCapsule);

    private static void ValidateUndoReference(
        SceneApplyItemPreview item,
        SceneSourceSelection source,
        DateTimeOffset occurredAt,
        UndoCapsuleReference? undoCapsule)
    {
        ArgumentNullException.ThrowIfNull(undoCapsule);
        ArgumentNullException.ThrowIfNull(undoCapsule.Id);
        ArgumentNullException.ThrowIfNull(undoCapsule.OperationId);
        ArgumentNullException.ThrowIfNull(undoCapsule.CorrelationId);
        ArgumentNullException.ThrowIfNull(undoCapsule.TargetDeviceId);
        ArgumentNullException.ThrowIfNull(undoCapsule.TargetActivityId);
        ArgumentNullException.ThrowIfNull(undoCapsule.IncomingActivityId);
        SceneReplaceTargetSnapshot target = item.ReplaceTarget
            ?? throw new ArgumentException(
                "A committed Scene Replace requires exact target evidence.",
                nameof(item));
        if (undoCapsule.OperationId != item.ChildOperationId
            || undoCapsule.CorrelationId != item.ChildCorrelationId
            || undoCapsule.TargetDeviceId != item.Destination.DeviceId
            || undoCapsule.TargetActivityId != target.ActivityId
            || undoCapsule.ExpectedTargetRevision != target.Revision
            || !string.Equals(
                undoCapsule.TargetDescriptorDigest,
                target.DescriptorDigest,
                StringComparison.Ordinal)
            || undoCapsule.IncomingActivityId != item.ActivityId
            || !string.Equals(
                undoCapsule.IncomingDescriptorDigest,
                source.DescriptorDigest,
                StringComparison.Ordinal)
            || undoCapsule.ExpiresAt.ToUniversalTime()
                <= occurredAt.ToUniversalTime())
        {
            throw new ArgumentException(
                "The Undo Capsule reference does not match the committed Scene Replace.",
                nameof(undoCapsule));
        }
    }
}

public sealed record SceneApplyResult
{
    private SceneApplyResult(
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        string previewFingerprint,
        DateTimeOffset acceptedAt,
        DateTimeOffset updatedAt,
        ImmutableArray<SceneApplyItemResult> items,
        SceneApplyOverallStatus status)
    {
        SceneId = sceneId;
        SceneRevision = sceneRevision;
        SceneDigest = sceneDigest;
        ParentOperationId = parentOperationId;
        ParentCorrelationId = parentCorrelationId;
        PreviewFingerprint = previewFingerprint;
        AcceptedAt = acceptedAt;
        UpdatedAt = updatedAt;
        Items = items;
        Status = status;
    }

    public SceneId SceneId { get; }

    public long SceneRevision { get; }

    public string SceneDigest { get; }

    public OperationId ParentOperationId { get; }

    public CorrelationId ParentCorrelationId { get; }

    public string PreviewFingerprint { get; }

    public DateTimeOffset AcceptedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public ImmutableArray<SceneApplyItemResult> Items { get; }

    public SceneApplyOverallStatus Status { get; }

    public static SceneApplyResult Create(
        SceneApplyPreview preview,
        DateTimeOffset acceptedAt,
        DateTimeOffset updatedAt,
        IEnumerable<SceneApplyItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(items);
        DateTimeOffset canonicalAcceptedAt = acceptedAt.ToUniversalTime();
        DateTimeOffset canonicalUpdatedAt = updatedAt.ToUniversalTime();
        if (canonicalAcceptedAt < preview.CreatedAt
            || canonicalAcceptedAt >= preview.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acceptedAt),
                "A new Scene apply result must bind an acceptance inside the preview window.");
        }

        if (canonicalUpdatedAt < canonicalAcceptedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAt));
        }

        ImmutableArray<SceneApplyItemResult> ordered = items.ToImmutableArray();
        if (ordered.Length != preview.Items.Length
            || ordered.Length is < 1 or > ScenePlan.MaximumActivities
            || ordered.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A Scene apply result must contain exactly one bounded result per preview item.",
                nameof(items));
        }

        SceneApplyItemReason? boundaryReason = null;
        for (int index = 0; index < ordered.Length; index++)
        {
            SceneApplyItemPreview expected = preview.Items[index];
            SceneApplyItemResult actual = ordered[index];
            if (actual.Index != index
                || actual.ActivityId != expected.ActivityId
                || actual.RequestedSourceDisposition != expected.SourceDisposition
                || actual.RequestedConflictPolicy != expected.ConflictPolicy
                || actual.Action != expected.Action
                || actual.ChildOperationId != expected.ChildOperationId
                || actual.ChildCorrelationId != expected.ChildCorrelationId)
            {
                throw new ArgumentException(
                    "Scene apply results must exactly match preview order and bindings.",
                    nameof(items));
            }

            if (actual.OccurredAt < canonicalAcceptedAt
                || actual.OccurredAt > canonicalUpdatedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    "Every Scene item result time must fall inside the accepted result interval.");
            }

            if (boundaryReason is not null)
            {
                if (actual.Outcome != SceneApplyItemOutcome.NotAttempted
                    || actual.Reason != boundaryReason.Value)
                {
                    throw new ArgumentException(
                        "No Scene item may execute after a Recovering or cancellation boundary.",
                        nameof(items));
                }

                continue;
            }

            if (actual.Outcome == SceneApplyItemOutcome.Recovering)
            {
                boundaryReason = SceneApplyItemReason.NotAttemptedAfterRecovering;
            }
            else if (actual.Outcome == SceneApplyItemOutcome.NotAttempted)
            {
                if (actual.Reason != SceneApplyItemReason.Cancelled)
                {
                    throw new ArgumentException(
                        "A Recovering remainder requires a preceding uncertain item.",
                        nameof(items));
                }

                boundaryReason = SceneApplyItemReason.Cancelled;
            }
        }

        SceneApplyOverallStatus status = Reduce(ordered);
        return new SceneApplyResult(
            preview.SceneId,
            preview.SceneRevision,
            preview.SceneDigest,
            preview.ParentOperationId,
            preview.ParentCorrelationId,
            preview.Fingerprint,
            canonicalAcceptedAt,
            canonicalUpdatedAt,
            ordered,
            status);
    }

    public override string ToString() =>
        $"Scene apply result ({Status}, {Items.Length} items)";

    internal static SceneApplyOverallStatus Reduce(
        ImmutableArray<SceneApplyItemResult> items)
    {
        if (items.Any(static item =>
                item.Outcome == SceneApplyItemOutcome.Recovering))
        {
            return SceneApplyOverallStatus.Recovering;
        }

        bool allSatisfied = items.All(static item => item.IsSatisfied);
        if (allSatisfied)
        {
            return items.Any(static item =>
                    item.Outcome == SceneApplyItemOutcome.CommittedWithWarning)
                ? SceneApplyOverallStatus.CompletedWithWarnings
                : SceneApplyOverallStatus.Completed;
        }

        if (items.Any(static item => item.IsSatisfied))
        {
            return SceneApplyOverallStatus.PartiallyCompleted;
        }

        if (items.Any(static item =>
                item.Outcome == SceneApplyItemOutcome.NotAttempted
                && item.Reason == SceneApplyItemReason.Cancelled))
        {
            return SceneApplyOverallStatus.Cancelled;
        }

        return SceneApplyOverallStatus.Blocked;
    }
}
