using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SwapTransactionParticipant
{
    private SwapTransactionParticipant(
        DeviceId deviceId,
        ActivityId activityId,
        long expectedRevision,
        string expectedDescriptorDigest,
        SwapReservationToken reservationToken)
    {
        DeviceId = deviceId;
        ActivityId = activityId;
        ExpectedRevision = expectedRevision;
        ExpectedDescriptorDigest = expectedDescriptorDigest;
        ReservationToken = reservationToken;
    }

    public DeviceId DeviceId { get; }

    public ActivityId ActivityId { get; }

    public long ExpectedRevision { get; }

    public string ExpectedDescriptorDigest { get; }

    public SwapReservationToken ReservationToken { get; }

    public static SwapTransactionParticipant Create(
        DeviceId deviceId,
        ActivityId activityId,
        long expectedRevision,
        string expectedDescriptorDigest,
        SwapReservationToken reservationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        ValidateDigest(expectedDescriptorDigest);
        ArgumentNullException.ThrowIfNull(reservationToken);
        return new SwapTransactionParticipant(
            deviceId,
            activityId,
            expectedRevision,
            expectedDescriptorDigest,
            reservationToken);
    }

    public static SwapTransactionParticipant FromActivity(
        ActivityInstance activity,
        SwapReservationToken reservationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Lifecycle != ActivityLifecycle.Active)
        {
            throw new InvalidOperationException(
                "Only an active Activity can identify a swap transaction participant.");
        }

        return Create(
            activity.Placement.DeviceId,
            activity.Descriptor.Id,
            activity.Revision,
            activity.Descriptor.DescriptorDigest,
            reservationToken);
    }

    internal SwapDecisionParticipant ToDecisionParticipant() =>
        SwapDecisionParticipant.Create(DeviceId, ReservationToken);

    internal static void ValidateDigest(string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        if (digest.Length != 64
            || !digest.All(static character =>
                char.IsAsciiDigit(character)
                || character is >= 'A' and <= 'F'))
        {
            throw new ArgumentException(
                "A swap transaction descriptor digest must be canonical uppercase hexadecimal.",
                nameof(digest));
        }
    }
}

public sealed record SwapCoordinatorTransaction
{
    private SwapCoordinatorTransaction(
        OperationContext context,
        ImmutableArray<SwapTransactionParticipant> participants,
        string requestDigest,
        SwapDecision? decision)
    {
        Context = context;
        Participants = participants;
        RequestDigest = requestDigest;
        Decision = decision;
    }

    public OperationContext Context { get; }

    public ImmutableArray<SwapTransactionParticipant> Participants { get; }

    public string RequestDigest { get; }

    public SwapDecision? Decision { get; }

    public static SwapCoordinatorTransaction Create(
        OperationContext context,
        ActivityInstance firstActivity,
        SwapReservationToken firstToken,
        ActivityInstance secondActivity,
        SwapReservationToken secondToken) => CreateRecorded(
            context,
            [
                SwapTransactionParticipant.FromActivity(firstActivity, firstToken),
                SwapTransactionParticipant.FromActivity(secondActivity, secondToken),
            ],
            null,
            null);

