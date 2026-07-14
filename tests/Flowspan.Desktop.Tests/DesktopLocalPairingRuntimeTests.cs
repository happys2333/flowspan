using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopLocalPairingRuntimeTests
{
    [Fact]
    public async Task ConstructionDoesNotStartNetworkUntilExplicitEnable()
    {
        var factory = new RecordingNetworkFactory();
        await using var runtime = new DesktopLocalPairingRuntime(factory);

        Assert.Equal(0, factory.StartCount);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);

        await runtime.EnableAsync();

        Assert.Equal(1, factory.StartCount);
        Assert.Equal(DesktopLocalPairingStatus.Enabled, runtime.Status);
        Assert.True(runtime.IsEnabled);
        Assert.Equal(4747, runtime.ListeningPort);
    }

    [Fact]
    public async Task DisableDrainsCurrentSessionAndAllowsExplicitReenable()
    {
        var factory = new RecordingNetworkFactory();
        await using var runtime = new DesktopLocalPairingRuntime(factory);
        await runtime.EnableAsync();
        StubNetworkSession first = Assert.IsType<StubNetworkSession>(
            factory.LastSession);

        await runtime.DisableAsync();

        Assert.True(first.Disposed);
        Assert.False(runtime.IsEnabled);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);
        Assert.Null(runtime.ListeningPort);

        await runtime.EnableAsync();
        Assert.Equal(2, factory.StartCount);
        Assert.NotSame(first, factory.LastSession);
    }

    [Fact]
    public async Task DisposeCancelsAndWaitsForInFlightEnable()
    {
        var factory = new BlockingNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        Task enabling = runtime.EnableAsync().AsTask();
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = runtime.DisposeAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enabling.WaitAsync(TimeSpan.FromSeconds(2)));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(factory.CancellationObserved);
    }

    [Fact]
    public async Task BrowserStartFailureReleasesBoundPortAndAdapter()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111"),
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        int port = ReserveLoopbackPort();
        var dns = new FailingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            new DesktopPairingDecisionSource(),
            () => new TcpListener(IPAddress.Loopback, port),
            () => new DesktopDnsSdTransport(dns, dns));

        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await factory.StartAsync());

        Assert.Equal("browse failed", failure.Message);
        Assert.Equal(1, dns.DisposeCount);
        var rebound = new TcpListener(IPAddress.Loopback, port);
        try
        {
            rebound.Start();
        }
        finally
        {
            rebound.Stop();
        }
    }

    [Fact]
    public async Task SuccessfulStartAdvertisesBoundPortAndDisposeWithdrawsEverything()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111"),
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns));

        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        int boundPort = session.ListeningPort;
        SignedDiscoveryOffer offer = Assert.Single(dns.PublishedOffers);

        Assert.InRange(boundPort, 1, ushort.MaxValue);
        Assert.Equal(boundPort, offer.Port);
        Assert.True(offer.Verify(identity.PublicIdentity, DateTimeOffset.UtcNow));
        Assert.Equal(1, dns.StartCount);

        await session.DisposeAsync();

        Assert.Equal(1, dns.WithdrawCount);
        Assert.Equal(1, dns.DisposeCount);
        var rebound = new TcpListener(IPAddress.Loopback, boundPort);
        try
        {
            rebound.Start();
        }
        finally
        {
            rebound.Stop();
        }
    }

    [Fact]
    public async Task BackgroundAdvertisementFailureStopsSessionAndAllowsRetry()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111"),
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var firstDns = new FailOnSecondPublishDnsSdTransport();
        var retryDns = new RecordingDnsSdTransport();
        var firstDelay = new ControlledAdvertisementDelay();
        var retryDelay = new BlockingAdvertisementDelay();
        var dnsTransports = new Queue<DesktopDnsSdTransport>([
            new(firstDns, firstDns),
            new(retryDns, retryDns),
        ]);
        var delays = new Queue<IDnsSdAdvertisementDelay>([
            firstDelay,
            retryDelay,
        ]);
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => dnsTransports.Dequeue(),
            () => delays.Dequeue());
        await using var runtime = new DesktopLocalPairingRuntime(factory);

        await runtime.EnableAsync();
        int firstPort = Assert.IsType<int>(runtime.ListeningPort);
        await firstDelay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        firstDelay.Release();

        await WaitUntilAsync(
            () => runtime.Status == DesktopLocalPairingStatus.Faulted
                && firstDns.DisposeCount == 1,
            TimeSpan.FromSeconds(2));
        Assert.False(runtime.IsEnabled);
        Assert.Null(runtime.ListeningPort);
        Assert.Equal(2, firstDns.PublishCount);
        Assert.Equal(1, firstDns.WithdrawCount);
        Assert.Equal(1, firstDns.DisposeCount);
        var rebound = new TcpListener(IPAddress.Loopback, firstPort);
        try
        {
            rebound.Start();
        }
        finally
        {
            rebound.Stop();
        }

        await runtime.EnableAsync();

        Assert.True(runtime.IsEnabled);
        Assert.Equal(DesktopLocalPairingStatus.Enabled, runtime.Status);
        Assert.Single(retryDns.PublishedOffers);
    }

    [Fact]
    public async Task BackgroundListenerFailureCancelsAdvertisementAndReleasesBrowser()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111"),
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        TcpListener? injectedListener = null;
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => injectedListener = new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        await using var runtime = new DesktopLocalPairingRuntime(factory);
        await runtime.EnableAsync();

        Assert.NotNull(injectedListener);
        injectedListener.Stop();

        await WaitUntilAsync(
            () => runtime.Status == DesktopLocalPairingStatus.Faulted
                && dns.DisposeCount == 1,
            TimeSpan.FromSeconds(2));
        Assert.False(runtime.IsEnabled);
        Assert.Null(runtime.ListeningPort);
        Assert.Equal(1, dns.WithdrawCount);
    }

    [Fact]
    public async Task SessionAlreadyFaultedAtFactoryReturnNeverBecomesEnabled()
    {
        var session = new StubNetworkSession();
        session.RaiseFault();
        var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.EnableAsync().AsTask());

        Assert.Contains("faulted during startup", failure.Message);
        Assert.Equal(DesktopLocalPairingStatus.Faulted, runtime.Status);
        Assert.False(runtime.IsEnabled);
        Assert.Null(runtime.ListeningPort);
        Assert.True(session.Disposed);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsAndDrainsInFlightOutboundPairing()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111"),
            "Desk");
        using DeviceIdentity peer = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "22222222-2222-2222-2222-222222222222"),
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns));
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        var peerListener = new TcpListener(IPAddress.Loopback, 0);
        peerListener.Start();
        try
        {
            var peerEndPoint = (IPEndPoint)peerListener.LocalEndpoint;
            SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
                peer,
                peerEndPoint.Port,
                [new Flowspan.Protocol.ProtocolVersion(1, 0)],
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(30),
                Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength).ToArray());
            var candidate = new UnverifiedPairingCandidate(
                "peer._flowspan._tcp.local",
                offer,
                peerEndPoint,
                PairingCandidateTrustState.UnverifiedPairingRequired);

            Task<PairingCeremonyResult> pairing = session.PairAsync(candidate).AsTask();
            using TcpClient accepted = await peerListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(2));

            Task disposing = session.DisposeAsync().AsTask();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                pairing.WaitAsync(TimeSpan.FromSeconds(2)));
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            peerListener.Stop();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task InboundPairingPersistsTrustAndPublishesTrustChanged()
    {
        using DeviceIdentity serverIdentity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "11111111-1111-1111-1111-111111111111"),
            "Server");
        using DeviceIdentity clientIdentity = DeviceIdentity.Generate(
            Flowspan.Domain.DeviceId.Parse(
                "22222222-2222-2222-2222-222222222222"),
            "Client");
        var serverTrustStore = new InMemoryTrustStore();
        var clientTrustStore = new InMemoryTrustStore();
        await using var serverTrust = new TrustSessionCoordinator(serverTrustStore);
        using var serverDecisions = new DesktopPairingDecisionSource();
        using var clientDecisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(serverIdentity),
            _ => ValueTask.FromResult(serverTrust),
            serverDecisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns));
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        var trustChanged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.TrustChanged += () => trustChanged.TrySetResult();
        try
        {
            DirectTcpPairingChannel channel = await DirectTcpPairingChannel.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, session.ListeningPort));
            var clientCeremony = new PairingCeremony(
                new PairingCeremonyProfile([new Flowspan.Protocol.ProtocolVersion(1, 0)]),
                clientDecisions,
                clientTrustStore);
            Task<PairingCeremonyResult> clientRun = clientCeremony.RunInitiatorAsync(
                channel,
                clientIdentity).AsTask();
            DesktopPairingPrompt serverPrompt = await WaitForPromptAsync(serverDecisions);
            DesktopPairingPrompt clientPrompt = await WaitForPromptAsync(clientDecisions);
            Assert.Equal(
                serverPrompt.ShortAuthenticationString,
                clientPrompt.ShortAuthenticationString);
            Assert.True(serverDecisions.TryAccept(
                serverPrompt.PromptId,
                CapabilityGrant.None));
            Assert.True(clientDecisions.TryAccept(
                clientPrompt.PromptId,
                CapabilityGrant.None));

            PairingCeremonyResult result = await clientRun
                .WaitAsync(TimeSpan.FromSeconds(5));
            await trustChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(result.Succeeded);
            Assert.True(serverTrustStore.TryGet(clientIdentity.DeviceId, out _));
            Assert.True(clientTrustStore.TryGet(serverIdentity.DeviceId, out _));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static async Task<DesktopPairingPrompt> WaitForPromptAsync(
        DesktopPairingDecisionSource source)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (source.CurrentPrompt is { } prompt)
            {
                return prompt;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The desktop pairing prompt did not open.");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class RecordingNetworkFactory : IDesktopLocalPairingNetworkFactory
    {
        public int StartCount { get; private set; }

        public IDesktopLocalPairingNetworkSession? LastSession { get; private set; }

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            LastSession = new StubNetworkSession();
            return ValueTask.FromResult(LastSession);
        }
    }

    private sealed class BlockingNetworkFactory : IDesktopLocalPairingNetworkFactory
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The blocking network factory unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class FixedNetworkFactory(
        IDesktopLocalPairingNetworkSession session) :
        IDesktopLocalPairingNetworkFactory
    {
        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(session);
        }
    }

    private sealed class StubNetworkSession : IDesktopLocalPairingNetworkSession
    {
        public bool Disposed { get; private set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public event Action<IDesktopLocalPairingNetworkSession>? Faulted;

        public int ListeningPort => 4747;

        public bool IsFaulted { get; private set; }

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseFault()
        {
            IsFaulted = true;
            Faulted?.Invoke(this);
        }
    }

    private sealed class FailingDnsSdTransport :
        IDnsSdServiceBrowser,
        IDnsSdServicePublisher,
        IDisposable
    {
        public int DisposeCount { get; private set; }

        public event Action<DnsSdServiceSnapshot>? ServiceChanged
        {
            add { }
            remove { }
        }

        public event Action<string>? ServiceRemoved
        {
            add { }
            remove { }
        }

        public void Dispose() => DisposeCount++;

        public void Publish(SignedDiscoveryOffer offer) =>
            throw new InvalidOperationException("Publish must not run after browse failed.");

        public void Start() => throw new IOException("browse failed");

        public void Withdraw()
        {
        }
    }

    private sealed class RecordingDnsSdTransport :
        IDnsSdServiceBrowser,
        IDnsSdServicePublisher,
        IDisposable
    {
        public int DisposeCount { get; private set; }

        public List<SignedDiscoveryOffer> PublishedOffers { get; } = [];

        public int StartCount { get; private set; }

        public int WithdrawCount { get; private set; }

        public event Action<DnsSdServiceSnapshot>? ServiceChanged
        {
            add { }
            remove { }
        }

        public event Action<string>? ServiceRemoved
        {
            add { }
            remove { }
        }

        public void Dispose() => DisposeCount++;

        public void Publish(SignedDiscoveryOffer offer) => PublishedOffers.Add(offer);

        public void Start() => StartCount++;

        public void Withdraw() => WithdrawCount++;
    }

    private sealed class FailOnSecondPublishDnsSdTransport :
        IDnsSdServiceBrowser,
        IDnsSdServicePublisher,
        IDisposable
    {
        private int publishCount;

        public int DisposeCount { get; private set; }

        public int PublishCount => Volatile.Read(ref publishCount);

        public int WithdrawCount { get; private set; }

        public event Action<DnsSdServiceSnapshot>? ServiceChanged
        {
            add { }
            remove { }
        }

        public event Action<string>? ServiceRemoved
        {
            add { }
            remove { }
        }

        public void Dispose() => DisposeCount++;

        public void Publish(SignedDiscoveryOffer offer)
        {
            if (Interlocked.Increment(ref publishCount) == 2)
            {
                throw new IOException("CANARY_ADVERTISEMENT_FAILURE");
            }
        }

        public void Start()
        {
        }

        public void Withdraw() => WithdrawCount++;
    }

    private sealed class ControlledAdvertisementDelay : IDnsSdAdvertisementDelay
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return new ValueTask(release.Task.WaitAsync(cancellationToken));
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class BlockingAdvertisementDelay : IDnsSdAdvertisementDelay
    {
        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default) =>
            new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    }
}
