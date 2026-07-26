using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface ISceneApplyStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class SceneApplyStatePersistenceException : IOException
{
    public SceneApplyStatePersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistentSceneApplyJournal : ISceneApplyJournal, IDisposable
{
    public const int MaximumPayloadBytes = 16 * 1024 * 1024;
    public const int MaximumAttemptCount = 32;

    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ISceneApplyStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private bool requiresReload;
    private Dictionary<OperationId, SceneApplyJournalState> states;

    private PersistentSceneApplyJournal(
        ISceneApplyStatePayloadStore payloadStore,
        IEnumerable<SceneApplyJournalState> states)
    {
        this.payloadStore = payloadStore;
        this.states = states.ToDictionary(
            static state => state.ParentOperationId);
    }

    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (snapshotGate)
            {
                return states.Count;
            }
        }
    }

    public static async ValueTask<PersistentSceneApplyJournal> OpenAsync(
        ISceneApplyStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentSceneApplyJournal(payloadStore, []);
        }

        try
        {
            return new PersistentSceneApplyJournal(
                payloadStore,
                SceneApplyStatePayloadCodec.Decode(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public ValueTask<SceneApplyJournalState?> LoadAsync(
        OperationId parentOperationId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(parentOperationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (snapshotGate)
        {
            states.TryGetValue(
                parentOperationId,
                out SceneApplyJournalState? state);
            return ValueTask.FromResult(state);
        }
    }

    public async ValueTask<SceneApplyJournalState> CreateAsync(
        SceneApplyPreview preview,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(preview);
        SceneApplyJournalState proposed = SceneApplyJournalState.Create(
            preview,
            acceptedAt);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<OperationId, SceneApplyJournalState> candidate = Snapshot();
            if (candidate.TryGetValue(
                    preview.ParentOperationId,
                    out SceneApplyJournalState? existing))
            {
                if (!existing.Matches(preview))
                {
                    throw new InvalidOperationException(
                        "The Scene parent Operation ID is bound to another attempt.");
                }

                return existing;
            }

            if (candidate.Count >= MaximumAttemptCount)
            {
                throw new InvalidOperationException(
                    $"The Scene apply journal cannot contain more than {MaximumAttemptCount} attempts.");
            }

            candidate.Add(preview.ParentOperationId, proposed);
            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            return proposed;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public ValueTask<SceneApplyJournalState> RecordItemStartedAsync(
        OperationId parentOperationId,
        int index,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            parentOperationId,
            state => state.StartItem(index, startedAt),
            cancellationToken);

    public ValueTask<SceneApplyJournalState> RecordItemOutcomeAsync(
        OperationId parentOperationId,
        SceneApplyItemResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return UpdateAsync(
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
        return UpdateAsync(
            parentOperationId,
            state => state.CompleteResult(result),
            cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        mutationGate.Dispose();
    }

    private async ValueTask<SceneApplyJournalState> UpdateAsync(
        OperationId parentOperationId,
        Func<SceneApplyJournalState, SceneApplyJournalState> update,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(parentOperationId);
        ArgumentNullException.ThrowIfNull(update);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<OperationId, SceneApplyJournalState> candidate = Snapshot();
            if (!candidate.TryGetValue(
                    parentOperationId,
                    out SceneApplyJournalState? current))
            {
                throw new InvalidOperationException(
                    "The Scene apply attempt does not exist.");
            }

            SceneApplyJournalState proposed = update(current);
            candidate[parentOperationId] = proposed;
            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            return proposed;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private Dictionary<OperationId, SceneApplyJournalState> Snapshot()
    {
        lock (snapshotGate)
        {
            return new Dictionary<OperationId, SceneApplyJournalState>(states);
        }
    }

    private async ValueTask SaveAndPublishAsync(
        Dictionary<OperationId, SceneApplyJournalState> candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = SceneApplyStatePayloadCodec.Encode(candidate.Values);
        try
        {
            await payloadStore.SaveAsync(payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            requiresReload = true;
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new SceneApplyStatePersistenceException(
                "The durable Scene apply state could not be saved.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            states = candidate;
        }
    }

    private void ThrowIfReloadRequired()
    {
        if (requiresReload)
        {
            throw new SceneApplyStatePersistenceException(
                "The Scene apply journal must be reopened after an ambiguous save failure.",
                new IOException("The prior durable save outcome is unknown."));
        }
    }
}

internal static class SceneApplyStatePayloadCodec
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowDuplicateProperties = false,
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<SceneApplyJournalState> Decode(
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty
            || payload.Length > PersistentSceneApplyJournal.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"A Scene apply state payload must contain 1 to {PersistentSceneApplyJournal.MaximumPayloadBytes} bytes.");
        }

        try
        {
            StateDto? state = JsonSerializer.Deserialize<StateDto>(
                payload,
                SerializerOptions);
            if (state is null
                || state.FormatVersion != CurrentFormatVersion
                || state.Attempts is null
                || state.Attempts.Length is < 1
                    or > PersistentSceneApplyJournal.MaximumAttemptCount)
            {
                throw new InvalidDataException(
                    "The Scene apply state payload has an unsupported or incomplete envelope.");
            }

            var decoded = new List<SceneApplyJournalState>(
                state.Attempts.Length);
            string? previousOperationId = null;
            foreach (AttemptDto encoded in state.Attempts)
            {
                SceneApplyJournalState attempt = DecodeAttempt(encoded);
                string operationId = attempt.ParentOperationId.ToString();
                if (previousOperationId is not null
                    && StringComparer.Ordinal.Compare(
                        previousOperationId,
                        operationId) >= 0)
                {
                    throw new InvalidDataException(
                        "Scene apply attempts are duplicated or not canonically ordered.");
                }

                previousOperationId = operationId;
                decoded.Add(attempt);
            }

            return decoded;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or InvalidOperationException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Scene apply state payload is malformed.",
                exception);
        }
    }

    public static byte[] Encode(IEnumerable<SceneApplyJournalState> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        SceneApplyJournalState[] ordered = attempts
            .OrderBy(
                static attempt => attempt.ParentOperationId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length is < 1
                or > PersistentSceneApplyJournal.MaximumAttemptCount
            || ordered.Select(static attempt => attempt.ParentOperationId)
                .Distinct()
                .Count() != ordered.Length)
        {
            throw new InvalidDataException(
                "Scene apply attempts exceed bounds or contain duplicate parent Operation IDs.");
        }

        var state = new StateDto(
            CurrentFormatVersion,
            ordered.Select(EncodeAttempt).ToArray());
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            state,
            SerializerOptions);
        if (payload.Length > PersistentSceneApplyJournal.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                $"A Scene apply state payload cannot exceed {PersistentSceneApplyJournal.MaximumPayloadBytes} bytes.");
        }

        return payload;
    }

    private static AttemptDto EncodeAttempt(SceneApplyJournalState attempt) =>
        new(
            attempt.SceneId.ToString(),
            attempt.SceneRevision,
            attempt.SceneDigest,
            attempt.PreviewFingerprint,
            attempt.ParentOperationId.ToString(),
            attempt.ParentCorrelationId.ToString(),
            FormatTimestamp(attempt.AcceptedAt),
            attempt.Items.Select(EncodeItem).ToArray(),
            attempt.CompletedAt is null
                ? null
                : FormatTimestamp(attempt.CompletedAt.Value),
            attempt.CompletedStatus is null
                ? null
                : checked((int)attempt.CompletedStatus.Value));

    private static SceneApplyJournalState DecodeAttempt(AttemptDto encoded)
    {
        if (encoded is null
            || encoded.Items is null
            || encoded.Items.Length is < 1 or > ScenePlan.MaximumActivities)
        {
            throw new InvalidDataException(
                "A Scene apply attempt is null, incomplete, or out of bounds.");
        }

        SceneApplyJournalItem[] items = encoded.Items
            .Select(DecodeItem)
            .ToArray();
        SceneApplyOverallStatus? completedStatus = encoded.CompletedStatus is null
            ? null
            : DecodeOverallStatus(encoded.CompletedStatus.Value);
        return SceneApplyJournalState.Restore(
            SceneId.Parse(encoded.SceneId),
            encoded.SceneRevision,
            encoded.SceneDigest,
            encoded.PreviewFingerprint,
            OperationId.Parse(encoded.ParentOperationId),
            CorrelationId.Parse(encoded.ParentCorrelationId),
            ParseTimestamp(encoded.AcceptedAt, "acceptedAt"),
            items,
            encoded.CompletedAt is null
                ? null
                : ParseTimestamp(encoded.CompletedAt, "completedAt"),
            completedStatus);
    }

    private static ItemDto EncodeItem(SceneApplyJournalItem item) =>
        new(
            item.Index,
            item.ChildOperationId.ToString(),
            item.ChildCorrelationId.ToString(),
            EncodeBoundItem(item.BoundItem),
            checked((int)item.Status),
            item.StartedAt is null
                ? null
                : FormatTimestamp(item.StartedAt.Value),
            item.Result is null ? null : EncodeResult(item.Result));

    private static SceneApplyJournalItem DecodeItem(ItemDto encoded)
    {
        if (encoded is null || encoded.BoundItem is null)
        {
            throw new InvalidDataException(
                "A Scene apply journal item cannot be null or incomplete.");
        }

        SceneApplyItemPreview boundItem = DecodeBoundItem(encoded.BoundItem);
        if (encoded.Index != boundItem.Index
            || OperationId.Parse(encoded.ChildOperationId)
                != boundItem.ChildOperationId
            || CorrelationId.Parse(encoded.ChildCorrelationId)
                != boundItem.ChildCorrelationId)
        {
            throw new InvalidDataException(
                "A Scene apply journal item identity does not match its bound preview.");
        }

        SceneApplyJournalItemStatus status = DecodeJournalItemStatus(
            encoded.Status);
        SceneApplyItemResult? result = encoded.Result is null
            ? null
            : DecodeResult(encoded.Result, boundItem);
        return SceneApplyJournalItem.Restore(
            boundItem,
            status,
            encoded.StartedAt is null
                ? null
                : ParseTimestamp(encoded.StartedAt, "startedAt"),
            result);
    }

    private static BoundItemDto EncodeBoundItem(SceneApplyItemPreview item) =>
        new(
            item.Index,
            item.ActivityId.ToString(),
            EncodePlacement(item.Destination),
            checked((int)item.SourceDisposition),
            checked((int)item.ConflictPolicy),
            item.Source is null ? null : EncodeSelection(item.Source),
            item.SourceLookup is null ? null : EncodeLookup(item.SourceLookup),
            EncodeOccupancy(item.Occupancy),
            item.ChildOperationId.ToString(),
            item.ChildCorrelationId.ToString(),
            checked((int)item.Action),
            checked((int)item.Reason));

    private static SceneApplyItemPreview DecodeBoundItem(BoundItemDto encoded)
    {
        if (encoded is null
            || encoded.Destination is null
            || encoded.Occupancy is null)
        {
            throw new InvalidDataException(
                "A bound Scene preview item cannot be null or incomplete.");
        }

        ActivityId activityId = ActivityId.Parse(encoded.ActivityId);
        SceneSourceDisposition sourceDisposition =
            DecodeSourceDisposition(encoded.SourceDisposition);
        SceneConflictPolicy conflictPolicy =
            DecodeConflictPolicy(encoded.ConflictPolicy);
        SceneActivityPlan plan = SceneActivityPlan.Place(
            activityId,
            DecodePlacement(encoded.Destination),
            sourceDisposition,
            conflictPolicy);
        SceneSourceSelection? source = encoded.Source is null
            ? null
            : DecodeSelection(encoded.Source);
        SceneSourceLookup? lookup = encoded.SourceLookup is null
            ? null
            : DecodeLookup(encoded.SourceLookup);
        SceneSlotOccupancy occupancy = DecodeOccupancy(encoded.Occupancy);
        OperationId operationId = OperationId.Parse(encoded.ChildOperationId);
        CorrelationId correlationId =
            CorrelationId.Parse(encoded.ChildCorrelationId);
        SceneApplyAction action = DecodeAction(encoded.Action);
        SceneApplyItemReason reason = DecodeReason(encoded.Reason);
        SceneApplyItemPreview restored = action switch
        {
            SceneApplyAction.Blocked when source is null =>
                SceneApplyItemPreview.BlockedBySourceLookup(
                    plan,
                    lookup ?? throw new InvalidDataException(
                        "A source-blocked Scene item requires lookup evidence."),
                    operationId,
                    correlationId),
            SceneApplyAction.Blocked
                when occupancy.Kind == SceneSlotOccupancyKind.NotInspected =>
                    SceneApplyItemPreview.BlockedBeforeOccupancy(
                        plan,
                        source ?? throw new InvalidDataException(
                            "A pre-inspection Scene blocker requires an exact source."),
                        reason,
                        operationId,
                        correlationId),
            SceneApplyAction.Blocked =>
                SceneApplyItemPreview.BlockedByOccupancy(
                    plan,
                    source ?? throw new InvalidDataException(
                        "An occupancy-blocked Scene item requires an exact source."),
                    occupancy,
                    operationId,
                    correlationId),
            SceneApplyAction.NoChange =>
                SceneApplyItemPreview.NoChange(
                    plan,
                    source ?? throw new InvalidDataException(
                        "A No Change Scene item requires an exact source."),
                    operationId,
                    correlationId),
            SceneApplyAction.Handoff or SceneApplyAction.Move =>
                SceneApplyItemPreview.TransferToEmpty(
                    plan,
                    source ?? throw new InvalidDataException(
                        "A transfer Scene item requires an exact source."),
                    operationId,
                    correlationId),
            SceneApplyAction.Replace =>
                SceneApplyItemPreview.Replace(
                    plan,
                    source ?? throw new InvalidDataException(
                        "A Replace Scene item requires an exact source."),
                    occupancy.Target ?? throw new InvalidDataException(
                        "A Replace Scene item requires an exact target."),
                    operationId,
                    correlationId),
            _ => throw new InvalidDataException(
                "A bound Scene preview item has an unknown action."),
        };
        if (encoded.Index != restored.Index
            || restored.Action != action
            || restored.Reason != reason
            || restored.Occupancy != occupancy
            || (source is null) != (restored.Source is null)
            || (lookup is null) != (restored.SourceLookup is null))
        {
            throw new InvalidDataException(
                "A bound Scene preview item does not match its derived policy result.");
        }

        return restored;
    }

    private static SourceSelectionDto EncodeSelection(
        SceneSourceSelection source) =>
        new(
            source.Index,
            source.ActivityId.ToString(),
            source.Revision,
            source.DescriptorDigest,
            source.Kind.Value,
            EncodePlacement(source.Placement));

    private static SceneSourceSelection DecodeSelection(
        SourceSelectionDto encoded)
    {
        if (encoded is null || encoded.Placement is null)
        {
            throw new InvalidDataException(
                "A Scene source selection cannot be null or incomplete.");
        }

        return SceneSourceSelection.Create(
            encoded.Index,
            ActivityId.Parse(encoded.ActivityId),
            encoded.Revision,
            encoded.DescriptorDigest,
            ActivityKind.Parse(encoded.Kind),
            DecodePlacement(encoded.Placement));
    }

    private static SourceLookupDto EncodeLookup(SceneSourceLookup lookup) =>
        new(
            lookup.Index,
            lookup.ActivityId.ToString(),
            checked((int)lookup.Status),
            checked((int)lookup.Reason),
            lookup.Candidates.Select(EncodeSelection).ToArray());

    private static SceneSourceLookup DecodeLookup(SourceLookupDto encoded)
    {
        if (encoded is null || encoded.Candidates is null)
        {
            throw new InvalidDataException(
                "A Scene source lookup cannot be null or incomplete.");
        }

        ActivityId activityId = ActivityId.Parse(encoded.ActivityId);
        SceneSourceLookupStatus status = DecodeLookupStatus(encoded.Status);
        SceneApplyItemReason reason = DecodeReason(encoded.Reason);
        SceneSourceSelection[] candidates = encoded.Candidates
            .Select(DecodeSelection)
            .ToArray();
        SceneSourceLookup restored = status switch
        {
            SceneSourceLookupStatus.NotFound
            or SceneSourceLookupStatus.UniqueSource
            or SceneSourceLookupStatus.SelectionRequired =>
                SceneSourceLookup.FromObservation(
                    encoded.Index,
                    activityId,
                    candidates,
                    isComplete: true),
            SceneSourceLookupStatus.Unavailable =>
                candidates.Length == 0
                    ? SceneSourceLookup.Unavailable(
                        encoded.Index,
                        activityId,
                        reason)
                    : throw new InvalidDataException(
                        "An unavailable Scene source lookup cannot disclose candidates."),
            _ => throw new InvalidDataException(
                "A Scene source lookup has an unknown status."),
        };
        if (restored.Status != status
            || restored.Reason != reason
            || !restored.Candidates.SequenceEqual(candidates))
        {
            throw new InvalidDataException(
                "A Scene source lookup is not canonical or internally consistent.");
        }

        return restored;
    }

    private static OccupancyDto EncodeOccupancy(SceneSlotOccupancy occupancy) =>
        new(
            checked((int)occupancy.Kind),
            occupancy.HasDurableUndoAvailability,
            occupancy.Target is null ? null : EncodeTarget(occupancy.Target));

    private static SceneSlotOccupancy DecodeOccupancy(OccupancyDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException(
                "A Scene exact-slot occupancy cannot be null.");
        }

        SceneSlotOccupancyKind kind = DecodeOccupancyKind(encoded.Kind);
        return kind switch
        {
            SceneSlotOccupancyKind.NotInspected
                when !encoded.HasDurableUndoAvailability
                    && encoded.Target is null =>
                SceneSlotOccupancy.NotInspected,
            SceneSlotOccupancyKind.Empty
                when !encoded.HasDurableUndoAvailability
                    && encoded.Target is null =>
                SceneSlotOccupancy.Empty,
            SceneSlotOccupancyKind.Opaque
                when !encoded.HasDurableUndoAvailability
                    && encoded.Target is null =>
                SceneSlotOccupancy.Opaque,
            SceneSlotOccupancyKind.Ambiguous
                when !encoded.HasDurableUndoAvailability
                    && encoded.Target is null =>
                SceneSlotOccupancy.Ambiguous,
            SceneSlotOccupancyKind.EligibleConflict
                when encoded.Target is not null =>
                SceneSlotOccupancy.EligibleConflict(
                    DecodeTarget(encoded.Target),
                    encoded.HasDurableUndoAvailability),
            _ => throw new InvalidDataException(
                "A Scene exact-slot occupancy has an invalid evidence shape."),
        };
    }

    private static ReplaceTargetDto EncodeTarget(
        SceneReplaceTargetSnapshot target) =>
        new(
            target.ActivityId.ToString(),
            target.Revision,
            target.DescriptorDigest,
            target.Kind.Value,
            EncodePlacement(target.Placement));

    private static SceneReplaceTargetSnapshot DecodeTarget(
        ReplaceTargetDto encoded)
    {
        if (encoded is null || encoded.Placement is null)
        {
            throw new InvalidDataException(
                "A Scene Replace target cannot be null or incomplete.");
        }

        return SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse(encoded.ActivityId),
            encoded.Revision,
            encoded.DescriptorDigest,
            ActivityKind.Parse(encoded.Kind),
            DecodePlacement(encoded.Placement));
    }

    private static PlacementDto EncodePlacement(ActivityPlacement placement) =>
        new(placement.DeviceId.ToString(), placement.Slot);

    private static ActivityPlacement DecodePlacement(PlacementDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException(
                "A Scene Activity placement cannot be null.");
        }

        return ActivityPlacement.On(
            DeviceId.Parse(encoded.DeviceId),
            encoded.Slot);
    }

    private static ResultDto EncodeResult(SceneApplyItemResult result) =>
        new(
            checked((int)result.Outcome),
            checked((int)result.Reason),
            checked((int)result.FailureCode),
            FormatTimestamp(result.OccurredAt),
            result.UndoCapsule is null
                ? null
                : EncodeUndo(result.UndoCapsule));

    private static SceneApplyItemResult DecodeResult(
        ResultDto encoded,
        SceneApplyItemPreview item)
    {
        if (encoded is null)
        {
            throw new InvalidDataException(
                "A Scene apply item result cannot be null.");
        }

        SceneApplyItemOutcome outcome = DecodeOutcome(encoded.Outcome);
        SceneApplyItemReason reason = DecodeReason(encoded.Reason);
        FailureCode failureCode = DecodeFailureCode(encoded.FailureCode);
        DateTimeOffset occurredAt = ParseTimestamp(
            encoded.OccurredAt,
            "occurredAt");
        UndoCapsuleReference? undo = encoded.Undo is null
            ? null
            : DecodeUndo(encoded.Undo, item.Destination.DeviceId);
        SceneApplyItemResult restored = outcome switch
        {
            SceneApplyItemOutcome.Blocked or SceneApplyItemOutcome.NoChange =>
                SceneApplyItemResult.FromPreviewOnly(item, occurredAt),
            SceneApplyItemOutcome.NotAttempted =>
                SceneApplyItemResult.NotAttempted(item, reason, occurredAt),
            SceneApplyItemOutcome.Committed
            or SceneApplyItemOutcome.CommittedWithWarning
            or SceneApplyItemOutcome.Rejected
            or SceneApplyItemOutcome.Failed
            or SceneApplyItemOutcome.Recovering =>
                RestoreOperationResult(
                    item,
                    outcome,
                    failureCode,
                    occurredAt,
                    undo),
            _ => throw new InvalidDataException(
                "A Scene apply item result has an unknown outcome."),
        };
        if (restored.Outcome != outcome
            || restored.Reason != reason
            || restored.FailureCode != failureCode
            || restored.UndoCapsule != undo)
        {
            throw new InvalidDataException(
                "A Scene apply item result does not match its derived evidence.");
        }

        return restored;
    }

    private static SceneApplyItemResult RestoreOperationResult(
        SceneApplyItemPreview item,
        SceneApplyItemOutcome outcome,
        FailureCode failureCode,
        DateTimeOffset occurredAt,
        UndoCapsuleReference? undo)
    {
        SceneSourceSelection source = item.Source
            ?? throw new InvalidDataException(
                "An executed Scene result requires exact source evidence.");
        OperationKind kind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new InvalidDataException(
                "Only an executable Scene item can contain an operation result."),
        };
        OperationStatus status = outcome switch
        {
            SceneApplyItemOutcome.Committed => OperationStatus.Committed,
            SceneApplyItemOutcome.CommittedWithWarning =>
                OperationStatus.CommittedWithWarning,
            SceneApplyItemOutcome.Rejected => OperationStatus.Rejected,
            SceneApplyItemOutcome.Failed => OperationStatus.Failed,
            SceneApplyItemOutcome.Recovering => OperationStatus.Recovering,
            _ => throw new InvalidDataException(
                "A Scene operation result has an invalid outcome."),
        };
        OperationReceipt receipt = OperationReceipt.FromRecordedResult(
            item.ChildOperationId,
            item.ChildCorrelationId,
            kind,
            status,
            source.DeviceId,
            item.Destination.DeviceId,
            item.ActivityId,
            source.Kind,
            source.DescriptorDigest,
            occurredAt,
            failureCode);
        return SceneApplyItemResult.FromOperation(item, receipt, undo);
    }

    private static UndoDto EncodeUndo(UndoCapsuleReference undo) =>
        new(
            undo.Id.ToString(),
            undo.OperationId.ToString(),
            undo.CorrelationId.ToString(),
            undo.TargetActivityId.ToString(),
            undo.ExpectedTargetRevision,
            undo.TargetDescriptorDigest,
            undo.IncomingActivityId.ToString(),
            undo.IncomingDescriptorDigest,
            FormatTimestamp(undo.ExpiresAt));

    private static UndoCapsuleReference DecodeUndo(
        UndoDto encoded,
        DeviceId targetDeviceId)
    {
        if (encoded is null)
        {
            throw new InvalidDataException(
                "A Scene Undo Capsule reference cannot be null.");
        }

        return new UndoCapsuleReference(
            UndoCapsuleId.Parse(encoded.Id),
            OperationId.Parse(encoded.OperationId),
            CorrelationId.Parse(encoded.CorrelationId),
            targetDeviceId,
            ActivityId.Parse(encoded.TargetActivityId),
            encoded.ExpectedTargetRevision,
            SceneApplyBinding.ValidateDigest(
                encoded.TargetDescriptorDigest,
                nameof(encoded.TargetDescriptorDigest)),
            ActivityId.Parse(encoded.IncomingActivityId),
            SceneApplyBinding.ValidateDigest(
                encoded.IncomingDescriptorDigest,
                nameof(encoded.IncomingDescriptorDigest)),
            ParseTimestamp(encoded.ExpiresAt, "undoExpiresAt"));
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "Scene apply state timestamps must be canonical UTC.");
        }

        return timestamp.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string value, string field)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp)
            || timestamp.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"The Scene apply state {field} timestamp is not canonical UTC.");
        }

        return timestamp;
    }

    private static SceneSourceDisposition DecodeSourceDisposition(int value) =>
        Enum.IsDefined(typeof(SceneSourceDisposition), value)
            ? (SceneSourceDisposition)value
            : throw new InvalidDataException(
                "A Scene apply state source disposition is unknown.");

    private static SceneConflictPolicy DecodeConflictPolicy(int value) =>
        Enum.IsDefined(typeof(SceneConflictPolicy), value)
            ? (SceneConflictPolicy)value
            : throw new InvalidDataException(
                "A Scene apply state conflict policy is unknown.");

    private static SceneApplyAction DecodeAction(int value) =>
        Enum.IsDefined(typeof(SceneApplyAction), value)
            ? (SceneApplyAction)value
            : throw new InvalidDataException(
                "A Scene apply state action is unknown.");

    private static SceneApplyItemReason DecodeReason(int value) =>
        Enum.IsDefined(typeof(SceneApplyItemReason), value)
            ? (SceneApplyItemReason)value
            : throw new InvalidDataException(
                "A Scene apply state reason is unknown.");

    private static SceneSourceLookupStatus DecodeLookupStatus(int value) =>
        Enum.IsDefined(typeof(SceneSourceLookupStatus), value)
            ? (SceneSourceLookupStatus)value
            : throw new InvalidDataException(
                "A Scene apply state source lookup status is unknown.");

    private static SceneSlotOccupancyKind DecodeOccupancyKind(int value) =>
        Enum.IsDefined(typeof(SceneSlotOccupancyKind), value)
            ? (SceneSlotOccupancyKind)value
            : throw new InvalidDataException(
                "A Scene apply state occupancy status is unknown.");

    private static SceneApplyJournalItemStatus DecodeJournalItemStatus(
        int value) =>
        Enum.IsDefined(typeof(SceneApplyJournalItemStatus), value)
            ? (SceneApplyJournalItemStatus)value
            : throw new InvalidDataException(
                "A Scene apply journal item status is unknown.");

    private static SceneApplyItemOutcome DecodeOutcome(int value) =>
        Enum.IsDefined(typeof(SceneApplyItemOutcome), value)
            ? (SceneApplyItemOutcome)value
            : throw new InvalidDataException(
                "A Scene apply item outcome is unknown.");

    private static FailureCode DecodeFailureCode(int value) =>
        Enum.IsDefined(typeof(FailureCode), value)
            ? (FailureCode)value
            : throw new InvalidDataException(
                "A Scene apply item failure code is unknown.");

    private static SceneApplyOverallStatus DecodeOverallStatus(int value) =>
        Enum.IsDefined(typeof(SceneApplyOverallStatus), value)
            ? (SceneApplyOverallStatus)value
            : throw new InvalidDataException(
                "A Scene apply overall status is unknown.");

    private sealed record StateDto(
        int FormatVersion,
        AttemptDto[] Attempts);

    private sealed record AttemptDto(
        string SceneId,
        long SceneRevision,
        string SceneDigest,
        string PreviewFingerprint,
        string ParentOperationId,
        string ParentCorrelationId,
        string AcceptedAt,
        ItemDto[] Items,
        string? CompletedAt,
        int? CompletedStatus);

    private sealed record ItemDto(
        int Index,
        string ChildOperationId,
        string ChildCorrelationId,
        BoundItemDto BoundItem,
        int Status,
        string? StartedAt,
        ResultDto? Result);

    private sealed record BoundItemDto(
        int Index,
        string ActivityId,
        PlacementDto Destination,
        int SourceDisposition,
        int ConflictPolicy,
        SourceSelectionDto? Source,
        SourceLookupDto? SourceLookup,
        OccupancyDto Occupancy,
        string ChildOperationId,
        string ChildCorrelationId,
        int Action,
        int Reason);

    private sealed record SourceSelectionDto(
        int Index,
        string ActivityId,
        long Revision,
        string DescriptorDigest,
        string Kind,
        PlacementDto Placement);

    private sealed record SourceLookupDto(
        int Index,
        string ActivityId,
        int Status,
        int Reason,
        SourceSelectionDto[] Candidates);

    private sealed record OccupancyDto(
        int Kind,
        bool HasDurableUndoAvailability,
        ReplaceTargetDto? Target);

    private sealed record ReplaceTargetDto(
        string ActivityId,
        long Revision,
        string DescriptorDigest,
        string Kind,
        PlacementDto Placement);

    private sealed record PlacementDto(
        string DeviceId,
        string Slot);

    private sealed record ResultDto(
        int Outcome,
        int Reason,
        int FailureCode,
        string OccurredAt,
        UndoDto? Undo);

    private sealed record UndoDto(
        string Id,
        string OperationId,
        string CorrelationId,
        string TargetActivityId,
        long ExpectedTargetRevision,
        string TargetDescriptorDigest,
        string IncomingActivityId,
        string IncomingDescriptorDigest,
        string ExpiresAt);
}
