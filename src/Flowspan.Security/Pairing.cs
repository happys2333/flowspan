using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Security;

public sealed class PairingParty
{
    public const int NonceLength = 32;
    private readonly byte[] nonce;

    public PairingParty(PublicDeviceIdentity identity, ReadOnlySpan<byte> nonce)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException(
                $"A pairing nonce must contain exactly {NonceLength} bytes.",
                nameof(nonce));
        }

        Identity = identity;
        this.nonce = nonce.ToArray();
    }

    public PublicDeviceIdentity Identity { get; }

    public byte[] ExportNonce() => (byte[])nonce.Clone();

    internal ReadOnlySpan<byte> Nonce => nonce;
}

public sealed class PairingTranscript
{
    private static readonly byte[] Context = Encoding.ASCII.GetBytes("FLOWSPAN-PAIR-V1");
    private static readonly byte[] SasContext = Encoding.ASCII.GetBytes("FLOWSPAN-SAS-V1");
    private readonly byte[] encoded;
    private readonly byte[] hash;

    private PairingTranscript(
        PairingParty initiator,
        PairingParty responder,
        ProtocolVersion protocolVersion,
        byte[] encoded,
        byte[] hash,
        string shortAuthenticationString)
    {
        Initiator = initiator;
        Responder = responder;
        ProtocolVersion = protocolVersion;
        this.encoded = encoded;
        this.hash = hash;
        ShortAuthenticationString = shortAuthenticationString;
    }

    public PairingParty Initiator { get; }

    public PairingParty Responder { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public string ShortAuthenticationString { get; }

    public static PairingTranscript Create(
        PairingParty initiator,
        PairingParty responder,
        ProtocolVersion protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        ArgumentNullException.ThrowIfNull(responder);
        if (initiator.Identity.DeviceId == responder.Identity.DeviceId)
        {
            throw new ArgumentException(
                "A device cannot pair with itself.",
                nameof(responder));
        }

        if (protocolVersion.Major < 1 || protocolVersion.Minor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                "A pairing protocol version must be initialized.");
        }

        var writer = new PairingBuffer();
        writer.WriteRaw(Context);
        writer.WriteUInt32(checked((uint)protocolVersion.Major));
        writer.WriteUInt32(checked((uint)protocolVersion.Minor));
        WriteParty(writer, role: 1, initiator);
        WriteParty(writer, role: 2, responder);
        byte[] encoded = writer.ToArray();
        byte[] hash = SHA256.HashData(encoded);

        byte[] sasMaterial = new byte[SasContext.Length + hash.Length];
        SasContext.CopyTo(sasMaterial, 0);
        hash.CopyTo(sasMaterial, SasContext.Length);
        byte[] sasHash = SHA256.HashData(sasMaterial);
        uint sasNumber = BinaryPrimitives.ReadUInt32BigEndian(sasHash) % 1_000_000;
        string sas = sasNumber.ToString("D6", CultureInfo.InvariantCulture);
        CryptographicOperations.ZeroMemory(sasMaterial);
        CryptographicOperations.ZeroMemory(sasHash);

        return new PairingTranscript(
            initiator,
            responder,
            protocolVersion,
            encoded,
            hash,
            sas);
    }

    public byte[] ExportEncoded() => (byte[])encoded.Clone();

    public byte[] ExportHash() => (byte[])hash.Clone();

    public byte[] Sign(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.DeviceId != Initiator.Identity.DeviceId
            && identity.DeviceId != Responder.Identity.DeviceId)
        {
            throw new InvalidOperationException(
                "Only a party in the pairing transcript can sign it.");
        }

        return identity.SignHash(hash);
    }

    private static void WriteParty(PairingBuffer writer, byte role, PairingParty party)
    {
        writer.WriteByte(role);
        writer.WriteUtf8(party.Identity.DeviceId.ToString());
        writer.WriteUtf8(party.Identity.DisplayName);
        writer.WriteBytes(party.Identity.ExportSubjectPublicKeyInfo());
        writer.WriteBytes(party.Nonce);
    }
}

public sealed class PairingConfirmation
{
    private static readonly byte[] Context = Encoding.ASCII.GetBytes("FLOWSPAN-CONFIRM-V1");
    private readonly byte[] signature;
    private readonly byte[] transcriptHash;

    private PairingConfirmation(
        DeviceId deviceId,
        bool accepted,
        byte[] transcriptHash,
        byte[] signature)
    {
        DeviceId = deviceId;
        Accepted = accepted;
        this.transcriptHash = transcriptHash;
        this.signature = signature;
    }

    public DeviceId DeviceId { get; }

    public bool Accepted { get; }

