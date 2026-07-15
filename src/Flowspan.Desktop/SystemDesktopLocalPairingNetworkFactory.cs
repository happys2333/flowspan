using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;
using Flowspan.Transport.Mdns;

namespace Flowspan.Desktop;

internal sealed record DesktopDnsSdTransport(
    IDnsSdServiceBrowser Browser,
    IDnsSdServicePublisher Publisher);

internal sealed class SystemDesktopLocalPairingNetworkFactory :
    IDesktopLocalPairingNetworkFactory
{
    private readonly Func<IDnsSdAdvertisementDelay> createAdvertisementDelay;
    private readonly Func<DesktopDnsSdTransport> createDnsSdTransport;
    private readonly Func<TcpListener> createListener;
    private readonly Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity;
    private readonly Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust;
    private readonly DesktopPairingDecisionSource pairingDecisions;
    private readonly DesktopActivityRuntime? activityRuntime;

    internal SystemDesktopLocalPairingNetworkFactory(
        DesktopIdentityStartup identityStartup,
        PersistentDesktopTrustAuthority trustAuthority,
        DesktopPairingDecisionSource pairingDecisions,
        DesktopActivityRuntime activityRuntime)
        : this(
            identityStartup.GetRuntimeIdentityAsync,
            trustAuthority.GetRuntimeCoordinatorAsync,
            pairingDecisions,
            CreateDualStackListener,
            CreateProductionDnsSdTransport,
            static () => new SystemDnsSdAdvertisementDelay(),
            activityRuntime)
    {
    }

    internal SystemDesktopLocalPairingNetworkFactory(
        Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity,
        Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust,
        DesktopPairingDecisionSource pairingDecisions,
        Func<TcpListener> createListener,
        Func<DesktopDnsSdTransport> createDnsSdTransport,
        Func<IDnsSdAdvertisementDelay>? createAdvertisementDelay = null,
        DesktopActivityRuntime? activityRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(getIdentity);
        ArgumentNullException.ThrowIfNull(getTrust);
        ArgumentNullException.ThrowIfNull(pairingDecisions);
        ArgumentNullException.ThrowIfNull(createListener);
        ArgumentNullException.ThrowIfNull(createDnsSdTransport);
        this.getIdentity = getIdentity;
        this.getTrust = getTrust;
        this.pairingDecisions = pairingDecisions;
        this.createListener = createListener;
        this.createDnsSdTransport = createDnsSdTransport;
        this.createAdvertisementDelay = createAdvertisementDelay
            ?? (static () => new SystemDnsSdAdvertisementDelay());
        this.activityRuntime = activityRuntime;
    }

    public async ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
        CancellationToken cancellationToken = default)
    {
        DeviceIdentity identity = await getIdentity(cancellationToken)
            .ConfigureAwait(false);
        TrustSessionCoordinator trust = await getTrust(cancellationToken)
            .ConfigureAwait(false);
        AuthenticatedActivitySessionHandler? activityHandler = activityRuntime is null
            ? null
            : await activityRuntime.GetSessionHandlerAsync(cancellationToken)
                .ConfigureAwait(false);
        TcpListener? listener = null;
        DnsSdUnverifiedPairingCandidateSource? candidates = null;
        DesktopTrustedPeerConnectionCoordinator? trustedConnections = null;
        SystemDesktopLocalPairingNetworkSession? session = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener = createListener()
                ?? throw new InvalidOperationException(
                    "The local pairing listener factory returned null.");
            listener.Start();
            DesktopDnsSdTransport dns = createDnsSdTransport()
                ?? throw new InvalidOperationException(
                    "The DNS-SD transport factory returned null.");
            candidates = new DnsSdUnverifiedPairingCandidateSource(
                identity.DeviceId,
                trust,
                dns.Browser);
            var trustedCandidateSource = new DesktopTrustedPeerCandidateSource(
                trust,
                candidates.GetSnapshot);
            trustedConnections = new DesktopTrustedPeerConnectionCoordinator(
                identity.DeviceId,
                trust,
                candidates.GetSnapshot,
                new SystemDesktopPeerReconnectLoopFactory(
                    identity,
                    trust,
                    trustedCandidateSource),
                activityHandler);
            IPEndPoint boundEndPoint = listener.LocalEndpoint as IPEndPoint
                ?? throw new InvalidOperationException(
                    "The local pairing listener did not expose an IP endpoint.");
            ProtocolVersion[] versions = [new ProtocolVersion(1, 0)];
            var sessionProfile = new AuthenticatedInboundSessionProfile(
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive,
                    Capability.ActivityReplace),
                versions,
                capabilityMatch: CapabilityRequirementMatch.Any);
            var inbound = new FlowspanTcpInboundListener(
                listener,
                identity,
                new PairingCeremonyProfile(versions),
                pairingDecisions,
                trust,
                new FlowspanTcpInboundProfile(sessionProfile),
                trustedConnections.SessionHandler);
            var advertisement = new DnsSdPeerAdvertisementService(
                identity,
                boundEndPoint.Port,
                versions,
                dns.Publisher,
                createAdvertisementDelay()
                    ?? throw new InvalidOperationException(
                        "The DNS-SD advertisement delay factory returned null."));
            session = new SystemDesktopLocalPairingNetworkSession(
                listener,
                candidates,
                advertisement,
                inbound,
                identity,
                trust,
                trustedConnections,
                pairingDecisions,
                new PairingCeremonyProfile(versions),
                boundEndPoint.Port);
            session.Start();
            listener = null;
            candidates = null;
            return session;
        }
        catch (Exception failure)
        {
            Exception? cleanupFailure = null;
            try
            {
                if (session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    if (trustedConnections is not null)
                    {
                        await trustedConnections.DisposeAsync().ConfigureAwait(false);
                    }

                    candidates?.Dispose();
                    listener?.Stop();
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    "The local pairing network failed to start and cleanup also failed.",
                    failure,
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private static TcpListener CreateDualStackListener()
    {
        var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true;
        return listener;
    }

    private static DesktopDnsSdTransport CreateProductionDnsSdTransport()
    {
        var adapter = new MakaretuDnsSdServiceBrowser();
        return new DesktopDnsSdTransport(adapter, adapter);
    }

    private sealed class SystemDesktopLocalPairingNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private readonly DnsSdPeerAdvertisementService advertisement;
        private readonly DnsSdUnverifiedPairingCandidateSource candidates;
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private readonly FlowspanTcpInboundListener inbound;
        private readonly DeviceIdentity localIdentity;
        private readonly PairingCeremonyProfile pairingProfile;
        private readonly DesktopPairingDecisionSource pairingDecisions;
        private readonly SemaphoreSlim pairingGate = new(1, 1);
        private readonly TcpListener socket;
        private readonly TrustSessionCoordinator trust;
        private readonly DesktopTrustedPeerConnectionCoordinator trustedConnections;
        private Task? advertisementTask;
        private Task? inboundTask;
        private Task? supervisionTask;
        private int disposed;
        private int faulted;

        public SystemDesktopLocalPairingNetworkSession(
            TcpListener socket,
            DnsSdUnverifiedPairingCandidateSource candidates,
            DnsSdPeerAdvertisementService advertisement,
            FlowspanTcpInboundListener inbound,
            DeviceIdentity localIdentity,
            TrustSessionCoordinator trust,
            DesktopTrustedPeerConnectionCoordinator trustedConnections,
            DesktopPairingDecisionSource pairingDecisions,
            PairingCeremonyProfile pairingProfile,
            int listeningPort)
        {
            this.socket = socket;
            this.candidates = candidates;
            this.advertisement = advertisement;
            this.inbound = inbound;
            this.localIdentity = localIdentity;
            this.trust = trust;
            this.trustedConnections = trustedConnections;
            this.pairingDecisions = pairingDecisions;
            this.pairingProfile = pairingProfile;
            ListeningPort = listeningPort;
            candidates.SnapshotChanged += OnChanged;
            inbound.PairingCompleted += OnPairingCompleted;
            trustedConnections.Changed += OnTrustedConnectionsChanged;
        }

        public event Action? Changed;

        public event Action<IDesktopLocalPairingNetworkSession>? Faulted;

        public event Action? TrustChanged;

        public int ListeningPort { get; }

        public bool IsFaulted => Volatile.Read(ref faulted) != 0;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return candidates.GetSnapshot();
        }

        public ImmutableArray<DesktopTrustedPeerConnectionSnapshot>
            GetTrustedPeerConnections()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return trustedConnections.GetSnapshot();
        }

        public ValueTask RefreshTrustedPeersAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return trustedConnections.RefreshTrustAsync(cancellationToken);
        }

        public async ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            ArgumentNullException.ThrowIfNull(candidate);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token);
            await pairingGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref disposed) != 0,
                    this);
                await using DirectTcpPairingChannel channel =
                    await DirectTcpPairingChannel.ConnectAsync(
                        candidate.EndPoint,
                        linked.Token).ConfigureAwait(false);
                var boundDecisions = new DiscoveryBoundPairingDecisionSource(
                    candidate,
                    pairingDecisions);
                var ceremony = new PairingCeremony(
                    pairingProfile,
                    boundDecisions,
                    trust);
                PairingCeremonyResult result = await ceremony.RunInitiatorAsync(
                    channel,
                    localIdentity,
                    linked.Token).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await trustedConnections.RefreshTrustAsync(linked.Token)
                        .ConfigureAwait(false);
                    PublishTrustChanged();
                    PublishChanged();
                }

                return result;
            }
            finally
            {
                pairingGate.Release();
            }
        }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            inboundTask = inbound.RunAsync(lifetimeCancellation.Token).AsTask();
            advertisementTask = advertisement
                .RunAsync(lifetimeCancellation.Token).AsTask();
            ThrowIfLoopEndedDuringStart(inboundTask);
            ThrowIfLoopEndedDuringStart(advertisementTask);
            trustedConnections.Start();
            supervisionTask = SuperviseLoopsAsync(inboundTask, advertisementTask);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            var failures = new List<Exception>();
            try
            {
                lifetimeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                trustedConnections.Cancel();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                socket.Stop();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            bool pairingDrained = false;
            try
            {
                await pairingGate.WaitAsync().ConfigureAwait(false);
                pairingDrained = true;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            await CaptureLoopFailureAsync(inboundTask, failures).ConfigureAwait(false);
            await CaptureLoopFailureAsync(advertisementTask, failures).ConfigureAwait(false);
            await CaptureLoopFailureAsync(supervisionTask, failures).ConfigureAwait(false);
            candidates.SnapshotChanged -= OnChanged;
            inbound.PairingCompleted -= OnPairingCompleted;
            trustedConnections.Changed -= OnTrustedConnectionsChanged;
            try
            {
                await trustedConnections.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                candidates.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            lifetimeCancellation.Dispose();
            if (pairingDrained)
            {
                pairingGate.Release();
            }

            pairingGate.Dispose();
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "One or more local pairing network resources failed to close.",
                    failures);
            }
        }

        private async Task CaptureLoopFailureAsync(
            Task? loop,
            List<Exception> failures)
        {
            if (loop is null)
            {
                return;
            }

            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private void OnChanged()
        {
            trustedConnections.NotifyCandidatesChanged();
            PublishChanged();
        }

        private void OnTrustedConnectionsChanged() => PublishChanged();

        private async Task SuperviseLoopsAsync(Task inboundLoop, Task advertisementLoop)
        {
            await Task.WhenAny(inboundLoop, advertisementLoop).ConfigureAwait(false);
            if (Volatile.Read(ref disposed) != 0
                || lifetimeCancellation.IsCancellationRequested
                || Interlocked.CompareExchange(ref faulted, 1, 0) != 0)
            {
                return;
            }

            try
            {
                lifetimeCancellation.Cancel();
            }
            catch
            {
                // Fault notification and disposal still need to proceed.
            }

            try
            {
                trustedConnections.Cancel();
            }
            catch
            {
                // Fault notification and disposal still need to proceed.
            }

            try
            {
                socket.Stop();
            }
            catch
            {
                // Disposal captures any independently observable close failure.
            }

            PublishFaulted();
        }

        private void OnPairingCompleted(InboundPairingCompleted completed)
        {
            if (completed.Result.Succeeded)
            {
                try
                {
                    trustedConnections.RefreshTrustAsync(lifetimeCancellation.Token)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                    when (lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Trust is already durable; the next explicit refresh can reconcile status.
                }

                PublishTrustChanged();
                PublishChanged();
            }
        }

        private void PublishTrustChanged()
        {
            foreach (Action subscriber in
                     TrustChanged?.GetInvocationList().Cast<Action>() ?? [])
            {
                try
                {
                    subscriber();
                }
                catch
                {
                    // Presentation callbacks cannot own network lifetime.
                }
            }
        }

        private void PublishFaulted()
        {
            foreach (Action<IDesktopLocalPairingNetworkSession> subscriber in
                     Faulted?.GetInvocationList()
                         .Cast<Action<IDesktopLocalPairingNetworkSession>>() ?? [])
            {
                try
                {
                    subscriber(this);
                }
                catch
                {
                    // Lifecycle observers cannot own network cleanup.
                }
            }
        }

        private void PublishChanged()
        {
            foreach (Action subscriber in Changed?.GetInvocationList().Cast<Action>() ?? [])
            {
                try
                {
                    subscriber();
                }
                catch
                {
                    // Presentation callbacks cannot own network lifetime.
                }
            }
        }

        private static void ThrowIfLoopEndedDuringStart(Task loop)
        {
            if (loop.IsCompleted)
            {
                loop.GetAwaiter().GetResult();
                throw new InvalidOperationException(
                    "A local pairing network loop ended during startup.");
            }
        }
    }
}
