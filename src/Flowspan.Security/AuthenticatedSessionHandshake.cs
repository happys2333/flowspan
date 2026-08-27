using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Security;

public sealed class SessionHandshakeHello
{
    public const int NonceLength = 32;
    private readonly byte[] ephemeralPublicKey;
    private readonly byte[] nonce;

    private SessionHandshakeHello(
        SecureSessionRole role,
        DeviceId deviceId,
        string identityFingerprint,
        ImmutableArray<ProtocolVersion> protocolVersions,
        byte[] ephemeralPublicKey,
        byte[] nonce)
    {
        Role = role;
        DeviceId = deviceId;
        IdentityFingerprint = identityFingerprint;
        ProtocolVersions = protocolVersions;
        this.ephemeralPublicKey = ephemeralPublicKey;
        this.nonce = nonce;
    }

    public SecureSessionRole Role { get; }

    public DeviceId DeviceId { get; }

    public string IdentityFingerprint { get; }

    public ImmutableArray<ProtocolVersion> ProtocolVersions { get; }

    public static SessionHandshakeHello Create(
        SecureSessionRole role,
        PublicDeviceIdentity identity,
        IEnumerable<ProtocolVersion> protocolVersions,
        ReadOnlySpan<byte> ephemeralPublicKey,
        ReadOnlySpan<byte> nonce)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown handshake role.");
        }

        ArgumentNullException.ThrowIfNull(identity);
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
                "A session hello must contain 1 to 16 initialized protocol versions.",
                nameof(protocolVersions));
        }

        EphemeralKeyAgreement.ValidateSubjectPublicKeyInfo(ephemeralPublicKey);
        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException(
                $"A session hello nonce must contain exactly {NonceLength} bytes.",
                nameof(nonce));
        }

        return new SessionHandshakeHello(
            role,
            identity.DeviceId,
            identity.Fingerprint,
            versions,
            ephemeralPublicKey.ToArray(),
            nonce.ToArray());
    }

    public byte[] ExportEphemeralPublicKey() => (byte[])ephemeralPublicKey.Clone();

    public byte[] ExportNonce() => (byte[])nonce.Clone();

    internal ReadOnlySpan<byte> EphemeralPublicKey => ephemeralPublicKey;

    internal ReadOnlySpan<byte> Nonce => nonce;

    internal bool MatchesIdentity(PublicDeviceIdentity identity) =>
        DeviceId == identity.DeviceId
        && StringComparer.Ordinal.Equals(IdentityFingerprint, identity.Fingerprint);
}

public sealed class SessionHandshakeTranscript
{
    private static readonly byte[] Context = Encoding.ASCII.GetBytes(
        "FLOWSPAN-HANDSHAKE-V1");
    private readonly byte[] encoded;
    private readonly byte[] hash;

    private SessionHandshakeTranscript(
        SessionHandshakeHello initiator,
        SessionHandshakeHello responder,
        ProtocolVersion protocolVersion,
        byte[] encoded,
        byte[] hash)
    {
        Initiator = initiator;
        Responder = responder;
        ProtocolVersion = protocolVersion;
        this.encoded = encoded;
        this.hash = hash;
    }

    public SessionHandshakeHello Initiator { get; }

    public SessionHandshakeHello Responder { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public static SessionHandshakeTranscript Create(
        SessionHandshakeHello initiator,
        SessionHandshakeHello responder)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        ArgumentNullException.ThrowIfNull(responder);
        if (initiator.Role != SecureSessionRole.Initiator)
        {
            throw new ArgumentException(
                "The first session hello must have the initiator role.",
                nameof(initiator));
        }

        if (responder.Role != SecureSessionRole.Responder)
        {
            throw new ArgumentException(
                "The second session hello must have the responder role.",
                nameof(responder));
        }

        if (initiator.DeviceId == responder.DeviceId)
        {
            throw new ArgumentException(
                "A device cannot establish a session with itself.",
                nameof(responder));
        }

        ProtocolNegotiationResult negotiation = ProtocolNegotiator.Negotiate(
            initiator.ProtocolVersions,
            responder.ProtocolVersions);
        if (!negotiation.Succeeded)
        {
            throw new SessionHandshakeException(
                SessionHandshakeFailure.NoCommonProtocolVersion,
                "The peers do not support a common protocol version.");
        }

        var writer = new SessionHandshakeBuffer();
        writer.WriteRaw(Context);
        writer.WriteUInt32(checked((uint)negotiation.Version.Major));
        writer.WriteUInt32(checked((uint)negotiation.Version.Minor));
        WriteHello(writer, initiator);
        WriteHello(writer, responder);
        byte[] encoded = writer.ToArray();
        byte[] hash = SHA256.HashData(encoded);
        return new SessionHandshakeTranscript(
            initiator,
            responder,
            negotiation.Version,
            encoded,
            hash);
    }

