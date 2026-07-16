using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class AuthenticatedSessionHandshakeTests
{
    [Fact]
    public void TrustedPartiesDeriveAuthenticatedBidirectionalSession()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello initiatorHello = CreateHello(
            initiatorIdentity,
            initiatorAgreement,
            SecureSessionRole.Initiator,
            nonceByte: 0x11,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        SessionHandshakeHello responderHello = CreateHello(
            responderIdentity,
            responderAgreement,
            SecureSessionRole.Responder,
            nonceByte: 0x22,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            initiatorHello,
            responderHello);
        SessionHandshakeAuthentication initiatorAuthentication =
            SessionHandshakeAuthentication.Create(transcript, initiatorIdentity);
        SessionHandshakeAuthentication responderAuthentication =
            SessionHandshakeAuthentication.Create(transcript, responderIdentity);

        using AuthenticatedSession initiator = AuthenticatedSessionHandshake.Complete(
            transcript,
            SecureSessionRole.Initiator,
            initiatorIdentity.PublicIdentity,
            responderIdentity.PublicIdentity,
            initiatorAgreement,
            responderAuthentication);
        using AuthenticatedSession responder = AuthenticatedSessionHandshake.Complete(
            transcript,
            SecureSessionRole.Responder,
            responderIdentity.PublicIdentity,
            initiatorIdentity.PublicIdentity,
            responderAgreement,
            initiatorAuthentication);
        byte[] request = initiator.SecureFrames.Encrypt(
            Encoding.UTF8.GetBytes("authenticated request"));
        byte[] response = responder.SecureFrames.Encrypt(
            Encoding.UTF8.GetBytes("authenticated response"));

        Assert.Equal(new ProtocolVersion(1, 1), initiator.ProtocolVersion);
        Assert.Equal(initiator.ProtocolVersion, responder.ProtocolVersion);
        Assert.Equal(
            "authenticated request",
            Encoding.UTF8.GetString(responder.SecureFrames.Decrypt(request)));
        Assert.Equal(
            "authenticated response",
            Encoding.UTF8.GetString(initiator.SecureFrames.Decrypt(response)));
    }

    [Fact]
    public void TrustedDeviceIdWithSubstitutedKeyIsRejected()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        DeviceId responderId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedResponder = DeviceIdentity.Generate(
            responderId,
            "Desk");
        using DeviceIdentity substitutedResponder = DeviceIdentity.Generate(
            responderId,
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            CreateHello(
                initiatorIdentity,
                initiatorAgreement,
                SecureSessionRole.Initiator,
                0x11,
                [new ProtocolVersion(1, 0)]),
            CreateHello(
                substitutedResponder,
                responderAgreement,
                SecureSessionRole.Responder,
                0x22,
                [new ProtocolVersion(1, 0)]));
        SessionHandshakeAuthentication substitutedAuthentication =
            SessionHandshakeAuthentication.Create(transcript, substitutedResponder);

        SessionHandshakeException exception = Assert.Throws<SessionHandshakeException>(
            () => AuthenticatedSessionHandshake.Complete(
                transcript,
                SecureSessionRole.Initiator,
                initiatorIdentity.PublicIdentity,
                trustedResponder.PublicIdentity,
                initiatorAgreement,
                substitutedAuthentication));

        Assert.Equal(SessionHandshakeFailure.PeerIdentityChanged, exception.Failure);
    }

    [Fact]
    public void SignatureFromDowngradedTranscriptIsRejected()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello initiatorHello = CreateHello(
            initiatorIdentity,
            initiatorAgreement,
            SecureSessionRole.Initiator,
            0x11,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        SessionHandshakeHello responderHello = CreateHello(
            responderIdentity,
            responderAgreement,
            SecureSessionRole.Responder,
            0x22,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        SessionHandshakeTranscript expected = SessionHandshakeTranscript.Create(
            initiatorHello,
            responderHello);
        SessionHandshakeTranscript downgraded = SessionHandshakeTranscript.Create(
            initiatorHello,
            CreateHello(
                responderIdentity,
                responderAgreement,
                SecureSessionRole.Responder,
                0x22,
                [new ProtocolVersion(1, 0)]));
        SessionHandshakeAuthentication downgradedAuthentication =
            SessionHandshakeAuthentication.Create(downgraded, responderIdentity);

        SessionHandshakeException exception = Assert.Throws<SessionHandshakeException>(
            () => AuthenticatedSessionHandshake.Complete(
                expected,
                SecureSessionRole.Initiator,
                initiatorIdentity.PublicIdentity,
                responderIdentity.PublicIdentity,
                initiatorAgreement,
                downgradedAuthentication));

        Assert.Equal(SessionHandshakeFailure.InvalidPeerSignature, exception.Failure);
    }

    [Fact]
    public void NoCommonProtocolVersionHasStructuredFailure()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();

        SessionHandshakeException exception = Assert.Throws<SessionHandshakeException>(
            () => SessionHandshakeTranscript.Create(
                CreateHello(
                    initiatorIdentity,
                    initiatorAgreement,
                    SecureSessionRole.Initiator,
                    0x11,
                    [new ProtocolVersion(1, 0)]),
                CreateHello(
                    responderIdentity,
                    responderAgreement,
                    SecureSessionRole.Responder,
                    0x22,
                    [new ProtocolVersion(2, 0)])));

        Assert.Equal(
            SessionHandshakeFailure.NoCommonProtocolVersion,
            exception.Failure);
    }

    [Fact]
    public void WireMessagesPreserveAuthenticatedTranscript()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello initiatorHello = CreateHello(
            initiatorIdentity,
            initiatorAgreement,
            SecureSessionRole.Initiator,
            0x11,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        SessionHandshakeHello responderHello = CreateHello(
            responderIdentity,
            responderAgreement,
            SecureSessionRole.Responder,
            0x22,
            [new ProtocolVersion(1, 1)]);
        SessionHandshakeTranscript original = SessionHandshakeTranscript.Create(
            initiatorHello,
            responderHello);

        SessionHandshakeHello decodedInitiator = SessionHandshakeWireCodec.DecodeHello(
            SessionHandshakeWireCodec.EncodeHello(initiatorHello),
            initiatorIdentity.PublicIdentity);
        SessionHandshakeHello decodedResponder = SessionHandshakeWireCodec.DecodeHello(
            SessionHandshakeWireCodec.EncodeHello(responderHello),
            responderIdentity.PublicIdentity);
        SessionHandshakeTranscript decodedTranscript = SessionHandshakeTranscript.Create(
            decodedInitiator,
            decodedResponder);
        SessionHandshakeAuthentication authentication =
            SessionHandshakeAuthentication.Create(original, responderIdentity);
        SessionHandshakeAuthentication decodedAuthentication =
            SessionHandshakeWireCodec.DecodeAuthentication(
                SessionHandshakeWireCodec.EncodeAuthentication(authentication));

        using AuthenticatedSession session = AuthenticatedSessionHandshake.Complete(
            decodedTranscript,
            SecureSessionRole.Initiator,
            initiatorIdentity.PublicIdentity,
            responderIdentity.PublicIdentity,
            initiatorAgreement,
            decodedAuthentication);

        Assert.Equal(original.ExportHash(), decodedTranscript.ExportHash());
        Assert.Equal(new ProtocolVersion(1, 1), session.ProtocolVersion);
        Assert.Equal(responderIdentity.DeviceId, session.PeerIdentity.DeviceId);
    }

    [Fact]
    public void FinishedWireFormatIsCanonicalAndPreservesBindings()
    {
        byte[] transcriptHash = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x22, 16).ToArray();
        SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
            SecureSessionRole.Initiator,
            transcriptHash,
            sessionIdentifier);

        byte[] encoded = SessionHandshakeWireCodec.EncodeFinished(finished);
        SessionHandshakeFinished decoded =
            SessionHandshakeWireCodec.DecodeFinished(encoded);

        Assert.Equal(
            Convert.FromHexString(
                "46534831030100000020"
                + "1111111111111111111111111111111111111111111111111111111111111111"
                + "00000010"
                + "22222222222222222222222222222222"),
            encoded);
        Assert.Equal(
            "FD15E6104A00DCB7F7809FE39B71BBB9DA3F673A511DC3EB6F77F7ED7068BDAF",
            Convert.ToHexString(SHA256.HashData(encoded)));
        Assert.True(decoded.Matches(
            SecureSessionRole.Initiator,
            transcriptHash,
            sessionIdentifier));
        Assert.False(decoded.Matches(
            SecureSessionRole.Responder,
            transcriptHash,
            sessionIdentifier));
        byte[] wrongTranscript = (byte[])transcriptHash.Clone();
        wrongTranscript[0] ^= 0x01;
        Assert.False(decoded.Matches(
            SecureSessionRole.Initiator,
            wrongTranscript,
            sessionIdentifier));
        byte[] wrongSession = (byte[])sessionIdentifier.Clone();
        wrongSession[0] ^= 0x01;
        Assert.False(decoded.Matches(
            SecureSessionRole.Initiator,
            transcriptHash,
            wrongSession));
    }

    [Fact]
    public void FinishedWireRejectsNonCanonicalShape()
    {
        SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
            SecureSessionRole.Initiator,
            new byte[SessionHandshakeFinished.TranscriptHashLength],
            new byte[SessionHandshakeFinished.SessionIdentifierLength]);
        byte[] encoded = SessionHandshakeWireCodec.EncodeFinished(finished);
        byte[] trailing = [.. encoded, 0x00];
        byte[] shortHash = (byte[])encoded.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(shortHash.AsSpan(6), 31);
        byte[] unknownRole = (byte[])encoded.Clone();
        unknownRole[5] = 0xff;

        Assert.Throws<InvalidDataException>(() =>
            SessionHandshakeWireCodec.DecodeFinished(trailing));
        Assert.Throws<InvalidDataException>(() =>
            SessionHandshakeWireCodec.DecodeFinished(shortHash));
        Assert.Throws<InvalidDataException>(() =>
            SessionHandshakeWireCodec.DecodeFinished(unknownRole));
    }

    [Fact]
    public void ClaimedHelloDeviceIdDoesNotBypassTrustedKeyValidation()
    {
        DeviceId deviceId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedIdentity = DeviceIdentity.Generate(
            deviceId,
            "Desk");
        using DeviceIdentity substitutedIdentity = DeviceIdentity.Generate(
            deviceId,
            "Desk");
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        byte[] encoded = SessionHandshakeWireCodec.EncodeHello(CreateHello(
            substitutedIdentity,
            agreement,
            SecureSessionRole.Initiator,
            0x11,
            [new ProtocolVersion(1, 0)]));

        DeviceId claimed =
            SessionHandshakeWireCodec.ReadClaimedHelloDeviceId(encoded);
        SessionHandshakeException failure = Assert.Throws<SessionHandshakeException>(
            () => SessionHandshakeWireCodec.DecodeHello(
                encoded,
                trustedIdentity.PublicIdentity));

        Assert.Equal(deviceId, claimed);
        Assert.Equal(SessionHandshakeFailure.PeerIdentityChanged, failure.Failure);
    }

    [Fact]
    public void NonCanonicalWireVersionOrderIsRejected()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello hello = CreateHello(
            identity,
            agreement,
            SecureSessionRole.Initiator,
            0x11,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        byte[] encoded = SessionHandshakeWireCodec.EncodeHello(hello);
        const int firstVersionOffset = 118;
        byte[] firstVersion = encoded.AsSpan(firstVersionOffset, 8).ToArray();
        encoded.AsSpan(firstVersionOffset + 8, 8).CopyTo(
            encoded.AsSpan(firstVersionOffset, 8));
        firstVersion.CopyTo(encoded, firstVersionOffset + 8);

        Assert.Throws<InvalidDataException>(() =>
            SessionHandshakeWireCodec.DecodeHello(
                encoded,
                identity.PublicIdentity));
    }

    [Fact]
    public void InvalidEphemeralKeyWireDataIsRejectedAsMalformed()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello hello = CreateHello(
            identity,
            agreement,
            SecureSessionRole.Initiator,
            0x11,
            [new ProtocolVersion(1, 0)]);
        byte[] encoded = SessionHandshakeWireCodec.EncodeHello(hello);
        const int ephemeralKeyOffset = 130;
        encoded[ephemeralKeyOffset] ^= 0xff;

        Assert.Throws<InvalidDataException>(() =>
            SessionHandshakeWireCodec.DecodeHello(
                encoded,
                identity.PublicIdentity));
    }

    private static SessionHandshakeHello CreateHello(
        DeviceIdentity identity,
        EphemeralKeyAgreement agreement,
        SecureSessionRole role,
        byte nonceByte,
        IEnumerable<ProtocolVersion> versions) => SessionHandshakeHello.Create(
            role,
            identity.PublicIdentity,
            versions,
            agreement.ExportSubjectPublicKeyInfo(),
            Enumerable.Repeat(nonceByte, SessionHandshakeHello.NonceLength)
                .Select(static value => (byte)value)
                .ToArray());
}
