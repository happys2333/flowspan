using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopTrustedPeerConnectionsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SmallerDeviceIdOwnsConnectorWhileOtherSideWaitsInbound()
    {
        using DeviceIdentity localConnector = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        var store = CreateTrustStore(remote, Capability.ActivityOffer);
        await using var trust = new TrustSessionCoordinator(store);
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            localConnector.DeviceId,
            trust,
            static () => [],
            loops);

        connections.Start();

        DesktopTrustedPeerConnectionSnapshot connector =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(DesktopTrustedPeerConnectionState.WaitingForPeer, connector.State);
        Assert.Single(loops.Created);

        await connections.DisposeAsync();

        using DeviceIdentity localListener = CreateIdentity("99999999", "Local");
        await using var listenerTrust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityOffer));
        var listenerLoops = new FakeReconnectLoopFactory();
        await using var listenerConnections =
            new DesktopTrustedPeerConnectionCoordinator(
                localListener.DeviceId,
                listenerTrust,
                static () => [],
                listenerLoops);

        listenerConnections.Start();

        DesktopTrustedPeerConnectionSnapshot listener =
            Assert.Single(listenerConnections.GetSnapshot());
        Assert.Equal(
            DesktopTrustedPeerConnectionState.WaitingForInbound,
            listener.State);
        Assert.Empty(listenerLoops.Created);
    }

    [Fact]
    public async Task ReceiveOnlyGrantStillAdmitsElectedControlChannelConnector()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityReceive));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);

        connections.Start();

        DesktopTrustedPeerConnectionSnapshot snapshot =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(
            DesktopTrustedPeerConnectionState.WaitingForPeer,
            snapshot.State);
        Assert.Single(loops.Created);
    }

    [Fact]
    public async Task ReplaceOnlyGrantStillAdmitsElectedControlChannelConnector()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityReplace));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);

        connections.Start();

        DesktopTrustedPeerConnectionSnapshot snapshot =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(
            DesktopTrustedPeerConnectionState.WaitingForPeer,
            snapshot.State);
        Assert.Single(loops.Created);
    }

    [Fact]
    public async Task SwapOnlyGrantStillAdmitsElectedControlChannelConnector()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivitySwap));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);

        connections.Start();

        DesktopTrustedPeerConnectionSnapshot snapshot =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(
            DesktopTrustedPeerConnectionState.WaitingForPeer,
            snapshot.State);
        Assert.Single(loops.Created);
    }

    [Fact]
    public async Task MissingActivityControlGrantIsPolicyIdleAndNeverContacted()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);

        connections.Start();

        DesktopTrustedPeerConnectionSnapshot snapshot =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(
            DesktopTrustedPeerConnectionState.CapabilityRequired,
            snapshot.State);
        Assert.Equal(
            "IDLE — ACTIVITY CONTROL CAPABILITY NOT GRANTED",
            snapshot.StatusLabel);
        Assert.Contains(
            "activity.offer, activity.receive, activity.replace, or activity.swap",
            snapshot.StatusDescription,
            StringComparison.Ordinal);
        Assert.Empty(loops.Created);
    }

    [Fact]
    public async Task WorkerProgressIsPerPeerAndNeverClaimsSharing()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityOffer));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();
        FakeReconnectLoop loop = Assert.Single(loops.Created);

        loop.Report(DesktopPeerReconnectProgress.Authenticating);
        Assert.Equal(
            DesktopTrustedPeerConnectionState.Authenticating,
            Assert.Single(connections.GetSnapshot()).State);

        using (connections.TrackAuthenticatedSession(
                   remote.DeviceId,
                   ProtocolFeatures.SecureSessionRekeyMinimumVersion))
        {
            DesktopTrustedPeerConnectionSnapshot authenticated =
                Assert.Single(connections.GetSnapshot());
            Assert.Equal(
                DesktopTrustedPeerConnectionState.AuthenticatedIdle,
                authenticated.State);
            Assert.Contains("NOT SHARING", authenticated.StatusLabel);
            Assert.False(authenticated.IsLegacyCompatibilityMode);
            Assert.False(authenticated.IsReconnectAtKeyLimitMode);
            Assert.Contains("live rekey", authenticated.StatusDescription);
            connections.NotifyCandidatesChanged();
            Assert.Equal(0, loop.DiscoverySignalCount);
        }

        loop.Report(DesktopPeerReconnectProgress.Retrying(TimeSpan.FromSeconds(2)));
        DesktopTrustedPeerConnectionSnapshot retry =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(DesktopTrustedPeerConnectionState.Retrying, retry.State);
        Assert.Equal(TimeSpan.FromSeconds(2), retry.RetryDelay);
    }

    [Fact]
    public async Task ProtocolOnePointTwoNamesReconnectAtKeyLimitMode()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityOffer));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();

        using IDisposable session = connections.TrackAuthenticatedSession(
            remote.DeviceId,
            ProtocolFeatures.SecureSessionFinishedMinimumVersion);
        DesktopTrustedPeerConnectionSnapshot snapshot =
            Assert.Single(connections.GetSnapshot());

        Assert.False(snapshot.IsLegacyCompatibilityMode);
        Assert.True(snapshot.IsReconnectAtKeyLimitMode);
        Assert.Contains("ENCRYPTED FINISHED", snapshot.StatusLabel);
        Assert.Contains("RECONNECT-AT-KEY-LIMIT", snapshot.StatusLabel);
        Assert.Contains("predates live rekey", snapshot.StatusDescription);
        Assert.Contains("fresh authenticated connection", snapshot.StatusDescription);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LegacyAuthenticatedSessionNamesDegradedSecurityMode(int minor)
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityOffer));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();

        using IDisposable session = connections.TrackAuthenticatedSession(
            remote.DeviceId,
            new ProtocolVersion(1, minor));
        DesktopTrustedPeerConnectionSnapshot snapshot =
            Assert.Single(connections.GetSnapshot());

        Assert.True(snapshot.IsLegacyCompatibilityMode);
        Assert.Equal(
            new ProtocolVersion(1, minor),
            Assert.Single(snapshot.ActiveProtocolVersions));
        Assert.Contains("LEGACY COMPATIBILITY", snapshot.StatusLabel);
        Assert.Contains("without encrypted Finished", snapshot.StatusDescription);
        Assert.Contains($"1.{minor}", snapshot.StatusDescription);
    }

    [Fact]
    public async Task ConflictingDiscoveryFingerprintIsBlockedAndLatched()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity trusted = CreateIdentity("22222222", "Trusted");
        using DeviceIdentity conflicting = DeviceIdentity.Generate(
            trusted.DeviceId,
            "Claimed replacement");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(trusted, Capability.ActivityOffer));
        ImmutableArray<UnverifiedPairingCandidate> candidates =
            [CreateCandidate(conflicting, PairingCandidateTrustState.IdentityChangedBlocked)];
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            () => candidates,
            loops);
        connections.Start();

        connections.NotifyCandidatesChanged();
        DesktopTrustedPeerConnectionSnapshot warned =
            Assert.Single(connections.GetSnapshot());
        Assert.True(warned.HasIdentityWarning);
        Assert.Equal(trusted.PublicIdentity.Fingerprint, warned.ExpectedFingerprint);
        Assert.Equal(conflicting.PublicIdentity.Fingerprint, warned.ConflictingFingerprint);
        Assert.Equal(1, Assert.Single(loops.Created).DiscoverySignalCount);

        candidates = [];
        connections.NotifyCandidatesChanged();

        DesktopTrustedPeerConnectionSnapshot latched =
            Assert.Single(connections.GetSnapshot());
        Assert.True(latched.HasIdentityWarning);
        Assert.Equal(conflicting.PublicIdentity.Fingerprint, latched.ConflictingFingerprint);
    }

    [Fact]
    public async Task HandshakeIdentityRejectionWarnsWithoutObservedFingerprint()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityOffer));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();

        Assert.Single(loops.Created).Complete(
            PeerReconnectStopReason.CandidateIdentityChanged);

        await WaitUntilAsync(
            () => Assert.Single(connections.GetSnapshot()).HasIdentityWarning);
        DesktopTrustedPeerConnectionSnapshot warning =
            Assert.Single(connections.GetSnapshot());
        Assert.Equal(
            DesktopTrustedPeerConnectionState.PermanentlyBlocked,
            warning.State);
        Assert.Null(warning.ConflictingFingerprint);
        Assert.Equal(
            PeerReconnectStopReason.CandidateIdentityChanged,
            warning.StopReason);
    }

    [Fact]
    public async Task UnexpectedWorkerFailureIsSanitizedAndStopsAutomaticRetry()
    {
        const string canary = "CANARY_RECONNECT_INTERNAL";
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        await using var trust = new TrustSessionCoordinator(
            CreateTrustStore(remote, Capability.ActivityOffer));
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();

        Assert.Single(loops.Created).Fail(new InvalidOperationException(canary));

        await WaitUntilAsync(() =>
            Assert.Single(connections.GetSnapshot()).State
                == DesktopTrustedPeerConnectionState.Unavailable);
        DesktopTrustedPeerConnectionSnapshot unavailable =
            Assert.Single(connections.GetSnapshot());
        Assert.DoesNotContain(canary, unavailable.StatusLabel);
        Assert.DoesNotContain(canary, unavailable.StatusDescription);
        Assert.Null(unavailable.StopReason);
    }

    [Fact]
    public async Task TrustReconcileStartsAfterGrantAndDrainsAfterDowngradeOrRevoke()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity remote = CreateIdentity("22222222", "Remote");
        var store = CreateTrustStore(remote);
        await using var trust = new TrustSessionCoordinator(store);
        var loops = new FakeReconnectLoopFactory();
        await using var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();
        Assert.Empty(loops.Created);

        Assert.Equal(
            TrustMutationResult.Applied,
            await trust.UpdateCapabilitiesAsync(
                remote.DeviceId,
                remote.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.ActivityOffer)));
        await connections.RefreshTrustAsync();
        FakeReconnectLoop first = Assert.Single(loops.Created);

        Assert.Equal(
            TrustMutationResult.Applied,
            await trust.UpdateCapabilitiesAsync(
                remote.DeviceId,
                remote.PublicIdentity.Fingerprint,
                CapabilityGrant.None));
        await connections.RefreshTrustAsync();
        Assert.True(first.Disposed);
        Assert.Equal(
            DesktopTrustedPeerConnectionState.CapabilityRequired,
            Assert.Single(connections.GetSnapshot()).State);

        Assert.Equal(
            TrustMutationResult.Applied,
            await trust.RevokePeerAsync(
                remote.DeviceId,
                remote.PublicIdentity.Fingerprint));
        await connections.RefreshTrustAsync();
        Assert.Empty(connections.GetSnapshot());
    }

    [Fact]
    public async Task DisposeCancelsAndAwaitsEveryReconnectLoop()
    {
        using DeviceIdentity local = CreateIdentity("11111111", "Local");
        using DeviceIdentity first = CreateIdentity("22222222", "First");
        using DeviceIdentity second = CreateIdentity("33333333", "Second");
        var store = CreateTrustStore(first, Capability.ActivityOffer);
        store.Register(new TrustRecord(
            second.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var trust = new TrustSessionCoordinator(store);
        var loops = new FakeReconnectLoopFactory();
        var connections = new DesktopTrustedPeerConnectionCoordinator(
            local.DeviceId,
            trust,
            static () => [],
            loops);
        connections.Start();
        Assert.Equal(2, loops.Created.Count);

        await connections.DisposeAsync();

        Assert.All(loops.Created, loop => Assert.True(loop.Disposed));
    }

    [Fact]
    public async Task CandidateSourceReturnsOnlyCurrentTrustSignedOffer()
    {
        using DeviceIdentity trusted = CreateIdentity("22222222", "Trusted");
        using DeviceIdentity conflicting = DeviceIdentity.Generate(
            trusted.DeviceId,
            "Conflict");
        var store = CreateTrustStore(trusted, Capability.ActivityOffer);
        await using var trust = new TrustSessionCoordinator(store);
        ImmutableArray<UnverifiedPairingCandidate> candidates =
        [
            CreateCandidate(
                conflicting,
                PairingCandidateTrustState.IdentityChangedBlocked,
                IPAddress.Parse("192.0.2.20")),
            CreateCandidate(
                trusted,
                PairingCandidateTrustState.AlreadyPaired,
                IPAddress.Parse("192.0.2.10")),
        ];
        var source = new DesktopTrustedPeerCandidateSource(
            trust,
            () => candidates,
            new FixedTimeProvider(Now));

        bool found = source.TryGet(trusted.DeviceId, out var candidate);

        Assert.True(found);
        Assert.NotNull(candidate);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), candidate.EndPoint.Address);
        Assert.True(candidate.CandidateIdentity.HasSameKey(trusted.PublicIdentity));

        candidates =
            [CreateCandidate(conflicting, PairingCandidateTrustState.IdentityChangedBlocked)];
        Assert.False(source.TryGet(trusted.DeviceId, out _));
    }

    [Theory]
    [InlineData(Capability.ActivityReceive, Capability.ActivityOffer)]
    [InlineData(Capability.ActivityOffer, Capability.ActivityReceive)]
    public async Task ProductionLoopAuthenticatesEitherOneWayCapabilityDirection(
        Capability connectorCapability,
        Capability listenerCapability)
    {
        using DeviceIdentity connectorIdentity =
            CreateIdentity("11111111", "Connector");
        using DeviceIdentity listenerIdentity =
            CreateIdentity("22222222", "Listener");
        await using var connectorTrust = new TrustSessionCoordinator(
            CreateTrustStore(listenerIdentity, connectorCapability));
        await using var listenerTrust = new TrustSessionCoordinator(
            CreateTrustStore(connectorIdentity, listenerCapability));
        var listenerSession = new ProtocolRecordingSessionHandler();
        var listenerLoops = new FakeReconnectLoopFactory();
        await using var listenerConnections =
            new DesktopTrustedPeerConnectionCoordinator(
                listenerIdentity.DeviceId,
                listenerTrust,
                static () => [],
                listenerLoops,
                listenerSession);
        listenerConnections.Start();
        Assert.Empty(listenerLoops.Created);

        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port;
        var inboundProfile = new AuthenticatedInboundSessionProfile(
            CapabilityGrant.Of(
                Capability.ActivityOffer,
                Capability.ActivityReceive),
            ProtocolFeatures.ProductionSupportedVersions,
            capabilityMatch: CapabilityRequirementMatch.Any);
        var inbound = new AuthenticatedTcpInboundListener(
            socket,
            listenerIdentity,
            listenerTrust,
            inboundProfile,
            listenerConnections.SessionHandler);
        using var stopInbound = new CancellationTokenSource();
        Task inboundRun = inbound.RunAsync(stopInbound.Token).AsTask();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            listenerIdentity,
            port,
            ProtocolFeatures.ProductionSupportedVersions,
            now,
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat((byte)0x55, SignedDiscoveryOffer.NonceLength).ToArray());
        ImmutableArray<UnverifiedPairingCandidate> candidates =
        [
            new(
                "listener._flowspan._tcp.local",
                offer,
                new IPEndPoint(IPAddress.Loopback, port),
                PairingCandidateTrustState.AlreadyPaired),
        ];
        var candidateSource = new DesktopTrustedPeerCandidateSource(
            connectorTrust,
            () => candidates);
        var systemLoops = new SystemDesktopPeerReconnectLoopFactory(
            connectorIdentity,
            connectorTrust,
            candidateSource,
            networkChanges: new SilentNetworkChangeSource());
        var connectorSession = new ProtocolRecordingSessionHandler();
        await using var connectorConnections =
            new DesktopTrustedPeerConnectionCoordinator(
                connectorIdentity.DeviceId,
                connectorTrust,
                () => candidates,
                systemLoops,
                connectorSession);

        try
        {
            connectorConnections.Start();

            await WaitUntilAsync(() =>
                Assert.Single(connectorConnections.GetSnapshot()).State
                    == DesktopTrustedPeerConnectionState.AuthenticatedIdle
                && Assert.Single(listenerConnections.GetSnapshot()).State
                    == DesktopTrustedPeerConnectionState.AuthenticatedIdle);

            Assert.Contains(
                "NOT SHARING",
                Assert.Single(connectorConnections.GetSnapshot()).StatusLabel);
            Assert.Contains(
                "NOT SHARING",
                Assert.Single(listenerConnections.GetSnapshot()).StatusLabel);
            Assert.Equal(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                await connectorSession.ProtocolVersion.Task.WaitAsync(
                    TimeSpan.FromSeconds(1)));
            Assert.Equal(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                await listenerSession.ProtocolVersion.Task.WaitAsync(
                    TimeSpan.FromSeconds(1)));
        }
        finally
        {
            await connectorConnections.DisposeAsync();
            stopInbound.Cancel();
            socket.Stop();
            try
            {
                await inboundRun;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    private static DeviceIdentity CreateIdentity(string prefix, string name) =>
        DeviceIdentity.Generate(
            DeviceId.Parse($"{prefix}-0000-0000-0000-000000000000"),
            name);

    private static InMemoryTrustStore CreateTrustStore(
        DeviceIdentity identity,
        params Capability[] capabilities)
    {
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            identity.PublicIdentity,
            Now,
            capabilities.Length == 0
                ? CapabilityGrant.None
                : CapabilityGrant.Of(capabilities)));
        return store;
    }

    private static UnverifiedPairingCandidate CreateCandidate(
        DeviceIdentity identity,
        PairingCandidateTrustState state,
        IPAddress? address = null)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength).ToArray());
        return new UnverifiedPairingCandidate(
            $"flowspan-{identity.DeviceId}",
            offer,
            new IPEndPoint(address ?? IPAddress.Parse("192.0.2.10"), 4747),
            state);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SilentNetworkChangeSource : INetworkChangeSource
    {
        public IDisposable Subscribe(Action networkChanged) =>
            NullSubscription.Instance;

        private sealed class NullSubscription : IDisposable
        {
            public static NullSubscription Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class ProtocolRecordingSessionHandler :
        IAuthenticatedControlSessionHandler
    {
        public TaskCompletionSource<ProtocolVersion> ProtocolVersion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            ProtocolVersion.TrySetResult(connection.ProtocolVersion);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FakeReconnectLoopFactory : IDesktopPeerReconnectLoopFactory
    {
        public List<FakeReconnectLoop> Created { get; } = [];

        public IDesktopPeerReconnectLoop Create(
            TrustedPeerSnapshot peer,
            Action<DesktopPeerReconnectProgress> report,
            IAuthenticatedControlSessionHandler idleHandler)
        {
            var loop = new FakeReconnectLoop(peer, report);
            Created.Add(loop);
            return loop;
        }
    }

    private sealed class FakeReconnectLoop(
        TrustedPeerSnapshot peer,
        Action<DesktopPeerReconnectProgress> report) : IDesktopPeerReconnectLoop
    {
        private readonly TaskCompletionSource<PeerReconnectStopReason> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DiscoverySignalCount { get; private set; }

        public bool Disposed { get; private set; }

        public TrustedPeerSnapshot Peer { get; } = peer;

        public void Complete(PeerReconnectStopReason reason) =>
            completion.TrySetResult(reason);

        public void Fail(Exception exception) =>
            completion.TrySetException(exception);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            completion.TrySetCanceled();
            return ValueTask.CompletedTask;
        }

        public void Report(DesktopPeerReconnectProgress progress) => report(progress);

        public void SignalDiscoveryChanged() => DiscoverySignalCount++;

        public ValueTask<PeerReconnectStopReason> RunAsync(
            CancellationToken cancellationToken = default) =>
            new(completion.Task.WaitAsync(cancellationToken));
    }
}
