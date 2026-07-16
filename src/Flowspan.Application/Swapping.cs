using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SwapPrepareCommand(
    OperationId OperationId,
    SwapReservationToken ReservationToken,
    ActivityInstance OriginalActivity,
    ActivityInstance IncomingActivity,
    DateTimeOffset ExpiresAt);

public sealed record SwapPrepareResult(
    bool Prepared,
    FailureCode FailureCode,
    SwapReservationToken? ReservationToken)
{
    public static SwapPrepareResult Success(SwapReservationToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new SwapPrepareResult(true, FailureCode.None, token);
    }

    public static SwapPrepareResult Rejected(FailureCode failureCode)
    {
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected swap prepare result must have a failure code.",
                nameof(failureCode));
        }

        return new SwapPrepareResult(false, failureCode, null);
    }
}

public sealed record SwapApplyResult(
    bool Applied,
    FailureCode FailureCode,
    SwapReservationPhase? Phase)
{
    public static SwapApplyResult Success(SwapReservationPhase phase) =>
        new(true, FailureCode.None, phase);

    public static SwapApplyResult Rejected(FailureCode failureCode) =>
        new(false, failureCode, null);
}

public interface ISwapEndpoint
{
    public DeviceId DeviceId { get; }

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity);

    public ValueTask<SwapPrepareResult> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken);

    public ValueTask<SwapApplyResult> ApplyDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken);
}

public sealed class InMemorySwapEndpoint : ISwapEndpoint
{
    private readonly IActivityCatalog catalog;
    private readonly Dictionary<OperationId, SwapDecision> decisions = [];
    private readonly Lock gate = new();
    private readonly Dictionary<OperationId, SwapReservation> reservations = [];

    public InMemorySwapEndpoint(DeviceId deviceId, IActivityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(catalog);
        DeviceId = deviceId;
        this.catalog = catalog;
    }

