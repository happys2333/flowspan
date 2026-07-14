using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopTrustAuthorityTests
{
    [Fact]
    public async Task InitializeAsyncReadsProtectedTrustedPeerSnapshot()
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
        await using var authority = new DesktopTrustAuthority(trustStore);

        DesktopTrustSnapshot snapshot = await authority.InitializeAsync();

        Assert.Equal(SecretStoreProtection.DegradedTestOnly, snapshot.Protection);
        Assert.Equal(
            [earlier.DeviceId, later.DeviceId],
            snapshot.TrustedPeers.Select(static peer => peer.DeviceId));
    }

    [Fact]
    public async Task UpdateCapabilitiesAsyncReturnsAuthoritativeSnapshot()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None));
        await using var authority = new DesktopTrustAuthority(trustStore);
        CapabilityGrant allCapabilities = CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.ActivityReceive,
            Capability.ActivityReplace,
            Capability.MirrorView,
            Capability.MirrorDrive,
            Capability.FileReceive,
            Capability.SceneApply);

        DesktopTrustMutationOutcome outcome =
            await authority.UpdateCapabilitiesAsync(
                peer.DeviceId,
                peer.PublicIdentity.Fingerprint,
                allCapabilities);

        Assert.Equal(DesktopTrustMutationStatus.Applied, outcome.Status);
        TrustedPeerSnapshot updated = Assert.Single(outcome.Snapshot.TrustedPeers);
        Assert.Equal(
            allCapabilities.Capabilities.Order(),
            updated.GrantedCapabilities.Capabilities.Order());
    }

    [Fact]
    public async Task CapabilityDowngradeReportsAppliedWhenSessionStopFails()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive)));
        await using var authority = new DesktopTrustAuthority(trustStore);
        await using TrustSessionRegistration registration =
            await authority.TryRegisterSessionAsync(
                peer.DeviceId,
                CapabilityGrant.Of(Capability.MirrorDrive),
                new FailingSession())
            ?? throw new InvalidOperationException("Expected an active session.");

        DesktopTrustMutationOutcome outcome =
            await authority.UpdateCapabilitiesAsync(
                peer.DeviceId,
                peer.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView));

        Assert.Equal(
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure,
            outcome.Status);
        TrustedPeerSnapshot updated = Assert.Single(outcome.Snapshot.TrustedPeers);
        Assert.True(updated.GrantedCapabilities.Allows(Capability.MirrorView));
        Assert.False(updated.GrantedCapabilities.Allows(Capability.MirrorDrive));
    }

    [Fact]
    public async Task RevokeAsyncReportsAppliedWhenSessionStopFails()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var authority = new DesktopTrustAuthority(trustStore);
        await using TrustSessionRegistration registration =
            await authority.TryRegisterSessionAsync(
                peer.DeviceId,
                CapabilityGrant.Of(Capability.MirrorView),
                new FailingSession())
            ?? throw new InvalidOperationException("Expected an active session.");

        DesktopTrustMutationOutcome outcome = await authority.RevokeAsync(
            peer.DeviceId,
            peer.PublicIdentity.Fingerprint);

        Assert.Equal(
            DesktopTrustMutationStatus.AppliedWithSessionStopFailure,
            outcome.Status);
        Assert.Empty(outcome.Snapshot.TrustedPeers);
    }

    [Fact]
    public async Task PersistentAuthorityLoadsCommittedTrustOnDesktopStartup()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var payloadStore = new MemoryTrustPayloadStore();
        using (PersistentTrustStore seed =
            await PersistentTrustStore.OpenAsync(payloadStore))
        {
            await seed.RegisterAsync(new TrustRecord(
                peer.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.Of(Capability.ActivityReceive)));
        }

        await using var authority = new PersistentDesktopTrustAuthority(payloadStore);
        DesktopTrustSnapshot snapshot = await authority.InitializeAsync();

        Assert.Equal(
            SecretStoreProtection.OperatingSystemProtected,
            snapshot.Protection);
        TrustedPeerSnapshot trustedPeer = Assert.Single(snapshot.TrustedPeers);
        Assert.Equal(peer.DeviceId, trustedPeer.DeviceId);
        Assert.True(
            trustedPeer.GrantedCapabilities.Allows(Capability.ActivityReceive));
    }

    [Fact]
    public async Task AggregatePayloadFailureIsNotMisreportedAsSessionStopFailure()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var payloadStore = new AggregateFailingTrustPayloadStore();
        CapabilityGrant capabilities =
            CapabilityGrant.Of(Capability.ActivityReceive);
        using (PersistentTrustStore seed =
            await PersistentTrustStore.OpenAsync(payloadStore))
        {
            await seed.RegisterAsync(new TrustRecord(
                peer.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                capabilities));
        }

        await using var authority = new PersistentDesktopTrustAuthority(payloadStore);
        await authority.InitializeAsync();
        payloadStore.FailSaves = true;

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            async () => await authority.UpdateCapabilitiesAsync(
                peer.DeviceId,
                peer.PublicIdentity.Fingerprint,
                capabilities));

        Assert.Same(payloadStore.Failure, failure);
    }

    [Fact]
    public async Task DisposeWaitsForAdmittedPersistentMutation()
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

        var authority = new PersistentDesktopTrustAuthority(payloadStore);
        await authority.InitializeAsync();
        payloadStore.BlockSaves = true;
        Task<DesktopTrustMutationOutcome> mutation = authority
            .UpdateCapabilitiesAsync(
                peer.DeviceId,
                peer.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.ActivityOffer))
            .AsTask();
        await payloadStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposing = authority.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        payloadStore.AllowSave.TrySetResult();

        DesktopTrustMutationOutcome outcome = await mutation;
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DesktopTrustMutationStatus.Applied, outcome.Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await authority.InitializeAsync());
    }

    private sealed class FailingSession : IRevocablePeerSession
    {
        public ValueTask StopAsync(TrustSessionStopReason reason) =>
            ValueTask.FromException(new IOException("Injected stop failure."));
    }

    private sealed class AggregateFailingTrustPayloadStore : ITrustPayloadStore
    {
        private byte[]? payload;

        public bool FailSaves { get; set; }

        public AggregateException Failure { get; } = new(
            "Injected payload failure.",
            new IOException("The protected payload was not written."));

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
                return ValueTask.FromException(Failure);
            }

            payload = newPayload.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTrustPayloadStore : ITrustPayloadStore
    {
        private byte[]? payload;

        public TaskCompletionSource AllowSave { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockSaves { get; set; }

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

    private sealed class MemoryTrustPayloadStore : ITrustPayloadStore
    {
        private byte[]? payload;

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
            payload = newPayload.ToArray();
            return ValueTask.CompletedTask;
        }
    }
}
