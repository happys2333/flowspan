using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record ReplaceActivityCommand
{
    private ReplaceActivityCommand(
        OperationContext context,
        ActivityId targetActivityId,
        long expectedTargetRevision,
        string expectedTargetDescriptorDigest,
        ActivityDescriptor incomingDescriptor,
        ActivityPlacement targetPlacement,
        DateTimeOffset undoExpiresAt,
        string requestDigest)
    {
        Context = context;
        TargetActivityId = targetActivityId;
        ExpectedTargetRevision = expectedTargetRevision;
        ExpectedTargetDescriptorDigest = expectedTargetDescriptorDigest;
        IncomingDescriptor = incomingDescriptor;
        TargetPlacement = targetPlacement;
        UndoExpiresAt = undoExpiresAt;
        RequestDigest = requestDigest;
    }

    public OperationContext Context { get; }

    public ActivityId TargetActivityId { get; }

    public long ExpectedTargetRevision { get; }

    public string ExpectedTargetDescriptorDigest { get; }

    public ActivityDescriptor IncomingDescriptor { get; }

    public ActivityPlacement TargetPlacement { get; }

    public DateTimeOffset UndoExpiresAt { get; }

    public string RequestDigest { get; }

    public static ReplaceActivityCommand Create(
        OperationContext context,
        ActivityId targetActivityId,
        long expectedTargetRevision,
        string expectedTargetDescriptorDigest,
        ActivityDescriptor incomingDescriptor,
        ActivityPlacement targetPlacement,
        DateTimeOffset undoExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetActivityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedTargetRevision, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTargetDescriptorDigest);
        if (expectedTargetDescriptorDigest.Length != 64
            || !expectedTargetDescriptorDigest.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                "An expected target descriptor digest must be a 32-byte hexadecimal value.",
                nameof(expectedTargetDescriptorDigest));
        }

        string normalizedTargetDigest = expectedTargetDescriptorDigest.ToUpperInvariant();
        ArgumentNullException.ThrowIfNull(incomingDescriptor);
        ArgumentNullException.ThrowIfNull(targetPlacement);

        string digestInput = string.Join(
            '\n',
            OperationKind.Replace.ToString(),
            context.OperationId.ToString(),
            context.CorrelationId.ToString(),
            targetActivityId.ToString(),
            expectedTargetRevision.ToString(CultureInfo.InvariantCulture),
            normalizedTargetDigest,
            incomingDescriptor.Id.ToString(),
            incomingDescriptor.Kind.Value,
            incomingDescriptor.DescriptorDigest,
            targetPlacement.DeviceId.ToString(),
            targetPlacement.Slot,
            context.Deadline.ToString("O", CultureInfo.InvariantCulture),
            undoExpiresAt.ToString("O", CultureInfo.InvariantCulture));
        string requestDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(digestInput)));

        return new ReplaceActivityCommand(
            context,
            targetActivityId,
            expectedTargetRevision,
            normalizedTargetDigest,
            incomingDescriptor,
            targetPlacement,
            undoExpiresAt,
            requestDigest);
    }

    public string BindAuthenticatedSender(DeviceId senderDeviceId)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        string material = string.Join('\n', senderDeviceId.ToString(), RequestDigest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

public sealed record UndoCapsule
{
    private UndoCapsule(
        UndoCapsuleId id,
        OperationId operationId,
        CorrelationId correlationId,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityInstance originalActivity,
        ActivityInstance replacementActivity,
        DateTimeOffset capturedAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        OperationId = operationId;
        CorrelationId = correlationId;
        SourceDeviceId = sourceDeviceId;
        TargetDeviceId = targetDeviceId;
        OriginalActivity = originalActivity;
        ReplacementActivity = replacementActivity;
        CapturedAt = capturedAt;
        ExpiresAt = expiresAt;
        Reference = new UndoCapsuleReference(
            id,
            operationId,
            correlationId,
            originalActivity.Descriptor.Id,
            originalActivity.Revision,
            originalActivity.Descriptor.DescriptorDigest,
            replacementActivity.Descriptor.Id,
            replacementActivity.Descriptor.DescriptorDigest,
            expiresAt);
    }

    public UndoCapsuleId Id { get; }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public DeviceId SourceDeviceId { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityInstance OriginalActivity { get; }

    public ActivityInstance ReplacementActivity { get; }

    public ActivityId TargetActivityId => OriginalActivity.Descriptor.Id;

    public long ExpectedTargetRevision => OriginalActivity.Revision;

    public string TargetDescriptorDigest => OriginalActivity.Descriptor.DescriptorDigest;

    public string IncomingDescriptorDigest => ReplacementActivity.Descriptor.DescriptorDigest;

    public DateTimeOffset CapturedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public UndoCapsuleReference Reference { get; }

    public static UndoCapsule Create(
        UndoCapsuleId id,
        OperationContext context,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityInstance originalActivity,
        ActivityInstance replacementActivity,
        DateTimeOffset capturedAt,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceDeviceId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(originalActivity);
        ArgumentNullException.ThrowIfNull(replacementActivity);
        if (originalActivity.Placement.DeviceId != targetDeviceId
            || replacementActivity.Placement.DeviceId != targetDeviceId)
        {
            throw new ArgumentException(
                "Undo capsule Activities must be placed on the target device.",
                nameof(targetDeviceId));
        }

        if (expiresAt <= capturedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "An undo capsule must expire after it is captured.");
        }

        return new UndoCapsule(
            id,
            context.OperationId,
            context.CorrelationId,
            sourceDeviceId,
            targetDeviceId,
            originalActivity,
            replacementActivity,
            capturedAt,
            expiresAt);
    }
}

public sealed record UndoCapsuleReference(
    UndoCapsuleId Id,
    OperationId OperationId,
    CorrelationId CorrelationId,
    ActivityId TargetActivityId,
    long ExpectedTargetRevision,
    string TargetDescriptorDigest,
    ActivityId IncomingActivityId,
    string IncomingDescriptorDigest,
    DateTimeOffset ExpiresAt);

public sealed record ReplaceOperationResult(
    OperationReceipt Receipt,
    UndoCapsuleReference? UndoCapsule);

public interface IReplacePeer
{
    public DeviceId DeviceId { get; }

    public ValueTask<ReplaceOperationResult> ReplaceAsync(
        DeviceId senderDeviceId,
        ReplaceActivityCommand command,
        CancellationToken cancellationToken);
}

public interface IReplaceChannel
{
    public DeviceId TargetDeviceId { get; }

    public ValueTask<ReplaceDeliveryResult> SendAsync(
        DeviceId senderDeviceId,
        ReplaceActivityCommand command,
        CancellationToken cancellationToken);
}

public readonly record struct ReplaceDeliveryResult(
    ActivityDeliveryStatus Status,
    ReplaceOperationResult? Result)
{
    public static ReplaceDeliveryResult Acknowledged(ReplaceOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReplaceDeliveryResult(ActivityDeliveryStatus.Acknowledged, result);
    }

    public static ReplaceDeliveryResult NotDelivered { get; } =
        new(ActivityDeliveryStatus.NotDelivered, null);

    public static ReplaceDeliveryResult AcknowledgementLost { get; } =
        new(ActivityDeliveryStatus.AcknowledgementLost, null);
}

public sealed record UndoReplaceResult
{
    private UndoReplaceResult(
        OperationId operationId,
        CorrelationId correlationId,
        UndoCapsuleId capsuleId,
        OperationStatus status,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        OperationId = operationId;
        CorrelationId = correlationId;
        CapsuleId = capsuleId;
        Status = status;
        FailureCode = failureCode;
        OccurredAt = occurredAt;
    }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public UndoCapsuleId CapsuleId { get; }

    public OperationStatus Status { get; }

    public FailureCode FailureCode { get; }

    public DateTimeOffset OccurredAt { get; }

    public bool IsSuccess => Status is OperationStatus.Committed
        or OperationStatus.CommittedWithWarning;

    public static UndoReplaceResult Committed(
        OperationContext context,
        UndoCapsuleId capsuleId,
        DateTimeOffset occurredAt) => Create(
            context,
            capsuleId,
            OperationStatus.Committed,
            FailureCode.None,
            occurredAt);

    public static UndoReplaceResult Rejected(
        OperationContext context,
        UndoCapsuleId capsuleId,
        FailureCode failureCode,
        DateTimeOffset occurredAt) => CreateFailure(
            context,
            capsuleId,
            OperationStatus.Rejected,
            failureCode,
            occurredAt);

    public static UndoReplaceResult Failed(
        OperationContext context,
        UndoCapsuleId capsuleId,
        FailureCode failureCode,
        DateTimeOffset occurredAt) => CreateFailure(
            context,
            capsuleId,
            OperationStatus.Failed,
            failureCode,
            occurredAt);

    public static UndoReplaceResult Recovering(
        OperationContext context,
        UndoCapsuleId capsuleId,
        FailureCode failureCode,
        DateTimeOffset occurredAt) => CreateFailure(
            context,
            capsuleId,
            OperationStatus.Recovering,
            failureCode,
            occurredAt);

    public static UndoReplaceResult FromRecordedResult(
        OperationId operationId,
        CorrelationId correlationId,
        UndoCapsuleId capsuleId,
        OperationStatus status,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(capsuleId);
        if (status is not (
                OperationStatus.Committed
                or OperationStatus.Rejected
                or OperationStatus.Failed
                or OperationStatus.Recovering))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        bool expectsFailure = status != OperationStatus.Committed;
        if (expectsFailure == (failureCode == FailureCode.None))
        {
            throw new ArgumentException(
                "The recorded undo status and failure code are inconsistent.",
                nameof(failureCode));
        }

        return new UndoReplaceResult(
            operationId,
            correlationId,
            capsuleId,
            status,
            failureCode,
            occurredAt);
    }

    private static UndoReplaceResult CreateFailure(
        OperationContext context,
        UndoCapsuleId capsuleId,
        OperationStatus status,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "An unsuccessful undo result must have a failure code.",
                nameof(failureCode));
        }

        return Create(context, capsuleId, status, failureCode, occurredAt);
    }

    private static UndoReplaceResult Create(
        OperationContext context,
        UndoCapsuleId capsuleId,
        OperationStatus status,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capsuleId);
        return new UndoReplaceResult(
            context.OperationId,
            context.CorrelationId,
            capsuleId,
            status,
            failureCode,
            occurredAt);
    }
}

