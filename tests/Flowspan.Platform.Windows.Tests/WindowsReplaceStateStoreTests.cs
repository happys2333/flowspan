using System.Text;
using Flowspan.Platform.Windows;

namespace Flowspan.Platform.Windows.Tests;

public sealed class WindowsReplaceStateStoreTests
{
    [Fact]
    public async Task SwapEndpointDpapiKeyAndFileUseIndependentPurpose()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-swap-endpoint-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "swap-endpoint-state.fsef");
        string keyPath = Path.Combine(directory, "swap-endpoint-state-key.dpapi");
        byte[] payload = "WINDOWS-SWAP-ENDPOINT-PLAINTEXT-CANARY"u8.ToArray();
        var protector = new FakeWindowsDataProtector();
        try
        {
            var first = new WindowsSwapEndpointStatePayloadStore(
                statePath,
                keyPath,
                protector);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "WINDOWS-SWAP-ENDPOINT-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(
                payload,
                await new WindowsSwapEndpointStatePayloadStore(
                    statePath,
                    keyPath,
                    protector).LoadAsync());
            Assert.NotEqual(
                WindowsSwapEndpointStatePayloadStore.GetDefaultStatePath(),
                WindowsSwapStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                WindowsSwapEndpointStateKeyStore.GetDefaultKeyPath(),
                WindowsSwapStateKeyStore.GetDefaultKeyPath());
            Assert.NotEqual(
                WindowsSwapEndpointStateKeyStore.ProtectionContext,
                WindowsSwapStateKeyStore.ProtectionContext);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionSwapEndpointKeyUsesCurrentUserDpapiOnWindowsOnly()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-native-swap-endpoint-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "swap-endpoint-state.fsef");
        string keyPath = Path.Combine(directory, "swap-endpoint-state-key.dpapi");
        var store = new WindowsSwapEndpointStatePayloadStore(
            statePath,
            keyPath,
            new CurrentUserDpapiProtector(
                $"Flowspan.Tests.SwapEndpointState.{Guid.NewGuid():N}"));
        byte[] payload = "WINDOWS-NATIVE-SWAP-ENDPOINT-STATE"u8.ToArray();
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
    public async Task SwapDpapiKeyAndAuthenticatedFileUseIndependentPurpose()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-swap-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "swap-state.fssf");
        string keyPath = Path.Combine(directory, "swap-state-key.dpapi");
        byte[] payload = "WINDOWS-SWAP-PLAINTEXT-CANARY"u8.ToArray();
        var protector = new FakeWindowsDataProtector();
        try
        {
            var first = new WindowsSwapStatePayloadStore(
                statePath,
                keyPath,
                protector);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "WINDOWS-SWAP-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            byte[]? restored = await new WindowsSwapStatePayloadStore(
                statePath,
                keyPath,
                protector).LoadAsync();

            Assert.Equal(payload, restored);
            Assert.NotEqual(
                WindowsSwapStatePayloadStore.GetDefaultStatePath(),
                WindowsReplaceStatePayloadStore.GetDefaultStatePath());
            Assert.NotEqual(
                WindowsSwapStateKeyStore.GetDefaultKeyPath(),
                WindowsReplaceStateKeyStore.GetDefaultKeyPath());
            Assert.NotEqual(
                WindowsSwapStateKeyStore.ProtectionContext,
                WindowsReplaceStateKeyStore.ProtectionContext);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DpapiKeyAndAuthenticatedFileRoundTripWithoutPlaintext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-replace-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "replace-state.fsrf");
        string keyPath = Path.Combine(directory, "replace-state-key.dpapi");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"descriptor\":\"WINDOWS-REPLACE-PLAINTEXT-CANARY\"}");
        var protector = new FakeWindowsDataProtector();
        try
        {
            var first = new WindowsReplaceStatePayloadStore(
                statePath,
                keyPath,
                protector);
            await first.SaveAsync(payload);

            Assert.NotEqual(
                payload,
                await File.ReadAllBytesAsync(statePath));
            Assert.DoesNotContain(
                "WINDOWS-REPLACE-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(1, protector.ProtectCount);

            var restarted = new WindowsReplaceStatePayloadStore(
                statePath,
                keyPath,
                protector);
            byte[]? restored = await restarted.LoadAsync();

            Assert.Equal(payload, restored);
            Assert.True(protector.UnprotectCount >= 1);
            Assert.NotEqual(
                WindowsReplaceStatePayloadStore.GetDefaultStatePath(),
                WindowsDeviceIdentityStore.GetDefaultStoragePath());
            Assert.NotEqual(
                WindowsReplaceStateKeyStore.GetDefaultKeyPath(),
                WindowsTrustPayloadStore.GetDefaultStoragePath());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionKeyStoreUsesCurrentUserDpapiOnWindowsOnly()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-native-replace-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "replace-state.fsrf");
        string keyPath = Path.Combine(directory, "replace-state-key.dpapi");
        var store = new WindowsReplaceStatePayloadStore(
            statePath,
            keyPath,
            new CurrentUserDpapiProtector(
                $"Flowspan.Tests.ReplaceState.{Guid.NewGuid():N}"));
        byte[] payload = "WINDOWS-NATIVE-REPLACE-STATE"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await store.SaveAsync(payload));
                return;
            }

            await store.SaveAsync(payload);
            byte[]? restored = await store.LoadAsync();
            Assert.Equal(payload, restored);
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
    public async Task ProductionSwapKeyStoreUsesCurrentUserDpapiOnWindowsOnly()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-native-swap-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "swap-state.fssf");
        string keyPath = Path.Combine(directory, "swap-state-key.dpapi");
        var store = new WindowsSwapStatePayloadStore(
            statePath,
            keyPath,
            new CurrentUserDpapiProtector(
                $"Flowspan.Tests.SwapState.{Guid.NewGuid():N}"));
        byte[] payload = "WINDOWS-NATIVE-SWAP-STATE"u8.ToArray();
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
        private static readonly byte[] Prefix = "FAKE-DPAPI-REPLACE-v1"u8.ToArray();

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
