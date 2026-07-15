using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface IReplaceStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class ReplaceStatePersistenceException : IOException
{
    public ReplaceStatePersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistentReplaceStateStore :
    IOperationJournal,
    IReplaceStateStore,
    IReplaceRecoverySnapshotSource,
    IDisposable
{
    public const int MaximumCapsuleCount = 16;
    public const int MaximumOperationCount = 256;
    public const int MaximumUndoOperationCount = 256;
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;

    private readonly Dictionary<OperationId, TaskCompletionSource<JournalExecutionResult>>
        inFlightOperations = [];
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly IReplaceStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private StoreState state;

    private PersistentReplaceStateStore(
        IReplaceStatePayloadStore payloadStore,
        PersistedReplaceState persisted)
    {
        this.payloadStore = payloadStore;
        state = new StoreState(
            persisted.Capsules.ToDictionary(static capsule => capsule.Id),
            persisted.Operations.ToDictionary(static operation => operation.OperationId),
            persisted.UndoOperations.ToDictionary(static operation => operation.OperationId));
    }

    public int CapsuleCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (snapshotGate)
            {
                return state.Capsules.Count;
            }
        }
    }

    public ReplaceRecoverySnapshot GetRecoverySnapshot(DateTimeOffset utcNow)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Replace recovery snapshot requires a UTC timestamp.",
                nameof(utcNow));
        }

        PersistedOperation[] operations;
        PersistedUndoOperation[] undoOperations;
        UndoCapsule[] capsules;
        lock (snapshotGate)
        {
            operations = state.Operations.Values.ToArray();
            undoOperations = state.UndoOperations.Values.ToArray();
            capsules = state.Capsules.Values.ToArray();
        }

        var capsuleByOperation = capsules.ToDictionary(
            static capsule => capsule.OperationId);
        var capsuleById = capsules.ToDictionary(static capsule => capsule.Id);
        var undoByCapsule = undoOperations
            .GroupBy(static undo => undo.CapsuleId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        IEnumerable<ReplaceRecoveryRecord> replaceRecords = operations.Select(
            operation => CreateReplaceRecoveryRecord(
                operation,
                capsuleByOperation.GetValueOrDefault(operation.OperationId),
                undoByCapsule,
                utcNow));
        IEnumerable<ReplaceRecoveryRecord> undoRecords = undoOperations.Select(
            operation => CreateUndoRecoveryRecord(
                operation,
                capsuleById.GetValueOrDefault(operation.CapsuleId)));
        ReplaceRecoveryRecord[] ordered = replaceRecords
            .Concat(undoRecords)
            .OrderByDescending(static record => record.IsRecoveryRequired)
            .ThenByDescending(static record =>
                record.RecordedAt ?? DateTimeOffset.MinValue)
            .ThenBy(static record => record.Kind)
            .ThenBy(
                static record => record.OperationId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        bool isTruncated = ordered.Length > ReplaceRecoverySnapshot.MaximumRecords;
        return new ReplaceRecoverySnapshot(
            utcNow,
            isTruncated,
            ordered.Take(ReplaceRecoverySnapshot.MaximumRecords).ToImmutableArray());
    }

    public static async ValueTask<PersistentReplaceStateStore> OpenAsync(
        IReplaceStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentReplaceStateStore(
                payloadStore,
                new PersistedReplaceState([], [], []));
        }

        try
        {
            PersistedReplaceState persisted = ReplaceStatePayloadCodec.Decode(payload);
            return new PersistentReplaceStateStore(payloadStore, persisted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public bool TryGetCapsule(
        UndoCapsuleId capsuleId,
        [NotNullWhen(true)] out UndoCapsule? capsule)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(capsuleId);
        lock (snapshotGate)
        {
            return state.Capsules.TryGetValue(capsuleId, out capsule);
        }
    }

    public bool TryGet(
        UndoCapsuleId capsuleId,
        [NotNullWhen(true)] out UndoCapsule? capsule) =>
        TryGetCapsule(capsuleId, out capsule);

    public bool TryGetByOperation(
        OperationId operationId,
        [NotNullWhen(true)] out UndoCapsule? capsule)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(operationId);
        lock (snapshotGate)
        {
            capsule = state.Capsules.Values.FirstOrDefault(candidate =>
                candidate.OperationId == operationId);
            return capsule is not null;
        }
    }

    public async ValueTask<JournalExecutionResult> ExecuteOnceAsync(
        OperationId operationId,
        string requestDigest,
        Func<CancellationToken, ValueTask<OperationReceipt>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(operationId);
        ValidateRequestDigest(requestDigest);
        ArgumentNullException.ThrowIfNull(operation);

        Task<JournalExecutionResult>? pendingExecution = null;
        TaskCompletionSource<JournalExecutionResult>? owner = null;
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreState candidate = Snapshot();
            if (candidate.Operations.TryGetValue(
                    operationId,
                    out PersistedOperation? existing))
            {
                if (!StringComparer.Ordinal.Equals(existing.RequestDigest, requestDigest))
                {
                    return new JournalExecutionResult(null, false, true);
                }

                if (existing.Receipt is not null)
                {
                    return new JournalExecutionResult(existing.Receipt, true, false);
                }

                if (!inFlightOperations.TryGetValue(operationId, out owner))
                {
                    return new JournalExecutionResult(
                        null,
                        true,
                        false,
                        IsRecoveryRequired: true);
                }

                pendingExecution = owner.Task;
            }
            else
            {
                if (candidate.Operations.Count >= MaximumOperationCount)
                {
                    throw new ReplaceStatePersistenceException(
                        "The protected Replace operation journal is full.",
                        new InvalidDataException(
                            $"A Replace state cannot contain more than {MaximumOperationCount} operations."));
                }

                candidate.Operations.Add(
                    operationId,
                    new PersistedOperation(operationId, requestDigest, null));
                await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
                owner = new TaskCompletionSource<JournalExecutionResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                inFlightOperations.Add(operationId, owner);
            }
        }
        finally
        {
            mutationGate.Release();
        }

        if (pendingExecution is not null)
        {
            JournalExecutionResult replay = await pendingExecution
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            return replay with { WasReplay = true };
        }

        try
        {
            OperationReceipt receipt = await operation(cancellationToken)
                .ConfigureAwait(false);
            JournalExecutionResult result;
            await mutationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                StoreState candidate = Snapshot();
                candidate.Operations[operationId] = new PersistedOperation(
                    operationId,
                    requestDigest,
                    receipt);
                try
                {
                    await CommitAsync(candidate, CancellationToken.None).ConfigureAwait(false);
                    result = new JournalExecutionResult(receipt, false, false);
                }
                catch (ReplaceStatePersistenceException)
                {
                    result = new JournalExecutionResult(
                        null,
                        false,
                        false,
                        IsRecoveryRequired: true);
                }

                inFlightOperations.Remove(operationId);
            }
            finally
            {
                mutationGate.Release();
            }

            owner!.TrySetResult(result);
            return result;
        }
        catch (Exception exception)
        {
            await mutationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                inFlightOperations.Remove(operationId);
            }
            finally
            {
                mutationGate.Release();
            }

            owner!.TrySetException(exception);
            throw;
        }
    }

    public async ValueTask<bool> TryAddCapsuleAsync(
        UndoCapsule capsule,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(capsule);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreState candidate = Snapshot();
            if (candidate.Capsules.ContainsKey(capsule.Id)
                || candidate.Capsules.Values.Any(existing =>
                    existing.OperationId == capsule.OperationId))
            {
                return false;
            }

            if (candidate.Capsules.Count >= MaximumCapsuleCount)
            {
                return false;
            }

            candidate.Capsules.Add(capsule.Id, capsule);
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public ValueTask<bool> TryAddAsync(
        UndoCapsule capsule,
        CancellationToken cancellationToken = default) =>
        TryAddCapsuleAsync(capsule, cancellationToken);

    public async ValueTask<bool> TryRemoveAsync(
        UndoCapsuleId capsuleId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(capsuleId);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreState candidate = Snapshot();
            if (!candidate.Capsules.Remove(capsuleId))
            {
                return false;
            }

            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<UndoJournalPreparation> PrepareUndoAsync(
        UndoCapsuleId capsuleId,
        OperationId operationId,
        string requestDigest,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(capsuleId);
        ArgumentNullException.ThrowIfNull(operationId);
        ValidateRequestDigest(requestDigest);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreState candidate = Snapshot();
            if (candidate.UndoOperations.TryGetValue(
                    operationId,
                    out PersistedUndoOperation? existing))
            {
                if (existing.CapsuleId != capsuleId
                    || !StringComparer.Ordinal.Equals(
                        existing.RequestDigest,
                        requestDigest))
                {
                    return new UndoJournalPreparation(
                        UndoJournalPreparationStatus.Conflict);
                }

                return existing.Result is null
                    ? new UndoJournalPreparation(
                        UndoJournalPreparationStatus.RecoveryRequired)
                    : new UndoJournalPreparation(
                        UndoJournalPreparationStatus.Replay,
                        existing.Result);
            }

            if (candidate.UndoOperations.Values.Any(entry =>
                    entry.CapsuleId == capsuleId && entry.Result is null))
            {
                return new UndoJournalPreparation(
                    UndoJournalPreparationStatus.CapsuleReserved);
            }

            if (candidate.UndoOperations.Count >= MaximumUndoOperationCount)
            {
                throw new ReplaceStatePersistenceException(
                    "The protected Replace undo journal is full.",
                    new InvalidDataException(
                        $"A Replace state cannot contain more than {MaximumUndoOperationCount} undo operations."));
            }

            bool consumed = candidate.UndoOperations.Values.Any(entry =>
                entry.CapsuleId == capsuleId
                && entry.Result?.Status == OperationStatus.Committed);
            candidate.UndoOperations.Add(
                operationId,
                new PersistedUndoOperation(
                    operationId,
                    capsuleId,
                    requestDigest,
                    null));
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new UndoJournalPreparation(
                consumed
                    ? UndoJournalPreparationStatus.PreparedConsumed
                    : UndoJournalPreparationStatus.Prepared);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask CompleteUndoAsync(
        OperationId operationId,
        UndoReplaceResult result,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(result);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreState candidate = Snapshot();
            if (!candidate.UndoOperations.TryGetValue(
                    operationId,
                    out PersistedUndoOperation? existing)
                || existing.Result is not null
                || existing.CapsuleId != result.CapsuleId
                || result.OperationId != operationId)
            {
                throw new InvalidOperationException(
                    "An undo result requires its matching pending journal entry.");
            }

            candidate.UndoOperations[operationId] = existing with { Result = result };
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<int> RemoveExpiredCapsulesAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Replace state cleanup requires a UTC timestamp.",
                nameof(utcNow));
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreState candidate = Snapshot();
            UndoCapsuleId[] removable = candidate.Capsules.Values
                .Where(capsule =>
                    capsule.ExpiresAt <= utcNow
                    && !candidate.UndoOperations.Values.Any(undo =>
                        undo.CapsuleId == capsule.Id && undo.Result is null)
                    && (!candidate.Operations.TryGetValue(
                            capsule.OperationId,
                            out PersistedOperation? operation)
                        || operation.Receipt is not null))
                .Select(static capsule => capsule.Id)
                .ToArray();
            if (removable.Length == 0)
            {
                return 0;
            }

            foreach (UndoCapsuleId capsuleId in removable)
            {
                UndoCapsule capsule = candidate.Capsules[capsuleId];
                candidate.Capsules.Remove(capsuleId);
                candidate.Operations.Remove(capsule.OperationId);
                OperationId[] disposableUndoEntries = candidate.UndoOperations.Values
                    .Where(undo =>
                        undo.CapsuleId == capsuleId
                        && undo.Result is not null
                        && undo.Result.Status != OperationStatus.Committed)
                    .Select(static undo => undo.OperationId)
                    .ToArray();
                foreach (OperationId operationId in disposableUndoEntries)
                {
                    candidate.UndoOperations.Remove(operationId);
                }
            }

            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return removable.Length;
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

    private async ValueTask CommitAsync(
        StoreState candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = ReplaceStatePayloadCodec.Encode(new PersistedReplaceState(
            candidate.Capsules.Values.ToArray(),
            candidate.Operations.Values.ToArray(),
            candidate.UndoOperations.Values.ToArray()));
        try
        {
            try
            {
                await payloadStore.SaveAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is
                IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or CryptographicException)
            {
                throw new ReplaceStatePersistenceException(
                    "The protected Replace state could not be saved atomically.",
                    exception);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            state = candidate;
        }
    }

    private StoreState Snapshot()
    {
        lock (snapshotGate)
        {
            return new StoreState(
                new Dictionary<UndoCapsuleId, UndoCapsule>(state.Capsules),
                new Dictionary<OperationId, PersistedOperation>(state.Operations),
                new Dictionary<OperationId, PersistedUndoOperation>(state.UndoOperations));
        }
    }

    private static void ValidateRequestDigest(string requestDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestDigest);
        if (requestDigest.Length > 256 || requestDigest.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A journal request digest must contain 1 to 256 non-control characters.",
                nameof(requestDigest));
        }
    }

    private static ReplaceRecoveryRecord CreateReplaceRecoveryRecord(
        PersistedOperation operation,
        UndoCapsule? capsule,
        IReadOnlyDictionary<UndoCapsuleId, PersistedUndoOperation[]> undoByCapsule,
        DateTimeOffset utcNow)
    {
        OperationReceipt? receipt = operation.Receipt;
        ReplaceRecoveryJournalState journalState = receipt is null
            ? ReplaceRecoveryJournalState.Pending
            : ReplaceRecoveryJournalState.Terminal;
        ReplaceRecoveryTimestampKind timestampKind = receipt is not null
            ? ReplaceRecoveryTimestampKind.Outcome
            : capsule is not null
                ? ReplaceRecoveryTimestampKind.CapsuleCaptured
                : ReplaceRecoveryTimestampKind.None;
        return new ReplaceRecoveryRecord(
            ReplaceRecoveryOperationKind.Replace,
            journalState,
            operation.OperationId,
            receipt?.Status ?? OperationStatus.Recovering,
            receipt?.FailureCode ?? FailureCode.OperationInProgress,
            receipt?.CorrelationId ?? capsule?.CorrelationId,
            receipt?.SourceDeviceId ?? capsule?.SourceDeviceId,
            receipt?.TargetDeviceId ?? capsule?.TargetDeviceId,
            capsule?.TargetActivityId,
            receipt?.ActivityId ?? capsule?.ReplacementActivity.Descriptor.Id,
            capsule?.Id,
            timestampKind,
            receipt?.OccurredAt ?? capsule?.CapturedAt,
            capsule?.ExpiresAt,
            GetUndoAvailability(receipt, capsule, undoByCapsule, utcNow));
    }

    private static ReplaceRecoveryRecord CreateUndoRecoveryRecord(
        PersistedUndoOperation operation,
        UndoCapsule? capsule)
    {
        UndoReplaceResult? result = operation.Result;
        return new ReplaceRecoveryRecord(
            ReplaceRecoveryOperationKind.Undo,
            result is null
                ? ReplaceRecoveryJournalState.Pending
                : ReplaceRecoveryJournalState.Terminal,
            operation.OperationId,
            result?.Status ?? OperationStatus.Recovering,
            result?.FailureCode ?? FailureCode.OperationInProgress,
            result?.CorrelationId,
            capsule?.SourceDeviceId,
            capsule?.TargetDeviceId,
            capsule?.TargetActivityId,
            capsule?.ReplacementActivity.Descriptor.Id,
            operation.CapsuleId,
            result is null
                ? ReplaceRecoveryTimestampKind.None
                : ReplaceRecoveryTimestampKind.Outcome,
            result?.OccurredAt,
            capsule?.ExpiresAt,
            ReplaceUndoAvailability.None);
    }

    private static ReplaceUndoAvailability GetUndoAvailability(
        OperationReceipt? receipt,
        UndoCapsule? capsule,
        IReadOnlyDictionary<UndoCapsuleId, PersistedUndoOperation[]> undoByCapsule,
        DateTimeOffset utcNow)
    {
        if (capsule is null)
        {
            return ReplaceUndoAvailability.None;
        }

        if (undoByCapsule.TryGetValue(
                capsule.Id,
                out PersistedUndoOperation[]? undoOperations))
        {
            if (undoOperations.Any(static undo =>
                    undo.Result?.Status == OperationStatus.Committed))
            {
                return ReplaceUndoAvailability.Consumed;
            }

            if (undoOperations.Any(static undo => undo.Result is null))
            {
                return ReplaceUndoAvailability.PendingOperation;
            }
        }

        if (receipt is null || !receipt.IsSuccess)
        {
            return receipt?.Status == OperationStatus.Recovering
                ? ReplaceUndoAvailability.PendingOperation
                : ReplaceUndoAvailability.None;
        }

        return capsule.ExpiresAt <= utcNow
            ? ReplaceUndoAvailability.Expired
            : ReplaceUndoAvailability.Available;
    }

    private sealed record StoreState(
        Dictionary<UndoCapsuleId, UndoCapsule> Capsules,
        Dictionary<OperationId, PersistedOperation> Operations,
        Dictionary<OperationId, PersistedUndoOperation> UndoOperations);
}

internal sealed record PersistedReplaceState(
    IReadOnlyList<UndoCapsule> Capsules,
    IReadOnlyList<PersistedOperation> Operations,
    IReadOnlyList<PersistedUndoOperation> UndoOperations);

internal sealed record PersistedOperation(
    OperationId OperationId,
    string RequestDigest,
    OperationReceipt? Receipt);

internal sealed record PersistedUndoOperation(
    OperationId OperationId,
    UndoCapsuleId CapsuleId,
    string RequestDigest,
    UndoReplaceResult? Result);

internal static class ReplaceStatePayloadCodec
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static PersistedReplaceState Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > PersistentReplaceStateStore.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"A Replace state payload must contain 1 to {PersistentReplaceStateStore.MaximumPayloadBytes} bytes.");
        }

        try
        {
            StateDto? state = JsonSerializer.Deserialize<StateDto>(payload, SerializerOptions);
            if (state is null
                || state.FormatVersion != CurrentFormatVersion
                || state.Capsules is null
                || state.Operations is null
                || state.UndoOperations is null)
            {
                throw new InvalidDataException(
                    "The Replace state payload has an unsupported or incomplete envelope.");
            }

            if (state.Capsules.Length > PersistentReplaceStateStore.MaximumCapsuleCount)
            {
                throw new InvalidDataException(
                    $"A Replace state payload cannot contain more than {PersistentReplaceStateStore.MaximumCapsuleCount} capsules.");
            }

            if (state.Operations.Length > PersistentReplaceStateStore.MaximumOperationCount)
            {
                throw new InvalidDataException(
                    $"A Replace state payload cannot contain more than {PersistentReplaceStateStore.MaximumOperationCount} operations.");
            }

            if (state.UndoOperations.Length
                > PersistentReplaceStateStore.MaximumUndoOperationCount)
            {
                throw new InvalidDataException(
                    $"A Replace state payload cannot contain more than {PersistentReplaceStateStore.MaximumUndoOperationCount} undo operations.");
            }

            var capsules = new List<UndoCapsule>(state.Capsules.Length);
            string? previousId = null;
            var operationIds = new HashSet<OperationId>();
            foreach (CapsuleDto encoded in state.Capsules)
            {
                UndoCapsule capsule = DecodeCapsule(encoded);
                string id = capsule.Id.ToString();
                if (previousId is not null
                    && StringComparer.Ordinal.Compare(previousId, id) >= 0)
                {
                    throw new InvalidDataException(
                        "Replace state capsules are duplicated or not canonically ordered.");
                }

                if (!operationIds.Add(capsule.OperationId))
                {
                    throw new InvalidDataException(
                        "Replace state capsules contain a duplicate Operation ID.");
                }

                previousId = id;
                capsules.Add(capsule);
            }

            var operations = new List<PersistedOperation>(state.Operations.Length);
            string? previousOperationId = null;
            foreach (OperationDto encoded in state.Operations)
            {
                PersistedOperation operation = DecodeOperation(encoded);
                string operationId = operation.OperationId.ToString();
                if (previousOperationId is not null
                    && StringComparer.Ordinal.Compare(
                        previousOperationId,
                        operationId) >= 0)
                {
                    throw new InvalidDataException(
                        "Replace journal operations are duplicated or not canonically ordered.");
                }

                previousOperationId = operationId;
                operations.Add(operation);
            }

            var undoOperations = new List<PersistedUndoOperation>(
                state.UndoOperations.Length);
            string? previousUndoOperationId = null;
            var terminalCapsules = new HashSet<UndoCapsuleId>();
            var pendingCapsules = new HashSet<UndoCapsuleId>();
            foreach (UndoOperationDto encoded in state.UndoOperations)
            {
                PersistedUndoOperation undo = DecodeUndoOperation(encoded);
                string operationId = undo.OperationId.ToString();
                if (previousUndoOperationId is not null
                    && StringComparer.Ordinal.Compare(
                        previousUndoOperationId,
                        operationId) >= 0)
                {
                    throw new InvalidDataException(
                        "Replace undo operations are duplicated or not canonically ordered.");
                }

                if (undo.Result is null)
                {
                    if (!pendingCapsules.Add(undo.CapsuleId))
                    {
                        throw new InvalidDataException(
                            "A Replace capsule cannot have multiple pending undo operations.");
                    }
                }
                else if (undo.Result.Status == OperationStatus.Committed
                    && !terminalCapsules.Add(undo.CapsuleId))
                {
                    throw new InvalidDataException(
                        "A Replace capsule cannot have multiple committed undo operations.");
                }

                previousUndoOperationId = operationId;
                undoOperations.Add(undo);
            }

            return new PersistedReplaceState(capsules, operations, undoOperations);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException("The Replace state payload is malformed.", exception);
        }
    }

    public static byte[] Encode(PersistedReplaceState persisted)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        UndoCapsule[] ordered = persisted.Capsules
            .OrderBy(static capsule => capsule.Id.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > PersistentReplaceStateStore.MaximumCapsuleCount)
        {
            throw new InvalidDataException(
                $"A Replace state payload cannot contain more than {PersistentReplaceStateStore.MaximumCapsuleCount} capsules.");
        }

        if (ordered.Select(static capsule => capsule.Id).Distinct().Count() != ordered.Length
            || ordered.Select(static capsule => capsule.OperationId).Distinct().Count()
                != ordered.Length)
        {
            throw new InvalidDataException(
                "Replace state capsules must have unique capsule and Operation IDs.");
        }

        PersistedOperation[] orderedOperations = persisted.Operations
            .OrderBy(
                static operation => operation.OperationId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (orderedOperations.Length > PersistentReplaceStateStore.MaximumOperationCount
            || orderedOperations.Select(static operation => operation.OperationId)
                .Distinct().Count() != orderedOperations.Length)
        {
            throw new InvalidDataException(
                "Replace state journal entries exceed bounds or contain duplicate Operation IDs.");
        }

        PersistedUndoOperation[] orderedUndoOperations = persisted.UndoOperations
            .OrderBy(
                static operation => operation.OperationId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (orderedUndoOperations.Length
                > PersistentReplaceStateStore.MaximumUndoOperationCount
            || orderedUndoOperations.Select(static operation => operation.OperationId)
                .Distinct().Count() != orderedUndoOperations.Length)
        {
            throw new InvalidDataException(
                "Replace state undo journal entries exceed bounds or contain duplicate Operation IDs.");
        }

        var state = new StateDto(
            CurrentFormatVersion,
            ordered.Select(EncodeCapsule).ToArray(),
            orderedOperations.Select(EncodeOperation).ToArray(),
            orderedUndoOperations.Select(EncodeUndoOperation).ToArray());
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
        if (payload.Length > PersistentReplaceStateStore.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                $"A Replace state payload cannot exceed {PersistentReplaceStateStore.MaximumPayloadBytes} bytes.");
        }

        return payload;
    }

    private static UndoOperationDto EncodeUndoOperation(
        PersistedUndoOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateRequestDigest(operation.RequestDigest);
        return new UndoOperationDto(
            operation.OperationId.ToString(),
            operation.CapsuleId.ToString(),
            operation.RequestDigest,
            operation.Result is null ? null : EncodeUndoResult(operation.Result));
    }

    private static PersistedUndoOperation DecodeUndoOperation(
        UndoOperationDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException("A Replace undo operation cannot be null.");
        }

        ValidateRequestDigest(encoded.RequestDigest);
        OperationId operationId = OperationId.Parse(encoded.OperationId);
        UndoCapsuleId capsuleId = UndoCapsuleId.Parse(encoded.CapsuleId);
        UndoReplaceResult? result = encoded.Result is null
            ? null
            : DecodeUndoResult(encoded.Result);
        if (result is not null
            && (result.OperationId != operationId || result.CapsuleId != capsuleId))
        {
            throw new InvalidDataException(
                "A Replace undo result does not match its journal entry.");
        }

        return new PersistedUndoOperation(
            operationId,
            capsuleId,
            encoded.RequestDigest,
            result);
    }

    private static UndoResultDto EncodeUndoResult(UndoReplaceResult result) => new(
        result.OperationId.ToString(),
        result.CorrelationId.ToString(),
        result.CapsuleId.ToString(),
        checked((int)result.Status),
        checked((int)result.FailureCode),
        result.OccurredAt.ToString("O", CultureInfo.InvariantCulture));

    private static UndoReplaceResult DecodeUndoResult(UndoResultDto encoded)
    {
        if (encoded is null
            || !Enum.IsDefined(typeof(OperationStatus), encoded.Status)
            || !Enum.IsDefined(typeof(FailureCode), encoded.FailureCode))
        {
            throw new InvalidDataException(
                "A Replace undo result contains an unknown enum value.");
        }

        return UndoReplaceResult.FromRecordedResult(
            OperationId.Parse(encoded.OperationId),
            CorrelationId.Parse(encoded.CorrelationId),
            UndoCapsuleId.Parse(encoded.CapsuleId),
            (OperationStatus)encoded.Status,
            (FailureCode)encoded.FailureCode,
            ParseTimestamp(encoded.OccurredAt, "undo result"));
    }

    private static OperationDto EncodeOperation(PersistedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateRequestDigest(operation.RequestDigest);
        return new OperationDto(
            operation.OperationId.ToString(),
            operation.RequestDigest,
            operation.Receipt is null ? null : EncodeReceipt(operation.Receipt));
    }

    private static PersistedOperation DecodeOperation(OperationDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException("A Replace journal operation cannot be null.");
        }

        ValidateRequestDigest(encoded.RequestDigest);
        OperationId operationId = OperationId.Parse(encoded.OperationId);
        OperationReceipt? receipt = encoded.Receipt is null
            ? null
            : DecodeReceipt(encoded.Receipt);
        if (receipt is not null && receipt.OperationId != operationId)
        {
            throw new InvalidDataException(
                "A Replace receipt does not match its journal entry.");
        }

        return new PersistedOperation(
            operationId,
            encoded.RequestDigest,
            receipt);
    }

    private static ReceiptDto EncodeReceipt(OperationReceipt receipt) => new(
        receipt.OperationId.ToString(),
        receipt.CorrelationId.ToString(),
        checked((int)receipt.Kind),
        checked((int)receipt.Status),
        receipt.SourceDeviceId.ToString(),
        receipt.TargetDeviceId.ToString(),
        receipt.ActivityId.ToString(),
        receipt.ActivityKind?.Value,
        receipt.DescriptorDigest,
        receipt.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
        checked((int)receipt.FailureCode));

    private static OperationReceipt DecodeReceipt(ReceiptDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException("A Replace journal receipt cannot be null.");
        }

        if (!Enum.IsDefined(typeof(OperationKind), encoded.Kind)
            || !Enum.IsDefined(typeof(OperationStatus), encoded.Status)
            || !Enum.IsDefined(typeof(FailureCode), encoded.FailureCode))
        {
            throw new InvalidDataException(
                "A Replace journal receipt contains an unknown enum value.");
        }

        ActivityKind? activityKind = encoded.ActivityKind is null
            ? null
            : ActivityKind.Parse(encoded.ActivityKind);
        if (encoded.DescriptorDigest is not null
            && (encoded.DescriptorDigest.Length != 64
                || !encoded.DescriptorDigest.All(char.IsAsciiHexDigit)))
        {
            throw new InvalidDataException(
                "A Replace journal receipt contains an invalid descriptor digest.");
        }

        return OperationReceipt.FromRecordedResult(
            OperationId.Parse(encoded.OperationId),
            CorrelationId.Parse(encoded.CorrelationId),
            (OperationKind)encoded.Kind,
            (OperationStatus)encoded.Status,
            DeviceId.Parse(encoded.SourceDeviceId),
            DeviceId.Parse(encoded.TargetDeviceId),
            ActivityId.Parse(encoded.ActivityId),
            activityKind,
            encoded.DescriptorDigest,
            ParseTimestamp(encoded.OccurredAt, "receipt"),
            (FailureCode)encoded.FailureCode);
    }

    private static void ValidateRequestDigest(string requestDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestDigest);
        if (requestDigest.Length > 256 || requestDigest.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "A Replace journal request digest must contain 1 to 256 non-control characters.");
        }
    }

    private static CapsuleDto EncodeCapsule(UndoCapsule capsule)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        return new CapsuleDto(
            capsule.Id.ToString(),
            capsule.OperationId.ToString(),
            capsule.CorrelationId.ToString(),
            capsule.SourceDeviceId.ToString(),
            capsule.TargetDeviceId.ToString(),
            EncodeActivity(capsule.OriginalActivity),
            EncodeActivity(capsule.ReplacementActivity),
            capsule.CapturedAt.ToString("O", CultureInfo.InvariantCulture),
            capsule.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static UndoCapsule DecodeCapsule(CapsuleDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException("A Replace state capsule cannot be null.");
        }

        DateTimeOffset capturedAt = ParseTimestamp(encoded.CapturedAt, "capture");
        DateTimeOffset expiresAt = ParseTimestamp(encoded.ExpiresAt, "expiry");
        OperationId operationId = OperationId.Parse(encoded.OperationId);
        CorrelationId correlationId = CorrelationId.Parse(encoded.CorrelationId);
        return UndoCapsule.Create(
            UndoCapsuleId.Parse(encoded.Id),
            OperationContext.Create(operationId, correlationId, expiresAt),
            DeviceId.Parse(encoded.SourceDeviceId),
            DeviceId.Parse(encoded.TargetDeviceId),
            DecodeActivity(encoded.OriginalActivity),
            DecodeActivity(encoded.ReplacementActivity),
            capturedAt,
            expiresAt);
    }

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

    private static ActivityInstance DecodeActivity(ActivityDto encoded)
    {
        if (encoded is null)
        {
            throw new InvalidDataException("A Replace state Activity cannot be null.");
        }

        if (!Enum.IsDefined(typeof(ActivitySensitivity), encoded.Sensitivity)
            || !Enum.IsDefined(typeof(ActivityLifecycle), encoded.Lifecycle)
            || (ActivityLifecycle)encoded.Lifecycle != ActivityLifecycle.Active)
        {
            throw new InvalidDataException(
                "A Replace state Activity contains an invalid lifecycle or sensitivity.");
        }

        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse(encoded.Id),
            ActivityKind.Parse(encoded.Kind),
            DeviceId.Parse(encoded.OriginDeviceId),
            encoded.Title,
            encoded.PayloadJson,
            (ActivitySensitivity)encoded.Sensitivity);
        if (!DigestsEqual(descriptor.PayloadDigest, encoded.PayloadDigest)
            || !DigestsEqual(descriptor.DescriptorDigest, encoded.DescriptorDigest))
        {
            throw new InvalidDataException(
                "A Replace state Activity descriptor digest does not match its content.");
        }

        return ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(
                DeviceId.Parse(encoded.PlacementDeviceId),
                encoded.PlacementSlot),
            encoded.Revision);
    }

    private static bool DigestsEqual(string computed, string encoded)
    {
        if (encoded.Length != 64 || !encoded.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(computed),
            Convert.FromHexString(encoded));
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
                $"The Replace state {field} timestamp is not canonical UTC.");
        }

        return timestamp;
    }

    private sealed record StateDto(
        int FormatVersion,
        CapsuleDto[] Capsules,
        OperationDto[] Operations,
        UndoOperationDto[] UndoOperations);

    private sealed record OperationDto(
        string OperationId,
        string RequestDigest,
        ReceiptDto? Receipt);

    private sealed record ReceiptDto(
        string OperationId,
        string CorrelationId,
        int Kind,
        int Status,
        string SourceDeviceId,
        string TargetDeviceId,
        string ActivityId,
        string? ActivityKind,
        string? DescriptorDigest,
        string OccurredAt,
        int FailureCode);

    private sealed record UndoOperationDto(
        string OperationId,
        string CapsuleId,
        string RequestDigest,
        UndoResultDto? Result);

    private sealed record UndoResultDto(
        string OperationId,
        string CorrelationId,
        string CapsuleId,
        int Status,
        int FailureCode,
        string OccurredAt);

    private sealed record CapsuleDto(
        string Id,
        string OperationId,
        string CorrelationId,
        string SourceDeviceId,
        string TargetDeviceId,
        ActivityDto OriginalActivity,
        ActivityDto ReplacementActivity,
        string CapturedAt,
        string ExpiresAt);

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
