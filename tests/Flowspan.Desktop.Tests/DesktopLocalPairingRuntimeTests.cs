using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
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

        await runtime.RefreshTrustedPeersAsync();
        Assert.Equal(1, Assert.IsType<StubNetworkSession>(factory.LastSession)
            .TrustRefreshCount);
    }

    [Fact]
    public async Task EnabledObserverCanReenterLifecycleWithoutWaitingForPublication()
    {
        var factory = new RecordingNetworkFactory();
        await using var runtime = new DesktopLocalPairingRuntime(factory);
        Task? reentrantDisable = null;
        bool completedInsideObserver = false;
        runtime.Changed += () =>
        {
            if (runtime.Status != DesktopLocalPairingStatus.Enabled
                || reentrantDisable is not null)
            {
                return;
            }

            reentrantDisable = runtime.DisableAsync().AsTask();
            completedInsideObserver = reentrantDisable.IsCompleted;
        };

        await runtime.EnableAsync();
        await reentrantDisable!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(completedInsideObserver);
        Assert.True(Assert.IsType<StubNetworkSession>(factory.LastSession).Disposed);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);
    }

    [Fact]
    public async Task SessionEventsRaisedDuringAttachPublishAfterLifecycleOperation()
    {
        var session = new EventsDuringAttachNetworkSession();
        await using var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));
        Task? changedDisable = null;
        Task? trustDisable = null;
        bool changedCompletedInsideObserver = false;
        bool trustCompletedInsideObserver = false;
        runtime.Changed += () =>
        {
            if (changedDisable is not null)
            {
                return;
            }

            changedDisable = runtime.DisableAsync().AsTask();
            changedCompletedInsideObserver = changedDisable.IsCompleted;
        };
        runtime.TrustChanged += () =>
        {
            if (trustDisable is not null)
            {
                return;
            }

            trustDisable = runtime.DisableAsync().AsTask();
            trustCompletedInsideObserver = trustDisable.IsCompleted;
        };

        await runtime.EnableAsync();
        await Task.WhenAll(changedDisable!, trustDisable!)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(changedCompletedInsideObserver);
        Assert.True(trustCompletedInsideObserver);
        Assert.True(session.Disposed);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);
    }

    [Fact]
    public async Task SessionAccessorReentryDoesNotWaitOnLifecycleBoundary()
    {
        var session = new ReentrantAttachNetworkSession();
        await using var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));
        session.DisableRuntime = runtime.DisableAsync;

        await runtime.EnableAsync();

        Assert.True(session.ReentrantDisableCompletedSynchronously);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ReentrantDisable!);
        Assert.True(runtime.IsEnabled);
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
    public async Task SessionDisposeCanInitiateRuntimeDisposeWithoutWaiting()
    {
        var session = new ReentrantDisposeNetworkSession();
        var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));
        session.DisposeRuntime = runtime.DisposeAsync;
        await runtime.EnableAsync();

        await runtime.DisableAsync();
        await runtime.DisposeAsync();

        Assert.True(session.ReentrantDisposeCompletedSynchronously);
    }

    [Fact]
    public async Task DisabledObserverRunsAfterSessionStopAndCanReenterLifecycle()
    {
        var factory = new RecordingNetworkFactory();
        await using var runtime = new DesktopLocalPairingRuntime(factory);
        await runtime.EnableAsync();
        StubNetworkSession first = Assert.IsType<StubNetworkSession>(
            factory.LastSession);
        Task? reentrantEnable = null;
        bool reentryStarted = false;
        bool stoppedBeforePublication = false;
        bool completedInsideObserver = false;
        runtime.Changed += () =>
        {
            if (runtime.Status != DesktopLocalPairingStatus.Disabled
                || reentryStarted)
            {
                return;
            }

            reentryStarted = true;
            stoppedBeforePublication = first.Disposed;
            reentrantEnable = runtime.EnableAsync().AsTask();
            completedInsideObserver = reentrantEnable.IsCompleted;
        };

        await runtime.DisableAsync();
        await reentrantEnable!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(stoppedBeforePublication);
        Assert.True(completedInsideObserver);
        Assert.Equal(DesktopLocalPairingStatus.Enabled, runtime.Status);
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
    public async Task DisposeRejectsAndStopsCancellationIgnoringLateEnableSession()
    {
        var factory = new CancellationIgnoringLateNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        var publications = new List<DesktopLocalPairingStatus>();
        runtime.Changed += () => publications.Add(runtime.Status);
        Task enabling = runtime.EnableAsync().AsTask();
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = runtime.DisposeAsync().AsTask();
        factory.ReleaseStart();

        await factory.Session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enabling.WaitAsync(TimeSpan.FromSeconds(2)));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, factory.Session.AttachCount);
        Assert.DoesNotContain(DesktopLocalPairingStatus.Enabled, publications);
    }

    [Fact]
    public async Task DisposeRejectsEnableWhenCloseRacesSessionAttach()
    {
        var session = new BlockingAttachNetworkSession();
        var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));
        Task enabling = Task.Run(async () => await runtime.EnableAsync());
        await session.AttachEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = runtime.DisposeAsync().AsTask();
        session.ReleaseAttach();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            enabling.WaitAsync(TimeSpan.FromSeconds(2)));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(session.Disposed);
        Assert.NotEqual(DesktopLocalPairingStatus.Enabled, runtime.Status);
    }

    [Fact]
    public async Task DisposeStopsSessionBeforeBlockingThrowingCancellationCallbackReturns()
    {
        const string canary = "HOSTILE_RUNTIME_CANCELLATION_CALLBACK";
        const string disposeCanary = "HOSTILE_RUNTIME_SESSION_DISPOSE";
        var factory = new HostileCancellationNetworkFactory(canary, disposeCanary);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var enabledPublicationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var enabledPublicationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Changed += OnChanged;
        Task enabling = Task.Run(async () => await runtime.EnableAsync());
        await enabledPublicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = Task.Run(async () => await runtime.DisposeAsync());
        await factory.CancellationCallbackEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        enabledPublicationRelease.TrySetResult();

        Exception? disposalFailure = null;
        try
        {
            await factory.Session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            factory.ReleaseCancellationCallback();
            try
            {
                await disposing.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }

            await enabling.WaitAsync(TimeSpan.FromSeconds(2));
            runtime.Changed -= OnChanged;
            if (!factory.Session.IsDisposed)
            {
                await factory.Session.DisposeAsync();
            }
        }

        Assert.True(factory.Session.IsDisposed);
        Assert.NotNull(disposalFailure);
        Assert.Contains(canary, disposalFailure.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            disposeCanary,
            disposalFailure.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            DesktopLocalPairingStatus.CleanupUnconfirmed,
            runtime.Status);

        void OnChanged()
        {
            if (runtime.Status == DesktopLocalPairingStatus.Enabled)
            {
                enabledPublicationEntered.TrySetResult();
                enabledPublicationRelease.Task.GetAwaiter().GetResult();
            }
        }
    }

    [Fact]
    public async Task ConcurrentDisposeCallersJoinTheSameRuntimeCleanupFailure()
    {
        const string canary = "RUNTIME_SHARED_DISPOSAL_FAILURE";
        var session = new BlockingFailingDisposeNetworkSession(canary);
        var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));
        await runtime.EnableAsync();

        Task first = runtime.DisposeAsync().AsTask();
        await session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task second = runtime.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        session.ReleaseDispose();
        InvalidOperationException firstFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                first.WaitAsync(TimeSpan.FromSeconds(2)));
        InvalidOperationException secondFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                second.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Same(firstFailure, secondFailure);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task DisposalKeepsLifecycleGateReleasableForAdmittedWaiters()
    {
        var runtime = new DesktopLocalPairingRuntime(
            new RecordingNetworkFactory());
        await runtime.DisposeAsync();
        SemaphoreSlim gate = Assert.IsType<SemaphoreSlim>(
            typeof(DesktopLocalPairingRuntime)
                .GetField(
                    "lifecycleGate",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(runtime));

        await gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Exception? releaseFailure = Record.Exception(() => gate.Release());

        Assert.Null(releaseFailure);
    }

    [Fact]
    public async Task DisposeDiscardsSessionEventsQueuedDuringDetach()
    {
        var session = new EventDuringDetachNetworkSession();
        var runtime = new DesktopLocalPairingRuntime(
            new FixedNetworkFactory(session));
        int publications = 0;
        runtime.Changed += () => publications++;
        await runtime.EnableAsync();
        publications = 0;

        await runtime.DisposeAsync();

        Assert.Equal(0, publications);
    }

    [Fact]
    public async Task EnableCancellationCallbackDisposeDoesNotWaitOnItsOwnCleanup()
    {
        var factory = new ReentrantDisposeCancellationNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        factory.DisposeRuntime = runtime.DisposeAsync;
        Task enabling = runtime.EnableAsync().AsTask();
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = runtime.DisposeAsync().AsTask();
        await factory.CancellationCallbackCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        factory.ReleaseStart();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enabling.WaitAsync(TimeSpan.FromSeconds(2)));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(factory.ReentrantDisposeCompletedSynchronously);
        Assert.True(factory.Session.Disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CallerCancellationCallbackCanInitiateDisposeWithoutWaiting()
    {
        var factory = new ReentrantDisposeCancellationNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        factory.DisposeRuntime = runtime.DisposeAsync;
        using var cancellation = new CancellationTokenSource();
        Task enabling = runtime.EnableAsync(cancellation.Token).AsTask();
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await factory.CancellationCallbackCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(factory.ReentrantDisposeCompletedSynchronously);
        factory.ReleaseStart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enabling.WaitAsync(TimeSpan.FromSeconds(2)));
        await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(factory.Session.Disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CapturedFactoryContextExpiresAfterStartBoundaryReturns()
    {
        var factory = new CapturedContextNetworkFactory();
        await using var runtime = new DesktopLocalPairingRuntime(factory);
        factory.DisableRuntime = runtime.DisableAsync;

        await runtime.EnableAsync();
        factory.RunCapturedOperation();
        await factory.OperationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(factory.OperationFailure);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);
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
        Assert.Equal<ProtocolVersion>(
            ProtocolFeatures.ProductionSupportedVersions,
            offer.ProtocolVersions);
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
    public async Task TrustedPeerWaitingWorkerIsOwnedAndDrainedByNetworkSession()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Desk");
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UtcNow,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var trust = new TrustSessionCoordinator(trustStore);
        using var decisions = new DesktopPairingDecisionSource();
        var dns = new RecordingDnsSdTransport();
        var factory = new SystemDesktopLocalPairingNetworkFactory(
            _ => ValueTask.FromResult(identity),
            _ => ValueTask.FromResult(trust),
            decisions,
            () => new TcpListener(IPAddress.Loopback, 0),
            () => new DesktopDnsSdTransport(dns, dns));

        IDesktopLocalPairingNetworkSession session = await factory.StartAsync();
        await WaitUntilAsync(
            () => session.GetTrustedPeerConnections() is [var status]
                && status.State is DesktopTrustedPeerConnectionState.WaitingForPeer
                    or DesktopTrustedPeerConnectionState.Retrying,
            TimeSpan.FromSeconds(2));

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, dns.WithdrawCount);
        Assert.Equal(1, dns.DisposeCount);
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
    public async Task FaultObserverRunsAfterSessionStopAndCanReenterLifecycle()
    {
        var factory = new RecordingNetworkFactory();
        await using var runtime = new DesktopLocalPairingRuntime(factory);
        await runtime.EnableAsync();
        StubNetworkSession first = Assert.IsType<StubNetworkSession>(
            factory.LastSession);
        var faultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? reentrantEnable = null;
        bool reentryStarted = false;
        bool stoppedBeforePublication = false;
        bool completedInsideObserver = false;
        runtime.Changed += () =>
        {
            if (runtime.Status != DesktopLocalPairingStatus.Faulted
                || reentryStarted)
            {
                return;
            }

            reentryStarted = true;
            stoppedBeforePublication = first.Disposed;
            reentrantEnable = runtime.EnableAsync().AsTask();
            completedInsideObserver = reentrantEnable.IsCompleted;
            faultPublished.TrySetResult();
        };

        first.RaiseFault();

        await faultPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await reentrantEnable!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(stoppedBeforePublication);
        Assert.True(completedInsideObserver);
        Assert.Equal(DesktopLocalPairingStatus.Enabled, runtime.Status);
        Assert.Equal(2, factory.StartCount);
    }

    [Fact]
    public async Task BackgroundFaultCleanupFailureRetainsOwnerAndBlocksRetry()
    {
        var factory = new BackgroundCleanupFailureNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        await runtime.EnableAsync();

        factory.Session.RaiseFault();

        await WaitUntilAsync(
            () => runtime.Status == DesktopLocalPairingStatus.CleanupUnconfirmed,
            TimeSpan.FromSeconds(2));
        Assert.False(runtime.IsEnabled);
        Assert.Equal(4747, runtime.ListeningPort);
        Assert.Equal(1, factory.Session.DisposeCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.EnableAsync().AsTask());
        Assert.Equal(1, factory.StartCount);

        await runtime.DisposeAsync();
        Assert.Equal(2, factory.Session.DisposeCount);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);
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
    public async Task StartupCleanupFailureRetainsOwnerAndBlocksRetry()
    {
        var factory = new StartupCleanupFailureNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            runtime.EnableAsync().AsTask());

        Assert.Equal(DesktopLocalPairingStatus.CleanupUnconfirmed, runtime.Status);
        Assert.False(runtime.IsEnabled);
        Assert.Equal(4747, runtime.ListeningPort);
        Assert.Equal(1, factory.StartCount);
        Assert.Equal(1, factory.Session.DisposeCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.EnableAsync().AsTask());
        Assert.Equal(1, factory.StartCount);

        await runtime.DisposeAsync();
        Assert.Equal(2, factory.Session.DisposeCount);
        Assert.Equal(DesktopLocalPairingStatus.Disabled, runtime.Status);
        Assert.Null(runtime.ListeningPort);
    }

    [Fact]
    public async Task DetachFailureRetainsOwnerAndBlocksEnableUntilStopRetry()
    {
        var first = new DetachFailureNetworkSession();
        var second = new StubNetworkSession();
        var factory = new QueuedNetworkFactory(first, second);
        var runtime = new DesktopLocalPairingRuntime(factory);
        try
        {
            await runtime.EnableAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.DisableAsync());

            Assert.Equal(
                DesktopLocalPairingStatus.CleanupUnconfirmed,
                runtime.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.EnableAsync());
            Assert.Equal(1, factory.StartCount);

            await runtime.DisableAsync();
            await runtime.EnableAsync();

            Assert.Equal(2, factory.StartCount);
            Assert.True(runtime.IsEnabled);
        }
        finally
        {
            try
            {
                await runtime.DisposeAsync();
            }
            catch
            {
            }
        }
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

    private sealed class QueuedNetworkFactory(
        params IDesktopLocalPairingNetworkSession[] sessions) :
        IDesktopLocalPairingNetworkFactory
    {
        private readonly Queue<IDesktopLocalPairingNetworkSession> remaining =
            new(sessions);

        public int StartCount { get; private set; }

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return ValueTask.FromResult(remaining.Dequeue());
        }
    }

    private sealed class DetachFailureNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private Action? changed;
        private int changedRemoveCalls;

        public event Action? Changed
        {
            add => changed += value;
            remove
            {
                changedRemoveCalls++;
                if (changedRemoveCalls == 1)
                {
                    throw new InvalidOperationException("detach failed");
                }

                changed -= value;
            }
        }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StartupCleanupFailureNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public int StartCount { get; private set; }

        public StartupCleanupFailureNetworkSession Session { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class StartupCleanupFailureNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int DisposeCount { get; private set; }

        public bool IsFaulted => true;

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeCount == 1
                ? ValueTask.FromException(
                    new IOException("STARTUP_CLEANUP_UNCONFIRMED"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class BackgroundCleanupFailureNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public int StartCount { get; private set; }

        public BackgroundCleanupFailureNetworkSession Session { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class BackgroundCleanupFailureNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public event Action<IDesktopLocalPairingNetworkSession>? Faulted;

        public int DisposeCount { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeCount == 1
                ? ValueTask.FromException(
                    new IOException("BACKGROUND_CLEANUP_UNCONFIRMED"))
                : ValueTask.CompletedTask;
        }

        public void RaiseFault() => Faulted?.Invoke(this);
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

    private sealed class CancellationIgnoringLateNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public LateNetworkSession Session { get; } = new();

        public async ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return Session;
        }

        public void ReleaseStart() => release.TrySetResult();
    }

    private sealed class ReentrantDisposeCancellationNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationCallbackCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ValueTask>? DisposeRuntime { get; set; }

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ReentrantDisposeCompletedSynchronously { get; private set; }

        public LateNetworkSession Session { get; } = new();

        public async ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    ValueTask reentrantDispose = DisposeRuntime!();
                    ReentrantDisposeCompletedSynchronously =
                        reentrantDispose.IsCompletedSuccessfully;
                    CancellationCallbackCompleted.TrySetResult();
                });
            Entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return Session;
        }

        public void ReleaseStart() => release.TrySetResult();
    }

    private sealed class CapturedContextNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        private readonly TaskCompletionSource runCapturedOperation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ValueTask>? DisableRuntime { get; set; }

        public TaskCompletionSource OperationCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? OperationFailure { get; private set; }

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            _ = Task.Run(
                async () =>
                {
                    await runCapturedOperation.Task.ConfigureAwait(false);
                    try
                    {
                        await DisableRuntime!().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        OperationFailure = exception;
                    }
                    finally
                    {
                        OperationCompleted.TrySetResult();
                    }
                },
                CancellationToken.None);
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(
                new StubNetworkSession());
        }

        public void RunCapturedOperation() => runCapturedOperation.TrySetResult();
    }

    private sealed class LateNetworkSession : IDesktopLocalPairingNetworkSession
    {
        private Action? changed;
        private Action<IDesktopLocalPairingNetworkSession>? faulted;
        private Action? trustChanged;

        public int AttachCount { get; private set; }

        public event Action? Changed
        {
            add
            {
                AttachCount++;
                changed += value;
            }
            remove => changed -= value;
        }

        public event Action<IDesktopLocalPairingNetworkSession>? Faulted
        {
            add
            {
                AttachCount++;
                faulted += value;
            }
            remove => faulted -= value;
        }

        public event Action? TrustChanged
        {
            add
            {
                AttachCount++;
                trustChanged += value;
            }
            remove => trustChanged -= value;
        }

        public TaskCompletionSource Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingAttachNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource attachRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? changed;

        public event Action? Changed
        {
            add
            {
                AttachEntered.TrySetResult();
                attachRelease.Task.GetAwaiter().GetResult();
                changed += value;
            }
            remove => changed -= value;
        }

        public TaskCompletionSource AttachEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public int ListeningPort => 4747;

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

        public void ReleaseAttach() => attachRelease.TrySetResult();
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

    private sealed class EventsDuringAttachNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private Action? changed;
        private Action? trustChanged;

        public event Action? Changed
        {
            add
            {
                changed += value;
                value?.Invoke();
            }
            remove => changed -= value;
        }

        public event Action? TrustChanged
        {
            add
            {
                trustChanged += value;
                value?.Invoke();
            }
            remove => trustChanged -= value;
        }

        public bool Disposed { get; private set; }

        public int ListeningPort => 4747;

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
    }

    private sealed class ReentrantAttachNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add
            {
                ReentrantDisable = DisableRuntime!().AsTask();
                ReentrantDisableCompletedSynchronously =
                    ReentrantDisable.IsCompleted;
            }
            remove { }
        }

        public Func<ValueTask>? DisableRuntime { get; set; }

        public Task? ReentrantDisable { get; private set; }

        public bool ReentrantDisableCompletedSynchronously { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ReentrantDisposeNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Func<ValueTask>? DisposeRuntime { get; set; }

        public bool ReentrantDisposeCompletedSynchronously { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            ValueTask reentrantDispose = DisposeRuntime!();
            ReentrantDisposeCompletedSynchronously =
                reentrantDispose.IsCompletedSuccessfully;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EventDuringDetachNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private Action? changed;

        public event Action? Changed
        {
            add => changed += value;
            remove
            {
                value?.Invoke();
                changed -= value;
            }
        }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HostileCancellationNetworkFactory(
        string canary,
        string disposeCanary) :
        IDesktopLocalPairingNetworkFactory
    {
        private readonly TaskCompletionSource cancellationCallbackRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationCallbackEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public HostileCancellationNetworkSession Session { get; } =
            new(disposeCanary);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken.Register(() =>
            {
                CancellationCallbackEntered.TrySetResult();
                cancellationCallbackRelease.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException(canary);
            });
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }

        public void ReleaseCancellationCallback() =>
            cancellationCallbackRelease.TrySetResult();
    }

    private sealed class HostileCancellationNetworkSession(string disposeCanary) :
        IDesktopLocalPairingNetworkSession
    {
        public TaskCompletionSource Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public bool IsDisposed { get; private set; }

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            Disposed.TrySetResult();
            return ValueTask.FromException(
                new InvalidOperationException(disposeCanary));
        }
    }

    private sealed class BlockingFailingDisposeNetworkSession(string canary) :
        IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeEntered.TrySetResult();
            await disposeRelease.Task.ConfigureAwait(false);
            throw new InvalidOperationException(canary);
        }

        public void ReleaseDispose() => disposeRelease.TrySetResult();
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

        public int TrustRefreshCount { get; private set; }

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask RefreshTrustedPeersAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrustRefreshCount++;
            return ValueTask.CompletedTask;
        }

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
