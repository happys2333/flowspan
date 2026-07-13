using System.Net;
using System.Net.Sockets;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class DirectTcpPairingChannel : IPairingMessageChannel
{
    private readonly DirectTcpPeerConnection connection;

    private DirectTcpPairingChannel(DirectTcpPeerConnection connection) =>
        this.connection = connection;

    public IPEndPoint LocalEndPoint => connection.LocalEndPoint;

    public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

    public static async ValueTask<DirectTcpPairingChannel> AcceptAsync(
        TcpListener listener,
        CancellationToken cancellationToken = default) => new(
        await DirectTcpPeerConnection.AcceptAsync(listener, cancellationToken)
            .ConfigureAwait(false));

    public static async ValueTask<DirectTcpPairingChannel> ConnectAsync(
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken = default) => new(
        await DirectTcpPeerConnection.ConnectAsync(remoteEndPoint, cancellationToken)
            .ConfigureAwait(false));

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    public ValueTask<byte[]> ReceiveAsync(
        CancellationToken cancellationToken = default) =>
        connection.ReceiveHandshakeAsync(cancellationToken);

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        if (message.IsEmpty || message.Length > PairingWireCodec.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                $"A pairing message must contain 1 to {PairingWireCodec.MaximumMessageBytes} bytes.");
        }

        return connection.SendHandshakeAsync(message, cancellationToken);
    }
}
