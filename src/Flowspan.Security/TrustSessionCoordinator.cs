using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Flowspan.Domain;

namespace Flowspan.Security;

public enum CapabilityRequirementMatch
{
    All,
    Any,
}

public enum TrustSessionStopReason
{
    PeerRevoked,
    CapabilityRevoked,
    LocalShutdown,
}

public sealed class TrustSessionStopException(
    IEnumerable<Exception> failures) : AggregateException(
        "One or more revoked peer sessions failed to stop.",
        failures)
{
}

public interface IRevocablePeerSession
{
    public ValueTask StopAsync(TrustSessionStopReason reason);
}

internal enum TrustPreparationReservationStatus
{
    Reserved,
    PeerNotFound,
    IdentityChanged,
    CapabilityDenied,
}

// This friend-only sink runs while the Trust coordinator owns its mutation
// gate. Implementations must be bounded, non-blocking, non-throwing, and must
// not call external code or re-enter the Trust coordinator.
internal interface ITrustPreparationInvalidationSink
{
    public void InvalidateTrustPreparationNow();
}

internal sealed record TrustPreparationReservationResult(
    TrustPreparationReservationStatus Status,
    TrustPreparationRegistration? Registration)
{
    public bool Reserved =>
        Status == TrustPreparationReservationStatus.Reserved
        && Registration is not null;
}

internal sealed class TrustPreparationRegistration : IAsyncDisposable
{
    private TrustSessionCoordinator? coordinator;
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private ITrustPreparationInvalidationSink? invalidationSink;
    private int current = 1;
    private int disposalStarted;

    internal TrustPreparationRegistration(
        TrustSessionCoordinator coordinator,
        long registrationId,
        ITrustPreparationInvalidationSink invalidationSink)
    {
        this.coordinator = coordinator;
        RegistrationId = registrationId;
        this.invalidationSink = invalidationSink;
    }

    internal long RegistrationId { get; }

    public bool IsCurrent => Volatile.Read(ref current) != 0;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref disposalStarted, 1, 0) == 0)
        {
            _ = CompleteDisposalAsync();
        }

        return new ValueTask(disposalCompleted.Task);
    }

    internal ITrustPreparationInvalidationSink? Deactivate()
    {
        if (Interlocked.Exchange(ref current, 0) == 0)
        {
            return null;
        }

        Volatile.Write(ref coordinator, null);
        return Interlocked.Exchange(ref invalidationSink, null);
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            TrustSessionCoordinator? owner = Volatile.Read(ref coordinator);
            if (owner is not null)
            {
                await owner.UnregisterPreparationAsync(this).ConfigureAwait(false);
            }

            Deactivate();
            disposalCompleted.TrySetResult();
        }
        catch (Exception exception)
        {
            disposalCompleted.TrySetException(exception);
        }
    }
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

