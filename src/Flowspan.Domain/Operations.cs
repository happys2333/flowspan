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
}
