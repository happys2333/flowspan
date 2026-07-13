using System.Security.Cryptography;
using System.Text;
using Flowspan.Security;

namespace Flowspan.Platform.Windows;

public interface IWindowsDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

public sealed class CurrentUserDpapiProtector : IWindowsDataProtector
{
    public const string DeviceIdentityContext = "Flowspan.DeviceIdentity.DPAPI.v1";
    public const string TrustRepositoryContext = "Flowspan.TrustRepository.DPAPI.v1";
    private readonly byte[] entropy;

    public CurrentUserDpapiProtector()
        : this(DeviceIdentityContext)
    {
    }

    public CurrentUserDpapiProtector(string protectionContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectionContext);
        if (protectionContext.Length > 200 || protectionContext.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A DPAPI protection context must contain 1 to 200 non-control characters.",
                nameof(protectionContext));
        }

        entropy = Encoding.UTF8.GetBytes(protectionContext);
    }

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
                entropy,
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
                entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static PlatformNotSupportedException CreatePlatformException() =>
        new("Windows DPAPI protected storage is available only on Windows.");
}

public sealed class WindowsTrustPayloadStore : ITrustPayloadStore
{
    private const int MaximumProtectedPayloadBytes = 128 * 1024;
    private readonly DpapiProtectedPayloadFile inner;

    public WindowsTrustPayloadStore()
        : this(GetDefaultStoragePath())
    {
    }

    public WindowsTrustPayloadStore(string storagePath)
        : this(
            storagePath,
            new CurrentUserDpapiProtector(
                CurrentUserDpapiProtector.TrustRepositoryContext))
    {
    }

    public WindowsTrustPayloadStore(
        string storagePath,
        IWindowsDataProtector dataProtector)
    {
        inner = new DpapiProtectedPayloadFile(
            storagePath,
            dataProtector,
            TrustStorePayloadCodec.MaximumPayloadBytes,
            MaximumProtectedPayloadBytes,
            "trust");
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

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
            "trust.dpapi"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveReplacingAsync(payload, cancellationToken);
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
    private const int MaximumProtectedPayloadBytes = 16 * 1024;
    private readonly DpapiProtectedPayloadFile inner;

    public DpapiIdentityPayloadStore(
        string storagePath,
        IWindowsDataProtector dataProtector)
    {
        inner = new DpapiProtectedPayloadFile(
            storagePath,
            dataProtector,
            DeviceIdentityPayloadCodec.MaximumPayloadBytes,
            MaximumProtectedPayloadBytes,
            "identity");
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(cancellationToken);

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.TrySaveNewAsync(payload, cancellationToken);
}

internal sealed class DpapiProtectedPayloadFile
{
    private const int LockAttempts = 500;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(10);
    private readonly IWindowsDataProtector dataProtector;
    private readonly int maximumPlaintextBytes;
    private readonly int maximumProtectedBytes;
    private readonly string payloadKind;
    private readonly string storagePath;

    public DpapiProtectedPayloadFile(
        string storagePath,
        IWindowsDataProtector dataProtector,
        int maximumPlaintextBytes,
        int maximumProtectedBytes,
        string payloadKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentNullException.ThrowIfNull(dataProtector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPlaintextBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumProtectedBytes,
            maximumPlaintextBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadKind);
        this.storagePath = Path.GetFullPath(storagePath);
        this.dataProtector = dataProtector;
        this.maximumPlaintextBytes = maximumPlaintextBytes;
        this.maximumProtectedBytes = maximumProtectedBytes;
        this.payloadKind = payloadKind;
    }

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
            await using FileStream coordinationLock = await AcquireLockAsync(
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
            if (stream.Length is < 1 || stream.Length > maximumProtectedBytes)
            {
                throw new InvalidDataException(
                    $"The protected Windows {payloadKind} payload has an invalid length.");
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
            if (plaintext.Length is < 1 || plaintext.Length > maximumPlaintextBytes)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new InvalidDataException(
                    $"The unprotected Windows {payloadKind} payload has an invalid length.");
            }

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    public async ValueTask SaveReplacingAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        await SaveProtectedAsync(
            payload,
            replaceExisting: true,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        SaveProtectedAsync(
            payload,
            replaceExisting: false,
            cancellationToken);

    private async ValueTask<bool> SaveProtectedAsync(
        ReadOnlyMemory<byte> payload,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty || payload.Length > maximumPlaintextBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"A {payloadKind} payload must contain 1 to {maximumPlaintextBytes} bytes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] protectedPayload = dataProtector.Protect(payload.Span);
        try
        {
            if (protectedPayload.Length is < 1 ||
                protectedPayload.Length > maximumProtectedBytes)
            {
                throw new InvalidDataException(
                    $"The protected Windows {payloadKind} payload has an invalid length.");
            }

            return await WriteAtomicallyAsync(
                protectedPayload,
                replaceExisting,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private async ValueTask<bool> WriteAtomicallyAsync(
        ReadOnlyMemory<byte> protectedPayload,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(storagePath)
            ?? throw new InvalidOperationException(
                $"The Windows {payloadKind} store path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using FileStream coordinationLock = await AcquireLockAsync(
            cancellationToken).ConfigureAwait(false);
        if (!replaceExisting && File.Exists(storagePath))
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
            if (replaceExisting)
            {
                File.Move(temporaryPath, storagePath, overwrite: true);
                return true;
            }

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

    private async ValueTask<FileStream> AcquireLockAsync(
        CancellationToken cancellationToken)
    {
        string lockPath = $"{storagePath}.lock";
        IOException? lastFailure = null;
        for (int attempt = 0; attempt < LockAttempts; attempt++)
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

            await Task.Delay(LockRetryDelay, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new IOException(
            $"Timed out waiting for exclusive access to the Windows {payloadKind} store.",
            lastFailure);
    }
}
