using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Security;

namespace Flowspan.Desktop;

internal interface IDesktopRemoteWindowHostAuthorizationInvalidationSink
{
    public void InvalidateAuthorizationPreparationNow();
}

internal interface IDesktopRemoteWindowHostAuthorizationRegistration :
    IAsyncDisposable
{
    public bool IsCurrent { get; }
}

internal sealed record DesktopRemoteWindowHostAuthorizationReservationResult
{
    private DesktopRemoteWindowHostAuthorizationReservationResult(
        LocalBoundaryResult boundary,
        IDesktopRemoteWindowHostAuthorizationRegistration? registration)
    {
        Boundary = boundary;
        Registration = registration;
    }

    public LocalBoundaryResult Boundary { get; }

    public IDesktopRemoteWindowHostAuthorizationRegistration? Registration
    {
        get;
    }

    public bool Reserved => Boundary.Succeeded
        && Registration?.IsCurrent == true;

    public static DesktopRemoteWindowHostAuthorizationReservationResult Confirmed(
        IDesktopRemoteWindowHostAuthorizationRegistration registration) => new(
            LocalBoundaryResult.Confirmed("mirror_capability_reserved"),
            registration ?? throw new ArgumentNullException(nameof(registration)));

    public static DesktopRemoteWindowHostAuthorizationReservationResult Rejected(
        string reasonCode) => new(
            LocalBoundaryResult.Failed(reasonCode),
            registration: null);
}

internal interface IDesktopRemoteWindowHostAuthorizationSource :
    IMirrorAuthorizationSource
{
    public ValueTask<DesktopRemoteWindowHostAuthorizationReservationResult>
        TryReservePreparationAsync(
            DeviceId peerDeviceId,
            string authenticatedPeerFingerprint,
            MirrorParticipantRole role,
            IDesktopRemoteWindowHostAuthorizationInvalidationSink invalidationSink,
            CancellationToken cancellationToken);
}

internal sealed class TrustMirrorAuthorizationSource(
    TrustSessionCoordinator trust) :
    IDesktopRemoteWindowHostAuthorizationSource
{
    private readonly TrustSessionCoordinator trust = trust
        ?? throw new ArgumentNullException(nameof(trust));

    public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
        trust.TryGetCurrentTrust(peerDeviceId, out TrustRecord? record)
            ? record.GrantedCapabilities
            : CapabilityGrant.None;

    public async ValueTask<DesktopRemoteWindowHostAuthorizationReservationResult>
        TryReservePreparationAsync(
            DeviceId peerDeviceId,
            string authenticatedPeerFingerprint,
            MirrorParticipantRole role,
            IDesktopRemoteWindowHostAuthorizationInvalidationSink invalidationSink,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedPeerFingerprint);
        ArgumentNullException.ThrowIfNull(invalidationSink);
        CapabilityGrant requiredCapabilities = role switch
        {
            MirrorParticipantRole.ViewOnly =>
                CapabilityGrant.Of(Capability.MirrorView),
            MirrorParticipantRole.DriverEligible => CapabilityGrant.Of(
                Capability.MirrorView,
                Capability.MirrorDrive),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        TrustPreparationReservationResult reserved =
            await trust.TryReservePreparationAsync(
                    peerDeviceId,
                    authenticatedPeerFingerprint,
                    requiredCapabilities,
                    new InvalidationSink(invalidationSink),
                    cancellationToken)
                .ConfigureAwait(false);
        return reserved.Status switch
        {
            TrustPreparationReservationStatus.Reserved
                when reserved.Registration is not null =>
                DesktopRemoteWindowHostAuthorizationReservationResult.Confirmed(
                    new Registration(reserved.Registration)),
            TrustPreparationReservationStatus.IdentityChanged =>
                DesktopRemoteWindowHostAuthorizationReservationResult.Rejected(
                    "authenticated_connection_stale"),
            TrustPreparationReservationStatus.PeerNotFound
                or TrustPreparationReservationStatus.CapabilityDenied =>
                DesktopRemoteWindowHostAuthorizationReservationResult.Rejected(
                    "mirror_capability_denied"),
            _ => DesktopRemoteWindowHostAuthorizationReservationResult.Rejected(
                "mirror_authorization_unavailable"),
        };
    }

    private sealed class InvalidationSink(
        IDesktopRemoteWindowHostAuthorizationInvalidationSink inner) :
        ITrustPreparationInvalidationSink
    {
        public void InvalidateTrustPreparationNow() =>
            inner.InvalidateAuthorizationPreparationNow();
    }

    private sealed class Registration(TrustPreparationRegistration inner) :
        IDesktopRemoteWindowHostAuthorizationRegistration
    {
        public bool IsCurrent => inner.IsCurrent;

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
