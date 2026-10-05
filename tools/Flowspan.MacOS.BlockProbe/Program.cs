using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.BlockProbe;

internal static unsafe partial class Program
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("block_probe=not_run mode=explicit_run_required");
            Console.WriteLine("Usage: Flowspan.MacOS.BlockProbe --run (macOS arm64; no capture or input)");
            return 0;
        }

        if (args is not ["--run"])
        {
            Console.Error.WriteLine("block_probe=fail reason=invalid_arguments");
            return 2;
        }

        if (!OperatingSystem.IsMacOS() ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            Console.WriteLine("block_probe=skip reason=unsupported_host native_pass=false");
            return 3;
        }

        try
        {
            RunNativeHeapCopyLifetime();
            Console.WriteLine(
                "block_probe=pass mode=actual_block_abi initial_copy_helpers=1 " +
                "extra_heap_copies=1 extra_copy_helpers=0 caller_releases=1 " +
                "last_copy_releases=1 root_free_confirmed=true native_retirement=true " +
                "drain_pending_while_active=true managed_drain=true abi_return_joined=true " +
                "callbacks=1 completed_observers=1 contained_failures=0 " +
                "capture_proved=false v1_acceptance=false");
            return 0;
        }
        catch (Exception)
        {
            // Exceptions may contain native identities or host paths. The raw
            // exit and this bounded diagnosis are the outward evidence boundary.
            Console.Error.WriteLine("block_probe=fail reason=lifetime_contract_or_cleanup");
            return 1;
        }
    }

    private static void RunNativeHeapCopyLifetime()
    {
        var entered = new ManualResetEventSlim();
        var allowExit = new ManualResetEventSlim();
        int callbackCount = 0;
        int completedCount = 0;
        Exception? callbackFailure = null;
        Exception? invocationFailure = null;
        nint extraCopy = 0;
        bool extraReleaseAttempted = false;
        bool extraReleaseConfirmed = false;
        Thread? invocation = null;
        var owner = MacOSRemoteWindowBlock.Prepare(
            (first, second) =>
            {
                Require(first == 0 && second == 0);
                Interlocked.Increment(ref callbackCount);
                entered.Set();
                Require(allowExit.Wait(WaitLimit));
            },
            failure: exception => Interlocked.CompareExchange(ref callbackFailure, exception, null),
            completed: () => Interlocked.Increment(ref completedCount));

        try
        {
            Require(!owner.AcquisitionAttempted && !owner.RootAllocationAttempted &&
                !owner.CopyAttempted && owner.Pointer == 0);
            Require(!owner.NativeCaptureRetirement.IsCompleted &&
                !owner.ManagedInvocationDrain.IsCompleted);
            owner.AcquireCopy();
            Require(owner.RootAllocationConfirmed && owner.CopyConfirmed && owner.Pointer != 0);
            Require(owner.PhysicalCaptureCopyCount == 1);

            nint callerCopy = owner.Pointer;
            var header = (BlockHeader*)callerCopy;
            Require(sizeof(BlockHeader) == 40 && sizeof(BlockDescriptor) == 40);
            Require((header->Flags & ((1 << 25) | (1 << 30))) == ((1 << 25) | (1 << 30)));
            var descriptor = (BlockDescriptor*)header->Descriptor;
            Require(descriptor != null && descriptor->Size == 40 &&
                descriptor->Copy != 0 && descriptor->Dispose != 0);
            Require(string.Equals(Marshal.PtrToStringUTF8(descriptor->Signature),
                "v24@?0@8@16", StringComparison.Ordinal));
            nint invoke = header->Invoke;
            Require(invoke != 0);

            // A heap-to-heap copy retains the same physical capture. The actual
            // primitive's helper counter is observation only, never drain proof.
            extraCopy = Native.BlockCopy(callerCopy);
            Require(extraCopy != 0 && extraCopy == callerCopy);
            Require(owner.PhysicalCaptureCopyCount == 1);
            Require(!owner.NativeCaptureRetirement.IsCompleted &&
                !owner.ManagedInvocationDrain.IsCompleted);

            nint invocationCopy = extraCopy;
            invocation = new Thread(() =>
            {
                try
                {
                    // Read the function before any release. After admission the
                    // real InvokeTwo has a strong local BlockState reference;
                    // neither this caller nor the thunk rereads the freed block.
                    ((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)invoke)(
                        invocationCopy, 0, 0);
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref invocationFailure, exception, null);
                }
            })
            {
                IsBackground = true,
                Name = "Flowspan Block ABI probe",
            };
            invocation.Start();
            Require(entered.Wait(WaitLimit));
            Require(owner.ActiveManagedInvocations == 1);

            owner.Dispose();
            Require(owner.OwnedReleaseAttempted && owner.OwnedReleaseConfirmed && owner.IsReleased);
            Require(owner.Pointer == 0 && !owner.RootReleaseAttempted && !owner.NativeCaptureRetired);
            Require(!owner.NativeCaptureRetirement.IsCompleted &&
                !owner.ManagedInvocationDrain.IsCompleted);
            Require(owner.PhysicalCaptureCopyCount == 1);

            extraReleaseAttempted = true;
            Native.BlockRelease(extraCopy);
            extraReleaseConfirmed = true;
            extraCopy = 0;
            Require(owner.RootReleaseAttempted && owner.RootReleaseConfirmed && owner.NativeCaptureRetired);
            Require(owner.NativeCaptureRetirement.IsCompletedSuccessfully);
            Require(owner.ActiveManagedInvocations == 1 && !owner.ManagedInvocationDrain.IsCompleted);

            allowExit.Set();
            Require(owner.ManagedInvocationDrain.Wait(WaitLimit));
            Require(invocation.Join(WaitLimit));
            Require(owner.ActiveManagedInvocations == 0 && owner.ManagedInvocationDrain.IsCompletedSuccessfully);
            Require(Volatile.Read(ref callbackCount) == 1 && Volatile.Read(ref completedCount) == 1);
            Require(callbackFailure is null && invocationFailure is null && owner.FirstFailure is null);
        }
        finally
        {
            // These cleanup effects are attempted once, even if an assertion
            // fails. The thread is joined before its managed wait gates dispose.
            allowExit.Set();
            bool joined = false;
            Exception? cleanupFailure = null;
            try { joined = invocation is null || invocation.Join(WaitLimit); }
            catch (Exception exception) { cleanupFailure = exception; }
            try
            {
                if (!owner.OwnedReleaseAttempted) { owner.Dispose(); }
            }
            catch (Exception exception) { cleanupFailure ??= exception; }
            try
            {
                if (extraCopy != 0 && !extraReleaseAttempted)
                {
                    extraReleaseAttempted = true;
                    Native.BlockRelease(extraCopy);
                    extraReleaseConfirmed = true;
                    extraCopy = 0;
                }
            }
            catch (Exception exception) { cleanupFailure ??= exception; }

            if (joined)
            {
                entered.Dispose();
                allowExit.Dispose();
            }

            Require(cleanupFailure is null && joined && owner.OwnedReleaseConfirmed &&
                (extraCopy == 0 || extraReleaseConfirmed));
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) { throw new InvalidOperationException("Block lifetime contract failed."); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockHeader
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

    private static partial class Native
    {
        private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";

        [LibraryImport(SystemLibrary, EntryPoint = "_Block_copy")]
        public static partial nint BlockCopy(nint block);

        [LibraryImport(SystemLibrary, EntryPoint = "_Block_release")]
        public static partial void BlockRelease(nint block);
    }
}
