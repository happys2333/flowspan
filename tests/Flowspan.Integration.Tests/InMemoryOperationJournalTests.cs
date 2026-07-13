using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class InMemoryOperationJournalTests
{
    [Fact]
    public async Task ConcurrentRetryExecutesHandlerOnceAndSharesResult()
    {
        var journal = new InMemoryOperationJournal();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int executionCount = 0;
        OperationId operationId =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Task<JournalExecutionResult> first = journal.ExecuteOnceAsync(
            operationId,
            "SAME-DIGEST",
            ExecuteAsync,
            CancellationToken.None).AsTask();
        await started.Task;

        Task<JournalExecutionResult> retry = journal.ExecuteOnceAsync(
            operationId,
            "SAME-DIGEST",
            ExecuteAsync,
            CancellationToken.None).AsTask();
        release.TrySetResult(true);

        JournalExecutionResult[] results = await Task.WhenAll(first, retry);

        Assert.Equal(1, Volatile.Read(ref executionCount));
        Assert.False(results[0].WasReplay);
        Assert.True(results[1].WasReplay);
        Assert.Same(results[0].Receipt, results[1].Receipt);

        async ValueTask<OperationReceipt> ExecuteAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executionCount);
            started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return CreateReceipt(operationId);
        }
    }

    [Fact]
    public async Task DifferentDigestIsConflictWithoutExecutingHandler()
    {
        var journal = new InMemoryOperationJournal();
        OperationId operationId =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        int executionCount = 0;

        await journal.ExecuteOnceAsync(
            operationId,
            "FIRST-DIGEST",
            ExecuteAsync,
            CancellationToken.None);
        JournalExecutionResult conflict = await journal.ExecuteOnceAsync(
            operationId,
            "DIFFERENT-DIGEST",
            ExecuteAsync,
            CancellationToken.None);

        Assert.True(conflict.IsConflict);
        Assert.Null(conflict.Receipt);
        Assert.Equal(1, executionCount);

        ValueTask<OperationReceipt> ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionCount++;
            return ValueTask.FromResult(CreateReceipt(operationId));
        }
    }

    private static OperationReceipt CreateReceipt(OperationId operationId)
    {
        DeviceId source = DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target = DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            source,
            "Test",
            "{\"text\":\"hello\"}");

        return OperationReceipt.Committed(
            operationId,
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            OperationKind.Handoff,
            source,
            target,
            descriptor,
            new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero));
    }
}
