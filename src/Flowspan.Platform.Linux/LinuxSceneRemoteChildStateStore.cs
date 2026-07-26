using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Linux;

public sealed class LinuxSceneRemoteChildStateKeyStore :
    ISceneRemoteChildStateKeyStore
{
    public const string DefaultAccount = "primary-scene-remote-child-state-key";
    private readonly SecretToolProtectedPayloadStore inner;

    public LinuxSceneRemoteChildStateKeyStore()
        : this(
            new SecretToolProcessRunner(),
            GetDefaultCoordinationLockPath())
    {
    }

    public LinuxSceneRemoteChildStateKeyStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account = DefaultAccount)
    {
        inner = new SecretToolProtectedPayloadStore(
            runner,
            coordinationLockPath,
            account,
            "scene-remote-child-state-key",
            "Flowspan Scene remote child state key",
            AuthenticatedSceneRemoteChildStateFile.KeyBytes);
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
                "scene-remote-child-state-key-secret-tool.lock");
        }

        return Path.GetFullPath(Path.Combine(
            GetSecurityDirectory(),
            "scene-remote-child-state-key-secret-tool.lock"));
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
                "The Linux Scene remote child state key disappeared after a concurrent create.");
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
            "The Linux Secret Service Scene remote child state key has an invalid length.");
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

public sealed class LinuxSceneRemoteChildStatePayloadStore :
    ISceneRemoteChildStatePayloadStore
{
    private readonly AuthenticatedSceneRemoteChildStateFile inner;

    public LinuxSceneRemoteChildStatePayloadStore()
        : this(GetDefaultStatePath(), new LinuxSceneRemoteChildStateKeyStore())
    {
    }

    public LinuxSceneRemoteChildStatePayloadStore(
        string statePath,
        ISecretToolProcessRunner runner,
        string keyCoordinationLockPath)
        : this(
            statePath,
            new LinuxSceneRemoteChildStateKeyStore(
                runner,
                keyCoordinationLockPath))
    {
    }

    public LinuxSceneRemoteChildStatePayloadStore(
        string statePath,
        ISceneRemoteChildStateKeyStore keyStore)
    {
        inner = new AuthenticatedSceneRemoteChildStateFile(statePath, keyStore);
    }

    public static string GetDefaultStatePath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "scene-remote-child-state.fsrc"));

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
