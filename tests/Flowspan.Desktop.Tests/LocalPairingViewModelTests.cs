using System.Collections.Immutable;
using System.Net;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class LocalPairingViewModelTests
{
    [Fact]
    public async Task ExplicitEnableStartsNetworkAndKeepsSharingClaimInactive()
    {
        var factory = new RecordingNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);

        Assert.Equal("LOCAL PAIRING OFF", viewModel.Status);
        Assert.Contains("local network", viewModel.PermissionEducation);
        Assert.Equal(0, factory.StartCount);

        await viewModel.EnableAsync();

        Assert.Equal(1, factory.StartCount);
        Assert.True(viewModel.IsEnabled);
        Assert.Equal("LOCAL PAIRING ENABLED", viewModel.Status);
        Assert.Contains("NOT SHARING", viewModel.StatusDescription);
        Assert.Equal("Listening on local TCP port 4747", viewModel.ListenerStatus);
    }

    [Fact]
    public async Task CandidateProjectionNamesUnverifiedAndIdentityBlockedStates()
    {
        using DeviceIdentity unverifiedIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        using DeviceIdentity changedIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Changed desk");
        UnverifiedPairingCandidate unverified = CreateCandidate(
            unverifiedIdentity,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        UnverifiedPairingCandidate blocked = CreateCandidate(
            changedIdentity,
            PairingCandidateTrustState.IdentityChangedBlocked,
            4749);
        var runtime = new DesktopLocalPairingRuntime(
            new RecordingNetworkFactory([unverified, blocked]));
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);

        await viewModel.EnableAsync();

        LocalPairingCandidateItemViewModel first = viewModel.Candidates[0];
        LocalPairingCandidateItemViewModel second = viewModel.Candidates[1];
        Assert.Equal("UNVERIFIED — PAIRING REQUIRED", first.Status);
        Assert.True(first.CanPair);
        Assert.Equal(unverified.Offer.IdentityFingerprint, first.Fingerprint);
        Assert.Equal("IDENTITY CHANGED — BLOCKED", second.Status);
        Assert.False(second.CanPair);
    }

    [Fact]
    public async Task SuccessfulPairingRefreshesAuthoritativeTrustedDevices()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var result = new PairingCeremonyResult(
            true,
            PairingFailure.None,
            peer.PublicIdentity,
            new ProtocolVersion(1, 0),
            TrustRegistrationResult.Added);
        var factory = new RecordingNetworkFactory([candidate], result);
        var runtime = new DesktopLocalPairingRuntime(factory);
        int trustRefreshes = 0;
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance,
            _ =>
            {
                trustRefreshes++;
                return Task.CompletedTask;
            });
        viewModel.SetPrerequisitesAvailable(true);
        await viewModel.EnableAsync();
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);

        await viewModel.PairSelectedAsync();

        Assert.Equal(1, factory.LastSession?.PairCount);
        Assert.Same(candidate, factory.LastSession?.LastCandidate);
        Assert.Equal(1, trustRefreshes);
        Assert.Equal("DEVICE PAIRED", viewModel.PairingStatus);
        Assert.False(viewModel.IsPairing);
    }

    [Fact]
    public async Task CommandStatesPermitOnlyExplicitSafeActions()
    {
        using DeviceIdentity unverifiedIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        using DeviceIdentity changedIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Changed desk");
        var runtime = new DesktopLocalPairingRuntime(
            new RecordingNetworkFactory([
                CreateCandidate(
                    unverifiedIdentity,
                    PairingCandidateTrustState.UnverifiedPairingRequired,
                    4748),
                CreateCandidate(
                    changedIdentity,
                    PairingCandidateTrustState.IdentityChangedBlocked,
                    4749),
            ]));
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);

        Assert.False(viewModel.EnableCommand.CanExecute(null));
        viewModel.SetPrerequisitesAvailable(true);
        Assert.True(viewModel.EnableCommand.CanExecute(null));
        await viewModel.EnableAsync();
        Assert.False(viewModel.EnableCommand.CanExecute(null));

        viewModel.SelectedCandidate = viewModel.Candidates[1];
        Assert.False(viewModel.PairDeviceCommand.CanExecute(null));
        viewModel.SelectedCandidate = viewModel.Candidates[0];
        Assert.True(viewModel.PairDeviceCommand.CanExecute(null));
        Assert.False(viewModel.CancelPairingCommand.CanExecute(null));
    }

    [Fact]
    public async Task EnableFailureIsSanitizedAndRemainsRetryable()
    {
        const string canary = "CANARY_PRIVATE_NETWORK_DETAIL";
        var runtime = new DesktopLocalPairingRuntime(
            new FailingNetworkFactory(new IOException(canary)));
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);

        await viewModel.EnableAsync();

        Assert.Equal("LOCAL PAIRING UNAVAILABLE", viewModel.Status);
        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.EnableCommand.CanExecute(null));
        Assert.DoesNotContain(canary, viewModel.StatusDescription, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, viewModel.RecoveryAction, StringComparison.Ordinal);
        Assert.Contains("firewall", viewModel.RecoveryAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BackgroundFailureIsSanitizedAndRemainsRetryable()
    {
        var factory = new RecordingNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await viewModel.EnableAsync();
        StubNetworkSession first = Assert.IsType<StubNetworkSession>(
            factory.LastSession);

        first.RaiseFault();

        await WaitUntilAsync(
            () => viewModel.Status == "LOCAL PAIRING UNAVAILABLE"
                && first.Disposed,
            TimeSpan.FromSeconds(2));
        Assert.False(viewModel.IsEnabled);
        Assert.Equal("Listener inactive", viewModel.ListenerStatus);
        Assert.DoesNotContain(
            "CANARY",
            viewModel.StatusDescription,
            StringComparison.Ordinal);
        Assert.Contains(
            "retry",
            viewModel.RecoveryAction,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.EnableCommand.CanExecute(null));
        Assert.True(first.Disposed);

        await viewModel.EnableAsync();

        Assert.Equal(2, factory.StartCount);
        Assert.True(viewModel.IsEnabled);
    }

    [Fact]
    public async Task DisposeCancelsInFlightEnableWithoutLeakingCancellation()
    {
        var factory = new BlockingEnableNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        Task enabling = viewModel.EnableAsync();
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await viewModel.DisposeAsync();

        await enabling.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(factory.CancellationObserved);
    }

    [Fact]
    public async Task CancelCommandStopsTheOneActivePairingAttempt()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new BlockingPairNetworkFactory(candidate);
        var runtime = new DesktopLocalPairingRuntime(factory);
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await viewModel.EnableAsync();
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        Task pairing = viewModel.PairSelectedAsync();
        await factory.Session.PairingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsPairing);
        Assert.True(viewModel.CancelPairingCommand.CanExecute(null));
        viewModel.CancelPairingCommand.Execute(null);
        await pairing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(factory.Session.CancellationObserved);
        Assert.False(viewModel.IsPairing);
        Assert.Equal("PAIRING CANCELED", viewModel.PairingStatus);
    }

    [Fact]
    public async Task InboundTrustChangedRefreshesAuthoritativeTrustedDevices()
    {
        var factory = new RecordingNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        var refreshed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance,
            _ =>
            {
                refreshed.TrySetResult();
                return Task.CompletedTask;
            });
        viewModel.SetPrerequisitesAvailable(true);
        await viewModel.EnableAsync();

        factory.LastSession!.RaiseTrustChanged();

        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static UnverifiedPairingCandidate CreateCandidate(
        DeviceIdentity identity,
        PairingCandidateTrustState state,
        int port)
    {
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            identity,
            port,
            [new ProtocolVersion(1, 0)],
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength).ToArray());
        return new UnverifiedPairingCandidate(
            $"{identity.DeviceId}._flowspan._tcp.local",
            offer,
            new IPEndPoint(IPAddress.Parse("192.168.50.20"), port),
            state);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class RecordingNetworkFactory(
        ImmutableArray<UnverifiedPairingCandidate> candidates = default,
        PairingCeremonyResult? pairingResult = null) :
        IDesktopLocalPairingNetworkFactory
    {
        public StubNetworkSession? LastSession { get; private set; }

        public int StartCount { get; private set; }

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            LastSession = new StubNetworkSession(
                candidates.IsDefault ? [] : candidates,
                pairingResult);
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(LastSession);
        }
    }

    private sealed class FailingNetworkFactory(Exception failure) :
        IDesktopLocalPairingNetworkFactory
    {
        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException<IDesktopLocalPairingNetworkSession>(failure);
        }
    }

    private sealed class BlockingEnableNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The blocking factory unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class BlockingPairNetworkFactory(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkFactory
    {
        public BlockingPairNetworkSession Session { get; } = new(candidate);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class BlockingPairNetworkSession(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkSession
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource PairingStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [candidate];

        public async ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate selected,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(candidate, selected);
            PairingStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The blocking pairing unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubNetworkSession(
        ImmutableArray<UnverifiedPairingCandidate> candidates,
        PairingCeremonyResult? pairingResult) :
        IDesktopLocalPairingNetworkSession
    {
        public UnverifiedPairingCandidate? LastCandidate { get; private set; }

        public int PairCount { get; private set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public event Action? TrustChanged;

        public event Action<IDesktopLocalPairingNetworkSession>? Faulted;

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => candidates;

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PairCount++;
            LastCandidate = candidate;
            return pairingResult is null
                ? ValueTask.FromException<PairingCeremonyResult>(
                    new NotSupportedException())
                : ValueTask.FromResult(pairingResult);
        }

        public bool Disposed { get; private set; }

        public bool IsFaulted { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseFault()
        {
            IsFaulted = true;
            Faulted?.Invoke(this);
        }

        public void RaiseTrustChanged() => TrustChanged?.Invoke();
    }
}
