using System.Collections.Immutable;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

public enum DesktopLocalPairingStatus
{
    Disabled,
    Enabling,
    Enabled,
    Stopping,
    Faulted,
}

internal interface IDesktopLocalPairingNetworkFactory
{
    public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
        CancellationToken cancellationToken = default);
}

internal interface IDesktopLocalPairingNetworkSession : IAsyncDisposable
{
    public event Action? Changed;

    public event Action<IDesktopLocalPairingNetworkSession>? Faulted
    {
        add { }
        remove { }
    }

    public event Action? TrustChanged
    {
        add { }
        remove { }
    }

    public int ListeningPort { get; }

    public bool IsFaulted => false;

    public ImmutableArray<UnverifiedPairingCandidate> GetCandidates();

    public ValueTask<PairingCeremonyResult> PairAsync(
        UnverifiedPairingCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class DesktopLocalPairingRuntime : IAsyncDisposable
{
    private readonly IDesktopLocalPairingNetworkFactory factory;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private IDesktopLocalPairingNetworkSession? session;
    private int disposed;

    internal DesktopLocalPairingRuntime(IDesktopLocalPairingNetworkFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.factory = factory;
    }

    public event Action? Changed;

    public event Action? TrustChanged;

    public bool IsEnabled => Status == DesktopLocalPairingStatus.Enabled;

    public int? ListeningPort => session?.ListeningPort;

    public DesktopLocalPairingStatus Status { get; private set; } =
        DesktopLocalPairingStatus.Disabled;

    public async ValueTask EnableAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        await lifecycleGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (session is not null)
            {
                return;
            }

            Status = DesktopLocalPairingStatus.Enabling;
            PublishChanged();
            try
            {
                IDesktopLocalPairingNetworkSession started = await factory
                    .StartAsync(linkedCancellation.Token).ConfigureAwait(false);
                session = started
                    ?? throw new InvalidOperationException(
                        "The local pairing network factory returned null.");
                session.Changed += OnSessionChanged;
                session.Faulted += OnSessionFaulted;
                session.TrustChanged += OnSessionTrustChanged;
                if (session.IsFaulted)
                {
                    throw new InvalidOperationException(
                        "The local pairing network faulted during startup.");
                }

                Status = DesktopLocalPairingStatus.Enabled;
            }
            catch
            {
                IDesktopLocalPairingNetworkSession? failed = session;
                session = null;
                if (failed is not null)
                {
                    DetachSession(failed);
                    try
                    {
                        await failed.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Preserve the startup failure; the UI receives no internals.
                    }
                }

                Status = DesktopLocalPairingStatus.Faulted;
                PublishChanged();
                throw;
            }

            PublishChanged();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public ImmutableArray<UnverifiedPairingCandidate> GetCandidates()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return session?.GetCandidates() ?? [];
    }

    public async ValueTask DisableAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public ValueTask<PairingCeremonyResult> PairAsync(
        UnverifiedPairingCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(candidate);
        IDesktopLocalPairingNetworkSession current = session
            ?? throw new InvalidOperationException(
                "Local pairing must be enabled before pairing a device.");
        return current.PairAsync(candidate, cancellationToken);
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
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
            lifetimeCancellation.Dispose();
        }
    }

    private void OnSessionChanged() => PublishChanged();

    private void OnSessionFaulted(IDesktopLocalPairingNetworkSession failed)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            _ = HandleSessionFaultAsync(failed);
        }
    }

    private void OnSessionTrustChanged()
    {
        foreach (Action subscriber in
                 TrustChanged?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                subscriber();
            }
            catch
            {
                // Presentation callbacks do not own network lifetime.
            }
        }
    }

    private async ValueTask StopCoreAsync()
    {
        Status = DesktopLocalPairingStatus.Stopping;
        PublishChanged();
        IDesktopLocalPairingNetworkSession? current = session;
        session = null;
        if (current is not null)
        {
            DetachSession(current);
            await current.DisposeAsync().ConfigureAwait(false);
        }

        Status = DesktopLocalPairingStatus.Disabled;
        PublishChanged();
    }

    private void DetachSession(IDesktopLocalPairingNetworkSession current)
    {
        current.Changed -= OnSessionChanged;
        current.Faulted -= OnSessionFaulted;
        current.TrustChanged -= OnSessionTrustChanged;
    }

    private async Task HandleSessionFaultAsync(
        IDesktopLocalPairingNetworkSession failed)
    {
        bool entered = false;
        try
        {
            await lifecycleGate.WaitAsync(lifetimeCancellation.Token)
                .ConfigureAwait(false);
            entered = true;
            if (Volatile.Read(ref disposed) != 0
                || !ReferenceEquals(session, failed))
            {
                return;
            }

            DetachSession(failed);
            session = null;
            Status = DesktopLocalPairingStatus.Faulted;
            PublishChanged();
            try
            {
                await failed.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // The public recovery state is intentionally sanitized.
            }
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
            when (Volatile.Read(ref disposed) != 0)
        {
        }
        finally
        {
            if (entered)
            {
                lifecycleGate.Release();
            }
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
                // Presentation callbacks do not own network lifetime.
            }
        }
    }
}
