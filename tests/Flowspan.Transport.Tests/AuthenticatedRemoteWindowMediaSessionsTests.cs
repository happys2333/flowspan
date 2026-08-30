using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
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
    public async Task ControlStopRejectsPreviouslyAttachedMediaBeforeWireSend()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x84);
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
        _ = responder.PrepareResponderRoute(SessionId, ActivityId);
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
        using RemoteWindowMediaFrame frame = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x10]);

        initiator.RequestControlStop();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => initiator.SendAsync(frame).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAttachmentMediaOperationRequestsLiveControlStop(
        bool send)
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions(seed: send ? (byte)0x89 : (byte)0x88);
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var session = new AuthenticatedRemoteWindowMediaSession(
            InitiatorDeviceId,
            ResponderDeviceId,
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            routes,
            ownedFrames))
        {
            int callbackCount = 0;
            using CancellationTokenRegistration liveRegistration =
                session.ControlStopToken.Register(
                    () => Interlocked.Increment(ref callbackCount));

            if (send)
            {
                using RemoteWindowMediaFrame frame = RemoteWindowMediaFrame.Create(
                    SessionId,
                    ActivityId,
                    RemoteWindowMediaKind.Video,
                    sequence: 1,
                    chunkIndex: 0,
                    chunkCount: 1,
                    [0x10]);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => session.SendAsync(frame).AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => session.ReceiveAsync().AsTask());
            }

            Assert.Equal(1, callbackCount);
            Assert.True(session.ControlStopToken.IsCancellationRequested);
            Assert.False(session.IsCurrent);
        }
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
    public async Task MediaDisposalInvalidatesConnectionPreparationBeforeControlStop()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x7f);
        using (initiatorFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var timeline = new List<string>();
            var mediaSession = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                routes,
                responderFrames);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(InitiatorDeviceId),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(
                () => timeline.Add("preparation.invalidate"));
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);
            bool callbackRanOutsideMediaGate = false;
            using CancellationTokenRegistration controlStop =
                mediaSession.ControlStopToken.Register(
                    () =>
                    {
                        Task reentering = Task.Factory.StartNew(
                            () => mediaSession.Binding,
                            CancellationToken.None,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default);
                        callbackRanOutsideMediaGate = reentering.Wait(
                            TimeSpan.FromSeconds(2));
                        timeline.Add("media.control_stop");
                    });

            await mediaSession.DisposeAsync();

            Assert.Equal(
                ["preparation.invalidate", "media.control_stop"],
                timeline);
            Assert.True(callbackRanOutsideMediaGate);
            Assert.False(Assert.IsAssignableFrom<
                IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                result.Registration).IsCurrent);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task MediaMutationAfterPreparationPromotionTriggersLiveCallbackBeforeCapture()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x83);
        using (initiatorFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var timeline = new List<string>();
            var mediaSession = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                routes,
                responderFrames);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(InitiatorDeviceId),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(
                    new RecordingConnectionPreparationSink(
                        () => timeline.Add("preparation.invalidate")));
            IAuthenticatedRemoteWindowConnectionPreparationRegistration
                preparationRegistration = Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                        result.Registration);
            bool callbackRanOutsideMediaGate = false;
            using CancellationTokenRegistration liveRegistration =
                mediaSession.ControlStopToken.UnsafeRegister(
                    _ =>
                    {
                        Task reentering = Task.Factory.StartNew(
                            () => mediaSession.Binding,
                            CancellationToken.None,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default);
                        callbackRanOutsideMediaGate = reentering.Wait(
                            TimeSpan.FromSeconds(2));
                        timeline.Add("media.invalidate");
                    },
                    state: null);
            preparationRegistration.Dispose();
            timeline.Add("promoted");

            mediaSession.RequestControlStop();
            timeline.Add("capture");

            Assert.Equal(
                ["promoted", "media.invalidate", "capture"],
                timeline);
            Assert.True(callbackRanOutsideMediaGate);
            Assert.Null(generation.RevokeAndReleaseOwner());
            await mediaSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task ControlStopRequestInvalidatesConnectionPreparationSynchronously()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x82);
        using (initiatorFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var timeline = new List<string>();
            var mediaSession = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                routes,
                responderFrames);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(InitiatorDeviceId),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(
                () => timeline.Add("preparation.invalidate"));
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);
            using CancellationTokenRegistration controlStop =
                mediaSession.ControlStopToken.Register(
                    () => timeline.Add("media.control_stop"));

            mediaSession.RequestControlStop();

            Assert.Equal(
                ["preparation.invalidate", "media.control_stop"],
                timeline);
            Assert.False(mediaSession.IsCurrent);
            Assert.False(Assert.IsAssignableFrom<
                IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                result.Registration).IsCurrent);
            Assert.Null(generation.RevokeAndReleaseOwner());
            await mediaSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResponderRouteInvalidationInvalidatesPreparationBeforeControlStop()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x80);
        using (initiatorFrames)
        {
            var timeline = new List<string>();
            var time = new AdvancingTimeProvider();
            await using var routes = new RemoteWindowMediaRouteRegistry(
                timeProvider: time);
            var mediaSession = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                routes,
                responderFrames);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(InitiatorDeviceId),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(
                () => timeline.Add("preparation.invalidate"));
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);
            var controlStopObserved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration controlStop =
                mediaSession.ControlStopToken.Register(
                    () =>
                    {
                        timeline.Add("media.control_stop");
                        controlStopObserved.TrySetResult();
                    });
            mediaSession.PrepareResponderRoute(
                SessionId,
                ActivityId,
                TimeSpan.FromSeconds(1));

            time.Advance(TimeSpan.FromSeconds(1));
            await controlStopObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(
                ["preparation.invalidate", "media.control_stop"],
                timeline);
            Assert.False(Assert.IsAssignableFrom<
                IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                result.Registration).IsCurrent);
            Assert.Null(generation.RevokeAndReleaseOwner());
            await mediaSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task FatalMediaPreparationInvalidationEscapesRawAfterRouteCleanup()
    {
        (SecureFrameSession initiatorFrames, SecureFrameSession responderFrames) =
            CreateSecureSessions(seed: 0x81);
        using (initiatorFrames)
        {
            var time = new AdvancingTimeProvider(throwOnTimerDispose: true);
            var routes = new RemoteWindowMediaRouteRegistry(timeProvider: time);
            var mediaSession = new AuthenticatedRemoteWindowMediaSession(
                ResponderDeviceId,
                InitiatorDeviceId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                routes,
                responderFrames);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(InitiatorDeviceId),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
            var fatal = new OutOfMemoryException(
                "FLOWSPAN_MEDIA_PREPARATION_FATAL_CANARY");
#pragma warning restore CA2201
            var sink = new RecordingConnectionPreparationSink(() => throw fatal);
            _ = lease.TryReservePreparation(sink);
            mediaSession.PrepareResponderRoute(
                SessionId,
                ActivityId,
                TimeSpan.FromSeconds(1));

            time.Advance(TimeSpan.FromSeconds(1));
            await WaitForCancellationAsync(mediaSession.ControlStopToken)
                .WaitAsync(TimeSpan.FromSeconds(2));
            OutOfMemoryException failure = await Assert.ThrowsAsync<
                OutOfMemoryException>(() => mediaSession.DisposeAsync().AsTask());

            Assert.Same(fatal, failure);
            Assert.False(mediaSession.IsCurrent);
            Assert.True(mediaSession.ControlStopToken.IsCancellationRequested);
            Assert.Null(generation.RevokeAndReleaseOwner());
            await Assert.ThrowsAnyAsync<Exception>(() => routes.DisposeAsync().AsTask());
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
    public async Task ConnectionLeaseAtomicallyBindsPreparationAndMediaGeneration()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);

            RemoteWindowMediaRouteBinding binding =
                lease.PrepareResponderRoute(SessionId, ActivityId);

            Assert.Equal(1, lease.Generation);
            Assert.Equal(ResponderDeviceId, lease.LocalDeviceId);
            Assert.Equal(InitiatorDeviceId, lease.PeerDeviceId);
            Assert.Equal(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                lease.ProtocolVersion);
            Assert.True(lease.IsCurrent);
            Assert.True(mediaSessions.TryGet(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowMediaSession? mediaSession));
            Assert.Equal(binding, Assert.IsType<
                AuthenticatedRemoteWindowMediaSession>(mediaSession).Binding);

            await initiatorConnection.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.True(lease.IsRevoked);
            Assert.False(lease.IsCurrent);
            Assert.False(handler.TryAcquireRemoteWindowConnection(
                InitiatorDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() =>
                lease.PrepareResponderRoute(SessionId, ActivityId));
        }
    }

    [Fact]
    public async Task VerifiedPeerBindingIsPinnedToTheAuthenticatedGeneration()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                version);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
                initiatorIdentity,
                responderConnection.RemoteEndPoint.Address,
                listenerPort: 4747,
                version);
            var validator = new RecordingCandidateValidator(isCurrent: true);
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    responderConnection,
                    candidate,
                    validator)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease? acquired;
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(3));
            while (!handler.TryAcquireRemoteWindowPeerConnection(
                       InitiatorDeviceId,
                       out acquired))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
            }

            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            VerifiedPeerConnectionCandidate pinned = Assert.IsType<
                VerifiedPeerConnectionCandidate>(lease.PeerConnectionCandidate);
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, 4747), pinned.EndPoint);
            Assert.True(validator.ValidationCount > 0);
            validator.SetCurrent(false);
            Assert.False(handler.TryAcquireRemoteWindowPeerConnection(
                InitiatorDeviceId,
                out _));

            await initiatorConnection.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task VerifiedConnectionLeaseConnectsPinnedPeerAndExchangesMedia()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var participantRoutes = new RemoteWindowMediaRouteRegistry();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory(participantRoutes);
        await using var participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: participantMedia);
        await using var hostRoutes = new RemoteWindowMediaRouteRegistry();
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory(hostRoutes);
        await using var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: hostMedia);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        using var mediaListener = new TcpListener(IPAddress.Loopback, 0);
        mediaListener.Start(backlog: 1);
        var mediaEndpoint = Assert.IsType<IPEndPoint>(mediaListener.LocalEndpoint);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            mediaEndpoint.Address,
            mediaEndpoint.Port,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        await using (participantConnection)
        await using (hostConnection)
        {
            Task participantRunning = participantHandler
                .RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            Task hostRunning = hostHandler.RunAsync(hostConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease participantLease =
                await WaitForPeerConnectionLeaseAsync(
                    participantHandler,
                    ResponderDeviceId);
            await using AuthenticatedRemoteWindowConnectionLease hostLease =
                await WaitForConnectionLeaseAsync(
                    hostHandler,
                    InitiatorDeviceId);
            RemoteWindowMediaRouteBinding binding = hostLease.PrepareResponderRoute(
                SessionId,
                ActivityId);
            DateTimeOffset deadline = DateTimeOffset.UtcNow;
            deadline = deadline.AddTicks(
                -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
            RemoteWindowPreparationRequest request =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    ResponderDeviceId,
                    InitiatorDeviceId,
                    MirrorParticipantRole.ViewOnly,
                    deadline.AddMinutes(1));
            Task accepting = AcceptOwnedMediaAsync();

            await participantLease.ConnectInitiatorAsync(request);
            await hostLease.WaitForMediaAttachmentAsync();
            using RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Video,
                sequence: 1,
                chunkIndex: 0,
                chunkCount: 1,
                [0x31, 0x32, 0x33]);
            Task<RemoteWindowMediaFrame> receiving =
                participantLease.ReceiveMediaAsync().AsTask();

            await hostLease.SendMediaAsync(expected);
            using RemoteWindowMediaFrame actual = await receiving;

            Assert.Equal(binding, participantMedia.TryGet(
                ResponderDeviceId,
                out AuthenticatedRemoteWindowMediaSession? participantSession)
                    ? participantSession!.Binding
                    : null);
            Assert.Equal(expected.ExportPayload(), actual.ExportPayload());

            await participantConnection.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                hostRunning.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<IOException>(() =>
                participantRunning.WaitAsync(TimeSpan.FromSeconds(5)));
            await accepting.WaitAsync(TimeSpan.FromSeconds(5));

            async Task AcceptOwnedMediaAsync()
            {
                using TcpClient accepted = await mediaListener.AcceptTcpClientAsync();
                RemoteWindowMediaAttachment attachment =
                    await hostRoutes.AcceptAsync(accepted.GetStream());
                await FlowspanTcpInboundListener.RunOwnedMediaAttachmentHandlerAsync(
                    attachment,
                    hostMedia,
                    CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task LateAcceptedAttachmentCannotRetargetReplacementAuthenticatedGeneration()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        var participantRoutes = new RemoteWindowMediaRouteRegistry();
        var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory(participantRoutes);
        var participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: participantMedia);
        var hostRoutes = new RemoteWindowMediaRouteRegistry();
        var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory(hostRoutes);
        var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: hostMedia);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        using var mediaListener = new TcpListener(IPAddress.Loopback, 0);
        mediaListener.Start(backlog: 2);
        var mediaEndpoint = Assert.IsType<IPEndPoint>(mediaListener.LocalEndpoint);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            mediaEndpoint.Address,
            mediaEndpoint.Port,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var oldGate = new BlockingForwardingMediaHandler(hostMedia);
        var replacementGate = new BlockingForwardingMediaHandler(hostMedia);
        AuthenticatedTcpControlConnection? oldParticipantConnection = null;
        AuthenticatedTcpControlConnection? oldHostConnection = null;
        AuthenticatedTcpControlConnection? replacementParticipantConnection = null;
        AuthenticatedTcpControlConnection? replacementHostConnection = null;
        AuthenticatedRemoteWindowConnectionLease? oldParticipantLease = null;
        AuthenticatedRemoteWindowConnectionLease? oldHostLease = null;
        AuthenticatedRemoteWindowConnectionLease? replacementParticipantLease = null;
        AuthenticatedRemoteWindowConnectionLease? replacementHostLease = null;
        Task? oldParticipantRunning = null;
        Task? oldHostRunning = null;
        Task? replacementParticipantRunning = null;
        Task? replacementHostRunning = null;
        Task? oldAccepting = null;
        Task? replacementAccepting = null;
        Task? oldConnecting = null;
        Task? replacementConnecting = null;
        Task? replacementAttachmentWait = null;
        Task<RemoteWindowMediaFrame>? replacementReceiving = null;
        Exception? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            (oldParticipantConnection, oldHostConnection) =
                await CreateControlPairAsync(
                    participantIdentity,
                    hostIdentity,
                    version);
            oldParticipantRunning = participantHandler
                .RunWithRemoteWindowPeerAsync(
                    oldParticipantConnection,
                    candidate,
                    validator)
                .AsTask();
            oldHostRunning = hostHandler.RunAsync(oldHostConnection).AsTask();
            oldParticipantLease = await WaitForPeerConnectionLeaseAsync(
                participantHandler,
                ResponderDeviceId);
            oldHostLease = await WaitForConnectionLeaseAsync(
                hostHandler,
                InitiatorDeviceId);
            long retiredParticipantGeneration = oldParticipantLease.Generation;
            long retiredHostGeneration = oldHostLease.Generation;
            RemoteWindowMediaRouteBinding retiredBinding =
                oldHostLease.PrepareResponderRoute(SessionId, ActivityId);
            oldAccepting = AcceptOwnedMediaAsync(oldGate);
            oldConnecting = oldParticipantLease
                .ConnectInitiatorAsync(CreatePreparationRequest(), deadline.Token)
                .AsTask();

            await oldGate.Entered.Task.WaitAsync(deadline.Token);
            await oldConnecting.WaitAsync(deadline.Token);
            Assert.Equal(retiredBinding, oldGate.Binding);
            Assert.Equal(1, oldGate.CallCount);
            Assert.Equal(0, oldGate.ForwardCount);
            Assert.True(participantMedia.TryGet(
                ResponderDeviceId,
                out AuthenticatedRemoteWindowMediaSession? retiredParticipantSession));
            Assert.True(Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                retiredParticipantSession).IsAttached);
            Assert.True(hostMedia.TryGet(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowMediaSession? retiredHostSession));
            Assert.False(Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                retiredHostSession).IsAttached);

            await oldHostLease.FailCloseAsync().AsTask().WaitAsync(deadline.Token);
            await ObserveControlStopAsync(oldHostRunning);
            await ObserveControlStopAsync(oldParticipantRunning);
            await WaitForNoOwnersAsync();
            Assert.False(oldGate.IsReleased);
            Assert.False(oldAccepting.IsCompleted);
            Assert.False(oldHostLease.IsCurrent);
            Assert.False(oldParticipantLease.IsCurrent);

            (replacementParticipantConnection, replacementHostConnection) =
                await CreateControlPairAsync(
                    participantIdentity,
                    hostIdentity,
                    version);
            replacementParticipantRunning = participantHandler
                .RunWithRemoteWindowPeerAsync(
                    replacementParticipantConnection,
                    candidate,
                    validator)
                .AsTask();
            replacementHostRunning = hostHandler
                .RunAsync(replacementHostConnection)
                .AsTask();
            replacementParticipantLease = await WaitForPeerConnectionLeaseAsync(
                participantHandler,
                ResponderDeviceId);
            replacementHostLease = await WaitForConnectionLeaseAsync(
                hostHandler,
                InitiatorDeviceId);
            Assert.True(
                replacementParticipantLease.Generation
                > retiredParticipantGeneration);
            Assert.True(replacementHostLease.Generation > retiredHostGeneration);
            RemoteWindowMediaRouteBinding replacementBinding =
                replacementHostLease.PrepareResponderRoute(SessionId, ActivityId);
            Assert.NotEqual(retiredBinding.RouteId, replacementBinding.RouteId);
            Assert.NotEqual(retiredBinding, replacementBinding);
            Assert.Equal(
                retiredBinding.ProtocolVersion,
                replacementBinding.ProtocolVersion);
            Assert.Equal(
                retiredBinding.InitiatorDeviceId,
                replacementBinding.InitiatorDeviceId);
            Assert.Equal(
                retiredBinding.ResponderDeviceId,
                replacementBinding.ResponderDeviceId);
            Assert.Equal(retiredBinding.SessionId, replacementBinding.SessionId);
            Assert.Equal(retiredBinding.ActivityId, replacementBinding.ActivityId);
            replacementAccepting = AcceptOwnedMediaAsync(replacementGate);
            replacementConnecting = replacementParticipantLease
                .ConnectInitiatorAsync(CreatePreparationRequest(), deadline.Token)
                .AsTask();

            await replacementConnecting.WaitAsync(deadline.Token);
            await replacementGate.Entered.Task.WaitAsync(deadline.Token);
            Assert.Equal(replacementBinding, replacementGate.Binding);
            Assert.True(participantMedia.TryGet(
                ResponderDeviceId,
                out AuthenticatedRemoteWindowMediaSession?
                    replacementParticipantSession));
            AuthenticatedRemoteWindowMediaSession currentParticipantSession =
                Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                    replacementParticipantSession);
            Assert.True(currentParticipantSession.IsAttached);
            Assert.Equal(replacementBinding, currentParticipantSession.Binding);
            Assert.True(hostMedia.TryGet(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowMediaSession? replacementHostSession));
            AuthenticatedRemoteWindowMediaSession currentHostSession =
                Assert.IsType<AuthenticatedRemoteWindowMediaSession>(
                    replacementHostSession);
            Assert.True(currentHostSession.IsCurrent);
            Assert.False(currentHostSession.IsAttached);
            Assert.Equal(replacementBinding, currentHostSession.Binding);
            Assert.Equal(1, hostRoutes.Count);
            replacementAttachmentWait = replacementHostLease
                .WaitForMediaAttachmentAsync(deadline.Token)
                .AsTask();
            Assert.False(replacementAttachmentWait.IsCompleted);

            oldGate.Release();
            Exception retiredAttachmentFailure = Assert.IsType<
                InvalidDataException>(await Record.ExceptionAsync(
                    async () => await oldAccepting.WaitAsync(
                        TimeSpan.FromSeconds(5))));
            Assert.Contains(
                "no live owning control connection",
                retiredAttachmentFailure.Message,
                StringComparison.Ordinal);
            Assert.Equal(1, oldGate.ForwardCount);
            Assert.True(oldGate.Exited.Task.IsCompletedSuccessfully);
            Assert.Equal(0, replacementGate.ForwardCount);
            Assert.False(replacementAttachmentWait.IsCompleted);
            Assert.True(replacementHostLease.IsCurrent);
            Assert.True(replacementParticipantLease.IsCurrent);
            Assert.False(replacementHostRunning.IsCompleted);
            Assert.False(replacementParticipantRunning.IsCompleted);
            Assert.Equal(1, hostRoutes.Count);
            Assert.True(currentHostSession.IsCurrent);
            Assert.False(currentHostSession.IsAttached);
            Assert.False(currentHostSession.ControlStopToken.IsCancellationRequested);
            Assert.True(currentParticipantSession.IsAttached);
            Assert.False(
                currentParticipantSession.ControlStopToken.IsCancellationRequested);
            Assert.True(hostHandler.TryAcquireRemoteWindowConnection(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? hostProbe));
            await using (AuthenticatedRemoteWindowConnectionLease currentProbe =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(hostProbe))
            {
                Assert.Equal(replacementHostLease.Generation, currentProbe.Generation);
            }
            Assert.True(participantHandler.TryAcquireRemoteWindowPeerConnection(
                ResponderDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? participantProbe));
            await using (AuthenticatedRemoteWindowConnectionLease currentProbe =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                    participantProbe))
            {
                Assert.Equal(
                    replacementParticipantLease.Generation,
                    currentProbe.Generation);
            }

            replacementGate.Release();
            await replacementAttachmentWait.WaitAsync(deadline.Token);
            Assert.Equal(1, replacementGate.ForwardCount);
            Assert.False(replacementGate.Exited.Task.IsCompleted);
            Assert.True(currentHostSession.IsAttached);
            Assert.True(currentParticipantSession.IsAttached);
            Assert.Equal(replacementBinding, currentHostSession.Binding);
            Assert.Equal(replacementBinding, currentParticipantSession.Binding);
            using RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Video,
                sequence: 1,
                chunkIndex: 0,
                chunkCount: 1,
                [0x41, 0x42, 0x43]);
            replacementReceiving = replacementParticipantLease
                .ReceiveMediaAsync(deadline.Token)
                .AsTask();
            await replacementHostLease.SendMediaAsync(expected, deadline.Token);
            using RemoteWindowMediaFrame actual = await replacementReceiving.WaitAsync(
                deadline.Token);
            Assert.Equal(expected.ExportPayload(), actual.ExportPayload());

            await replacementHostLease.FailCloseAsync()
                .AsTask()
                .WaitAsync(deadline.Token);
            await ObserveControlStopAsync(replacementHostRunning);
            await ObserveControlStopAsync(replacementParticipantRunning);
            await replacementAccepting.WaitAsync(deadline.Token);
            await WaitForNoOwnersAsync();
            Assert.Equal(1, replacementGate.ForwardCount);
            Assert.True(replacementGate.Exited.Task.IsCompletedSuccessfully);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            oldGate.Release();
            replacementGate.Release();
            await CaptureCleanupAsync(async () =>
            {
                if (replacementHostLease is { IsCurrent: true })
                {
                    await replacementHostLease.FailCloseAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
            });
            await CaptureCleanupAsync(async () =>
            {
                if (oldHostLease is { IsCurrent: true })
                {
                    await oldHostLease.FailCloseAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
            });
            await DisposeLeaseAsync(replacementParticipantLease);
            await DisposeLeaseAsync(replacementHostLease);
            await DisposeLeaseAsync(oldParticipantLease);
            await DisposeLeaseAsync(oldHostLease);
            await DisposeConnectionAsync(replacementParticipantConnection);
            await DisposeConnectionAsync(replacementHostConnection);
            await DisposeConnectionAsync(oldParticipantConnection);
            await DisposeConnectionAsync(oldHostConnection);
            Exception? listenerStopFailure = Record.Exception(mediaListener.Stop);
            if (listenerStopFailure is not null)
            {
                cleanupFailures.Add(listenerStopFailure);
            }
            await ObserveCleanupTaskAsync(oldConnecting, allowInvalidData: false);
            await ObserveCleanupTaskAsync(
                replacementConnecting,
                allowInvalidData: false);
            await ObserveCleanupTaskAsync(
                replacementAttachmentWait,
                allowInvalidData: false);
            await ObserveCleanupTaskAsync(
                replacementReceiving,
                allowInvalidData: false);
            await ObserveCleanupTaskAsync(oldAccepting, allowInvalidData: true);
            await ObserveCleanupTaskAsync(
                replacementAccepting,
                allowInvalidData: false);
            await ObserveCleanupTaskAsync(
                oldParticipantRunning,
                allowInvalidData: true);
            await ObserveCleanupTaskAsync(oldHostRunning, allowInvalidData: true);
            await ObserveCleanupTaskAsync(
                replacementParticipantRunning,
                allowInvalidData: true);
            await ObserveCleanupTaskAsync(
                replacementHostRunning,
                allowInvalidData: true);
            await DisposeOwnerAsync(participantHandler);
            await DisposeOwnerAsync(hostHandler);
            await DisposeOwnerAsync(participantMedia);
            await DisposeOwnerAsync(hostMedia);
            await DisposeOwnerAsync(participantRoutes);
            await DisposeOwnerAsync(hostRoutes);
        }

        if (primaryFailure is not null && cleanupFailures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (primaryFailure is not null)
        {
            cleanupFailures.Insert(0, primaryFailure);
            throw new AggregateException(
                "Authenticated media ABA test and cleanup both failed.",
                cleanupFailures);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Authenticated media ABA test cleanup failed.",
                cleanupFailures);
        }

        RemoteWindowPreparationRequest CreatePreparationRequest()
        {
            DateTimeOffset requestDeadline = DateTimeOffset.UtcNow;
            requestDeadline = requestDeadline.AddTicks(
                -(requestDeadline.Ticks % TimeSpan.TicksPerMillisecond));
            return RemoteWindowPreparationRequest.Create(
                CorrelationId.From(Guid.NewGuid()),
                SessionId,
                ActivityId,
                ResponderDeviceId,
                InitiatorDeviceId,
                MirrorParticipantRole.ViewOnly,
                requestDeadline.AddMinutes(1));
        }

        async Task AcceptOwnedMediaAsync(BlockingForwardingMediaHandler gate)
        {
            using TcpClient accepted = await mediaListener.AcceptTcpClientAsync(
                deadline.Token);
            RemoteWindowMediaAttachment attachment =
                await hostRoutes.AcceptAsync(accepted.GetStream());
            await FlowspanTcpInboundListener.RunOwnedMediaAttachmentHandlerAsync(
                attachment,
                gate,
                CancellationToken.None);
        }

        async Task WaitForNoOwnersAsync()
        {
            while (participantMedia.TryGet(ResponderDeviceId, out _)
                || hostMedia.TryGet(InitiatorDeviceId, out _)
                || participantRoutes.Count != 0
                || hostRoutes.Count != 0
                || participantHandler.GetConnectedPeers().Count != 0
                || hostHandler.GetConnectedPeers().Count != 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), deadline.Token);
            }
        }

        static async Task ObserveControlStopAsync(Task running)
        {
            Exception? failure = await Record.ExceptionAsync(
                async () => await running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotNull(failure);
            Assert.True(
                failure is OperationCanceledException
                    or IOException
                    or InvalidDataException
                    or ObjectDisposedException,
                failure.ToString());
        }

        async Task CaptureCleanupAsync(Func<Task> cleanup)
        {
            Exception? failure = await Record.ExceptionAsync(cleanup);
            if (failure is not null)
            {
                cleanupFailures.Add(failure);
            }
        }

        async Task DisposeLeaseAsync(
            AuthenticatedRemoteWindowConnectionLease? lease)
        {
            if (lease is not null)
            {
                await CaptureCleanupAsync(async () =>
                    await lease.DisposeAsync().AsTask().WaitAsync(
                        TimeSpan.FromSeconds(5)));
            }
        }

        async Task DisposeConnectionAsync(
            AuthenticatedTcpControlConnection? connection)
        {
            if (connection is not null)
            {
                await CaptureCleanupAsync(async () =>
                    await connection.DisposeAsync().AsTask().WaitAsync(
                        TimeSpan.FromSeconds(5)));
            }
        }

        async Task DisposeOwnerAsync(IAsyncDisposable owner) =>
            await CaptureCleanupAsync(async () =>
                await owner.DisposeAsync().AsTask().WaitAsync(
                    TimeSpan.FromSeconds(5)));

        async Task ObserveCleanupTaskAsync(
            Task? running,
            bool allowInvalidData)
        {
            if (running is not null)
            {
                Exception? failure = await Record.ExceptionAsync(async () =>
                    await running.WaitAsync(TimeSpan.FromSeconds(5)));
                if (failure is not null
                    && !IsExpectedCleanupTaskFailure(failure, allowInvalidData))
                {
                    cleanupFailures.Add(failure);
                }
            }
        }

        static bool IsExpectedCleanupTaskFailure(
            Exception failure,
            bool allowInvalidData) => failure switch
            {
                AggregateException aggregate =>
                    aggregate.Flatten().InnerExceptions.Count > 0
                    && aggregate.Flatten().InnerExceptions.All(
                        inner => IsExpectedCleanupTaskFailure(
                            inner,
                            allowInvalidData)),
                InvalidDataException => allowInvalidData,
                OperationCanceledException or IOException or ObjectDisposedException =>
                    true,
                _ => false,
            };
    }

    [Fact]
    public async Task CandidateInvalidatedAfterPeerConnectFailsClosedBeforeAttachment()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            IPAddress.Loopback,
            listenerPort: 4747,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var connector = new InvalidatingPeerStreamConnector(validator);
        await using (participantConnection)
        await using (hostConnection)
        {
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForPeerConnectionLeaseAsync(
                    handler,
                    ResponderDeviceId);
            DateTimeOffset deadline = DateTimeOffset.UtcNow;
            deadline = deadline.AddTicks(
                -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
            RemoteWindowPreparationRequest request =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    ResponderDeviceId,
                    InitiatorDeviceId,
                    MirrorParticipantRole.ViewOnly,
                    deadline.AddMinutes(1));

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    lease.ConnectInitiatorAsync(
                            request,
                            connector,
                            CancellationToken.None)
                        .AsTask());

            Assert.Contains("no longer current", failure.Message);
            Assert.False(connector.Stream.CanRead);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(mediaSessions.TryGet(ResponderDeviceId, out _));
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task FailedPeerConnectRevokesGenerationBeforeReturnedStreamDisposalCompletes()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            IPAddress.Loopback,
            listenerPort: 4747,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var connector = new InvalidatingBlockingDisposePeerStreamConnector(
            validator);
        await using (participantConnection)
        await using (hostConnection)
        {
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForPeerConnectionLeaseAsync(
                    handler,
                    ResponderDeviceId);
            await using (lease)
            {
                DateTimeOffset deadline = DateTimeOffset.UtcNow;
                deadline = deadline.AddTicks(
                    -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
                RemoteWindowPreparationRequest request =
                    RemoteWindowPreparationRequest.Create(
                        CorrelationId.From(Guid.NewGuid()),
                        SessionId,
                        ActivityId,
                        ResponderDeviceId,
                        InitiatorDeviceId,
                        MirrorParticipantRole.ViewOnly,
                        deadline.AddMinutes(1));
                Task connecting = lease.ConnectInitiatorAsync(
                        request,
                        connector,
                        CancellationToken.None)
                    .AsTask();

                await connector.Stream.DisposeStarted.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));
                try
                {
                    Assert.True(lease.IsRevoked);
                    Assert.False(connecting.IsCompleted);
                    Assert.False(handler.TryAcquireRemoteWindowPeerConnection(
                        ResponderDeviceId,
                        out _));
                }
                finally
                {
                    connector.Stream.ReleaseDispose.TrySetResult();
                }

                InvalidOperationException failure =
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        connecting.WaitAsync(TimeSpan.FromSeconds(5)));

                Assert.Contains("no longer current", failure.Message);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    running.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.False(mediaSessions.TryGet(ResponderDeviceId, out _));
                Assert.Equal(0, routes.Count);
            }
        }
    }

    [Fact]
    public async Task FailedPreparationPeerConnectPoisonsGenerationUntilExplicitFailClose()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            IPAddress.Loopback,
            listenerPort: 4747,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var connector = new InvalidatingBlockingDisposePeerStreamConnector(
            validator);
        await using (participantConnection)
        await using (hostConnection)
        {
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForPeerConnectionLeaseAsync(
                    handler,
                    ResponderDeviceId);
            int revocationCount = 0;
            using IDisposable registration =
                lease.RegisterRevocationCallback(
                    () => Interlocked.Increment(ref revocationCount));
            DateTimeOffset deadline = DateTimeOffset.UtcNow;
            deadline = deadline.AddTicks(
                -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
            RemoteWindowPreparationRequest request =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    ResponderDeviceId,
                    InitiatorDeviceId,
                    MirrorParticipantRole.ViewOnly,
                    deadline.Add(
                        RemoteWindowControlMessageCodec.MaximumCommandTimeToLive));
            Task connecting = lease.ConnectInitiatorForPreparationAsync(
                    request,
                    connector,
                    CancellationToken.None)
                .AsTask();
            await connector.Stream.DisposeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            try
            {
                Assert.False(connecting.IsCompleted);
                Assert.False(lease.IsCurrent);
                Assert.False(lease.IsRevoked);
                Assert.Equal(0, Volatile.Read(ref revocationCount));
                Assert.False(running.IsCompleted);
                Assert.True(handler.TryGetChannel(ResponderDeviceId, out _));
                Assert.True(mediaSessions.TryGet(ResponderDeviceId, out _));
                Assert.False(handler.TryAcquireRemoteWindowConnection(
                    ResponderDeviceId,
                    out _));
                Assert.False(handler.TryAcquireRemoteWindowPeerConnection(
                    ResponderDeviceId,
                    out _));
                Assert.Throws<InvalidOperationException>(() =>
                    lease.PrepareResponderRoute(SessionId, ActivityId));
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    lease.WaitForMediaAttachmentAsync().AsTask());
                var retryConnector = new CountingPeerStreamConnector();
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    lease.ConnectInitiatorForPreparationAsync(
                            request,
                            retryConnector,
                            CancellationToken.None)
                        .AsTask());
                Assert.Equal(0, retryConnector.ConnectCount);
                Assert.False(running.IsCompleted);
                Assert.Equal(0, Volatile.Read(ref revocationCount));
            }
            finally
            {
                connector.Stream.ReleaseDispose.TrySetResult();
            }

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    connecting.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("no longer current", failure.Message);

            Task firstCleanup = lease.FailCloseAsync().AsTask();
            Task secondCleanup = lease.FailCloseAsync().AsTask();

            Assert.Same(firstCleanup, secondCleanup);
            Assert.True(lease.IsRevoked);
            await firstCleanup.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, Volatile.Read(ref revocationCount));
            Assert.False(mediaSessions.TryGet(ResponderDeviceId, out _));
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task FailedPreparationMediaAttachmentWaitsForExplicitFailClose()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            IPAddress.Loopback,
            listenerPort: 4747,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var connector = new FailingAttachmentPeerStreamConnector();
        await using (participantConnection)
        await using (hostConnection)
        {
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForPeerConnectionLeaseAsync(
                    handler,
                    ResponderDeviceId);
            Assert.True(mediaSessions.TryGet(
                ResponderDeviceId,
                out AuthenticatedRemoteWindowMediaSession? currentSession));
            AuthenticatedRemoteWindowMediaSession mediaSession = Assert.IsType<
                AuthenticatedRemoteWindowMediaSession>(currentSession);
            int revocationCount = 0;
            using IDisposable registration =
                lease.RegisterRevocationCallback(
                    () => Interlocked.Increment(ref revocationCount));
            DateTimeOffset deadline = DateTimeOffset.UtcNow;
            deadline = deadline.AddTicks(
                -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
            RemoteWindowPreparationRequest request =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    ResponderDeviceId,
                    InitiatorDeviceId,
                    MirrorParticipantRole.ViewOnly,
                    deadline.Add(
                        RemoteWindowControlMessageCodec.MaximumCommandTimeToLive));

            IOException failure = await Assert.ThrowsAsync<IOException>(() =>
                lease.ConnectInitiatorForPreparationAsync(
                        request,
                        connector,
                        CancellationToken.None)
                    .AsTask());

            Assert.Same(connector.Failure, failure);
            Assert.False(mediaSession.ControlStopToken.IsCancellationRequested);
            Assert.False(lease.IsCurrent);
            Assert.False(lease.IsRevoked);
            Assert.False(running.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref revocationCount));
            Assert.True(handler.TryGetChannel(ResponderDeviceId, out _));
            Assert.True(mediaSessions.TryGet(ResponderDeviceId, out _));
            Assert.False(handler.TryAcquireRemoteWindowPeerConnection(
                ResponderDeviceId,
                out _));

            await lease.FailCloseAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.True(lease.IsRevoked);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, Volatile.Read(ref revocationCount));
            Assert.False(mediaSessions.TryGet(ResponderDeviceId, out _));
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task FailCloseCancelsAndJoinsPendingVerifiedPeerConnect()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            IPAddress.Loopback,
            listenerPort: 4747,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var connector = new NonCooperativePeerStreamConnector();
        await using (participantConnection)
        await using (hostConnection)
        {
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForPeerConnectionLeaseAsync(
                    handler,
                    ResponderDeviceId);
            DateTimeOffset deadline = DateTimeOffset.UtcNow;
            deadline = deadline.AddTicks(
                -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
            RemoteWindowPreparationRequest request =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    ResponderDeviceId,
                    InitiatorDeviceId,
                    MirrorParticipantRole.ViewOnly,
                    deadline.AddMinutes(1));
            Task connecting = lease.ConnectInitiatorAsync(
                    request,
                    connector,
                    CancellationToken.None)
                .AsTask();
            await connector.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task cleanup = lease.FailCloseAsync().AsTask();
            await connector.CancellationObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.False(cleanup.IsCompleted);
            Assert.False(connecting.IsCompleted);

            connector.Release.TrySetResult();

            await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                connecting.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(mediaSessions.TryGet(ResponderDeviceId, out _));
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task FailedPeerConnectFailClosesAfterConcurrentLeaseDisposal()
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Host");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(InitiatorDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        (AuthenticatedTcpControlConnection participantConnection,
            AuthenticatedTcpControlConnection hostConnection) =
            await CreateControlPairAsync(
                participantIdentity,
                hostIdentity,
                version);
        VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
            hostIdentity,
            IPAddress.Loopback,
            listenerPort: 4747,
            version);
        var validator = new RecordingCandidateValidator(isCurrent: true);
        var connector = new FailingBlockedPeerStreamConnector();
        await using (participantConnection)
        await using (hostConnection)
        {
            Task running = handler.RunWithRemoteWindowPeerAsync(
                    participantConnection,
                    candidate,
                    validator)
                .AsTask();
            AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForPeerConnectionLeaseAsync(
                    handler,
                    ResponderDeviceId);
            DateTimeOffset deadline = DateTimeOffset.UtcNow;
            deadline = deadline.AddTicks(
                -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
            RemoteWindowPreparationRequest request =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    ResponderDeviceId,
                    InitiatorDeviceId,
                    MirrorParticipantRole.ViewOnly,
                    deadline.AddMinutes(1));
            Task connecting = lease.ConnectInitiatorAsync(
                    request,
                    connector,
                    CancellationToken.None)
                .AsTask();
            await connector.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await lease.DisposeAsync();
            connector.Release.TrySetResult();

            Exception failure = Assert.IsType<IOException>(
                await Record.ExceptionAsync(() =>
                    connecting.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Same(connector.Failure, failure);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(handler.TryAcquireRemoteWindowPeerConnection(
                ResponderDeviceId,
                out _));
            Assert.False(mediaSessions.TryGet(ResponderDeviceId, out _));
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task ConnectionLeaseRetainsAuthenticatedHandshakePeerFingerprint()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);

            Assert.Equal(
                responderConnection.PeerIdentity.Fingerprint,
                lease.AuthenticatedPeerFingerprint);
            Assert.Equal(
                initiatorIdentity.PublicIdentity.Fingerprint,
                lease.AuthenticatedPeerFingerprint);

            await initiatorConnection.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task SameDeviceIdWithNewKeyCannotRetargetOlderConnectionLease()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity replacementInitiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Replacement initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        AuthenticatedRemoteWindowConnectionLease firstLease;
        (AuthenticatedTcpControlConnection firstInitiator,
            AuthenticatedTcpControlConnection firstResponder) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (firstInitiator)
        await using (firstResponder)
        {
            Task firstRunning = handler.RunAsync(firstResponder).AsTask();
            firstLease = await WaitForConnectionLeaseAsync(
                handler,
                InitiatorDeviceId);
            await firstInitiator.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                firstRunning.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        (AuthenticatedTcpControlConnection secondInitiator,
            AuthenticatedTcpControlConnection secondResponder) =
            await CreateControlPairAsync(
                replacementInitiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (firstLease)
        await using (secondInitiator)
        await using (secondResponder)
        {
            Task secondRunning = handler.RunAsync(secondResponder).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease secondLease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);

            Assert.True(firstLease.IsRevoked);
            Assert.False(firstLease.IsCurrent);
            Assert.True(secondLease.IsCurrent);
            Assert.True(secondLease.Generation > firstLease.Generation);
            Assert.Equal(
                initiatorIdentity.PublicIdentity.Fingerprint,
                firstLease.AuthenticatedPeerFingerprint);
            Assert.Equal(
                replacementInitiatorIdentity.PublicIdentity.Fingerprint,
                secondLease.AuthenticatedPeerFingerprint);
            Assert.NotEqual(
                firstLease.AuthenticatedPeerFingerprint,
                secondLease.AuthenticatedPeerFingerprint);
            Assert.Equal(firstLease.PeerDeviceId, secondLease.PeerDeviceId);
            Assert.Throws<InvalidOperationException>(() =>
                firstLease.PrepareResponderRoute(SessionId, ActivityId));

            RemoteWindowMediaRouteBinding currentBinding =
                secondLease.PrepareResponderRoute(SessionId, ActivityId);
            Assert.True(mediaSessions.TryGet(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowMediaSession? currentSession));
            Assert.Equal(currentBinding, Assert.IsType<
                AuthenticatedRemoteWindowMediaSession>(currentSession).Binding);

            await secondInitiator.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                secondRunning.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ConnectionLeaseFailCloseRevokesGenerationAndConsumesOwner()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease first =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);
            await using AuthenticatedRemoteWindowConnectionLease second =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);
            first.PrepareResponderRoute(SessionId, ActivityId);

            Task firstCleanup = first.FailCloseAsync().AsTask();
            Task secondCleanup = second.FailCloseAsync().AsTask();

            Assert.Same(firstCleanup, secondCleanup);
            Assert.False(first.IsCurrent);
            Assert.False(second.IsCurrent);
            Assert.True(first.IsRevoked);
            Assert.False(handler.TryAcquireRemoteWindowConnection(
                InitiatorDeviceId,
                out _));
            Assert.Throws<InvalidOperationException>(() =>
                second.PrepareResponderRoute(SessionId, ActivityId));

            await firstCleanup.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(mediaSessions.TryGet(InitiatorDeviceId, out _));
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task FailCloseFromOwnRevocationCallbackDoesNotJoinItself()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);
            var callbackReturnedCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration =
                lease.RegisterRevocationCallback(() =>
                {
                    Task callbackCleanup = lease.FailCloseAsync().AsTask();
                    callbackReturnedCompleted.TrySetResult(
                        callbackCleanup.IsCompletedSuccessfully);
                });

            Task externalCleanup = lease.FailCloseAsync().AsTask();

            Assert.True(await callbackReturnedCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)));
            await externalCleanup.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task DisposedConnectionLeaseCannotFailCloseItsFormerGeneration()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);

            await lease.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                lease.FailCloseAsync().AsTask());
            Assert.True(handler.TryAcquireRemoteWindowConnection(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? replacement));
            await Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                replacement).DisposeAsync();
            await initiatorConnection.DisposeAsync();
            await Assert.ThrowsAnyAsync<IOException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task GenerationRevocationAllowsReentrantHandlerDisposal()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);
            var callbackCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration =
                lease.RegisterRevocationCallback(() =>
                {
                    try
                    {
                        callbackCompleted.TrySetResult(
                            handler.DisposeAsync().AsTask().IsCompletedSuccessfully);
                    }
                    catch (Exception exception)
                    {
                        callbackCompleted.TrySetException(exception);
                    }
                });

            await initiatorConnection.DisposeAsync();

            Assert.True(await callbackCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)));
            await handler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<IOException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task GenerationRevocationAllowsTaskRunHandlerDisposal()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);
            var callbackCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration =
                lease.RegisterRevocationCallback(() =>
                {
                    try
                    {
                        Task reentrantDisposal = Task.Run(async () =>
                            await handler.DisposeAsync());
                        callbackCompleted.TrySetResult(
                            reentrantDisposal.Wait(TimeSpan.FromSeconds(2)));
                    }
                    catch (Exception exception)
                    {
                        callbackCompleted.TrySetException(exception);
                    }
                });

            await initiatorConnection.DisposeAsync();

            Assert.True(await callbackCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)));
            await handler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<IOException>(() =>
                running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task CopiedGenerationRevocationContextJoinsAfterCallbackReturns()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            InitiatorDeviceId,
            "Initiator");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            ResponderDeviceId,
            "Responder");
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ResponderDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions);
        (AuthenticatedTcpControlConnection initiatorConnection,
            AuthenticatedTcpControlConnection responderConnection) =
            await CreateControlPairAsync(
                initiatorIdentity,
                responderIdentity,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);
        await using (initiatorConnection)
        await using (responderConnection)
        {
            Task running = handler.RunAsync(responderConnection).AsTask();
            await using AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForConnectionLeaseAsync(handler, InitiatorDeviceId);
            Assert.True(mediaSessions.TryGet(
                InitiatorDeviceId,
                out AuthenticatedRemoteWindowMediaSession? acquiredMediaSession));
            AuthenticatedRemoteWindowMediaSession mediaSession = Assert.IsType<
                AuthenticatedRemoteWindowMediaSession>(acquiredMediaSession);
            var copiedContextReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCopiedContext = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var copiedDisposalCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var copiedDisposalReturned = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var mediaCleanupStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseMediaCleanup = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration mediaCleanupRegistration =
                mediaSession.ControlStopToken.Register(() =>
                {
                    mediaCleanupStarted.TrySetResult();
                    releaseMediaCleanup.Task.GetAwaiter().GetResult();
                });
            using IDisposable revocationRegistration =
                lease.RegisterRevocationCallback(() =>
                    _ = DisposeFromCopiedContextAsync());

            try
            {
                await initiatorConnection.DisposeAsync();
                await copiedContextReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await mediaCleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                releaseCopiedContext.TrySetResult();

                Assert.False(await copiedDisposalCompleted.Task.WaitAsync(
                    TimeSpan.FromSeconds(5)));
                Assert.False(copiedDisposalReturned.Task.IsCompleted);
                releaseMediaCleanup.TrySetResult();
                await copiedDisposalReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.ThrowsAnyAsync<IOException>(() =>
                    running.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                releaseCopiedContext.TrySetResult();
                releaseMediaCleanup.TrySetResult();
            }

            async Task DisposeFromCopiedContextAsync()
            {
                copiedContextReady.TrySetResult();
                await releaseCopiedContext.Task;
                Task disposal = handler.DisposeAsync().AsTask();
                copiedDisposalCompleted.TrySetResult(
                    disposal.IsCompletedSuccessfully);
                await disposal;
                copiedDisposalReturned.TrySetResult();
            }
        }
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
    public async Task ConcurrentControlStopAndDisposalSignalLiveCallbackExactlyOnce()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions(seed: 0x85);
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                ownedFrames);
            int[] callbackCount = [0];
            using CancellationTokenRegistration liveRegistration =
                session.ControlStopToken.UnsafeRegister(
                    static state =>
                        Interlocked.Increment(ref ((int[])state!)[0]),
                    callbackCount);

            Task stop = Task.Run(session.RequestControlStop);
            Task dispose = Task.Run(async () => await session.DisposeAsync());
            await Task.WhenAll(stop, dispose).WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(1, Volatile.Read(ref callbackCount[0]));
            Assert.True(session.ControlStopToken.IsCancellationRequested);
            Assert.False(session.IsCurrent);
        }
    }

    [Fact]
    public async Task FatalLiveCallbackEscapesRawAfterMediaCleanup()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions(seed: 0x86);
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                ownedFrames);
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
            var fatal = new OutOfMemoryException(
                "FLOWSPAN_MEDIA_LIVE_CALLBACK_FATAL_CANARY");