    public static PairingConfirmation Create(
        DeviceIdentity identity,
        PairingTranscript transcript,
        bool accepted)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(transcript);
        byte[] hash = transcript.ExportHash();
        byte[] payloadHash = ComputePayloadHash(identity.DeviceId, hash, accepted);
        byte[] signature = identity.SignHash(payloadHash);
        CryptographicOperations.ZeroMemory(payloadHash);
        return new PairingConfirmation(identity.DeviceId, accepted, hash, signature);
    }

    public bool Verify(PublicDeviceIdentity identity, PairingTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(transcript);
        byte[] expectedHash = transcript.ExportHash();
        bool transcriptMatches = CryptographicOperations.FixedTimeEquals(
            transcriptHash,
            expectedHash);
        byte[] payloadHash = ComputePayloadHash(DeviceId, transcriptHash, Accepted);
        bool valid = identity.DeviceId == DeviceId
            && transcriptMatches
            && identity.VerifyHash(payloadHash, signature);
        CryptographicOperations.ZeroMemory(expectedHash);
        CryptographicOperations.ZeroMemory(payloadHash);
        return valid;
    }

    private static byte[] ComputePayloadHash(
        DeviceId deviceId,
        ReadOnlySpan<byte> transcriptHash,
        bool accepted)
    {
        var writer = new PairingBuffer();
        writer.WriteRaw(Context);
        writer.WriteBytes(transcriptHash);
        writer.WriteUtf8(deviceId.ToString());
        writer.WriteByte(accepted ? (byte)1 : (byte)0);
        return SHA256.HashData(writer.ToArray());
    }
}

public sealed record TrustRecord(
    PublicDeviceIdentity PeerIdentity,
    DateTimeOffset VerifiedAt,
    CapabilityGrant GrantedCapabilities);

public enum PairingFailure
{
    None,
    Timeout,
    Rejected,
    InvalidTranscriptSignature,
    InvalidConfirmation,
}

public sealed record PairingOutcome(
    bool Succeeded,
    PairingFailure Failure,
    TrustRecord? InitiatorTrust,
    TrustRecord? ResponderTrust);

public static class PairingVerifier
{
    public static PairingOutcome EstablishTrust(
        PairingTranscript transcript,
        ReadOnlySpan<byte> initiatorTranscriptSignature,
        ReadOnlySpan<byte> responderTranscriptSignature,
        PairingConfirmation initiatorConfirmation,
        PairingConfirmation responderConfirmation,
        CapabilityGrant capabilitiesGrantedToResponder,
        CapabilityGrant capabilitiesGrantedToInitiator,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(initiatorConfirmation);
        ArgumentNullException.ThrowIfNull(responderConfirmation);
        ArgumentNullException.ThrowIfNull(capabilitiesGrantedToResponder);
        ArgumentNullException.ThrowIfNull(capabilitiesGrantedToInitiator);

        if (now > expiresAt)
        {
            return Failure(PairingFailure.Timeout);
        }

        byte[] transcriptHash = transcript.ExportHash();
        bool validTranscriptSignatures = transcript.Initiator.Identity.VerifyHash(
                transcriptHash,
                initiatorTranscriptSignature)
            && transcript.Responder.Identity.VerifyHash(
                transcriptHash,
                responderTranscriptSignature);
        CryptographicOperations.ZeroMemory(transcriptHash);
        if (!validTranscriptSignatures)
        {
            return Failure(PairingFailure.InvalidTranscriptSignature);
        }

        if (!initiatorConfirmation.Accepted || !responderConfirmation.Accepted)
        {
            return Failure(PairingFailure.Rejected);
        }

        if (!initiatorConfirmation.Verify(transcript.Initiator.Identity, transcript)
            || !responderConfirmation.Verify(transcript.Responder.Identity, transcript))
        {
            return Failure(PairingFailure.InvalidConfirmation);
        }

        return new PairingOutcome(
            true,
            PairingFailure.None,
            new TrustRecord(
                transcript.Responder.Identity,
                now,
                capabilitiesGrantedToResponder),
            new TrustRecord(
                transcript.Initiator.Identity,
                now,
                capabilitiesGrantedToInitiator));
    }

    private static PairingOutcome Failure(PairingFailure failure) =>
        new(false, failure, null, null);
}

internal sealed class PairingBuffer
{
    private readonly ArrayBufferWriter<byte> buffer = new();

    public void WriteByte(byte value)
    {
        Span<byte> destination = buffer.GetSpan(1);
        destination[0] = value;
        buffer.Advance(1);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> destination = buffer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        buffer.Advance(sizeof(uint));
    }

    public void WriteUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteBytes(Encoding.UTF8.GetBytes(value));
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32(checked((uint)value.Length));
        WriteRaw(value);
    }

    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    public byte[] ToArray() => buffer.WrittenSpan.ToArray();
}
