using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedRemoteWindowMediaLoopbackTests
{
    private const ulong TestPlaintextBudget = 220;
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly DeviceId InitiatorId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DeviceId ResponderId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly ActivityId ActivityId =
        ActivityId.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly CapabilityGrant Capabilities =
        CapabilityGrant.Of(Capability.ActivityReceive, Capability.MirrorView);

    [Fact]
    public async Task ProtocolOnePointSixOwnsSamePortMediaUntilControlCloses()
    {
        ProtocolVersion version = ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using DeviceIdentity initiatorIdentity =
            DeviceIdentity.Generate(InitiatorId, "Initiator");
        using DeviceIdentity responderIdentity =
            DeviceIdentity.Generate(ResponderId, "Responder");
        var responderTrustStore = new InMemoryTrustStore();
        responderTrustStore.Register(new TrustRecord(
            initiatorIdentity.PublicIdentity,
            Now,
            Capabilities));
        await using var responderTrustSessions =
            new TrustSessionCoordinator(responderTrustStore);
        await using var responderMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var responderHandler = CreateHandler(ResponderId, responderMedia);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = new FlowspanTcpInboundListener(
            socket,
            responderIdentity,
            new PairingCeremonyProfile([version], TimeSpan.FromSeconds(2)),
            new RejectingPairingDecisionSource(),
            responderTrustSessions,
            new FlowspanTcpInboundProfile(new AuthenticatedInboundSessionProfile(
                CapabilityGrant.Of(Capability.ActivityReceive),
                [version],
                maximumConcurrentSessions: 2,
                handshakeTimeout: TimeSpan.FromSeconds(2))),
            responderHandler,
            remoteWindowMediaSessions: responderMedia);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();

        await using var initiatorMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var initiatorHandler = CreateHandler(InitiatorId, initiatorMedia);
        await using AuthenticatedTcpControlConnection initiatorConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                new TrustRecord(
                    responderIdentity.PublicIdentity,
                    Now,
                    Capabilities),
                [version],
                cancellationToken: deadline.Token);
        Task initiatorRun = initiatorHandler
            .RunAsync(initiatorConnection, deadline.Token)
            .AsTask();
        AuthenticatedRemoteWindowMediaSession initiatorSession =
            await WaitForSessionAsync(initiatorMedia, ResponderId, deadline.Token);
        AuthenticatedRemoteWindowMediaSession responderSession =
            await WaitForSessionAsync(responderMedia, InitiatorId, deadline.Token);
        RemoteWindowMediaRouteBinding route = responderSession.PrepareResponderRoute(
            SessionId,
            ActivityId);
        using var mediaClient = new TcpClient(AddressFamily.InterNetwork);
        await mediaClient.ConnectAsync(endpoint, deadline.Token);
        var recordingStream = new PrefixRecordingStream(
            mediaClient.GetStream(),
            sizeof(int) + RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes);

        await initiatorSession.ConnectInitiatorAsync(
            recordingStream,
            SessionId,
            ActivityId,
            deadline.Token);
        await responderSession.WaitForAttachmentAsync(deadline.Token);
        Assert.Equal(route, initiatorSession.Binding);
        Assert.Equal(route, responderSession.Binding);
        using RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x10, 0x20, 0x30, 0x40]);
        Task<RemoteWindowMediaFrame> receiving = responderSession
            .ReceiveAsync(deadline.Token)
            .AsTask();

        await initiatorSession.SendAsync(expected, deadline.Token);
        using RemoteWindowMediaFrame actual = await receiving;

        Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
        byte[] oldRequest = recordingStream.Snapshot();
        Assert.Equal(
            sizeof(int) + RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes,
            oldRequest.Length);

        await initiatorHandler.DisposeAsync();
        await ObserveControlStopAsync(initiatorRun);
        await WaitForSessionRemovalAsync(
            responderMedia,
            InitiatorId,
            deadline.Token);
        Assert.False(initiatorSession.IsCurrent);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            initiatorSession.SendAsync(expected, deadline.Token).AsTask());

        var rejected = new TaskCompletionSource<InboundConnectionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionFaulted += failure =>
        {
            if (failure.Stage == InboundConnectionFailureStage.MediaAttachment)
            {
                rejected.TrySetResult(failure);
            }
        };
        try
        {
            using var replayClient = new TcpClient(AddressFamily.InterNetwork);
            await replayClient.ConnectAsync(endpoint, deadline.Token);
            await replayClient.GetStream().WriteAsync(oldRequest, deadline.Token);
            await replayClient.GetStream().FlushAsync(deadline.Token);

            InboundConnectionFailure rejection = await rejected.Task.WaitAsync(
                deadline.Token);
            Assert.Equal(InboundConnectionFailureStage.MediaAttachment, rejection.Stage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldRequest);
        }

        listenerStop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenerRun);
    }

    [Theory]
    [InlineData(
        MediaBudgetBoundary.FrameCount,
        MediaDirection.InitiatorToResponder)]
    [InlineData(
        MediaBudgetBoundary.FrameCount,
        MediaDirection.ResponderToInitiator)]
    [InlineData(
        MediaBudgetBoundary.PlaintextBytes,
        MediaDirection.InitiatorToResponder)]
    [InlineData(
        MediaBudgetBoundary.PlaintextBytes,
        MediaDirection.ResponderToInitiator)]
    public async Task BudgetExhaustionRequiresFreshControlHandshakeAndMediaRoute(
        MediaBudgetBoundary budgetBoundary,
        MediaDirection direction)
    {
        ProtocolVersion version = ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion;
        var mediaLimits = new SecureFrameSessionUsageLimits(
            budgetBoundary == MediaBudgetBoundary.FrameCount
                ? 2
                : SecureFrameSession.MaximumFramesPerEpoch,
            budgetBoundary == MediaBudgetBoundary.PlaintextBytes
                ? TestPlaintextBudget
                : SecureFrameSession.MaximumPlaintextBytesPerEpoch);
        bool initiatorSends = direction == MediaDirection.InitiatorToResponder;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using DeviceIdentity initiatorIdentity =
            DeviceIdentity.Generate(InitiatorId, "Initiator");
        using DeviceIdentity responderIdentity =
            DeviceIdentity.Generate(ResponderId, "Responder");
        var responderTrustStore = new InMemoryTrustStore();
        responderTrustStore.Register(new TrustRecord(
            initiatorIdentity.PublicIdentity,
            Now,
            Capabilities));
        await using var responderTrustSessions =
            new TrustSessionCoordinator(responderTrustStore);
        await using var responderMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory();
        await using var responderHandler = CreateHandler(ResponderId, responderMedia);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(backlog: 8);
        var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
        var listener = new FlowspanTcpInboundListener(
            socket,
            responderIdentity,
            new PairingCeremonyProfile([version], TimeSpan.FromSeconds(2)),
            new RejectingPairingDecisionSource(),
            responderTrustSessions,
            new FlowspanTcpInboundProfile(new AuthenticatedInboundSessionProfile(
                CapabilityGrant.Of(Capability.ActivityReceive),
                [version],
                // Directory removal precedes release of the accepted-session listener slot.
                maximumConcurrentSessions: 2,
                handshakeTimeout: TimeSpan.FromSeconds(2))),
            responderHandler,
            responderMedia,
            mediaLimits);
        using var listenerStop = new CancellationTokenSource();
        Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
        byte[]? oldRequest = null;

        try
        {
            await using var initiatorMedia =
                new AuthenticatedRemoteWindowMediaSessionDirectory();
            await using var initiatorHandler = CreateHandler(InitiatorId, initiatorMedia);
            RemoteWindowMediaRouteBinding firstRoute;
            await using (AuthenticatedTcpControlConnection firstConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    initiatorIdentity,
                    new TrustRecord(
                        responderIdentity.PublicIdentity,
                        Now,
                        Capabilities),
                    [version],
                    mediaLimits,
                    deadline.Token))
            {
                Task firstControl = initiatorHandler
                    .RunAsync(firstConnection, deadline.Token)
                    .AsTask();
                AuthenticatedRemoteWindowMediaSession firstInitiatorSession =
                    await WaitForSessionAsync(
                        initiatorMedia,
                        ResponderId,
                        deadline.Token);
                AuthenticatedRemoteWindowMediaSession firstResponderSession =
                    await WaitForSessionAsync(
                        responderMedia,
                        InitiatorId,
                        deadline.Token);
                AuthenticatedRemoteWindowMediaSession firstSender = initiatorSends
                    ? firstInitiatorSession
                    : firstResponderSession;
                AuthenticatedRemoteWindowMediaSession firstReceiver = initiatorSends
                    ? firstResponderSession
                    : firstInitiatorSession;
                firstRoute = firstResponderSession.PrepareResponderRoute(
                    SessionId,
                    ActivityId);
                Assert.Equal(0, initiatorMedia.Routes.Count);
                Assert.Equal(1, responderMedia.Routes.Count);
                using var firstMediaClient = new TcpClient(AddressFamily.InterNetwork);
                await firstMediaClient.ConnectAsync(endpoint, deadline.Token);
                var firstStream = new PrefixRecordingStream(
                    firstMediaClient.GetStream(),
                    sizeof(int) + RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes);
                await firstInitiatorSession.ConnectInitiatorAsync(
                    firstStream,
                    SessionId,
                    ActivityId,
                    deadline.Token);
                await firstResponderSession.WaitForAttachmentAsync(deadline.Token);

                using RemoteWindowMediaFrame firstFrame = CreateVideoFrame(sequence: 1);
                Task<RemoteWindowMediaFrame> firstReceive = firstReceiver
                    .ReceiveAsync(deadline.Token)
                    .AsTask();
                await firstSender.SendAsync(firstFrame, deadline.Token);
                using RemoteWindowMediaFrame firstReceived = await firstReceive;
                Assert.Equal(firstFrame.ExportPayload(), firstReceived.ExportPayload());

                oldRequest = firstStream.Snapshot();
                using RemoteWindowMediaFrame exhaustedFrame = CreateVideoFrame(sequence: 2);
                long wireBytesBeforeRejection = initiatorSends
                    ? firstStream.BytesWritten
                    : firstStream.BytesRead;
                Task<RemoteWindowMediaFrame> rejectedReceive = firstReceiver
                    .ReceiveAsync(deadline.Token)
                    .AsTask();
                await Assert.ThrowsAsync<CryptographicException>(() =>
                    firstSender
                        .SendAsync(exhaustedFrame, deadline.Token)
                        .AsTask());
                await Assert.ThrowsAnyAsync<IOException>(() => rejectedReceive);
                Assert.Equal(
                    wireBytesBeforeRejection,
                    initiatorSends
                        ? firstStream.BytesWritten
                        : firstStream.BytesRead);
                await ObserveControlStopAsync(firstControl);
                await WaitForSessionRemovalAsync(
                    initiatorMedia,
                    ResponderId,
                    deadline.Token);
                await WaitForSessionRemovalAsync(
                    responderMedia,
                    InitiatorId,
                    deadline.Token);
                await WaitForControlSessionRemovalAsync(
                    initiatorHandler,
                    ResponderId,
                    deadline.Token);
                await WaitForControlSessionRemovalAsync(
                    responderHandler,
                    InitiatorId,
                    deadline.Token);
                Assert.False(firstInitiatorSession.IsCurrent);
                Assert.False(firstResponderSession.IsCurrent);
                Assert.False(firstInitiatorSession.IsAttached);
                Assert.False(firstResponderSession.IsAttached);
                Assert.Equal(0, initiatorMedia.Routes.Count);
                Assert.Equal(0, responderMedia.Routes.Count);
                Assert.False(initiatorHandler.TryGetChannel(ResponderId, out _));
                Assert.False(responderHandler.TryGetChannel(InitiatorId, out _));
                Assert.False(initiatorHandler.TryGetRemoteWindowChannel(
                    ResponderId,
                    out _));
                Assert.False(responderHandler.TryGetRemoteWindowChannel(
                    InitiatorId,
                    out _));
                await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    firstSender.SendAsync(exhaustedFrame, deadline.Token).AsTask());
            }

            Assert.NotNull(oldRequest);
            Assert.Equal(
                sizeof(int) + RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes,
                oldRequest.Length);
            await using (AuthenticatedTcpControlConnection secondConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    initiatorIdentity,
                    new TrustRecord(
                        responderIdentity.PublicIdentity,
                        Now,
                        Capabilities),
                    [version],
                    mediaLimits,
                    deadline.Token))
            {
                Task secondControl = initiatorHandler
                    .RunAsync(secondConnection, deadline.Token)
                    .AsTask();
                AuthenticatedRemoteWindowMediaSession secondInitiatorSession =
                    await WaitForSessionAsync(
                        initiatorMedia,
                        ResponderId,
                        deadline.Token);
                AuthenticatedRemoteWindowMediaSession secondResponderSession =
                    await WaitForSessionAsync(
                        responderMedia,
                        InitiatorId,
                        deadline.Token);
                RemoteWindowMediaRouteBinding secondRoute =
                    secondResponderSession.PrepareResponderRoute(
                        SessionId,
                        ActivityId);
                Assert.NotEqual(firstRoute.RouteId, secondRoute.RouteId);
                Assert.Equal(0, initiatorMedia.Routes.Count);
                Assert.Equal(1, responderMedia.Routes.Count);
                var replayRejected =
                    new TaskCompletionSource<InboundConnectionFailure>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                void OnConnectionFaulted(InboundConnectionFailure failure)
                {
                    if (failure.Stage == InboundConnectionFailureStage.MediaAttachment)
                    {
                        replayRejected.TrySetResult(failure);
                    }
                }

                listener.ConnectionFaulted += OnConnectionFaulted;
                try
                {
                    using var replayClient = new TcpClient(AddressFamily.InterNetwork);
                    await replayClient.ConnectAsync(endpoint, deadline.Token);
                    await replayClient.GetStream().WriteAsync(
                        oldRequest,
                        deadline.Token);
                    await replayClient.GetStream().FlushAsync(deadline.Token);
                    InboundConnectionFailure rejection =
                        await replayRejected.Task.WaitAsync(deadline.Token);
                    Assert.Equal(
                        InboundConnectionFailureStage.MediaAttachment,
                        rejection.Stage);
                    Assert.Equal(1, responderMedia.Routes.Count);
                }
                finally
                {
                    listener.ConnectionFaulted -= OnConnectionFaulted;
                }

                using var secondMediaClient = new TcpClient(AddressFamily.InterNetwork);
                await secondMediaClient.ConnectAsync(endpoint, deadline.Token);
                var secondStream = new PrefixRecordingStream(
                    secondMediaClient.GetStream(),
                    sizeof(int) + RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes);
                await secondInitiatorSession.ConnectInitiatorAsync(
                    secondStream,
                    SessionId,
                    ActivityId,
                    deadline.Token);
                await secondResponderSession.WaitForAttachmentAsync(deadline.Token);
                Assert.False(oldRequest.SequenceEqual(secondStream.Snapshot()));
                Assert.Equal(0, initiatorMedia.Routes.Count);
                Assert.Equal(1, responderMedia.Routes.Count);

                using RemoteWindowMediaFrame recoveredFrame =
                    CreateVideoFrame(sequence: 1);
                AuthenticatedRemoteWindowMediaSession secondSender = initiatorSends
                    ? secondInitiatorSession
                    : secondResponderSession;
                AuthenticatedRemoteWindowMediaSession secondReceiver = initiatorSends
                    ? secondResponderSession
                    : secondInitiatorSession;
                Task<RemoteWindowMediaFrame> recoveredReceive = secondReceiver
                    .ReceiveAsync(deadline.Token)
                    .AsTask();
                await secondSender.SendAsync(
                    recoveredFrame,
                    deadline.Token);
                using RemoteWindowMediaFrame recovered = await recoveredReceive;
                Assert.Equal(
                    recoveredFrame.ExportPayload(),
                    recovered.ExportPayload());

                await initiatorHandler.DisposeAsync();
                await ObserveControlStopAsync(secondControl);
                await WaitForSessionRemovalAsync(
                    responderMedia,
                    InitiatorId,
                    deadline.Token);
                await WaitForControlSessionRemovalAsync(
                    responderHandler,
                    InitiatorId,
                    deadline.Token);
                Assert.False(secondInitiatorSession.IsCurrent);
                Assert.False(secondResponderSession.IsCurrent);
                Assert.False(secondInitiatorSession.IsAttached);
                Assert.False(secondResponderSession.IsAttached);
                Assert.Equal(0, initiatorMedia.Routes.Count);
                Assert.Equal(0, responderMedia.Routes.Count);
            }
        }
        finally
        {
            if (oldRequest is not null)
            {
                CryptographicOperations.ZeroMemory(oldRequest);
            }

            listenerStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenerRun);
        }
    }

    private static RemoteWindowMediaFrame CreateVideoFrame(ulong sequence) =>
        RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence,
            chunkIndex: 0,
            chunkCount: 1,
            [0x10, 0x20, 0x30, 0x40]);

    public enum MediaBudgetBoundary
    {
        FrameCount,
        PlaintextBytes,
    }

    public enum MediaDirection
    {
        InitiatorToResponder,
        ResponderToInitiator,
    }

    private static AuthenticatedActivitySessionHandler CreateHandler(
        DeviceId localDeviceId,
        AuthenticatedRemoteWindowMediaSessionDirectory mediaSessions) => new(
            new RejectingActivityPeer(localDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            timeProvider: TimeProvider.System,
            scenePeer: null,
            remoteWindowPeer: null,
            remoteWindowMediaSessions: mediaSessions);

    private static async Task<AuthenticatedRemoteWindowMediaSession>
        WaitForSessionAsync(
            AuthenticatedRemoteWindowMediaSessionDirectory directory,
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
    {
        while (true)
        {
            if (directory.TryGet(peerDeviceId, out var session)
                && session is not null)
            {
                return session;
            }

            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => changed.TrySetResult();
            directory.Changed += OnChanged;
            try
            {
                if (directory.TryGet(peerDeviceId, out session)
                    && session is not null)
                {
                    return session;
                }

                await changed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                directory.Changed -= OnChanged;
            }
        }
    }

    private static async Task WaitForSessionRemovalAsync(
        AuthenticatedRemoteWindowMediaSessionDirectory directory,
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        while (directory.TryGet(peerDeviceId, out _))
        {
            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => changed.TrySetResult();
            directory.Changed += OnChanged;
            try
            {
                if (!directory.TryGet(peerDeviceId, out _))
                {
                    return;
                }

                await changed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                directory.Changed -= OnChanged;
            }
        }
    }

    private static async Task WaitForControlSessionRemovalAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        while (handler.TryGetChannel(peerDeviceId, out _)
            || handler.TryGetRemoteWindowChannel(peerDeviceId, out _))
        {
            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => changed.TrySetResult();
            handler.Changed += OnChanged;
            try
            {
                if (!handler.TryGetChannel(peerDeviceId, out _)
                    && !handler.TryGetRemoteWindowChannel(peerDeviceId, out _))
                {
                    return;
                }

                await changed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                handler.Changed -= OnChanged;
            }
        }
    }

    private static async Task ObserveControlStopAsync(Task control)
    {
        try
        {
            await control.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (
            exception is OperationCanceledException
                or IOException
                or SocketException)
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
            ValueTask.FromException<OperationReceipt>(new InvalidOperationException(
                "No Activity transfer is expected in the media loopback."));
    }

    private sealed class RejectingPairingDecisionSource : IPairingDecisionSource
    {
        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingDecision>(new InvalidOperationException(
                "No pairing ceremony is expected in the trusted media loopback."));
    }

    private sealed class PrefixRecordingStream(Stream inner, int captureBytes) : Stream
    {
        private readonly byte[] capture = new byte[captureBytes];
        private readonly Lock gate = new();
        private int captured;
        private long readBytes;
        private long writtenBytes;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public long BytesRead
        {
            get
            {
                lock (gate)
                {
                    return readBytes;
                }
            }
        }

        public long BytesWritten
        {
            get
            {
                lock (gate)
                {
                    return writtenBytes;
                }
            }
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            RecordRead(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            RecordRead(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public byte[] Snapshot()
        {
            lock (gate)
            {
                return capture.AsSpan(0, captured).ToArray();
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            RecordWrite(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            RecordWrite(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (gate)
                {
                    CryptographicOperations.ZeroMemory(capture);
                    captured = 0;
                }

                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => base.DisposeAsync();

        private void RecordRead(int count)
        {
            lock (gate)
            {
                readBytes = checked(readBytes + count);
            }
        }

        private void RecordWrite(ReadOnlySpan<byte> bytes)
        {
            lock (gate)
            {
                writtenBytes = checked(writtenBytes + bytes.Length);
                int count = Math.Min(bytes.Length, capture.Length - captured);
                bytes[..count].CopyTo(capture.AsSpan(captured));
                captured += count;
            }
        }
    }
}
