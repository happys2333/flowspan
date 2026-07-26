using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Application;

public enum SceneCompensationItemOutcome
{
    Committed,
    Rejected,
    Failed,
    Recovering,
    Cancelled,
}

public enum SceneCompensationStatus
{
    NothingToUndo,
    Completed,
    PartiallyCompleted,
    Recovering,
    Cancelled,
}

public sealed record SceneCompensationItemResult
{
    private SceneCompensationItemResult(
        int sceneIndex,
        DeviceId targetDeviceId,
        UndoCapsuleId capsuleId,
        OperationId operationId,
        CorrelationId correlationId,
        SceneCompensationItemOutcome outcome,
        FailureCode failureCode,
        DateTimeOffset occurredAt)
    {
        SceneIndex = sceneIndex;
        TargetDeviceId = targetDeviceId;
        CapsuleId = capsuleId;
        OperationId = operationId;
        CorrelationId = correlationId;
        Outcome = outcome;
        FailureCode = failureCode;
        OccurredAt = occurredAt;
    }

    public int SceneIndex { get; }

    public DeviceId TargetDeviceId { get; }

    public UndoCapsuleId CapsuleId { get; }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public SceneCompensationItemOutcome Outcome { get; }

    public FailureCode FailureCode { get; }

    public DateTimeOffset OccurredAt { get; }

    internal static SceneCompensationItemResult FromUndo(
        int sceneIndex,
        DeviceId targetDeviceId,
        UndoReplaceResult result)
    {
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(result);
        SceneCompensationItemOutcome outcome = result.Status switch
        {
            OperationStatus.Committed =>
                SceneCompensationItemOutcome.Committed,
            OperationStatus.Rejected =>
                SceneCompensationItemOutcome.Rejected,
            OperationStatus.Failed => SceneCompensationItemOutcome.Failed,
            OperationStatus.Recovering =>
                SceneCompensationItemOutcome.Recovering,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        return new SceneCompensationItemResult(
            sceneIndex,
            targetDeviceId,
            result.CapsuleId,
            result.OperationId,
            result.CorrelationId,
            outcome,
            result.FailureCode,
            result.OccurredAt.ToUniversalTime());
    }

    internal static SceneCompensationItemResult Cancelled(
        int sceneIndex,
        UndoCapsuleReference capsule,
        OperationContext context,
        DateTimeOffset occurredAt) =>
        new(
            sceneIndex,
            capsule.TargetDeviceId,
            capsule.Id,
            context.OperationId,
            context.CorrelationId,
            SceneCompensationItemOutcome.Cancelled,
            FailureCode.None,
            occurredAt.ToUniversalTime());

    public override string ToString() =>
        $"Scene compensation item {SceneIndex} ({Outcome})";
}

public sealed record SceneCompensationResult
{
    private SceneCompensationResult(
        OperationId parentOperationId,
        ImmutableArray<SceneCompensationItemResult> items,
        SceneCompensationStatus status)
    {
        ParentOperationId = parentOperationId;
        Items = items;
        Status = status;
    }

    public OperationId ParentOperationId { get; }

    public ImmutableArray<SceneCompensationItemResult> Items { get; }

    public SceneCompensationStatus Status { get; }

    internal static SceneCompensationResult Create(
        OperationId parentOperationId,
        IEnumerable<SceneCompensationItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(parentOperationId);
        ArgumentNullException.ThrowIfNull(items);
        ImmutableArray<SceneCompensationItemResult> ordered =
            items.ToImmutableArray();
        if (ordered.Length > ScenePlan.MaximumActivities
            || ordered.Any(static item => item is null)
            || ordered.Select(static item => item.SceneIndex).Distinct().Count()
                != ordered.Length
            || !ordered
                .Select(static item => item.SceneIndex)
                .SequenceEqual(ordered
                    .Select(static item => item.SceneIndex)
                    .OrderDescending()))
        {
            throw new ArgumentException(
                "Scene compensation results must be bounded, distinct, and in reverse Scene order.",
                nameof(items));
        }

        SceneCompensationStatus status = ordered.Length == 0
            ? SceneCompensationStatus.NothingToUndo
            : ordered.Any(static item =>
                item.Outcome == SceneCompensationItemOutcome.Recovering)
                ? SceneCompensationStatus.Recovering
                : ordered.All(static item =>
                    item.Outcome == SceneCompensationItemOutcome.Committed)
                    ? SceneCompensationStatus.Completed
                    : ordered.All(static item =>
                        item.Outcome == SceneCompensationItemOutcome.Cancelled)
                        ? SceneCompensationStatus.Cancelled
                        : SceneCompensationStatus.PartiallyCompleted;
        return new SceneCompensationResult(parentOperationId, ordered, status);
    }

