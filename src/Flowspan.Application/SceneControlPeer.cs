using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class SceneControlPeer : ISceneControlPeer
{
    private readonly IClock clock;
    private readonly SceneActivityOperationEndpoint endpoint;
    private readonly ISceneRemoteChildJournal journal;
    private readonly ISceneActivityOperationPort operationPort;

    public SceneControlPeer(
        IClock clock,
        SceneActivityOperationEndpoint endpoint,
        ISceneActivityOperationPort operationPort,
        ISceneRemoteChildJournal journal)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.endpoint = endpoint
            ?? throw new ArgumentNullException(nameof(endpoint));
        this.operationPort = operationPort
            ?? throw new ArgumentNullException(nameof(operationPort));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public DeviceId DeviceId => endpoint.DeviceId;

    public ValueTask<SceneSourceLookup> LocateSourceAsync(
        DeviceId coordinatorDeviceId,
        SceneSourceLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinatorDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        if (query.TargetDeviceId != DeviceId)
        {
            throw new ArgumentException(
                "A Scene source query targets another Device.",
                nameof(query));
        }

        return endpoint.LocateSourceAsync(
            coordinatorDeviceId,
            query.ActivityId,
            query.Index,
            query.Context,
            cancellationToken);
    }

    public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        DeviceId coordinatorDeviceId,
        SceneExactSlotQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinatorDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        if (query.TargetDeviceId != DeviceId)
        {
            throw new ArgumentException(
                "A Scene exact-slot query targets another Device.",
                nameof(query));
        }

        return endpoint.InspectExactSlotAsync(
            coordinatorDeviceId,
            query.Item,
            query.Source,
            query.Context,
            cancellationToken);
    }

    public async ValueTask<SceneActivityOperationResult> ExecuteChildAsync(
        DeviceId coordinatorDeviceId,
        SceneRemoteChildInstruction instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinatorDeviceId);
        ArgumentNullException.ThrowIfNull(instruction);
        if (coordinatorDeviceId != instruction.CoordinatorDeviceId
            || instruction.SourceDeviceId != DeviceId)
        {
            throw new ArgumentException(
                "A remote Scene child participants do not match this source peer.",
                nameof(instruction));
        }


        cancellationToken.ThrowIfCancellationRequested();
        SceneRemoteChildJournalStart start;
        try
        {
            start = await journal.LoadOrStartAsync(
                instruction,
                clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                instruction,
                OperationStatus.Recovering,
                FailureCode.InternalFailure);
        }

        if (start.Entry.BindingDigest != instruction.BindingDigest
            || start.Entry.CorrelationId != instruction.Item.ChildCorrelationId)
        {
            return Failed(
                instruction,
                OperationStatus.Rejected,
                FailureCode.OperationIdConflict);
        }

        if (!start.WasCreated)
        {
            if (start.Entry.Status != SceneRemoteChildJournalStatus.Terminal)
            {
                return Failed(
                    instruction,
                    OperationStatus.Recovering,
                    FailureCode.OperationInProgress);
            }

            SceneActivityOperationResult replayed = start.Entry.Result
                ?? throw new InvalidOperationException(
                    "A terminal remote Scene child requires a result.");
            return IsBoundResult(instruction, replayed)
                ? replayed
                : Failed(
                    instruction,
                    OperationStatus.Recovering,
                    FailureCode.InternalFailure);
        }

        SceneActivityOperationResult result;
        if (instruction.Deadline <= clock.UtcNow)
        {
            result = Failed(
                instruction,
                OperationStatus.Rejected,
                FailureCode.DeadlineExpired);
        }
        else if (!endpoint.Allows(coordinatorDeviceId, Capability.SceneApply))
        {
            result = Failed(
                instruction,
                OperationStatus.Rejected,
                FailureCode.CapabilityDenied);
        }
        else
        {
            try
            {
                result = await operationPort.ExecuteAsync(
                    SceneActivityPreparation.Create(instruction),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return Failed(
                    instruction,
                    OperationStatus.Recovering,
                    FailureCode.AcknowledgementLost);
            }
        }

        if (!IsBoundResult(instruction, result))
        {
            return Failed(
                instruction,
                OperationStatus.Recovering,
                FailureCode.InternalFailure);
        }

        if (result.Receipt.Status == OperationStatus.Recovering)
        {
            return result;
        }

        try
        {
            SceneRemoteChildJournalEntry terminal =
                await journal.RecordTerminalAsync(
                    instruction,
                    result,
                    cancellationToken).ConfigureAwait(false);
            return terminal.Result
                ?? throw new InvalidOperationException(
                    "A terminal remote Scene child requires a result.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                instruction,
                OperationStatus.Recovering,
                FailureCode.AcknowledgementLost);
        }
    }

    private SceneActivityOperationResult Failed(
        SceneRemoteChildInstruction instruction,
        OperationStatus status,
        FailureCode failureCode)
    {
        SceneApplyItemPreview item = instruction.Item;
        SceneSourceSelection source = item.Source!;
        OperationKind kind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        return SceneActivityOperationResult.Create(
            OperationReceipt.FromRecordedResult(
                item.ChildOperationId,
                item.ChildCorrelationId,
                kind,
                status,
                source.DeviceId,
                item.Destination.DeviceId,
                item.ActivityId,
                source.Kind,
                source.DescriptorDigest,
                clock.UtcNow,
                failureCode),
                undoCapsule: null);
    }

    private static bool IsBoundResult(
        SceneRemoteChildInstruction instruction,
        SceneActivityOperationResult result)
    {
        SceneApplyItemPreview item = instruction.Item;
        SceneSourceSelection source = item.Source!;
        OperationReceipt receipt = result.Receipt;
        OperationKind kind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        if (receipt.OperationId != item.ChildOperationId
            || receipt.CorrelationId != item.ChildCorrelationId
            || receipt.Kind != kind
            || receipt.SourceDeviceId != source.DeviceId
            || receipt.TargetDeviceId != item.Destination.DeviceId
            || receipt.ActivityId != item.ActivityId
            || (receipt.ActivityKind is not null
                && receipt.ActivityKind != source.Kind)
            || (receipt.DescriptorDigest is not null
                && receipt.DescriptorDigest != source.DescriptorDigest))
        {
            return false;
        }

        UndoCapsuleReference? undo = result.UndoCapsule;
        if (item.Action != SceneApplyAction.Replace || !receipt.IsSuccess)
        {
            return undo is null;
        }

        SceneReplaceTargetSnapshot? target = item.ReplaceTarget;
        return target is not null
            && undo is not null
            && undo.OperationId == item.ChildOperationId
            && undo.CorrelationId == item.ChildCorrelationId
            && undo.TargetActivityId == target.ActivityId
            && undo.TargetDescriptorDigest == target.DescriptorDigest
            && undo.IncomingActivityId == item.ActivityId
            && undo.IncomingDescriptorDigest == source.DescriptorDigest;
    }
}
