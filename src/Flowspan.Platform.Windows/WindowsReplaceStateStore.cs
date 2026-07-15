using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Windows;

public sealed class WindowsReplaceStateKeyStore : IReplaceStateKeyStore
{
    private const int MaximumProtectedKeyBytes = 8 * 1024;
    public const string ProtectionContext = "Flowspan.ReplaceStateKey.DPAPI.v1";
    private readonly DpapiProtectedPayloadFile inner;

    public WindowsReplaceStateKeyStore()
        : this(GetDefaultKeyPath())
    {
    }

    public WindowsReplaceStateKeyStore(string keyPath)
        : this(
            keyPath,
            new CurrentUserDpapiProtector(ProtectionContext))
    {
    }

    public WindowsReplaceStateKeyStore(
        string keyPath,
        IWindowsDataProtector dataProtector)
    {
        inner = new DpapiProtectedPayloadFile(
            keyPath,
            dataProtector,
            AuthenticatedReplaceStateFile.KeyBytes,
            MaximumProtectedKeyBytes,
            "Replace state key");
    }

    public static string GetDefaultKeyPath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "replace-state-key.dpapi"));

    public async ValueTask<byte[]> GetOrCreateKeyAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? existing = await inner.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return RequireValidKey(existing);
        }

        byte[] candidate = RandomNumberGenerator.GetBytes(
            AuthenticatedReplaceStateFile.KeyBytes);
        bool saved;
        try
        {
            saved = await inner.TrySaveNewAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            if (saved)
            {
                return candidate;
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(candidate);
            throw;
        }

        CryptographicOperations.ZeroMemory(candidate);
        byte[] winner = await inner.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException(
                "The Windows Replace state key disappeared after a concurrent create.");
        return RequireValidKey(winner);
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedReplaceStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The protected Windows Replace state key has an invalid length.");
    }

    private static string GetSecurityDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user has no LocalApplicationData directory.");
        }

        return Path.Combine(localApplicationData, "Flowspan", "Security");
    }
}

public sealed class WindowsReplaceStatePayloadStore : IReplaceStatePayloadStore
{
    private readonly AuthenticatedReplaceStateFile inner;

    public WindowsReplaceStatePayloadStore()
        : this(GetDefaultStatePath(), new WindowsReplaceStateKeyStore())
    {
    }

    public WindowsReplaceStatePayloadStore(
        string statePath,
        string keyPath,
        IWindowsDataProtector dataProtector)
        : this(
            statePath,
            new WindowsReplaceStateKeyStore(keyPath, dataProtector))
    {
    }

    public WindowsReplaceStatePayloadStore(
        string statePath,
        IReplaceStateKeyStore keyStore)
    {
        inner = new AuthenticatedReplaceStateFile(statePath, keyStore);
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
            "replace-state.fsrf"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