public interface IReplaceStateStore
{
    public ValueTask<bool> TryAddAsync(
        UndoCapsule capsule,
        CancellationToken cancellationToken = default);

    public bool TryGet(
        UndoCapsuleId capsuleId,
        [NotNullWhen(true)] out UndoCapsule? capsule);

    public bool TryGetByOperation(
        OperationId operationId,
        [NotNullWhen(true)] out UndoCapsule? capsule);

    public ValueTask<bool> TryRemoveAsync(
        UndoCapsuleId capsuleId,
        CancellationToken cancellationToken = default);

    public ValueTask<UndoJournalPreparation> PrepareUndoAsync(
        UndoCapsuleId capsuleId,
        OperationId operationId,
        string requestDigest,
        CancellationToken cancellationToken = default);

    public ValueTask CompleteUndoAsync(
        OperationId operationId,
        UndoReplaceResult result,
        CancellationToken cancellationToken = default);
}

public enum UndoJournalPreparationStatus
{
    Prepared,
    PreparedConsumed,
    Replay,
    Conflict,
    RecoveryRequired,
    CapsuleReserved,
}

public readonly record struct UndoJournalPreparation(
    UndoJournalPreparationStatus Status,
    UndoReplaceResult? Result = null);

public sealed class InMemoryReplaceStateStore : IReplaceStateStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<UndoCapsuleId, UndoCapsule> capsules = [];
    private readonly Dictionary<OperationId, UndoCapsuleId> operationIndex = [];
    private readonly Dictionary<OperationId, InMemoryUndoEntry> undoOperations = [];

    public int Count
    {
        get
        {
            lock (gate)
            {
                return capsules.Count;
            }
        }
    }

    public ValueTask<bool> TryAddAsync(
        UndoCapsule capsule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (capsules.ContainsKey(capsule.Id)
                || operationIndex.ContainsKey(capsule.OperationId))
            {
                return ValueTask.FromResult(false);
            }

            capsules.Add(capsule.Id, capsule);
            operationIndex.Add(capsule.OperationId, capsule.Id);
            return ValueTask.FromResult(true);
        }
    }

    public bool TryGet(
        UndoCapsuleId capsuleId,
        [NotNullWhen(true)] out UndoCapsule? capsule)
    {
        ArgumentNullException.ThrowIfNull(capsuleId);
        lock (gate)
        {
            return capsules.TryGetValue(capsuleId, out capsule);
        }
    }

    public bool TryGetByOperation(
        OperationId operationId,
        [NotNullWhen(true)] out UndoCapsule? capsule)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        lock (gate)
        {
            if (operationIndex.TryGetValue(operationId, out UndoCapsuleId? capsuleId))
            {
                return capsules.TryGetValue(capsuleId, out capsule);
            }

            capsule = null;
            return false;
        }
    }

    public ValueTask<bool> TryRemoveAsync(
        UndoCapsuleId capsuleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capsuleId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!capsules.Remove(capsuleId, out UndoCapsule? capsule))
            {
                return ValueTask.FromResult(false);
            }

            operationIndex.Remove(capsule.OperationId);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<UndoJournalPreparation> PrepareUndoAsync(
        UndoCapsuleId capsuleId,
        OperationId operationId,
        string requestDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capsuleId);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestDigest);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (undoOperations.TryGetValue(
                    operationId,
                    out InMemoryUndoEntry? existing))
            {
                if (existing.CapsuleId != capsuleId
                    || !StringComparer.Ordinal.Equals(
                        existing.RequestDigest,
                        requestDigest))
                {
                    return ValueTask.FromResult(new UndoJournalPreparation(
                        UndoJournalPreparationStatus.Conflict));
                }

                return ValueTask.FromResult(existing.Result is null
                    ? new UndoJournalPreparation(
                        UndoJournalPreparationStatus.RecoveryRequired)
                    : new UndoJournalPreparation(
                        UndoJournalPreparationStatus.Replay,
                        existing.Result));
            }

            if (undoOperations.Values.Any(entry =>
                    entry.CapsuleId == capsuleId && entry.Result is null))
            {
                return ValueTask.FromResult(new UndoJournalPreparation(
                    UndoJournalPreparationStatus.CapsuleReserved));
            }

            bool consumed = undoOperations.Values.Any(entry =>
                entry.CapsuleId == capsuleId
                && entry.Result?.Status == OperationStatus.Committed);
            undoOperations.Add(
                operationId,
                new InMemoryUndoEntry(capsuleId, requestDigest, null));
            return ValueTask.FromResult(new UndoJournalPreparation(
                consumed
                    ? UndoJournalPreparationStatus.PreparedConsumed
                    : UndoJournalPreparationStatus.Prepared));
        }
    }

    public ValueTask CompleteUndoAsync(
        OperationId operationId,
        UndoReplaceResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!undoOperations.TryGetValue(
                    operationId,
                    out InMemoryUndoEntry? existing)
                || existing.Result is not null
                || existing.CapsuleId != result.CapsuleId
                || result.OperationId != operationId)
            {
                throw new InvalidOperationException(
                    "An undo result requires its matching pending journal entry.");
            }

            undoOperations[operationId] = existing with { Result = result };
            return ValueTask.CompletedTask;
        }
    }

    private sealed record InMemoryUndoEntry(
        UndoCapsuleId CapsuleId,
        string RequestDigest,
        UndoReplaceResult? Result);
}

