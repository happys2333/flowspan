using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedRemoteWindowLogicalVideoFrameLoopbackTests
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
    public async Task MaximumLogicalFrameTraversesAuthenticatedSessionsAndCleansUp()
    {
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion;
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

        try
        {
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
                await WaitForSessionAsync(
                    initiatorMedia,
                    ResponderId,
                    deadline.Token);
            AuthenticatedRemoteWindowMediaSession responderSession =
                await WaitForSessionAsync(
                    responderMedia,
                    InitiatorId,
                    deadline.Token);
            RemoteWindowMediaRouteBinding route =
                responderSession.PrepareResponderRoute(SessionId, ActivityId);
            using var mediaClient = new TcpClient(AddressFamily.InterNetwork);
            await mediaClient.ConnectAsync(endpoint, deadline.Token);

            await initiatorSession.ConnectInitiatorAsync(
                mediaClient.GetStream(),
                SessionId,
                ActivityId,
                deadline.Token);
            await responderSession.WaitForAttachmentAsync(deadline.Token);

            var budget = new RemoteWindowMediaSessionBudget();
            await using var sender = new RemoteWindowLogicalVideoFrameSender(
                budget,
                ResponderId,
                initiatorSession);
            using var assembler = new RemoteWindowVideoFrameAssembler(
                SessionId,
                ActivityId);
            byte[] payload = GC.AllocateUninitializedArray<byte>(
                RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes);
            FillPayload(payload);
            try
            {
                var logicalFrame = RemoteWindowLogicalVideoFrame.Create(
                    SessionId,
                    ActivityId,
                    firstSequence: 41,
                    payload);
                Task<ReceivedLogicalFrame> receiving = ReceiveLogicalFrameAsync(
                    responderSession,
                    assembler,
                    deadline.Token);

                RemoteWindowLogicalVideoFrameOutcome outcome =
                    await sender.TakeOwnership(logicalFrame)
                        .WaitAsync(deadline.Token);
                ReceivedLogicalFrame received =
                    await receiving.WaitAsync(deadline.Token);
                using RemoteWindowVideoFrameAssembly assembly = received.Assembly;

                Assert.Equal(RemoteWindowLogicalVideoFrameOutcome.Sent, outcome);
                Assert.Equal(RemoteWindowMediaFrame.MaximumVideoChunks, received.Chunks);
                Assert.Equal(route, initiatorSession.Binding);
                Assert.Equal(route, responderSession.Binding);
                Assert.Equal(SessionId, assembly.SessionId);
                Assert.Equal(ActivityId, assembly.ActivityId);
                Assert.Equal<ulong>(41, assembly.FirstSequence);
                Assert.Equal<ulong>(56, assembly.LastSequence);
                Assert.Equal(payload.Length, assembly.PayloadLength);
                Assert.True(assembly.Payload.Span.SequenceEqual(payload));
                Assert.Throws<ObjectDisposedException>(logicalFrame.ExportPayload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }

            await sender.DisposeAsync();
            await initiatorSession.DisposeAsync();
            await ObserveControlStopAsync(initiatorRun);
            await WaitForSessionRemovalAsync(
                initiatorMedia,
                ResponderId,
                deadline.Token);
            await WaitForSessionRemovalAsync(
                responderMedia,
                InitiatorId,
                deadline.Token);
            await responderSession.DisposeAsync();

            Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
            Assert.False(initiatorSession.IsCurrent);
            Assert.False(responderSession.IsCurrent);
            Assert.False(initiatorMedia.TryGet(ResponderId, out _));
            Assert.False(responderMedia.TryGet(InitiatorId, out _));
            Assert.Equal(0, initiatorMedia.Routes.Count);
            Assert.Equal(0, responderMedia.Routes.Count);
        }
        finally
        {
            listenerStop.Cancel();
            await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
        }
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

    private static void FillPayload(Span<byte> payload)
    {
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = checked((byte)(index % 251));
        }
    }

    private static async Task<ReceivedLogicalFrame> ReceiveLogicalFrameAsync(
        AuthenticatedRemoteWindowMediaSession session,
        RemoteWindowVideoFrameAssembler assembler,
        CancellationToken cancellationToken)
    {
        var chunks = 0;
        while (true)
        {
            RemoteWindowMediaFrame frame =
                await session.ReceiveAsync(cancellationToken);
            chunks++;
            RemoteWindowVideoFrameAssembly? assembly = assembler.Add(frame);
            if (assembly is not null)
            {
                return new ReceivedLogicalFrame(assembly, chunks);
            }
        }
    }

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

    private static async Task ObserveListenerStopAsync(
        Task listener,
        CancellationToken stopToken)
    {
        try
        {
            await listener.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
        }
        catch (Exception exception) when (
            stopToken.IsCancellationRequested
                && exception is OperationCanceledException or SocketException)
        {
        }
    }

    private sealed record ReceivedLogicalFrame(
        RemoteWindowVideoFrameAssembly Assembly,
        int Chunks);

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(new InvalidOperationException(
                "No Activity transfer is expected in the logical media loopback."));
    }

    private sealed class RejectingPairingDecisionSource : IPairingDecisionSource
    {
        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingDecision>(new InvalidOperationException(
                "No pairing ceremony is expected in the trusted logical media loopback."));
    }
}
