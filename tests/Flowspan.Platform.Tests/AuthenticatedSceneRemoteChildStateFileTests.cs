using System.Text;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedSceneRemoteChildStateFileTests
{
    [Fact]
    public async Task EncryptedAtomicFileUsesIndependentPurposeAndRejectsTamper()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-scene-remote-child-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-remote-child.fsrc");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"child\":\"REMOTE-CHILD-PLAINTEXT-CANARY\"}");
        var keyStore = new FixedKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedSceneRemoteChildStateFile(path, keyStore);
        try
        {
            await store.SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("FSRC"u8.ToArray(), protectedBytes[..4]);
            Assert.DoesNotContain(
                "REMOTE-CHILD-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            byte[]? restored = await new AuthenticatedSceneRemoteChildStateFile(
                path,
                keyStore).LoadAsync();
            Assert.Equal(payload, restored);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneApplyStateFile(path, keyStore)
                    .LoadAsync());

            protectedBytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, protectedBytes);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneRemoteChildStateFile(path, keyStore)
                    .LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedKeyStore(byte[] key) :
        ISceneRemoteChildStateKeyStore,
        ISceneApplyStateKeyStore
    {
        public ValueTask<byte[]> GetOrCreateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(key.ToArray());
        }
    }
}
