using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SwapPrepareCommand(
    OperationId OperationId,
    CorrelationId CorrelationId,
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

public sealed record SwapActivitySnapshotQuery
{
    private SwapActivitySnapshotQuery(
        OperationContext context,
        DeviceId targetDeviceId,
        ActivityId activityId)
    {
        Context = context;
        TargetDeviceId = targetDeviceId;
        ActivityId = activityId;
    }

    public OperationContext Context { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityId ActivityId { get; }

    public static SwapActivitySnapshotQuery Create(
        OperationContext context,
        DeviceId targetDeviceId,
        ActivityId activityId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(activityId);
        return new SwapActivitySnapshotQuery(context, targetDeviceId, activityId);
    }
}

public sealed record SwapActivitySnapshotResult
{
    private SwapActivitySnapshotResult(
        OperationId operationId,
        CorrelationId correlationId,
        DeviceId requestingDeviceId,
        DeviceId targetDeviceId,
        ActivityId requestedActivityId,
        ActivityInstance? activity,
        FailureCode failureCode)
    {
        OperationId = operationId;
        CorrelationId = correlationId;
        RequestingDeviceId = requestingDeviceId;
        TargetDeviceId = targetDeviceId;
        RequestedActivityId = requestedActivityId;
        Activity = activity;
        FailureCode = failureCode;
    }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public DeviceId RequestingDeviceId { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityId RequestedActivityId { get; }

    public ActivityInstance? Activity { get; }

    public FailureCode FailureCode { get; }

    public bool IsSuccess => FailureCode == FailureCode.None;

    public static SwapActivitySnapshotResult Success(
        DeviceId requestingDeviceId,
        SwapActivitySnapshotQuery query,
        ActivityInstance activity)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Descriptor.Id != query.ActivityId
            || activity.Placement.DeviceId != query.TargetDeviceId
            || activity.Lifecycle != ActivityLifecycle.Active
            || activity.Descriptor.Sensitivity != ActivitySensitivity.Normal)
        {
            throw new ArgumentException(
                "A successful Swap snapshot must be the requested eligible local Activity.",
                nameof(activity));
        }

        return new SwapActivitySnapshotResult(
            query.Context.OperationId,
            query.Context.CorrelationId,
            requestingDeviceId,
            query.TargetDeviceId,
            query.ActivityId,
            activity,
            FailureCode.None);
    }

    public static SwapActivitySnapshotResult Rejected(
        DeviceId requestingDeviceId,
        SwapActivitySnapshotQuery query,
        FailureCode failureCode)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected Swap snapshot requires a failure code.",
                nameof(failureCode));
        }

        return new SwapActivitySnapshotResult(
            query.Context.OperationId,
            query.Context.CorrelationId,
            requestingDeviceId,
            query.TargetDeviceId,
            query.ActivityId,
            null,
            failureCode);
    }
}

public interface ISwapEndpoint
{
    public DeviceId DeviceId { get; }

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity);

    public bool MatchesOperation(
        OperationId operationId,
        CorrelationId correlationId,
        DeviceId peerDeviceId);

    public ValueTask<SwapPrepareResult> PrepareAsync(
        SwapPrepareCommand command,
        CancellationToken cancellationToken);

    public ValueTask<SwapApplyResult> ApplyDecisionAsync(
        CorrelationId correlationId,
        SwapDecision decision,
        CancellationToken cancellationToken);
}

public interface ISwapEndpointPeer
{
    public DeviceId DeviceId { get; }

    public ValueTask<SwapActivitySnapshotResult> QueryActivityAsync(
        DeviceId requestingDeviceId,
        SwapActivitySnapshotQuery query,
        CancellationToken cancellationToken);

    public ValueTask<SwapPrepareResult> PrepareAsync(
        DeviceId senderDeviceId,
        SwapPrepareCommand command,
        CancellationToken cancellationToken);

    public ValueTask<SwapApplyResult> ApplyDecisionAsync(
        DeviceId senderDeviceId,
        CorrelationId correlationId,
        SwapDecision decision,
        CancellationToken cancellationToken);
}

public sealed class AuthorizedSwapEndpoint : ISwapEndpointPeer
{
    private readonly IClock clock;
    private readonly ISwapEndpoint endpoint;
    private readonly ConcurrentDictionary<DeviceId, CapabilityGrant> peerGrants = new();

