using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.BlockProbe;

internal static unsafe partial class Program
{
    private static void RunNativeCaptureCompletionLifetime()
    {
        var completedObserverEntered = new ManualResetEventSlim();
        var allowCompletedObserverExit = new ManualResetEventSlim();
        int callbackCount = 0;
        int completedCount = 0;
        Exception? callbackFailure = null;
        Exception? invocationFailure = null;
        nint extraCopy = 0;
        bool extraReleaseAttempted = false;
        bool extraReleaseConfirmed = false;
        Thread? invocation = null;
        var owner = MacOSRemoteWindowCaptureCompletion.Create(
            argument =>
            {
                Require(argument == 0);
                Interlocked.Increment(ref callbackCount);
            },
            failure: exception => Interlocked.CompareExchange(ref callbackFailure, exception, null),
            completed: () =>
            {
                // The notification has occurred, but this observer is still
                // admitted inside the real one-argument unmanaged invocation.
                Interlocked.Increment(ref completedCount);
                completedObserverEntered.Set();
                Require(allowCompletedObserverExit.Wait(WaitLimit));
            });
        MacOSRemoteWindowBlock primitive = owner.Primitive;

        try
        {
            Require(owner.Pointer == 0 && !owner.IsReleased && owner.FirstFailure is null);
            Require(!primitive.AcquisitionAttempted && !primitive.RootAllocationAttempted &&
                !primitive.CopyAttempted && !primitive.OwnedReleaseAttempted);
            Require(!owner.NativeCaptureRetirement.IsCompleted &&
                !owner.ManagedInvocationDrain.IsCompleted);
            owner.AcquireCopy();
            Require(primitive.RootAllocationConfirmed && primitive.CopyConfirmed && owner.Pointer != 0);
            Require(primitive.PhysicalCaptureCopyCount == 1);

            nint callerCopy = owner.Pointer;
            var header = (BlockHeader*)callerCopy;
            Require(sizeof(BlockHeader) == 40 && sizeof(BlockDescriptor) == 40);
            Require((header->Flags & ((1 << 25) | (1 << 30))) == ((1 << 25) | (1 << 30)));
            var descriptor = (BlockDescriptor*)header->Descriptor;
            Require(descriptor != null && descriptor->Size == 40 &&
                descriptor->Copy != 0 && descriptor->Dispose != 0);
            Require(string.Equals(Marshal.PtrToStringUTF8(descriptor->Signature),
                "v16@?0@8", StringComparison.Ordinal));
            nint invoke = header->Invoke;
            Require(invoke != 0);

            // This real heap retain shares the original physical capture;
            // its helper count is observation only, never lifetime proof.
            extraCopy = Native.BlockCopy(callerCopy);
            Require(extraCopy != 0 && extraCopy == callerCopy);
            Require(primitive.PhysicalCaptureCopyCount == 1);

            nint invocationCopy = extraCopy;
            invocation = new Thread(() =>
            {
                try
                {
                    // Read the function before any release. Once admitted,
                    // InvokeOne has a strong managed BlockState local; no
                    // native block pointer is reread after last-copy release.
                    ((delegate* unmanaged[Cdecl]<nint, nint, void>)invoke)(invocationCopy, 0);
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref invocationFailure, exception, null);
                }
            })
            {
                IsBackground = true,
                Name = "Flowspan capture completion Block ABI probe",
            };
            invocation.Start();
            Require(completedObserverEntered.Wait(WaitLimit));
            Require(Volatile.Read(ref callbackCount) == 1 && Volatile.Read(ref completedCount) == 1);
            Require(primitive.ActiveManagedInvocations == 1);

            owner.Dispose();
            Require(primitive.OwnedReleaseAttempted && primitive.OwnedReleaseConfirmed && owner.IsReleased);
            Require(owner.Pointer == 0 && !primitive.RootReleaseAttempted && !primitive.NativeCaptureRetired);
            Require(!owner.NativeCaptureRetirement.IsCompleted &&
                !owner.ManagedInvocationDrain.IsCompleted && primitive.PhysicalCaptureCopyCount == 1);

            extraReleaseAttempted = true;
            Native.BlockRelease(extraCopy);
            extraReleaseConfirmed = true;
            extraCopy = 0;
            Require(primitive.RootReleaseAttempted && primitive.RootReleaseConfirmed && primitive.NativeCaptureRetired);
            Require(owner.NativeCaptureRetirement.IsCompletedSuccessfully);
            Require(primitive.ActiveManagedInvocations == 1 && !owner.ManagedInvocationDrain.IsCompleted);

            allowCompletedObserverExit.Set();
            Require(owner.ManagedInvocationDrain.Wait(WaitLimit));
            // Managed drain publishes before the unmanaged thunk returns.
            // Only this separate dedicated-thread join observes ABI return.
            Require(invocation.Join(WaitLimit));
            Require(primitive.ActiveManagedInvocations == 0 && owner.ManagedInvocationDrain.IsCompletedSuccessfully);
            Require(Volatile.Read(ref callbackCount) == 1 && Volatile.Read(ref completedCount) == 1);
            Require(callbackFailure is null && invocationFailure is null && owner.FirstFailure is null);
        }
        finally
        {
            allowCompletedObserverExit.Set();
            bool joined = false;
            Exception? cleanupFailure = null;
            try { joined = invocation is null || invocation.Join(WaitLimit); }
            catch (Exception exception) { cleanupFailure = exception; }
            // A failed join may mean the thread has not entered the ABI yet.
            // Retain its known Block references; a watchdog is not borrow-exit
            // proof and a later failure assertion cannot undo an early release.
            try
            {
                if (joined && !primitive.OwnedReleaseAttempted) { owner.Dispose(); }
            }
            catch (Exception exception) { cleanupFailure ??= exception; }
            try
            {
                if (joined && extraCopy != 0 && !extraReleaseAttempted)
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
                completedObserverEntered.Dispose();
                allowCompletedObserverExit.Dispose();
            }

            Require(cleanupFailure is null && joined && primitive.OwnedReleaseConfirmed &&
                (extraCopy == 0 || extraReleaseConfirmed));
        }
    }
}
