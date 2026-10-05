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
            Console.WriteLine("       Flowspan.MacOS.BlockProbe --run-enumeration (actual enumeration Block; controlled non-capture effects)");
            return 0;
        }

        if (args is not ["--run"] && args is not ["--run-enumeration"])
        {
            Console.Error.WriteLine("block_probe=fail reason=invalid_arguments");
            return 2;
        }

        bool enumerationMode = args is ["--run-enumeration"];
        if (!OperatingSystem.IsMacOS() ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            Console.WriteLine(enumerationMode
                ? "enumeration_block_probe=skip reason=unsupported_host native_pass=false"
                : "block_probe=skip reason=unsupported_host native_pass=false");
            return 3;
        }

        try
        {
            if (enumerationMode)
            {
                RunNativeEnumerationComposition();
                Console.WriteLine(
                    "enumeration_block_probe=pass mode=actual_enumeration_block_abi " +
                    "real_orchestration=true real_source_ownership_pool=true inert_preparation=true " +
                    "extra_heap_copies=1 caller_release_before_retirement=true " +
                    "batch_pending_before_last_release=true native_retirement=true " +
                    "drain_pending_while_completed_observer_active=true managed_drain=true " +
                    "abi_return_joined=true empty_result=true final_pool_catalogs=0 " +
                    "final_pool_sources=0 final_pool_batches=0 controlled_external_effects=true " +
                    "sck_executed=false tcc_preflight_called=false capture_proved=false v1_acceptance=false");
                return 0;
            }

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
            Console.Error.WriteLine(enumerationMode
                ? "enumeration_block_probe=fail reason=lifetime_contract_or_cleanup"
                : "block_probe=fail reason=lifetime_contract_or_cleanup");
            return 1;
        }
    }

    private static void RunNativeEnumerationComposition()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new NativeEnumerationEffects();
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>>? enumeration = null;
        try
        {
            enumeration = MacOSRemoteWindowScreenCaptureKitApi
                .EnumerateDirectWithOperations(pool, effects).AsTask();
            effects.WaitForCompletedObserver();
            MacOSRemoteWindowEnumerationCompletion owner = effects.Completion
                ?? throw new InvalidOperationException("Enumeration completion is unavailable.");
            Require(SpinWait.SpinUntil(() => owner.IsReleased, WaitLimit));

            // The same production core has consumed its caller-owned +1. Its
            // original batch cannot settle while our real extra native copy
            // still holds the physical capture or the observer is in flight.
            Require(owner.Pointer == 0 && owner.FirstFailure is null);
            Require(!owner.NativeCaptureRetirement.IsCompleted &&
                !owner.ManagedInvocationDrain.IsCompleted && !enumeration.IsCompleted);
            Require(pool.GetUsage() == (1, 128, 1));
            Require(effects.ContentReferences == 1 && effects.RetainCount == 1 &&
                effects.ReleaseCount == 1 && effects.PoolPushCount == 2 &&
                effects.PoolPopCount == 2);

            effects.ReleaseExtraCopy();
            Require(owner.NativeCaptureRetirement.IsCompletedSuccessfully);
            Require(!owner.ManagedInvocationDrain.IsCompleted && !enumeration.IsCompleted);
            Require(pool.GetUsage() == (1, 128, 1));

            effects.AllowCompletedObserverExit();
            Require(owner.ManagedInvocationDrain.Wait(WaitLimit));
            // The managed drain notification precedes unmanaged ABI return.
            // A separate join, not that notification, observes the latter.
            Require(effects.JoinInvocation());
            Require(enumeration.Wait(WaitLimit));
            Require(enumeration.GetAwaiter().GetResult().Count == 0);
            Require(owner.FirstFailure is null && effects.InvocationFailure is null &&
                effects.CompletedCount == 1 && effects.ExtraReleaseConfirmed);
            Require(pool.GetUsage() == (0, 0, 0));
        }
        finally
        {
            // Cleanup never disposes the completion behind the core's back.
            // It only settles this probe's observer/thread and extra native +1.
            effects.Dispose();
            if (enumeration is not null) { Require(enumeration.Wait(WaitLimit)); }
        }
    }

    private sealed class NativeEnumerationEffects : IMacOSRemoteWindowEnumerationOperations, IDisposable
    {
        private const int Content = 10;
        private const int Windows = 20;
        private readonly ManualResetEventSlim completedObserverEntered = new();
        private readonly ManualResetEventSlim allowCompletedObserverExit = new();
        private nint extraCopy;
        private bool extraReleaseAttempted;
        private bool disposed;
        private Thread? invocation;
        private Exception? invocationFailure;
        private int contentReferences = 1;
        private int retainCount;
        private int releaseCount;
        private int completedCount;

        internal MacOSRemoteWindowEnumerationCompletion? Completion { get; private set; }
        internal int ContentReferences => Volatile.Read(ref contentReferences);
        internal int RetainCount => Volatile.Read(ref retainCount);
        internal int ReleaseCount => Volatile.Read(ref releaseCount);
        internal int CompletedCount => Volatile.Read(ref completedCount);
        internal int PoolPushCount { get; private set; }
        internal int PoolPopCount { get; private set; }
        internal bool ExtraReleaseConfirmed { get; private set; }
        internal Exception? InvocationFailure => Volatile.Read(ref invocationFailure);

        // These are controlled external API effects, not real TCC/AppKit/SCK
        // access or real autorelease pools. Only the Block operations are native.
        public bool PreflightCaptureAccess() => true;
        public bool HasExistingApplication() => true;
        public void InitializeRuntime() { }

        public IMacOSRemoteWindowEnumerationCompletion PrepareCompletion(
            Action<nint, nint> action, Action<Exception> failure, Action completed)
        {
            Completion = MacOSRemoteWindowEnumerationCompletion.Prepare(action, failure, () =>
            {
                completed();
                Interlocked.Increment(ref completedCount);
                completedObserverEntered.Set();
                Require(allowCompletedObserverExit.Wait(WaitLimit));
            });
            Require(Completion.Pointer == 0 && !Completion.IsReleased &&
                !Completion.NativeCaptureRetirement.IsCompleted &&
                !Completion.ManagedInvocationDrain.IsCompleted);
            return Completion;
        }

        public nint PushAutoreleasePool() => ++PoolPushCount;

        public void PopAutoreleasePool(nint pool)
        {
            Require(pool is 1 or 2);
            PoolPopCount++;
        }

        public void Dispatch(nint completion)
        {
            Require(completion != 0 && Completion is not null &&
                Completion.Pointer == completion && extraCopy == 0 && invocation is null);
            var header = (BlockHeader*)completion;
            Require(sizeof(BlockHeader) == 40 && header->Context != 0 && header->Invoke != 0);
            nint invoke = header->Invoke;
            extraCopy = Native.BlockCopy(completion);
            Require(extraCopy != 0 && extraCopy == completion);
            nint invocationCopy = extraCopy;
            invocation = new Thread(() =>
            {
                try
                {
                    // The real thunk holds its managed state before the held
                    // completed observer. No pointer is reread after release.
                    ((delegate* unmanaged[Cdecl]<nint, nint, nint, void>)invoke)(
                        invocationCopy, Content, 0);
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref invocationFailure, exception, null);
                }
            })
            {
                IsBackground = true,
                Name = "Flowspan enumeration Block ABI probe",
            };
            invocation.Start();
        }

        public nint RetainContent(nint content)
        {
            Require(content == Content);
            Interlocked.Increment(ref retainCount);
            Interlocked.Increment(ref contentReferences);
            return content;
        }

        public void ReleaseContent(nint content)
        {
            Require(content == Content);
            Interlocked.Increment(ref releaseCount);
            Interlocked.Decrement(ref contentReferences);
        }

        public nint GetWindows(nint content)
        {
            Require(content == Content);
            return Windows;
        }

        public nuint GetWindowCount(nint windows)
        {
            Require(windows == Windows);
            return 0;
        }

        public nint GetWindow(nint windows, nuint index) =>
            throw new InvalidOperationException("Empty enumeration cannot create a source.");

        public IMacOSRemoteWindowSourceCreationOperations SourceCreationOperations =>
            throw new InvalidOperationException("Empty enumeration cannot create a source.");

        internal void WaitForCompletedObserver() => Require(completedObserverEntered.Wait(WaitLimit));

        internal void AllowCompletedObserverExit() => allowCompletedObserverExit.Set();

        internal bool JoinInvocation() => invocation is not null && invocation.Join(WaitLimit);

        internal void ReleaseExtraCopy()
        {
            Require(extraCopy != 0 && !extraReleaseAttempted);
            extraReleaseAttempted = true;
            Native.BlockRelease(extraCopy);
            ExtraReleaseConfirmed = true;
            extraCopy = 0;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            allowCompletedObserverExit.Set();
            bool joined = false;
            Exception? cleanupFailure = null;
            try { joined = invocation is null || invocation.Join(WaitLimit); }
            catch (Exception exception) { cleanupFailure = exception; }
            try
            {
                if (extraCopy != 0 && !extraReleaseAttempted) { ReleaseExtraCopy(); }
            }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (joined)
            {
                completedObserverEntered.Dispose();
                allowCompletedObserverExit.Dispose();
            }
            Require(cleanupFailure is null && joined && (extraCopy == 0 || ExtraReleaseConfirmed));
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
