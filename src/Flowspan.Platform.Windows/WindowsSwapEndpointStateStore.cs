using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Windows;

public sealed class WindowsSwapEndpointStateKeyStore :
    ISwapEndpointStateKeyStore
{
    private const int MaximumProtectedKeyBytes = 8 * 1024;
    public const string ProtectionContext =
        "Flowspan.SwapEndpointStateKey.DPAPI.v1";
    private readonly DpapiProtectedPayloadFile inner;

    public WindowsSwapEndpointStateKeyStore()
        : this(GetDefaultKeyPath())
    {
    }

    public WindowsSwapEndpointStateKeyStore(string keyPath)
        : this(keyPath, new CurrentUserDpapiProtector(ProtectionContext))
    {
    }

    public WindowsSwapEndpointStateKeyStore(
        string keyPath,
        IWindowsDataProtector dataProtector)
    {
        inner = new DpapiProtectedPayloadFile(
            keyPath,
            dataProtector,
            AuthenticatedSwapEndpointStateFile.KeyBytes,
            MaximumProtectedKeyBytes,
            "Swap endpoint state key");
    }

    public static string GetDefaultKeyPath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "swap-endpoint-state-key.dpapi"));

    public async ValueTask<byte[]> GetOrCreateKeyAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? existing = await inner.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return RequireValidKey(existing);
        }

        byte[] candidate = RandomNumberGenerator.GetBytes(
            AuthenticatedSwapEndpointStateFile.KeyBytes);
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
                "The Windows Swap endpoint state key disappeared after a concurrent create.");
        return RequireValidKey(winner);
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedSwapEndpointStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The protected Windows Swap endpoint state key has an invalid length.");
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

public sealed class WindowsSwapEndpointStatePayloadStore :
    ISwapEndpointStatePayloadStore
{
    private readonly AuthenticatedSwapEndpointStateFile inner;

    public WindowsSwapEndpointStatePayloadStore()
        : this(GetDefaultStatePath(), new WindowsSwapEndpointStateKeyStore())
    {
    }

    public WindowsSwapEndpointStatePayloadStore(
        string statePath,
        string keyPath,
        IWindowsDataProtector dataProtector)
        : this(
            statePath,
            new WindowsSwapEndpointStateKeyStore(keyPath, dataProtector))
    {
    }

    public WindowsSwapEndpointStatePayloadStore(
        string statePath,
        ISwapEndpointStateKeyStore keyStore)
    {
        inner = new AuthenticatedSwapEndpointStateFile(statePath, keyStore);
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
            "swap-endpoint-state.fsef"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
