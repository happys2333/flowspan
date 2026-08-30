using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class TrustSessionCoordinatorTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task PreparationReservationRequiresExactFingerprintAndAllCapabilities()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var sink = new RecordingPreparationInvalidationSink();

        TrustPreparationReservationResult result =
            await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(
                    Capability.MirrorView,
                    Capability.MirrorDrive),
                sink);

        Assert.Equal(TrustPreparationReservationStatus.Reserved, result.Status);
        TrustPreparationRegistration registration = Assert.IsType<
            TrustPreparationRegistration>(result.Registration);
        Assert.True(registration.IsCurrent);

        await registration.DisposeAsync();

        Assert.False(registration.IsCurrent);
        Assert.Equal(0, sink.InvalidationCount);
    }

    [Fact]
    public async Task AppliedSameGrantUpdateInvalidatesPreparationReservation()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        CapabilityGrant grant = CapabilityGrant.Of(Capability.MirrorView);
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            grant));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var sink = new RecordingPreparationInvalidationSink();
        TrustPreparationReservationResult reservation =
            await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                grant,
                sink);

        TrustMutationResult result = await coordinator.UpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView));

        Assert.Equal(TrustMutationResult.Applied, result);
        Assert.Equal(1, sink.InvalidationCount);
        Assert.False(Assert.IsType<TrustPreparationRegistration>(
            reservation.Registration).IsCurrent);
    }

    [Fact]
    public async Task PreparationReservationReportsExactRejectionStatus()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var sink = new RecordingPreparationInvalidationSink();
        DeviceId missingPeer =
            DeviceId.Parse("33333333-3333-3333-3333-333333333333");

        TrustPreparationReservationResult missing =
            await coordinator.TryReservePreparationAsync(
                missingPeer,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView),
                sink);
        TrustPreparationReservationResult changed =
            await coordinator.TryReservePreparationAsync(
                PeerId,
                "authenticated-but-different-fingerprint",
                CapabilityGrant.Of(Capability.MirrorView),
                sink);
        TrustPreparationReservationResult denied =
            await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(
                    Capability.MirrorView,
                    Capability.MirrorDrive),
                sink);

        Assert.Equal(TrustPreparationReservationStatus.PeerNotFound, missing.Status);
        Assert.Equal(TrustPreparationReservationStatus.IdentityChanged, changed.Status);
        Assert.Equal(TrustPreparationReservationStatus.CapabilityDenied, denied.Status);
        Assert.Null(missing.Registration);
        Assert.Null(changed.Registration);
        Assert.Null(denied.Registration);
        Assert.False(missing.Reserved);
        Assert.False(changed.Reserved);
        Assert.False(denied.Reserved);
        Assert.Equal(0, sink.InvalidationCount);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.None,
                sink));
    }

    [Fact]
    public async Task RevokeRegrantAndLateOldDisposeCannotReviveOrRemoveReplacement()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(PeerId, "Original");
        using DeviceIdentity replacement = DeviceIdentity.Generate(PeerId, "Replacement");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            original.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var oldSink = new RecordingPreparationInvalidationSink();
        TrustPreparationRegistration oldRegistration = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                original.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView),
                oldSink)).Registration);

        Assert.Equal(
            TrustMutationResult.Applied,
            await coordinator.RevokePeerAsync(
                PeerId,
                original.PublicIdentity.Fingerprint));
        Assert.False(oldRegistration.IsCurrent);
        Assert.Equal(1, oldSink.InvalidationCount);
        Assert.Equal(
            TrustRegistrationResult.Added,
            await coordinator.RegisterAsync(new TrustRecord(
                replacement.PublicIdentity,
                DateTimeOffset.UnixEpoch.AddMinutes(1),
                CapabilityGrant.Of(Capability.MirrorView))));
        var replacementSink = new RecordingPreparationInvalidationSink();
        TrustPreparationRegistration replacementRegistration = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                replacement.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView),
                replacementSink)).Registration);

        await oldRegistration.DisposeAsync();
        TrustMutationResult staleRevoke = await coordinator.RevokePeerAsync(
            PeerId,
            original.PublicIdentity.Fingerprint);

        Assert.Equal(TrustMutationResult.IdentityChanged, staleRevoke);
        Assert.True(replacementRegistration.IsCurrent);
        Assert.Equal(0, replacementSink.InvalidationCount);
        Assert.True(await coordinator.RevokePeerAsync(PeerId));
        Assert.False(replacementRegistration.IsCurrent);
        Assert.Equal(1, replacementSink.InvalidationCount);
    }

    [Fact]
    public async Task StoreCommitAndPreparationReservationAreSerializedOnBothSides()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        CapabilityGrant originalGrant = CapabilityGrant.Of(
            Capability.MirrorView,
            Capability.MirrorDrive);
        var trustStore = new ControlledUpdateTrustStore(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            originalGrant));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var existingSink = new RecordingPreparationInvalidationSink();
        TrustPreparationRegistration existing = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                originalGrant,
                existingSink)).Registration);

        Task<TrustMutationResult> mutation = coordinator.UpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView)).AsTask();
        await trustStore.UpdateStarted;
        Assert.False(trustStore.UpdateCommitted.IsCompleted);
        Assert.True(existing.IsCurrent);

        Task<TrustPreparationReservationResult> queuedReservation =
            coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorDrive),
                new RecordingPreparationInvalidationSink()).AsTask();
        trustStore.AllowUpdate();

        await existingSink.Invalidated;
        Assert.True(trustStore.UpdateCommitted.IsCompleted);
        Assert.False(existing.IsCurrent);
        Assert.Equal(TrustMutationResult.Applied, await mutation);
        TrustPreparationReservationResult denied = await queuedReservation;
        Assert.Equal(
            TrustPreparationReservationStatus.CapabilityDenied,
            denied.Status);
    }

    [Fact]
    public async Task AppliedInvalidationRunsBeforeChangedAndBlockingSessionStop()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var session = new BlockingRevocableSession();
        await using TrustSessionRegistration sessionRegistration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                session)
            ?? throw new InvalidOperationException("Expected an active session.");
        var changed = false;
        coordinator.Changed += () => changed = true;
        bool? sinkSawChanged = null;
        bool? sinkSawStop = null;
        var sink = new RecordingPreparationInvalidationSink(() =>
        {
            sinkSawChanged = changed;
            sinkSawStop = session.StopStarted.IsCompleted;
        });
        TrustPreparationRegistration preparation = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView),
                sink)).Registration);

        Task<TrustMutationResult> mutation = coordinator.UpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.None).AsTask();
        await session.StopStarted;

        Assert.False(preparation.IsCurrent);
        Assert.Equal(1, sink.InvalidationCount);
        Assert.False(sinkSawChanged);
        Assert.False(sinkSawStop);
        Assert.True(changed);
        Assert.False(mutation.IsCompleted);
        session.AllowStop();
        Assert.Equal(TrustMutationResult.Applied, await mutation);
    }

    [Fact]
    public async Task RejectedThrownAndCanceledMutationsLeavePreparationCurrent()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        CapabilityGrant grant = CapabilityGrant.Of(Capability.MirrorView);
        var trustStore = new ControlledUpdateTrustStore(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            grant));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var sink = new RecordingPreparationInvalidationSink();
        TrustPreparationRegistration registration = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                grant,
                sink)).Registration);

        trustStore.AllowUpdate();
        Assert.Equal(
            TrustMutationResult.IdentityChanged,
            await coordinator.UpdateCapabilitiesAsync(
                PeerId,
                "stale-fingerprint",
                grant));
        Assert.True(registration.IsCurrent);

        var injected = new IOException("Injected store failure.");
        trustStore.Reset(injected);
        Task<TrustMutationResult> throwing = coordinator.UpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            grant).AsTask();
        await trustStore.UpdateStarted;
        trustStore.AllowUpdate();
        IOException observed = await Assert.ThrowsAsync<IOException>(() => throwing);
        Assert.Same(injected, observed);
        Assert.True(registration.IsCurrent);

        trustStore.Reset();
        using var cancellation = new CancellationTokenSource();
        Task<TrustMutationResult> canceled = coordinator.UpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            grant,
            cancellation.Token).AsTask();
        await trustStore.UpdateStarted;
        cancellation.Cancel();
        OperationCanceledException cancellationFailure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal(cancellation.Token, cancellationFailure.CancellationToken);
        Assert.True(registration.IsCurrent);
        Assert.Equal(0, sink.InvalidationCount);
    }

    [Fact]
    public async Task PreparationGateCancellationPreservesExactCallerToken()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        CapabilityGrant grant = CapabilityGrant.Of(Capability.MirrorView);
        var trustStore = new ControlledUpdateTrustStore(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            grant));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        Task<TrustMutationResult> blockingMutation =
            coordinator.UpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                grant).AsTask();
        await trustStore.UpdateStarted;
        using var cancellation = new CancellationTokenSource();

        Task<TrustPreparationReservationResult> reservation =
            coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                grant,
                new RecordingPreparationInvalidationSink(),
                cancellation.Token).AsTask();
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reservation);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        trustStore.AllowUpdate();
        Assert.Equal(TrustMutationResult.Applied, await blockingMutation);
    }

    [Fact]
    public async Task SinkFailureDoesNotUndoMutationAndCombinesAfterSessionStop()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var sinkFailure = new IOException("Injected invalidation failure.");
        var stopFailure = new IOException("Injected stop failure.");
        var sink = new RecordingPreparationInvalidationSink(
            invalidating: null,
            failure: sinkFailure);
        TrustPreparationRegistration preparation = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView),
                sink)).Registration);
        var session = new FailingRevocableSession(stopFailure);
        await using TrustSessionRegistration sessionRegistration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                session)
            ?? throw new InvalidOperationException("Expected an active session.");

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            async () => await coordinator.UpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.None));

        Assert.False(preparation.IsCurrent);
        Assert.Equal(1, sink.InvalidationCount);
        Assert.Equal(1, session.StopCount);
        Assert.False(trustStore.Allows(PeerId, Capability.MirrorView));
        Assert.Collection(
            failure.InnerExceptions,
            first => Assert.Same(sinkFailure, first),
            second =>
            {
                TrustSessionStopException stop = Assert.IsType<
                    TrustSessionStopException>(second);
                Assert.Same(stopFailure, Assert.Single(stop.InnerExceptions));
            });
    }

    [Fact]
    public async Task SingleSinkFailureIsRethrownRawAfterAppliedMutation()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var injected = new IOException("Injected single sink failure.");
        var sink = new RecordingPreparationInvalidationSink(
            invalidating: null,
            failure: injected);
        TrustPreparationRegistration registration = Assert.IsType<
            TrustPreparationRegistration>((await coordinator.TryReservePreparationAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView),
                sink)).Registration);

        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await coordinator.UpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorView)));

        Assert.Same(injected, failure);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.InvalidationCount);
        Assert.True(trustStore.Allows(PeerId, Capability.MirrorView));
    }

    [Fact]
    public async Task OutOfMemoryEscapesAfterAllPreparationRegistrationsDeactivate()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "Injected Trust Preparation invalidation exhaustion.");
