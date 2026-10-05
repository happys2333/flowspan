using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Flowspan.Platform.MacOS;

// Owns one native heap-block reference. Pointer is borrowed: the caller must
// keep this owner alive until the native API has copied it, and must serialize
// that handoff with Dispose. Disposing this owner does not cancel native copies.
internal sealed unsafe partial class MacOSRemoteWindowBlock : IDisposable
{
    private readonly object releaseGate = new();
    private readonly BlockState state;
    private readonly IMacOSRemoteWindowBlockOperations? stagedOperations;
    private readonly int stagedArgumentCount;
    private nint pointer;
    private bool releaseInProgress;
    private int acquisitionAttempted;
    private int copyAttempted;
    private int copyConfirmed;
    private int ownedReleaseAttempted;
    private int ownedReleaseConfirmed;
    private bool acquisitionInProgress;
    private bool disposeRequested;
    private readonly InvalidOperationException allocationUnavailable = new(
        "Native completion block allocation is unavailable.");
    private readonly InvalidOperationException acquisitionUnavailable = new(
        "Completion acquisition is closed or has already been attempted.");

    private MacOSRemoteWindowBlock(
        Delegate action,
        int argumentCount,
        Action<Exception>? failure,
        Action? completed,
        IMacOSRemoteWindowBlockOperations operations)
    {
        stagedOperations = operations;
        stagedArgumentCount = argumentCount;
        state = new BlockState(action, failure, completed, allocateRoot: false);
    }

