using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Platform.Windows;

public interface IWindowsDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

public sealed class CurrentUserDpapiProtector : IWindowsDataProtector
{
    private static readonly byte[] Entropy =
        "Flowspan.DeviceIdentity.DPAPI.v1"u8.ToArray();

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw CreatePlatformException();
        }

        byte[] input = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(
                input,
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw CreatePlatformException();
        }

        byte[] input = protectedData.ToArray();
        try
        {
            return ProtectedData.Unprotect(
                input,
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static PlatformNotSupportedException CreatePlatformException() =>
        new("Windows DPAPI identity storage is available only on Windows.");
}

public sealed class WindowsDeviceIdentityStore : IDeviceIdentityStore
{
    private readonly PayloadBackedDeviceIdentityStore inner;

    public WindowsDeviceIdentityStore()
        : this(GetDefaultStoragePath())
    {
    }

    public WindowsDeviceIdentityStore(string storagePath)
        : this(storagePath, new CurrentUserDpapiProtector())
    {
    }

    public WindowsDeviceIdentityStore(
        string storagePath,
        IWindowsDataProtector dataProtector)
    {
        inner = new PayloadBackedDeviceIdentityStore(
            new DpapiIdentityPayloadStore(storagePath, dataProtector));
    }

    public SecretStoreProtection Protection => inner.Protection;

    public static string GetDefaultStoragePath()
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
            "device-identity.dpapi"));
    }

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(cancellationToken);

    public ValueTask<DeviceIdentity?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask<bool> TrySaveNewAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default) =>
        inner.TrySaveNewAsync(identity, cancellationToken);
}

internal sealed class DpapiIdentityPayloadStore : IDeviceIdentityPayloadStore
{
    private const int CreationLockAttempts = 500;
    private static readonly TimeSpan CreationLockRetryDelay =
        TimeSpan.FromMilliseconds(10);
    private const int MaximumProtectedPayloadBytes = 16 * 1024;
    private readonly IWindowsDataProtector dataProtector;
    private readonly string storagePath;

    public DpapiIdentityPayloadStore(
        string storagePath,
        IWindowsDataProtector dataProtector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentNullException.ThrowIfNull(dataProtector);
        this.storagePath = Path.GetFullPath(storagePath);
        this.dataProtector = dataProtector;
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

    public async ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? directory = Path.GetDirectoryName(storagePath);
        if (directory is null || !Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            await using FileStream creationLock = await AcquireCreationLockAsync(
                cancellationToken).ConfigureAwait(false);
            if (!File.Exists(storagePath))
            {
                return false;
            }

            File.Delete(storagePath);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public async ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] protectedPayload;
        try
        {
            await using var stream = new FileStream(
                storagePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            if (stream.Length is < 1 or > MaximumProtectedPayloadBytes)
            {
                throw new InvalidDataException(
                    "The protected Windows identity payload has an invalid length.");
            }

            protectedPayload = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(protectedPayload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            FileNotFoundException
            or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            byte[] plaintext = dataProtector.Unprotect(protectedPayload);
            if (plaintext.Length is < 1 or > DeviceIdentityPayloadCodec.MaximumPayloadBytes)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new InvalidDataException(
                    "The unprotected Windows identity payload has an invalid length.");
            }

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    public async ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty || payload.Length > DeviceIdentityPayloadCodec.MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"An identity payload must contain 1 to {DeviceIdentityPayloadCodec.MaximumPayloadBytes} bytes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] protectedPayload = dataProtector.Protect(payload.Span);
        try
        {
            if (protectedPayload.Length is < 1 or > MaximumProtectedPayloadBytes)
            {
                throw new InvalidDataException(
                    "The protected Windows identity payload has an invalid length.");
            }

            return await TryWriteAtomicallyAsync(protectedPayload, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private async ValueTask<bool> TryWriteAtomicallyAsync(
        ReadOnlyMemory<byte> protectedPayload,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(storagePath)
            ?? throw new InvalidOperationException(
                "The Windows identity store path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using FileStream creationLock = await AcquireCreationLockAsync(
            cancellationToken).ConfigureAwait(false);
        if (File.Exists(storagePath))
        {
            return false;
        }

        string temporaryPath = $"{storagePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                }))
            {
                await stream.WriteAsync(protectedPayload, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, storagePath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(storagePath))
            {
                return false;
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private async ValueTask<FileStream> AcquireCreationLockAsync(
        CancellationToken cancellationToken)
    {
        string lockPath = $"{storagePath}.lock";
        IOException? lastFailure = null;
        for (int attempt = 0; attempt < CreationLockAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.OpenOrCreate,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                    });
            }
            catch (IOException exception)
            {
                lastFailure = exception;
            }

            await Task.Delay(CreationLockRetryDelay, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new IOException(
            "Timed out waiting for exclusive access to the Windows identity store.",
            lastFailure);
    }
}
