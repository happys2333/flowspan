using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class InMemoryTrustStoreTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameDeviceIdWithDifferentKeyIsBlockedWithoutOverwrite()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(PeerId, "Desk");
        using DeviceIdentity substituted = DeviceIdentity.Generate(PeerId, "Desk");
        var store = new InMemoryTrustStore();
        TrustRecord originalRecord = CreateRecord(
            original,
            CapabilityGrant.Of(Capability.ActivityReceive));

        TrustRegistrationResult added = store.Register(originalRecord);
        TrustRegistrationResult changed = store.Register(CreateRecord(
            substituted,
            CapabilityGrant.Of(Capability.MirrorDrive)));

        Assert.Equal(SecretStoreProtection.DegradedTestOnly, store.Protection);
        Assert.Equal(TrustRegistrationResult.Added, added);
        Assert.Equal(TrustRegistrationResult.IdentityChanged, changed);
        Assert.True(store.TryGet(PeerId, out TrustRecord? retained));
        Assert.Equal(original.PublicIdentity.Fingerprint, retained.PeerIdentity.Fingerprint);
        Assert.True(retained.GrantedCapabilities.Allows(Capability.ActivityReceive));
        Assert.False(retained.GrantedCapabilities.Allows(Capability.MirrorDrive));
    }

    [Fact]
    public void CapabilityUpdateRequiresExpectedIdentityFingerprint()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var store = new InMemoryTrustStore();
        store.Register(CreateRecord(identity, CapabilityGrant.None));

        bool wrongIdentity = store.TryUpdateCapabilities(
            PeerId,
            new string('0', 64),
            CapabilityGrant.Of(Capability.MirrorView));
        bool updated = store.TryUpdateCapabilities(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView));

        Assert.False(wrongIdentity);
        Assert.True(updated);
        Assert.True(store.Allows(PeerId, Capability.MirrorView));
        Assert.False(store.Allows(PeerId, Capability.MirrorDrive));
    }

    [Fact]
    public void RevocationImmediatelyRemovesEveryCapability()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var store = new InMemoryTrustStore();
        store.Register(CreateRecord(
            identity,
            CapabilityGrant.Of(
                Capability.ActivityReceive,
                Capability.MirrorView,
                Capability.MirrorDrive)));

        bool revoked = store.Revoke(PeerId);

        Assert.True(revoked);
        Assert.False(store.Allows(PeerId, Capability.ActivityReceive));
        Assert.False(store.Allows(PeerId, Capability.MirrorView));
        Assert.False(store.TryGet(PeerId, out _));
    }

    [Fact]
    public void TrustedPeerSnapshotIsCanonicalPointInTimeProjection()
    {
        using DeviceIdentity later = DeviceIdentity.Generate(
            PeerId,
            "Later desk");
        using DeviceIdentity earlier = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Earlier desk");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            later.PublicIdentity,
            Now.AddMinutes(1),
            CapabilityGrant.Of(Capability.ActivityReceive)));
        store.Register(new TrustRecord(
            earlier.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));

        var snapshot = store.GetSnapshot();
        store.TryUpdateCapabilities(
            earlier.DeviceId,
            earlier.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView));

        Assert.Equal(
            [earlier.DeviceId, later.DeviceId],
            snapshot.Select(static peer => peer.DeviceId));
        Assert.Equal("Earlier desk", snapshot[0].DisplayName);
        Assert.Equal(earlier.PublicIdentity.Fingerprint, snapshot[0].Fingerprint);
        Assert.Equal(Now, snapshot[0].VerifiedAt);
        Assert.True(snapshot[0].GrantedCapabilities.Allows(Capability.ActivityOffer));
        Assert.False(snapshot[0].GrantedCapabilities.Allows(Capability.MirrorView));
    }

    private static TrustRecord CreateRecord(
        DeviceIdentity identity,
        CapabilityGrant capabilities) => new(
            identity.PublicIdentity,
            Now,
            capabilities);
}
