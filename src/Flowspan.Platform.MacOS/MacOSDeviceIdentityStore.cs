using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Platform.MacOS;

public interface IMacOSKeychain
{
    public bool DeleteGenericPassword(string service, string account);

    public byte[]? LoadGenericPassword(string service, string account);

    public bool TryAddGenericPassword(
        string service,
        string account,
        ReadOnlyMemory<byte> value);
}

public sealed class MacOSDeviceIdentityStore : IDeviceIdentityStore
{
    public const string DefaultAccount = "primary-device";
    public const string DefaultService = "app.flowspan.device-identity";
    private readonly PayloadBackedDeviceIdentityStore inner;

    public MacOSDeviceIdentityStore()
        : this(DefaultService, DefaultAccount)
    {
    }

    public MacOSDeviceIdentityStore(string service, string account)
        : this(new SecurityFrameworkKeychain(), service, account)
    {
    }

    public MacOSDeviceIdentityStore(
        IMacOSKeychain keychain,
        string service = DefaultService,
        string account = DefaultAccount)
    {
        inner = new PayloadBackedDeviceIdentityStore(
            new MacOSKeychainIdentityPayloadStore(keychain, service, account));
    }

    public SecretStoreProtection Protection => inner.Protection;

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(cancellationToken);

    public ValueTask<DeviceIdentity?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask<bool> TrySaveNewAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default) =>
        inner.TrySaveNewAsync(identity, cancellationToken);
}

internal sealed class MacOSKeychainIdentityPayloadStore : IDeviceIdentityPayloadStore
{
    private readonly string account;
    private readonly IMacOSKeychain keychain;
    private readonly string service;

    public MacOSKeychainIdentityPayloadStore(
        IMacOSKeychain keychain,
        string service,
        string account)
    {
        ArgumentNullException.ThrowIfNull(keychain);
        this.service = ValidateIdentifier(service, nameof(service));
        this.account = ValidateIdentifier(account, nameof(account));
        this.keychain = keychain;
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            keychain.DeleteGenericPassword(service, account));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? payload = keychain.LoadGenericPassword(service, account);
        if (payload is not null
            && payload.Length is < 1 or > DeviceIdentityPayloadCodec.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                "The macOS Keychain identity payload has an invalid length.");
        }

        return ValueTask.FromResult(payload);
    }

    public ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty || payload.Length > DeviceIdentityPayloadCodec.MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"An identity payload must contain 1 to {DeviceIdentityPayloadCodec.MaximumPayloadBytes} bytes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            keychain.TryAddGenericPassword(service, account, payload));
    }

    private static string ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A Keychain identifier must contain 1 to 200 non-control characters.",
                parameterName);
        }

        return value;
    }
}
