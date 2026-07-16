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

    [Theory]
    [InlineData(OperationStatus.Failed)]
    [InlineData(OperationStatus.Recovering)]
    public async Task TransientResultKeepsOperationBoundToOriginalDigest(
        OperationStatus transientStatus)
    {
        var journal = new InMemoryOperationJournal();
        OperationId operationId =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        int differentHandlerCalls = 0;

        JournalExecutionResult transient = await journal.ExecuteOnceAsync(
            operationId,
            "ORIGINAL-DIGEST",
            _ => ValueTask.FromResult(CreateReceipt(operationId, transientStatus)),
            CancellationToken.None);
        JournalExecutionResult conflict = await journal.ExecuteOnceAsync(
            operationId,
            "DIFFERENT-DIGEST",
            _ =>
            {
                differentHandlerCalls++;
                return ValueTask.FromResult(CreateReceipt(operationId));
            },
            CancellationToken.None);
        JournalExecutionResult retry = await journal.ExecuteOnceAsync(
            operationId,
            "ORIGINAL-DIGEST",
            _ => ValueTask.FromResult(CreateReceipt(operationId)),
            CancellationToken.None);

        Assert.Equal(transientStatus, transient.Receipt?.Status);
        Assert.True(conflict.IsConflict);
        Assert.Equal(0, differentHandlerCalls);
        Assert.Equal(OperationStatus.Committed, retry.Receipt?.Status);
        Assert.False(retry.IsConflict);
    }

    [Fact]
    public async Task HandlerExceptionKeepsOperationBoundToOriginalDigest()
    {
        var journal = new InMemoryOperationJournal();
        OperationId operationId =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var failure = new IOException("Injected operation failure.");
        int differentHandlerCalls = 0;

        IOException thrown = await Assert.ThrowsAsync<IOException>(
            async () => await journal.ExecuteOnceAsync(
                operationId,
                "ORIGINAL-DIGEST",
                _ => ValueTask.FromException<OperationReceipt>(failure),
                CancellationToken.None));
        JournalExecutionResult conflict = await journal.ExecuteOnceAsync(
            operationId,
            "DIFFERENT-DIGEST",
            _ =>
            {
                differentHandlerCalls++;
                return ValueTask.FromResult(CreateReceipt(operationId));
            },
            CancellationToken.None);
        JournalExecutionResult retry = await journal.ExecuteOnceAsync(
            operationId,
            "ORIGINAL-DIGEST",
            _ => ValueTask.FromResult(CreateReceipt(operationId)),
            CancellationToken.None);

        Assert.Same(failure, thrown);
        Assert.True(conflict.IsConflict);
        Assert.Equal(0, differentHandlerCalls);
        Assert.Equal(OperationStatus.Committed, retry.Receipt?.Status);
        Assert.False(retry.IsConflict);
    }

    [Fact]
    public async Task CapacityExhaustionRejectsUnknownOperationBeforeHandler()
    {
        var journal = new InMemoryOperationJournal(capacity: 1);
        OperationId firstOperation =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        OperationId unknownOperation =
            OperationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        int unknownHandlerCalls = 0;

        await journal.ExecuteOnceAsync(
            firstOperation,
            "FIRST-DIGEST",
            _ => ValueTask.FromResult(CreateReceipt(
                firstOperation,
                OperationStatus.Failed)),
            CancellationToken.None);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await journal.ExecuteOnceAsync(
                    unknownOperation,
                    "UNKNOWN-DIGEST",
                    _ =>
                    {
                        unknownHandlerCalls++;
                        return ValueTask.FromResult(CreateReceipt(unknownOperation));
                    },
                    CancellationToken.None));

        Assert.Contains("capacity", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, unknownHandlerCalls);

        JournalExecutionResult retry = await journal.ExecuteOnceAsync(
            firstOperation,
            "FIRST-DIGEST",
            _ => ValueTask.FromResult(CreateReceipt(firstOperation)),
            CancellationToken.None);
        Assert.Equal(OperationStatus.Committed, retry.Receipt?.Status);
    }

    [Fact]
    public async Task SeededOperationSequencesPreserveTerminalIdempotency()
    {
        OperationStatus[] generatedStatuses =
        [
            OperationStatus.Committed,
            OperationStatus.CommittedWithWarning,
            OperationStatus.Rejected,
            OperationStatus.Failed,
            OperationStatus.Recovering,
        ];
        for (int seed = 0; seed < 32; seed++)
        {
            var random = new Random(seed);
            var journal = new InMemoryOperationJournal();
            var bindings = new Dictionary<OperationId, string>();
            var terminal = new Dictionary<OperationId, OperationReceipt>();
            for (int eventIndex = 0; eventIndex < 128; eventIndex++)
            {
                int slot = random.Next(8);
                OperationId operationId = OperationId.Parse(
                    $"bbbbbbbb-bbbb-bbbb-bbbb-{slot.ToString("D12", System.Globalization.CultureInfo.InvariantCulture)}");
                string digest = $"DIGEST-{slot}-{random.Next(3)}";
                OperationStatus generatedStatus =
                    generatedStatuses[random.Next(generatedStatuses.Length)];
                OperationReceipt generatedReceipt = CreateReceipt(
                    operationId,
                    generatedStatus);
                int handlerCalls = 0;
                string trace =
                    $"seed={seed}, event={eventIndex}, operation={operationId}, digest={digest}, generated={generatedStatus}";

                JournalExecutionResult result = await ExecuteWithTraceAsync(
                    trace,
                    () => journal.ExecuteOnceAsync(
                        operationId,
                        digest,
                        ExecuteAsync,
                        CancellationToken.None));
                if (bindings.TryGetValue(operationId, out string? recordedDigest)
                    && !StringComparer.Ordinal.Equals(recordedDigest, digest))
                {
                    Assert.True(result.IsConflict, trace);
                    Assert.False(result.WasReplay, trace);
                    Assert.True(result.Receipt is null, trace);
                    Assert.True(handlerCalls == 0, trace);
                    continue;
                }

                if (terminal.TryGetValue(operationId, out OperationReceipt? recorded))
                {
                    Assert.True(result.WasReplay, trace);
                    Assert.False(result.IsConflict, trace);
                    Assert.True(ReferenceEquals(recorded, result.Receipt), trace);
                    Assert.True(handlerCalls == 0, trace);
                    continue;
                }

                bindings.TryAdd(operationId, digest);

                Assert.False(result.WasReplay, trace);
                Assert.False(result.IsConflict, trace);
                Assert.True(ReferenceEquals(generatedReceipt, result.Receipt), trace);
                Assert.True(handlerCalls == 1, trace);
                if (generatedStatus is not OperationStatus.Failed
                    and not OperationStatus.Recovering)
                {
                    terminal.Add(operationId, generatedReceipt);
                }

                ValueTask<OperationReceipt> ExecuteAsync(CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    handlerCalls++;
                    return ValueTask.FromResult(generatedReceipt);
                }
            }
        }
    }

    private static async ValueTask<T> ExecuteWithTraceAsync<T>(
        string trace,
        Func<ValueTask<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{trace}, unexpected generated-operation exception.",
                exception);
        }
    }

    private static OperationReceipt CreateReceipt(
        OperationId operationId,
        OperationStatus status = OperationStatus.Committed)
    {
        DeviceId source = DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target = DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            source,
            "Test",
            "{\"text\":\"hello\"}");

        CorrelationId correlationId =
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        DateTimeOffset occurredAt =
            new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);
        return status switch
        {
            OperationStatus.Committed => OperationReceipt.Committed(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                occurredAt),
            OperationStatus.CommittedWithWarning => OperationReceipt.CommittedWithWarning(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                occurredAt,
                FailureCode.SourceCleanupFailed),
            OperationStatus.Rejected => OperationReceipt.Rejected(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                occurredAt,
                FailureCode.DescriptorRejected),
            OperationStatus.Failed => OperationReceipt.Failed(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                occurredAt,
                FailureCode.PeerUnavailable),
            OperationStatus.Recovering => OperationReceipt.Recovering(
                operationId,
                correlationId,
                OperationKind.Handoff,
                source,
                target,
                descriptor,
                occurredAt,
                FailureCode.AcknowledgementLost),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}
