using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SceneActivityPreparation
{
    private SceneActivityPreparation(
        DeviceId? remoteCoordinatorDeviceId,
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        string previewFingerprint,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset acceptedAt,
        SceneApplyItemPreview item)
    {
        RemoteCoordinatorDeviceId = remoteCoordinatorDeviceId;
        SceneId = sceneId;
        SceneRevision = sceneRevision;
        SceneDigest = sceneDigest;
        PreviewFingerprint = previewFingerprint;
        ParentOperationId = parentOperationId;
        ParentCorrelationId = parentCorrelationId;
        AcceptedAt = acceptedAt;
        Item = item;
    }

    public SceneId SceneId { get; }

    public DeviceId? RemoteCoordinatorDeviceId { get; }

    public long SceneRevision { get; }

    public string SceneDigest { get; }

    public string PreviewFingerprint { get; }

    public OperationId ParentOperationId { get; }

    public CorrelationId ParentCorrelationId { get; }

    public DateTimeOffset AcceptedAt { get; }

    public SceneApplyItemPreview Item { get; }

    internal static SceneActivityPreparation Create(
        SceneApplyPreview preview,
        DateTimeOffset acceptedAt,
        SceneApplyItemPreview item)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(item);
        if (item.Index < 0
            || item.Index >= preview.Items.Length
            || preview.Items[item.Index] != item)
        {
            throw new ArgumentException(
                "A Scene Activity preparation must use an exact preview item.",
                nameof(item));
        }

        return new SceneActivityPreparation(
            remoteCoordinatorDeviceId: null,
            preview.SceneId,
            preview.SceneRevision,
            preview.SceneDigest,
            preview.Fingerprint,
            preview.ParentOperationId,
            preview.ParentCorrelationId,
            acceptedAt.ToUniversalTime(),
            item);
    }

    internal static SceneActivityPreparation Create(
        SceneRemoteChildInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return new SceneActivityPreparation(
            instruction.CoordinatorDeviceId,
            instruction.SceneId,
            instruction.SceneRevision,
            instruction.SceneDigest,
            instruction.PreviewFingerprint,
            instruction.ParentOperationId,
            instruction.ParentCorrelationId,
            instruction.AcceptedAt,
            instruction.Item);
    }

    public override string ToString() =>
        $"Scene Activity preparation {Item.Index} ({Item.Action})";
}

public sealed record SceneActivityOperationResult
{
    private SceneActivityOperationResult(
        OperationReceipt receipt,
        UndoCapsuleReference? undoCapsule)
    {
        Receipt = receipt;
        UndoCapsule = undoCapsule;
    }

    public OperationReceipt Receipt { get; }

    public UndoCapsuleReference? UndoCapsule { get; }

    public static SceneActivityOperationResult Create(
        OperationReceipt receipt,
        UndoCapsuleReference? undoCapsule)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new SceneActivityOperationResult(receipt, undoCapsule);
    }
}

public interface ISceneActivityOperationPort
{
    public ValueTask<SceneActivityOperationResult> ExecuteAsync(
        SceneActivityPreparation preparation,
        CancellationToken cancellationToken);

    public ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleReference capsule,
        OperationContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<UndoReplaceResult>(
            new NotSupportedException(
                "This Scene operation port does not support compensation."));
}

public enum SceneApplyJournalItemStatus
{
    Pending,
    Started,
    Terminal,
}

public sealed record SceneApplyJournalItem
{
    private SceneApplyJournalItem(
        int index,
        OperationId childOperationId,
        CorrelationId childCorrelationId,
        SceneApplyItemPreview boundItem,
        SceneApplyJournalItemStatus status,
        DateTimeOffset? startedAt,
        SceneApplyItemResult? result)
    {
        Index = index;
        ChildOperationId = childOperationId;
        ChildCorrelationId = childCorrelationId;
        BoundItem = boundItem;
        Status = status;
        StartedAt = startedAt;
        Result = result;
    }

    public int Index { get; }

    public OperationId ChildOperationId { get; }

    public CorrelationId ChildCorrelationId { get; }

