using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Platform.Windows;
using Flowspan.Security;

namespace Flowspan.Platform.Windows.Tests;

public sealed class WindowsTrustPayloadStoreTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void DefaultPathIsStableAndSeparateFromIdentity()
    {
        string first = WindowsTrustPayloadStore.GetDefaultStoragePath();
        string second = WindowsTrustPayloadStore.GetDefaultStoragePath();

        Assert.Equal(first, second);
        Assert.True(Path.IsPathFullyQualified(first));
        Assert.NotEqual(
            WindowsDeviceIdentityStore.GetDefaultStoragePath(),
            first);
        Assert.EndsWith(
            Path.Combine("Flowspan", "Security", "trust.dpapi"),
            first,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedAtomicReplaceNeverWritesPlaintextOrPartialSnapshot()
    {
        string directory = CreateTemporaryDirectoryName();
        string path = Path.Combine(directory, "trust.dpapi");
        try
        {
            var protector = new FakeWindowsDataProtector();
            var store = new WindowsTrustPayloadStore(path, protector);
            byte[] first = "FSTR-first-snapshot"u8.ToArray();
            byte[] second = "FSTR-second-snapshot"u8.ToArray();

            await store.SaveAsync(first);
            byte[] firstAtRest = await File.ReadAllBytesAsync(path);
            await store.SaveAsync(second);
            byte[] secondAtRest = await File.ReadAllBytesAsync(path);
            var restarted = new WindowsTrustPayloadStore(path, protector);
            byte[] loaded = await restarted.LoadAsync()
                ?? throw new InvalidOperationException("Expected a trust payload.");

            Assert.False(firstAtRest.AsSpan().StartsWith("FSTR"u8));
            Assert.False(secondAtRest.AsSpan().StartsWith("FSTR"u8));
            Assert.Equal(second, loaded);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            Assert.Equal(
                SecretStoreProtection.OperatingSystemProtected,
                store.Protection);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CancelledContendedReplacePreservesCommittedSnapshot()
    {
        string directory = CreateTemporaryDirectoryName();
        string path = Path.Combine(directory, "trust.dpapi");
        try
        {
            var protector = new FakeWindowsDataProtector();
            var store = new WindowsTrustPayloadStore(path, protector);
            byte[] committed = "committed"u8.ToArray();
            await store.SaveAsync(committed);
            await using var heldLock = new FileStream(
                $"{path}.lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.SaveAsync("replacement"u8.ToArray(), cancellation.Token));
            byte[] loaded = await store.LoadAsync()
                ?? throw new InvalidOperationException("Expected the old snapshot.");

            Assert.Equal(committed, loaded);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ProductionStoreUsesDpapiOnWindowsOnly()
    {
        string directory = CreateTemporaryDirectoryName();
        string path = Path.Combine(directory, "trust.dpapi");
        try
        {
            var payloadStore = new WindowsTrustPayloadStore(path);
            if (!OperatingSystem.IsWindows())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await payloadStore.SaveAsync(new byte[] { 0x01 }));
                return;
            }

            using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
            using PersistentTrustStore first =
                await PersistentTrustStore.OpenAsync(payloadStore);
            await first.RegisterAsync(CreateRecord(identity));
            using PersistentTrustStore restarted =
                await PersistentTrustStore.OpenAsync(
                    new WindowsTrustPayloadStore(path));
            Assert.True(restarted.Allows(PeerId, Capability.MirrorView));

            Assert.True(await restarted.TryUpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorDrive)));
            using PersistentTrustStore afterUpdate =
                await PersistentTrustStore.OpenAsync(
                    new WindowsTrustPayloadStore(path));
            Assert.True(afterUpdate.Allows(PeerId, Capability.MirrorDrive));
            Assert.False(afterUpdate.Allows(PeerId, Capability.MirrorView));

            Assert.True(await afterUpdate.RevokeAsync(PeerId));
            using PersistentTrustStore afterRevoke =
                await PersistentTrustStore.OpenAsync(
                    new WindowsTrustPayloadStore(path));
            Assert.False(afterRevoke.TryGet(PeerId, out _));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ProductionDpapiContextsAreDomainSeparated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var identityProtector = new CurrentUserDpapiProtector(
            CurrentUserDpapiProtector.DeviceIdentityContext);
        var trustProtector = new CurrentUserDpapiProtector(
            CurrentUserDpapiProtector.TrustRepositoryContext);
        byte[] protectedIdentity = identityProtector.Protect("identity"u8);

        Assert.Throws<CryptographicException>(() =>
            trustProtector.Unprotect(protectedIdentity));
    }

    private static TrustRecord CreateRecord(DeviceIdentity identity) => new(
        identity.PublicIdentity,
        DateTimeOffset.UnixEpoch,
        CapabilityGrant.Of(Capability.MirrorView));

    private static string CreateTemporaryDirectoryName() => Path.Combine(
        Path.GetTempPath(),
        $"flowspan-windows-trust-{Guid.NewGuid():N}");

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeWindowsDataProtector : IWindowsDataProtector
    {
        private static ReadOnlySpan<byte> Prefix => "fake-dpapi:"u8;

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            byte[] protectedData = new byte[Prefix.Length + plaintext.Length];
            Prefix.CopyTo(protectedData);
            for (int index = 0; index < plaintext.Length; index++)
            {
                protectedData[Prefix.Length + index] =
                    (byte)(plaintext[index] ^ 0xa5);
            }

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
                plaintext[index] =
                    (byte)(protectedData[Prefix.Length + index] ^ 0xa5);
            }

            return plaintext;
        }
    }
}
