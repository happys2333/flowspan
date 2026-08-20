using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

public enum DesktopTrustedPeerConnectionState
{
    WaitingForPeer,
    WaitingForInbound,
    Authenticating,
    AuthenticatedIdle,
    Retrying,
    CapabilityRequired,
    PermanentlyBlocked,
    Unavailable,
}

public sealed record DesktopTrustedPeerConnectionSnapshot(
    DeviceId DeviceId,
    string DisplayName,
    string ExpectedFingerprint,
    DesktopTrustedPeerConnectionState State,
    TimeSpan? RetryDelay,
    PeerReconnectStopReason? StopReason,
    string? ConflictingFingerprint)
{
    public ImmutableArray<ProtocolVersion> ActiveProtocolVersions { get; init; } = [];

    public bool HasIdentityWarning =>
        ConflictingFingerprint is not null
        || StopReason == PeerReconnectStopReason.CandidateIdentityChanged;

    public bool IsLegacyCompatibilityMode =>
        State == DesktopTrustedPeerConnectionState.AuthenticatedIdle
        && ActiveProtocolVersions.Any(static version =>
            version < ProtocolFeatures.SecureSessionFinishedMinimumVersion);

    public bool IsReconnectAtKeyLimitMode =>
        State == DesktopTrustedPeerConnectionState.AuthenticatedIdle
        && !IsLegacyCompatibilityMode
        && ActiveProtocolVersions.Any(static version =>
            ProtocolFeatures.RequiresSecureSessionFinished(version)
            && !ProtocolFeatures.SupportsLiveRekey(version));

    public string StatusLabel => State switch
    {
        DesktopTrustedPeerConnectionState.WaitingForPeer =>
            DesktopText.Get("TrustedConnection_WaitingForPeerStatus"),
        DesktopTrustedPeerConnectionState.WaitingForInbound =>
            DesktopText.Get("TrustedConnection_WaitingForInboundStatus"),
        DesktopTrustedPeerConnectionState.Authenticating => DesktopText.Get(
            "TrustedConnection_AuthenticatingStatus"),
        DesktopTrustedPeerConnectionState.AuthenticatedIdle
            when IsLegacyCompatibilityMode =>
                DesktopText.Get("TrustedConnection_LegacyIdleStatus"),
        DesktopTrustedPeerConnectionState.AuthenticatedIdle
            when IsReconnectAtKeyLimitMode =>
                DesktopText.Get("TrustedConnection_KeyLimitIdleStatus"),
        DesktopTrustedPeerConnectionState.AuthenticatedIdle =>
            DesktopText.Get("TrustedConnection_IdleStatus"),
        DesktopTrustedPeerConnectionState.Retrying => DesktopText.Get(
            "TrustedConnection_RetryingStatus"),
        DesktopTrustedPeerConnectionState.CapabilityRequired =>
            DesktopText.Get("TrustedConnection_CapabilityRequiredStatus"),
        DesktopTrustedPeerConnectionState.PermanentlyBlocked => StopReason switch
        {
            PeerReconnectStopReason.CandidateIdentityChanged =>
                DesktopText.Get("TrustedConnection_IdentityChangedStatus"),
            PeerReconnectStopReason.PeerNotTrusted => DesktopText.Get(
                "TrustedConnection_TrustRemovedStatus"),
            PeerReconnectStopReason.CapabilityDenied =>
                DesktopText.Get("TrustedConnection_CapabilityDeniedStatus"),
            PeerReconnectStopReason.ProtocolIncompatible =>
                DesktopText.Get("TrustedConnection_ProtocolIncompatibleStatus"),
            PeerReconnectStopReason.AuthenticationFailed =>
                DesktopText.Get("TrustedConnection_AuthenticationFailedStatus"),
            _ => DesktopText.Get("TrustedConnection_SecurityPolicyStatus"),
        },
        DesktopTrustedPeerConnectionState.Unavailable =>
            DesktopText.Get("TrustedConnection_UnavailableStatus"),
        _ => throw new InvalidOperationException(
            "The trusted-peer connection state is not supported."),
    };

    public string StatusDescription => State switch
    {
        DesktopTrustedPeerConnectionState.WaitingForPeer =>
            DesktopText.Get("TrustedConnection_WaitingForPeerDescription"),
        DesktopTrustedPeerConnectionState.WaitingForInbound =>
            DesktopText.Get("TrustedConnection_WaitingForInboundDescription"),
        DesktopTrustedPeerConnectionState.Authenticating =>
            DesktopText.Get("TrustedConnection_AuthenticatingDescription"),
        DesktopTrustedPeerConnectionState.AuthenticatedIdle
            when IsLegacyCompatibilityMode =>
                DesktopText.Format(
                    "TrustedConnection_LegacyIdleDescription",
                    string.Join(
                        DesktopText.Get("TrustedConnection_ProtocolSeparator"),
                        ActiveProtocolVersions)),
        DesktopTrustedPeerConnectionState.AuthenticatedIdle
            when IsReconnectAtKeyLimitMode =>
                DesktopText.Format(
                    "TrustedConnection_KeyLimitIdleDescription",
                    string.Join(
                        DesktopText.Get("TrustedConnection_ProtocolSeparator"),
                        ActiveProtocolVersions)),
        DesktopTrustedPeerConnectionState.AuthenticatedIdle =>
            DesktopText.Get("TrustedConnection_IdleDescription"),
        DesktopTrustedPeerConnectionState.Retrying => RetryDelay is TimeSpan delay
            ? DesktopText.Format(
                "TrustedConnection_RetryingDelayDescription",
                delay.TotalSeconds)
            : DesktopText.Get("TrustedConnection_RetryingDescription"),
        DesktopTrustedPeerConnectionState.CapabilityRequired =>
            DesktopText.Get("TrustedConnection_CapabilityRequiredDescription"),
        DesktopTrustedPeerConnectionState.PermanentlyBlocked =>
            DesktopText.Get("TrustedConnection_BlockedDescription"),
        DesktopTrustedPeerConnectionState.Unavailable =>
            DesktopText.Get("TrustedConnection_UnavailableDescription"),
        _ => string.Empty,
    };

    public string IdentityWarning => HasIdentityWarning
        ? ConflictingFingerprint is null
            ? DesktopText.Get("TrustedConnection_AuthenticationIdentityWarning")
            : DesktopText.Get("TrustedConnection_DiscoveryIdentityWarning")
        : string.Empty;
}

