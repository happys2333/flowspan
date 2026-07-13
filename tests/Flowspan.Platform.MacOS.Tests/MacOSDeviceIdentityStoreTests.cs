using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;
using Flowspan.Security;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSDeviceIdentityStoreTests
{
    [Fact]
    public async Task KeychainBoundaryRestoresIdentityAndClearsTemporaryPayloads()
    {
        using var keychain = new FakeMacOSKeychain();
        const string service = "app.flowspan.tests.identity";
        const string account = "round-trip";
        var store = new MacOSDeviceIdentityStore(keychain, service, account);
        using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
            store,
            "MacBook",
            () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"));

        var restartedStore = new MacOSDeviceIdentityStore(keychain, service, account);
        using DeviceIdentity restarted = await restartedStore.LoadAsync()
            ?? throw new InvalidOperationException("Expected a persisted identity.");

        Assert.Equal(SecretStoreProtection.OperatingSystemProtected, store.Protection);
        Assert.Equal(first.DeviceId, restarted.DeviceId);
        Assert.Equal(
            first.PublicIdentity.Fingerprint,
            restarted.PublicIdentity.Fingerprint);
        Assert.All(
            keychain.LastSaveInput.ToArray(),
            static value => Assert.Equal(0, value));
        Assert.All(keychain.LastLoadOutput, static value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ProductionStoreUsesSecurityFrameworkOnMacOSAndRejectsOtherPlatforms()
    {
        const string service = "app.flowspan.tests.identity";
        string account = $"native-{Guid.NewGuid():N}";
        var store = new MacOSDeviceIdentityStore(service, account);
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await DeviceIdentityProvisioner.LoadOrCreateAsync(
                        store,
                        "MacBook"));
                return;
            }

            using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "MacBook",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"));
            var restartedStore = new MacOSDeviceIdentityStore(service, account);
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
            if (OperatingSystem.IsMacOS())
            {
                await store.DeleteAsync();
            }
        }
    }

    [Fact]
    public async Task ConcurrentFirstLaunchesConvergeInMacOSKeychain()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        const string service = "app.flowspan.tests.identity";
        string account = $"concurrent-{Guid.NewGuid():N}";
        var cleanupStore = new MacOSDeviceIdentityStore(service, account);
        try
        {
            Task<DeviceIdentity> first = DeviceIdentityProvisioner.LoadOrCreateAsync(
                new MacOSDeviceIdentityStore(service, account),
                "MacBook",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"))
                .AsTask();
            Task<DeviceIdentity> second = DeviceIdentityProvisioner.LoadOrCreateAsync(
                new MacOSDeviceIdentityStore(service, account),
                "MacBook",
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
            await cleanupStore.DeleteAsync();
        }
    }

    [Fact]
    public async Task MalformedKeychainPayloadIsRejectedWithoutReplacement()
    {
        using var keychain = new FakeMacOSKeychain();
        keychain.Seed([0x01, 0x02, 0x03, 0x04]);
        var store = new MacOSDeviceIdentityStore(
            keychain,
            "app.flowspan.tests.identity",
            "corrupt");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync());

        byte[] retained = keychain.LoadGenericPassword("ignored", "ignored")
            ?? throw new InvalidOperationException("Expected retained corrupt data.");
        try
        {
            Assert.Equal([0x01, 0x02, 0x03, 0x04], retained);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(retained);
        }
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotCallKeychain()
    {
        using var keychain = new FakeMacOSKeychain();
        var store = new MacOSDeviceIdentityStore(
            keychain,
            "app.flowspan.tests.identity",
            "cancelled");
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "MacBook");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.TrySaveNewAsync(identity, cancellation.Token));

        Assert.Equal(0, keychain.SaveCalls);
    }

    private sealed class FakeMacOSKeychain : IMacOSKeychain, IDisposable
    {
        private readonly Lock gate = new();
        private byte[]? secret;

        public byte[] LastLoadOutput { get; private set; } = [];

        public ReadOnlyMemory<byte> LastSaveInput { get; private set; }

        public int SaveCalls { get; private set; }

        public void Seed(ReadOnlySpan<byte> value)
        {
            lock (gate)
            {
                if (secret is not null)
                {
                    CryptographicOperations.ZeroMemory(secret);
                }

                secret = value.ToArray();
            }
        }

        public bool DeleteGenericPassword(string service, string account)
        {
            lock (gate)
            {
                if (secret is null)
                {
                    return false;
                }

                CryptographicOperations.ZeroMemory(secret);
                secret = null;
                return true;
            }
        }

        public byte[]? LoadGenericPassword(string service, string account)
        {
            lock (gate)
            {
                LastLoadOutput = secret is null ? [] : (byte[])secret.Clone();
                return LastLoadOutput.Length == 0 ? null : LastLoadOutput;
            }
        }

        public bool TryAddGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            lock (gate)
            {
                SaveCalls++;
                LastSaveInput = value;
                if (secret is not null)
                {
                    return false;
                }

                secret = value.ToArray();
                return true;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (secret is not null)
                {
                    CryptographicOperations.ZeroMemory(secret);
                    secret = null;
                }
            }
        }
    }
}
