using Flowspan.Domain;
using Flowspan.Platform.Windows;
using Flowspan.Security;

namespace Flowspan.Platform.Windows.Tests;

public sealed class WindowsDeviceIdentityStoreTests
{
    [Fact]
    public void DefaultPathIsStableUnderCurrentUsersLocalApplicationData()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        string first = WindowsDeviceIdentityStore.GetDefaultStoragePath();
        string second = WindowsDeviceIdentityStore.GetDefaultStoragePath();

        Assert.Equal(first, second);
        Assert.True(Path.IsPathFullyQualified(first));
        Assert.StartsWith(
            Path.GetFullPath(localApplicationData),
            first,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Flowspan", "Security", "device-identity.dpapi"),
            first,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedAtomicFileRestoresIdentityWithoutPlaintextAtRest()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-store-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-identity.dpapi");
        try
        {
            var protector = new FakeWindowsDataProtector();
            var store = new WindowsDeviceIdentityStore(path, protector);
            using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "Laptop",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"));

            byte[] atRest = await File.ReadAllBytesAsync(path);
            var restartedStore = new WindowsDeviceIdentityStore(path, protector);
            using DeviceIdentity restarted = await restartedStore.LoadAsync()
                ?? throw new InvalidOperationException("Expected a persisted identity.");

            Assert.Equal(SecretStoreProtection.OperatingSystemProtected, store.Protection);
            Assert.False(atRest.AsSpan().StartsWith("FSID"u8));
            Assert.Equal(first.DeviceId, restarted.DeviceId);
            Assert.Equal(
                first.PublicIdentity.Fingerprint,
                restarted.PublicIdentity.Fingerprint);
            Assert.All(protector.LastProtectedOutput, static value => Assert.Equal(0, value));
            Assert.All(
                protector.LastUnprotectedOutput,
                static value => Assert.Equal(0, value));
            Assert.True(await restartedStore.DeleteAsync());
            Assert.False(File.Exists(path));
            Assert.False(await restartedStore.DeleteAsync());
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
    public async Task ConcurrentFirstLaunchesConvergeAcrossStoreInstances()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-store-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-identity.dpapi");
        try
        {
            var protector = new FakeWindowsDataProtector();
            Task<DeviceIdentity> first = DeviceIdentityProvisioner.LoadOrCreateAsync(
                new WindowsDeviceIdentityStore(path, protector),
                "Laptop",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"))
                .AsTask();
            Task<DeviceIdentity> second = DeviceIdentityProvisioner.LoadOrCreateAsync(
                new WindowsDeviceIdentityStore(path, protector),
                "Laptop",
                () => DeviceId.Parse("22222222-2222-2222-2222-222222222222"))
                .AsTask();
            DeviceIdentity[] identities = await Task.WhenAll(first, second);
            try
            {
                Assert.Equal(identities[0].DeviceId, identities[1].DeviceId);
                Assert.Equal(
                    identities[0].PublicIdentity.Fingerprint,
                    identities[1].PublicIdentity.Fingerprint);
            }
            finally
            {
                foreach (DeviceIdentity identity in identities)
                {
                    identity.Dispose();
                }
            }
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
    public async Task CorruptProtectedPayloadIsRejectedWithoutReplacement()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-store-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-identity.dpapi");
        try
        {
            Directory.CreateDirectory(directory);
            byte[] corrupt = "not-a-dpapi-payload"u8.ToArray();
            await File.WriteAllBytesAsync(path, corrupt);
            var store = new WindowsDeviceIdentityStore(
                path,
                new FakeWindowsDataProtector());

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync());

            Assert.Equal(corrupt, await File.ReadAllBytesAsync(path));
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
    public async Task PreCancelledSaveCreatesNoIdentityArtifact()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-store-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-identity.dpapi");
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new WindowsDeviceIdentityStore(
            path,
            new FakeWindowsDataProtector());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.TrySaveNewAsync(identity, cancellation.Token));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ContendedCreationLockHonorsCancellationAndCleansTemporaryData()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-store-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-identity.dpapi");
        Directory.CreateDirectory(directory);
        try
        {
            await using var heldLock = new FileStream(
                $"{path}.lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            using DeviceIdentity identity = DeviceIdentity.Generate(
                DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
                "Laptop");
            var protector = new FakeWindowsDataProtector();
            var store = new WindowsDeviceIdentityStore(path, protector);
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.TrySaveNewAsync(identity, cancellation.Token));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            Assert.All(
                protector.LastProtectedOutput,
                static value => Assert.Equal(0, value));
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
    public async Task ProductionStoreUsesDpapiOnWindowsAndRejectsOtherPlatforms()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-windows-dpapi-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-identity.dpapi");
        try
        {
            var store = new WindowsDeviceIdentityStore(path);
            if (!OperatingSystem.IsWindows())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await DeviceIdentityProvisioner.LoadOrCreateAsync(
                        store,
                        "Laptop"));
                return;
            }

            using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "Laptop",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"));
            var restartedStore = new WindowsDeviceIdentityStore(path);
            using DeviceIdentity restarted = await restartedStore.LoadAsync()
                ?? throw new InvalidOperationException("Expected a persisted identity.");

            Assert.Equal(first.DeviceId, restarted.DeviceId);
            Assert.Equal(
                first.PublicIdentity.Fingerprint,
                restarted.PublicIdentity.Fingerprint);
            Assert.True(await restartedStore.DeleteAsync());
            Assert.Null(await restartedStore.LoadAsync());
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
        private static ReadOnlySpan<byte> Prefix => "fake-dpapi:"u8;

        public byte[] LastProtectedOutput { get; private set; } = [];

        public byte[] LastUnprotectedOutput { get; private set; } = [];

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            byte[] protectedData = new byte[Prefix.Length + plaintext.Length];
            Prefix.CopyTo(protectedData);
            for (int index = 0; index < plaintext.Length; index++)
            {
                protectedData[Prefix.Length + index] = (byte)(plaintext[index] ^ 0xa5);
            }

            LastProtectedOutput = protectedData;
            return protectedData;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            if (!protectedData.StartsWith(Prefix))
            {
                throw new InvalidDataException("The fake protected value is invalid.");
            }

            byte[] plaintext = new byte[protectedData.Length - Prefix.Length];
            for (int index = 0; index < plaintext.Length; index++)
            {
                plaintext[index] = (byte)(protectedData[Prefix.Length + index] ^ 0xa5);
            }

            LastUnprotectedOutput = plaintext;
            return plaintext;
        }
    }
}
