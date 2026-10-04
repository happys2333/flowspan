using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flowspan.Platform.MacOS;

// Owns one native heap-block reference. Pointer is borrowed: the caller must
// keep this owner alive until the native API has copied it, and must serialize
// that handoff with Dispose. Disposing this owner does not cancel native copies.
internal sealed unsafe partial class MacOSRemoteWindowBlock : IDisposable
{
    private readonly object releaseGate = new();
    private readonly BlockState state;
    private nint pointer;
    private bool releaseInProgress;

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

    ~MacOSRemoteWindowBlock() => ReleaseOwnerReference();

    public nint Pointer => Volatile.Read(ref pointer);

    internal bool IsReleased => Volatile.Read(ref pointer) == 0;

    internal Exception? FirstFailure => state.FirstFailure;

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
            if (capturedState is not null)
            {
                ((Action)capturedState.Callback)();
            }
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
            if (capturedState is not null)
            {
                ((Action<nint>)capturedState.Callback)(argument);
            }
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
            if (capturedState is not null)
            {
                ((Action<nint, nint>)capturedState.Callback)(firstArgument, secondArgument);
            }
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
        private nint handle;
        private int references = 1;

        public BlockState(
            Delegate callback,
            Action<Exception>? failure,
            Action? completed)
        {
            Callback = callback;
            this.failure = failure;
            this.completed = completed;
            handle = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        }

        public Delegate Callback { get; }

        public Exception? FirstFailure => Volatile.Read(ref firstFailure);

        public nint Handle => Volatile.Read(ref handle);

        // Count physical captures, not native heap-block retains. The native
        // runtime calls CopyCapture only when it creates a new copied capture.
        public void AddReference() => Interlocked.Increment(ref references);

        public void ReleaseReference()
        {
            if (Interlocked.Decrement(ref references) == 0)
            {
                nint ownedHandle = Interlocked.Exchange(ref handle, 0);
                if (ownedHandle != 0)
                {
                    GCHandle.FromIntPtr(ownedHandle).Free();
                }
            }
        }

        public void ReportFailure(Exception exception)
        {
            _ = Interlocked.CompareExchange(ref firstFailure, exception, null);
            try
            {
                failure?.Invoke(exception);
            }
            catch (Exception)
            {
                // Fault observers are also inside the unmanaged ABI boundary.
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
                zeroArgumentDescriptor = CreateDescriptor("v8@?0\0"u8);
                oneArgumentDescriptor = CreateDescriptor("v16@?0@8\0"u8);
                twoArgumentDescriptor = CreateDescriptor("v24@?0@8@16\0"u8);
                StackIsa = stackIsa;
                ZeroArgumentDescriptor = zeroArgumentDescriptor;
                OneArgumentDescriptor = oneArgumentDescriptor;
                TwoArgumentDescriptor = twoArgumentDescriptor;
            }
            catch
            {
                // Roll back unpublished metadata only. Successfully published
                // descriptors, signatures and library remain process-lifetime.
                NativeMemory.Free((void*)twoArgumentDescriptor);
                NativeMemory.Free((void*)oneArgumentDescriptor);
                NativeMemory.Free((void*)zeroArgumentDescriptor);
                NativeLibrary.Free(systemLibrary);
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

    private static partial class Native
    {
        public const string SystemLibrary = "/usr/lib/libSystem.B.dylib";

        [LibraryImport(SystemLibrary, EntryPoint = "_Block_copy")]
        public static partial nint BlockCopy(nint block);

        [LibraryImport(SystemLibrary, EntryPoint = "_Block_release")]
        public static partial void BlockRelease(nint block);
    }
}
