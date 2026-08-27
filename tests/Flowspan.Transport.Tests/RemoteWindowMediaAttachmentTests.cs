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

public sealed class RemoteWindowMediaAttachmentTests
{
    private static readonly DeviceId InitiatorDeviceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ResponderDeviceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ProtocolOnePointSixAttachesAndContinuesMediaOverLoopback()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        RemoteWindowMediaRouteBinding binding = CreateBinding(initiatorSession);
        await using var registry = new RemoteWindowMediaRouteRegistry();
        await using RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(binding, responderSession);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using TcpClient server = await accepting;

        Task<RemoteWindowMediaAttachment> acceptingAttachment =
            registry.AcceptAsync(server.GetStream()).AsTask();
        await using RemoteWindowMediaAttachment initiator =
            await RemoteWindowMediaAttachment.ConnectAsync(
                client.GetStream(),
                binding,
                initiatorSession);
        await using RemoteWindowMediaAttachment responder =
            await acceptingAttachment;
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
        Assert.Equal(binding, responder.Binding);
        Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
        Assert.True(registration.IsAttached);
    }

    [Fact]
    public async Task WrongAcknowledgementBindingDisposesConnectorOwnedSession()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x14);
        using (responderSession)
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        using (var client = new TcpClient())
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiatorSession);
            RemoteWindowMediaRouteBinding wrongBinding =
                RemoteWindowMediaRouteBinding.Create(
                    binding.ProtocolVersion,
                    binding.InitiatorDeviceId,
                    binding.ResponderDeviceId,
                    binding.RouteId,
                    RemoteWindowSessionId.Parse(
                        "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    binding.ActivityId);
            listener.Start(backlog: 1);
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            using TcpClient server = await accepting;
            NetworkStream clientStream = client.GetStream();
            Task responding = SendAcknowledgementAsync(
                server.GetStream(),
                responderSession,
                wrongBinding,
                tamper: false);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await RemoteWindowMediaAttachment.ConnectAsync(
                    clientStream,
                    binding,
                    initiatorSession));
            await responding;

            Assert.False(clientStream.CanRead);
            Assert.Throws<ObjectDisposedException>(() =>
                initiatorSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task TamperedAcknowledgementDisposesConnectorOwnedSession()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x16);
        using (responderSession)
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        using (var client = new TcpClient())
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiatorSession);
            listener.Start(backlog: 1);
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            using TcpClient server = await accepting;
            NetworkStream clientStream = client.GetStream();
            Task responding = SendAcknowledgementAsync(
                server.GetStream(),
                responderSession,
                binding,
                tamper: true);

            await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
                await RemoteWindowMediaAttachment.ConnectAsync(
                    clientStream,
                    binding,
                    initiatorSession));
            await responding;

            Assert.False(clientStream.CanRead);
            Assert.Throws<ObjectDisposedException>(() =>
                initiatorSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task ConnectorCleanupFailurePreservesProtocolFailureAndDisposesSession()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x17);
        using (responderSession)
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        using (var client = new TcpClient())
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiatorSession);
            RemoteWindowMediaRouteBinding wrongBinding =
                RemoteWindowMediaRouteBinding.Create(
                    binding.ProtocolVersion,
                    binding.InitiatorDeviceId,
                    binding.ResponderDeviceId,
                    binding.RouteId,
                    binding.SessionId,
                    ActivityId.Parse(
                        "dddddddd-dddd-dddd-dddd-dddddddddddd"));
            listener.Start(backlog: 1);
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            using TcpClient server = await accepting;
            var connectorStream = new ThrowingDisposeStream(client.GetStream());
            Task responding = SendAcknowledgementAsync(
                server.GetStream(),
                responderSession,
                wrongBinding,
                tamper: false);

            AggregateException failure =
                await Assert.ThrowsAsync<AggregateException>(async () =>
                    await RemoteWindowMediaAttachment.ConnectAsync(
                        connectorStream,
                        binding,
                        initiatorSession));
            await responding;

            Assert.Contains(
                failure.Flatten().InnerExceptions,
                static inner => inner is InvalidDataException);
            Assert.Contains(
                failure.Flatten().InnerExceptions,
                static inner => inner is InvalidOperationException
                    && inner.Message == "stream cleanup failed");
            Assert.Throws<ObjectDisposedException>(() =>
                initiatorSession.Encrypt([0x01]));
        }
    }

    [Theory]
    [InlineData(RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes)]
    [InlineData(RemoteWindowMediaAttachmentCodec.AcknowledgementEnvelopeBytes)]
    public async Task WireWriteKeepsNonCooperativeBorrowedEnvelopeStable(
        int envelopeBytes)
    {
        var stream = new NonCooperativeAttachmentWriteStream();
        byte[] envelope = Enumerable.Repeat((byte)0x5a, envelopeBytes).ToArray();
        using var cancellation = new CancellationTokenSource();
        Task writing = RemoteWindowMediaAttachmentWire.WriteAsync(
                stream,
                envelope,
                cancellation.Token)
            .AsTask();
        await stream.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        CryptographicOperations.ZeroMemory(envelope);
        stream.CompleteWrite();

        Assert.True(stream.PayloadWasStableUntilCompletion);
    }

    [Fact]
    public async Task WireReadDefersZeroingNonCooperativeBorrowedEnvelope()
    {
        var stream = new NonCooperativeAttachmentReadStream(
            RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes);
        using var cancellation = new CancellationTokenSource();
        Task<byte[]> reading = RemoteWindowMediaAttachmentWire.ReadAsync(
                stream,
                RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes,
                cancellation.Token)
            .AsTask();
        await stream.PayloadReadStarted.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        stream.CompleteRead();

        Assert.True(stream.PayloadWasStableUntilCompletion);
    }

    [Fact]
    public async Task MatchingRouteWithUnsupportedEnvelopeIsConsumedAndRevoked()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        {
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x11, 32).ToArray(),
                initiatorSession);
            request[6] = 0x01;
            var candidate = new MemoryStream();

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(1)));

            Assert.Equal(0, registry.Count);
            Assert.False(registration.IsAttached);
            Assert.False(candidate.CanRead);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task OversizedPreReadEnvelopeIsRejectedBeforeRouteLookup()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x18);
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        {
            var candidate = new MemoryStream();
            byte[] oversized = new byte[
                RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes + 1];

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await registry.AcceptAsync(
                    candidate,
                    oversized,
                    TimeSpan.FromSeconds(1)));

            Assert.False(candidate.CanRead);
            Assert.Equal(1, registry.Count);
            Assert.False(registration.IsAttached);
            RemoteWindowMediaRouteId.FromSession(responderSession);
        }
    }

    [Fact]
    public async Task SecondClaimIsRejectedWithoutRevokingAttachedChannel()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        {
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x21, 32).ToArray(),
                initiatorSession);
            await using RemoteWindowMediaAttachment attached =
                await registry.AcceptAsync(
                    new MemoryStream(),
                    request,
                    TimeSpan.FromSeconds(1));
            var replayCandidate = new MemoryStream();

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await registry.AcceptAsync(
                    replayCandidate,
                    request,
                    TimeSpan.FromSeconds(1)));

            Assert.True(registration.IsAttached);
            Assert.Equal(1, registry.Count);
            Assert.False(replayCandidate.CanRead);
        }
    }

    [Fact]
    public async Task RegisterAndRegistryDisposeRaceHasOneTimerAndSessionOwner()
    {
        var time = new ControllableTimeProvider(blockChange: true);
        var registry = new RemoteWindowMediaRouteRegistry(timeProvider: time);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiatorSession);
            Task<RemoteWindowMediaRouteRegistration> registering = Task.Run(() =>
                registry.RegisterOwnedRoute(binding, responderSession));
            await time.ChangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task disposing = Task.Run(async () => await registry.DisposeAsync());
            Assert.False(disposing.IsCompleted);

            time.AllowChange.TrySetResult();
            RemoteWindowMediaRouteRegistration registration = await registering;
            await disposing;
            await registration.DisposeAsync();

            Assert.Equal(0, registry.Count);
            Assert.Equal(1, time.TimerDisposeCount);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task ConcurrentRegistryDisposersJoinInProgressCleanup()
    {
        var registry = new RemoteWindowMediaRouteRegistry();
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x24);
        using (initiatorSession)
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x25, 32).ToArray(),
                initiatorSession);
            var candidate = new BlockingAttachmentWriteStream(
                delayFailureAfterDispose: true);
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Task firstDisposal = registry.DisposeAsync().AsTask();
            await candidate.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));
            Task secondDisposal = registry.DisposeAsync().AsTask();

            Assert.False(firstDisposal.IsCompleted);
            Assert.False(secondDisposal.IsCompleted);
            candidate.AllowFailureAfterDispose.TrySetResult();
            await Task.WhenAll(firstDisposal, secondDisposal)
                .WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
            await registration.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task RevokeAndAcceptCleanupFailuresReachEveryCleanupOwner()
    {
        var time = new ControllableTimeProvider(throwOnTimerDispose: true);
        var registry = new RemoteWindowMediaRouteRegistry(timeProvider: time);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x26);
        using (initiatorSession)
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x27, 32).ToArray(),
                initiatorSession);
            var candidate = new BlockingAttachmentWriteStream(
                delayFailureAfterDispose: true,
                throwOnRepeatedDispose: true);
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Task registrationCleanup = registration.DisposeAsync().AsTask();
            await candidate.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));
            Task registryCleanup = registry.DisposeAsync().AsTask();
            candidate.AllowFailureAfterDispose.TrySetResult();

            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
            Exception registrationFailure =
                await Assert.ThrowsAnyAsync<Exception>(() => registrationCleanup);
            Exception registryFailure =
                await Assert.ThrowsAnyAsync<Exception>(() => registryCleanup);
            AssertCleanupCauses(registrationFailure);
            AssertCleanupCauses(registryFailure);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }

        static void AssertCleanupCauses(Exception failure)
        {
            IReadOnlyCollection<Exception> causes = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [failure];
            Assert.Contains(
                causes,
                static cause => cause.Message == "timer cleanup failed");
            Assert.Contains(
                causes,
                static cause => cause.Message == "candidate cleanup failed");
        }
    }

    [Fact]
    public async Task RevokeAfterAttachmentPublicationJoinsAcceptCleanup()
    {
        var time = new ControllableTimeProvider(
            blockTimerDispose: true,
            throwOnTimerDispose: true);
        var registry = new RemoteWindowMediaRouteRegistry(
            timeProvider: time);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x38);
        using (initiatorSession)
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x39, 32).ToArray(),
                initiatorSession);
            var candidate = new ThrowingDisposeStream(new MemoryStream());
            Task<RemoteWindowMediaAttachment> accepting = Task.Run(async () =>
                await registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2)));
            await time.TimerDisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Task registrationCleanup = registration.DisposeAsync().AsTask();
            try
            {
                Task completed = await Task.WhenAny(
                    registrationCleanup,
                    Task.Delay(TimeSpan.FromMilliseconds(100)));
                Assert.NotSame(registrationCleanup, completed);
            }
            finally
            {
                time.AllowTimerDispose.TrySetResult();
            }

            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
            Exception registrationFailure =
                await Assert.ThrowsAnyAsync<Exception>(() => registrationCleanup);
            IReadOnlyCollection<Exception> causes =
                registrationFailure is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions
                    : [registrationFailure];
            Assert.Contains(
                causes,
                static cause => cause.Message == "timer cleanup failed");
            Assert.Contains(
                causes,
                static cause => cause.Message == "stream cleanup failed");
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));

            Exception registryFailure =
                await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await registry.DisposeAsync());
            IReadOnlyCollection<Exception> registryCauses =
                registryFailure is AggregateException registryAggregate
                    ? registryAggregate.Flatten().InnerExceptions
                    : [registryFailure];
            Assert.Contains(
                registryCauses,
                static cause => cause.Message == "timer cleanup failed");
            Assert.Contains(
                registryCauses,
                static cause => cause.Message == "stream cleanup failed");
        }
    }

    [Fact]
    public async Task RevokingRouteRetainsCapacityAndRegistryDisposeJoins()
    {
        var registry = new RemoteWindowMediaRouteRegistry(maximumRoutes: 1);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions(seed: 0x28);
        (SecureFrameSession rejectedInitiator, SecureFrameSession rejectedResponder) =
            CreateSecureSessions(seed: 0x29);
        using (initiatorSession)
        using (rejectedInitiator)
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x2a, 32).ToArray(),
                initiatorSession);
            var candidate = new BlockingAttachmentWriteStream(
                delayFailureAfterDispose: true);
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Task revoking = registration.DisposeAsync().AsTask();
            await candidate.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(rejectedInitiator),
                    rejectedResponder));
            Task registryCleanup = registry.DisposeAsync().AsTask();

            Assert.False(revoking.IsCompleted);
            Assert.False(registryCleanup.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() =>
                rejectedResponder.Encrypt([0x01]));
            candidate.AllowFailureAfterDispose.TrySetResult();
            await Task.WhenAll(revoking, registryCleanup)
                .WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
        }
    }

    [Fact]
    public async Task RegistryDisposeStartsEveryOwnedRouteBeforeJoiningCleanup()
    {
        var registry = new RemoteWindowMediaRouteRegistry(maximumRoutes: 2);
        (SecureFrameSession firstInitiator, SecureFrameSession firstResponder) =
            CreateSecureSessions(seed: 0x2c);
        (SecureFrameSession secondInitiator, SecureFrameSession secondResponder) =
            CreateSecureSessions(seed: 0x2d);
        using (firstInitiator)
        using (secondInitiator)
        {
            RemoteWindowMediaRouteRegistration firstRegistration =
                registry.RegisterOwnedRoute(
                    CreateBinding(firstInitiator),
                    firstResponder);
            RemoteWindowMediaRouteRegistration secondRegistration =
                registry.RegisterOwnedRoute(
                    CreateBinding(secondInitiator),
                    secondResponder);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                firstRegistration.Binding,
                Enumerable.Repeat((byte)0x2e, 32).ToArray(),
                firstInitiator);
            var candidate = new BlockingAttachmentWriteStream(
                delayFailureAfterDispose: true);
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Task registryCleanup = registry.DisposeAsync().AsTask();
            await candidate.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(registryCleanup.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() =>
                secondResponder.Encrypt([0x01]));
            candidate.AllowFailureAfterDispose.TrySetResult();
            await registryCleanup.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
            await firstRegistration.DisposeAsync();
            await secondRegistration.DisposeAsync();
        }
    }

    [Fact]
    public async Task CleanupCompletionPublishesCapacityRelease()
    {
        await using var registry = new RemoteWindowMediaRouteRegistry(
            maximumRoutes: 1);
        for (var iteration = 0; iteration < 256; iteration++)
        {
            (SecureFrameSession initiator, SecureFrameSession responder) =
                CreateSecureSessions(seed: 30_000 + iteration);
            using (initiator)
            {
                RemoteWindowMediaRouteRegistration registration =
                    registry.RegisterOwnedRoute(
                        CreateBinding(initiator),
                        responder);

                await registration.DisposeAsync();
                Assert.Equal(0, registry.Count);
                Assert.Throws<ObjectDisposedException>(() =>
                    responder.Encrypt([0x01]));
            }
        }
    }

    [Fact]
    public async Task TimerArmFailuresDoNotConsumeRouteHistory()
    {
        var time = new ControllableTimeProvider(
            failedChanges: RemoteWindowMediaRouteRegistry.MaximumRememberedRouteIds);
        await using var registry = new RemoteWindowMediaRouteRegistry(
            maximumRoutes: 1,
            timeProvider: time);
        for (var index = 0;
            index < RemoteWindowMediaRouteRegistry.MaximumRememberedRouteIds;
            index++)
        {
            (SecureFrameSession initiator, SecureFrameSession responder) =
                CreateSecureSessions(seed: 40_000 + index);
            using (initiator)
            {
                InvalidOperationException failure =
                    Assert.Throws<InvalidOperationException>(() =>
                        registry.RegisterOwnedRoute(
                            CreateBinding(initiator),
                            responder));

                Assert.Equal(
                    "The Remote Window media route expiry could not be armed.",
                    failure.Message);
                Assert.Equal(0, registry.Count);
                Assert.Throws<ObjectDisposedException>(() =>
                    responder.Encrypt([0x01]));
            }
        }

        Assert.Equal(
            RemoteWindowMediaRouteRegistry.MaximumRememberedRouteIds,
            time.TimerDisposeCount);
        (SecureFrameSession admittedInitiator, SecureFrameSession admittedResponder) =
            CreateSecureSessions(seed: 41_000);
        using (admittedInitiator)
        await using (RemoteWindowMediaRouteRegistration admitted =
            registry.RegisterOwnedRoute(
                CreateBinding(admittedInitiator),
                admittedResponder))
        {
            Assert.Equal(1, registry.Count);
        }
    }

    [Fact]
    public async Task ConsumedRouteHistoryIsBoundedAndRecoversAfterReplayWindow()
    {
        var time = new ControllableTimeProvider();
        await using var registry = new RemoteWindowMediaRouteRegistry(
            maximumRoutes: 1,
            timeProvider: time);
        for (var index = 0;
            index < RemoteWindowMediaRouteRegistry.MaximumRememberedRouteIds;
            index++)
        {
            (SecureFrameSession initiator, SecureFrameSession responder) =
                CreateSecureSessions(seed: 10_000 + index);
            using (initiator)
            {
                RemoteWindowMediaRouteRegistration registration =
                    registry.RegisterOwnedRoute(
                        CreateBinding(initiator),
                        responder);
                await registration.DisposeAsync();
            }
        }

        (SecureFrameSession blockedInitiator, SecureFrameSession blockedResponder) =
            CreateSecureSessions(seed: 20_000);
        using (blockedInitiator)
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(blockedInitiator),
                    blockedResponder));
            Assert.Throws<ObjectDisposedException>(() =>
                blockedResponder.Encrypt([0x01]));
        }

        time.Advance(RemoteWindowMediaRouteRegistry.MaximumRouteLifetime);
        (SecureFrameSession resumedInitiator, SecureFrameSession resumedResponder) =
            CreateSecureSessions(seed: 20_001);
        using (resumedInitiator)
        await using (RemoteWindowMediaRouteRegistration resumed =
            registry.RegisterOwnedRoute(
                CreateBinding(resumedInitiator),
                resumedResponder))
        {
            Assert.Equal(1, registry.Count);
        }
    }

    [Fact]
    public async Task RejectedConsumedRouteReplayDoesNotEraseTombstone()
    {
        (SecureFrameSession firstInitiator, SecureFrameSession firstResponder) =
            CreateSecureSessions(seed: 45_000);
        (SecureFrameSession firstReplayInitiator,
            SecureFrameSession firstReplayResponder) =
            CreateSecureSessions(seed: 45_000);
        (SecureFrameSession secondReplayInitiator,
            SecureFrameSession secondReplayResponder) =
            CreateSecureSessions(seed: 45_000);
        using (firstInitiator)
        using (firstReplayInitiator)
        using (secondReplayInitiator)
        await using (var registry = new RemoteWindowMediaRouteRegistry(
            maximumRoutes: 1))
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(firstInitiator),
                    firstResponder);
            await registration.DisposeAsync();

            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(firstReplayInitiator),
                    firstReplayResponder));
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(secondReplayInitiator),
                    secondReplayResponder));

            Assert.Equal(0, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                firstReplayResponder.Encrypt([0x01]));
            Assert.Throws<ObjectDisposedException>(() =>
                secondReplayResponder.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task AttachedRouteRetainsHistorySlotUntilCleanupThenHardCapRecovers()
    {
        var time = new ControllableTimeProvider();
        await using var registry = new RemoteWindowMediaRouteRegistry(
            maximumRoutes: 2,
            timeProvider: time);
        (SecureFrameSession activeInitiator, SecureFrameSession activeResponder) =
            CreateSecureSessions(seed: 50_000);
        using (activeInitiator)
        {
            RemoteWindowMediaRouteRegistration activeRegistration =
                registry.RegisterOwnedRoute(
                    CreateBinding(activeInitiator),
                    activeResponder);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                activeRegistration.Binding,
                Enumerable.Repeat((byte)0x5a, 32).ToArray(),
                activeInitiator);
            RemoteWindowMediaAttachment activeAttachment =
                await registry.AcceptAsync(
                    new MemoryStream(),
                    request,
                    TimeSpan.FromSeconds(2));
            Assert.True(activeRegistration.IsAttached);
            time.Advance(RemoteWindowMediaRouteRegistry.MaximumRouteLifetime);

            for (var index = 0;
                index < RemoteWindowMediaRouteRegistry.MaximumRememberedRouteIds - 1;
                index++)
            {
                (SecureFrameSession initiator, SecureFrameSession responder) =
                    CreateSecureSessions(seed: 51_000 + index);
                using (initiator)
                {
                    RemoteWindowMediaRouteRegistration registration =
                        registry.RegisterOwnedRoute(
                            CreateBinding(initiator),
                            responder);
                    await registration.DisposeAsync();
                }
            }

            (SecureFrameSession blockedInitiator, SecureFrameSession blockedResponder) =
                CreateSecureSessions(seed: 52_000);
            using (blockedInitiator)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    registry.RegisterOwnedRoute(
                        CreateBinding(blockedInitiator),
                        blockedResponder));
                Assert.Throws<ObjectDisposedException>(() =>
                    blockedResponder.Encrypt([0x01]));
            }

            await activeRegistration.DisposeAsync();
            await activeAttachment.DisposeAsync();
            Assert.Equal(0, registry.Count);

            (SecureFrameSession resumedInitiator,
                SecureFrameSession resumedResponder) =
                CreateSecureSessions(seed: 52_001);
            using (resumedInitiator)
            await using (RemoteWindowMediaRouteRegistration resumed =
                registry.RegisterOwnedRoute(
                    CreateBinding(resumedInitiator),
                    resumedResponder))
            {
                Assert.Equal(1, registry.Count);
            }
        }
    }

    [Fact]
    public async Task ExpiryRevokesPendingRouteAndRegistrationJoinsCleanup()
    {
        var time = new ControllableTimeProvider();
        await using var registry = new RemoteWindowMediaRouteRegistry(
            timeProvider: time);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession,
                    TimeSpan.FromSeconds(1));

            time.Advance(TimeSpan.FromSeconds(1));
            await registration.DisposeAsync();

            Assert.Equal(0, registry.Count);
            Assert.Equal(1, time.TimerDisposeCount);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task TimerCleanupFailureRemainsObservableToRegistrationAndRegistryOwners()
    {
        var time = new ControllableTimeProvider(throwOnTimerDispose: true);
        var registry = new RemoteWindowMediaRouteRegistry(
            timeProvider: time);
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession);

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await registration.DisposeAsync());

            Assert.Equal("timer cleanup failed", failure.Message);
            Assert.Equal(0, registry.Count);
            Assert.Equal(1, time.TimerDisposeCount);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));

            Exception registryFailure =
                await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await registry.DisposeAsync());
            IReadOnlyCollection<Exception> registryCauses =
                registryFailure is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions
                    : [registryFailure];
            Assert.Contains(
                registryCauses,
                static cause => cause.Message == "timer cleanup failed");
        }
    }

    [Fact]
    public async Task RevokeDuringAcknowledgementWriteDrainsBeforeReturning()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        {
            RemoteWindowMediaRouteRegistration registration =
                registry.RegisterOwnedRoute(
                    CreateBinding(initiatorSession),
                    responderSession);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x31, 32).ToArray(),
                initiatorSession);
            var candidate = new BlockingAttachmentWriteStream();
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Task revoking = registration.DisposeAsync().AsTask();
            await revoking.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => accepting);

            Assert.True(candidate.IsDisposed);
            Assert.True(candidate.PayloadWasStableUntilDispose);
            Assert.Equal(0, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task ConsumedRouteCannotBeRepublishedDuringOrAfterCleanup()
    {
        (SecureFrameSession firstInitiator, SecureFrameSession firstResponder) =
            CreateSecureSessions(seed: 0x34);
        (SecureFrameSession concurrentReuseInitiator,
            SecureFrameSession concurrentReuseResponder) =
            CreateSecureSessions(seed: 0x34);
        (SecureFrameSession laterReuseInitiator,
            SecureFrameSession laterReuseResponder) =
            CreateSecureSessions(seed: 0x34);
        (SecureFrameSession freshInitiator, SecureFrameSession freshResponder) =
            CreateSecureSessions(seed: 0x37);
        using (firstInitiator)
        using (concurrentReuseInitiator)
        using (laterReuseInitiator)
        using (freshInitiator)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        {
            RemoteWindowMediaRouteRegistration firstRegistration =
                registry.RegisterOwnedRoute(
                    CreateBinding(firstInitiator),
                    firstResponder);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                firstRegistration.Binding,
                Enumerable.Repeat((byte)0x35, 32).ToArray(),
                firstInitiator);
            var candidate = new BlockingAttachmentWriteStream(
                delayFailureAfterDispose: true);
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            Task revoking = firstRegistration.DisposeAsync().AsTask();
            await candidate.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(concurrentReuseInitiator),
                    concurrentReuseResponder));
            candidate.AllowFailureAfterDispose.TrySetResult();

            await revoking.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
            await firstRegistration.DisposeAsync();
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(laterReuseInitiator),
                    laterReuseResponder));
            await using RemoteWindowMediaRouteRegistration freshRegistration =
                registry.RegisterOwnedRoute(
                    CreateBinding(freshInitiator),
                    freshResponder);
            byte[] freshRequest =
                RemoteWindowMediaAttachmentCodec.EncodeRequest(
                    freshRegistration.Binding,
                    Enumerable.Repeat((byte)0x36, 32).ToArray(),
                    freshInitiator);
            await using RemoteWindowMediaAttachment freshAttachment =
                await registry.AcceptAsync(
                    new MemoryStream(),
                    freshRequest,
                    TimeSpan.FromSeconds(1));

            Assert.Equal(1, registry.Count);
            Assert.True(freshRegistration.IsAttached);
            Assert.Throws<ObjectDisposedException>(() =>
                concurrentReuseResponder.Encrypt([0x01]));
            Assert.Throws<ObjectDisposedException>(() =>
                laterReuseResponder.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task CancellationDuringAcknowledgementWriteConsumesRoute()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        using (var cancellation = new CancellationTokenSource())
        {
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x32, 32).ToArray(),
                initiatorSession);
            var candidate = new BlockingAttachmentWriteStream();
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(2),
                    cancellation.Token)
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accepting);

            Assert.True(candidate.IsDisposed);
            Assert.Equal(0, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task HandshakeTimeoutDuringAcknowledgementWriteConsumesRoute()
    {
        var time = new ControllableTimeProvider();
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry(
            timeProvider: time))
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        {
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x33, 32).ToArray(),
                initiatorSession);
            var candidate = new BlockingAttachmentWriteStream();
            Task<RemoteWindowMediaAttachment> accepting = registry.AcceptAsync(
                    candidate,
                    request,
                    TimeSpan.FromSeconds(1))
                .AsTask();
            await candidate.PayloadWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

            time.Advance(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<TimeoutException>(() => accepting);

            Assert.True(candidate.IsDisposed);
            Assert.Equal(0, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("initiator")]
    [InlineData("responder")]
    [InlineData("protocol")]
    [InlineData("session")]
    [InlineData("activity")]
    public async Task WrongProtectedBindingConsumesMatchedRoute(string mismatch)
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        {
            RemoteWindowMediaRouteBinding expected = registration.Binding;
            RemoteWindowMediaRouteBinding wrongBinding =
                RemoteWindowMediaRouteBinding.Create(
                    mismatch == "protocol"
                        ? new ProtocolVersion(1, 7)
                        : expected.ProtocolVersion,
                    mismatch switch
                    {
                        "direction" => expected.ResponderDeviceId,
                        "initiator" => DeviceId.Parse(
                            "44444444-4444-4444-4444-444444444444"),
                        _ => expected.InitiatorDeviceId,
                    },
                    mismatch switch
                    {
                        "direction" => expected.InitiatorDeviceId,
                        "responder" => DeviceId.Parse(
                            "33333333-3333-3333-3333-333333333333"),
                        _ => expected.ResponderDeviceId,
                    },
                    expected.RouteId,
                    mismatch == "session"
                        ? RemoteWindowSessionId.Parse(
                            "cccccccc-cccc-cccc-cccc-cccccccccccc")
                        : expected.SessionId,
                    mismatch == "activity"
                        ? ActivityId.Parse(
                            "dddddddd-dddd-dddd-dddd-dddddddddddd")
                        : expected.ActivityId);
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                wrongBinding,
                Enumerable.Repeat((byte)0x41, 32).ToArray(),
                initiatorSession);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await registry.AcceptAsync(
                    new MemoryStream(),
                    request,
                    TimeSpan.FromSeconds(1)));

            Assert.Equal(0, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task TamperedProtectedRequestConsumesMatchedRoute()
    {
        (SecureFrameSession initiatorSession, SecureFrameSession responderSession) =
            CreateSecureSessions();
        using (initiatorSession)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(initiatorSession),
                responderSession))
        {
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x42, 32).ToArray(),
                initiatorSession);
            request[^1] ^= 0x01;

            await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
                await registry.AcceptAsync(
                    new MemoryStream(),
                    request,
                    TimeSpan.FromSeconds(1)));

            Assert.Equal(0, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                responderSession.Encrypt([0x01]));
        }
    }

    [Fact]
    public async Task ReusedInitiatorNonceOnAnotherRouteIsRejected()
    {
        (SecureFrameSession firstInitiator, SecureFrameSession firstResponder) =
            CreateSecureSessions(seed: 0x51);
        (SecureFrameSession secondInitiator, SecureFrameSession secondResponder) =
            CreateSecureSessions(seed: 0x52);
        using (firstInitiator)
        using (secondInitiator)
        await using (var registry = new RemoteWindowMediaRouteRegistry())
        await using (RemoteWindowMediaRouteRegistration firstRegistration =
            registry.RegisterOwnedRoute(
                CreateBinding(firstInitiator),
                firstResponder))
        await using (RemoteWindowMediaRouteRegistration secondRegistration =
            registry.RegisterOwnedRoute(
                CreateBinding(secondInitiator),
                secondResponder))
        {
            byte[] repeatedNonce = Enumerable.Repeat((byte)0x53, 32).ToArray();
            byte[] firstRequest = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                firstRegistration.Binding,
                repeatedNonce,
                firstInitiator);
            byte[] secondRequest = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                secondRegistration.Binding,
                repeatedNonce,
                secondInitiator);
            await using RemoteWindowMediaAttachment firstAttachment =
                await registry.AcceptAsync(
                    new MemoryStream(),
                    firstRequest,
                    TimeSpan.FromSeconds(1));

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await registry.AcceptAsync(
                    new MemoryStream(),
                    secondRequest,
                    TimeSpan.FromSeconds(1)));

            Assert.True(firstRegistration.IsAttached);
            Assert.False(secondRegistration.IsAttached);
            Assert.Equal(1, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                secondResponder.Encrypt([0x01]));
        }
    }

    [Fact]
    public void InitiatorNonceCacheFailsClosedAtBoundAndRecoversAfterWindow()
    {
        var cache = new RemoteWindowMediaNonceReplayCache(
            RemoteWindowMediaRouteRegistry.MaximumRememberedNonces,
            RemoteWindowMediaRouteRegistry.MaximumRouteLifetime);
        var now = new DateTimeOffset(
            2026,
            8,
            20,
            12,
            0,
            0,
            TimeSpan.Zero);
        for (var index = 0;
            index < RemoteWindowMediaRouteRegistry.MaximumRememberedNonces;
            index++)
        {
            Assert.True(cache.TryRemember(CreateNonce(index), now));
        }

        byte[] blockedNonce = CreateNonce(
            RemoteWindowMediaRouteRegistry.MaximumRememberedNonces);
        Assert.Equal(
            RemoteWindowMediaRouteRegistry.MaximumRememberedNonces,
            cache.Count);
        Assert.False(cache.TryRemember(blockedNonce, now));
        Assert.False(cache.TryRemember(
            CreateNonce(0),
            now + RemoteWindowMediaRouteRegistry.MaximumRouteLifetime
                - TimeSpan.FromTicks(1)));

        Assert.True(cache.TryRemember(
            blockedNonce,
            now + RemoteWindowMediaRouteRegistry.MaximumRouteLifetime));
        Assert.Equal(1, cache.Count);

        static byte[] CreateNonce(int value)
        {
            byte[] nonce = new byte[RemoteWindowMediaAttachmentCodec.NonceBytes];
            BinaryPrimitives.WriteInt32LittleEndian(nonce, value);
            return nonce;
        }
    }

    [Fact]
    public async Task CapacityAndBindingAdmissionFailuresDisposeOwnedSessions()
    {
        (SecureFrameSession firstInitiator, SecureFrameSession firstResponder) =
            CreateSecureSessions(seed: 0x61);
        (SecureFrameSession secondInitiator, SecureFrameSession secondResponder) =
            CreateSecureSessions(seed: 0x62);
        (SecureFrameSession thirdInitiator, SecureFrameSession thirdResponder) =
            CreateSecureSessions(seed: 0x63);
        using (firstInitiator)
        using (secondInitiator)
        using (thirdInitiator)
        await using (var registry = new RemoteWindowMediaRouteRegistry(
            maximumRoutes: 1))
        await using (RemoteWindowMediaRouteRegistration registration =
            registry.RegisterOwnedRoute(
                CreateBinding(firstInitiator),
                firstResponder))
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(secondInitiator),
                    secondResponder));
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterOwnedRoute(
                    CreateBinding(firstInitiator),
                    thirdResponder));

            Assert.Equal(1, registry.Count);
            Assert.Throws<ObjectDisposedException>(() =>
                secondResponder.Encrypt([0x01]));
            Assert.Throws<ObjectDisposedException>(() =>
                thirdResponder.Encrypt([0x01]));
            Assert.True(registration.Binding.RouteId.Equals(
                RemoteWindowMediaRouteId.FromSession(firstResponder)));
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                registration.Binding,
                Enumerable.Repeat((byte)0x64, 32).ToArray(),
                firstInitiator);
            await using RemoteWindowMediaAttachment attachment =
                await registry.AcceptAsync(
                    new MemoryStream(),
                    request,
                    TimeSpan.FromSeconds(1));
            Assert.True(registration.IsAttached);
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
        CreateSecureSessions(int seed = 0x44)
    {
        byte[] secret = SHA256.HashData(BitConverter.GetBytes(seed));
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes($"media-attachment-loopback-{seed:x8}"));
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

    private static async Task SendAcknowledgementAsync(
        Stream stream,
        SecureFrameSession responderSession,
        RemoteWindowMediaRouteBinding acknowledgementBinding,
        bool tamper)
    {
        byte[] requestEnvelope =
            await RemoteWindowMediaAttachmentWire.ReadAsync(
                stream,
                RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes,
                CancellationToken.None);
        byte[]? initiatorNonce = null;
        byte[]? acknowledgement = null;
        try
        {
            RemoteWindowMediaAttachmentRequest request =
                RemoteWindowMediaAttachmentCodec.DecodeRequest(
                    requestEnvelope,
                    responderSession);
            initiatorNonce = request.ExportInitiatorNonce();
            byte[] responderNonce = Enumerable.Repeat((byte)0x15, 32).ToArray();
            try
            {
                acknowledgement =
                    RemoteWindowMediaAttachmentCodec.EncodeAcknowledgement(
                        acknowledgementBinding,
                        initiatorNonce,
                        responderNonce,
                        responderSession);
                if (tamper)
                {
                    acknowledgement[^1] ^= 0x01;
                }

                await RemoteWindowMediaAttachmentWire.WriteAsync(
                    stream,
                    acknowledgement,
                    CancellationToken.None);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(responderNonce);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(requestEnvelope);
            if (initiatorNonce is not null)
            {
                CryptographicOperations.ZeroMemory(initiatorNonce);
            }

            if (acknowledgement is not null)
            {
                CryptographicOperations.ZeroMemory(acknowledgement);
            }
        }
    }

    private sealed class ControllableTimeProvider : TimeProvider
    {
        private readonly bool blockChange;
        private readonly bool blockTimerDispose;
        private int failedChangesRemaining;
        private readonly Lock gate = new();
        private readonly bool throwOnTimerDispose;
        private ControllableTimer? timer;
        private DateTimeOffset utcNow =
            new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        public ControllableTimeProvider(
            bool blockChange = false,
            int failedChanges = 0,
            bool blockTimerDispose = false,
            bool throwOnTimerDispose = false)
        {
            this.blockChange = blockChange;
            failedChangesRemaining = failedChanges;
            this.blockTimerDispose = blockTimerDispose;
            this.throwOnTimerDispose = throwOnTimerDispose;
        }

        public TaskCompletionSource AllowChange { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ChangeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowTimerDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TimerDisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int TimerDisposeCount { get; private set; }

        public void Advance(TimeSpan elapsed)
        {
            ControllableTimer? current;
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
            var created = new ControllableTimer(this, callback, state);
            lock (gate)
            {
                timer = created;
            }

            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                created.Change(dueTime, period);
            }

            return created;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ControllableTimer(
            ControllableTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _ = period;
                if (owner.blockChange)
                {
                    owner.ChangeStarted.TrySetResult();
                    owner.AllowChange.Task.GetAwaiter().GetResult();
                }

                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    if (owner.failedChangesRemaining > 0)
                    {
                        owner.failedChangesRemaining--;
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
                    if (disposed)
                    {
                        return;
                    }

                    disposed = true;
                    owner.TimerDisposeCount++;
                    if (ReferenceEquals(owner.timer, this))
                    {
                        owner.timer = null;
                    }
                }

                owner.TimerDisposeStarted.TrySetResult();
                if (owner.blockTimerDispose)
                {
                    owner.AllowTimerDispose.Task.GetAwaiter().GetResult();
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

    private sealed class BlockingAttachmentWriteStream : Stream
    {
        private readonly bool delayFailureAfterDispose;
        private readonly bool throwOnRepeatedDispose;
        private readonly TaskCompletionSource disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? payloadCopy;
        private ReadOnlyMemory<byte> pendingPayload;
        private int disposeCalls;
        private int writes;

        public BlockingAttachmentWriteStream(
            bool delayFailureAfterDispose = false,
            bool throwOnRepeatedDispose = false)
        {
            this.delayFailureAfterDispose = delayFailureAfterDispose;
            this.throwOnRepeatedDispose = throwOnRepeatedDispose;
        }

        public TaskCompletionSource AllowFailureAfterDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => !IsDisposed;

        public override bool CanSeek => false;

        public override bool CanWrite => !IsDisposed;

        public bool IsDisposed { get; private set; }

        public Task DisposeStarted => disposeStarted.Task;

        public Task PayloadWriteStarted { get; private set; } = Task.CompletedTask;

        public bool PayloadWasStableUntilDispose { get; private set; }

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

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
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (Interlocked.Increment(ref writes) == 1)
            {
                return ValueTask.CompletedTask;
            }

            pendingPayload = buffer;
            payloadCopy = buffer.ToArray();
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            PayloadWriteStarted = started.Task;
            started.TrySetResult();
            return new ValueTask(WaitForDisposeAsync(cancellationToken));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Increment(ref disposeCalls) > 1
                && throwOnRepeatedDispose)
            {
                throw new InvalidOperationException("candidate cleanup failed");
            }

            if (disposing && !IsDisposed)
            {
                PayloadWasStableUntilDispose = payloadCopy is not null
                    && pendingPayload.Span.SequenceEqual(payloadCopy);
                IsDisposed = true;
                disposeStarted.TrySetResult();
                disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }

        private async Task WaitForDisposeAsync(CancellationToken cancellationToken)
        {
            await disposed.Task.WaitAsync(cancellationToken);
            if (delayFailureAfterDispose)
            {
                await AllowFailureAfterDispose.Task.WaitAsync(cancellationToken);
            }

            throw new IOException("candidate stream closed");
        }
    }

    private sealed class ThrowingDisposeStream(Stream inner) : Stream
    {
        private bool disposed;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                inner.Dispose();
                throw new InvalidOperationException("stream cleanup failed");
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NonCooperativeAttachmentWriteStream : Stream
    {
        private readonly TaskCompletionSource writeCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? payloadCopy;
        private ReadOnlyMemory<byte> pendingPayload;
        private int writes;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public bool PayloadWasStableUntilCompletion { get; private set; }

        public Task PayloadWriteStarted { get; private set; } = Task.CompletedTask;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CompleteWrite()
        {
            PayloadWasStableUntilCompletion = payloadCopy is not null
                && pendingPayload.Span.SequenceEqual(payloadCopy);
            writeCompletion.TrySetResult();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (Interlocked.Increment(ref writes) == 1)
            {
                return ValueTask.CompletedTask;
            }

            pendingPayload = buffer;
            payloadCopy = buffer.ToArray();
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            PayloadWriteStarted = started.Task;
            started.TrySetResult();
            return new ValueTask(writeCompletion.Task);
        }
    }

    private sealed class NonCooperativeAttachmentReadStream(int payloadBytes) : Stream
    {
        private readonly TaskCompletionSource<int> readCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? payloadCopy;
        private Memory<byte> pendingPayload;
        private int reads;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public Task PayloadReadStarted { get; private set; } = Task.CompletedTask;

        public bool PayloadWasStableUntilCompletion { get; private set; }

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CompleteRead()
        {
            PayloadWasStableUntilCompletion = payloadCopy is not null
                && pendingPayload.Span.SequenceEqual(payloadCopy);
            readCompletion.TrySetResult(pendingPayload.Length);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (Interlocked.Increment(ref reads) == 1)
            {
                BinaryPrimitives.WriteInt32BigEndian(buffer.Span, payloadBytes);
                return new ValueTask<int>(buffer.Length);
            }

            buffer.Span.Fill(0x6b);
            pendingPayload = buffer;
            payloadCopy = buffer.ToArray();
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            PayloadReadStarted = started.Task;
            started.TrySetResult();
            return new ValueTask<int>(readCompletion.Task);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
