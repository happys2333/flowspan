using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

internal sealed class ThrowAfterFirstResultOperationJournal(Exception failure) : IOperationJournal
{
    private readonly InMemoryOperationJournal inner = new();
    private int failNextResult = 1;

    public async ValueTask<JournalExecutionResult> ExecuteOnceAsync(
        OperationId operationId,
        string requestDigest,
        Func<CancellationToken, ValueTask<OperationReceipt>> operation,
        CancellationToken cancellationToken)
    {
        JournalExecutionResult result = await inner.ExecuteOnceAsync(
            operationId,
            requestDigest,
            operation,
            cancellationToken);
        if (Interlocked.Exchange(ref failNextResult, 0) == 1)
        {
            throw failure;
        }

        return result;
    }
}
