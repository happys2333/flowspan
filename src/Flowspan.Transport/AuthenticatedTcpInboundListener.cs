using System.Collections.Immutable;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public enum InboundSessionFailureStage
{
    Authentication,
    Authorization,
    Handler,
    Shutdown,
}

public sealed record InboundSessionFailure(
    InboundSessionFailureStage Stage,
    DeviceId? PeerDeviceId,
    Exception Exception);

public interface IAcceptedAuthenticatedControlSession : IAsyncDisposable
{
    public PublicDeviceIdentity PeerIdentity { get; }

    public ValueTask RunAsync(
        IAuthenticatedControlSessionHandler handler,
        CancellationToken cancellationToken = default);
}

public interface IAuthenticatedControlSessionAcceptor
{
    public ValueTask<IAcceptedAuthenticatedControlSession> AcceptAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AuthenticatedInboundSessionProfile
{
    public const int DefaultMaximumConcurrentSessions = 32;
    public const int MaximumConcurrentSessionsLimit = 128;

    public AuthenticatedInboundSessionProfile(
        CapabilityGrant requiredCapabilities,
        IEnumerable<ProtocolVersion> supportedVersions,
        int maximumConcurrentSessions = DefaultMaximumConcurrentSessions,
        TimeSpan? handshakeTimeout = null,
        CapabilityRequirementMatch capabilityMatch = CapabilityRequirementMatch.All)
    {
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(supportedVersions);
        if (requiredCapabilities.Capabilities.Count == 0)
        {
            throw new ArgumentException(
                "An inbound session must require at least one capability.",
                nameof(requiredCapabilities));
        }

        ImmutableArray<ProtocolVersion> versions = supportedVersions
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (versions.IsDefaultOrEmpty
            || versions.Length > 16
            || versions.Any(static version => version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "An inbound listener must contain 1 to 16 initialized protocol versions.",
                nameof(supportedVersions));
        }

        if (maximumConcurrentSessions is < 1 or > MaximumConcurrentSessionsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentSessions));
        }

        if (!Enum.IsDefined(capabilityMatch))
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityMatch));
        }

        TimeSpan timeout = handshakeTimeout
            ?? AuthenticatedTcpControlConnection.DefaultHandshakeTimeout;
        if (timeout <= TimeSpan.Zero
            || timeout > AuthenticatedTcpControlConnection.MaximumHandshakeTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));
        }

        RequiredCapabilities = requiredCapabilities;
        CapabilityMatch = capabilityMatch;
        SupportedVersions = versions;
        MaximumConcurrentSessions = maximumConcurrentSessions;
        HandshakeTimeout = timeout;
    }

    public TimeSpan HandshakeTimeout { get; }

    public int MaximumConcurrentSessions { get; }

    public CapabilityRequirementMatch CapabilityMatch { get; }

    public CapabilityGrant RequiredCapabilities { get; }

    public ImmutableArray<ProtocolVersion> SupportedVersions { get; }

    internal ValueTask<TrustSessionRegistration?> TryRegisterAsync(
        TrustSessionCoordinator trustSessions,
        DeviceId peerDeviceId,
        IRevocablePeerSession session,
        CancellationToken cancellationToken) => CapabilityMatch switch
        {
            CapabilityRequirementMatch.All => trustSessions.TryRegisterAsync(
                peerDeviceId,
                RequiredCapabilities,
                session,
                cancellationToken),
            CapabilityRequirementMatch.Any => trustSessions.TryRegisterAnyAsync(
                peerDeviceId,
                RequiredCapabilities,
                session,
                cancellationToken),
            _ => throw new InvalidOperationException(
                "The inbound capability match mode is invalid."),
        };
}

