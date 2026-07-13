using Flowspan.Domain;

namespace Flowspan.Security;

public enum TrustSessionStopReason
{
    PeerRevoked,
    CapabilityRevoked,
}

public interface IRevocablePeerSession
{
    public ValueTask StopAsync(TrustSessionStopReason reason);
}

public sealed class TrustSessionRegistration : IAsyncDisposable
{
    private TrustSessionCoordinator? coordinator;

    internal TrustSessionRegistration(
        TrustSessionCoordinator coordinator,
        Guid registrationId)
    {
        this.coordinator = coordinator;
        RegistrationId = registrationId;
    }

    internal Guid RegistrationId { get; }

    public ValueTask DisposeAsync()
    {
        TrustSessionCoordinator? owner = Interlocked.Exchange(
            ref coordinator,
            null);
        return owner is null
            ? ValueTask.CompletedTask
            : owner.UnregisterAsync(RegistrationId);
    }
}

public sealed class TrustSessionCoordinator
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, TrackedSession> sessions = [];
    private readonly InMemoryTrustStore trustStore;

    public TrustSessionCoordinator(InMemoryTrustStore trustStore)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        this.trustStore = trustStore;
    }

    public ValueTask<TrustSessionRegistration?> TryRegisterAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(session);
        if (requiredCapabilities.Capabilities.Count == 0)
        {
            throw new ArgumentException(
                "An active peer session must require at least one capability.",
                nameof(requiredCapabilities));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (requiredCapabilities.Capabilities.Any(capability =>
                    !trustStore.Allows(peerDeviceId, capability)))
            {
                return ValueTask.FromResult<TrustSessionRegistration?>(null);
            }

            Guid registrationId = Guid.NewGuid();
            sessions.Add(
                registrationId,
                new TrackedSession(
                    peerDeviceId,
                    requiredCapabilities,
                    session));
            return ValueTask.FromResult<TrustSessionRegistration?>(
                new TrustSessionRegistration(this, registrationId));
        }
    }

    public async ValueTask<bool> RevokePeerAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        TrackedSession[] revokedSessions;
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!trustStore.Revoke(peerDeviceId))
            {
                return false;
            }

            revokedSessions = RemoveSessions(static (tracked, peer) =>
                tracked.PeerDeviceId == peer, peerDeviceId);
        }

        await StopAllAsync(
            revokedSessions,
            TrustSessionStopReason.PeerRevoked).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> TryUpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(capabilities);
        cancellationToken.ThrowIfCancellationRequested();
        TrackedSession[] unauthorizedSessions;
        lock (gate)
        {
            if (!trustStore.TryUpdateCapabilities(
                    peerDeviceId,
                    expectedFingerprint,
                    capabilities))
            {
                return false;
            }

            unauthorizedSessions = RemoveSessions(
                static (tracked, state) =>
                    tracked.PeerDeviceId == state.PeerDeviceId
                    && tracked.RequiredCapabilities.Capabilities.Any(capability =>
                        !state.Capabilities.Allows(capability)),
                (PeerDeviceId: peerDeviceId, Capabilities: capabilities));
        }

        await StopAllAsync(
            unauthorizedSessions,
            TrustSessionStopReason.CapabilityRevoked).ConfigureAwait(false);
        return true;
    }

    internal ValueTask UnregisterAsync(Guid registrationId)
    {
        lock (gate)
        {
            sessions.Remove(registrationId);
        }

        return ValueTask.CompletedTask;
    }

    private TrackedSession[] RemoveSessions<TState>(
        Func<TrackedSession, TState, bool> predicate,
        TState state)
    {
        Guid[] registrationIds = sessions
            .Where(entry => predicate(entry.Value, state))
            .Select(static entry => entry.Key)
            .ToArray();
        var removed = new TrackedSession[registrationIds.Length];
        for (int index = 0; index < registrationIds.Length; index++)
        {
            removed[index] = sessions[registrationIds[index]];
            sessions.Remove(registrationIds[index]);
        }

        return removed;
    }

    private static async ValueTask StopAllAsync(
        IEnumerable<TrackedSession> sessions,
        TrustSessionStopReason reason)
    {
        var failures = new List<Exception>();
        var stops = new List<Task>();
        foreach (TrackedSession session in sessions)
        {
            try
            {
                stops.Add(session.Session.StopAsync(reason).AsTask());
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        foreach (Task stop in stops)
        {
            try
            {
                await stop.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "One or more revoked peer sessions failed to stop.",
                failures);
        }
    }

    private sealed record TrackedSession(
        DeviceId PeerDeviceId,
        CapabilityGrant RequiredCapabilities,
        IRevocablePeerSession Session);
}
