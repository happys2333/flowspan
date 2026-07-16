using System.Text;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedSwapEndpointStateFileTests
{
    [Fact]
    public async Task EncryptedAtomicFileUsesIndependentMagicAndRejectsTamper()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-swap-endpoint-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "swap-endpoint-state.fsef");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"reservation\":\"SWAP-ENDPOINT-PLAINTEXT-CANARY\"}");
        var keyStore = new FixedEndpointStateKeyStore(
            Enumerable.Range(64, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedSwapEndpointStateFile(path, keyStore);
        try
        {
            await store.SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("FSEF"u8.ToArray(), protectedBytes[..4]);
            Assert.DoesNotContain(
                "SWAP-ENDPOINT-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            Assert.Equal(
                payload,
                await new AuthenticatedSwapEndpointStateFile(path, keyStore)
                    .LoadAsync());
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSwapStateFile(path, keyStore).LoadAsync());

            protectedBytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, protectedBytes);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSwapEndpointStateFile(path, keyStore)
                    .LoadAsync());
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
            $"flowspan-swap-endpoint-bounds-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "swap-endpoint-state.fsef");
        var keyStore = new FixedEndpointStateKeyStore(new byte[32]);
        var store = new AuthenticatedSwapEndpointStateFile(path, keyStore);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.SaveAsync(
                new byte[PersistentSwapEndpointJournal.MaximumPayloadBytes + 1]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.SaveAsync("cancelled"u8.ToArray(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(cancellation.Token));

        Assert.Equal(0, keyStore.CallCount);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task PersistentEndpointJournalRoundTripsThroughAuthenticatedFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-protected-swap-endpoint-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "swap-endpoint-state.fsef");
        var keyStore = new FixedEndpointStateKeyStore(
            Enumerable.Range(96, 32).Select(static value => (byte)value).ToArray());
        TestState state = CreateState();
        try
        {
            using (PersistentSwapEndpointJournal journal =
                   await PersistentSwapEndpointJournal.OpenAsync(
                       state.DeviceId,
                       new AuthenticatedSwapEndpointStateFile(path, keyStore)))
            {
                await journal.TryPrepareAsync(
                    state.CorrelationId,
                    state.Reservation);
                await journal.TryRecordDecisionAsync(
                    state.CorrelationId,
                    state.Decision);
            }

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain(
                "ENDPOINT-CONTENT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            using PersistentSwapEndpointJournal restarted =
                await PersistentSwapEndpointJournal.OpenAsync(
                    state.DeviceId,
                    new AuthenticatedSwapEndpointStateFile(path, keyStore));
            Assert.True(restarted.TryGet(
                state.Reservation.OperationId,
                out SwapEndpointRecord? restored));
            Assert.Equal(SwapReservationPhase.Committed, restored.Reservation?.Phase);
            Assert.Equal(state.Decision.Digest, restored.Decision?.Digest);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TestState CreateState()
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
                ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ActivityKind.Parse("workspace.note/v1"),
                secondDevice,
                "Endpoint canary",
                "{\"text\":\"ENDPOINT-CONTENT-CANARY\"}"),
            ActivityPlacement.On(secondDevice));
        OperationContext context = OperationContext.Create(
            OperationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            new DateTimeOffset(2026, 7, 16, 3, 0, 0, TimeSpan.Zero));
        SwapReservationToken firstToken = SwapReservationToken.From(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        SwapReservationToken secondToken = SwapReservationToken.From(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        SwapReservation reservation = SwapReservation.Prepare(
            context.OperationId,
            firstToken,
            first,
            second,
            context.Deadline);
        SwapDecision decision = SwapCoordinatorTransaction.Create(
                context,
                first,
                firstToken,
                second,
                secondToken)
            .CreateDecision(
                SwapDecisionOutcome.Commit,
                context.Deadline.AddSeconds(-1));
        return new TestState(
            firstDevice,
            context.CorrelationId,
            reservation,
            decision);
    }

    private sealed record TestState(
        DeviceId DeviceId,
        CorrelationId CorrelationId,
        SwapReservation Reservation,
        SwapDecision Decision);

    private sealed class FixedEndpointStateKeyStore(byte[] key) :
        ISwapEndpointStateKeyStore,
        ISwapStateKeyStore
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
