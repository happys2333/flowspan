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
