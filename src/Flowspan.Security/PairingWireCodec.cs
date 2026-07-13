using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Security;

public sealed class PairingHello
{
    private PairingHello(
        SecureSessionRole role,
        PairingParty party,
        ImmutableArray<ProtocolVersion> protocolVersions)
    {
        Role = role;
        Party = party;
        ProtocolVersions = protocolVersions;
    }

    public PairingParty Party { get; }

    public ImmutableArray<ProtocolVersion> ProtocolVersions { get; }

    public SecureSessionRole Role { get; }

    public static PairingHello Create(
        SecureSessionRole role,
        PairingParty party,
        IEnumerable<ProtocolVersion> protocolVersions)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown pairing role.");
        }

        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(protocolVersions);
        ImmutableArray<ProtocolVersion> versions = protocolVersions
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (versions.IsDefaultOrEmpty
            || versions.Length > 16
            || versions.Any(static version => version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "A pairing hello must contain 1 to 16 protocol versions.",
                nameof(protocolVersions));
        }

        return new PairingHello(role, party, versions);
    }
}

public static class PairingWireCodec
{
    public const int MaximumMessageBytes = 4 * 1024;
    private const byte CompletionProofKind = 4;
    private const byte ConfirmationKind = 3;
    private const byte HelloKind = 1;
    private const byte TranscriptSignatureKind = 2;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FSP1");

