using System.Text;
using Flowspan.Platform.Windows;

namespace Flowspan.Platform.Windows.Tests;

public sealed class WindowsSceneRepositoryStateStoreTests
{
    [Fact]
    public async Task SceneRepositoryDpapiKeyAndFileUseIndependentPurpose()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-scene-repository-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        string keyPath = Path.Combine(
            directory,
            "scene-repository-state-key.dpapi");
        byte[] payload =
            "WINDOWS-SCENE-REPOSITORY-PLAINTEXT-CANARY"u8.ToArray();
        var protector = new FakeWindowsDataProtector();
        try
        {
            var first = new WindowsSceneRepositoryStatePayloadStore(
                statePath,
                keyPath,
                protector);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "WINDOWS-SCENE-REPOSITORY-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(
                payload,
                await new WindowsSceneRepositoryStatePayloadStore(
                    statePath,
                    keyPath,
                    protector).LoadAsync());
            Assert.EndsWith(
                "scene-repository-state.fscr",
                WindowsSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                StringComparison.Ordinal);
            Assert.NotEqual(
                WindowsSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                WindowsSceneApplyStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                WindowsSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                WindowsReplaceStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                WindowsSceneRepositoryStatePayloadStore.GetDefaultStatePath(),
                WindowsOperationHistoryStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                WindowsSceneRepositoryStateKeyStore.GetDefaultKeyPath(),
                WindowsSceneApplyStateKeyStore.GetDefaultKeyPath());
            Assert.NotEqual(
                WindowsSceneRepositoryStateKeyStore.GetDefaultKeyPath(),
                WindowsReplaceStateKeyStore.GetDefaultKeyPath());
            Assert.NotEqual(
                WindowsSceneRepositoryStateKeyStore.GetDefaultKeyPath(),
                WindowsOperationHistoryStateKeyStore.GetDefaultKeyPath());
            Assert.NotEqual(
                WindowsSceneRepositoryStateKeyStore.ProtectionContext,
                WindowsSceneApplyStateKeyStore.ProtectionContext);
            Assert.NotEqual(
                WindowsSceneRepositoryStateKeyStore.ProtectionContext,
                WindowsReplaceStateKeyStore.ProtectionContext);
            Assert.NotEqual(
                WindowsSceneRepositoryStateKeyStore.ProtectionContext,
                WindowsOperationHistoryStateKeyStore.ProtectionContext);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DpapiCustodyCreatesThenReturnsTheSameSceneRepositoryKey()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-scene-repository-key-{Guid.NewGuid():N}");
        string keyPath = Path.Combine(
            directory,
            "scene-repository-state-key.dpapi");
        var protector = new FakeWindowsDataProtector();
        try
        {
            var keyStore = new WindowsSceneRepositoryStateKeyStore(
                keyPath,
                protector);

            byte[] first = await keyStore.GetOrCreateKeyAsync();
            byte[] second = await keyStore.GetOrCreateKeyAsync();

            Assert.Equal(
                AuthenticatedSceneRepositoryStateFile.KeyBytes,
                first.Length);
            Assert.Equal(first, second);
            Assert.Equal(1, protector.ProtectCount);
            Assert.Equal(1, protector.UnprotectCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentFirstKeyCreationsConvergeAcrossKeyStoreInstances()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-scene-repository-race-{Guid.NewGuid():N}");
        string keyPath = Path.Combine(
            directory,
            "scene-repository-state-key.dpapi");
        var protector = new FakeWindowsDataProtector();
        try
        {
            Task<byte[]> first = new WindowsSceneRepositoryStateKeyStore(
                keyPath,
                protector).GetOrCreateKeyAsync().AsTask();
            Task<byte[]> second = new WindowsSceneRepositoryStateKeyStore(
                keyPath,
                protector).GetOrCreateKeyAsync().AsTask();
            byte[][] keys = await Task.WhenAll(first, second);

            Assert.Equal(keys[0], keys[1]);
            Assert.Equal(
                AuthenticatedSceneRepositoryStateFile.KeyBytes,
                keys[0].Length);
            Assert.True(File.Exists(keyPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidLengthProtectedKeyIsRejectedWithoutReplacement()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-scene-repository-corrupt-{Guid.NewGuid():N}");
        string keyPath = Path.Combine(
            directory,
            "scene-repository-state-key.dpapi");
        var protector = new FakeWindowsDataProtector();
        try
        {
            Directory.CreateDirectory(directory);
            byte[] shortKey = protector.Protect(new byte[16]);
            await File.WriteAllBytesAsync(keyPath, shortKey);
            var keyStore = new WindowsSceneRepositoryStateKeyStore(
                keyPath,
                protector);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await keyStore.GetOrCreateKeyAsync());

            Assert.Equal(shortKey, await File.ReadAllBytesAsync(keyPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreCancelledSaveCreatesNoStateArtifact()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-scene-repository-cancelled-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        string keyPath = Path.Combine(
            directory,
            "scene-repository-state-key.dpapi");
        var protector = new FakeWindowsDataProtector();
        var store = new WindowsSceneRepositoryStatePayloadStore(
            statePath,
            keyPath,
            protector);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.SaveAsync(
                    "WINDOWS-SCENE-REPOSITORY-CANCELLED"u8.ToArray(),
                    cancellation.Token));

            Assert.Equal(0, protector.ProtectCount);
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
    public async Task NativeSceneRepositoryStoreRoundTripsOnlyOnWindows()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-scene-repository-native-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "scene-repository-state.fscr");
        string keyPath = Path.Combine(
            directory,
            "scene-repository-state-key.dpapi");
        var store = new WindowsSceneRepositoryStatePayloadStore(
            statePath,
            keyPath,
            new CurrentUserDpapiProtector(
                $"Flowspan.Tests.SceneRepositoryState.{Guid.NewGuid():N}"));
        byte[] payload = "WINDOWS-NATIVE-SCENE-REPOSITORY-STATE"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsWindows())
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
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NativeOperationHistoryStoreRoundTripsOnlyOnWindows()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-history-native-{Guid.NewGuid():N}");
        var store = new WindowsOperationHistoryStatePayloadStore(
            Path.Combine(directory, "history.fsoh"),
            Path.Combine(directory, "history-key.dpapi"),
            new CurrentUserDpapiProtector(
                $"Flowspan.Tests.OperationHistory.{Guid.NewGuid():N}"));
        byte[] payload = "WINDOWS-NATIVE-OPERATION-HISTORY"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsWindows())
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
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeWindowsDataProtector : IWindowsDataProtector
    {
        private static readonly byte[] Prefix =
            "FAKE-DPAPI-SCENE-REPOSITORY-v1"u8.ToArray();

        public int ProtectCount { get; private set; }

        public int UnprotectCount { get; private set; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            ProtectCount++;
            byte[] protectedData = new byte[Prefix.Length + plaintext.Length];
            Prefix.CopyTo(protectedData, 0);
            for (int index = 0; index < plaintext.Length; index++)
            {
                protectedData[Prefix.Length + index] = (byte)(plaintext[index] ^ 0xa5);
            }

            return protectedData;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            UnprotectCount++;
            if (!protectedData.StartsWith(Prefix))
            {
                throw new InvalidDataException("The fake DPAPI payload is invalid.");
            }

            byte[] plaintext = new byte[protectedData.Length - Prefix.Length];
            for (int index = 0; index < plaintext.Length; index++)
            {
                plaintext[index] = (byte)(protectedData[Prefix.Length + index] ^ 0xa5);
            }

            return plaintext;
        }
    }
}
