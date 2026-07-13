using System.Net;
using Flowspan.Transport;
using Makaretu.Dns;

namespace Flowspan.Transport.Mdns.Tests;

public sealed class MakaretuDnsSdServiceBrowserTests
{
    [Fact]
    public void ResolutionCacheCombinesSplitSrvTxtAndAddressBatches()
    {
        using DeviceIdentityFixture identity = DeviceIdentityFixture.Create();
        var cache = new DnsSdResolutionCache();
        IReadOnlyDictionary<string, string> text =
            DnsSdDiscoveryOfferTxtCodec.Encode(identity.Offer);

        DnsSdCacheUpdate partial = cache.Observe(
        [
            new MdnsSrvResource(
                "desk._flowspan._tcp.local",
                "desk-host.local",
                identity.Offer.Port,
                TimeSpan.FromSeconds(120)),
            new MdnsTxtResource(
                "desk._flowspan._tcp.local",
                text.Select(static pair => $"{pair.Key}={pair.Value}").ToArray(),
                TimeSpan.FromSeconds(120)),
        ]);

        Assert.Empty(partial.Snapshots);
        Assert.Equal<string>(["desk-host.local"], partial.HostsToQuery);

        DnsSdCacheUpdate resolved = cache.Observe(
        [
            new MdnsAddressResource(
                "desk-host.local",
                IPAddress.Parse("192.168.50.20"),
                TimeSpan.FromSeconds(120)),
            new MdnsAddressResource(
                "desk-host.local",
                IPAddress.Parse("fd00::20"),
                TimeSpan.FromSeconds(120)),
        ]);

        DnsSdServiceSnapshot snapshot = Assert.Single(resolved.Snapshots);
        Assert.Equal("desk._flowspan._tcp.local", snapshot.InstanceName);
        Assert.Equal(identity.Offer.Port, snapshot.Port);
        Assert.Equal<IPAddress>(
            [IPAddress.Parse("192.168.50.20"), IPAddress.Parse("fd00::20")],
            snapshot.Addresses);
        Assert.Equal(text.Count, snapshot.TextRecords.Count);
    }

    [Fact]
    public void ResolutionCacheRejectsDuplicateTxtKeysAndTtlZeroData()
    {
        var cache = new DnsSdResolutionCache();
        cache.Observe(
        [
            new MdnsSrvResource(
                "desk._flowspan._tcp.local",
                "desk-host.local",
                4747,
                TimeSpan.FromSeconds(120)),
            new MdnsTxtResource(
                "desk._flowspan._tcp.local",
                ["txtvers=1", "TXTVERS=1", "fsc=1", "fs0=AAAA"],
                TimeSpan.FromSeconds(120)),
            new MdnsAddressResource(
                "desk-host.local",
                IPAddress.Parse("192.168.50.20"),
                TimeSpan.FromSeconds(120)),
        ]);

        Assert.Empty(cache.Observe([]).Snapshots);

        cache.Observe(
        [
            new MdnsAddressResource(
                "desk-host.local",
                IPAddress.Parse("192.168.50.20"),
                TimeSpan.Zero),
        ]);

        Assert.Empty(cache.Observe([]).Snapshots);
    }

    [Fact]
    public void ResolutionCacheRejectsOversizedRecordBatchAtomically()
    {
        var cache = new DnsSdResolutionCache();
        MdnsSrvResource record = new(
            "desk._flowspan._tcp.local",
            "desk-host.local",
            4747,
            TimeSpan.FromSeconds(120));

        DnsSdCacheUpdate result = cache.Observe(
            Enumerable.Repeat<MdnsResource>(record, 257).ToArray());

        Assert.Empty(result.Snapshots);
        Assert.Empty(result.HostsToQuery);
        Assert.Empty(cache.Observe([]).Snapshots);
    }

    [Fact]
    public void MakaretuMessagesTranslateOnlyBoundedRelevantRecordShapes()
    {
        var message = new Message();
        var txt = new TXTRecord
        {
            Name = "desk._flowspan._tcp.local",
        };
        txt.Strings.Add("txtvers=1");
        message.Answers.Add(new SRVRecord
        {
            Name = "desk._flowspan._tcp.local",
            Target = "desk-host.local",
            Port = 4747,
        });
        message.Answers.Add(txt);
        message.AdditionalRecords.Add(AddressRecord.Create(
            "desk-host.local",
            IPAddress.Parse("192.168.50.20")));

        IReadOnlyList<MdnsResource> translated =
            MakaretuMdnsDiscoveryStack.Translate(message);

        Assert.Collection(
            translated,
            resource => Assert.IsType<MdnsSrvResource>(resource),
            resource => Assert.IsType<MdnsTxtResource>(resource),
            resource => Assert.IsType<MdnsAddressResource>(resource));
    }

    [Fact]
    public void MakaretuTranslationAttachesReceiveScopeToIpv6LinkLocalAddress()
    {
        var message = new Message();
        message.Answers.Add(AddressRecord.Create(
            "desk-host.local",
            IPAddress.Parse("fe80::20")));

        MdnsAddressResource translated = Assert.IsType<MdnsAddressResource>(
            Assert.Single(MakaretuMdnsDiscoveryStack.Translate(message, 7)));

        Assert.True(translated.Address.IsIPv6LinkLocal);
        Assert.Equal(7, translated.Address.ScopeId);
    }