internal readonly record struct DesktopPeerReconnectProgress(
    DesktopTrustedPeerConnectionState State,
    TimeSpan? RetryDelay)
{
    public static DesktopPeerReconnectProgress WaitingForPeer { get; } =
        new(DesktopTrustedPeerConnectionState.WaitingForPeer, null);

    public static DesktopPeerReconnectProgress Authenticating { get; } =
        new(DesktopTrustedPeerConnectionState.Authenticating, null);

    public static DesktopPeerReconnectProgress Retrying(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);

        return new DesktopPeerReconnectProgress(
            DesktopTrustedPeerConnectionState.Retrying,
            delay);
    }
}

internal interface IDesktopPeerReconnectLoop : IAsyncDisposable
{
    public ValueTask<PeerReconnectStopReason> RunAsync(
        CancellationToken cancellationToken = default);

    public void SignalDiscoveryChanged();
}

internal interface IDesktopPeerReconnectLoopFactory
{
    public IDesktopPeerReconnectLoop Create(
        TrustedPeerSnapshot peer,
        Action<DesktopPeerReconnectProgress> report,
        IAuthenticatedControlSessionHandler idleHandler);
}

internal sealed class DesktopTrustedPeerConnectionCoordinator : IAsyncDisposable
{
    private readonly Func<ImmutableArray<UnverifiedPairingCandidate>> getCandidates;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly DeviceId localDeviceId;
    private readonly IDesktopPeerReconnectLoopFactory loopFactory;
    private readonly Dictionary<DeviceId, PeerState> peers = [];
    private readonly TrustSessionCoordinator trust;
    private int disposed;
    private int started;

