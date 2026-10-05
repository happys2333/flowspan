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

    internal bool TryReserveCatalog(IMacOSRemoteWindowSourceCreationFailureSink owner,
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

    internal IMacOSRemoteWindowSourceCreationFailureSink BindCreationContext(BatchRecord batch,
        MacOSRemoteWindowSourceCreationContext context)
    {
        lock (gate)
        {
            if (batch.Owner is null || batch.Failed || batch.Context is not null)
            {
                throw new InvalidOperationException("macos_source_creation_admission_closed");
            }
            batch.Context = context;
            return batch.Owner.Owner!;
        }
    }

    internal SourceRecord PrepareCreation(BatchRecord batch,
        MacOSRemoteWindowSourceCreationLedger ledger)
    {
        lock (gate)
        {
            if (batch.Owner is null || batch.Failed || ledger.Context.IsClosed
                || !ReferenceEquals(batch.Context, ledger.Context))
            {
                throw new InvalidOperationException("macos_source_creation_admission_closed");
            }
            foreach (SourceRecord? record in batch.Slots)
            {
                if (record is not null && record.Native is null && record.Acquisition is null)
                {
                    record.Acquisition = ledger;
                    return record;
                }
            }
            throw new InvalidOperationException("macos_source_ownership_capacity_exhausted");
        }
    }

    internal void PrepareEnumeration(MacOSRemoteWindowEnumerationOwnershipLedger ledger)
    {
        lock (gate)
        {
            BatchRecord batch = ledger.Context.Batch;
            if (batch.Owner is null || batch.Failed || ledger.Context.IsClosed
                || !ReferenceEquals(batch.Context, ledger.Context)
                || batch.Enumeration is not null)
            {
                throw new InvalidOperationException("macos_source_enumeration_admission_closed");
            }
            batch.Enumeration = ledger;
        }
    }

    internal bool FailEnumeration(MacOSRemoteWindowEnumerationOwnershipLedger ledger)
    {
        lock (gate)
        {
            BatchRecord batch = ledger.Context.Batch;
            if (!ReferenceEquals(batch.Context, ledger.Context)
                || !ReferenceEquals(batch.Enumeration, ledger)) { return false; }
            batch.Failed = true;
            return true;
        }
    }

    internal bool FailCreation(MacOSRemoteWindowSourceCreationLedger ledger,
        IMacOSRemoteWindowNativeSource? native)
    {
        lock (gate)
        {
            SourceRecord record = ledger.Record!;
            if (!ReferenceEquals(record.Acquisition, ledger)
                || (native is not null && !ReferenceEquals(record.Native, native))) { return false; }
            record.Failed = true;
            BatchRecord batch = ledger.Context.Batch;
            if (ReferenceEquals(batch.Context, ledger.Context))
            {
                foreach (SourceRecord? candidate in batch.Slots)
                {
                    if (ReferenceEquals(candidate, record)) { batch.Failed = true; break; }
                }
            }
            return true;
        }
    }

    internal void AttachCreatedSource(MacOSRemoteWindowSourceCreationLedger ledger,
        IMacOSRemoteWindowNativeSource native)
    {
        lock (gate)
        {
            SourceRecord record = ledger.Record!;
            if (!ReferenceEquals(record.Acquisition, ledger) || record.Native is not null
                || ledger.Context.IsClosed
                || !ReferenceEquals(ledger.Context.Batch.Context, ledger.Context))
            {
                throw new InvalidOperationException("macos_source_ownership_transfer_unavailable");
            }
            record.Native = native;
        }
    }

    internal void CompleteCreatedSource(MacOSRemoteWindowSourceCreationLedger ledger,
        IMacOSRemoteWindowNativeSource? native)
    {
        lock (gate)
        {
            SourceRecord record = ledger.Record!;
            if (!ReferenceEquals(record.Acquisition, ledger)
                || !ReferenceEquals(record.Native, native) || record.Failed) { return; }
            ledger.CleanupConfirmed = true;
            // An entry also owes registry cleanup; NativeSource cannot return
            // that obligation before CompleteEntry confirms the entry's end.
            if (record.Entry is not null || record.Registration is not null) { return; }
            CatalogRecord owner = record.Owner!;
            BatchRecord batch = ledger.Context.Batch;
            for (int index = 0; ReferenceEquals(batch.Context, ledger.Context)
                && index < batch.Slots.Length; index++)
            {
                if (ReferenceEquals(batch.Slots[index], record))
                {
                    batch.Slots[index] = null;
                    break;
                }
            }
            ReturnSource(record);
            TryReturnCatalog(owner);
        }
    }

    internal SourceRecord PrepareEntry(BatchRecord batch,
        IMacOSRemoteWindowNativeSource native,
        NativeRemoteWindowSourceRegistration registration)
    {
        lock (gate)
        {
            foreach (SourceRecord? record in batch.Slots)
            {
                if (record is not null && ReferenceEquals(record.Native, native)
                    && record.Registration is null && record.Entry is null)
                {
                    record.Registration = registration;
                    return record;
                }
            }
            foreach (SourceRecord? record in batch.Slots)
            {
                if (record is not null && record.Native is null && record.Acquisition is null)
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

    internal void CompleteBatch(BatchRecord batch,
        MacOSRemoteWindowSourceCreationContext? context = null)
    {
        lock (gate)
        {
            if (context is not null && !ReferenceEquals(batch.Context, context)) { return; }
            if (batch.Failed) { return; }
            if (batch.Enumeration is { IsContentLifecycleEnded: false })
            {
                // Reentrant settlement closes new work, but cannot detach
                // the original graph while content effects are in flight.
                batch.Context?.Close();
                return;
            }
            CatalogRecord owner = batch.Owner!;
            for (int index = 0; index < batch.Slots.Length; index++)
            {
                if (batch.Slots[index] is { } record)
                {
                    if (record.Acquisition is null || record.Acquisition.CleanupConfirmed)
                    {
                        ReturnSource(record);
                    }
                    batch.Slots[index] = null;
                }
            }
            batch.Original = null;
            Array.Clear(batch.KnownSources);
            batch.KnownSourceCount = 0;
            batch.Context?.Close();
            batch.Context = null;
            batch.Enumeration = null;
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
        record.Acquisition = null;
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
        internal IMacOSRemoteWindowSourceCreationFailureSink? Owner;
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
        internal MacOSRemoteWindowSourceCreationLedger? Acquisition;
        internal bool Failed;
    }

    internal sealed class BatchRecord
    {
        internal CatalogRecord? Owner;
        internal IReadOnlyList<IMacOSRemoteWindowNativeSource>? Original;
        internal bool Failed;
        internal MacOSRemoteWindowSourceCreationContext? Context;
        internal MacOSRemoteWindowEnumerationOwnershipLedger? Enumeration;
        internal SourceRecord?[] Slots { get; } = new SourceRecord?[NativeRemoteWindowSourceRegistry.MaximumSources];
        internal IMacOSRemoteWindowNativeSource[] KnownSources { get; } =
            new IMacOSRemoteWindowNativeSource[NativeRemoteWindowSourceRegistry.MaximumSources];
        internal int KnownSourceCount;
    }
}