#pragma warning restore CA2201
            using CancellationTokenRegistration liveRegistration =
                session.ControlStopToken.UnsafeRegister(
                    static state => throw (OutOfMemoryException)state!,
                    fatal);

            OutOfMemoryException first = await Assert.ThrowsAsync<
                OutOfMemoryException>(() => session.DisposeAsync().AsTask());
            OutOfMemoryException repeated = await Assert.ThrowsAsync<
                OutOfMemoryException>(() => session.DisposeAsync().AsTask());

            Assert.Same(fatal, first);
            Assert.Same(first, repeated);
            Assert.True(session.ControlStopToken.IsCancellationRequested);
            Assert.False(session.IsCurrent);
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task LateOldLiveRegistrationDisposeCannotClearReplacement()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions(seed: 0x87);
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        {
            var session = new AuthenticatedRemoteWindowMediaSession(
                InitiatorDeviceId,
                ResponderDeviceId,
                ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
                routes,
                ownedFrames);
            int[] counts = [0, 0];
            CancellationTokenRegistration old = session.ControlStopToken.UnsafeRegister(
                static state => Interlocked.Increment(ref ((int[])state!)[0]),
                counts);
            old.Dispose();
            using CancellationTokenRegistration replacement =
                session.ControlStopToken.UnsafeRegister(
                    static state => Interlocked.Increment(ref ((int[])state!)[1]),
                    counts);

            old.Dispose();
            session.RequestControlStop();

            Assert.Equal(0, Volatile.Read(ref counts[0]));
            Assert.Equal(1, Volatile.Read(ref counts[1]));
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposalSignalsControlStopBeforeRouteCleanup()
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

            Assert.True(controlStoppedBeforeRevocation);
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

    private static async Task<(
        AuthenticatedTcpControlConnection Initiator,
        AuthenticatedTcpControlConnection Responder)> CreateControlPairAsync(
        DeviceIdentity initiatorIdentity,
        DeviceIdentity responderIdentity,
        ProtocolVersion version)
    {
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
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();
        AuthenticatedTcpControlConnection initiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]);
        try
        {
            return (initiator, await accepting);
        }
        catch
        {
            await initiator.DisposeAsync();
            throw;
        }
    }

    private static async Task<AuthenticatedRemoteWindowConnectionLease>
        WaitForConnectionLeaseAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        AuthenticatedRemoteWindowConnectionLease? lease;
        while (!handler.TryAcquireRemoteWindowConnection(
            peerDeviceId,
            out lease))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }

        return Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(lease);
    }

    private static async Task<AuthenticatedRemoteWindowConnectionLease>
        WaitForPeerConnectionLeaseAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        AuthenticatedRemoteWindowConnectionLease? lease;
        while (!handler.TryAcquireRemoteWindowPeerConnection(
            peerDeviceId,
            out lease))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }

        return Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(lease);
    }

    private static VerifiedPeerConnectionCandidate CreateVerifiedCandidate(
        DeviceIdentity peer,
        IPAddress address,
        int listenerPort,
        ProtocolVersion version)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            peer,
            listenerPort,
            [version],
            now.Subtract(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat(
                    (byte)0x5a,
                    SignedDiscoveryOffer.NonceLength)
                .ToArray());
        return VerifiedPeerConnectionCandidate.Create(
            new IPEndPoint(address, listenerPort),
            offer,
            peer.PublicIdentity,
            now);
    }

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

    private sealed class RecordingConnectionPreparationSink(Action invalidated) :
        IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
    {
        public IAuthenticatedRemoteWindowConnectionPreparationRegistration?
            OwnedRegistration
        { get; private set; }

        public void InvalidateAuthenticatedRemoteWindowConnectionPreparationNow() =>
            invalidated();

        public void OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration) =>
            OwnedRegistration = registration;
    }

    private sealed class UnusedPreparationChannel(DeviceId participantDeviceId) :
        IRemoteWindowPreparationChannel
    {
        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BlockingForwardingMediaHandler(
        IRemoteWindowMediaAttachmentHandler inner) :
        IRemoteWindowMediaAttachmentHandler
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;
        private int forwardCount;
        private int released;

        public RemoteWindowMediaRouteBinding? Binding { get; private set; }

        public int CallCount => Volatile.Read(ref callCount);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Exited { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ForwardCount => Volatile.Read(ref forwardCount);

        public bool IsReleased => Volatile.Read(ref released) != 0;

        public async ValueTask HandleAsync(
            RemoteWindowMediaAttachment attachment,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            Interlocked.Increment(ref callCount);
            Binding = attachment.Binding;
            Entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref forwardCount);
            try
            {
                await inner.HandleAsync(attachment, cancellationToken);
            }
            finally
            {
                Exited.TrySetResult();
            }
        }

        public void Release()
        {
            Volatile.Write(ref released, 1);
            release.TrySetResult();
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

    private sealed class RecordingCandidateValidator(bool isCurrent) :
        IVerifiedPeerConnectionCandidateValidator
    {
        private int current = isCurrent ? 1 : 0;
        private int validationCount;

        public int ValidationCount => Volatile.Read(ref validationCount);

        public void SetCurrent(bool value) =>
            Volatile.Write(ref current, value ? 1 : 0);

        public bool IsCurrent(
            VerifiedPeerConnectionCandidate candidate,
            ProtocolVersion protocolVersion)
        {
            Interlocked.Increment(ref validationCount);
            return Volatile.Read(ref current) != 0;
        }
    }

    private sealed class InvalidatingPeerStreamConnector(
        RecordingCandidateValidator validator) :
        IRemoteWindowPeerStreamConnector
    {
        public MemoryStream Stream { get; } = new();

        public ValueTask<Stream> ConnectAsync(
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            validator.SetCurrent(false);
            return ValueTask.FromResult<Stream>(Stream);
        }
    }

    private sealed class InvalidatingBlockingDisposePeerStreamConnector(
        RecordingCandidateValidator validator) :
        IRemoteWindowPeerStreamConnector
    {
        public BlockingAsyncDisposeStream Stream { get; } = new();

        public ValueTask<Stream> ConnectAsync(
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            validator.SetCurrent(false);
            return ValueTask.FromResult<Stream>(Stream);
        }
    }

    private sealed class BlockingAsyncDisposeStream : MemoryStream
    {
        public TaskCompletionSource DisposeStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDispose { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            await ReleaseDispose.Task.ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class NonCooperativePeerStreamConnector :
        IRemoteWindowPeerStreamConnector
    {
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Stream> ConnectAsync(
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => CancellationObserved.TrySetResult());
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new MemoryStream();
        }
    }

    private sealed class FailingBlockedPeerStreamConnector :
        IRemoteWindowPeerStreamConnector
    {
        public IOException Failure { get; } = new("CANARY_CONNECT_FAILURE");

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Stream> ConnectAsync(
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            throw Failure;
        }
    }

    private sealed class FailingAttachmentPeerStreamConnector :
        IRemoteWindowPeerStreamConnector
    {
        public IOException Failure { get; } = new("CANARY_ATTACHMENT_FAILURE");

        public ValueTask<Stream> ConnectAsync(
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(
                new FailingAttachmentStream(Failure));
        }
    }

    private sealed class FailingAttachmentStream(IOException failure) :
        MemoryStream
    {
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(failure);
    }

    private sealed class CountingPeerStreamConnector :
        IRemoteWindowPeerStreamConnector
    {
        private int connectCount;

        public int ConnectCount => Volatile.Read(ref connectCount);

        public ValueTask<Stream> ConnectAsync(
            IPEndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref connectCount);
            return ValueTask.FromResult<Stream>(new MemoryStream());
        }
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
