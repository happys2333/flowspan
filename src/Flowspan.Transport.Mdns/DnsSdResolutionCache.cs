using System.Collections.Immutable;
using System.Net;
using System.Text;

namespace Flowspan.Transport.Mdns;

internal sealed class DnsSdResolutionCache
{
    private const int MaximumAddressesPerHost = 32;
    private const int MaximumInstances = 128;
    private const int MaximumRecordsPerBatch = 256;
    private readonly Dictionary<string, HashSet<IPAddress>> addresses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InstanceState> instances =
        new(StringComparer.OrdinalIgnoreCase);

    public string[] Clear()
    {
        string[] removed = instances.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
        instances.Clear();
        addresses.Clear();
        return removed;
    }

    public DnsSdCacheUpdate Observe(IReadOnlyList<MdnsResource> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count > MaximumRecordsPerBatch)
        {
            return new DnsSdCacheUpdate([], []);
        }

        var hostsToQuery = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MdnsSrvResource srv in records.OfType<MdnsSrvResource>())
        {
            ObserveSrv(srv, hostsToQuery);
        }

        foreach (MdnsTxtResource txt in records.OfType<MdnsTxtResource>())
        {
            ObserveTxt(txt);
        }

        HashSet<string> referencedHosts = instances.Values
            .Where(static state => state.Target is not null)
            .Select(static state => state.Target!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MdnsAddressResource address in records.OfType<MdnsAddressResource>())
        {
            ObserveAddress(address, referencedHosts);
        }

        ImmutableArray<DnsSdServiceSnapshot>.Builder snapshots =
            ImmutableArray.CreateBuilder<DnsSdServiceSnapshot>();
        foreach ((string instanceName, InstanceState state) in instances
                     .OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (state.Port is not ushort port
                || state.Target is not string target
                || state.TextRecords is null
                || !addresses.TryGetValue(target, out HashSet<IPAddress>? hostAddresses)
                || hostAddresses.Count == 0)
            {
                continue;
            }

            snapshots.Add(DnsSdServiceSnapshot.Create(
                instanceName,
                port,
                hostAddresses
                    .OrderBy(static address => address.AddressFamily)
                    .ThenBy(static address => address.ToString(), StringComparer.Ordinal),
                state.TextRecords));
        }

        return new DnsSdCacheUpdate(
            snapshots.ToImmutable(),
            hostsToQuery.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    public bool RemoveInstance(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        string normalized = NormalizeName(instanceName);
        if (!instances.Remove(normalized, out InstanceState? removed))
        {
            return false;
        }

        RemoveUnreferencedHost(removed.Target);
        return true;
    }

    private static bool IsFlowspanInstance(string name) =>
        name.EndsWith(
            $".{DnsSdPeerConnectionCandidateSource.ServiceType}",
            StringComparison.OrdinalIgnoreCase)
        && name.Length > DnsSdPeerConnectionCandidateSource.ServiceType.Length + 1
        && Encoding.UTF8.GetByteCount(name) <= 255
        && !name.Any(char.IsControl);

    private static string NormalizeName(string name) => name.TrimEnd('.');

    private static Dictionary<string, string>? ParseTextRecords(
        IReadOnlyList<string> strings)
    {
        if (strings.Count is < 1 or > 16)
        {
            return null;
        }

        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string text in strings)
        {
            if (text is null
                || Encoding.UTF8.GetByteCount(text)
                    > DnsSdDiscoveryOfferTxtCodec.MaximumTxtStringBytes)
            {
                return null;
            }

            int separator = text.IndexOf('=');
            if (separator <= 0
                || string.IsNullOrWhiteSpace(text[..separator])
                || !properties.TryAdd(
                    text[..separator],
                    text[(separator + 1)..]))
            {
                return null;
            }
        }

        return properties;
    }

    private void ObserveAddress(
        MdnsAddressResource resource,
        HashSet<string> referencedHosts)
    {
        string hostName = NormalizeName(resource.Name);
        if (!referencedHosts.Contains(hostName))
        {
            return;
        }

        if (resource.TimeToLive <= TimeSpan.Zero)
        {
            if (addresses.TryGetValue(hostName, out HashSet<IPAddress>? current))
            {
                current.Remove(resource.Address);
                if (current.Count == 0)
                {
                    addresses.Remove(hostName);
                }
            }

            return;
        }

        if (!addresses.TryGetValue(hostName, out HashSet<IPAddress>? hostAddresses))
        {
            hostAddresses = [];
            addresses.Add(hostName, hostAddresses);
        }

        if (hostAddresses.Count < MaximumAddressesPerHost)
        {
            hostAddresses.Add(resource.Address);
        }
    }

    private void ObserveSrv(
        MdnsSrvResource resource,
        HashSet<string> hostsToQuery)
    {
        string instanceName = NormalizeName(resource.Name);
        if (!IsFlowspanInstance(instanceName))
        {
            return;
        }

        if (resource.TimeToLive <= TimeSpan.Zero)
        {
            RemoveInstance(instanceName);
            return;
        }

        string target = NormalizeName(resource.Target);
        if (string.IsNullOrWhiteSpace(target)
            || Encoding.UTF8.GetByteCount(target) > 255
            || target.Any(char.IsControl)
            || resource.Port == 0)
        {
            return;
        }

        InstanceState? state = GetOrCreate(instanceName);
        if (state is null)
        {
            return;
        }

        if (state.Target is not null
            && !StringComparer.OrdinalIgnoreCase.Equals(state.Target, target))
        {
            string oldTarget = state.Target;
            state.Target = target;
            RemoveUnreferencedHost(oldTarget);
        }
        else
        {
            state.Target = target;
        }
        state.Port = resource.Port;
        hostsToQuery.Add(target);
    }

    private void ObserveTxt(MdnsTxtResource resource)
    {
        string instanceName = NormalizeName(resource.Name);
        if (!IsFlowspanInstance(instanceName))
        {
            return;
        }

        if (resource.TimeToLive <= TimeSpan.Zero)
        {
            if (instances.TryGetValue(instanceName, out InstanceState? removed))
            {
                removed.TextRecords = null;
            }

            return;
        }

        if (instances.TryGetValue(instanceName, out InstanceState? state))
        {
            state.TextRecords = ParseTextRecords(resource.Strings);
        }
    }

    private InstanceState? GetOrCreate(string instanceName)
    {
        if (instances.TryGetValue(instanceName, out InstanceState? existing))
        {
            return existing;
        }

        if (instances.Count >= MaximumInstances)
        {
            return null;
        }

        var created = new InstanceState();
        instances.Add(instanceName, created);
        return created;
    }

    private void RemoveUnreferencedHost(string? hostName)
    {
        if (hostName is not null
            && !instances.Values.Any(state =>
                StringComparer.OrdinalIgnoreCase.Equals(state.Target, hostName)))
        {
            addresses.Remove(hostName);
        }
    }

    private sealed class InstanceState
    {
        public ushort? Port { get; set; }

        public string? Target { get; set; }

        public IReadOnlyDictionary<string, string>? TextRecords { get; set; }
    }
}