    public static PairingCompletionProof DecodeCompletionProof(
        ReadOnlySpan<byte> message)
    {
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(CompletionProofKind);
            DeviceId deviceId = ReadDeviceId(ref reader);
            byte[] transcriptHash = reader.ReadBytes(SHA256.HashSizeInBytes);
            byte[] signature = reader.ReadBytes(
                PairingTranscriptSignature.SignatureLength);
            reader.RequireEnd();
            return PairingCompletionProof.Import(
                deviceId,
                transcriptHash,
                signature);
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new InvalidDataException(
                "The pairing completion-proof message is malformed.",
                exception);
        }
    }

    public static PairingConfirmation DecodeConfirmation(ReadOnlySpan<byte> message)
    {
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(ConfirmationKind);
            DeviceId deviceId = ReadDeviceId(ref reader);
            bool accepted = reader.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException(
                    "The pairing confirmation decision is invalid."),
            };
            byte[] transcriptHash = reader.ReadBytes(SHA256.HashSizeInBytes);
            byte[] signature = reader.ReadBytes(
                PairingTranscriptSignature.SignatureLength);
            reader.RequireEnd();
            return PairingConfirmation.Import(
                deviceId,
                accepted,
                transcriptHash,
                signature);
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new InvalidDataException(
                "The pairing confirmation message is malformed.",
                exception);
        }
    }

    public static PairingHello DecodeHello(ReadOnlySpan<byte> message)
    {
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(HelloKind);
            SecureSessionRole role = FromWireRole(reader.ReadByte());
            DeviceId deviceId = ReadDeviceId(ref reader);
            string displayName = reader.ReadUtf8(maximumBytes: 320);
            byte[] subjectPublicKeyInfo = reader.ReadBytes(maximumBytes: 1024);
            byte[] nonce = reader.ReadBytes(maximumBytes: PairingParty.NonceLength);
            uint versionCount = reader.ReadUInt32();
            if (versionCount is < 1 or > 16)
            {
                throw new InvalidDataException(
                    "The pairing hello protocol version count is invalid.");
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
                        "Pairing hello protocol versions must be unique and strictly increasing.");
                }

                versions.Add(version);
                previousVersion = version;
            }

            reader.RequireEnd();
            var identity = new PublicDeviceIdentity(
                deviceId,
                displayName,
                subjectPublicKeyInfo);
            if (!StringComparer.Ordinal.Equals(identity.DisplayName, displayName))
            {
                throw new InvalidDataException(
                    "The pairing hello display name is not canonical.");
            }

            return PairingHello.Create(
                role,
                new PairingParty(identity, nonce),
                versions.MoveToImmutable());
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new InvalidDataException(
                "The pairing hello message is malformed.",
                exception);
        }
    }

    public static PairingTranscriptSignature DecodeTranscriptSignature(
        ReadOnlySpan<byte> message)
    {
        ValidateMessageSize(message);
        try
        {
            var reader = new SessionHandshakeWireReader(message);
            reader.RequireMagic(Magic);
            reader.RequireKind(TranscriptSignatureKind);
            DeviceId deviceId = ReadDeviceId(ref reader);
            byte[] transcriptHash = reader.ReadBytes(SHA256.HashSizeInBytes);
            byte[] signature = reader.ReadBytes(
                PairingTranscriptSignature.SignatureLength);
            reader.RequireEnd();
            return PairingTranscriptSignature.Import(
                deviceId,
                transcriptHash,
                signature);
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new InvalidDataException(
                "The pairing transcript-signature message is malformed.",
                exception);
        }
    }

    public static byte[] EncodeConfirmation(PairingConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var writer = CreateWriter(ConfirmationKind);
        writer.WriteUtf8(confirmation.DeviceId.ToString());
        writer.WriteByte(confirmation.Accepted ? (byte)1 : (byte)0);
        writer.WriteBytes(confirmation.TranscriptHash);
        writer.WriteBytes(confirmation.Signature);
        return EnforceEncodedLimit(writer.ToArray());
    }

    public static byte[] EncodeCompletionProof(PairingCompletionProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        var writer = CreateWriter(CompletionProofKind);
        writer.WriteUtf8(proof.DeviceId.ToString());
        writer.WriteBytes(proof.TranscriptHash);
        writer.WriteBytes(proof.Signature);
        return EnforceEncodedLimit(writer.ToArray());
    }

    public static byte[] EncodeHello(PairingHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        var writer = CreateWriter(HelloKind);
        writer.WriteByte(ToWireRole(hello.Role));
        writer.WriteUtf8(hello.Party.Identity.DeviceId.ToString());
        writer.WriteUtf8(hello.Party.Identity.DisplayName);
        writer.WriteBytes(hello.Party.Identity.ExportSubjectPublicKeyInfo());
        writer.WriteBytes(hello.Party.Nonce);
        writer.WriteUInt32(checked((uint)hello.ProtocolVersions.Length));
        foreach (ProtocolVersion version in hello.ProtocolVersions)
        {
            writer.WriteUInt32(checked((uint)version.Major));
            writer.WriteUInt32(checked((uint)version.Minor));
        }

        return EnforceEncodedLimit(writer.ToArray());
    }

    public static byte[] EncodeTranscriptSignature(
        PairingTranscriptSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var writer = CreateWriter(TranscriptSignatureKind);
        writer.WriteUtf8(signature.DeviceId.ToString());
        writer.WriteBytes(signature.TranscriptHash);
        writer.WriteBytes(signature.Signature);
        return EnforceEncodedLimit(writer.ToArray());
    }

    private static PairingBuffer CreateWriter(byte kind)
    {
        var writer = new PairingBuffer();
        writer.WriteRaw(Magic);
        writer.WriteByte(kind);
        return writer;
    }

    private static byte[] EnforceEncodedLimit(byte[] encoded) =>
        encoded.Length <= MaximumMessageBytes
            ? encoded
            : throw new InvalidDataException(
                $"A pairing message cannot exceed {MaximumMessageBytes} bytes.");

    private static SecureSessionRole FromWireRole(byte role) => role switch
    {
        1 => SecureSessionRole.Initiator,
        2 => SecureSessionRole.Responder,
        _ => throw new InvalidDataException("The pairing role is unknown."),
    };

    private static bool IsMalformed(Exception exception) => exception is
        ArgumentException
        or CryptographicException
        or InvalidDataException
        or OverflowException;

    private static DeviceId ReadDeviceId(ref SessionHandshakeWireReader reader)
    {
        string text = reader.ReadUtf8(maximumBytes: 36);
        if (!Guid.TryParseExact(text, "D", out Guid value)
            || value == Guid.Empty
            || !StringComparer.Ordinal.Equals(value.ToString("D"), text))
        {
            throw new InvalidDataException(
                "The pairing Device ID is invalid or non-canonical.");
        }

        return DeviceId.From(value);
    }

    private static byte ToWireRole(SecureSessionRole role) => role switch
    {
        SecureSessionRole.Initiator => 1,
        SecureSessionRole.Responder => 2,
        _ => throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "Unknown pairing role."),
    };

    private static void ValidateMessageSize(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty || message.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException(
                $"A pairing message must contain 1 to {MaximumMessageBytes} bytes.");
        }
    }
}
