using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class InMemoryActivityCatalog : IActivityCatalog
{
    private readonly ConcurrentDictionary<ActivityId, ActivityInstance> activities = new();

    public int Count => activities.Count;

    public bool TryGet(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        return activities.TryGetValue(activityId, out activity);
    }

    public bool TryAdd(ActivityInstance activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activities.TryAdd(activity.Descriptor.Id, activity);
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
