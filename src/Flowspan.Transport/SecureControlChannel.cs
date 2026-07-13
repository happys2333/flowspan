using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class SecureControlChannel : IAsyncDisposable
{
    public const int MaximumEncryptedFrameBytes =
        ControlMessageCodec.MaximumFrameBytes + 128;

    private const int LengthPrefixBytes = sizeof(int);
    private readonly SemaphoreSlim receiveGate = new(1, 1);
    private readonly SecureFrameSession session;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Stream stream;
    private int disposed;
    private int faulted;

    public SecureControlChannel(Stream stream, SecureFrameSession session)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(session);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException(
                "A secure control channel requires a readable and writable stream.",
                nameof(stream));
        }

        this.stream = stream;
        this.session = session;
    }

    public async ValueTask SendAsync(
        ControlMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfUnavailable();
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        byte[]? encryptedFrame = null;
        try
        {
            ThrowIfUnavailable();
            plaintext = ControlMessageCodec.Encode(message);
            encryptedFrame = session.Encrypt(plaintext);
            if (encryptedFrame.Length > MaximumEncryptedFrameBytes)
            {
                throw new InvalidDataException(
                    "The encrypted control frame exceeds the transport limit.");
            }

            byte[] lengthPrefix = new byte[LengthPrefixBytes];
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, encryptedFrame.Length);
            await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(encryptedFrame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Fault();
            throw;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (encryptedFrame is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedFrame);
            }

            sendGate.Release();
        }
    }

    public async ValueTask<ControlMessage> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? encryptedFrame = null;
        byte[]? plaintext = null;
        try
        {
            ThrowIfUnavailable();
            byte[] lengthPrefix = new byte[LengthPrefixBytes];
            await stream.ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            int frameLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
            if (frameLength is < 1 or > MaximumEncryptedFrameBytes)
            {
                throw new InvalidDataException(
                    $"An encrypted control frame length must be from 1 to {MaximumEncryptedFrameBytes} bytes.");
            }

            encryptedFrame = GC.AllocateUninitializedArray<byte>(frameLength);
            await stream.ReadExactlyAsync(encryptedFrame, cancellationToken).ConfigureAwait(false);
            plaintext = session.Decrypt(encryptedFrame);
            return ControlMessageCodec.Decode(plaintext);
        }
        catch
        {
            Fault();
            throw;
        }
        finally
        {
            if (encryptedFrame is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedFrame);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            receiveGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await receiveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                session.Dispose();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                receiveGate.Release();
            }
        }
        finally
        {
            sendGate.Release();
        }
    }

    internal void RejectPeerMessage() => Fault();

    private void Fault()
    {
        if (Interlocked.Exchange(ref faulted, 1) == 0)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
                // Preserve the protocol or I/O exception that faulted the channel.
            }

            try
            {
                stream.Dispose();
            }
            catch (Exception)
            {
                // Preserve the protocol or I/O exception that faulted the channel.
            }
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        if (Volatile.Read(ref faulted) != 0)
        {
            throw new InvalidOperationException(
                "The secure control channel is faulted and cannot be reused.");
        }
    }
}
