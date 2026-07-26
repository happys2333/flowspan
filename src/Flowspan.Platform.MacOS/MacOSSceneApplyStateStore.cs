using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSSceneApplyStateKeyStore :
    ISceneApplyStateKeyStore
{
    public const string DefaultAccount = "primary-scene-apply-state-key";
    public const string DefaultService =
        "app.flowspan.scene-apply-state-key";
    private readonly string account;
    private readonly IMacOSKeychain keychain;
    private readonly string service;

    public MacOSSceneApplyStateKeyStore()
        : this(new SecurityFrameworkKeychain())
    {
    }

    public MacOSSceneApplyStateKeyStore(
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
            AuthenticatedSceneApplyStateFile.KeyBytes);
        if (keychain.TryAddGenericPassword(service, account, candidate))
        {
            return ValueTask.FromResult(candidate);
        }

        CryptographicOperations.ZeroMemory(candidate);
        byte[] winner = keychain.LoadGenericPassword(service, account)
            ?? throw new IOException(
                "The macOS Scene apply state key disappeared after a concurrent create.");
        return ValueTask.FromResult(RequireValidKey(winner));
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedSceneApplyStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The macOS Keychain Scene apply state key has an invalid length.");
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

public sealed class MacOSSceneApplyStatePayloadStore :
    ISceneApplyStatePayloadStore
{
    private readonly AuthenticatedSceneApplyStateFile inner;

    public MacOSSceneApplyStatePayloadStore()
        : this(GetDefaultStatePath(), new MacOSSceneApplyStateKeyStore())
    {
    }

    public MacOSSceneApplyStatePayloadStore(
        string statePath,
        IMacOSKeychain keychain)
        : this(statePath, new MacOSSceneApplyStateKeyStore(keychain))
    {
    }

    public MacOSSceneApplyStatePayloadStore(
        string statePath,
        ISceneApplyStateKeyStore keyStore)
    {
        inner = new AuthenticatedSceneApplyStateFile(statePath, keyStore);
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
            "scene-apply-state.fsaf"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
