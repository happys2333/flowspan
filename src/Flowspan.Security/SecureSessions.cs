using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Flowspan.Security;

public sealed class EphemeralKeyAgreement : IDisposable
{
    private readonly ECDiffieHellman keyAgreement;
    private bool disposed;

    private EphemeralKeyAgreement(ECDiffieHellman keyAgreement) =>
        this.keyAgreement = keyAgreement;

    public static EphemeralKeyAgreement Generate() => new(
        ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));

    public byte[] ExportSubjectPublicKeyInfo()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return keyAgreement.ExportSubjectPublicKeyInfo();
    }

    public byte[] DeriveRawSecret(ReadOnlySpan<byte> peerSubjectPublicKeyInfo)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using ECDiffieHellman peer = ImportSubjectPublicKeyInfo(
            peerSubjectPublicKeyInfo);

        return keyAgreement.DeriveRawSecretAgreement(peer.PublicKey);
    }

    internal static void ValidateSubjectPublicKeyInfo(
        ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        using ECDiffieHellman _ = ImportSubjectPublicKeyInfo(subjectPublicKeyInfo);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        keyAgreement.Dispose();
        disposed = true;
    }

    private static ECDiffieHellman ImportSubjectPublicKeyInfo(
        ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty || subjectPublicKeyInfo.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectPublicKeyInfo),
                "An ephemeral ECDH SPKI must contain 1 to 1024 bytes.");
        }

        ECDiffieHellman peer = ECDiffieHellman.Create();
        try
        {
            peer.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
            ECParameters parameters = peer.ExportParameters(includePrivateParameters: false);
            if (bytesRead != subjectPublicKeyInfo.Length
                || peer.KeySize != 256
                || parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                throw new CryptographicException(
                    "The ephemeral agreement key must be exactly one P-256 SPKI value.");
            }

            return peer;
        }
        catch
        {
            peer.Dispose();
            throw;
        }
    }
}

public static class HkdfSha256
{
    public const int MaximumOutputBytes = 255 * SHA256.HashSizeInBytes;

    public static byte[] DeriveKey(
        ReadOnlySpan<byte> inputKeyMaterial,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info,
        int outputLength)
    {
        if (inputKeyMaterial.IsEmpty)
        {
            throw new ArgumentException(
                "HKDF input key material cannot be empty.",
                nameof(inputKeyMaterial));
        }

        if (outputLength is < 1 or > MaximumOutputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputLength),
                $"HKDF output must contain 1 to {MaximumOutputBytes} bytes.");
        }

        byte[] output = GC.AllocateUninitializedArray<byte>(outputLength);
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            inputKeyMaterial,
            output,
            salt,
            info);
        return output;
    }
}

public enum SecureSessionRole
{
    Initiator,
    Responder,
}