public sealed class SystemAuthenticatedControlSessionAcceptor :
    IAuthenticatedControlSessionAcceptor
{
    private readonly DeviceIdentity localIdentity;
    private readonly AuthenticatedInboundSessionProfile profile;
    private readonly TcpListener listener;
    private readonly TrustSessionCoordinator trustSessions;

    public SystemAuthenticatedControlSessionAcceptor(
        TcpListener listener,
        DeviceIdentity localIdentity,
        TrustSessionCoordinator trustSessions,
        AuthenticatedInboundSessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(profile);
        this.listener = listener;
        this.localIdentity = localIdentity;
        this.trustSessions = trustSessions;
        this.profile = profile;
    }

    public async ValueTask<IAcceptedAuthenticatedControlSession> AcceptAsync(
        CancellationToken cancellationToken = default)
    {
        AuthenticatedTcpControlConnection connection =
            await AuthenticatedTcpControlConnection.AcceptAnyTrustedAsync(
                listener,
                localIdentity,
                trustSessions,
                profile.SupportedVersions,
                profile.HandshakeTimeout,
                cancellationToken).ConfigureAwait(false);
        return new AcceptedTcpControlSession(connection);
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

public sealed class AuthenticatedTcpInboundListener
{
    private readonly HashSet<Task> activeSessions = [];
    private readonly IAuthenticatedControlSessionAcceptor acceptor;
    private readonly Lock gate = new();
    private readonly IAuthenticatedControlSessionHandler handler;
    private readonly AuthenticatedInboundSessionProfile profile;
    private readonly TrustSessionCoordinator trustSessions;
    private int running;

    public AuthenticatedTcpInboundListener(
        TcpListener listener,
        DeviceIdentity localIdentity,
        TrustSessionCoordinator trustSessions,
        AuthenticatedInboundSessionProfile profile,
        IAuthenticatedControlSessionHandler handler)
        : this(
            new SystemAuthenticatedControlSessionAcceptor(
                listener,
                localIdentity,
                trustSessions,
                profile),
            trustSessions,
            profile,
            handler)
    {
    }

    public AuthenticatedTcpInboundListener(
        IAuthenticatedControlSessionAcceptor acceptor,
        TrustSessionCoordinator trustSessions,
        AuthenticatedInboundSessionProfile profile,
        IAuthenticatedControlSessionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(acceptor);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(handler);
        this.acceptor = acceptor;
        this.trustSessions = trustSessions;
        this.profile = profile;
        this.handler = handler;
    }

    public event Action<InboundSessionFailure>? SessionFaulted;

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "An authenticated inbound listener can run only one loop at a time.");
        }

        using CancellationTokenSource stop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var slots = new SemaphoreSlim(
            profile.MaximumConcurrentSessions,
            profile.MaximumConcurrentSessions);
        try
        {
            while (true)
            {
                await slots.WaitAsync(stop.Token).ConfigureAwait(false);
                IAcceptedAuthenticatedControlSession accepted;
                try
                {
                    accepted = await acceptor.AcceptAsync(stop.Token)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            "The authenticated session acceptor returned null.");
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    slots.Release();
                    throw;
                }
                catch (IncomingPeerAuthenticationException exception)
                {
                    slots.Release();
                    PublishFailure(new InboundSessionFailure(
                        InboundSessionFailureStage.Authentication,
                        null,
                        exception));
                    continue;
                }
                catch
                {
                    slots.Release();
                    throw;
                }

                Task session = RunTrackedAsync(accepted, slots, stop.Token);
                lock (gate)
                {
                    activeSessions.RemoveWhere(static task => task.IsCompleted);
                    activeSessions.Add(session);
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
                PublishFailure(new InboundSessionFailure(
                    InboundSessionFailureStage.Shutdown,
                    null,
                    exception));
            }

            Task[] drain;
            lock (gate)
            {
                drain = activeSessions.ToArray();
            }

            try
            {
                await Task.WhenAll(drain).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    activeSessions.Clear();
                }

                Volatile.Write(ref running, 0);
            }
        }
    }

    private async Task RunTrackedAsync(
        IAcceptedAuthenticatedControlSession accepted,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunAcceptedAsync(
                accepted,
                trustSessions,
                profile,
                handler,
                PublishFailure,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    internal static async Task RunAcceptedAsync(
        IAcceptedAuthenticatedControlSession accepted,
        TrustSessionCoordinator trustSessions,
        AuthenticatedInboundSessionProfile profile,
        IAuthenticatedControlSessionHandler handler,
        Action<InboundSessionFailure> publishFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(trustSessions);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(publishFailure);
        DeviceId peerDeviceId = accepted.PeerIdentity.DeviceId;
        try
        {
            await using (accepted.ConfigureAwait(false))
            {
                if (!trustSessions.TryGetCurrentTrust(
                        peerDeviceId,
                        out TrustRecord? currentTrust)
                    || !currentTrust.PeerIdentity.HasSameKey(accepted.PeerIdentity))
                {
                    publishFailure(new InboundSessionFailure(
                        InboundSessionFailureStage.Authentication,
                        peerDeviceId,
                        new UnauthorizedAccessException(
                            "The authenticated peer no longer matches current trust.")));
                    return;
                }

                using var revocable = new RevocableInboundSession();
                TrustSessionRegistration? registration;
                try
                {
                    registration = await profile.TryRegisterAsync(
                        trustSessions,
                        peerDeviceId,
                        revocable,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    publishFailure(new InboundSessionFailure(
                        InboundSessionFailureStage.Authorization,
                        peerDeviceId,
                        exception));
                    return;
                }

                if (registration is null)
                {
                    publishFailure(new InboundSessionFailure(
                        InboundSessionFailureStage.Authorization,
                        peerDeviceId,
                        new UnauthorizedAccessException(
                            "The incoming peer is no longer trusted or authorized.")));
                    return;
                }

                await using (registration.ConfigureAwait(false))
                {
                    try
                    {
                        await revocable.RunAsync(
                            accepted,
                            handler,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested
                        || revocable.StopReason is not null)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        publishFailure(new InboundSessionFailure(
                            InboundSessionFailureStage.Handler,
                            peerDeviceId,
                            exception));
                    }
                }
            }
        }
        catch (Exception exception)
        {
            publishFailure(new InboundSessionFailure(
                InboundSessionFailureStage.Shutdown,
                peerDeviceId,
                exception));
        }
    }

    private void PublishFailure(InboundSessionFailure failure)
    {
        foreach (Action<InboundSessionFailure> subscriber in
                 SessionFaulted?.GetInvocationList()
                     .Cast<Action<InboundSessionFailure>>() ?? [])
        {
            try
            {
                subscriber(failure);
            }
            catch
            {
                // Diagnostics must not tear down listener or session cleanup.
            }
        }
    }

    private sealed class RevocableInboundSession : IRevocablePeerSession, IDisposable
    {
        private readonly TaskCompletionSource completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource stop = new();
        private int stopReason = -1;

        public TrustSessionStopReason? StopReason
        {
            get
            {
                int value = Volatile.Read(ref stopReason);
                return value < 0 ? null : (TrustSessionStopReason)value;
            }
        }

        public void Dispose() => stop.Dispose();

        public async ValueTask RunAsync(
            IAcceptedAuthenticatedControlSession accepted,
            IAuthenticatedControlSessionHandler handler,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    stop.Token);
            try
            {
                await accepted.RunAsync(handler, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                completed.TrySetResult();
            }
        }

        public async ValueTask StopAsync(TrustSessionStopReason reason)
        {
            if (!Enum.IsDefined(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            Interlocked.CompareExchange(ref stopReason, (int)reason, -1);
            try
            {
                stop.Cancel();
            }
            catch (AggregateException)
            {
                // The handler still observes cancellation; await its completion.
            }

            await completed.Task.ConfigureAwait(false);
        }
    }
}
