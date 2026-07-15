using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedTcpPeerSessionAttemptTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UntrustedCandidateIsRejectedBeforeTcpConnect()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        VerifiedPeerConnectionCandidate candidate = CreateCandidate(peerIdentity);
        var candidates = new TestCandidateSource(candidate);
        var connector = new NeverConnector();
        var trustStore = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            candidates,
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result.StopReason);
        Assert.Equal(0, connector.Count);
    }

    [Fact]
    public async Task TrustedPeerWithoutCurrentCandidateIsTransient()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new NeverConnector();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new EmptyCandidateSource(),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptResult.TransientFailure, result);
        Assert.Equal(0, connector.Count);
    }

    [Fact]
    public async Task MissingRequiredCapabilityIsRejectedBeforeTcpConnect()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new NeverConnector();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(PeerReconnectStopReason.CapabilityDenied, result.StopReason);
        Assert.Equal(0, connector.Count);
    }

    [Fact]
    public async Task AnyCapabilityProfileConnectsPeerWithOneAlternativeGrant()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new FailingConnector(new IOException("Connection refused."));
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)],
                capabilityMatch: CapabilityRequirementMatch.Any),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptResult.TransientFailure, result);
        Assert.Equal(1, connector.Count);
    }

    [Fact]
    public async Task ChangedCandidateIdentityIsRejectedBeforeTcpConnect()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        DeviceId peerDeviceId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedPeer = DeviceIdentity.Generate(peerDeviceId, "Desk");
        using DeviceIdentity changedPeer = DeviceIdentity.Generate(peerDeviceId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            trustedPeer.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new NeverConnector();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerDeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(changedPeer)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(
            PeerReconnectStopReason.CandidateIdentityChanged,
            result.StopReason);
        Assert.Equal(0, connector.Count);
    }

    [Fact]
    public async Task ExpiredTrustedCandidateIsTransientBeforeTcpConnect()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new NeverConnector();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now.AddSeconds(30)));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptResult.TransientFailure, result);
        Assert.Equal(0, connector.Count);
    }

    [Fact]
    public async Task SameIdentityKeyWithUpdatedDisplayNameCanConnect()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        DeviceId peerDeviceId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedPeer = DeviceIdentity.Generate(peerDeviceId, "Desk");
        byte[] privateKey = trustedPeer.ExportPkcs8ForSecretStore();
        DeviceIdentity renamedPeer;
        try
        {
            renamedPeer = DeviceIdentity.ImportPkcs8(
                peerDeviceId,
                "Studio",
                privateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }

        using (renamedPeer)
        {
            var trustStore = new InMemoryTrustStore();
            trustStore.Register(new TrustRecord(
                trustedPeer.PublicIdentity,
                Now,
                CapabilityGrant.Of(Capability.ActivityReceive)));
            await using var trustSessions = new TrustSessionCoordinator(trustStore);
            var connector = new FailingConnector(new IOException("Connection refused."));
            var attempt = new AuthenticatedTcpPeerSessionAttempt(
                new AuthenticatedPeerSessionProfile(
                    peerDeviceId,
                    CapabilityGrant.Of(Capability.ActivityReceive),
                    [new ProtocolVersion(1, 0)]),
                localIdentity,
                trustSessions,
                new TestCandidateSource(CreateCandidate(renamedPeer)),
                connector,
                new NeverSessionHandler(),
                new FixedTimeProvider(Now));

            PeerSessionAttemptResult result = await attempt.RunAsync();

            Assert.Equal(PeerSessionAttemptResult.TransientFailure, result);
            Assert.Equal(1, connector.Count);
        }
    }

    [Fact]
    public async Task TcpConnectFailureIsTransient()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new FailingConnector(new IOException("Connection refused."));
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptResult.TransientFailure, result);
        Assert.Equal(1, connector.Count);
    }

    [Fact]
    public async Task EveryAttemptReloadsCurrentTrustBeforeTcpConnect()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        CapabilityGrant required = CapabilityGrant.Of(Capability.ActivityReceive);
        var trustStore = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new FailingConnector(new IOException("Connection refused."));
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                required,
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult beforeTrust = await attempt.RunAsync();
        trustStore.Register(new TrustRecord(peerIdentity.PublicIdentity, Now, required));
        PeerSessionAttemptResult whileTrusted = await attempt.RunAsync();
        bool revoked = await trustSessions.RevokePeerAsync(peerIdentity.DeviceId);
        PeerSessionAttemptResult afterRevoke = await attempt.RunAsync();

        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, beforeTrust.StopReason);
        Assert.Equal(PeerSessionAttemptResult.TransientFailure, whileTrusted);
        Assert.True(revoked);
        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, afterRevoke.StopReason);
        Assert.Equal(1, connector.Count);
    }

    [Fact]
    public async Task ProtocolHandshakeRejectionIsPermanentAndStructured()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var connector = new FailingConnector(new SessionHandshakeException(
            SessionHandshakeFailure.NoCommonProtocolVersion,
            "No common protocol."));
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                CapabilityGrant.Of(Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();

        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(
            PeerReconnectStopReason.ProtocolIncompatible,
            result.StopReason);
        Assert.Equal(1, connector.Count);
    }

    [Fact]
    public async Task TrustedPeerRunsRealAuthenticatedLoopbackSession()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        CapabilityGrant required = CapabilityGrant.Of(Capability.ActivityReceive);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(peerIdentity.PublicIdentity, Now, required));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endPoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var responderTrust = new TrustRecord(
            localIdentity.PublicIdentity,
            Now,
            required);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                peerIdentity,
                responderTrust,
                [new ProtocolVersion(1, 0)]).AsTask();
        var handler = new RecordingSessionHandler();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                required,
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity, endPoint.Port)),
            new SystemAuthenticatedTcpConnector(),
            handler,
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();
        await using AuthenticatedTcpControlConnection responder = await accepting;

        Assert.Equal(PeerSessionAttemptResult.AuthenticatedSessionEnded, result);
        Assert.Equal(1, handler.Count);
        Assert.Equal(peerIdentity.DeviceId, handler.PeerDeviceId);
    }

    [Fact]
    public async Task PeerRevocationCancelsAndDrainsAuthenticatedHandler()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        CapabilityGrant required = CapabilityGrant.Of(Capability.ActivityReceive);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(peerIdentity.PublicIdentity, Now, required));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endPoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                peerIdentity,
                new TrustRecord(localIdentity.PublicIdentity, Now, required),
                [new ProtocolVersion(1, 0)]).AsTask();
        var handler = new BlockingSessionHandler();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                required,
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity, endPoint.Port)),
            new SystemAuthenticatedTcpConnector(),
            handler,
            new FixedTimeProvider(Now));

        Task<PeerSessionAttemptResult> running = attempt.RunAsync().AsTask();
        await using AuthenticatedTcpControlConnection responder = await accepting;
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        bool revoked = await trustSessions.RevokePeerAsync(peerIdentity.DeviceId);
        PeerSessionAttemptResult result = await running.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.True(revoked);
        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result.StopReason);
        Assert.True(handler.CancellationObserved);
        Assert.False(trustStore.TryGet(peerIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task CapabilityDowngradeCancelsAndDrainsAuthenticatedHandler()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        CapabilityGrant required = CapabilityGrant.Of(Capability.ActivityReceive);
        CapabilityGrant initialGrant = CapabilityGrant.Of(
            Capability.ActivityReceive,
            Capability.MirrorView);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            Now,
            initialGrant));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endPoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                peerIdentity,
                new TrustRecord(localIdentity.PublicIdentity, Now, required),
                [new ProtocolVersion(1, 0)]).AsTask();
        var handler = new BlockingSessionHandler();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                required,
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity, endPoint.Port)),
            new SystemAuthenticatedTcpConnector(),
            handler,
            new FixedTimeProvider(Now));

        Task<PeerSessionAttemptResult> running = attempt.RunAsync().AsTask();
        await using AuthenticatedTcpControlConnection responder = await accepting;
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        bool updated = await trustSessions.TryUpdateCapabilitiesAsync(
            peerIdentity.DeviceId,
            peerIdentity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView));
        PeerSessionAttemptResult result = await running.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.True(updated);
        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(PeerReconnectStopReason.CapabilityDenied, result.StopReason);
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task RevocationAfterHandshakeButBeforeRegistrationBlocksHandler()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        CapabilityGrant required = CapabilityGrant.Of(Capability.ActivityReceive);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(peerIdentity.PublicIdentity, Now, required));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endPoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                peerIdentity,
                new TrustRecord(localIdentity.PublicIdentity, Now, required),
                [new ProtocolVersion(1, 0)]).AsTask();
        var connector = new PausingConnector();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                required,
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity, endPoint.Port)),
            connector,
            new NeverSessionHandler(),
            new FixedTimeProvider(Now));

        Task<PeerSessionAttemptResult> running = attempt.RunAsync().AsTask();
        await using AuthenticatedTcpControlConnection responder = await accepting;
        await connector.Connected.Task.WaitAsync(TimeSpan.FromSeconds(1));
        bool revoked = await trustSessions.RevokePeerAsync(peerIdentity.DeviceId);
        connector.Release.TrySetResult();
        PeerSessionAttemptResult result = await running.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.True(revoked);
        Assert.Equal(PeerSessionAttemptStatus.PermanentRejection, result.Status);
        Assert.Equal(PeerReconnectStopReason.PeerNotTrusted, result.StopReason);
    }

    [Fact]
    public async Task IoFailureAfterAuthenticationIsSessionEndNotConnectFailure()
    {
        using DeviceIdentity localIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity peerIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        CapabilityGrant required = CapabilityGrant.Of(Capability.ActivityReceive);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(peerIdentity.PublicIdentity, Now, required));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endPoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                peerIdentity,
                new TrustRecord(localIdentity.PublicIdentity, Now, required),
                [new ProtocolVersion(1, 0)]).AsTask();
        var attempt = new AuthenticatedTcpPeerSessionAttempt(
            new AuthenticatedPeerSessionProfile(
                peerIdentity.DeviceId,
                required,
                [new ProtocolVersion(1, 0)]),
            localIdentity,
            trustSessions,
            new TestCandidateSource(CreateCandidate(peerIdentity, endPoint.Port)),
            new SystemAuthenticatedTcpConnector(),
            new FailingSessionHandler(new IOException("Peer disconnected.")),
            new FixedTimeProvider(Now));

        PeerSessionAttemptResult result = await attempt.RunAsync();
        await using AuthenticatedTcpControlConnection responder = await accepting;

        Assert.Equal(PeerSessionAttemptResult.AuthenticatedSessionEnded, result);
    }

    private static VerifiedPeerConnectionCandidate CreateCandidate(
        DeviceIdentity peerIdentity,
        int port = 4747)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            peerIdentity,
            port,
            [new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x11, SignedDiscoveryOffer.NonceLength)
                .ToArray());
        return VerifiedPeerConnectionCandidate.Create(
            new IPEndPoint(IPAddress.Loopback, port),
            offer,
            peerIdentity.PublicIdentity,
            Now);
    }

    private sealed class TestCandidateSource(
        VerifiedPeerConnectionCandidate candidate) : IPeerConnectionCandidateSource
    {
        public bool TryGet(
            DeviceId peerDeviceId,
            [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? result)
        {
            result = peerDeviceId == candidate.Offer.DeviceId ? candidate : null;
            return result is not null;
        }
    }

    private sealed class EmptyCandidateSource : IPeerConnectionCandidateSource
    {
        public bool TryGet(
            DeviceId peerDeviceId,
            [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? candidate)
        {
            candidate = null;
            return false;
        }
    }

    private sealed class NeverConnector : IAuthenticatedTcpConnector
    {
        public int Count { get; private set; }

        public ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
            IPEndPoint remoteEndPoint,
            DeviceIdentity localIdentity,
            TrustRecord trustedPeer,
            IReadOnlyList<ProtocolVersion> supportedVersions,
            TimeSpan handshakeTimeout,
            CancellationToken cancellationToken = default)
        {
            Count++;
            throw new InvalidOperationException("TCP must not be opened.");
        }
    }

    private sealed class FailingConnector(Exception failure) : IAuthenticatedTcpConnector
    {
        public int Count { get; private set; }

        public ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
            IPEndPoint remoteEndPoint,
            DeviceIdentity localIdentity,
            TrustRecord trustedPeer,
            IReadOnlyList<ProtocolVersion> supportedVersions,
            TimeSpan handshakeTimeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return ValueTask.FromException<AuthenticatedTcpControlConnection>(failure);
        }
    }

    private sealed class PausingConnector : IAuthenticatedTcpConnector
    {
        private readonly SystemAuthenticatedTcpConnector inner = new();

        public TaskCompletionSource Connected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
            IPEndPoint remoteEndPoint,
            DeviceIdentity localIdentity,
            TrustRecord trustedPeer,
            IReadOnlyList<ProtocolVersion> supportedVersions,
            TimeSpan handshakeTimeout,
            CancellationToken cancellationToken = default)
        {
            AuthenticatedTcpControlConnection connection = await inner.ConnectAsync(
                remoteEndPoint,
                localIdentity,
                trustedPeer,
                supportedVersions,
                handshakeTimeout,
                cancellationToken);
            Connected.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }

    private sealed class NeverSessionHandler : IAuthenticatedControlSessionHandler
    {
        public ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A rejected peer has no session.");
    }

    private sealed class RecordingSessionHandler : IAuthenticatedControlSessionHandler
    {
        public int Count { get; private set; }

        public DeviceId? PeerDeviceId { get; private set; }

        public ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            PeerDeviceId = connection.PeerIdentity.DeviceId;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSessionHandler : IAuthenticatedControlSessionHandler
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class FailingSessionHandler(Exception failure) :
        IAuthenticatedControlSessionHandler
    {
        public ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException(failure);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