public sealed class TrustSessionCoordinator : IAsyncDisposable, IPairingTrustAuthority
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<long, TrackedPreparation> preparations = [];
    private readonly Dictionary<Guid, TrackedSession> sessions = [];
    private readonly ITrustStore trustStore;
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? disposalFailure;
    private int disposalState;
    private long nextPreparationRegistrationId;

    public TrustSessionCoordinator(ITrustStore trustStore)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        this.trustStore = trustStore;
    }

    public event Action? Changed;

    public bool TryGetCurrentTrust(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out TrustRecord? trustRecord)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        return trustStore.TryGet(peerDeviceId, out trustRecord);
    }

    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out TrustRecord? trustRecord) =>
        TryGetCurrentTrust(peerDeviceId, out trustRecord);

    public ImmutableArray<TrustedPeerSnapshot> GetTrustedPeers()
    {
        ThrowIfShuttingDown();
        return trustStore.GetSnapshot();
    }

    public async ValueTask<TrustRegistrationResult> RegisterAsync(
        TrustRecord trustRecord,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(trustRecord);
        TrustRegistrationResult result;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            result = await trustStore.RegisterAsync(trustRecord, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        if (result == TrustRegistrationResult.Added)
        {
            PublishChanged();
        }

        return result;
    }

    public ValueTask<TrustSessionRegistration?> TryRegisterAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default) =>
        TryRegisterCoreAsync(
            peerDeviceId,
            requiredCapabilities,
            CapabilityRequirementMatch.All,
            session,
            cancellationToken);

    public ValueTask<TrustSessionRegistration?> TryRegisterAnyAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default) =>
        TryRegisterCoreAsync(
            peerDeviceId,
            requiredCapabilities,
            CapabilityRequirementMatch.Any,
            session,
            cancellationToken);

    internal async ValueTask<TrustPreparationReservationResult>
        TryReservePreparationAsync(
            DeviceId peerDeviceId,
            string authenticatedFingerprint,
            CapabilityGrant requiredCapabilities,
            ITrustPreparationInvalidationSink invalidationSink,
            CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedFingerprint);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(invalidationSink);
        if (requiredCapabilities.Capabilities.Count == 0)
        {
            throw new ArgumentException(
                "A Trust Preparation reservation must require at least one capability.",
                nameof(requiredCapabilities));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            if (!trustStore.TryGet(peerDeviceId, out TrustRecord? current))
            {
                return new TrustPreparationReservationResult(
                    TrustPreparationReservationStatus.PeerNotFound,
                    null);
            }

            if (!StringComparer.Ordinal.Equals(
                    authenticatedFingerprint,
                    current.PeerIdentity.Fingerprint))
            {
                return new TrustPreparationReservationResult(
                    TrustPreparationReservationStatus.IdentityChanged,
                    null);
            }

            if (!requiredCapabilities.Capabilities.All(
                    current.GrantedCapabilities.Allows))
            {
                return new TrustPreparationReservationResult(
                    TrustPreparationReservationStatus.CapabilityDenied,
                    null);
            }

            if (nextPreparationRegistrationId == long.MaxValue)
            {
                throw new InvalidOperationException(
                    "Trust Preparation registration identity space is exhausted.");
            }

            long registrationId = ++nextPreparationRegistrationId;
            var registration = new TrustPreparationRegistration(
                this,
                registrationId,
                invalidationSink);
            preparations.Add(
                registrationId,
                new TrackedPreparation(peerDeviceId, registration));
            return new TrustPreparationReservationResult(
                TrustPreparationReservationStatus.Reserved,
                registration);
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<TrustSessionRegistration?> TryRegisterCoreAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        CapabilityRequirementMatch capabilityMatch,
        IRevocablePeerSession session,
        CancellationToken cancellationToken)
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
            if (!IsCapabilityRequirementSatisfied(
                    requiredCapabilities,
                    capabilityMatch,
                    capability => trustStore.Allows(peerDeviceId, capability)))
            {
                return null;
            }

            Guid registrationId = Guid.NewGuid();
            sessions.Add(
                registrationId,
                new TrackedSession(
                    peerDeviceId,
                    requiredCapabilities,
                    capabilityMatch,
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
        Exception? preparationFailure = null;
        TrackedSession[] revokedSessions;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            if (!trustStore.TryGet(peerDeviceId, out TrustRecord? current)
                || await trustStore.RevokeAsync(
                    peerDeviceId,
                    current.PeerIdentity.Fingerprint,
                    cancellationToken).ConfigureAwait(false)
                    != TrustMutationResult.Applied)
            {
                return false;
            }

            revokedSessions = RemoveSessions(static (tracked, peer) =>
                tracked.PeerDeviceId == peer, peerDeviceId);
            preparationFailure = InvalidatePreparations(
                static (tracked, peer) => tracked.PeerDeviceId == peer,
                peerDeviceId);
        }
        finally
        {
            gate.Release();
        }

        PublishChanged();
        Exception? stopFailure = await TryStopAllAsync(
            revokedSessions,
            TrustSessionStopReason.PeerRevoked).ConfigureAwait(false);
        ThrowMutationFailures(preparationFailure, stopFailure);
        return true;
    }

    public async ValueTask<TrustMutationResult> RevokePeerAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        Exception? preparationFailure = null;
        TrackedSession[] revokedSessions;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            TrustMutationResult result = await trustStore.RevokeAsync(
                peerDeviceId,
                expectedFingerprint,
                cancellationToken).ConfigureAwait(false);
            if (result != TrustMutationResult.Applied)
            {
                return result;
            }

            revokedSessions = RemoveSessions(static (tracked, peer) =>
                tracked.PeerDeviceId == peer, peerDeviceId);
            preparationFailure = InvalidatePreparations(
                static (tracked, peer) => tracked.PeerDeviceId == peer,
                peerDeviceId);
        }
        finally
        {
            gate.Release();
        }

        PublishChanged();
        Exception? stopFailure = await TryStopAllAsync(
            revokedSessions,
            TrustSessionStopReason.PeerRevoked).ConfigureAwait(false);
        ThrowMutationFailures(preparationFailure, stopFailure);
        return TrustMutationResult.Applied;
    }

    public async ValueTask<bool> TryUpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default) =>
        await UpdateCapabilitiesAsync(
            peerDeviceId,
            expectedFingerprint,
            capabilities,
            cancellationToken).ConfigureAwait(false) == TrustMutationResult.Applied;

    public async ValueTask<TrustMutationResult> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(capabilities);
        Exception? preparationFailure = null;
        TrackedSession[] unauthorizedSessions;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShuttingDown();
            TrustMutationResult result = await trustStore.UpdateCapabilitiesAsync(
                    peerDeviceId,
                    expectedFingerprint,
                    capabilities,
                    cancellationToken).ConfigureAwait(false);
            if (result != TrustMutationResult.Applied)
            {
                return result;
            }

            unauthorizedSessions = RemoveSessions(
                static (tracked, state) =>
                    tracked.PeerDeviceId == state.PeerDeviceId
                    && !IsCapabilityRequirementSatisfied(
                        tracked.RequiredCapabilities,
                        tracked.CapabilityMatch,
                        state.Capabilities.Allows),
                (PeerDeviceId: peerDeviceId, Capabilities: capabilities));
            preparationFailure = InvalidatePreparations(
                static (tracked, peer) => tracked.PeerDeviceId == peer,
                peerDeviceId);
        }
        finally
        {
            gate.Release();
        }

        PublishChanged();
        Exception? stopFailure = await TryStopAllAsync(
            unauthorizedSessions,
            TrustSessionStopReason.CapabilityRevoked).ConfigureAwait(false);
        ThrowMutationFailures(preparationFailure, stopFailure);
        return TrustMutationResult.Applied;
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

    internal async ValueTask UnregisterPreparationAsync(
        TrustPreparationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (preparations.TryGetValue(
                    registration.RegistrationId,
                    out TrackedPreparation? tracked)
                && ReferenceEquals(tracked.Registration, registration))
            {
                preparations.Remove(registration.RegistrationId);
            }

            registration.Deactivate();
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

    private Exception? InvalidatePreparations<TState>(
        Func<TrackedPreparation, TState, bool> predicate,
        TState state)
    {
        long[] registrationIds = preparations
            .Where(entry => predicate(entry.Value, state))
            .Select(static entry => entry.Key)
            .Order()
            .ToArray();
        var invalidations = new List<ITrustPreparationInvalidationSink>(
            registrationIds.Length);
        foreach (long registrationId in registrationIds)
        {
            TrackedPreparation tracked = preparations[registrationId];
            preparations.Remove(registrationId);
            ITrustPreparationInvalidationSink? sink =
                tracked.Registration.Deactivate();
            if (sink is not null)
            {
                invalidations.Add(sink);
            }
        }

        var failures = new List<Exception>();
        foreach (ITrustPreparationInvalidationSink sink in invalidations)
        {
            try
            {
                sink.InvalidateTrustPreparationNow();
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                failures.Add(exception);
            }
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                "One or more Trust Preparation reservations failed to invalidate.",
                failures),
        };
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
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
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
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new TrustSessionStopException(failures);
        }
    }

    private static async ValueTask<Exception?> TryStopAllAsync(
        IEnumerable<TrackedSession> sessions,
        TrustSessionStopReason reason)
    {
        try
        {
            await StopAllAsync(sessions, reason).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            return exception;
        }
    }

    private static void ThrowMutationFailures(
        Exception? preparationFailure,
        Exception? stopFailure)
    {
        Exception? failure = (preparationFailure, stopFailure) switch
        {
            (null, null) => null,
            (not null, null) => preparationFailure,
            (null, not null) => stopFailure,
            _ => new AggregateException(
                "A committed Trust mutation had one or more cleanup failures.",
                preparationFailure!,
                stopFailure!),
        };
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
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
            Exception? preparationFailure;
            TrackedSession[] activeSessions;
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                activeSessions = sessions.Values.ToArray();
                sessions.Clear();
                preparationFailure = InvalidatePreparations(
                    static (_, _) => true,
                    0);
            }
            finally
            {
                gate.Release();
            }

            Exception? stopFailure = await TryStopAllAsync(
                activeSessions,
                TrustSessionStopReason.LocalShutdown).ConfigureAwait(false);
            ThrowMutationFailures(preparationFailure, stopFailure);
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
                // Observers cannot own persisted Trust mutation or session draining.
            }
        }
    }

    private static bool IsCapabilityRequirementSatisfied(
        CapabilityGrant requiredCapabilities,
        CapabilityRequirementMatch capabilityMatch,
        Func<Capability, bool> allows) => capabilityMatch switch
        {
            CapabilityRequirementMatch.All =>
                requiredCapabilities.Capabilities.All(allows),
            CapabilityRequirementMatch.Any =>
                requiredCapabilities.Capabilities.Any(allows),
            _ => throw new ArgumentOutOfRangeException(
                nameof(capabilityMatch),
                capabilityMatch,
                "Unknown session capability match mode."),
        };

    private sealed record TrackedSession(
        DeviceId PeerDeviceId,
        CapabilityGrant RequiredCapabilities,
        CapabilityRequirementMatch CapabilityMatch,
        IRevocablePeerSession Session);

    private sealed record TrackedPreparation(
        DeviceId PeerDeviceId,
        TrustPreparationRegistration Registration);
}