    private MacOSRemoteWindowBlock(
        Delegate action,
        int argumentCount,
        Action<Exception>? failure,
        Action? completed)
    {
        state = new BlockState(action, failure, completed);
        try
        {
            var literal = new BlockLiteral
            {
                Isa = Abi.StackIsa,
                Flags = (1 << 25) | (1 << 30), // copy/dispose helpers + signature
                Invoke = argumentCount switch
                {
                    0 => (nint)(delegate* unmanaged[Cdecl]<nint, void>)&InvokeZero,
                    1 => (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&InvokeOne,
                    _ => (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&InvokeTwo,
                },
                Descriptor = argumentCount switch
                {
                    0 => Abi.ZeroArgumentDescriptor,
                    1 => Abi.OneArgumentDescriptor,
                    _ => Abi.TwoArgumentDescriptor,
                },
                Context = state.Handle,
            };

            pointer = Native.BlockCopy((nint)(&literal));
            if (pointer == 0)
            {
                throw new InvalidOperationException(
                    "Native completion block allocation is unavailable.");
            }

            // A caught copy-helper failure must not publish a block whose
            // capture was never rooted independently of the temporary literal.
            if (((BlockLiteral*)pointer)->Context == 0)
            {
                ReleaseOwnerReference();
                throw state.FirstFailure ?? new InvalidOperationException(
                    "Native completion block capture could not be copied.");
            }
        }
        finally
        {
            state.ReleaseReference();
        }
    }

    ~MacOSRemoteWindowBlock()
    {
        if (stagedOperations is null) { ReleaseOwnerReference(); }
    }

    public nint Pointer => stagedOperations is not null &&
        (Volatile.Read(ref acquisitionInProgress) ||
            Volatile.Read(ref ownedReleaseAttempted) != 0 || !CopyConfirmed)
        ? 0 : Volatile.Read(ref pointer);

    internal bool IsReleased => stagedOperations is null
        ? Volatile.Read(ref pointer) == 0
        : Volatile.Read(ref ownedReleaseConfirmed) != 0;

    internal Exception? FirstFailure => state.FirstFailure;

    internal bool RootAllocationAttempted => state.RootAllocationAttempted;

    internal bool RootAllocationConfirmed => state.RootAllocationConfirmed;

    internal bool CopyAttempted => Volatile.Read(ref copyAttempted) != 0;

    internal bool AcquisitionAttempted => Volatile.Read(ref acquisitionAttempted) != 0;

    internal bool CopyConfirmed => Volatile.Read(ref copyConfirmed) != 0;

    internal bool OwnedReleaseAttempted => Volatile.Read(ref ownedReleaseAttempted) != 0;

    internal bool OwnedReleaseConfirmed => Volatile.Read(ref ownedReleaseConfirmed) != 0;

    internal bool NativeCaptureRetired => state.RootReleaseConfirmed;

    internal bool RootReleaseAttempted => state.RootReleaseAttempted;

    internal bool RootReleaseConfirmed => state.RootReleaseConfirmed;

    internal Task NativeCaptureRetirement => state.NativeCaptureRetirement;

    internal Task ManagedInvocationDrain => state.ManagedInvocationDrain;

    internal bool TryGetKnownLifetimeJoin(out Task? join)
    {
        join = null;
        lock (releaseGate)
        {
            // Only this actual staged primitive can qualify its own effects.
            // Unknown acquisition, caller release or root free never becomes
            // a recoverable lifetime wait merely because a Task is pending.
            if (stagedOperations is null || acquisitionInProgress || releaseInProgress
                || !RootAllocationAttempted || !RootAllocationConfirmed
                || !CopyAttempted || !CopyConfirmed
                || !OwnedReleaseAttempted || !OwnedReleaseConfirmed
                || !state.IsRootRetirementKnown)
            {
                return false;
            }
        }

        // These are the original lifetime Tasks, even if they became terminal
        // after a previous cleanup observation. No synthetic success or retry.
        join = Task.WhenAll(NativeCaptureRetirement, ManagedInvocationDrain);
        return true;
    }

    internal int ActiveManagedInvocations => state.ActiveManagedInvocations;

    // Observation only: heap retains can share one physical capture. This
    // count never authorizes release, retirement, join or batch settlement.
    internal long PhysicalCaptureCopyCount => state.PhysicalCaptureCopyCount;

    internal static MacOSRemoteWindowBlock Prepare(
        Action<nint> action,
        Action<Exception>? failure = null,
        Action? completed = null) =>
        PrepareWithOperations(action, NativeBlockOperations.Instance, failure, completed);

    internal static MacOSRemoteWindowBlock PrepareWithOperations(
        Action<nint> action,
        IMacOSRemoteWindowBlockOperations operations,
        Action<Exception>? failure = null,
        Action? completed = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(operations);
        return new MacOSRemoteWindowBlock(action, 1, failure, completed, operations);
    }

    internal static MacOSRemoteWindowBlock Prepare(
        Action<nint, nint> action,
        Action<Exception>? failure = null,
        Action? completed = null) =>
        PrepareWithOperations(action, NativeBlockOperations.Instance, failure, completed);

    internal static MacOSRemoteWindowBlock PrepareWithOperations(
        Action<nint, nint> action,
        IMacOSRemoteWindowBlockOperations operations,
        Action<Exception>? failure = null,
        Action? completed = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(operations);
        return new MacOSRemoteWindowBlock(action, 2, failure, completed, operations);
    }

    internal void AcquireCopy()
    {
        if (stagedOperations is null)
        {
            throw new InvalidOperationException("Only a prepared completion can acquire a native copy.");
        }

        lock (releaseGate)
        {
            if (acquisitionAttempted != 0 || disposeRequested)
            {
                throw acquisitionUnavailable;
            }

            Volatile.Write(ref acquisitionAttempted, 1);
            Volatile.Write(ref acquisitionInProgress, true);
        }

        Exception? acquisitionFailure = null;
        Exception? rootCleanupFailure = null;
        try
        {
            nint isa = 0;
            if (stagedOperations is NativeBlockOperations)
            {
                if (!OperatingSystem.IsMacOS() ||
                    RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
                {
                    throw new PlatformNotSupportedException("Native completion blocks require macOS arm64.");
                }

                isa = Abi.StackIsa;
            }

            nint descriptor = stagedArgumentCount == 1
                ? CaptureMetadata.OneArgumentDescriptor : CaptureMetadata.TwoArgumentDescriptor;
            state.AcquireRoot(stagedOperations);
            var literal = new BlockLiteral
            {
                Isa = isa,
                Flags = (1 << 25) | (1 << 30),
                Invoke = stagedArgumentCount == 1
                    ? (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&InvokeOne
                    : (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&InvokeTwo,
                Descriptor = descriptor,
                Context = state.Handle,
            };
            Volatile.Write(ref copyAttempted, 1);
            pointer = stagedOperations.CopyBlock((nint)(&literal));
            if (pointer == 0 || ((BlockLiteral*)pointer)->Context != state.Handle)
            {
                throw allocationUnavailable;
            }

            Volatile.Write(ref copyConfirmed, 1);
        }
        catch (Exception exception)
        {
            acquisitionFailure = exception;
            state.ReportFailure(exception);
        }
        finally
        {
            if (state.RootAllocationConfirmed)
            {
                try { state.ReleaseReference(); }
                catch (Exception exception)
                {
                    rootCleanupFailure = exception;
                    state.ReportFailure(exception);
                }
            }

            bool releaseRequested;
            lock (releaseGate)
            {
                Volatile.Write(ref acquisitionInProgress, false);
                releaseRequested = disposeRequested;
            }

            if (releaseRequested) { ReleaseStagedOwnerReference(); }
        }

        Exception? selectedFailure = state.FirstFailure is { } observed
            ? MacOSRemoteWindowFailure.FindFatal(observed) : null;
        selectedFailure ??= acquisitionFailure ?? rootCleanupFailure ?? state.FirstFailure;
        if (selectedFailure is not null)
        {
            ExceptionDispatchInfo.Capture(selectedFailure).Throw();
        }
    }

    // completed runs per invocation after the action and its fault observer.
    // Observers must be thread-safe if the native API invokes concurrently.
    public static MacOSRemoteWindowBlock Create(
        Action action,
        Action<Exception>? failure = null,
        Action? completed = null) => CreateCore(action, 0, failure, completed);

    // These signatures describe borrowed Objective-C object parameters. The
    // callback, rather than the block wrapper, owns any explicit retain/release.
    public static MacOSRemoteWindowBlock Create(
        Action<nint> action,
        Action<Exception>? failure = null,
        Action? completed = null) => CreateCore(action, 1, failure, completed);

    public static MacOSRemoteWindowBlock Create(
        Action<nint, nint> action,
        Action<Exception>? failure = null,
        Action? completed = null) => CreateCore(action, 2, failure, completed);

    public void Dispose()
    {
        ReleaseOwnerReference();
        if (IsReleased)
        {
            GC.SuppressFinalize(this);
        }
    }

    private static MacOSRemoteWindowBlock CreateCore(
        Delegate action,
        int argumentCount,
        Action<Exception>? failure,
        Action? completed)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!OperatingSystem.IsMacOS() ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            throw new PlatformNotSupportedException(
                "Native completion blocks require macOS arm64.");
        }

        // Ensure metadata exists before allocating a per-block managed root.
        _ = Abi.StackIsa;
        return new MacOSRemoteWindowBlock(action, argumentCount, failure, completed);
    }

    private void ReleaseOwnerReference()
    {
        if (stagedOperations is not null)
        {
            ReleaseStagedOwnerReference();
            return;
        }

        if (IsReleased)
        {
            return;
        }

        bool ownsRelease = false;
        bool nativeReleased = false;
        try
        {
            nint ownedPointer;
            lock (releaseGate)
            {
                ownedPointer = Volatile.Read(ref pointer);
                if (ownedPointer == 0 || Volatile.Read(ref releaseInProgress))
                {
                    return;
                }

                releaseInProgress = true;
                ownsRelease = true;
            }

            // Native release can synchronously invoke the capture disposer and
            // its fault observer. Neither runs while the selection gate is held.
            Native.BlockRelease(ownedPointer);
            nativeReleased = true;
        }
        catch (Exception exception)
        {
            // A binding failure before native entry retains the pointer/root
            // for retry. Reverse-entry faults are contained in ReleaseCapture.
            // Keep releaseInProgress set while notifying a reentrant observer.
            state.ReportFailure(exception);
        }
        finally
        {
            if (ownsRelease)
            {
                if (nativeReleased)
                {
                    Volatile.Write(ref pointer, 0);
                }

                Volatile.Write(ref releaseInProgress, false);
            }
        }
    }

    private void ReleaseStagedOwnerReference()
    {
        nint ownedPointer;
        lock (releaseGate)
        {
            disposeRequested = true;
            if (acquisitionInProgress) { return; }
            ownedPointer = Volatile.Read(ref pointer);
            if (ownedPointer == 0 || ownedReleaseAttempted != 0) { return; }
            Volatile.Write(ref ownedReleaseAttempted, 1);
        }

        try
        {
            stagedOperations!.ReleaseBlock(ownedPointer);
            Volatile.Write(ref ownedReleaseConfirmed, 1);
            Volatile.Write(ref pointer, 0);
        }
        catch (Exception exception)
        {
            // The runtime may already have consumed the reference. Keep the
            // durable unknown fact, hide the borrowed pointer and never retry.
            state.ReportFailure(exception);
        }
    }

    private static BlockState? StateOf(nint block)
    {
        nint context = block == 0 ? 0 : ((BlockLiteral*)block)->Context;
        return context == 0 ? null : GCHandle.FromIntPtr(context).Target as BlockState;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CopyCapture(nint destination, nint source)
    {
        BlockState? capturedState = null;
        try
        {
            ((BlockLiteral*)destination)->Context = 0;
            capturedState = StateOf(source);
            if (capturedState is null)
            {
                return;
            }

            capturedState.RecordPhysicalCaptureCopy();
            capturedState.AddReference();
            ((BlockLiteral*)destination)->Context = capturedState.Handle;
        }
        catch (Exception exception)
        {
            capturedState?.ReportFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReleaseCapture(nint block)
    {
        BlockState? capturedState = null;
        try
        {
            capturedState = StateOf(block);
            capturedState?.ReleaseReference();
        }
        catch (Exception exception)
        {
            capturedState?.ReportFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InvokeZero(nint block)
    {
        BlockState? capturedState = null;
        try
        {
            capturedState = StateOf(block);
            if (capturedState is not null && capturedState.TryEnterInvocation())
            {
                ((Action)capturedState.Callback)();
            }
            else { capturedState = null; }
        }
        catch (Exception exception)
        {
            capturedState?.ReportFailure(exception);
        }
        finally
        {
            capturedState?.ReportInvocationExit();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InvokeOne(nint block, nint argument)
    {
        BlockState? capturedState = null;
        try
        {
            capturedState = StateOf(block);
            if (capturedState is not null && capturedState.TryEnterInvocation())
            {
                ((Action<nint>)capturedState.Callback)(argument);
            }
            else { capturedState = null; }
        }
        catch (Exception exception)
        {
            capturedState?.ReportFailure(exception);
        }
        finally
        {
            capturedState?.ReportInvocationExit();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InvokeTwo(nint block, nint firstArgument, nint secondArgument)
    {
        BlockState? capturedState = null;
        try
        {
            capturedState = StateOf(block);
            if (capturedState is not null && capturedState.TryEnterInvocation())
            {
                ((Action<nint, nint>)capturedState.Callback)(firstArgument, secondArgument);
            }
            else { capturedState = null; }
        }
        catch (Exception exception)
        {
            capturedState?.ReportFailure(exception);
        }
        finally
        {
            capturedState?.ReportInvocationExit();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public nint Descriptor;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
        public nint Copy;
        public nint Dispose;
        public nint Signature;
    }

    private sealed class BlockState
    {
        private readonly Action? completed;
        private readonly Action<Exception>? failure;
        private Exception? firstFailure;
        private OutOfMemoryException? firstFatal;
        private readonly bool staged;
        private readonly InvalidOperationException rootUnavailable = new(
            "Native completion root allocation is unavailable.");
        private nint handle;
        private int references = 1;
        private int rootAllocationAttempted;
        private int rootAllocationConfirmed;
        private IMacOSRemoteWindowBlockOperations? operations;
        private int rootReleaseAttempted;
        private int rootReleaseConfirmed;
        private readonly TaskCompletionSource nativeCaptureRetirement = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource managedInvocationDrain = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object invocationGate = new();
        private int activeManagedInvocations;
        private long physicalCaptureCopyCount;

        public BlockState(
            Delegate callback,
            Action<Exception>? failure,
            Action? completed,
            bool allocateRoot = true)
        {
            Callback = callback;
            this.failure = failure;
            this.completed = completed;
            staged = !allocateRoot;
            if (allocateRoot) { handle = GCHandle.ToIntPtr(GCHandle.Alloc(this)); }
        }

        public Delegate Callback { get; }

        public Exception? FirstFailure => staged && Volatile.Read(ref firstFatal) is { } fatal
            ? fatal : Volatile.Read(ref firstFailure);

        public nint Handle => Volatile.Read(ref handle);

        public bool RootAllocationAttempted => Volatile.Read(ref rootAllocationAttempted) != 0;

        public bool RootAllocationConfirmed => Volatile.Read(ref rootAllocationConfirmed) != 0;

        public bool RootReleaseAttempted => Volatile.Read(ref rootReleaseAttempted) != 0;

        public bool RootReleaseConfirmed => Volatile.Read(ref rootReleaseConfirmed) != 0;

        public bool IsRootRetirementKnown
        {
            get
            {
                lock (invocationGate)
                {
                    return rootReleaseAttempted == 0 || RootReleaseConfirmed;
                }
            }
        }

        public Task NativeCaptureRetirement => nativeCaptureRetirement.Task;

        public Task ManagedInvocationDrain => managedInvocationDrain.Task;

        public int ActiveManagedInvocations => Volatile.Read(ref activeManagedInvocations);

        public long PhysicalCaptureCopyCount => Volatile.Read(ref physicalCaptureCopyCount);

        public void RecordPhysicalCaptureCopy() => Interlocked.Increment(ref physicalCaptureCopyCount);

        public bool TryEnterInvocation()
        {
            lock (invocationGate)
            {
                if (operations is not null && rootReleaseAttempted != 0) { return false; }
                activeManagedInvocations++;
                return true;
            }
        }

        public void AcquireRoot(IMacOSRemoteWindowBlockOperations operations)
        {
            this.operations = operations;
            Volatile.Write(ref rootAllocationAttempted, 1);
            Volatile.Write(ref handle, operations.AllocateRoot(this));
            nint returnedRoot = Volatile.Read(ref handle);
            if (returnedRoot == 0 ||
                !ReferenceEquals(GCHandle.FromIntPtr(returnedRoot).Target, this))
            {
                throw rootUnavailable;
            }

            Volatile.Write(ref rootAllocationConfirmed, 1);
        }

        // Count physical captures, not native heap-block retains. The native
        // runtime calls CopyCapture only when it creates a new copied capture.
        public void AddReference() => Interlocked.Increment(ref references);

        public void ReleaseReference()
        {
            if (Interlocked.Decrement(ref references) == 0)
            {
                nint ownedHandle = operations is null
                    ? Interlocked.Exchange(ref handle, 0)
                    : Volatile.Read(ref handle);
                if (ownedHandle != 0)
                {
                    lock (invocationGate) { Volatile.Write(ref rootReleaseAttempted, 1); }
                    if (operations is { } stagedEffects)
                    {
                        stagedEffects.FreeRoot(ownedHandle);
                        Volatile.Write(ref handle, 0);
                    }
                    else { GCHandle.FromIntPtr(ownedHandle).Free(); }

                    // A zero handle, native owned-reference release or helper
                    // call count cannot publish retirement. The actual last
                    // physical capture's root free has now returned normally.
                    Volatile.Write(ref rootReleaseConfirmed, 1);
                    nativeCaptureRetirement.TrySetResult();
                    TryPublishManagedInvocationDrain();
                }
            }
        }

        public void ReportFailure(Exception exception)
        {
            _ = Interlocked.CompareExchange(ref firstFailure, exception, null);
            if (staged && MacOSRemoteWindowFailure.FindFatal(exception) is { } fatal)
            {
                _ = Interlocked.CompareExchange(ref firstFatal, fatal, null);
            }
            try
            {
                failure?.Invoke(exception);
            }
            catch (Exception observerFailure)
            {
                // Fault observers are also inside the unmanaged ABI boundary.
                // Preserve their earlier fatal without recursively invoking an
                // observer that has already failed. Legacy first-fault policy
                // remains unchanged on the non-staged Create path.
                if (staged && MacOSRemoteWindowFailure.FindFatal(observerFailure) is { } observerFatal)
                {
                    _ = Interlocked.CompareExchange(ref firstFatal, observerFatal, null);
                }
            }
        }

        // This notification follows managed callback/fault handling, but still
        // precedes the ABI return. Native resource release also requires the
        // caller's owned-copy/queue drain; a latch observer must not throw.
        public void ReportInvocationExit()
        {
            try
            {
                completed?.Invoke();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
            finally
            {
                lock (invocationGate) { activeManagedInvocations--; }
                TryPublishManagedInvocationDrain();
            }
        }

        private void TryPublishManagedInvocationDrain()
        {
            lock (invocationGate)
            {
                if (rootReleaseConfirmed != 0 && activeManagedInvocations == 0)
                {
                    managedInvocationDrain.TrySetResult();
                }
            }
        }
    }

    private static class Abi
    {
        public static readonly nint StackIsa;
        public static readonly nint ZeroArgumentDescriptor;
        public static readonly nint OneArgumentDescriptor;
        public static readonly nint TwoArgumentDescriptor;

        // An explicit static constructor prevents eager native loading before
        // CreateCore's platform guard. Metadata remains valid for process life.
        static Abi()
        {
            nint systemLibrary = NativeLibrary.Load(Native.SystemLibrary);
            nint zeroArgumentDescriptor = 0;
            nint oneArgumentDescriptor = 0;
            nint twoArgumentDescriptor = 0;
            try
            {
                nint stackIsa = NativeLibrary.GetExport(systemLibrary, "_NSConcreteStackBlock");
                zeroArgumentDescriptor = CaptureMetadata.ZeroArgumentDescriptor;
                oneArgumentDescriptor = CaptureMetadata.OneArgumentDescriptor;
                twoArgumentDescriptor = CaptureMetadata.TwoArgumentDescriptor;
                StackIsa = stackIsa;
                ZeroArgumentDescriptor = zeroArgumentDescriptor;
                OneArgumentDescriptor = oneArgumentDescriptor;
                TwoArgumentDescriptor = twoArgumentDescriptor;
            }
            catch
            {
                // Metadata owns its own acquisition rollback. Only this
                // unpublished native library binding remains to release here.
                NativeLibrary.Free(systemLibrary);
                throw;
            }
        }
    }

    private static class CaptureMetadata
    {
        public static readonly nint ZeroArgumentDescriptor;
        public static readonly nint OneArgumentDescriptor;
        public static readonly nint TwoArgumentDescriptor;

        static CaptureMetadata()
        {
            nint zero = 0;
            nint one = 0;
            nint two = 0;
            try
            {
                zero = CreateDescriptor("v8@?0\0"u8);
                one = CreateDescriptor("v16@?0@8\0"u8);
                two = CreateDescriptor("v24@?0@8@16\0"u8);
                ZeroArgumentDescriptor = zero;
                OneArgumentDescriptor = one;
                TwoArgumentDescriptor = two;
            }
            catch
            {
                NativeMemory.Free((void*)two);
                NativeMemory.Free((void*)one);
                NativeMemory.Free((void*)zero);
                throw;
            }
        }

        private static nint CreateDescriptor(ReadOnlySpan<byte> signature)
        {
            var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed(
                (nuint)sizeof(BlockDescriptor) + (nuint)signature.Length);
            if (descriptor == null)
            {
                throw new InvalidOperationException(
                    "Native completion block metadata allocation is unavailable.");
            }

            nint signaturePointer = (nint)(descriptor + 1);
            signature.CopyTo(new Span<byte>((void*)signaturePointer, signature.Length));
            descriptor->Size = (nuint)sizeof(BlockLiteral);
            descriptor->Copy = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&CopyCapture;
            descriptor->Dispose = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&ReleaseCapture;
            descriptor->Signature = signaturePointer;
            return (nint)descriptor;
        }
    }

    private sealed class NativeBlockOperations : IMacOSRemoteWindowBlockOperations
    {
        public static readonly NativeBlockOperations Instance = new();

        public nint AllocateRoot(object target) => GCHandle.ToIntPtr(GCHandle.Alloc(target));
        public void FreeRoot(nint root) => GCHandle.FromIntPtr(root).Free();
        public nint CopyBlock(nint block) => Native.BlockCopy(block);
        public void ReleaseBlock(nint block) => Native.BlockRelease(block);
    }

    private static partial class Native
    {
        public const string SystemLibrary = "/usr/lib/libSystem.B.dylib";

        [LibraryImport(SystemLibrary, EntryPoint = "_Block_copy")]
        public static partial nint BlockCopy(nint block);

        [LibraryImport(SystemLibrary, EntryPoint = "_Block_release")]
        public static partial void BlockRelease(nint block);
    }
}
