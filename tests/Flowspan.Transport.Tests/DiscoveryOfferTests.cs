using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class DiscoveryOfferTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId Device =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void OfferVerifiesOnlyForBoundIdentityAndLifetime()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        using DeviceIdentity substituted = DeviceIdentity.Generate(Device, "Laptop");
        SignedDiscoveryOffer offer = CreateOffer(identity, Now);

        Assert.True(offer.Verify(identity.PublicIdentity, Now));
        Assert.False(offer.Verify(substituted.PublicIdentity, Now));
        Assert.False(offer.Verify(identity.PublicIdentity, Now.AddSeconds(30)));
        Assert.False(offer.Verify(
            identity.PublicIdentity,
            Now.Add(SignedDiscoveryOffer.MaximumLifetime).AddMilliseconds(1)));
        Assert.False(offer.Verify(
            identity.PublicIdentity,
            Now.Subtract(SignedDiscoveryOffer.MaximumFutureClockSkew).AddMilliseconds(-1)));
    }

    [Fact]
    public void ProtocolVersionsAreCanonicalizedIntoOfferDigest()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        byte[] nonce = Enumerable.Repeat(
            (byte)0x11,
            SignedDiscoveryOffer.NonceLength).ToArray();
        SignedDiscoveryOffer first = SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 1), new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            nonce);
        SignedDiscoveryOffer reordered = SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1), new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            nonce);

        Assert.Equal(first.OfferDigest, reordered.OfferDigest);
        Assert.Equal<ProtocolVersion>(
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)],
            first.ProtocolVersions);
        Assert.True(first.Verify(identity.PublicIdentity, Now));
        Assert.True(reordered.Verify(identity.PublicIdentity, Now));
    }

    [Fact]
    public void DirectoryRefreshesNewerOfferAndRejectsStaleReplay()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        var directory = new InMemoryDiscoveryDirectory();
        SignedDiscoveryOffer original = CreateOffer(identity, Now, nonceByte: 0x11);
        SignedDiscoveryOffer refreshed = CreateOffer(
            identity,
            Now.AddSeconds(1),
            nonceByte: 0x22);

        DiscoveryPublishResult added = directory.Publish(
            original,
            identity.PublicIdentity,
            Now);
        DiscoveryPublishResult updated = directory.Publish(
            refreshed,
            identity.PublicIdentity,
            Now.AddSeconds(1));
        DiscoveryPublishResult stale = directory.Publish(
            original,
            identity.PublicIdentity,
            Now.AddSeconds(1));

        Assert.Equal(DiscoveryPublishResult.Added, added);
        Assert.Equal(DiscoveryPublishResult.Refreshed, updated);
        Assert.Equal(DiscoveryPublishResult.Stale, stale);
        DiscoveredPeer peer = Assert.Single(directory.Snapshot(Now.AddSeconds(1)));
        Assert.Equal(refreshed.OfferDigest, peer.Offer.OfferDigest);
    }

    [Fact]
    public void DirectoryDeduplicatesRepeatedOffer()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        var directory = new InMemoryDiscoveryDirectory();
        SignedDiscoveryOffer offer = CreateOffer(identity, Now);
        directory.Publish(offer, identity.PublicIdentity, Now);

        DiscoveryPublishResult duplicate = directory.Publish(
            offer,
            identity.PublicIdentity,
            Now);

        Assert.Equal(DiscoveryPublishResult.Duplicate, duplicate);
        Assert.Single(directory.Snapshot(Now));
    }

    [Fact]
    public void DirectoryRejectsConflictingOfferAtSameIssueTime()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        var directory = new InMemoryDiscoveryDirectory();
        SignedDiscoveryOffer original = CreateOffer(identity, Now, nonceByte: 0x11);
        SignedDiscoveryOffer conflicting = CreateOffer(identity, Now, nonceByte: 0x22);
        directory.Publish(original, identity.PublicIdentity, Now);

        DiscoveryPublishResult result = directory.Publish(
            conflicting,
            identity.PublicIdentity,
            Now);

        Assert.Equal(DiscoveryPublishResult.Stale, result);
        DiscoveredPeer retained = Assert.Single(directory.Snapshot(Now));
        Assert.Equal(original.OfferDigest, retained.Offer.OfferDigest);
    }

    [Fact]
    public void DirectoryExpiresOfferWithoutNetworkCallback()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        var directory = new InMemoryDiscoveryDirectory();
        SignedDiscoveryOffer offer = CreateOffer(identity, Now);
        directory.Publish(offer, identity.PublicIdentity, Now);

        IReadOnlyList<DiscoveredPeer> expired = directory.Snapshot(
            Now.AddSeconds(30));

        Assert.Empty(expired);
    }

    [Fact]
    public void DirectorySurfacesIdentityChangeAndRetainsVerifiedCandidate()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(Device, "Laptop");
        using DeviceIdentity changed = DeviceIdentity.Generate(Device, "Laptop");
        var directory = new InMemoryDiscoveryDirectory();
        SignedDiscoveryOffer originalOffer = CreateOffer(original, Now);
        SignedDiscoveryOffer changedOffer = CreateOffer(changed, Now.AddSeconds(1));
        directory.Publish(originalOffer, original.PublicIdentity, Now);

        DiscoveryPublishResult result = directory.Publish(
            changedOffer,
            changed.PublicIdentity,
            Now.AddSeconds(1));

        Assert.Equal(DiscoveryPublishResult.IdentityChanged, result);
        DiscoveredPeer retained = Assert.Single(directory.Snapshot(Now.AddSeconds(1)));
        Assert.Equal(
            original.PublicIdentity.Fingerprint,
            retained.CandidateIdentity.Fingerprint);
    }

    [Fact]
    public void InvalidOfferLimitsAreRejectedBeforeSigning()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        byte[] nonce = new byte[SignedDiscoveryOffer.NonceLength];

        Assert.Throws<ArgumentOutOfRangeException>(() => SignedDiscoveryOffer.Create(
            identity,
            0,
            [new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            nonce));
        Assert.Throws<ArgumentOutOfRangeException>(() => SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            Now,
            SignedDiscoveryOffer.MaximumLifetime.Add(TimeSpan.FromSeconds(1)),
            nonce));
        Assert.Throws<ArgumentException>(() => SignedDiscoveryOffer.Create(
            identity,
            4747,
            [],
            Now,
            TimeSpan.FromSeconds(30),
            nonce));
        Assert.Throws<ArgumentException>(() => SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            nonce.AsSpan(0, nonce.Length - 1)));
    }

    private static SignedDiscoveryOffer CreateOffer(
        DeviceIdentity identity,
        DateTimeOffset issuedAt,
        byte nonceByte = 0x11) => SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            issuedAt,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat(nonceByte, SignedDiscoveryOffer.NonceLength)
                .Select(static value => (byte)value)
                .ToArray());
}
