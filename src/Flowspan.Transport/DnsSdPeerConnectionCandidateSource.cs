using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class DnsSdServiceSnapshot
{
    private DnsSdServiceSnapshot(
        string instanceName,
        ushort port,
        ImmutableArray<IPAddress> addresses,
        ImmutableDictionary<string, string> textRecords)
    {
        InstanceName = instanceName;
        Port = port;
        Addresses = addresses;
        TextRecords = textRecords;
    }

    public ImmutableArray<IPAddress> Addresses { get; }

    public string InstanceName { get; }

    public ushort Port { get; }

    public IReadOnlyDictionary<string, string> TextRecords { get; }

    public static DnsSdServiceSnapshot Create(
        string instanceName,
        int port,
        IEnumerable<IPAddress> addresses,
        IReadOnlyDictionary<string, string> textRecords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(textRecords);
        if (Encoding.UTF8.GetByteCount(instanceName) > 255
            || instanceName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A DNS-SD instance name must fit one bounded canonical name.",
                nameof(instanceName));
        }

        if (port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        ImmutableArray<IPAddress> boundedAddresses = addresses
            .Select(static address => address
                ?? throw new ArgumentException("A DNS-SD address cannot be null."))
            .Distinct()
            .Take(33)
            .ToImmutableArray();
        if (boundedAddresses.IsDefaultOrEmpty || boundedAddresses.Length > 32)
        {
            throw new ArgumentException(
                "A DNS-SD snapshot must contain 1 to 32 addresses.",
                nameof(addresses));
        }

        if (textRecords.Count is < 1 or > 16)
        {
            throw new ArgumentException(
                "A DNS-SD snapshot must contain 1 to 16 TXT properties.",
                nameof(textRecords));
        }

        var boundedText = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in textRecords)
        {
            if (string.IsNullOrWhiteSpace(key)
                || value is null
                || Encoding.UTF8.GetByteCount($"{key}={value}")
                    > DnsSdDiscoveryOfferTxtCodec.MaximumTxtStringBytes
                || !boundedText.TryAdd(key, value))
            {
                throw new ArgumentException(
                    "A DNS-SD TXT property is invalid, oversized, or duplicated.",
                    nameof(textRecords));
            }
        }

        return new DnsSdServiceSnapshot(
            instanceName,
            checked((ushort)port),
            boundedAddresses,
            boundedText.ToImmutable());
    }
}

public interface IDnsSdServiceBrowser : IDisposable
{
    public event Action<DnsSdServiceSnapshot>? ServiceChanged;

    public event Action<string>? ServiceRemoved;

    public void Start();
}

public sealed class DnsSdPeerConnectionCandidateSource :
    IPeerConnectionCandidateSource,
    IDisposable
{
    public const string ServiceType = "_flowspan._tcp.local";
    private readonly IDnsSdServiceBrowser browser;
    private readonly Lock gate = new();
    private readonly DeviceId localDeviceId;
    private readonly Dictionary<DeviceId, int> nextCandidate = [];
    private readonly Dictionary<string, ObservedService> services =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;
    private readonly ITrustStore trustStore;
    private int disposed;

    public DnsSdPeerConnectionCandidateSource(
        DeviceId localDeviceId,
        ITrustStore trustStore,
        IDnsSdServiceBrowser browser,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(localDeviceId);
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(browser);
        this.localDeviceId = localDeviceId;
        this.trustStore = trustStore;
        this.browser = browser;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        browser.ServiceChanged += OnServiceChanged;
        browser.ServiceRemoved += OnServiceRemoved;
        try
        {
            browser.Start();
        }
        catch
        {
            browser.ServiceChanged -= OnServiceChanged;
            browser.ServiceRemoved -= OnServiceRemoved;
            browser.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        browser.ServiceChanged -= OnServiceChanged;
        browser.ServiceRemoved -= OnServiceRemoved;
        lock (gate)
        {
            services.Clear();
            nextCandidate.Clear();
        }

        browser.Dispose();
    }

    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? candidate)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        candidate = null;
        if (!trustStore.TryGet(peerDeviceId, out TrustRecord? currentTrust))
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (gate)
        {
            RemoveExpired(now);
            VerifiedPeerConnectionCandidate[] available = services.Values
                .Where(service => service.PeerDeviceId == peerDeviceId)
                .OrderBy(static service => service.InstanceName, StringComparer.Ordinal)
                .SelectMany(static service => service.Candidates)
                .Where(item =>
                    item.CandidateIdentity.HasSameKey(currentTrust.PeerIdentity)
                    && StringComparer.Ordinal.Equals(
                        item.Offer.IdentityFingerprint,
                        currentTrust.PeerIdentity.Fingerprint)
                    && item.Offer.Verify(item.CandidateIdentity, now))
                .ToArray();
            if (available.Length == 0)
            {
                nextCandidate.Remove(peerDeviceId);
                return false;
            }

            int index = nextCandidate.TryGetValue(peerDeviceId, out int current)
                ? current % available.Length
                : 0;
            candidate = available[index];
            nextCandidate[peerDeviceId] = (index + 1) % available.Length;
            return true;
        }
    }

    private void OnServiceChanged(DnsSdServiceSnapshot snapshot)
    {
        if (Volatile.Read(ref disposed) != 0
            || !DnsSdDiscoveryOfferTxtCodec.TryDecode(
                snapshot.TextRecords,
                out SignedDiscoveryOffer? offer)
            || offer.DeviceId == localDeviceId
            || snapshot.Port != offer.Port
            || !trustStore.TryGet(offer.DeviceId, out TrustRecord? trustRecord)
            || !StringComparer.Ordinal.Equals(
                offer.IdentityFingerprint,
                trustRecord.PeerIdentity.Fingerprint))
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        PublicDeviceIdentity candidateIdentity;
        try
        {
            candidateIdentity = new PublicDeviceIdentity(
                offer.DeviceId,
                offer.DisplayName,
                trustRecord.PeerIdentity.ExportSubjectPublicKeyInfo());
        }
        catch (Exception exception) when (
            exception is ArgumentException or CryptographicException)
        {
            return;
        }

        if (!offer.Verify(candidateIdentity, now))
        {
            return;
        }

        ImmutableArray<VerifiedPeerConnectionCandidate> candidates = snapshot.Addresses
            .Select(static address => address.IsIPv4MappedToIPv6
                ? address.MapToIPv4()
                : address)
            .Where(PeerConnectionAddressPolicy.IsUsable)
            .Distinct()
            .Select(address => VerifiedPeerConnectionCandidate.Create(
                new IPEndPoint(address, snapshot.Port),
                offer,
                candidateIdentity,
                now))
            .ToImmutableArray();
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            services[snapshot.InstanceName] = new ObservedService(
                snapshot.InstanceName,
                offer.DeviceId,
                offer.ExpiresAt,
                candidates);
        }
    }

    private void OnServiceRemoved(string instanceName)
    {
        if (Volatile.Read(ref disposed) != 0 || string.IsNullOrWhiteSpace(instanceName))
        {
            return;
        }

        lock (gate)
        {
            services.Remove(instanceName);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        string[] expired = services
            .Where(entry => now >= entry.Value.ExpiresAt)
            .Select(static entry => entry.Key)
            .ToArray();
        foreach (string instanceName in expired)
        {
            services.Remove(instanceName);
        }
    }

    private sealed record ObservedService(
        string InstanceName,
        DeviceId PeerDeviceId,
        DateTimeOffset ExpiresAt,
        ImmutableArray<VerifiedPeerConnectionCandidate> Candidates);
}
