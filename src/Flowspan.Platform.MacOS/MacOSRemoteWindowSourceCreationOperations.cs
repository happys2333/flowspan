namespace Flowspan.Platform.MacOS;

// Only native system effects are replaceable; creation and NativeSource remain
// the same production orchestration used by enumeration.
internal interface IMacOSRemoteWindowSourceCreationOperations : IMacOSRemoteWindowSourceOperations
{
    public bool TryGetIdentity(nint window, out MacOSRemoteWindowNativeIdentity identity);
    public nint AllocateFilter();
    // Objective-C init-family contract: the nonzero receiver carries one +1.
    // Normal return consumes that allocation ownership; a nonzero result is
    // the sole returned +1 (same or changed self), and nil leaves no owner.
    // A throw confirms neither consumption nor output: do not guess rollback.
    public nint InitializeFilter(nint allocatedFilter, nint window);
    public (double X, double Y, double Width, double Height) GetFrame(nint window);
    public float GetScale(nint filter);
    public bool ValidateWindow(MacOSRemoteWindowNativeIdentity identity, NativeRemoteWindowGeometry geometry);
}

internal interface IMacOSRemoteWindowSourceCreationFailureSink
{
    public void RecordProducerFailure(Exception failure);
}

internal sealed class MacOSRemoteWindowSourceCreationContext
{
    private readonly IMacOSRemoteWindowSourceCreationFailureSink owner;
    internal MacOSRemoteWindowSourceOwnershipPool Pool { get; }
    internal MacOSRemoteWindowSourceOwnershipPool.BatchRecord Batch { get; }
    private int closed;
    internal bool IsClosed => Volatile.Read(ref closed) != 0;

    internal MacOSRemoteWindowSourceCreationContext(
        MacOSRemoteWindowSourceOwnershipPool pool,
        MacOSRemoteWindowSourceOwnershipPool.BatchRecord batch)
    {
        Pool = pool;
        Batch = batch;
        owner = Pool.BindCreationContext(batch, this);
    }

    internal void Close() => Volatile.Write(ref closed, 1);

    internal void FailEnumeration(MacOSRemoteWindowEnumerationOwnershipLedger ledger,
        Exception failure)
    {
        Close();
        if (!Pool.FailEnumeration(ledger)) { return; }
        owner.RecordProducerFailure(failure);
    }

    internal void Fail(MacOSRemoteWindowSourceCreationLedger ledger, Exception failure,
        IMacOSRemoteWindowNativeSource? native = null)
    {
        Close();
        if (!Pool.FailCreation(ledger, native)) { return; }
        // Notification is outside the pool gate. It closes this catalog's
        // authority, not every existing process-wide Capture.
        owner.RecordProducerFailure(failure);
    }
}

internal sealed class MacOSRemoteWindowSourceCreationLedger(
    MacOSRemoteWindowSourceCreationContext context,
    nint borrowedWindow, IMacOSRemoteWindowSourceCreationOperations operations)
{
    internal MacOSRemoteWindowSourceCreationContext Context { get; } = context;
    internal nint BorrowedWindow { get; } = borrowedWindow;
    internal IMacOSRemoteWindowSourceCreationOperations Operations { get; } = operations;
    internal MacOSRemoteWindowSourceOwnershipPool.SourceRecord? Record;
    internal bool AllocationAttempted;
    internal bool AllocationConfirmed;
    internal nint AllocatedFilter;
    internal bool InitializationAttempted;
    // Normal return also confirms the init-family receiver was consumed.
    internal bool InitializationConfirmed;
    internal nint Filter;
    internal bool WindowRetainAttempted;
    internal bool WindowRetainConfirmed;
    internal nint Window;
    internal bool WindowReleaseAttempted;
    internal bool WindowReleaseConfirmed;
    internal bool FilterReleaseAttempted;
    internal bool FilterReleaseConfirmed;
    internal bool CleanupConfirmed;
    internal Exception? Failure;

    internal bool HasUnconfirmedAcquisition =>
        (AllocationAttempted && !AllocationConfirmed)
        || (InitializationAttempted && !InitializationConfirmed)
        || (WindowRetainAttempted && !WindowRetainConfirmed);

    internal bool InitialOwnersCleaned => !HasUnconfirmedAcquisition
        && (Filter == 0 || FilterReleaseConfirmed)
        && (!WindowRetainAttempted || WindowReleaseConfirmed);
}
