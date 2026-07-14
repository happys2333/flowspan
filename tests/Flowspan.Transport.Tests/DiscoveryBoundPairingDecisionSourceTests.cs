using System.Net;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class DiscoveryBoundPairingDecisionSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 13, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId PeerDevice =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task TranscriptIdentityMismatchRejectsBeforeSasDecision()
    {
        using DeviceIdentity advertised = DeviceIdentity.Generate(PeerDevice, "Desk");
        using DeviceIdentity substituted = DeviceIdentity.Generate(PeerDevice, "Desk");
        SignedDiscoveryOffer offer = CreateOffer(advertised);
        var pinned = new UnverifiedPairingCandidate(
            "desk._flowspan._tcp.local",
            offer,
            new IPEndPoint(IPAddress.Parse("192.168.50.20"), offer.Port),
            PairingCandidateTrustState.UnverifiedPairingRequired);
        var inner = new RecordingDecisionSource();
        var source = new DiscoveryBoundPairingDecisionSource(
            pinned,
            inner,
            new FixedTimeProvider(Now));

        PairingDecision decision = await source.DecideAsync(new PairingConfirmationRequest(
            substituted.PublicIdentity,
            new ProtocolVersion(1, 0),
            "123456",
            Now.AddMinutes(1)));

        Assert.False(decision.Accepted);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task ForgedPinnedOfferRejectsBeforeSasDecision()
    {
        using DeviceIdentity advertised = DeviceIdentity.Generate(PeerDevice, "Desk");
        SignedDiscoveryOffer forged = TamperSignature(CreateOffer(advertised));
        var pinned = new UnverifiedPairingCandidate(
            "desk._flowspan._tcp.local",
            forged,
            new IPEndPoint(IPAddress.Parse("192.168.50.20"), forged.Port),
            PairingCandidateTrustState.UnverifiedPairingRequired);
        var inner = new RecordingDecisionSource();
        var source = new DiscoveryBoundPairingDecisionSource(
            pinned,
            inner,
            new FixedTimeProvider(Now));

        PairingDecision decision = await source.DecideAsync(new PairingConfirmationRequest(
            advertised.PublicIdentity,
            new ProtocolVersion(1, 0),
            "123456",
            Now.AddMinutes(1)));

        Assert.False(decision.Accepted);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task ValidPinnedOfferAllowsTheAuthenticatedPeerToReachSasDecision()
    {
        using DeviceIdentity advertised = DeviceIdentity.Generate(PeerDevice, "Desk");
        SignedDiscoveryOffer offer = CreateOffer(advertised);
        var pinned = new UnverifiedPairingCandidate(
            "desk._flowspan._tcp.local",
            offer,
            new IPEndPoint(IPAddress.Parse("192.168.50.20"), offer.Port),
            PairingCandidateTrustState.UnverifiedPairingRequired);
        var inner = new RecordingDecisionSource();
        var source = new DiscoveryBoundPairingDecisionSource(
            pinned,
            inner,
            new FixedTimeProvider(Now));
        var request = new PairingConfirmationRequest(
            advertised.PublicIdentity,
            new ProtocolVersion(1, 0),
            "123456",
            Now.AddMinutes(1));

        PairingDecision decision = await source.DecideAsync(request);

        Assert.True(decision.Accepted);
        Assert.Equal(1, inner.CallCount);
        Assert.Same(request, inner.LastRequest);
    }

    [Theory]
    [InlineData(PairingCandidateTrustState.AlreadyPaired)]
    [InlineData(PairingCandidateTrustState.IdentityChangedBlocked)]
    public async Task NonPairableTrustStateRejectsBeforeSasDecision(
        PairingCandidateTrustState trustState)
    {
        using DeviceIdentity advertised = DeviceIdentity.Generate(PeerDevice, "Desk");
        SignedDiscoveryOffer offer = CreateOffer(advertised);
        var pinned = new UnverifiedPairingCandidate(
            "desk._flowspan._tcp.local",
            offer,
            new IPEndPoint(IPAddress.Parse("192.168.50.20"), offer.Port),
            trustState);
        var inner = new RecordingDecisionSource();
        var source = new DiscoveryBoundPairingDecisionSource(
            pinned,
            inner,
            new FixedTimeProvider(Now));

        PairingDecision decision = await source.DecideAsync(new PairingConfirmationRequest(
            advertised.PublicIdentity,
            new ProtocolVersion(1, 0),
            "123456",
            Now.AddMinutes(1)));

        Assert.False(decision.Accepted);
        Assert.Equal(0, inner.CallCount);
    }

    private static SignedDiscoveryOffer CreateOffer(DeviceIdentity identity) =>
        SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength).ToArray());

    private static SignedDiscoveryOffer TamperSignature(SignedDiscoveryOffer offer)
    {
        const int chunkCharacters = 240;
        var text = new Dictionary<string, string>(
            DnsSdDiscoveryOfferTxtCodec.Encode(offer),
            StringComparer.OrdinalIgnoreCase);
        int chunkCount = int.Parse(
            text["fsc"],
            System.Globalization.CultureInfo.InvariantCulture);
        string encoded = string.Concat(
            Enumerable.Range(0, chunkCount).Select(index => text[$"fs{index}"]));
        byte[] payload = Convert.FromBase64String(encoded);
        payload[^1] ^= 0x01;
        string tampered = Convert.ToBase64String(payload);
        for (int index = 0; index < chunkCount; index++)
        {
            int offset = index * chunkCharacters;
            text[$"fs{index}"] = tampered.Substring(
                offset,
                Math.Min(chunkCharacters, tampered.Length - offset));
        }

        Assert.True(DnsSdDiscoveryOfferTxtCodec.TryDecode(
            text,
            out SignedDiscoveryOffer? decoded));
        return Assert.IsType<SignedDiscoveryOffer>(decoded);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingDecisionSource : IPairingDecisionSource
    {
        public int CallCount { get; private set; }

        public PairingConfirmationRequest? LastRequest { get; private set; }

        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return ValueTask.FromResult(new PairingDecision(
                accepted: true,
                CapabilityGrant.None));
        }
    }
}
