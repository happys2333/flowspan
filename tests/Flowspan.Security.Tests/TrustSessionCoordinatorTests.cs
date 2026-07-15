using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class TrustSessionCoordinatorTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RevokingPeerStopsActiveSessionBeforeReturning()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var session = new RecordingRevocableSession();
        await using TrustSessionRegistration registration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                session)
            ?? throw new InvalidOperationException("Expected an authorized session.");

        bool revoked = await coordinator.RevokePeerAsync(PeerId);

        Assert.True(revoked);
        Assert.False(trustStore.TryGet(PeerId, out _));
        Assert.Equal(1, session.StopCount);
        Assert.Equal(TrustSessionStopReason.PeerRevoked, session.LastReason);
    }

    [Fact]
    public async Task CapabilityDowngradeStopsOnlySessionsThatNeedRemovedCapability()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var viewSession = new RecordingRevocableSession();
        var driveSession = new RecordingRevocableSession();
        await using TrustSessionRegistration viewRegistration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                viewSession)
            ?? throw new InvalidOperationException("Expected a view session.");
        await using TrustSessionRegistration driveRegistration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive),
                driveSession)
            ?? throw new InvalidOperationException("Expected a drive session.");

        bool updated = await coordinator.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView));
        TrustSessionRegistration? rejected = await coordinator.TryRegisterAsync(
            PeerId,
            CapabilityGrant.Of(Capability.MirrorDrive),
            new RecordingRevocableSession());

        Assert.True(updated);
        Assert.Equal(0, viewSession.StopCount);
        Assert.Equal(1, driveSession.StopCount);
        Assert.Equal(
            TrustSessionStopReason.CapabilityRevoked,
            driveSession.LastReason);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task AnyCapabilitySessionAcceptsPeerWithOfferGrant()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);

        await using TrustSessionRegistration? registration =
            await coordinator.TryRegisterAnyAsync(
                PeerId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive),
                new RecordingRevocableSession());

        Assert.NotNull(registration);
    }

    [Fact]
    public async Task AnyCapabilitySessionAcceptsPeerWithReceiveGrant()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);

        await using TrustSessionRegistration? registration =
            await coordinator.TryRegisterAnyAsync(
                PeerId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive),
                new RecordingRevocableSession());

        Assert.NotNull(registration);
    }

    [Fact]
    public async Task AnyCapabilitySessionRejectsPeerWithNeitherGrant()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);

        TrustSessionRegistration? registration =
            await coordinator.TryRegisterAnyAsync(
                PeerId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive),
                new RecordingRevocableSession());

        Assert.Null(registration);
    }

    [Fact]
    public async Task AnyCapabilitySessionStopsOnlyAfterFinalAlternativeIsRemoved()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(
                Capability.ActivityOffer,
                Capability.ActivityReceive)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var session = new RecordingRevocableSession();
        await using TrustSessionRegistration registration =
            await coordinator.TryRegisterAnyAsync(
                PeerId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive),
                session)
            ?? throw new InvalidOperationException("Expected an authorized session.");

        Assert.True(await coordinator.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        Assert.Equal(0, session.StopCount);

        Assert.True(await coordinator.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView)));

        Assert.Equal(1, session.StopCount);
        Assert.Equal(
            TrustSessionStopReason.CapabilityRevoked,
            session.LastReason);
    }

    [Fact]
    public async Task OneStopFailureDoesNotLeaveAnotherRevokedSessionRunning()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var failingSession = new RecordingRevocableSession(throwOnStop: true);
        var healthySession = new RecordingRevocableSession();
        await using TrustSessionRegistration first =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                failingSession)
            ?? throw new InvalidOperationException("Expected the first session.");
        await using TrustSessionRegistration second =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                healthySession)
            ?? throw new InvalidOperationException("Expected the second session.");

        await Assert.ThrowsAsync<TrustSessionStopException>(async () =>
            await coordinator.RevokePeerAsync(PeerId));

        Assert.False(trustStore.TryGet(PeerId, out _));
        Assert.Equal(1, failingSession.StopCount);
        Assert.Equal(1, healthySession.StopCount);
    }

    [Fact]
    public async Task NewSessionIsRejectedWhileRevokedSessionIsStillStopping()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var activeSession = new BlockingRevocableSession();
        await using TrustSessionRegistration registration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                activeSession)
            ?? throw new InvalidOperationException("Expected an active session.");

        Task<bool> revocation = coordinator.RevokePeerAsync(PeerId).AsTask();
        await activeSession.StopStarted;
        TrustSessionRegistration? rejected = await coordinator.TryRegisterAsync(
            PeerId,
            CapabilityGrant.Of(Capability.MirrorView),
            new RecordingRevocableSession());
        activeSession.AllowStop();

        Assert.Null(rejected);
        Assert.True(await revocation);
    }

    [Fact]
    public async Task PersistentRevocationCommitsBeforeSessionStopBegins()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var payloadStore = new TestTrustPayloadStore();
        using PersistentTrustStore trustStore =
            await PersistentTrustStore.OpenAsync(payloadStore);
        await trustStore.RegisterAsync(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var session = new PersistenceObservingSession(payloadStore);
        await using TrustSessionRegistration registration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                session)
            ?? throw new InvalidOperationException("Expected an authorized session.");

        bool revoked = await coordinator.RevokePeerAsync(PeerId);

        Assert.True(revoked);
        Assert.True(session.SawPersistedRevocation);
    }

    [Fact]
    public async Task LocalShutdownStopsSessionsAndMakesRegistrationDisposalSafe()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        var coordinator = new TrustSessionCoordinator(trustStore);
        var session = new RecordingRevocableSession();
        TrustSessionRegistration registration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                session)
            ?? throw new InvalidOperationException("Expected an authorized session.");

        await coordinator.DisposeAsync();
        await registration.DisposeAsync();

        Assert.Equal(1, session.StopCount);
        Assert.Equal(TrustSessionStopReason.LocalShutdown, session.LastReason);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                new RecordingRevocableSession()));
    }

    [Fact]
    public async Task PairingRegistrationIsSerializedBeforeConcurrentRevocation()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new BlockingRegistrationTrustStore();
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var record = new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView));

        Task<TrustRegistrationResult> registration =
            coordinator.RegisterAsync(record).AsTask();
        await trustStore.RegistrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task<bool> revocation = coordinator.RevokePeerAsync(PeerId).AsTask();

        Assert.False(revocation.IsCompleted);
        trustStore.AllowRegistration.TrySetResult();

        Assert.Equal(TrustRegistrationResult.Added, await registration);
        Assert.True(await revocation);
        Assert.False(coordinator.TryGet(PeerId, out _));
    }

    [Fact]
    public async Task StaleSnapshotCannotRevokeReplacementIdentity()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(PeerId, "Original desk");
        using DeviceIdentity replacement = DeviceIdentity.Generate(
            PeerId,
            "Replacement desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            original.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        TrustedPeerSnapshot stale = Assert.Single(coordinator.GetTrustedPeers());
        Assert.True(await coordinator.RevokePeerAsync(PeerId));
        Assert.Equal(
            TrustRegistrationResult.Added,
            await coordinator.RegisterAsync(new TrustRecord(
                replacement.PublicIdentity,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                CapabilityGrant.Of(Capability.MirrorView))));

        TrustMutationResult result = await coordinator.RevokePeerAsync(
            PeerId,
            stale.Fingerprint);

        Assert.Equal(TrustMutationResult.IdentityChanged, result);
        Assert.True(coordinator.TryGet(PeerId, out TrustRecord? retained));
        Assert.Equal(
            replacement.PublicIdentity.Fingerprint,
            retained.PeerIdentity.Fingerprint);
    }

    [Fact]
    public async Task StaleSnapshotCannotUpdateReplacementIdentityCapabilities()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(PeerId, "Original desk");
        using DeviceIdentity replacement = DeviceIdentity.Generate(
            PeerId,
            "Replacement desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            original.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        TrustedPeerSnapshot stale = Assert.Single(coordinator.GetTrustedPeers());
        Assert.True(await coordinator.RevokePeerAsync(PeerId));
        Assert.Equal(
            TrustRegistrationResult.Added,
            await coordinator.RegisterAsync(new TrustRecord(
                replacement.PublicIdentity,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                CapabilityGrant.Of(Capability.MirrorView))));

        TrustMutationResult result = await coordinator.UpdateCapabilitiesAsync(
            PeerId,
            stale.Fingerprint,
            CapabilityGrant.Of(Capability.ActivityOffer));

        Assert.Equal(TrustMutationResult.IdentityChanged, result);
        Assert.True(coordinator.TryGet(PeerId, out TrustRecord? retained));
        Assert.True(retained.GrantedCapabilities.Allows(Capability.MirrorView));
        Assert.False(retained.GrantedCapabilities.Allows(Capability.ActivityOffer));
    }

    private sealed class BlockingRevocableSession : IRevocablePeerSession
    {
        private readonly TaskCompletionSource allowStop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource stopStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StopStarted => stopStarted.Task;

        public void AllowStop() => allowStop.SetResult();

        public async ValueTask StopAsync(TrustSessionStopReason reason)
        {
            stopStarted.SetResult();
            await allowStop.Task;
        }
    }

    private sealed class RecordingRevocableSession : IRevocablePeerSession
    {
        private readonly bool throwOnStop;

        public RecordingRevocableSession(bool throwOnStop = false) =>
            this.throwOnStop = throwOnStop;

        public TrustSessionStopReason? LastReason { get; private set; }

        public int StopCount { get; private set; }

        public ValueTask StopAsync(TrustSessionStopReason reason)
        {
            StopCount++;
            LastReason = reason;
            if (throwOnStop)
            {
                throw new IOException("Injected session stop failure.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class PersistenceObservingSession(
        TestTrustPayloadStore payloadStore) : IRevocablePeerSession
    {
        public bool SawPersistedRevocation { get; private set; }

        public ValueTask StopAsync(TrustSessionStopReason reason)
        {
            byte[] payload = payloadStore.Snapshot()
                ?? throw new InvalidOperationException("Expected a persisted snapshot.");
            SawPersistedRevocation = TrustStorePayloadCodec.Decode(payload).Count == 0;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRegistrationTrustStore : ITrustStore
    {
        private readonly InMemoryTrustStore inner = new();

        public TaskCompletionSource AllowRegistration { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RegistrationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SecretStoreProtection Protection => inner.Protection;

        public System.Collections.Immutable.ImmutableArray<TrustedPeerSnapshot>
            GetSnapshot() => inner.GetSnapshot();

        public bool Allows(DeviceId peerDeviceId, Capability capability) =>
            inner.Allows(peerDeviceId, capability);

        public async ValueTask<TrustRegistrationResult> RegisterAsync(
            TrustRecord trustRecord,
            CancellationToken cancellationToken = default)
        {
            RegistrationStarted.TrySetResult();
            await AllowRegistration.Task.WaitAsync(cancellationToken);
            return await inner.RegisterAsync(trustRecord, cancellationToken);
        }

        public ValueTask<TrustMutationResult> RevokeAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CancellationToken cancellationToken = default) =>
            inner.RevokeAsync(
                peerDeviceId,
                expectedFingerprint,
                cancellationToken);

        public bool TryGet(
            DeviceId peerDeviceId,
            [NotNullWhen(true)] out TrustRecord? trustRecord) =>
            inner.TryGet(peerDeviceId, out trustRecord);

        public ValueTask<TrustMutationResult> UpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default) =>
            inner.UpdateCapabilitiesAsync(
                peerDeviceId,
                expectedFingerprint,
                capabilities,
                cancellationToken);
    }
}
