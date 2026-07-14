using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop;

public sealed record DesktopTrustSnapshot(
    SecretStoreProtection Protection,
    ImmutableArray<TrustedPeerSnapshot> TrustedPeers);

public enum DesktopTrustMutationStatus
{
    Applied,
    PeerNotFound,
    IdentityChanged,
    AppliedWithSessionStopFailure,
}

public sealed record DesktopTrustMutationOutcome(
    DesktopTrustMutationStatus Status,
    DesktopTrustSnapshot Snapshot);

public interface IDesktopTrustAuthority : IAsyncDisposable
{
    public ValueTask<DesktopTrustSnapshot> InitializeAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default);

    public ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);

    public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default);
}

public sealed class DesktopTrustAuthority : IDesktopTrustAuthority
{
    private readonly TrustSessionCoordinator coordinator;
    private readonly IDisposable? ownedTrustStore;
    private readonly ITrustStore trustStore;
    private int disposed;

    public DesktopTrustAuthority(ITrustStore trustStore)
        : this(trustStore, null)
    {
    }

    internal DesktopTrustAuthority(
        ITrustStore trustStore,
        IDisposable? ownedTrustStore)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        this.trustStore = trustStore;
        this.ownedTrustStore = ownedTrustStore;
        coordinator = new TrustSessionCoordinator(trustStore);
    }

    public ValueTask<DesktopTrustSnapshot> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DesktopTrustSnapshot(
            trustStore.Protection,
            coordinator.GetTrustedPeers()));
    }

    internal TrustSessionCoordinator GetRuntimeCoordinator()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        return coordinator;
    }

    public async ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        TrustMutationResult result;
        try
        {
            result = await coordinator.UpdateCapabilitiesAsync(
                peerDeviceId,
                expectedFingerprint,
                capabilities,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TrustSessionStopException)
        {
            DesktopTrustSnapshot refreshed = CreateSnapshot();
            return new DesktopTrustMutationOutcome(
                DesktopTrustMutationStatus.AppliedWithSessionStopFailure,
                refreshed);
        }

        return new DesktopTrustMutationOutcome(
            result switch
            {
                TrustMutationResult.Applied => DesktopTrustMutationStatus.Applied,
                TrustMutationResult.PeerNotFound =>
                    DesktopTrustMutationStatus.PeerNotFound,
                TrustMutationResult.IdentityChanged =>
                    DesktopTrustMutationStatus.IdentityChanged,
                _ => throw new InvalidOperationException(
                    "The Trust mutation result is not supported."),
            },
            CreateSnapshot());
    }

    public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        return coordinator.TryRegisterAsync(
            peerDeviceId,
            requiredCapabilities,
            session,
            cancellationToken);
    }

    public async ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        TrustMutationResult result;
        try
        {
            result = await coordinator.RevokePeerAsync(
                peerDeviceId,
                expectedFingerprint,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TrustSessionStopException)
        {
            DesktopTrustSnapshot refreshed = CreateSnapshot();
            return new DesktopTrustMutationOutcome(
                DesktopTrustMutationStatus.AppliedWithSessionStopFailure,
                refreshed);
        }

        return new DesktopTrustMutationOutcome(
            result switch
            {
                TrustMutationResult.Applied => DesktopTrustMutationStatus.Applied,
                TrustMutationResult.PeerNotFound =>
                    DesktopTrustMutationStatus.PeerNotFound,
                TrustMutationResult.IdentityChanged =>
                    DesktopTrustMutationStatus.IdentityChanged,
                _ => throw new InvalidOperationException(
                    "The Trust mutation result is not supported."),
            },
            CreateSnapshot());
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await coordinator.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            ownedTrustStore?.Dispose();
        }
    }

    private DesktopTrustSnapshot CreateSnapshot() => new(
        trustStore.Protection,
        coordinator.GetTrustedPeers());
}

public sealed class PersistentDesktopTrustAuthority : IDesktopTrustAuthority
{
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly ITrustPayloadStore payloadStore;
    private DesktopTrustAuthority? authority;
    private Exception? disposalFailure;
    private int disposalState;

    public PersistentDesktopTrustAuthority(ITrustPayloadStore payloadStore)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        this.payloadStore = payloadStore;
    }

    public ValueTask<DesktopTrustSnapshot> InitializeAsync(
        CancellationToken cancellationToken = default) => ExecuteAsync(
            static (current, token) => current.InitializeAsync(token),
            cancellationToken);

    internal ValueTask<TrustSessionCoordinator> GetRuntimeCoordinatorAsync(
        CancellationToken cancellationToken = default) => ExecuteAsync(
            static (current, _) => ValueTask.FromResult(
                current.GetRuntimeCoordinator()),
            cancellationToken);

    public ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            (current, token) => current.UpdateCapabilitiesAsync(
                peerDeviceId,
                expectedFingerprint,
                capabilities,
                token),
            cancellationToken);

    public ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            (current, token) => current.RevokeAsync(
                peerDeviceId,
                expectedFingerprint,
                token),
            cancellationToken);

    public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
        DeviceId peerDeviceId,
        CapabilityGrant requiredCapabilities,
        IRevocablePeerSession session,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            (current, token) => current.TryRegisterSessionAsync(
                peerDeviceId,
                requiredCapabilities,
                session,
                token),
            cancellationToken);

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
            await operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                DesktopTrustAuthority? current = authority;
                authority = null;
                if (current is not null)
                {
                    await current.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            disposalFailure = failure;
            Volatile.Write(ref disposalState, 2);
            disposalCompleted.SetResult();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async ValueTask<T> ExecuteAsync<T>(
        Func<DesktopTrustAuthority, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposalState) != 0,
            this);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposalState) != 0,
                this);
            DesktopTrustAuthority current = await GetOrCreateCoreAsync(
                cancellationToken).ConfigureAwait(false);
            return await operation(current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async ValueTask<DesktopTrustAuthority> GetOrCreateCoreAsync(
        CancellationToken cancellationToken)
    {
        if (authority is not null)
        {
            return authority;
        }

        PersistentTrustStore trustStore = await PersistentTrustStore.OpenAsync(
            payloadStore,
            cancellationToken).ConfigureAwait(false);
        authority = new DesktopTrustAuthority(trustStore, trustStore);
        return authority;
    }
}