    internal static SwapCoordinatorTransaction CreateRecorded(
        OperationContext context,
        IEnumerable<SwapTransactionParticipant> participants,
        SwapDecision? decision,
        string? expectedRequestDigest)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(participants);
        if (context.Deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A swap transaction deadline must be UTC.",
                nameof(context));
        }

        ImmutableArray<SwapTransactionParticipant> ordered = participants
            .OrderBy(
                static participant => participant.DeviceId.ToString(),
                StringComparer.Ordinal)
            .ToImmutableArray();
        if (ordered.Length != 2
            || ordered.Select(static participant => participant.DeviceId)
                .Distinct()
                .Count() != 2
            || ordered.Select(static participant => participant.ActivityId)
                .Distinct()
                .Count() != 2
            || ordered.Select(static participant => participant.ReservationToken)
                .Distinct()
                .Count() != 2)
        {
            throw new ArgumentException(
                "A swap transaction requires two distinct Device, Activity, and token participants.",
                nameof(participants));
        }

        string requestDigest = ComputeRequestDigest(context, ordered);
        if (expectedRequestDigest is not null
            && !DigestsEqual(requestDigest, expectedRequestDigest))
        {
            throw new InvalidDataException(
                "The swap transaction request digest does not match its fields.");
        }

        var transaction = new SwapCoordinatorTransaction(
            context,
            ordered,
            requestDigest,
            null);
        return decision is null ? transaction : transaction.WithDecision(decision);
    }

    public bool MatchesRequest(
        OperationContext context,
        DeviceId firstDeviceId,
        ActivityId firstActivityId,
        DeviceId secondDeviceId,
        ActivityId secondActivityId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(firstDeviceId);
        ArgumentNullException.ThrowIfNull(firstActivityId);
        ArgumentNullException.ThrowIfNull(secondDeviceId);
        ArgumentNullException.ThrowIfNull(secondActivityId);
        return Context == context
            && MatchesParticipant(firstDeviceId, firstActivityId)
            && MatchesParticipant(secondDeviceId, secondActivityId)
            && firstDeviceId != secondDeviceId;
    }

    public bool MatchesParticipants(
        DeviceId firstDeviceId,
        DeviceId secondDeviceId)
    {
        ArgumentNullException.ThrowIfNull(firstDeviceId);
        ArgumentNullException.ThrowIfNull(secondDeviceId);
        return firstDeviceId != secondDeviceId
            && Participants.Any(participant => participant.DeviceId == firstDeviceId)
            && Participants.Any(participant => participant.DeviceId == secondDeviceId);
    }

    public SwapTransactionParticipant GetParticipant(DeviceId deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        return Participants.Single(participant => participant.DeviceId == deviceId);
    }

    public SwapDecision CreateDecision(
        SwapDecisionOutcome outcome,
        DateTimeOffset decidedAt,
        FailureCode failureCode = FailureCode.None) => SwapDecision.Create(
            Context.OperationId,
            outcome,
            decidedAt,
            Participants.Select(static participant => participant.ToDecisionParticipant()),
            failureCode);

    public SwapCoordinatorTransaction WithDecision(SwapDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.OperationId != Context.OperationId
            || decision.Participants.Length != Participants.Length
            || decision.Participants.Any(decisionParticipant =>
                !Participants.Any(participant =>
                    participant.DeviceId == decisionParticipant.DeviceId
                    && participant.ReservationToken
                        == decisionParticipant.ReservationToken)))
        {
            throw new InvalidOperationException(
                "A swap decision does not match its coordinator transaction.");
        }

        if (Decision is not null)
        {
            return StringComparer.Ordinal.Equals(Decision.Digest, decision.Digest)
                ? this
                : throw new InvalidOperationException(
                    "A decided swap transaction cannot accept another decision.");
        }

        return new SwapCoordinatorTransaction(
            Context,
            Participants,
            RequestDigest,
            decision);
    }

    private bool MatchesParticipant(DeviceId deviceId, ActivityId activityId) =>
        Participants.Any(participant =>
            participant.DeviceId == deviceId
            && participant.ActivityId == activityId);

    private static string ComputeRequestDigest(
        OperationContext context,
        IEnumerable<SwapTransactionParticipant> participants)
    {
        var material = new StringBuilder()
            .Append(context.OperationId.ToString()).Append('\n')
            .Append(context.CorrelationId.ToString()).Append('\n')
            .Append(context.Deadline.ToString("O", CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (SwapTransactionParticipant participant in participants)
        {
            material
                .Append(participant.DeviceId.ToString()).Append('\n')
                .Append(participant.ActivityId.ToString()).Append('\n')
                .Append(participant.ExpectedRevision.ToString(
                    CultureInfo.InvariantCulture)).Append('\n')
                .Append(participant.ExpectedDescriptorDigest).Append('\n')
                .Append(participant.ReservationToken.ToString()).Append('\n');
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private static bool DigestsEqual(string computed, string encoded)
    {
        SwapTransactionParticipant.ValidateDigest(encoded);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(computed),
            Convert.FromHexString(encoded));
    }
}

public enum SwapTransactionWriteStatus
{
    Stored,
    Replayed,
    Conflict,
    CapacityExceeded,
}

public sealed record SwapTransactionWriteResult(
    SwapTransactionWriteStatus Status,
    SwapCoordinatorTransaction? Transaction);

public interface ISwapTransactionJournal
{
    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapCoordinatorTransaction? transaction);

    public ValueTask<SwapTransactionWriteResult> TryCreateAsync(
        SwapCoordinatorTransaction transaction,
        CancellationToken cancellationToken = default);

    public ValueTask<SwapTransactionWriteResult> TryRecordDecisionAsync(
        OperationId operationId,
        SwapDecision decision,
        CancellationToken cancellationToken = default);
}

public sealed class InMemorySwapTransactionJournal : ISwapTransactionJournal
{
    private readonly Lock gate = new();
    private readonly Dictionary<OperationId, SwapCoordinatorTransaction> transactions = [];

    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapCoordinatorTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        lock (gate)
        {
            return transactions.TryGetValue(operationId, out transaction);
        }
    }

    public ValueTask<SwapTransactionWriteResult> TryCreateAsync(
        SwapCoordinatorTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();
        if (transaction.Decision is not null)
        {
            throw new ArgumentException(
                "A new swap transaction journal entry cannot already contain a decision.",
                nameof(transaction));
        }

        lock (gate)
        {
            if (transactions.TryGetValue(
                    transaction.Context.OperationId,
                    out SwapCoordinatorTransaction? existing))
            {
                return ValueTask.FromResult(new SwapTransactionWriteResult(
                    StringComparer.Ordinal.Equals(
                        existing.RequestDigest,
                        transaction.RequestDigest)
                        ? SwapTransactionWriteStatus.Replayed
                        : SwapTransactionWriteStatus.Conflict,
                    existing));
            }

            if (transactions.Count
                >= PersistentSwapTransactionJournal.MaximumTransactionCount)
            {
                return ValueTask.FromResult(new SwapTransactionWriteResult(
                    SwapTransactionWriteStatus.CapacityExceeded,
                    null));
            }

            transactions.Add(transaction.Context.OperationId, transaction);
            return ValueTask.FromResult(new SwapTransactionWriteResult(
                SwapTransactionWriteStatus.Stored,
                transaction));
        }
    }

    public ValueTask<SwapTransactionWriteResult> TryRecordDecisionAsync(
        OperationId operationId,
        SwapDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!transactions.TryGetValue(
                    operationId,
                    out SwapCoordinatorTransaction? existing))
            {
                return ValueTask.FromResult(new SwapTransactionWriteResult(
                    SwapTransactionWriteStatus.Conflict,
                    null));
            }

            if (existing.Decision is not null)
            {
                return ValueTask.FromResult(new SwapTransactionWriteResult(
                    StringComparer.Ordinal.Equals(existing.Decision.Digest, decision.Digest)
                        ? SwapTransactionWriteStatus.Replayed
                        : SwapTransactionWriteStatus.Conflict,
                    existing));
            }

            SwapCoordinatorTransaction decided;
            try
            {
                decided = existing.WithDecision(decision);
            }
            catch (InvalidOperationException)
            {
                return ValueTask.FromResult(new SwapTransactionWriteResult(
                    SwapTransactionWriteStatus.Conflict,
                    existing));
            }

            transactions[operationId] = decided;
            return ValueTask.FromResult(new SwapTransactionWriteResult(
                SwapTransactionWriteStatus.Stored,
                decided));
        }
    }
}

