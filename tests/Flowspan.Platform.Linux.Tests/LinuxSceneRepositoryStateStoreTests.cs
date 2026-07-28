using System.Security.Cryptography;
using System.Text;
using Flowspan.Platform.Linux;

namespace Flowspan.Platform.Linux.Tests;

public sealed class LinuxSceneRepositoryStateStoreTests
{
    [Fact]
    public async Task SceneRepositorySecretServiceKeyAndFileUseIndependentPurpose()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-scene-repository-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        string keyLockPath = Path.Combine(directory, "scene-repository-key.lock");
        byte[] payload =
            "LINUX-SCENE-REPOSITORY-PLAINTEXT-CANARY"u8.ToArray();
        using var runner = new FakeSecretToolRunner();
        try
        {
            var first = new LinuxSceneRepositoryStatePayloadStore(
                statePath,
                runner,
                keyLockPath);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "LINUX-SCENE-REPOSITORY-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(
                payload,
                await new LinuxSceneRepositoryStatePayloadStore(
                    statePath,
                    runner,
                    keyLockPath).LoadAsync());
            Assert.EndsWith(
                "scene-repository-state.fscr",
                LinuxSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                StringComparison.Ordinal);
            Assert.NotEqual(
                LinuxSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                LinuxSceneApplyStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                LinuxSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                LinuxReplaceStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                LinuxSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                LinuxOperationHistoryStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                LinuxSceneRepositoryStateKeyStore.GetDefaultCoordinationLockPath(),
                LinuxSceneApplyStateKeyStore.GetDefaultCoordinationLockPath());
            Assert.NotEqual(
                LinuxSceneRepositoryStateKeyStore.GetDefaultCoordinationLockPath(),
                LinuxReplaceStateKeyStore.GetDefaultCoordinationLockPath());
            Assert.NotEqual(
                LinuxSceneRepositoryStateKeyStore.GetDefaultCoordinationLockPath(),
                LinuxOperationHistoryStateKeyStore.GetDefaultCoordinationLockPath());
            Assert.NotEqual(
                LinuxSceneRepositoryStateKeyStore.DefaultAccount,
                LinuxSceneApplyStateKeyStore.DefaultAccount);
            Assert.NotEqual(
                LinuxSceneRepositoryStateKeyStore.DefaultAccount,
                LinuxReplaceStateKeyStore.DefaultAccount);
            Assert.NotEqual(
                LinuxSceneRepositoryStateKeyStore.DefaultAccount,
                LinuxOperationHistoryStateKeyStore.DefaultAccount);
            Assert.Contains(runner.Arguments, static arguments =>
                arguments.Contains(
                    "scene-repository-state-key",
                    StringComparer.Ordinal));
            Assert.DoesNotContain(runner.Arguments, static arguments =>
                arguments.Contains(
                    "scene-apply-state-key",
                    StringComparer.Ordinal));
            Assert.DoesNotContain(runner.Arguments, static arguments =>
                arguments.Contains("replace-state-key", StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SecretServiceCustodyCreatesThenReturnsTheSameSceneRepositoryKey()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-scene-repository-key-{Guid.NewGuid():N}");
        string keyLockPath = Path.Combine(directory, "scene-repository-key.lock");
        using var runner = new FakeSecretToolRunner();
        try
        {
            var keyStore = new LinuxSceneRepositoryStateKeyStore(
                runner,
                keyLockPath);

            byte[] first = await keyStore.GetOrCreateKeyAsync();
            byte[] second = await keyStore.GetOrCreateKeyAsync();

            Assert.Equal(
                AuthenticatedSceneRepositoryStateFile.KeyBytes,
                first.Length);
            Assert.Equal(first, second);
            Assert.Equal(1, runner.StoreCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentFirstKeyCreationsConvergeOnOneStoredKey()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-scene-repository-race-{Guid.NewGuid():N}");
        string keyLockPath = Path.Combine(directory, "scene-repository-key.lock");
        using var runner = new FakeSecretToolRunner();
        try
        {
            Task<byte[]> first = new LinuxSceneRepositoryStateKeyStore(
                runner,
                keyLockPath).GetOrCreateKeyAsync().AsTask();
            Task<byte[]> second = new LinuxSceneRepositoryStateKeyStore(
                runner,
                keyLockPath).GetOrCreateKeyAsync().AsTask();
            byte[][] keys = await Task.WhenAll(first, second);

            Assert.Equal(keys[0], keys[1]);
            Assert.Equal(
                AuthenticatedSceneRepositoryStateFile.KeyBytes,
                keys[0].Length);
            Assert.Equal(1, runner.StoreCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidLengthSecretServiceKeyIsRejectedWithoutReplacement()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-scene-repository-corrupt-{Guid.NewGuid():N}");
        string keyLockPath = Path.Combine(directory, "scene-repository-key.lock");
        using var runner = new FakeSecretToolRunner();
        runner.SeedBase64(
            Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[16])));
        var keyStore = new LinuxSceneRepositoryStateKeyStore(runner, keyLockPath);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await keyStore.GetOrCreateKeyAsync());

            Assert.Equal(0, runner.StoreCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreCancelledSaveInvokesNoSecretToolAndCreatesNoStateFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-scene-repository-cancelled-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        string keyLockPath = Path.Combine(directory, "scene-repository-key.lock");
        using var runner = new FakeSecretToolRunner();
        var store = new LinuxSceneRepositoryStatePayloadStore(
            statePath,
            runner,
            keyLockPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.SaveAsync(
                    "LINUX-SCENE-REPOSITORY-CANCELLED"u8.ToArray(),
                    cancellation.Token));

            Assert.Empty(runner.Arguments);
            Assert.Equal(0, runner.StoreCount);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductionStoreRejectsOtherPlatformsAndMissingToolIsStructured()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-scene-repository-native-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        byte[] payload = "LINUX-NATIVE-SCENE-REPOSITORY-STATE"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                var store = new LinuxSceneRepositoryStatePayloadStore();
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await store.SaveAsync(payload));
                return;
            }

            var missing = new LinuxSceneRepositoryStatePayloadStore(
                statePath,
                new SecretToolProcessRunner(
                    Path.Combine(directory, "missing-secret-tool")),
                Path.Combine(directory, "scene-repository-key.lock"));
            LinuxSecretServiceException exception =
                await Assert.ThrowsAsync<LinuxSecretServiceException>(async () =>
                    await missing.SaveAsync(payload));

            Assert.Equal("start", exception.Operation);
            Assert.Null(exception.ExitCode);
            Assert.NotEmpty(exception.RecoveryAction);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductionHistoryStoreRejectsOtherPlatformsOrMissingTool()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-history-native-{Guid.NewGuid():N}");
        byte[] payload = "LINUX-NATIVE-OPERATION-HISTORY"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await new LinuxOperationHistoryStatePayloadStore()
                        .SaveAsync(payload));
                return;
            }

