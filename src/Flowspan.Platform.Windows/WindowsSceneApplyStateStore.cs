using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Windows;

public sealed class WindowsSceneApplyStateKeyStore :
    ISceneApplyStateKeyStore
{
    private const int MaximumProtectedKeyBytes = 8 * 1024;
    public const string ProtectionContext =
        "Flowspan.SceneApplyStateKey.DPAPI.v1";
    private readonly DpapiProtectedPayloadFile inner;

    public WindowsSceneApplyStateKeyStore()
        : this(GetDefaultKeyPath())
    {
    }

    public WindowsSceneApplyStateKeyStore(string keyPath)
        : this(keyPath, new CurrentUserDpapiProtector(ProtectionContext))
    {
    }

    public WindowsSceneApplyStateKeyStore(
        string keyPath,
        IWindowsDataProtector dataProtector)
    {
        inner = new DpapiProtectedPayloadFile(
            keyPath,
            dataProtector,
            AuthenticatedSceneApplyStateFile.KeyBytes,
            MaximumProtectedKeyBytes,
            "Scene apply state key");
    }

    public static string GetDefaultKeyPath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "scene-apply-state-key.dpapi"));

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
            AuthenticatedSceneApplyStateFile.KeyBytes);
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
                "The Windows Scene apply state key disappeared after a concurrent create.");
        return RequireValidKey(winner);
    }

    private static byte[] RequireValidKey(byte[] key)
    {
        if (key.Length == AuthenticatedSceneApplyStateFile.KeyBytes)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException(
            "The protected Windows Scene apply state key has an invalid length.");
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

public sealed class WindowsSceneApplyStatePayloadStore :
    ISceneApplyStatePayloadStore
{
    private readonly AuthenticatedSceneApplyStateFile inner;

    public WindowsSceneApplyStatePayloadStore()
        : this(GetDefaultStatePath(), new WindowsSceneApplyStateKeyStore())
    {
    }

    public WindowsSceneApplyStatePayloadStore(
        string statePath,
        string keyPath,
        IWindowsDataProtector dataProtector)
        : this(
            statePath,
            new WindowsSceneApplyStateKeyStore(keyPath, dataProtector))
    {
    }

    public WindowsSceneApplyStatePayloadStore(
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
