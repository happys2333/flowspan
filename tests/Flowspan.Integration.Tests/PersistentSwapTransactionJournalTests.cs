using System.Text;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class PersistentSwapTransactionJournalTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IntentAndDecisionSurviveRestartWithoutActivityContent()
    {
        var payloadStore = new TestSwapStatePayloadStore();
        SwapCoordinatorTransaction transaction = CreateTransaction(1);
        SwapDecision decision = transaction.CreateDecision(
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1));
        Assert.Equal(
            "C4E0F5335A598AD0122C1EAE2AB4AC62DF8F8E5DC9FDDCFAC1577483CC4D5AE2",
            transaction.RequestDigest);
        Assert.Equal(
            "2C1BF3A173F6B4AC09A3347BD966B3E9AB4A344F9377A26694740A01CB0A036D",
            decision.Digest);

        using (PersistentSwapTransactionJournal journal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            SwapTransactionWriteResult created = await journal.TryCreateAsync(transaction);
            SwapTransactionWriteResult decided = await journal.TryRecordDecisionAsync(
                transaction.Context.OperationId,
                decision);

            Assert.Equal(SwapTransactionWriteStatus.Stored, created.Status);
            Assert.Equal(SwapTransactionWriteStatus.Stored, decided.Status);
        }

        string encoded = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        Assert.DoesNotContain("FIRST-CONTENT-CANARY", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("SECOND-CONTENT-CANARY", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("First private title", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Second private title", encoded, StringComparison.Ordinal);

        using PersistentSwapTransactionJournal restarted =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        Assert.True(restarted.TryGet(
            transaction.Context.OperationId,
            out SwapCoordinatorTransaction? restored));
        Assert.Equal(transaction.RequestDigest, restored.RequestDigest);
        Assert.Equal(decision.Digest, restored.Decision?.Digest);
        Assert.Equal(2, restored.Participants.Length);
    }

    [Fact]
    public async Task FailedSaveDoesNotPublishIntentOrDecision()
    {
        var payloadStore = new TestSwapStatePayloadStore { FailNextSave = true };
        SwapCoordinatorTransaction transaction = CreateTransaction(1);
        using (PersistentSwapTransactionJournal failedIntentJournal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            await Assert.ThrowsAsync<SwapStatePersistenceException>(async () =>
                await failedIntentJournal.TryCreateAsync(transaction));
            Assert.Equal(0, failedIntentJournal.Count);
            Assert.False(failedIntentJournal.TryGet(
                transaction.Context.OperationId,
                out _));
            await Assert.ThrowsAsync<SwapStatePersistenceException>(async () =>
                await failedIntentJournal.TryCreateAsync(transaction));
        }

        using (PersistentSwapTransactionJournal decisionJournal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            await decisionJournal.TryCreateAsync(transaction);
            payloadStore.FailNextSave = true;
            SwapDecision commit = transaction.CreateDecision(
                SwapDecisionOutcome.Commit,
                Now.AddSeconds(1));
            await Assert.ThrowsAsync<SwapStatePersistenceException>(async () =>
                await decisionJournal.TryRecordDecisionAsync(
                    transaction.Context.OperationId,
                    commit));

            Assert.True(decisionJournal.TryGet(
                transaction.Context.OperationId,
                out SwapCoordinatorTransaction? pending));
            Assert.Null(pending.Decision);
            await Assert.ThrowsAsync<SwapStatePersistenceException>(async () =>
                await decisionJournal.TryRecordDecisionAsync(
                    transaction.Context.OperationId,
                    commit));
        }

        using PersistentSwapTransactionJournal restarted =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        Assert.True(restarted.TryGet(
            transaction.Context.OperationId,
            out SwapCoordinatorTransaction? restored));
        Assert.Null(restored.Decision);
    }

    [Fact]
    public async Task ExactReplayIsIdempotentAndDifferentOperationContentConflicts()
    {
        var payloadStore = new TestSwapStatePayloadStore();
        using PersistentSwapTransactionJournal journal =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        SwapCoordinatorTransaction transaction = CreateTransaction(1);

        SwapTransactionWriteResult first = await journal.TryCreateAsync(transaction);
        SwapTransactionWriteResult replay = await journal.TryCreateAsync(transaction);
        SwapCoordinatorTransaction conflicting = CreateTransaction(
            1,
            secondActivityId: "99999999-9999-9999-9999-999999999999");
        SwapTransactionWriteResult conflict = await journal.TryCreateAsync(conflicting);

        Assert.Equal(SwapTransactionWriteStatus.Stored, first.Status);
        Assert.Equal(SwapTransactionWriteStatus.Replayed, replay.Status);
        Assert.Equal(SwapTransactionWriteStatus.Conflict, conflict.Status);
        Assert.Equal(1, journal.Count);
    }

    [Fact]
    public async Task DecisionDigestTamperFailsClosed()
    {
        var payloadStore = new TestSwapStatePayloadStore();
        SwapCoordinatorTransaction transaction = CreateTransaction(1);
        SwapDecision decision = transaction.CreateDecision(
            SwapDecisionOutcome.Abort,
            Now.AddSeconds(1),
            FailureCode.PeerUnavailable);
        using (PersistentSwapTransactionJournal journal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            await journal.TryCreateAsync(transaction);
            await journal.TryRecordDecisionAsync(
                transaction.Context.OperationId,
                decision);
        }

        string encoded = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        encoded = encoded.Replace(
            decision.Digest,
            new string('A', 64),
            StringComparison.Ordinal);
        payloadStore.Payload = Encoding.UTF8.GetBytes(encoded);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore));
    }

    [Fact]
    public async Task UnknownPayloadMemberFailsClosed()
    {
        var payloadStore = new TestSwapStatePayloadStore
        {
            Payload = Encoding.UTF8.GetBytes(
                "{\"formatVersion\":1,\"transactions\":[],\"unknown\":true}"),
        };

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore));
    }

    [Fact]
    public async Task JournalEnforcesBoundWithoutPublishingOverflow()
    {
        var payloadStore = new TestSwapStatePayloadStore();
        using PersistentSwapTransactionJournal journal =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        for (int index = 1;
             index <= PersistentSwapTransactionJournal.MaximumTransactionCount;
             index++)
        {
            SwapTransactionWriteResult result = await journal.TryCreateAsync(
                CreateTransaction(index));
            Assert.Equal(SwapTransactionWriteStatus.Stored, result.Status);
        }

        int savesBeforeOverflow = payloadStore.SaveCount;
        SwapTransactionWriteResult overflow = await journal.TryCreateAsync(
            CreateTransaction(
                PersistentSwapTransactionJournal.MaximumTransactionCount + 1));

        Assert.Equal(SwapTransactionWriteStatus.CapacityExceeded, overflow.Status);
        Assert.Equal(
            PersistentSwapTransactionJournal.MaximumTransactionCount,
            journal.Count);
        Assert.Equal(savesBeforeOverflow, payloadStore.SaveCount);
    }

    internal static SwapCoordinatorTransaction CreateTransaction(
        int index,
        string secondActivityId = "dddddddd-dddd-dddd-dddd-dddddddddddd")
    {
        string operationId = GuidFromIndex(0x10000000, index).ToString("D");
        string correlationId = GuidFromIndex(0x20000000, index).ToString("D");
        DeviceId firstDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId secondDevice =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityInstance first = CreateActivity(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            firstDevice,
            "First private title",
            "FIRST-CONTENT-CANARY");
        ActivityInstance second = CreateActivity(
            secondActivityId,
            secondDevice,
            "Second private title",
            "SECOND-CONTENT-CANARY");
        return SwapCoordinatorTransaction.Create(
            OperationContext.Create(
                OperationId.Parse(operationId),
                CorrelationId.Parse(correlationId),
                Now.AddMinutes(1)),
            first,
            SwapReservationToken.From(GuidFromIndex(0x30000000, index)),
            second,
            SwapReservationToken.From(GuidFromIndex(0x40000000, index)));
    }

    private static ActivityInstance CreateActivity(
        string activityId,
        DeviceId deviceId,
        string title,
        string content)
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse(activityId),
            ActivityKind.Parse("workspace.note/v1"),
            deviceId,
            title,
            $"{{\"text\":\"{content}\"}}");
        return ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(deviceId));
    }

    private static Guid GuidFromIndex(int prefix, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, prefix);
        BitConverter.TryWriteBytes(bytes[4..], index);
        bytes[8] = 0x80;
        bytes[15] = 0x01;
        return new Guid(bytes);
    }
}

internal sealed class TestSwapStatePayloadStore : ISwapStatePayloadStore
{
    public byte[]? Payload { get; set; }

    public bool FailNextSave { get; set; }

    public int? FailOnSaveAttempt { get; set; }

    public int? FailAfterWriteOnSaveAttempt { get; set; }

    public int SaveCount { get; private set; }

    public int SaveAttempts { get; private set; }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Payload?.ToArray());
    }

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveAttempts++;
        if (FailNextSave || FailOnSaveAttempt == SaveAttempts)
        {
            FailNextSave = false;
            throw new IOException("Injected swap state save failure.");
        }

        Payload = payload.ToArray();
        if (FailAfterWriteOnSaveAttempt == SaveAttempts)
        {
            throw new IOException("Injected ambiguous post-write swap state failure.");
        }

        SaveCount++;
        return ValueTask.CompletedTask;
    }
}
