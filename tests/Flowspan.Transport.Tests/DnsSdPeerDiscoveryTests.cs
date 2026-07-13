using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class DnsSdPeerDiscoveryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId LocalDevice =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerDevice =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TxtCodecRoundTripsMaximumNameWithinDnsStringLimits()
    {
        string maximumUtf8Name = new('界', 80);
        using DeviceIdentity identity = DeviceIdentity.Generate(
            PeerDevice,
            maximumUtf8Name);
        SignedDiscoveryOffer original = CreateOffer(identity, Now);

        IReadOnlyDictionary<string, string> textRecords =
            DnsSdDiscoveryOfferTxtCodec.Encode(original);

        Assert.InRange(textRecords.Count, 2, 16);
        Assert.All(textRecords, property => Assert.InRange(
            Encoding.UTF8.GetByteCount($"{property.Key}={property.Value}"),
            1,
            255));
        Assert.True(DnsSdDiscoveryOfferTxtCodec.TryDecode(
            textRecords,
            out SignedDiscoveryOffer? decoded));
        Assert.NotNull(decoded);
        Assert.Equal(original.OfferDigest, decoded.OfferDigest);
        Assert.Equal(original.DisplayName, decoded.DisplayName);
        Assert.Equal(original.Port, decoded.Port);
        Assert.Equal<ProtocolVersion>(
            original.ProtocolVersions,
            decoded.ProtocolVersions);
        Assert.True(decoded.Verify(identity.PublicIdentity, Now));
    }

    [Fact]
    public void TxtCodecRejectsMissingAndNonCanonicalChunks()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            PeerDevice,
            "Desk");
        var missing = new Dictionary<string, string>(
            DnsSdDiscoveryOfferTxtCodec.Encode(CreateOffer(identity, Now)),
            StringComparer.OrdinalIgnoreCase);
        string firstChunk = missing["fs0"];
        missing.Remove("fs0");

        Assert.False(DnsSdDiscoveryOfferTxtCodec.TryDecode(missing, out _));

        missing["fs0"] = $"{firstChunk[..^1]}!";
        Assert.False(DnsSdDiscoveryOfferTxtCodec.TryDecode(missing, out _));

        missing = new Dictionary<string, string>(
            DnsSdDiscoveryOfferTxtCodec.Encode(CreateOffer(identity, Now)),
            StringComparer.OrdinalIgnoreCase)
        {
            ["fsc"] = "01",
        };
        Assert.False(DnsSdDiscoveryOfferTxtCodec.TryDecode(missing, out _));
    }

    [Fact]
    public void TxtCodecContainsRandomHostilePayloadsWithoutThrowing()
    {
        var random = new Random(0x5F10A);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            byte[] payload = new byte[random.Next(
                1,
                DnsSdDiscoveryOfferTxtCodec.MaximumPayloadBytes + 1)];
            random.NextBytes(payload);
            string encoded = Convert.ToBase64String(payload);
            string[] chunks = encoded
                .Chunk(240)
                .Select(static chunk => new string(chunk))
                .ToArray();
            var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["txtvers"] = "1",
                ["fsc"] = chunks.Length.ToString(CultureInfo.InvariantCulture),
            };
            for (int index = 0; index < chunks.Length; index++)
            {
                text[$"fs{index.ToString(CultureInfo.InvariantCulture)}"] = chunks[index];
            }

            DnsSdDiscoveryOfferTxtCodec.TryDecode(text, out _);
        }
    }

    [Fact]
    public void TrustedPeerProducesRotatingConcreteCandidates()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            PeerDevice,
            "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        var trustStore = TrustedStore(identity.PublicIdentity);
        var time = new MutableTimeProvider(Now);
        using var source = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            trustStore,
            browser,
            time);
        SignedDiscoveryOffer offer = CreateOffer(identity, Now);
        browser.Change(CreateSnapshot(
            "desk._flowspan._tcp.local",
            offer,
            [IPAddress.Parse("192.168.50.20"), IPAddress.Parse("fd00::20")]));

        Assert.Equal(1, browser.StartCount);
        Assert.True(source.TryGet(PeerDevice, out VerifiedPeerConnectionCandidate? first));
        Assert.True(source.TryGet(PeerDevice, out VerifiedPeerConnectionCandidate? second));
        Assert.True(source.TryGet(PeerDevice, out VerifiedPeerConnectionCandidate? third));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.NotEqual(first.EndPoint.Address, second.EndPoint.Address);
        Assert.Equal(first.EndPoint, third.EndPoint);
        Assert.Equal(offer.OfferDigest, first.Offer.OfferDigest);
        Assert.True(first.CandidateIdentity.HasSameKey(identity.PublicIdentity));
    }

    [Fact]
    public void SameKeyDisplayNameChangeRemainsVerifiable()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(
            PeerDevice,
            "Old name");
        byte[] privateKey = original.ExportPkcs8ForSecretStore();
        using DeviceIdentity renamed = DeviceIdentity.ImportPkcs8(
            PeerDevice,
            "New name",
            privateKey);
        CryptographicOperations.ZeroMemory(privateKey);
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            TrustedStore(original.PublicIdentity),
            browser,
            new MutableTimeProvider(Now));
        browser.Change(CreateSnapshot(
            "desk._flowspan._tcp.local",
            CreateOffer(renamed, Now),
            [IPAddress.Parse("192.168.50.20")]));

        Assert.True(source.TryGet(PeerDevice, out VerifiedPeerConnectionCandidate? candidate));
        Assert.NotNull(candidate);
        Assert.Equal("New name", candidate.CandidateIdentity.DisplayName);
        Assert.True(candidate.CandidateIdentity.HasSameKey(original.PublicIdentity));
    }

    [Fact]
    public void InvalidRefreshDoesNotEraseLastVerifiedCandidate()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            PeerDevice,
            "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            TrustedStore(identity.PublicIdentity),
            browser,
            new MutableTimeProvider(Now));
        SignedDiscoveryOffer offer = CreateOffer(identity, Now);
        DnsSdServiceSnapshot valid = CreateSnapshot(
            "desk._flowspan._tcp.local",
            offer,
            [IPAddress.Parse("192.168.50.20")]);
        browser.Change(valid);
        var corrupt = new Dictionary<string, string>(
            valid.TextRecords,
            StringComparer.OrdinalIgnoreCase);
        corrupt["fs0"] = $"{corrupt["fs0"][..^1]}!";
        browser.Change(DnsSdServiceSnapshot.Create(
            valid.InstanceName,
            valid.Port,
            valid.Addresses,
            corrupt));

        Assert.True(source.TryGet(PeerDevice, out VerifiedPeerConnectionCandidate? retained));
        Assert.NotNull(retained);
        Assert.Equal(offer.OfferDigest, retained.Offer.OfferDigest);
    }

    [Fact]
    public void RemovalAndExpiryWithdrawCandidates()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            PeerDevice,
            "Desk");
        var browser = new FakeDnsSdServiceBrowser();
        var time = new MutableTimeProvider(Now);
        using var source = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            TrustedStore(identity.PublicIdentity),
            browser,
            time);
        const string instance = "desk._flowspan._tcp.local";
        browser.Change(CreateSnapshot(
            instance,
            CreateOffer(identity, Now),
            [IPAddress.Parse("192.168.50.20")]));
        browser.Remove(instance);

        Assert.False(source.TryGet(PeerDevice, out _));

        browser.Change(CreateSnapshot(
            instance,
            CreateOffer(identity, Now),
            [IPAddress.Parse("192.168.50.20")]));
        time.UtcNow = Now.AddSeconds(31);

        Assert.False(source.TryGet(PeerDevice, out _));
    }

    [Fact]
    public void UntrustedSelfAndUnsafeAddressesNeverBecomeCandidates()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            PeerDevice,
            "Desk");
        using DeviceIdentity local = DeviceIdentity.Generate(
            LocalDevice,
            "Laptop");
        var browser = new FakeDnsSdServiceBrowser();
        using var source = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new MutableTimeProvider(Now));
        browser.Change(CreateSnapshot(
            "untrusted._flowspan._tcp.local",
            CreateOffer(peer, Now),
            [IPAddress.Parse("192.168.50.20")]));
        browser.Change(CreateSnapshot(
            "self._flowspan._tcp.local",
            CreateOffer(local, Now),
            [IPAddress.Parse("192.168.50.21")]));

        Assert.False(source.TryGet(PeerDevice, out _));
        Assert.False(source.TryGet(LocalDevice, out _));

        var trustedBrowser = new FakeDnsSdServiceBrowser();
        using var trustedSource = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            TrustedStore(peer.PublicIdentity),
            trustedBrowser,
            new MutableTimeProvider(Now));
        trustedBrowser.Change(CreateSnapshot(
            "unsafe._flowspan._tcp.local",
            CreateOffer(peer, Now),
            [
                IPAddress.Any,
                IPAddress.IPv6Any,
                IPAddress.Broadcast,
                IPAddress.Loopback,
                IPAddress.IPv6Loopback,
                IPAddress.Parse("224.0.0.251"),
                IPAddress.Parse("ff02::fb"),
                IPAddress.Parse("fe80::20"),
            ]));

        Assert.False(trustedSource.TryGet(PeerDevice, out _));
    }

    [Fact]
    public void DisposeUnsubscribesAndDisposesBrowser()
    {
        var browser = new FakeDnsSdServiceBrowser();
        var source = new DnsSdPeerConnectionCandidateSource(
            LocalDevice,
            new InMemoryTrustStore(),
            browser,
            new MutableTimeProvider(Now));

        source.Dispose();
        source.Dispose();

        Assert.Equal(1, browser.DisposeCount);
        Assert.Equal(0, browser.ChangeSubscriberCount);
        Assert.Equal(0, browser.RemoveSubscriberCount);
        Assert.Throws<ObjectDisposedException>(() => source.TryGet(PeerDevice, out _));
    }

    [Fact]
    public void BrowserStartFailureDrainsCandidateSourceSubscriptions()
    {
        var browser = new FakeDnsSdServiceBrowser
        {
            StartException = new IOException("browse failed"),
        };

        IOException failure = Assert.Throws<IOException>(() =>
            new DnsSdPeerConnectionCandidateSource(
                LocalDevice,
                new InMemoryTrustStore(),
                browser,
                new MutableTimeProvider(Now)));

        Assert.Equal("browse failed", failure.Message);
        Assert.Equal(1, browser.DisposeCount);
        Assert.Equal(0, browser.ChangeSubscriberCount);
        Assert.Equal(0, browser.RemoveSubscriberCount);
    }

    private static InMemoryTrustStore TrustedStore(PublicDeviceIdentity identity)
    {
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            identity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        return store;
    }

    private static DnsSdServiceSnapshot CreateSnapshot(
        string instanceName,
        SignedDiscoveryOffer offer,
        IEnumerable<IPAddress> addresses) => DnsSdServiceSnapshot.Create(
            instanceName,
            offer.Port,
            addresses,
            DnsSdDiscoveryOfferTxtCodec.Encode(offer));

    private static SignedDiscoveryOffer CreateOffer(
        DeviceIdentity identity,
        DateTimeOffset issuedAt) => SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)],
            issuedAt,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength)
                .ToArray());

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

        public void Change(DnsSdServiceSnapshot snapshot) =>
            serviceChanged?.Invoke(snapshot);

        public void Dispose() => DisposeCount++;

        public void Remove(string instanceName) =>
            serviceRemoved?.Invoke(instanceName);

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
