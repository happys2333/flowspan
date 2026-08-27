using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedRemoteWindowMediaSessionsTests
{
    private static readonly DeviceId InitiatorDeviceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ResponderDeviceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DeviceId OtherDeviceId =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task BoundSessionsExchangeFramesThroughTheirPublicInterface()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions();
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var initiator = new AuthenticatedRemoteWindowMediaSession(
            InitiatorDeviceId,
            ResponderDeviceId,
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            routes,
            initiatorFrames);
        await using var responder = new AuthenticatedRemoteWindowMediaSession(
            ResponderDeviceId,
            InitiatorDeviceId,
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            routes,
            responderFrames);
        RemoteWindowMediaRouteBinding binding = responder.PrepareResponderRoute(
            SessionId,
            ActivityId);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<TcpClient> acceptingClient = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using TcpClient server = await acceptingClient;
        Task<RemoteWindowMediaAttachment> acceptingAttachment = routes
            .AcceptAsync(server.GetStream())
            .AsTask();

        await initiator.ConnectInitiatorAsync(
            client.GetStream(),
            SessionId,
            ActivityId);
        RemoteWindowMediaAttachment responderAttachment =
            await acceptingAttachment;
        Assert.True(responder.TryAcceptResponderAttachment(responderAttachment));

        using RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x10, 0x20, 0x30]);
        Task<RemoteWindowMediaFrame> receiving = responder.ReceiveAsync().AsTask();

        await initiator.SendAsync(expected);
        using RemoteWindowMediaFrame actual = await receiving;

        Assert.Equal(binding, initiator.Binding);
        Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
    }

    [Fact]
    public async Task RouteAdmissionFailureCancelsOutsideTheSessionLock()
    {
        (SecureFrameSession registeredInitiator, SecureFrameSession registeredResponder) =
            CreateSecureSessions(seed: 0x74);
        (SecureFrameSession duplicateInitiator, SecureFrameSession duplicateResponder) =
            CreateSecureSessions(seed: 0x74);
        using (registeredInitiator)
        using (duplicateInitiator)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            routes.RegisterOwnedRoute(
                CreateBinding(registeredResponder),
                registeredResponder))
        await using (var session = new AuthenticatedRemoteWindowMediaSession(
            ResponderDeviceId,
            InitiatorDeviceId,
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            routes,
            duplicateResponder))
        {
            var callbackResult = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration cancellation =
                session.ControlStopToken.Register(() =>
                {
                    Task reentering = Task.Factory.StartNew(
                        () => session.Binding,
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    callbackResult.TrySetResult(
                        reentering.Wait(TimeSpan.FromSeconds(2)));
                });

            Assert.Throws<InvalidOperationException>(() =>
                session.PrepareResponderRoute(SessionId, ActivityId));

            Assert.True(await callbackResult.Task.WaitAsync(
                TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public async Task CallerCancellationAndConcurrentDisposalDoNotWaitForHandshakeIo()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x75);
        using (responderFrames)
        using (var cancellation = new CancellationTokenSource())
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                initiatorFrames);
            using var stream = new NonCooperativeHandshakeStream(responderFrames);
            Task connecting = session.ConnectInitiatorAsync(
                    stream,
                    SessionId,
                    ActivityId,
                    cancellation.Token)
                .AsTask();
            await stream.AcknowledgementReadStarted.WaitAsync(
                TimeSpan.FromSeconds(2));
            Task controlStopped = WaitForCancellationAsync(
                session.ControlStopToken);

            cancellation.Cancel();
            Task firstDisposal = session.DisposeAsync().AsTask();
            Task secondDisposal = session.DisposeAsync().AsTask();
            await stream.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(firstDisposal, secondDisposal);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await connecting.WaitAsync(TimeSpan.FromSeconds(2)));
            await firstDisposal.WaitAsync(TimeSpan.FromSeconds(2));
            await controlStopped.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(session.IsCurrent);
            Assert.Equal(0, routes.Count);
            Assert.True(stream.PendingAcknowledgementBufferIsStable);

            stream.CompleteWrongAcknowledgement();

            Assert.True(stream.PendingAcknowledgementBufferWasStableUntilCompletion);
            await stream.WaitForPendingAcknowledgementBufferClearAsync();
        }
    }

    [Fact]
    public async Task ConcurrentDisposalPropagatesCleanupFailureWithoutWaitingForHandshakeIo()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x76);
        using (responderFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                initiatorFrames);
            using var stream = new NonCooperativeHandshakeStream(
                responderFrames,
                throwOnFirstDispose: true);
            Task connecting = session.ConnectInitiatorAsync(
                    stream,
                    SessionId,
                    ActivityId)
                .AsTask();
            await stream.AcknowledgementReadStarted.WaitAsync(
                TimeSpan.FromSeconds(2));

            Task firstDisposal = session.DisposeAsync().AsTask();
            Task secondDisposal = session.DisposeAsync().AsTask();
            await stream.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(firstDisposal, secondDisposal);

            AggregateException connectionFailure =
                await Assert.ThrowsAsync<AggregateException>(async () =>
                    await connecting.WaitAsync(TimeSpan.FromSeconds(2)));
            InvalidOperationException firstDisposalFailure =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await firstDisposal.WaitAsync(TimeSpan.FromSeconds(2)));
            InvalidOperationException secondDisposalFailure =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await secondDisposal.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains(
                connectionFailure.Flatten().InnerExceptions,
                static failure => failure is OperationCanceledException);
            Assert.Contains(
                connectionFailure.Flatten().InnerExceptions,
                failure => ReferenceEquals(failure, stream.CleanupFailure));
            Assert.Same(stream.CleanupFailure, firstDisposalFailure);
            Assert.Same(firstDisposalFailure, secondDisposalFailure);
            Assert.False(session.IsCurrent);
            Assert.True(session.ControlStopToken.IsCancellationRequested);
            Assert.Equal(0, routes.Count);
            Assert.True(stream.PendingAcknowledgementBufferIsStable);

            stream.CompleteWrongAcknowledgement();

            Assert.True(stream.PendingAcknowledgementBufferWasStableUntilCompletion);
            await stream.WaitForPendingAcknowledgementBufferClearAsync();
        }
    }

    [Fact]
    public async Task ExpiredResponderRouteStopsItsOwningControlSession()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x79);
        using (initiatorFrames)
        {
            var time = new AdvancingTimeProvider();
            await using var routes = new RemoteWindowMediaRouteRegistry(
                timeProvider: time);
            var session = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                responderFrames);
            session.PrepareResponderRoute(
                SessionId,
                ActivityId,
                TimeSpan.FromSeconds(1));
            Task waitingForAttachment = session.WaitForAttachmentAsync().AsTask();
            Task controlStopped = WaitForCancellationAsync(session.ControlStopToken);

            time.Advance(TimeSpan.FromSeconds(1));

            await controlStopped.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<IOException>(() => waitingForAttachment);
            Assert.False(session.IsCurrent);
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task InvalidProtectedResponderRequestStopsItsOwningControlSession()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x7a);
        using (initiatorFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                responderFrames);
            RemoteWindowMediaRouteBinding binding = session.PrepareResponderRoute(
                SessionId,
                ActivityId);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                binding,
                Enumerable.Repeat((byte)0x7b, 32).ToArray(),
                initiatorFrames);
            request[^1] ^= 0x01;
            Task waitingForAttachment = session.WaitForAttachmentAsync().AsTask();
            Task controlStopped = WaitForCancellationAsync(session.ControlStopToken);

            try
            {
                await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
                    await routes.AcceptAsync(
                        new MemoryStream(),
                        request,
                        TimeSpan.FromSeconds(1)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(request);
            }

            await controlStopped.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<IOException>(() => waitingForAttachment);
            Assert.False(session.IsCurrent);
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResponderRouteCleanupFailureReachesSessionDisposal()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x7d);
        using (initiatorFrames)
        {
            var time = new AdvancingTimeProvider(throwOnTimerDispose: true);
            var routes = new RemoteWindowMediaRouteRegistry(
                timeProvider: time);
            var session = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                responderFrames);
            session.PrepareResponderRoute(
                SessionId,
                ActivityId,
                TimeSpan.FromSeconds(1));
            Task controlStopped = WaitForCancellationAsync(session.ControlStopToken);

            time.Advance(TimeSpan.FromSeconds(1));

            await controlStopped.WaitAsync(TimeSpan.FromSeconds(2));
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await session.DisposeAsync());
            IReadOnlyCollection<Exception> causes = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [failure];
            Assert.Contains(
                causes,
                static cause => cause.Message == "timer cleanup failed");
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await routes.DisposeAsync());
        }
    }

    [Fact]
    public async Task RegistrationKeepsControlStopTokenAfterSessionCleanup()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions(seed: 0x7e);
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var directory =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes))
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                ownedFrames);
            await using var registration =
                new AuthenticatedRemoteWindowMediaSessionRegistration(
                    directory,
                    session);
            CancellationToken expected = registration.ControlStopToken;

            await session.DisposeAsync();

            Assert.Equal(expected, registration.ControlStopToken);
            Assert.True(registration.ControlStopToken.IsCancellationRequested);
        }
    }

    [Fact]
    public async Task ControlSessionRemainsRegisteredUntilMediaRouteIsRevoked()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var controlListener = new TcpListener(IPAddress.Loopback, 0);
        controlListener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(controlListener.LocalEndpoint);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion;
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                controlListener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();
        var initiatorConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]);
        await using AuthenticatedTcpControlConnection responderConnection =
            await accepting;
        using SecureFrameSession initiatorFrames =
            initiatorConnection.TakeRemoteWindowMediaFrames();
        var blockingTime = new BlockingTimerTimeProvider();
        await using var routes = new RemoteWindowMediaRouteRegistry(
            timeProvider: blockingTime);
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        var sessionRegistered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveRegistration()
        {
            if (mediaSessions.TryGet(InitiatorDeviceId, out _))
            {
                sessionRegistered.TrySetResult();
            }
        }

        mediaSessions.Changed += ObserveRegistration;
        Task running = handler.RunAsync(responderConnection).AsTask();
        ObserveRegistration();
        await sessionRegistered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        mediaSessions.Changed -= ObserveRegistration;
        Assert.True(mediaSessions.TryGet(
            InitiatorDeviceId,
            out AuthenticatedRemoteWindowMediaSession? mediaSession));
        Assert.NotNull(mediaSession);
        mediaSession.PrepareResponderRoute(SessionId, ActivityId);

        await initiatorConnection.DisposeAsync();
        await blockingTime.TimerDisposeStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3));

        bool remainedRegistered = handler.TryGetChannel(
            InitiatorDeviceId,
            out _);
        blockingTime.AllowTimerDispose.TrySetResult();
        await Assert.ThrowsAnyAsync<IOException>(() =>
            running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(remainedRegistered);
        Assert.False(handler.TryGetChannel(InitiatorDeviceId, out _));
        Assert.False(mediaSessions.TryGet(InitiatorDeviceId, out _));
    }

    [Fact]
    public async Task SessionConstructionFailureReleasesOwnedMediaRegistration()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion;
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection initiatorConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]);
        await using AuthenticatedTcpControlConnection responderConnection =
            await accepting;
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(OtherDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.RunAsync(responderConnection).AsTask());

        Assert.False(mediaSessions.TryGet(InitiatorDeviceId, out _));
    }

    [Fact]
    public async Task ConcurrentDisposersWaitForTheSameControlStopCleanup()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions(seed: 0x77);
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                ownedFrames);
            var stopCallbackStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowStopCallback = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration stopCallback =
                session.ControlStopToken.Register(() =>
                {
                    stopCallbackStarted.TrySetResult();
                    allowStopCallback.Task.GetAwaiter().GetResult();
                });
            Task firstDisposal = Task.Run(async () =>
                await session.DisposeAsync());
            await stopCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task secondDisposal = session.DisposeAsync().AsTask();

            Assert.False(secondDisposal.IsCompleted);
            allowStopCallback.TrySetResult();
            await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(
                TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task DisposalSignalsControlStopAfterRouteRevocation()
    {
        (SecureFrameSession counterpartFrames, SecureFrameSession ownedFrames) =
            CreateSecureSessions(seed: 0x78);
        using (counterpartFrames)
        {
            var blockingTime = new BlockingTimerTimeProvider();
            await using var routes = new RemoteWindowMediaRouteRegistry(
                timeProvider: blockingTime);
            var session = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                ownedFrames);
            session.PrepareResponderRoute(SessionId, ActivityId);
            var controlStopObserved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration controlStop =
                session.ControlStopToken.Register(
                    () => controlStopObserved.TrySetResult());

            Task disposing = session.DisposeAsync().AsTask();
            await blockingTime.TimerDisposeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2));
            bool controlStoppedBeforeRevocation = controlStopObserved.Task.IsCompleted;
            blockingTime.AllowTimerDispose.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.False(controlStoppedBeforeRevocation);
            Assert.True(controlStopObserved.Task.IsCompletedSuccessfully);
        }
    }

    private static RemoteWindowMediaRouteBinding CreateBinding(
        SecureFrameSession mediaSession) =>
        RemoteWindowMediaRouteBinding.Create(
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            InitiatorDeviceId,
            ResponderDeviceId,
            RemoteWindowMediaRouteId.FromSession(mediaSession),
            SessionId,
            ActivityId);

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSecureSessions(int seed = 0x73)
    {
        byte[] secret = SHA256.HashData(BitConverter.GetBytes(seed));
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes($"authenticated-media-session-{seed:x8}"));
        using SecureSessionKeyMaterial material =
            SecureSessionKeyMaterial.DeriveRemoteWindowMedia(
                secret,
                transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    private static async Task WaitForCancellationAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No Activity was expected."));
    }

    private sealed class BlockingTimerTimeProvider : TimeProvider
    {
        public TaskCompletionSource AllowTimerDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TimerDisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) => new BlockingTimer(this);

        private sealed class BlockingTimer(BlockingTimerTimeProvider owner) :
            ITimer
        {
            private int disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                Volatile.Read(ref disposed) == 0;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                owner.TimerDisposeStarted.TrySetResult();
                owner.AllowTimerDispose.Task.GetAwaiter().GetResult();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class AdvancingTimeProvider(
        bool throwOnTimerDispose = false) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly bool throwOnTimerDispose = throwOnTimerDispose;
        private ManualTimer? timer;
        private DateTimeOffset utcNow = DateTimeOffset.UnixEpoch;

        public void Advance(TimeSpan elapsed)
        {
            ManualTimer? current;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                current = timer;
            }

            current?.FireIfDue(now);
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var created = new ManualTimer(this, callback, state);
            lock (gate)
            {
                timer = created;
            }

            created.Change(dueTime, period);
            return created;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            AdvancingTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private bool disposed;
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _ = period;
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    if (ReferenceEquals(owner.timer, this))
                    {
                        owner.timer = null;
                    }
                }

                if (owner.throwOnTimerDispose)
                {
                    throw new InvalidOperationException("timer cleanup failed");
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || now < dueAt)
                    {
                        return;
                    }

                    dueAt = DateTimeOffset.MaxValue;
                }

                callback(state);
            }
        }
    }

    private sealed class NonCooperativeHandshakeStream : Stream
    {
        private readonly SecureFrameSession responderFrames;
        private readonly TaskCompletionSource<int> acknowledgementRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource acknowledgementReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool throwOnFirstDispose;
        private byte[]? acknowledgement;
        private Memory<byte> pendingAcknowledgementBuffer;
        private int disposeCalls;
        private int reads;
        private int writes;

        public NonCooperativeHandshakeStream(
            SecureFrameSession responderFrames,
            bool throwOnFirstDispose = false)
        {
            this.responderFrames = responderFrames;
            this.throwOnFirstDispose = throwOnFirstDispose;
        }

        public Task AcknowledgementReadStarted => acknowledgementReadStarted.Task;

        public Exception CleanupFailure { get; } =
            new InvalidOperationException("injected stream cleanup failure");

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public Task DisposeStarted => disposeStarted.Task;

        public override long Length => throw new NotSupportedException();

        public bool PendingAcknowledgementBufferIsStable =>
            !pendingAcknowledgementBuffer.IsEmpty
            && pendingAcknowledgementBuffer.Span.IndexOfAnyExcept((byte)0x6b) < 0;

        public bool PendingAcknowledgementBufferIsCleared =>
            !pendingAcknowledgementBuffer.IsEmpty
            && pendingAcknowledgementBuffer.Span.IndexOfAnyExcept((byte)0) < 0;

        public bool PendingAcknowledgementBufferWasStableUntilCompletion { get; private set; }

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CompleteWrongAcknowledgement()
        {
            byte[] current = acknowledgement
                ?? throw new InvalidOperationException(
                    "The acknowledgement was not prepared.");
            PendingAcknowledgementBufferWasStableUntilCompletion =
                PendingAcknowledgementBufferIsStable;
            current.CopyTo(pendingAcknowledgementBuffer);
            acknowledgement = null;
            CryptographicOperations.ZeroMemory(current);
            acknowledgementRead.TrySetResult(
                RemoteWindowMediaAttachmentCodec.AcknowledgementEnvelopeBytes);
        }

        public async Task WaitForPendingAcknowledgementBufferClearAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!PendingAcknowledgementBufferIsCleared)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
            }
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (Interlocked.Increment(ref reads) == 1)
            {
                BinaryPrimitives.WriteInt32BigEndian(
                    buffer.Span,
                    RemoteWindowMediaAttachmentCodec.AcknowledgementEnvelopeBytes);
                return new ValueTask<int>(buffer.Length);
            }

            buffer.Span.Fill(0x6b);
            pendingAcknowledgementBuffer = buffer;
            acknowledgementReadStarted.TrySetResult();
            return new ValueTask<int>(acknowledgementRead.Task);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (Interlocked.Increment(ref writes) == 1)
            {
                return ValueTask.CompletedTask;
            }

            byte[] requestEnvelope = buffer.ToArray();
            byte[]? initiatorNonce = null;
            byte[] responderNonce = Enumerable.Repeat((byte)0x7c, 32).ToArray();
            try
            {
                RemoteWindowMediaAttachmentRequest request =
                    RemoteWindowMediaAttachmentCodec.DecodeRequest(
                        requestEnvelope,
                        responderFrames);
                initiatorNonce = request.ExportInitiatorNonce();
                RemoteWindowMediaRouteBinding wrongBinding =
                    RemoteWindowMediaRouteBinding.Create(
                        request.Binding.ProtocolVersion,
                        request.Binding.InitiatorDeviceId,
                        request.Binding.ResponderDeviceId,
                        request.Binding.RouteId,
                        request.Binding.SessionId,
                        ActivityId.Parse(
                            "cccccccc-cccc-cccc-cccc-cccccccccccc"));
                acknowledgement =
                    RemoteWindowMediaAttachmentCodec.EncodeAcknowledgement(
                        wrongBinding,
                        initiatorNonce,
                        responderNonce,
                        responderFrames);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(requestEnvelope);
                CryptographicOperations.ZeroMemory(responderNonce);
                if (initiatorNonce is not null)
                {
                    CryptographicOperations.ZeroMemory(initiatorNonce);
                }
            }

            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                disposeStarted.TrySetResult();
                if (Interlocked.Increment(ref disposeCalls) == 1
                    && throwOnFirstDispose)
                {
                    throw CleanupFailure;
                }
            }

            base.Dispose(disposing);
        }
    }
}
