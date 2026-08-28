using System.Net;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class DnsSdUnverifiedPairingCandidateSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId LocalDevice =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerDevice =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void UnpairedValidSnapshotAppearsOnlyAsUnverifiedPairingCandidate()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        UnverifiedPairingCandidate candidate = Assert.Single(source.GetSnapshot());
        Assert.Equal(PairingCandidateTrustState.UnverifiedPairingRequired, candidate.TrustState);
        Assert.Equal(PeerDevice, candidate.Offer.DeviceId);
        Assert.Equal("Desk", candidate.Offer.DisplayName);
        Assert.Equal(peer.PublicIdentity.Fingerprint, candidate.Offer.IdentityFingerprint);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.50.20"), 4747), candidate.EndPoint);
        Assert.Equal(offer.OfferDigest, candidate.Offer.OfferDigest);
        Assert.Equal(1, browser.StartCount);
    }

    [Fact]
    public void ExpiredCandidateLeavesSnapshotWithoutBrowserCallback()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        var time = new MutableTimeProvider(Now);
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            time);
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);
        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        time.UtcNow = Now.AddSeconds(30);

        Assert.Empty(source.GetSnapshot());
    }

    [Fact]
    public void OnlyConcreteSafePeerAddressesAppear()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [
                IPAddress.Any,
                IPAddress.IPv6Any,
                IPAddress.Broadcast,
                IPAddress.Loopback,
                IPAddress.IPv6Loopback,
                IPAddress.Parse("224.0.0.251"),
                IPAddress.Parse("ff02::fb"),
                IPAddress.Parse("fe80::20"),
                IPAddress.Parse("192.168.50.20"),
            ],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        UnverifiedPairingCandidate candidate = Assert.Single(source.GetSnapshot());
        Assert.Equal(IPAddress.Parse("192.168.50.20"), candidate.EndPoint.Address);
    }

    [Fact]
    public void TrustedDeviceIdWithAnotherFingerprintIsBlocked()
    {
        using DeviceIdentity trusted = DeviceIdentity.Generate(PeerDevice, "Desk");
        using DeviceIdentity changed = DeviceIdentity.Generate(PeerDevice, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            trusted.PublicIdentity,
            Now,
            CapabilityGrant.None));
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            trustStore,
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(changed, Now);

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        UnverifiedPairingCandidate candidate = Assert.Single(source.GetSnapshot());
        Assert.Equal(PairingCandidateTrustState.IdentityChangedBlocked, candidate.TrustState);
        Assert.NotEqual(trusted.PublicIdentity.Fingerprint, candidate.Offer.IdentityFingerprint);
    }

    [Fact]
    public void MatchingTrustedIdentityIsClassifiedAsAlreadyPaired()
    {
        using DeviceIdentity trusted = DeviceIdentity.Generate(PeerDevice, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            trusted.PublicIdentity,
            Now,
            CapabilityGrant.None));
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            trustStore,
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(trusted, Now);

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        UnverifiedPairingCandidate candidate = Assert.Single(source.GetSnapshot());
        Assert.Equal(PairingCandidateTrustState.AlreadyPaired, candidate.TrustState);
    }

    [Fact]
    public void OfferBeyondAllowedFutureClockSkewNeverAppears()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer future = CreateOffer(
            peer,
            Now.Add(SignedDiscoveryOffer.MaximumFutureClockSkew).AddTicks(1));

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            future.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(future)));

        Assert.Empty(source.GetSnapshot());
    }

    [Fact]
    public void OfferAtFutureClockSkewBoundaryAppears()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer boundary = CreateOffer(
            peer,
            Now.Add(SignedDiscoveryOffer.MaximumFutureClockSkew));

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            boundary.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(boundary)));

        Assert.Single(source.GetSnapshot());
    }

    [Fact]
    public void MinimumIssueTimeAppearsWithoutClockSkewUnderflow()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(DateTimeOffset.MinValue));
        SignedDiscoveryOffer offer = CreateOffer(peer, DateTimeOffset.MinValue);

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        Assert.Single(source.GetSnapshot());
    }

    [Fact]
    public void CandidateAtMaximumExpiryIsExcludedWithoutOverflow()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(DateTimeOffset.MaxValue));
        SignedDiscoveryOffer offer = CreateOffer(
            peer,
            DateTimeOffset.MaxValue.Subtract(TimeSpan.FromSeconds(30)));

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        Assert.Empty(source.GetSnapshot());
    }

    [Fact]
    public void BrowserStartFailureDrainsSubscriptionsAndBrowser()
    {
        var browser = new FakeDnsSdServiceBrowser
        {
            StartException = new IOException("browse failed"),
        };

        IOException failure = Assert.Throws<IOException>(() =>
            new DnsSdUnverifiedPairingCandidateSource(
                LocalDevice,
                new InMemoryTrustStore(),
                browser,
                new FixedTimeProvider(Now)));

        Assert.Equal("browse failed", failure.Message);
        Assert.Equal(1, browser.DisposeCount);
        Assert.Equal(0, browser.ChangeSubscriberCount);
        Assert.Equal(0, browser.RemoveSubscriberCount);
    }

    [Fact]
    public void TrustAddedAfterDiscoveryReclassifiesTheNextSnapshot()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var trustStore = new InMemoryTrustStore();
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            trustStore,
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);
        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));
        Assert.Equal(
            PairingCandidateTrustState.UnverifiedPairingRequired,
            Assert.Single(source.GetSnapshot()).TrustState);

        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            Now,
            CapabilityGrant.None));

        Assert.Equal(
            PairingCandidateTrustState.AlreadyPaired,
            Assert.Single(source.GetSnapshot()).TrustState);
    }

    [Fact]
    public void AcceptedServiceChangePublishesSnapshotChanged()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        int changes = 0;
        source.SnapshotChanged += () => changes++;
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);

        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        Assert.Equal(1, changes);
    }

    [Fact]
    public void ServiceRemovalDropsCandidatesAndPublishesOneChange()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        int changes = 0;
        source.SnapshotChanged += () => changes++;
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);
        browser.Change(DnsSdServiceSnapshot.Create(
            "desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));
        changes = 0;

        browser.Remove("DESK._flowspan._tcp.local");
        browser.Remove("desk._flowspan._tcp.local");

        Assert.Empty(source.GetSnapshot());
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SnapshotOrderIsCanonicalAcrossServiceAndAddressInsertionOrder()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(PeerDevice, "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdUnverifiedPairingCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new FixedTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(peer, Now);
        browser.Change(DnsSdServiceSnapshot.Create(
            "z-desk._flowspan._tcp.local",
            offer.Port,
            [
                IPAddress.Parse("192.168.50.30"),
                IPAddress.Parse("192.168.50.20"),
            ],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));
        browser.Change(DnsSdServiceSnapshot.Create(
            "a-desk._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer)));

        UnverifiedPairingCandidate[] snapshot = source.GetSnapshot().ToArray();

        Assert.Collection(
            snapshot,
            candidate =>
            {
                Assert.Equal("a-desk._flowspan._tcp.local", candidate.InstanceName);
                Assert.Equal(IPAddress.Parse("192.168.50.20"), candidate.EndPoint.Address);
            },
            candidate =>
            {
                Assert.Equal("z-desk._flowspan._tcp.local", candidate.InstanceName);
                Assert.Equal(IPAddress.Parse("192.168.50.20"), candidate.EndPoint.Address);
            },
            candidate =>
            {
                Assert.Equal("z-desk._flowspan._tcp.local", candidate.InstanceName);
                Assert.Equal(IPAddress.Parse("192.168.50.30"), candidate.EndPoint.Address);
            });
    }

    private static SignedDiscoveryOffer CreateOffer(
        DeviceIdentity identity,
        DateTimeOffset issuedAt) => SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            issuedAt,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength).ToArray());

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeDnsSdServiceBrowser : IDnsSdServiceBrowser
    {
        private Action<DnsSdServiceSnapshot>? serviceChanged;
        private Action<string>? serviceRemoved;

        public int ChangeSubscriberCount =>
            serviceChanged?.GetInvocationList().Length ?? 0;

        public int DisposeCount { get; private set; }

        public int RemoveSubscriberCount =>
            serviceRemoved?.GetInvocationList().Length ?? 0;

        public int StartCount { get; private set; }

        public Exception? StartException { get; init; }

        public event Action<DnsSdServiceSnapshot>? ServiceChanged
        {
            add => serviceChanged += value;
            remove => serviceChanged -= value;
        }

        public event Action<string>? ServiceRemoved
        {
            add => serviceRemoved += value;
            remove => serviceRemoved -= value;
        }

        public void Change(DnsSdServiceSnapshot snapshot) => serviceChanged?.Invoke(snapshot);

        public void Dispose() => DisposeCount++;

        public void Remove(string instanceName) => serviceRemoved?.Invoke(instanceName);

        public void Start()
        {
            StartCount++;
            if (StartException is not null)
            {
                throw StartException;
            }
        }
    }
}
