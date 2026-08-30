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
    CleanupUnconfirmed,
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

    public ImmutableArray<DesktopTrustedPeerConnectionSnapshot>
        GetTrustedPeerConnections() => [];

    public ValueTask<PairingCeremonyResult> PairAsync(
        UnverifiedPairingCandidate candidate,
        CancellationToken cancellationToken = default);

    public ValueTask RefreshTrustedPeersAsync(
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

public sealed class DesktopLocalPairingRuntime : IAsyncDisposable
{
    private readonly TaskCompletionSource disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDesktopLocalPairingNetworkFactory factory;
    private readonly AsyncLocal<LifecycleCallbackScope?> lifecycleCallbackScope = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Lock lifecycleOperationsGate = new();
    private Exception? disposalFailure;
    private int lifecycleOperationsInFlight;
    private TaskCompletionSource lifecycleOperationsDrained =
        CreateCompletedSignal();
    private int pendingChangedPublication;
    private int pendingTrustChangedPublication;
    private int publicationDrainActive;
    private IDesktopLocalPairingNetworkSession? retiringSession;
    private IDesktopLocalPairingNetworkSession? session;
    private bool sessionBoundaryInProgress;
    private int disposed;

    internal DesktopLocalPairingRuntime(IDesktopLocalPairingNetworkFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.factory = factory;
    }

    public event Action? Changed;

    public event Action? TrustChanged;

    public bool IsEnabled => Status == DesktopLocalPairingStatus.Enabled;

    public int? ListeningPort =>
        session?.ListeningPort ?? retiringSession?.ListeningPort;

    public DesktopLocalPairingStatus Status { get; private set; } =
        DesktopLocalPairingStatus.Disabled;

    public async ValueTask EnableAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ThrowIfReentrantLifecycleOperation();
        Exception? operationFailure = null;
        bool startRejected = false;
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        bool lifecycleOperationAdmitted = TryEnterLifecycleOperation();
        ObjectDisposedException.ThrowIf(!lifecycleOperationAdmitted, this);

        bool enteredLifecycleGate = false;
        try
        {
            await lifecycleGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            enteredLifecycleGate = true;
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (session is not null)
                {
                    return;
                }

                if (retiringSession is not null || sessionBoundaryInProgress)
                {
                    throw new InvalidOperationException(
                        "Local pairing cleanup is unconfirmed; retry stop before enabling.");
                }

                Status = DesktopLocalPairingStatus.Enabling;
                IDesktopLocalPairingNetworkSession? started = null;
                try
                {
                    using LifecycleCallbackScopeLease callbackScope =
                        EnterLifecycleCallbackScope();
                    started = await factory
                        .StartAsync(linkedCancellation.Token).ConfigureAwait(false);

                    if (started is null)
                    {
                        throw new InvalidOperationException(
                            "The local pairing network factory returned null.");
                    }
                }
                catch (Exception exception)
                {
                    Status = DesktopLocalPairingStatus.Faulted;
                    operationFailure = exception;
                }

                if (started is not null)
                {
                    retiringSession = started;
                    sessionBoundaryInProgress = true;
                    lifecycleGate.Release();
                    enteredLifecycleGate = false;

                    var boundaryFailures = new List<Exception>();
                    bool attachAttempted = Volatile.Read(ref disposed) == 0
                        && !linkedCancellation.IsCancellationRequested;
                    bool attached = attachAttempted
                        && AttachSession(started, boundaryFailures);

                    await lifecycleGate.WaitAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    enteredLifecycleGate = true;
                    bool closed = Volatile.Read(ref disposed) != 0
                        || linkedCancellation.IsCancellationRequested;
                    if (attached && !closed)
                    {
                        session = started;
                        retiringSession = null;
                        sessionBoundaryInProgress = false;
                        Status = DesktopLocalPairingStatus.Enabled;
                    }
                    else
                    {
                        lifecycleGate.Release();
                        enteredLifecycleGate = false;
                        var cleanupFailures = new List<Exception>();
                        if (attachAttempted)
                        {
                            DetachSession(started, cleanupFailures);
                        }

                        await DisposeSessionAsync(started, cleanupFailures)
                            .ConfigureAwait(false);
                        await lifecycleGate.WaitAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                        enteredLifecycleGate = true;

                        sessionBoundaryInProgress = false;
                        if (cleanupFailures.Count == 0)
                        {
                            retiringSession = null;
                        }

                        Status = cleanupFailures.Count == 0
                            ? closed
                                ? DesktopLocalPairingStatus.Disabled
                                : DesktopLocalPairingStatus.Faulted
                            : DesktopLocalPairingStatus.CleanupUnconfirmed;
                        startRejected = closed;
                        List<Exception> failures = [
                            .. boundaryFailures,
                            .. cleanupFailures,
                        ];
                        if (failures.Count == 1)
                        {
                            operationFailure = failures[0];
                        }
                        else if (failures.Count > 1)
                        {
                            operationFailure = new AggregateException(
                                "Local pairing startup and cleanup failed.",
                                failures);
                        }
                    }
                }
            }
            finally
            {
                if (enteredLifecycleGate)
                {
                    lifecycleGate.Release();
                    enteredLifecycleGate = false;
                }
            }

            if (startRejected)
            {
                if (operationFailure is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(operationFailure)
                        .Throw();
                }

                lifetimeCancellation.Token.ThrowIfCancellationRequested();
                linkedCancellation.Token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                return;
            }

            RequestChangedPublication();
            if (operationFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(operationFailure)
                    .Throw();
            }
        }
        finally
        {
            if (enteredLifecycleGate)
            {
                lifecycleGate.Release();
            }

            ExitLifecycleOperation();
        }
    }

    public ImmutableArray<UnverifiedPairingCandidate> GetCandidates()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return session?.GetCandidates() ?? [];
    }

    public ImmutableArray<DesktopTrustedPeerConnectionSnapshot>
        GetTrustedPeerConnections()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return session?.GetTrustedPeerConnections() ?? [];
    }

    public ValueTask RefreshTrustedPeersAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        IDesktopLocalPairingNetworkSession? current = session;
        return current is null
            ? ValueTask.CompletedTask
            : current.RefreshTrustedPeersAsync(cancellationToken);
    }

    public async ValueTask DisableAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ThrowIfReentrantLifecycleOperation();
        Exception? operationFailure = null;
        bool lifecycleOperationAdmitted = TryEnterLifecycleOperation();
        ObjectDisposedException.ThrowIf(!lifecycleOperationAdmitted, this);

        try
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (sessionBoundaryInProgress)
                {
                    throw new InvalidOperationException(
                        "Local pairing is already changing lifecycle state.");
                }

                try
                {
                    await StopCoreAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    operationFailure = exception;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }

            RequestChangedPublication();
            if (operationFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(operationFailure)
                    .Throw();
            }
        }
        finally
        {
            ExitLifecycleOperation();
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
        bool isLifecycleCallback = IsLifecycleCallbackActive;

        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            _ = DisposeResourcesAsync();
        }

        if (isLifecycleCallback)
        {
            return;
        }

        await disposalCompleted.Task.ConfigureAwait(false);
        if (disposalFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(disposalFailure)
                .Throw();
        }
    }

    private async Task DisposeResourcesAsync()
    {
        var failures = new List<Exception>();
        try
        {
            Task cancellationTask;
            try
            {
                cancellationTask = lifetimeCancellation.CancelAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                cancellationTask = Task.CompletedTask;
            }

            await GetLifecycleOperationsDrainedTask().ConfigureAwait(false);

            bool enteredLifecycleGate = false;
            try
            {
                await lifecycleGate.WaitAsync().ConfigureAwait(false);
                enteredLifecycleGate = true;
                try
                {
                    await StopCoreAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                if (enteredLifecycleGate)
                {
                    lifecycleGate.Release();
                }
            }

            try
            {
                await cancellationTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                lifetimeCancellation.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            disposalFailure = failures.Count switch
            {
                0 => null,
                1 => failures[0],
                _ => new AggregateException(
                    "One or more local pairing resources failed to close.",
                    failures),
            };
        }
        finally
        {
            disposalCompleted.TrySetResult();
        }
    }

    private void OnSessionChanged() => RequestChangedPublication();

    private void OnSessionFaulted(IDesktopLocalPairingNetworkSession failed)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            _ = HandleSessionFaultAsync(failed);
        }
    }

    private void OnSessionTrustChanged() => RequestTrustChangedPublication();

    private void PublishTrustChanged()
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
        var failures = new List<Exception>();
        Status = DesktopLocalPairingStatus.Stopping;
        IDesktopLocalPairingNetworkSession? current = session ?? retiringSession;
        session = null;
        if (current is null)
        {
            Status = DesktopLocalPairingStatus.Disabled;
            return;
        }

        retiringSession = current;
        sessionBoundaryInProgress = true;
        lifecycleGate.Release();
        try
        {
            DetachSession(current, failures);
            await DisposeSessionAsync(current, failures).ConfigureAwait(false);
        }
        finally
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            sessionBoundaryInProgress = false;
        }

        if (failures.Count == 0
            && ReferenceEquals(retiringSession, current))
        {
            retiringSession = null;
        }

        Status = failures.Count == 0
            ? DesktopLocalPairingStatus.Disabled
            : DesktopLocalPairingStatus.CleanupUnconfirmed;
        ThrowCleanupFailures(failures);
    }

    private void DetachSession(
        IDesktopLocalPairingNetworkSession current,
        List<Exception>? failures = null)
    {
        using LifecycleCallbackScopeLease callbackScope =
            EnterLifecycleCallbackScope();
        try
        {
            current.Changed -= OnSessionChanged;
        }
        catch (Exception exception)
        {
            failures?.Add(exception);
        }

        try
        {
            current.Faulted -= OnSessionFaulted;
        }
        catch (Exception exception)
        {
            failures?.Add(exception);
        }

        try
        {
            current.TrustChanged -= OnSessionTrustChanged;
        }
        catch (Exception exception)
        {
            failures?.Add(exception);
        }
    }

    private bool AttachSession(
        IDesktopLocalPairingNetworkSession current,
        List<Exception> failures)
    {
        using LifecycleCallbackScopeLease callbackScope =
            EnterLifecycleCallbackScope();
        try
        {
            current.Changed += OnSessionChanged;
            current.Faulted += OnSessionFaulted;
            current.TrustChanged += OnSessionTrustChanged;
            if (current.IsFaulted)
            {
                throw new InvalidOperationException(
                    "The local pairing network faulted during startup.");
            }

            return true;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return false;
        }
    }

    private async ValueTask DisposeSessionAsync(
        IDesktopLocalPairingNetworkSession current,
        List<Exception> failures)
    {
        using LifecycleCallbackScopeLease callbackScope =
            EnterLifecycleCallbackScope();
        try
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void ThrowIfReentrantLifecycleOperation()
    {
        if (IsLifecycleCallbackActive)
        {
            throw new InvalidOperationException(
                "Local pairing lifecycle operations cannot re-enter a session boundary.");
        }
    }

    private bool IsLifecycleCallbackActive =>
        lifecycleCallbackScope.Value?.IsActive == true;

    private LifecycleCallbackScopeLease EnterLifecycleCallbackScope() =>
        new LifecycleCallbackScopeLease(lifecycleCallbackScope);

    private static void ThrowCleanupFailures(List<Exception> failures)
    {
        switch (failures.Count)
        {
            case 0:
                return;
            case 1:
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(failures[0])
                    .Throw();
                return;
            default:
                throw new AggregateException(
                    "One or more local pairing resources failed to close.",
                    failures);
        }
    }

    private async Task HandleSessionFaultAsync(
        IDesktopLocalPairingNetworkSession failed)
    {
        bool entered = false;
        bool publishChanged = false;
        if (!TryEnterLifecycleOperation())
        {
            return;
        }

        try
        {
            await lifecycleGate.WaitAsync(lifetimeCancellation.Token)
                .ConfigureAwait(false);
            entered = true;
            if (Volatile.Read(ref disposed) != 0
                || sessionBoundaryInProgress
                || !ReferenceEquals(session, failed))
            {
                return;
            }

            var cleanupFailures = new List<Exception>();
            session = null;
            retiringSession = failed;
            sessionBoundaryInProgress = true;
            lifecycleGate.Release();
            entered = false;
            DetachSession(failed, cleanupFailures);
            await DisposeSessionAsync(failed, cleanupFailures).ConfigureAwait(false);
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            entered = true;
            sessionBoundaryInProgress = false;

            if (cleanupFailures.Count == 0)
            {
                retiringSession = null;
                Status = DesktopLocalPairingStatus.Faulted;
            }
            else
            {
                Status = DesktopLocalPairingStatus.CleanupUnconfirmed;
            }

            publishChanged = true;
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

        try
        {
            if (publishChanged)
            {
                RequestChangedPublication();
            }
        }
        finally
        {
            ExitLifecycleOperation();
        }
    }

    private bool TryEnterLifecycleOperation()
    {
        lock (lifecycleOperationsGate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            if (lifecycleOperationsInFlight == 0)
            {
                lifecycleOperationsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            lifecycleOperationsInFlight++;
            return true;
        }
    }

    private void ExitLifecycleOperation()
    {
        TaskCompletionSource? drained = null;
        lock (lifecycleOperationsGate)
        {
            lifecycleOperationsInFlight--;
            if (lifecycleOperationsInFlight == 0)
            {
                drained = lifecycleOperationsDrained;
            }
        }

        drained?.TrySetResult();
        if (drained is not null)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                DiscardPendingPublications();
            }
            else
            {
                DrainPendingPublications();
            }
        }
    }

    private Task GetLifecycleOperationsDrainedTask()
    {
        lock (lifecycleOperationsGate)
        {
            return lifecycleOperationsDrained.Task;
        }
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }

    private void RequestChangedPublication()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref pendingChangedPublication, 1);
        if (Volatile.Read(ref disposed) != 0)
        {
            DiscardPendingPublications();
            return;
        }

        if (Volatile.Read(ref lifecycleOperationsInFlight) == 0)
        {
            DrainPendingPublications();
        }
    }

    private void RequestTrustChangedPublication()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref pendingTrustChangedPublication, 1);
        if (Volatile.Read(ref disposed) != 0)
        {
            DiscardPendingPublications();
            return;
        }

        if (Volatile.Read(ref lifecycleOperationsInFlight) == 0)
        {
            DrainPendingPublications();
        }
    }

    private void DrainPendingPublications()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            DiscardPendingPublications();
            return;
        }

        if (Volatile.Read(ref lifecycleOperationsInFlight) != 0
            || Interlocked.CompareExchange(ref publicationDrainActive, 1, 0) != 0)
        {
            return;
        }

        try
        {
            while (Volatile.Read(ref disposed) == 0
                   && Volatile.Read(ref lifecycleOperationsInFlight) == 0)
            {
                if (Interlocked.Exchange(ref pendingChangedPublication, 0) != 0)
                {
                    PublishChanged();
                    continue;
                }

                if (Volatile.Read(ref lifecycleOperationsInFlight) != 0)
                {
                    break;
                }

                if (Interlocked.Exchange(ref pendingTrustChangedPublication, 0) != 0)
                {
                    PublishTrustChanged();
                    continue;
                }

                break;
            }
        }
        finally
        {
            Volatile.Write(ref publicationDrainActive, 0);
        }

        if (Volatile.Read(ref disposed) == 0
            && Volatile.Read(ref lifecycleOperationsInFlight) == 0
            && (Volatile.Read(ref pendingChangedPublication) != 0
                || Volatile.Read(ref pendingTrustChangedPublication) != 0))
        {
            DrainPendingPublications();
        }
        else if (Volatile.Read(ref disposed) != 0)
        {
            DiscardPendingPublications();
        }
    }

    private void DiscardPendingPublications()
    {
        Interlocked.Exchange(ref pendingChangedPublication, 0);
        Interlocked.Exchange(ref pendingTrustChangedPublication, 0);
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

    private sealed class LifecycleCallbackScope
    {
        private int active = 1;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }

    private sealed class LifecycleCallbackScopeLease : IDisposable
    {
        private readonly LifecycleCallbackScope current = new();
        private readonly AsyncLocal<LifecycleCallbackScope?> owner;
        private readonly LifecycleCallbackScope? previous;
        private int disposed;

        public LifecycleCallbackScopeLease(
            AsyncLocal<LifecycleCallbackScope?> owner)
        {
            this.owner = owner;
            previous = owner.Value;
            owner.Value = current;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            current.Deactivate();
            owner.Value = previous;
        }
    }
}