    public byte[] ExportEncoded() => (byte[])encoded.Clone();

    public byte[] ExportHash() => (byte[])hash.Clone();

    internal byte[] Sign(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Initiator.MatchesIdentity(identity.PublicIdentity)
            && !Responder.MatchesIdentity(identity.PublicIdentity))
        {
            throw new InvalidOperationException(
                "Only an identity bound to the session transcript can sign it.");
        }

        return identity.SignHash(hash);
    }

    internal bool VerifySignature(
        PublicDeviceIdentity identity,
        ReadOnlySpan<byte> signature) =>
        (Initiator.MatchesIdentity(identity) || Responder.MatchesIdentity(identity))
        && identity.VerifyHash(hash, signature);

    private static void WriteHello(
        SessionHandshakeBuffer writer,
        SessionHandshakeHello hello)
    {
        writer.WriteByte(hello.Role switch
        {
            SecureSessionRole.Initiator => 1,
            SecureSessionRole.Responder => 2,
            _ => throw new InvalidOperationException("Unknown session handshake role."),
        });
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
    }
}

public sealed class SessionHandshakeAuthentication
{
    public const int SignatureLength = 64;
    private readonly byte[] signature;
    private readonly byte[] transcriptHash;

    private SessionHandshakeAuthentication(
        SecureSessionRole role,
        byte[] transcriptHash,
        byte[] signature)
    {
        Role = role;
        this.transcriptHash = transcriptHash;
        this.signature = signature;
    }

    public SecureSessionRole Role { get; }

    public static SessionHandshakeAuthentication Create(
        SessionHandshakeTranscript transcript,
        DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(identity);
        SecureSessionRole role = transcript.Initiator.MatchesIdentity(
            identity.PublicIdentity)
                ? SecureSessionRole.Initiator
                : transcript.Responder.MatchesIdentity(identity.PublicIdentity)
                    ? SecureSessionRole.Responder
                    : throw new InvalidOperationException(
                        "Only an identity bound to the session transcript can authenticate it.");
        return new SessionHandshakeAuthentication(
            role,
            transcript.ExportHash(),
            transcript.Sign(identity));
    }

    public byte[] ExportTranscriptHash() => (byte[])transcriptHash.Clone();

    public byte[] ExportSignature() => (byte[])signature.Clone();

    internal ReadOnlySpan<byte> Signature => signature;

    internal ReadOnlySpan<byte> TranscriptHash => transcriptHash;

    internal static SessionHandshakeAuthentication Import(
        SecureSessionRole role,
        ReadOnlySpan<byte> transcriptHash,
        ReadOnlySpan<byte> signature)
    {
        if (!Enum.IsDefined(role))
        {
            throw new InvalidDataException("The session authentication role is unknown.");
        }

        if (transcriptHash.Length != SHA256.HashSizeInBytes
            || signature.Length != SignatureLength)
        {
            throw new InvalidDataException(
                "The session authentication hash or signature length is invalid.");
        }

        return new SessionHandshakeAuthentication(
            role,
            transcriptHash.ToArray(),
            signature.ToArray());
    }

    internal bool Verify(
        SessionHandshakeTranscript transcript,
        SecureSessionRole expectedRole,
        PublicDeviceIdentity expectedIdentity)
    {
        if (Role != expectedRole)
        {
            return false;
        }

        byte[] expectedHash = transcript.ExportHash();
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                    transcriptHash,
                    expectedHash)
                && transcript.VerifySignature(expectedIdentity, signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }
}

public sealed class SessionHandshakeFinished
{
    public const int SessionIdentifierLength = 16;
    public const int TranscriptHashLength = SHA256.HashSizeInBytes;
    private readonly byte[] sessionIdentifier;
    private readonly byte[] transcriptHash;

    private SessionHandshakeFinished(
        SecureSessionRole role,
        byte[] transcriptHash,
        byte[] sessionIdentifier)
    {
        Role = role;
        this.transcriptHash = transcriptHash;
        this.sessionIdentifier = sessionIdentifier;
    }