    public DeviceId DeviceId { get; }

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity) =>
        catalog.TryGet(activityId, out activity);

    public bool TryGetReservation(
        OperationId operationId,
        [NotNullWhen(true)] out SwapReservation? reservation)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        lock (gate)
        {
            return reservations.TryGetValue(operationId, out reservation);
        }
    }

    public bool TryGetDecision(
        OperationId operationId,
        [NotNullWhen(true)] out SwapDecision? decision)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        lock (gate)
        {
            return decisions.TryGetValue(operationId, out decision);
        }
    }

    public ValueTask<SwapPrepareResult> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.OriginalActivity.Placement.DeviceId != DeviceId)
        {
            return ValueTask.FromResult(
                SwapPrepareResult.Rejected(FailureCode.RevisionConflict));
        }

        lock (gate)
        {
            if (decisions.ContainsKey(command.OperationId))
            {
                return ValueTask.FromResult(
                    SwapPrepareResult.Rejected(FailureCode.DecisionConflict));
            }

            if (reservations.TryGetValue(command.OperationId, out SwapReservation? existing))
            {
                return ValueTask.FromResult(
                    existing.Token == command.ReservationToken
                    && existing.MatchesRequest(
                        command.OriginalActivity,
                        command.IncomingActivity,
                        command.ExpiresAt)
                        ? SwapPrepareResult.Success(existing.Token)
                        : SwapPrepareResult.Rejected(FailureCode.ReservationConflict));
            }

            if (reservations.Values.Any(reservation =>
                    reservation.Phase == SwapReservationPhase.Prepared
                    && (reservation.OriginalActivity.Descriptor.Id
                            == command.OriginalActivity.Descriptor.Id
                        || reservation.OriginalActivity.Descriptor.Id
                            == command.IncomingActivity.Descriptor.Id
                        || reservation.IncomingActivity.Descriptor.Id
                            == command.OriginalActivity.Descriptor.Id
                        || reservation.IncomingActivity.Descriptor.Id
                            == command.IncomingActivity.Descriptor.Id)))
            {
                return ValueTask.FromResult(
                    SwapPrepareResult.Rejected(FailureCode.ReservationConflict));
            }

            if (!catalog.TryGet(
                    command.OriginalActivity.Descriptor.Id,
                    out ActivityInstance? current)
                || current != command.OriginalActivity)
            {
                return ValueTask.FromResult(
                    SwapPrepareResult.Rejected(FailureCode.RevisionConflict));
            }

            if (catalog.TryGet(command.IncomingActivity.Descriptor.Id, out _))
            {
                return ValueTask.FromResult(
                    SwapPrepareResult.Rejected(FailureCode.RevisionConflict));
            }

            SwapReservation reservation = SwapReservation.Prepare(
                command.OperationId,
                command.ReservationToken,
                command.OriginalActivity,
                command.IncomingActivity,
                command.ExpiresAt);
            reservations.Add(command.OperationId, reservation);
            return ValueTask.FromResult(SwapPrepareResult.Success(reservation.Token));
        }
    }

    public ValueTask<SwapApplyResult> ApplyDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (!decision.TryGetReservationToken(
                    DeviceId,
                    out SwapReservationToken? participantToken))
            {
                return ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.DecisionConflict));
            }

            if (!reservations.TryGetValue(decision.OperationId, out SwapReservation? current))
            {
                if (decision.Outcome == SwapDecisionOutcome.Abort)
                {
                    if (decisions.TryGetValue(
                            decision.OperationId,
                            out SwapDecision? existingDecision))
                    {
                        return ValueTask.FromResult(
                            StringComparer.Ordinal.Equals(
                                existingDecision.Digest,
                                decision.Digest)
                                ? SwapApplyResult.Success(SwapReservationPhase.Aborted)
                                : SwapApplyResult.Rejected(FailureCode.DecisionConflict));
                    }

                    decisions.Add(decision.OperationId, decision);
                    return ValueTask.FromResult(
                        SwapApplyResult.Success(SwapReservationPhase.Aborted));
                }

                return ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.ReservationConflict));
            }

            if (participantToken != current.Token)
            {
                return ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.DecisionConflict));
            }

            SwapReservation decided;
            try
            {
                decided = current.ApplyDecision(decision);
            }
            catch (InvalidOperationException)
            {
                FailureCode failure = decision.Outcome == SwapDecisionOutcome.Commit
                    && decision.DecidedAt > current.ExpiresAt
                        ? FailureCode.ReservationExpired
                        : FailureCode.DecisionConflict;
                return ValueTask.FromResult(SwapApplyResult.Rejected(failure));
            }

            if (current.Phase == SwapReservationPhase.Prepared
                && decided.Phase == SwapReservationPhase.Committed
                && !catalog.TrySwapReplace(
                    current.OriginalActivity,
                    decided.CreateCommittedReplacement()))
            {
                return ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.RevisionConflict));
            }

            reservations[decision.OperationId] = decided;
            decisions[decision.OperationId] = decision;
            return ValueTask.FromResult(SwapApplyResult.Success(decided.Phase));
        }
    }
}

public interface ISwapTokenSource
{
    public SwapReservationToken CreateToken();
}

public sealed class DeterministicSwapTokenSource : ISwapTokenSource
{
    private readonly Lock gate = new();
    private readonly Queue<SwapReservationToken> tokens;

    public DeterministicSwapTokenSource(IEnumerable<SwapReservationToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        this.tokens = new Queue<SwapReservationToken>(tokens);
    }

    public SwapReservationToken CreateToken()
    {
        lock (gate)
        {
            return tokens.Count > 0
                ? tokens.Dequeue()
                : throw new InvalidOperationException("The deterministic swap token source is empty.");
        }
    }
}

public sealed record SwapDeliveryResult<T>(
    ActivityDeliveryStatus Status,
    T? Response)
    where T : class
{
}

