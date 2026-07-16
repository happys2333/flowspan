using System.Text;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedSwapStateFileTests
{
    [Fact]
    public async Task EncryptedAtomicFileUsesIndependentMagicAndRejectsTamper()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-swap-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "swap-state.fssf");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"intent\":\"SWAP-STATE-PLAINTEXT-CANARY\"}");
        var keyStore = new FixedSwapStateKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedSwapStateFile(path, keyStore);
        try
        {
            await store.SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("FSSF"u8.ToArray(), protectedBytes[..4]);
            Assert.DoesNotContain(
                "SWAP-STATE-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            byte[]? restored = await new AuthenticatedSwapStateFile(path, keyStore)
                .LoadAsync();
            Assert.Equal(payload, restored);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedReplaceStateFile(path, keyStore).LoadAsync());

            protectedBytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, protectedBytes);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSwapStateFile(path, keyStore).LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BoundsAndPreCancellationFailBeforeKeyOrFileMutation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-swap-state-bounds-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "swap-state.fssf");
        var keyStore = new FixedSwapStateKeyStore(new byte[32]);
        var store = new AuthenticatedSwapStateFile(path, keyStore);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.SaveAsync(
                new byte[PersistentSwapTransactionJournal.MaximumPayloadBytes + 1]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.SaveAsync("cancelled"u8.ToArray(), cancellation.Token));

        Assert.Equal(0, keyStore.CallCount);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task PersistentJournalRoundTripsThroughAuthenticatedFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-protected-swap-journal-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "swap-state.fssf");
        var keyStore = new FixedSwapStateKeyStore(
            Enumerable.Range(32, 32).Select(static value => (byte)value).ToArray());
        SwapCoordinatorTransaction transaction = CreateTransaction();
        SwapDecision abort = transaction.CreateDecision(
            SwapDecisionOutcome.Abort,
            new DateTimeOffset(2026, 7, 16, 2, 0, 1, TimeSpan.Zero),
            FailureCode.PeerUnavailable);
        try
        {
            using (PersistentSwapTransactionJournal journal =
                   await PersistentSwapTransactionJournal.OpenAsync(
                       new AuthenticatedSwapStateFile(path, keyStore)))
            {
                await journal.TryCreateAsync(transaction);
                await journal.TryRecordDecisionAsync(
                    transaction.Context.OperationId,
                    abort);
            }

            using PersistentSwapTransactionJournal restarted =
                await PersistentSwapTransactionJournal.OpenAsync(
                    new AuthenticatedSwapStateFile(path, keyStore));
            Assert.True(restarted.TryGet(
                transaction.Context.OperationId,
                out SwapCoordinatorTransaction? restored));
            Assert.Equal(transaction.RequestDigest, restored.RequestDigest);
            Assert.Equal(abort.Digest, restored.Decision?.Digest);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SwapCoordinatorTransaction CreateTransaction()
    {
        DeviceId firstDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId secondDevice =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityInstance first = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                firstDevice,
                "First",
                "{\"text\":\"first\"}"),
            ActivityPlacement.On(firstDevice));
        ActivityInstance second = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                ActivityKind.Parse("workspace.note/v1"),
                secondDevice,
                "Second",
                "{\"text\":\"second\"}"),
            ActivityPlacement.On(secondDevice));
        return SwapCoordinatorTransaction.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                new DateTimeOffset(2026, 7, 16, 2, 1, 0, TimeSpan.Zero)),
            first,
            SwapReservationToken.From(
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")),
            second,
            SwapReservationToken.From(
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
    }

    private sealed class FixedSwapStateKeyStore(byte[] key) :
        ISwapStateKeyStore,
        IReplaceStateKeyStore
    {
        public int CallCount { get; private set; }

        public ValueTask<byte[]> GetOrCreateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(key.ToArray());
        }
    }
}
