using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SwapEndpointRecord
{
    private SwapEndpointRecord(
        DeviceId deviceId,
        SwapReservation? reservation,
        SwapDecision? decision)
    {
        DeviceId = deviceId;
        Reservation = reservation;
        Decision = decision;
        OperationId = reservation?.OperationId
            ?? decision?.OperationId
            ?? throw new ArgumentException(
                "A swap endpoint record requires a reservation or decision.");
    }

    public DeviceId DeviceId { get; }

    public SwapDecision? Decision { get; }

    public OperationId OperationId { get; }

    public SwapReservation? Reservation { get; }

    public static SwapEndpointRecord Prepared(
        DeviceId deviceId,
        SwapReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.Phase != SwapReservationPhase.Prepared
            || reservation.OriginalActivity.Placement.DeviceId != deviceId)
        {
            throw new ArgumentException(
                "A prepared endpoint record must belong to its local Device.",
                nameof(reservation));
        }

        return new SwapEndpointRecord(deviceId, reservation, null);
    }

    public static SwapEndpointRecord AbortTombstone(
        DeviceId deviceId,
        SwapDecision decision)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Outcome != SwapDecisionOutcome.Abort
            || !decision.TryGetReservationToken(deviceId, out _))
        {
            throw new ArgumentException(
                "A swap endpoint tombstone requires a Device-bound Abort decision.",
                nameof(decision));
        }

        return new SwapEndpointRecord(deviceId, null, decision);
    }

    internal static SwapEndpointRecord Restore(
        DeviceId deviceId,
        SwapReservation? reservation,
        SwapDecision? decision)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        if (reservation is null)
        {
            return AbortTombstone(
                deviceId,
                decision ?? throw new InvalidDataException(
                    "A decision-only endpoint record cannot omit its decision."));
        }

        if (reservation.OriginalActivity.Placement.DeviceId != deviceId)
        {
            throw new InvalidDataException(
                "A swap endpoint reservation belongs to another Device.");
        }

        if (decision is null)
        {
            if (reservation.Phase != SwapReservationPhase.Prepared)
            {
                throw new InvalidDataException(
                    "A terminal endpoint reservation cannot omit its decision.");
            }

            return new SwapEndpointRecord(deviceId, reservation, null);
        }

        if (reservation.Phase == SwapReservationPhase.Prepared
            || reservation.DecisionDigest is null
            || !StringComparer.Ordinal.Equals(
                reservation.DecisionDigest,
                decision.Digest))
        {
            throw new InvalidDataException(
                "A terminal endpoint reservation does not match its decision.");
        }

        return new SwapEndpointRecord(deviceId, reservation, decision);
    }

    public SwapEndpointRecord WithDecision(SwapDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.OperationId != OperationId
            || !decision.TryGetReservationToken(DeviceId, out _))
        {
            throw new InvalidOperationException(
                "A swap decision does not bind this endpoint record.");
        }

        if (Decision is not null)
        {
            return StringComparer.Ordinal.Equals(Decision.Digest, decision.Digest)
                ? this
                : throw new InvalidOperationException(
                    "A terminal endpoint record cannot accept another decision.");
        }

        if (Reservation is null)
        {
            return decision.Outcome == SwapDecisionOutcome.Abort
                ? AbortTombstone(DeviceId, decision)
                : throw new InvalidOperationException(
                    "A Commit decision requires a prepared endpoint reservation.");
        }

        return new SwapEndpointRecord(
            DeviceId,
            Reservation.ApplyDecision(decision),
            decision);
    }
}

public enum SwapEndpointWriteStatus
{
    Stored,
    Replayed,
    Conflict,
    CapacityExceeded,
}

public sealed record SwapEndpointWriteResult(
    SwapEndpointWriteStatus Status,
    SwapEndpointRecord? Record);

public interface ISwapEndpointJournal
{
    public DeviceId DeviceId { get; }

    public IReadOnlyList<SwapEndpointRecord> Snapshot();

    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapEndpointRecord? record);

    public ValueTask<SwapEndpointWriteResult> TryPrepareAsync(
        SwapReservation reservation,
        CancellationToken cancellationToken = default);

    public ValueTask<SwapEndpointWriteResult> TryRecordDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken = default);
}