public static class SwapDelivery
{
    public static SwapDeliveryResult<T> Acknowledged<T>(T response)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(response);
        return new SwapDeliveryResult<T>(ActivityDeliveryStatus.Acknowledged, response);
    }

    public static SwapDeliveryResult<T> NotDelivered<T>()
        where T : class => new(ActivityDeliveryStatus.NotDelivered, null);

    public static SwapDeliveryResult<T> AcknowledgementLost<T>()
        where T : class => new(ActivityDeliveryStatus.AcknowledgementLost, null);
}

public interface ISwapEndpointChannel
{
    public DeviceId TargetDeviceId { get; }

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity);

    public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken);

    public ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken);
}

public class DirectSwapEndpointChannel : ISwapEndpointChannel
{
    private readonly ISwapEndpoint target;

    public DirectSwapEndpointChannel(ISwapEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(target);
        this.target = target;
    }

    public DeviceId TargetDeviceId => target.DeviceId;

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity) =>
        target.TryGetActivity(activityId, out activity);

    public virtual async ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        SwapPrepareResult response = await target
            .PrepareAsync(command, cancellationToken)
            .ConfigureAwait(false);
        return SwapDelivery.Acknowledged(response);
    }

    public virtual async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        SwapApplyResult response = await target
            .ApplyDecisionAsync(decision, cancellationToken)
            .ConfigureAwait(false);
        return SwapDelivery.Acknowledged(response);
    }
}

public sealed class DeterministicSwapEndpointChannel : DirectSwapEndpointChannel
{
    private readonly Lock gate = new();
    private readonly Queue<ActivityDeliveryFault> decisionFaults;
    private readonly Queue<ActivityDeliveryFault> prepareFaults;
    private readonly ISwapEndpoint target;

    public DeterministicSwapEndpointChannel(
        ISwapEndpoint target,
        IEnumerable<ActivityDeliveryFault> decisionFaults)
        : this(target, [], decisionFaults)
    {
    }

    public DeterministicSwapEndpointChannel(
        ISwapEndpoint target,
        IEnumerable<ActivityDeliveryFault> prepareFaults,
        IEnumerable<ActivityDeliveryFault> decisionFaults)
        : base(target)
    {
        ArgumentNullException.ThrowIfNull(prepareFaults);
        ArgumentNullException.ThrowIfNull(decisionFaults);
        this.target = target;
        this.prepareFaults = new Queue<ActivityDeliveryFault>(prepareFaults);
        this.decisionFaults = new Queue<ActivityDeliveryFault>(decisionFaults);
    }

    public override async ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        ActivityDeliveryFault fault;
        lock (gate)
        {
            fault = prepareFaults.Count > 0
                ? prepareFaults.Dequeue()
                : ActivityDeliveryFault.None;
        }

        if (fault == ActivityDeliveryFault.DropBeforeDelivery)
        {
            return SwapDelivery.NotDelivered<SwapPrepareResult>();
        }

        SwapPrepareResult response = await target
            .PrepareAsync(command, cancellationToken)
            .ConfigureAwait(false);
        if (fault == ActivityDeliveryFault.DropAcknowledgement)
        {
            return SwapDelivery.AcknowledgementLost<SwapPrepareResult>();
        }

        if (fault == ActivityDeliveryFault.DuplicateDelivery)
        {
            response = await target
                .PrepareAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }

        return SwapDelivery.Acknowledged(response);
    }

    public override async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        ActivityDeliveryFault fault;
        lock (gate)
        {
            fault = decisionFaults.Count > 0
                ? decisionFaults.Dequeue()
                : ActivityDeliveryFault.None;
        }

        if (fault == ActivityDeliveryFault.DropBeforeDelivery)
        {
            return SwapDelivery.NotDelivered<SwapApplyResult>();
        }

        SwapApplyResult response = await target
            .ApplyDecisionAsync(decision, cancellationToken)
            .ConfigureAwait(false);
        if (fault == ActivityDeliveryFault.DropAcknowledgement)
        {
            return SwapDelivery.AcknowledgementLost<SwapApplyResult>();
        }

        if (fault == ActivityDeliveryFault.DuplicateDelivery)
        {
            response = await target
                .ApplyDecisionAsync(decision, cancellationToken)
                .ConfigureAwait(false);
        }

        return SwapDelivery.Acknowledged(response);
    }
}

