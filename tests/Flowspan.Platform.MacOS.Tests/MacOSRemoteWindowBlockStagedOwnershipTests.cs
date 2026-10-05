using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowBlockStagedOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcquisitionIsNotRetriedAfterSuccessOrUnknownRootFault(bool unknownRoot)
    {
        var original = unknownRoot ? new IOException("Controlled one-shot root fault.") : null;
        using var effects = new BlockEffects { RootAllocationFailure = original };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);

        Assert.Same(original, Record.Exception(owner.AcquireCopy));
        Assert.Throws<InvalidOperationException>(owner.AcquireCopy);

        Assert.Equal(1, effects.RootAllocationAttempts);
        Assert.Equal(unknownRoot ? 0 : 1, effects.CopyAttempts);
        Assert.Same(original, owner.FirstFailure);
        owner.Dispose();
        Assert.Equal(unknownRoot ? 0 : 1, effects.ReleaseAttempts);
        Assert.Equal(unknownRoot ? 0 : 1, effects.RootFreeAttempts);
        Assert.Equal(unknownRoot ? 1 : 0, effects.LiveRoots);
    }

    [Fact]
    public void InertDisposeRejectsAcquisitionBeforeOwnedEffects()
    {
        using var effects = new BlockEffects();
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);
        owner.Dispose();

        Assert.Throws<InvalidOperationException>(owner.AcquireCopy);

        Assert.False(owner.AcquisitionAttempted);
        Assert.False(owner.RootAllocationAttempted);
        Assert.False(owner.CopyAttempted);
        Assert.Equal(0, effects.RootAllocationAttempts);
        Assert.Equal(0, effects.CopyAttempts);
        Assert.Equal(0, effects.ReleaseAttempts);
        Assert.Equal(0, effects.RootFreeAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbandonedStagedOwnerDoesNotUseFinalizerToGuessDebtCleanup(bool unknownRoot)
    {
        using var effects = new BlockEffects
        {
            RootAllocationFailure = unknownRoot ? new IOException("Controlled abandoned root fault.") : null,
        };
        WeakReference abandoned = AbandonStagedOwner(effects);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(abandoned.IsAlive);
        Assert.Equal(1, effects.LiveRoots);
        Assert.Equal(unknownRoot ? 0 : 1, effects.LiveBlocks);
        Assert.Equal(0, effects.ReleaseAttempts);
        Assert.Equal(0, effects.RootFreeAttempts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Reliability", "CA2000", Justification = "Intentionally abandons a staged shell to verify no finalizer retries; the test-owned fixture releases actual allocations after assertions.")]
    private static WeakReference AbandonStagedOwner(BlockEffects effects)
    {
        var owner = MacOSRemoteWindowBlock.PrepareWithOperations(static (_, _) => { }, effects);
        var actual = Record.Exception(owner.AcquireCopy);
        Assert.Same(effects.RootAllocationFailure, actual);
        return new WeakReference(owner);
    }

    [Fact]
    public void ConcurrentAndCrossThreadReentrantDisposeSelectsOneEffectOutsideGate()
    {
        using var effects = new BlockEffects();
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);
        owner.AcquireCopy();
        effects.BeforeRelease = () =>
            Task.Run(owner.Dispose).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        Parallel.For(0, 32, _ => owner.Dispose());

        Assert.Equal(1, effects.ReleaseAttempts);
        Assert.Equal(1, effects.RootFreeAttempts);
        Assert.Equal(0, effects.LiveBlocks);
        Assert.Equal(0, effects.LiveRoots);
        Assert.True(owner.OwnedReleaseConfirmed);
        Assert.True(owner.NativeCaptureRetired);
        Assert.Null(owner.FirstFailure);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects earlier failure-observer fatal and later completed-observer fatal through actual ABI callbacks.")]
    public void EarlierFailureObserverFatalSurvivesLaterCompletedFailure()
    {
        using var effects = new BlockEffects();
        var fatal = new OutOfMemoryException("Controlled earlier failure observer fatal.");
        var laterFatal = new OutOfMemoryException("Controlled later completed observer fatal.");
        int observations = 0;
        int completions = 0;
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations(
            (_, _) => throw new IOException("Controlled ordinary callback fault."), effects,
            _ => { observations++; throw new IOException("Controlled failure observer wrapper.", fatal); },
            () => { completions++; throw new IOException("Controlled completed observer wrapper.", laterFatal); });
        owner.AcquireCopy();

        effects.Invoke(owner.Pointer, 0, 0);
        owner.Dispose();

        Assert.Equal(2, observations);
        Assert.Equal(1, completions);
        Assert.Equal(0, owner.ActiveManagedInvocations);
        Assert.Same(fatal, owner.FirstFailure);
        Assert.True(owner.NativeCaptureRetired);
        Assert.True(owner.ManagedInvocationDrain.IsCompletedSuccessfully);
    }

    [Fact]
    public void ExtraHeapReferenceOutlivesInvocationAndDefersTerminalDrain()
    {
        using var effects = new BlockEffects();
        int actions = 0;
        int completions = 0;
        (nint First, nint Second) received = default;
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations(
            (first, second) => { actions++; received = (first, second); }, effects,
            completed: () => completions++);
        owner.AcquireCopy();
        Assert.Equal(1, owner.PhysicalCaptureCopyCount);
        nint extra = effects.CopyBlock(owner.Pointer);
        Assert.Equal(owner.Pointer, extra);
        Assert.Equal(1, owner.PhysicalCaptureCopyCount);

        owner.Dispose();

        Assert.True(owner.OwnedReleaseConfirmed);
        Assert.False(owner.NativeCaptureRetired);
        Assert.False(owner.NativeCaptureRetirement.IsCompleted);
        Assert.False(owner.ManagedInvocationDrain.IsCompleted);
        Assert.Equal(0, effects.RootFreeAttempts);
        effects.Invoke(extra, 5, 7);
        Assert.Equal(1, actions);
        Assert.Equal(1, completions);
        Assert.Equal(((nint)5, (nint)7), received);
        Assert.Equal(0, owner.ActiveManagedInvocations);
        Assert.False(owner.ManagedInvocationDrain.IsCompleted);

        effects.ReleaseBlock(extra);

        Assert.True(owner.NativeCaptureRetired);
        Assert.True(owner.NativeCaptureRetirement.IsCompletedSuccessfully);
        Assert.True(owner.ManagedInvocationDrain.IsCompletedSuccessfully);
        Assert.Equal(1, effects.RootFreeAttempts);
        Assert.Equal(0, effects.LiveRoots);
        Assert.Null(owner.FirstFailure);
    }

    [Fact]
    public void ForeignCopyCaptureIsNotConfirmedOrPublished()
    {
        using var effects = new BlockEffects { ForeignCopyCapture = true };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);

        var actual = Record.Exception(owner.AcquireCopy);

        Assert.IsType<InvalidOperationException>(actual);
        Assert.True(owner.RootAllocationConfirmed);
        Assert.True(owner.CopyAttempted);
        Assert.False(owner.CopyConfirmed);
        Assert.Equal(0, owner.Pointer);
        owner.Dispose();
        Assert.Equal(1, effects.ReleaseAttempts);
        Assert.Equal(0, effects.RootFreeAttempts);
        Assert.Equal(2, effects.LiveRoots);
        Assert.True(owner.OwnedReleaseConfirmed);
        Assert.False(owner.NativeCaptureRetired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidRootReturnIsNotConfirmedCopiedOrGuessedFreed(bool foreign)
    {
        using var effects = new BlockEffects { InvalidRootReturn = foreign ? "foreign" : "zero" };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);

        var actual = Record.Exception(owner.AcquireCopy);
        owner.Dispose();

        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal(1, effects.RootAllocationAttempts);
        Assert.Equal(1, effects.LiveRoots);
        Assert.Equal(0, effects.RootFreeAttempts);
        Assert.Equal(0, effects.CopyAttempts);
        Assert.Equal(0, effects.ReleaseAttempts);
        Assert.True(owner.RootAllocationAttempted);
        Assert.False(owner.RootAllocationConfirmed);
        Assert.False(owner.CopyAttempted);
        Assert.False(owner.NativeCaptureRetired);
        Assert.Equal(0, owner.Pointer);
    }

    [Fact]
    public void ReentrantDisposeDuringCopyRetiresConfirmedCopyAfterAcquisition()
    {
        using var effects = new BlockEffects();
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);
        effects.AfterCopyCapture = owner.Dispose;

        owner.AcquireCopy();

        Assert.Equal(1, effects.CopyAttempts);
        Assert.Equal(1, effects.ReleaseAttempts);
        Assert.Equal(1, effects.RootFreeAttempts);
        Assert.Equal(0, effects.LiveBlocks);
        Assert.Equal(0, effects.LiveRoots);
        Assert.True(owner.IsReleased);
        Assert.True(owner.NativeCaptureRetired);
        Assert.True(owner.ManagedInvocationDrain.IsCompletedSuccessfully);
        Assert.Equal(0, owner.Pointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested fatal and independent later root cleanup fault.")]
    public void CopyFatalIsNotHiddenByIndependentRootCleanupFailure(bool laterFatal)
    {
        var fatal = new OutOfMemoryException("Controlled earlier Block copy fatal.");
        var original = new IOException("Controlled Block copy wrapper.", fatal);
        Exception cleanup = laterFatal
            ? new IOException("Controlled later root wrapper.", new OutOfMemoryException("Controlled later root fatal."))
            : new IOException("Controlled later root cleanup fault.");
        using var effects = new BlockEffects
        {
            CopyBeforeEffectFailure = original,
            RootFreeFailure = cleanup,
        };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);

        var actual = Record.Exception(owner.AcquireCopy);

        Assert.Equal(1, effects.CopyAttempts);
        Assert.Equal(1, effects.RootFreeAttempts);
        Assert.Equal(0, effects.LiveBlocks);
        Assert.Equal(0, effects.LiveRoots);
        Assert.Same(fatal, actual);
        Assert.Same(fatal, owner.FirstFailure);
        Assert.False(owner.NativeCaptureRetired);
        Assert.False(owner.CopyConfirmed);
    }

    [Fact]
    public async Task NativeRetirementDoesNotJoinActiveCompletedObserver()
    {
        using var effects = new BlockEffects();
        using var completedEntered = new ManualResetEventSlim();
        using var allowCompletedExit = new ManualResetEventSlim();
        nint extra = 0;
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations(
            (_, _) => effects.ReleaseBlock(extra), effects,
            completed: () =>
            {
                completedEntered.Set();
                if (!allowCompletedExit.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Controlled completed observer timed out.");
                }
            });
        owner.AcquireCopy();
        extra = effects.CopyBlock(owner.Pointer);
        owner.Dispose();
        Assert.False(owner.NativeCaptureRetired);

        Task invocation = Task.Run(() => effects.Invoke(extra, 0, 0));
        try
        {
            Assert.True(completedEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(owner.NativeCaptureRetired);
            Assert.True(owner.NativeCaptureRetirement.IsCompletedSuccessfully);
            Assert.False(owner.ManagedInvocationDrain.IsCompleted);
        }
        finally
        {
            allowCompletedExit.Set();
            await invocation.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await owner.ManagedInvocationDrain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(owner.FirstFailure);
        Assert.Equal(1, effects.RootFreeAttempts);
    }

    [Fact]
    public void RootFreeConsumedThenThrowsDoesNotProveNativeRetirementOrRetry()
    {
        var fault = new IOException("Controlled root free after-effect fault.");
        using var effects = new BlockEffects { RootFreeFailure = fault };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);
        owner.AcquireCopy();

        owner.Dispose();
        owner.Dispose();

        Assert.Same(fault, owner.FirstFailure);
        Assert.Equal(0, effects.LiveBlocks);
        Assert.Equal(0, effects.LiveRoots);
        Assert.Equal(1, effects.ReleaseAttempts);
        Assert.Equal(1, effects.RootFreeAttempts);
        Assert.True(owner.OwnedReleaseConfirmed);
        Assert.False(owner.NativeCaptureRetired);
        Assert.True(owner.RootReleaseAttempted);
        Assert.False(owner.RootReleaseConfirmed);
    }

    [Fact]
    public void StagedReleaseConsumedThenThrowsIsNotRetried()
    {
        var fault = new IOException("Controlled Block release after-effect fault.");
        using var effects = new BlockEffects { ReleaseFailure = fault };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);
        owner.AcquireCopy();

        owner.Dispose();
        owner.Dispose();

        Assert.Same(fault, owner.FirstFailure);
        Assert.Equal(0, effects.LiveBlocks);
        Assert.Equal(0, effects.LiveRoots);
        Assert.Equal(1, effects.RootFreeAttempts);
        Assert.Equal(1, effects.ReleaseAttempts);
        Assert.True(owner.OwnedReleaseAttempted);
        Assert.False(owner.OwnedReleaseConfirmed);
        Assert.False(owner.IsReleased);
        Assert.Equal(0, owner.Pointer);
    }

    [Fact]
    public void PreparedOwnerContainsCopyAfterEffectFaultWithoutGuessingRelease()
    {
        var fault = new IOException("Controlled Block copy after-effect fault.");
        using var effects = new BlockEffects { CopyFailure = fault };
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);

        var actual = Record.Exception(owner.AcquireCopy);

        Assert.Same(fault, actual);
        Assert.Same(fault, owner.FirstFailure);
        Assert.Equal(1, effects.RootAllocationAttempts);
        Assert.Equal(1, effects.CopyAttempts);
        Assert.Equal(1, effects.LiveBlocks);
        Assert.Equal(1, effects.LiveRoots);
        Assert.Equal(0, effects.RootFreeAttempts);
        Assert.Equal(0, effects.ReleaseAttempts);
        Assert.Equal(0, owner.Pointer);
        Assert.True(owner.RootAllocationConfirmed);
        Assert.True(owner.CopyAttempted);
        Assert.False(owner.CopyConfirmed);
    }

    [Fact]
    public void PreparedOwnerContainsRootAllocationAfterEffectFault()
    {
        var fault = new IOException("Controlled root allocation after-effect fault.");
        using var effects = new BlockEffects { RootAllocationFailure = fault };
        MacOSRemoteWindowBlock? attached = null;
        using var owner = MacOSRemoteWindowBlock.PrepareWithOperations((_, _) => { }, effects);
        attached = owner;
        effects.BeforeRootAllocation = () => Assert.Same(owner, attached);
        Assert.Equal(0, effects.RootAllocationAttempts);

        var actual = Record.Exception(owner.AcquireCopy);

        Assert.Same(fault, actual);
        Assert.Same(fault, owner.FirstFailure);
        Assert.Equal(1, effects.RootAllocationAttempts);
        Assert.Equal(1, effects.LiveRoots);
        Assert.Equal(0, effects.CopyAttempts);
        Assert.Equal(0, effects.RootFreeAttempts);
        Assert.True(owner.RootAllocationAttempted);
        Assert.False(owner.RootAllocationConfirmed);
    }

    private sealed class BlockEffects : IMacOSRemoteWindowBlockOperations, IDisposable
    {
        private readonly List<GCHandle> roots = [];
        private readonly Dictionary<nint, int> blocks = [];

        public Action? BeforeRootAllocation { get; set; }
        public Action? AfterCopyCapture { get; set; }
        public Action? BeforeRelease { get; set; }
        public Exception? RootAllocationFailure { get; init; }
        public Exception? CopyFailure { get; init; }
        public Exception? CopyBeforeEffectFailure { get; init; }
        public Exception? ReleaseFailure { get; init; }
        public Exception? RootFreeFailure { get; init; }
        public string? InvalidRootReturn { get; init; }
        public bool ForeignCopyCapture { get; init; }
        public int RootAllocationAttempts { get; private set; }
        public int RootFreeAttempts { get; private set; }
        public int CopyAttempts { get; private set; }
        public int ReleaseAttempts { get; private set; }
        public int LiveRoots => roots.Count;
        public int LiveBlocks => blocks.Count;

        public nint AllocateRoot(object target)
        {
            RootAllocationAttempts++;
            BeforeRootAllocation?.Invoke();
            var root = GCHandle.Alloc(InvalidRootReturn == "foreign" ? new object() : target);
            roots.Add(root);
            if (RootAllocationFailure is { } failure) { throw failure; }
            if (InvalidRootReturn == "zero") { return 0; }
            return GCHandle.ToIntPtr(root);
        }

        public void FreeRoot(nint root)
        {
            RootFreeAttempts++;
            var owned = GCHandle.FromIntPtr(root);
            roots.Remove(owned);
            owned.Free();
            if (RootFreeFailure is { } failure) { throw failure; }
        }

        public nint CopyBlock(nint block)
        {
            CopyAttempts++;
            if (CopyBeforeEffectFailure is { } beforeFailure) { throw beforeFailure; }
            if (blocks.TryGetValue(block, out int references))
            {
                blocks[block] = references + 1;
                return block;
            }

            nint descriptor = Marshal.ReadIntPtr(block, 24);
            int size = checked((int)Marshal.ReadIntPtr(descriptor, 8));
            var bytes = new byte[size];
            Marshal.Copy(block, bytes, 0, bytes.Length);
            nint copied = Marshal.AllocHGlobal(size);
            Marshal.Copy(bytes, 0, copied, bytes.Length);
            blocks.Add(copied, 1);
            var copyCapture = Marshal.GetDelegateForFunctionPointer<CopyCapture>(
                Marshal.ReadIntPtr(descriptor, 16));
            copyCapture(copied, block);
            if (ForeignCopyCapture)
            {
                var foreignRoot = GCHandle.Alloc(new object());
                roots.Add(foreignRoot);
                Marshal.WriteIntPtr(copied, 32, GCHandle.ToIntPtr(foreignRoot));
            }

            AfterCopyCapture?.Invoke();
            if (CopyFailure is { } failure) { throw failure; }
            return copied;
        }

        public void ReleaseBlock(nint block)
        {
            ReleaseAttempts++;
            BeforeRelease?.Invoke();
            ReleaseFixtureReference(block);
            if (ReleaseFailure is { } failure) { throw failure; }
        }

        public void Invoke(nint block, nint first, nint second)
        {
            if (!blocks.ContainsKey(block))
            {
                throw new IOException("Controlled invocation requires a live test Block.");
            }

            var invoke = Marshal.GetDelegateForFunctionPointer<InvokeTwo>(
                Marshal.ReadIntPtr(block, 16));
            invoke(block, first, second);
        }

        private void ReleaseFixtureReference(nint block)
        {
            int remaining = blocks[block] - 1;
            if (remaining != 0) { blocks[block] = remaining; return; }
            blocks.Remove(block);
            nint descriptor = Marshal.ReadIntPtr(block, 24);
            var releaseCapture = Marshal.GetDelegateForFunctionPointer<ReleaseCapture>(
                Marshal.ReadIntPtr(descriptor, 24));
            releaseCapture(block);
            Marshal.FreeHGlobal(block);
        }

        public void Dispose()
        {
            // Test-owned allocation cleanup does not confirm staged debt.
            foreach (nint block in blocks.Keys.ToArray())
            {
                blocks[block] = 1;
                ReleaseFixtureReference(block);
            }

            foreach (var root in roots) { root.Free(); }
            roots.Clear();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CopyCapture(nint destination, nint source);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ReleaseCapture(nint block);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InvokeTwo(nint block, nint first, nint second);
    }
}
