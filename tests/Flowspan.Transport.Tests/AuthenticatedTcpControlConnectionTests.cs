using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedTcpControlConnectionTests
{
    [Fact]
    public async Task TrustedPeersEstablishVersionAndIdentityBoundEncryptedChannel()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
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
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [new ProtocolVersion(1, 1)]).AsTask();

        await using AuthenticatedTcpControlConnection initiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        await using AuthenticatedTcpControlConnection responder = await accept;
        ControlMessage request = ControlMessage.Create(
            new ProtocolVersion(1, 1),
            ControlMessageType.Hello,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            initiatorIdentity.DeviceId,
            new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            "{\"authenticated\":true}");

        await initiator.SendAsync(request);
        ControlMessage received = await responder.ReceiveAsync();

        Assert.Equal(new ProtocolVersion(1, 1), initiator.ProtocolVersion);
        Assert.Equal(initiator.ProtocolVersion, responder.ProtocolVersion);
        Assert.Equal(responderIdentity.DeviceId, initiator.PeerIdentity.DeviceId);
        Assert.Equal(initiatorIdentity.DeviceId, responder.PeerIdentity.DeviceId);
        Assert.Equal(request.BodyDigest, received.BodyDigest);

        ControlMessage wrongVersion = ControlMessage.Create(
            new ProtocolVersion(1, 0),
            ControlMessageType.Hello,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            initiatorIdentity.DeviceId,
            new DateTimeOffset(2026, 7, 13, 8, 0, 1, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            "{\"wrongVersion\":true}");
        ControlMessage wrongSender = ControlMessage.Create(
            new ProtocolVersion(1, 1),
            ControlMessageType.Hello,
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            responderIdentity.DeviceId,
            new DateTimeOffset(2026, 7, 13, 8, 0, 1, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            "{\"wrongSender\":true}");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await initiator.SendAsync(wrongVersion));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await initiator.SendAsync(wrongSender));
    }

    [Fact]
    public async Task TrustedDeviceIdWithChangedKeyCannotUpgradeConnection()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        DeviceId responderId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedResponder = DeviceIdentity.Generate(
            responderId,
            "Desk");
        using DeviceIdentity changedResponder = DeviceIdentity.Generate(
            responderId,
            "Desk");
        var initiatorTrust = new TrustRecord(
            trustedResponder.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                changedResponder,
                responderTrust,
                [new ProtocolVersion(1, 0)],
                timeout.Token).AsTask();

        SessionHandshakeException exception =
            await Assert.ThrowsAsync<SessionHandshakeException>(async () =>
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    initiatorIdentity,
                    initiatorTrust,
                    [new ProtocolVersion(1, 0)],
                    timeout.Token));
        Exception? responderFailure = await Record.ExceptionAsync(async () =>
            await accept);

        Assert.Equal(SessionHandshakeFailure.PeerIdentityChanged, exception.Failure);
        Assert.NotNull(responderFailure);
    }

    [Fact]
    public async Task SilentConnectedPeerCannotHoldHandshakeOpenIndefinitely()
    {
        using DeviceIdentity expectedPeer = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var responderTrust = new TrustRecord(
            expectedPeer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [new ProtocolVersion(1, 0)],
                TimeSpan.FromMilliseconds(100)).AsTask();
        using var silentPeer = new TcpClient(AddressFamily.InterNetwork);
        await silentPeer.ConnectAsync(endpoint);

        await Assert.ThrowsAsync<TimeoutException>(async () => await accept);
    }
}
