using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Linux;

public sealed class LinuxSceneApplyStateKeyStore :
    ISceneApplyStateKeyStore
{
    public const string DefaultAccount = "primary-scene-apply-state-key";
    private readonly SecretToolProtectedPayloadStore inner;

    public LinuxSceneApplyStateKeyStore()
        : this(
            new SecretToolProcessRunner(),
            GetDefaultCoordinationLockPath())
    {
    }

    public LinuxSceneApplyStateKeyStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account = DefaultAccount)
    {
        inner = new SecretToolProtectedPayloadStore(
            runner,
            coordinationLockPath,
            account,
            "scene-apply-state-key",
            "Flowspan Scene apply state key",
            AuthenticatedSceneApplyStateFile.KeyBytes);
    }

    public static string GetDefaultCoordinationLockPath()
    {
        string? runtimeDirectory =
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDirectory)
            && Path.IsPathFullyQualified(runtimeDirectory))
        {
            return Path.Combine(
                runtimeDirectory,
                "flowspan",
                "scene-apply-state-key-secret-tool.lock");
        }

        return Path.GetFullPath(Path.Combine(
            GetSecurityDirectory(),
            "scene-apply-state-key-secret-tool.lock"));
    }

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
                "The Linux Scene apply state key disappeared after a concurrent create.");
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
            "The Linux Secret Service Scene apply state key has an invalid length.");
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

public sealed class LinuxSceneApplyStatePayloadStore :
    ISceneApplyStatePayloadStore
{
    private readonly AuthenticatedSceneApplyStateFile inner;

    public LinuxSceneApplyStatePayloadStore()
        : this(GetDefaultStatePath(), new LinuxSceneApplyStateKeyStore())
    {
    }

    public LinuxSceneApplyStatePayloadStore(
        string statePath,
        ISecretToolProcessRunner runner,
        string keyCoordinationLockPath)
        : this(
            statePath,
            new LinuxSceneApplyStateKeyStore(
                runner,
                keyCoordinationLockPath))
    {
    }

    public LinuxSceneApplyStatePayloadStore(
        string statePath,
        ISceneApplyStateKeyStore keyStore)
    {
        inner = new AuthenticatedSceneApplyStateFile(statePath, keyStore);
    }

    public static string GetDefaultStatePath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "scene-apply-state.fsaf"));

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);

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
