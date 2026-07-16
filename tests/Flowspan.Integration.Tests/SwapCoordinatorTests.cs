using System.Diagnostics.CodeAnalysis;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SwapCoordinatorTests
{
    [Fact]
    public async Task HappyPathExchangesBothPlacements()
    {
        Fixture fixture = new();

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            new DirectSwapEndpointChannel(fixture.SecondEndpoint));

        Assert.True(result.IsSuccess);
        fixture.AssertSwapped();
        Assert.True(fixture.Transactions.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Commit, transaction.Decision?.Outcome);
    }

    [Fact]
    public async Task PrepareFailureAbortsPreparedEndpointAndChangesNeitherActivity()
    {
        Fixture fixture = new();
        var rejectedSecond = new RejectPrepareChannel(
            fixture.SecondEndpoint,
            FailureCode.RevisionConflict);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            rejectedSecond);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.RevisionConflict, result.FailureCode);
        fixture.AssertOriginals();
        Assert.True(fixture.FirstEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out SwapReservation? reservation));
        Assert.Equal(SwapReservationPhase.Aborted, reservation.Phase);
        Assert.True(fixture.Transactions.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Abort, transaction.Decision?.Outcome);
    }

    [Fact]
    public async Task LostSecondPrepareAcknowledgementExplicitlyAbortsBothReservations()
    {
        Fixture fixture = new();
        var firstChannel = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
        var secondChannel = new DeterministicSwapEndpointChannel(
            fixture.SecondEndpoint,
            [ActivityDeliveryFault.DropAcknowledgement],
            []);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.AcknowledgementLost, result.FailureCode);
        fixture.AssertOriginals();
        Assert.True(fixture.FirstEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out SwapReservation? firstReservation));
        Assert.True(fixture.SecondEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out SwapReservation? secondReservation));
        Assert.Equal(SwapReservationPhase.Aborted, firstReservation.Phase);
        Assert.Equal(SwapReservationPhase.Aborted, secondReservation.Phase);
        Assert.True(fixture.Transactions.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Abort, transaction.Decision?.Outcome);
        Assert.Equal(2, transaction.Decision?.ReservationTokens.Length);
    }

    [Fact]
    public async Task DroppedCommitDeliveryRecoversFromDurableDecision()
    {
        Fixture fixture = new();
        var firstChannel = new DeterministicSwapEndpointChannel(
            fixture.FirstEndpoint,
            [ActivityDeliveryFault.DropBeforeDelivery, ActivityDeliveryFault.None]);
        var secondChannel = new DirectSwapEndpointChannel(fixture.SecondEndpoint);

        SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Recovering, uncertain.Status);
        Assert.Equal(FailureCode.PeerUnavailable, uncertain.FailureCode);
        Assert.True(fixture.FirstEndpoint.TryGetActivity(
            fixture.FirstActivity.Descriptor.Id,
            out _));
        Assert.True(fixture.SecondEndpoint.TryGetActivity(
            fixture.FirstActivity.Descriptor.Id,
            out _));

        SwapCoordinatorResult recovered = await fixture.Coordinator.RecoverAsync(
            fixture.Context.OperationId,
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task LostCommitAcknowledgementIsRecoveringEvenIfBothApplied()
    {
        Fixture fixture = new();
        var firstChannel = new DeterministicSwapEndpointChannel(
            fixture.FirstEndpoint,
            [ActivityDeliveryFault.DropAcknowledgement, ActivityDeliveryFault.None]);
        var secondChannel = new DirectSwapEndpointChannel(fixture.SecondEndpoint);

        SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Recovering, uncertain.Status);
        Assert.Equal(FailureCode.AcknowledgementLost, uncertain.FailureCode);
        fixture.AssertSwapped();

        SwapCoordinatorResult recovered = await fixture.Coordinator.RecoverAsync(
            fixture.Context.OperationId,
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task DuplicateCommitDeliveryIsIdempotent()
    {
        Fixture fixture = new();
        var firstChannel = new DeterministicSwapEndpointChannel(
            fixture.FirstEndpoint,
            [ActivityDeliveryFault.DuplicateDelivery]);
        var secondChannel = new DeterministicSwapEndpointChannel(
            fixture.SecondEndpoint,
            [ActivityDeliveryFault.DuplicateDelivery]);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, result.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task ExecuteRetryUsesRecordedDecisionWithoutNewTokens()
    {
        Fixture fixture = new();
        var firstChannel = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
        var secondChannel = new DirectSwapEndpointChannel(fixture.SecondEndpoint);
        await fixture.ExecuteAsync(firstChannel, secondChannel);

        SwapCoordinatorResult replay = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, replay.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task IntentSaveFailureTouchesNeitherEndpoint()
    {
        var payloadStore = new TestSwapStatePayloadStore { FailNextSave = true };
        using PersistentSwapTransactionJournal transactions =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        Fixture fixture = new(transactions);
        var first = new CountingChannel(fixture.FirstEndpoint);
        var second = new CountingChannel(fixture.SecondEndpoint);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(first, second);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(FailureCode.InternalFailure, result.FailureCode);
        Assert.Equal(0, first.PrepareCount);
        Assert.Equal(0, second.PrepareCount);
        Assert.Equal(0, first.DecisionCount);
        Assert.Equal(0, second.DecisionCount);
        fixture.AssertOriginals();
    }

    [Fact]
    public async Task DecisionSaveFailureRequiresReloadThenRecoveryAborts()
    {
        var payloadStore = new TestSwapStatePayloadStore { FailOnSaveAttempt = 2 };
        Fixture fixture;
        DirectSwapEndpointChannel first;
        DirectSwapEndpointChannel second;
        using (PersistentSwapTransactionJournal transactions =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            fixture = new Fixture(transactions);
            first = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
            second = new DirectSwapEndpointChannel(fixture.SecondEndpoint);

            SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(first, second);

            Assert.Equal(OperationStatus.Recovering, uncertain.Status);
            Assert.Equal(FailureCode.InternalFailure, uncertain.FailureCode);
            fixture.AssertOriginals();
            Assert.True(fixture.FirstEndpoint.TryGetReservation(
                fixture.Context.OperationId,
                out SwapReservation? firstPrepared));
            Assert.True(fixture.SecondEndpoint.TryGetReservation(
                fixture.Context.OperationId,
                out SwapReservation? secondPrepared));
            Assert.Equal(SwapReservationPhase.Prepared, firstPrepared.Phase);
            Assert.Equal(SwapReservationPhase.Prepared, secondPrepared.Phase);

            SwapCoordinatorResult blocked = await fixture.ExecuteAsync(first, second);
            Assert.Equal(OperationStatus.Recovering, blocked.Status);
            Assert.Equal(FailureCode.InternalFailure, blocked.FailureCode);
        }

        using PersistentSwapTransactionJournal restarted =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        var recoveredCoordinator = new SwapCoordinator(
            fixture.FirstEndpoint.DeviceId,
            new TestClock(Fixture.Now),
            restarted,
            new DeterministicSwapTokenSource([]));
        SwapCoordinatorResult recovered = await recoveredCoordinator.RecoverAsync(
            fixture.Context.OperationId,
            first,
            second);

        Assert.Equal(OperationStatus.Rejected, recovered.Status);
        Assert.Equal(FailureCode.OperationInProgress, recovered.FailureCode);
        fixture.AssertOriginals();
        Assert.True(restarted.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Abort, transaction.Decision?.Outcome);
    }

    [Fact]
    public async Task AmbiguousPostWriteFailureReloadsCommittedDecisionWithoutAbortOverwrite()
    {
        var payloadStore = new TestSwapStatePayloadStore
        {
            FailAfterWriteOnSaveAttempt = 2,
        };
        Fixture fixture;
        DirectSwapEndpointChannel first;
        DirectSwapEndpointChannel second;
        using (PersistentSwapTransactionJournal firstJournal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            fixture = new Fixture(firstJournal);
            first = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
            second = new DirectSwapEndpointChannel(fixture.SecondEndpoint);

            SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(first, second);
            SwapCoordinatorResult blocked = await fixture.ExecuteAsync(first, second);

            Assert.Equal(OperationStatus.Recovering, uncertain.Status);
            Assert.Equal(FailureCode.InternalFailure, uncertain.FailureCode);
            Assert.Equal(OperationStatus.Recovering, blocked.Status);
            fixture.AssertOriginals();
        }

        using PersistentSwapTransactionJournal restarted =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        Assert.True(restarted.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Commit, transaction.Decision?.Outcome);
        var recoveredCoordinator = new SwapCoordinator(
            fixture.FirstEndpoint.DeviceId,
            new TestClock(Fixture.Now),
            restarted,
            new DeterministicSwapTokenSource([]));

        SwapCoordinatorResult recovered = await recoveredCoordinator.RecoverAsync(
            fixture.Context.OperationId,
            first,
            second);

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task ReconstructedUndecidedIntentCanOnlyRecoverToAbort()
    {
        var payloadStore = new TestSwapStatePayloadStore();
        Fixture fixture;
        using (PersistentSwapTransactionJournal firstJournal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            fixture = new Fixture(firstJournal);
            var first = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
            var throwingSecond = new ThrowPrepareChannel(fixture.SecondEndpoint);

            SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(
                first,
                throwingSecond);

            Assert.Equal(OperationStatus.Recovering, uncertain.Status);
            Assert.Equal(FailureCode.InternalFailure, uncertain.FailureCode);
            Assert.True(fixture.FirstEndpoint.TryGetReservation(
                fixture.Context.OperationId,
                out SwapReservation? reservation));
            Assert.Equal(SwapReservationPhase.Prepared, reservation.Phase);
        }

        using PersistentSwapTransactionJournal restarted =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        var recoveredCoordinator = new SwapCoordinator(
            fixture.FirstEndpoint.DeviceId,
            new TestClock(Fixture.Now),
            restarted,
            new DeterministicSwapTokenSource([]));

        SwapCoordinatorResult recovered = await recoveredCoordinator.RecoverAsync(
            fixture.Context.OperationId,
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            new DirectSwapEndpointChannel(fixture.SecondEndpoint));

        Assert.Equal(OperationStatus.Rejected, recovered.Status);
        Assert.Equal(FailureCode.OperationInProgress, recovered.FailureCode);
        fixture.AssertOriginals();
        Assert.True(restarted.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Abort, transaction.Decision?.Outcome);
    }

    [Fact]
    public async Task DurableCommitDecisionRecoversAfterCoordinatorReconstruction()
    {
        var payloadStore = new TestSwapStatePayloadStore();
        Fixture fixture;
        DeterministicSwapEndpointChannel firstChannel;
        using (PersistentSwapTransactionJournal firstJournal =
               await PersistentSwapTransactionJournal.OpenAsync(payloadStore))
        {
            fixture = new Fixture(firstJournal);
            firstChannel = new DeterministicSwapEndpointChannel(
                fixture.FirstEndpoint,
                [ActivityDeliveryFault.DropBeforeDelivery]);

            SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(
                firstChannel,
                new DirectSwapEndpointChannel(fixture.SecondEndpoint));

            Assert.Equal(OperationStatus.Recovering, uncertain.Status);
            Assert.Equal(FailureCode.PeerUnavailable, uncertain.FailureCode);
        }

        using PersistentSwapTransactionJournal restarted =
            await PersistentSwapTransactionJournal.OpenAsync(payloadStore);
        var recoveredCoordinator = new SwapCoordinator(
            fixture.FirstEndpoint.DeviceId,
            new TestClock(Fixture.Now),
            restarted,
            new DeterministicSwapTokenSource([]));

        SwapCoordinatorResult recovered = await recoveredCoordinator.RecoverAsync(
            fixture.Context.OperationId,
            firstChannel,
            new DirectSwapEndpointChannel(fixture.SecondEndpoint));

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task AbortBeforePrepareCreatesIdempotentTombstone()
    {
        Fixture fixture = new();
        SwapCoordinatorTransaction transaction = SwapCoordinatorTransaction.Create(
            fixture.Context,
            fixture.FirstActivity,
            Fixture.FirstToken,
            fixture.SecondActivity,
            Fixture.SecondToken);
        SwapDecision abort = transaction.CreateDecision(
            SwapDecisionOutcome.Abort,
            Fixture.Now.AddSeconds(1),
            FailureCode.PeerUnavailable);
        var command = new SwapPrepareCommand(
            fixture.Context.OperationId,
            fixture.Context.CorrelationId,
            Fixture.FirstToken,
            fixture.FirstActivity,
            fixture.SecondActivity,
            fixture.Context.Deadline);

        SwapApplyResult applied = await fixture.FirstEndpoint.ApplyDecisionAsync(
            fixture.Context.CorrelationId,
            abort,
            default);
        SwapApplyResult replay = await fixture.FirstEndpoint.ApplyDecisionAsync(
            fixture.Context.CorrelationId,
            abort,
            default);
        SwapPrepareResult delayedPrepare = await fixture.FirstEndpoint.PrepareAsync(
            command,
            default);

        Assert.True(applied.Applied);
        Assert.Equal(SwapReservationPhase.Aborted, applied.Phase);
        Assert.True(replay.Applied);
        Assert.False(delayedPrepare.Prepared);
        Assert.Equal(FailureCode.DecisionConflict, delayedPrepare.FailureCode);
        fixture.AssertOriginals();
    }

    [Fact]
    public async Task PreparedActivityExcludesAnotherOperationUntilAbort()
    {
        Fixture fixture = new();
        var firstCommand = new SwapPrepareCommand(
            fixture.Context.OperationId,
            fixture.Context.CorrelationId,
            Fixture.FirstToken,
            fixture.FirstActivity,
            fixture.SecondActivity,
            fixture.Context.Deadline);
        OperationId secondOperation =
            OperationId.Parse("12121212-1212-1212-1212-121212121212");
        SwapReservationToken thirdToken = SwapReservationToken.From(
            Guid.Parse("13131313-1313-1313-1313-131313131313"));
        var overlapping = new SwapPrepareCommand(
            secondOperation,
            fixture.Context.CorrelationId,
            thirdToken,
            fixture.FirstActivity,
            fixture.SecondActivity,
            fixture.Context.Deadline);

        Assert.True((await fixture.FirstEndpoint.PrepareAsync(
            firstCommand,
            default)).Prepared);
        SwapPrepareResult conflict = await fixture.FirstEndpoint.PrepareAsync(
            overlapping,
            default);
        Assert.False(conflict.Prepared);
        Assert.Equal(FailureCode.ReservationConflict, conflict.FailureCode);

        SwapCoordinatorTransaction transaction = SwapCoordinatorTransaction.Create(
            fixture.Context,
            fixture.FirstActivity,
            Fixture.FirstToken,
            fixture.SecondActivity,
            Fixture.SecondToken);
        SwapDecision abort = transaction.CreateDecision(
            SwapDecisionOutcome.Abort,
            Fixture.Now.AddSeconds(1),
            FailureCode.OperationInProgress);
        Assert.True((await fixture.FirstEndpoint.ApplyDecisionAsync(
            fixture.Context.CorrelationId,
            abort,
            default)).Applied);
        Assert.True((await fixture.FirstEndpoint.PrepareAsync(
            overlapping,
            default)).Prepared);
    }

    [Fact]
    public async Task PrepareRejectsIncomingActivityIdAlreadyPresentOnEndpoint()
    {
        Fixture fixture = new();
        Assert.True(fixture.FirstCatalog.TryAdd(ActivityInstance.Active(
            fixture.SecondActivity.Descriptor,
            ActivityPlacement.On(fixture.FirstEndpoint.DeviceId),
            fixture.SecondActivity.Revision)));
        var command = new SwapPrepareCommand(
            fixture.Context.OperationId,
            fixture.Context.CorrelationId,
            Fixture.FirstToken,
            fixture.FirstActivity,
            fixture.SecondActivity,
            fixture.Context.Deadline);

        SwapPrepareResult result = await fixture.FirstEndpoint.PrepareAsync(
            command,
            default);

        Assert.False(result.Prepared);
        Assert.Equal(FailureCode.RevisionConflict, result.FailureCode);
        Assert.False(fixture.FirstEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out _));
    }

    [Fact]
    public async Task SameOperationWithDifferentParticipantsIsRejectedAsConflict()
    {
        Fixture fixture = new();
        var first = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
        var second = new DirectSwapEndpointChannel(fixture.SecondEndpoint);
        await fixture.ExecuteAsync(first, second);

        SwapCoordinatorResult conflict = await fixture.Coordinator.ExecuteAsync(
            fixture.Context,
            first,
            fixture.FirstActivity.Descriptor.Id,
            second,
            ActivityId.Parse("99999999-9999-9999-9999-999999999999"));

        Assert.Equal(OperationStatus.Rejected, conflict.Status);
        Assert.Equal(FailureCode.OperationIdConflict, conflict.FailureCode);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task SnapshotTransportFailureFailsBeforeDurableIntent()
    {
        Fixture fixture = new();
        var unavailable = new ThrowSnapshotChannel(
            fixture.SecondEndpoint,
            new IOException("Injected snapshot transport failure."));

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            unavailable);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(FailureCode.PeerUnavailable, result.FailureCode);
        Assert.False(fixture.Transactions.TryGet(fixture.Context.OperationId, out _));
        fixture.AssertOriginals();
    }

    [Fact]
    public async Task MismatchedSnapshotBindingFailsBeforeDurableIntent()
    {
        Fixture fixture = new();
        var mismatched = new MismatchedSnapshotChannel(fixture.SecondEndpoint);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            mismatched);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(FailureCode.ProtocolIncompatible, result.FailureCode);
        Assert.False(fixture.Transactions.TryGet(fixture.Context.OperationId, out _));
        fixture.AssertOriginals();
    }

    [Fact]
    public async Task SnapshotDelayPastDeadlineDoesNotCreateDurableIntent()
    {
        var clock = new MutableClock(Fixture.Now);
        Fixture fixture = new(clock: clock);
        var delayed = new AdvanceClockAfterSnapshotChannel(
            fixture.SecondEndpoint,
            clock,
            fixture.Context.Deadline);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            delayed);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.DeadlineExpired, result.FailureCode);
        Assert.False(fixture.Transactions.TryGet(fixture.Context.OperationId, out _));
        fixture.AssertOriginals();
    }

    [Theory]
    [InlineData(ActivityDeliveryFault.DropBeforeDelivery)]
    [InlineData(ActivityDeliveryFault.DropAcknowledgement)]
    [InlineData(ActivityDeliveryFault.DuplicateDelivery)]
    public async Task GeneratedDecisionFaultsConvergeWithoutMixedFinalState(
        ActivityDeliveryFault fault)
    {
        for (int faultedParticipant = 0; faultedParticipant < 2; faultedParticipant++)
        {
            Fixture fixture = new();
            ISwapEndpointChannel first = faultedParticipant == 0
                ? new DeterministicSwapEndpointChannel(
                    fixture.FirstEndpoint,
                    [fault])
                : new DirectSwapEndpointChannel(fixture.FirstEndpoint);
            ISwapEndpointChannel second = faultedParticipant == 1
                ? new DeterministicSwapEndpointChannel(
                    fixture.SecondEndpoint,
                    [fault])
                : new DirectSwapEndpointChannel(fixture.SecondEndpoint);

            SwapCoordinatorResult initial = await fixture.ExecuteAsync(first, second);
            SwapCoordinatorResult recovered = await fixture.Coordinator.RecoverAsync(
                fixture.Context.OperationId,
                first,
                second);

            Assert.Equal(
                fault == ActivityDeliveryFault.DuplicateDelivery
                    ? OperationStatus.Committed
                    : OperationStatus.Recovering,
                initial.Status);
            Assert.Equal(OperationStatus.Committed, recovered.Status);
            fixture.AssertSwapped();
        }
    }

    [Theory]
    [InlineData(ActivityDeliveryFault.DropBeforeDelivery)]
    [InlineData(ActivityDeliveryFault.DropAcknowledgement)]
    [InlineData(ActivityDeliveryFault.DuplicateDelivery)]
    public async Task GeneratedPrepareFaultsCommitOrAbortBothParticipants(
        ActivityDeliveryFault fault)
    {
        for (int faultedParticipant = 0; faultedParticipant < 2; faultedParticipant++)
        {
            Fixture fixture = new();
            ISwapEndpointChannel first = faultedParticipant == 0
                ? new DeterministicSwapEndpointChannel(
                    fixture.FirstEndpoint,
                    [fault],
                    [])
                : new DirectSwapEndpointChannel(fixture.FirstEndpoint);
            ISwapEndpointChannel second = faultedParticipant == 1
                ? new DeterministicSwapEndpointChannel(
                    fixture.SecondEndpoint,
                    [fault],
                    [])
                : new DirectSwapEndpointChannel(fixture.SecondEndpoint);

            SwapCoordinatorResult result = await fixture.ExecuteAsync(first, second);

            if (fault == ActivityDeliveryFault.DuplicateDelivery)
            {
                Assert.Equal(OperationStatus.Committed, result.Status);
                fixture.AssertSwapped();
            }
            else
            {
                Assert.Equal(OperationStatus.Rejected, result.Status);
                fixture.AssertOriginals();
                Assert.True(fixture.Transactions.TryGet(
                    fixture.Context.OperationId,
                    out SwapCoordinatorTransaction? transaction));
                Assert.Equal(SwapDecisionOutcome.Abort, transaction.Decision?.Outcome);
            }
        }
    }

    [Fact]
    public async Task DelayPastReservationDeadlineDurablyAbortsBothParticipants()
    {
        var clock = new MutableClock(Fixture.Now);
        Fixture fixture = new(clock: clock);
        var delayedSecond = new AdvanceClockAfterPrepareChannel(
            fixture.SecondEndpoint,
            clock,
            fixture.Context.Deadline.AddTicks(1));

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            delayedSecond);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.ReservationExpired, result.FailureCode);
        fixture.AssertOriginals();
        Assert.True(fixture.Transactions.TryGet(
            fixture.Context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Abort, transaction.Decision?.Outcome);
    }

    [Fact]
    public async Task ExhaustiveGeneratedFaultMatrixPreservesAtomicTerminalInvariant()
    {
        ActivityDeliveryFault[] faults = Enum.GetValues<ActivityDeliveryFault>();
        foreach (ActivityDeliveryFault firstPrepareFault in faults)
        {
            foreach (ActivityDeliveryFault secondPrepareFault in faults)
            {
                foreach (ActivityDeliveryFault firstDecisionFault in faults)
                {
                    foreach (ActivityDeliveryFault secondDecisionFault in faults)
                    {
                        Fixture fixture = new();
                        var first = new DeterministicSwapEndpointChannel(
                            fixture.FirstEndpoint,
                            [firstPrepareFault],
                            [firstDecisionFault]);
                        var second = new DeterministicSwapEndpointChannel(
                            fixture.SecondEndpoint,
                            [secondPrepareFault],
                            [secondDecisionFault]);

                        await fixture.ExecuteAsync(first, second);
                        SwapCoordinatorResult recovered =
                            await fixture.Coordinator.RecoverAsync(
                                fixture.Context.OperationId,
                                first,
                                second);
                        SwapCoordinatorResult replay =
                            await fixture.Coordinator.RecoverAsync(
                                fixture.Context.OperationId,
                                first,
                                second);

                        Assert.True(fixture.Transactions.TryGet(
                            fixture.Context.OperationId,
                            out SwapCoordinatorTransaction? transaction));
                        Assert.NotNull(transaction.Decision);
                        if (transaction.Decision.Outcome
                            == SwapDecisionOutcome.Commit)
                        {
                            Assert.Equal(OperationStatus.Committed, recovered.Status);
                            Assert.Equal(OperationStatus.Committed, replay.Status);
                            fixture.AssertSwapped();
                        }
                        else
                        {
                            Assert.Equal(OperationStatus.Rejected, recovered.Status);
                            Assert.Equal(OperationStatus.Rejected, replay.Status);
                            fixture.AssertOriginals();
                        }

                        Assert.Equal(recovered.DecisionDigest, replay.DecisionDigest);
                    }
                }
            }
        }
    }

    private sealed class Fixture
    {
        public static DateTimeOffset Now { get; } =
            new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

        public Fixture(
            ISwapTransactionJournal? transactions = null,
            ISwapTokenSource? tokenSource = null,
            IClock? clock = null)
        {
            DeviceId firstDevice =
                DeviceId.Parse("11111111-1111-1111-1111-111111111111");
            DeviceId secondDevice =
                DeviceId.Parse("22222222-2222-2222-2222-222222222222");
            FirstActivity = CreateActivity(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                firstDevice,
                "First",
                "first");
            SecondActivity = CreateActivity(
                "dddddddd-dddd-dddd-dddd-dddddddddddd",
                secondDevice,
                "Second",
                "second");

            FirstCatalog = new InMemoryActivityCatalog();
            SecondCatalog = new InMemoryActivityCatalog();
            FirstCatalog.TryAdd(FirstActivity);
            SecondCatalog.TryAdd(SecondActivity);
            FirstEndpoint = new InMemorySwapEndpoint(firstDevice, FirstCatalog);
            SecondEndpoint = new InMemorySwapEndpoint(secondDevice, SecondCatalog);
            Transactions = transactions ?? new InMemorySwapTransactionJournal();
            Coordinator = new SwapCoordinator(
                firstDevice,
                clock ?? new TestClock(Now),
                Transactions,
                tokenSource ?? new DeterministicSwapTokenSource(
                [
                    FirstToken,
                    SecondToken,
                ]));
            Context = OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30));
        }

        public ActivityInstance FirstActivity { get; }

        public ActivityInstance SecondActivity { get; }

        public InMemorySwapEndpoint FirstEndpoint { get; }

        public InMemorySwapEndpoint SecondEndpoint { get; }

        public InMemoryActivityCatalog FirstCatalog { get; }

        public InMemoryActivityCatalog SecondCatalog { get; }

        public static SwapReservationToken FirstToken { get; } =
            SwapReservationToken.From(
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));

        public static SwapReservationToken SecondToken { get; } =
            SwapReservationToken.From(
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        public ISwapTransactionJournal Transactions { get; }

        public SwapCoordinator Coordinator { get; }

        public OperationContext Context { get; }

        public ValueTask<SwapCoordinatorResult> ExecuteAsync(
            ISwapEndpointChannel first,
            ISwapEndpointChannel second) => Coordinator.ExecuteAsync(
                Context,
                first,
                FirstActivity.Descriptor.Id,
                second,
                SecondActivity.Descriptor.Id);

        public void AssertOriginals()
        {
            Assert.True(FirstEndpoint.TryGetActivity(FirstActivity.Descriptor.Id, out _));
            Assert.True(SecondEndpoint.TryGetActivity(SecondActivity.Descriptor.Id, out _));
        }

        public void AssertSwapped()
        {
            Assert.True(FirstEndpoint.TryGetActivity(
                SecondActivity.Descriptor.Id,
                out ActivityInstance? onFirst));
            Assert.Equal(FirstEndpoint.DeviceId, onFirst.Placement.DeviceId);
            Assert.True(SecondEndpoint.TryGetActivity(
                FirstActivity.Descriptor.Id,
                out ActivityInstance? onSecond));
            Assert.Equal(SecondEndpoint.DeviceId, onSecond.Placement.DeviceId);
            Assert.False(FirstEndpoint.TryGetActivity(FirstActivity.Descriptor.Id, out _));
            Assert.False(SecondEndpoint.TryGetActivity(SecondActivity.Descriptor.Id, out _));
        }

        private static ActivityInstance CreateActivity(
            string activityId,
            DeviceId deviceId,
            string title,
            string text)
        {
            ActivityDescriptor descriptor = ActivityDescriptor.Create(
                ActivityId.Parse(activityId),
                ActivityKind.Parse("workspace.note/v1"),
                deviceId,
                title,
                $"{{\"text\":\"{text}\"}}");
            return ActivityInstance.Active(descriptor, ActivityPlacement.On(deviceId));
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class AdvanceClockAfterPrepareChannel(
        ISwapEndpoint target,
        MutableClock clock,
        DateTimeOffset advancedTime) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken) =>
            new DirectSwapEndpointChannel(target).QueryActivityAsync(
                requestingDeviceId,
                query,
                cancellationToken);

        public async ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken)
        {
            SwapPrepareResult response = await target.PrepareAsync(
                command,
                cancellationToken);
            clock.UtcNow = advancedTime;
            return SwapDelivery.Acknowledged(response);
        }

        public async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken)
        {
            SwapApplyResult response = await target.ApplyDecisionAsync(
                correlationId,
                decision,
                cancellationToken);
            return SwapDelivery.Acknowledged(response);
        }
    }

    private sealed class AdvanceClockAfterSnapshotChannel(
        ISwapEndpoint target,
        MutableClock clock,
        DateTimeOffset advancedTime) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public async ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken)
        {
            SwapDeliveryResult<SwapActivitySnapshotResult> result =
                await new DirectSwapEndpointChannel(target).QueryActivityAsync(
                    requestingDeviceId,
                    query,
                    cancellationToken);
            clock.UtcNow = advancedTime;
            return result;
        }

        public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Prepare must not run after expiry.");

        public ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Decision must not run after expiry.");
    }

    private sealed class CountingChannel(ISwapEndpoint target) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public int PrepareCount { get; private set; }

        public int DecisionCount { get; private set; }

        public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken) =>
            new DirectSwapEndpointChannel(target).QueryActivityAsync(
                requestingDeviceId,
                query,
                cancellationToken);

        public async ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken)
        {
            PrepareCount++;
            SwapPrepareResult response = await target.PrepareAsync(
                command,
                cancellationToken);
            return SwapDelivery.Acknowledged(response);
        }

        public async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken)
        {
            DecisionCount++;
            SwapApplyResult response = await target.ApplyDecisionAsync(
                correlationId,
                decision,
                cancellationToken);
            return SwapDelivery.Acknowledged(response);
        }
    }

    private sealed class ThrowPrepareChannel(ISwapEndpoint target) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken) =>
            new DirectSwapEndpointChannel(target).QueryActivityAsync(
                requestingDeviceId,
                query,
                cancellationToken);

        public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken) =>
            throw new IOException("Injected failure after the first endpoint prepared.");

        public async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken)
        {
            SwapApplyResult response = await target.ApplyDecisionAsync(
                correlationId,
                decision,
                cancellationToken);
            return SwapDelivery.Acknowledged(response);
        }
    }

    private sealed class ThrowSnapshotChannel(
        ISwapEndpoint target,
        Exception failure) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken) =>
            ValueTask.FromException<SwapDeliveryResult<SwapActivitySnapshotResult>>(
                failure);

        public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Prepare must not run after snapshot failure.");

        public ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Decision must not run after snapshot failure.");
    }

    private sealed class MismatchedSnapshotChannel(ISwapEndpoint target) :
        ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(target.TryGetActivity(
                query.ActivityId,
                out ActivityInstance? activity));
            SwapActivitySnapshotQuery forged = SwapActivitySnapshotQuery.Create(
                OperationContext.Create(
                    OperationId.Parse("99999999-9999-9999-9999-999999999999"),
                    query.Context.CorrelationId,
                    query.Context.Deadline),
                query.TargetDeviceId,
                query.ActivityId);
            return ValueTask.FromResult(SwapDelivery.Acknowledged(
                SwapActivitySnapshotResult.Success(
                    requestingDeviceId,
                    forged,
                    activity)));
        }

        public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Prepare must not run after snapshot failure.");

        public ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Decision must not run after snapshot failure.");
    }

    private sealed class RejectPrepareChannel(
        ISwapEndpoint target,
        FailureCode failureCode) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
            QueryActivityAsync(
                DeviceId requestingDeviceId,
                SwapActivitySnapshotQuery query,
                CancellationToken cancellationToken) =>
            new DirectSwapEndpointChannel(target).QueryActivityAsync(
                requestingDeviceId,
                query,
                cancellationToken);

        public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            DeviceId senderDeviceId,
            SwapPrepareCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SwapDelivery.Acknowledged(
                SwapPrepareResult.Rejected(failureCode)));
        }

        public async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            DeviceId senderDeviceId,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken)
        {
            SwapApplyResult response = await target.ApplyDecisionAsync(
                correlationId,
                decision,
                cancellationToken);
            return SwapDelivery.Acknowledged(response);
        }
    }
}