    public SceneApplyItemPreview BoundItem { get; }

    public SceneApplyJournalItemStatus Status { get; }

    public DateTimeOffset? StartedAt { get; }

    public SceneApplyItemResult? Result { get; }

    internal static SceneApplyJournalItem Pending(SceneApplyItemPreview item) =>
        new(
            item.Index,
            item.ChildOperationId,
            item.ChildCorrelationId,
            item,
            SceneApplyJournalItemStatus.Pending,
            null,
            null);

    internal static SceneApplyJournalItem Restore(
        SceneApplyItemPreview boundItem,
        SceneApplyJournalItemStatus status,
        DateTimeOffset? startedAt,
        SceneApplyItemResult? result)
    {
        ArgumentNullException.ThrowIfNull(boundItem);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        DateTimeOffset? canonicalStartedAt = startedAt?.ToUniversalTime();
        bool validShape = status switch
        {
            SceneApplyJournalItemStatus.Pending =>
                canonicalStartedAt is null && result is null,
            SceneApplyJournalItemStatus.Started =>
                canonicalStartedAt is not null
                && result is null
                && IsExecutable(boundItem.Action),
            SceneApplyJournalItemStatus.Terminal =>
                result is not null
                && IsValidTerminalShape(
                    boundItem,
                    canonicalStartedAt,
                    result),
            _ => false,
        };
        if (!validShape)
        {
            throw new InvalidDataException(
                "A restored Scene journal item has an invalid status shape.");
        }

        if (result is not null
            && (result.Index != boundItem.Index
                || result.ActivityId != boundItem.ActivityId
                || result.RequestedSourceDisposition != boundItem.SourceDisposition
                || result.RequestedConflictPolicy != boundItem.ConflictPolicy
                || result.Action != boundItem.Action
                || result.ChildOperationId != boundItem.ChildOperationId
                || result.ChildCorrelationId != boundItem.ChildCorrelationId))
        {
            throw new InvalidDataException(
                "A restored Scene result does not match its bound journal item.");
        }

        return new SceneApplyJournalItem(
            boundItem.Index,
            boundItem.ChildOperationId,
            boundItem.ChildCorrelationId,
            boundItem,
            status,
            canonicalStartedAt,
            result);
    }

    internal SceneApplyJournalItem Start(DateTimeOffset startedAt)
    {
        if (Status != SceneApplyJournalItemStatus.Pending
            || !IsExecutable(BoundItem.Action))
        {
            throw new InvalidOperationException(
                "Only a pending executable Scene journal item can start.");
        }

        return new SceneApplyJournalItem(
            Index,
            ChildOperationId,
            ChildCorrelationId,
            BoundItem,
            SceneApplyJournalItemStatus.Started,
            startedAt.ToUniversalTime(),
            null);
    }

    internal SceneApplyJournalItem Complete(SceneApplyItemResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Status == SceneApplyJournalItemStatus.Terminal
            || result.Index != Index
            || result.ActivityId != BoundItem.ActivityId
            || result.RequestedSourceDisposition != BoundItem.SourceDisposition
            || result.RequestedConflictPolicy != BoundItem.ConflictPolicy
            || result.Action != BoundItem.Action
            || result.ChildOperationId != ChildOperationId
            || result.ChildCorrelationId != ChildCorrelationId
            || !IsValidTerminalShape(BoundItem, StartedAt, result))
        {
            throw new InvalidOperationException(
                "The terminal Scene result does not match an open journal item.");
        }

