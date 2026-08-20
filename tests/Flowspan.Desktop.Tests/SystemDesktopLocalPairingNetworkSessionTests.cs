using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class SystemDesktopLocalPairingNetworkSessionTests
{
    [Fact]
    public async Task ProductionInboundRetainsSceneOnlyPeerAdmission()
    {
        using DeviceIdentity identity = CreateIdentity(
            "99999999-9999-9999-9999-999999999999",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            DateTimeOffset.UtcNow,
            CapabilityGrant.Of(Capability.SceneApply)));
        await using var trust = new TrustSessionCoordinator(store);
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        await using IDesktopLocalPairingNetworkSession session =
            await factory.StartAsync();
        var authenticated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += OnSessionChanged;
        try
        {
            await using AuthenticatedTcpControlConnection connection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    new IPEndPoint(IPAddress.Loopback, session.ListeningPort),
                    peerIdentity,
                    new TrustRecord(
                        identity.PublicIdentity,
                        DateTimeOffset.UtcNow,
                        CapabilityGrant.Of(Capability.SceneApply)),
                    ProtocolFeatures.ProductionSupportedVersions);

            await authenticated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            DesktopTrustedPeerConnectionSnapshot snapshot = Assert.Single(
                session.GetTrustedPeerConnections());
            Assert.Equal(peerIdentity.DeviceId, snapshot.DeviceId);
            Assert.Equal(
                DesktopTrustedPeerConnectionState.AuthenticatedIdle,
                snapshot.State);
        }
        finally
        {
            session.Changed -= OnSessionChanged;
        }

        void OnSessionChanged()
        {
            if (session.GetTrustedPeerConnections().Any(snapshot =>
                    snapshot.DeviceId == peerIdentity.DeviceId
                    && snapshot.State
                        == DesktopTrustedPeerConnectionState.AuthenticatedIdle))
            {
                authenticated.TrySetResult();
            }
        }
    }

    [Theory]
    [InlineData(Capability.MirrorView, true)]
    [InlineData(Capability.MirrorDrive, false)]
    public async Task ProductionInboundAdmitsMirrorOnlyPeerWithoutBroadeningPicker(
        Capability capability,
        bool expectsViewTarget)
    {
        using DeviceIdentity identity = CreateIdentity(
            "99999999-9999-9999-9999-999999999999",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            DateTimeOffset.UtcNow,
            CapabilityGrant.Of(capability)));
        await using var trust = new TrustSessionCoordinator(store);
        await using var activityRuntime = new DesktopActivityRuntime(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(identity);
            },
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(trust);
            });
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay(),
            activityRuntime);
        await using IDesktopLocalPairingNetworkSession session =
            await factory.StartAsync();
        var authenticated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += OnSessionChanged;
        activityRuntime.Changed += OnRuntimeChanged;

        try
        {
            await using AuthenticatedTcpControlConnection connection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    new IPEndPoint(IPAddress.Loopback, session.ListeningPort),
                    peerIdentity,
                    new TrustRecord(
                        identity.PublicIdentity,
                        DateTimeOffset.UtcNow,
                        CapabilityGrant.Of(capability)),
                    ProtocolFeatures.ProductionSupportedVersions);

            await authenticated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await runtimeObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                connection.ProtocolVersion);
            Assert.True(activityRuntime.TryGetRemoteWindowChannel(
                peerIdentity.DeviceId,
                out IRemoteWindowControlChannel? remoteWindowChannel));
            Assert.NotNull(remoteWindowChannel);
            if (expectsViewTarget)
            {
                DesktopActivityTargetSnapshot target = Assert.Single(
                    activityRuntime.GetRemoteWindowTargets(
                        MirrorParticipantRole.ViewOnly));
                Assert.Equal(peerIdentity.DeviceId, target.DeviceId);
            }
            else
            {
                Assert.Empty(activityRuntime.GetRemoteWindowTargets(
                    MirrorParticipantRole.ViewOnly));
            }

            Assert.Empty(activityRuntime.GetRemoteWindowTargets(
                MirrorParticipantRole.DriverEligible));
        }
        finally
        {
            activityRuntime.Changed -= OnRuntimeChanged;
            session.Changed -= OnSessionChanged;
        }

        void OnSessionChanged()
        {
            if (session.GetTrustedPeerConnections().Any(snapshot =>
                    snapshot.DeviceId == peerIdentity.DeviceId
                    && snapshot.State
                        == DesktopTrustedPeerConnectionState.AuthenticatedIdle))
            {
                authenticated.TrySetResult();
            }
        }

        void OnRuntimeChanged()
        {
            if (!expectsViewTarget
                || activityRuntime.GetRemoteWindowTargets(
                        MirrorParticipantRole.ViewOnly)
                    .Any(target => target.DeviceId == peerIdentity.DeviceId))
            {
                runtimeObserved.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task ProductionProtocol14PeerIsExcludedFromRemoteWindowPicker()
    {
        using DeviceIdentity identity = CreateIdentity(
            "99999999-9999-9999-9999-999999999999",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            peerIdentity.PublicIdentity,
            DateTimeOffset.UtcNow,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var trust = new TrustSessionCoordinator(store);
        await using var activityRuntime = new DesktopActivityRuntime(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(identity);
            },
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(trust);
            });
        AuthenticatedActivitySessionHandler handler =
            await activityRuntime.GetSessionHandlerAsync();
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay(),
            activityRuntime);
        await using IDesktopLocalPairingNetworkSession session =
            await factory.StartAsync();
        var routeObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activityRuntime.Changed += OnRuntimeChanged;

        try
        {
            await using AuthenticatedTcpControlConnection connection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    new IPEndPoint(IPAddress.Loopback, session.ListeningPort),
                    peerIdentity,
                    new TrustRecord(
                        identity.PublicIdentity,
                        DateTimeOffset.UtcNow,
                        CapabilityGrant.Of(Capability.MirrorView)),
                    [new ProtocolVersion(1, 4)]);

            await routeObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new ProtocolVersion(1, 4), connection.ProtocolVersion);
            Assert.True(handler.TryGetChannel(peerIdentity.DeviceId, out _));
            Assert.False(activityRuntime.TryGetRemoteWindowChannel(
                peerIdentity.DeviceId,
                out _));
            Assert.Empty(activityRuntime.GetRemoteWindowTargets(
                MirrorParticipantRole.ViewOnly));
        }
        finally
        {
            activityRuntime.Changed -= OnRuntimeChanged;
        }

        void OnRuntimeChanged()
        {
            if (handler.TryGetChannel(peerIdentity.DeviceId, out _))
            {
                routeObserved.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task DisposeWaitsForActivePublicationAndClosesPublicationAdmission()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
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
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        using var release = new ManualResetEventSlim();
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observerExited = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackCount = 0;
        session.Changed += OnChanged;
        DnsSdServiceSnapshot snapshot = CreateDiscoverySnapshot(peerIdentity);

        dns.RaiseServiceChanged(snapshot);
        await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposing = session.DisposeAsync().AsTask();
        int returnedBeforeObserverExited = 0;
        Task disposalObserved = disposing.ContinueWith(
            _ =>
            {
                if (!observerExited.Task.IsCompleted)
                {
                    Interlocked.Exchange(ref returnedBeforeObserverExited, 1);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        dns.RaiseServiceChanged(snapshot);
        try
        {
            Assert.Equal(0, Volatile.Read(ref returnedBeforeObserverExited));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(disposing, disposalObserved)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(0, Volatile.Read(ref returnedBeforeObserverExited));
        int countAfterDispose = Volatile.Read(ref callbackCount);
        dns.RaiseServiceChanged(snapshot);
        Assert.Equal(1, countAfterDispose);
        Assert.Equal(countAfterDispose, Volatile.Read(ref callbackCount));

        void OnChanged()
        {
            Interlocked.Increment(ref callbackCount);
            observerEntered.TrySetResult();
            try
            {
                release.Wait(TimeSpan.FromSeconds(10));
            }
            finally
            {
                observerExited.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task ChangedPublicationIsSingleConsumerAndCoalesced()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
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
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        using var release = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstExited = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int concurrentBeforeFirstExited = 0;
        Task concurrentEntryObserved = concurrentEntered.Task.ContinueWith(
            _ =>
            {
                if (!firstExited.Task.IsCompleted)
                {
                    Interlocked.Exchange(ref concurrentBeforeFirstExited, 1);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        int active = 0;
        int callbackCount = 0;
        int maximumActive = 0;
        session.Changed += OnChanged;
        DnsSdServiceSnapshot snapshot = CreateDiscoverySnapshot(peerIdentity);

        dns.RaiseServiceChanged(snapshot);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int index = 0; index < 64; index++)
        {
            dns.RaiseServiceChanged(snapshot);
        }

        try
        {
            Assert.Equal(0, Volatile.Read(ref concurrentBeforeFirstExited));
        }
        finally
        {
            release.Set();
        }

        await firstExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        if (concurrentEntered.Task.IsCompleted)
        {
            await concurrentEntryObserved.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(0, Volatile.Read(ref concurrentBeforeFirstExited));
        Assert.Equal(1, Volatile.Read(ref maximumActive));
        Assert.InRange(Volatile.Read(ref callbackCount), 1, 2);

        void OnChanged()
        {
            int now = Interlocked.Increment(ref active);
            int callback = Interlocked.Increment(ref callbackCount);
            int observed;
            do
            {
                observed = Volatile.Read(ref maximumActive);
                if (observed >= now)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(
                       ref maximumActive,
                       now,
                       observed) != observed);
            firstEntered.TrySetResult();
            if (now > 1)
            {
                concurrentEntered.TrySetResult();
            }

            release.Wait(TimeSpan.FromSeconds(10));
            Interlocked.Decrement(ref active);
            if (callback == 1)
            {
                firstExited.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task CancellationObserverJoinsTheSameWithdrawalFailure()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        using var peerDecisions = new DesktopPairingDecisionSource();
        var dns = new FailingWithdrawalDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        await using DirectTcpPairingChannel peerChannel =
            await DirectTcpPairingChannel.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, session.ListeningPort));
        Task<PairingCeremonyResult> peerPairing = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
            peerDecisions,
            new InMemoryTrustStore()).RunInitiatorAsync(
                peerChannel,
                peerIdentity).AsTask();
        await WaitForPromptAsync(decisions);
        var observerFailure = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        decisions.PromptChanged += OnPromptChanged;

        Task firstDispose = session.DisposeAsync().AsTask();
        Exception? first = await Record.ExceptionAsync(() => firstDispose);
        Exception? callback = await observerFailure.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Task laterDispose = session.DisposeAsync().AsTask();
        Exception? later = await Record.ExceptionAsync(() => laterDispose);

        Assert.NotNull(first);
        Assert.Same(first, callback);
        Assert.Same(first, later);
        Assert.Same(firstDispose, laterDispose);
        Assert.Equal(1, dns.WithdrawCount);
        await Record.ExceptionAsync(
            () => peerPairing.WaitAsync(TimeSpan.FromSeconds(5)));

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled)
            {
                observerFailure.TrySetResult(Record.Exception(
                    () => session.DisposeAsync().AsTask().GetAwaiter().GetResult()));
            }
        }
    }

    [Fact]
    public async Task CancellationObserverCanDispatchDisposeToAnotherThread()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        using var peerDecisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        await using DirectTcpPairingChannel peerChannel =
            await DirectTcpPairingChannel.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, session.ListeningPort));
        Task<PairingCeremonyResult> peerPairing = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
            peerDecisions,
            new InMemoryTrustStore()).RunInitiatorAsync(
                peerChannel,
                peerIdentity).AsTask();
        await WaitForPromptAsync(decisions);
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        decisions.PromptChanged += OnPromptChanged;

        Task disposing = Task.Run(async () => await session.DisposeAsync());

        await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await Record.ExceptionAsync(
            () => peerPairing.WaitAsync(TimeSpan.FromSeconds(5)));

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind != DesktopPairingPromptChangeKind.Canceled)
            {
                return;
            }

            observerEntered.TrySetResult();
            Task.Run(() => session.DisposeAsync().AsTask().GetAwaiter().GetResult())
                .GetAwaiter()
                .GetResult();
            observerReturned.TrySetResult();
        }
    }

    [Fact]
    public async Task SuccessfulPairChangedObserverCanSynchronouslyDisposeTheSession()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        var peerTrust = new InMemoryTrustStore();
        using var decisions = new DesktopPairingDecisionSource();
        using var peerDecisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        int port = session.ListeningPort;
        var peerListener = new TcpListener(IPAddress.Loopback, 0);
        peerListener.Start();
        try
        {
            var peerEndPoint = Assert.IsType<IPEndPoint>(
                peerListener.LocalEndpoint);
            Task<DirectTcpPairingChannel> accepting =
                DirectTcpPairingChannel.AcceptAsync(peerListener).AsTask();
            Task<PairingCeremonyResult> localPairing = session.PairAsync(
                CreateCandidate(peerIdentity, peerEndPoint)).AsTask();
            await using DirectTcpPairingChannel peerChannel = await accepting;
            Task<PairingCeremonyResult> peerPairing = new PairingCeremony(
                new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
                peerDecisions,
                peerTrust).RunResponderAsync(
                    peerChannel,
                    peerIdentity).AsTask();
            DesktopPairingPrompt localPrompt = await WaitForPromptAsync(decisions);
            DesktopPairingPrompt peerPrompt = await WaitForPromptAsync(peerDecisions);
            var observerEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var observerReturned = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int laterCallbackCount = 0;
            session.Changed += OnChanged;
            session.Changed += OnLaterChanged;

            Assert.True(decisions.TryAccept(
                localPrompt.PromptId,
                CapabilityGrant.None));
            Assert.True(peerDecisions.TryAccept(
                peerPrompt.PromptId,
                CapabilityGrant.None));

            await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Volatile.Read(ref laterCallbackCount));
            await Record.ExceptionAsync(
                () => localPairing.WaitAsync(TimeSpan.FromSeconds(5)));
            await Record.ExceptionAsync(
                () => peerPairing.WaitAsync(TimeSpan.FromSeconds(5)));
            var rebound = new TcpListener(IPAddress.Loopback, port);
            try
            {
                rebound.Start();
            }
            finally
            {
                rebound.Stop();
            }

            void OnChanged()
            {
                observerEntered.TrySetResult();
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
                observerReturned.TrySetResult();
            }

            void OnLaterChanged() => Interlocked.Increment(ref laterCallbackCount);
        }
        finally
        {
            peerListener.Stop();
        }
    }

    [Fact]
    public async Task BackgroundFaultObserverCanSynchronouslyDisposeTheSession()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new FailOnSecondPublishDnsSdTransport();
        var delay = new ControlledAdvertisementDelay();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => delay);
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        int port = session.ListeningPort;
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += OnFaulted;

        delay.Release();

        await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var rebound = new TcpListener(IPAddress.Loopback, port);
        try
        {
            rebound.Start();
        }
        finally
        {
            rebound.Stop();
        }

        void OnFaulted(IDesktopLocalPairingNetworkSession failed)
        {
            observerEntered.TrySetResult();
            failed.DisposeAsync().AsTask().GetAwaiter().GetResult();
            observerReturned.TrySetResult();
        }
    }

    [Fact]
    public async Task DisposeCancelsQueuedPairAndDrainsPairHoldingTheGate()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
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
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        var peerListener = new TcpListener(IPAddress.Loopback, 0);
        peerListener.Start();
        try
        {
            var peerEndPoint = Assert.IsType<IPEndPoint>(
                peerListener.LocalEndpoint);
            UnverifiedPairingCandidate candidate = CreateCandidate(
                peerIdentity,
                peerEndPoint);
            Task<PairingCeremonyResult> holder =
                session.PairAsync(candidate).AsTask();
            using TcpClient accepted = await peerListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Task<PairingCeremonyResult> waiter =
                session.PairAsync(candidate).AsTask();
            Assert.False(waiter.IsCompleted);
            Assert.False(peerListener.Pending());

            Task firstDispose = session.DisposeAsync().AsTask();
            Task concurrentDispose = session.DisposeAsync().AsTask();

            Assert.Same(firstDispose, concurrentDispose);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => holder.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(5));
            await concurrentDispose.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => session.PairAsync(candidate).AsTask());
        }
        finally
        {
            peerListener.Stop();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeDoesNotReportAlreadyPublishedLoopFaultAsCleanupFailure()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new FailOnSecondPublishDnsSdTransport();
        var delay = new ControlledAdvertisementDelay();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => delay);
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        int port = session.ListeningPort;
        var faultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += _ => faultPublished.TrySetResult();

        delay.Release();
        await faultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(session.IsFaulted);
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
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
    public async Task CancellationObserverCanDisposeWithoutWaitingOnItself()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        using var peerDecisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        await using DirectTcpPairingChannel peerChannel =
            await DirectTcpPairingChannel.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, session.ListeningPort));
        Task<PairingCeremonyResult> peerPairing = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
            peerDecisions,
            new InMemoryTrustStore()).RunInitiatorAsync(
                peerChannel,
                peerIdentity).AsTask();
        await WaitForPromptAsync(decisions);
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        decisions.PromptChanged += OnPromptChanged;

        Task disposing = Task.Run(async () => await session.DisposeAsync());

        await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await Record.ExceptionAsync(
            () => peerPairing.WaitAsync(TimeSpan.FromSeconds(5)));

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind != DesktopPairingPromptChangeKind.Canceled)
            {
                return;
            }

            observerEntered.TrySetResult();
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            observerReturned.TrySetResult();
        }
    }

    [Fact]
    public async Task BackgroundFaultClosesListeningSocketBeforePublishingCancellation()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        using var peerDecisions = new DesktopPairingDecisionSource();
        var dns = new FailOnSecondPublishDnsSdTransport();
        var delay = new ControlledAdvertisementDelay();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => delay);
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        await using DirectTcpPairingChannel peerChannel =
            await DirectTcpPairingChannel.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, session.ListeningPort));
        Task<PairingCeremonyResult> peerPairing = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
            peerDecisions,
            new InMemoryTrustStore()).RunInitiatorAsync(
                peerChannel,
                peerIdentity).AsTask();
        await WaitForPromptAsync(decisions);
        using var observerRelease = new ManualResetEventSlim();
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var faultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        decisions.PromptChanged += OnPromptChanged;
        session.Faulted += _ => faultPublished.TrySetResult();
        try
        {
            delay.Release();
            await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var rebound = new TcpListener(
                IPAddress.Loopback,
                session.ListeningPort);
            try
            {
                rebound.Start();
            }
            finally
            {
                rebound.Stop();
            }

            Assert.True(session.IsFaulted);
            await faultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            observerRelease.Set();
            decisions.PromptChanged -= OnPromptChanged;
        }
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Record.ExceptionAsync(
            () => peerPairing.WaitAsync(TimeSpan.FromSeconds(5)));

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled)
            {
                observerEntered.TrySetResult();
                observerRelease.Wait(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task DisposeClosesListeningSocketBeforePublishingCancellation()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Desk");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        using var peerDecisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        await using DirectTcpPairingChannel peerChannel =
            await DirectTcpPairingChannel.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, session.ListeningPort));
        Task<PairingCeremonyResult> peerPairing = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
            peerDecisions,
            new InMemoryTrustStore()).RunInitiatorAsync(
                peerChannel,
                peerIdentity).AsTask();
        await WaitForPromptAsync(decisions);
        using var observerRelease = new ManualResetEventSlim();
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        decisions.PromptChanged += OnPromptChanged;
        Task? disposing = null;
        try
        {
            disposing = Task.Run(async () => await session.DisposeAsync());
            await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var rebound = new TcpListener(
                IPAddress.Loopback,
                session.ListeningPort);
            try
            {
                rebound.Start();
            }
            finally
            {
                rebound.Stop();
            }

            Assert.Equal(1, dns.WithdrawCount);
        }
        finally
        {
            observerRelease.Set();
            decisions.PromptChanged -= OnPromptChanged;
            if (disposing is not null)
            {
                await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        await Record.ExceptionAsync(
            () => peerPairing.WaitAsync(TimeSpan.FromSeconds(5)));

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled)
            {
                observerEntered.TrySetResult();
                observerRelease.Wait(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task LaterDisposeJoinsTheSameFailedWithdrawal()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Desk");
        await using var trust = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new FailingWithdrawalDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns),
            () => new BlockingAdvertisementDelay());
        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();

        Task firstDispose = session.DisposeAsync().AsTask();
        Task concurrentDispose = session.DisposeAsync().AsTask();

        Assert.Same(firstDispose, concurrentDispose);
        Exception? first = await Record.ExceptionAsync(() => firstDispose);
        Exception? concurrent = await Record.ExceptionAsync(
            () => concurrentDispose);
        Task laterDispose = session.DisposeAsync().AsTask();
        Exception? later = await Record.ExceptionAsync(() => laterDispose);

        Assert.NotNull(first);
        Assert.Same(first, concurrent);
        Assert.Same(first, later);
        Assert.Same(firstDispose, laterDispose);
        Assert.True(dns.IsPublished);
        Assert.Equal(1, dns.WithdrawCount);
    }

    private sealed class FailingWithdrawalDnsSdTransport :
        IDnsSdServiceBrowser,
        IDnsSdServicePublisher
    {
        public bool IsPublished { get; private set; }

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

        public void Dispose()
        {
        }

        public void Publish(SignedDiscoveryOffer offer) => IsPublished = true;

        public void Start()
        {
        }

        public void Withdraw()
        {
            WithdrawCount++;
            throw new IOException("CANARY_WITHDRAW_FAILURE");
        }
    }

    private sealed class RecordingDnsSdTransport :
        IDnsSdServiceBrowser,
        IDnsSdServicePublisher
    {
        private int withdrawCount;

        public int WithdrawCount => Volatile.Read(ref withdrawCount);

        public event Action<DnsSdServiceSnapshot>? ServiceChanged;

        public event Action<string>? ServiceRemoved
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }

        public void Publish(SignedDiscoveryOffer offer)
        {
        }

        public void RaiseServiceChanged(DnsSdServiceSnapshot snapshot) =>
            ServiceChanged?.Invoke(snapshot);

        public void Start()
        {
        }

        public void Withdraw()
        {
            Interlocked.Increment(ref withdrawCount);
        }
    }

    private sealed class FailOnSecondPublishDnsSdTransport :
        IDnsSdServiceBrowser,
        IDnsSdServicePublisher
    {
        private int publishCount;

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

        public void Dispose()
        {
        }

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

        public void Withdraw()
        {
        }
    }

    private sealed class BlockingAdvertisementDelay : IDnsSdAdvertisementDelay
    {
        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default) =>
            new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    }

    private sealed class ControlledAdvertisementDelay : IDnsSdAdvertisementDelay
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default) =>
            new(release.Task.WaitAsync(cancellationToken));

        public void Release() => release.TrySetResult();
    }

    private static DeviceIdentity CreateIdentity(string id, string name) =>
        DeviceIdentity.Generate(DeviceId.Parse(id), name);

    private static DnsSdServiceSnapshot CreateDiscoverySnapshot(
        DeviceIdentity peerIdentity)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            peerIdentity,
            4747,
            [new ProtocolVersion(1, 0)],
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x24, SignedDiscoveryOffer.NonceLength).ToArray());
        return DnsSdServiceSnapshot.Create(
            "peer._flowspan._tcp.local",
            offer.Port,
            [IPAddress.Parse("192.168.50.20")],
            DnsSdDiscoveryOfferTxtCodec.Encode(offer));
    }

    private static UnverifiedPairingCandidate CreateCandidate(
        DeviceIdentity peerIdentity,
        IPEndPoint endPoint)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            peerIdentity,
            endPoint.Port,
            [new ProtocolVersion(1, 0)],
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength).ToArray());
        return new UnverifiedPairingCandidate(
            "peer._flowspan._tcp.local",
            offer,
            endPoint,
            PairingCandidateTrustState.UnverifiedPairingRequired);
    }

    private static async Task<DesktopPairingPrompt> WaitForPromptAsync(
        DesktopPairingDecisionSource source)
    {
        if (source.CurrentPrompt is { } current)
        {
            return current;
        }

        var completion = new TaskCompletionSource<DesktopPairingPrompt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Opened
                && source.CurrentPrompt is { } prompt)
            {
                completion.TrySetResult(prompt);
            }
        }

        source.PromptChanged += OnChanged;
        try
        {
            if (source.CurrentPrompt is { } raced)
            {
                return raced;
            }

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            source.PromptChanged -= OnChanged;
        }
    }
}
