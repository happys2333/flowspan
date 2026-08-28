using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class VerifiedPeerConnectionCandidate
{
    private readonly IPEndPoint endPoint;

    private VerifiedPeerConnectionCandidate(
        IPEndPoint endPoint,
        SignedDiscoveryOffer offer,
        PublicDeviceIdentity candidateIdentity)
    {
        this.endPoint = endPoint;
        Offer = offer;
        CandidateIdentity = candidateIdentity;
    }

    public PublicDeviceIdentity CandidateIdentity { get; }

    public IPEndPoint EndPoint => Clone(endPoint);

    public SignedDiscoveryOffer Offer { get; }

    public static VerifiedPeerConnectionCandidate Create(
        IPEndPoint endPoint,
        SignedDiscoveryOffer offer,
        PublicDeviceIdentity candidateIdentity,
        DateTimeOffset observedAt) => CreateCore(
            endPoint,
            offer,
            candidateIdentity,
            observedAt,
            afterValidation: null);

    internal static VerifiedPeerConnectionCandidate CreateForTesting(
        IPEndPoint endPoint,
        SignedDiscoveryOffer offer,
        PublicDeviceIdentity candidateIdentity,
        DateTimeOffset observedAt,
        Action afterValidation)
    {
        ArgumentNullException.ThrowIfNull(afterValidation);
        return CreateCore(
            endPoint,
            offer,
            candidateIdentity,
            observedAt,
            afterValidation);
    }

    private static VerifiedPeerConnectionCandidate CreateCore(
        IPEndPoint endPoint,
        SignedDiscoveryOffer offer,
        PublicDeviceIdentity candidateIdentity,
        DateTimeOffset observedAt,
        Action? afterValidation)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        IPEndPoint endPointSnapshot = Clone(endPoint);
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(candidateIdentity);
        if (endPointSnapshot.Port == 0
            || endPointSnapshot.Address.Equals(IPAddress.Any)
            || endPointSnapshot.Address.Equals(IPAddress.IPv6Any))
        {
            throw new ArgumentException(
                "A peer candidate requires a concrete address and port.",
                nameof(endPoint));
        }

        if (endPointSnapshot.Port != offer.Port)
        {
            throw new ArgumentException(
                "A peer candidate endpoint must use the signed discovery port.",
                nameof(endPoint));
        }

        if (!offer.Verify(candidateIdentity, observedAt))
        {
            throw new ArgumentException(
                "A peer candidate requires a valid signed discovery offer.",
                nameof(offer));
        }

        afterValidation?.Invoke();
        return new VerifiedPeerConnectionCandidate(
            endPointSnapshot,
            offer,
            candidateIdentity);
    }

    private static IPEndPoint Clone(IPEndPoint endPoint)
    {
        IPAddress sourceAddress = endPoint.Address;
        byte[] addressBytes = sourceAddress.GetAddressBytes();
        IPAddress address = sourceAddress.AddressFamily
            == AddressFamily.InterNetworkV6
                ? new IPAddress(
                    addressBytes,
                    sourceAddress.ScopeId)
                : new IPAddress(addressBytes);
        return new IPEndPoint(address, endPoint.Port);
    }
}

public interface IPeerConnectionCandidateSource
{
    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? candidate);
}

public interface IAuthenticatedTcpConnector
{
    public ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
        IPEndPoint remoteEndPoint,
        DeviceIdentity localIdentity,
        TrustRecord trustedPeer,
        IReadOnlyList<ProtocolVersion> supportedVersions,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default);
}

public sealed class SystemAuthenticatedTcpConnector : IAuthenticatedTcpConnector
{
    public ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
        IPEndPoint remoteEndPoint,
        DeviceIdentity localIdentity,
        TrustRecord trustedPeer,
        IReadOnlyList<ProtocolVersion> supportedVersions,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default) =>
        AuthenticatedTcpControlConnection.ConnectAsync(
            remoteEndPoint,
            localIdentity,
            trustedPeer,
            supportedVersions,
            handshakeTimeout,
            cancellationToken);
}

public interface IAuthenticatedControlSessionHandler
{
    public ValueTask RunAsync(
        AuthenticatedTcpControlConnection connection,
        CancellationToken cancellationToken = default);
}

public sealed class AuthenticatedPeerSessionProfile
{
    public AuthenticatedPeerSessionProfile(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IEnumerable<ProtocolVersion> supportedVersions,
        TimeSpan? handshakeTimeout = null,
        CapabilityRequirementMatch capabilityMatch = CapabilityRequirementMatch.All)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(supportedVersions);
        if (requiredCapabilities.Capabilities.Count == 0)
        {
            throw new ArgumentException(
                "An authenticated peer session must require at least one capability.",
                nameof(requiredCapabilities));
        }

