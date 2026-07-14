using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class DiscoveryBoundPairingDecisionSource : IPairingDecisionSource
{
    private readonly IPairingDecisionSource inner;
    private readonly UnverifiedPairingCandidate pinnedCandidate;
    private readonly TimeProvider timeProvider;

    public DiscoveryBoundPairingDecisionSource(
        UnverifiedPairingCandidate pinnedCandidate,
        IPairingDecisionSource inner,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedCandidate);
        ArgumentNullException.ThrowIfNull(inner);
        this.pinnedCandidate = pinnedCandidate;
        this.inner = inner;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<PairingDecision> DecideAsync(
        PairingConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PeerIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        if (pinnedCandidate.TrustState
                != PairingCandidateTrustState.UnverifiedPairingRequired
            || pinnedCandidate.Offer.DeviceId != request.PeerIdentity.DeviceId
            || !StringComparer.Ordinal.Equals(
                pinnedCandidate.Offer.IdentityFingerprint,
                request.PeerIdentity.Fingerprint)
            || !pinnedCandidate.Offer.Verify(
                request.PeerIdentity,
                timeProvider.GetUtcNow()))
        {
            return ValueTask.FromResult(PairingDecision.Reject);
        }

        return inner.DecideAsync(request, cancellationToken);
    }
}