public interface IUndoCapsuleIdSource
{
    public UndoCapsuleId CreateId();
}

public sealed class DeterministicUndoCapsuleIdSource : IUndoCapsuleIdSource
{
    private readonly Lock gate = new();
    private readonly Queue<UndoCapsuleId> ids;

    public DeterministicUndoCapsuleIdSource(IEnumerable<UndoCapsuleId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        this.ids = new Queue<UndoCapsuleId>(ids);
    }

    public UndoCapsuleId CreateId()
    {
        lock (gate)
        {
            return ids.Count > 0
                ? ids.Dequeue()
                : throw new InvalidOperationException(
                    "The deterministic undo capsule ID source is empty.");
        }
    }
}

public sealed class CryptographicUndoCapsuleIdSource : IUndoCapsuleIdSource
{
    public UndoCapsuleId CreateId() => UndoCapsuleId.From(Guid.NewGuid());
}

public sealed class ReplaceEndpoint : IReplacePeer, IDisposable
{
    public static readonly TimeSpan MaximumUndoRetention = TimeSpan.FromMinutes(15);

    private readonly ActivityAdapterRegistry adapterRegistry;
    private readonly IActivityCatalog catalog;
    private readonly IClock clock;
    private readonly IUndoCapsuleIdSource idSource;
    private readonly IOperationJournal journal;
    private readonly ConcurrentDictionary<DeviceId, CapabilityGrant> peerGrants = new();
    private readonly IReceiptSink receiptSink;
    private readonly SemaphoreSlim serializationGate = new(1, 1);
    private readonly IReplaceStateStore replaceState;