        if (!Enum.IsDefined(capabilityMatch))
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityMatch));
        }

        ImmutableArray<ProtocolVersion> versions = supportedVersions
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (versions.IsDefaultOrEmpty
            || versions.Length > 16
            || versions.Any(static version => version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "An authenticated peer session must support 1 to 16 protocol versions.",
                nameof(supportedVersions));
        }

        TimeSpan timeout = handshakeTimeout
            ?? AuthenticatedTcpControlConnection.DefaultHandshakeTimeout;
        if (timeout <= TimeSpan.Zero
            || timeout > AuthenticatedTcpControlConnection.MaximumHandshakeTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(handshakeTimeout),
                "The handshake timeout is outside the supported range.");
        }

        PeerDeviceId = peerDeviceId;
        RequiredCapabilities = requiredCapabilities;
        CapabilityMatch = capabilityMatch;
        SupportedVersions = versions;
        HandshakeTimeout = timeout;
    }

    public TimeSpan HandshakeTimeout { get; }

    public CapabilityRequirementMatch CapabilityMatch { get; }

    public DeviceId PeerDeviceId { get; }

    public CapabilityGrant RequiredCapabilities { get; }

    public ImmutableArray<ProtocolVersion> SupportedVersions { get; }

    internal bool IsSatisfiedBy(CapabilityGrant grantedCapabilities) =>
        CapabilityMatch switch
        {
            CapabilityRequirementMatch.All =>
                RequiredCapabilities.Capabilities.All(grantedCapabilities.Allows),
            CapabilityRequirementMatch.Any =>
                RequiredCapabilities.Capabilities.Any(grantedCapabilities.Allows),
            _ => throw new InvalidOperationException(
                "The peer capability match mode is invalid."),
        };

    internal ValueTask<TrustSessionRegistration?> TryRegisterAsync(
        TrustSessionCoordinator trustSessions,
        IRevocablePeerSession session,
        CancellationToken cancellationToken) => CapabilityMatch switch
        {
            CapabilityRequirementMatch.All => trustSessions.TryRegisterAsync(
                PeerDeviceId,
                RequiredCapabilities,
                session,
                cancellationToken),
            CapabilityRequirementMatch.Any => trustSessions.TryRegisterAnyAsync(
                PeerDeviceId,
                RequiredCapabilities,
                session,
                cancellationToken),
            _ => throw new InvalidOperationException(
                "The peer capability match mode is invalid."),
        };
}

