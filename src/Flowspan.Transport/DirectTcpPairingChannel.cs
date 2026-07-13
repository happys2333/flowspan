using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class DirectTcpPairingChannel : IPairingMessageChannel
{
    private readonly DirectTcpPeerConnection connection;
    private byte[]? initialMessage;

    private DirectTcpPairingChannel(
        DirectTcpPeerConnection connection,
        byte[]? initialMessage = null)
    {
        this.connection = connection;
        this.initialMessage = initialMessage;
    }

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

    public ValueTask DisposeAsync()
    {
        byte[]? unconsumed = Interlocked.Exchange(ref initialMessage, null);
        if (unconsumed is not null)
        {
            CryptographicOperations.ZeroMemory(unconsumed);
        }

        return connection.DisposeAsync();
    }

    public ValueTask<byte[]> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? prefetched = Interlocked.Exchange(ref initialMessage, null);
        if (prefetched is null)
        {
            return connection.ReceiveHandshakeAsync(cancellationToken);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(prefetched);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(prefetched);
            throw;
        }
    }

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

    internal static DirectTcpPairingChannel FromAcceptedConnection(
        DirectTcpPeerConnection connection,
        byte[] initialMessage)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(initialMessage);
        if (initialMessage.Length is < 1 or > PairingWireCodec.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(initialMessage));
        }

        return new DirectTcpPairingChannel(connection, initialMessage);
    }
}