public interface ISwapEndpointStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class SwapEndpointStatePersistenceException : IOException
{
    public SwapEndpointStatePersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistentSwapEndpointJournal : ISwapEndpointJournal, IDisposable
{
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;
    public const int MaximumRecordCount = 32;

    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ISwapEndpointStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private Dictionary<OperationId, SwapEndpointRecord> records;
    private bool requiresReload;

    private PersistentSwapEndpointJournal(
        DeviceId deviceId,
        ISwapEndpointStatePayloadStore payloadStore,
        IEnumerable<SwapEndpointRecord> records)
    {
        DeviceId = deviceId;
        this.payloadStore = payloadStore;
        this.records = records.ToDictionary(static record => record.OperationId);
    }

    public int Count
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (snapshotGate)
            {
                return records.Count;
            }
        }
    }

    public DeviceId DeviceId { get; }

    public static async ValueTask<PersistentSwapEndpointJournal> OpenAsync(
        DeviceId deviceId,
        ISwapEndpointStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentSwapEndpointJournal(deviceId, payloadStore, []);
        }

        try
        {
            return new PersistentSwapEndpointJournal(
                deviceId,
                payloadStore,
                SwapEndpointPayloadCodec.Decode(payload, deviceId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public IReadOnlyList<SwapEndpointRecord> Snapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (snapshotGate)
        {
            return records.Values
                .OrderBy(
                    static record => record.OperationId.ToString(),
                    StringComparer.Ordinal)
                .ToArray();
        }
    }

    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapEndpointRecord? record)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(operationId);
        lock (snapshotGate)
        {
            return records.TryGetValue(operationId, out record);
        }
    }

    public async ValueTask<SwapEndpointWriteResult> TryPrepareAsync(
        SwapReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SwapEndpointRecord proposed = SwapEndpointRecord.Prepared(DeviceId, reservation);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<OperationId, SwapEndpointRecord> candidate = CopyRecords();
            if (candidate.TryGetValue(proposed.OperationId, out SwapEndpointRecord? existing))
            {
                bool replay = existing.Decision is null
                    && existing.Reservation is not null
                    && existing.Reservation.Token == reservation.Token
                    && StringComparer.Ordinal.Equals(
                        existing.Reservation.RequestDigest,
                        reservation.RequestDigest);
                return new SwapEndpointWriteResult(
                    replay
                        ? SwapEndpointWriteStatus.Replayed
                        : SwapEndpointWriteStatus.Conflict,
                    existing);
            }

            if (candidate.Count >= MaximumRecordCount)
            {
                return new SwapEndpointWriteResult(
                    SwapEndpointWriteStatus.CapacityExceeded,
                    null);
            }

            candidate.Add(proposed.OperationId, proposed);
            await SaveAndPublishAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new SwapEndpointWriteResult(SwapEndpointWriteStatus.Stored, proposed);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<SwapEndpointWriteResult> TryRecordDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.TryGetReservationToken(DeviceId, out _))
        {
            return new SwapEndpointWriteResult(SwapEndpointWriteStatus.Conflict, null);
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<OperationId, SwapEndpointRecord> candidate = CopyRecords();
            if (!candidate.TryGetValue(
                    decision.OperationId,
                    out SwapEndpointRecord? existing))
            {
                if (decision.Outcome != SwapDecisionOutcome.Abort)
                {
                    return new SwapEndpointWriteResult(
                        SwapEndpointWriteStatus.Conflict,
                        null);
                }

                if (candidate.Count >= MaximumRecordCount)
                {
                    return new SwapEndpointWriteResult(
                        SwapEndpointWriteStatus.CapacityExceeded,
                        null);
                }

                SwapEndpointRecord tombstone = SwapEndpointRecord.AbortTombstone(
                    DeviceId,
                    decision);
                candidate.Add(decision.OperationId, tombstone);
                await SaveAndPublishAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);
                return new SwapEndpointWriteResult(
                    SwapEndpointWriteStatus.Stored,
                    tombstone);
            }

            if (existing.Decision is not null)
            {
                return new SwapEndpointWriteResult(
                    StringComparer.Ordinal.Equals(existing.Decision.Digest, decision.Digest)
                        ? SwapEndpointWriteStatus.Replayed
                        : SwapEndpointWriteStatus.Conflict,
                    existing);
            }

            SwapEndpointRecord decided;
            try
            {
                decided = existing.WithDecision(decision);
            }
            catch (InvalidOperationException)
            {
                return new SwapEndpointWriteResult(
                    SwapEndpointWriteStatus.Conflict,
                    existing);
            }

            candidate[decision.OperationId] = decided;
            await SaveAndPublishAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new SwapEndpointWriteResult(SwapEndpointWriteStatus.Stored, decided);
        }
        finally
        {
            mutationGate.Release();
        }
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

    private Dictionary<OperationId, SwapEndpointRecord> CopyRecords()
    {
        lock (snapshotGate)
        {
            return new Dictionary<OperationId, SwapEndpointRecord>(records);
        }
    }

    private async ValueTask SaveAndPublishAsync(
        Dictionary<OperationId, SwapEndpointRecord> candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = SwapEndpointPayloadCodec.Encode(DeviceId, candidate.Values);
        try
        {
            await payloadStore.SaveAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            requiresReload = true;
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new SwapEndpointStatePersistenceException(
                "The durable swap endpoint state could not be saved.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            records = candidate;
        }
    }

    private void ThrowIfReloadRequired()
    {
        if (requiresReload)
        {
            throw new SwapEndpointStatePersistenceException(
                "The swap endpoint journal must be reopened after an ambiguous save failure.",
                new IOException("The prior durable endpoint save outcome is unknown."));
        }
    }
}

