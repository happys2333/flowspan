using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

// Replaces only external allocation/runtime effects. Capture copy, disposal,
// invocation and failure containment remain the production Block ABI entries.
internal sealed class MacOSRemoteWindowControlledBlockRuntime : IMacOSRemoteWindowBlockOperations, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<nint, GCHandle> roots = [];
    private readonly Dictionary<nint, Allocation> blocks = [];
    private int activeOperations;
    private bool disposed;
    private int rootAllocationAttempts;
    private int rootFreeAttempts;
    private int copyAttempts;
    private int releaseAttempts;
    private int invokeAttempts;
    private int invokeReturns;

    public Action? BeforeRootAllocation { get; set; }
    public Action? AfterCopyCapture { get; set; }
    public Action? BeforeRelease { get; set; }
    public Exception? RootAllocationFailure { get; set; }
    public Exception? CopyFailure { get; set; }
    public Exception? CopyBeforeEffectFailure { get; set; }
    public Exception? ReleaseFailure { get; set; }
    public Exception? RootFreeFailure { get; set; }
    public string? InvalidRootReturn { get; set; }
    public bool ForeignCopyCapture { get; set; }

    public int RootAllocationAttempts { get { lock (gate) { return rootAllocationAttempts; } } }
    public int RootFreeAttempts { get { lock (gate) { return rootFreeAttempts; } } }
    public int CopyAttempts { get { lock (gate) { return copyAttempts; } } }
    public int ReleaseAttempts { get { lock (gate) { return releaseAttempts; } } }
    public int InvokeAttempts { get { lock (gate) { return invokeAttempts; } } }
    public int InvokeReturns { get { lock (gate) { return invokeReturns; } } }
    public int LiveRoots { get { lock (gate) { return roots.Count; } } }
    public int LiveBlocks { get { lock (gate) { return blocks.Values.Count(static block => block.References != 0); } } }

    public nint AllocateRoot(object target)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            rootAllocationAttempts++;
            activeOperations++;
        }

        try
        {
            BeforeRootAllocation?.Invoke();
            var root = GCHandle.Alloc(InvalidRootReturn == "foreign" ? new object() : target);
            nint pointer = GCHandle.ToIntPtr(root);
            lock (gate) { roots.Add(pointer, root); }
            if (RootAllocationFailure is { } failure) { throw failure; }
            return InvalidRootReturn == "zero" ? 0 : pointer;
        }
        finally { EndOperation(); }
    }

    public void FreeRoot(nint root)
    {
        GCHandle owned;
        lock (gate)
        {
            ThrowIfDisposed();
            rootFreeAttempts++;
            if (!roots.Remove(root, out owned))
            {
                throw new IOException("Controlled root free requires a live test root.");
            }

            activeOperations++;
        }

        try
        {
            owned.Free();
            if (RootFreeFailure is { } failure) { throw failure; }
        }
        finally { EndOperation(); }
    }

    public nint CopyBlock(nint block)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            copyAttempts++;
            activeOperations++;
        }

        try
        {
            if (CopyBeforeEffectFailure is { } beforeFailure) { throw beforeFailure; }
            lock (gate)
            {
                if (blocks.TryGetValue(block, out var existing))
                {
                    if (existing.References == 0)
                    {
                        throw new IOException("Controlled copy requires a live test Block reference.");
                    }

                    existing.References++;
                    // Native heap retains share the physical capture and do
                    // not invoke the production copy helper again.
                    return block;
                }
            }

            nint descriptor = Marshal.ReadIntPtr(block, 24);
            int size = checked((int)Marshal.ReadIntPtr(descriptor, 8));
            var bytes = new byte[size];
            Marshal.Copy(block, bytes, 0, bytes.Length);
            nint copied = Marshal.AllocHGlobal(size);
            Allocation? allocation = null;
            try
            {
                Marshal.Copy(bytes, 0, copied, bytes.Length);
                allocation = new Allocation(copied) { ActiveUses = 1 };
                lock (gate) { blocks.Add(copied, allocation); }
                var copyCapture = Marshal.GetDelegateForFunctionPointer<CopyCapture>(
                    Marshal.ReadIntPtr(descriptor, 16));
                copyCapture(copied, block);
                if (ForeignCopyCapture)
                {
                    var foreignRoot = GCHandle.Alloc(new object());
                    nint foreignPointer = GCHandle.ToIntPtr(foreignRoot);
                    lock (gate) { roots.Add(foreignPointer, foreignRoot); }
                    Marshal.WriteIntPtr(copied, 32, foreignPointer);
                }

                AfterCopyCapture?.Invoke();
                if (CopyFailure is { } failure) { throw failure; }
                return copied;
            }
            finally
            {
                if (allocation is null) { Marshal.FreeHGlobal(copied); }
                else { EndBlockUse(allocation); }
            }
        }
        finally { EndOperation(); }
    }

    public void ReleaseBlock(nint block)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            releaseAttempts++;
            activeOperations++;
        }

        try
        {
            BeforeRelease?.Invoke();
            Allocation? retired = null;
            lock (gate)
            {
                if (!blocks.TryGetValue(block, out var allocation) || allocation.References == 0)
                {
                    throw new IOException("Controlled release requires a live test Block reference.");
                }

                allocation.References--;
                if (allocation.References == 0)
                {
                    allocation.ActiveUses++;
                    retired = allocation;
                }
            }

            if (retired is not null)
            {
                try
                {
                    nint descriptor = Marshal.ReadIntPtr(block, 24);
                    var releaseCapture = Marshal.GetDelegateForFunctionPointer<ReleaseCapture>(
                        Marshal.ReadIntPtr(descriptor, 24));
                    releaseCapture(block);
                }
                finally
                {
                    lock (gate) { retired.CaptureDisposalReturned = true; }
                    EndBlockUse(retired);
                }
            }

            if (ReleaseFailure is { } failure) { throw failure; }
        }
        finally { EndOperation(); }
    }

    public void Invoke(nint block, nint first, nint second)
    {
        Allocation allocation;
        lock (gate)
        {
            ThrowIfDisposed();
            invokeAttempts++;
            if (!blocks.TryGetValue(block, out allocation!) || allocation.References == 0)
            {
                throw new IOException("Controlled invocation requires a live test Block reference.");
            }

            allocation.ActiveUses++;
            activeOperations++;
        }

        try
        {
            var invoke = Marshal.GetDelegateForFunctionPointer<InvokeTwo>(
                Marshal.ReadIntPtr(block, 16));
            invoke(block, first, second);
            lock (gate) { invokeReturns++; }
        }
        finally
        {
            EndBlockUse(allocation);
            EndOperation();
        }
    }

    private void EndBlockUse(Allocation allocation)
    {
        bool free;
        lock (gate)
        {
            allocation.ActiveUses--;
            free = allocation.ActiveUses == 0 && allocation.CaptureDisposalReturned;
            if (free) { blocks.Remove(allocation.Pointer); }
        }

        // This raw memory lease only keeps the literal readable until actual
        // ABI return. Production capture retirement is not delayed by it.
        if (free) { Marshal.FreeHGlobal(allocation.Pointer); }
    }

    private void EndOperation()
    {
        lock (gate) { activeOperations--; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        nint[] allocations;
        GCHandle[] allocatedRoots;
        lock (gate)
        {
            if (disposed) { return; }
            if (activeOperations != 0)
            {
                throw new InvalidOperationException("Controlled Block runtime cleanup requires all test operations to return.");
            }

            disposed = true;
            allocations = [.. blocks.Keys];
            allocatedRoots = [.. roots.Values];
            blocks.Clear();
            roots.Clear();
        }

        // Cleanup of test-owned allocations is deliberately not an effect
        // call: no production capture helper, observer or retirement signal
        // runs, no counters change, and unknown owner debt stays unknown.
        foreach (nint block in allocations) { Marshal.FreeHGlobal(block); }
        foreach (var root in allocatedRoots) { root.Free(); }
    }

    private sealed class Allocation(nint pointer)
    {
        public nint Pointer { get; } = pointer;
        public int References { get; set; } = 1;
        public int ActiveUses { get; set; }
        public bool CaptureDisposalReturned { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CopyCapture(nint destination, nint source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseCapture(nint block);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InvokeTwo(nint block, nint first, nint second);
}
