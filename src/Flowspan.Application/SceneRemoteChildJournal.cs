using Flowspan.Domain;

namespace Flowspan.Application;

public enum SceneRemoteChildJournalStatus
{
    Started,
    Terminal,
}

public sealed record SceneRemoteChildJournalEntry
{
    private SceneRemoteChildJournalEntry(
        OperationId operationId,
        CorrelationId correlationId,
        string bindingDigest,
        SceneRemoteChildJournalStatus status,
        DateTimeOffset startedAt,
        SceneActivityOperationResult? result)
    {
        OperationId = operationId;
        CorrelationId = correlationId;
        BindingDigest = bindingDigest;
        Status = status;
        StartedAt = startedAt;
        Result = result;
    }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public string BindingDigest { get; }

    public SceneRemoteChildJournalStatus Status { get; }

    public DateTimeOffset StartedAt { get; }

    public SceneActivityOperationResult? Result { get; }

    internal static SceneRemoteChildJournalEntry Started(
        SceneRemoteChildInstruction instruction,
        DateTimeOffset startedAt) => new(
            instruction.Item.ChildOperationId,
            instruction.Item.ChildCorrelationId,
            instruction.BindingDigest,
            SceneRemoteChildJournalStatus.Started,
            startedAt.ToUniversalTime(),
            null);

    internal SceneRemoteChildJournalEntry Complete(
        SceneActivityOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Receipt.Status == OperationStatus.Recovering)
        {
            throw new ArgumentException(
                "A Recovering remote Scene child is not a terminal journal result.",
                nameof(result));
        }

        return new SceneRemoteChildJournalEntry(
            OperationId,
            CorrelationId,
            BindingDigest,
            SceneRemoteChildJournalStatus.Terminal,
            StartedAt,
            result);
    }

    internal static SceneRemoteChildJournalEntry Restore(
        OperationId operationId,
        CorrelationId correlationId,
        string bindingDigest,
        SceneRemoteChildJournalStatus status,
        DateTimeOffset startedAt,
        SceneActivityOperationResult? result)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        string canonicalDigest = SceneApplyBinding.ValidateDigest(
            bindingDigest,
            nameof(bindingDigest));
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        bool validShape = status switch
        {
            SceneRemoteChildJournalStatus.Started => result is null,
            SceneRemoteChildJournalStatus.Terminal =>
                result is not null
                && result.Receipt.Status != OperationStatus.Recovering
                && result.Receipt.OperationId == operationId
                && result.Receipt.CorrelationId == correlationId
                && (result.UndoCapsule is null
                    || (result.UndoCapsule.OperationId == operationId
                        && result.UndoCapsule.CorrelationId == correlationId)),
            _ => false,
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "The remote Scene child journal entry shape is invalid.",
                nameof(result));
        }

        return new SceneRemoteChildJournalEntry(
            operationId,
            correlationId,
            canonicalDigest,
            status,
            startedAt.ToUniversalTime(),
            result);
    }
}

public sealed record SceneRemoteChildJournalStart(
    SceneRemoteChildJournalEntry Entry,
    bool WasCreated);

public interface ISceneRemoteChildJournal
{
    public ValueTask<SceneRemoteChildJournalStart> LoadOrStartAsync(
        SceneRemoteChildInstruction instruction,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    public ValueTask<SceneRemoteChildJournalEntry> RecordTerminalAsync(
        SceneRemoteChildInstruction instruction,
        SceneActivityOperationResult result,
        CancellationToken cancellationToken);
}

public sealed class InMemorySceneRemoteChildJournal : ISceneRemoteChildJournal
{
    private readonly Lock gate = new();
    private readonly Dictionary<OperationId, SceneRemoteChildJournalEntry> entries = [];

    public ValueTask<SceneRemoteChildJournalStart> LoadOrStartAsync(
        SceneRemoteChildInstruction instruction,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (entries.TryGetValue(
                    instruction.Item.ChildOperationId,
                    out SceneRemoteChildJournalEntry? existing))
            {
                return ValueTask.FromResult(
                    new SceneRemoteChildJournalStart(existing, WasCreated: false));
            }

            SceneRemoteChildJournalEntry created =
                SceneRemoteChildJournalEntry.Started(instruction, startedAt);
            entries.Add(created.OperationId, created);
            return ValueTask.FromResult(
                new SceneRemoteChildJournalStart(created, WasCreated: true));
        }
    }

    public ValueTask<SceneRemoteChildJournalEntry> RecordTerminalAsync(
        SceneRemoteChildInstruction instruction,
        SceneActivityOperationResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!entries.TryGetValue(
                    instruction.Item.ChildOperationId,
                    out SceneRemoteChildJournalEntry? existing)
                || existing.BindingDigest != instruction.BindingDigest)
            {
                throw new InvalidOperationException(
                    "The remote Scene child journal binding changed before completion.");
            }

            if (existing.Status == SceneRemoteChildJournalStatus.Terminal)
            {
                if (existing.Result != result)
                {
                    throw new InvalidOperationException(
                        "The remote Scene child terminal result changed.");
                }

                return ValueTask.FromResult(existing);
            }

            SceneRemoteChildJournalEntry terminal = existing.Complete(result);
            entries[terminal.OperationId] = terminal;
            return ValueTask.FromResult(terminal);
        }
    }
}