            var missing = new LinuxOperationHistoryStatePayloadStore(
                Path.Combine(directory, "history.fsoh"),
                new SecretToolProcessRunner(
                    Path.Combine(directory, "missing-secret-tool")),
                Path.Combine(directory, "history-key.lock"));
            LinuxSecretServiceException exception =
                await Assert.ThrowsAsync<LinuxSecretServiceException>(async () =>
                    await missing.SaveAsync(payload));

            Assert.Equal("start", exception.Operation);
            Assert.Null(exception.ExitCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeSecretToolRunner : ISecretToolProcessRunner, IDisposable
    {
        private readonly Lock gate = new();
        private byte[]? storedBase64;

        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public int StoreCount { get; private set; }

        public ValueTask<SecretToolProcessResult> RunAsync(
            SecretToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                Arguments.Add(invocation.Arguments);
                return ValueTask.FromResult(invocation.Verb switch
                {
                    "lookup" => storedBase64 is null
                        ? new SecretToolProcessResult(1, [], [])
                        : new SecretToolProcessResult(
                            0,
                            [.. storedBase64, (byte)'\n'],
                            []),
                    "store" => Store(invocation.StandardInput),
                    _ => throw new InvalidOperationException(
                        "Unexpected secret-tool verb."),
                });
            }
        }

        public void SeedBase64(ReadOnlySpan<byte> value)
        {
            lock (gate)
            {
                if (storedBase64 is not null)
                {
                    CryptographicOperations.ZeroMemory(storedBase64);
                }

                storedBase64 = value.ToArray();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (storedBase64 is not null)
                {
                    CryptographicOperations.ZeroMemory(storedBase64);
                    storedBase64 = null;
                }
            }
        }

        private SecretToolProcessResult Store(ReadOnlyMemory<byte> standardInput)
        {
            StoreCount++;
            if (storedBase64 is not null)
            {
                CryptographicOperations.ZeroMemory(storedBase64);
            }

            storedBase64 = standardInput.ToArray();
            return new SecretToolProcessResult(0, [], []);
        }
    }
}
