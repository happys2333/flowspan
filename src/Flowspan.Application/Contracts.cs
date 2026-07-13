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
    bool IsConflict);

public interface IReceiptSink
{
    public void Write(OperationReceipt receipt);
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

    public ValueTask<OperationReceipt> ReceiveHandoffAsync(
        DeviceId senderDeviceId,
        HandoffOffer offer,
        CancellationToken cancellationToken);
}
