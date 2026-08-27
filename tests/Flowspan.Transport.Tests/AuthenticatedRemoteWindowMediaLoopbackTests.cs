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

        public byte[] Snapshot()
        {
            lock (gate)
            {
                return capture.AsSpan(0, captured).ToArray();
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Record(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Record(buffer.Span);
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

        private void Record(ReadOnlySpan<byte> bytes)
        {
            lock (gate)
            {
                int count = Math.Min(bytes.Length, capture.Length - captured);
                bytes[..count].CopyTo(capture.AsSpan(captured));
                captured += count;
            }
        }
    }
}