public sealed record SwapCoordinatorResult(
    OperationId OperationId,
    OperationStatus Status,
    FailureCode FailureCode,
    string? DecisionDigest)
{
    public bool IsSuccess => Status == OperationStatus.Committed;
}

public sealed class SwapCoordinator
{
    private readonly IClock clock;
    private readonly ISwapTransactionJournal transactionJournal;
    private readonly ISwapTokenSource tokenSource;

    public SwapCoordinator(
        IClock clock,
        ISwapTransactionJournal transactionJournal,
        ISwapTokenSource tokenSource)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(transactionJournal);
        ArgumentNullException.ThrowIfNull(tokenSource);
        this.clock = clock;
        this.transactionJournal = transactionJournal;
        this.tokenSource = tokenSource;
    }

    public async ValueTask<SwapCoordinatorResult> ExecuteAsync(
        OperationContext context,
        ISwapEndpointChannel first,
        ActivityId firstActivityId,
        ISwapEndpointChannel second,
        ActivityId secondActivityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(firstActivityId);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(secondActivityId);

        if (transactionJournal.TryGet(
                context.OperationId,
                out SwapCoordinatorTransaction? recorded))
        {
            if (!recorded.MatchesRequest(
                    context,
                    first.TargetDeviceId,
                    firstActivityId,
                    second.TargetDeviceId,
                    secondActivityId))
            {
                return Rejected(
                    context.OperationId,
                    FailureCode.OperationIdConflict);
            }

            return await ContinueRecordedAsync(
                recorded,
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Deadline <= clock.UtcNow)
        {
            return Rejected(
                context.OperationId,
                FailureCode.DeadlineExpired);
        }

        if (first.TargetDeviceId == second.TargetDeviceId)
        {
            return Rejected(
                context.OperationId,
                FailureCode.RevisionConflict);
        }

        if (!first.TryGetActivity(firstActivityId, out ActivityInstance? firstActivity)
            || !second.TryGetActivity(secondActivityId, out ActivityInstance? secondActivity))
        {
            return Rejected(
                context.OperationId,
                FailureCode.ActivityNotFound);
        }

        SwapReservationToken firstToken = tokenSource.CreateToken();
        SwapReservationToken secondToken = tokenSource.CreateToken();
        SwapCoordinatorTransaction transaction;
        try
        {
            transaction = SwapCoordinatorTransaction.Create(
                context,
                firstActivity,
                firstToken,
                secondActivity,
                secondToken);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or InvalidOperationException)
        {
            return Rejected(
                context.OperationId,
                FailureCode.RevisionConflict);
        }

        SwapTransactionWriteResult intentWrite;
        try
        {
            intentWrite = await transactionJournal
                .TryCreateAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            SwapStatePersistenceException
            or OperationCanceledException)
        {
            return Failed(
                context.OperationId,
                FailureCode.InternalFailure);
        }

        if (intentWrite.Status == SwapTransactionWriteStatus.Conflict)
        {
            return Rejected(
                context.OperationId,
                FailureCode.OperationIdConflict);
        }

        if (intentWrite.Status == SwapTransactionWriteStatus.CapacityExceeded
            || intentWrite.Transaction is null)
        {
            return Failed(
                context.OperationId,
                FailureCode.InternalFailure);
        }

        if (intentWrite.Status == SwapTransactionWriteStatus.Replayed)
        {
            return await ContinueRecordedAsync(
                intentWrite.Transaction,
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        return await PrepareAndDecideAsync(
            intentWrite.Transaction,
            firstActivity,
            first,
            secondActivity,
            second,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SwapCoordinatorResult> RecoverAsync(
        OperationId operationId,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!transactionJournal.TryGet(
                operationId,
                out SwapCoordinatorTransaction? transaction))
        {
            return Rejected(operationId, FailureCode.DecisionConflict);
        }

        if (!transaction.MatchesParticipants(
                first.TargetDeviceId,
                second.TargetDeviceId))
        {
            return Rejected(operationId, FailureCode.OperationIdConflict);
        }

        return await ContinueRecordedAsync(
            transaction,
            first,
            second,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SwapCoordinatorResult> PrepareAndDecideAsync(
        SwapCoordinatorTransaction transaction,
        ActivityInstance firstActivity,
        ISwapEndpointChannel first,
        ActivityInstance secondActivity,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        SwapTransactionParticipant firstParticipant = transaction.GetParticipant(
            first.TargetDeviceId);
        SwapTransactionParticipant secondParticipant = transaction.GetParticipant(
            second.TargetDeviceId);
        var firstCommand = new SwapPrepareCommand(
            transaction.Context.OperationId,
            firstParticipant.ReservationToken,
            firstActivity,
            secondActivity,
            transaction.Context.Deadline);
        var secondCommand = new SwapPrepareCommand(
            transaction.Context.OperationId,
            secondParticipant.ReservationToken,
            secondActivity,
            firstActivity,
            transaction.Context.Deadline);

        SwapDeliveryResult<SwapPrepareResult> firstPrepare;
        try
        {
            firstPrepare = await first
                .PrepareAsync(firstCommand, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Recovering(
                transaction.Context.OperationId,
                exception is OperationCanceledException
                    ? FailureCode.OperationInProgress
                    : FailureCode.InternalFailure);
        }

        if (!WasPrepared(firstPrepare))
        {
            return await DecideAndApplyAbortAsync(
                transaction,
                PrepareFailureCode(firstPrepare),
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        SwapDeliveryResult<SwapPrepareResult> secondPrepare;
        try
        {
            secondPrepare = await second
                .PrepareAsync(secondCommand, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Recovering(
                transaction.Context.OperationId,
                exception is OperationCanceledException
                    ? FailureCode.OperationInProgress
                    : FailureCode.InternalFailure);
        }

        if (!WasPrepared(secondPrepare))
        {
            return await DecideAndApplyAbortAsync(
                transaction,
                PrepareFailureCode(secondPrepare),
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        if (clock.UtcNow > transaction.Context.Deadline)
        {
            return await DecideAndApplyAbortAsync(
                transaction,
                FailureCode.ReservationExpired,
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        SwapDecision commit = transaction.CreateDecision(
            SwapDecisionOutcome.Commit,
            clock.UtcNow);
        return await RecordAndApplyDecisionAsync(
            transaction,
            commit,
            first,
            second,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SwapCoordinatorResult> ContinueRecordedAsync(
        SwapCoordinatorTransaction transaction,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        if (transaction.Decision is not null)
        {
            return await ApplyRecordedDecisionAsync(
                transaction.Decision,
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        return await DecideAndApplyAbortAsync(
            transaction,
            FailureCode.OperationInProgress,
            first,
            second,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SwapCoordinatorResult> DecideAndApplyAbortAsync(
        SwapCoordinatorTransaction transaction,
        FailureCode failureCode,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        SwapDecision abort = transaction.CreateDecision(
            SwapDecisionOutcome.Abort,
            clock.UtcNow,
            failureCode);
        return await RecordAndApplyDecisionAsync(
            transaction,
            abort,
            first,
            second,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SwapCoordinatorResult> RecordAndApplyDecisionAsync(
        SwapCoordinatorTransaction transaction,
        SwapDecision decision,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        SwapTransactionWriteResult write;
        try
        {
            write = await transactionJournal.TryRecordDecisionAsync(
                transaction.Context.OperationId,
                decision,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            SwapStatePersistenceException
            or OperationCanceledException)
        {
            return Recovering(
                transaction.Context.OperationId,
                FailureCode.InternalFailure);
        }

        if (write.Transaction?.Decision is not null)
        {
            return await ApplyRecordedDecisionAsync(
                write.Transaction.Decision,
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        return Recovering(
            transaction.Context.OperationId,
            FailureCode.DecisionConflict);
    }

    private static bool WasPrepared(SwapDeliveryResult<SwapPrepareResult> delivery) =>
        delivery.Status == ActivityDeliveryStatus.Acknowledged
        && delivery.Response is { Prepared: true };

    private static FailureCode PrepareFailureCode(
        SwapDeliveryResult<SwapPrepareResult> delivery) =>
        delivery.Status == ActivityDeliveryStatus.NotDelivered
            ? FailureCode.PeerUnavailable
            : delivery.Status == ActivityDeliveryStatus.AcknowledgementLost
                ? FailureCode.AcknowledgementLost
                : delivery.Response?.FailureCode ?? FailureCode.InternalFailure;

    private static SwapCoordinatorResult Rejected(
        OperationId operationId,
        FailureCode failureCode) => new(
        operationId,
        OperationStatus.Rejected,
        failureCode,
        null);

    private static SwapCoordinatorResult Failed(
        OperationId operationId,
        FailureCode failureCode) => new(
        operationId,
        OperationStatus.Failed,
        failureCode,
        null);

    private static SwapCoordinatorResult Recovering(
        OperationId operationId,
        FailureCode failureCode) => new(
        operationId,
        OperationStatus.Recovering,
        failureCode,
        null);

    private static async ValueTask<SwapCoordinatorResult> ApplyRecordedDecisionAsync(
        SwapDecision decision,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        SwapDeliveryResult<SwapApplyResult> firstApply = await ApplySafelyAsync(
            first,
            decision,
            cancellationToken).ConfigureAwait(false);
        SwapDeliveryResult<SwapApplyResult> secondApply = await ApplySafelyAsync(
            second,
            decision,
            cancellationToken).ConfigureAwait(false);
        bool bothApplied = WasApplied(firstApply, decision.Outcome)
            && WasApplied(secondApply, decision.Outcome);

        if (decision.Outcome == SwapDecisionOutcome.Abort)
        {
            return new SwapCoordinatorResult(
                decision.OperationId,
                bothApplied ? OperationStatus.Rejected : OperationStatus.Recovering,
                bothApplied
                    ? decision.FailureCode
                    : ApplyFailureCode(firstApply, secondApply),
                decision.Digest);
        }

        return new SwapCoordinatorResult(
            decision.OperationId,
            bothApplied ? OperationStatus.Committed : OperationStatus.Recovering,
            bothApplied ? FailureCode.None : ApplyFailureCode(firstApply, secondApply),
            decision.Digest);
    }

    private static async ValueTask<SwapDeliveryResult<SwapApplyResult>>
        ApplySafelyAsync(
            ISwapEndpointChannel channel,
            SwapDecision decision,
            CancellationToken cancellationToken)
    {
        try
        {
            return await channel.ApplyDecisionAsync(decision, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return SwapDelivery.AcknowledgementLost<SwapApplyResult>();
        }
    }

    private static bool WasApplied(
        SwapDeliveryResult<SwapApplyResult> delivery,
        SwapDecisionOutcome outcome) =>
        delivery.Status == ActivityDeliveryStatus.Acknowledged
        && delivery.Response is { Applied: true } response
        && response.Phase == (outcome == SwapDecisionOutcome.Commit
            ? SwapReservationPhase.Committed
            : SwapReservationPhase.Aborted);

    private static FailureCode ApplyFailureCode(
        SwapDeliveryResult<SwapApplyResult> first,
        SwapDeliveryResult<SwapApplyResult> second)
    {
        foreach (SwapDeliveryResult<SwapApplyResult> delivery in new[] { first, second })
        {
            if (delivery.Status == ActivityDeliveryStatus.NotDelivered)
            {
                return FailureCode.PeerUnavailable;
            }

            if (delivery.Status == ActivityDeliveryStatus.AcknowledgementLost)
            {
                return FailureCode.AcknowledgementLost;
            }

            if (delivery.Response is { Applied: false } response)
            {
                return response.FailureCode;
            }
        }

        return FailureCode.InternalFailure;
    }
}