    [Fact]
    public void BrowserQueriesDetailsAndRestartsAcrossNetworkChange()
    {
        using DeviceIdentityFixture identity = DeviceIdentityFixture.Create();
        var network = new FakeNetworkChangeSource();
        var first = new FakeMdnsDiscoveryStack();
        var second = new FakeMdnsDiscoveryStack();
        var stacks = new Queue<FakeMdnsDiscoveryStack>([first, second]);
        using var browser = new MakaretuDnsSdServiceBrowser(
            network,
            () => stacks.Dequeue());
        var changed = new List<DnsSdServiceSnapshot>();
        var removed = new List<string>();
        browser.ServiceChanged += changed.Add;
        browser.ServiceRemoved += removed.Add;

        browser.Start();
        first.Discover("desk._flowspan._tcp.local");
        first.Observe(
        [
            new MdnsSrvResource(
                "desk._flowspan._tcp.local",
                "desk-host.local",
                identity.Offer.Port,
                TimeSpan.FromSeconds(120)),
            new MdnsTxtResource(
                "desk._flowspan._tcp.local",
                DnsSdDiscoveryOfferTxtCodec.Encode(identity.Offer)
                    .Select(static pair => $"{pair.Key}={pair.Value}")
                    .ToArray(),
                TimeSpan.FromSeconds(120)),
        ]);
        first.Observe(
        [
            new MdnsAddressResource(
                "desk-host.local",
                IPAddress.Parse("192.168.50.20"),
                TimeSpan.FromSeconds(120)),
        ]);

        Assert.Equal(1, first.StartCount);
        Assert.Equal(1, first.QueryInstancesCount);
        Assert.Equal<string>(
            ["desk._flowspan._tcp.local"],
            first.InstanceQueries);
        Assert.Equal<string>(["desk-host.local"], first.HostQueries);
        Assert.Single(changed);

        network.Signal();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal<string>(["desk._flowspan._tcp.local"], removed);
        Assert.Equal(1, second.StartCount);
        Assert.Equal(1, second.QueryInstancesCount);
        Assert.Equal(1, network.SubscriberCount);
    }

    [Fact]
    public void BrowserDisposalIsIdempotentAndDrainsSubscriptions()
    {
        var network = new FakeNetworkChangeSource();
        var stack = new FakeMdnsDiscoveryStack();
        var browser = new MakaretuDnsSdServiceBrowser(network, () => stack);
        browser.Start();

        browser.Dispose();
        browser.Dispose();

        Assert.Equal(1, stack.DisposeCount);
        Assert.Equal(0, network.SubscriberCount);
        Assert.Throws<ObjectDisposedException>(browser.Start);
    }

    [Fact]
    public void FailedNetworkRestartKeepsOldStackAndIsolatesFaultHandlers()
    {
        var network = new FakeNetworkChangeSource();
        var stack = new FakeMdnsDiscoveryStack();
        int created = 0;
        using var browser = new MakaretuDnsSdServiceBrowser(
            network,
            () => ++created == 1
                ? stack
                : throw new IOException("restart failed"));
        var faults = new List<Exception>();
        browser.Faulted += _ => throw new InvalidOperationException("diagnostic failed");
        browser.Faulted += faults.Add;
        browser.Start();

        network.Signal();

        Assert.Equal(0, stack.DisposeCount);
        Assert.Equal(1, network.SubscriberCount);
        Assert.IsType<IOException>(Assert.Single(faults));
    }

    [Fact]
    public void InitialStackFailureDrainsNetworkSubscription()
    {
        var network = new FakeNetworkChangeSource();
        var stack = new FakeMdnsDiscoveryStack
        {
            StartException = new IOException("bind failed"),
        };
        using var browser = new MakaretuDnsSdServiceBrowser(network, () => stack);

        IOException failure = Assert.Throws<IOException>(browser.Start);

        Assert.Equal("bind failed", failure.Message);
        Assert.Equal(1, stack.DisposeCount);
        Assert.Equal(0, network.SubscriberCount);
    }

    private sealed class FakeMdnsDiscoveryStack : IMdnsDiscoveryStack
    {
        public int DisposeCount { get; private set; }

        public List<string> HostQueries { get; } = [];

        public List<string> InstanceQueries { get; } = [];

        public int QueryInstancesCount { get; private set; }

        public int StartCount { get; private set; }

        public Exception? StartException { get; init; }

        public event Action<string>? InstanceDiscovered;

        public event Action<string>? InstanceRemoved;

        public event Action<IReadOnlyList<MdnsResource>>? RecordsReceived;

        public void Discover(string instanceName) =>
            InstanceDiscovered?.Invoke(instanceName);

        public void Dispose() => DisposeCount++;

        public void Observe(IReadOnlyList<MdnsResource> records) =>
            RecordsReceived?.Invoke(records);

        public void QueryHost(string hostName) => HostQueries.Add(hostName);

        public void QueryInstance(string instanceName) =>
            InstanceQueries.Add(instanceName);

        public void QueryInstances() => QueryInstancesCount++;

        public void Remove(string instanceName) =>
            InstanceRemoved?.Invoke(instanceName);

        public void Start()
        {
            StartCount++;
            if (StartException is not null)
            {
                throw StartException;
            }
        }
    }

    private sealed class FakeNetworkChangeSource : INetworkChangeSource
    {
        private Action? changed;

        public int SubscriberCount => changed?.GetInvocationList().Length ?? 0;

        public void Signal() => changed?.Invoke();

        public IDisposable Subscribe(Action networkChanged)
        {
            changed += networkChanged;
            return new CallbackDisposable(() => changed -= networkChanged);
        }

        private sealed class CallbackDisposable(Action callback) : IDisposable
        {
            private Action? callback = callback;

            public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
        }
    }
}
