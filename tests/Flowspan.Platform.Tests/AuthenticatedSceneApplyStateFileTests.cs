using System.Text;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedSceneApplyStateFileTests
{
    [Fact]
    public async Task EncryptedAtomicFileUsesIndependentMagicAndRejectsTamper()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-scene-apply-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-apply-state.fsaf");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"attempt\":\"SCENE-APPLY-PLAINTEXT-CANARY\","
            + "\"title\":\"SCENE-APPLY-TITLE-CANARY\","
            + "\"exception\":\"SCENE-APPLY-EXCEPTION-CANARY\"}");
        var keyStore = new FixedSceneApplyStateKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedSceneApplyStateFile(path, keyStore);
        try
        {
            await store.SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("FSAF"u8.ToArray(), protectedBytes[..4]);
            Assert.DoesNotContain(
                "SCENE-APPLY-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SCENE-APPLY-TITLE-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SCENE-APPLY-EXCEPTION-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            byte[]? restored = await new AuthenticatedSceneApplyStateFile(
                path,
                keyStore).LoadAsync();
            Assert.Equal(payload, restored);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSwapStateFile(path, keyStore).LoadAsync());

            protectedBytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, protectedBytes);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneApplyStateFile(path, keyStore)
                    .LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreCancelledMissingFileLoadDoesNotAccessKeyOrCreateStorage()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-cancelled-scene-apply-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-apply-state.fsaf");
        var keyStore = new RecordingSceneApplyStateKeyStore();
        var store = new AuthenticatedSceneApplyStateFile(path, keyStore);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(cancellation.Token));

        Assert.Equal(0, keyStore.CallCount);
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(path));
    }

    private sealed class FixedSceneApplyStateKeyStore(byte[] key) :
        ISceneApplyStateKeyStore,
        ISwapStateKeyStore
    {
        public ValueTask<byte[]> GetOrCreateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(key.ToArray());
        }
    }

    private sealed class RecordingSceneApplyStateKeyStore :
        ISceneApplyStateKeyStore
    {
        public int CallCount { get; private set; }

        public ValueTask<byte[]> GetOrCreateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException(
                "cancelled-load-key-access-canary");
        }
    }
}
