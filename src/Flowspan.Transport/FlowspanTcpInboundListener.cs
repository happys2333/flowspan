using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Transport;

public enum InboundConnectionFailureStage
{
    ProtocolSelection,
    Capacity,
    Pairing,
    Shutdown,
}

public sealed record InboundConnectionFailure(
    InboundConnectionFailureStage Stage,
    IPEndPoint? RemoteEndPoint,
    Exception Exception);

public sealed record InboundPairingCompleted(
    IPEndPoint RemoteEndPoint,
    PairingCeremonyResult Result);

public sealed class FlowspanTcpInboundProfile
{
    public const int DefaultMaximumConcurrentPairings = 1;
    public const int MaximumConcurrentConnectionsLimit = 128;
    public const int MaximumConcurrentPairingsLimit = 8;
    public static readonly TimeSpan DefaultProtocolSelectionTimeout =
        TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaximumProtocolSelectionTimeout =
        TimeSpan.FromMinutes(2);

    public FlowspanTcpInboundProfile(
        AuthenticatedInboundSessionProfile sessionProfile,
        int? maximumConcurrentConnections = null,
        int maximumConcurrentPairings = DefaultMaximumConcurrentPairings,
        TimeSpan? protocolSelectionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(sessionProfile);
        if (maximumConcurrentPairings is < 1
            or > MaximumConcurrentPairingsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentPairings));
        }

        int defaultConnections = Math.Min(
            MaximumConcurrentConnectionsLimit,
            sessionProfile.MaximumConcurrentSessions + maximumConcurrentPairings);
        int connections = maximumConcurrentConnections ?? defaultConnections;
        if (connections < sessionProfile.MaximumConcurrentSessions
            || connections < maximumConcurrentPairings
            || connections > MaximumConcurrentConnectionsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentConnections));
        }

        TimeSpan selectionTimeout = protocolSelectionTimeout
            ?? DefaultProtocolSelectionTimeout;
        if (selectionTimeout <= TimeSpan.Zero
            || selectionTimeout > MaximumProtocolSelectionTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolSelectionTimeout));
        }

        MaximumConcurrentConnections = connections;
        MaximumConcurrentPairings = maximumConcurrentPairings;
        ProtocolSelectionTimeout = selectionTimeout;
        SessionProfile = sessionProfile;
    }

    public int MaximumConcurrentConnections { get; }

    public int MaximumConcurrentPairings { get; }

    public TimeSpan ProtocolSelectionTimeout { get; }

    public AuthenticatedInboundSessionProfile SessionProfile { get; }
}

public sealed class FlowspanTcpInboundListener
{
    private readonly HashSet<Task> activeConnections = [];
    private readonly Lock gate = new();
    private readonly IAuthenticatedControlSessionHandler handler;
    private readonly DeviceIdentity localIdentity;
    private readonly PairingCeremony pairingCeremony;
    private readonly FlowspanTcpInboundProfile profile;
    private readonly TcpListener socket;
    private readonly TimeProvider timeProvider;
    private readonly TrustSessionCoordinator trustSessions;
    private int running;

    public FlowspanTcpInboundListener(
        TcpListener socket,
        DeviceIdentity localIdentity,
        PairingCeremonyProfile pairingProfile,
        IPairingDecisionSource pairingDecisions,
        TrustSessionCoordinator trustSessions,
        FlowspanTcpInboundProfile profile,
        IAuthenticatedControlSessionHandler handler,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(pairingProfile);
        ArgumentNullException.ThrowIfNull(pairingDecisions);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(handler);
        this.socket = socket;
        this.localIdentity = localIdentity;
        this.trustSessions = trustSessions;
        this.profile = profile;
        this.handler = handler;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        pairingCeremony = new PairingCeremony(
            pairingProfile,
            pairingDecisions,
            trustSessions,
            this.timeProvider);
    }

    public event Action<InboundConnectionFailure>? ConnectionFaulted;

    public event Action<InboundPairingCompleted>? PairingCompleted;