public interface ISwapStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class SwapStatePersistenceException : IOException
{
    public SwapStatePersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistentSwapTransactionJournal :
    ISwapTransactionJournal,
    IDisposable
{
    public const int MaximumPayloadBytes = 1024 * 1024;
    public const int MaximumTransactionCount = 256;

    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ISwapStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private bool requiresReload;
    private Dictionary<OperationId, SwapCoordinatorTransaction> transactions;

    private PersistentSwapTransactionJournal(
        ISwapStatePayloadStore payloadStore,
        IEnumerable<SwapCoordinatorTransaction> transactions)
    {
        this.payloadStore = payloadStore;
        this.transactions = transactions.ToDictionary(
            static transaction => transaction.Context.OperationId);
    }

    public int Count
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (snapshotGate)
            {
                return transactions.Count;
            }
        }
    }

    public static async ValueTask<PersistentSwapTransactionJournal> OpenAsync(
        ISwapStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentSwapTransactionJournal(payloadStore, []);
        }

        try
        {
            return new PersistentSwapTransactionJournal(
                payloadStore,
                SwapTransactionPayloadCodec.Decode(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapCoordinatorTransaction? transaction)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(operationId);
        lock (snapshotGate)
        {
            return transactions.TryGetValue(operationId, out transaction);
        }
    }

    public async ValueTask<SwapTransactionWriteResult> TryCreateAsync(
        SwapCoordinatorTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Decision is not null)
        {
            throw new ArgumentException(
                "A new swap transaction journal entry cannot already contain a decision.",
                nameof(transaction));
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<OperationId, SwapCoordinatorTransaction> candidate = Snapshot();
            if (candidate.TryGetValue(
                    transaction.Context.OperationId,
                    out SwapCoordinatorTransaction? existing))
            {
                return new SwapTransactionWriteResult(
                    StringComparer.Ordinal.Equals(
                        existing.RequestDigest,
                        transaction.RequestDigest)
                        ? SwapTransactionWriteStatus.Replayed
                        : SwapTransactionWriteStatus.Conflict,
                    existing);
            }

            if (candidate.Count >= MaximumTransactionCount)
            {
                return new SwapTransactionWriteResult(
                    SwapTransactionWriteStatus.CapacityExceeded,
                    null);
            }

            candidate.Add(transaction.Context.OperationId, transaction);
            await SaveAndPublishAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new SwapTransactionWriteResult(
                SwapTransactionWriteStatus.Stored,
                transaction);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<SwapTransactionWriteResult> TryRecordDecisionAsync(
        OperationId operationId,
        SwapDecision decision,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(decision);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<OperationId, SwapCoordinatorTransaction> candidate = Snapshot();
            if (!candidate.TryGetValue(
                    operationId,
                    out SwapCoordinatorTransaction? existing))
            {
                return new SwapTransactionWriteResult(
                    SwapTransactionWriteStatus.Conflict,
                    null);
            }

            if (existing.Decision is not null)
            {
                return new SwapTransactionWriteResult(
                    StringComparer.Ordinal.Equals(existing.Decision.Digest, decision.Digest)
                        ? SwapTransactionWriteStatus.Replayed
                        : SwapTransactionWriteStatus.Conflict,
                    existing);
            }

            SwapCoordinatorTransaction decided;
            try
            {
                decided = existing.WithDecision(decision);
            }
            catch (InvalidOperationException)
            {
                return new SwapTransactionWriteResult(
                    SwapTransactionWriteStatus.Conflict,
                    existing);
            }

            candidate[operationId] = decided;
            await SaveAndPublishAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new SwapTransactionWriteResult(
                SwapTransactionWriteStatus.Stored,
                decided);
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

    private Dictionary<OperationId, SwapCoordinatorTransaction> Snapshot()
    {
        lock (snapshotGate)
        {
            return new Dictionary<OperationId, SwapCoordinatorTransaction>(transactions);
        }
    }

    private async ValueTask SaveAndPublishAsync(
        Dictionary<OperationId, SwapCoordinatorTransaction> candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = SwapTransactionPayloadCodec.Encode(candidate.Values);
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

            throw new SwapStatePersistenceException(
                "The durable swap transaction state could not be saved.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            transactions = candidate;
        }
    }

    private void ThrowIfReloadRequired()
    {
        if (requiresReload)
        {
            throw new SwapStatePersistenceException(
                "The swap transaction journal must be reopened after an ambiguous save failure.",
                new IOException("The prior durable save outcome is unknown."));
        }
    }
}

internal static class SwapTransactionPayloadCodec
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 16,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<SwapCoordinatorTransaction> Decode(
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty
            || payload.Length > PersistentSwapTransactionJournal.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"A swap transaction payload must contain 1 to {PersistentSwapTransactionJournal.MaximumPayloadBytes} bytes.");
        }

        try
        {
            StateDto? state = JsonSerializer.Deserialize<StateDto>(payload, SerializerOptions);
            if (state is null
                || state.FormatVersion != CurrentFormatVersion
                || state.Transactions is null)
            {
                throw new InvalidDataException(
                    "The swap transaction payload has an unsupported or incomplete envelope.");
            }

            if (state.Transactions.Length
                > PersistentSwapTransactionJournal.MaximumTransactionCount)
            {
                throw new InvalidDataException(
                    $"A swap transaction payload cannot contain more than {PersistentSwapTransactionJournal.MaximumTransactionCount} transactions.");
            }

            var decoded = new List<SwapCoordinatorTransaction>(state.Transactions.Length);
            string? previousOperationId = null;
            foreach (TransactionDto encoded in state.Transactions)
            {
                SwapCoordinatorTransaction transaction = DecodeTransaction(encoded);
                string operationId = transaction.Context.OperationId.ToString();
                if (previousOperationId is not null
                    && StringComparer.Ordinal.Compare(previousOperationId, operationId) >= 0)
                {
                    throw new InvalidDataException(
                        "Swap transactions are duplicated or not canonically ordered.");
                }

                previousOperationId = operationId;
                decoded.Add(transaction);
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
                "The swap transaction payload is malformed.",
                exception);
        }
    }

    public static byte[] Encode(IEnumerable<SwapCoordinatorTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        SwapCoordinatorTransaction[] ordered = transactions
            .OrderBy(
                static transaction => transaction.Context.OperationId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > PersistentSwapTransactionJournal.MaximumTransactionCount
            || ordered.Select(static transaction => transaction.Context.OperationId)
                .Distinct()
                .Count() != ordered.Length)
        {
            throw new InvalidDataException(
                "Swap transactions exceed bounds or contain duplicate Operation IDs.");
        }

        var state = new StateDto(
            CurrentFormatVersion,
            ordered.Select(EncodeTransaction).ToArray());
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
        if (payload.Length > PersistentSwapTransactionJournal.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                $"A swap transaction payload cannot exceed {PersistentSwapTransactionJournal.MaximumPayloadBytes} bytes.");
        }

        return payload;
    }

    private static TransactionDto EncodeTransaction(
        SwapCoordinatorTransaction transaction) => new(
            transaction.Context.OperationId.ToString(),
            transaction.Context.CorrelationId.ToString(),
            transaction.Context.Deadline.ToString("O", CultureInfo.InvariantCulture),
            transaction.RequestDigest,
            transaction.Participants.Select(static participant => new ParticipantDto(
                participant.DeviceId.ToString(),
                participant.ActivityId.ToString(),
                participant.ExpectedRevision,
                participant.ExpectedDescriptorDigest,
                participant.ReservationToken.ToString())).ToArray(),
            transaction.Decision is null
                ? null
                : new DecisionDto(
                    checked((int)transaction.Decision.Outcome),
                    transaction.Decision.DecidedAt.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    checked((int)transaction.Decision.FailureCode),
                    transaction.Decision.Digest));

    private static SwapCoordinatorTransaction DecodeTransaction(TransactionDto encoded)
    {
        if (encoded is null || encoded.Participants is null)
        {
            throw new InvalidDataException("A swap transaction cannot be null or incomplete.");
        }

        DateTimeOffset deadline = ParseTimestamp(encoded.Deadline, "deadline");
        var context = OperationContext.Create(
            OperationId.Parse(encoded.OperationId),
            CorrelationId.Parse(encoded.CorrelationId),
            deadline);
        if (encoded.Participants.Any(static participant => participant is null))
        {
            throw new InvalidDataException(
                "A swap transaction participant cannot be null.");
        }

        SwapTransactionParticipant[] participants = encoded.Participants
            .Select(static participant => SwapTransactionParticipant.Create(
                DeviceId.Parse(participant.DeviceId),
                ActivityId.Parse(participant.ActivityId),
                participant.ExpectedRevision,
                participant.ExpectedDescriptorDigest,
                SwapReservationToken.From(Guid.Parse(participant.ReservationToken))))
            .ToArray();
        string[] encodedOrder = encoded.Participants
            .Select(static participant => participant.DeviceId)
            .ToArray();
        if (!encodedOrder.SequenceEqual(
                encodedOrder.OrderBy(static value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Swap transaction participants are not canonically ordered.");
        }

        SwapDecision? decision = null;
        if (encoded.Decision is not null)
        {
            if (!Enum.IsDefined(typeof(SwapDecisionOutcome), encoded.Decision.Outcome)
                || !Enum.IsDefined(typeof(FailureCode), encoded.Decision.FailureCode))
            {
                throw new InvalidDataException(
                    "A swap transaction decision contains an unknown enum value.");
            }

            decision = SwapDecision.Create(
                context.OperationId,
                (SwapDecisionOutcome)encoded.Decision.Outcome,
                ParseTimestamp(encoded.Decision.DecidedAt, "decision"),
                participants.Select(static participant =>
                    participant.ToDecisionParticipant()),
                (FailureCode)encoded.Decision.FailureCode);
            SwapTransactionParticipant.ValidateDigest(encoded.Decision.Digest);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(decision.Digest),
                    Convert.FromHexString(encoded.Decision.Digest)))
            {
                throw new InvalidDataException(
                    "The swap decision digest does not match its fields.");
            }
        }

        return SwapCoordinatorTransaction.CreateRecorded(
            context,
            participants,
            decision,
            encoded.RequestDigest);
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
                $"The swap transaction {field} timestamp is not canonical UTC.");
        }

        return timestamp;
    }

    private sealed record StateDto(
        int FormatVersion,
        TransactionDto[] Transactions);

    private sealed record TransactionDto(
        string OperationId,
        string CorrelationId,
        string Deadline,
        string RequestDigest,
        ParticipantDto[] Participants,
        DecisionDto? Decision);

    private sealed record ParticipantDto(
        string DeviceId,
        string ActivityId,
        long ExpectedRevision,
        string ExpectedDescriptorDigest,
        string ReservationToken);

    private sealed record DecisionDto(
        int Outcome,
        string DecidedAt,
        int FailureCode,
        string Digest);
}
