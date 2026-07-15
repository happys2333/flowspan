using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class PersistentReplaceStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StoredCapsuleRoundTripsAcrossStateStoreRestart()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = CreateCapsule();

        using (PersistentReplaceStateStore first =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            Assert.True(await first.TryAddCapsuleAsync(capsule));
            Assert.Equal(1, first.CapsuleCount);
        }

        using PersistentReplaceStateStore restarted =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);

        Assert.Equal(1, restarted.CapsuleCount);
        Assert.True(restarted.TryGetCapsule(capsule.Id, out UndoCapsule? restored));
        Assert.Equal(capsule, restored);
        Assert.Equal(capsule.Reference, restored.Reference);
    }

    [Fact]
    public async Task FailedPayloadSaveDoesNotPublishCapsuleInMemoryOrAfterRestart()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore
        {
            FailSaves = true,
        };
        UndoCapsule capsule = CreateCapsule();
        using PersistentReplaceStateStore first =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);

        ReplaceStatePersistenceException failure =
            await Assert.ThrowsAsync<ReplaceStatePersistenceException>(async () =>
            await first.TryAddCapsuleAsync(capsule));

        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal(0, first.CapsuleCount);
        Assert.False(first.TryGetCapsule(capsule.Id, out _));
        payloadStore.FailSaves = false;
        using PersistentReplaceStateStore restarted =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        Assert.Equal(0, restarted.CapsuleCount);
    }

    [Fact]
    public async Task CapsulePersistenceFailureRejectsBeforeIncomingResumeOrCatalogMutation()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore();
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        payloadStore.FailSaves = true;
        var catalog = new InMemoryActivityCatalog();
        UndoCapsule expectedCapsule = CreateCapsule();
        ActivityInstance original = expectedCapsule.OriginalActivity;
        ActivityDescriptor incoming = expectedCapsule.ReplacementActivity.Descriptor;
        Assert.True(catalog.TryAdd(original));
        var adapter = new CountingReplaceAdapter();
        using var endpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            catalog,
            new InMemoryOperationJournal(),
            new ActivityAdapterRegistry([adapter]),
            state,
            new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
            NullReceiptSink.Instance);
        endpoint.SetPeerGrant(source, CapabilityGrant.Of(Capability.ActivityReplace));

        ReplaceOperationResult result = await endpoint.ReplaceAsync(
            source,
            ReplaceActivityCommand.Create(
                OperationContext.Create(
                    expectedCapsule.OperationId,
                    expectedCapsule.CorrelationId,
                    Now.AddSeconds(30)),
                original.Descriptor.Id,
                original.Revision,
                original.Descriptor.DescriptorDigest,
                incoming,
                expectedCapsule.ReplacementActivity.Placement,
                expectedCapsule.ExpiresAt));

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.UndoUnavailable, result.Receipt.FailureCode);
        Assert.Null(result.UndoCapsule);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(0, adapter.ResumeCount);
        Assert.True(catalog.TryGet(original.Descriptor.Id, out ActivityInstance? preserved));
        Assert.Equal(original, preserved);
        Assert.Equal(0, state.CapsuleCount);
    }

    [Fact]
    public async Task CleanupSaveFailureAfterRejectedResumeRetainsCapsuleAndRecovers()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore
        {
            FailOnSaveAttempt = 3,
        };
        UndoCapsule expectedCapsule = CreateCapsule();
        var catalog = new InMemoryActivityCatalog();
        Assert.True(catalog.TryAdd(expectedCapsule.OriginalActivity));
        var adapter = new CountingReplaceAdapter(resumeSucceeds: false);
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        using var endpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            catalog,
            state,
            new ActivityAdapterRegistry([adapter]),
            state,
            new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
            NullReceiptSink.Instance);
        endpoint.SetPeerGrant(source, CapabilityGrant.Of(Capability.ActivityReplace));

        ReplaceOperationResult result = await endpoint.ReplaceAsync(
            source,
            CreateReplaceCommand(expectedCapsule));

        Assert.Equal(OperationStatus.Recovering, result.Receipt.Status);
        Assert.Equal(FailureCode.InternalFailure, result.Receipt.FailureCode);
        Assert.Null(result.UndoCapsule);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(1, adapter.ResumeCount);
        Assert.Equal(1, state.CapsuleCount);
        Assert.True(catalog.TryGet(expectedCapsule.OriginalActivity.Descriptor.Id, out _));

        ReplaceOperationResult replay = await endpoint.ReplaceAsync(
            source,
            CreateReplaceCommand(expectedCapsule));
        Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(1, adapter.ResumeCount);
    }

    [Fact]
    public async Task CatalogSwapFailureAfterResumeRetainsCapsuleAndRecovers()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule expectedCapsule = CreateCapsule();
        var catalog = new RejectingSwapCatalog();
        Assert.True(catalog.TryAdd(expectedCapsule.OriginalActivity));
        var adapter = new CountingReplaceAdapter();
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        using var endpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            catalog,
            state,
            new ActivityAdapterRegistry([adapter]),
            state,
            new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
            NullReceiptSink.Instance);
        endpoint.SetPeerGrant(source, CapabilityGrant.Of(Capability.ActivityReplace));

        ReplaceOperationResult result = await endpoint.ReplaceAsync(
            source,
            CreateReplaceCommand(expectedCapsule));

        Assert.Equal(OperationStatus.Recovering, result.Receipt.Status);
        Assert.Equal(FailureCode.InternalFailure, result.Receipt.FailureCode);
        Assert.Null(result.UndoCapsule);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(1, adapter.ResumeCount);
        Assert.Equal(1, state.CapsuleCount);
        Assert.True(catalog.TryGet(expectedCapsule.OriginalActivity.Descriptor.Id, out _));

        ReplaceOperationResult replay = await endpoint.ReplaceAsync(
            source,
            CreateReplaceCommand(expectedCapsule));
        Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(1, adapter.ResumeCount);
    }

    [Fact]
    public async Task CompletedReplaceReplaysAcrossStateStoreRestartWithoutAdapterWork()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule expectedCapsule = CreateCapsule();
        ReplaceActivityCommand command = CreateReplaceCommand(expectedCapsule);
        var firstCatalog = new InMemoryActivityCatalog();
        Assert.True(firstCatalog.TryAdd(expectedCapsule.OriginalActivity));
        var firstAdapter = new CountingReplaceAdapter();
        ReplaceOperationResult firstResult;
        ActivityInstance replacement;
        using (PersistentReplaceStateStore firstState =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        using (var firstEndpoint = new ReplaceEndpoint(
                   target,
                   new TestClock(Now),
                   firstCatalog,
                   firstState,
                   new ActivityAdapterRegistry([firstAdapter]),
                   firstState,
                   new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
                   NullReceiptSink.Instance))
        {
            firstEndpoint.SetPeerGrant(
                source,
                CapabilityGrant.Of(Capability.ActivityReplace));
            firstResult = await firstEndpoint.ReplaceAsync(source, command);
            Assert.True(firstCatalog.TryGet(
                expectedCapsule.ReplacementActivity.Descriptor.Id,
                out ActivityInstance? committedReplacement));
            replacement = Assert.IsType<ActivityInstance>(committedReplacement);
        }

        var restartedCatalog = new InMemoryActivityCatalog();
        Assert.True(restartedCatalog.TryAdd(replacement));
        var restartedAdapter = new CountingReplaceAdapter();
        using PersistentReplaceStateStore restartedState =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        using var restartedEndpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            restartedCatalog,
            restartedState,
            new ActivityAdapterRegistry([restartedAdapter]),
            restartedState,
            new DeterministicUndoCapsuleIdSource(Array.Empty<UndoCapsuleId>()),
            NullReceiptSink.Instance);
        restartedEndpoint.SetPeerGrant(
            source,
            CapabilityGrant.Of(Capability.ActivityReplace));

        ReplaceOperationResult replay = await restartedEndpoint.ReplaceAsync(source, command);

        Assert.Equal(firstResult.Receipt, replay.Receipt);
        Assert.Equal(firstResult.UndoCapsule, replay.UndoCapsule);
        Assert.Equal(OperationStatus.Committed, replay.Receipt.Status);
        Assert.Equal(0, restartedAdapter.CaptureCount);
        Assert.Equal(0, restartedAdapter.ResumeCount);
        Assert.Equal(1, restartedState.CapsuleCount);
        Assert.Equal(1, restartedCatalog.Count);
    }

    [Fact]
    public async Task PendingReplaceAfterTerminalSaveFailureRecoversWithoutDuplicateWork()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore
        {
            FailOnSaveAttempt = 3,
        };
        UndoCapsule expectedCapsule = CreateCapsule();
        ReplaceActivityCommand command = CreateReplaceCommand(expectedCapsule);
        var firstCatalog = new InMemoryActivityCatalog();
        Assert.True(firstCatalog.TryAdd(expectedCapsule.OriginalActivity));
        var firstAdapter = new CountingReplaceAdapter();
        ReplaceOperationResult firstResult;
        ActivityInstance replacement;
        using (PersistentReplaceStateStore firstState =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        using (var firstEndpoint = new ReplaceEndpoint(
                   target,
                   new TestClock(Now),
                   firstCatalog,
                   firstState,
                   new ActivityAdapterRegistry([firstAdapter]),
                   firstState,
                   new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
                   NullReceiptSink.Instance))
        {
            firstEndpoint.SetPeerGrant(
                source,
                CapabilityGrant.Of(Capability.ActivityReplace));
            firstResult = await firstEndpoint.ReplaceAsync(source, command);
            Assert.True(firstCatalog.TryGet(
                expectedCapsule.ReplacementActivity.Descriptor.Id,
                out ActivityInstance? committedReplacement));
            replacement = Assert.IsType<ActivityInstance>(committedReplacement);
        }

        Assert.Equal(OperationStatus.Recovering, firstResult.Receipt.Status);
        Assert.Equal(FailureCode.OperationInProgress, firstResult.Receipt.FailureCode);
        Assert.Null(firstResult.UndoCapsule);
        Assert.Equal(1, firstAdapter.CaptureCount);
        Assert.Equal(1, firstAdapter.ResumeCount);
        Assert.Equal(3, payloadStore.SaveAttemptCount);

        var restartedCatalog = new InMemoryActivityCatalog();
        Assert.True(restartedCatalog.TryAdd(replacement));
        var restartedAdapter = new CountingReplaceAdapter();
        using PersistentReplaceStateStore restartedState =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        using var restartedEndpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            restartedCatalog,
            restartedState,
            new ActivityAdapterRegistry([restartedAdapter]),
            restartedState,
            new DeterministicUndoCapsuleIdSource(Array.Empty<UndoCapsuleId>()),
            NullReceiptSink.Instance);
        restartedEndpoint.SetPeerGrant(
            source,
            CapabilityGrant.Of(Capability.ActivityReplace));

        ReplaceOperationResult replay = await restartedEndpoint.ReplaceAsync(source, command);

        Assert.Equal(OperationStatus.Recovering, replay.Receipt.Status);
        Assert.Equal(FailureCode.OperationInProgress, replay.Receipt.FailureCode);
        Assert.Null(replay.UndoCapsule);
        Assert.Equal(0, restartedAdapter.CaptureCount);
        Assert.Equal(0, restartedAdapter.ResumeCount);
        Assert.Equal(1, restartedState.CapsuleCount);
        Assert.Equal(1, restartedCatalog.Count);
    }

    [Fact]
    public async Task CompletedUndoReplaysAcrossStateStoreRestartWithoutRestore()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule expectedCapsule = CreateCapsule();
        ReplaceActivityCommand command = CreateReplaceCommand(expectedCapsule);
        OperationContext undoContext = OperationContext.Create(
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            Now.AddSeconds(30));
        var firstCatalog = new InMemoryActivityCatalog();
        Assert.True(firstCatalog.TryAdd(expectedCapsule.OriginalActivity));
        var firstAdapter = new CountingReplaceAdapter();
        UndoReplaceResult firstUndo;
        ActivityInstance restored;
        using (PersistentReplaceStateStore firstState =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        using (var firstEndpoint = new ReplaceEndpoint(
                   target,
                   new TestClock(Now),
                   firstCatalog,
                   firstState,
                   new ActivityAdapterRegistry([firstAdapter]),
                   firstState,
                   new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
                   NullReceiptSink.Instance))
        {
            firstEndpoint.SetPeerGrant(
                source,
                CapabilityGrant.Of(Capability.ActivityReplace));
            ReplaceOperationResult replaced = await firstEndpoint.ReplaceAsync(
                source,
                command);
            UndoCapsuleReference reference =
                Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);
            firstUndo = await firstEndpoint.UndoReplaceAsync(
                reference.Id,
                undoContext);
            Assert.True(firstCatalog.TryGet(
                expectedCapsule.OriginalActivity.Descriptor.Id,
                out ActivityInstance? committedRestore));
            restored = Assert.IsType<ActivityInstance>(committedRestore);
        }

        Assert.Equal(OperationStatus.Committed, firstUndo.Status);
        Assert.Equal(1, firstAdapter.RestoreCount);

        var restartedCatalog = new InMemoryActivityCatalog();
        Assert.True(restartedCatalog.TryAdd(restored));
        var restartedAdapter = new CountingReplaceAdapter();
        using PersistentReplaceStateStore restartedState =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        using var restartedEndpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            restartedCatalog,
            restartedState,
            new ActivityAdapterRegistry([restartedAdapter]),
            restartedState,
            new DeterministicUndoCapsuleIdSource(Array.Empty<UndoCapsuleId>()),
            NullReceiptSink.Instance);

        UndoReplaceResult replay = await restartedEndpoint.UndoReplaceAsync(
            expectedCapsule.Id,
            undoContext);

        Assert.Equal(firstUndo, replay);
        Assert.Equal(OperationStatus.Committed, replay.Status);
        Assert.Equal(0, restartedAdapter.RestoreCount);
        Assert.Equal(1, restartedCatalog.Count);

        UndoReplaceResult consumed = await restartedEndpoint.UndoReplaceAsync(
            expectedCapsule.Id,
            OperationContext.Create(
                OperationId.Parse("12121212-1212-1212-1212-121212121212"),
                CorrelationId.Parse("34343434-3434-3434-3434-343434343434"),
                Now.AddSeconds(30)));
        Assert.Equal(OperationStatus.Rejected, consumed.Status);
        Assert.Equal(FailureCode.UndoCapsuleConsumed, consumed.FailureCode);
        Assert.Equal(0, restartedAdapter.RestoreCount);
    }

    [Fact]
    public async Task PendingUndoAfterTerminalSaveFailureRecoversWithoutDuplicateRestore()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var payloadStore = new MemoryReplaceStatePayloadStore
        {
            FailOnSaveAttempt = 5,
        };
        UndoCapsule expectedCapsule = CreateCapsule();
        ReplaceActivityCommand command = CreateReplaceCommand(expectedCapsule);
        OperationContext undoContext = OperationContext.Create(
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            Now.AddSeconds(30));
        var firstCatalog = new InMemoryActivityCatalog();
        Assert.True(firstCatalog.TryAdd(expectedCapsule.OriginalActivity));
        var firstAdapter = new CountingReplaceAdapter();
        UndoReplaceResult firstUndo;
        ActivityInstance restored;
        using (PersistentReplaceStateStore firstState =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        using (var firstEndpoint = new ReplaceEndpoint(
                   target,
                   new TestClock(Now),
                   firstCatalog,
                   firstState,
                   new ActivityAdapterRegistry([firstAdapter]),
                   firstState,
                   new DeterministicUndoCapsuleIdSource([expectedCapsule.Id]),
                   NullReceiptSink.Instance))
        {
            firstEndpoint.SetPeerGrant(
                source,
                CapabilityGrant.Of(Capability.ActivityReplace));
            ReplaceOperationResult replaced = await firstEndpoint.ReplaceAsync(
                source,
                command);
            UndoCapsuleReference reference =
                Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);
            firstUndo = await firstEndpoint.UndoReplaceAsync(
                reference.Id,
                undoContext);
            Assert.True(firstCatalog.TryGet(
                expectedCapsule.OriginalActivity.Descriptor.Id,
                out ActivityInstance? committedRestore));
            restored = Assert.IsType<ActivityInstance>(committedRestore);
        }

        Assert.Equal(OperationStatus.Recovering, firstUndo.Status);
        Assert.Equal(FailureCode.InternalFailure, firstUndo.FailureCode);
        Assert.Equal(1, firstAdapter.RestoreCount);
        Assert.Equal(5, payloadStore.SaveAttemptCount);

        var restartedCatalog = new InMemoryActivityCatalog();
        Assert.True(restartedCatalog.TryAdd(restored));
        var restartedAdapter = new CountingReplaceAdapter();
        using PersistentReplaceStateStore restartedState =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        using var restartedEndpoint = new ReplaceEndpoint(
            target,
            new TestClock(Now),
            restartedCatalog,
            restartedState,
            new ActivityAdapterRegistry([restartedAdapter]),
            restartedState,
            new DeterministicUndoCapsuleIdSource(Array.Empty<UndoCapsuleId>()),
            NullReceiptSink.Instance);

        UndoReplaceResult replay = await restartedEndpoint.UndoReplaceAsync(
            expectedCapsule.Id,
            undoContext);

        Assert.Equal(OperationStatus.Recovering, replay.Status);
        Assert.Equal(FailureCode.OperationInProgress, replay.FailureCode);
        Assert.Equal(0, restartedAdapter.RestoreCount);
        Assert.Equal(1, restartedCatalog.Count);
    }

    [Fact]
    public async Task ExpiryCleanupDurablyRemovesCapsuleAndCompletedReplaceReplay()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = CreateCapsule();
        ReplaceActivityCommand command = CreateReplaceCommand(capsule);
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            JournalExecutionResult journal = await state.ExecuteOnceAsync(
                capsule.OperationId,
                command.BindAuthenticatedSender(capsule.SourceDeviceId),
                _ => ValueTask.FromResult(OperationReceipt.Committed(
                    capsule.OperationId,
                    capsule.CorrelationId,
                    OperationKind.Replace,
                    capsule.SourceDeviceId,
                    capsule.TargetDeviceId,
                    capsule.ReplacementActivity.Descriptor,
                    Now)),
                CancellationToken.None);
            Assert.NotNull(journal.Receipt);
            Assert.True(await state.TryAddCapsuleAsync(capsule));

            int removed = await state.RemoveExpiredCapsulesAsync(
                capsule.ExpiresAt.AddTicks(1));

            Assert.Equal(1, removed);
            Assert.Equal(0, state.CapsuleCount);
        }

        using PersistentReplaceStateStore restarted =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        Assert.Equal(0, restarted.CapsuleCount);
        int executionCount = 0;
        JournalExecutionResult retried = await restarted.ExecuteOnceAsync(
            capsule.OperationId,
            command.BindAuthenticatedSender(capsule.SourceDeviceId),
            _ =>
            {
                executionCount++;
                return ValueTask.FromResult(OperationReceipt.Rejected(
                    capsule.OperationId,
                    capsule.CorrelationId,
                    OperationKind.Replace,
                    capsule.SourceDeviceId,
                    capsule.TargetDeviceId,
                    capsule.ReplacementActivity.Descriptor,
                    capsule.ExpiresAt.AddTicks(1),
                    FailureCode.DeadlineExpired));
            },
            CancellationToken.None);
        Assert.Equal(1, executionCount);
        Assert.False(retried.WasReplay);
        Assert.Equal(FailureCode.DeadlineExpired, retried.Receipt?.FailureCode);
    }

    [Fact]
    public async Task ExpiryCleanupPreservesCapsuleReservedByPendingUndo()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = CreateCapsule();
        OperationId undoOperation =
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            Assert.True(await state.TryAddCapsuleAsync(capsule));
            UndoJournalPreparation prepared = await state.PrepareUndoAsync(
                capsule.Id,
                undoOperation,
                new string('A', 64));
            Assert.Equal(UndoJournalPreparationStatus.Prepared, prepared.Status);

            int removed = await state.RemoveExpiredCapsulesAsync(
                capsule.ExpiresAt.AddTicks(1));

            Assert.Equal(0, removed);
            Assert.Equal(1, state.CapsuleCount);
        }

        using PersistentReplaceStateStore restarted =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        Assert.Equal(1, restarted.CapsuleCount);
        UndoJournalPreparation recovery = await restarted.PrepareUndoAsync(
            capsule.Id,
            undoOperation,
            new string('A', 64));
        Assert.Equal(
            UndoJournalPreparationStatus.RecoveryRequired,
            recovery.Status);
    }

    [Fact]
    public async Task CanonicalPayloadTamperFailsClosedOnDescriptorDigestMismatch()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = CreateCapsule();
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            Assert.True(await state.TryAddCapsuleAsync(capsule));
        }

        payloadStore.ReplaceUtf8("keep me", "lose me");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PersistentReplaceStateStore.OpenAsync(payloadStore));
    }

    [Fact]
    public async Task ConcurrentPersistentJournalRetryExecutesHandlerOnce()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        UndoCapsule capsule = CreateCapsule();
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;

        Task<JournalExecutionResult> first = state.ExecuteOnceAsync(
            capsule.OperationId,
            new string('B', 64),
            ExecuteAsync,
            CancellationToken.None).AsTask();
        await started.Task;
        Task<JournalExecutionResult> retry = state.ExecuteOnceAsync(
            capsule.OperationId,
            new string('B', 64),
            ExecuteAsync,
            CancellationToken.None).AsTask();
        release.TrySetResult(true);

        JournalExecutionResult[] results = await Task.WhenAll(first, retry);

        Assert.Equal(1, executions);
        Assert.False(results[0].WasReplay);
        Assert.True(results[1].WasReplay);
        Assert.Equal(results[0].Receipt, results[1].Receipt);

        async ValueTask<OperationReceipt> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            executions++;
            started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return OperationReceipt.Committed(
                capsule.OperationId,
                capsule.CorrelationId,
                OperationKind.Replace,
                capsule.SourceDeviceId,
                capsule.TargetDeviceId,
                capsule.ReplacementActivity.Descriptor,
                Now);
        }
    }

    private static UndoCapsule CreateCapsule()
    {
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityInstance original = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                target,
                "Original note",
                "{\"text\":\"keep me\"}"),
            ActivityPlacement.On(target, "main"),
            revision: 7);
        ActivityInstance replacement = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                ActivityKind.Parse("workspace.note/v1"),
                source,
                "Incoming note",
                "{\"text\":\"replace with me\"}"),
            ActivityPlacement.On(target, "main"),
            revision: 8);
        return UndoCapsule.Create(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            source,
            target,
            original,
            replacement,
            Now,
            Now.AddMinutes(10));
    }

    private static ReplaceActivityCommand CreateReplaceCommand(UndoCapsule capsule) =>
        ReplaceActivityCommand.Create(
            OperationContext.Create(
                capsule.OperationId,
                capsule.CorrelationId,
                Now.AddSeconds(30)),
            capsule.OriginalActivity.Descriptor.Id,
            capsule.OriginalActivity.Revision,
            capsule.OriginalActivity.Descriptor.DescriptorDigest,
            capsule.ReplacementActivity.Descriptor,
            capsule.ReplacementActivity.Placement,
            capsule.ExpiresAt);

    private sealed class MemoryReplaceStatePayloadStore : IReplaceStatePayloadStore
    {
        private byte[]? payload;

        public bool FailSaves { get; set; }

        public int? FailOnSaveAttempt { get; init; }

        public int SaveAttemptCount { get; private set; }

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveAttemptCount++;
            if (FailSaves || SaveAttemptCount == FailOnSaveAttempt)
            {
                throw new IOException("Injected Replace state save failure.");
            }

            payload = value.ToArray();
            return ValueTask.CompletedTask;
        }

        public void ReplaceUtf8(string existing, string replacement)
        {
            if (existing.Length != replacement.Length || payload is null)
            {
                throw new InvalidOperationException(
                    "A deterministic payload replacement requires equal text lengths and stored state.");
            }

            string json = System.Text.Encoding.UTF8.GetString(payload);
            string tampered = json.Replace(
                existing,
                replacement,
                StringComparison.Ordinal);
            if (StringComparer.Ordinal.Equals(json, tampered))
            {
                throw new InvalidOperationException(
                    "The requested payload text was not present.");
            }

            payload = System.Text.Encoding.UTF8.GetBytes(tampered);
        }
    }

    private sealed class CountingReplaceAdapter(bool resumeSucceeds = true) :
        IReplaceActivityAdapter
    {
        public ActivityKind Kind { get; } = ActivityKind.Parse("workspace.note/v1");

        public int CaptureCount { get; private set; }

        public int ResumeCount { get; private set; }

        public int RestoreCount { get; private set; }

        public ValueTask<CaptureUndoResult> CaptureUndoAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCount++;
            return ValueTask.FromResult(CaptureUndoResult.Success(activity.Descriptor));
        }

        public ValueTask<ResumeActivityResult> ResumeAsync(
            ActivityDescriptor descriptor,
            ActivityPlacement placement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCount++;
            return ValueTask.FromResult(resumeSucceeds
                ? ResumeActivityResult.Success
                : ResumeActivityResult.Rejected(FailureCode.DescriptorRejected));
        }

        public ValueTask<CloseActivityResult> CloseAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CloseActivityResult.Success);
        }

        public ValueTask<RestoreActivityResult> RestoreAsync(
            UndoCapsule capsule,
            ActivityPlacement placement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            return ValueTask.FromResult(RestoreActivityResult.Success);
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RejectingSwapCatalog : IActivityCatalog
    {
        private readonly InMemoryActivityCatalog inner = new();

        public bool TryGet(
            ActivityId activityId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out ActivityInstance? activity) => inner.TryGet(activityId, out activity);

        public bool TryAdd(ActivityInstance activity) => inner.TryAdd(activity);

        public bool TryUpdate(
            ActivityInstance expected,
            ActivityInstance replacement) => inner.TryUpdate(expected, replacement);

        public bool TrySwapReplace(
            ActivityInstance expected,
            ActivityInstance replacement) => false;
    }
}
