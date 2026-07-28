using System.Security.Cryptography;
using System.Text;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSSceneRepositoryStateStoreTests
{
    [Fact]
    public async Task SceneRepositoryKeychainKeyAndFileUseIndependentPurpose()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-macos-scene-repository-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        byte[] payload =
            "MACOS-SCENE-REPOSITORY-PLAINTEXT-CANARY"u8.ToArray();
        var keychain = new FakeMacOSKeychain();
        try
        {
            var first = new MacOSSceneRepositoryStatePayloadStore(
                statePath,
                keychain);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "MACOS-SCENE-REPOSITORY-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(
                payload,
                await new MacOSSceneRepositoryStatePayloadStore(
                    statePath,
                    keychain).LoadAsync());
            Assert.EndsWith(
                "scene-repository-state.fscr",
                MacOSSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                StringComparison.Ordinal);
            Assert.NotEqual(
                MacOSSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                MacOSSceneApplyStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                MacOSSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                MacOSReplaceStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                MacOSSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                MacOSOperationHistoryStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                MacOSSceneRepositoryStateKeyStore.DefaultService,
                MacOSSceneApplyStateKeyStore.DefaultService);
            Assert.NotEqual(
                MacOSSceneRepositoryStateKeyStore.DefaultService,
                MacOSReplaceStateKeyStore.DefaultService);
            Assert.NotEqual(
                MacOSSceneRepositoryStateKeyStore.DefaultService,
                MacOSOperationHistoryStateKeyStore.DefaultService);
            Assert.NotEqual(
                MacOSSceneRepositoryStateKeyStore.DefaultAccount,
                MacOSSceneApplyStateKeyStore.DefaultAccount);
            Assert.NotEqual(
                MacOSSceneRepositoryStateKeyStore.DefaultAccount,
                MacOSReplaceStateKeyStore.DefaultAccount);
            Assert.NotEqual(
                MacOSSceneRepositoryStateKeyStore.DefaultAccount,
                MacOSOperationHistoryStateKeyStore.DefaultAccount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task KeychainCustodyCreatesThenReturnsTheSameSceneRepositoryKey()
    {
        var keychain = new FakeMacOSKeychain();
        var keyStore = new MacOSSceneRepositoryStateKeyStore(keychain);

        byte[] first = await keyStore.GetOrCreateKeyAsync();
        byte[] second = await keyStore.GetOrCreateKeyAsync();

        Assert.Equal(
            AuthenticatedSceneRepositoryStateFile.KeyBytes,
            first.Length);
        Assert.Equal(first, second);
        Assert.Equal(1, keychain.AddCount);
        Assert.Equal(0, keychain.UpdateCount);
    }

    [Fact]
    public async Task ConcurrentKeyCreateRaceAdoptsTheKeychainWinner()
    {
        byte[] winner = RandomNumberGenerator.GetBytes(
            AuthenticatedSceneRepositoryStateFile.KeyBytes);
        var keychain = new FakeMacOSKeychain
        {
            ConcurrentWinnerOnNextAdd = winner,
        };
        var keyStore = new MacOSSceneRepositoryStateKeyStore(keychain);

        byte[] adopted = await keyStore.GetOrCreateKeyAsync();

        Assert.Equal(winner, adopted);
        Assert.Equal(1, keychain.AddCount);
        Assert.Equal(0, keychain.UpdateCount);
    }

    [Fact]
    public async Task InvalidLengthKeychainKeyIsRejectedWithoutReplacement()
    {
        var keychain = new FakeMacOSKeychain();
        keychain.Seed(
            MacOSSceneRepositoryStateKeyStore.DefaultService,
            MacOSSceneRepositoryStateKeyStore.DefaultAccount,
            new byte[16]);
        var keyStore = new MacOSSceneRepositoryStateKeyStore(keychain);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await keyStore.GetOrCreateKeyAsync());

        Assert.Equal(0, keychain.AddCount);
        Assert.Equal(0, keychain.UpdateCount);
        Assert.Equal(
            new byte[16],
            keychain.LoadGenericPassword(
                MacOSSceneRepositoryStateKeyStore.DefaultService,
                MacOSSceneRepositoryStateKeyStore.DefaultAccount));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotCallKeychainOrCreateStateFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-macos-scene-repository-cancelled-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        var keychain = new FakeMacOSKeychain();
        var store = new MacOSSceneRepositoryStatePayloadStore(statePath, keychain);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.SaveAsync(
                    "MACOS-SCENE-REPOSITORY-CANCELLED"u8.ToArray(),
                    cancellation.Token));

            Assert.Equal(0, keychain.AddCount);
            Assert.Equal(0, keychain.UpdateCount);
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
    public async Task NativeSceneRepositoryStoreRoundTripsOnlyOnMacOS()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-macos-scene-repository-native-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        string service =
            $"app.flowspan.scene-repository-state-key.test-{Guid.NewGuid():N}";
        string account = $"test-scene-repository-{Guid.NewGuid():N}";
        var keychain = new SecurityFrameworkKeychain();
        var store = new MacOSSceneRepositoryStatePayloadStore(
            statePath,
            new MacOSSceneRepositoryStateKeyStore(keychain, service, account));
        byte[] payload = "MACOS-NATIVE-SCENE-REPOSITORY-STATE"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await store.SaveAsync(payload));
                return;
            }

            await store.SaveAsync(payload);
            Assert.Equal(
                payload,
                await new MacOSSceneRepositoryStatePayloadStore(
                    statePath,
                    new MacOSSceneRepositoryStateKeyStore(
                        keychain,
                        service,
                        account)).LoadAsync());
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                keychain.DeleteGenericPassword(service, account);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NativeOperationHistoryStoreRoundTripsOnlyOnMacOS()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-macos-history-native-{Guid.NewGuid():N}");
        string service = $"app.flowspan.history.test-{Guid.NewGuid():N}";
        string account = $"test-history-{Guid.NewGuid():N}";
        var keychain = new SecurityFrameworkKeychain();
        var store = new MacOSOperationHistoryStatePayloadStore(
            Path.Combine(directory, "history.fsoh"),
            new MacOSOperationHistoryStateKeyStore(keychain, service, account));
        byte[] payload = "MACOS-NATIVE-OPERATION-HISTORY"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await store.SaveAsync(payload));
                return;
            }

            await store.SaveAsync(payload);
            Assert.Equal(payload, await store.LoadAsync());
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                keychain.DeleteGenericPassword(service, account);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeMacOSKeychain : IMacOSKeychain
    {
        private readonly Dictionary<(string Service, string Account), byte[]> values = [];

        public int AddCount { get; private set; }

        public byte[]? ConcurrentWinnerOnNextAdd { get; init; }

        public int UpdateCount { get; private set; }

        public bool DeleteGenericPassword(string service, string account) =>
            values.Remove((service, account));

        public byte[]? LoadGenericPassword(string service, string account) =>
            values.TryGetValue((service, account), out byte[]? value)
                ? value.ToArray()
                : null;

        public void Seed(string service, string account, ReadOnlySpan<byte> value) =>
            values[(service, account)] = value.ToArray();

        public bool TryAddGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            AddCount++;
            if (ConcurrentWinnerOnNextAdd is not null && AddCount == 1)
            {
                values[(service, account)] = ConcurrentWinnerOnNextAdd.ToArray();
                return false;
            }

            return values.TryAdd((service, account), value.ToArray());
        }

        public bool UpdateGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            UpdateCount++;
            if (!values.ContainsKey((service, account)))
            {
                return false;
            }

            values[(service, account)] = value.ToArray();
            return true;
        }
    }
}
