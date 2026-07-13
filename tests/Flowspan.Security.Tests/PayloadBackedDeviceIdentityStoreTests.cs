using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class PayloadBackedDeviceIdentityStoreTests
{
    [Fact]
    public async Task PlatformPayloadBoundaryPreservesIdentityAcrossRestart()
    {
        using var payloadStore = new RecordingPayloadStore();
        var firstStore = new PayloadBackedDeviceIdentityStore(payloadStore);
        DeviceId expectedId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
            firstStore,
            "Laptop",
            () => expectedId);

        var restartedStore = new PayloadBackedDeviceIdentityStore(payloadStore);
        using DeviceIdentity restarted = await restartedStore.LoadAsync()
            ?? throw new InvalidOperationException("Expected a persisted identity.");

        Assert.Equal(SecretStoreProtection.OperatingSystemProtected, restartedStore.Protection);
        Assert.Equal(expectedId, restarted.DeviceId);
        Assert.Equal(first.PublicIdentity.Fingerprint, restarted.PublicIdentity.Fingerprint);
        Assert.InRange(
            payloadStore.StoredPayloadLength,
            1,
            DeviceIdentityPayloadCodec.MaximumPayloadBytes);
    }

    private sealed class RecordingPayloadStore : IDeviceIdentityPayloadStore, IDisposable
    {
        private byte[]? payload;

        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public int StoredPayloadLength => payload?.Length ?? 0;

        public ValueTask<bool> DeleteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (payload is null)
            {
                return ValueTask.FromResult(false);
            }

            CryptographicOperations.ZeroMemory(payload);
            payload = null;
            return ValueTask.FromResult(true);
        }

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload is null
                ? null
                : (byte[])payload.Clone());
        }

        public ValueTask<bool> TrySaveNewAsync(
            ReadOnlyMemory<byte> newPayload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (payload is not null)
            {
                return ValueTask.FromResult(false);
            }

            payload = newPayload.ToArray();
            return ValueTask.FromResult(true);
        }

        public void Dispose()
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
                payload = null;
            }
        }
    }
}
