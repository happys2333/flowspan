using System.Text;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedSceneRepositoryStateFileTests
{
    [Fact]
    public async Task EncryptedAtomicFileUsesRepositoryMagicWithoutPlaintext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-scene-repository-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-repository-state.fscr");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"scenes\":\"FLOWSPAN-SCENE-REPOSITORY-CANARY\"}");
        var keyStore = new FixedSceneRepositoryStateKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedSceneRepositoryStateFile(path, keyStore);
        try
        {
            await store.SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("FSCR"u8.ToArray(), protectedBytes[..4]);
            Assert.DoesNotContain(
                "FLOWSPAN-SCENE-REPOSITORY-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            byte[]? restored = await new AuthenticatedSceneRepositoryStateFile(
                path,
                keyStore).LoadAsync();
            Assert.Equal(payload, restored);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(path));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CrossPurposeStateFilesRejectEachOtherInBothDirections()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-scene-repository-cross-{Guid.NewGuid():N}");
        string repositoryPath = Path.Combine(directory, "scene-repository-state.fscr");
        string applyPath = Path.Combine(directory, "scene-apply-state.fsaf");
        var keyStore = new FixedSceneRepositoryStateKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        try
        {
            await new AuthenticatedSceneRepositoryStateFile(repositoryPath, keyStore)
                .SaveAsync("{\"purpose\":\"repository\"}"u8.ToArray());
            await new AuthenticatedSceneApplyStateFile(applyPath, keyStore)
                .SaveAsync("{\"purpose\":\"apply\"}"u8.ToArray());

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneApplyStateFile(repositoryPath, keyStore)
                    .LoadAsync());
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneRepositoryStateFile(applyPath, keyStore)
                    .LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TamperedOrTruncatedStateFileFailsIntegrityVerification()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-scene-repository-tamper-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-repository-state.fscr");
        var keyStore = new FixedSceneRepositoryStateKeyStore(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());
        var store = new AuthenticatedSceneRepositoryStateFile(path, keyStore);
        try
        {
            await store.SaveAsync("{\"scenes\":[]}"u8.ToArray());
            byte[] protectedBytes = await File.ReadAllBytesAsync(path);

            byte[] tampered = protectedBytes.ToArray();
            tampered[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, tampered);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneRepositoryStateFile(path, keyStore)
                    .LoadAsync());

            await File.WriteAllBytesAsync(path, protectedBytes[..^1]);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneRepositoryStateFile(path, keyStore)
                    .LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingStateFileLoadsAsNullWithoutKeyAccess()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-missing-scene-repository-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-repository-state.fscr");
        var keyStore = new RecordingSceneRepositoryStateKeyStore();
        var store = new AuthenticatedSceneRepositoryStateFile(path, keyStore);

        byte[]? restored = await store.LoadAsync();

        Assert.Null(restored);
        Assert.Equal(0, keyStore.CallCount);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task PreCancelledMissingFileLoadDoesNotAccessKeyOrCreateStorage()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-cancelled-scene-repository-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-repository-state.fscr");
        var keyStore = new RecordingSceneRepositoryStateKeyStore();
        var store = new AuthenticatedSceneRepositoryStateFile(path, keyStore);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(cancellation.Token));

        Assert.Equal(0, keyStore.CallCount);
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task OversizedPayloadSaveFailsBeforeKeyOrFileMutation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-oversized-scene-repository-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "scene-repository-state.fscr");
        var keyStore = new RecordingSceneRepositoryStateKeyStore();
        var store = new AuthenticatedSceneRepositoryStateFile(path, keyStore);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.SaveAsync(
                new byte[PersistentSceneRepository.MaximumPayloadBytes + 1]));

        Assert.Equal(0, keyStore.CallCount);
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(path));
    }

    private sealed class FixedSceneRepositoryStateKeyStore(byte[] key) :
        ISceneRepositoryStateKeyStore,
        ISceneApplyStateKeyStore
    {
        public ValueTask<byte[]> GetOrCreateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(key.ToArray());
        }
    }

    private sealed class RecordingSceneRepositoryStateKeyStore :
        ISceneRepositoryStateKeyStore
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
