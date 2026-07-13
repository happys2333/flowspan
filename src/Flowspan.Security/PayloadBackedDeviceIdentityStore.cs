using System.Security.Cryptography;

namespace Flowspan.Security;

public interface IDeviceIdentityPayloadStore
{
    public SecretStoreProtection Protection { get; }

    public ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);

    public ValueTask<byte[]?> LoadAsync(CancellationToken cancellationToken = default);

    public ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class PayloadBackedDeviceIdentityStore : IDeviceIdentityStore
{
    private readonly IDeviceIdentityPayloadStore payloadStore;

    public PayloadBackedDeviceIdentityStore(IDeviceIdentityPayloadStore payloadStore)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        this.payloadStore = payloadStore;
    }

    public SecretStoreProtection Protection => payloadStore.Protection;

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default) =>
        payloadStore.DeleteAsync(cancellationToken);

    public async ValueTask<DeviceIdentity?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        try
        {
            return DeviceIdentityPayloadCodec.Decode(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async ValueTask<bool> TrySaveNewAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload = DeviceIdentityPayloadCodec.Encode(identity);
        try
        {
            return await payloadStore.TrySaveNewAsync(payload, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