        return new SceneApplyJournalItem(
            Index,
            ChildOperationId,
            ChildCorrelationId,
            BoundItem,
            SceneApplyJournalItemStatus.Terminal,
            StartedAt,
            result);
    }

    private static bool IsExecutable(SceneApplyAction action) =>
        action is SceneApplyAction.Handoff
            or SceneApplyAction.Move
            or SceneApplyAction.Replace;

    private static bool IsValidTerminalShape(
        SceneApplyItemPreview boundItem,
        DateTimeOffset? startedAt,
        SceneApplyItemResult result)
    {
        if (startedAt is not null)
        {
            bool hasOperationOutcome = result.Outcome is
                SceneApplyItemOutcome.Committed
                or SceneApplyItemOutcome.CommittedWithWarning
                or SceneApplyItemOutcome.Rejected
                or SceneApplyItemOutcome.Failed
                or SceneApplyItemOutcome.Recovering;
            return IsExecutable(boundItem.Action)
                && result.OccurredAt >= startedAt.Value
                && hasOperationOutcome;
        }

        return result.Outcome is SceneApplyItemOutcome.Blocked
            or SceneApplyItemOutcome.NoChange
            or SceneApplyItemOutcome.NotAttempted;
    }
}

public sealed record SceneApplyJournalState
{
    private SceneApplyJournalState(
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        string previewFingerprint,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset acceptedAt,
        ImmutableArray<SceneApplyJournalItem> items,
        DateTimeOffset? completedAt,
        SceneApplyOverallStatus? completedStatus)
    {
        SceneId = sceneId;
        SceneRevision = sceneRevision;
        SceneDigest = sceneDigest;
        PreviewFingerprint = previewFingerprint;
        ParentOperationId = parentOperationId;
        ParentCorrelationId = parentCorrelationId;
        AcceptedAt = acceptedAt;
        Items = items;
        CompletedAt = completedAt;
        CompletedStatus = completedStatus;
    }

    public SceneId SceneId { get; }

    public long SceneRevision { get; }

    public string SceneDigest { get; }

    public string PreviewFingerprint { get; }

    public OperationId ParentOperationId { get; }

    public CorrelationId ParentCorrelationId { get; }

    public DateTimeOffset AcceptedAt { get; }

    public ImmutableArray<SceneApplyJournalItem> Items { get; }

    public DateTimeOffset? CompletedAt { get; }

    public SceneApplyOverallStatus? CompletedStatus { get; }

    public bool IsCompleted => CompletedAt is not null;

