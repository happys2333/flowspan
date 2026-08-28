using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal sealed class DesktopRemoteWindowPeerEndpointResolver :
    IVerifiedPeerConnectionCandidateValidator
{
    private readonly Func<ImmutableArray<UnverifiedPairingCandidate>> getCandidates;
    private readonly TimeProvider timeProvider;
    private readonly TrustSessionCoordinator trust;

    public DesktopRemoteWindowPeerEndpointResolver(
        TrustSessionCoordinator trust,
        Func<ImmutableArray<UnverifiedPairingCandidate>> getCandidates,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(getCandidates);
        this.trust = trust;
        this.getCandidates = getCandidates;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryResolve(
        AuthenticatedTcpControlConnection connection,
        out VerifiedPeerConnectionCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return TryResolve(
            connection.PeerIdentity,
            connection.ProtocolVersion,
            connection.RemoteEndPoint,
            out candidate);
    }

    public bool IsCurrent(
        VerifiedPeerConnectionCandidate candidate,
        ProtocolVersion protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.Offer.ProtocolVersions.Contains(protocolVersion))
        {
            return false;
        }

        IPEndPoint pinnedEndPoint = CloneEndpoint(candidate.EndPoint);
        if (!TryResolve(
                candidate.CandidateIdentity,
                protocolVersion,
                pinnedEndPoint,
                out VerifiedPeerConnectionCandidate? current)
            || current is null
            || !current.CandidateIdentity.HasSameKey(candidate.CandidateIdentity))
        {
            return false;
        }

        IPEndPoint currentEndPoint = current.EndPoint;
        return currentEndPoint.Port == pinnedEndPoint.Port
            && NormalizeAddress(currentEndPoint.Address).Equals(
                NormalizeAddress(pinnedEndPoint.Address));
    }

    public bool TryResolve(
        PublicDeviceIdentity authenticatedPeer,
        ProtocolVersion protocolVersion,
        IPEndPoint remoteEndPoint,
        out VerifiedPeerConnectionCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPeer);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        candidate = null;
        if (!trust.TryGetCurrentTrust(
                authenticatedPeer.DeviceId,
                out TrustRecord? trustRecord)
            || !trustRecord.PeerIdentity.HasSameKey(authenticatedPeer))
        {
            return false;
        }

        ImmutableArray<UnverifiedPairingCandidate> snapshot = getCandidates();
        if (snapshot.IsDefaultOrEmpty)
        {
            return false;
        }

        IPAddress remoteAddress = NormalizeAddress(remoteEndPoint.Address);
        DateTimeOffset now = timeProvider.GetUtcNow();
        VerifiedPeerConnectionCandidate? resolved = null;
        string? resolvedOfferDigest = null;
        foreach (UnverifiedPairingCandidate? observed in snapshot)
        {
            if (observed is null)
            {
                continue;
            }

            IPEndPoint observedEndPoint = observed.EndPoint;
            IPEndPoint endpointSnapshot = CloneEndpoint(observedEndPoint);
            if (observed.TrustState != PairingCandidateTrustState.AlreadyPaired
                || observed.Offer.DeviceId != authenticatedPeer.DeviceId
                || !StringComparer.Ordinal.Equals(
                    observed.Offer.IdentityFingerprint,
                    trustRecord.PeerIdentity.Fingerprint)
                || !observed.Offer.ProtocolVersions.Contains(protocolVersion)
                || !endpointSnapshot.Address.Equals(remoteAddress))
            {
                continue;
            }

            try
            {
                var candidateIdentity = new PublicDeviceIdentity(
                    observed.Offer.DeviceId,
                    observed.Offer.DisplayName,
                    trustRecord.PeerIdentity.ExportSubjectPublicKeyInfo());
                VerifiedPeerConnectionCandidate verified =
                    VerifiedPeerConnectionCandidate.Create(
                    endpointSnapshot,
                    observed.Offer,
                    candidateIdentity,
                    now);
                if (resolved is null)
                {
                    resolved = verified;
                    resolvedOfferDigest = observed.Offer.OfferDigest;
                }
                else if (resolved.EndPoint.Port != verified.EndPoint.Port
                    || !StringComparer.Ordinal.Equals(
                        resolvedOfferDigest,
                        observed.Offer.OfferDigest))
                {
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or CryptographicException)
            {
                // Snapshot trust labels never replace current signature validation.
            }
        }

        candidate = resolved;
        return candidate is not null;
    }

    private static IPEndPoint CloneEndpoint(IPEndPoint endpoint)
    {
        IPAddress address = endpoint.Address;
        int port = endpoint.Port;
        return new IPEndPoint(CloneAddress(address), port);
    }

    private static IPAddress CloneAddress(IPAddress address)
    {
        IPAddress normalized = NormalizeAddress(address);
        return normalized.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? new IPAddress(normalized.GetAddressBytes(), normalized.ScopeId)
            : new IPAddress(normalized.GetAddressBytes());
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