    public AuthorizedSwapEndpoint(IClock clock, ISwapEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(endpoint);
        this.clock = clock;
        this.endpoint = endpoint;
    }

    public DeviceId DeviceId => endpoint.DeviceId;

    public void SetPeerGrant(DeviceId peerDeviceId, CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(grant);
        peerGrants[peerDeviceId] = grant;
    }

    public ValueTask<SwapActivitySnapshotResult> QueryActivityAsync(
        DeviceId requestingDeviceId,
        SwapActivitySnapshotQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.TargetDeviceId != DeviceId)
        {
            throw new ArgumentException(
                "A Swap snapshot query targets another Device.",
                nameof(query));
        }

        FailureCode failureCode = query.Context.Deadline <= clock.UtcNow
            ? FailureCode.DeadlineExpired
            : !AllowsSwap(requestingDeviceId)
                ? FailureCode.CapabilityDenied
                : FailureCode.None;
        if (failureCode != FailureCode.None)
        {
            return ValueTask.FromResult(
                SwapActivitySnapshotResult.Rejected(
                    requestingDeviceId,
                    query,
                    failureCode));
        }

        if (!endpoint.TryGetActivity(query.ActivityId, out ActivityInstance? activity)
            || activity is null)
        {
            return ValueTask.FromResult(
                SwapActivitySnapshotResult.Rejected(
                    requestingDeviceId,
                    query,
                    FailureCode.ActivityNotFound));
        }

        return ValueTask.FromResult(
            activity.Lifecycle == ActivityLifecycle.Active
            && activity.Descriptor.Sensitivity == ActivitySensitivity.Normal
                ? SwapActivitySnapshotResult.Success(
                    requestingDeviceId,
                    query,
                    activity)
                : SwapActivitySnapshotResult.Rejected(
                    requestingDeviceId,
                    query,
                    FailureCode.DescriptorRejected));
    }

    public ValueTask<SwapPrepareResult> PrepareAsync(
        DeviceId senderDeviceId,
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.MatchesOperation(
                command.OperationId,
                command.CorrelationId,
                senderDeviceId))
        {
            return endpoint.PrepareAsync(command, cancellationToken);
        }

        return command.ExpiresAt <= clock.UtcNow
            ? ValueTask.FromResult(
                SwapPrepareResult.Rejected(FailureCode.DeadlineExpired))
            : command.OriginalActivity.Placement.DeviceId != DeviceId
                || command.IncomingActivity.Placement.DeviceId != senderDeviceId
                || command.OriginalActivity.Lifecycle != ActivityLifecycle.Active
                || command.IncomingActivity.Lifecycle != ActivityLifecycle.Active
                || command.OriginalActivity.Descriptor.Sensitivity
                    != ActivitySensitivity.Normal
                || command.IncomingActivity.Descriptor.Sensitivity
                    != ActivitySensitivity.Normal
                ? ValueTask.FromResult(
                    SwapPrepareResult.Rejected(FailureCode.DescriptorRejected))
                : AllowsSwap(senderDeviceId)
                    ? endpoint.PrepareAsync(command, cancellationToken)
                    : ValueTask.FromResult(
                        SwapPrepareResult.Rejected(FailureCode.CapabilityDenied));
    }

    public ValueTask<SwapApplyResult> ApplyDecisionAsync(
        DeviceId senderDeviceId,
        CorrelationId correlationId,
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();
        if (senderDeviceId == DeviceId
            || !decision.TryGetReservationToken(DeviceId, out _)
            || !decision.TryGetReservationToken(senderDeviceId, out _))
        {
            return ValueTask.FromResult(
                SwapApplyResult.Rejected(FailureCode.DecisionConflict));
        }

        return AllowsSwap(senderDeviceId)
            || endpoint.MatchesOperation(
                decision.OperationId,
                correlationId,
                senderDeviceId)
                ? endpoint.ApplyDecisionAsync(
                    correlationId,
                    decision,
                    cancellationToken)
                : ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.CapabilityDenied));
    }

    private bool AllowsSwap(DeviceId peerDeviceId) =>
        peerGrants.TryGetValue(peerDeviceId, out CapabilityGrant? grant)
        && grant.Allows(Capability.ActivitySwap);
}