    internal static SceneApplyJournalState Create(
        SceneApplyPreview preview,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DateTimeOffset canonicalAcceptedAt = acceptedAt.ToUniversalTime();
        if (canonicalAcceptedAt < preview.CreatedAt
            || canonicalAcceptedAt >= preview.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedAt));
        }

        return new SceneApplyJournalState(
            preview.SceneId,
            preview.SceneRevision,
            preview.SceneDigest,
            preview.Fingerprint,
            preview.ParentOperationId,
            preview.ParentCorrelationId,
            canonicalAcceptedAt,
            preview.Items.Select(SceneApplyJournalItem.Pending).ToImmutableArray(),
            null,
            null);
    }

    internal static SceneApplyJournalState Restore(
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        string previewFingerprint,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset acceptedAt,
        IEnumerable<SceneApplyJournalItem> items,
        DateTimeOffset? completedAt,
        SceneApplyOverallStatus? completedStatus)
    {
        ArgumentNullException.ThrowIfNull(sceneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(sceneRevision, 1);
        string canonicalSceneDigest = SceneApplyBinding.ValidateDigest(
            sceneDigest,
            nameof(sceneDigest));
        string canonicalPreviewFingerprint = SceneApplyBinding.ValidateDigest(
            previewFingerprint,
            nameof(previewFingerprint));
        ArgumentNullException.ThrowIfNull(parentOperationId);
        ArgumentNullException.ThrowIfNull(parentCorrelationId);
        if (acceptedAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "A restored Scene acceptance timestamp must be canonical UTC.");
        }

        ArgumentNullException.ThrowIfNull(items);
        ImmutableArray<SceneApplyJournalItem> restoredItems =
            items.ToImmutableArray();
        if (restoredItems.Length is < 1 or > ScenePlan.MaximumActivities
            || restoredItems.Any(static item => item is null))
        {
            throw new InvalidDataException(
                "A restored Scene attempt must contain 1 through 64 items.");
        }

        var operationIds = new HashSet<OperationId> { parentOperationId };
        var correlationIds = new HashSet<CorrelationId> { parentCorrelationId };
        int phase = 0;
        bool hasStarted = false;
        SceneApplyItemReason? boundaryReason = null;
        for (int index = 0; index < restoredItems.Length; index++)
        {
            SceneApplyJournalItem item = restoredItems[index];
            if (item.Index != index
                || !operationIds.Add(item.ChildOperationId)
                || !correlationIds.Add(item.ChildCorrelationId))
            {
                throw new InvalidDataException(
                    "Restored Scene items must be ordered with distinct child identities.");
            }

            if (item.StartedAt is not null
                && (item.StartedAt.Value.Offset != TimeSpan.Zero
                    || item.StartedAt.Value < acceptedAt))
            {
                throw new InvalidDataException(
                    "A restored Scene item start time is invalid.");
            }

            switch (item.Status)
            {
                case SceneApplyJournalItemStatus.Terminal:
                    if (phase != 0)
                    {
                        throw new InvalidDataException(
                            "A terminal Scene journal item cannot follow an open item.");
                    }

                    SceneApplyItemResult result = item.Result
                        ?? throw new InvalidDataException(
                            "A terminal Scene journal item requires a result.");
                    if (result.OccurredAt.Offset != TimeSpan.Zero
                        || result.OccurredAt < acceptedAt)
                    {
                        throw new InvalidDataException(
                            "A restored Scene item result time is invalid.");
                    }

                    if (boundaryReason is not null
                        && (result.Outcome != SceneApplyItemOutcome.NotAttempted
                            || result.Reason != boundaryReason.Value))
                    {
                        throw new InvalidDataException(
                            "A restored Scene attempt executed after a terminal boundary.");
                    }

                    if (result.Outcome == SceneApplyItemOutcome.Recovering)
                    {
                        boundaryReason =
                            SceneApplyItemReason.NotAttemptedAfterRecovering;
                    }
                    else if (result.Outcome == SceneApplyItemOutcome.NotAttempted)
                    {
                        if (result.Reason != SceneApplyItemReason.Cancelled
                            && boundaryReason is null)
                        {
                            throw new InvalidDataException(
                                "A Recovering remainder requires a preceding uncertain item.");
                        }

                        boundaryReason ??= SceneApplyItemReason.Cancelled;
                    }

                    break;
                case SceneApplyJournalItemStatus.Started:
                    if (phase != 0 || hasStarted)
                    {
                        throw new InvalidDataException(
                            "A restored Scene attempt can contain only one next Started item.");
                    }

                    hasStarted = true;
                    phase = 1;
                    break;
                case SceneApplyJournalItemStatus.Pending:
                    phase = 2;
                    break;
                default:
                    throw new InvalidDataException(
                        "A restored Scene journal item has an unknown status.");
            }
        }

        if ((completedAt is null) != (completedStatus is null))
        {
            throw new InvalidDataException(
                "A restored Scene completion timestamp and status must appear together.");
        }

        DateTimeOffset? canonicalCompletedAt = completedAt?.ToUniversalTime();
        if (completedStatus is not null
            && !Enum.IsDefined(completedStatus.Value))
        {
            throw new InvalidDataException(
                "A restored Scene attempt has an unknown completion status.");
        }

        if (canonicalCompletedAt is not null)
        {
            if (completedAt!.Value.Offset != TimeSpan.Zero
                || canonicalCompletedAt.Value < acceptedAt
                || restoredItems.Any(static item =>
                    item.Status != SceneApplyJournalItemStatus.Terminal))
            {
                throw new InvalidDataException(
                    "A completed restored Scene attempt must contain only terminal evidence.");
            }

            ImmutableArray<SceneApplyItemResult> results = restoredItems
                .Select(static item => item.Result!)
                .ToImmutableArray();
            if (results.Any(result =>
                    result.OccurredAt > canonicalCompletedAt.Value)
                || SceneApplyResult.Reduce(results) != completedStatus)
            {
                throw new InvalidDataException(
                    "A restored Scene completion does not match its item evidence.");
            }
        }

        return new SceneApplyJournalState(
            sceneId,
            sceneRevision,
            canonicalSceneDigest,
            canonicalPreviewFingerprint,
            parentOperationId,
            parentCorrelationId,
            acceptedAt,
            restoredItems,
            canonicalCompletedAt,
            completedStatus);
    }

    public bool Matches(SceneApplyPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (SceneId != preview.SceneId
            || SceneRevision != preview.SceneRevision
            || !string.Equals(SceneDigest, preview.SceneDigest, StringComparison.Ordinal)
            || !string.Equals(
                PreviewFingerprint,
                preview.Fingerprint,
                StringComparison.Ordinal)
            || ParentOperationId != preview.ParentOperationId
            || ParentCorrelationId != preview.ParentCorrelationId
            || Items.Length != preview.Items.Length)
        {
            return false;
        }

        for (int index = 0; index < Items.Length; index++)
        {
            if (Items[index].Index != index
                || Items[index].ChildOperationId
                    != preview.Items[index].ChildOperationId
                || Items[index].ChildCorrelationId
                    != preview.Items[index].ChildCorrelationId)
            {
                return false;
            }
        }

        return true;
    }

    internal SceneApplyJournalState StartItem(
        int index,
        DateTimeOffset startedAt)
    {
        EnsureOpenIndex(index);
        EnsureNextOpenIndex(index);
        if (FindBoundaryBefore(index) is not null)
        {
            throw new InvalidOperationException(
                "A Scene journal cannot start execution after a terminal boundary.");
        }

        if (startedAt.ToUniversalTime() < AcceptedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAt));
        }

        ImmutableArray<SceneApplyJournalItem> updated = Items.SetItem(
            index,
            Items[index].Start(startedAt));
        return Copy(updated, CompletedAt, CompletedStatus);
    }

    internal SceneApplyJournalState RecordOutcome(SceneApplyItemResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureOpenIndex(result.Index);
        EnsureNextOpenIndex(result.Index);
        if (result.OccurredAt < AcceptedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(result));
        }

        SceneApplyItemReason? boundaryReason = FindBoundaryBefore(result.Index);
        if (boundaryReason is not null
            && (result.Outcome != SceneApplyItemOutcome.NotAttempted
                || result.Reason != boundaryReason.Value))
        {
            throw new InvalidOperationException(
                "A Scene journal cannot record execution after a terminal boundary.");
        }

        if (boundaryReason is null
            && result.Outcome == SceneApplyItemOutcome.NotAttempted
            && result.Reason == SceneApplyItemReason.NotAttemptedAfterRecovering)
        {
            throw new InvalidOperationException(
                "A Recovering remainder requires a preceding uncertain item.");
        }

        ImmutableArray<SceneApplyJournalItem> updated = Items.SetItem(
            result.Index,
            Items[result.Index].Complete(result));
        return Copy(updated, CompletedAt, CompletedStatus);
    }

    internal SceneApplyJournalState CompleteResult(SceneApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (IsCompleted)
        {
            throw new InvalidOperationException("The Scene attempt is already complete.");
        }

        if (result.ParentOperationId != ParentOperationId
            || result.ParentCorrelationId != ParentCorrelationId
            || !string.Equals(
                result.PreviewFingerprint,
                PreviewFingerprint,
                StringComparison.Ordinal)
            || result.AcceptedAt != AcceptedAt
            || Items.Any(static item =>
                item.Status != SceneApplyJournalItemStatus.Terminal)
            || !Items.Select(static item => item.Result)
                .SequenceEqual(result.Items.Cast<SceneApplyItemResult?>()))
        {
            throw new InvalidOperationException(
                "The completed Scene result does not match its journal.");
        }

        return Copy(Items, result.UpdatedAt, result.Status);
    }

    private void EnsureOpenIndex(int index)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("The Scene attempt is complete.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Items.Length);
    }

    private void EnsureNextOpenIndex(int index)
    {
        if (Items.Take(index).Any(static item =>
                item.Status != SceneApplyJournalItemStatus.Terminal)
            || Items.Skip(index + 1).Any(static item =>
                item.Status != SceneApplyJournalItemStatus.Pending))
        {
            throw new InvalidOperationException(
                "Scene journal mutations must follow exact saved order.");
        }
    }

    private SceneApplyItemReason? FindBoundaryBefore(int index)
    {
        for (int earlier = 0; earlier < index; earlier++)
        {
            SceneApplyItemResult result = Items[earlier].Result
                ?? throw new InvalidOperationException(
                    "A preceding Scene journal item requires a terminal result.");
            if (result.Outcome == SceneApplyItemOutcome.Recovering)
            {
                return SceneApplyItemReason.NotAttemptedAfterRecovering;
            }

            if (result.Outcome == SceneApplyItemOutcome.NotAttempted
                && result.Reason == SceneApplyItemReason.Cancelled)
            {
                return SceneApplyItemReason.Cancelled;
            }
        }

        return null;
    }

    private SceneApplyJournalState Copy(
        ImmutableArray<SceneApplyJournalItem> items,
        DateTimeOffset? completedAt,
        SceneApplyOverallStatus? completedStatus) =>
        new(
            SceneId,
            SceneRevision,
            SceneDigest,
            PreviewFingerprint,
            ParentOperationId,
            ParentCorrelationId,
            AcceptedAt,
            items,
            completedAt,
            completedStatus);
}