public sealed record SwapEndpointRecoveryResult(
    OperationId OperationId,
    OperationStatus Status,
    FailureCode FailureCode,
    SwapReservationPhase Phase);

public sealed class PersistentSwapEndpoint : ISwapEndpoint, IDisposable
{
    private readonly IActivityCatalog catalog;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISwapEndpointJournal journal;
    private bool disposed;

    public PersistentSwapEndpoint(
        DeviceId deviceId,
        IActivityCatalog catalog,
        ISwapEndpointJournal journal)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.DeviceId != deviceId)
        {
            throw new ArgumentException(
                "A swap endpoint journal belongs to another Device.",
                nameof(journal));
        }

        DeviceId = deviceId;
        this.catalog = catalog;
        this.journal = journal;
    }

    public DeviceId DeviceId { get; }

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return catalog.TryGet(activityId, out activity);
    }

    public bool TryGetReservation(
        OperationId operationId,
        [NotNullWhen(true)] out SwapReservation? reservation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (journal.TryGet(operationId, out SwapEndpointRecord? record)
            && record.Reservation is not null)
        {
            reservation = record.Reservation;
            return true;
        }

        reservation = null;
        return false;
    }

    public bool TryGetDecision(
        OperationId operationId,
        [NotNullWhen(true)] out SwapDecision? decision)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (journal.TryGet(operationId, out SwapEndpointRecord? record)
            && record.Decision is not null)
        {
            decision = record.Decision;
            return true;
        }

        decision = null;
        return false;
    }

    public async ValueTask<SwapPrepareResult> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.OriginalActivity.Placement.DeviceId != DeviceId)
        {
            return SwapPrepareResult.Rejected(FailureCode.RevisionConflict);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (journal.TryGet(command.OperationId, out SwapEndpointRecord? existing))
            {
                if (existing.Decision is not null)
                {
                    return SwapPrepareResult.Rejected(FailureCode.DecisionConflict);
                }

                SwapReservation? existingReservation = existing.Reservation;
                return existingReservation is not null
                    && existingReservation.Token == command.ReservationToken
                    && existingReservation.MatchesRequest(
                        command.OriginalActivity,
                        command.IncomingActivity,
                        command.ExpiresAt)
                    ? SwapPrepareResult.Success(existingReservation.Token)
                    : SwapPrepareResult.Rejected(FailureCode.ReservationConflict);
            }

            if (journal.Snapshot().Any(record =>
                    record.Reservation is { } reservation
                    && RequiresDecisionReduction(record)
                    && Overlaps(reservation, command)))
            {
                return SwapPrepareResult.Rejected(FailureCode.ReservationConflict);
            }

            if (!catalog.TryGet(
                    command.OriginalActivity.Descriptor.Id,
                    out ActivityInstance? current)
                || current != command.OriginalActivity
                || catalog.TryGet(command.IncomingActivity.Descriptor.Id, out _))
            {
                return SwapPrepareResult.Rejected(FailureCode.RevisionConflict);
            }

            SwapReservation reservation;
            try
            {
                reservation = SwapReservation.Prepare(
                    command.OperationId,
                    command.ReservationToken,
                    command.OriginalActivity,
                    command.IncomingActivity,
                    command.ExpiresAt);
            }
            catch (Exception exception) when (exception is
                ArgumentException
                or InvalidOperationException)
            {
                return SwapPrepareResult.Rejected(FailureCode.RevisionConflict);
            }

            SwapEndpointWriteResult write = await journal
                .TryPrepareAsync(reservation, cancellationToken)
                .ConfigureAwait(false);
            return write.Status is SwapEndpointWriteStatus.Stored
                or SwapEndpointWriteStatus.Replayed
                ? SwapPrepareResult.Success(reservation.Token)
                : SwapPrepareResult.Rejected(
                    write.Status == SwapEndpointWriteStatus.Conflict
                        ? FailureCode.ReservationConflict
                        : FailureCode.InternalFailure);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<SwapApplyResult> ApplyDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();
        if (!decision.TryGetReservationToken(DeviceId, out _))
        {
            return SwapApplyResult.Rejected(FailureCode.DecisionConflict);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SwapEndpointWriteResult write = await journal
                .TryRecordDecisionAsync(decision, cancellationToken)
                .ConfigureAwait(false);
            if (write.Status is SwapEndpointWriteStatus.Conflict
                or SwapEndpointWriteStatus.CapacityExceeded
                || write.Record?.Decision is null)
            {
                return SwapApplyResult.Rejected(
                    write.Status == SwapEndpointWriteStatus.CapacityExceeded
                        ? FailureCode.InternalFailure
                        : FailureCode.DecisionConflict);
            }

            return Reduce(write.Record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<SwapEndpointRecoveryResult>> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<SwapEndpointRecoveryResult>();
            foreach (SwapEndpointRecord record in journal.Snapshot())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (record.Decision is null)
                {
                    results.Add(new SwapEndpointRecoveryResult(
                        record.OperationId,
                        OperationStatus.Recovering,
                        FailureCode.OperationInProgress,
                        SwapReservationPhase.Prepared));
                    continue;
                }

                SwapApplyResult reduced = Reduce(record);
                results.Add(new SwapEndpointRecoveryResult(
                    record.OperationId,
                    reduced.Applied
                        ? record.Decision.Outcome == SwapDecisionOutcome.Commit
                            ? OperationStatus.Committed
                            : OperationStatus.Rejected
                        : OperationStatus.Recovering,
                    reduced.Applied
                        ? record.Decision.Outcome == SwapDecisionOutcome.Commit
                            ? FailureCode.None
                            : record.Decision.FailureCode
                        : reduced.FailureCode,
                    record.Decision.Outcome == SwapDecisionOutcome.Commit
                        ? SwapReservationPhase.Committed
                        : SwapReservationPhase.Aborted));
            }

            return results;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
    }

    private static bool Overlaps(
        SwapReservation reservation,
        SwapPrepareCommand command) =>
        reservation.OriginalActivity.Descriptor.Id
            == command.OriginalActivity.Descriptor.Id
        || reservation.OriginalActivity.Descriptor.Id
            == command.IncomingActivity.Descriptor.Id
        || reservation.IncomingActivity.Descriptor.Id
            == command.OriginalActivity.Descriptor.Id
        || reservation.IncomingActivity.Descriptor.Id
            == command.IncomingActivity.Descriptor.Id;

    private bool RequiresDecisionReduction(SwapEndpointRecord record)
    {
        SwapReservation? reservation = record.Reservation;
        if (reservation is null || record.Decision?.Outcome == SwapDecisionOutcome.Abort)
        {
            return false;
        }

        if (record.Decision is null)
        {
            return reservation.Phase == SwapReservationPhase.Prepared;
        }

        ActivityInstance replacement = reservation.CreateCommittedReplacement();
        return catalog.TryGet(reservation.OriginalActivity.Descriptor.Id, out _)
            || !catalog.TryGet(
                replacement.Descriptor.Id,
                out ActivityInstance? currentReplacement)
            || currentReplacement != replacement;
    }

    private SwapApplyResult Reduce(SwapEndpointRecord record)
    {
        SwapDecision decision = record.Decision
            ?? throw new InvalidOperationException(
                "Only a decided endpoint record can be reduced.");
        if (decision.Outcome == SwapDecisionOutcome.Abort)
        {
            return SwapApplyResult.Success(SwapReservationPhase.Aborted);
        }

        SwapReservation reservation = record.Reservation
            ?? throw new InvalidDataException(
                "A committed endpoint record has no reservation.");
        ActivityInstance replacement = reservation.CreateCommittedReplacement();
        if (catalog.TryGet(
                reservation.OriginalActivity.Descriptor.Id,
                out ActivityInstance? currentOriginal))
        {
            if (currentOriginal != reservation.OriginalActivity
                || catalog.TryGet(replacement.Descriptor.Id, out _)
                || !catalog.TrySwapReplace(reservation.OriginalActivity, replacement))
            {
                return SwapApplyResult.Rejected(FailureCode.RevisionConflict);
            }

            return SwapApplyResult.Success(SwapReservationPhase.Committed);
        }

        return catalog.TryGet(replacement.Descriptor.Id, out ActivityInstance? currentReplacement)
            && currentReplacement == replacement
            ? SwapApplyResult.Success(SwapReservationPhase.Committed)
            : SwapApplyResult.Rejected(FailureCode.RevisionConflict);
    }
}