    public DesktopTrustedPeerConnectionCoordinator(
        DeviceId localDeviceId,
        TrustSessionCoordinator trust,
        Func<ImmutableArray<UnverifiedPairingCandidate>> getCandidates,
        IDesktopPeerReconnectLoopFactory loopFactory,
        IAuthenticatedControlSessionHandler? sessionHandler = null)
    {
        ArgumentNullException.ThrowIfNull(localDeviceId);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(getCandidates);
        ArgumentNullException.ThrowIfNull(loopFactory);
        this.localDeviceId = localDeviceId;
        this.trust = trust;
        this.getCandidates = getCandidates;
        this.loopFactory = loopFactory;
        SessionHandler = new TrackingSessionHandler(
            this,
            sessionHandler ?? IdleSessionHandler.Instance);
    }

    public event Action? Changed;

    public IAuthenticatedControlSessionHandler SessionHandler { get; }

    public void Cancel()
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            lifetimeCancellation.Cancel();
        }
    }

    public ImmutableArray<DesktopTrustedPeerConnectionSnapshot> GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (gate)
        {
            return peers.Values
                .OrderBy(
                    static peer => peer.Peer.DeviceId.ToString(),
                    StringComparer.Ordinal)
                .Select(static peer => peer.CreateSnapshot())
                .ToImmutableArray();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Trusted peer connections can be started only once.");
        }

        ImmutableArray<TrustedPeerSnapshot> current = trust.GetTrustedPeers();
        List<(PeerState State, IDesktopPeerReconnectLoop Loop)> loops = [];
        lock (gate)
        {
            foreach (TrustedPeerSnapshot peer in current)
            {
                PeerState state = CreatePeerState(peer);
                peers.Add(peer.DeviceId, state);
                if (ShouldStartConnector(peer))
                {
                    IDesktopPeerReconnectLoop loop = CreateLoop(state);
                    loops.Add((state, loop));
                }
            }
        }

        foreach ((PeerState state, IDesktopPeerReconnectLoop loop) in loops)
        {
            StartLoop(state, loop);
        }

        UpdateCandidateWarnings(signalLoops: false);
        PublishChanged();
    }

    public async ValueTask RefreshTrustAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        await lifecycleGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ImmutableArray<TrustedPeerSnapshot> current = trust.GetTrustedPeers();
            var currentById = current.ToDictionary(static peer => peer.DeviceId);
            var stopping = new List<DetachedLoop>();
            lock (gate)
            {
                foreach (DeviceId removed in peers.Keys
                             .Where(deviceId => !currentById.ContainsKey(deviceId))
                             .ToArray())
                {
                    PeerState state = peers[removed];
                    DetachLoop(state, stopping);
                    peers.Remove(removed);
                }

                foreach (TrustedPeerSnapshot snapshot in current)
                {
                    if (!peers.TryGetValue(snapshot.DeviceId, out PeerState? state)
                        || !StringComparer.Ordinal.Equals(
                            state.Peer.Fingerprint,
                            snapshot.Fingerprint))
                    {
                        if (state is not null)
                        {
                            DetachLoop(state, stopping);
                        }

                        state = CreatePeerState(snapshot);
                        peers[snapshot.DeviceId] = state;
                        continue;
                    }

                    bool wasEligible = IsConnectorEligible(state.Peer);
                    state.Peer = snapshot;
                    bool isEligible = IsConnectorEligible(snapshot);
                    if (!isEligible)
                    {
                        DetachLoop(state, stopping);
                        state.State = HasControlChannelCapability(snapshot)
                            ? DesktopTrustedPeerConnectionState.WaitingForInbound
                            : DesktopTrustedPeerConnectionState.CapabilityRequired;
                        state.RetryDelay = null;
                        state.StopReason = null;
                        state.LoopCompleted = false;
                    }
                    else if (!wasEligible)
                    {
                        state.State = DesktopTrustedPeerConnectionState.WaitingForPeer;
                        state.RetryDelay = null;
                        state.StopReason = null;
                        state.LoopCompleted = false;
                    }
                }
            }

            await DisposeDetachedAsync(stopping).ConfigureAwait(false);

            var starting = new List<(PeerState State, IDesktopPeerReconnectLoop Loop)>();
            lock (gate)
            {
                if (Volatile.Read(ref disposed) == 0)
                {
                    foreach (PeerState state in peers.Values.Where(state =>
                                 ShouldStartConnector(state.Peer)
                                 && state.Loop is null
                                 && !state.LoopCompleted))
                    {
                        IDesktopPeerReconnectLoop loop = CreateLoop(state);
                        starting.Add((state, loop));
                    }
                }
            }

            foreach ((PeerState state, IDesktopPeerReconnectLoop loop) in starting)
            {
                StartLoop(state, loop);
            }

            NotifyCandidatesChanged();
            PublishChanged();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public void NotifyCandidatesChanged()
    {
        UpdateCandidateWarnings(signalLoops: true);
    }

    private void UpdateCandidateWarnings(bool signalLoops)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        ImmutableArray<UnverifiedPairingCandidate> candidates;
        try
        {
            candidates = getCandidates();
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var loops = new List<IDesktopPeerReconnectLoop>();
        bool changed = false;
        lock (gate)
        {
            foreach (UnverifiedPairingCandidate candidate in candidates)
            {
                if (!peers.TryGetValue(candidate.Offer.DeviceId, out PeerState? state)
                    || StringComparer.Ordinal.Equals(
                        state.Peer.Fingerprint,
                        candidate.Offer.IdentityFingerprint)
                    || state.ConflictingFingerprint is not null)
                {
                    continue;
                }

                state.ConflictingFingerprint = candidate.Offer.IdentityFingerprint;
                changed = true;
            }

            if (signalLoops)
            {
                loops.AddRange(peers.Values
                    .Where(static state => state.ActiveSessions == 0)
                    .Select(static state => state.Loop)
                    .Where(static loop => loop is not null)
                    .Cast<IDesktopPeerReconnectLoop>());
            }
        }

        foreach (IDesktopPeerReconnectLoop loop in loops)
        {
            loop.SignalDiscoveryChanged();
        }

        if (changed)
        {
            PublishChanged();
        }
    }

    internal IDisposable TrackAuthenticatedSession(
        DeviceId peerDeviceId,
        ProtocolVersion protocolVersion)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        if (protocolVersion.Major < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                "An authenticated session requires an initialized protocol version.");
        }

        lock (gate)
        {
            if (!peers.TryGetValue(peerDeviceId, out PeerState? state))
            {
                if (!trust.TryGetCurrentTrust(
                        peerDeviceId,
                        out TrustRecord? trustRecord))
                {
                    throw new InvalidOperationException(
                        "An authenticated session has no current Trust Record.");
                }

                var snapshot = new TrustedPeerSnapshot(
                    peerDeviceId,
                    trustRecord.PeerIdentity.DisplayName,
                    trustRecord.PeerIdentity.Fingerprint,
                    trustRecord.VerifiedAt,
                    trustRecord.GrantedCapabilities);
                state = CreatePeerState(snapshot);
                peers.Add(peerDeviceId, state);
            }

            state.ActiveSessions++;
            state.ActiveProtocolVersions[protocolVersion] =
                state.ActiveProtocolVersions.GetValueOrDefault(protocolVersion) + 1;
        }

        PublishChanged();
        return new AuthenticatedSessionLease(
            this,
            peerDeviceId,
            protocolVersion);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var stopping = new List<DetachedLoop>();
            lock (gate)
            {
                foreach (PeerState state in peers.Values)
                {
                    DetachLoop(state, stopping);
                }

                peers.Clear();
            }

            await DisposeDetachedAsync(stopping).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
            lifetimeCancellation.Dispose();
        }
    }

    private PeerState CreatePeerState(TrustedPeerSnapshot peer) => new(peer)
    {
        State = HasControlChannelCapability(peer)
            ? IsLocalConnector(peer.DeviceId)
                ? DesktopTrustedPeerConnectionState.WaitingForPeer
                : DesktopTrustedPeerConnectionState.WaitingForInbound
            : DesktopTrustedPeerConnectionState.CapabilityRequired,
    };

    private IDesktopPeerReconnectLoop CreateLoop(PeerState state)
    {
        IDesktopPeerReconnectLoop loop = loopFactory.Create(
            state.Peer,
            progress => ApplyProgress(
                state.Peer.DeviceId,
                state.Peer.Fingerprint,
                progress),
            SessionHandler)
            ?? throw new InvalidOperationException(
                "The desktop reconnect loop factory returned null.");
        state.Loop = loop;
        return loop;
    }

    private void StartLoop(PeerState state, IDesktopPeerReconnectLoop loop)
    {
        Task task = RunLoopAsync(
            state.Peer.DeviceId,
            state.Peer.Fingerprint,
            loop);
        lock (gate)
        {
            if (ReferenceEquals(state.Loop, loop))
            {
                state.LoopTask = task;
            }
        }
    }

    private async Task RunLoopAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        IDesktopPeerReconnectLoop loop)
    {
        PeerReconnectStopReason? stopReason = null;
        bool unavailable = false;
        try
        {
            stopReason = await loop.RunAsync(lifetimeCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
            return;
        }
        catch (Exception)
        {
            unavailable = true;
        }

        bool changed = false;
        lock (gate)
        {
            if (peers.TryGetValue(peerDeviceId, out PeerState? state)
                && StringComparer.Ordinal.Equals(
                    state.Peer.Fingerprint,
                    expectedFingerprint)
                && ReferenceEquals(state.Loop, loop))
            {
                state.Loop = null;
                state.LoopTask = null;
                state.LoopCompleted = true;
                state.RetryDelay = null;
                state.StopReason = stopReason;
                state.State = unavailable
                    ? DesktopTrustedPeerConnectionState.Unavailable
                    : DesktopTrustedPeerConnectionState.PermanentlyBlocked;
                changed = true;
            }
        }

        if (changed)
        {
            PublishChanged();
        }
    }

    private void ApplyProgress(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        DesktopPeerReconnectProgress progress)
    {
        bool changed = false;
        lock (gate)
        {
            if (peers.TryGetValue(peerDeviceId, out PeerState? state)
                && StringComparer.Ordinal.Equals(
                    state.Peer.Fingerprint,
                    expectedFingerprint)
                && state.Loop is not null)
            {
                state.State = progress.State;
                state.RetryDelay = progress.RetryDelay;
                state.StopReason = null;
                changed = true;
            }
        }

        if (changed)
        {
            PublishChanged();
        }
    }

    private void ReleaseAuthenticatedSession(
        DeviceId peerDeviceId,
        ProtocolVersion protocolVersion)
    {
        bool changed = false;
        lock (gate)
        {
            if (peers.TryGetValue(peerDeviceId, out PeerState? state)
                && state.ActiveSessions > 0)
            {
                state.ActiveSessions--;
                int versionSessions =
                    state.ActiveProtocolVersions.GetValueOrDefault(protocolVersion);
                if (versionSessions <= 1)
                {
                    state.ActiveProtocolVersions.Remove(protocolVersion);
                }
                else
                {
                    state.ActiveProtocolVersions[protocolVersion] =
                        versionSessions - 1;
                }

                changed = true;
            }
        }

        if (changed)
        {
            PublishChanged();
        }
    }

    private bool ShouldStartConnector(TrustedPeerSnapshot peer) =>
        IsConnectorEligible(peer);

    private bool IsConnectorEligible(TrustedPeerSnapshot peer) =>
        HasControlChannelCapability(peer)
        && IsLocalConnector(peer.DeviceId);

    private static bool HasControlChannelCapability(TrustedPeerSnapshot peer) =>
        peer.GrantedCapabilities.Allows(Capability.ActivityOffer)
        || peer.GrantedCapabilities.Allows(Capability.ActivityReceive)
        || peer.GrantedCapabilities.Allows(Capability.ActivityReplace)
        || peer.GrantedCapabilities.Allows(Capability.ActivitySwap)
        || peer.GrantedCapabilities.Allows(Capability.SceneApply)
        || peer.GrantedCapabilities.Allows(Capability.MirrorView)
        || peer.GrantedCapabilities.Allows(Capability.MirrorDrive);

    private bool IsLocalConnector(DeviceId peerDeviceId) =>
        StringComparer.Ordinal.Compare(
            localDeviceId.ToString(),
            peerDeviceId.ToString()) < 0;

    private static void DetachLoop(
        PeerState state,
        ICollection<DetachedLoop> stopping)
    {
        if (state.Loop is not null)
        {
            stopping.Add(new DetachedLoop(state.Loop, state.LoopTask));
            state.Loop = null;
            state.LoopTask = null;
        }
    }

    private static async ValueTask DisposeDetachedAsync(
        IEnumerable<DetachedLoop> stopping)
    {
        var failures = new List<Exception>();
        foreach (DetachedLoop detached in stopping)
        {
            try
            {
                await detached.Loop.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (detached.Task is not null)
            {
                try
                {
                    await detached.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "One or more trusted-peer reconnect loops failed to close.",
                failures);
        }
    }

    private void PublishChanged()
    {
        foreach (Action subscriber in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                subscriber();
            }
            catch
            {
                // Presentation observers cannot own connection lifetime.
            }
        }
    }

    private sealed class PeerState(TrustedPeerSnapshot peer)
    {
        public Dictionary<ProtocolVersion, int> ActiveProtocolVersions { get; } = [];

        public int ActiveSessions { get; set; }

        public string? ConflictingFingerprint { get; set; }

        public IDesktopPeerReconnectLoop? Loop { get; set; }

        public bool LoopCompleted { get; set; }

        public Task? LoopTask { get; set; }

        public TrustedPeerSnapshot Peer { get; set; } = peer;

        public TimeSpan? RetryDelay { get; set; }

        public DesktopTrustedPeerConnectionState State { get; set; }

        public PeerReconnectStopReason? StopReason { get; set; }

        public DesktopTrustedPeerConnectionSnapshot CreateSnapshot() => new(
                Peer.DeviceId,
                Peer.DisplayName,
                Peer.Fingerprint,
                ActiveSessions > 0
                    ? DesktopTrustedPeerConnectionState.AuthenticatedIdle
                    : State,
                ActiveSessions > 0 ? null : RetryDelay,
                StopReason,
                ConflictingFingerprint)
        {
            ActiveProtocolVersions = ActiveProtocolVersions.Keys
                    .Order()
                    .ToImmutableArray(),
        };
    }

    private sealed class TrackingSessionHandler(
        DesktopTrustedPeerConnectionCoordinator owner,
        IAuthenticatedControlSessionHandler inner) :
        IAuthenticatedControlSessionHandler
    {
        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            using IDisposable lease = owner.TrackAuthenticatedSession(
                connection.PeerIdentity.DeviceId,
                connection.ProtocolVersion);
            await inner.RunAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class IdleSessionHandler : IAuthenticatedControlSessionHandler
    {
        private IdleSessionHandler()
        {
        }

        public static IdleSessionHandler Instance { get; } = new();

        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            try
            {
                _ = await connection.ReceiveAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or System.Net.Sockets.SocketException)
            {
                return;
            }

            throw new InvalidDataException(
                "The desktop idle channel received a message before an Activity handler was available.");
        }
    }

    private sealed class AuthenticatedSessionLease(
        DesktopTrustedPeerConnectionCoordinator owner,
        DeviceId peerDeviceId,
        ProtocolVersion protocolVersion) : IDisposable
    {
        private DesktopTrustedPeerConnectionCoordinator? owner = owner;

        public void Dispose()
        {
            DesktopTrustedPeerConnectionCoordinator? current =
                Interlocked.Exchange(ref owner, null);
            current?.ReleaseAuthenticatedSession(peerDeviceId, protocolVersion);
        }
    }

    private sealed record DetachedLoop(
        IDesktopPeerReconnectLoop Loop,
        Task? Task);
}

internal sealed class DesktopTrustedPeerCandidateSource :
    IPeerConnectionCandidateSource
{
    private readonly Func<ImmutableArray<UnverifiedPairingCandidate>> getCandidates;
    private readonly Lock gate = new();
    private readonly Dictionary<DeviceId, int> nextCandidate = [];
    private readonly TimeProvider timeProvider;
    private readonly TrustSessionCoordinator trust;

    public DesktopTrustedPeerCandidateSource(
        TrustSessionCoordinator trust,
        Func<ImmutableArray<UnverifiedPairingCandidate>> getCandidates,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(getCandidates);
        this.trust = trust;
        this.getCandidates = getCandidates;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        candidate = null;
        if (!trust.TryGetCurrentTrust(peerDeviceId, out TrustRecord? trustRecord))
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var verified = new List<VerifiedPeerConnectionCandidate>();
        foreach (UnverifiedPairingCandidate observed in getCandidates()
                     .Where(item => item.Offer.DeviceId == peerDeviceId
                         && item.TrustState == PairingCandidateTrustState.AlreadyPaired
                         && StringComparer.Ordinal.Equals(
                             item.Offer.IdentityFingerprint,
                             trustRecord.PeerIdentity.Fingerprint))
                     .OrderBy(
                         static item => item.EndPoint.Address.AddressFamily)
                     .ThenBy(
                         static item => Convert.ToHexString(
                             item.EndPoint.Address.GetAddressBytes()),
                         StringComparer.Ordinal)
                     .ThenBy(static item => item.EndPoint.Port)
                     .ThenBy(
                         static item => item.InstanceName,
                         StringComparer.Ordinal))
        {
            try
            {
                var identity = new PublicDeviceIdentity(
                    observed.Offer.DeviceId,
                    observed.Offer.DisplayName,
                    trustRecord.PeerIdentity.ExportSubjectPublicKeyInfo());
                verified.Add(VerifiedPeerConnectionCandidate.Create(
                    observed.EndPoint,
                    observed.Offer,
                    identity,
                    now));
            }
            catch (Exception exception) when (
                exception is ArgumentException or CryptographicException)
            {
                // A matching fingerprint is not sufficient; signature verification is required.
            }
        }

        if (verified.Count == 0)
        {
            lock (gate)
            {
                nextCandidate.Remove(peerDeviceId);
            }

            return false;
        }

        lock (gate)
        {
            int index = nextCandidate.TryGetValue(peerDeviceId, out int current)
                ? current % verified.Count
                : 0;
            candidate = verified[index];
            nextCandidate[peerDeviceId] = (index + 1) % verified.Count;
        }

        return true;
    }
}

internal sealed class SystemDesktopPeerReconnectLoopFactory :
    IDesktopPeerReconnectLoopFactory
{
    private readonly IPeerConnectionCandidateSource candidates;
    private readonly IAuthenticatedTcpConnector connector;
    private readonly Func<IReconnectDelay> createDelay;
    private readonly DeviceIdentity localIdentity;
    private readonly INetworkChangeSource networkChanges;
    private readonly TrustSessionCoordinator trust;

    public SystemDesktopPeerReconnectLoopFactory(
        DeviceIdentity localIdentity,
        TrustSessionCoordinator trust,
        IPeerConnectionCandidateSource candidates,
        IAuthenticatedTcpConnector? connector = null,
        INetworkChangeSource? networkChanges = null,
        Func<IReconnectDelay>? createDelay = null)
    {
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(candidates);
        this.localIdentity = localIdentity;
        this.trust = trust;
        this.candidates = candidates;
        this.connector = connector ?? new SystemAuthenticatedTcpConnector();
        this.networkChanges = networkChanges ?? new SystemNetworkChangeSource();
        this.createDelay = createDelay ?? (static () => new SystemReconnectDelay());
    }

    public IDesktopPeerReconnectLoop Create(
        TrustedPeerSnapshot peer,
        Action<DesktopPeerReconnectProgress> report,
        IAuthenticatedControlSessionHandler idleHandler) => new SystemLoop(
            peer,
            report,
            idleHandler,
            localIdentity,
            trust,
            candidates,
            connector,
            networkChanges,
            createDelay());

    private sealed class SystemLoop : IDesktopPeerReconnectLoop
    {
        private readonly DesktopReconnectChangeSource changes;
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private readonly PeerReconnectSupervisor supervisor;
        private readonly TaskCompletionSource runCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeSession;
        private int disposed;
        private int running;

        public SystemLoop(
            TrustedPeerSnapshot peer,
            Action<DesktopPeerReconnectProgress> report,
            IAuthenticatedControlSessionHandler idleHandler,
            DeviceIdentity localIdentity,
            TrustSessionCoordinator trust,
            IPeerConnectionCandidateSource candidates,
            IAuthenticatedTcpConnector connector,
            INetworkChangeSource networkChanges,
            IReconnectDelay delay)
        {
            ArgumentNullException.ThrowIfNull(peer);
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(idleHandler);
            ArgumentNullException.ThrowIfNull(delay);
            changes = new DesktopReconnectChangeSource(networkChanges);
            var reportingCandidates = new ReportingCandidateSource(
                peer.DeviceId,
                candidates,
                report);
            var reportingHandler = new ReportingSessionHandler(
                idleHandler,
                active => Volatile.Write(ref activeSession, active ? 1 : 0));
            var profile = new AuthenticatedPeerSessionProfile(
                peer.DeviceId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive,
                    Capability.ActivityReplace,
                    Capability.ActivitySwap,
                    Capability.SceneApply,
                    Capability.MirrorView,
                    Capability.MirrorDrive),
                ProtocolFeatures.ProductionSupportedVersions,
                capabilityMatch: CapabilityRequirementMatch.Any);
            var attempt = new AuthenticatedTcpPeerSessionAttempt(
                profile,
                localIdentity,
                trust,
                reportingCandidates,
                connector,
                reportingHandler);
            supervisor = new PeerReconnectSupervisor(
                attempt,
                changes,
                new ReportingDelay(delay, report),
                new ReconnectBackoff(
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(30),
                    jitterFraction: 0.2),
                Random.Shared.NextDouble);
        }

        public async ValueTask<PeerReconnectStopReason> RunAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "A desktop reconnect loop can run only once.");
            }

            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token);
            try
            {
                return await supervisor.RunAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                runCompleted.TrySetResult();
            }
        }

        public void SignalDiscoveryChanged()
        {
            if (Volatile.Read(ref disposed) == 0
                && Volatile.Read(ref activeSession) == 0)
            {
                changes.Signal();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            lifetimeCancellation.Cancel();
            if (Volatile.Read(ref running) != 0)
            {
                await runCompleted.Task.ConfigureAwait(false);
            }

            lifetimeCancellation.Dispose();
        }
    }

    private sealed class ReportingCandidateSource(
        DeviceId peerDeviceId,
        IPeerConnectionCandidateSource inner,
        Action<DesktopPeerReconnectProgress> report) :
        IPeerConnectionCandidateSource
    {
        public bool TryGet(
            DeviceId requestedPeer,
            [NotNullWhen(true)] out VerifiedPeerConnectionCandidate? candidate)
        {
            if (requestedPeer != peerDeviceId)
            {
                candidate = null;
                return false;
            }

            bool found = inner.TryGet(requestedPeer, out candidate);
            report(found
                ? DesktopPeerReconnectProgress.Authenticating
                : DesktopPeerReconnectProgress.WaitingForPeer);
            return found;
        }
    }

    private sealed class ReportingDelay(
        IReconnectDelay inner,
        Action<DesktopPeerReconnectProgress> report) : IReconnectDelay
    {
        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            report(DesktopPeerReconnectProgress.Retrying(delay));
            return inner.WaitAsync(delay, cancellationToken);
        }
    }

    private sealed class ReportingSessionHandler(
        IAuthenticatedControlSessionHandler inner,
        Action<bool> setActive) : IAuthenticatedControlSessionHandler
    {
        public async ValueTask RunAsync(
            AuthenticatedTcpControlConnection connection,
            CancellationToken cancellationToken = default)
        {
            setActive(true);
            try
            {
                await inner.RunAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                setActive(false);
            }
        }
    }
}