public interface ISceneApplyJournal
{
    public ValueTask<SceneApplyJournalState?> LoadAsync(
        OperationId parentOperationId,
        CancellationToken cancellationToken);

    public ValueTask<SceneApplyJournalState> CreateAsync(
        SceneApplyPreview preview,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken);

    public ValueTask<SceneApplyJournalState> RecordItemStartedAsync(
        OperationId parentOperationId,
        int index,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    public ValueTask<SceneApplyJournalState> RecordItemOutcomeAsync(
        OperationId parentOperationId,
        SceneApplyItemResult result,
        CancellationToken cancellationToken);

    public ValueTask<SceneApplyJournalState> RecordCompletedAsync(
        OperationId parentOperationId,
        SceneApplyResult result,
        CancellationToken cancellationToken);
}

public sealed class InMemorySceneApplyJournal : ISceneApplyJournal
{
    private readonly Lock gate = new();
    private readonly Dictionary<OperationId, SceneApplyJournalState> states = [];

    public int EntryCount
    {
        get
        {
            lock (gate)
            {
                return states.Count;
            }
        }
    }

    public ValueTask<SceneApplyJournalState?> LoadAsync(
        OperationId parentOperationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentOperationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            states.TryGetValue(parentOperationId, out SceneApplyJournalState? state);
            return ValueTask.FromResult(state);
        }
    }

