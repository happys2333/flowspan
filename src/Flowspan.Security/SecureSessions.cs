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
        if (peerSubjectPublicKeyInfo.IsEmpty || peerSubjectPublicKeyInfo.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(peerSubjectPublicKeyInfo),
                "An ephemeral ECDH SPKI must contain 1 to 1024 bytes.");
        }

        using ECDiffieHellman peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerSubjectPublicKeyInfo, out int bytesRead);
        ECParameters parameters = peer.ExportParameters(includePrivateParameters: false);
        if (bytesRead != peerSubjectPublicKeyInfo.Length
            || peer.KeySize != 256
            || parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
        {
            throw new CryptographicException(
                "The ephemeral agreement key must be exactly one P-256 SPKI value.");
        }

        return keyAgreement.DeriveRawSecretAgreement(peer.PublicKey);
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
        ReadOnlySpan<byte> authenticatedTranscriptHash)
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
            Info,
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
    private readonly SecureFrameProtector receiver;
    private readonly SecureFrameProtector sender;
    private bool disposed;

    internal SecureFrameSession(
        ReadOnlySpan<byte> sendKey,
        SecureFrameDirection sendDirection,
        ReadOnlySpan<byte> receiveKey,
        SecureFrameDirection receiveDirection,
        ReadOnlySpan<byte> sessionIdentifier)
    {
        sender = new SecureFrameProtector(sendKey, sendDirection, sessionIdentifier);
        receiver = new SecureFrameProtector(receiveKey, receiveDirection, sessionIdentifier);
        SessionIdentifier = Convert.ToHexString(sessionIdentifier);
    }

    public string SessionIdentifier { get; }

    public ulong NextSendSequence => sender.Sequence;

    public ulong NextReceiveSequence => receiver.Sequence;

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
    private const uint Epoch = 1;
    private static readonly byte[] AssociatedDataContext =
        Encoding.ASCII.GetBytes("FLOWSPAN-AEAD-V1");
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FSE1");
    private readonly SecureFrameDirection direction;
    private readonly Lock gate = new();
    private readonly byte[] key;
    private readonly byte[] sessionIdentifier;
    private bool disposed;

    public SecureFrameProtector(
        ReadOnlySpan<byte> key,
        SecureFrameDirection direction,
        ReadOnlySpan<byte> sessionIdentifier)
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

        this.key = key.ToArray();
        this.direction = direction;
        this.sessionIdentifier = sessionIdentifier.ToArray();
    }

    public ulong Sequence { get; private set; }

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
            EnsureSequenceAvailable();
            ulong sequence = Sequence;
            byte[] frame = GC.AllocateUninitializedArray<byte>(
                HeaderLength + plaintext.Length + TagLength);
            Magic.CopyTo(frame, 0);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), Epoch);
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(8), sequence);
            BinaryPrimitives.WriteUInt32BigEndian(
                frame.AsSpan(16),
                checked((uint)plaintext.Length));
            Span<byte> ciphertext = frame.AsSpan(HeaderLength, plaintext.Length);
            Span<byte> tag = frame.AsSpan(HeaderLength + plaintext.Length, TagLength);
            Span<byte> nonce = stackalloc byte[12];
            WriteNonce(nonce, sequence);
            byte[] associatedData = CreateAssociatedData(sequence, plaintext.Length);
            using var cipher = new AesGcm(key, TagLength);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            CryptographicOperations.ZeroMemory(associatedData);
            Sequence++;
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
        if (epoch != Epoch
            || ciphertextLength > MaximumPlaintextBytes
            || frame.Length != HeaderLength + ciphertextLength + TagLength)
        {
            throw new InvalidDataException("The secure frame length or epoch is invalid.");
        }

        lock (gate)
        {
            EnsureSequenceAvailable();
            if (sequence != Sequence)
            {
                throw new InvalidDataException(
                    "The secure frame sequence is a replay or gap.");
            }

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
                Sequence++;
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
        BinaryPrimitives.WriteUInt32BigEndian(associatedData.AsSpan(offset), Epoch);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64BigEndian(associatedData.AsSpan(offset), sequence);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt32BigEndian(
            associatedData.AsSpan(offset),
            checked((uint)payloadLength));
        return associatedData;
    }

    private static void WriteNonce(Span<byte> nonce, ulong sequence)
    {
        BinaryPrimitives.WriteUInt32BigEndian(nonce, Epoch);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[sizeof(uint)..], sequence);
    }

    private void EnsureSequenceAvailable()
    {
        if (Sequence == ulong.MaxValue)
        {
            throw new CryptographicException(
                "The secure frame sequence is exhausted; rekey is required.");
        }
    }
}