internal sealed class DesktopReconnectChangeSource(
    INetworkChangeSource networkChanges) : INetworkChangeSource
{
    private readonly Lock gate = new();
    private Action? manualSubscribers;

    public IDisposable Subscribe(Action networkChanged)
    {
        ArgumentNullException.ThrowIfNull(networkChanged);
        IDisposable networkSubscription = networkChanges.Subscribe(networkChanged);
        lock (gate)
        {
            manualSubscribers += networkChanged;
        }

        return new Subscription(this, networkChanged, networkSubscription);
    }

    public void Signal()
    {
        Action[] subscribers;
        lock (gate)
        {
            subscribers = manualSubscribers?.GetInvocationList().Cast<Action>().ToArray()
                ?? [];
        }

        foreach (Action subscriber in subscribers)
        {
            try
            {
                subscriber();
            }
            catch
            {
                // A discovery wake-up cannot own reconnect cleanup.
            }
        }
    }

    private void Remove(Action subscriber)
    {
        lock (gate)
        {
            manualSubscribers -= subscriber;
        }
    }

    private sealed class Subscription(
        DesktopReconnectChangeSource owner,
        Action subscriber,
        IDisposable networkSubscription) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            owner.Remove(subscriber);
            networkSubscription.Dispose();
        }
    }
}
