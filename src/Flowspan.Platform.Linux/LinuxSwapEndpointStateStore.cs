using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Platform;

namespace Flowspan.Platform.Linux;

public sealed class LinuxSwapEndpointStateKeyStore :
    ISwapEndpointStateKeyStore
{
    public const string DefaultAccount = "primary-swap-endpoint-state-key";
    private readonly SecretToolProtectedPayloadStore inner;

    public LinuxSwapEndpointStateKeyStore()
        : this(
            new SecretToolProcessRunner(),
            GetDefaultCoordinationLockPath())
    {
    }

    public LinuxSwapEndpointStateKeyStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account = DefaultAccount)
    {
        inner = new SecretToolProtectedPayloadStore(
            runner,
            coordinationLockPath,
            account,
            "swap-endpoint-state-key",
            "Flowspan Swap endpoint state key",
            AuthenticatedSwapEndpointStateFile.KeyBytes);
    }

    public static string GetDefaultCoordinationLockPath()
    {
        string? runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDirectory)
            && Path.IsPathFullyQualified(runtimeDirectory))
        {
            return Path.Combine(
                runtimeDirectory,
                "flowspan",
                "swap-endpoint-state-key-secret-tool.lock");
        }

        return Path.GetFullPath(Path.Combine(
            GetSecurityDirectory(),
            "swap-endpoint-state-key-secret-tool.lock"));
    }

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
                "The Linux Swap endpoint state key disappeared after a concurrent create.");
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
            "The Linux Secret Service Swap endpoint state key has an invalid length.");
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

public sealed class LinuxSwapEndpointStatePayloadStore :
    ISwapEndpointStatePayloadStore
{
    private readonly AuthenticatedSwapEndpointStateFile inner;

    public LinuxSwapEndpointStatePayloadStore()
        : this(GetDefaultStatePath(), new LinuxSwapEndpointStateKeyStore())
    {
    }

    public LinuxSwapEndpointStatePayloadStore(
        string statePath,
        ISecretToolProcessRunner runner,
        string keyCoordinationLockPath)
        : this(
            statePath,
            new LinuxSwapEndpointStateKeyStore(runner, keyCoordinationLockPath))
    {
    }

    public LinuxSwapEndpointStatePayloadStore(
        string statePath,
        ISwapEndpointStateKeyStore keyStore)
    {
        inner = new AuthenticatedSwapEndpointStateFile(statePath, keyStore);
    }

    public static string GetDefaultStatePath() => Path.GetFullPath(Path.Combine(
        GetSecurityDirectory(),
        "swap-endpoint-state.fsef"));

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