    public ValueTask<SceneApplyJournalState> CreateAsync(
        SceneApplyPreview preview,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        SceneApplyJournalState candidate = SceneApplyJournalState.Create(
            preview,
            acceptedAt);
        lock (gate)
        {
            if (states.TryGetValue(
                    preview.ParentOperationId,
                    out SceneApplyJournalState? existing))
            {
                if (!existing.Matches(preview))
                {
                    throw new InvalidOperationException(
                        "The Scene parent Operation ID is bound to another attempt.");
                }

                return ValueTask.FromResult(existing);
            }

            states.Add(preview.ParentOperationId, candidate);
            return ValueTask.FromResult(candidate);
        }
    }

    public ValueTask<SceneApplyJournalState> RecordItemStartedAsync(
        OperationId parentOperationId,
        int index,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) =>
        Update(
            parentOperationId,
            state => state.StartItem(index, startedAt),
            cancellationToken);

    public ValueTask<SceneApplyJournalState> RecordItemOutcomeAsync(
        OperationId parentOperationId,
        SceneApplyItemResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Update(
            parentOperationId,
            state => state.RecordOutcome(result),
            cancellationToken);
    }

    public ValueTask<SceneApplyJournalState> RecordCompletedAsync(
        OperationId parentOperationId,
        SceneApplyResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Update(
            parentOperationId,
            state => state.CompleteResult(result),
            cancellationToken);
    }

