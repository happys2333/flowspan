using System.Text;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class AuthenticatedOperationHistoryStateFileTests
{
    [Fact]
    public async Task HistoryFileUsesDedicatedMagicAndHidesPlaintext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-history-state-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "history.fsoh");
        byte[] payload =
            "{\"receipt\":\"HISTORY-PLAINTEXT-CANARY\"}"u8.ToArray();
        var keys = new FixedHistoryKeyStore();
        try
        {
            await new AuthenticatedOperationHistoryStateFile(path, keys)
                .SaveAsync(payload);

            byte[] protectedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("FSOH"u8.ToArray(), protectedBytes[..4]);
            Assert.DoesNotContain(
                "HISTORY-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(protectedBytes),
                StringComparison.Ordinal);
            Assert.Equal(
                payload,
                await new AuthenticatedOperationHistoryStateFile(path, keys)
                    .LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HistoryAndSceneFilesRejectEachOtherInBothDirections()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-history-cross-{Guid.NewGuid():N}");
        string historyPath = Path.Combine(directory, "history.fsoh");
        string scenePath = Path.Combine(directory, "scene.fscr");
        var keys = new FixedHistoryKeyStore();
        try
        {
            await new AuthenticatedOperationHistoryStateFile(historyPath, keys)
                .SaveAsync("{\"purpose\":\"history\"}"u8.ToArray());
            await new AuthenticatedSceneRepositoryStateFile(scenePath, keys)
                .SaveAsync("{\"purpose\":\"scene\"}"u8.ToArray());

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedSceneRepositoryStateFile(historyPath, keys)
                    .LoadAsync());
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new AuthenticatedOperationHistoryStateFile(scenePath, keys)
                    .LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TamperAndOversizeFailBeforePublishingPlaintext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-history-tamper-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "history.fsoh");
        var keys = new FixedHistoryKeyStore();
        var store = new AuthenticatedOperationHistoryStateFile(path, keys);
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await store.SaveAsync(
                    new byte[OperationHistoryStorageLimits.MaximumPayloadBytes + 1]));
            Assert.False(File.Exists(path));

            await store.SaveAsync("{\"entries\":[]}"u8.ToArray());
            byte[] tampered = await File.ReadAllBytesAsync(path);
            tampered[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, tampered);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedHistoryKeyStore :
        IOperationHistoryStateKeyStore,
        ISceneRepositoryStateKeyStore
    {
        private static readonly byte[] Key = Enumerable.Range(1, 32)
            .Select(static value => (byte)value)
            .ToArray();

        public ValueTask<byte[]> GetOrCreateKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Key.ToArray());
        }
    }
}
