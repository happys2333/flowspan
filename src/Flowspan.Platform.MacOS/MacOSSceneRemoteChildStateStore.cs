using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSSceneRemoteChildStateKeyStore :
    ISceneRemoteChildStateKeyStore
{
    public const string DefaultAccount = "primary-scene-remote-child-state-key";
    public const string DefaultService =
        "app.flowspan.scene-remote-child-state-key";
    private readonly string account;
    private readonly IMacOSKeychain keychain;
    private readonly string service;

    public MacOSSceneRemoteChildStateKeyStore()
        : this(new SecurityFrameworkKeychain())
    {
    }

    public MacOSSceneRemoteChildStateKeyStore(
        IMacOSKeychain keychain,
        string service = DefaultService,
        string account = DefaultAccount)
    {
        ArgumentNullException.ThrowIfNull(keychain);
        this.keychain = keychain;
        this.service = ValidateIdentifier(service, nameof(service));
        this.account = ValidateIdentifier(account, nameof(account));
    }

    public ValueTask<byte[]> GetOrCreateKeyAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? existing = keychain.LoadGenericPassword(service, account);
        if (existing is not null)
        {
            return ValueTask.FromResult(RequireValidKey(existing));
        }

        byte[] candidate = RandomNumberGenerator.GetBytes(
            AuthenticatedSceneRemoteChildStateFile.KeyBytes);
        if (keychain.TryAddGenericPassword(service, account, candidate))
        {
            return ValueTask.FromResult(candidate);
        }

        CryptographicOperations.ZeroMemory(candidate);
        byte[] winner = keychain.LoadGenericPassword(service, account)
            ?? throw new IOException(
                "The macOS Scene remote child state key disappeared after a concurrent create.");
        return ValueTask.FromResult(RequireValidKey(winner));
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedSceneRemoteChildStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The macOS Keychain Scene remote child state key has an invalid length.");
    }

    private static string ValidateIdentifier(
        string value,
        string parameterName)
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

public sealed class MacOSSceneRemoteChildStatePayloadStore :
    ISceneRemoteChildStatePayloadStore
{
    private readonly AuthenticatedSceneRemoteChildStateFile inner;

    public MacOSSceneRemoteChildStatePayloadStore()
        : this(GetDefaultStatePath(), new MacOSSceneRemoteChildStateKeyStore())
    {
    }

    public MacOSSceneRemoteChildStatePayloadStore(
        string statePath,
        IMacOSKeychain keychain)
        : this(statePath, new MacOSSceneRemoteChildStateKeyStore(keychain))
    {
    }

    public MacOSSceneRemoteChildStatePayloadStore(
        string statePath,
        ISceneRemoteChildStateKeyStore keyStore)
    {
        inner = new AuthenticatedSceneRemoteChildStateFile(statePath, keyStore);
    }

    public static string GetDefaultStatePath()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user has no LocalApplicationData directory.");
        }

        return Path.GetFullPath(Path.Combine(
            localApplicationData,
            "Flowspan",
            "Security",
            "scene-remote-child-state.fsrc"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
