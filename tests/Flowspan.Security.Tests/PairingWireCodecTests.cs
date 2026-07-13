using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class PairingWireCodecTests
{
    private const string GoldenPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4hL+g7t+qOo9wpKA/txIxMZo" +
        "TebBMrU5dohV35yuBIj1MLNpX9FqtUqsS5o/odaOlHvyR8Nse+O7HQJZ1a5+8g==";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HelloRoundTripsCanonicallyWithSortedVersions()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        var party = new PairingParty(
            identity.PublicIdentity,
            Enumerable.Repeat((byte)0x11, PairingParty.NonceLength).ToArray());
        PairingHello hello = PairingHello.Create(
            SecureSessionRole.Initiator,
            party,
            [
                new ProtocolVersion(1, 1),
                new ProtocolVersion(1, 0),
                new ProtocolVersion(1, 1),
            ]);

        byte[] encoded = PairingWireCodec.EncodeHello(hello);
        PairingHello decoded = PairingWireCodec.DecodeHello(encoded);

        Assert.Equal(SecureSessionRole.Initiator, decoded.Role);
        Assert.Equal(identity.DeviceId, decoded.Party.Identity.DeviceId);
        Assert.Equal(identity.DisplayName, decoded.Party.Identity.DisplayName);
        Assert.True(decoded.Party.Identity.HasSameKey(identity.PublicIdentity));
        Assert.Equal(party.ExportNonce(), decoded.Party.ExportNonce());
        Assert.Equal<ProtocolVersion>(
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)],
            decoded.ProtocolVersions);
        Assert.Equal(encoded, PairingWireCodec.EncodeHello(decoded));
    }

    [Fact]
    public void SignatureAndConfirmationRoundTripEstablishTrust()
    {
        using DeviceIdentity initiator = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responder = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        PairingTranscript transcript = CreateTranscript(initiator, responder);
        PairingTranscriptSignature initiatorSignature =
            PairingWireCodec.DecodeTranscriptSignature(
                PairingWireCodec.EncodeTranscriptSignature(
                    PairingTranscriptSignature.Create(transcript, initiator)));
        PairingTranscriptSignature responderSignature =
            PairingWireCodec.DecodeTranscriptSignature(
                PairingWireCodec.EncodeTranscriptSignature(
                    PairingTranscriptSignature.Create(transcript, responder)));
        PairingConfirmation initiatorConfirmation =
            PairingWireCodec.DecodeConfirmation(
                PairingWireCodec.EncodeConfirmation(PairingConfirmation.Create(
                    initiator,
                    transcript,
                    accepted: true)));
        PairingConfirmation responderConfirmation =
            PairingWireCodec.DecodeConfirmation(
                PairingWireCodec.EncodeConfirmation(PairingConfirmation.Create(
                    responder,
                    transcript,
                    accepted: true)));
        PairingCompletionProof completion =
            PairingWireCodec.DecodeCompletionProof(
                PairingWireCodec.EncodeCompletionProof(
                    PairingCompletionProof.Create(responder, transcript)));

        PairingOutcome outcome = PairingVerifier.EstablishTrust(
            transcript,
            initiatorSignature.ExportSignature(),
            responderSignature.ExportSignature(),
            initiatorConfirmation,
            responderConfirmation,
            CapabilityGrant.None,
            CapabilityGrant.None,
            Now,
            Now.AddMinutes(1));

        Assert.True(outcome.Succeeded);
        Assert.Equal(initiator.DeviceId, initiatorSignature.DeviceId);
        Assert.Equal(responder.DeviceId, responderSignature.DeviceId);
        Assert.True(completion.Verify(responder.PublicIdentity, transcript));
    }

    [Fact]
    public void OversizedTrailingAndNonCanonicalHelloMessagesAreRejected()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        PairingHello hello = PairingHello.Create(
            SecureSessionRole.Initiator,
            new PairingParty(
                identity.PublicIdentity,
                new byte[PairingParty.NonceLength]),
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        byte[] canonical = PairingWireCodec.EncodeHello(hello);
        byte[] trailing = [.. canonical, 0x00];
        byte[] duplicateVersion = (byte[])canonical.Clone();
        duplicateVersion.AsSpan(duplicateVersion.Length - 16, 8).CopyTo(
            duplicateVersion.AsSpan(duplicateVersion.Length - 8, 8));

        Assert.Throws<InvalidDataException>(() =>
            PairingWireCodec.DecodeHello(
                new byte[PairingWireCodec.MaximumMessageBytes + 1]));
        Assert.Throws<InvalidDataException>(() =>
            PairingWireCodec.DecodeHello(trailing));
        Assert.Throws<InvalidDataException>(() =>
            PairingWireCodec.DecodeHello(duplicateVersion));
    }

    [Fact]
    public void NonBooleanConfirmationIsRejected()
    {
        using DeviceIdentity initiator = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responder = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        PairingTranscript transcript = CreateTranscript(initiator, responder);
        byte[] encoded = PairingWireCodec.EncodeConfirmation(
            PairingConfirmation.Create(initiator, transcript, accepted: true));
        const int acceptedOffset = 4 + 1 + 4 + 36;
        encoded[acceptedOffset] = 2;

        Assert.Throws<InvalidDataException>(() =>
            PairingWireCodec.DecodeConfirmation(encoded));
    }

    [Fact]
    public void GoldenHelloFixtureFreezesVersionOneWireEncoding()
    {
        var identity = new PublicDeviceIdentity(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Golden",
            Convert.FromBase64String(GoldenPublicKey));
        PairingHello hello = PairingHello.Create(
            SecureSessionRole.Initiator,
            new PairingParty(
                identity,
                Enumerable.Repeat((byte)0x11, PairingParty.NonceLength).ToArray()),
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);

        byte[] encoded = PairingWireCodec.EncodeHello(hello);

        Assert.Equal(207, encoded.Length);
        Assert.Equal(
            "535BD965C1EB2A0B6E83725F9CC5A3E5BD5EB98C4DA783826EB109511082ABE3",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded)));
    }

    [Fact]
    public void SeededHostileMessagesFailWithinTheBoundedCodec()
    {
        var random = new Random(0x5F5A_2026);
        Func<byte[], object>[] decoders =
        [
            static message => PairingWireCodec.DecodeHello(message),
            static message => PairingWireCodec.DecodeTranscriptSignature(message),
            static message => PairingWireCodec.DecodeConfirmation(message),
            static message => PairingWireCodec.DecodeCompletionProof(message),
        ];

        for (int index = 0; index < 512; index++)
        {
            byte[] message = new byte[random.Next(
                PairingWireCodec.MaximumMessageBytes + 257)];
            random.NextBytes(message);
            foreach (Func<byte[], object> decode in decoders)
            {
                Exception? failure = Record.Exception(() => decode(message));
                Assert.IsType<InvalidDataException>(failure);
            }
        }
    }

    private static DeviceIdentity CreateIdentity(string id, string name) =>
        DeviceIdentity.Generate(DeviceId.Parse(id), name);

    private static PairingTranscript CreateTranscript(
        DeviceIdentity initiator,
        DeviceIdentity responder) => PairingTranscript.Create(
        new PairingParty(
            initiator.PublicIdentity,
            Enumerable.Repeat((byte)0x11, PairingParty.NonceLength).ToArray()),
        new PairingParty(
            responder.PublicIdentity,
            Enumerable.Repeat((byte)0x22, PairingParty.NonceLength).ToArray()),
        new ProtocolVersion(1, 0));
}