    public event Action<InboundSessionFailure>? SessionFaulted;

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A Flowspan TCP listener can run only one loop at a time.");
        }

        using CancellationTokenSource stop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var connectionSlots = new SemaphoreSlim(
            profile.MaximumConcurrentConnections,
            profile.MaximumConcurrentConnections);
        using var pairingSlots = new SemaphoreSlim(
            profile.MaximumConcurrentPairings,
            profile.MaximumConcurrentPairings);
        using var sessionSlots = new SemaphoreSlim(
            profile.SessionProfile.MaximumConcurrentSessions,
            profile.SessionProfile.MaximumConcurrentSessions);
        try
        {
            while (true)
            {
                await connectionSlots.WaitAsync(stop.Token).ConfigureAwait(false);
                DirectTcpPeerConnection connection;
                try
                {
                    connection = await DirectTcpPeerConnection.AcceptAsync(
                        socket,
                        stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    connectionSlots.Release();
                    throw;
                }
                catch
                {
                    connectionSlots.Release();
                    throw;
                }

                Task active = RunTrackedAsync(
                    connection,
                    connectionSlots,
                    pairingSlots,
                    sessionSlots,
                    stop.Token);
                lock (gate)
                {
                    activeConnections.RemoveWhere(static task => task.IsCompleted);
                    activeConnections.Add(active);
                }
            }
        }
        finally
        {
            try
            {
                stop.Cancel();
            }
            catch (AggregateException exception)
            {
                PublishConnectionFailure(new InboundConnectionFailure(
                    InboundConnectionFailureStage.Shutdown,
                    null,
                    exception));
            }

            Task[] drain;
            lock (gate)
            {
                drain = activeConnections.ToArray();
            }

            try
            {
                await Task.WhenAll(drain).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    activeConnections.Clear();
                }

                Volatile.Write(ref running, 0);
            }
        }
    }

    private void PublishConnectionFailure(InboundConnectionFailure failure) =>
        PublishSafely(ConnectionFaulted, failure);

    private void PublishPairingCompleted(InboundPairingCompleted completed) =>
        PublishSafely(PairingCompleted, completed);

    private void PublishSessionFailure(InboundSessionFailure failure) =>
        PublishSafely(SessionFaulted, failure);

    private static void PublishSafely<T>(Action<T>? subscribers, T value)
    {
        foreach (Action<T> subscriber in
                 subscribers?.GetInvocationList().Cast<Action<T>>() ?? [])
        {
            try
            {
                subscriber(value);
            }
            catch
            {
                // Diagnostics must not tear down listener or connection cleanup.
            }
        }
    }

    private async Task RunAuthenticatedAsync(
        DirectTcpPeerConnection connection,
        byte[] initialHello,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        IAcceptedAuthenticatedControlSession accepted;
        try
        {
            AuthenticatedTcpControlConnection authenticated =
                await AuthenticatedTcpControlConnection.AcceptAnyTrustedAsync(
                    connection,
                    initialHello,
                    localIdentity,
                    trustSessions,
                    profile.SessionProfile.SupportedVersions,
                    profile.SessionProfile.HandshakeTimeout,
                    cancellationToken).ConfigureAwait(false);
            accepted = new AcceptedTcpControlSession(authenticated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (IncomingPeerAuthenticationException exception)
        {
            PublishSessionFailure(new InboundSessionFailure(
                InboundSessionFailureStage.Authentication,
                null,
                exception));
            return;
        }
        catch (Exception exception)
        {
            PublishConnectionFailure(new InboundConnectionFailure(
                InboundConnectionFailureStage.Shutdown,
                remoteEndPoint,
                exception));
            return;
        }

        await AuthenticatedTcpInboundListener.RunAcceptedAsync(
            accepted,
            trustSessions,
            profile.SessionProfile,
            handler,
            PublishSessionFailure,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunConnectionAsync(
        DirectTcpPeerConnection connection,
        SemaphoreSlim pairingSlots,
        SemaphoreSlim sessionSlots,
        CancellationToken cancellationToken)
    {
        IPEndPoint remoteEndPoint = connection.RemoteEndPoint;
        byte[]? initialMessage = null;
        try
        {
            {
                using var selectionDeadline = new CancellationTokenSource(
                    profile.ProtocolSelectionTimeout,
                    timeProvider);
                using CancellationTokenSource selection =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        selectionDeadline.Token);
                try
                {
                    initialMessage = await connection.ReceiveHandshakeAsync(
                        selection.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (
                    !cancellationToken.IsCancellationRequested
                    && selectionDeadline.IsCancellationRequested)
                {
                    PublishConnectionFailure(new InboundConnectionFailure(
                        InboundConnectionFailureStage.ProtocolSelection,
                        remoteEndPoint,
                        new TimeoutException(
                            "The inbound protocol selection timed out.",
                            exception)));
                    return;
                }
            }

            InboundHandshakeProtocol protocol;
            try
            {
                protocol = HandshakeProtocolClassifier.ClassifyInitialHello(
                    initialMessage);
            }
            catch (InvalidDataException exception)
            {
                PublishConnectionFailure(new InboundConnectionFailure(
                    InboundConnectionFailureStage.ProtocolSelection,
                    remoteEndPoint,
                    exception));
                return;
            }

            if (protocol == InboundHandshakeProtocol.Pairing)
            {
                if (!pairingSlots.Wait(0, cancellationToken))
                {
                    PublishConnectionFailure(new InboundConnectionFailure(
                        InboundConnectionFailureStage.Capacity,
                        remoteEndPoint,
                        new InvalidOperationException(
                            "The inbound pairing capacity is exhausted.")));
                    return;
                }

                try
                {
                    DirectTcpPairingChannel channel =
                        DirectTcpPairingChannel.FromAcceptedConnection(
                            connection,
                            initialMessage);
                    initialMessage = null;
                    try
                    {
                        PairingCeremonyResult result =
                            await pairingCeremony.RunResponderAsync(
                                channel,
                                localIdentity,
                                cancellationToken).ConfigureAwait(false);
                        PublishPairingCompleted(new InboundPairingCompleted(
                            remoteEndPoint,
                            result));
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        PublishConnectionFailure(new InboundConnectionFailure(
                            InboundConnectionFailureStage.Pairing,
                            remoteEndPoint,
                            exception));
                    }
                }
                finally
                {
                    pairingSlots.Release();
                }

                return;
            }

            if (!sessionSlots.Wait(0, cancellationToken))
            {
                PublishConnectionFailure(new InboundConnectionFailure(
                    InboundConnectionFailureStage.Capacity,
                    remoteEndPoint,
                    new InvalidOperationException(
                        "The inbound authenticated-session capacity is exhausted.")));
                return;
            }

            try
            {
                byte[] sessionHello = initialMessage;
                initialMessage = null;
                await RunAuthenticatedAsync(
                    connection,
                    sessionHello,
                    remoteEndPoint,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sessionSlots.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Listener shutdown owns cancellation and drain completion.
        }
        catch (Exception exception)
        {
            PublishConnectionFailure(new InboundConnectionFailure(
                InboundConnectionFailureStage.ProtocolSelection,
                remoteEndPoint,
                exception));
        }
        finally
        {
            if (initialMessage is not null)
            {
                CryptographicOperations.ZeroMemory(initialMessage);
            }

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                PublishConnectionFailure(new InboundConnectionFailure(
                    InboundConnectionFailureStage.Shutdown,
                    remoteEndPoint,
                    exception));
            }
        }
    }

    private async Task RunTrackedAsync(
        DirectTcpPeerConnection connection,
        SemaphoreSlim connectionSlots,
        SemaphoreSlim pairingSlots,
        SemaphoreSlim sessionSlots,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunConnectionAsync(
                connection,
                pairingSlots,
                sessionSlots,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionSlots.Release();
        }
    }

    private sealed class AcceptedTcpControlSession(
        AuthenticatedTcpControlConnection connection) :
        IAcceptedAuthenticatedControlSession
    {
        public PublicDeviceIdentity PeerIdentity => connection.PeerIdentity;

        public ValueTask DisposeAsync() => connection.DisposeAsync();

        public ValueTask RunAsync(
            IAuthenticatedControlSessionHandler handler,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return handler.RunAsync(connection, cancellationToken);
        }
    }
}
