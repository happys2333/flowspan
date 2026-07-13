using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;
using Flowspan.Security;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSTrustPayloadStoreTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task KeychainBoundaryCreatesThenAtomicallyUpdatesPayload()
    {
        using var keychain = new FakeMacOSKeychain();
        var store = new MacOSTrustPayloadStore(
            keychain,
            "app.flowspan.tests.trust",
            "replace");

        await store.SaveAsync(new byte[] { 0x01, 0x02 });
        await store.SaveAsync(new byte[] { 0x03, 0x04, 0x05 });
        byte[] loaded = await store.LoadAsync()
            ?? throw new InvalidOperationException("Expected a trust payload.");

        Assert.Equal([0x03, 0x04, 0x05], loaded);
        Assert.Equal(1, keychain.AddCalls);
        Assert.Equal(2, keychain.UpdateCalls);
        Assert.Equal(0, keychain.DeleteCalls);
        Assert.Equal(
            SecretStoreProtection.OperatingSystemProtected,
            store.Protection);
    }

    [Fact]
    public async Task PersistentTrustRoundTripsThroughKeychainBoundary()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        using var keychain = new FakeMacOSKeychain();
        var payloadStore = new MacOSTrustPayloadStore(
            keychain,
            "app.flowspan.tests.trust",
            "persistent");
        using PersistentTrustStore first =
            await PersistentTrustStore.OpenAsync(payloadStore);
        await first.RegisterAsync(CreateRecord(identity));
        using PersistentTrustStore restarted =
            await PersistentTrustStore.OpenAsync(payloadStore);

        Assert.True(restarted.Allows(PeerId, Capability.MirrorView));
        Assert.Equal(1, keychain.AddCalls);
        Assert.Equal(1, keychain.UpdateCalls);
    }

    [Fact]
    public async Task ConcurrentCreateRaceRetriesAtomicUpdate()
    {
        using var keychain = new FakeMacOSKeychain
        {
            InjectConcurrentCreateOnNextAdd = true,
        };
        var store = new MacOSTrustPayloadStore(
            keychain,
            "app.flowspan.tests.trust",
            "concurrent-create");

        await store.SaveAsync(new byte[] { 0xAA, 0xBB });
        byte[] loaded = await store.LoadAsync()
            ?? throw new InvalidOperationException("Expected a trust payload.");

        Assert.Equal([0xAA, 0xBB], loaded);
        Assert.Equal(1, keychain.AddCalls);
        Assert.Equal(2, keychain.UpdateCalls);
    }

    [Fact]
    public async Task BoundsAndCancellationFailBeforeKeychainMutation()
    {
        using var keychain = new FakeMacOSKeychain();
        var store = new MacOSTrustPayloadStore(
            keychain,
            "app.flowspan.tests.trust",
            "limits");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.SaveAsync(new byte[] { 0x01 }, cancellation.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.SaveAsync(
                new byte[TrustStorePayloadCodec.MaximumPayloadBytes + 1]));

        Assert.Equal(0, keychain.AddCalls);
        Assert.Equal(0, keychain.UpdateCalls);
    }

    [Fact]
    public async Task ProductionStoreUsesSecurityFrameworkOnMacOSOnly()
    {
        const string service = "app.flowspan.tests.trust";
        string account = $"native-{Guid.NewGuid():N}";
        var payloadStore = new MacOSTrustPayloadStore(service, account);
        var keychain = new SecurityFrameworkKeychain();
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await payloadStore.LoadAsync());
                return;
            }

            using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
            using PersistentTrustStore first =
                await PersistentTrustStore.OpenAsync(payloadStore);
            await first.RegisterAsync(CreateRecord(identity));
            using PersistentTrustStore restarted =
                await PersistentTrustStore.OpenAsync(
                    new MacOSTrustPayloadStore(service, account));
            Assert.True(restarted.Allows(PeerId, Capability.MirrorView));

            Assert.True(await restarted.TryUpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorDrive)));
            using PersistentTrustStore afterUpdate =
                await PersistentTrustStore.OpenAsync(
                    new MacOSTrustPayloadStore(service, account));
            Assert.True(afterUpdate.Allows(PeerId, Capability.MirrorDrive));
            Assert.False(afterUpdate.Allows(PeerId, Capability.MirrorView));

            Assert.True(await afterUpdate.RevokeAsync(PeerId));
            using PersistentTrustStore afterRevoke =
                await PersistentTrustStore.OpenAsync(
                    new MacOSTrustPayloadStore(service, account));
            Assert.False(afterRevoke.TryGet(PeerId, out _));
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                keychain.DeleteGenericPassword(service, account);
            }
        }
    }

    private static TrustRecord CreateRecord(DeviceIdentity identity) => new(
        identity.PublicIdentity,
        DateTimeOffset.UnixEpoch,
        CapabilityGrant.Of(Capability.MirrorView));

    private sealed class FakeMacOSKeychain : IMacOSKeychain, IDisposable
    {
        private byte[]? secret;

        public int AddCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public bool InjectConcurrentCreateOnNextAdd { get; init; }

        public int UpdateCalls { get; private set; }

        public bool DeleteGenericPassword(string service, string account)
        {
            DeleteCalls++;
            if (secret is null)
            {
                return false;
            }

            CryptographicOperations.ZeroMemory(secret);
            secret = null;
            return true;
        }

        public byte[]? LoadGenericPassword(string service, string account) =>
            secret is null ? null : (byte[])secret.Clone();

        public bool TryAddGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            AddCalls++;
            if (InjectConcurrentCreateOnNextAdd && AddCalls == 1)
            {
                secret = new byte[] { 0xCC };
                return false;
            }

            if (secret is not null)
            {
                return false;
            }

            secret = value.ToArray();
            return true;
        }

        public bool UpdateGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            UpdateCalls++;
            if (secret is null)
            {
                return false;
            }

            CryptographicOperations.ZeroMemory(secret);
            secret = value.ToArray();
            return true;
        }

        public void Dispose()
        {
            if (secret is not null)
            {
                CryptographicOperations.ZeroMemory(secret);
                secret = null;
            }
        }
    }
}