#pragma warning restore CA2201
        var firstSink = new RecordingPreparationInvalidationSink(
            invalidating: null,
            failure: injected);
        var secondSink = new RecordingPreparationInvalidationSink();
        TrustPreparationRegistration first = Assert.IsType<
            TrustPreparationRegistration>((await coordinator
                .TryReservePreparationAsync(
                    PeerId,
                    identity.PublicIdentity.Fingerprint,
                    CapabilityGrant.Of(Capability.MirrorView),
                    firstSink)).Registration);
        TrustPreparationRegistration second = Assert.IsType<
            TrustPreparationRegistration>((await coordinator
                .TryReservePreparationAsync(
                    PeerId,
                    identity.PublicIdentity.Fingerprint,
                    CapabilityGrant.Of(Capability.MirrorView),
                    secondSink)).Registration);

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await coordinator
                .UpdateCapabilitiesAsync(
                    PeerId,
                    identity.PublicIdentity.Fingerprint,
                    CapabilityGrant.None));

        Assert.Same(injected, failure);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.Equal(1, firstSink.InvalidationCount);
        Assert.Equal(0, secondSink.InvalidationCount);
        Assert.False(trustStore.Allows(PeerId, Capability.MirrorView));
    }

    [Fact]
    public async Task OutOfMemoryFromSessionStopEscapesCommittedMutationUnwrapped()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var sink = new RecordingPreparationInvalidationSink();
        TrustPreparationRegistration preparation = Assert.IsType<
            TrustPreparationRegistration>((await coordinator
                .TryReservePreparationAsync(
                    PeerId,
                    identity.PublicIdentity.Fingerprint,
                    CapabilityGrant.Of(Capability.MirrorView),
                    sink)).Registration);
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var injected = new OutOfMemoryException(
            "Injected Trust session stop exhaustion.");
