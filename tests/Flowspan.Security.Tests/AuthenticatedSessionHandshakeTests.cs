using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class AuthenticatedSessionHandshakeTests
{
    [Fact]
    public void MediaSessionCannotBeBorrowedThroughPublicApi()
    {
        Assert.Null(typeof(AuthenticatedSession).GetProperty(
            "RemoteWindowMediaFrames",
            BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(AuthenticatedSession).GetMethod(
            "TakeRemoteWindowMediaFrames",
            BindingFlags.Instance | BindingFlags.Public));
    }

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
        Assert.Null(initiator.RemoteWindowMediaFrames);
        Assert.Null(responder.RemoteWindowMediaFrames);
        Assert.Equal(
            "authenticated request",
            Encoding.UTF8.GetString(responder.SecureFrames.Decrypt(request)));
        Assert.Equal(
            "authenticated response",
            Encoding.UTF8.GetString(initiator.SecureFrames.Decrypt(response)));
    }

    [Fact]
    public void ProtocolOnePointFiveDerivesPurposeSeparatedRemoteWindowMediaSessions()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            CreateHello(
                initiatorIdentity,
                initiatorAgreement,
                SecureSessionRole.Initiator,
                nonceByte: 0x11,
                [ProtocolFeatures.RemoteWindowMinimumVersion]),
            CreateHello(
                responderIdentity,
                responderAgreement,
                SecureSessionRole.Responder,
                nonceByte: 0x22,
                [ProtocolFeatures.RemoteWindowMinimumVersion]));
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
        SecureFrameSession initiatorMedia = Assert.IsType<SecureFrameSession>(
            initiator.RemoteWindowMediaFrames);
        SecureFrameSession responderMedia = Assert.IsType<SecureFrameSession>(
            responder.RemoteWindowMediaFrames);
        byte[] plaintext = Encoding.UTF8.GetBytes("purpose-separated-media");

        byte[] controlCiphertext = initiator.SecureFrames.Encrypt(plaintext);
        byte[] mediaCiphertext = initiatorMedia.Encrypt(plaintext);

        Assert.NotEqual(
            initiator.SecureFrames.SessionIdentifier,
            initiatorMedia.SessionIdentifier);
        Assert.False(controlCiphertext.AsSpan().SequenceEqual(mediaCiphertext));
        Assert.ThrowsAny<CryptographicException>(() =>
            responderMedia.Decrypt(controlCiphertext));
        Assert.Equal(plaintext, responder.SecureFrames.Decrypt(controlCiphertext));
        Assert.Equal(plaintext, responderMedia.Decrypt(mediaCiphertext));

        byte[] intact = initiatorMedia.Encrypt(plaintext);
        byte[] tampered = intact.ToArray();
        tampered[^1] ^= 0xff;
        Assert.ThrowsAny<CryptographicException>(() => responderMedia.Decrypt(tampered));
        Assert.Equal(plaintext, responderMedia.Decrypt(intact));
    }

    [Fact]
    public void ProtocolOnePointFiveRetainsMediaSessionOwnership()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        SecureFrameSession control = CreateSecureSession(0x41);
        SecureFrameSession media = CreateSecureSession(0x42);
        var authenticated = new AuthenticatedSession(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            peer.PublicIdentity,
            control,
            media);

        Assert.Throws<InvalidOperationException>(() =>
            authenticated.TakeRemoteWindowMediaFrames());

        authenticated.Dispose();
        Assert.Throws<ObjectDisposedException>(() => media.Encrypt([0x01]));
    }

    [Fact]
    public void ProtocolOnePointSixTransfersMediaSessionOwnershipExactlyOnce()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        SecureFrameSession control = CreateSecureSession(0x51);
        SecureFrameSession media = CreateSecureSession(0x52);
        var authenticated = new AuthenticatedSession(
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            peer.PublicIdentity,
            control,
            media);

        SecureFrameSession transferred =
            authenticated.TakeRemoteWindowMediaFrames();
        Assert.Same(media, transferred);
        Assert.Null(authenticated.RemoteWindowMediaFrames);
        Assert.Throws<InvalidOperationException>(() =>
            authenticated.TakeRemoteWindowMediaFrames());

        authenticated.Dispose();
        byte[] encrypted = transferred.Encrypt([0x01]);
        Assert.NotEmpty(encrypted);
        transferred.Dispose();
        Assert.Throws<ObjectDisposedException>(() => transferred.Encrypt([0x02]));
    }

    [Fact]
    public async Task TakeAndDisposeRaceHasExactlyOneMediaSessionOwner()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        for (var iteration = 0; iteration < 256; iteration++)
        {
            SecureFrameSession control = CreateSecureSession(0x53);
            SecureFrameSession media = CreateSecureSession(0x54);
            var authenticated = new AuthenticatedSession(
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                peer.PublicIdentity,
                control,
                media);
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SecureFrameSession? transferred = null;
            Exception? transferFailure = null;
            Task taking = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    transferred = authenticated.TakeRemoteWindowMediaFrames();
                }
                catch (Exception failure)
                    when (failure is ObjectDisposedException
                        or InvalidOperationException)
                {
                    transferFailure = failure;
                }
            });
            Task disposing = Task.Run(async () =>
            {
                await start.Task;
                authenticated.Dispose();
            });

            start.TrySetResult();
            await Task.WhenAll(taking, disposing);
            try
            {
                Assert.Throws<ObjectDisposedException>(() =>
                    control.Encrypt([0x01]));
                if (transferred is null)
                {
                    Assert.NotNull(transferFailure);
                    Assert.Throws<ObjectDisposedException>(() =>
                        media.Encrypt([0x02]));
                }
                else
                {
                    Assert.Null(transferFailure);
                    Assert.Same(media, transferred);
                    Assert.NotEmpty(transferred.Encrypt([0x03]));
                }
            }
            finally
            {
                transferred?.Dispose();
                authenticated.Dispose();
            }
        }
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

    private static SecureFrameSession CreateSecureSession(byte seed)
    {
        byte[] secret = Enumerable.Repeat(seed, 32).ToArray();
        byte[] transcriptHash = SHA256.HashData([seed]);
        using SecureSessionKeyMaterial material = SecureSessionKeyMaterial.Derive(
            secret,
            transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return material.CreateSession(SecureSessionRole.Initiator);
    }
}
