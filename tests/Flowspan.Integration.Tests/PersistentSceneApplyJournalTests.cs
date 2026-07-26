using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class PersistentSceneApplyJournalTests
{
    private static readonly DateTimeOffset AcceptedAt =
        new(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedAttemptRoundTripsAndReplaysWithoutAnotherOperation()
    {
        Fixture fixture = CreateFixture();
        var payloadStore = new InMemoryPayloadStore();
        var clock = new MutableClock(AcceptedAt);
        var firstPort = new RecordingOperationPort(
            fixture.Descriptor,
            clock,
            failIfCalled: false);
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            var coordinator = new SceneApplyCoordinator(clock, journal, firstPort);

            SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
                fixture.Scene,
                fixture.Preview,
                fixture.Approval,
                CancellationToken.None);

            Assert.Equal(SceneApplyOverallStatus.Completed, execution.Result?.Status);
            Assert.Equal(1, firstPort.CallCount);
        }

        string plaintext = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        Assert.DoesNotContain("scene-name-canary", plaintext, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "activity-title-canary",
            plaintext,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "activity-payload-canary",
            plaintext,
            StringComparison.Ordinal);

        clock.UtcNow = fixture.Preview.ExpiresAt.AddHours(1);
        var replayPort = new RecordingOperationPort(
            fixture.Descriptor,
            clock,
            failIfCalled: true);
        using PersistentSceneApplyJournal reopened =
            await PersistentSceneApplyJournal.OpenAsync(payloadStore);
        var replayCoordinator = new SceneApplyCoordinator(
            clock,
            reopened,
            replayPort);

        SceneApplyExecutionResult replay = await replayCoordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(replay.Result);
        Assert.Equal(SceneApplyOverallStatus.Completed, result.Status);
        Assert.Equal(fixture.Preview.Items[0].ChildOperationId, result.Items[0].ChildOperationId);
        Assert.Equal(0, replayPort.CallCount);
    }

    [Fact]
    public async Task AmbiguousSavePoisonsOpenJournalUntilDurableStateIsReopened()
    {
        Fixture fixture = CreateFixture();
        var payloadStore = new InMemoryPayloadStore
        {
            FailAfterNextWrite = true,
        };
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            await Assert.ThrowsAsync<SceneApplyStatePersistenceException>(async () =>
                await journal.CreateAsync(
                    fixture.Preview,
                    AcceptedAt,
                    CancellationToken.None));
            Assert.Equal(0, journal.EntryCount);
            Assert.Equal(1, payloadStore.SaveCount);

            await Assert.ThrowsAsync<SceneApplyStatePersistenceException>(async () =>
                await journal.CreateAsync(
                    fixture.Preview,
                    AcceptedAt,
                    CancellationToken.None));
            Assert.Equal(1, payloadStore.SaveCount);
        }

        using PersistentSceneApplyJournal reopened =
            await PersistentSceneApplyJournal.OpenAsync(payloadStore);
        SceneApplyJournalState? restored = await reopened.LoadAsync(
            fixture.Preview.ParentOperationId,
            CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(1, reopened.EntryCount);
        Assert.True(restored.Matches(fixture.Preview));
    }

    [Theory]
    [InlineData(JournalSaveBoundary.Started)]
    [InlineData(JournalSaveBoundary.Outcome)]
    [InlineData(JournalSaveBoundary.Completed)]
    public async Task EveryUpdateSaveBoundaryPoisonsWithoutPublishingCandidate(
        JournalSaveBoundary boundary)
    {
        Fixture fixture = CreateFixture();
        var payloadStore = new InMemoryPayloadStore();
        SceneApplyItemResult committed = CreateCommittedHandoffResult(fixture);
        SceneApplyResult completed = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt,
            [committed]);
        SceneApplyJournalItemStatus expectedOpenStatus;
        bool expectedDurableCompletion;
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            await journal.CreateAsync(
                fixture.Preview,
                AcceptedAt,
                CancellationToken.None);
            if (boundary is JournalSaveBoundary.Outcome
                or JournalSaveBoundary.Completed)
            {
                await journal.RecordItemStartedAsync(
                    fixture.Preview.ParentOperationId,
                    0,
                    AcceptedAt,
                    CancellationToken.None);
            }

            if (boundary == JournalSaveBoundary.Completed)
            {
                await journal.RecordItemOutcomeAsync(
                    fixture.Preview.ParentOperationId,
                    committed,
                    CancellationToken.None);
            }

            SceneApplyJournalState before = Assert.IsType<SceneApplyJournalState>(
                await journal.LoadAsync(
                    fixture.Preview.ParentOperationId,
                    CancellationToken.None));
            expectedOpenStatus = before.Items[0].Status;
            expectedDurableCompletion =
                boundary == JournalSaveBoundary.Completed;
            int saveCountBeforeFailure = payloadStore.SaveCount;
            payloadStore.FailAfterNextWrite = true;

            await Assert.ThrowsAsync<SceneApplyStatePersistenceException>(
                async () =>
                {
                    switch (boundary)
                    {
                        case JournalSaveBoundary.Started:
                            await journal.RecordItemStartedAsync(
                                fixture.Preview.ParentOperationId,
                                0,
                                AcceptedAt,
                                CancellationToken.None);
                            break;
                        case JournalSaveBoundary.Outcome:
                            await journal.RecordItemOutcomeAsync(
                                fixture.Preview.ParentOperationId,
                                committed,
                                CancellationToken.None);
                            break;
                        case JournalSaveBoundary.Completed:
                            await journal.RecordCompletedAsync(
                                fixture.Preview.ParentOperationId,
                                completed,
                                CancellationToken.None);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(boundary));
                    }
                });

            SceneApplyJournalState unchanged =
                Assert.IsType<SceneApplyJournalState>(
                    await journal.LoadAsync(
                        fixture.Preview.ParentOperationId,
                        CancellationToken.None));
            Assert.Equal(expectedOpenStatus, unchanged.Items[0].Status);
            Assert.False(unchanged.IsCompleted);
            Assert.Equal(saveCountBeforeFailure + 1, payloadStore.SaveCount);
            await Assert.ThrowsAsync<SceneApplyStatePersistenceException>(
                async () =>
                await journal.CreateAsync(
                    fixture.Preview,
                    AcceptedAt,
                    CancellationToken.None));
            Assert.Equal(saveCountBeforeFailure + 1, payloadStore.SaveCount);
        }

        using PersistentSceneApplyJournal reopened =
            await PersistentSceneApplyJournal.OpenAsync(payloadStore);
        SceneApplyJournalState durable = Assert.IsType<SceneApplyJournalState>(
            await reopened.LoadAsync(
                fixture.Preview.ParentOperationId,
                CancellationToken.None));
        SceneApplyJournalItemStatus expectedDurableStatus = boundary switch
        {
            JournalSaveBoundary.Started =>
                SceneApplyJournalItemStatus.Started,
            JournalSaveBoundary.Outcome
            or JournalSaveBoundary.Completed =>
                SceneApplyJournalItemStatus.Terminal,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
        };
        Assert.Equal(expectedDurableStatus, durable.Items[0].Status);
        Assert.Equal(expectedDurableCompletion, durable.IsCompleted);
    }

    [Fact]
    public async Task StrictCodecRejectsTerminalItemWithoutResultEvidence()
    {
        Fixture fixture = CreateFixture();
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            await journal.CreateAsync(
                fixture.Preview,
                AcceptedAt,
                CancellationToken.None);
        }

        string original = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        string tampered = original.Replace(
            "\"status\":0",
            "\"status\":2",
            StringComparison.Ordinal);
        Assert.NotEqual(original, tampered);
        payloadStore.ReplacePayload(Encoding.UTF8.GetBytes(tampered));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSceneApplyJournal.OpenAsync(payloadStore));
    }

    [Fact]
    public async Task CommittedReplaceUndoReferenceRoundTripsWithoutSensitiveContent()
    {
        ReplaceFixture fixture = CreateReplaceFixture();
        var payloadStore = new InMemoryPayloadStore();
        var clock = new MutableClock(AcceptedAt);
        var port = new RecordingReplaceOperationPort(
            fixture.IncomingDescriptor,
            fixture.Target,
            clock);
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            var coordinator = new SceneApplyCoordinator(clock, journal, port);

            SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
                fixture.Scene,
                fixture.Preview,
                fixture.Approval,
                CancellationToken.None);

            Assert.Equal(SceneApplyOverallStatus.Completed, execution.Result?.Status);
            Assert.Equal(1, port.CallCount);
        }

        string plaintext = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        Assert.DoesNotContain(
            "replace-scene-name-canary",
            plaintext,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "replace-incoming-title-canary",
            plaintext,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "replace-incoming-payload-canary",
            plaintext,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "replace-target-title-canary",
            plaintext,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "replace-target-payload-canary",
            plaintext,
            StringComparison.Ordinal);

        using PersistentSceneApplyJournal reopened =
            await PersistentSceneApplyJournal.OpenAsync(payloadStore);
        SceneApplyJournalState restored = Assert.IsType<SceneApplyJournalState>(
            await reopened.LoadAsync(
                fixture.Preview.ParentOperationId,
                CancellationToken.None));
        UndoCapsuleReference undo = Assert.IsType<UndoCapsuleReference>(
            Assert.Single(restored.Items).Result?.UndoCapsule);

        Assert.Equal(fixture.Target.ActivityId, undo.TargetActivityId);
        Assert.Equal(fixture.Target.Revision, undo.ExpectedTargetRevision);
        Assert.Equal(
            fixture.Target.DescriptorDigest,
            undo.TargetDescriptorDigest);
        Assert.Equal(fixture.Preview.Items[0].ActivityId, undo.IncomingActivityId);
        Assert.Equal(
            fixture.IncomingDescriptor.DescriptorDigest,
            undo.IncomingDescriptorDigest);
    }

    [Fact]
    public async Task SourceLookupAndOccupancyBlockersRoundTripExactly()
    {
        BlockerFixture fixture = CreateBlockerFixture();
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            await journal.CreateAsync(
                fixture.Preview,
                AcceptedAt,
                CancellationToken.None);
        }

        using PersistentSceneApplyJournal reopened =
            await PersistentSceneApplyJournal.OpenAsync(payloadStore);
        SceneApplyJournalState restored = Assert.IsType<SceneApplyJournalState>(
            await reopened.LoadAsync(
                fixture.Preview.ParentOperationId,
                CancellationToken.None));
        SceneApplyItemPreview sourceBlocked = restored.Items[0].BoundItem;
        SceneApplyItemPreview occupancyBlocked = restored.Items[1].BoundItem;

        Assert.Equal(
            SceneApplyItemReason.SourceSelectionRequired,
            sourceBlocked.Reason);
        Assert.Equal(
            fixture.SourceLookup,
            Assert.IsType<SceneSourceLookup>(sourceBlocked.SourceLookup));
        Assert.Equal(SceneApplyItemReason.UndoUnavailable, occupancyBlocked.Reason);
        Assert.Equal(
            fixture.Occupancy,
            occupancyBlocked.Occupancy);
        Assert.Equal(
            fixture.Occupancy.Target,
            occupancyBlocked.ReplaceTarget);
    }

    [Fact]
    public async Task JournalCapsAtThirtyTwoAttemptsButReplaysKnownAttempt()
    {
        var payloadStore = new InMemoryPayloadStore();
        Fixture first = CreateFixture(attempt: 1);
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            for (int attempt = 1;
                 attempt <= PersistentSceneApplyJournal.MaximumAttemptCount;
                 attempt++)
            {
                Fixture fixture = CreateFixture(attempt);
                await journal.CreateAsync(
                    fixture.Preview,
                    AcceptedAt,
                    CancellationToken.None);
            }

            Assert.Equal(
                PersistentSceneApplyJournal.MaximumAttemptCount,
                journal.EntryCount);
            int saveCountAtCapacity = payloadStore.SaveCount;

            SceneApplyJournalState replayed = await journal.CreateAsync(
                first.Preview,
                AcceptedAt,
                CancellationToken.None);

            Assert.True(replayed.Matches(first.Preview));
            Assert.Equal(saveCountAtCapacity, payloadStore.SaveCount);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await journal.CreateAsync(
                    CreateFixture(
                        PersistentSceneApplyJournal.MaximumAttemptCount + 1)
                        .Preview,
                    AcceptedAt,
                    CancellationToken.None));
            Assert.Equal(saveCountAtCapacity, payloadStore.SaveCount);
        }

        using PersistentSceneApplyJournal reopened =
            await PersistentSceneApplyJournal.OpenAsync(payloadStore);
        Assert.Equal(
            PersistentSceneApplyJournal.MaximumAttemptCount,
            reopened.EntryCount);
    }

    [Fact]
    public async Task StrictCodecRejectsUnknownDuplicateNonCanonicalAndOutOfBoundFields()
    {
        Fixture fixture = CreateFixture();
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            await journal.CreateAsync(
                fixture.Preview,
                AcceptedAt,
                CancellationToken.None);
        }

        string original = Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(payloadStore.Payload));
        string acceptedAt = AcceptedAt.ToString(
            "O",
            CultureInfo.InvariantCulture);
        string[] tamperedPayloads =
        [
            ReplaceRequired(
                original,
                "\"formatVersion\":1",
                "\"formatVersion\":1,\"unexpected\":true"),
            ReplaceRequired(
                original,
                "\"formatVersion\":1",
                "\"formatVersion\":1,\"formatVersion\":1"),
            ReplaceRequired(
                original,
                "\"formatVersion\":1",
                "\"FormatVersion\":1"),
            ReplaceRequired(
                original,
                acceptedAt,
                "2026-07-26T02:00:00Z"),
            ReplaceRequired(
                original,
                "\"status\":0",
                "\"status\":99"),
            ReplaceRequired(
                original,
                "\"index\":0",
                "\"index\":64"),
        ];

        foreach (string tampered in tamperedPayloads)
        {
            payloadStore.ReplacePayload(Encoding.UTF8.GetBytes(tampered));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await PersistentSceneApplyJournal.OpenAsync(payloadStore));
        }
    }

    [Fact]
    public async Task StrictCodecRejectsNonCanonicalAttemptOrder()
    {
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneApplyJournal journal =
               await PersistentSceneApplyJournal.OpenAsync(payloadStore))
        {
            await journal.CreateAsync(
                CreateFixture(attempt: 1).Preview,
                AcceptedAt,
                CancellationToken.None);
            await journal.CreateAsync(
                CreateFixture(attempt: 2).Preview,
                AcceptedAt,
                CancellationToken.None);
        }

        JsonObject root = Assert.IsType<JsonObject>(
            JsonNode.Parse(Assert.IsType<byte[]>(payloadStore.Payload)));
        JsonArray attempts = Assert.IsType<JsonArray>(root["attempts"]);
        Assert.Equal(2, attempts.Count);
        JsonNode first = Assert.IsType<JsonObject>(attempts[0]).DeepClone();
        JsonNode second = Assert.IsType<JsonObject>(attempts[1]).DeepClone();
        attempts.Clear();
        attempts.Add(second);
        attempts.Add(first);
        payloadStore.ReplacePayload(Encoding.UTF8.GetBytes(root.ToJsonString()));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentSceneApplyJournal.OpenAsync(payloadStore));
    }

    [Fact]
    public async Task MutationApiRejectsSkippedAndUnstartedOperationOutcomes()
    {
        BlockerFixture blockers = CreateBlockerFixture();
        var blockerPayloadStore = new InMemoryPayloadStore();
        using (PersistentSceneApplyJournal blockerJournal =
               await PersistentSceneApplyJournal.OpenAsync(blockerPayloadStore))
        {
            await blockerJournal.CreateAsync(
                blockers.Preview,
                AcceptedAt,
                CancellationToken.None);
            SceneApplyItemResult skipped =
                SceneApplyItemResult.FromPreviewOnly(
                    blockers.Preview.Items[1],
                    AcceptedAt);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await blockerJournal.RecordItemOutcomeAsync(
                    blockers.Preview.ParentOperationId,
                    skipped,
                    CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await blockerJournal.RecordItemStartedAsync(
                    blockers.Preview.ParentOperationId,
                    0,
                    AcceptedAt,
                    CancellationToken.None));
        }

        Fixture fixture = CreateFixture();
        var operationPayloadStore = new InMemoryPayloadStore();
        using PersistentSceneApplyJournal operationJournal =
            await PersistentSceneApplyJournal.OpenAsync(operationPayloadStore);
        await operationJournal.CreateAsync(
            fixture.Preview,
            AcceptedAt,
            CancellationToken.None);
        SceneApplyItemPreview item = fixture.Preview.Items[0];
        SceneSourceSelection source =
            Assert.IsType<SceneSourceSelection>(item.Source);
        OperationReceipt receipt = OperationReceipt.Committed(
            item.ChildOperationId,
            item.ChildCorrelationId,
            OperationKind.Handoff,
            source.DeviceId,
            item.Destination.DeviceId,
            fixture.Descriptor,
            AcceptedAt);
        SceneApplyItemResult committed = SceneApplyItemResult.FromOperation(
            item,
            receipt,
            undoCapsule: null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await operationJournal.RecordItemOutcomeAsync(
                fixture.Preview.ParentOperationId,
                committed,
                CancellationToken.None));
    }

    private static Fixture CreateFixture(int attempt = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        DeviceId sourceDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityId activityId =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            activityId,
            kind,
            sourceDevice,
            "activity-title-canary",
            "{\"activity-payload-canary\":true}");
        SceneActivityPlan plan = SceneActivityPlan.Place(
            activityId,
            ActivityPlacement.On(targetDevice, "work"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "scene-name-canary",
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            activityId,
            7,
            descriptor.DescriptorDigest,
            kind,
            ActivityPlacement.On(sourceDevice, "source"));
        SceneApplyItemPreview item = SceneApplyItemPreview.TransferToEmpty(
            plan,
            source,
            OperationId.Parse(
                $"44444444-4444-4444-4444-{attempt:000000000000}"),
            CorrelationId.Parse(
                $"55555555-5555-5555-5555-{attempt:000000000000}"));
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse(
                $"66666666-6666-6666-6666-{attempt:000000000000}"),
            CorrelationId.Parse(
                $"77777777-7777-7777-7777-{attempt:000000000000}"),
            AcceptedAt.AddMinutes(-1),
            AcceptedAt.AddMinutes(4),
            [item]);
        return new Fixture(
            scene,
            preview,
            SceneApplyApproval.Create(
                preview.Fingerprint,
                preview.RequiredReplaceConfirmations),
            descriptor);
    }

    private static SceneApplyItemResult CreateCommittedHandoffResult(
        Fixture fixture)
    {
        SceneApplyItemPreview item = fixture.Preview.Items[0];
        SceneSourceSelection source =
            Assert.IsType<SceneSourceSelection>(item.Source);
        OperationReceipt receipt = OperationReceipt.Committed(
            item.ChildOperationId,
            item.ChildCorrelationId,
            OperationKind.Handoff,
            source.DeviceId,
            item.Destination.DeviceId,
            fixture.Descriptor,
            AcceptedAt);
        return SceneApplyItemResult.FromOperation(
            item,
            receipt,
            undoCapsule: null);
    }

    private static ReplaceFixture CreateReplaceFixture()
    {
        DeviceId sourceDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            kind,
            sourceDevice,
            "replace-incoming-title-canary",
            "{\"replace-incoming-payload-canary\":true}");
        ActivityDescriptor existing = ActivityDescriptor.Create(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            kind,
            targetDevice,
            "replace-target-title-canary",
            "{\"replace-target-payload-canary\":true}");
        ActivityPlacement destination =
            ActivityPlacement.On(targetDevice, "replace-target-slot-canary");
        SceneActivityPlan plan = SceneActivityPlan.Place(
            incoming.Id,
            destination,
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "replace-scene-name-canary",
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            incoming.Id,
            7,
            incoming.DescriptorDigest,
            kind,
            ActivityPlacement.On(sourceDevice, "replace-source-slot-canary"));
        SceneReplaceTargetSnapshot target = SceneReplaceTargetSnapshot.Create(
            existing.Id,
            9,
            existing.DescriptorDigest,
            kind,
            destination);
        SceneApplyItemPreview item = SceneApplyItemPreview.Replace(
            plan,
            source,
            target,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            AcceptedAt.AddMinutes(-1),
            AcceptedAt.AddMinutes(4),
            [item]);
        return new ReplaceFixture(
            scene,
            preview,
            SceneApplyApproval.Create(
                preview.Fingerprint,
                preview.RequiredReplaceConfirmations),
            incoming,
            target);
    }

    private static BlockerFixture CreateBlockerFixture()
    {
        DeviceId sourceDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId secondSourceDevice =
            DeviceId.Parse("12121212-1212-1212-1212-121212121212");
        DeviceId targetDevice =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        ActivityId lookupActivity =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityId occupancyActivity =
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        ActivityPlacement lookupDestination =
            ActivityPlacement.On(targetDevice, "lookup-destination");
        ActivityPlacement occupancyDestination =
            ActivityPlacement.On(targetDevice, "occupancy-destination");
        SceneActivityPlan lookupPlan = SceneActivityPlan.Place(
            lookupActivity,
            lookupDestination,
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        SceneActivityPlan occupancyPlan = SceneActivityPlan.Place(
            occupancyActivity,
            occupancyDestination,
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        SceneSourceLookup lookup = SceneSourceLookup.FromObservation(
            0,
            lookupActivity,
            [
                SceneSourceSelection.Create(
                    0,
                    lookupActivity,
                    2,
                    new string('A', 64),
                    kind,
                    ActivityPlacement.On(sourceDevice, "lookup-source-one")),
                SceneSourceSelection.Create(
                    0,
                    lookupActivity,
                    3,
                    new string('B', 64),
                    kind,
                    ActivityPlacement.On(
                        secondSourceDevice,
                        "lookup-source-two")),
            ],
            isComplete: true);
        SceneApplyItemPreview lookupBlocked =
            SceneApplyItemPreview.BlockedBySourceLookup(
                lookupPlan,
                lookup,
                OperationId.Parse("44444444-4444-4444-4444-444444444444"),
                CorrelationId.Parse(
                    "55555555-5555-5555-5555-555555555555"));
        SceneSourceSelection occupancySource = SceneSourceSelection.Create(
            1,
            occupancyActivity,
            4,
            new string('C', 64),
            kind,
            ActivityPlacement.On(sourceDevice, "occupancy-source"));
        SceneReplaceTargetSnapshot target = SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            5,
            new string('D', 64),
            kind,
            occupancyDestination);
        SceneSlotOccupancy occupancy = SceneSlotOccupancy.EligibleConflict(
            target,
            hasDurableUndoAvailability: false);
        SceneApplyItemPreview occupancyBlocked =
            SceneApplyItemPreview.BlockedByOccupancy(
                occupancyPlan,
                occupancySource,
                occupancy,
                OperationId.Parse("88888888-8888-8888-8888-888888888888"),
                CorrelationId.Parse(
                    "99999999-9999-9999-9999-999999999999"));
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "blocker-scene-name-canary",
            [lookupPlan, occupancyPlan]);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            AcceptedAt.AddMinutes(-1),
            AcceptedAt.AddMinutes(4),
            [lookupBlocked, occupancyBlocked]);
        return new BlockerFixture(scene, preview, lookup, occupancy);
    }

    private static string ReplaceRequired(
        string value,
        string oldValue,
        string newValue)
    {
        string replaced = value.Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);
        Assert.NotEqual(value, replaced);
        return replaced;
    }

    private sealed record Fixture(
        ScenePlan Scene,
        SceneApplyPreview Preview,
        SceneApplyApproval Approval,
        ActivityDescriptor Descriptor);

    private sealed record ReplaceFixture(
        ScenePlan Scene,
        SceneApplyPreview Preview,
        SceneApplyApproval Approval,
        ActivityDescriptor IncomingDescriptor,
        SceneReplaceTargetSnapshot Target);

    private sealed record BlockerFixture(
        ScenePlan Scene,
        SceneApplyPreview Preview,
        SceneSourceLookup SourceLookup,
        SceneSlotOccupancy Occupancy);

    public enum JournalSaveBoundary
    {
        Started,
        Outcome,
        Completed,
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class InMemoryPayloadStore : ISceneApplyStatePayloadStore
    {
        public byte[]? Payload { get; private set; }

        public bool FailAfterNextWrite { get; set; }

        public int SaveCount { get; private set; }

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
            Payload = payload.ToArray();
            SaveCount++;
            if (FailAfterNextWrite)
            {
                FailAfterNextWrite = false;
                throw new IOException(
                    "scene-apply-post-write-exception-canary");
            }

            return ValueTask.CompletedTask;
        }

        public void ReplacePayload(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            Payload = payload.ToArray();
        }
    }

    private sealed class RecordingOperationPort(
        ActivityDescriptor descriptor,
        MutableClock clock,
        bool failIfCalled) : ISceneActivityOperationPort
    {
        public int CallCount { get; private set; }

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (failIfCalled)
            {
                throw new InvalidOperationException(
                    "replayed-operation-exception-canary");
            }

            cancellationToken.ThrowIfCancellationRequested();
            SceneSourceSelection source =
                Assert.IsType<SceneSourceSelection>(preparation.Item.Source);
            OperationReceipt receipt = OperationReceipt.Committed(
                preparation.Item.ChildOperationId,
                preparation.Item.ChildCorrelationId,
                OperationKind.Handoff,
                source.DeviceId,
                preparation.Item.Destination.DeviceId,
                descriptor,
                clock.UtcNow);
            return ValueTask.FromResult(
                SceneActivityOperationResult.Create(receipt, undoCapsule: null));
        }
    }

    private sealed class RecordingReplaceOperationPort(
        ActivityDescriptor descriptor,
        SceneReplaceTargetSnapshot target,
        MutableClock clock) : ISceneActivityOperationPort
    {
        public int CallCount { get; private set; }

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            SceneApplyItemPreview item = preparation.Item;
            SceneSourceSelection source =
                Assert.IsType<SceneSourceSelection>(item.Source);
            Assert.Equal(target, item.ReplaceTarget);
            OperationReceipt receipt = OperationReceipt.Committed(
                item.ChildOperationId,
                item.ChildCorrelationId,
                OperationKind.Replace,
                source.DeviceId,
                item.Destination.DeviceId,
                descriptor,
                clock.UtcNow);
            var undo = new UndoCapsuleReference(
                UndoCapsuleId.Parse(
                    "10101010-1010-1010-1010-101010101010"),
                item.ChildOperationId,
                item.ChildCorrelationId,
                target.ActivityId,
                target.Revision,
                target.DescriptorDigest,
                item.ActivityId,
                source.DescriptorDigest,
                clock.UtcNow.AddHours(1));
            return ValueTask.FromResult(
                SceneActivityOperationResult.Create(receipt, undo));
        }
    }
}
