using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class WorkspaceShellViewModelTests
{
    [Fact]
    public async Task InitializeAsyncExposesTruthfulSafeState()
    {
        var startup = new StubStartup(new LocalIdentitySnapshot(
            "Desk",
            "11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            "Operating-system protected",
            false));
        await using var viewModel = new WorkspaceShellViewModel(startup);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsIdentityAvailable);
        Assert.False(viewModel.IsStartupBlocked);
        Assert.False(viewModel.IsTestMode);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.Equal("LOCAL WORKSPACE READY", viewModel.StartupStatus);
        Assert.Contains("sharing remain inactive", viewModel.StartupDescription);
    }

    [Fact]
    public async Task ToggleIdentityDetailsCommandChangesVisibleTextAndState()
    {
        await using var viewModel = CreateReadyViewModel();
        await viewModel.InitializeAsync();

        viewModel.ToggleIdentityDetailsCommand.Execute(null);

        Assert.True(viewModel.IsIdentityDetailsVisible);
        Assert.Equal("Hide identity details", viewModel.IdentityDetailsActionLabel);

        viewModel.ToggleIdentityDetailsCommand.Execute(null);

        Assert.False(viewModel.IsIdentityDetailsVisible);
        Assert.Equal("Show identity details", viewModel.IdentityDetailsActionLabel);
    }

    [Fact]
    public async Task InitializeAsyncBlocksWithoutLeakingStartupException()
    {
        const string canary = "CANARY_SECRET_STORE_DETAIL";
        await using var viewModel = new WorkspaceShellViewModel(
            new StubStartup(new IOException(canary)));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsStartupBlocked);
        Assert.False(viewModel.IsIdentityAvailable);
        Assert.False(viewModel.IsEmergencyStopAvailable);
        Assert.Equal("IDENTITY UNAVAILABLE", viewModel.StartupStatus);
        Assert.DoesNotContain(canary, viewModel.StartupDescription, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, viewModel.RecoveryAction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeDuringInitializationCancelsBeforeDisposingStartup()
    {
        var startup = new BlockingStartup();
        var viewModel = new WorkspaceShellViewModel(startup);
        Task initialization = viewModel.InitializeAsync();
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        await Task.WhenAll(initialization, disposing)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(startup.Disposed);
        Assert.False(startup.WasDisposedWhileInitializing);
    }

    [Fact]
    public async Task SecondInitializationRecoversAfterTransientFailure()
    {
        var startup = new RecoveringStartup();
        await using var viewModel = new WorkspaceShellViewModel(startup);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.IsStartupBlocked);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsIdentityAvailable);
        Assert.False(viewModel.IsStartupBlocked);
        Assert.Equal(2, startup.Attempts);
    }

    [Fact]
    public async Task DisposeAsyncCancelsTrustMutationBeforeDisposingDependencies()
    {
        var startup = new TrackingStartup();
        var authority = new BlockingTrustAuthority();
        var viewModel = new WorkspaceShellViewModel(
            startup,
            trustAuthority: authority);
        await viewModel.InitializeAsync();
        viewModel.TrustedDevices.GrantActivityOffer = true;
        Task saving = viewModel.TrustedDevices.SaveCapabilitiesAsync();
        await authority.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();

        await Task.WhenAll(saving, disposing).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(authority.Disposed);
        Assert.False(authority.WasDisposedDuringMutation);
        Assert.True(startup.Disposed);
    }

    private static WorkspaceShellViewModel CreateReadyViewModel() => new(
        new StubStartup(new LocalIdentitySnapshot(
            "Desk",
            "11111111-1111-1111-1111-111111111111",
            new string('A', 64),
            "Operating-system protected",
            false)));

    private sealed class StubStartup : IDesktopIdentityStartup
    {
        private readonly Exception? failure;
        private readonly LocalIdentitySnapshot? snapshot;

        public StubStartup(LocalIdentitySnapshot snapshot) => this.snapshot = snapshot;

        public StubStartup(Exception failure) => this.failure = failure;

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null
                ? ValueTask.FromResult(snapshot!)
                : ValueTask.FromException<LocalIdentitySnapshot>(failure);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingStartup : IDesktopIdentityStartup
    {
        public bool Disposed { get; private set; }

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class BlockingTrustAuthority : IDesktopTrustAuthority
    {
        private readonly DeviceId peerDeviceId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        private bool mutationActive;

        public bool Disposed { get; private set; }

        public TaskCompletionSource MutationStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposedDuringMutation { get; private set; }

        public ValueTask<DesktopTrustSnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DesktopTrustSnapshot(
                SecretStoreProtection.OperatingSystemProtected,
                [new TrustedPeerSnapshot(
                    peerDeviceId,
                    "Peer desk",
                    new string('B', 64),
                    DateTimeOffset.UnixEpoch,
                    CapabilityGrant.None)]));
        }

        public async ValueTask<DesktopTrustMutationOutcome> UpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default)
        {
            mutationActive = true;
            MutationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The blocking Trust mutation unexpectedly completed.");
            }
            finally
            {
                mutationActive = false;
            }
        }

        public ValueTask<DesktopTrustMutationOutcome> RevokeAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DesktopTrustMutationOutcome>(
                new NotSupportedException());

        public ValueTask<TrustSessionRegistration?> TryRegisterSessionAsync(
            DeviceId peerDeviceId,
            CapabilityGrant requiredCapabilities,
            IRevocablePeerSession session,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TrustSessionRegistration?>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            WasDisposedDuringMutation = mutationActive;
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingStartup : IDesktopIdentityStartup
    {
        private bool initializing;

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public bool WasDisposedWhileInitializing { get; private set; }

        public async ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            initializing = true;
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking startup unexpectedly completed.");
            }
            finally
            {
                initializing = false;
            }
        }

        public void Dispose()
        {
            WasDisposedWhileInitializing = initializing;
            Disposed = true;
        }
    }

    private sealed class RecoveringStartup : IDesktopIdentityStartup
    {
        public int Attempts { get; private set; }

        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            if (Attempts == 1)
            {
                return ValueTask.FromException<LocalIdentitySnapshot>(
                    new IOException("Transient credential-store failure."));
            }

            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose()
        {
        }
    }
}
