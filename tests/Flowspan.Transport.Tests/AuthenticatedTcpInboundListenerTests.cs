using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedTcpInboundListenerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 13, 0, 0, TimeSpan.Zero);
    private static readonly CapabilityGrant Required =
        CapabilityGrant.Of(Capability.ActivityReceive);

    [Fact]
    public async Task RealListenerAuthenticatesTwoDifferentTrustedPeers()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity firstPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        using DeviceIdentity secondPeer = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Laptop");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(firstPeer));
        trustStore.Register(CreateTrust(secondPeer));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 4);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var handler = new MultiPeerBlockingHandler(expectedPeers: 2);
        var listener = new AuthenticatedTcpInboundListener(
            socket,
            serverIdentity,
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 2),
            handler);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        Task<AuthenticatedTcpControlConnection> firstConnecting =
            AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                firstPeer,
                CreateTrust(serverIdentity),
                [new ProtocolVersion(1, 0)]).AsTask();
        Task<AuthenticatedTcpControlConnection> secondConnecting =
            AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                secondPeer,
                CreateTrust(serverIdentity),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection first = await firstConnecting;
        await using AuthenticatedTcpControlConnection second = await secondConnecting;

        await handler.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal<DeviceId>(
            [firstPeer.DeviceId, secondPeer.DeviceId],
            handler.PeerDeviceIds
                .OrderBy(static id => id.ToString(), StringComparer.Ordinal)
                .ToArray());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(2, handler.CancellationCount);
    }

    [Fact]
    public async Task UntrustedPeerIsRejectedAndNextTrustedPeerIsAccepted()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity trustedPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        using DeviceIdentity untrustedPeer = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Unknown");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(trustedPeer));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 4);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var handler = new MultiPeerBlockingHandler(expectedPeers: 1);
        var failures = new List<InboundSessionFailure>();
        var listener = new AuthenticatedTcpInboundListener(
            socket,
            serverIdentity,
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 2),
            handler);
        listener.SessionFaulted += failures.Add;
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using AuthenticatedTcpControlConnection rejected =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endPoint,
                    untrustedPeer,
                    CreateTrust(serverIdentity),
                    [new ProtocolVersion(1, 0)]);
        });
        await using AuthenticatedTcpControlConnection accepted =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                trustedPeer,
                CreateTrust(serverIdentity),
                [new ProtocolVersion(1, 0)]);
        await handler.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        InboundSessionFailure failure = Assert.Single(failures);
        Assert.Equal(InboundSessionFailureStage.Authentication, failure.Stage);
        var authenticationFailure = Assert.IsType<IncomingPeerAuthenticationException>(
            failure.Exception);
        var handshakeFailure = Assert.IsType<SessionHandshakeException>(
            authenticationFailure.InnerException);
        Assert.Equal(SessionHandshakeFailure.PeerNotTrusted, handshakeFailure.Failure);
        Assert.Equal<DeviceId>([trustedPeer.DeviceId], handler.PeerDeviceIds);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task ConcurrencyLimitDefersSecondAcceptUntilFirstSessionEnds()
    {
        using DeviceIdentity firstIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        using DeviceIdentity secondIdentity = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Laptop");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(firstIdentity));
        trustStore.Register(CreateTrust(secondIdentity));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var first = new FakeAcceptedSession(firstIdentity.PublicIdentity, block: true);
        var second = new FakeAcceptedSession(secondIdentity.PublicIdentity, block: false);
        var acceptor = new QueueSessionAcceptor(first, second);
        var listener = new AuthenticatedTcpInboundListener(
            acceptor,
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 1),
            new NeverHandler());
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, acceptor.ReturnedCount);
        Assert.False(second.Started.Task.IsCompleted);

        first.Release.TrySetResult();
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, acceptor.ReturnedCount);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task RevokingOnePeerDrainsOnlyItsInboundSession()
    {
        using DeviceIdentity firstIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        using DeviceIdentity secondIdentity = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Laptop");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(firstIdentity));
        trustStore.Register(CreateTrust(secondIdentity));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var first = new FakeAcceptedSession(firstIdentity.PublicIdentity, block: true);
        var second = new FakeAcceptedSession(secondIdentity.PublicIdentity, block: true);
        var listener = new AuthenticatedTcpInboundListener(
            new QueueSessionAcceptor(first, second),
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 2),
            new NeverHandler());
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        await Task.WhenAll(first.Started.Task, second.Started.Task)
            .WaitAsync(TimeSpan.FromSeconds(1));

        bool revoked = await trustSessions.RevokePeerAsync(firstIdentity.DeviceId);

        Assert.True(revoked);
        Assert.True(first.CancellationObserved);
        Assert.False(second.CancellationObserved);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(second.CancellationObserved);
    }

    [Fact]
    public async Task FatalAcceptFailureCancelsAndDrainsActiveSession()
    {
        using DeviceIdentity identity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(identity));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var active = new FakeAcceptedSession(identity.PublicIdentity, block: true);
        var listener = new AuthenticatedTcpInboundListener(
            new QueueSessionAcceptor(
                active,
                new InvalidOperationException("listener failed")),
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 2),
            new NeverHandler());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => listener.RunAsync().AsTask());

        Assert.Equal("listener failed", failure.Message);
        Assert.True(active.CancellationObserved);
        Assert.Equal(1, active.DisposeCount);
    }

    [Fact]
    public async Task HandlerFailureIsReportedWithoutStoppingOtherPeers()
    {
        using DeviceIdentity firstIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        using DeviceIdentity secondIdentity = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Laptop");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(firstIdentity));
        trustStore.Register(CreateTrust(secondIdentity));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var failed = new FaultingAcceptedSession(
            firstIdentity.PublicIdentity,
            new InvalidOperationException("handler failed"));
        var active = new FakeAcceptedSession(
            secondIdentity.PublicIdentity,
            block: true);
        var failures = new ConcurrentQueue<InboundSessionFailure>();
        var listener = new AuthenticatedTcpInboundListener(
            new QueueSessionAcceptor(failed, active),
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 2),
            new NeverHandler());
        listener.SessionFaulted += failures.Enqueue;
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        await Task.WhenAll(failed.Disposed.Task, active.Started.Task)
            .WaitAsync(TimeSpan.FromSeconds(1));

        InboundSessionFailure failure = Assert.Single(failures);
        Assert.Equal(InboundSessionFailureStage.Handler, failure.Stage);
        Assert.Equal(firstIdentity.DeviceId, failure.PeerDeviceId);
        Assert.Equal("handler failed", failure.Exception.Message);
        Assert.False(active.Disposed.Task.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(active.CancellationObserved);
    }

    [Fact]
    public void ProfileEnforcesDefaultAndHardConcurrencyLimits()
    {
        var defaultProfile = new AuthenticatedInboundSessionProfile(
            Required,
            [new ProtocolVersion(1, 0)]);

        Assert.Equal(
            AuthenticatedInboundSessionProfile.DefaultMaximumConcurrentSessions,
            defaultProfile.MaximumConcurrentSessions);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateProfile(
                AuthenticatedInboundSessionProfile.MaximumConcurrentSessionsLimit + 1));
    }

    [Fact]
    public async Task CapabilityDenialIsReportedWithoutRunningSession()
    {
        using DeviceIdentity identity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var accepted = new FakeAcceptedSession(identity.PublicIdentity, block: false);
        var failures = new List<InboundSessionFailure>();
        var listener = new AuthenticatedTcpInboundListener(
            new QueueSessionAcceptor(accepted),
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 1),
            new NeverHandler());
        listener.SessionFaulted += failures.Add;
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        await accepted.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        InboundSessionFailure failure = Assert.Single(failures);
        Assert.Equal(InboundSessionFailureStage.Authorization, failure.Stage);
        Assert.Equal(identity.DeviceId, failure.PeerDeviceId);
        Assert.False(accepted.Started.Task.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task AnyCapabilityProfileRunsPeerWithOneAlternativeGrant()
    {
        using DeviceIdentity identity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var accepted = new FakeAcceptedSession(identity.PublicIdentity, block: false);
        var listener = new AuthenticatedTcpInboundListener(
            new QueueSessionAcceptor(accepted),
            trustSessions,
            new AuthenticatedInboundSessionProfile(
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive),
                [new ProtocolVersion(1, 0)],
                maximumConcurrentSessions: 1,
                handshakeTimeout: TimeSpan.FromSeconds(2),
                capabilityMatch: CapabilityRequirementMatch.Any),
            new NeverHandler());
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        await accepted.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task AcceptedSessionIsRecheckedAgainstCurrentTrustedKey()
    {
        DeviceId deviceId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedIdentity = DeviceIdentity.Generate(
            deviceId,
            "Desk");
        using DeviceIdentity substitutedIdentity = DeviceIdentity.Generate(
            deviceId,
            "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(CreateTrust(trustedIdentity));
        await using var trustSessions = new TrustSessionCoordinator(trustStore);
        var accepted = new FakeAcceptedSession(
            substitutedIdentity.PublicIdentity,
            block: false);
        var failures = new List<InboundSessionFailure>();
        var listener = new AuthenticatedTcpInboundListener(
            new QueueSessionAcceptor(accepted),
            trustSessions,
            CreateProfile(maximumConcurrentSessions: 1),
            new NeverHandler());
        listener.SessionFaulted += failures.Add;
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        await accepted.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        InboundSessionFailure failure = Assert.Single(failures);
        Assert.Equal(InboundSessionFailureStage.Authentication, failure.Stage);
        Assert.Equal(deviceId, failure.PeerDeviceId);
        Assert.False(accepted.Started.Task.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    private static DeviceIdentity CreateIdentity(string id, string name) =>
        DeviceIdentity.Generate(DeviceId.Parse(id), name);

    private static AuthenticatedInboundSessionProfile CreateProfile(
        int maximumConcurrentSessions) => new(
            Required,
            [new ProtocolVersion(1, 0)],
            maximumConcurrentSessions,
            handshakeTimeout: TimeSpan.FromSeconds(2));

    private static TrustRecord CreateTrust(DeviceIdentity identity) =>
        new(identity.PublicIdentity, Now, Required);

    private sealed class QueueSessionAcceptor(params object[] outcomes) :
        IAuthenticatedControlSessionAcceptor
    {
        private readonly Queue<object> outcomes = new(outcomes);

        public int ReturnedCount { get; private set; }

        public async ValueTask<IAcceptedAuthenticatedControlSession> AcceptAsync(
            CancellationToken cancellationToken = default)
        {
            object? outcome;
            lock (outcomes)
            {
                outcome = outcomes.Count > 0 ? outcomes.Dequeue() : null;
            }

            if (outcome is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable blocked accept.");
            }

            if (outcome is Exception exception)
            {
                throw exception;
            }

            ReturnedCount++;
            return (IAcceptedAuthenticatedControlSession)outcome;
        }
    }

    private sealed class FakeAcceptedSession(
        PublicDeviceIdentity peerIdentity,
        bool block) : IAcceptedAuthenticatedControlSession
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public PublicDeviceIdentity PeerIdentity { get; } = peerIdentity;

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public async ValueTask RunAsync(
            IAuthenticatedControlSessionHandler handler,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (!block)
            {
                return;
            }

            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class FaultingAcceptedSession(
        PublicDeviceIdentity peerIdentity,
        Exception failure) : IAcceptedAuthenticatedControlSession
    {
        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PublicDeviceIdentity PeerIdentity { get; } = peerIdentity;

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask RunAsync(
            IAuthenticatedControlSessionHandler handler,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(failure);
    }

    private sealed class MultiPeerBlockingHandler(int expectedPeers) :
        IAuthenticatedControlSessionHandler
    {
        private readonly ConcurrentDictionary<DeviceId, byte> peers = [];
        private int cancellationCount;

        public TaskCompletionSource AllStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CancellationCount => Volatile.Read(ref cancellationCount);

        public IEnumerable<DeviceId> PeerDeviceIds => peers.Keys;

        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            peers.TryAdd(connection.PeerIdentity.DeviceId, 0);
            if (peers.Count == expectedPeers)
            {
                AllStarted.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref cancellationCount);
                throw;
            }
        }
    }

    private sealed class NeverHandler : IAuthenticatedControlSessionHandler
    {
        public ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fake sessions own their run behavior.");
    }
}