    private ValueTask<SceneApplyJournalState> Update(
        OperationId parentOperationId,
        Func<SceneApplyJournalState, SceneApplyJournalState> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentOperationId);
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!states.TryGetValue(
                    parentOperationId,
                    out SceneApplyJournalState? current))
            {
                throw new InvalidOperationException("The Scene attempt does not exist.");
            }

            SceneApplyJournalState candidate = update(current);
            states[parentOperationId] = candidate;
            return ValueTask.FromResult(candidate);
        }
    }
}

public sealed record SceneApplyExecutionResult
{
    private SceneApplyExecutionResult(
        SceneApplyApprovalStatus approvalStatus,
        SceneApplyResult? result)
    {
        ApprovalStatus = approvalStatus;
        Result = result;
    }

    public SceneApplyApprovalStatus ApprovalStatus { get; }

    public SceneApplyResult? Result { get; }

    internal static SceneApplyExecutionResult Rejected(
        SceneApplyApprovalStatus status)
    {
        if (status == SceneApplyApprovalStatus.Valid)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new SceneApplyExecutionResult(status, null);
    }

    internal static SceneApplyExecutionResult Accepted(SceneApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SceneApplyExecutionResult(
            SceneApplyApprovalStatus.Valid,
            result);
    }
}

public sealed class SceneApplyCoordinator
{
    private readonly IClock clock;
    private readonly ISceneApplyJournal journal;
    private readonly ISceneActivityOperationPort operationPort;

