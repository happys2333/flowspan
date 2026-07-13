using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Flowspan.Domain;

namespace Flowspan.Security;

public enum TrustSessionStopReason
{
    PeerRevoked,
    CapabilityRevoked,
    LocalShutdown,
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

public sealed class TrustSessionCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, TrackedSession> sessions = [];
    private readonly ITrustStore trustStore;
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? disposalFailure;
    private int disposalState;

    public TrustSessionCoordinator(ITrustStore trustStore)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        this.trustStore = trustStore;
    }

    public bool TryGetCurrentTrust(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out TrustRecord? trustRecord)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        return trustStore.TryGet(peerDeviceId, out trustRecord);
    }

    public async ValueTask<TrustSessionRegistration?> TryRegisterAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(session);
        if (requiredCapabilities.Capabilities.Count == 0)
        {
            throw new ArgumentException(
                "An active peer session must require at least one capability.",
                nameof(requiredCapabilities));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            if (requiredCapabilities.Capabilities.Any(capability =>
                    !trustStore.Allows(peerDeviceId, capability)))
            {
                return null;
            }

            Guid registrationId = Guid.NewGuid();
            sessions.Add(
                registrationId,
                new TrackedSession(
                    peerDeviceId,
                    requiredCapabilities,
                    session));
            return new TrustSessionRegistration(this, registrationId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<bool> RevokePeerAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        TrackedSession[] revokedSessions;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            if (!await trustStore.RevokeAsync(peerDeviceId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            revokedSessions = RemoveSessions(static (tracked, peer) =>
                tracked.PeerDeviceId == peer, peerDeviceId);
        }
        finally
        {
            gate.Release();
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
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(capabilities);
        TrackedSession[] unauthorizedSessions;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            if (!await trustStore.TryUpdateCapabilitiesAsync(
                    peerDeviceId,
                    expectedFingerprint,
                    capabilities,
                    cancellationToken).ConfigureAwait(false))
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
        finally
        {
            gate.Release();
        }

        await StopAllAsync(
            unauthorizedSessions,
            TrustSessionStopReason.CapabilityRevoked).ConfigureAwait(false);
        return true;
    }

    internal async ValueTask UnregisterAsync(Guid registrationId)
    {
        if (Volatile.Read(ref disposalState) != 0)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposalState) != 0)
            {
                return;
            }

            sessions.Remove(registrationId);
        }
        finally
        {
            gate.Release();
        }
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref disposalState, 1, 0) != 0)
        {
            await disposalCompleted.Task.ConfigureAwait(false);
            if (disposalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(disposalFailure).Throw();
            }

            return;
        }

        Exception? failure = null;
        try
        {
            TrackedSession[] activeSessions;
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                activeSessions = sessions.Values.ToArray();
                sessions.Clear();
            }
            finally
            {
                gate.Release();
            }

            await StopAllAsync(activeSessions, TrustSessionStopReason.LocalShutdown)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            Volatile.Write(ref disposalState, 2);
            disposalFailure = failure;
            disposalCompleted.SetResult();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void ThrowIfShuttingDown() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposalState) != 0,
            this);

    private sealed record TrackedSession(
        DeviceId PeerDeviceId,
        CapabilityGrant RequiredCapabilities,
        IRevocablePeerSession Session);
}
