using System.Text;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedReplaceStateFileTests
{
    [Fact]
    public async Task EncryptedAtomicFileRoundTripsWithoutPlaintextAndRejectsTamper()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-replace-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "replace-state.fsrf");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"descriptor\":\"REPLACE-STATE-PLAINTEXT-CANARY\"}");
        var keyStore = new FixedReplaceStateKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedReplaceStateFile(path, keyStore);
        try
        {
            await store.SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain(
                "REPLACE-STATE-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            byte[]? restored = await new AuthenticatedReplaceStateFile(path, keyStore)
                .LoadAsync();
            Assert.Equal(payload, restored);

            protectedBytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, protectedBytes);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedReplaceStateFile(path, keyStore).LoadAsync());
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
            $"flowspan-replace-state-bounds-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "replace-state.fsrf");
        var keyStore = new FixedReplaceStateKeyStore(new byte[32]);
        var store = new AuthenticatedReplaceStateFile(path, keyStore);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.SaveAsync(
                new byte[PersistentReplaceStateStore.MaximumPayloadBytes + 1]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.SaveAsync("cancelled"u8.ToArray(), cancellation.Token));

        Assert.Equal(0, keyStore.CallCount);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(directory));
    }

    private sealed class FixedReplaceStateKeyStore(byte[] key) : IReplaceStateKeyStore
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
