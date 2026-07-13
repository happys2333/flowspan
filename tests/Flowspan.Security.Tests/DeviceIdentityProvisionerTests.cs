using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class DeviceIdentityProvisionerTests
{
    [Fact]
    public async Task RestartLoadsSameIdentityFromExplicitlyDegradedTestStore()
    {
        using var store = new InMemoryDeviceIdentityStore();
        DeviceId expectedId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
            store,
            "Laptop",
            () => expectedId);
        string fingerprint = first.PublicIdentity.Fingerprint;

        using DeviceIdentity restarted =
            await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "Ignored new name",
                () => DeviceId.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.Equal(SecretStoreProtection.DegradedTestOnly, store.Protection);
        Assert.Equal(expectedId, restarted.DeviceId);
        Assert.Equal("Laptop", restarted.DisplayName);
        Assert.Equal(fingerprint, restarted.PublicIdentity.Fingerprint);
    }

    [Fact]
    public async Task DeletingStoredIdentityRequiresFreshIdentityOnNextStart()
    {
        using var store = new InMemoryDeviceIdentityStore();
        DeviceId originalId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId replacementId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity original = await DeviceIdentityProvisioner.LoadOrCreateAsync(
            store,
            "Laptop",
            () => originalId);
        string originalFingerprint = original.PublicIdentity.Fingerprint;

        Assert.True(await store.DeleteAsync());
        using DeviceIdentity replacement =
            await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "Laptop",
                () => replacementId);

        Assert.Equal(replacementId, replacement.DeviceId);
        Assert.NotEqual(originalFingerprint, replacement.PublicIdentity.Fingerprint);
    }

    [Fact]
    public async Task ConcurrentFirstLaunchesConvergeOnSingleStoredIdentity()
    {
        using var store = new CoordinatedIdentityStore();
        Task<DeviceIdentity> first = DeviceIdentityProvisioner.LoadOrCreateAsync(
            store,
            "Laptop",
            () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"))
            .AsTask();
        Task<DeviceIdentity> second = DeviceIdentityProvisioner.LoadOrCreateAsync(
            store,
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

    private sealed class CoordinatedIdentityStore : IDeviceIdentityStore, IDisposable
    {
        private readonly TaskCompletionSource bothInitialLoads = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly InMemoryDeviceIdentityStore inner = new();
        private int loadCount;

        public SecretStoreProtection Protection => inner.Protection;

        public ValueTask<bool> DeleteAsync(
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(cancellationToken);

        public async ValueTask<DeviceIdentity?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            int currentLoad = Interlocked.Increment(ref loadCount);
            if (currentLoad <= 2)
            {
                if (currentLoad == 2)
                {
                    bothInitialLoads.SetResult();
                }

                await bothInitialLoads.Task.WaitAsync(cancellationToken);
                return null;
            }

            return await inner.LoadAsync(cancellationToken);
        }

        public ValueTask<bool> TrySaveNewAsync(
            DeviceIdentity identity,
            CancellationToken cancellationToken = default) =>
            inner.TrySaveNewAsync(identity, cancellationToken);

        public void Dispose() => inner.Dispose();
    }
}
