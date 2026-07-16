using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class PersistentSwapEndpointJournalTests
{
    [Fact]
    public async Task PreparedReservationAndTerminalDecisionSurviveRestart()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal firstJournal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var endpoint = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                fixture.FirstCatalog,
                firstJournal);

            SwapPrepareResult prepared = await endpoint.PrepareAsync(
                fixture.FirstPrepare,
                default);

            Assert.True(prepared.Prepared);
            Assert.True(firstJournal.TryGet(
                fixture.Context.OperationId,
                out SwapEndpointRecord? record));
            Assert.Equal(SwapReservationPhase.Prepared, record.Reservation?.Phase);
        }

        using (PersistentSwapEndpointJournal secondJournal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var restarted = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                fixture.FirstCatalog,
                secondJournal);

            SwapApplyResult applied = await restarted.ApplyDecisionAsync(
                fixture.Commit,
                default);

            Assert.True(applied.Applied);
            Assert.Equal(SwapReservationPhase.Committed, applied.Phase);
            fixture.AssertFirstSwapped();
        }

        using PersistentSwapEndpointJournal thirdJournal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var replayed = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            thirdJournal);

        SwapApplyResult replay = await replayed.ApplyDecisionAsync(
            fixture.Commit,
            default);

        Assert.True(replay.Applied);
        Assert.Equal(SwapReservationPhase.Committed, replay.Phase);
        Assert.True(replayed.TryGetDecision(
            fixture.Context.OperationId,
            out SwapDecision? decision));
        Assert.Equal(fixture.Commit.Digest, decision.Digest);
    }

    [Fact]
    public async Task ReconstructedEndpointsConvergeFromDurableCoordinatorCommit()
    {
        Fixture fixture = new();
        var coordinatorStore = new TestSwapStatePayloadStore();
        var firstEndpointStore = new TestSwapEndpointStatePayloadStore();
        var secondEndpointStore = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapTransactionJournal coordinatorJournal =
               await PersistentSwapTransactionJournal.OpenAsync(coordinatorStore))
        using (PersistentSwapEndpointJournal firstJournal =
               await PersistentSwapEndpointJournal.OpenAsync(
                   fixture.FirstDevice,
                   firstEndpointStore))
        using (PersistentSwapEndpointJournal secondJournal =
               await PersistentSwapEndpointJournal.OpenAsync(
                   fixture.SecondDevice,
                   secondEndpointStore))
        using (var firstEndpoint = new PersistentSwapEndpoint(
                   fixture.FirstDevice,
                   fixture.FirstCatalog,
                   firstJournal))
        using (var secondEndpoint = new PersistentSwapEndpoint(
                   fixture.SecondDevice,
                   fixture.SecondCatalog,
                   secondJournal))
        {
            var coordinator = new SwapCoordinator(
                new TestClock(fixture.Now),
                coordinatorJournal,
                new DeterministicSwapTokenSource(
                    [fixture.FirstToken, fixture.SecondToken]));
            var droppedFirstCommit = new DeterministicSwapEndpointChannel(
                firstEndpoint,
                [ActivityDeliveryFault.DropBeforeDelivery]);

            SwapCoordinatorResult initial = await coordinator.ExecuteAsync(
                fixture.Context,
                droppedFirstCommit,
                fixture.FirstActivity.Descriptor.Id,
                new DirectSwapEndpointChannel(secondEndpoint),
                fixture.SecondActivity.Descriptor.Id);

            Assert.Equal(OperationStatus.Recovering, initial.Status);
            fixture.AssertFirstOriginal();
            fixture.AssertSecondSwapped();
        }

        using PersistentSwapTransactionJournal restartedCoordinatorJournal =
            await PersistentSwapTransactionJournal.OpenAsync(coordinatorStore);
        using PersistentSwapEndpointJournal restartedFirstJournal =
            await PersistentSwapEndpointJournal.OpenAsync(
                fixture.FirstDevice,
                firstEndpointStore);
        using PersistentSwapEndpointJournal restartedSecondJournal =
            await PersistentSwapEndpointJournal.OpenAsync(
                fixture.SecondDevice,
                secondEndpointStore);
        using var restartedFirst = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            restartedFirstJournal);
        using var restartedSecond = new PersistentSwapEndpoint(
            fixture.SecondDevice,
            fixture.SecondCatalog,
            restartedSecondJournal);
        var restartedCoordinator = new SwapCoordinator(
            new TestClock(fixture.Now),
            restartedCoordinatorJournal,
            new DeterministicSwapTokenSource([]));

        SwapCoordinatorResult recovered = await restartedCoordinator.RecoverAsync(
            fixture.Context.OperationId,
            new DirectSwapEndpointChannel(restartedFirst),
            new DirectSwapEndpointChannel(restartedSecond));

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertFirstSwapped();
        fixture.AssertSecondSwapped();
    }

    [Fact]
    public async Task DurableCommitReducesAfterRestartWhenCatalogMutationInitiallyFails()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal firstJournal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var rejectingCatalog = new RejectNextSwapCatalog(fixture.FirstCatalog);
            var endpoint = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                rejectingCatalog,
                firstJournal);
            Assert.True((await endpoint.PrepareAsync(
                fixture.FirstPrepare,
                default)).Prepared);

            SwapApplyResult blocked = await endpoint.ApplyDecisionAsync(
                fixture.Commit,
                default);
            SwapPrepareResult overlap = await endpoint.PrepareAsync(
                fixture.CreateOverlappingPrepare(),
                default);

            Assert.False(blocked.Applied);
            Assert.Equal(FailureCode.RevisionConflict, blocked.FailureCode);
            Assert.False(overlap.Prepared);
            Assert.Equal(FailureCode.ReservationConflict, overlap.FailureCode);
            fixture.AssertFirstOriginal();
            Assert.True(firstJournal.TryGet(
                fixture.Context.OperationId,
                out SwapEndpointRecord? record));
            Assert.Equal(SwapReservationPhase.Committed, record.Reservation?.Phase);
        }

        using PersistentSwapEndpointJournal restartedJournal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var restarted = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            restartedJournal);

        IReadOnlyList<SwapEndpointRecoveryResult> recovery =
            await restarted.RecoverAsync();

        SwapEndpointRecoveryResult result = Assert.Single(recovery);
        Assert.Equal(OperationStatus.Committed, result.Status);
        Assert.Equal(FailureCode.None, result.FailureCode);
        fixture.AssertFirstSwapped();
    }

    [Fact]
    public async Task PreparedRestartDoesNotGuessAbortAndStillExcludesOverlap()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal firstJournal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var endpoint = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                fixture.FirstCatalog,
                firstJournal);
            Assert.True((await endpoint.PrepareAsync(
                fixture.FirstPrepare,
                default)).Prepared);
        }

        using PersistentSwapEndpointJournal restartedJournal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var restarted = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            restartedJournal);

        SwapEndpointRecoveryResult pending = Assert.Single(
            await restarted.RecoverAsync());
        SwapPrepareResult overlap = await restarted.PrepareAsync(
            fixture.CreateOverlappingPrepare(),
            default);

        Assert.Equal(OperationStatus.Recovering, pending.Status);
        Assert.Equal(FailureCode.OperationInProgress, pending.FailureCode);
        Assert.False(overlap.Prepared);
        Assert.Equal(FailureCode.ReservationConflict, overlap.FailureCode);
        fixture.AssertFirstOriginal();
    }

    [Fact]
    public async Task ConflictingCatalogKeepsDurableCommitRecovering()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal journal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            SwapReservation reservation = SwapReservation.Prepare(
                fixture.Context.OperationId,
                fixture.FirstToken,
                fixture.FirstActivity,
                fixture.SecondActivity,
                fixture.Context.Deadline);
            Assert.Equal(
                SwapEndpointWriteStatus.Stored,
                (await journal.TryPrepareAsync(reservation)).Status);
            Assert.Equal(
                SwapEndpointWriteStatus.Stored,
                (await journal.TryRecordDecisionAsync(fixture.Commit)).Status);
        }

        ActivityInstance drifted = ActivityInstance.Active(
            fixture.FirstActivity.Descriptor,
            fixture.FirstActivity.Placement,
            fixture.FirstActivity.Revision + 1);
        Assert.True(fixture.FirstCatalog.TryUpdate(fixture.FirstActivity, drifted));
        using PersistentSwapEndpointJournal restartedJournal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var restarted = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            restartedJournal);

        SwapEndpointRecoveryResult result = Assert.Single(
            await restarted.RecoverAsync());
        SwapPrepareResult overlap = await restarted.PrepareAsync(
            fixture.CreateOverlappingPrepare(),
            default);

        Assert.Equal(OperationStatus.Recovering, result.Status);
        Assert.Equal(FailureCode.RevisionConflict, result.FailureCode);
        Assert.False(overlap.Prepared);
        Assert.Equal(FailureCode.ReservationConflict, overlap.FailureCode);
        Assert.True(fixture.FirstCatalog.TryGet(
            fixture.FirstActivity.Descriptor.Id,
            out ActivityInstance? current));
        Assert.Equal(drifted, current);
        Assert.False(fixture.FirstCatalog.TryGet(
            fixture.SecondActivity.Descriptor.Id,
            out _));
    }

    [Fact]
    public async Task AbortBeforePreparePersistsTombstoneAcrossRestart()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal firstJournal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var endpoint = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                fixture.FirstCatalog,
                firstJournal);
            SwapApplyResult applied = await endpoint.ApplyDecisionAsync(
                fixture.Abort,
                default);

            Assert.True(applied.Applied);
            Assert.Equal(SwapReservationPhase.Aborted, applied.Phase);
        }

        using PersistentSwapEndpointJournal restartedJournal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var restarted = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            restartedJournal);

        SwapApplyResult replay = await restarted.ApplyDecisionAsync(
            fixture.Abort,
            default);
        SwapPrepareResult delayed = await restarted.PrepareAsync(
            fixture.FirstPrepare,
            default);

        Assert.True(replay.Applied);
        Assert.False(delayed.Prepared);
        Assert.Equal(FailureCode.DecisionConflict, delayed.FailureCode);
        fixture.AssertFirstOriginal();
    }

    [Fact]
    public async Task AmbiguousDecisionSaveRequiresReloadBeforeCommitReduction()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore
        {
            ThrowAfterSaveNumber = 2,
        };
        using (PersistentSwapEndpointJournal firstJournal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var endpoint = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                fixture.FirstCatalog,
                firstJournal);
            Assert.True((await endpoint.PrepareAsync(
                fixture.FirstPrepare,
                default)).Prepared);

            await Assert.ThrowsAsync<SwapEndpointStatePersistenceException>(async () =>
                await endpoint.ApplyDecisionAsync(fixture.Commit, default));
            fixture.AssertFirstOriginal();
            await Assert.ThrowsAsync<SwapEndpointStatePersistenceException>(async () =>
                await endpoint.ApplyDecisionAsync(fixture.Abort, default));
        }

        store.ThrowAfterSaveNumber = null;
        using PersistentSwapEndpointJournal restartedJournal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var restarted = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            restartedJournal);

        SwapEndpointRecoveryResult recovered = Assert.Single(
            await restarted.RecoverAsync());

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertFirstSwapped();
    }

    [Fact]
    public async Task FailedPrepareSavePublishesNoReservationAndChangesNoActivity()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore
        {
            ThrowBeforeSaveNumber = 1,
        };
        using PersistentSwapEndpointJournal journal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        var endpoint = new PersistentSwapEndpoint(
            fixture.FirstDevice,
            fixture.FirstCatalog,
            journal);

        await Assert.ThrowsAsync<SwapEndpointStatePersistenceException>(async () =>
            await endpoint.PrepareAsync(fixture.FirstPrepare, default));

        Assert.False(journal.TryGet(fixture.Context.OperationId, out _));
        Assert.Null(store.Payload);
        fixture.AssertFirstOriginal();
    }

    [Fact]
    public async Task PayloadRejectsUnknownFieldsDeviceMismatchAndDigestTamper()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal journal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            var endpoint = new PersistentSwapEndpoint(
                fixture.FirstDevice,
                fixture.FirstCatalog,
                journal);
            Assert.True((await endpoint.PrepareAsync(
                fixture.FirstPrepare,
                default)).Prepared);
            Assert.Equal(
                SwapEndpointWriteStatus.Stored,
                (await journal.TryRecordDecisionAsync(fixture.Commit)).Status);
        }

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapEndpointJournal.OpenAsync(fixture.SecondDevice, store));

        byte[] valid = store.Payload!;
        store.Payload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(valid).Replace(
                "{\"formatVersion\":1,",
                "{\"formatVersion\":1,\"unexpected\":true,",
                StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store));

        store.Payload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(valid).Replace(
                fixture.FirstActivity.Descriptor.DescriptorDigest,
                new string('0', 64),
                StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store));

        store.Payload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(valid).Replace(
                fixture.Commit.Digest,
                fixture.Commit.Digest.ToLowerInvariant(),
                StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store));
    }

    [Fact]
    public async Task PayloadRejectsOrderingBoundsEnumsTimestampsAndParticipantMismatch()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using (PersistentSwapEndpointJournal journal =
               await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store))
        {
            SwapReservation reservation = SwapReservation.Prepare(
                fixture.Context.OperationId,
                fixture.FirstToken,
                fixture.FirstActivity,
                fixture.SecondActivity,
                fixture.Context.Deadline);
            Assert.Equal(
                SwapEndpointWriteStatus.Stored,
                (await journal.TryPrepareAsync(reservation)).Status);
            Assert.Equal(
                SwapEndpointWriteStatus.Stored,
                (await journal.TryRecordDecisionAsync(fixture.Commit)).Status);
            Assert.Equal(
                SwapEndpointWriteStatus.Stored,
                (await journal.TryPrepareAsync(fixture.CreateUniqueReservation())).Status);
        }

        byte[] valid = store.Payload!;
        JsonObject duplicated = ParsePayload(valid);
        JsonArray duplicateRecords = duplicated["records"]!.AsArray();
        duplicateRecords.Add(duplicateRecords[0]!.DeepClone());
        await AssertPayloadRejectedAsync(store, fixture.FirstDevice, duplicated);

        JsonObject reordered = ParsePayload(valid);
        JsonArray reorderedRecords = reordered["records"]!.AsArray();
        JsonNode first = reorderedRecords[0]!.DeepClone();
        JsonNode second = reorderedRecords[1]!.DeepClone();
        reorderedRecords.Clear();
        reorderedRecords.Add(second);
        reorderedRecords.Add(first);
        await AssertPayloadRejectedAsync(store, fixture.FirstDevice, reordered);

        JsonObject excessive = ParsePayload(valid);
        JsonArray excessiveRecords = excessive["records"]!.AsArray();
        JsonNode template = excessiveRecords[0]!.DeepClone();
        excessiveRecords.Clear();
        for (int index = 0;
             index <= PersistentSwapEndpointJournal.MaximumRecordCount;
             index++)
        {
            excessiveRecords.Add(template.DeepClone());
        }

        await AssertPayloadRejectedAsync(store, fixture.FirstDevice, excessive);

        JsonObject invalidPhase = ParsePayload(valid);
        FindCommittedRecord(invalidPhase)["reservation"]!["phase"] = int.MaxValue;
        await AssertPayloadRejectedAsync(store, fixture.FirstDevice, invalidPhase);

        JsonObject nonUtc = ParsePayload(valid);
        FindCommittedRecord(nonUtc)["reservation"]!["expiresAt"] =
            "2026-07-16T13:00:00.0000000+01:00";
        await AssertPayloadRejectedAsync(store, fixture.FirstDevice, nonUtc);

        SwapReservationToken wrongLocalToken = SwapReservationToken.From(
            Guid.Parse("30303030-3030-3030-3030-303030303030"));
        SwapDecision mismatched = SwapDecision.Create(
            fixture.Context.OperationId,
            SwapDecisionOutcome.Commit,
            fixture.Commit.DecidedAt,
            [
                SwapDecisionParticipant.Create(
                    fixture.FirstDevice,
                    wrongLocalToken),
                SwapDecisionParticipant.Create(
                    fixture.SecondDevice,
                    fixture.SecondToken),
            ]);
        JsonObject participantMismatch = ParsePayload(valid);
        JsonObject committed = FindCommittedRecord(participantMismatch);
        JsonObject decision = committed["decision"]!.AsObject();
        JsonObject localParticipant = decision["participants"]!
            .AsArray()
            .Select(static node => node!.AsObject())
            .Single(participant => StringComparer.Ordinal.Equals(
                participant["deviceId"]!.GetValue<string>(),
                "11111111-1111-1111-1111-111111111111"));
        localParticipant["reservationToken"] = wrongLocalToken.ToString();
        decision["digest"] = mismatched.Digest;
        committed["reservation"]!["decisionDigest"] = mismatched.Digest;
        await AssertPayloadRejectedAsync(
            store,
            fixture.FirstDevice,
            participantMismatch);

        store.Payload = new byte[
            PersistentSwapEndpointJournal.MaximumPayloadBytes + 1];
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapEndpointJournal.OpenAsync(
                fixture.FirstDevice,
                store));
    }

    [Fact]
    public async Task JournalCapacityRejectsOverflowWithoutPublishingIt()
    {
        Fixture fixture = new();
        var store = new TestSwapEndpointStatePayloadStore();
        using PersistentSwapEndpointJournal journal =
            await PersistentSwapEndpointJournal.OpenAsync(fixture.FirstDevice, store);
        for (int index = 0;
             index < PersistentSwapEndpointJournal.MaximumRecordCount;
             index++)
        {
            SwapReservation reservation = fixture.CreateUniqueReservation();
            SwapEndpointWriteResult result = await journal.TryPrepareAsync(reservation);
            Assert.Equal(SwapEndpointWriteStatus.Stored, result.Status);
        }

        byte[] fullPayload = store.Payload!.ToArray();
        SwapEndpointWriteResult overflow = await journal.TryPrepareAsync(
            fixture.CreateUniqueReservation());

        Assert.Equal(SwapEndpointWriteStatus.CapacityExceeded, overflow.Status);
        Assert.Equal(fullPayload, store.Payload);
        Assert.Equal(PersistentSwapEndpointJournal.MaximumRecordCount, journal.Count);
    }

    private static async Task AssertPayloadRejectedAsync(
        TestSwapEndpointStatePayloadStore store,
        DeviceId deviceId,
        JsonObject payload)
    {
        store.Payload = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSwapEndpointJournal.OpenAsync(deviceId, store));
    }

    private static JsonObject FindCommittedRecord(JsonObject payload) => payload["records"]!
        .AsArray()
        .Select(static node => node!.AsObject())
        .Single(static record => record["decision"] is not null);

    private static JsonObject ParsePayload(byte[] payload) =>
        JsonNode.Parse(Encoding.UTF8.GetString(payload))!.AsObject();

    private sealed class Fixture
    {
        private int uniqueSequence;

        public Fixture()
        {
            FirstDevice = DeviceId.Parse("11111111-1111-1111-1111-111111111111");
            SecondDevice = DeviceId.Parse("22222222-2222-2222-2222-222222222222");
            FirstActivity = CreateActivity(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                FirstDevice,
                "First",
                "first");
            SecondActivity = CreateActivity(
                ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                SecondDevice,
                "Second",
                "second");
            FirstToken = SwapReservationToken.From(
                Guid.Parse("10101010-1010-1010-1010-101010101010"));
            SecondToken = SwapReservationToken.From(
                Guid.Parse("20202020-2020-2020-2020-202020202020"));
            Context = OperationContext.Create(
                OperationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
            Now = Context.Deadline.AddMinutes(-2);
            FirstPrepare = new SwapPrepareCommand(
                Context.OperationId,
                FirstToken,
                FirstActivity,
                SecondActivity,
                Context.Deadline);
            SwapCoordinatorTransaction transaction = SwapCoordinatorTransaction.Create(
                Context,
                FirstActivity,
                FirstToken,
                SecondActivity,
                SecondToken);
            Commit = transaction.CreateDecision(
                SwapDecisionOutcome.Commit,
                Context.Deadline.AddMinutes(-1));
            Abort = transaction.CreateDecision(
                SwapDecisionOutcome.Abort,
                Context.Deadline.AddMinutes(-1),
                FailureCode.PeerUnavailable);
            FirstCatalog = new InMemoryActivityCatalog();
            Assert.True(FirstCatalog.TryAdd(FirstActivity));
            SecondCatalog = new InMemoryActivityCatalog();
            Assert.True(SecondCatalog.TryAdd(SecondActivity));
        }

        public SwapDecision Abort { get; }
        public SwapDecision Commit { get; }
        public OperationContext Context { get; }
        public ActivityInstance FirstActivity { get; }
        public InMemoryActivityCatalog FirstCatalog { get; }
        public DeviceId FirstDevice { get; }
        public SwapPrepareCommand FirstPrepare { get; }
        public SwapReservationToken FirstToken { get; }
        public DateTimeOffset Now { get; }
        public ActivityInstance SecondActivity { get; }
        public InMemoryActivityCatalog SecondCatalog { get; }
        public DeviceId SecondDevice { get; }
        public SwapReservationToken SecondToken { get; }

        public void AssertFirstOriginal()
        {
            Assert.True(FirstCatalog.TryGet(
                FirstActivity.Descriptor.Id,
                out ActivityInstance? current));
            Assert.Equal(FirstActivity, current);
            Assert.False(FirstCatalog.TryGet(SecondActivity.Descriptor.Id, out _));
        }

        public void AssertFirstSwapped()
        {
            Assert.False(FirstCatalog.TryGet(FirstActivity.Descriptor.Id, out _));
            Assert.True(FirstCatalog.TryGet(
                SecondActivity.Descriptor.Id,
                out ActivityInstance? current));
            Assert.Equal(
                ActivityInstance.Active(
                    SecondActivity.Descriptor,
                    FirstActivity.Placement,
                    SecondActivity.Revision + 1),
                current);
        }

        public void AssertSecondSwapped()
        {
            Assert.False(SecondCatalog.TryGet(SecondActivity.Descriptor.Id, out _));
            Assert.True(SecondCatalog.TryGet(
                FirstActivity.Descriptor.Id,
                out ActivityInstance? current));
            Assert.Equal(
                ActivityInstance.Active(
                    FirstActivity.Descriptor,
                    SecondActivity.Placement,
                    FirstActivity.Revision + 1),
                current);
        }

        public SwapPrepareCommand CreateOverlappingPrepare() => new(
            OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            SwapReservationToken.From(
                Guid.Parse("30303030-3030-3030-3030-303030303030")),
            FirstActivity,
            SecondActivity,
            Context.Deadline);

        public SwapReservation CreateUniqueReservation()
        {
            uniqueSequence++;
            string prefix = uniqueSequence.ToString("X8", CultureInfo.InvariantCulture);
            DeviceId remote = DeviceId.Parse(
                $"{prefix}-2222-2222-2222-222222222222");
            ActivityInstance original = CreateActivity(
                ActivityId.Parse($"{prefix}-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                FirstDevice,
                $"Original {uniqueSequence}",
                $"original-{uniqueSequence}");
            ActivityInstance incoming = CreateActivity(
                ActivityId.Parse($"{prefix}-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                remote,
                $"Incoming {uniqueSequence}",
                $"incoming-{uniqueSequence}");
            return SwapReservation.Prepare(
                OperationId.Parse($"{prefix}-cccc-cccc-cccc-cccccccccccc"),
                SwapReservationToken.From(Guid.Parse(
                    $"{prefix}-dddd-dddd-dddd-dddddddddddd")),
                original,
                incoming,
                Context.Deadline);
        }

        private static ActivityInstance CreateActivity(
            ActivityId id,
            DeviceId deviceId,
            string title,
            string content) => ActivityInstance.Active(
                ActivityDescriptor.Create(
                    id,
                    ActivityKind.Parse("workspace.note/v1"),
                    deviceId,
                    title,
                    $"{{\"content\":\"{content}\"}}"),
                ActivityPlacement.On(deviceId));
    }

    private sealed class RejectNextSwapCatalog(IActivityCatalog inner) : IActivityCatalog
    {
        private bool reject = true;

        public bool TryGet(
            ActivityId activityId,
            [NotNullWhen(true)]
            out ActivityInstance? activity) => inner.TryGet(activityId, out activity);

        public bool TryAdd(ActivityInstance activity) => inner.TryAdd(activity);

        public bool TryUpdate(
            ActivityInstance expected,
            ActivityInstance replacement) => inner.TryUpdate(expected, replacement);

        public bool TrySwapReplace(
            ActivityInstance expected,
            ActivityInstance replacement)
        {
            if (reject)
            {
                reject = false;
                return false;
            }

            return inner.TrySwapReplace(expected, replacement);
        }
    }

    private sealed class TestSwapEndpointStatePayloadStore :
        ISwapEndpointStatePayloadStore
    {
        private int saveCount;

        public byte[]? Payload { get; set; }
        public int? ThrowAfterSaveNumber { get; set; }
        public int? ThrowBeforeSaveNumber { get; set; }

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            saveCount++;
            if (ThrowBeforeSaveNumber == saveCount)
            {
                throw new IOException("Injected endpoint save failure before write.");
            }

            Payload = payload.ToArray();
            if (ThrowAfterSaveNumber == saveCount)
            {
                throw new IOException("Injected endpoint save failure after write.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