public sealed class InMemorySwapEndpoint : ISwapEndpoint
{
    private readonly IActivityCatalog catalog;
    private readonly Dictionary<OperationId, SwapEndpointBinding> bindings = [];
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

    public bool MatchesOperation(
        OperationId operationId,
        CorrelationId correlationId,
        DeviceId peerDeviceId)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (gate)
        {
            return bindings.TryGetValue(
                    operationId,
                    out SwapEndpointBinding? binding)
                && binding.CorrelationId == correlationId
                && binding.PeerDeviceId == peerDeviceId;
        }
    }

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
        if (command.OriginalActivity.Placement.DeviceId != DeviceId
            || command.IncomingActivity.Revision == long.MaxValue)
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
                    bindings.TryGetValue(
                        command.OperationId,
                        out SwapEndpointBinding? binding)
                    && binding.CorrelationId == command.CorrelationId
                    && binding.PeerDeviceId
                        == command.IncomingActivity.Placement.DeviceId
                    && existing.Token == command.ReservationToken
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
            bindings.Add(
                command.OperationId,
                new SwapEndpointBinding(
                    command.CorrelationId,
                    command.IncomingActivity.Placement.DeviceId));
            reservations.Add(command.OperationId, reservation);
            return ValueTask.FromResult(SwapPrepareResult.Success(reservation.Token));
        }
    }

    public ValueTask<SwapApplyResult> ApplyDecisionAsync(
        CorrelationId correlationId,
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
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

            DeviceId peerDeviceId = decision.Participants
                .Single(participant => participant.DeviceId != DeviceId)
                .DeviceId;

            if (!reservations.TryGetValue(decision.OperationId, out SwapReservation? current))
            {
                if (decision.Outcome == SwapDecisionOutcome.Abort)
                {
                    if (decisions.TryGetValue(
                            decision.OperationId,
                            out SwapDecision? existingDecision))
                    {
                        return ValueTask.FromResult(
                            bindings.TryGetValue(
                                decision.OperationId,
                                out SwapEndpointBinding? existingBinding)
                            && existingBinding.CorrelationId == correlationId
                            && existingBinding.PeerDeviceId == peerDeviceId
                            && StringComparer.Ordinal.Equals(
                                existingDecision.Digest,
                                decision.Digest)
                                ? SwapApplyResult.Success(SwapReservationPhase.Aborted)
                                : SwapApplyResult.Rejected(FailureCode.DecisionConflict));
                    }

                    bindings.Add(
                        decision.OperationId,
                        new SwapEndpointBinding(correlationId, peerDeviceId));
                    decisions.Add(decision.OperationId, decision);
                    return ValueTask.FromResult(
                        SwapApplyResult.Success(SwapReservationPhase.Aborted));
                }

                return ValueTask.FromResult(
                    SwapApplyResult.Rejected(FailureCode.ReservationConflict));
            }

            if (!bindings.TryGetValue(
                    decision.OperationId,
                    out SwapEndpointBinding? binding)
                || binding.CorrelationId != correlationId
                || binding.PeerDeviceId != peerDeviceId
                || participantToken != current.Token)
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

    private sealed record SwapEndpointBinding(
        CorrelationId CorrelationId,
        DeviceId PeerDeviceId);
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

    public ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> QueryActivityAsync(
        DeviceId requestingDeviceId,
        SwapActivitySnapshotQuery query,
        CancellationToken cancellationToken);

    public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
        DeviceId senderDeviceId,
        SwapPrepareCommand command,
        CancellationToken cancellationToken);

    public ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
        DeviceId senderDeviceId,
        CorrelationId correlationId,
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

    public virtual ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>>
        QueryActivityAsync(
            DeviceId requestingDeviceId,
            SwapActivitySnapshotQuery query,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        SwapActivitySnapshotResult result = target.TryGetActivity(
            query.ActivityId,
            out ActivityInstance? activity)
            && activity is not null
            && activity.Lifecycle == ActivityLifecycle.Active
            && activity.Descriptor.Sensitivity == ActivitySensitivity.Normal
                ? SwapActivitySnapshotResult.Success(
                    requestingDeviceId,
                    query,
                    activity)
                : SwapActivitySnapshotResult.Rejected(
                    requestingDeviceId,
                    query,
                    activity is null
                        ? FailureCode.ActivityNotFound
                        : FailureCode.DescriptorRejected);
        return ValueTask.FromResult(SwapDelivery.Acknowledged(result));
    }

    public virtual async ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
        DeviceId senderDeviceId,
        SwapPrepareCommand command,
        CancellationToken cancellationToken)
    {
        SwapPrepareResult response = await target
            .PrepareAsync(command, cancellationToken)
            .ConfigureAwait(false);
        return SwapDelivery.Acknowledged(response);
    }

    public virtual async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
        DeviceId senderDeviceId,
        CorrelationId correlationId,
        SwapDecision decision,
        CancellationToken cancellationToken)
    {
        SwapApplyResult response = await target
            .ApplyDecisionAsync(correlationId, decision, cancellationToken)
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
        DeviceId senderDeviceId,
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
        DeviceId senderDeviceId,
        CorrelationId correlationId,
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
            .ApplyDecisionAsync(correlationId, decision, cancellationToken)
            .ConfigureAwait(false);
        if (fault == ActivityDeliveryFault.DropAcknowledgement)
        {
            return SwapDelivery.AcknowledgementLost<SwapApplyResult>();
        }

        if (fault == ActivityDeliveryFault.DuplicateDelivery)
        {
            response = await target
                .ApplyDecisionAsync(correlationId, decision, cancellationToken)
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
    private readonly DeviceId coordinatorDeviceId;
    private readonly ISwapTransactionJournal transactionJournal;
    private readonly ISwapTokenSource tokenSource;

    public SwapCoordinator(
        DeviceId coordinatorDeviceId,
        IClock clock,
        ISwapTransactionJournal transactionJournal,
        ISwapTokenSource tokenSource)
    {
        ArgumentNullException.ThrowIfNull(coordinatorDeviceId);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(transactionJournal);
        ArgumentNullException.ThrowIfNull(tokenSource);
        this.coordinatorDeviceId = coordinatorDeviceId;
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

        SwapActivitySnapshotQuery firstQuery = SwapActivitySnapshotQuery.Create(
            context,
            first.TargetDeviceId,
            firstActivityId);
        SwapActivitySnapshotQuery secondQuery = SwapActivitySnapshotQuery.Create(
            context,
            second.TargetDeviceId,
            secondActivityId);
        (SwapDeliveryResult<SwapActivitySnapshotResult>? Delivery, FailureCode FailureCode)
            firstAttempt = await QueryActivitySafelyAsync(
                first,
                firstQuery,
                cancellationToken).ConfigureAwait(false);
        if (firstAttempt.Delivery is null)
        {
            return Failed(context.OperationId, firstAttempt.FailureCode);
        }

        (SwapDeliveryResult<SwapActivitySnapshotResult>? Delivery, FailureCode FailureCode)
            secondAttempt = await QueryActivitySafelyAsync(
                second,
                secondQuery,
                cancellationToken).ConfigureAwait(false);
        if (secondAttempt.Delivery is null)
        {
            return Failed(context.OperationId, secondAttempt.FailureCode);
        }

        SwapDeliveryResult<SwapActivitySnapshotResult> firstSnapshot =
            firstAttempt.Delivery;
        SwapDeliveryResult<SwapActivitySnapshotResult> secondSnapshot =
            secondAttempt.Delivery;
        if (firstSnapshot.Status != ActivityDeliveryStatus.Acknowledged
            || secondSnapshot.Status != ActivityDeliveryStatus.Acknowledged)
        {
            return Failed(
                context.OperationId,
                firstSnapshot.Status == ActivityDeliveryStatus.AcknowledgementLost
                || secondSnapshot.Status == ActivityDeliveryStatus.AcknowledgementLost
                    ? FailureCode.AcknowledgementLost
                    : FailureCode.PeerUnavailable);
        }

        SwapActivitySnapshotResult firstResult = firstSnapshot.Response
            ?? throw new InvalidOperationException(
                "An acknowledged Swap snapshot must include a result.");
        SwapActivitySnapshotResult secondResult = secondSnapshot.Response
            ?? throw new InvalidOperationException(
                "An acknowledged Swap snapshot must include a result.");
        if (!MatchesSnapshotResult(firstResult, firstQuery)
            || !MatchesSnapshotResult(secondResult, secondQuery))
        {
            return Failed(
                context.OperationId,
                FailureCode.ProtocolIncompatible);
        }

        if (!firstResult.IsSuccess
            || !secondResult.IsSuccess
            || firstResult.Activity is not ActivityInstance firstActivity
            || secondResult.Activity is not ActivityInstance secondActivity)
        {
            return Rejected(
                context.OperationId,
                !firstResult.IsSuccess
                    ? firstResult.FailureCode
                    : secondResult.FailureCode);
        }

        if (context.Deadline <= clock.UtcNow)
        {
            return Rejected(
                context.OperationId,
                FailureCode.DeadlineExpired);
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
            transaction.Context.CorrelationId,
            firstParticipant.ReservationToken,
            firstActivity,
            secondActivity,
            transaction.Context.Deadline);
        var secondCommand = new SwapPrepareCommand(
            transaction.Context.OperationId,
            transaction.Context.CorrelationId,
            secondParticipant.ReservationToken,
            secondActivity,
            firstActivity,
            transaction.Context.Deadline);

        SwapDeliveryResult<SwapPrepareResult> firstPrepare;
        try
        {
            firstPrepare = await first
                .PrepareAsync(coordinatorDeviceId, firstCommand, cancellationToken)
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
                .PrepareAsync(coordinatorDeviceId, secondCommand, cancellationToken)
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

        if (clock.UtcNow >= transaction.Context.Deadline)
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
                transaction.Context.CorrelationId,
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
                write.Transaction.Context.CorrelationId,
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

    private async ValueTask<(
        SwapDeliveryResult<SwapActivitySnapshotResult>? Delivery,
        FailureCode FailureCode)> QueryActivitySafelyAsync(
        ISwapEndpointChannel channel,
        SwapActivitySnapshotQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            SwapDeliveryResult<SwapActivitySnapshotResult> delivery = await channel
                .QueryActivityAsync(
                    coordinatorDeviceId,
                    query,
                    cancellationToken)
                .ConfigureAwait(false);
            return (delivery, FailureCode.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return (null, FailureCode.ProtocolIncompatible);
        }
        catch (IOException)
        {
            return (null, FailureCode.PeerUnavailable);
        }
        catch (TimeoutException)
        {
            return (null, FailureCode.AcknowledgementLost);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (null, FailureCode.InternalFailure);
        }
    }

    private bool MatchesSnapshotResult(
        SwapActivitySnapshotResult result,
        SwapActivitySnapshotQuery query) =>
        result.OperationId == query.Context.OperationId
        && result.CorrelationId == query.Context.CorrelationId
        && result.RequestingDeviceId == coordinatorDeviceId
        && result.TargetDeviceId == query.TargetDeviceId
        && result.RequestedActivityId == query.ActivityId
        && (result.Activity is null
            || result.Activity.Descriptor.Id == query.ActivityId
            && result.Activity.Placement.DeviceId == query.TargetDeviceId
            && result.Activity.Lifecycle == ActivityLifecycle.Active
            && result.Activity.Descriptor.Sensitivity == ActivitySensitivity.Normal);

    private async ValueTask<SwapCoordinatorResult> ApplyRecordedDecisionAsync(
        SwapDecision decision,
        CorrelationId correlationId,
        ISwapEndpointChannel first,
        ISwapEndpointChannel second,
        CancellationToken cancellationToken)
    {
        SwapDeliveryResult<SwapApplyResult> firstApply = await ApplySafelyAsync(
            first,
            correlationId,
            decision,
            cancellationToken).ConfigureAwait(false);
        SwapDeliveryResult<SwapApplyResult> secondApply = await ApplySafelyAsync(
            second,
            correlationId,
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

    private async ValueTask<SwapDeliveryResult<SwapApplyResult>>
        ApplySafelyAsync(
            ISwapEndpointChannel channel,
            CorrelationId correlationId,
            SwapDecision decision,
            CancellationToken cancellationToken)
    {
        try
        {
            return await channel.ApplyDecisionAsync(
                    coordinatorDeviceId,
                    correlationId,
                    decision,
                    cancellationToken)
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
