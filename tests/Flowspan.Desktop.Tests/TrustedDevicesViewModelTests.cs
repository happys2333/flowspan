using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class TrustedDevicesViewModelTests
{
    [Fact]
    public async Task InitializeAsyncShowsCanonicalProtectedDeviceList()
    {
        using DeviceIdentity later = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Later desk");
        using DeviceIdentity earlier = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Earlier desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            later.PublicIdentity,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CapabilityGrant.Of(Capability.ActivityReceive)));
        trustStore.Register(new TrustRecord(
            earlier.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var viewModel = new TrustedDevicesViewModel(
            new DesktopTrustAuthority(trustStore));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsTrustAvailable);
        Assert.False(viewModel.IsEmpty);
        Assert.Equal("2 PAIRED DEVICES", viewModel.Status);
        Assert.Equal("TEST MODE — trust is not persisted", viewModel.Protection);
        Assert.Equal(
            [earlier.DeviceId.ToString(), later.DeviceId.ToString()],
            viewModel.Devices.Select(static peer => peer.DeviceId));
        Assert.Same(viewModel.Devices[0], viewModel.SelectedDevice);
        Assert.True(viewModel.GrantActivityOffer);
        Assert.False(viewModel.GrantActivityReceive);
    }

    [Fact]
    public async Task SaveCapabilitiesAsyncRoundTripsCompleteGrant()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None));
        int connectionReconciles = 0;
        await using var viewModel = new TrustedDevicesViewModel(
            new DesktopTrustAuthority(trustStore),
            _ =>
            {
                connectionReconciles++;
                return Task.CompletedTask;
            });
        await viewModel.InitializeAsync();

        viewModel.GrantActivityOffer = true;
        viewModel.GrantActivityReceive = true;
        viewModel.GrantActivityReplace = true;
        viewModel.GrantActivitySwap = true;
        viewModel.GrantMirrorView = true;
        viewModel.GrantMirrorDrive = true;
        viewModel.GrantFileReceive = true;
        viewModel.GrantSceneApply = true;
        Assert.True(viewModel.HasUnsavedChanges);

        await viewModel.SaveCapabilitiesAsync();

        Assert.False(viewModel.HasUnsavedChanges);
        Assert.Equal("CAPABILITIES SAVED", viewModel.MutationStatus);
        Assert.True(trustStore.TryGet(peer.DeviceId, out TrustRecord? updated));
        Assert.Equal(
            Enum.GetValues<Capability>().Order(),
            updated.GrantedCapabilities.Capabilities.Order());
        Assert.Contains(
            "activity.swap",
            viewModel.SelectedDevice?.CapabilitySummary,
            StringComparison.Ordinal);
        Assert.Equal(1, connectionReconciles);
    }

    [Fact]
    public async Task StaleCapabilityDraftRefreshesReplacementIdentityWithoutOverwrite()
    {
        DeviceId peerId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity original = DeviceIdentity.Generate(peerId, "Original desk");
        using DeviceIdentity replacement = DeviceIdentity.Generate(
            peerId,
            "Replacement desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            original.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var viewModel = new TrustedDevicesViewModel(
            new DesktopTrustAuthority(trustStore));
        await viewModel.InitializeAsync();
        viewModel.GrantActivityOffer = true;
        Assert.True(trustStore.Revoke(peerId));
        Assert.Equal(
            TrustRegistrationResult.Added,
            trustStore.Register(new TrustRecord(
                replacement.PublicIdentity,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                CapabilityGrant.Of(Capability.MirrorView))));

        await viewModel.SaveCapabilitiesAsync();

        Assert.Equal(
            "IDENTITY CHANGED — REVIEW REQUIRED",
            viewModel.MutationStatus);
        Assert.Equal("Replacement desk", viewModel.SelectedDevice?.DisplayName);
        Assert.False(viewModel.GrantActivityOffer);
        Assert.True(viewModel.GrantMirrorView);
        Assert.True(trustStore.Allows(peerId, Capability.MirrorView));
        Assert.False(trustStore.Allows(peerId, Capability.ActivityOffer));
    }

    [Fact]
    public async Task RevokeRequiresReviewAndCancelDoesNotMutateTrust()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var viewModel = new TrustedDevicesViewModel(
            new DesktopTrustAuthority(trustStore));
        await viewModel.InitializeAsync();

        viewModel.BeginRevoke();
        Assert.True(viewModel.IsRevokeConfirmationVisible);
        Assert.Contains("Peer desk", viewModel.RevokeConfirmation);
        Assert.True(trustStore.TryGet(peer.DeviceId, out _));
        viewModel.CancelRevoke();
        Assert.False(viewModel.IsRevokeConfirmationVisible);
        Assert.True(trustStore.TryGet(peer.DeviceId, out _));

        viewModel.BeginRevoke();
        await viewModel.ConfirmRevokeAsync();

        Assert.False(viewModel.IsRevokeConfirmationVisible);
        Assert.True(viewModel.IsEmpty);
        Assert.Equal("DEVICE REVOKED", viewModel.MutationStatus);
        Assert.False(trustStore.TryGet(peer.DeviceId, out _));
    }

    [Fact]
    public async Task InitializeAsyncFailsClosedWithoutLeakingStoreError()
    {
        const string canary = "CANARY_TRUST_STORE_DETAIL";
        await using var viewModel = new TrustedDevicesViewModel(
            new PersistentDesktopTrustAuthority(
                new FailingTrustPayloadStore(canary)));

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsTrustAvailable);
        Assert.True(viewModel.IsEmpty);
        Assert.Equal("TRUST STORE UNAVAILABLE", viewModel.Status);
        Assert.Empty(viewModel.Devices);
        Assert.DoesNotContain(canary, viewModel.StatusDescription);
        Assert.DoesNotContain(canary, viewModel.RecoveryAction);
    }

    [Fact]
    public async Task FailedCapabilitySaveRefreshesCommittedGrantWithoutLeak()
    {
        const string canary = "CANARY_TRUST_SAVE_DETAIL";
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var payloadStore = new ToggleTrustPayloadStore(canary);
        using (PersistentTrustStore seed =
            await PersistentTrustStore.OpenAsync(payloadStore))
        {
            await seed.RegisterAsync(new TrustRecord(
                peer.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.None));
        }

        await using var viewModel = new TrustedDevicesViewModel(
            new PersistentDesktopTrustAuthority(payloadStore));
        await viewModel.InitializeAsync();
        viewModel.GrantActivityOffer = true;
        payloadStore.FailSaves = true;

        await viewModel.SaveCapabilitiesAsync();

        Assert.Equal("TRUST UPDATE FAILED", viewModel.MutationStatus);
        Assert.DoesNotContain(canary, viewModel.MutationDescription);
        Assert.False(viewModel.GrantActivityOffer);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task FailedRevokeRefreshesCommittedTrustWithoutLeak()
    {
        const string canary = "CANARY_TRUST_REVOKE_DETAIL";
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var payloadStore = new ToggleTrustPayloadStore(canary);
        using (PersistentTrustStore seed =
            await PersistentTrustStore.OpenAsync(payloadStore))
        {
            await seed.RegisterAsync(new TrustRecord(
                peer.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.Of(Capability.ActivityReceive)));
        }

        await using var viewModel = new TrustedDevicesViewModel(
            new PersistentDesktopTrustAuthority(payloadStore));
        await viewModel.InitializeAsync();
        viewModel.BeginRevoke();
        payloadStore.FailSaves = true;

        await viewModel.ConfirmRevokeAsync();

        Assert.Equal("TRUST REVOKE FAILED", viewModel.MutationStatus);
        Assert.DoesNotContain(canary, viewModel.MutationDescription);
        Assert.False(viewModel.IsRevokeConfirmationVisible);
        Assert.Equal("Peer desk", viewModel.SelectedDevice?.DisplayName);
    }

    [Fact]
    public async Task PendingSaveDisablesCompetingTrustMutations()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var payloadStore = new BlockingTrustPayloadStore();
        using (PersistentTrustStore seed =
            await PersistentTrustStore.OpenAsync(payloadStore))
        {
            await seed.RegisterAsync(new TrustRecord(
                peer.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.None));
        }

        await using var viewModel = new TrustedDevicesViewModel(
            new PersistentDesktopTrustAuthority(payloadStore));
        await viewModel.InitializeAsync();
        viewModel.GrantActivityOffer = true;
        payloadStore.BlockSaves = true;

        Task saving = viewModel.SaveCapabilitiesAsync();
        await payloadStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsMutationInProgress);
        Assert.False(viewModel.SaveCapabilitiesCommand.CanExecute(null));
        Assert.False(viewModel.BeginRevokeCommand.CanExecute(null));
        viewModel.BeginRevoke();
        Assert.False(viewModel.IsRevokeConfirmationVisible);

        payloadStore.AllowSave.TrySetResult();
        await saving;
        Assert.False(viewModel.IsMutationInProgress);
    }

    [Fact]
    public async Task DisposeCancelsPendingProtectedStoreMutation()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var payloadStore = new BlockingTrustPayloadStore();
        using (PersistentTrustStore seed =
            await PersistentTrustStore.OpenAsync(payloadStore))
        {
            await seed.RegisterAsync(new TrustRecord(
                peer.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.None));
        }

        var viewModel = new TrustedDevicesViewModel(
            new PersistentDesktopTrustAuthority(payloadStore));
        await viewModel.InitializeAsync();
        viewModel.GrantActivityOffer = true;
        payloadStore.BlockSaves = true;
        Task saving = viewModel.SaveCapabilitiesAsync();
        await payloadStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();
        try
        {
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));
            await saving.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            payloadStore.AllowSave.TrySetResult();
            await Task.WhenAll(disposing, saving);
        }

        using PersistentTrustStore restarted =
            await PersistentTrustStore.OpenAsync(payloadStore);
        Assert.False(restarted.Allows(peer.DeviceId, Capability.ActivityOffer));
    }

    [Fact]
    public async Task DisposeCancelsPendingProtectedStoreInitialization()
    {
        var payloadStore = new BlockingLoadTrustPayloadStore();
        var viewModel = new TrustedDevicesViewModel(
            new PersistentDesktopTrustAuthority(payloadStore));
        Task initializing = viewModel.InitializeAsync();
        await payloadStore.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = viewModel.DisposeAsync().AsTask();

        await Task.WhenAll(initializing, disposing)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TrustExportUsesRedactedLocalDataService()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        var localData = new FakeDesktopLocalDataService();
        await using var viewModel = new TrustedDevicesViewModel(
            new DesktopTrustAuthority(trustStore),
            localDataService: localData);
        await viewModel.InitializeAsync();

        await viewModel.ExportTrustAsync();

        Assert.Equal(1, localData.TrustExportCount);
        Assert.Equal("/exports/trust.json", viewModel.TrustExportPath);
        Assert.Contains("redacted", viewModel.TrustExportPreview);
        Assert.Equal("REDACTED TRUST EXPORT WRITTEN", viewModel.MutationStatus);
    }

    [Fact]
    public async Task TrustExportFailureUsesFixedNonEchoingText()
    {
        const string canary = "TRUST-EXPORT-EXCEPTION-CANARY";
        var localData = new FakeDesktopLocalDataService
        {
            Failure = new IOException(canary),
        };
        await using var viewModel = new TrustedDevicesViewModel(
            new DesktopTrustAuthority(new InMemoryTrustStore()),
            localDataService: localData);
        await viewModel.InitializeAsync();

        await viewModel.ExportTrustAsync();

        Assert.Equal("TRUST EXPORT FAILED", viewModel.MutationStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.MutationDescription,
            StringComparison.Ordinal);
    }

    private sealed class FailingTrustPayloadStore(string canary)
        : ITrustPayloadStore
    {
        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(new IOException(canary));

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException(canary));
    }

    private sealed class ToggleTrustPayloadStore(string canary)
        : ITrustPayloadStore
    {
        private byte[]? payload;

        public bool FailSaves { get; set; }

        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> newPayload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSaves)
            {
                return ValueTask.FromException(new IOException(canary));
            }

            payload = newPayload.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTrustPayloadStore : ITrustPayloadStore
    {
        private byte[]? payload;

        public bool BlockSaves { get; set; }

        public TaskCompletionSource AllowSave { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SaveStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public async ValueTask SaveAsync(
            ReadOnlyMemory<byte> newPayload,
            CancellationToken cancellationToken = default)
        {
            if (BlockSaves)
            {
                SaveStarted.TrySetResult();
                await AllowSave.Task.WaitAsync(cancellationToken);
            }

            payload = newPayload.ToArray();
        }
    }

    private sealed class BlockingLoadTrustPayloadStore : ITrustPayloadStore
    {
        public TaskCompletionSource LoadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public async ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
