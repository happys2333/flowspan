using System.Collections.Immutable;
using System.Net;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Transport;

public enum PairingCandidateTrustState
{
    UnverifiedPairingRequired,
    AlreadyPaired,
    IdentityChangedBlocked,
}

public sealed record UnverifiedPairingCandidate(
    string InstanceName,
    SignedDiscoveryOffer Offer,
    IPEndPoint EndPoint,
    PairingCandidateTrustState TrustState);

public sealed class DnsSdUnverifiedPairingCandidateSource : IDisposable
{
    private readonly IDnsSdServiceBrowser browser;
    private readonly Lock gate = new();
    private readonly DeviceId localDeviceId;
    private readonly Dictionary<string, ImmutableArray<UnverifiedPairingCandidate>> services =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;
    private readonly IPairingTrustAuthority trustStore;
    private int disposed;

    public event Action? SnapshotChanged;

    public DnsSdUnverifiedPairingCandidateSource(
        DeviceId localDeviceId,
        IPairingTrustAuthority trustStore,
        IDnsSdServiceBrowser browser,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(localDeviceId);
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(browser);
        this.localDeviceId = localDeviceId;
        this.browser = browser;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.trustStore = trustStore;
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

    public ImmutableArray<UnverifiedPairingCandidate> GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (gate)
        {
            RemoveExpired(timeProvider.GetUtcNow());
            return services.Values
                .SelectMany(static candidates => candidates)
                .Select(candidate => candidate with
                {
                    TrustState = GetTrustState(candidate.Offer),
                })
                .OrderBy(
                    static candidate => candidate.Offer.DeviceId.ToString(),
                    StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.EndPoint.Address.AddressFamily)
                .ThenBy(
                    static candidate => Convert.ToHexString(
                        candidate.EndPoint.Address.GetAddressBytes()),
                    StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.EndPoint.Port)
                .ThenBy(
                    static candidate => candidate.InstanceName,
                    StringComparer.Ordinal)
                .ThenBy(
                    static candidate => candidate.Offer.OfferDigest,
                    StringComparer.Ordinal)
                .ToImmutableArray();
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
        }

        browser.Dispose();
    }

    private void OnServiceChanged(DnsSdServiceSnapshot snapshot)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (Volatile.Read(ref disposed) != 0
            || !DnsSdDiscoveryOfferTxtCodec.TryDecode(
                snapshot.TextRecords,
                out SignedDiscoveryOffer? offer)
            || offer.DeviceId == localDeviceId
            || snapshot.Port != offer.Port
            || now < offer.IssuedAt.Subtract(SignedDiscoveryOffer.MaximumFutureClockSkew)
            || now >= offer.ExpiresAt)
        {
            return;
        }

        ImmutableArray<UnverifiedPairingCandidate> candidates = snapshot.Addresses
            .Select(static address => address.IsIPv4MappedToIPv6
                ? address.MapToIPv4()
                : address)
            .Where(PeerConnectionAddressPolicy.IsUsable)
            .Distinct()
            .Select(address => new UnverifiedPairingCandidate(
                snapshot.InstanceName,
                offer,
                new IPEndPoint(address, snapshot.Port),
                GetTrustState(offer)))
            .ToImmutableArray();
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        bool changed = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                services[snapshot.InstanceName] = candidates;
                changed = true;
            }
        }

        if (changed)
        {
            PublishSnapshotChanged();
        }
    }

    private void OnServiceRemoved(string instanceName)
    {
        if (Volatile.Read(ref disposed) != 0 || string.IsNullOrWhiteSpace(instanceName))
        {
            return;
        }

        bool removed;
        lock (gate)
        {
            removed = services.Remove(instanceName);
        }

        if (removed)
        {
            PublishSnapshotChanged();
        }
    }

    private void PublishSnapshotChanged()
    {
        foreach (Action subscriber in
                 SnapshotChanged?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                subscriber();
            }
            catch
            {
                // Presentation callbacks cannot weaken discovery validation.
            }
        }
    }

    private PairingCandidateTrustState GetTrustState(SignedDiscoveryOffer offer)
    {
        if (!trustStore.TryGet(offer.DeviceId, out TrustRecord? currentTrust))
        {
            return PairingCandidateTrustState.UnverifiedPairingRequired;
        }

        if (!StringComparer.Ordinal.Equals(
            currentTrust.PeerIdentity.Fingerprint,
            offer.IdentityFingerprint))
        {
            return PairingCandidateTrustState.IdentityChangedBlocked;
        }

        return PairingCandidateTrustState.AlreadyPaired;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        string[] expired = services
            .Where(static entry => entry.Value.IsDefaultOrEmpty)
            .Concat(services.Where(entry => entry.Value.All(
                candidate => now >= candidate.Offer.ExpiresAt)))
            .Select(static entry => entry.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string instanceName in expired)
        {
            services.Remove(instanceName);
        }
    }
}