    public SecureSessionRole Role { get; }

    public static SessionHandshakeFinished Create(
        SecureSessionRole role,
        ReadOnlySpan<byte> transcriptHash,
        ReadOnlySpan<byte> sessionIdentifier)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unknown Finished role.");
        }

        if (transcriptHash.Length != TranscriptHashLength)
        {
            throw new ArgumentException(
                $"A Finished transcript hash must contain {TranscriptHashLength} bytes.",
                nameof(transcriptHash));
        }

        if (sessionIdentifier.Length != SessionIdentifierLength)
        {
            throw new ArgumentException(
                $"A Finished session identifier must contain {SessionIdentifierLength} bytes.",
                nameof(sessionIdentifier));
        }

        return new SessionHandshakeFinished(
            role,
            transcriptHash.ToArray(),
            sessionIdentifier.ToArray());
    }

    public bool Matches(
        SecureSessionRole expectedRole,
        ReadOnlySpan<byte> expectedTranscriptHash,
        ReadOnlySpan<byte> expectedSessionIdentifier)
    {
        if (Role != expectedRole
            || expectedTranscriptHash.Length != TranscriptHashLength
            || expectedSessionIdentifier.Length != SessionIdentifierLength)
        {
            return false;
        }

        bool transcriptMatches = CryptographicOperations.FixedTimeEquals(
            transcriptHash,
            expectedTranscriptHash);
        bool sessionMatches = CryptographicOperations.FixedTimeEquals(
            sessionIdentifier,
            expectedSessionIdentifier);
        return transcriptMatches & sessionMatches;
    }

    internal ReadOnlySpan<byte> SessionIdentifier => sessionIdentifier;

    internal ReadOnlySpan<byte> TranscriptHash => transcriptHash;

    internal static SessionHandshakeFinished Import(
        SecureSessionRole role,
        ReadOnlySpan<byte> transcriptHash,
        ReadOnlySpan<byte> sessionIdentifier)
    {
        try
        {
            return Create(role, transcriptHash, sessionIdentifier);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The Finished binding lengths are invalid.",
                exception);
        }
    }
}

public enum SessionHandshakeFailure
{
    NoCommonProtocolVersion,
    LocalIdentityMismatch,
    PeerIdentityChanged,
    InvalidPeerSignature,
    InvalidPeerFinished,
    EphemeralKeyMismatch,
    PeerNotTrusted,
}

public sealed class SessionHandshakeException : CryptographicException
{
    public SessionHandshakeException(
        SessionHandshakeFailure failure,
        string message)
        : base(message) => Failure = failure;

    public SessionHandshakeException(
        SessionHandshakeFailure failure,
        string message,
        Exception innerException)
        : base(message, innerException) => Failure = failure;

    public SessionHandshakeFailure Failure { get; }
}

public sealed class AuthenticatedSession : IDisposable
{
    private int disposed;
    private SecureFrameSession? remoteWindowMediaFrames;

    internal AuthenticatedSession(
        ProtocolVersion protocolVersion,
        PublicDeviceIdentity peerIdentity,
        SecureFrameSession secureFrames,
        SecureFrameSession? remoteWindowMediaFrames)
    {
        ProtocolVersion = protocolVersion;
        PeerIdentity = peerIdentity;
        SecureFrames = secureFrames;
        this.remoteWindowMediaFrames = remoteWindowMediaFrames;
    }

    public ProtocolVersion ProtocolVersion { get; }

    public PublicDeviceIdentity PeerIdentity { get; }

    internal SecureFrameSession? RemoteWindowMediaFrames =>
        Volatile.Read(ref remoteWindowMediaFrames);

    public SecureFrameSession SecureFrames { get; }

    internal SecureFrameSession TakeRemoteWindowMediaFrames()
    {
        if (!ProtocolFeatures.SupportsRemoteWindowMediaRoute(ProtocolVersion))
        {
            throw new InvalidOperationException(
                $"Remote Window media-session transfer requires protocol {ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion} or later.");
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        SecureFrameSession? transferred = Interlocked.Exchange(
            ref remoteWindowMediaFrames,
            null);
        if (transferred is null)
        {
            throw new InvalidOperationException(
                "The Remote Window media session has already been transferred.");
        }

        return transferred;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        SecureFrameSession? media = Interlocked.Exchange(
            ref remoteWindowMediaFrames,
            null);
        try
        {
            SecureFrames.Dispose();
        }
        finally
        {
            media?.Dispose();
        }
    }
}

public static class AuthenticatedSessionHandshake
{
    public static AuthenticatedSession Complete(
        SessionHandshakeTranscript transcript,
        SecureSessionRole localRole,
        PublicDeviceIdentity localIdentity,
        PublicDeviceIdentity trustedPeerIdentity,
        EphemeralKeyAgreement localKeyAgreement,
        SessionHandshakeAuthentication peerAuthentication) => Complete(
            transcript,
            localRole,
            localIdentity,
            trustedPeerIdentity,
            localKeyAgreement,
            peerAuthentication,
            remoteWindowMediaUsageLimits: null);

