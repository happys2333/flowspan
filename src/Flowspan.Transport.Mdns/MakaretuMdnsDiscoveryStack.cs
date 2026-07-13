using System.Net;
using Makaretu.Dns;

namespace Flowspan.Transport.Mdns;

internal sealed class MakaretuMdnsDiscoveryStack : IMdnsDiscoveryStack
{
    private const int MaximumTranslatedRecords = 256;
    private static readonly DomainName ServiceName = "_flowspan._tcp";
    private readonly ServiceDiscovery discovery;
    private readonly MulticastService multicast;
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

        discovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
        discovery.ServiceInstanceShutdown -= OnServiceInstanceShutdown;
        multicast.AnswerReceived -= OnAnswerReceived;
        multicast.NetworkInterfaceDiscovered -= OnNetworkInterfaceDiscovered;
        discovery.Dispose();
        multicast.Dispose();
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

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        multicast.Start();
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
