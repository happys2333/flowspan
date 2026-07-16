using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class SecureSessionTests
{
    [Fact]
    public void HkdfMatchesRfc5869Sha256TestCaseOne()
    {
        byte[] inputKeyMaterial = Enumerable.Repeat((byte)0x0b, 22).ToArray();
        byte[] salt = Convert.FromHexString("000102030405060708090A0B0C");
        byte[] info = Convert.FromHexString("F0F1F2F3F4F5F6F7F8F9");
        byte[] expected = Convert.FromHexString(
            "3CB25F25FAACD57A90434F64D0362F2A"
            + "2D2D0A90CF1A5A4C5DB02D56ECC4C5BF"
            + "34007208D5B887185865");

        byte[] actual = HkdfSha256.DeriveKey(
            inputKeyMaterial,
            salt,
            info,
            expected.Length);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IndependentEcdhPartiesDeriveMatchingBidirectionalSessions()
    {
        using SessionPair pair = SessionPair.Create();

        byte[] request = Encoding.UTF8.GetBytes("request");
        byte[] response = Encoding.UTF8.GetBytes("response");
        byte[] requestFrame = pair.Initiator.Encrypt(request);
        byte[] responseFrame = pair.Responder.Encrypt(response);

        Assert.Equal(request, pair.Responder.Decrypt(requestFrame));
        Assert.Equal(response, pair.Initiator.Decrypt(responseFrame));
        Assert.Equal(pair.Initiator.SessionIdentifier, pair.Responder.SessionIdentifier);
        Assert.Equal<ulong>(1, pair.Initiator.NextSendSequence);
        Assert.Equal<ulong>(1, pair.Responder.NextReceiveSequence);
    }

    [Fact]
    public void TamperDoesNotAdvanceReceiveSequence()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] frame = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("secret"));
        byte[] tampered = (byte[])frame.Clone();
        tampered[20] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => pair.Responder.Decrypt(tampered));
        Assert.Equal<ulong>(0, pair.Responder.NextReceiveSequence);
        Assert.Equal("secret", Encoding.UTF8.GetString(pair.Responder.Decrypt(frame)));
    }

    [Fact]
    public void ReplayAndSequenceGapAreRejectedWithoutLosingValidFrames()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] first = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("first"));
        byte[] second = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("second"));

        Assert.Throws<InvalidDataException>(() => pair.Responder.Decrypt(second));
        Assert.Equal("first", Encoding.UTF8.GetString(pair.Responder.Decrypt(first)));
        Assert.Throws<InvalidDataException>(() => pair.Responder.Decrypt(first));
        Assert.Equal("second", Encoding.UTF8.GetString(pair.Responder.Decrypt(second)));
    }

    [Fact]
    public void WrongDirectionSessionCannotAuthenticateFrame()
    {
        using SessionPair pair = SessionPair.Create();
        using SecureFrameSession anotherInitiator = pair.Material.CreateSession(
            SecureSessionRole.Initiator);
        byte[] frame = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("request"));

        Assert.ThrowsAny<CryptographicException>(() => anotherInitiator.Decrypt(frame));
    }

    [Fact]
    public void MalformedLengthAndOversizedPlaintextAreRejected()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] frame = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("request"));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(16), 999);

        Assert.Throws<InvalidDataException>(() => pair.Responder.Decrypt(frame));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Initiator.Encrypt(
            new byte[(256 * 1024) + 1]));
    }

    [Fact]
    public void MaximumPlaintextRoundTrips()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] plaintext = new byte[256 * 1024];
        RandomNumberGenerator.Fill(plaintext);

        byte[] decrypted = pair.Responder.Decrypt(pair.Initiator.Encrypt(plaintext));

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptedFinishedConfirmsDirectionalKeyAtSequenceZero()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] transcriptHash = SHA256.HashData(
            Encoding.UTF8.GetBytes("authenticated handshake transcript"));
        SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
            SecureSessionRole.Initiator,
            transcriptHash,
            pair.Initiator.ExportSessionIdentifier());
        byte[] plaintext = SessionHandshakeWireCodec.EncodeFinished(finished);
        byte[] frame = pair.Initiator.Encrypt(plaintext);
        byte[] tampered = (byte[])frame.Clone();
        tampered[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() =>
            pair.Responder.Decrypt(tampered));
        Assert.Equal<ulong>(0, pair.Responder.NextReceiveSequence);

        SessionHandshakeFinished decoded = SessionHandshakeWireCodec.DecodeFinished(
            pair.Responder.Decrypt(frame));

        Assert.True(decoded.Matches(
            SecureSessionRole.Initiator,
            transcriptHash,
            pair.Responder.ExportSessionIdentifier()));
        Assert.Equal<ulong>(1, pair.Initiator.NextSendSequence);
        Assert.Equal<ulong>(1, pair.Responder.NextReceiveSequence);
    }

    [Fact]
    public void MatchingDirectionalEpochUpdateResetsSequenceAndCarriesTraffic()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] oldFrame = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("before"));
        Assert.Equal("before", Encoding.UTF8.GetString(
            pair.Responder.Decrypt(oldFrame)));

        pair.Initiator.AdvanceSendEpoch(nextEpoch: 2);
        pair.Responder.AdvanceReceiveEpoch(nextEpoch: 2);

        Assert.Equal<uint>(2, pair.Initiator.SendEpoch);
        Assert.Equal<uint>(2, pair.Responder.ReceiveEpoch);
        Assert.Equal<ulong>(0, pair.Initiator.NextSendSequence);
        Assert.Equal<ulong>(0, pair.Responder.NextReceiveSequence);
        byte[] newFrame = pair.Initiator.Encrypt(Encoding.UTF8.GetBytes("after"));
        Assert.Equal("after", Encoding.UTF8.GetString(
            pair.Responder.Decrypt(newFrame)));
        Assert.Throws<InvalidDataException>(() => pair.Responder.Decrypt(oldFrame));
    }

    [Fact]
    public void ProtectedPlaintextByteCountsResetWithDirectionalEpoch()
    {
        using SessionPair pair = SessionPair.Create();
        byte[] plaintext = Encoding.UTF8.GetBytes("before");
        byte[] frame = pair.Initiator.Encrypt(plaintext);
        _ = pair.Responder.Decrypt(frame);

        Assert.Equal<ulong>((ulong)plaintext.Length, pair.Initiator.SendPlaintextBytes);
        Assert.Equal<ulong>((ulong)plaintext.Length, pair.Responder.ReceivePlaintextBytes);

        pair.Initiator.AdvanceSendEpoch(nextEpoch: 2);
        pair.Responder.AdvanceReceiveEpoch(nextEpoch: 2);

        Assert.Equal<ulong>(0, pair.Initiator.SendPlaintextBytes);
        Assert.Equal<ulong>(0, pair.Responder.ReceivePlaintextBytes);
    }

    [Fact]
    public void HardPerEpochFrameBoundCannotWrapAndResetsAfterRekey()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateBoundedSessions(maximumFramesPerEpoch: 2);
        using (initiator)
        using (responder)
        {
            byte[] first = initiator.Encrypt("first"u8);
            byte[] second = initiator.Encrypt("second"u8);
            Assert.Equal("first"u8.ToArray(), responder.Decrypt(first));
            Assert.Equal("second"u8.ToArray(), responder.Decrypt(second));
            Assert.Throws<CryptographicException>(() =>
                initiator.Encrypt("over-limit"u8));

            initiator.AdvanceSendEpoch(nextEpoch: 2);
            responder.AdvanceReceiveEpoch(nextEpoch: 2);

            byte[] after = initiator.Encrypt("after"u8);
            Assert.Equal("after"u8.ToArray(), responder.Decrypt(after));
        }
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateBoundedSessions(ulong maximumFramesPerEpoch)
    {
        byte[] firstKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] secondKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x33, 16).ToArray();
        return (
            new SecureFrameSession(
                firstKey,
                SecureFrameDirection.InitiatorToResponder,
                secondKey,
                SecureFrameDirection.ResponderToInitiator,
                sessionIdentifier,
                maximumFramesPerEpoch),
            new SecureFrameSession(
                secondKey,
                SecureFrameDirection.ResponderToInitiator,
                firstKey,
                SecureFrameDirection.InitiatorToResponder,
                sessionIdentifier,
                maximumFramesPerEpoch));
    }

    private sealed class SessionPair : IDisposable
    {
        private SessionPair(
            SecureSessionKeyMaterial material,
            SecureFrameSession initiator,
            SecureFrameSession responder)
        {
            Material = material;
            Initiator = initiator;
            Responder = responder;
        }

        public SecureSessionKeyMaterial Material { get; }

        public SecureFrameSession Initiator { get; }

        public SecureFrameSession Responder { get; }

        public static SessionPair Create()
        {
            using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
            using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
            byte[] initiatorSecret = initiatorAgreement.DeriveRawSecret(
                responderAgreement.ExportSubjectPublicKeyInfo());
            byte[] responderSecret = responderAgreement.DeriveRawSecret(
                initiatorAgreement.ExportSubjectPublicKeyInfo());
            try
            {
                Assert.Equal(initiatorSecret, responderSecret);
                byte[] transcriptHash = SHA256.HashData(
                    Encoding.UTF8.GetBytes("authenticated handshake transcript"));
                SecureSessionKeyMaterial material = SecureSessionKeyMaterial.Derive(
                    initiatorSecret,
                    transcriptHash);
                return new SessionPair(
                    material,
                    material.CreateSession(SecureSessionRole.Initiator),
                    material.CreateSession(SecureSessionRole.Responder));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(initiatorSecret);
                CryptographicOperations.ZeroMemory(responderSecret);
            }
        }

        public void Dispose()
        {
            Initiator.Dispose();
            Responder.Dispose();
            Material.Dispose();
        }
    }
}