    public SceneApplyCoordinator(
        IClock clock,
        ISceneApplyJournal journal,
        ISceneActivityOperationPort operationPort)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.operationPort = operationPort
            ?? throw new ArgumentNullException(nameof(operationPort));
    }

    public async ValueTask<SceneApplyExecutionResult> ApplyAsync(
        ScenePlan scene,
        SceneApplyPreview preview,
        SceneApplyApproval approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(approval);
        SceneApplyJournalState? state = await journal.LoadAsync(
            preview.ParentOperationId,
            CancellationToken.None);
        if (state is null)
        {
            DateTimeOffset acceptedAt = clock.UtcNow.ToUniversalTime();
            SceneApplyApprovalStatus approvalStatus =
                SceneApplyApprovalVerifier.Validate(
                    scene,
                    preview,
                    approval,
                    acceptedAt);
            if (approvalStatus != SceneApplyApprovalStatus.Valid)
            {
                return SceneApplyExecutionResult.Rejected(approvalStatus);
            }

            state = await journal.CreateAsync(
                preview,
                acceptedAt,
                CancellationToken.None);
        }
        else
        {
            SceneApplyApprovalStatus replayStatus = ValidateReplay(
                state,
                preview,
                approval);
            if (replayStatus != SceneApplyApprovalStatus.Valid)
            {
                return SceneApplyExecutionResult.Rejected(replayStatus);
            }
        }

        var results = new List<SceneApplyItemResult>(preview.Items.Length);
        SceneApplyItemReason? boundaryReason = null;
        for (int index = 0; index < preview.Items.Length; index++)
        {
            SceneApplyJournalItem journalItem = state.Items[index];
            SceneApplyItemPreview item = journalItem.BoundItem;
            if (boundaryReason is not null)
            {
                SceneApplyItemResult remainder = journalItem.Result
                    ?? SceneApplyItemResult.NotAttempted(
                        item,
                        boundaryReason.Value,
                        clock.UtcNow);
                if (journalItem.Result is null)
                {
                    state = await journal.RecordItemOutcomeAsync(
                        preview.ParentOperationId,
                        remainder,
                        CancellationToken.None);
                }

                results.Add(remainder);
                continue;
            }

            if (journalItem.Status == SceneApplyJournalItemStatus.Terminal)
            {
                SceneApplyItemResult terminal = journalItem.Result
                    ?? throw new InvalidOperationException(
                        "A terminal Scene journal item requires a result.");
                results.Add(terminal);
                boundaryReason = BoundaryAfter(terminal);
                continue;
            }

            if (journalItem.Status == SceneApplyJournalItemStatus.Started)
            {
                SceneApplyItemResult recovering =
                    SceneApplyItemResult.RecoveringUnknown(
                        item,
                        FailureCode.OperationInProgress,
                        clock.UtcNow);
                state = await journal.RecordItemOutcomeAsync(
                    preview.ParentOperationId,
                    recovering,
                    CancellationToken.None);
                results.Add(recovering);
                boundaryReason = SceneApplyItemReason.NotAttemptedAfterRecovering;
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                SceneApplyItemResult cancelled = SceneApplyItemResult.NotAttempted(
                    item,
                    SceneApplyItemReason.Cancelled,
                    clock.UtcNow);
                state = await journal.RecordItemOutcomeAsync(
                    preview.ParentOperationId,
                    cancelled,
                    CancellationToken.None);
                results.Add(cancelled);
                boundaryReason = SceneApplyItemReason.Cancelled;
                continue;
            }

            if (item.Action is SceneApplyAction.Blocked
                or SceneApplyAction.NoChange)
            {
                SceneApplyItemResult previewOnly =
                    SceneApplyItemResult.FromPreviewOnly(item, clock.UtcNow);
                state = await journal.RecordItemOutcomeAsync(
                    preview.ParentOperationId,
                    previewOnly,
                    CancellationToken.None);
                results.Add(previewOnly);
                continue;
            }

            state = await journal.RecordItemStartedAsync(
                preview.ParentOperationId,
                index,
                clock.UtcNow,
                CancellationToken.None);
            SceneApplyItemResult operationResult;
            try
            {
                SceneActivityOperationResult operation =
                    await operationPort.ExecuteAsync(
                        SceneActivityPreparation.Create(
                            preview,
                            state.AcceptedAt,
                            item),
                        cancellationToken);
                operationResult = SceneApplyItemResult.FromOperation(
                    item,
                    operation.Receipt,
                    operation.UndoCapsule);
            }
            catch (OperationCanceledException)
            {
                operationResult = SceneApplyItemResult.RecoveringUnknown(
                    item,
                    FailureCode.AcknowledgementLost,
                    clock.UtcNow);
            }
            catch (Exception)
            {
                operationResult = SceneApplyItemResult.RecoveringUnknown(
                    item,
                    FailureCode.InternalFailure,
                    clock.UtcNow);
            }

            state = await journal.RecordItemOutcomeAsync(
                preview.ParentOperationId,
                operationResult,
                CancellationToken.None);
            results.Add(operationResult);
            boundaryReason = BoundaryAfter(operationResult);
        }

        DateTimeOffset updatedAt = state.CompletedAt
            ?? results
                .Select(static result => result.OccurredAt)
                .Append(clock.UtcNow.ToUniversalTime())
                .Max();
        SceneApplyResult result = SceneApplyResult.Create(
            preview,
            state.AcceptedAt,
            updatedAt,
            results);
        if (!state.IsCompleted)
        {
            await journal.RecordCompletedAsync(
                preview.ParentOperationId,
                result,
                CancellationToken.None);
        }

        return SceneApplyExecutionResult.Accepted(result);
    }

    private static SceneApplyApprovalStatus ValidateReplay(
        SceneApplyJournalState state,
        SceneApplyPreview preview,
        SceneApplyApproval approval)
    {
        if (!state.Matches(preview)
            || !string.Equals(
                approval.PreviewFingerprint,
                preview.Fingerprint,
                StringComparison.Ordinal))
        {
            return SceneApplyApprovalStatus.PreviewMismatch;
        }

        return approval.ReplaceConfirmations.SequenceEqual(
            preview.RequiredReplaceConfirmations)
            ? SceneApplyApprovalStatus.Valid
            : SceneApplyApprovalStatus.ReplaceConfirmationMismatch;
    }

    private static SceneApplyItemReason? BoundaryAfter(
        SceneApplyItemResult result) => result.Outcome switch
        {
            SceneApplyItemOutcome.Recovering =>
                SceneApplyItemReason.NotAttemptedAfterRecovering,
            SceneApplyItemOutcome.NotAttempted
                when result.Reason == SceneApplyItemReason.Cancelled =>
                    SceneApplyItemReason.Cancelled,
            _ => null,
        };
}