public sealed class SecureSessionKeyMaterial : IDisposable
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("FLOWSPAN-SESSION-V1");
    private static readonly byte[] RemoteWindowMediaInfo =
        Encoding.ASCII.GetBytes("FLOWSPAN-REMOTE-WINDOW-MEDIA-V1");
    private readonly byte[] initiatorToResponderKey;
    private readonly byte[] responderToInitiatorKey;
    private readonly byte[] sessionIdentifier;
    private bool disposed;

    private SecureSessionKeyMaterial(
        byte[] initiatorToResponderKey,
        byte[] responderToInitiatorKey,
        byte[] sessionIdentifier)
    {
        this.initiatorToResponderKey = initiatorToResponderKey;
        this.responderToInitiatorKey = responderToInitiatorKey;
        this.sessionIdentifier = sessionIdentifier;
    }

    public static SecureSessionKeyMaterial Derive(
        ReadOnlySpan<byte> rawSharedSecret,
        ReadOnlySpan<byte> authenticatedTranscriptHash) => Derive(
            rawSharedSecret,
            authenticatedTranscriptHash,
            Info);

    internal static SecureSessionKeyMaterial DeriveRemoteWindowMedia(
        ReadOnlySpan<byte> rawSharedSecret,
        ReadOnlySpan<byte> authenticatedTranscriptHash) => Derive(
            rawSharedSecret,
            authenticatedTranscriptHash,
            RemoteWindowMediaInfo);

    private static SecureSessionKeyMaterial Derive(
        ReadOnlySpan<byte> rawSharedSecret,
        ReadOnlySpan<byte> authenticatedTranscriptHash,
        ReadOnlySpan<byte> info)
    {
        if (rawSharedSecret.IsEmpty)
        {
            throw new ArgumentException(
                "A raw ECDH shared secret is required.",
                nameof(rawSharedSecret));
        }

        if (authenticatedTranscriptHash.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                "The authenticated transcript hash must be SHA-256.",
                nameof(authenticatedTranscriptHash));
        }

        byte[] output = HkdfSha256.DeriveKey(
            rawSharedSecret,
            authenticatedTranscriptHash,
            info,
            80);
        try
        {
            return new SecureSessionKeyMaterial(
                output[..32],
                output[32..64],
                output[64..80]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(output);
        }
    }

    public SecureFrameSession CreateSession(SecureSessionRole role)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return role switch
        {
            SecureSessionRole.Initiator => new SecureFrameSession(
                initiatorToResponderKey,
                SecureFrameDirection.InitiatorToResponder,
                responderToInitiatorKey,
                SecureFrameDirection.ResponderToInitiator,
                sessionIdentifier),
            SecureSessionRole.Responder => new SecureFrameSession(
                responderToInitiatorKey,
                SecureFrameDirection.ResponderToInitiator,
                initiatorToResponderKey,
                SecureFrameDirection.InitiatorToResponder,
                sessionIdentifier),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown session role."),
        };
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(initiatorToResponderKey);
        CryptographicOperations.ZeroMemory(responderToInitiatorKey);
        CryptographicOperations.ZeroMemory(sessionIdentifier);
        disposed = true;
    }
}

public sealed class SecureFrameSession : IDisposable
{
    public const ulong MaximumFramesPerEpoch = 1_048_576;
    public const ulong MaximumPlaintextBytesPerEpoch = 1024UL * 1024 * 1024;
    private readonly SecureFrameProtector receiver;
    private readonly SecureFrameProtector sender;
    private bool disposed;

    internal SecureFrameSession(
        ReadOnlySpan<byte> sendKey,
        SecureFrameDirection sendDirection,
        ReadOnlySpan<byte> receiveKey,
        SecureFrameDirection receiveDirection,
        ReadOnlySpan<byte> sessionIdentifier,
        ulong maximumFramesPerEpoch = MaximumFramesPerEpoch,
        ulong maximumPlaintextBytesPerEpoch = MaximumPlaintextBytesPerEpoch)
    {
        sender = new SecureFrameProtector(
            sendKey,
            sendDirection,
            sessionIdentifier,
            maximumFramesPerEpoch,
            maximumPlaintextBytesPerEpoch);
        receiver = new SecureFrameProtector(
            receiveKey,
            receiveDirection,
            sessionIdentifier,
            maximumFramesPerEpoch,
            maximumPlaintextBytesPerEpoch);
        SessionIdentifier = Convert.ToHexString(sessionIdentifier);
    }

    public string SessionIdentifier { get; }

    public ulong NextSendSequence => sender.Sequence;

    public ulong NextReceiveSequence => receiver.Sequence;

    public uint ReceiveEpoch => receiver.Epoch;

    public ulong ReceivePlaintextBytes => receiver.PlaintextBytes;

    public uint SendEpoch => sender.Epoch;

    public ulong SendPlaintextBytes => sender.PlaintextBytes;

    public bool ShouldRekeyBeforeSend(int nextPlaintextBytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextPlaintextBytes);
        return sender.ShouldReserveEpochTransition(
            nextPlaintextBytes,
            SecureSessionKeyUpdateCodec.EncodedLength);
    }

    public byte[] ExportSessionIdentifier()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Convert.FromHexString(SessionIdentifier);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return sender.Encrypt(plaintext);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return receiver.Decrypt(frame);
    }

    public void AdvanceReceiveEpoch(uint nextEpoch)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        receiver.AdvanceEpoch(nextEpoch);
    }

    public void AdvanceSendEpoch(uint nextEpoch)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        sender.AdvanceEpoch(nextEpoch);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        sender.Dispose();
        receiver.Dispose();
        disposed = true;
    }
}

internal enum SecureFrameDirection : byte
{
    InitiatorToResponder = 1,
    ResponderToInitiator = 2,
}

