using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowPeerEndpointResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolvesSignedListenerPortForAuthenticatedInboundAddress()
    {
        using DeviceIdentity initiator = CreateIdentity("11111111", "Initiator");
        using DeviceIdentity responder = CreateIdentity("22222222", "Responder");
        await using TrustSessionCoordinator trust = CreateTrust(initiator);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(initiator, responder);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            int signedListenerPort = responderConnection.RemoteEndPoint.Port == 4747
                ? 4748
                : 4747;
            UnverifiedPairingCandidate observed = CreateCandidate(
                initiator,
                signedListenerPort,
                IPAddress.Loopback);
            ImmutableArray<UnverifiedPairingCandidate> candidates = [observed];
            var resolver = new DesktopRemoteWindowPeerEndpointResolver(
                trust,
                () => candidates,
                new FixedTimeProvider(Now));

            bool resolved = resolver.TryResolve(
                responderConnection,
                out VerifiedPeerConnectionCandidate? candidate);

            Assert.True(resolved);
            VerifiedPeerConnectionCandidate verified = Assert.IsType<
                VerifiedPeerConnectionCandidate>(candidate);
            Assert.Equal(
                new IPEndPoint(IPAddress.Loopback, signedListenerPort),
                verified.EndPoint);
            Assert.NotSame(observed.EndPoint, verified.EndPoint);
            observed.EndPoint.Port = signedListenerPort == 4747 ? 4748 : 4747;
            Assert.Equal(signedListenerPort, verified.EndPoint.Port);
        }
    }

    [Theory]
    [InlineData(4747)]
    [InlineData(4748)]
    public async Task RejectsAmbiguousSignedOffersForAuthenticatedAddress(
        int secondPort)
    {
        using DeviceIdentity initiator = CreateIdentity("11111111", "Initiator");
        using DeviceIdentity responder = CreateIdentity("22222222", "Responder");
        await using TrustSessionCoordinator trust = CreateTrust(initiator);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(initiator, responder);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            ImmutableArray<UnverifiedPairingCandidate> candidates =
            [
                CreateCandidate(initiator, 4747, IPAddress.Loopback, nonceByte: 0x42),
                CreateCandidate(
                    initiator,
                    secondPort,
                    IPAddress.Loopback,
                    nonceByte: 0x43),
            ];
            var resolver = new DesktopRemoteWindowPeerEndpointResolver(
                trust,
                () => candidates,
                new FixedTimeProvider(Now));

            Assert.False(resolver.TryResolve(responderConnection, out _));
        }
    }

    [Theory]
    [InlineData(CandidateFailure.Expired)]
    [InlineData(CandidateFailure.IdentityChanged)]
    [InlineData(CandidateFailure.ForgedIdentity)]
    [InlineData(CandidateFailure.WrongProtocol)]
    [InlineData(CandidateFailure.WrongAddress)]
    [InlineData(CandidateFailure.UnsignedPort)]
    public async Task RejectsCandidateOutsideAuthenticatedEndpointBinding(
        CandidateFailure failure)
    {
        using DeviceIdentity initiator = CreateIdentity("11111111", "Initiator");
        using DeviceIdentity responder = CreateIdentity("22222222", "Responder");
        using DeviceIdentity conflicting = DeviceIdentity.Generate(
            initiator.DeviceId,
            initiator.DisplayName);
        await using TrustSessionCoordinator trust = CreateTrust(initiator);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(initiator, responder);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            DeviceIdentity signer = failure == CandidateFailure.ForgedIdentity
                ? conflicting
                : initiator;
            UnverifiedPairingCandidate observed = CreateCandidate(
                signer,
                4747,
                failure == CandidateFailure.WrongAddress
                    ? IPAddress.Parse("192.0.2.10")
                    : IPAddress.Loopback,
                state: failure == CandidateFailure.IdentityChanged
                    ? PairingCandidateTrustState.IdentityChangedBlocked
                    : PairingCandidateTrustState.AlreadyPaired,
                protocolVersions: failure == CandidateFailure.WrongProtocol
                    ? [ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion]
                    : [ProtocolFeatures.RemoteWindowPreparationMinimumVersion],
                issuedAt: failure == CandidateFailure.Expired
                    ? Now.Subtract(TimeSpan.FromMinutes(2))
                    : Now.Subtract(TimeSpan.FromSeconds(1)));
            if (failure == CandidateFailure.UnsignedPort)
            {
                observed.EndPoint.Port++;
            }

            ImmutableArray<UnverifiedPairingCandidate> candidates = [observed];
            var resolver = new DesktopRemoteWindowPeerEndpointResolver(
                trust,
                () => candidates,
                new FixedTimeProvider(Now));

            Assert.False(resolver.TryResolve(responderConnection, out _));
        }
    }

    [Fact]
    public async Task RejectsCandidateWhenAuthenticatedPeerIsNotCurrentlyTrusted()
    {
        using DeviceIdentity initiator = CreateIdentity("11111111", "Initiator");
        using DeviceIdentity responder = CreateIdentity("22222222", "Responder");
        await using var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(initiator, responder);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            ImmutableArray<UnverifiedPairingCandidate> candidates =
                [CreateCandidate(initiator, 4747, IPAddress.Loopback)];
            var resolver = new DesktopRemoteWindowPeerEndpointResolver(
                trust,
                () => candidates,
                new FixedTimeProvider(Now));

            Assert.False(resolver.TryResolve(responderConnection, out _));
        }
    }

    [Fact]
    public async Task NormalizesAuthenticatedIpv4MappedRemoteAddress()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, IPAddress.Parse("192.0.2.10"))];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        IPAddress mappedRemoteAddress = IPAddress.Parse("::ffff:192.0.2.10");

        bool resolved = resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(mappedRemoteAddress, 62000),
            out VerifiedPeerConnectionCandidate? candidate);

        Assert.True(resolved);
        Assert.Equal(
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 4747),
            Assert.IsType<VerifiedPeerConnectionCandidate>(candidate).EndPoint);
    }

    [Fact]
    public async Task RequiresAndPreservesAuthenticatedIpv6Scope()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        byte[] linkLocalBytes = IPAddress.Parse("fe80::1234").GetAddressBytes();
        var observedAddress = new IPAddress(linkLocalBytes, scopeid: 7);
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, observedAddress)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));

        bool resolved = resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(new IPAddress(linkLocalBytes, scopeid: 7), 62000),
            out VerifiedPeerConnectionCandidate? candidate);
        bool wrongScopeResolved = resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(new IPAddress(linkLocalBytes, scopeid: 8), 62000),
            out _);

        Assert.True(resolved);
        Assert.Equal(
            7,
            Assert.IsType<VerifiedPeerConnectionCandidate>(candidate)
                .EndPoint.Address.ScopeId);
        Assert.False(wrongScopeResolved);
    }

    [Fact]
    public async Task SnapshotsObservedEndpointBeforeAddressComparisonSideEffects()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPEndPoint? mutableEndpoint = null;
        var mutatingAddress = new MutatingIPAddress(
            IPAddress.Parse("192.0.2.10"),
            () => mutableEndpoint!.Address = IPAddress.Parse("192.0.2.20"));
        mutableEndpoint = new IPEndPoint(mutatingAddress, 4747);
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            peer,
            mutableEndpoint.Port,
            [ProtocolFeatures.RemoteWindowPreparationMinimumVersion],
            Now.Subtract(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat((byte)0x44, SignedDiscoveryOffer.NonceLength).ToArray());
        ImmutableArray<UnverifiedPairingCandidate> candidates =
        [
            new(
                $"flowspan-{peer.DeviceId}",
                offer,
                mutableEndpoint,
                PairingCandidateTrustState.AlreadyPaired),
        ];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));

        bool resolved = resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 62000),
            out VerifiedPeerConnectionCandidate? candidate);

        Assert.True(resolved);
        Assert.Equal(
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 4747),
            Assert.IsType<VerifiedPeerConnectionCandidate>(candidate).EndPoint);
    }

    [Fact]
    public async Task AcceptsSignedDisplayNameRefreshForCurrentTrustedKey()
    {
        using DeviceIdentity paired = CreateIdentity("11111111", "Paired name");
        byte[] privateKey = paired.ExportPkcs8ForSecretStore();
        using DeviceIdentity renamed = DeviceIdentity.ImportPkcs8(
            paired.DeviceId,
            "Renamed peer",
            privateKey);
        CryptographicOperations.ZeroMemory(privateKey);
        await using TrustSessionCoordinator trust = CreateTrust(paired);
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(renamed, 4747, IPAddress.Parse("192.0.2.10"))];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));

        bool resolved = resolver.TryResolve(
            paired.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 62000),
            out VerifiedPeerConnectionCandidate? candidate);

        Assert.True(resolved);
        VerifiedPeerConnectionCandidate verified = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        Assert.Equal("Renamed peer", verified.CandidateIdentity.DisplayName);
        Assert.True(verified.CandidateIdentity.HasSameKey(paired.PublicIdentity));
    }

    [Fact]
    public async Task IsCurrentAcceptsRenewedOfferForSameEndpointAndKey()
    {
        using DeviceIdentity paired = CreateIdentity("11111111", "Paired name");
        byte[] privateKey = paired.ExportPkcs8ForSecretStore();
        using DeviceIdentity renamed = DeviceIdentity.ImportPkcs8(
            paired.DeviceId,
            "Renamed peer",
            privateKey);
        CryptographicOperations.ZeroMemory(privateKey);
        await using TrustSessionCoordinator trust = CreateTrust(paired);
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(paired, 4747, IPAddress.Parse("192.0.2.10"))];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            paired.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        candidates =
        [
            CreateCandidate(
                renamed,
                4747,
                IPAddress.Parse("192.0.2.10"),
                nonceByte: 0x43),
        ];

        Assert.True(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsChangedSignedListenerPort()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPAddress address = IPAddress.Parse("192.0.2.10");
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, address)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(address, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        candidates = [CreateCandidate(peer, 4748, address, nonceByte: 0x43)];

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsChangedAddress()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPAddress pinnedAddress = IPAddress.Parse("192.0.2.10");
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, pinnedAddress)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(pinnedAddress, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        candidates =
        [
            CreateCandidate(
                peer,
                4747,
                IPAddress.Parse("192.0.2.11"),
                nonceByte: 0x43),
        ];

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsChangedIpv6Scope()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        byte[] addressBytes = IPAddress.Parse("fe80::1234").GetAddressBytes();
        var pinnedAddress = new IPAddress(addressBytes, scopeid: 7);
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, pinnedAddress)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(pinnedAddress, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        candidates =
        [
            CreateCandidate(
                peer,
                4747,
                new IPAddress(addressBytes, scopeid: 8),
                nonceByte: 0x43),
        ];

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsRevokedTrust()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPAddress address = IPAddress.Parse("192.0.2.10");
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, address)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(address, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        Assert.True(await trust.RevokePeerAsync(peer.DeviceId));

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsReplacementIdentityForSameDeviceId()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        using DeviceIdentity replacement = DeviceIdentity.Generate(
            peer.DeviceId,
            peer.DisplayName);
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPAddress address = IPAddress.Parse("192.0.2.10");
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, address)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(address, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        Assert.True(await trust.RevokePeerAsync(peer.DeviceId));
        Assert.Equal(
            TrustRegistrationResult.Added,
            await trust.RegisterAsync(CreateTrustRecord(replacement)));
        candidates =
            [CreateCandidate(replacement, 4747, address, nonceByte: 0x43)];

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsOfferThatRemovedExactNegotiatedProtocol()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPAddress address = IPAddress.Parse("192.0.2.10");
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, address)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(address, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        candidates =
        [
            CreateCandidate(
                peer,
                4747,
                address,
                nonceByte: 0x43,
                protocolVersions:
                    [ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion]),
        ];

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public async Task IsCurrentRejectsAmbiguousCurrentOffers()
    {
        using DeviceIdentity peer = CreateIdentity("11111111", "Peer");
        await using TrustSessionCoordinator trust = CreateTrust(peer);
        IPAddress address = IPAddress.Parse("192.0.2.10");
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(peer, 4747, address)];
        var resolver = new DesktopRemoteWindowPeerEndpointResolver(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));
        Assert.True(resolver.TryResolve(
            peer.PublicIdentity,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            new IPEndPoint(address, 62000),
            out VerifiedPeerConnectionCandidate? candidate));
        VerifiedPeerConnectionCandidate pinned = Assert.IsType<
            VerifiedPeerConnectionCandidate>(candidate);
        candidates =
        [
            CreateCandidate(peer, 4747, address, nonceByte: 0x43),
            CreateCandidate(peer, 4747, address, nonceByte: 0x44),
        ];

        Assert.False(resolver.IsCurrent(
            pinned,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    private static DeviceIdentity CreateIdentity(string prefix, string displayName) =>
        DeviceIdentity.Generate(
            DeviceId.Parse($"{prefix}-0000-0000-0000-000000000000"),
            displayName);

    private static TrustSessionCoordinator CreateTrust(DeviceIdentity peer)
    {
        var store = new InMemoryTrustStore();
        store.Register(CreateTrustRecord(peer));
        return new TrustSessionCoordinator(store);
    }

    private static TrustRecord CreateTrustRecord(DeviceIdentity peer) => new(
        peer.PublicIdentity,
        Now,
        CapabilityGrant.Of(Capability.MirrorView));

    private static UnverifiedPairingCandidate CreateCandidate(
        DeviceIdentity peer,
        int port,
        IPAddress address,
        byte nonceByte = 0x42,
        PairingCandidateTrustState state = PairingCandidateTrustState.AlreadyPaired,
        IEnumerable<ProtocolVersion>? protocolVersions = null,
        DateTimeOffset? issuedAt = null)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            peer,
            port,
            protocolVersions ?? [ProtocolFeatures.RemoteWindowPreparationMinimumVersion],
            issuedAt ?? Now.Subtract(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat(nonceByte, SignedDiscoveryOffer.NonceLength).ToArray());
        return new UnverifiedPairingCandidate(
            $"flowspan-{peer.DeviceId}",
            offer,
            new IPEndPoint(address, port),
            state);
    }

    private static async Task<(
        AuthenticatedTcpControlConnection Initiator,
        AuthenticatedTcpControlConnection Responder)> CreateControlPairAsync(
        DeviceIdentity initiator,
        DeviceIdentity responder)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responder,
                CreateTrustRecord(initiator),
                [ProtocolFeatures.RemoteWindowPreparationMinimumVersion])
            .AsTask();
        AuthenticatedTcpControlConnection? initiatorConnection = null;
        try
        {
            initiatorConnection = await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiator,
                CreateTrustRecord(responder),
                [ProtocolFeatures.RemoteWindowPreparationMinimumVersion]);
            AuthenticatedTcpControlConnection responderConnection = await accepting;
            return (initiatorConnection, responderConnection);
        }
        catch
        {
            if (initiatorConnection is not null)
            {
                await initiatorConnection.DisposeAsync();
            }

            throw;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutatingIPAddress(IPAddress address, Action onEquals) :
        IPAddress(address.GetAddressBytes())
    {
        public override bool Equals(object? comparand)
        {
            onEquals();
            return base.Equals(comparand);
        }

        public override int GetHashCode() => base.GetHashCode();
    }

    public enum CandidateFailure
    {
        Expired,
        IdentityChanged,
        ForgedIdentity,
        WrongProtocol,
        WrongAddress,
        UnsignedPort,
    }
}
