using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Security;

public static class SessionHandshakeWireCodec
{
    public const int MaximumMessageBytes = 4 * 1024;
    private const byte AuthenticationKind = 2;
    private const byte HelloKind = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FSH1");

    public static byte[] EncodeHello(SessionHandshakeHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        var writer = new SessionHandshakeBuffer();
        writer.WriteRaw(Magic);
        writer.WriteByte(HelloKind);
        writer.WriteByte(ToWireRole(hello.Role));
        writer.WriteUtf8(hello.DeviceId.ToString());
        writer.WriteUtf8(hello.IdentityFingerprint);
        writer.WriteUInt32(checked((uint)hello.ProtocolVersions.Length));
        foreach (ProtocolVersion version in hello.ProtocolVersions)
        {
            writer.WriteUInt32(checked((uint)version.Major));
            writer.WriteUInt32(checked((uint)version.Minor));
        }

        writer.WriteBytes(hello.EphemeralPublicKey);
        writer.WriteBytes(hello.Nonce);
        return EnforceEncodedLimit(writer.ToArray());
    }

    public static SessionHandshakeHello DecodeHello(
        ReadOnlySpan<byte> message,
        PublicDeviceIdentity expectedIdentity)
    {
        ArgumentNullException.ThrowIfNull(expectedIdentity);
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(HelloKind);
            SecureSessionRole role = FromWireRole(reader.ReadByte());
            DeviceId deviceId = ReadDeviceId(ref reader);

            string fingerprint = reader.ReadUtf8(maximumBytes: 64);
            uint versionCount = reader.ReadUInt32();
            if (versionCount is < 1 or > 16)
            {
                throw new InvalidDataException(
                    "The session hello protocol version count is invalid.");
            }

            var versions = ImmutableArray.CreateBuilder<ProtocolVersion>(
                checked((int)versionCount));
            ProtocolVersion? previousVersion = null;
            for (int index = 0; index < versionCount; index++)
            {
                var version = new ProtocolVersion(
                    checked((int)reader.ReadUInt32()),
                    checked((int)reader.ReadUInt32()));
                if (previousVersion is { } previous && version <= previous)
                {
                    throw new InvalidDataException(
                        "Session hello protocol versions must be unique and strictly increasing.");
                }

                versions.Add(version);
                previousVersion = version;
            }

            byte[] ephemeralPublicKey = reader.ReadBytes(maximumBytes: 1024);
            byte[] nonce = reader.ReadBytes(maximumBytes: SessionHandshakeHello.NonceLength);
            reader.RequireEnd();
            if (expectedIdentity.DeviceId != deviceId
                || !StringComparer.Ordinal.Equals(
                    expectedIdentity.Fingerprint,
                    fingerprint))
            {
                throw new SessionHandshakeException(
                    SessionHandshakeFailure.PeerIdentityChanged,
                    "The session hello does not match the trusted identity.");
            }

            return SessionHandshakeHello.Create(
                role,
                expectedIdentity,
                versions.MoveToImmutable(),
                ephemeralPublicKey,
                nonce);
        }
        catch (SessionHandshakeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or CryptographicException
            or OverflowException)
        {
            throw new InvalidDataException("The session hello is malformed.", exception);
        }
    }

    public static DeviceId ReadClaimedHelloDeviceId(ReadOnlySpan<byte> message)
    {
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(HelloKind);
            _ = FromWireRole(reader.ReadByte());
            return ReadDeviceId(ref reader);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The session hello identity claim is malformed.",
                exception);
        }
    }

    public static byte[] EncodeAuthentication(
        SessionHandshakeAuthentication authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        var writer = new SessionHandshakeBuffer();
        writer.WriteRaw(Magic);
        writer.WriteByte(AuthenticationKind);
        writer.WriteByte(ToWireRole(authentication.Role));
        writer.WriteBytes(authentication.TranscriptHash);
        writer.WriteBytes(authentication.Signature);
        return EnforceEncodedLimit(writer.ToArray());
    }

    public static SessionHandshakeAuthentication DecodeAuthentication(
        ReadOnlySpan<byte> message)
    {
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(AuthenticationKind);
            SecureSessionRole role = FromWireRole(reader.ReadByte());
            byte[] transcriptHash = reader.ReadBytes(
                maximumBytes: System.Security.Cryptography.SHA256.HashSizeInBytes);
            byte[] signature = reader.ReadBytes(
                maximumBytes: SessionHandshakeAuthentication.SignatureLength);
            reader.RequireEnd();
            return SessionHandshakeAuthentication.Import(
                role,
                transcriptHash,
                signature);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The session authentication message is malformed.",
                exception);
        }
    }

    private static byte[] EnforceEncodedLimit(byte[] encoded) =>
        encoded.Length <= MaximumMessageBytes
            ? encoded
            : throw new InvalidDataException(
                $"A session handshake message cannot exceed {MaximumMessageBytes} bytes.");

    private static SecureSessionRole FromWireRole(byte role) => role switch
    {
        1 => SecureSessionRole.Initiator,
        2 => SecureSessionRole.Responder,
        _ => throw new InvalidDataException("The session handshake role is unknown."),
    };

    private static byte ToWireRole(SecureSessionRole role) => role switch
    {
        SecureSessionRole.Initiator => 1,
        SecureSessionRole.Responder => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown session role."),
    };

    private static void ValidateMessageSize(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty || message.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException(
                $"A session handshake message must contain 1 to {MaximumMessageBytes} bytes.");
        }
    }

    private static DeviceId ReadDeviceId(ref SessionHandshakeWireReader reader)
    {
        string deviceIdText = reader.ReadUtf8(maximumBytes: 36);
        if (!Guid.TryParseExact(deviceIdText, "D", out Guid deviceId)
            || deviceId == Guid.Empty)
        {
            throw new InvalidDataException("The session hello device ID is invalid.");
        }

        return DeviceId.From(deviceId);
    }
}

internal ref struct SessionHandshakeWireReader
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly ReadOnlySpan<byte> source;
    private int offset;

    public SessionHandshakeWireReader(ReadOnlySpan<byte> source) =>
        this.source = source;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return source[offset++];
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32BigEndian(source[offset..]);
        offset += sizeof(uint);
        return value;
    }

    public byte[] ReadBytes(int maximumBytes)
    {
        uint length = ReadUInt32();
        if (length > maximumBytes)
        {
            throw new InvalidDataException("A session handshake field is too large.");
        }

        int count = checked((int)length);
        EnsureAvailable(count);
        byte[] value = source.Slice(offset, count).ToArray();
        offset += count;
        return value;
    }

    public string ReadUtf8(int maximumBytes) =>
        StrictUtf8.GetString(ReadBytes(maximumBytes));

    public void RequireEnd()
    {
        if (offset != source.Length)
        {
            throw new InvalidDataException(
                "A session handshake message contains trailing data.");
        }
    }

    public void RequireKind(byte expected)
    {
        if (ReadByte() != expected)
        {
            throw new InvalidDataException(
                "The session handshake message kind is unexpected.");
        }
    }

    public void RequireMagic(ReadOnlySpan<byte> expected)
    {
        EnsureAvailable(expected.Length);
        if (!source.Slice(offset, expected.Length).SequenceEqual(expected))
        {
            throw new InvalidDataException("The session handshake magic is invalid.");
        }

        offset += expected.Length;
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || source.Length - offset < count)
        {
            throw new InvalidDataException(
                "The session handshake message ended unexpectedly.");
        }
    }
}
