using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public enum ReplaceRecoveryOperationKind
{
    Replace,
    Undo,
}

public enum ReplaceRecoveryJournalState
{
    Pending,
    Terminal,
}

public enum ReplaceRecoveryTimestampKind
{
    None,
    CapsuleCaptured,
    Outcome,
}

public enum ReplaceUndoAvailability
{
    None,
    PendingOperation,
    Available,
    Expired,
    Consumed,
}

public sealed record ReplaceRecoveryRecord
{
    internal ReplaceRecoveryRecord(
        ReplaceRecoveryOperationKind kind,
        ReplaceRecoveryJournalState journalState,
        OperationId operationId,
        OperationStatus status,
        FailureCode failureCode,
        CorrelationId? correlationId,
        DeviceId? replaceSourceDeviceId,
        DeviceId? replaceTargetDeviceId,
        ActivityId? targetActivityId,
        ActivityId? incomingActivityId,
        UndoCapsuleId? capsuleId,
        ReplaceRecoveryTimestampKind timestampKind,
        DateTimeOffset? recordedAt,
        DateTimeOffset? undoExpiresAt,
        ReplaceUndoAvailability undoAvailability)
    {
        Kind = kind;
        JournalState = journalState;
        OperationId = operationId;
        Status = status;
        FailureCode = failureCode;
        CorrelationId = correlationId;
        ReplaceSourceDeviceId = replaceSourceDeviceId;
        ReplaceTargetDeviceId = replaceTargetDeviceId;
        TargetActivityId = targetActivityId;
        IncomingActivityId = incomingActivityId;
        CapsuleId = capsuleId;
        TimestampKind = timestampKind;
        RecordedAt = recordedAt;
        UndoExpiresAt = undoExpiresAt;
        UndoAvailability = undoAvailability;
    }

    public ReplaceRecoveryOperationKind Kind { get; }

    public ReplaceRecoveryJournalState JournalState { get; }

    public OperationId OperationId { get; }

    public OperationStatus Status { get; }

    public FailureCode FailureCode { get; }

    public CorrelationId? CorrelationId { get; }

    public DeviceId? ReplaceSourceDeviceId { get; }

    public DeviceId? ReplaceTargetDeviceId { get; }

    public ActivityId? TargetActivityId { get; }

    public ActivityId? IncomingActivityId { get; }

    public UndoCapsuleId? CapsuleId { get; }

    public ReplaceRecoveryTimestampKind TimestampKind { get; }

    public DateTimeOffset? RecordedAt { get; }

    public DateTimeOffset? UndoExpiresAt { get; }

    public ReplaceUndoAvailability UndoAvailability { get; }

    public bool IsRecoveryRequired =>
        JournalState == ReplaceRecoveryJournalState.Pending
        || Status == OperationStatus.Recovering;

    public bool HasCompleteReplaceBindings =>
        CorrelationId is not null
        && ReplaceSourceDeviceId is not null
        && ReplaceTargetDeviceId is not null
        && TargetActivityId is not null
        && IncomingActivityId is not null;
}

public sealed record ReplaceRecoverySnapshot
{
    public const int MaximumRecords = 64;

    internal ReplaceRecoverySnapshot(
        DateTimeOffset capturedAt,
        bool isTruncated,
        ImmutableArray<ReplaceRecoveryRecord> records)
    {
        CapturedAt = capturedAt;
        IsTruncated = isTruncated;
        Records = records;
    }

    public DateTimeOffset CapturedAt { get; }

    public bool IsTruncated { get; }

    public ImmutableArray<ReplaceRecoveryRecord> Records { get; }
}

public interface IReplaceRecoverySnapshotSource
{
    public ReplaceRecoverySnapshot GetRecoverySnapshot(DateTimeOffset utcNow);
}

public sealed record ReplaceRestartUndoCandidate(
    UndoCapsuleId CapsuleId,
    ActivityInstance ExactReplacement);

public sealed record ReplaceRestartRecoveryPlan(
    bool IsBlockedByUnresolvedOperation,
    ImmutableArray<ActivityInstance> CurrentActivities,
    ImmutableArray<ReplaceRestartUndoCandidate> UndoCandidates);
