using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using Makaretu.Dns;

namespace Flowspan.Transport.Mdns;

internal sealed class MakaretuMdnsDiscoveryStack : IMdnsDiscoveryStack
{
    private const int MaximumTranslatedRecords = 256;
    private static readonly DomainName ServiceName = "_flowspan._tcp";
    private readonly ServiceDiscovery discovery;
    private readonly MulticastService multicast;
    private ServiceProfile? advertisedProfile;
    private int disposed;

    public MakaretuMdnsDiscoveryStack()
    {
        multicast = new MulticastService();
        discovery = new ServiceDiscovery(multicast)
        {
            AnswersContainsAdditionalRecords = true,
        };
        multicast.AnswerReceived += OnAnswerReceived;
        multicast.NetworkInterfaceDiscovered += OnNetworkInterfaceDiscovered;
        discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        discovery.ServiceInstanceShutdown += OnServiceInstanceShutdown;
    }

    public event Action<string>? InstanceDiscovered;

    public event Action<string>? InstanceRemoved;

    public event Action<IReadOnlyList<MdnsResource>>? RecordsReceived;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>(capacity: 3);
        AttemptCleanup(Withdraw, failures);
        discovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
        discovery.ServiceInstanceShutdown -= OnServiceInstanceShutdown;
        multicast.AnswerReceived -= OnAnswerReceived;
        multicast.NetworkInterfaceDiscovered -= OnNetworkInterfaceDiscovered;
        AttemptCleanup(discovery.Dispose, failures);
        AttemptCleanup(multicast.Dispose, failures);
        ThrowCleanupFailures(failures);
    }

    public void QueryHost(string hostName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var name = new DomainName(hostName);
        multicast.SendQuery(name, type: DnsType.A);
        multicast.SendQuery(name, type: DnsType.AAAA);
    }

    public void QueryInstance(string instanceName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var name = new DomainName(instanceName);
        multicast.SendQuery(name, type: DnsType.SRV);
        multicast.SendQuery(name, type: DnsType.TXT);
    }

    public void QueryInstances()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        discovery.QueryServiceInstances(ServiceName);
    }

    public void Publish(SignedDiscoveryOffer offer)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(offer);
        if (advertisedProfile is not null)
        {
            TXTRecord current = advertisedProfile.Resources
                .OfType<TXTRecord>()
                .Single();
            IReadOnlyDictionary<string, string> expected =
                DnsSdDiscoveryOfferTxtCodec.Encode(offer);
            var currentProperties = current.Strings
                .Select(static value => value.Split('=', 2))
                .ToDictionary(
                    static pair => pair[0],
                    static pair => pair[1],
                    StringComparer.OrdinalIgnoreCase);
            if (currentProperties.Count == expected.Count
                && expected.All(pair => currentProperties.TryGetValue(
                    pair.Key,
                    out string? value)
                    && StringComparer.Ordinal.Equals(value, pair.Value)))
            {
                return;
            }

            Withdraw();
        }

        ServiceProfile profile = CreateProfile(offer);
        discovery.Advertise(profile);
        advertisedProfile = profile;
        try
        {
            discovery.Announce(profile);
        }
        catch (Exception announceFailure)
        {
            try
            {
                Withdraw();
            }
            catch (Exception withdrawFailure)
            {
                throw new AggregateException(
                    "The mDNS announcement and its withdrawal both failed.",
                    announceFailure,
                    withdrawFailure);
            }

            ExceptionDispatchInfo.Capture(announceFailure).Throw();
            throw new UnreachableException();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        multicast.Start();
    }

    public void Withdraw()
    {
        ServiceProfile? profile = advertisedProfile;
        advertisedProfile = null;
        if (profile is not null)
        {
            discovery.Unadvertise(profile);
        }
    }

    internal static ServiceProfile CreateProfile(SignedDiscoveryOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        string instanceName = $"flowspan-{offer.DeviceId.ToString().Replace(
            "-",
            string.Empty,
            StringComparison.Ordinal)}";
        var profile = new ServiceProfile(
            instanceName,
            ServiceName,
            offer.Port);
        TXTRecord textRecord = profile.Resources.OfType<TXTRecord>().Single();
        textRecord.Strings.Clear();
        foreach ((string key, string value) in
                 DnsSdDiscoveryOfferTxtCodec.Encode(offer)
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            textRecord.Strings.Add($"{key}={value}");
        }

        return profile;
    }

    private static void AttemptCleanup(
        Action cleanup,
        List<Exception> failures)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowCleanupFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new UnreachableException();
        }

        throw new AggregateException(
            "Multiple failures occurred while disposing the mDNS stack.",
            failures);
    }

    internal static IReadOnlyList<MdnsResource> Translate(
        Message message,
        long ipv6ScopeId = 0)
    {
        ArgumentNullException.ThrowIfNull(message);
        var translated = new List<MdnsResource>();
        IEnumerable<ResourceRecord> records = message.Answers
            .Concat(message.AdditionalRecords);
        foreach (ResourceRecord record in records)
        {
            if (translated.Count >= MaximumTranslatedRecords)
            {
                break;
            }

            switch (record)
            {
                case SRVRecord srv:
                    translated.Add(new MdnsSrvResource(
                        srv.Name.ToString(),
                        srv.Target.ToString(),
                        srv.Port,
                        srv.TTL));
                    break;
                case TXTRecord txt when txt.Strings.Count <= 16:
                    translated.Add(new MdnsTxtResource(
                        txt.Name.ToString(),
                        txt.Strings.ToArray(),
                        txt.TTL));
                    break;
                case AddressRecord address:
                    IPAddress resolvedAddress = address.Address;
                    if (ipv6ScopeId > 0
                        && resolvedAddress.IsIPv6LinkLocal
                        && resolvedAddress.ScopeId == 0)
                    {
                        resolvedAddress = new IPAddress(
                            resolvedAddress.GetAddressBytes(),
                            ipv6ScopeId);
                    }

                    translated.Add(new MdnsAddressResource(
                        address.Name.ToString(),
                        resolvedAddress,
                        address.TTL));
                    break;
            }
        }

        return translated;
    }

    private void OnAnswerReceived(object? sender, MessageEventArgs eventArgs)
    {
        long scopeId = eventArgs.RemoteEndPoint?.Address.ScopeId ?? 0;
        IReadOnlyList<MdnsResource> records = Translate(eventArgs.Message, scopeId);
        if (records.Count > 0)
        {
            RecordsReceived?.Invoke(records);
        }
    }

    private void OnNetworkInterfaceDiscovered(
        object? sender,
        NetworkInterfaceEventArgs eventArgs) => QueryInstances();

    private void OnServiceInstanceDiscovered(
        object? sender,
        ServiceInstanceDiscoveryEventArgs eventArgs)
    {
        IReadOnlyList<MdnsResource> records = Translate(eventArgs.Message);
        if (records.Count > 0)
        {
            RecordsReceived?.Invoke(records);
        }

        InstanceDiscovered?.Invoke(eventArgs.ServiceInstanceName.ToString());
    }

    private void OnServiceInstanceShutdown(
        object? sender,
        ServiceInstanceShutdownEventArgs eventArgs) =>
        InstanceRemoved?.Invoke(eventArgs.ServiceInstanceName.ToString());
}