    internal static AuthenticatedSession Complete(
        SessionHandshakeTranscript transcript,
        SecureSessionRole localRole,
        PublicDeviceIdentity localIdentity,
        PublicDeviceIdentity trustedPeerIdentity,
        EphemeralKeyAgreement localKeyAgreement,
        SessionHandshakeAuthentication peerAuthentication,
        SecureFrameSessionUsageLimits? remoteWindowMediaUsageLimits)
    {
        ArgumentNullException.ThrowIfNull(peerAuthentication);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustedPeerIdentity);
        ArgumentNullException.ThrowIfNull(localKeyAgreement);
        SecureSessionRole peerRole = localRole switch
        {
            SecureSessionRole.Initiator => SecureSessionRole.Responder,
            SecureSessionRole.Responder => SecureSessionRole.Initiator,
            _ => throw new ArgumentOutOfRangeException(
                nameof(localRole),
                localRole,
                "Unknown secure session role."),
        };
        SessionHandshakeHello localHello = localRole == SecureSessionRole.Initiator
            ? transcript.Initiator
            : transcript.Responder;
        SessionHandshakeHello peerHello = localRole == SecureSessionRole.Initiator
            ? transcript.Responder
            : transcript.Initiator;
        if (!localHello.MatchesIdentity(localIdentity))
        {
            throw new SessionHandshakeException(
                SessionHandshakeFailure.LocalIdentityMismatch,
                "The local identity does not match the session transcript.");
        }

        if (!peerHello.MatchesIdentity(trustedPeerIdentity))
        {
            throw new SessionHandshakeException(
                SessionHandshakeFailure.PeerIdentityChanged,
                "The peer identity does not match the trusted identity.");
        }

        if (!peerAuthentication.Verify(
            transcript,
            peerRole,
            trustedPeerIdentity))
        {
            throw new SessionHandshakeException(
                SessionHandshakeFailure.InvalidPeerSignature,
                "The peer did not authenticate the session transcript.");
        }

        byte[] localPublicKey = localKeyAgreement.ExportSubjectPublicKeyInfo();
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                localHello.EphemeralPublicKey,
                localPublicKey))
            {
                throw new SessionHandshakeException(
                    SessionHandshakeFailure.EphemeralKeyMismatch,
                    "The local ephemeral key does not match the session transcript.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localPublicKey);
        }

        byte[] sharedSecret = localKeyAgreement.DeriveRawSecret(
            peerHello.EphemeralPublicKey);
        byte[] transcriptHash = transcript.ExportHash();
        try
        {
            using SecureSessionKeyMaterial material = SecureSessionKeyMaterial.Derive(
                sharedSecret,
                transcriptHash);
            SecureFrameSession secureFrames = material.CreateSession(localRole);
            SecureFrameSession? remoteWindowMediaFrames = null;
            try
            {
                if (ProtocolFeatures.SupportsRemoteWindow(transcript.ProtocolVersion))
                {
                    using SecureSessionKeyMaterial mediaMaterial =
                        SecureSessionKeyMaterial.DeriveRemoteWindowMedia(
                            sharedSecret,
                            transcriptHash);
                    remoteWindowMediaFrames = remoteWindowMediaUsageLimits is null
                        ? mediaMaterial.CreateSession(localRole)
                        : mediaMaterial.CreateSession(
                            localRole,
                            remoteWindowMediaUsageLimits);
                }

                return new AuthenticatedSession(
                    transcript.ProtocolVersion,
                    trustedPeerIdentity,
                    secureFrames,
                    remoteWindowMediaFrames);
            }
            catch
            {
                secureFrames.Dispose();
                remoteWindowMediaFrames?.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(transcriptHash);
        }
    }
}

internal sealed class SessionHandshakeBuffer
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