#pragma warning restore CA2201
        var session = new FailingRevocableSession(injected);
        await using TrustSessionRegistration sessionRegistration =
            await coordinator.TryRegisterAsync(
                PeerId,
                CapabilityGrant.Of(Capability.MirrorView),
                session)
            ?? throw new InvalidOperationException("Expected an active session.");

        OutOfMemoryException failure = await Assert.ThrowsAsync<
            OutOfMemoryException>(async () => await coordinator
                .UpdateCapabilitiesAsync(
                    PeerId,
                    identity.PublicIdentity.Fingerprint,
                    CapabilityGrant.None));

        Assert.Same(injected, failure);
        Assert.False(preparation.IsCurrent);
        Assert.Equal(1, sink.InvalidationCount);
        Assert.Equal(1, session.StopCount);
        Assert.False(trustStore.Allows(PeerId, Capability.MirrorView));
    }

    [Fact]
    public async Task DisposeInvalidatesAllInStableOrderAndRetainsFailure()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        var coordinator = new TrustSessionCoordinator(trustStore);
        var order = new List<int>();
        var firstFailure = new IOException("first");
        var thirdFailure = new IOException("third");
        TrustPreparationRegistration first = await ReserveAsync(
            new OrderedPreparationInvalidationSink(1, order, firstFailure));
        TrustPreparationRegistration second = await ReserveAsync(
            new OrderedPreparationInvalidationSink(2, order));
        TrustPreparationRegistration third = await ReserveAsync(
            new OrderedPreparationInvalidationSink(3, order, thirdFailure));
        Assert.True(first.RegistrationId < second.RegistrationId);
        Assert.True(second.RegistrationId < third.RegistrationId);

        AggregateException initial = await Assert.ThrowsAsync<AggregateException>(
            () => coordinator.DisposeAsync().AsTask());
        AggregateException repeated = await Assert.ThrowsAsync<AggregateException>(
            () => coordinator.DisposeAsync().AsTask());

        Assert.Equal([1, 2, 3], order);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.False(third.IsCurrent);
        Assert.Collection(
            initial.InnerExceptions,
            failure => Assert.Same(firstFailure, failure),
            failure => Assert.Same(thirdFailure, failure));
        Assert.Same(initial, repeated);
        await first.DisposeAsync();
        await second.DisposeAsync();
        await third.DisposeAsync();

        async Task<TrustPreparationRegistration> ReserveAsync(
            ITrustPreparationInvalidationSink sink) => Assert.IsType<
                TrustPreparationRegistration>((await coordinator
                    .TryReservePreparationAsync(
                        PeerId,
                        identity.PublicIdentity.Fingerprint,
                        CapabilityGrant.Of(Capability.MirrorView),
                        sink)).Registration);
    }

    [Fact]
    public async Task SuccessfulTrustMutationsNotifyAfterCommitAndIsolateObservers()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
        await using var coordinator = new TrustSessionCoordinator(
            new InMemoryTrustStore());
        var observedStates = new List<string>();
        coordinator.Changed += static () =>
            throw new InvalidOperationException("Injected observer failure.");
        coordinator.Changed += ObserveCommittedState;
        var initial = new TrustRecord(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView));

        Assert.Equal(
            TrustRegistrationResult.Added,
            await coordinator.RegisterAsync(initial));
        Assert.Equal(
            TrustRegistrationResult.AlreadyTrusted,
            await coordinator.RegisterAsync(initial));
        Assert.Equal(
            TrustMutationResult.IdentityChanged,
            await coordinator.UpdateCapabilitiesAsync(
                PeerId,
                "stale-fingerprint",
                CapabilityGrant.Of(Capability.MirrorDrive)));
        Assert.Equal(
            TrustMutationResult.Applied,
            await coordinator.UpdateCapabilitiesAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint,
                CapabilityGrant.Of(Capability.MirrorDrive)));
        Assert.Equal(
            TrustMutationResult.IdentityChanged,
            await coordinator.RevokePeerAsync(PeerId, "stale-fingerprint"));
        Assert.Equal(
            TrustMutationResult.Applied,
            await coordinator.RevokePeerAsync(
                PeerId,
                identity.PublicIdentity.Fingerprint));
        Assert.False(await coordinator.RevokePeerAsync(PeerId));

        Assert.Equal(["view", "drive", "revoked"], observedStates);

        void ObserveCommittedState()
        {
            if (!coordinator.TryGetCurrentTrust(PeerId, out TrustRecord? current))
            {
                observedStates.Add("revoked");
            }
            else if (current.GrantedCapabilities.Allows(Capability.MirrorDrive))
            {
                observedStates.Add("drive");
            }
            else
            {
                observedStates.Add("view");
            }
        }
    }

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
                Capability.MirrorView)));
        await using var coordinator = new TrustSessionCoordinator(trustStore);
        var session = new RecordingRevocableSession();
        await using TrustSessionRegistration registration =
            await coordinator.TryRegisterAnyAsync(
                PeerId,
                CapabilityGrant.Of(
                    Capability.ActivityOffer,
                    Capability.ActivityReceive,
                    Capability.ActivityReplace,
                    Capability.ActivitySwap,
                    Capability.MirrorView,
                    Capability.MirrorDrive),
                session)
            ?? throw new InvalidOperationException("Expected an authorized session.");

        Assert.True(await coordinator.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorView)));
        Assert.Equal(0, session.StopCount);

        Assert.True(await coordinator.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.MirrorDrive)));
        Assert.Equal(0, session.StopCount);

        Assert.True(await coordinator.TryUpdateCapabilitiesAsync(
            PeerId,
            identity.PublicIdentity.Fingerprint,
            CapabilityGrant.Of(Capability.FileReceive)));

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

    private sealed class RecordingPreparationInvalidationSink(
        Action? invalidating = null,
        Exception? failure = null) : ITrustPreparationInvalidationSink
    {
        private readonly TaskCompletionSource invalidated = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int invalidationCount;

        public Task Invalidated => invalidated.Task;

        public int InvalidationCount => Volatile.Read(ref invalidationCount);

        public void InvalidateTrustPreparationNow()
        {
            Interlocked.Increment(ref invalidationCount);
            invalidating?.Invoke();
            invalidated.TrySetResult();
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private sealed class OrderedPreparationInvalidationSink(
        int marker,
        List<int> order,
        Exception? failure = null) : ITrustPreparationInvalidationSink
    {
        public void InvalidateTrustPreparationNow()
        {
            order.Add(marker);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private sealed class FailingRevocableSession(Exception failure) :
        IRevocablePeerSession
    {
        public int StopCount { get; private set; }

        public ValueTask StopAsync(TrustSessionStopReason reason)
        {
            StopCount++;
            throw failure;
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

    private sealed class ControlledUpdateTrustStore : ITrustStore
    {
        private readonly InMemoryTrustStore inner = new();
        private TaskCompletionSource allowUpdate = NewSignal();
        private Exception? failure;
        private TaskCompletionSource updateCommitted = NewSignal();
        private TaskCompletionSource updateStarted = NewSignal();

        public ControlledUpdateTrustStore(TrustRecord initial) =>
            inner.Register(initial);

        public Task UpdateCommitted => updateCommitted.Task;

        public Task UpdateStarted => updateStarted.Task;

        public SecretStoreProtection Protection => inner.Protection;

        public void AllowUpdate() => allowUpdate.TrySetResult();

        public void Reset(Exception? injectedFailure = null)
        {
            allowUpdate = NewSignal();
            updateCommitted = NewSignal();
            updateStarted = NewSignal();
            failure = injectedFailure;
        }

        public System.Collections.Immutable.ImmutableArray<TrustedPeerSnapshot>
            GetSnapshot() => inner.GetSnapshot();

        public bool Allows(DeviceId peerDeviceId, Capability capability) =>
            inner.Allows(peerDeviceId, capability);

        public ValueTask<TrustRegistrationResult> RegisterAsync(
            TrustRecord trustRecord,
            CancellationToken cancellationToken = default) =>
            inner.RegisterAsync(trustRecord, cancellationToken);

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

        public async ValueTask<TrustMutationResult> UpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default)
        {
            updateStarted.TrySetResult();
            await allowUpdate.Task.WaitAsync(cancellationToken);
            if (failure is not null)
            {
                throw failure;
            }

            TrustMutationResult result = await inner.UpdateCapabilitiesAsync(
                peerDeviceId,
                expectedFingerprint,
                capabilities,
                cancellationToken);
            if (result == TrustMutationResult.Applied)
            {
                updateCommitted.TrySetResult();
            }

            return result;
        }

        private static TaskCompletionSource NewSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
