using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class FlowspanTcpInboundListenerTests
{
    private static readonly CapabilityGrant Required =
        CapabilityGrant.Of(Capability.ActivityReceive);
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task InternalRawMediaRouterSupportsFocusedListenerContractTesting()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        var serverTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateMediaSessions();
        RemoteWindowMediaRouteBinding binding = CreateMediaBinding(initiatorSession);
        await using var registry = new RemoteWindowMediaRouteRegistry();
        await using RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(binding, responderSession);
        var mediaHandler = new ReceivingMediaHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = new FlowspanTcpInboundListener(
            socket,
            serverIdentity,
            CreatePairingProfile(),
            new AcceptingDecisionSource(Required),
            trustSessions,
            new FlowspanTcpInboundProfile(CreateSessionProfile()),
            new NeverHandler(),
            mediaRoutes: registry,
            mediaHandler: mediaHandler);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(endPoint);
        await using RemoteWindowMediaAttachment initiator =
            await RemoteWindowMediaAttachment.ConnectAsync(
                client.GetStream(),
                binding,
                initiatorSession);
        using RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
            binding.SessionId,
            binding.ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x10, 0x20, 0x30]);

        await initiator.SendAsync(expected);
        using RemoteWindowMediaFrame actual = await mediaHandler.Received.Task
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
        Assert.Equal(binding, mediaHandler.Binding);
        Assert.True(registration.IsAttached);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task OwnedMediaHandlerPreservesPrimaryAndAttachmentCleanupFailures()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateMediaSessions();
        using (initiatorSession)
        {
            RemoteWindowMediaRouteBinding binding =
                CreateMediaBinding(initiatorSession);
            var stream = new CleanupFailingMediaStream();
            var channel = new SecureRemoteWindowMediaChannel(
                stream,
                responderSession,
                binding.SessionId,
                binding.ActivityId);
            var attachment = new RemoteWindowMediaAttachment(binding, channel);
            var handler = new FailingMediaHandler();

            AggregateException failure =
                await Assert.ThrowsAsync<AggregateException>(async () =>
                    await FlowspanTcpInboundListener
                        .RunOwnedMediaAttachmentHandlerAsync(
                            attachment,
                            handler,
                            CancellationToken.None));

            Assert.Contains(
                failure.Flatten().InnerExceptions,
                cause => ReferenceEquals(cause, handler.PrimaryFailure));
            Assert.Contains(
                failure.Flatten().InnerExceptions,
                cause => ReferenceEquals(cause, stream.CleanupFailure));
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task OwnedMediaHandlerDoesNotHideCleanupFailureBehindCancellation()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateMediaSessions();
        using (initiatorSession)
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            RemoteWindowMediaRouteBinding binding =
                CreateMediaBinding(initiatorSession);
            var stream = new CleanupFailingMediaStream();
            var channel = new SecureRemoteWindowMediaChannel(
                stream,
                responderSession,
                binding.SessionId,
                binding.ActivityId);
            var attachment = new RemoteWindowMediaAttachment(binding, channel);
            var handler = new FailingMediaHandler(
                new OperationCanceledException(cancellation.Token));

            AggregateException failure =
                await Assert.ThrowsAsync<AggregateException>(async () =>
                    await FlowspanTcpInboundListener
                        .RunOwnedMediaAttachmentHandlerAsync(
                            attachment,
                            handler,
                            cancellation.Token));

            Assert.Contains(
                failure.Flatten().InnerExceptions,
                cause => ReferenceEquals(cause, handler.PrimaryFailure));
            Assert.Contains(
                failure.Flatten().InnerExceptions,
                cause => ReferenceEquals(cause, stream.CleanupFailure));
        }
    }

    [Fact]
    public void PublicConstructionRequiresConnectionOwnedMediaComposition()
    {
        System.Reflection.ConstructorInfo[] constructors =
            typeof(FlowspanTcpInboundListener).GetConstructors();

        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor =>
        {
            Type[] parameterTypes = constructor
                .GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray();
            Assert.DoesNotContain(
                typeof(RemoteWindowMediaRouteRegistry),
                parameterTypes);
            Assert.DoesNotContain(
                typeof(IRemoteWindowMediaAttachmentHandler),
                parameterTypes);
            Assert.Contains(
                typeof(AuthenticatedRemoteWindowMediaSessionDirectory),
                parameterTypes);
        });
        Assert.All(
            typeof(AuthenticatedRemoteWindowMediaSessionDirectory)
                .GetConstructors(),
            constructor => Assert.DoesNotContain(
                typeof(RemoteWindowMediaRouteRegistry),
                constructor
                    .GetParameters()
                    .Select(static parameter => parameter.ParameterType)));
    }

    [Theory]
    [InlineData("wrong-length")]
    [InlineData("unknown-route")]
    [InlineData("revoked-route")]
    public async Task InvalidMediaAttachmentPublishesExplicitFailureStage(string kind)
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        var serverTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        await using var registry = new RemoteWindowMediaRouteRegistry();
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateMediaSessions();
        using (initiatorSession)
        {
            RemoteWindowMediaRouteBinding binding = CreateMediaBinding(initiatorSession);
            RemoteWindowMediaRouteRegistration? registration = kind == "unknown-route"
                ? null
                : registry.RegisterOwnedRoute(binding, responderSession);
            if (kind == "unknown-route")
            {
                responderSession.Dispose();
            }
            else if (kind == "revoked-route")
            {
                await registration!.DisposeAsync();
            }

            await using (registration)
            using (var socket = new TcpListener(IPAddress.Loopback, 0))
            {
                socket.Start(backlog: 8);
                var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
                var failureSeen = new TaskCompletionSource<InboundConnectionFailure>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var listener = new FlowspanTcpInboundListener(
                    socket,
                    serverIdentity,
                    CreatePairingProfile(),
                    new AcceptingDecisionSource(Required),
                    trustSessions,
                    new FlowspanTcpInboundProfile(CreateSessionProfile()),
                    new NeverHandler(),
                    mediaRoutes: registry,
                    mediaHandler: new ReceivingMediaHandler());
                listener.ConnectionFaulted += failure =>
                {
                    if (failure.Stage == InboundConnectionFailureStage.MediaAttachment)
                    {
                        failureSeen.TrySetResult(failure);
                    }
                };
                using var cancellation = new CancellationTokenSource();
                Task running = listener.RunAsync(cancellation.Token).AsTask();
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(endPoint);
                byte[] request = kind == "wrong-length"
                    ? CreateWrongLengthMediaRequest()
                    : RemoteWindowMediaAttachmentCodec.EncodeRequest(
                        binding,
                        Enumerable.Repeat((byte)0x62, 32).ToArray(),
                        initiatorSession);
                try
                {
                    await SendFrameAsync(client.GetStream(), request);
                    InboundConnectionFailure failure = await failureSeen.Task
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(
                        InboundConnectionFailureStage.MediaAttachment,
                        failure.Stage);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(request);
                }

                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            }
        }
    }

    [Fact]
    public async Task MediaCapacityIsIndependentAndShutdownJoinsItsHandler()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        var serverTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateMediaSessions();
        RemoteWindowMediaRouteBinding binding = CreateMediaBinding(initiatorSession);
        await using var registry = new RemoteWindowMediaRouteRegistry();
        await using RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(binding, responderSession);
        var mediaHandler = new BlockingMediaHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var profile = new FlowspanTcpInboundProfile(
            CreateSessionProfile(maximumSessions: 1),
            maximumConcurrentMediaAttachments: 1);
        var capacitySeen = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new FlowspanTcpInboundListener(
            socket,
            serverIdentity,
            CreatePairingProfile(),
            new AcceptingDecisionSource(Required),
            trustSessions,
            profile,
            new NeverHandler(),
            mediaRoutes: registry,
            mediaHandler: mediaHandler);
        listener.ConnectionFaulted += failure =>
        {
            if (failure.Stage == InboundConnectionFailureStage.Capacity)
            {
                capacitySeen.TrySetResult(failure);
            }
        };
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        using var firstClient = new TcpClient(AddressFamily.InterNetwork);
        await firstClient.ConnectAsync(endPoint);
        await using RemoteWindowMediaAttachment firstAttachment =
            await RemoteWindowMediaAttachment.ConnectAsync(
                firstClient.GetStream(),
                binding,
                initiatorSession);
        await mediaHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var secondClient = new TcpClient(AddressFamily.InterNetwork);
        await secondClient.ConnectAsync(endPoint);
        byte[] secondRequest = CreateWrongLengthMediaRequest(
            RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes);
        await SendFrameAsync(secondClient.GetStream(), secondRequest);
        CryptographicOperations.ZeroMemory(secondRequest);

        InboundConnectionFailure capacity = await capacitySeen.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("media-attachment capacity", capacity.Exception.Message);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(mediaHandler.CancellationObserved);
    }

    [Fact]
    public async Task MediaAttachmentDoesNotConsumeAuthenticatedSessionSlot()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Peer");
        var serverTrust = new InMemoryTrustStore();
        serverTrust.Register(CreateTrust(peerIdentity, Required));
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateMediaSessions();
        RemoteWindowMediaRouteBinding binding = CreateMediaBinding(initiatorSession);
        await using var registry = new RemoteWindowMediaRouteRegistry();
        await using RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(binding, responderSession);
        var controlHandler = new BlockingHandler();
        var mediaHandler = new ReceivingMediaHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = new FlowspanTcpInboundListener(
            socket,
            serverIdentity,
            CreatePairingProfile(),
            new AcceptingDecisionSource(Required),
            trustSessions,
            new FlowspanTcpInboundProfile(CreateSessionProfile(maximumSessions: 1)),
            controlHandler,
            mediaRoutes: registry,
            mediaHandler: mediaHandler);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        await using AuthenticatedTcpControlConnection control =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                peerIdentity,
                CreateTrust(serverIdentity, CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await controlHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var mediaClient = new TcpClient(AddressFamily.InterNetwork);
        await mediaClient.ConnectAsync(endPoint);
        await using RemoteWindowMediaAttachment media =
            await RemoteWindowMediaAttachment.ConnectAsync(
                mediaClient.GetStream(),
                binding,
                initiatorSession);
        using RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
            binding.SessionId,
            binding.ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x71]);

        await media.SendAsync(expected);
        using RemoteWindowMediaFrame actual = await mediaHandler.Received.Task
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task RawUnframedMediaMagicIsRejectedDuringProtocolSelection()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        var serverTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        await using var registry = new RemoteWindowMediaRouteRegistry();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var failureSeen = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new FlowspanTcpInboundListener(
            socket,
            serverIdentity,
            CreatePairingProfile(),
            new AcceptingDecisionSource(Required),
            trustSessions,
            new FlowspanTcpInboundProfile(CreateSessionProfile()),
            new NeverHandler(),
            mediaRoutes: registry,
            mediaHandler: new ReceivingMediaHandler());
        listener.ConnectionFaulted += failure => failureSeen.TrySetResult(failure);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(endPoint);

        await client.GetStream().WriteAsync("FSM1"u8.ToArray());
        InboundConnectionFailure failure = await failureSeen.Task
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(InboundConnectionFailureStage.ProtocolSelection, failure.Stage);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task SamePublishedPortPairsThenAuthenticatesANewConnection()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity peerIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Laptop");
        var serverTrust = new InMemoryTrustStore();
        var peerTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var handler = new BlockingHandler();
        var pairingCompleted = new TaskCompletionSource<InboundPairingCompleted>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = CreateListener(
            socket,
            serverIdentity,
            trustSessions,
            new AcceptingDecisionSource(Required),
            handler);
        listener.PairingCompleted += result => pairingCompleted.TrySetResult(result);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        DirectTcpPairingChannel pairingChannel =
            await DirectTcpPairingChannel.ConnectAsync(endPoint);
        PairingCeremonyResult peerPairing = await new PairingCeremony(
            CreatePairingProfile(),
            new AcceptingDecisionSource(CapabilityGrant.None),
            peerTrust).RunInitiatorAsync(
                pairingChannel,
                peerIdentity);
        InboundPairingCompleted serverPairing = await pairingCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(peerPairing.Succeeded);
        Assert.True(serverPairing.Result.Succeeded);
        Assert.Equal(peerIdentity.DeviceId, serverPairing.Result.PeerIdentity!.DeviceId);
        Assert.True(serverTrust.Allows(peerIdentity.DeviceId, Capability.ActivityReceive));
        Assert.True(peerTrust.TryGet(
            serverIdentity.DeviceId,
            out TrustRecord? serverRecord));

        await using AuthenticatedTcpControlConnection authenticated =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                peerIdentity,
                serverRecord,
                [new ProtocolVersion(1, 0)]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(peerIdentity.DeviceId, handler.PeerDeviceId);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task PendingPairingDecisionDoesNotBlockATrustedSession()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity pairingPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "New Laptop");
        using DeviceIdentity trustedPeer = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Trusted Desk");
        var serverTrust = new InMemoryTrustStore();
        serverTrust.Register(CreateTrust(trustedPeer, Required));
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        var pendingDecision = new BlockingDecisionSource();
        var handler = new BlockingHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = CreateListener(
            socket,
            serverIdentity,
            trustSessions,
            pendingDecision,
            handler);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        var pairingPeerTrust = new InMemoryTrustStore();
        DirectTcpPairingChannel pairingChannel =
            await DirectTcpPairingChannel.ConnectAsync(endPoint);
        Task<PairingCeremonyResult> pairing = new PairingCeremony(
            CreatePairingProfile(),
            new AcceptingDecisionSource(CapabilityGrant.None),
            pairingPeerTrust).RunInitiatorAsync(
                pairingChannel,
                pairingPeer).AsTask();
        await pendingDecision.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using AuthenticatedTcpControlConnection authenticated =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                trustedPeer,
                CreateTrust(serverIdentity, CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(trustedPeer.DeviceId, handler.PeerDeviceId);
        Assert.False(pairing.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<Exception>(() => pairing);
        Assert.True(pendingDecision.CancellationObserved);
        Assert.True(handler.CancellationObserved);
        Assert.False(serverTrust.TryGet(pairingPeer.DeviceId, out _));
    }

    [Fact]
    public async Task UnknownFirstFrameIsIsolatedBeforeNextAuthenticatedPeer()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity trustedPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var serverTrust = new InMemoryTrustStore();
        serverTrust.Register(CreateTrust(trustedPeer, Required));
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        var handler = new BlockingHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var failureSeen = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = CreateListener(
            socket,
            serverIdentity,
            trustSessions,
            new AcceptingDecisionSource(Required),
            handler);
        listener.ConnectionFaulted += failure => failureSeen.TrySetResult(failure);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        using (var malformed = new TcpClient(AddressFamily.InterNetwork))
        {
            await malformed.ConnectAsync(endPoint);
            await SendFrameAsync(malformed.GetStream(), "BAD!"u8.ToArray().Append((byte)1).ToArray());
            InboundConnectionFailure failure = await failureSeen.Task
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(
                InboundConnectionFailureStage.ProtocolSelection,
                failure.Stage);
            Assert.IsType<InvalidDataException>(failure.Exception);
        }

        await using AuthenticatedTcpControlConnection authenticated =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                trustedPeer,
                CreateTrust(serverIdentity, CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(trustedPeer.DeviceId, handler.PeerDeviceId);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task PairingCapacityRejectsAnotherPromptWithoutWritingTrust()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity firstPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "First");
        using DeviceIdentity secondPeer = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Second");
        var serverTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        var pendingDecision = new BlockingDecisionSource();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var capacitySeen = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = CreateListener(
            socket,
            serverIdentity,
            trustSessions,
            pendingDecision,
            new NeverHandler());
        listener.ConnectionFaulted += failure =>
        {
            if (failure.Stage == InboundConnectionFailureStage.Capacity)
            {
                capacitySeen.TrySetResult(failure);
            }
        };
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        Task<PairingCeremonyResult> first = StartPairingAsync(
            endPoint,
            firstPeer,
            new InMemoryTrustStore());
        await pendingDecision.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<PairingCeremonyResult> second = StartPairingAsync(
            endPoint,
            secondPeer,
            new InMemoryTrustStore());

        InboundConnectionFailure capacity = await capacitySeen.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("pairing capacity", capacity.Exception.Message);
        await Assert.ThrowsAnyAsync<Exception>(() => second);
        Assert.False(serverTrust.TryGet(secondPeer.DeviceId, out _));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<Exception>(() => first);
        Assert.False(serverTrust.TryGet(firstPeer.DeviceId, out _));
    }

    [Fact]
    public async Task AuthenticatedCapacityRejectsAnotherSessionWithoutBlockingAccept()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity firstPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "First");
        using DeviceIdentity secondPeer = CreateIdentity(
            "33333333-3333-3333-3333-333333333333",
            "Second");
        var serverTrust = new InMemoryTrustStore();
        serverTrust.Register(CreateTrust(firstPeer, Required));
        serverTrust.Register(CreateTrust(secondPeer, Required));
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        var handler = new BlockingHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var capacitySeen = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = CreateListener(
            socket,
            serverIdentity,
            trustSessions,
            new AcceptingDecisionSource(Required),
            handler,
            maximumSessions: 1);
        listener.ConnectionFaulted += failure =>
        {
            if (failure.Stage == InboundConnectionFailureStage.Capacity)
            {
                capacitySeen.TrySetResult(failure);
            }
        };
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();

        await using AuthenticatedTcpControlConnection first =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                firstPeer,
                CreateTrust(serverIdentity, CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using AuthenticatedTcpControlConnection second =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endPoint,
                    secondPeer,
                    CreateTrust(serverIdentity, CapabilityGrant.None),
                    [new ProtocolVersion(1, 0)]);
        });
        InboundConnectionFailure capacity = await capacitySeen.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("authenticated-session capacity", capacity.Exception.Message);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task ProtocolSelectionTimeoutUsesInjectedTime()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        var serverTrust = new InMemoryTrustStore();
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var failureSeen = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionProfile = CreateSessionProfile();
        var listener = new FlowspanTcpInboundListener(
            socket,
            serverIdentity,
            CreatePairingProfile(),
            new AcceptingDecisionSource(Required),
            trustSessions,
            new FlowspanTcpInboundProfile(sessionProfile),
            new NeverHandler(),
            time);
        listener.ConnectionFaulted += failure => failureSeen.TrySetResult(failure);
        using var cancellation = new CancellationTokenSource();
        Task running = listener.RunAsync(cancellation.Token).AsTask();
        using var silent = new TcpClient(AddressFamily.InterNetwork);
        await silent.ConnectAsync(endPoint);
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));

        time.Advance(FlowspanTcpInboundProfile.DefaultProtocolSelectionTimeout);
        InboundConnectionFailure failure = await failureSeen.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(InboundConnectionFailureStage.ProtocolSelection, failure.Stage);
        Assert.IsType<TimeoutException>(failure.Exception);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task FatalAcceptFailureCancelsAndDrainsAuthenticatedSession()
    {
        using DeviceIdentity serverIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Server");
        using DeviceIdentity trustedPeer = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var serverTrust = new InMemoryTrustStore();
        serverTrust.Register(CreateTrust(trustedPeer, Required));
        await using var trustSessions = new TrustSessionCoordinator(serverTrust);
        var handler = new BlockingHandler();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endPoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = CreateListener(
            socket,
            serverIdentity,
            trustSessions,
            new AcceptingDecisionSource(Required),
            handler);
        Task running = listener.RunAsync().AsTask();

        await using AuthenticatedTcpControlConnection authenticated =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endPoint,
                trustedPeer,
                CreateTrust(serverIdentity, CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        socket.Stop();

        await Assert.ThrowsAnyAsync<Exception>(() => running);
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public void ProfileBoundsTotalPairingAndSelectionCapacity()
    {
        AuthenticatedInboundSessionProfile sessions = CreateSessionProfile();
        var profile = new FlowspanTcpInboundProfile(sessions);

        Assert.Equal(
            sessions.MaximumConcurrentSessions
                + FlowspanTcpInboundProfile.DefaultMaximumConcurrentPairings
                + FlowspanTcpInboundProfile
                    .DefaultMaximumConcurrentMediaAttachments,
            profile.MaximumConcurrentConnections);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FlowspanTcpInboundProfile(
                sessions,
                maximumConcurrentConnections: sessions.MaximumConcurrentSessions - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FlowspanTcpInboundProfile(
                sessions,
                maximumConcurrentPairings:
                    FlowspanTcpInboundProfile.MaximumConcurrentPairingsLimit + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FlowspanTcpInboundProfile(
                sessions,
                protocolSelectionTimeout:
                    FlowspanTcpInboundProfile.MaximumProtocolSelectionTimeout
                    + TimeSpan.FromTicks(1)));
    }

    private static FlowspanTcpInboundListener CreateListener(
        TcpListener socket,
        DeviceIdentity localIdentity,
        TrustSessionCoordinator trustSessions,
        IPairingDecisionSource decisions,
        IAuthenticatedControlSessionHandler handler,
        int maximumSessions = 2) => new(
        socket,
        localIdentity,
        CreatePairingProfile(),
        decisions,
        trustSessions,
        new FlowspanTcpInboundProfile(CreateSessionProfile(maximumSessions)),
        handler);

    private static DeviceIdentity CreateIdentity(string id, string name) =>
        DeviceIdentity.Generate(DeviceId.Parse(id), name);

    private static PairingCeremonyProfile CreatePairingProfile() => new(
        [new ProtocolVersion(1, 0)],
        timeout: TimeSpan.FromSeconds(5));

    private static AuthenticatedInboundSessionProfile CreateSessionProfile(
        int maximumSessions = 2) => new(
        Required,
        [new ProtocolVersion(1, 0)],
        maximumConcurrentSessions: maximumSessions,
        handshakeTimeout: TimeSpan.FromSeconds(2));

    private static TrustRecord CreateTrust(
        DeviceIdentity identity,
        CapabilityGrant capabilities) => new(
        identity.PublicIdentity,
        Now,
        capabilities);

    private static RemoteWindowMediaRouteBinding CreateMediaBinding(
        SecureFrameSession mediaSession) =>
        RemoteWindowMediaRouteBinding.Create(
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            RemoteWindowMediaRouteId.FromSession(mediaSession),
            RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static byte[] CreateWrongLengthMediaRequest(int length = 199)
    {
        byte[] request = new byte[length];
        "FSM1"u8.CopyTo(request);
        return request;
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateMediaSessions()
    {
        byte[] secret = SHA256.HashData(BitConverter.GetBytes(0x61));
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes("flowspan-listener-media-route"));
        using SecureSessionKeyMaterial material =
            SecureSessionKeyMaterial.DeriveRemoteWindowMedia(secret, transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    private static async Task SendFrameAsync(
        NetworkStream stream,
        byte[] message)
    {
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, message.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(message);
        await stream.FlushAsync();
    }

    private static async Task<PairingCeremonyResult> StartPairingAsync(
        IPEndPoint endPoint,
        DeviceIdentity identity,
        ITrustStore trustStore)
    {
        DirectTcpPairingChannel channel =
            await DirectTcpPairingChannel.ConnectAsync(endPoint);
        return await new PairingCeremony(
            CreatePairingProfile(),
            new AcceptingDecisionSource(CapabilityGrant.None),
            trustStore).RunInitiatorAsync(channel, identity);
    }

    private sealed class AcceptingDecisionSource(CapabilityGrant capabilities) :
        IPairingDecisionSource
    {
        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PairingDecision(
                accepted: true,
                capabilities));
        }
    }

    private sealed class BlockingDecisionSource : IPairingDecisionSource
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable pairing decision.");
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class BlockingHandler : IAuthenticatedControlSessionHandler
    {
        public bool CancellationObserved { get; private set; }

        public DeviceId? PeerDeviceId { get; private set; }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            PeerDeviceId = connection.PeerIdentity.DeviceId;
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

    private sealed class NeverHandler : IAuthenticatedControlSessionHandler
    {
        public ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(
                new InvalidOperationException("No authenticated session is expected."));
    }

    private sealed class ReceivingMediaHandler : IRemoteWindowMediaAttachmentHandler
    {
        public RemoteWindowMediaRouteBinding? Binding { get; private set; }

        public TaskCompletionSource<RemoteWindowMediaFrame> Received { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask HandleAsync(
            RemoteWindowMediaAttachment attachment,
            CancellationToken cancellationToken = default)
        {
            Binding = attachment.Binding;
            Received.TrySetResult(await attachment.ReceiveAsync(cancellationToken));
        }
    }

    private sealed class BlockingMediaHandler : IRemoteWindowMediaAttachmentHandler
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask HandleAsync(
            RemoteWindowMediaAttachment attachment,
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

    private sealed class FailingMediaHandler : IRemoteWindowMediaAttachmentHandler
    {
        public FailingMediaHandler(Exception? primaryFailure = null) =>
            PrimaryFailure = primaryFailure
                ?? new InvalidDataException("injected media handler failure");

        public Exception PrimaryFailure { get; }

        public ValueTask HandleAsync(
            RemoteWindowMediaAttachment attachment,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(PrimaryFailure);
    }

    private sealed class CleanupFailingMediaStream : MemoryStream
    {
        private int disposeCalls;

        public Exception CleanupFailure { get; } =
            new InvalidOperationException("injected media attachment cleanup failure");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Interlocked.Increment(ref disposeCalls) == 1)
            {
                throw CleanupFailure;
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = utcNow;

        public TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates.Where(timer => timer.IsDue(now)))
            {
                timer.Fire(now);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            TimerCreated.TrySetResult();
            return timer;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    period = newPeriod;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > now)
                    {
                        return;
                    }

                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : now.Add(period);
                }

                callback(state);
            }

            public bool IsDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueAt <= now;
                }
            }
        }
    }
}
