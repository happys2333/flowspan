namespace Flowspan.Platform.MacOS;

// Separate from Capture permits and delegate association records. All nodes
// exist before enumeration; a failed cleanup only changes links and state.
internal sealed class MacOSRemoteWindowSourceOwnershipPool
{
    internal static MacOSRemoteWindowSourceOwnershipPool Shared { get; } = new();
    private readonly object gate = new();
    private readonly CatalogRecord[] catalogs;
    private readonly SourceRecord[] sources;
    private readonly BatchRecord[] batches;

    internal MacOSRemoteWindowSourceOwnershipPool(
        int catalogCapacity = 8, int sourceCapacity = 1024, int batchCapacity = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catalogCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchCapacity, 1);
        catalogs = new CatalogRecord[catalogCapacity];
        sources = new SourceRecord[sourceCapacity];
        batches = new BatchRecord[batchCapacity];
        for (int index = 0; index < catalogs.Length; index++) { catalogs[index] = new(); }
        for (int index = 0; index < sources.Length; index++) { sources[index] = new(); }
        for (int index = 0; index < batches.Length; index++) { batches[index] = new(); }
    }

    internal bool TryReserveCatalog(MacOSRemoteWindowSourceCatalog owner,
        out CatalogRecord? record)
    {
        lock (gate)
        {
            foreach (CatalogRecord candidate in catalogs)
            {
                if (candidate.Owner is null)
                {
                    candidate.Owner = owner;
                    record = candidate;
                    return true;
                }
            }
            record = null;
            return false;
        }
    }

    internal bool TryReserveBatch(CatalogRecord owner, out BatchRecord? record)
    {
        lock (gate)
        {
            BatchRecord? selected = null;
            foreach (BatchRecord candidate in batches)
            {
                if (candidate.Owner is null) { selected = candidate; break; }
            }
            int available = 0;
            foreach (SourceRecord candidate in sources)
            {
                if (candidate.Owner is null) { available++; }
            }
            if (selected is null || available < selected.Slots.Length)
            {
                record = null;
                return false;
            }

            selected.Owner = owner;
            int next = 0;
            foreach (SourceRecord candidate in sources)
            {
                if (candidate.Owner is not null) { continue; }
                candidate.Owner = owner;
                selected.Slots[next++] = candidate;
                owner.SourceCount++;
                if (next == selected.Slots.Length) { break; }
            }
            owner.BatchCount++;
            record = selected;
            return true;
        }
    }

    internal void AttachBatch(BatchRecord batch,
        IReadOnlyList<IMacOSRemoteWindowNativeSource> original)
    {
        lock (gate) { batch.Original = original; }
    }

    internal SourceRecord PrepareEntry(BatchRecord batch,
        IMacOSRemoteWindowNativeSource native,
        NativeRemoteWindowSourceRegistration registration)
    {
        lock (gate)
        {
            foreach (SourceRecord? record in batch.Slots)
            {
                if (record is not null && record.Native is null)
                {
                    record.Native = native;
                    record.Registration = registration;
                    return record;
                }
            }
            throw new InvalidOperationException("macos_source_ownership_transfer_unavailable");
        }
    }

    internal void AttachEntry(SourceRecord record,
        MacOSRemoteWindowSourceCatalog.SourceEntry entry)
    {
        lock (gate) { record.Entry = entry; }
    }

    internal void PublishEntry(BatchRecord batch, SourceRecord record)
    {
        lock (gate)
        {
            for (int index = 0; index < batch.Slots.Length; index++)
            {
                if (ReferenceEquals(batch.Slots[index], record))
                {
                    batch.Slots[index] = null;
                    return;
                }
            }
            throw new InvalidOperationException("macos_source_ownership_transfer_unavailable");
        }
    }

    internal void FailBatch(BatchRecord batch)
    {
        lock (gate) { batch.Failed = true; }
    }

    internal void CompleteBatch(BatchRecord batch)
    {
        lock (gate)
        {
            if (batch.Failed) { return; }
            CatalogRecord owner = batch.Owner!;
            for (int index = 0; index < batch.Slots.Length; index++)
            {
                if (batch.Slots[index] is { } record)
                {
                    ReturnSource(record);
                    batch.Slots[index] = null;
                }
            }
            batch.Original = null;
            Array.Clear(batch.KnownSources);
            batch.KnownSourceCount = 0;
            batch.Owner = null;
            owner.BatchCount--;
            TryReturnCatalog(owner);
        }
    }

    internal void FailEntry(SourceRecord record,
        MacOSRemoteWindowSourceCatalog.SourceEntry entry)
    {
        lock (gate)
        {
            if (ReferenceEquals(record.Entry, entry)) { record.Failed = true; }
        }
    }

    internal void CompleteEntry(SourceRecord record,
        MacOSRemoteWindowSourceCatalog.SourceEntry entry)
    {
        lock (gate)
        {
            if (record.Failed || !ReferenceEquals(record.Entry, entry)) { return; }
            CatalogRecord owner = record.Owner!;
            ReturnSource(record);
            TryReturnCatalog(owner);
        }
    }

    internal void CloseCatalog(CatalogRecord record, bool registryConfirmed)
    {
        lock (gate)
        {
            record.Closed = true;
            record.Failed |= !registryConfirmed;
            TryReturnCatalog(record);
        }
    }

    internal (int Catalogs, int Sources, int Batches) GetUsage()
    {
        lock (gate)
        {
            int catalogCount = 0, sourceCount = 0, batchCount = 0;
            foreach (CatalogRecord record in catalogs) { if (record.Owner is not null) { catalogCount++; } }
            foreach (SourceRecord record in sources) { if (record.Owner is not null) { sourceCount++; } }
            foreach (BatchRecord record in batches) { if (record.Owner is not null) { batchCount++; } }
            return (catalogCount, sourceCount, batchCount);
        }
    }

    private static void ReturnSource(SourceRecord record)
    {
        record.Owner!.SourceCount--;
        record.Owner = null;
        record.Native = null;
        record.Registration = null;
        record.Entry = null;
        record.Failed = false;
    }

    private static void TryReturnCatalog(CatalogRecord record)
    {
        if (record.Closed && !record.Failed && record.SourceCount == 0 && record.BatchCount == 0)
        {
            record.Owner = null;
            record.Closed = false;
        }
    }

    internal sealed class CatalogRecord
    {
        internal MacOSRemoteWindowSourceCatalog? Owner;
        internal int SourceCount;
        internal int BatchCount;
        internal bool Closed;
        internal bool Failed;
    }

    internal sealed class SourceRecord
    {
        internal CatalogRecord? Owner;
        internal IMacOSRemoteWindowNativeSource? Native;
        internal NativeRemoteWindowSourceRegistration? Registration;
        internal MacOSRemoteWindowSourceCatalog.SourceEntry? Entry;
        internal bool Failed;
    }

    internal sealed class BatchRecord
    {
        internal CatalogRecord? Owner;
        internal IReadOnlyList<IMacOSRemoteWindowNativeSource>? Original;
        internal bool Failed;
        internal SourceRecord?[] Slots { get; } = new SourceRecord?[NativeRemoteWindowSourceRegistry.MaximumSources];
        internal IMacOSRemoteWindowNativeSource[] KnownSources { get; } =
            new IMacOSRemoteWindowNativeSource[NativeRemoteWindowSourceRegistry.MaximumSources];
        internal int KnownSourceCount;
    }
}
