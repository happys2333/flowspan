using System.Collections.Concurrent;
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

            if (!catalog.TryGet(
                    command.OriginalActivity.Descriptor.Id,
                    out ActivityInstance? current)
                || current != command.OriginalActivity)
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
            if (!reservations.TryGetValue(decision.OperationId, out SwapReservation? current))
            {
                return ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.ReservationConflict));
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
            return ValueTask.FromResult(SwapApplyResult.Success(decided.Phase));
        }
    }
}

public interface ISwapDecisionJournal
{
    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapDecision? decision);

    public bool TryRecord(SwapDecision decision);
}

public sealed class InMemorySwapDecisionJournal : ISwapDecisionJournal
{
    private readonly ConcurrentDictionary<OperationId, SwapDecision> decisions = new();

    public bool TryGet(
        OperationId operationId,
        [NotNullWhen(true)] out SwapDecision? decision)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        return decisions.TryGetValue(operationId, out decision);
    }

    public bool TryRecord(SwapDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        SwapDecision recorded = decisions.GetOrAdd(decision.OperationId, decision);
        return StringComparer.Ordinal.Equals(recorded.Digest, decision.Digest);
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
    private readonly ISwapDecisionJournal decisionJournal;
    private readonly ISwapTokenSource tokenSource;

    public SwapCoordinator(
        IClock clock,
        ISwapDecisionJournal decisionJournal,
        ISwapTokenSource tokenSource)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(decisionJournal);
        ArgumentNullException.ThrowIfNull(tokenSource);
        this.clock = clock;
        this.decisionJournal = decisionJournal;
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

        if (decisionJournal.TryGet(context.OperationId, out SwapDecision? recorded))
        {
            return await ApplyRecordedDecisionAsync(
                recorded,
                first,
                second,
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Deadline <= clock.UtcNow
            || first.TargetDeviceId == second.TargetDeviceId
            || !first.TryGetActivity(firstActivityId, out ActivityInstance? firstActivity)
            || !second.TryGetActivity(secondActivityId, out ActivityInstance? secondActivity))
        {
            return new SwapCoordinatorResult(
                context.OperationId,
                OperationStatus.Rejected,
                FailureCode.RevisionConflict,
                null);
        }

        SwapReservationToken firstToken = tokenSource.CreateToken();
        SwapReservationToken secondToken = tokenSource.CreateToken();
        var firstCommand = new SwapPrepareCommand(
            context.OperationId,
            firstToken,
            firstActivity,
            secondActivity,
            context.Deadline);
        var secondCommand = new SwapPrepareCommand(
            context.OperationId,
            secondToken,
            secondActivity,
            firstActivity,
            context.Deadline);

        SwapDeliveryResult<SwapPrepareResult> firstPrepare = await first
            .PrepareAsync(firstCommand, cancellationToken)
            .ConfigureAwait(false);
        if (!WasPrepared(firstPrepare))
        {
            if (firstPrepare.Status == ActivityDeliveryStatus.AcknowledgementLost)
            {
                SwapDecision abort = SwapDecision.Create(
                    context.OperationId,
                    SwapDecisionOutcome.Abort,
                    clock.UtcNow,
                    [firstToken]);
                if (!decisionJournal.TryRecord(abort))
                {
                    return DecisionConflict(context.OperationId);
                }

                await ApplyAbortBestEffortAsync(
                    abort,
                    first,
                    null,
                    cancellationToken).ConfigureAwait(false);
                return new SwapCoordinatorResult(
                    context.OperationId,
                    OperationStatus.Rejected,
                    FailureCode.AcknowledgementLost,
                    abort.Digest);
            }

            return PrepareFailure(context.OperationId, firstPrepare);
        }

        SwapDeliveryResult<SwapPrepareResult> secondPrepare = await second
            .PrepareAsync(secondCommand, cancellationToken)
            .ConfigureAwait(false);
        if (!WasPrepared(secondPrepare) || clock.UtcNow > context.Deadline)
        {
            var abortTokens = new List<SwapReservationToken> { firstToken };
            bool secondMayBePrepared = WasPrepared(secondPrepare)
                || secondPrepare.Status == ActivityDeliveryStatus.AcknowledgementLost;
            if (secondMayBePrepared)
            {
                abortTokens.Add(secondToken);
            }

            SwapDecision abort = SwapDecision.Create(
                context.OperationId,
                SwapDecisionOutcome.Abort,
                clock.UtcNow,
                abortTokens);
            if (!decisionJournal.TryRecord(abort))
            {
                return DecisionConflict(context.OperationId);
            }

            await ApplyAbortBestEffortAsync(
                abort,
                first,
                secondMayBePrepared ? second : null,
                cancellationToken).ConfigureAwait(false);
            return new SwapCoordinatorResult(
                context.OperationId,
                OperationStatus.Rejected,
                WasPrepared(secondPrepare)
                    ? FailureCode.ReservationExpired
                    : PrepareFailureCode(secondPrepare),
                abort.Digest);
        }

        SwapDecision commit = SwapDecision.Create(
            context.OperationId,
            SwapDecisionOutcome.Commit,
            clock.UtcNow,
            [firstToken, secondToken]);
        if (!decisionJournal.TryRecord(commit))
        {
            return DecisionConflict(context.OperationId);
        }

        return await ApplyRecordedDecisionAsync(
            commit,
            first,
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
        if (!decisionJournal.TryGet(operationId, out SwapDecision? decision))
        {
            return new SwapCoordinatorResult(
                operationId,
                OperationStatus.Rejected,
                FailureCode.DecisionConflict,
                null);
        }

        return await ApplyRecordedDecisionAsync(
            decision,
            first,
            second,
            cancellationToken).ConfigureAwait(false);
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

    private static SwapCoordinatorResult PrepareFailure(
        OperationId operationId,
        SwapDeliveryResult<SwapPrepareResult> delivery) => new(
            operationId,
            delivery.Status == ActivityDeliveryStatus.AcknowledgementLost
                ? OperationStatus.Recovering
                : OperationStatus.Rejected,
            PrepareFailureCode(delivery),
            null);

    private static SwapCoordinatorResult DecisionConflict(OperationId operationId) => new(
        operationId,
        OperationStatus.Recovering,
        FailureCode.DecisionConflict,
        null);

    private static async ValueTask ApplyAbortBestEffortAsync(
        SwapDecision abort,
        ISwapEndpointChannel first,
        ISwapEndpointChannel? second,
        CancellationToken cancellationToken)
    {
        await first.ApplyDecisionAsync(abort, cancellationToken).ConfigureAwait(false);
        if (second is not null)
        {
            await second.ApplyDecisionAsync(abort, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<SwapCoordinatorResult> ApplyRecordedDecisionAsync(
        SwapDecision decision,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        SwapDeliveryResult<SwapApplyResult> firstApply = await first
            .ApplyDecisionAsync(decision, cancellationToken)
            .ConfigureAwait(false);
        SwapDeliveryResult<SwapApplyResult> secondApply = await second
            .ApplyDecisionAsync(decision, cancellationToken)
            .ConfigureAwait(false);
        bool bothApplied = WasApplied(firstApply) && WasApplied(secondApply);

        if (decision.Outcome == SwapDecisionOutcome.Abort)
        {
            return new SwapCoordinatorResult(
                decision.OperationId,
                bothApplied ? OperationStatus.Rejected : OperationStatus.Recovering,
                bothApplied ? FailureCode.ReservationConflict : FailureCode.AcknowledgementLost,
                decision.Digest);
        }

        return new SwapCoordinatorResult(
            decision.OperationId,
            bothApplied ? OperationStatus.Committed : OperationStatus.Recovering,
            bothApplied ? FailureCode.None : ApplyFailureCode(firstApply, secondApply),
            decision.Digest);
    }

    private static bool WasApplied(SwapDeliveryResult<SwapApplyResult> delivery) =>
        delivery.Status == ActivityDeliveryStatus.Acknowledged
        && delivery.Response is { Applied: true };

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
