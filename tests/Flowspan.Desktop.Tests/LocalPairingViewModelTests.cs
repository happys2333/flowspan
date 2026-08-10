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
    public async Task ReviewAndAcknowledgementAreRequiredBeforeNetworkStarts()
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
        Assert.True(viewModel.ReviewPermissionCommand.CanExecute(null));
        Assert.False(viewModel.EnableCommand.CanExecute(null));

        await viewModel.EnableAsync();
        Assert.Equal(0, factory.StartCount);

        viewModel.ReviewPermissionCommand.Execute(null);

        Assert.True(viewModel.IsPermissionReviewVisible);
        Assert.False(viewModel.EnableCommand.CanExecute(null));
        Assert.Equal(0, factory.StartCount);
        viewModel.HasAcknowledgedPermissionReview = true;
        Assert.True(viewModel.EnableCommand.CanExecute(null));

        await viewModel.EnableAsync();

        Assert.Equal(1, factory.StartCount);
        Assert.True(viewModel.IsEnabled);
        Assert.Equal("LOCAL PAIRING ENABLED", viewModel.Status);
        Assert.Contains("NOT SHARING", viewModel.StatusDescription);
        Assert.Equal("Listening on local TCP port 4747", viewModel.ListenerStatus);
        Assert.False(viewModel.IsPermissionReviewVisible);
    }

    [Theory]
    [InlineData(DesktopPlatformFamily.Windows, "Windows", "private networks", "Windows Security")]
    [InlineData(DesktopPlatformFamily.MacOS, "macOS", "Local Network", "System Settings")]
    [InlineData(DesktopPlatformFamily.Linux, "Linux", "firewall", "distribution")]
    public void PlatformGuideNamesExposurePromptAndRevocation(
        DesktopPlatformFamily platform,
        string platformName,
        string promptFragment,
        string revocationFragment)
    {
        DesktopLocalNetworkPermissionGuide guide =
            DesktopLocalNetworkPermissionGuide.ForPlatform(platform);

        Assert.Equal(platform, guide.Platform);
        Assert.Equal(platformName, guide.PlatformName);
        Assert.Contains("Device ID", guide.DataExposure);
        Assert.Contains("identity fingerprint", guide.DataExposure);
        Assert.Contains("Activity content", guide.DataExposure);
        Assert.Contains(promptFragment, guide.PromptExpectation);
        Assert.Contains(revocationFragment, guide.RevocationAction);
    }

    [Fact]
    public void CurrentPlatformGuideMatchesTheHostedOperatingSystem()
    {
        DesktopPlatformFamily expected = OperatingSystem.IsWindows()
            ? DesktopPlatformFamily.Windows
            : OperatingSystem.IsMacOS()
                ? DesktopPlatformFamily.MacOS
                : OperatingSystem.IsLinux()
                    ? DesktopPlatformFamily.Linux
                    : throw new PlatformNotSupportedException();

        Assert.Equal(
            expected,
            DesktopLocalNetworkPermissionGuide.ForCurrentPlatform().Platform);
    }

    [Fact]
    public async Task CancelingPermissionReviewClearsAcknowledgementWithoutNetworking()
    {
        var factory = new RecordingNetworkFactory();
        await using var viewModel = new LocalPairingViewModel(
            new DesktopLocalPairingRuntime(factory),
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        viewModel.ReviewPermissionCommand.Execute(null);
        viewModel.HasAcknowledgedPermissionReview = true;

        viewModel.CancelPermissionReviewCommand.Execute(null);

        Assert.False(viewModel.IsPermissionReviewVisible);
        Assert.False(viewModel.HasAcknowledgedPermissionReview);
        Assert.Equal(0, factory.StartCount);
        Assert.True(viewModel.ReviewPermissionCommand.CanExecute(null));
        Assert.False(viewModel.EnableCommand.CanExecute(null));
    }

    [Fact]
    public async Task DisableRequiresFreshReviewForTheNextNetworkLifetime()
    {
        var factory = new RecordingNetworkFactory();
        await using var viewModel = new LocalPairingViewModel(
            new DesktopLocalPairingRuntime(factory),
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);

        await viewModel.DisableAsync();

        Assert.False(viewModel.IsEnabled);
        Assert.False(viewModel.IsPermissionReviewVisible);
        Assert.False(viewModel.HasAcknowledgedPermissionReview);
        Assert.True(viewModel.ReviewPermissionCommand.CanExecute(null));
        Assert.False(viewModel.EnableCommand.CanExecute(null));
        Assert.Equal(1, factory.StartCount);
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

        await ReviewAndEnableAsync(viewModel);

        LocalPairingCandidateItemViewModel first = viewModel.Candidates[0];
        LocalPairingCandidateItemViewModel second = viewModel.Candidates[1];
        Assert.Equal("UNVERIFIED — PAIRING REQUIRED", first.Status);
        Assert.True(first.CanPair);
        Assert.Equal(unverified.Offer.IdentityFingerprint, first.Fingerprint);
        Assert.Equal("IDENTITY CHANGED — BLOCKED", second.Status);
        Assert.False(second.CanPair);
    }

    [Fact]
    public async Task TrustedReconnectProjectionKeepsIdleAndIdentityWarningTruthful()
    {
        var peerDeviceId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        const string expected =
            "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string conflicting =
            "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var connection = new DesktopTrustedPeerConnectionSnapshot(
            peerDeviceId,
            "Peer desk",
            expected,
            DesktopTrustedPeerConnectionState.AuthenticatedIdle,
            null,
            null,
            conflicting);
        var runtime = new DesktopLocalPairingRuntime(
            new RecordingNetworkFactory(connections: [connection]));
        await using var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);

        await ReviewAndEnableAsync(viewModel);

        TrustedPeerConnectionItemViewModel item =
            Assert.Single(viewModel.TrustedPeerConnections);
        Assert.Equal("AUTHENTICATED — IDLE / NOT SHARING", item.Status);
        Assert.True(item.HasIdentityWarning);
        Assert.Equal(expected, item.ExpectedFingerprint);
        Assert.Equal(conflicting, item.ConflictingFingerprint);
        Assert.Contains("discovery alone", item.IdentityWarning);
        Assert.True(viewModel.HasTrustedPeerConnections);
        Assert.True(viewModel.HasIdentityWarnings);
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
        await ReviewAndEnableAsync(viewModel);
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

        Assert.False(viewModel.ReviewPermissionCommand.CanExecute(null));
        Assert.False(viewModel.EnableCommand.CanExecute(null));
        viewModel.SetPrerequisitesAvailable(true);
        Assert.True(viewModel.ReviewPermissionCommand.CanExecute(null));
        Assert.False(viewModel.EnableCommand.CanExecute(null));
        viewModel.ReviewPermissionCommand.Execute(null);
        viewModel.HasAcknowledgedPermissionReview = true;
        Assert.True(viewModel.EnableCommand.CanExecute(null));
        await viewModel.EnableAsync();
        Assert.False(viewModel.ReviewPermissionCommand.CanExecute(null));
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

        await ReviewAndEnableAsync(viewModel);

        Assert.Equal("LOCAL PAIRING UNAVAILABLE", viewModel.Status);
        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.IsPermissionReviewVisible);
        Assert.True(viewModel.HasAcknowledgedPermissionReview);
        Assert.True(viewModel.EnableCommand.CanExecute(null));
        Assert.DoesNotContain(canary, viewModel.StatusDescription, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, viewModel.RecoveryAction, StringComparison.Ordinal);
        Assert.Contains("firewall", viewModel.RecoveryAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CleanupUnconfirmedFailureBlocksEnableRetry()
    {
        var factory = new CleanupUnconfirmedNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);

        await ReviewAndEnableAsync(viewModel);

        Assert.Equal("LOCAL PAIRING UNAVAILABLE", viewModel.Status);
        Assert.Equal(DesktopLocalPairingStatus.CleanupUnconfirmed, runtime.Status);
        Assert.Equal(4747, runtime.ListeningPort);
        Assert.Contains(
            "cleanup unconfirmed",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "may still be listening",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4747", viewModel.ListenerStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "inactive",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.HasAcknowledgedPermissionReview);
        Assert.False(viewModel.EnableCommand.CanExecute(null));
        Assert.Equal(1, factory.StartCount);
        await viewModel.EnableAsync();
        Assert.Equal(1, factory.StartCount);
        Assert.Equal(
            DesktopLocalPairingStatus.CleanupUnconfirmed,
            runtime.Status);
        Assert.DoesNotContain(
            "inactive",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        await viewModel.DisposeAsync();
        Assert.Equal(2, factory.Session.DisposeCount);
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
        await ReviewAndEnableAsync(viewModel);
        StubNetworkSession first = Assert.IsType<StubNetworkSession>(
            factory.LastSession);
        var failurePublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Changed += OnRuntimeChanged;

        try
        {
            first.RaiseFault();
            await failurePublished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            runtime.Changed -= OnRuntimeChanged;
        }

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

        void OnRuntimeChanged()
        {
            if (runtime.Status == DesktopLocalPairingStatus.Faulted
                && first.Disposed
                && viewModel.Status == "LOCAL PAIRING UNAVAILABLE")
            {
                failurePublished.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task BackgroundCleanupUnconfirmedWarnsListenerMayRemainActive()
    {
        var factory = new BackgroundCleanupUnconfirmedNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        var failurePublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Changed += OnRuntimeChanged;

        try
        {
            factory.Session.RaiseFault();
            await failurePublished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            runtime.Changed -= OnRuntimeChanged;
        }

        Assert.Equal(4747, runtime.ListeningPort);
        Assert.Contains(
            "cleanup unconfirmed",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "may still be listening",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4747", viewModel.ListenerStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "inactive",
            viewModel.ListenerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.EnableCommand.CanExecute(null));
        Assert.Equal(1, factory.StartCount);

        await viewModel.DisposeAsync();
        Assert.Equal(2, factory.Session.DisposeCount);

        void OnRuntimeChanged()
        {
            if (runtime.Status == DesktopLocalPairingStatus.CleanupUnconfirmed
                && factory.Session.DisposeCount == 1
                && viewModel.Status == "LOCAL PAIRING UNAVAILABLE")
            {
                failurePublished.TrySetResult();
            }
        }
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
        viewModel.ReviewPermissionCommand.Execute(null);
        viewModel.HasAcknowledgedPermissionReview = true;
        Task enabling = viewModel.EnableAsync();
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await viewModel.DisposeAsync();

        await enabling.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(factory.CancellationObserved);
    }

    [Fact]
    public async Task DisposeStopsRuntimeBeforeDrainingAdmittedEnablePresentation()
    {
        var factory = new ReleasableEnableNetworkFactory();
        var dispatcher = new QueuedDispatcher();
        var viewModel = new LocalPairingViewModel(runtime: new(factory), dispatcher);
        viewModel.SetPrerequisitesAvailable(true);
        viewModel.ReviewPermissionCommand.Execute(null);
        viewModel.HasAcknowledgedPermissionReview = true;
        var presentationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var presentationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int disposalCompleted = 0;
        int propertyChangesAfterDisposal = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (Volatile.Read(ref disposalCompleted) != 0)
            {
                Interlocked.Increment(ref propertyChangesAfterDisposal);
            }

            if (eventArgs.PropertyName
                    == nameof(LocalPairingViewModel.IsPermissionReviewVisible)
                && !viewModel.IsPermissionReviewVisible)
            {
                presentationEntered.TrySetResult();
                presentationRelease.Task.GetAwaiter().GetResult();
            }
        };

        Task enabling = viewModel.EnableAsync();
        await factory.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        factory.ReleaseStart();
        await presentationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        int returnedBeforePresentationReleased = 0;
        Task disposalObserved = disposing.ContinueWith(
            _ =>
            {
                if (!presentationRelease.Task.IsCompleted)
                {
                    Interlocked.Exchange(
                        ref returnedBeforePresentationReleased,
                        1);
                }

                Volatile.Write(ref disposalCompleted, 1);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await factory.Session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        presentationRelease.TrySetResult();
        await enabling.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(disposing, disposalObserved)
            .WaitAsync(TimeSpan.FromSeconds(2));
        dispatcher.RunAll();

        Assert.Equal(0, Volatile.Read(ref returnedBeforePresentationReleased));
        Assert.Equal(0, Volatile.Read(ref propertyChangesAfterDisposal));
    }

    [Fact]
    public async Task EnablePresentationObserverCanInitiateDisposeWithoutWaiting()
    {
        var factory = new ReleasableEnableNetworkFactory();
        var dispatcher = new QueuedDispatcher();
        var viewModel = new LocalPairingViewModel(runtime: new(factory), dispatcher);
        viewModel.SetPrerequisitesAvailable(true);
        viewModel.ReviewPermissionCommand.Execute(null);
        viewModel.HasAcknowledgedPermissionReview = true;
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposeCompletedSynchronously = false;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName
                    != nameof(LocalPairingViewModel.IsPermissionReviewVisible)
                || viewModel.IsPermissionReviewVisible)
            {
                return;
            }

            ValueTask reentrantDispose = viewModel.DisposeAsync();
            disposeCompletedSynchronously =
                reentrantDispose.IsCompletedSuccessfully;
            observerReturned.TrySetResult();
        };

        Task enabling = viewModel.EnableAsync();
        await factory.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        factory.ReleaseStart();
        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await enabling.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        dispatcher.RunAll();

        Assert.True(disposeCompletedSynchronously);
        Assert.True(factory.Session.DisposeEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposeDrainsDisablePresentationAfterRuntimeStopCompletes()
    {
        var factory = new BlockingDisableNetworkFactory();
        var dispatcher = new QueuedDispatcher();
        var viewModel = new LocalPairingViewModel(runtime: new(factory), dispatcher);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        dispatcher.RunAll();
        var presentationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var presentationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int disposalCompleted = 0;
        int propertyChangesAfterDisposal = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (Volatile.Read(ref disposalCompleted) != 0)
            {
                Interlocked.Increment(ref propertyChangesAfterDisposal);
            }

            if (eventArgs.PropertyName == nameof(LocalPairingViewModel.IsEnabled)
                && !viewModel.IsEnabled)
            {
                presentationEntered.TrySetResult();
                presentationRelease.Task.GetAwaiter().GetResult();
            }
        };

        Task disabling = viewModel.DisableAsync();
        await factory.Session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task disposing = viewModel.DisposeAsync().AsTask();
        int returnedBeforePresentationReleased = 0;
        Task disposalObserved = disposing.ContinueWith(
            _ =>
            {
                if (!presentationRelease.Task.IsCompleted)
                {
                    Interlocked.Exchange(
                        ref returnedBeforePresentationReleased,
                        1);
                }

                Volatile.Write(ref disposalCompleted, 1);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        factory.Session.ReleaseDispose();
        await presentationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        presentationRelease.TrySetResult();
        await disabling.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(disposing, disposalObserved)
            .WaitAsync(TimeSpan.FromSeconds(2));
        dispatcher.RunAll();

        Assert.Equal(0, Volatile.Read(ref returnedBeforePresentationReleased));
        Assert.Equal(0, Volatile.Read(ref propertyChangesAfterDisposal));
    }

    [Fact]
    public async Task DisposeStopsRuntimeBeforeBlockingThrowingPairCancellationReturns()
    {
        const string canary = "HOSTILE_PAIR_CANCELLATION_CALLBACK";
        const string disposeCanary = "HOSTILE_PAIR_SESSION_DISPOSE";
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new HostilePairCancellationNetworkFactory(
            candidate,
            canary,
            disposeCanary);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        Task pairing = viewModel.PairSelectedAsync();
        await factory.Session.PairingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = Task.Run(async () => await viewModel.DisposeAsync());
        await factory.Session.CancellationCallbackEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        Exception? disposalFailure = null;
        try
        {
            await factory.Session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            factory.Session.ReleaseCancellationCallback();
            try
            {
                await disposing.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }

            await pairing.WaitAsync(TimeSpan.FromSeconds(2));
            if (!factory.Session.IsDisposed)
            {
                try
                {
                    await runtime.DisposeAsync();
                }
                catch
                {
                    // The RED cleanup path still releases the owned session.
                }
            }
        }

        Assert.True(factory.Session.IsDisposed);
        Assert.NotNull(disposalFailure);
        Assert.Contains(canary, disposalFailure.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            disposeCanary,
            disposalFailure.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentDisposeCallersJoinTheSameViewModelCleanupFailure()
    {
        const string canary = "VIEW_MODEL_SHARED_DISPOSAL_FAILURE";
        var factory = new BlockingFailingDisposeNetworkFactory(canary);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);

        Task first = viewModel.DisposeAsync().AsTask();
        await factory.Session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task second = viewModel.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        factory.Session.ReleaseDispose();
        InvalidOperationException firstFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                first.WaitAsync(TimeSpan.FromSeconds(2)));
        InvalidOperationException secondFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                second.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Same(firstFailure, secondFailure);
        Assert.Equal(1, factory.Session.DisposeCount);
    }

    [Fact]
    public async Task DisposalKeepsPairingGateReleasableForAdmittedWaiters()
    {
        var viewModel = new LocalPairingViewModel(
            new DesktopLocalPairingRuntime(new RecordingNetworkFactory()),
            InlineDesktopUiDispatcher.Instance);
        await viewModel.DisposeAsync();
        SemaphoreSlim gate = Assert.IsType<SemaphoreSlim>(
            typeof(LocalPairingViewModel)
                .GetField(
                    "pairingGate",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(viewModel));

        await gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Exception? releaseFailure = Record.Exception(() => gate.Release());

        Assert.Null(releaseFailure);
    }

    [Fact]
    public async Task PairCancellationCallbackDisposeDoesNotWaitOnItsOwnCleanup()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new ReentrantPairCancellationNetworkFactory(candidate);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        factory.DisposeViewModel = viewModel.DisposeAsync;
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        Task pairing = viewModel.PairSelectedAsync();
        await factory.Session.PairingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        await factory.Session.CancellationCallbackCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));

        await pairing.WaitAsync(TimeSpan.FromSeconds(2));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(factory.Session.ReentrantDisposeCompletedSynchronously);
    }

    [Fact]
    public async Task CancelCallbackCanInitiateViewModelDisposeWithoutWaiting()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new ReentrantPairCancellationNetworkFactory(candidate);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        factory.DisposeViewModel = viewModel.DisposeAsync;
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        Task pairing = viewModel.PairSelectedAsync();
        await factory.Session.PairingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.CancelPairing();
        await factory.Session.CancellationCallbackCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        await pairing.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(factory.Session.ReentrantDisposeCompletedSynchronously);
    }

    [Fact]
    public async Task CapturedPairContextExpiresAfterPairBoundaryReturns()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new CapturedPairContextNetworkFactory(candidate);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        factory.Session.DisposeViewModel = viewModel.DisposeAsync;
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        await viewModel.PairSelectedAsync();

        factory.Session.RunCapturedDispose();
        await factory.Session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int returnedBeforeSessionDisposeReleased = 0;
        Task disposalObserved =
            factory.Session.CapturedDisposeCompleted.Task.ContinueWith(
                _ =>
                {
                    if (!factory.Session.DisposeReleased)
                    {
                        Interlocked.Exchange(
                            ref returnedBeforeSessionDisposeReleased,
                            1);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        try
        {
            Assert.Equal(
                0,
                Volatile.Read(ref returnedBeforeSessionDisposeReleased));
        }
        finally
        {
            factory.Session.ReleaseDispose();
            await Task.WhenAll(
                    factory.Session.CapturedDisposeCompleted.Task,
                    disposalObserved)
                .WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(0, Volatile.Read(ref returnedBeforeSessionDisposeReleased));
    }

    [Fact]
    public async Task PairingCancellationSourceIsDisposedAfterCallbackAndOperationDrain()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new PairCancellationSourceLifetimeNetworkFactory(candidate);
        var runtime = new DesktopLocalPairingRuntime(factory);
        var viewModel = new LocalPairingViewModel(
            runtime,
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        Task pairing = viewModel.PairSelectedAsync();
        await factory.Session.PairingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        await factory.Session.CancellationCallbackEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        await pairing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(disposing.IsCompleted);

        factory.Session.ReleaseCancellationCallback();
        await factory.Session.CancellationCallbackCompleted.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(factory.Session.CallbackObservedDisposedSource);
    }

    [Fact]
    public async Task PairPresentationObserverCanInitiateDisposeWithoutWaiting()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new RecordingNetworkFactory([candidate]);
        var viewModel = new LocalPairingViewModel(
            new DesktopLocalPairingRuntime(factory),
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposeCompletedSynchronously = false;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName != nameof(LocalPairingViewModel.IsPairing)
                || !viewModel.IsPairing)
            {
                return;
            }

            ValueTask reentrantDispose = viewModel.DisposeAsync();
            disposeCompletedSynchronously =
                reentrantDispose.IsCompletedSuccessfully;
            observerReturned.TrySetResult();
        };

        Task pairing = viewModel.PairSelectedAsync();
        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await pairing.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(disposeCompletedSynchronously);
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
        await ReviewAndEnableAsync(viewModel);
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
        await ReviewAndEnableAsync(viewModel);

        factory.LastSession!.RaiseTrustChanged();

        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TrustPresentationObserverCanInitiateDisposeWithoutWaiting()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        UnverifiedPairingCandidate candidate = CreateCandidate(
            peer,
            PairingCandidateTrustState.UnverifiedPairingRequired,
            4748);
        var factory = new RecordingNetworkFactory([candidate]);
        var viewModel = new LocalPairingViewModel(
            new DesktopLocalPairingRuntime(factory),
            InlineDesktopUiDispatcher.Instance);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        viewModel.SelectedCandidate = Assert.Single(viewModel.Candidates);
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposeCompletedSynchronously = false;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName
                    != nameof(LocalPairingViewModel.SelectedCandidate)
                || viewModel.SelectedCandidate is not null)
            {
                return;
            }

            ValueTask reentrantDispose = viewModel.DisposeAsync();
            disposeCompletedSynchronously =
                reentrantDispose.IsCompletedSuccessfully;
            observerReturned.TrySetResult();
        };

        factory.LastSession!.RaiseTrustChanged();
        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(disposeCompletedSynchronously);
    }

    [Fact]
    public async Task QueuedTrustChangedReturnsQuietlyAfterDisposal()
    {
        var factory = new RecordingNetworkFactory();
        var runtime = new DesktopLocalPairingRuntime(factory);
        var dispatcher = new QueuedDispatcher();
        int trustRefreshes = 0;
        var viewModel = new LocalPairingViewModel(
            runtime,
            dispatcher,
            _ =>
            {
                trustRefreshes++;
                return Task.CompletedTask;
            });
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        dispatcher.RunAll();
        int propertyChanges = 0;
        viewModel.PropertyChanged += (_, _) => propertyChanges++;

        factory.LastSession!.RaiseTrustChanged();
        Assert.Equal(1, dispatcher.Count);
        await viewModel.DisposeAsync();
        int changesBeforeDispatch = propertyChanges;
        string pairingStatusBeforeDispatch = viewModel.PairingStatus;

        Exception? dispatchFailure = Record.Exception(dispatcher.RunAll);
        await Task.Yield();

        Assert.Null(dispatchFailure);
        Assert.Equal(0, trustRefreshes);
        Assert.Equal(changesBeforeDispatch, propertyChanges);
        Assert.Equal(pairingStatusBeforeDispatch, viewModel.PairingStatus);
    }

    [Fact]
    public async Task DisposeDoesNotRaceAnAdmittedQueuedRuntimeRead()
    {
        var factory = new BlockingCandidateReadNetworkFactory();
        var dispatcher = new QueuedDispatcher();
        var viewModel = new LocalPairingViewModel(runtime: new(factory), dispatcher);
        viewModel.SetPrerequisitesAvailable(true);
        await ReviewAndEnableAsync(viewModel);
        dispatcher.RunAll();
        factory.Session.BlockNextCandidateRead();
        factory.Session.RaiseChanged();
        Assert.Equal(1, dispatcher.Count);

        Task dispatching = Task.Run(dispatcher.RunAll);
        await factory.Session.CandidateReadEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        Task disposing = viewModel.DisposeAsync().AsTask();
        int disposedBeforeCandidateReadReleased = 0;
        Task disposalObserved = factory.Session.DisposeEntered.Task.ContinueWith(
            _ =>
            {
                if (!factory.Session.CandidateReadReleased)
                {
                    Interlocked.Exchange(
                        ref disposedBeforeCandidateReadReleased,
                        1);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            Assert.Equal(
                0,
                Volatile.Read(ref disposedBeforeCandidateReadReleased));
        }
        finally
        {
            factory.Session.ReleaseCandidateRead();
        }

        Exception? dispatchFailure = await Record.ExceptionAsync(() =>
            dispatching.WaitAsync(TimeSpan.FromSeconds(2)));
        await Task.WhenAll(factory.Session.DisposeEntered.Task, disposalObserved)
            .WaitAsync(TimeSpan.FromSeconds(2));
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, Volatile.Read(ref disposedBeforeCandidateReadReleased));
        Assert.Null(dispatchFailure);
        Assert.False(factory.Session.DisposedWhileCandidateRead);
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

    private static async Task ReviewAndEnableAsync(LocalPairingViewModel viewModel)
    {
        viewModel.ReviewPermissionCommand.Execute(null);
        viewModel.HasAcknowledgedPermissionReview = true;
        await viewModel.EnableAsync();
    }

    private sealed class QueuedDispatcher : IDesktopUiDispatcher
    {
        private readonly Queue<Action> callbacks = [];

        public int Count => callbacks.Count;

        public void Post(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            callbacks.Enqueue(callback);
        }

        public void RunAll()
        {
            while (callbacks.TryDequeue(out Action? callback))
            {
                callback();
            }
        }
    }

    private sealed class RecordingNetworkFactory(
        ImmutableArray<UnverifiedPairingCandidate> candidates = default,
        PairingCeremonyResult? pairingResult = null,
        ImmutableArray<DesktopTrustedPeerConnectionSnapshot> connections = default) :
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
                pairingResult,
                connections.IsDefault ? [] : connections);
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

    private sealed class CleanupUnconfirmedNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public int StartCount { get; private set; }

        public CleanupUnconfirmedNetworkSession Session { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class CleanupUnconfirmedNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int DisposeCount { get; private set; }

        public bool IsFaulted => true;

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeCount == 1
                ? ValueTask.FromException(
                    new IOException("VIEW_CLEANUP_UNCONFIRMED"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class BackgroundCleanupUnconfirmedNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public int StartCount { get; private set; }

        public BackgroundCleanupUnconfirmedNetworkSession Session { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class BackgroundCleanupUnconfirmedNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public event Action<IDesktopLocalPairingNetworkSession>? Faulted;

        public int DisposeCount { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeCount == 1
                ? ValueTask.FromException(
                    new IOException("BACKGROUND_CLEANUP_UNCONFIRMED"))
                : ValueTask.CompletedTask;
        }

        public void RaiseFault() => Faulted?.Invoke(this);
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

    private sealed class ReleasableEnableNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        private readonly TaskCompletionSource startRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ReleasableEnableNetworkSession Session { get; } = new();

        public TaskCompletionSource StartEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            StartEntered.TrySetResult();
            await startRelease.Task.WaitAsync(cancellationToken);
            return Session;
        }

        public void ReleaseStart() => startRelease.TrySetResult();
    }

    private sealed class ReleasableEnableNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDisableNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public BlockingDisableNetworkSession Session { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class BlockingDisableNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await disposeRelease.Task.ConfigureAwait(false);
        }

        public void ReleaseDispose() => disposeRelease.TrySetResult();
    }

    private sealed class BlockingCandidateReadNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public BlockingCandidateReadNetworkSession Session { get; } = new();

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class BlockingCandidateReadNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        private TaskCompletionSource candidateReadEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource candidateReadRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int blockCandidateRead;
        private int candidateReadInFlight;
        private int disposed;

        public event Action? Changed;

        public TaskCompletionSource CandidateReadEntered => candidateReadEntered;

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CandidateReadReleased =>
            candidateReadRelease.Task.IsCompletedSuccessfully;

        public bool DisposedWhileCandidateRead { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates()
        {
            if (Interlocked.Exchange(ref blockCandidateRead, 0) == 0)
            {
                return [];
            }

            Volatile.Write(ref candidateReadInFlight, 1);
            CandidateReadEntered.TrySetResult();
            try
            {
                candidateReadRelease.Task.GetAwaiter().GetResult();
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref disposed) != 0,
                    this);
                return [];
            }
            finally
            {
                Volatile.Write(ref candidateReadInFlight, 0);
            }
        }

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposedWhileCandidateRead =
                Volatile.Read(ref candidateReadInFlight) != 0;
            Volatile.Write(ref disposed, 1);
            DisposeEntered.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void BlockNextCandidateRead()
        {
            candidateReadEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            candidateReadRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref blockCandidateRead, 1);
        }

        public void RaiseChanged() => Changed?.Invoke();

        public void ReleaseCandidateRead() => candidateReadRelease.TrySetResult();
    }

    private sealed class BlockingPairNetworkFactory(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkFactory
    {
        public BlockingPairNetworkSession Session { get; } = new(candidate);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class ReentrantPairCancellationNetworkFactory(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkFactory
    {
        public Func<ValueTask>? DisposeViewModel { get; set; }

        public ReentrantPairCancellationNetworkSession Session { get; } =
            new(candidate);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default)
        {
            Session.DisposeViewModel = DisposeViewModel;
            return ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
        }
    }

    private sealed class CapturedPairContextNetworkFactory(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkFactory
    {
        public CapturedPairContextNetworkSession Session { get; } = new(candidate);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class CapturedPairContextNetworkSession(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource runCapturedDispose = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource CapturedDisposeCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ValueTask>? DisposeViewModel { get; set; }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DisposeReleased => disposeRelease.Task.IsCompletedSuccessfully;

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() =>
            [candidate];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate selected,
            CancellationToken cancellationToken = default)
        {
            _ = Task.Run(
                async () =>
                {
                    await runCapturedDispose.Task.ConfigureAwait(false);
                    await DisposeViewModel!().ConfigureAwait(false);
                    CapturedDisposeCompleted.TrySetResult();
                },
                CancellationToken.None);
            return ValueTask.FromResult(new PairingCeremonyResult(
                false,
                PairingFailure.Rejected,
                null,
                null,
                null));
        }

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await disposeRelease.Task.ConfigureAwait(false);
        }

        public void ReleaseDispose() => disposeRelease.TrySetResult();

        public void RunCapturedDispose() => runCapturedDispose.TrySetResult();
    }

    private sealed class ReentrantPairCancellationNetworkSession(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource<PairingCeremonyResult> pairingCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationCallbackCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ValueTask>? DisposeViewModel { get; set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public TaskCompletionSource PairingStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ReentrantDisposeCompletedSynchronously { get; private set; }

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() =>
            [candidate];

        public async ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate selected,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(candidate, selected);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    ValueTask reentrantDispose = DisposeViewModel!();
                    ReentrantDisposeCompletedSynchronously =
                        reentrantDispose.IsCompletedSuccessfully;
                    pairingCompletion.TrySetCanceled(cancellationToken);
                    CancellationCallbackCompleted.TrySetResult();
                });
            PairingStarted.TrySetResult();
            return await pairingCompletion.Task.ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PairCancellationSourceLifetimeNetworkFactory(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkFactory
    {
        public PairCancellationSourceLifetimeNetworkSession Session { get; } =
            new(candidate);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class PairCancellationSourceLifetimeNetworkSession(
        UnverifiedPairingCandidate candidate) : IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource cancellationCallbackRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<PairingCeremonyResult> pairingCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CallbackObservedDisposedSource { get; private set; }

        public TaskCompletionSource CancellationCallbackCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationCallbackEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public TaskCompletionSource PairingStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() =>
            [candidate];

        public async ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate selected,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(candidate, selected);
            _ = cancellationToken.Register(() =>
            {
                CancellationCallbackEntered.TrySetResult();
                pairingCompletion.TrySetCanceled(cancellationToken);
                cancellationCallbackRelease.Task.GetAwaiter().GetResult();
                try
                {
                    _ = cancellationToken.WaitHandle;
                }
                catch (ObjectDisposedException)
                {
                    CallbackObservedDisposedSource = true;
                }

                CancellationCallbackCompleted.TrySetResult();
            });
            PairingStarted.TrySetResult();
            return await pairingCompletion.Task.ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void ReleaseCancellationCallback() =>
            cancellationCallbackRelease.TrySetResult();
    }

    private sealed class BlockingFailingDisposeNetworkFactory(string canary) :
        IDesktopLocalPairingNetworkFactory
    {
        public BlockingFailingDisposeNetworkSession Session { get; } = new(canary);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class BlockingFailingDisposeNetworkSession(string canary) :
        IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource disposeRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeEntered.TrySetResult();
            await disposeRelease.Task.ConfigureAwait(false);
            throw new InvalidOperationException(canary);
        }

        public void ReleaseDispose() => disposeRelease.TrySetResult();
    }

    private sealed class HostilePairCancellationNetworkFactory(
        UnverifiedPairingCandidate candidate,
        string canary,
        string disposeCanary) : IDesktopLocalPairingNetworkFactory
    {
        public HostilePairCancellationNetworkSession Session { get; } =
            new(candidate, canary, disposeCanary);

        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(Session);
    }

    private sealed class HostilePairCancellationNetworkSession(
        UnverifiedPairingCandidate candidate,
        string canary,
        string disposeCanary) : IDesktopLocalPairingNetworkSession
    {
        private readonly TaskCompletionSource cancellationCallbackRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<PairingCeremonyResult> pairingCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationCallbackEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public bool IsDisposed { get; private set; }

        public TaskCompletionSource PairingStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() =>
            [candidate];

        public async ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate selected,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(candidate, selected);
            _ = cancellationToken.Register(() =>
            {
                CancellationCallbackEntered.TrySetResult();
                cancellationCallbackRelease.Task.GetAwaiter().GetResult();
                pairingCompletion.TrySetCanceled(cancellationToken);
                throw new InvalidOperationException(canary);
            });
            PairingStarted.TrySetResult();
            return await pairingCompletion.Task.ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            Disposed.TrySetResult();
            return ValueTask.FromException(
                new InvalidOperationException(disposeCanary));
        }

        public void ReleaseCancellationCallback() =>
            cancellationCallbackRelease.TrySetResult();
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
        PairingCeremonyResult? pairingResult,
        ImmutableArray<DesktopTrustedPeerConnectionSnapshot> connections) :
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

        public ImmutableArray<DesktopTrustedPeerConnectionSnapshot>
            GetTrustedPeerConnections() => connections;

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