internal static class SwapEndpointPayloadCodec
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<SwapEndpointRecord> Decode(
        ReadOnlySpan<byte> payload,
        DeviceId expectedDeviceId)
    {
        ArgumentNullException.ThrowIfNull(expectedDeviceId);
        if (payload.IsEmpty
            || payload.Length > PersistentSwapEndpointJournal.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"A swap endpoint payload must contain 1 to {PersistentSwapEndpointJournal.MaximumPayloadBytes} bytes.");
        }

        try
        {
            StateDto? state = JsonSerializer.Deserialize<StateDto>(
                payload,
                SerializerOptions);
            if (state is null
                || state.FormatVersion != CurrentFormatVersion
                || state.Records is null)
            {
                throw new InvalidDataException(
                    "The swap endpoint payload is incomplete or unsupported.");
            }

            DeviceId deviceId = DeviceId.Parse(state.DeviceId);
            if (deviceId != expectedDeviceId)
            {
                throw new InvalidDataException(
                    "The swap endpoint payload belongs to another Device.");
            }

            if (state.Records.Length > PersistentSwapEndpointJournal.MaximumRecordCount
                || state.Records.Any(static record => record is null))
            {
                throw new InvalidDataException(
                    "Swap endpoint records exceed bounds or contain null entries.");
            }

            var decoded = new List<SwapEndpointRecord>(state.Records.Length);
            string? previousOperationId = null;
            foreach (RecordDto encoded in state.Records)
            {
                OperationId operationId = OperationId.Parse(encoded.OperationId);
                string canonicalOperationId = operationId.ToString();
                if (previousOperationId is not null
                    && StringComparer.Ordinal.Compare(
                        previousOperationId,
                        canonicalOperationId) >= 0)
                {
                    throw new InvalidDataException(
                        "Swap endpoint records are duplicated or not canonically ordered.");
                }

                SwapDecision? decision = encoded.Decision is null
                    ? null
                    : DecodeDecision(operationId, encoded.Decision);
                SwapReservation? reservation = encoded.Reservation is null
                    ? null
                    : DecodeReservation(operationId, encoded.Reservation, decision);
                SwapEndpointRecord record = SwapEndpointRecord.Restore(
                    deviceId,
                    reservation,
                    decision);
                if (record.OperationId != operationId)
                {
                    throw new InvalidDataException(
                        "A swap endpoint record Operation ID does not match its content.");
                }

                previousOperationId = canonicalOperationId;
                decoded.Add(record);
            }

            return decoded;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The swap endpoint payload is malformed.",
                exception);
        }
    }

    public static byte[] Encode(
        DeviceId deviceId,
        IEnumerable<SwapEndpointRecord> records)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(records);
        SwapEndpointRecord[] ordered = records
            .OrderBy(static record => record.OperationId.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > PersistentSwapEndpointJournal.MaximumRecordCount
            || ordered.Select(static record => record.OperationId).Distinct().Count()
                != ordered.Length
            || ordered.Any(record => record.DeviceId != deviceId))
        {
            throw new InvalidDataException(
                "Swap endpoint records exceed bounds, contain duplicates, or belong to another Device.");
        }

        var state = new StateDto(
            CurrentFormatVersion,
            deviceId.ToString(),
            ordered.Select(EncodeRecord).ToArray());
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
        if (payload.Length > PersistentSwapEndpointJournal.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                $"A swap endpoint payload cannot exceed {PersistentSwapEndpointJournal.MaximumPayloadBytes} bytes.");
        }

        return payload;
    }

    private static RecordDto EncodeRecord(SwapEndpointRecord record) => new(
        record.OperationId.ToString(),
        record.Reservation is null ? null : EncodeReservation(record.Reservation),
        record.Decision is null ? null : EncodeDecision(record.Decision));

    private static ReservationDto EncodeReservation(SwapReservation reservation) => new(
        reservation.Token.ToString(),
        EncodeActivity(reservation.OriginalActivity),
        EncodeActivity(reservation.IncomingActivity),
        reservation.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
        reservation.RequestDigest,
        checked((int)reservation.Phase),
        reservation.DecisionDigest);

    private static DecisionDto EncodeDecision(SwapDecision decision) => new(
        checked((int)decision.Outcome),
        decision.DecidedAt.ToString("O", CultureInfo.InvariantCulture),
        checked((int)decision.FailureCode),
        decision.Digest,
        decision.Participants.Select(static participant => new ParticipantDto(
            participant.DeviceId.ToString(),
            participant.ReservationToken.ToString())).ToArray());

    private static ActivityDto EncodeActivity(ActivityInstance activity) => new(
        activity.Descriptor.Id.ToString(),
        activity.Descriptor.Kind.Value,
        activity.Descriptor.OriginDeviceId.ToString(),
        activity.Descriptor.Title,
        activity.Descriptor.PayloadJson,
        activity.Descriptor.PayloadDigest,
        activity.Descriptor.DescriptorDigest,
        checked((int)activity.Descriptor.Sensitivity),
        activity.Placement.DeviceId.ToString(),
        activity.Placement.Slot,
        activity.Revision,
        checked((int)activity.Lifecycle));

    private static SwapReservation DecodeReservation(
        OperationId operationId,
        ReservationDto encoded,
        SwapDecision? decision)
    {
        if (encoded is null)
        {
            throw new InvalidDataException(
                "A swap endpoint reservation cannot be null.");
        }

        if (!Enum.IsDefined(typeof(SwapReservationPhase), encoded.Phase))
        {
            throw new InvalidDataException(
                "A swap endpoint reservation has an unknown phase.");
        }

        SwapReservation reservation = SwapReservation.Prepare(
            operationId,
            SwapReservationToken.From(Guid.Parse(encoded.Token)),
            DecodeActivity(encoded.OriginalActivity),
            DecodeActivity(encoded.IncomingActivity),
            ParseTimestamp(encoded.ExpiresAt, "reservation expiry"));
        RequireDigestMatch(
            reservation.RequestDigest,
            encoded.RequestDigest,
            "reservation request");
        if (decision is not null)
        {
            try
            {
                reservation = reservation.ApplyDecision(decision);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(
                    "A swap endpoint decision does not bind its reservation.",
                    exception);
            }
        }

        if (reservation.Phase != (SwapReservationPhase)encoded.Phase
            || !StringComparer.Ordinal.Equals(
                reservation.DecisionDigest,
                encoded.DecisionDigest))
        {
            throw new InvalidDataException(
                "A swap endpoint reservation phase or decision digest is inconsistent.");
        }

        return reservation;
    }

    private static SwapDecision DecodeDecision(
        OperationId operationId,
        DecisionDto encoded)
    {
        if (encoded is null
            || encoded.Participants is null
            || encoded.Participants.Any(static participant => participant is null)
            || !Enum.IsDefined(typeof(SwapDecisionOutcome), encoded.Outcome)
            || !Enum.IsDefined(typeof(FailureCode), encoded.FailureCode))
        {
            throw new InvalidDataException(
                "A swap endpoint decision is null, incomplete, or contains an unknown enum.");
        }

        string[] encodedOrder = encoded.Participants
            .Select(static participant => participant.DeviceId)
            .ToArray();
        if (!encodedOrder.SequenceEqual(
                encodedOrder.OrderBy(static value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Swap endpoint decision participants are not canonically ordered.");
        }

        SwapDecisionParticipant[] participants = encoded.Participants
            .Select(static participant => SwapDecisionParticipant.Create(
                DeviceId.Parse(participant.DeviceId),
                SwapReservationToken.From(Guid.Parse(participant.ReservationToken))))
            .ToArray();
        SwapDecision decision = SwapDecision.Create(
            operationId,
            (SwapDecisionOutcome)encoded.Outcome,
            ParseTimestamp(encoded.DecidedAt, "decision"),
            participants,
            (FailureCode)encoded.FailureCode);
        RequireDigestMatch(decision.Digest, encoded.Digest, "decision");
        return decision;
    }

    private static ActivityInstance DecodeActivity(ActivityDto encoded)
    {
        if (encoded is null
            || !Enum.IsDefined(typeof(ActivitySensitivity), encoded.Sensitivity)
            || !Enum.IsDefined(typeof(ActivityLifecycle), encoded.Lifecycle)
            || (ActivityLifecycle)encoded.Lifecycle != ActivityLifecycle.Active)
        {
            throw new InvalidDataException(
                "A swap endpoint Activity is null or has invalid enum values.");
        }

        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse(encoded.Id),
            ActivityKind.Parse(encoded.Kind),
            DeviceId.Parse(encoded.OriginDeviceId),
            encoded.Title,
            encoded.PayloadJson,
            (ActivitySensitivity)encoded.Sensitivity);
        RequireDigestMatch(descriptor.PayloadDigest, encoded.PayloadDigest, "payload");
        RequireDigestMatch(
            descriptor.DescriptorDigest,
            encoded.DescriptorDigest,
            "descriptor");
        return ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(
                DeviceId.Parse(encoded.PlacementDeviceId),
                encoded.PlacementSlot),
            encoded.Revision);
    }

    private static void RequireDigestMatch(
        string computed,
        string encoded,
        string field)
    {
        SwapTransactionParticipant.ValidateDigest(encoded);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(computed),
                Convert.FromHexString(encoded)))
        {
            throw new InvalidDataException(
                $"The swap endpoint {field} digest does not match its fields.");
        }
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
                $"The swap endpoint {field} timestamp is not canonical UTC.");
        }

        return timestamp;
    }

    private sealed record StateDto(
        int FormatVersion,
        string DeviceId,
        RecordDto[] Records);

    private sealed record RecordDto(
        string OperationId,
        ReservationDto? Reservation,
        DecisionDto? Decision);

    private sealed record ReservationDto(
        string Token,
        ActivityDto OriginalActivity,
        ActivityDto IncomingActivity,
        string ExpiresAt,
        string RequestDigest,
        int Phase,
        string? DecisionDigest);

    private sealed record DecisionDto(
        int Outcome,
        string DecidedAt,
        int FailureCode,
        string Digest,
        ParticipantDto[] Participants);

    private sealed record ParticipantDto(
        string DeviceId,
        string ReservationToken);

    private sealed record ActivityDto(
        string Id,
        string Kind,
        string OriginDeviceId,
        string Title,
        string PayloadJson,
        string PayloadDigest,
        string DescriptorDigest,
        int Sensitivity,
        string PlacementDeviceId,
        string PlacementSlot,
        long Revision,
        int Lifecycle);
}
