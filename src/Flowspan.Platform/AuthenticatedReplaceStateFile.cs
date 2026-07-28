using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Application;

namespace Flowspan.Platform;

public interface IAuthenticatedStateKeyStore
{
    public ValueTask<byte[]> GetOrCreateKeyAsync(
        CancellationToken cancellationToken = default);
}

public interface IReplaceStateKeyStore : IAuthenticatedStateKeyStore
{
}

public interface ISwapStateKeyStore : IAuthenticatedStateKeyStore
{
}

public interface ISwapEndpointStateKeyStore : IAuthenticatedStateKeyStore
{
}

public interface ISceneApplyStateKeyStore : IAuthenticatedStateKeyStore
{
}

public interface ISceneRemoteChildStateKeyStore : IAuthenticatedStateKeyStore
{
}

public interface ISceneRepositoryStateKeyStore : IAuthenticatedStateKeyStore
{
}

public interface IOperationHistoryStateKeyStore : IAuthenticatedStateKeyStore
{
}

public sealed class AuthenticatedReplaceStateFile : IReplaceStatePayloadStore
{
    public const int KeyBytes = 32;
    private const byte CurrentFormatVersion = 1;
    private const int LockAttempts = 500;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = 4 + sizeof(byte) + NonceBytes + sizeof(int);
    private static readonly byte[] ReplaceMagic = "FSRF"u8.ToArray();
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(10);
    private readonly IAuthenticatedStateKeyStore keyStore;
    private readonly byte[] magic;
    private readonly int maximumPayloadBytes;
    private readonly string stateName;
    private readonly string storagePath;

    public AuthenticatedReplaceStateFile(
        string storagePath,
        IReplaceStateKeyStore keyStore)
        : this(
            storagePath,
            keyStore,
            ReplaceMagic,
            PersistentReplaceStateStore.MaximumPayloadBytes,
            "Replace")
    {
    }

    internal AuthenticatedReplaceStateFile(
        string storagePath,
        IAuthenticatedStateKeyStore keyStore,
        ReadOnlySpan<byte> magic,
        int maximumPayloadBytes,
        string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentNullException.ThrowIfNull(keyStore);
        if (magic.Length != 4)
        {
            throw new ArgumentException(
                "An authenticated state file magic value must contain four bytes.",
                nameof(magic));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPayloadBytes, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        this.storagePath = Path.GetFullPath(storagePath);
        this.keyStore = keyStore;
        this.magic = magic.ToArray();
        this.maximumPayloadBytes = maximumPayloadBytes;
        this.stateName = stateName;
    }

    public async ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] envelope;
        try
        {
            RejectReparsePoint(storagePath);
            await using var stream = new FileStream(
                storagePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            long maximumEnvelopeBytes =
                HeaderBytes + maximumPayloadBytes + TagBytes;
            if (stream.Length is < HeaderBytes + 1 + TagBytes
                || stream.Length > maximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    $"The authenticated {stateName} state file has an invalid length.");
            }

            envelope = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(envelope, cancellationToken)
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
            {
                ReadOnlySpan<byte> header = envelope.AsSpan(0, HeaderBytes);
                if (!header[..magic.Length].SequenceEqual(magic)
                    || header[magic.Length] != CurrentFormatVersion)
                {
                    throw new InvalidDataException(
                        $"The authenticated {stateName} state file has an unsupported header.");
                }

            }

            int ciphertextLength = BinaryPrimitives.ReadInt32BigEndian(
                envelope.AsSpan(HeaderBytes - sizeof(int), sizeof(int)));
            if (ciphertextLength is < 1
                || ciphertextLength > maximumPayloadBytes
                || envelope.Length != HeaderBytes + ciphertextLength + TagBytes)
            {
                throw new InvalidDataException(
                    $"The authenticated {stateName} state ciphertext has an invalid length.");
            }

            byte[] key = await LoadKeyAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] plaintext = new byte[ciphertextLength];
                try
                {
                    ReadOnlySpan<byte> header = envelope.AsSpan(0, HeaderBytes);
                    using var cipher = new AesGcm(key, TagBytes);
                    cipher.Decrypt(
                        header.Slice(magic.Length + sizeof(byte), NonceBytes),
                        envelope.AsSpan(HeaderBytes, ciphertextLength),
                        envelope.AsSpan(HeaderBytes + ciphertextLength, TagBytes),
                        plaintext,
                        header);
                    return plaintext;
                }
                catch (AuthenticationTagMismatchException exception)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    throw new InvalidDataException(
                        $"The authenticated {stateName} state file failed integrity verification.",
                        exception);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    public async ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty
            || payload.Length > maximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"A {stateName} state payload must contain 1 to {maximumPayloadBytes} bytes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] key = await LoadKeyAsync(cancellationToken).ConfigureAwait(false);
        byte[] envelope = new byte[HeaderBytes + payload.Length + TagBytes];
        try
        {
            Span<byte> header = envelope.AsSpan(0, HeaderBytes);
            magic.CopyTo(header);
            header[magic.Length] = CurrentFormatVersion;
            Span<byte> nonce = header.Slice(magic.Length + sizeof(byte), NonceBytes);
            RandomNumberGenerator.Fill(nonce);
            BinaryPrimitives.WriteInt32BigEndian(
                header[(HeaderBytes - sizeof(int))..],
                payload.Length);
            using (var cipher = new AesGcm(key, TagBytes))
            {
                cipher.Encrypt(
                    nonce,
                    payload.Span,
                    envelope.AsSpan(HeaderBytes, payload.Length),
                    envelope.AsSpan(HeaderBytes + payload.Length, TagBytes),
                    header);
            }

            await WriteAtomicallyAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    private async ValueTask<byte[]> LoadKeyAsync(CancellationToken cancellationToken)
    {
        byte[] key = await keyStore.GetOrCreateKeyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (key.Length != KeyBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException(
                $"A {stateName} state key must contain exactly {KeyBytes} bytes.");
        }

        return key;
    }

    private async ValueTask WriteAtomicallyAsync(
        ReadOnlyMemory<byte> envelope,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(storagePath)
            ?? throw new InvalidOperationException(
                $"The authenticated {stateName} state path has no parent directory.");
        Directory.CreateDirectory(directory);
        SetOwnerOnlyDirectoryMode(directory);
        await using FileStream coordinationLock = await AcquireLockAsync(
            cancellationToken).ConfigureAwait(false);
        RejectReparsePoint(storagePath);
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
                SetOwnerOnlyFileMode(temporaryPath);
                await stream.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, storagePath, overwrite: true);
            SetOwnerOnlyFileMode(storagePath);
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
                var stream = new FileStream(
                    lockPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.OpenOrCreate,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                    });
                SetOwnerOnlyFileMode(lockPath);
                return stream;
            }
            catch (IOException exception)
            {
                lastFailure = exception;
            }

            await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new IOException(
            $"Timed out waiting for exclusive access to the authenticated {stateName} state file.",
            lastFailure);
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "An authenticated state path cannot be a reparse point.");
        }
    }

    private static void SetOwnerOnlyDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static void SetOwnerOnlyFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
