using System.Collections.Immutable;
using System.Net;

namespace Flowspan.Transport.Mdns;

internal abstract record MdnsResource(
    string Name,
    TimeSpan TimeToLive);

internal sealed record MdnsSrvResource(
    string Name,
    string Target,
    ushort Port,
    TimeSpan TimeToLive) : MdnsResource(Name, TimeToLive);

internal sealed record MdnsTxtResource(
    string Name,
    IReadOnlyList<string> Strings,
    TimeSpan TimeToLive) : MdnsResource(Name, TimeToLive);

internal sealed record MdnsAddressResource(
    string Name,
    IPAddress Address,
    TimeSpan TimeToLive) : MdnsResource(Name, TimeToLive);

internal sealed record DnsSdCacheUpdate(
    ImmutableArray<DnsSdServiceSnapshot> Snapshots,
    ImmutableArray<string> HostsToQuery);

internal interface IMdnsDiscoveryStack : IDisposable
{
    public event Action<string>? InstanceDiscovered;

    public event Action<string>? InstanceRemoved;

    public event Action<IReadOnlyList<MdnsResource>>? RecordsReceived;

    public void QueryHost(string hostName);

    public void QueryInstance(string instanceName);

    public void QueryInstances();

    public void Start();
}
