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
        DesktopActivityNetworkBindings? activityBindings = activityRuntime is null
            ? null
            : await activityRuntime.GetNetworkBindingsAsync(cancellationToken)
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
            var remoteWindowPeerResolver =
                new DesktopRemoteWindowPeerEndpointResolver(
                    trust,
                    candidates.GetSnapshot);
            var trustedCandidateSource = new DesktopTrustedPeerCandidateSource(
                trust,
                candidates.GetSnapshot);
            IAuthenticatedControlSessionHandler? activitySessionHandler =
                activityBindings is null
                    ? null
                    : new DesktopRemoteWindowPeerSessionHandler(
                        activityBindings.SessionHandler,
                        remoteWindowPeerResolver);
            trustedConnections = new DesktopTrustedPeerConnectionCoordinator(
                identity.DeviceId,
                trust,
                candidates.GetSnapshot,
                new SystemDesktopPeerReconnectLoopFactory(
                    identity,
                    trust,
                    trustedCandidateSource),
                activitySessionHandler);
            IPEndPoint boundEndPoint = listener.LocalEndpoint as IPEndPoint
                ?? throw new InvalidOperationException(
                    "The local pairing listener did not expose an IP endpoint.");
            ProtocolVersion[] versions =
                ProtocolFeatures.ProductionSupportedVersions.ToArray();
            var sessionProfile = new AuthenticatedInboundSessionProfile(
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive,
                    Capability.ActivityReplace,
                    Capability.ActivitySwap,
                    Capability.MirrorView,
                    Capability.MirrorDrive,
                    Capability.SceneApply),
                versions,
                capabilityMatch: CapabilityRequirementMatch.Any);
            var inbound = new FlowspanTcpInboundListener(
                listener,
                identity,
                new PairingCeremonyProfile(versions),
                pairingDecisions,
                trust,
                new FlowspanTcpInboundProfile(sessionProfile),
                trustedConnections.SessionHandler,
                remoteWindowMediaSessions:
                    activityBindings?.RemoteWindowMediaSessions);
            var advertisementPublisher = new CleanupTrackingDnsSdPublisher(
                dns.Publisher);
            var advertisement = new DnsSdPeerAdvertisementService(
                identity,
                boundEndPoint.Port,
                versions,
                advertisementPublisher,
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
                advertisementPublisher,
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

    private sealed class CleanupTrackingDnsSdPublisher(
        IDnsSdServicePublisher inner) : IDnsSdServicePublisher
    {
        private readonly Lock gate = new();
        private bool stopped;
        private Exception? withdrawalFailure;

        public Exception? WithdrawalFailure =>
            Volatile.Read(ref withdrawalFailure);

        public void Publish(SignedDiscoveryOffer offer)
        {
            lock (gate)
            {
                if (!stopped)
                {
                    inner.Publish(offer);
                }
            }
        }

        public void Withdraw() => StopPublishing();

        public void StopPublishing()
        {
            lock (gate)
            {
                if (stopped)
                {
                    return;
                }

                stopped = true;
                try
                {
                    inner.Withdraw();
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(
                        ref withdrawalFailure,
                        exception,
                        null);
                    throw;
                }
            }
        }
    }

    private sealed class SystemDesktopLocalPairingNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private static readonly AsyncLocal<PublicationLease?> CurrentPublication =
            new();

        private readonly DnsSdPeerAdvertisementService advertisement;
        private readonly CleanupTrackingDnsSdPublisher advertisementPublisher;
        private readonly DnsSdUnverifiedPairingCandidateSource candidates;
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private readonly FlowspanTcpInboundListener inbound;
        private readonly DeviceIdentity localIdentity;
        private readonly Lock pairingOperationGate = new();
        private readonly HashSet<PublicationKind> pendingPublicationKinds = [];
        private readonly Queue<PublicationKind> pendingPublications = new();
        private readonly PairingCeremonyProfile pairingProfile;
        private readonly DesktopPairingDecisionSource pairingDecisions;
        private readonly Lock publicationGate = new();
        private readonly SemaphoreSlim pairingGate = new(1, 1);
        private readonly TcpListener socket;
        private readonly TrustSessionCoordinator trust;
        private readonly DesktopTrustedPeerConnectionCoordinator trustedConnections;
        private readonly TaskCompletionSource disposalCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? advertisementTask;
        private int activePairingOperations;
        private Task? inboundTask;
        private TaskCompletionSource? pairingOperationsDrained;
        private PublicationLease? activePublication;
        private bool publicationClosed;
        private TaskCompletionSource? publicationDrainCompletion;
        private Exception? publicationWorkerFailure;
        private bool publicationWorkerRunning;
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
            CleanupTrackingDnsSdPublisher advertisementPublisher,
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
            this.advertisementPublisher = advertisementPublisher;
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
            RegisterPairingOperation();
            bool enteredPairingGate = false;
            try
            {
                using CancellationTokenSource linked =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        lifetimeCancellation.Token);
                await pairingGate.WaitAsync(linked.Token).ConfigureAwait(false);
                enteredPairingGate = true;
                linked.Token.ThrowIfCancellationRequested();
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
                    QueueTrustChanged();
                    QueueChanged();
                }

                return result;
            }
            finally
            {
                if (enteredPairingGate)
                {
                    pairingGate.Release();
                }

                CompletePairingOperation();
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

        public ValueTask DisposeAsync()
        {
            PublicationLease? callerLease = CurrentPublication.Value;
            Task publicationDrainTask;
            bool selfDisposal = false;
            bool startDisposal = false;
            lock (publicationGate)
            {
                if (!publicationClosed)
                {
                    publicationClosed = true;
                    pendingPublications.Clear();
                    pendingPublicationKinds.Clear();
                    Volatile.Write(ref disposed, 1);
                    startDisposal = true;
                }

                if (callerLease is not null
                    && callerLease.Active
                    && ReferenceEquals(callerLease.Owner, this)
                    && ReferenceEquals(activePublication, callerLease))
                {
                    selfDisposal = true;
                }

                publicationDrainTask = GetPublicationDrainTask();
            }

            if (startDisposal)
            {
                _ = DisposeAndCompleteAsync(publicationDrainTask);
            }

            return selfDisposal
                ? ValueTask.CompletedTask
                : new ValueTask(disposalCompletion.Task);
        }

        private Task GetPublicationDrainTask() =>
            publicationDrainCompletion?.Task ?? Task.CompletedTask;

        private async Task DisposeAndCompleteAsync(Task publicationDrainTask)
        {
            try
            {
                await DisposeResourcesAsync(publicationDrainTask).ConfigureAwait(false);
                disposalCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                disposalCompletion.TrySetException(exception);
            }
        }

        private async Task DisposeResourcesAsync(Task publicationDrainTask)
        {
            var cleanupFailures = new List<Exception>();
            pairingDecisions.RunWithCancellationPublicationsDeferred(() =>
            {
                try
                {
                    socket.Stop();
                }
                catch (Exception exception)
                {
                    AddFailureByReference(cleanupFailures, exception);
                }

                try
                {
                    advertisementPublisher.StopPublishing();
                }
                catch (Exception exception)
                {
                    AddFailureByReference(cleanupFailures, exception);
                }
            });

            try
            {
                trustedConnections.Cancel();
            }
            catch (Exception exception)
            {
                AddFailureByReference(cleanupFailures, exception);
            }

            try
            {
                CancelLifetime();
            }
            catch (Exception exception)
            {
                AddFailureByReference(cleanupFailures, exception);
            }

            Task pairingDrainTask = WaitForPairingOperationsAsync();
            Task loopDrainTask = Task.WhenAll(
                DrainLoopAsync(inboundTask),
                DrainLoopAsync(advertisementTask),
                DrainLoopAsync(supervisionTask));

            Exception? publicationPrimaryFailure = null;
            try
            {
                await publicationDrainTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                publicationPrimaryFailure = exception;
            }

            if (Volatile.Read(ref publicationWorkerFailure) is
                { } retainedPublicationFailure)
            {
                publicationPrimaryFailure = retainedPublicationFailure;
            }

            try
            {
                await pairingDrainTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AddFailureByReference(cleanupFailures, exception);
            }

            await loopDrainTask.ConfigureAwait(false);
            if (advertisementPublisher.WithdrawalFailure is { } withdrawalFailure)
            {
                AddFailureByReference(cleanupFailures, withdrawalFailure);
            }
            candidates.SnapshotChanged -= OnChanged;
            inbound.PairingCompleted -= OnPairingCompleted;
            trustedConnections.Changed -= OnTrustedConnectionsChanged;
            try
            {
                await trustedConnections.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AddFailureByReference(cleanupFailures, exception);
            }

            try
            {
                candidates.Dispose();
            }
            catch (Exception exception)
            {
                AddFailureByReference(cleanupFailures, exception);
            }

            lifetimeCancellation.Dispose();
            pairingGate.Dispose();
            var orderedFailures = new List<Exception>(
                cleanupFailures.Count + (publicationPrimaryFailure is null ? 0 : 1));
            if (publicationPrimaryFailure is not null)
            {
                orderedFailures.Add(publicationPrimaryFailure);
            }

            foreach (Exception cleanupFailure in cleanupFailures)
            {
                AddFailureByReference(orderedFailures, cleanupFailure);
            }

            if (orderedFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(orderedFailures[0]).Throw();
            }

            if (orderedFailures.Count > 1)
            {
                throw new AggregateException(
                    "One or more local pairing network resources failed to close.",
                    orderedFailures);
            }
        }

        private static void AddFailureByReference(
            List<Exception> failures,
            Exception candidate)
        {
            if (!failures.Any(failure => ReferenceEquals(failure, candidate)))
            {
                failures.Add(candidate);
            }
        }

        private static async Task DrainLoopAsync(Task? loop)
        {
            if (loop is null)
            {
                return;
            }

            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                // Loop faults are published by supervision. Cleanup failures are
                // captured at the resource boundary that owns the close attempt.
            }
        }

        private void RegisterPairingOperation()
        {
            lock (pairingOperationGate)
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref disposed) != 0,
                    this);
                activePairingOperations++;
            }
        }

        private void CompletePairingOperation()
        {
            TaskCompletionSource? drained = null;
            lock (pairingOperationGate)
            {
                activePairingOperations--;
                if (activePairingOperations == 0)
                {
                    drained = pairingOperationsDrained;
                    pairingOperationsDrained = null;
                }
            }

            drained?.TrySetResult();
        }

        private Task WaitForPairingOperationsAsync()
        {
            lock (pairingOperationGate)
            {
                if (activePairingOperations == 0)
                {
                    return Task.CompletedTask;
                }

                pairingOperationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return pairingOperationsDrained.Task;
            }
        }

        private void OnChanged()
        {
            trustedConnections.NotifyCandidatesChanged();
            QueueChanged();
        }

        private void OnTrustedConnectionsChanged() => QueueChanged();

        private async Task SuperviseLoopsAsync(Task inboundLoop, Task advertisementLoop)
        {
            await Task.WhenAny(inboundLoop, advertisementLoop).ConfigureAwait(false);
            if (Volatile.Read(ref disposed) != 0
                || lifetimeCancellation.IsCancellationRequested
                || Interlocked.CompareExchange(ref faulted, 1, 0) != 0)
            {
                return;
            }

            pairingDecisions.RunWithCancellationPublicationsDeferred(() =>
            {
                try
                {
                    socket.Stop();
                }
                catch
                {
                    // Fault notification and disposal still need to proceed.
                }

                try
                {
                    advertisementPublisher.StopPublishing();
                }
                catch
                {
                    // Disposal reports the recorded withdrawal failure.
                }
            });

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
                CancelLifetime();
            }
            catch
            {
                // Disposal captures any independently observable close failure.
            }

            QueueFaulted();
        }

        private void QueueFaulted()
            => QueuePublication(PublicationKind.Faulted);

        private void CancelLifetime()
            => lifetimeCancellation.Cancel();

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

                QueueTrustChanged();
                QueueChanged();
            }
        }

        private void QueueChanged()
            => QueuePublication(PublicationKind.Changed);

        private void QueueTrustChanged()
            => QueuePublication(PublicationKind.TrustChanged);

        private void QueuePublication(PublicationKind kind)
        {
            TaskCompletionSource? workerCompletion = null;
            lock (publicationGate)
            {
                if (publicationClosed
                    || !pendingPublicationKinds.Add(kind))
                {
                    return;
                }

                pendingPublications.Enqueue(kind);
                if (!publicationWorkerRunning)
                {
                    publicationWorkerRunning = true;
                    workerCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    publicationDrainCompletion = workerCompletion;
                }
            }

            if (workerCompletion is not null)
            {
                try
                {
                    StartPublicationWorker(workerCompletion);
                }
                catch
                {
                    RollBackPublicationWorkerStart(workerCompletion);
                    throw;
                }
            }
        }

        private void StartPublicationWorker(TaskCompletionSource completion)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                QueueLongRunningPublicationWorker(completion);
                return;
            }

            using (ExecutionContext.SuppressFlow())
            {
                QueueLongRunningPublicationWorker(completion);
            }
        }

        private void QueueLongRunningPublicationWorker(
            TaskCompletionSource completion) =>
            // Publication must start even when unrelated blocking work has
            // exhausted the process ThreadPool. The gate still admits at most
            // one of these dedicated workers per session.
            _ = Task.Factory.StartNew(
                () => ProcessPublications(completion),
                CancellationToken.None,
                TaskCreationOptions.LongRunning
                    | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);

        private void RollBackPublicationWorkerStart(
            TaskCompletionSource completion)
        {
            lock (publicationGate)
            {
                if (ReferenceEquals(publicationDrainCompletion, completion))
                {
                    publicationWorkerRunning = false;
                    publicationDrainCompletion = null;
                    pendingPublications.Clear();
                    pendingPublicationKinds.Clear();
                }
            }

            completion.TrySetResult();
        }

        private void ProcessPublications(TaskCompletionSource workerCompletion)
        {
            Exception? workerFailure = null;
            try
            {
                while (true)
                {
                    PublicationKind publication;
                    PublicationLease lease;
                    lock (publicationGate)
                    {
                        if (publicationClosed)
                        {
                            pendingPublications.Clear();
                            pendingPublicationKinds.Clear();
                        }

                        if (pendingPublications.Count == 0)
                        {
                            if (ReferenceEquals(
                                    publicationDrainCompletion,
                                    workerCompletion))
                            {
                                publicationWorkerRunning = false;
                            }

                            return;
                        }

                        publication = pendingPublications.Dequeue();
                        pendingPublicationKinds.Remove(publication);
                        lease = new PublicationLease(this);
                        activePublication = lease;
                    }

                    PublicationLease? previous = CurrentPublication.Value;
                    CurrentPublication.Value = lease;
                    try
                    {
                        switch (publication)
                        {
                            case PublicationKind.Changed:
                                PublishChanged();
                                break;
                            case PublicationKind.TrustChanged:
                                PublishTrustChanged();
                                break;
                            case PublicationKind.Faulted:
                                PublishFaulted();
                                break;
                            default:
                                throw new InvalidOperationException(
                                    $"Unknown local pairing publication kind: {publication}.");
                        }
                    }
                    finally
                    {
                        lease.Deactivate();
                        CurrentPublication.Value = previous;
                        CompletePublication(lease);
                    }
                }
            }
            catch (Exception exception)
            {
                workerFailure = exception;
                Interlocked.CompareExchange(
                    ref publicationWorkerFailure,
                    exception,
                    null);
                Interlocked.CompareExchange(ref faulted, 1, 0);
                _ = DisposeAsync().AsTask();
            }
            finally
            {
                lock (publicationGate)
                {
                    if (ReferenceEquals(
                            publicationDrainCompletion,
                            workerCompletion))
                    {
                        publicationWorkerRunning = false;
                    }
                }

                if (workerFailure is null)
                {
                    workerCompletion.TrySetResult();
                }
                else
                {
                    workerCompletion.TrySetException(workerFailure);
                }
            }
        }

        private void CompletePublication(PublicationLease lease)
        {
            lock (publicationGate)
            {
                if (ReferenceEquals(activePublication, lease))
                {
                    activePublication = null;
                }
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
                catch (Exception exception) when (
                    exception is not OutOfMemoryException)
                {
                    // Presentation callbacks cannot own network lifetime.
                }

                if (IsPublicationClosed())
                {
                    break;
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
                catch (Exception exception) when (
                    exception is not OutOfMemoryException)
                {
                    // Lifecycle observers cannot own network cleanup.
                }

                if (IsPublicationClosed())
                {
                    break;
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
                catch (Exception exception) when (
                    exception is not OutOfMemoryException)
                {
                    // Presentation callbacks cannot own network lifetime.
                }

                if (IsPublicationClosed())
                {
                    break;
                }
            }
        }

        private bool IsPublicationClosed()
        {
            lock (publicationGate)
            {
                return publicationClosed;
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

        private enum PublicationKind
        {
            Changed,
            TrustChanged,
            Faulted,
        }

        private sealed class PublicationLease(
            SystemDesktopLocalPairingNetworkSession owner)
        {
            private int active = 1;

            public bool Active => Volatile.Read(ref active) != 0;

            public SystemDesktopLocalPairingNetworkSession Owner { get; } = owner;

            public void Deactivate() => Volatile.Write(ref active, 0);
        }

    }
}