internal sealed class SecureFrameProtector : IDisposable
{
    public const int MaximumPlaintextBytes = 256 * 1024;
    private const int HeaderLength = 4 + sizeof(uint) + sizeof(ulong) + sizeof(uint);
    private const int TagLength = 16;
    private static readonly byte[] AssociatedDataContext =
        Encoding.ASCII.GetBytes("FLOWSPAN-AEAD-V1");
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FSE1");
    private readonly SecureFrameDirection direction;
    private readonly Lock gate = new();
    private readonly ulong maximumFramesPerEpoch;
    private readonly ulong maximumPlaintextBytesPerEpoch;
    private readonly byte[] sessionIdentifier;
    private bool disposed;
    private uint epoch = 1;
    private byte[] key;
    private ulong plaintextBytes;
    private ulong sequence;

    public SecureFrameProtector(
        ReadOnlySpan<byte> key,
        SecureFrameDirection direction,
        ReadOnlySpan<byte> sessionIdentifier,
        ulong maximumFramesPerEpoch,
        ulong maximumPlaintextBytesPerEpoch)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(key));
        }

        if (sessionIdentifier.Length != 16)
        {
            throw new ArgumentException(
                "A secure session identifier must contain 16 bytes.",
                nameof(sessionIdentifier));
        }

        if (maximumFramesPerEpoch is < 1 or > SecureFrameSession.MaximumFramesPerEpoch)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFramesPerEpoch),
                $"A secure-session epoch must permit 1 to {SecureFrameSession.MaximumFramesPerEpoch} frames.");
        }

        if (maximumPlaintextBytesPerEpoch is < 1
            or > SecureFrameSession.MaximumPlaintextBytesPerEpoch)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPlaintextBytesPerEpoch),
                $"A secure-session epoch must permit 1 to {SecureFrameSession.MaximumPlaintextBytesPerEpoch} plaintext bytes.");
        }

        this.key = key.ToArray();
        this.direction = direction;
        this.sessionIdentifier = sessionIdentifier.ToArray();
        this.maximumFramesPerEpoch = maximumFramesPerEpoch;
        this.maximumPlaintextBytesPerEpoch = maximumPlaintextBytesPerEpoch;
    }

    public uint Epoch
    {
        get
        {
            lock (gate)
            {
                return epoch;
            }
        }
    }

    public ulong PlaintextBytes
    {
        get
        {
            lock (gate)
            {
                return plaintextBytes;
            }
        }
    }

    public ulong Sequence
    {
        get
        {
            lock (gate)
            {
                return sequence;
            }
        }
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (plaintext.Length > MaximumPlaintextBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plaintext),
                $"Secure-frame plaintext cannot exceed {MaximumPlaintextBytes} bytes.");
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            EnsureUsageAvailable(plaintext.Length);
            ulong currentSequence = sequence;
            byte[] frame = GC.AllocateUninitializedArray<byte>(
                HeaderLength + plaintext.Length + TagLength);
            Magic.CopyTo(frame, 0);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), epoch);
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(8), currentSequence);
            BinaryPrimitives.WriteUInt32BigEndian(
                frame.AsSpan(16),
                checked((uint)plaintext.Length));
            Span<byte> ciphertext = frame.AsSpan(HeaderLength, plaintext.Length);
            Span<byte> tag = frame.AsSpan(HeaderLength + plaintext.Length, TagLength);
            Span<byte> nonce = stackalloc byte[12];
            WriteNonce(nonce, currentSequence);
            byte[] associatedData = CreateAssociatedData(
                currentSequence,
                plaintext.Length);
            using var cipher = new AesGcm(key, TagLength);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            CryptographicOperations.ZeroMemory(associatedData);
            plaintextBytes = checked(plaintextBytes + (ulong)plaintext.Length);
            sequence++;
            return frame;
        }
    }

    public byte[] Decrypt(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frame.Length < HeaderLength + TagLength
            || !frame[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The secure frame header is invalid.");
        }

        uint epoch = BinaryPrimitives.ReadUInt32BigEndian(frame[4..]);
        ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(frame[8..]);
        uint ciphertextLength = BinaryPrimitives.ReadUInt32BigEndian(frame[16..]);
        if (ciphertextLength > MaximumPlaintextBytes
            || frame.Length != HeaderLength + ciphertextLength + TagLength)
        {
            throw new InvalidDataException("The secure frame length or epoch is invalid.");
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (epoch != this.epoch)
            {
                throw new InvalidDataException(
                    "The secure frame epoch is a replay or gap.");
            }

            if (sequence != this.sequence)
            {
                throw new InvalidDataException(
                    "The secure frame sequence is a replay or gap.");
            }

            EnsureUsageAvailable(checked((int)ciphertextLength));

            ReadOnlySpan<byte> ciphertext = frame.Slice(HeaderLength, checked((int)ciphertextLength));
            ReadOnlySpan<byte> tag = frame.Slice(
                HeaderLength + checked((int)ciphertextLength),
                TagLength);
            byte[] plaintext = GC.AllocateUninitializedArray<byte>(checked((int)ciphertextLength));
            Span<byte> nonce = stackalloc byte[12];
            WriteNonce(nonce, sequence);
            byte[] associatedData = CreateAssociatedData(sequence, checked((int)ciphertextLength));
            try
            {
                using var cipher = new AesGcm(key, TagLength);
                cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                plaintextBytes = checked(
                    plaintextBytes + (ulong)ciphertextLength);
                this.sequence++;
                return plaintext;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }
        }
    }

    public void AdvanceEpoch(uint nextEpoch)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (epoch == uint.MaxValue || nextEpoch != epoch + 1)
            {
                throw new InvalidOperationException(
                    "A secure frame epoch must advance by exactly one.");
            }

            SecureSessionRole senderRole = direction switch
            {
                SecureFrameDirection.InitiatorToResponder =>
                    SecureSessionRole.Initiator,
                SecureFrameDirection.ResponderToInitiator =>
                    SecureSessionRole.Responder,
                _ => throw new InvalidOperationException(
                    "The secure frame direction is not supported."),
            };
            byte[] nextKey = SecureSessionEpochKeyDerivation.DeriveNextKey(
                key,
                sessionIdentifier,
                senderRole,
                nextEpoch);
            CryptographicOperations.ZeroMemory(key);
            key = nextKey;
            epoch = nextEpoch;
            plaintextBytes = 0;
            sequence = 0;
        }
    }

    public bool ShouldReserveEpochTransition(
        int nextPlaintextBytes,
        int transitionPlaintextBytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ulong requiredBytes = checked(
                (ulong)nextPlaintextBytes + (ulong)transitionPlaintextBytes);
            return sequence >= maximumFramesPerEpoch - 1
                || requiredBytes > maximumPlaintextBytesPerEpoch
                || plaintextBytes
                    > maximumPlaintextBytesPerEpoch - requiredBytes;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (gate)
        {
            if (!disposed)
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(sessionIdentifier);
                disposed = true;
            }
        }
    }

    private byte[] CreateAssociatedData(ulong sequence, int payloadLength)
    {
        byte[] associatedData = new byte[
            AssociatedDataContext.Length
            + sessionIdentifier.Length
            + 1
            + sizeof(uint)
            + sizeof(ulong)
            + sizeof(uint)];
        int offset = 0;
        AssociatedDataContext.CopyTo(associatedData, offset);
        offset += AssociatedDataContext.Length;
        sessionIdentifier.CopyTo(associatedData, offset);
        offset += sessionIdentifier.Length;
        associatedData[offset++] = (byte)direction;
        BinaryPrimitives.WriteUInt32BigEndian(associatedData.AsSpan(offset), epoch);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64BigEndian(associatedData.AsSpan(offset), sequence);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt32BigEndian(
            associatedData.AsSpan(offset),
            checked((uint)payloadLength));
        return associatedData;
    }

    private void WriteNonce(Span<byte> nonce, ulong sequence)
    {
        BinaryPrimitives.WriteUInt32BigEndian(nonce, epoch);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[sizeof(uint)..], sequence);
    }

    private void EnsureUsageAvailable(int plaintextLength)
    {
        if (sequence >= maximumFramesPerEpoch)
        {
            throw new CryptographicException(
                "The secure frame epoch usage bound is exhausted; rekey or reconnect is required.");
        }

        ulong requestedBytes = checked((ulong)plaintextLength);
        if (requestedBytes > maximumPlaintextBytesPerEpoch
            || plaintextBytes > maximumPlaintextBytesPerEpoch - requestedBytes)
        {
            throw new CryptographicException(
                "The secure frame epoch plaintext bound is exhausted; rekey or reconnect is required.");
        }
    }
}
