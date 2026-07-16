using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class AuthenticatedTcpControlConnection : IAsyncDisposable
{
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaximumHandshakeTimeout = TimeSpan.FromMinutes(2);
    private readonly AuthenticatedSession authenticatedSession;
    private readonly SecureControlChannel channel;

    private AuthenticatedTcpControlConnection(
        AuthenticatedSession authenticatedSession,
        SecureControlChannel channel,
        DeviceId localDeviceId,
        IPEndPoint localEndPoint,
        IPEndPoint remoteEndPoint)
    {
        this.authenticatedSession = authenticatedSession;
        this.channel = channel;
        LocalDeviceId = localDeviceId;
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
    }

    public DeviceId LocalDeviceId { get; }

    public IPEndPoint LocalEndPoint { get; }

    internal ulong NextSecureReceiveSequence =>
        authenticatedSession.SecureFrames.NextReceiveSequence;

    internal ulong NextSecureSendSequence =>
        authenticatedSession.SecureFrames.NextSendSequence;

    internal uint SecureReceiveEpoch =>
        authenticatedSession.SecureFrames.ReceiveEpoch;

    internal uint SecureSendEpoch =>
        authenticatedSession.SecureFrames.SendEpoch;

    public PublicDeviceIdentity PeerIdentity => authenticatedSession.PeerIdentity;

    public ProtocolVersion ProtocolVersion => authenticatedSession.ProtocolVersion;

    public ValueTask RekeyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!ProtocolFeatures.SupportsLiveRekey(ProtocolVersion))
        {
            throw new InvalidOperationException(
                "Live rekey requires negotiated protocol 1.3 or later.");
        }

        return channel.RekeyAsync(timeout, cancellationToken);
    }

    public IPEndPoint RemoteEndPoint { get; }

    public async ValueTask<ControlMessage> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        ControlMessage message = await channel.ReceiveAsync(cancellationToken)
            .ConfigureAwait(false);
        if (message.Version != ProtocolVersion
            || message.SenderDeviceId != PeerIdentity.DeviceId)
        {
            var failure = new InvalidDataException(
                "The control message is not bound to the authenticated peer and negotiated version.");
            throw channel.RejectPeerMessage(failure);
        }

        return message;
    }

    public ValueTask SendAsync(
        ControlMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Version != ProtocolVersion
            || message.SenderDeviceId != LocalDeviceId)
        {
            throw new InvalidOperationException(
                "A control message must use the authenticated local identity and negotiated version.");
        }

        return channel.SendAsync(message, cancellationToken);
    }

    public static ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
        IPEndPoint remoteEndPoint,
        DeviceIdentity localIdentity,
        TrustRecord trustedPeer,
        IEnumerable<ProtocolVersion> supportedVersions,
        CancellationToken cancellationToken = default) => ConnectAsync(
            remoteEndPoint,
            localIdentity,
            trustedPeer,
            supportedVersions,
            DefaultHandshakeTimeout,
            cancellationToken);

    public static async ValueTask<AuthenticatedTcpControlConnection> ConnectAsync(
        IPEndPoint remoteEndPoint,
        DeviceIdentity localIdentity,
        TrustRecord trustedPeer,
        IEnumerable<ProtocolVersion> supportedVersions,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustedPeer);
        ArgumentNullException.ThrowIfNull(supportedVersions);
        ValidateHandshakeTimeout(handshakeTimeout);
        DirectTcpPeerConnection connection = await DirectTcpPeerConnection.ConnectAsync(
            remoteEndPoint,
            cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource handshakeCancellation =
                CreateHandshakeCancellation(handshakeTimeout, cancellationToken);
            try
            {
                return await AuthenticateInitiatorAsync(
                    connection,
                    localIdentity,
                    trustedPeer,
                    supportedVersions,
                    handshakeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested
                && handshakeCancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The authenticated TCP handshake timed out.",
                    exception);
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static ValueTask<AuthenticatedTcpControlConnection> AcceptAsync(
        TcpListener listener,
        DeviceIdentity localIdentity,
        TrustRecord trustedPeer,
        IEnumerable<ProtocolVersion> supportedVersions,
        CancellationToken cancellationToken = default) => AcceptAsync(
            listener,
            localIdentity,
            trustedPeer,
            supportedVersions,
            DefaultHandshakeTimeout,
            cancellationToken);

    public static async ValueTask<AuthenticatedTcpControlConnection> AcceptAsync(
        TcpListener listener,
        DeviceIdentity localIdentity,
        TrustRecord trustedPeer,
        IEnumerable<ProtocolVersion> supportedVersions,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustedPeer);
        ArgumentNullException.ThrowIfNull(supportedVersions);
        ValidateHandshakeTimeout(handshakeTimeout);
        DirectTcpPeerConnection connection = await DirectTcpPeerConnection.AcceptAsync(
            listener,
            cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource handshakeCancellation =
                CreateHandshakeCancellation(handshakeTimeout, cancellationToken);
            try
            {
                return await AuthenticateResponderAsync(
                    connection,
                    localIdentity,
                    trustedPeer,
                    supportedVersions,
                    handshakeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested
                && handshakeCancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The authenticated TCP handshake timed out.",
                    exception);
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async ValueTask<AuthenticatedTcpControlConnection>
        AcceptAnyTrustedAsync(
            TcpListener listener,
            DeviceIdentity localIdentity,
            TrustSessionCoordinator trustSessions,
            IEnumerable<ProtocolVersion> supportedVersions,
            TimeSpan handshakeTimeout,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(supportedVersions);
        ValidateHandshakeTimeout(handshakeTimeout);
        DirectTcpPeerConnection connection = await DirectTcpPeerConnection.AcceptAsync(
            listener,
            cancellationToken).ConfigureAwait(false);
        return await AcceptAnyTrustedConnectionAsync(
            connection,
            initialHello: null,
            localIdentity,
            trustSessions,
            supportedVersions,
            handshakeTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    internal static ValueTask<AuthenticatedTcpControlConnection>
        AcceptAnyTrustedAsync(
            DirectTcpPeerConnection connection,
            byte[] initialHello,
            DeviceIdentity localIdentity,
            TrustSessionCoordinator trustSessions,
            IEnumerable<ProtocolVersion> supportedVersions,
            TimeSpan handshakeTimeout,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(initialHello);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(supportedVersions);
        ValidateHandshakeTimeout(handshakeTimeout);
        return AcceptAnyTrustedConnectionAsync(
            connection,
            initialHello,
            localIdentity,
            trustSessions,
            supportedVersions,
            handshakeTimeout,
            cancellationToken);
    }

    private static async ValueTask<AuthenticatedTcpControlConnection>
        AcceptAnyTrustedConnectionAsync(
            DirectTcpPeerConnection connection,
            byte[]? initialHello,
            DeviceIdentity localIdentity,
            TrustSessionCoordinator trustSessions,
            IEnumerable<ProtocolVersion> supportedVersions,
            TimeSpan handshakeTimeout,
            CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource handshakeCancellation =
                CreateHandshakeCancellation(handshakeTimeout, cancellationToken);
            try
            {
                return await AuthenticateResolvedResponderAsync(
                    connection,
                    initialHello,
                    localIdentity,
                    trustSessions,
                    supportedVersions,
                    handshakeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested
                && handshakeCancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The authenticated TCP handshake timed out.",
                    exception);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            IPEndPoint remoteEndPoint = connection.RemoteEndPoint;
            Exception failure = exception;
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                failure = new AggregateException(
                    "Incoming authentication and connection cleanup both failed.",
                    exception,
                    cleanupFailure);
            }

            throw new IncomingPeerAuthenticationException(remoteEndPoint, failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            authenticatedSession.Dispose();
        }
    }

    private static async ValueTask<AuthenticatedTcpControlConnection>
        AuthenticateInitiatorAsync(
            DirectTcpPeerConnection connection,
            DeviceIdentity localIdentity,
            TrustRecord trustedPeer,
            IEnumerable<ProtocolVersion> supportedVersions,
            CancellationToken cancellationToken)
    {
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello localHello = CreateHello(
            SecureSessionRole.Initiator,
            localIdentity,
            supportedVersions,
            agreement);
        await SendAsync(connection, SessionHandshakeWireCodec.EncodeHello(localHello), cancellationToken)
            .ConfigureAwait(false);
        SessionHandshakeHello peerHello = await ReceiveHelloAsync(
            connection,
            trustedPeer.PeerIdentity,
            cancellationToken).ConfigureAwait(false);
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            localHello,
            peerHello);
        SessionHandshakeAuthentication localAuthentication =
            SessionHandshakeAuthentication.Create(transcript, localIdentity);
        await SendAsync(
            connection,
            SessionHandshakeWireCodec.EncodeAuthentication(localAuthentication),
            cancellationToken).ConfigureAwait(false);
        SessionHandshakeAuthentication peerAuthentication =
            await ReceiveAuthenticationAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        AuthenticatedSession authenticated = AuthenticatedSessionHandshake.Complete(
            transcript,
            SecureSessionRole.Initiator,
            localIdentity.PublicIdentity,
            trustedPeer.PeerIdentity,
            agreement,
            peerAuthentication);
        try
        {
            if (ProtocolFeatures.RequiresSecureSessionFinished(
                authenticated.ProtocolVersion))
            {
                await AuthenticatedSessionFinishedExchange.ConfirmAsInitiatorAsync(
                    connection,
                    authenticated,
                    transcript,
                    cancellationToken).ConfigureAwait(false);
            }

            return Upgrade(connection, authenticated, localIdentity.DeviceId);
        }
        catch
        {
            authenticated.Dispose();
            throw;
        }
    }

    private static async ValueTask<AuthenticatedTcpControlConnection>
        AuthenticateResponderAsync(
            DirectTcpPeerConnection connection,
            DeviceIdentity localIdentity,
            TrustRecord trustedPeer,
            IEnumerable<ProtocolVersion> supportedVersions,
            CancellationToken cancellationToken)
    {
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello peerHello = await ReceiveHelloAsync(
            connection,
            trustedPeer.PeerIdentity,
            cancellationToken).ConfigureAwait(false);
        SessionHandshakeHello localHello = CreateHello(
            SecureSessionRole.Responder,
            localIdentity,
            supportedVersions,
            agreement);
        await SendAsync(connection, SessionHandshakeWireCodec.EncodeHello(localHello), cancellationToken)
            .ConfigureAwait(false);
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            peerHello,
            localHello);
        SessionHandshakeAuthentication peerAuthentication =
            await ReceiveAuthenticationAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        AuthenticatedSession authenticated = AuthenticatedSessionHandshake.Complete(
            transcript,
            SecureSessionRole.Responder,
            localIdentity.PublicIdentity,
            trustedPeer.PeerIdentity,
            agreement,
            peerAuthentication);
        try
        {
            SessionHandshakeAuthentication localAuthentication =
                SessionHandshakeAuthentication.Create(transcript, localIdentity);
            await SendAsync(
                connection,
                SessionHandshakeWireCodec.EncodeAuthentication(localAuthentication),
                cancellationToken).ConfigureAwait(false);
            if (ProtocolFeatures.RequiresSecureSessionFinished(
                authenticated.ProtocolVersion))
            {
                await AuthenticatedSessionFinishedExchange.ConfirmAsResponderAsync(
                    connection,
                    authenticated,
                    transcript,
                    cancellationToken).ConfigureAwait(false);
            }

            return Upgrade(connection, authenticated, localIdentity.DeviceId);
        }
        catch
        {
            authenticated.Dispose();
            throw;
        }
    }

    private static async ValueTask<AuthenticatedTcpControlConnection>
        AuthenticateResolvedResponderAsync(
            DirectTcpPeerConnection connection,
            byte[]? initialHello,
            DeviceIdentity localIdentity,
            TrustSessionCoordinator trustSessions,
            IEnumerable<ProtocolVersion> supportedVersions,
            CancellationToken cancellationToken)
    {
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        byte[] message = initialHello
            ?? await connection.ReceiveHandshakeAsync(cancellationToken)
                .ConfigureAwait(false);
        SessionHandshakeHello peerHello;
        TrustRecord trustedPeer;
        try
        {
            DeviceId claimedPeerId =
                SessionHandshakeWireCodec.ReadClaimedHelloDeviceId(message);
            if (!trustSessions.TryGetCurrentTrust(
                    claimedPeerId,
                    out TrustRecord? currentTrust))
            {
                throw new SessionHandshakeException(
                    SessionHandshakeFailure.PeerNotTrusted,
                    "The incoming peer is not currently trusted.");
            }

            trustedPeer = currentTrust;
            peerHello = SessionHandshakeWireCodec.DecodeHello(
                message,
                trustedPeer.PeerIdentity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }

        SessionHandshakeHello localHello = CreateHello(
            SecureSessionRole.Responder,
            localIdentity,
            supportedVersions,
            agreement);
        await SendAsync(connection, SessionHandshakeWireCodec.EncodeHello(localHello), cancellationToken)
            .ConfigureAwait(false);
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            peerHello,
            localHello);
        SessionHandshakeAuthentication peerAuthentication =
            await ReceiveAuthenticationAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        AuthenticatedSession authenticated = AuthenticatedSessionHandshake.Complete(
            transcript,
            SecureSessionRole.Responder,
            localIdentity.PublicIdentity,
            trustedPeer.PeerIdentity,
            agreement,
            peerAuthentication);
        try
        {
            SessionHandshakeAuthentication localAuthentication =
                SessionHandshakeAuthentication.Create(transcript, localIdentity);
            await SendAsync(
                connection,
                SessionHandshakeWireCodec.EncodeAuthentication(localAuthentication),
                cancellationToken).ConfigureAwait(false);
            if (ProtocolFeatures.RequiresSecureSessionFinished(
                authenticated.ProtocolVersion))
            {
                await AuthenticatedSessionFinishedExchange.ConfirmAsResponderAsync(
                    connection,
                    authenticated,
                    transcript,
                    cancellationToken).ConfigureAwait(false);
            }

            return Upgrade(connection, authenticated, localIdentity.DeviceId);
        }
        catch
        {
            authenticated.Dispose();
            throw;
        }
    }

    private static SessionHandshakeHello CreateHello(
        SecureSessionRole role,
        DeviceIdentity identity,
        IEnumerable<ProtocolVersion> supportedVersions,
        EphemeralKeyAgreement agreement)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(SessionHandshakeHello.NonceLength);
        try
        {
            return SessionHandshakeHello.Create(
                role,
                identity.PublicIdentity,
                supportedVersions,
                agreement.ExportSubjectPublicKeyInfo(),
                nonce);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private static async ValueTask<SessionHandshakeAuthentication>
        ReceiveAuthenticationAsync(
            DirectTcpPeerConnection connection,
            CancellationToken cancellationToken)
    {
        byte[] message = await connection.ReceiveHandshakeAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return SessionHandshakeWireCodec.DecodeAuthentication(message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private static async ValueTask<SessionHandshakeHello> ReceiveHelloAsync(
        DirectTcpPeerConnection connection,
        PublicDeviceIdentity expectedIdentity,
        CancellationToken cancellationToken)
    {
        byte[] message = await connection.ReceiveHandshakeAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return SessionHandshakeWireCodec.DecodeHello(message, expectedIdentity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private static async ValueTask SendAsync(
        DirectTcpPeerConnection connection,
        byte[] message,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendHandshakeAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private static AuthenticatedTcpControlConnection Upgrade(
        DirectTcpPeerConnection connection,
        AuthenticatedSession authenticated,
        DeviceId localDeviceId)
    {
        try
        {
            SecureControlChannel channel = connection.UpgradeToSecureControl(
                authenticated.SecureFrames,
                ProtocolFeatures.SupportsLiveRekey(
                    authenticated.ProtocolVersion));
            return new AuthenticatedTcpControlConnection(
                authenticated,
                channel,
                localDeviceId,
                connection.LocalEndPoint,
                connection.RemoteEndPoint);
        }
        catch
        {
            authenticated.Dispose();
            throw;
        }
    }

    private static CancellationTokenSource CreateHandshakeCancellation(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static void ValidateHandshakeTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaximumHandshakeTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"A TCP handshake timeout must be positive and at most {MaximumHandshakeTimeout.TotalMinutes} minutes.");
        }
    }
}

public sealed class IncomingPeerAuthenticationException : Exception
{
    public IncomingPeerAuthenticationException(
        IPEndPoint remoteEndPoint,
        Exception innerException)
        : base("An incoming TCP peer failed authentication.", innerException)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        RemoteEndPoint = remoteEndPoint;
    }

    public IPEndPoint RemoteEndPoint { get; }
}
