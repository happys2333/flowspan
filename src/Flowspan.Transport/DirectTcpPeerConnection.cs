using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Flowspan.Security;

namespace Flowspan.Transport;

internal interface IAuthenticatedHandshakeTransport : IAsyncDisposable
{
    public ValueTask SendHandshakeAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken);

    public ValueTask<byte[]> ReceiveHandshakeAsync(
        CancellationToken cancellationToken);
}

public sealed class DirectTcpPeerConnection :
    IAsyncDisposable,
    IAuthenticatedHandshakeTransport
{
    private readonly TcpClient client;
    private readonly Lock gate = new();
    private bool disposed;
    private bool upgraded;

    private DirectTcpPeerConnection(TcpClient client)
    {
        this.client = client;
        client.NoDelay = true;
        LocalEndPoint = RequireEndpoint(client.Client.LocalEndPoint, "local");
        RemoteEndPoint = RequireEndpoint(client.Client.RemoteEndPoint, "remote");
    }

    public IPEndPoint LocalEndPoint { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public static async ValueTask<DirectTcpPeerConnection> ConnectAsync(
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (remoteEndPoint.Port == 0
            || remoteEndPoint.Address.Equals(IPAddress.Any)
            || remoteEndPoint.Address.Equals(IPAddress.IPv6Any))
        {
            throw new ArgumentException(
                "A direct TCP connection requires a concrete remote address and port.",
                nameof(remoteEndPoint));
        }

        var client = new TcpClient(remoteEndPoint.AddressFamily);
        try
        {
            await client.ConnectAsync(remoteEndPoint, cancellationToken)
                .ConfigureAwait(false);
            return new DirectTcpPeerConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static async ValueTask<DirectTcpPeerConnection> AcceptAsync(
        TcpListener listener,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listener);
        TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return new DirectTcpPeerConnection(accepted);
        }
        catch
        {
            accepted.Dispose();
            throw;
        }
    }

    public SecureControlChannel UpgradeToSecureControl(SecureFrameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (upgraded)
            {
                throw new InvalidOperationException(
                    "A direct TCP connection can be upgraded only once.");
            }

            var channel = new SecureControlChannel(client.GetStream(), session);
            upgraded = true;
            return channel;
        }
    }

    internal async ValueTask SendHandshakeAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        if (message.IsEmpty
            || message.Length > SessionHandshakeWireCodec.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                $"A handshake message must contain 1 to {SessionHandshakeWireCodec.MaximumMessageBytes} bytes.");
        }

        NetworkStream transport = GetHandshakeStream();
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, message.Length);
        await transport.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await transport.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await transport.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<byte[]> ReceiveHandshakeAsync(
        CancellationToken cancellationToken)
    {
        NetworkStream transport = GetHandshakeStream();
        byte[] prefix = new byte[sizeof(int)];
        await transport.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length is < 1 or > SessionHandshakeWireCodec.MaximumMessageBytes)
        {
            throw new InvalidDataException(
                $"A handshake frame length must be from 1 to {SessionHandshakeWireCodec.MaximumMessageBytes} bytes.");
        }

        byte[] message = GC.AllocateUninitializedArray<byte>(length);
        await transport.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
        return message;
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            if (!upgraded)
            {
                client.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    ValueTask<byte[]> IAuthenticatedHandshakeTransport.ReceiveHandshakeAsync(
        CancellationToken cancellationToken) =>
        ReceiveHandshakeAsync(cancellationToken);

    ValueTask IAuthenticatedHandshakeTransport.SendHandshakeAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken) =>
        SendHandshakeAsync(message, cancellationToken);

    private static IPEndPoint RequireEndpoint(EndPoint? endpoint, string kind) =>
        endpoint as IPEndPoint
        ?? throw new InvalidOperationException(
            $"The connected TCP socket has no {kind} IP endpoint.");

    private NetworkStream GetHandshakeStream()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (upgraded)
            {
                throw new InvalidOperationException(
                    "Handshake messages cannot be exchanged after secure upgrade.");
            }

            return client.GetStream();
        }
    }
}
