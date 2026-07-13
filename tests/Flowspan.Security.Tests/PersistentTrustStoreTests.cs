using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class PersistentTrustStoreTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RegisterUpdateAndRevokeSurviveRestart()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var payloadStore = new TestTrustPayloadStore();
        using PersistentTrustStore store =
            await PersistentTrustStore.OpenAsync(payloadStore);

        TrustRegistrationResult registered = await store.RegisterAsync(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        using PersistentTrustStore afterRegister =
            await PersistentTrustStore.OpenAsync(payloadStore);
        bool updated = await afterRegister.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive));
        using PersistentTrustStore afterUpdate =
            await PersistentTrustStore.OpenAsync(payloadStore);
        bool revoked = await afterUpdate.RevokeAsync(PeerId);
        using PersistentTrustStore afterRevoke =
            await PersistentTrustStore.OpenAsync(payloadStore);

        Assert.Equal(TrustRegistrationResult.Added, registered);
        Assert.True(updated);
        Assert.True(revoked);
        Assert.False(afterRevoke.TryGet(PeerId, out _));
        Assert.Equal(3, payloadStore.SaveCount);
        Assert.Equal(
            SecretStoreProtection.OperatingSystemProtected,
            afterRevoke.Protection);
    }

    [Fact]
    public async Task FailedSaveKeepsPreviouslyCommittedAuthority()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var payloadStore = new TestTrustPayloadStore();
        using PersistentTrustStore store =
            await PersistentTrustStore.OpenAsync(payloadStore);
        await store.RegisterAsync(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        payloadStore.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(async () =>
            await store.TryUpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorDrive)));
        using PersistentTrustStore restarted =
            await PersistentTrustStore.OpenAsync(payloadStore);

        Assert.True(store.Allows(PeerId, Capability.MirrorView));
        Assert.False(store.Allows(PeerId, Capability.MirrorDrive));
        Assert.True(restarted.Allows(PeerId, Capability.MirrorView));
        Assert.False(restarted.Allows(PeerId, Capability.MirrorDrive));
    }

    [Fact]
    public async Task CorruptPayloadBlocksOpenWithoutReplacement()
    {
        var payloadStore = new TestTrustPayloadStore();
        payloadStore.SetPayload("ordinary plaintext trust"u8.ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentTrustStore.OpenAsync(payloadStore));

        Assert.Equal(0, payloadStore.SaveCount);
        Assert.Equal("ordinary plaintext trust"u8.ToArray(), payloadStore.Snapshot());
    }

    [Fact]
    public async Task IdentityChangeIsRejectedWithoutReplacingCommittedPayload()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(PeerId, "Desk");
        using DeviceIdentity substituted = DeviceIdentity.Generate(PeerId, "Desk");
        var payloadStore = new TestTrustPayloadStore();
        using PersistentTrustStore store =
            await PersistentTrustStore.OpenAsync(payloadStore);
        await store.RegisterAsync(CreateRecord(original));

        TrustRegistrationResult result = await store.RegisterAsync(
            CreateRecord(substituted));
        using PersistentTrustStore restarted =
            await PersistentTrustStore.OpenAsync(payloadStore);

        Assert.Equal(TrustRegistrationResult.IdentityChanged, result);
        Assert.Equal(1, payloadStore.SaveCount);
        Assert.True(restarted.TryGet(PeerId, out TrustRecord? retained));
        Assert.True(retained.PeerIdentity.HasSameKey(original.PublicIdentity));
    }

    [Fact]
    public async Task CancelledMutationDoesNotPublishOrSave()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var payloadStore = new TestTrustPayloadStore();
        using PersistentTrustStore store =
            await PersistentTrustStore.OpenAsync(payloadStore);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.RegisterAsync(CreateRecord(identity), cancellation.Token));

        Assert.False(store.TryGet(PeerId, out _));
        Assert.Null(payloadStore.Snapshot());
        Assert.Equal(0, payloadStore.SaveCount);
    }

    [Fact]
    public async Task ConcurrentRegistrationsSerializeWithoutLostUpdate()
    {
        var payloadStore = new TestTrustPayloadStore();
        using PersistentTrustStore store =
            await PersistentTrustStore.OpenAsync(payloadStore);
        using DeviceIdentity first = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "First");
        using DeviceIdentity second = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Second");

        await Task.WhenAll(
            store.RegisterAsync(CreateRecord(first)).AsTask(),
            store.RegisterAsync(CreateRecord(second)).AsTask());
        using PersistentTrustStore restarted =
            await PersistentTrustStore.OpenAsync(payloadStore);

        Assert.True(restarted.TryGet(first.DeviceId, out _));
        Assert.True(restarted.TryGet(second.DeviceId, out _));
    }

    private static TrustRecord CreateRecord(DeviceIdentity identity) => new(
        identity.PublicIdentity,
        DateTimeOffset.UnixEpoch,
        CapabilityGrant.Of(Capability.ActivityReceive));
}
