using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface IClock
{
    public DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    private SystemClock()
    {
    }

    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IActivityAdapter
{
    public ActivityKind Kind { get; }

    public ValueTask<ResumeActivityResult> ResumeAsync(
        ActivityDescriptor descriptor,
        ActivityPlacement placement,
        CancellationToken cancellationToken);

    public ValueTask<CloseActivityResult> CloseAsync(
        ActivityInstance activity,
        CancellationToken cancellationToken);
}

public interface IReplaceActivityAdapter : IActivityAdapter
{
    public ValueTask<CaptureUndoResult> CaptureUndoAsync(
        ActivityInstance activity,
        CancellationToken cancellationToken);

    public ValueTask<RestoreActivityResult> RestoreAsync(
        UndoCapsule capsule,
        ActivityPlacement placement,
        CancellationToken cancellationToken);
}

public readonly record struct CaptureUndoResult(
    bool Succeeded,
    ActivityDescriptor? PreservedDescriptor,
    FailureCode FailureCode)
{
    public static CaptureUndoResult Success(ActivityDescriptor preservedDescriptor)
    {
        ArgumentNullException.ThrowIfNull(preservedDescriptor);
        return new CaptureUndoResult(true, preservedDescriptor, FailureCode.None);
    }

    public static CaptureUndoResult Rejected(FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected undo capture must have a failure code.",
                nameof(failureCode));
        }

        return new CaptureUndoResult(false, null, failureCode);
    }
}

public readonly record struct RestoreActivityResult(bool Succeeded, FailureCode FailureCode)
{
    public static RestoreActivityResult Success { get; } = new(true, FailureCode.None);

    public static RestoreActivityResult Rejected(FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected Activity restore must have a failure code.",
                nameof(failureCode));
        }

        return new RestoreActivityResult(false, failureCode);
    }
}

public readonly record struct CloseActivityResult(bool Succeeded, FailureCode FailureCode)
{
    public static CloseActivityResult Success { get; } = new(true, FailureCode.None);

    public static CloseActivityResult Failed(FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A failed close result must have a failure code.",
                nameof(failureCode));
        }

        return new CloseActivityResult(false, failureCode);
    }
}

public readonly record struct ResumeActivityResult(bool Succeeded, FailureCode FailureCode)
{
    public static ResumeActivityResult Success { get; } = new(true, FailureCode.None);

    public static ResumeActivityResult Rejected(FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected adapter result must have a failure code.",
                nameof(failureCode));
        }

        return new ResumeActivityResult(false, failureCode);
    }
}

public interface IActivityCatalog
{
    public bool TryGet(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity);

    public bool TryAdd(ActivityInstance activity);

    public bool TryUpdate(ActivityInstance expected, ActivityInstance replacement);

    public bool TrySwapReplace(ActivityInstance expected, ActivityInstance replacement);
}

public interface IActivitySnapshotSource
{
    public IReadOnlyList<ActivityInstance> GetSnapshot();
}

public interface IOperationJournal
{
    public ValueTask<JournalExecutionResult> ExecuteOnceAsync(
        OperationId operationId,
        string requestDigest,
        Func<CancellationToken, ValueTask<OperationReceipt>> operation,
        CancellationToken cancellationToken);
}

public readonly record struct JournalExecutionResult(
    OperationReceipt? Receipt,
    bool WasReplay,
    bool IsConflict,
    bool IsRecoveryRequired = false);

public interface IReceiptSink
{
    public void Write(OperationReceipt receipt);
}

public interface IOperationHistoryStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public static class OperationHistoryStorageLimits
{
    public const int MaximumEntryCount = 256;
    public const int MaximumPayloadBytes = 1 * 1024 * 1024;
}

public sealed class NullReceiptSink : IReceiptSink
{
    private NullReceiptSink()
    {
    }

    public static NullReceiptSink Instance { get; } = new();

    public void Write(OperationReceipt receipt) =>
        ArgumentNullException.ThrowIfNull(receipt);
}

public interface IActivityPeer
{
    public DeviceId DeviceId { get; }

    public ValueTask<OperationReceipt> ReceiveActivityAsync(
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        CancellationToken cancellationToken);
}

public interface IActivityChannel
{
    public DeviceId TargetDeviceId { get; }

    public ValueTask<ActivityDeliveryResult> SendAsync(
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        CancellationToken cancellationToken);
}

public enum ActivityDeliveryStatus
{
    Acknowledged,
    NotDelivered,
    AcknowledgementLost,
}

public readonly record struct ActivityDeliveryResult(
    ActivityDeliveryStatus Status,
    OperationReceipt? Receipt)
{
    public static ActivityDeliveryResult Acknowledged(OperationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new ActivityDeliveryResult(ActivityDeliveryStatus.Acknowledged, receipt);
    }

    public static ActivityDeliveryResult NotDelivered { get; } =
        new(ActivityDeliveryStatus.NotDelivered, null);

    public static ActivityDeliveryResult AcknowledgementLost { get; } =
        new(ActivityDeliveryStatus.AcknowledgementLost, null);
}
