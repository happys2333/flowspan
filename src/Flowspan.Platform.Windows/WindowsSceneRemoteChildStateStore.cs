using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Windows;

public sealed class WindowsSceneRemoteChildStateKeyStore :
    ISceneRemoteChildStateKeyStore
{
    private const int MaximumProtectedKeyBytes = 8 * 1024;
    public const string ProtectionContext =
        "Flowspan.SceneRemoteChildStateKey.DPAPI.v1";
    private readonly DpapiProtectedPayloadFile inner;

    public WindowsSceneRemoteChildStateKeyStore()
        : this(GetDefaultKeyPath())
    {
    }

    public WindowsSceneRemoteChildStateKeyStore(string keyPath)
        : this(keyPath, new CurrentUserDpapiProtector(ProtectionContext))
    {
    }

    public WindowsSceneRemoteChildStateKeyStore(
        string keyPath,
        IWindowsDataProtector dataProtector)
    {
        inner = new DpapiProtectedPayloadFile(
            keyPath,
            dataProtector,
            AuthenticatedSceneRemoteChildStateFile.KeyBytes,
            MaximumProtectedKeyBytes,
            "Scene remote child state key");
    }

    public static string GetDefaultKeyPath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "scene-remote-child-state-key.dpapi"));

    public async ValueTask<byte[]> GetOrCreateKeyAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? existing = await inner.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return RequireValidKey(existing);
        }

        byte[] candidate = RandomNumberGenerator.GetBytes(
            AuthenticatedSceneRemoteChildStateFile.KeyBytes);
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
        byte[] winner = await inner.LoadAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new IOException(
                "The Windows Scene remote child state key disappeared after a concurrent create.");
        return RequireValidKey(winner);
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedSceneRemoteChildStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The protected Windows Scene remote child state key has an invalid length.");
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

public sealed class WindowsSceneRemoteChildStatePayloadStore :
    ISceneRemoteChildStatePayloadStore
{
    private readonly AuthenticatedSceneRemoteChildStateFile inner;

    public WindowsSceneRemoteChildStatePayloadStore()
        : this(GetDefaultStatePath(), new WindowsSceneRemoteChildStateKeyStore())
    {
    }

    public WindowsSceneRemoteChildStatePayloadStore(
        string statePath,
        string keyPath,
        IWindowsDataProtector dataProtector)
        : this(
            statePath,
            new WindowsSceneRemoteChildStateKeyStore(keyPath, dataProtector))
    {
    }

    public WindowsSceneRemoteChildStatePayloadStore(
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
