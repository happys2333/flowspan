namespace Flowspan.Domain;

public enum OperationKind
{
    Handoff,
    Move,
    Replace,
    Swap,
    Mirror,
    TransferDriver,
    ApplyScene,
}

public enum OperationStatus
{
    Committed,
    CommittedWithWarning,
    Rejected,
    Failed,
    Recovering,
}

public enum FailureCode
{
    None,
    ActivityNotFound,
    ActivityAlreadyExists,
    CapabilityDenied,
    DeadlineExpired,
    DescriptorRejected,
    AdapterUnavailable,
    OperationIdConflict,
    OperationInProgress,
    ProtocolIncompatible,
    PeerUnavailable,
    AcknowledgementLost,
    SourceCleanupFailed,
    RevisionConflict,
    ReservationConflict,
    ReservationExpired,
    DecisionConflict,
    InternalFailure,
}

public sealed record OperationReceipt
{
    private OperationReceipt(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        OperationStatus status,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityId activityId,
        ActivityKind? activityKind,
        string? descriptorDigest,
        DateTimeOffset occurredAt,
        FailureCode failureCode)
    {
        OperationId = operationId;
        CorrelationId = correlationId;
        Kind = kind;
        Status = status;
        SourceDeviceId = sourceDeviceId;
        TargetDeviceId = targetDeviceId;
        ActivityId = activityId;
        ActivityKind = activityKind;
        DescriptorDigest = descriptorDigest;
        OccurredAt = occurredAt;
        FailureCode = failureCode;
    }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public OperationKind Kind { get; }

    public OperationStatus Status { get; }

    public DeviceId SourceDeviceId { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityId ActivityId { get; }

    public ActivityKind? ActivityKind { get; }

    public string? DescriptorDigest { get; }

    public DateTimeOffset OccurredAt { get; }

    public FailureCode FailureCode { get; }

    public bool IsSuccess => Status is OperationStatus.Committed
        or OperationStatus.CommittedWithWarning;

    public static OperationReceipt Committed(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return Create(
            operationId,
            correlationId,
            kind,
            OperationStatus.Committed,
            sourceDeviceId,
            targetDeviceId,
            descriptor,
            occurredAt,
            FailureCode.None);
    }

    public static OperationReceipt Rejected(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt,
        FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected receipt must contain a failure code.",
                nameof(failureCode));
        }

        return Create(
            operationId,
            correlationId,
            kind,
            OperationStatus.Rejected,
            sourceDeviceId,
            targetDeviceId,
            descriptor,
            occurredAt,
            failureCode);
    }

    public static OperationReceipt Failed(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt,
        FailureCode failureCode) => CreateNonSuccess(
            operationId,
            correlationId,
            kind,
            OperationStatus.Failed,
            sourceDeviceId,
            targetDeviceId,
            descriptor,
            occurredAt,
            failureCode);

    public static OperationReceipt Recovering(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt,
        FailureCode failureCode) => CreateNonSuccess(
            operationId,
            correlationId,
            kind,
            OperationStatus.Recovering,
            sourceDeviceId,
            targetDeviceId,
            descriptor,
            occurredAt,
            failureCode);

    public static OperationReceipt CommittedWithWarning(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt,
        FailureCode failureCode) => CreateNonSuccess(
            operationId,
            correlationId,
            kind,
            OperationStatus.CommittedWithWarning,
            sourceDeviceId,
            targetDeviceId,
            descriptor,
            occurredAt,
            failureCode);

    public static OperationReceipt RejectedMissingActivity(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityId activityId,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sourceDeviceId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(activityId);

        return new OperationReceipt(
            operationId,
            correlationId,
            kind,
            OperationStatus.Rejected,
            sourceDeviceId,
            targetDeviceId,
            activityId,
            null,
            null,
            occurredAt,
            FailureCode.ActivityNotFound);
    }

    public static OperationReceipt FromRecordedResult(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        OperationStatus status,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityId activityId,
        ActivityKind? activityKind,
        string? descriptorDigest,
        DateTimeOffset occurredAt,
        FailureCode failureCode)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sourceDeviceId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(activityId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (!Enum.IsDefined(failureCode))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        }

        bool expectsFailure = status is not OperationStatus.Committed;
        if (expectsFailure == (failureCode == FailureCode.None))
        {
            throw new ArgumentException(
                "The recorded status and failure code are inconsistent.",
                nameof(failureCode));
        }

        if ((activityKind is null) != (descriptorDigest is null))
        {
            throw new ArgumentException(
                "Recorded Activity kind and descriptor digest must both be present or absent.",
                nameof(descriptorDigest));
        }

        if (activityKind is null && failureCode != FailureCode.ActivityNotFound)
        {
            throw new ArgumentException(
                "Only a missing-Activity result may omit descriptor metadata.",
                nameof(activityKind));
        }

        if (descriptorDigest is not null
            && (descriptorDigest.Length != 64
                || !descriptorDigest.All(char.IsAsciiHexDigit)))
        {
            throw new ArgumentException(
                "A recorded descriptor digest must be a 32-byte hexadecimal value.",
                nameof(descriptorDigest));
        }

        return new OperationReceipt(
            operationId,
            correlationId,
            kind,
            status,
            sourceDeviceId,
            targetDeviceId,
            activityId,
            activityKind,
            descriptorDigest,
            occurredAt,
            failureCode);
    }

    private static OperationReceipt Create(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        OperationStatus status,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt,
        FailureCode failureCode)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sourceDeviceId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(descriptor);

        return new OperationReceipt(
            operationId,
            correlationId,
            kind,
            status,
            sourceDeviceId,
            targetDeviceId,
            descriptor.Id,
            descriptor.Kind,
            descriptor.DescriptorDigest,
            occurredAt,
            failureCode);
    }

    private static OperationReceipt CreateNonSuccess(
        OperationId operationId,
        CorrelationId correlationId,
        OperationKind kind,
        OperationStatus status,
        DeviceId sourceDeviceId,
        DeviceId targetDeviceId,
        ActivityDescriptor descriptor,
        DateTimeOffset occurredAt,
        FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "This receipt status must contain a failure code.",
                nameof(failureCode));
        }

        return Create(
            operationId,
            correlationId,
            kind,
            status,
            sourceDeviceId,
            targetDeviceId,
            descriptor,
            occurredAt,
            failureCode);
    }
}
