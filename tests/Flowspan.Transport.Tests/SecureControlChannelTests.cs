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

public sealed class SecureControlChannelTests
{
    [Fact]
    public async Task ControlMessagesRoundTripOverLoopbackTcp()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        (SecureFrameSession initiator, SecureFrameSession responder) = CreateSessions();
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder);
        ControlMessage request = CreateMessage(
            "11111111-1111-1111-1111-111111111111",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "{\"versions\":[\"1.0\"]}");
        ControlMessage response = CreateMessage(
            "22222222-2222-2222-2222-222222222222",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "{\"selected\":\"1.0\"}");

        await clientChannel.SendAsync(request);
        ControlMessage receivedRequest = await serverChannel.ReceiveAsync();
        await serverChannel.SendAsync(response);
        ControlMessage receivedResponse = await clientChannel.ReceiveAsync();

        Assert.Equal(request.MessageId, receivedRequest.MessageId);
        Assert.Equal(request.BodyDigest, receivedRequest.BodyDigest);
        Assert.Equal(response.MessageId, receivedResponse.MessageId);
        Assert.Equal(response.BodyDigest, receivedResponse.BodyDigest);
    }

    [Fact]
    public async Task RequestedRekeyIsInternalAndTrafficContinuesAtNextEpoch()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        (SecureFrameSession initiator, SecureFrameSession responder) = CreateSessions();
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator, liveRekeyEnabled: true);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder, liveRekeyEnabled: true);
        Task<ControlMessage> serverReceive = serverChannel.ReceiveAsync().AsTask();
        Task<ControlMessage> clientReceive = clientChannel.ReceiveAsync().AsTask();

        await clientChannel.RekeyAsync(TimeSpan.FromSeconds(2));

        Assert.Equal<uint>(2, initiator.SendEpoch);
        Assert.Equal<uint>(2, initiator.ReceiveEpoch);
        Assert.Equal<uint>(2, responder.SendEpoch);
        Assert.Equal<uint>(2, responder.ReceiveEpoch);
        ControlMessage request = CreateMessage(
            "11111111-1111-1111-1111-111111111111",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "{\"epoch\":2}");
        ControlMessage response = CreateMessage(
            "22222222-2222-2222-2222-222222222222",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "{\"epoch\":2}");
        await clientChannel.SendAsync(request);
        Assert.Equal(request.BodyDigest, (await serverReceive).BodyDigest);
        await serverChannel.SendAsync(response);
        Assert.Equal(response.BodyDigest, (await clientReceive).BodyDigest);
    }

    [Fact]
    public async Task SimultaneousRekeyRequestsConvergeWithoutSecondRotation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        (SecureFrameSession initiator, SecureFrameSession responder) = CreateSessions();
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator, liveRekeyEnabled: true);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder, liveRekeyEnabled: true);
        Task<ControlMessage> serverReceive = serverChannel.ReceiveAsync().AsTask();
        Task<ControlMessage> clientReceive = clientChannel.ReceiveAsync().AsTask();

        await Task.WhenAll(
            clientChannel.RekeyAsync(TimeSpan.FromSeconds(2)).AsTask(),
            serverChannel.RekeyAsync(TimeSpan.FromSeconds(2)).AsTask());

        Assert.Equal<uint>(2, initiator.SendEpoch);
        Assert.Equal<uint>(2, initiator.ReceiveEpoch);
        Assert.Equal<uint>(2, responder.SendEpoch);
        Assert.Equal<uint>(2, responder.ReceiveEpoch);
        ControlMessage request = CreateMessage(
            "11111111-1111-1111-1111-111111111111",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "{\"simultaneous\":true}");
        ControlMessage response = CreateMessage(
            "22222222-2222-2222-2222-222222222222",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "{\"simultaneous\":true}");
        await clientChannel.SendAsync(request);
        await serverChannel.SendAsync(response);

        Assert.Equal(request.BodyDigest, (await serverReceive).BodyDigest);
        Assert.Equal(response.BodyDigest, (await clientReceive).BodyDigest);
    }

    [Fact]
    public async Task FrameThresholdAutomaticallyRekeysBeforeApplicationData()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateBoundedSessions(maximumFramesPerEpoch: 3);
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator, liveRekeyEnabled: true);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder, liveRekeyEnabled: true);

        for (int index = 0; index < 2; index++)
        {
            Task<ControlMessage> receiving = serverChannel.ReceiveAsync().AsTask();
            ControlMessage message = CreateMessage(
                "11111111-1111-1111-1111-111111111111",
                $"00000000-0000-0000-0000-{index + 1:000000000000}",
                $"{{\"index\":{index}}}");
            await clientChannel.SendAsync(message);
            Assert.Equal(message.BodyDigest, (await receiving).BodyDigest);
        }

        Task<ControlMessage> clientReceive = clientChannel.ReceiveAsync().AsTask();
        Task<ControlMessage> serverReceive = serverChannel.ReceiveAsync().AsTask();
        ControlMessage third = CreateMessage(
            "11111111-1111-1111-1111-111111111111",
            "00000000-0000-0000-0000-000000000003",
            "{\"index\":2}");
        await clientChannel.SendAsync(third);

        Assert.Equal(third.BodyDigest, (await serverReceive).BodyDigest);
        Assert.Equal<uint>(2, initiator.SendEpoch);
        Assert.Equal<uint>(2, initiator.ReceiveEpoch);
        Assert.Equal<uint>(2, responder.SendEpoch);
        Assert.Equal<uint>(2, responder.ReceiveEpoch);
        ControlMessage response = CreateMessage(
            "22222222-2222-2222-2222-222222222222",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "{\"epoch\":2}");
        await serverChannel.SendAsync(response);
        Assert.Equal(response.BodyDigest, (await clientReceive).BodyDigest);
    }

    [Fact]
    public async Task PlaintextByteThresholdReservesKeyUpdateBeforeApplicationData()
    {
        ControlMessage first = CreateMessage(
            "11111111-1111-1111-1111-111111111111",
            "00000000-0000-0000-0000-000000000001",
            "{\"bytes\":1}");
        ControlMessage second = CreateMessage(
            "11111111-1111-1111-1111-111111111111",
            "00000000-0000-0000-0000-000000000002",
            "{\"bytes\":2}");
        byte[] encoded = ControlMessageCodec.Encode(first);
        ulong applicationBytes = checked((ulong)encoded.Length);
        CryptographicOperations.ZeroMemory(encoded);
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateBoundedSessions(
                maximumFramesPerEpoch: 4,
                maximumPlaintextBytesPerEpoch:
                    applicationBytes + SecureSessionKeyUpdateCodec.EncodedLength);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator, liveRekeyEnabled: true);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder, liveRekeyEnabled: true);

        Task<ControlMessage> firstReceive = serverChannel.ReceiveAsync().AsTask();
        await clientChannel.SendAsync(first);
        Assert.Equal(first.BodyDigest, (await firstReceive).BodyDigest);
        Assert.Equal(applicationBytes, initiator.SendPlaintextBytes);

        Task<ControlMessage> clientReceive = clientChannel.ReceiveAsync().AsTask();
        Task<ControlMessage> secondReceive = serverChannel.ReceiveAsync().AsTask();
        await clientChannel.SendAsync(second);

        Assert.Equal(second.BodyDigest, (await secondReceive).BodyDigest);
        Assert.Equal<uint>(2, initiator.SendEpoch);
        Assert.Equal<uint>(2, initiator.ReceiveEpoch);
        Assert.Equal<uint>(2, responder.SendEpoch);
        Assert.Equal<uint>(2, responder.ReceiveEpoch);
        Assert.Equal(applicationBytes, initiator.SendPlaintextBytes);

        ControlMessage response = CreateMessage(
            "22222222-2222-2222-2222-222222222222",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "{\"bytes\":2}");
        await serverChannel.SendAsync(response);
        Assert.Equal(response.BodyDigest, (await clientReceive).BodyDigest);
    }

    [Fact]
    public async Task OversizedFrameFaultsChannelBeforeAllocation()
    {
        byte[] hostilePrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(
            hostilePrefix,
            SecureControlChannel.MaximumEncryptedFrameBytes + 1);
        var stream = new MemoryStream(hostilePrefix);
        (SecureFrameSession unused, SecureFrameSession receiver) = CreateSessions();
        unused.Dispose();
        await using var channel = new SecureControlChannel(stream, receiver);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await channel.ReceiveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.ReceiveAsync());
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSessions()
    {
        byte[] secret = Enumerable.Repeat((byte)0x33, 32).ToArray();
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes("authenticated-test-transcript"));
        using SecureSessionKeyMaterial material = SecureSessionKeyMaterial.Derive(
            secret,
            transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateBoundedSessions(
            ulong maximumFramesPerEpoch,
            ulong maximumPlaintextBytesPerEpoch =
                SecureFrameSession.MaximumPlaintextBytesPerEpoch)
    {
        byte[] firstKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] secondKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x33, 16).ToArray();
        return (
            new SecureFrameSession(
                firstKey,
                SecureFrameDirection.InitiatorToResponder,
                secondKey,
                SecureFrameDirection.ResponderToInitiator,
                sessionIdentifier,
                maximumFramesPerEpoch,
                maximumPlaintextBytesPerEpoch),
            new SecureFrameSession(
                secondKey,
                SecureFrameDirection.ResponderToInitiator,
                firstKey,
                SecureFrameDirection.InitiatorToResponder,
                sessionIdentifier,
                maximumFramesPerEpoch,
                maximumPlaintextBytesPerEpoch));
    }

    private static ControlMessage CreateMessage(
        string senderDeviceId,
        string messageId,
        string bodyJson) => ControlMessage.Create(
            new ProtocolVersion(1, 0),
            ControlMessageType.Hello,
            Guid.Parse(messageId),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            DeviceId.Parse(senderDeviceId),
            new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            bodyJson);
}
