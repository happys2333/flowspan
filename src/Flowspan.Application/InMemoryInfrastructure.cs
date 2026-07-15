using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class InMemoryActivityCatalog : IActivityCatalog
{
    private readonly Dictionary<ActivityId, ActivityInstance> activities = [];
    private readonly Lock gate = new();

    public int Count
    {
        get
        {
            lock (gate)
            {
                return activities.Count;
            }
        }
    }

    public IReadOnlyList<ActivityInstance> Snapshot()
    {
        lock (gate)
        {
            return activities.Values
                .OrderBy(
                    static activity => activity.Descriptor.Id.ToString(),
                    StringComparer.Ordinal)
                .ToArray();
        }
    }

    public bool TryGet(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        lock (gate)
        {
            return activities.TryGetValue(activityId, out activity);
        }
    }

    public bool TryAdd(ActivityInstance activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (gate)
        {
            return activities.TryAdd(activity.Descriptor.Id, activity);
        }
    }

    public bool TryUpdate(ActivityInstance expected, ActivityInstance replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (expected.Descriptor.Id != replacement.Descriptor.Id)
        {
            throw new ArgumentException(
                "An Activity update cannot change its ID.",
                nameof(replacement));
        }

        lock (gate)
        {
            if (!activities.TryGetValue(expected.Descriptor.Id, out ActivityInstance? current)
                || current != expected)
            {
                return false;
            }

            activities[expected.Descriptor.Id] = replacement;
            return true;
        }
    }

    public bool TrySwapReplace(ActivityInstance expected, ActivityInstance replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        lock (gate)
        {
            if (!activities.TryGetValue(expected.Descriptor.Id, out ActivityInstance? current)
                || current != expected
                || (expected.Descriptor.Id != replacement.Descriptor.Id
                    && activities.ContainsKey(replacement.Descriptor.Id)))
            {
                return false;
            }

            activities.Remove(expected.Descriptor.Id);
            activities.Add(replacement.Descriptor.Id, replacement);
            return true;
        }
    }
}

public sealed class InMemoryOperationJournal : IOperationJournal
{
    private readonly Lock gate = new();
    private readonly Dictionary<OperationId, Entry> entries = [];

    public async ValueTask<JournalExecutionResult> ExecuteOnceAsync(
        OperationId operationId,
        string requestDigest,
        Func<CancellationToken, ValueTask<OperationReceipt>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestDigest);
        ArgumentNullException.ThrowIfNull(operation);

        Entry entry;
        bool execute;

        lock (gate)
        {
            if (entries.TryGetValue(operationId, out Entry? existing))
            {
                if (!StringComparer.Ordinal.Equals(existing.RequestDigest, requestDigest))
                {
                    return new JournalExecutionResult(null, false, true);
                }

                entry = existing;
                execute = false;
            }
            else
            {
                entry = new Entry(requestDigest);
                entries.Add(operationId, entry);
                execute = true;
            }
        }

        if (!execute)
        {
            OperationReceipt replay = await entry.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return new JournalExecutionResult(replay, true, false);
        }

        try
        {
            OperationReceipt receipt = await operation(cancellationToken).ConfigureAwait(false);
            entry.Completion.TrySetResult(receipt);
            if (receipt.Status is OperationStatus.Failed or OperationStatus.Recovering)
            {
                lock (gate)
                {
                    if (entries.TryGetValue(operationId, out Entry? current)
                        && ReferenceEquals(current, entry))
                    {
                        entries.Remove(operationId);
                    }
                }
            }

            return new JournalExecutionResult(receipt, false, false);
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (entries.TryGetValue(operationId, out Entry? current)
                    && ReferenceEquals(current, entry))
                {
                    entries.Remove(operationId);
                }
            }

            entry.Completion.TrySetException(exception);
            throw;
        }
    }

    private sealed class Entry
    {
        public Entry(string requestDigest)
        {
            RequestDigest = requestDigest;
            Completion = new TaskCompletionSource<OperationReceipt>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string RequestDigest { get; }

        public TaskCompletionSource<OperationReceipt> Completion { get; }
    }
}

public sealed class ActivityAdapterRegistry
{
    private readonly IReadOnlyList<IActivityAdapter> adapters;

    public ActivityAdapterRegistry(IEnumerable<IActivityAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        this.adapters = adapters.ToArray();
    }

    public bool TryFind(ActivityKind kind, out IActivityAdapter? adapter)
    {
        ArgumentNullException.ThrowIfNull(kind);
        adapter = adapters.FirstOrDefault(candidate => candidate.Kind == kind);
        return adapter is not null;
    }
}
