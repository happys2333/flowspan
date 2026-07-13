using System.Security.Cryptography;
using Flowspan.Domain;

namespace Flowspan.Security;

public enum SecretStoreProtection
{
    OperatingSystemProtected,
    DegradedTestOnly,
}

public interface IDeviceIdentityStore
{
    public SecretStoreProtection Protection { get; }

    public ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);

    public ValueTask<DeviceIdentity?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<bool> TrySaveNewAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default);
}

public static class DeviceIdentityProvisioner
{
    public static ValueTask<DeviceIdentity> LoadOrCreateAsync(
        IDeviceIdentityStore store,
        string displayName,
        CancellationToken cancellationToken = default) => LoadOrCreateAsync(
            store,
            displayName,
            static () => DeviceId.From(Guid.NewGuid()),
            cancellationToken);

    public static async ValueTask<DeviceIdentity> LoadOrCreateAsync(
        IDeviceIdentityStore store,
        string displayName,
        Func<DeviceId> deviceIdFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(deviceIdFactory);
        DeviceIdentity? existing = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        DeviceId deviceId = deviceIdFactory()
            ?? throw new InvalidOperationException(
                "The device ID factory returned no identity.");
        DeviceIdentity created = DeviceIdentity.Generate(deviceId, displayName);
        bool callerOwnsCreated = false;
        try
        {
            if (await store.TrySaveNewAsync(created, cancellationToken)
                .ConfigureAwait(false))
            {
                callerOwnsCreated = true;
                return created;
            }

            return await store.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException(
                    "A competing identity save won but no identity can be loaded.");
        }
        finally
        {
            if (!callerOwnsCreated)
            {
                created.Dispose();
            }
        }
    }
}

public sealed class InMemoryDeviceIdentityStore : IDeviceIdentityStore, IDisposable
{
    private readonly Lock gate = new();
    private DeviceId? deviceId;
    private bool disposed;
    private string? displayName;
    private byte[]? privateKey;

    public SecretStoreProtection Protection => SecretStoreProtection.DegradedTestOnly;

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (privateKey is null)
            {
                return ValueTask.FromResult(false);
            }

            CryptographicOperations.ZeroMemory(privateKey);
            privateKey = null;
            deviceId = null;
            displayName = null;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<DeviceIdentity?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? privateKeyCopy;
        DeviceId? storedDeviceId;
        string? storedDisplayName;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            privateKeyCopy = privateKey is null ? null : (byte[])privateKey.Clone();
            storedDeviceId = deviceId;
            storedDisplayName = displayName;
        }

        if (privateKeyCopy is null)
        {
            return ValueTask.FromResult<DeviceIdentity?>(null);
        }

        try
        {
            return ValueTask.FromResult<DeviceIdentity?>(DeviceIdentity.ImportPkcs8(
                storedDeviceId
                    ?? throw new InvalidDataException("The stored device ID is missing."),
                storedDisplayName
                    ?? throw new InvalidDataException("The stored display name is missing."),
                privateKeyCopy));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyCopy);
        }
    }

    public ValueTask<bool> TrySaveNewAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] exported = identity.ExportPkcs8ForSecretStore();
        bool stored = false;
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (privateKey is not null)
                {
                    return ValueTask.FromResult(false);
                }

                privateKey = exported;
                deviceId = identity.DeviceId;
                displayName = identity.DisplayName;
                stored = true;
                return ValueTask.FromResult(true);
            }
        }
        finally
        {
            if (!stored)
            {
                CryptographicOperations.ZeroMemory(exported);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (privateKey is not null)
            {
                CryptographicOperations.ZeroMemory(privateKey);
                privateKey = null;
            }

            deviceId = null;
            displayName = null;
            disposed = true;
        }
    }
}
