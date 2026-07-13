using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class PairingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TranscriptAndSasAreDeterministicAndRoleBound()
    {
        using var identities = new IdentityPair();
        PairingParty initiator = identities.CreateInitiatorParty();
        PairingParty responder = identities.CreateResponderParty();

        PairingTranscript first = PairingTranscript.Create(
            initiator,
            responder,
            new ProtocolVersion(1, 0));
        PairingTranscript repeated = PairingTranscript.Create(
            initiator,
            responder,
            new ProtocolVersion(1, 0));
        PairingTranscript rolesReversed = PairingTranscript.Create(
            responder,
            initiator,
            new ProtocolVersion(1, 0));

        Assert.Equal(first.ExportEncoded(), repeated.ExportEncoded());
        Assert.Equal(first.ExportHash(), repeated.ExportHash());
        Assert.Equal(first.ShortAuthenticationString, repeated.ShortAuthenticationString);
        Assert.Equal(6, first.ShortAuthenticationString.Length);
        Assert.All(first.ShortAuthenticationString, static character => Assert.True(char.IsAsciiDigit(character)));
        Assert.NotEqual(first.ExportHash(), rolesReversed.ExportHash());
        Assert.NotEqual(first.ShortAuthenticationString, rolesReversed.ShortAuthenticationString);
    }

    [Fact]
    public void ValidDualConfirmationCreatesDirectionalTrustRecords()
    {
        using var identities = new IdentityPair();
        PairingTranscript transcript = identities.CreateTranscript();
        byte[] initiatorSignature = transcript.Sign(identities.Initiator);
        byte[] responderSignature = transcript.Sign(identities.Responder);
        PairingConfirmation initiatorConfirmation = PairingConfirmation.Create(
            identities.Initiator,
            transcript,
            accepted: true);
        PairingConfirmation responderConfirmation = PairingConfirmation.Create(
            identities.Responder,
            transcript,
            accepted: true);

        PairingOutcome outcome = PairingVerifier.EstablishTrust(
            transcript,
            initiatorSignature,
            responderSignature,
            initiatorConfirmation,
            responderConfirmation,
            CapabilityGrant.Of(Capability.ActivityReceive),
            CapabilityGrant.Of(Capability.MirrorView),
            Now,
            Now.AddMinutes(1));

        Assert.True(outcome.Succeeded);
        Assert.Equal(identities.Responder.DeviceId, outcome.InitiatorTrust!.PeerIdentity.DeviceId);
        Assert.True(outcome.InitiatorTrust.GrantedCapabilities.Allows(
            Capability.ActivityReceive));
        Assert.Equal(identities.Initiator.DeviceId, outcome.ResponderTrust!.PeerIdentity.DeviceId);
        Assert.True(outcome.ResponderTrust.GrantedCapabilities.Allows(Capability.MirrorView));
    }

    [Fact]
    public void RejectionAndTimeoutCreateNoTrustRecord()
    {
        using var identities = new IdentityPair();
        PairingTranscript transcript = identities.CreateTranscript();
        byte[] initiatorSignature = transcript.Sign(identities.Initiator);
        byte[] responderSignature = transcript.Sign(identities.Responder);
        PairingConfirmation accepted = PairingConfirmation.Create(
            identities.Initiator,
            transcript,
            accepted: true);
        PairingConfirmation rejected = PairingConfirmation.Create(
            identities.Responder,
            transcript,
            accepted: false);

        PairingOutcome rejection = PairingVerifier.EstablishTrust(
            transcript,
            initiatorSignature,
            responderSignature,
            accepted,
            rejected,
            CapabilityGrant.None,
            CapabilityGrant.None,
            Now,
            Now.AddMinutes(1));
        PairingOutcome timeout = PairingVerifier.EstablishTrust(
            transcript,
            initiatorSignature,
            responderSignature,
            accepted,
            PairingConfirmation.Create(identities.Responder, transcript, accepted: true),
            CapabilityGrant.None,
            CapabilityGrant.None,
            Now.AddMinutes(1),
            Now.AddMinutes(1));

        Assert.Equal(PairingFailure.Rejected, rejection.Failure);
        Assert.Null(rejection.InitiatorTrust);
        Assert.Null(rejection.ResponderTrust);
        Assert.Equal(PairingFailure.Timeout, timeout.Failure);
        Assert.Null(timeout.InitiatorTrust);
    }

    [Fact]
    public void AlteredTranscriptSignatureAndSubstitutedIdentityAreRejected()
    {
        using var identities = new IdentityPair();
        PairingTranscript original = identities.CreateTranscript();
        PairingTranscript altered = PairingTranscript.Create(
            identities.CreateInitiatorParty(nonceByte: 0x33),
            identities.CreateResponderParty(),
            new ProtocolVersion(1, 0));
        byte[] originalInitiatorSignature = original.Sign(identities.Initiator);
        byte[] originalResponderSignature = original.Sign(identities.Responder);

        PairingOutcome alteredOutcome = PairingVerifier.EstablishTrust(
            altered,
            originalInitiatorSignature,
            originalResponderSignature,
            PairingConfirmation.Create(identities.Initiator, altered, accepted: true),
            PairingConfirmation.Create(identities.Responder, altered, accepted: true),
            CapabilityGrant.None,
            CapabilityGrant.None,
            Now,
            Now.AddMinutes(1));

        using DeviceIdentity stranger = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Stranger");
        PairingOutcome substitution = PairingVerifier.EstablishTrust(
            original,
            originalInitiatorSignature,
            originalResponderSignature,
            PairingConfirmation.Create(stranger, original, accepted: true),
            PairingConfirmation.Create(identities.Responder, original, accepted: true),
            CapabilityGrant.None,
            CapabilityGrant.None,
            Now,
            Now.AddMinutes(1));

        Assert.Equal(PairingFailure.InvalidTranscriptSignature, alteredOutcome.Failure);
        Assert.Equal(PairingFailure.InvalidConfirmation, substitution.Failure);
    }

    private sealed class IdentityPair : IDisposable
    {
        public IdentityPair()
        {
            Initiator = DeviceIdentity.Generate(
                DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
                "Laptop");
            Responder = DeviceIdentity.Generate(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Desk");
        }

        public DeviceIdentity Initiator { get; }

        public DeviceIdentity Responder { get; }

        public PairingParty CreateInitiatorParty(byte nonceByte = 0x11) => new(
            Initiator.PublicIdentity,
            Enumerable.Repeat(nonceByte, PairingParty.NonceLength)
                .Select(static value => (byte)value)
                .ToArray());

        public PairingParty CreateResponderParty() => new(
            Responder.PublicIdentity,
            Enumerable.Repeat((byte)0x22, PairingParty.NonceLength).ToArray());

        public PairingTranscript CreateTranscript() => PairingTranscript.Create(
            CreateInitiatorParty(),
            CreateResponderParty(),
            new ProtocolVersion(1, 0));

        public void Dispose()
        {
            Initiator.Dispose();
            Responder.Dispose();
        }
    }
}
