using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSOperationHistoryStateKeyStore :
    IOperationHistoryStateKeyStore
{
    public const string DefaultAccount = "primary-operation-history-state-key";
    public const string DefaultService =
        "app.flowspan.operation-history-state-key";
    private readonly string account;
    private readonly IMacOSKeychain keychain;
    private readonly string service;

    public MacOSOperationHistoryStateKeyStore()
        : this(new SecurityFrameworkKeychain())
    {
    }

    public MacOSOperationHistoryStateKeyStore(
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
            AuthenticatedOperationHistoryStateFile.KeyBytes);
        if (keychain.TryAddGenericPassword(service, account, candidate))
        {
            return ValueTask.FromResult(candidate);
        }

        CryptographicOperations.ZeroMemory(candidate);
        byte[] winner = keychain.LoadGenericPassword(service, account)
            ?? throw new IOException(
                "The macOS operation history key disappeared after a concurrent create.");
        return ValueTask.FromResult(RequireValidKey(winner));
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedOperationHistoryStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The macOS operation history key has an invalid length.");
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

public sealed class MacOSOperationHistoryStatePayloadStore :
    IOperationHistoryStatePayloadStore
{
    private readonly AuthenticatedOperationHistoryStateFile inner;

    public MacOSOperationHistoryStatePayloadStore()
        : this(GetDefaultStatePath(), new MacOSOperationHistoryStateKeyStore())
    {
    }

    public MacOSOperationHistoryStatePayloadStore(
        string statePath,
        IMacOSKeychain keychain)
        : this(statePath, new MacOSOperationHistoryStateKeyStore(keychain))
    {
    }

    public MacOSOperationHistoryStatePayloadStore(
        string statePath,
        IOperationHistoryStateKeyStore keyStore)
    {
        inner = new AuthenticatedOperationHistoryStateFile(statePath, keyStore);
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
            "operation-history-state.fsoh"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