    public ReplaceEndpoint(
        DeviceId deviceId,
        IClock clock,
        IActivityCatalog catalog,
        IOperationJournal journal,
        ActivityAdapterRegistry adapterRegistry,
        IReplaceStateStore replaceState,
        IUndoCapsuleIdSource idSource,
        IReceiptSink receiptSink)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(adapterRegistry);
        ArgumentNullException.ThrowIfNull(replaceState);
        ArgumentNullException.ThrowIfNull(idSource);
        ArgumentNullException.ThrowIfNull(receiptSink);

        DeviceId = deviceId;
        this.clock = clock;
        this.catalog = catalog;
        this.journal = journal;
        this.adapterRegistry = adapterRegistry;
        this.replaceState = replaceState;
        this.idSource = idSource;
        this.receiptSink = receiptSink;
    }

    public DeviceId DeviceId { get; }

    public void SetPeerGrant(DeviceId peerDeviceId, CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(grant);
        peerGrants[peerDeviceId] = grant;
    }

    public void Dispose() => serializationGate.Dispose();

    public async ValueTask<ReplaceOperationResult> ReplaceAsync(
        DeviceId senderDeviceId,
        ReplaceActivityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(command);

        JournalExecutionResult execution;
        try
        {
            await serializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                execution = await journal.ExecuteOnceAsync(
                    command.Context.OperationId,
                    command.BindAuthenticatedSender(senderDeviceId),
                    ExecuteOnceAsync,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                serializationGate.Release();
            }
        }
        catch (ReplaceStatePersistenceException)
        {
            OperationReceipt unavailable = Reject(
                command,
                senderDeviceId,
                FailureCode.UndoUnavailable);
            receiptSink.Write(unavailable);
            return new ReplaceOperationResult(unavailable, null);
        }

        OperationReceipt receipt;
        if (execution.IsConflict)
        {
            receipt = Reject(command, senderDeviceId, FailureCode.OperationIdConflict);
            receiptSink.Write(receipt);
        }
        else if (execution.IsRecoveryRequired)
        {
            receipt = OperationReceipt.Recovering(
                command.Context.OperationId,
                command.Context.CorrelationId,
                OperationKind.Replace,
                senderDeviceId,
                DeviceId,
                command.IncomingDescriptor,
                clock.UtcNow,
                FailureCode.OperationInProgress);
            receiptSink.Write(receipt);
        }
        else
        {
            receipt = execution.Receipt
                ?? throw new InvalidOperationException(
                    "The Replace journal returned no operation receipt.");
        }

        UndoCapsule? capsule = null;
        if (receipt.IsSuccess)
        {
            replaceState.TryGetByOperation(command.Context.OperationId, out capsule);
        }

        return new ReplaceOperationResult(receipt, capsule?.Reference);

        async ValueTask<OperationReceipt> ExecuteOnceAsync(CancellationToken innerToken)
        {
            OperationReceipt result = await ExecuteSerializedAsync(innerToken)
                .ConfigureAwait(false);
            receiptSink.Write(result);
            return result;
        }

        async ValueTask<OperationReceipt> ExecuteSerializedAsync(CancellationToken innerToken)
        {
            if (command.Context.Deadline <= clock.UtcNow)
            {
                return Reject(command, senderDeviceId, FailureCode.DeadlineExpired);
            }

            if (!peerGrants.TryGetValue(senderDeviceId, out CapabilityGrant? grant)
                || !grant.Allows(Capability.ActivityReplace))
            {
                return Reject(command, senderDeviceId, FailureCode.CapabilityDenied);
            }

            if (command.TargetPlacement.DeviceId != DeviceId)
            {
                return Reject(command, senderDeviceId, FailureCode.DescriptorRejected);
            }

            if (command.UndoExpiresAt <= clock.UtcNow
                || command.UndoExpiresAt - clock.UtcNow > MaximumUndoRetention)
            {
                return Reject(command, senderDeviceId, FailureCode.UndoUnavailable);
            }

            if (!catalog.TryGet(command.TargetActivityId, out ActivityInstance? original)
                || original is null)
            {
                return Reject(command, senderDeviceId, FailureCode.ActivityNotFound);
            }

            if (original.Revision != command.ExpectedTargetRevision
                || !DigestsEqual(
                    original.Descriptor.DescriptorDigest,
                    command.ExpectedTargetDescriptorDigest)
                || original.Placement != command.TargetPlacement
                || original.Lifecycle != ActivityLifecycle.Active)
            {
                return Reject(command, senderDeviceId, FailureCode.RevisionConflict);
            }

            if (original.Descriptor.Kind != command.IncomingDescriptor.Kind
                || !adapterRegistry.TryFind(
                    original.Descriptor.Kind,
                    out IActivityAdapter? activityAdapter)
                || activityAdapter is not IReplaceActivityAdapter adapter)
            {
                return Reject(command, senderDeviceId, FailureCode.UndoUnavailable);
            }

            CaptureUndoResult captured = await adapter
                .CaptureUndoAsync(original, innerToken)
                .ConfigureAwait(false);
            if (!captured.Succeeded)
            {
                return Reject(command, senderDeviceId, captured.FailureCode);
            }

            if (captured.PreservedDescriptor is null
                || captured.PreservedDescriptor.Id != original.Descriptor.Id
                || !StringComparer.Ordinal.Equals(
                    captured.PreservedDescriptor.DescriptorDigest,
                    original.Descriptor.DescriptorDigest))
            {
                return Reject(command, senderDeviceId, FailureCode.UndoCapsuleInvalid);
            }

            ActivityInstance replacement = ActivityInstance.Active(
                command.IncomingDescriptor,
                command.TargetPlacement,
                checked(original.Revision + 1));
            UndoCapsule capsule = UndoCapsule.Create(
                idSource.CreateId(),
                command.Context,
                senderDeviceId,
                DeviceId,
                original,
                replacement,
                clock.UtcNow,
                command.UndoExpiresAt);
            bool capsuleStored;
            try
            {
                capsuleStored = await replaceState
                    .TryAddAsync(capsule, innerToken)
                    .ConfigureAwait(false);
            }
            catch (ReplaceStatePersistenceException)
            {
                return Reject(command, senderDeviceId, FailureCode.UndoUnavailable);
            }

            if (!capsuleStored)
            {
                return Reject(command, senderDeviceId, FailureCode.UndoUnavailable);
            }

            ResumeActivityResult resume = await adapter
                .ResumeAsync(
                    command.IncomingDescriptor,
                    command.TargetPlacement,
                    innerToken)
                .ConfigureAwait(false);
            if (!resume.Succeeded)
            {
                try
                {
                    await replaceState.TryRemoveAsync(capsule.Id, innerToken)
                        .ConfigureAwait(false);
                }
                catch (ReplaceStatePersistenceException)
                {
                    return OperationReceipt.Recovering(
                        command.Context.OperationId,
                        command.Context.CorrelationId,
                        OperationKind.Replace,
                        senderDeviceId,
                        DeviceId,
                        command.IncomingDescriptor,
                        clock.UtcNow,
                        FailureCode.InternalFailure);
                }

                return Reject(command, senderDeviceId, resume.FailureCode);
            }

            if (!catalog.TrySwapReplace(original, replacement))
            {
                return OperationReceipt.Recovering(
                    command.Context.OperationId,
                    command.Context.CorrelationId,
                    OperationKind.Replace,
                    senderDeviceId,
                    DeviceId,
                    command.IncomingDescriptor,
                    clock.UtcNow,
                    FailureCode.InternalFailure);
            }

            return OperationReceipt.Committed(
                command.Context.OperationId,
                command.Context.CorrelationId,
                OperationKind.Replace,
                senderDeviceId,
                DeviceId,
                command.IncomingDescriptor,
                clock.UtcNow);
        }
    }

    public async ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleId capsuleId,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capsuleId);
        ArgumentNullException.ThrowIfNull(context);

        await serializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string requestDigest = ComputeUndoRequestDigest(capsuleId, context);
            UndoJournalPreparation preparation;
            try
            {
                preparation = await replaceState.PrepareUndoAsync(
                    capsuleId,
                    context.OperationId,
                    requestDigest,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ReplaceStatePersistenceException)
            {
                return UndoReplaceResult.Failed(
                    context,
                    capsuleId,
                    FailureCode.UndoUnavailable,
                    clock.UtcNow);
            }

            if (preparation.Status == UndoJournalPreparationStatus.Replay)
            {
                return preparation.Result
                    ?? throw new InvalidOperationException(
                        "A replayed undo journal entry has no result.");
            }

            if (preparation.Status == UndoJournalPreparationStatus.Conflict)
            {
                return UndoReplaceResult.Rejected(
                    context,
                    capsuleId,
                    FailureCode.OperationIdConflict,
                    clock.UtcNow);
            }

            if (preparation.Status is
                UndoJournalPreparationStatus.RecoveryRequired
                or UndoJournalPreparationStatus.CapsuleReserved)
            {
                return UndoReplaceResult.Recovering(
                    context,
                    capsuleId,
                    FailureCode.OperationInProgress,
                    clock.UtcNow);
            }

            UndoReplaceResult result = preparation.Status
                == UndoJournalPreparationStatus.PreparedConsumed
                    ? UndoReplaceResult.Rejected(
                        context,
                        capsuleId,
                        FailureCode.UndoCapsuleConsumed,
                        clock.UtcNow)
                    : await ExecuteUndoAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await replaceState.CompleteUndoAsync(
                    context.OperationId,
                    result,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ReplaceStatePersistenceException)
            {
                return UndoReplaceResult.Recovering(
                    context,
                    capsuleId,
                    FailureCode.InternalFailure,
                    clock.UtcNow);
            }

            return result;
        }
        finally
        {
            serializationGate.Release();
        }

        async ValueTask<UndoReplaceResult> ExecuteUndoAsync(CancellationToken innerToken)
        {
            if (context.Deadline <= clock.UtcNow)
            {
                return UndoReplaceResult.Rejected(
                    context,
                    capsuleId,
                    FailureCode.DeadlineExpired,
                    clock.UtcNow);
            }

            if (!replaceState.TryGet(capsuleId, out UndoCapsule? capsule)
                || capsule is null)
            {
                return UndoReplaceResult.Rejected(
                    context,
                    capsuleId,
                    FailureCode.UndoCapsuleNotFound,
                    clock.UtcNow);
            }

            if (capsule.ExpiresAt <= clock.UtcNow)
            {
                return UndoReplaceResult.Rejected(
                    context,
                    capsuleId,
                    FailureCode.UndoCapsuleExpired,
                    clock.UtcNow);
            }

            if (!catalog.TryGet(
                    capsule.ReplacementActivity.Descriptor.Id,
                    out ActivityInstance? current)
                || current != capsule.ReplacementActivity)
            {
                return UndoReplaceResult.Rejected(
                    context,
                    capsuleId,
                    FailureCode.RevisionConflict,
                    clock.UtcNow);
            }

            if (!adapterRegistry.TryFind(
                    capsule.OriginalActivity.Descriptor.Kind,
                    out IActivityAdapter? activityAdapter)
                || activityAdapter is not IReplaceActivityAdapter adapter)
            {
                return UndoReplaceResult.Rejected(
                    context,
                    capsuleId,
                    FailureCode.UndoUnavailable,
                    clock.UtcNow);
            }

            RestoreActivityResult restore = await adapter
                .RestoreAsync(
                    capsule,
                    capsule.OriginalActivity.Placement,
                    innerToken)
                .ConfigureAwait(false);
            if (!restore.Succeeded)
            {
                return UndoReplaceResult.Failed(
                    context,
                    capsuleId,
                    restore.FailureCode,
                    clock.UtcNow);
            }

            ActivityInstance restored = ActivityInstance.Active(
                capsule.OriginalActivity.Descriptor,
                capsule.OriginalActivity.Placement,
                checked(capsule.ReplacementActivity.Revision + 1));
            if (!catalog.TrySwapReplace(capsule.ReplacementActivity, restored))
            {
                return UndoReplaceResult.Failed(
                    context,
                    capsuleId,
                    FailureCode.RevisionConflict,
                    clock.UtcNow);
            }

            return UndoReplaceResult.Committed(context, capsuleId, clock.UtcNow);
        }
    }

    private static string ComputeUndoRequestDigest(
        UndoCapsuleId capsuleId,
        OperationContext context)
    {
        string material = string.Join(
            '\n',
            context.OperationId.ToString(),
            context.CorrelationId.ToString(),
            capsuleId.ToString(),
            context.Deadline.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static bool DigestsEqual(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(first),
            Convert.FromHexString(second));

    private OperationReceipt Reject(
        ReplaceActivityCommand command,
        DeviceId senderDeviceId,
        FailureCode failureCode) => OperationReceipt.Rejected(
            command.Context.OperationId,
            command.Context.CorrelationId,
            OperationKind.Replace,
            senderDeviceId,
            DeviceId,
            command.IncomingDescriptor,
            clock.UtcNow,
            failureCode);

}