    public override string ToString() =>
        $"Scene compensation for {ParentOperationId} ({Status})";
}

public sealed class SceneApplyCompensator
{
    private const string OperationDomain =
        "flowspan.scene-compensation-operation/v1";
    private const string CorrelationDomain =
        "flowspan.scene-compensation-correlation/v1";

    private readonly IClock clock;
    private readonly ISceneActivityOperationPort operationPort;

    public SceneApplyCompensator(
        IClock clock,
        ISceneActivityOperationPort operationPort)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.operationPort = operationPort
            ?? throw new ArgumentNullException(nameof(operationPort));
    }

    public async ValueTask<SceneCompensationResult> CompensateAsync(
        SceneApplyResult applyResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applyResult);
        SceneApplyItemResult[] eligible = applyResult.Items
            .Where(static item =>
                item.Action == SceneApplyAction.Replace
                && item.RequestedSourceDisposition
                    == SceneSourceDisposition.PreserveSource
                && item.Outcome is SceneApplyItemOutcome.Committed
                    or SceneApplyItemOutcome.CommittedWithWarning
                && item.UndoCapsule is not null)
            .OrderByDescending(static item => item.Index)
            .ToArray();
        var results = new List<SceneCompensationItemResult>(eligible.Length);
        bool cancelled = false;
        foreach (SceneApplyItemResult item in eligible)
        {
            UndoCapsuleReference capsule = item.UndoCapsule!;
            OperationContext context = CreateStableContext(
                applyResult,
                item,
                capsule);
            if (cancelled || cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                results.Add(SceneCompensationItemResult.Cancelled(
                    item.Index,
                    capsule,
                    context,
                    clock.UtcNow));
                continue;
            }

            UndoReplaceResult undo;
            if (capsule.ExpiresAt.ToUniversalTime() <= clock.UtcNow)
            {
                undo = UndoReplaceResult.Rejected(
                    context,
                    capsule.Id,
                    FailureCode.UndoCapsuleExpired,
                    clock.UtcNow);
            }
            else
            {
                try
                {
                    undo = await operationPort.UndoReplaceAsync(
                        capsule,
                        context,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    undo = UndoReplaceResult.Recovering(
                        context,
                        capsule.Id,
                        FailureCode.AcknowledgementLost,
                        clock.UtcNow);
                }
                catch (Exception)
                {
                    undo = UndoReplaceResult.Recovering(
                        context,
                        capsule.Id,
                        FailureCode.InternalFailure,
                        clock.UtcNow);
                }
            }

            if (undo.OperationId != context.OperationId
                || undo.CorrelationId != context.CorrelationId
                || undo.CapsuleId != capsule.Id)
            {
                undo = UndoReplaceResult.Recovering(
                    context,
                    capsule.Id,
                    FailureCode.InternalFailure,
                    clock.UtcNow);
            }

            results.Add(SceneCompensationItemResult.FromUndo(
                item.Index,
                capsule.TargetDeviceId,
                undo));
        }

        return SceneCompensationResult.Create(
            applyResult.ParentOperationId,
            results);
    }

    internal static OperationContext CreateStableContext(
        SceneApplyResult applyResult,
        SceneApplyItemResult item,
        UndoCapsuleReference capsule)
    {
        ArgumentNullException.ThrowIfNull(applyResult);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(capsule);
        OperationId operationId = OperationId.From(DeriveGuid(
            OperationDomain,
            applyResult.ParentOperationId,
            item.Index,
            capsule.Id));
        CorrelationId correlationId = CorrelationId.From(DeriveGuid(
            CorrelationDomain,
            applyResult.ParentOperationId,
            item.Index,
            capsule.Id));
        return OperationContext.Create(
            operationId,
            correlationId,
            capsule.ExpiresAt.ToUniversalTime());
    }

    private static Guid DeriveGuid(
        string domain,
        OperationId parentOperationId,
        int sceneIndex,
        UndoCapsuleId capsuleId)
    {
        string material = string.Join(
            '\n',
            domain,
            parentOperationId.ToString(),
            sceneIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            capsuleId.ToString());
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }
}