public sealed class AuthenticatedTcpPeerSessionAttempt :
    IAuthenticatedPeerSessionAttempt
{
    private readonly IPeerConnectionCandidateSource candidates;
    private readonly IAuthenticatedTcpConnector connector;
    private readonly IAuthenticatedControlSessionHandler handler;
    private readonly DeviceIdentity localIdentity;
    private readonly AuthenticatedPeerSessionProfile profile;
    private readonly TimeProvider timeProvider;
    private readonly TrustSessionCoordinator trustSessions;

    public AuthenticatedTcpPeerSessionAttempt(
        AuthenticatedPeerSessionProfile profile,
        DeviceIdentity localIdentity,
        TrustSessionCoordinator trustSessions,
        IPeerConnectionCandidateSource candidates,
        IAuthenticatedTcpConnector connector,
        IAuthenticatedControlSessionHandler handler,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(handler);
        this.candidates = candidates;
        this.connector = connector;
        this.handler = handler;
        this.localIdentity = localIdentity;
        this.profile = profile;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.trustSessions = trustSessions;
    }

    public async ValueTask<PeerSessionAttemptResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!trustSessions.TryGetCurrentTrust(
                profile.PeerDeviceId,
                out TrustRecord? trustRecord))
        {
            return PeerSessionAttemptResult.PermanentlyRejected(
                PeerReconnectStopReason.PeerNotTrusted);
        }

        if (!profile.IsSatisfiedBy(trustRecord.GrantedCapabilities))
        {
            return PeerSessionAttemptResult.PermanentlyRejected(
                PeerReconnectStopReason.CapabilityDenied);
        }

        if (!candidates.TryGet(
                profile.PeerDeviceId,
                out VerifiedPeerConnectionCandidate? candidate))
        {
            return PeerSessionAttemptResult.TransientFailure;
        }

        if (!trustRecord.PeerIdentity.HasSameKey(candidate.CandidateIdentity))
        {
            return PeerSessionAttemptResult.PermanentlyRejected(
                PeerReconnectStopReason.CandidateIdentityChanged);
        }

        if (!candidate.Offer.Verify(
                candidate.CandidateIdentity,
                timeProvider.GetUtcNow()))
        {
            return PeerSessionAttemptResult.TransientFailure;
        }

        try
        {
            await using AuthenticatedTcpControlConnection connection =
                await connector.ConnectAsync(
                    candidate.EndPoint,
                    localIdentity,
                    trustRecord,
                    profile.SupportedVersions,
                    profile.HandshakeTimeout,
                    cancellationToken).ConfigureAwait(false);
            using var revocableSession = new RevocableControlSession();
            TrustSessionRegistration? registration =
                await profile.TryRegisterAsync(
                    trustSessions,
                    revocableSession,
                    cancellationToken).ConfigureAwait(false);
            if (registration is null)
            {
                return PeerSessionAttemptResult.PermanentlyRejected(
                    trustSessions.TryGetCurrentTrust(profile.PeerDeviceId, out _)
                        ? PeerReconnectStopReason.CapabilityDenied
                        : PeerReconnectStopReason.PeerNotTrusted);
            }

            await using (registration)
            {
                try
                {
                    await revocableSession.RunAsync(
                        handler,
                        connection,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested
                    && revocableSession.StopReason is not null)
                {
                    return PeerSessionAttemptResult.PermanentlyRejected(
                        StopReasonFor(revocableSession.StopReason.Value));
                }
                catch (Exception exception) when (
                    exception is IOException
                        or SocketException
                        or TimeoutException
                        or CryptographicException)
                {
                    return revocableSession.StopReason is TrustSessionStopReason reason
                        ? PeerSessionAttemptResult.PermanentlyRejected(
                            StopReasonFor(reason))
                        : PeerSessionAttemptResult.AuthenticatedSessionEnded;
                }

                if (revocableSession.StopReason is TrustSessionStopReason stopReason)
                {
                    return PeerSessionAttemptResult.PermanentlyRejected(
                        StopReasonFor(stopReason));
                }

                return PeerSessionAttemptResult.AuthenticatedSessionEnded;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SessionHandshakeException exception)
        {
            PeerReconnectStopReason stopReason = exception.Failure switch
            {
                SessionHandshakeFailure.NoCommonProtocolVersion =>
                    PeerReconnectStopReason.ProtocolIncompatible,
                SessionHandshakeFailure.PeerIdentityChanged =>
                    PeerReconnectStopReason.CandidateIdentityChanged,
                _ => PeerReconnectStopReason.AuthenticationFailed,
            };
            return PeerSessionAttemptResult.PermanentlyRejected(stopReason);
        }
        catch (Exception exception) when (
            exception is IOException or SocketException or TimeoutException)
        {
            return PeerSessionAttemptResult.TransientFailure;
        }
    }

    private static PeerReconnectStopReason StopReasonFor(
        TrustSessionStopReason stopReason) => stopReason switch
        {
            TrustSessionStopReason.CapabilityRevoked =>
                PeerReconnectStopReason.CapabilityDenied,
            TrustSessionStopReason.PeerRevoked or TrustSessionStopReason.LocalShutdown =>
                PeerReconnectStopReason.PeerNotTrusted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stopReason),
                stopReason,
                "Unknown trust-session stop reason."),
        };

    private sealed class RevocableControlSession : IRevocablePeerSession, IDisposable
    {
        private readonly TaskCompletionSource completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource stop = new();
        private int stopReason = -1;

        public TrustSessionStopReason? StopReason
        {
            get
            {
                int value = Volatile.Read(ref stopReason);
                return value < 0 ? null : (TrustSessionStopReason)value;
            }
        }

        public async ValueTask RunAsync(
            IAuthenticatedControlSessionHandler handler,
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    stop.Token);
            try
            {
                await handler.RunAsync(
                    connection,
                    linkedCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                completed.TrySetResult();
            }
        }

        public async ValueTask StopAsync(TrustSessionStopReason reason)
        {
            if (!Enum.IsDefined(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            Interlocked.CompareExchange(ref stopReason, (int)reason, -1);
            try
            {
                stop.Cancel();
            }
            catch (AggregateException)
            {
                // The handler still observes cancellation; await its completion.
            }

            await completed.Task.ConfigureAwait(false);
        }

        public void Dispose() => stop.Dispose();
    }
}
