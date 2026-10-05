using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowEnumerationCompositionTests
{
    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Verifies an initial notification fatal precedes a later independent factory cleanup fatal.")]
    public async Task SourceFactoryNotificationFatalPrecedesLaterFilterCleanupFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("Controlled earlier factory notification fatal.");
        var sink = new ThrowingFailureSink(new IOException("Controlled factory notification wrapper.", fatal));
        Assert.True(pool.TryReserveCatalog(sink, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var windows = new WindowEffects
        {
            WindowRetainFailure = new IOException("Controlled ordinary source retain fault."),
            FilterReleaseFailure = new IOException("Controlled later filter wrapper.",
                new OutOfMemoryException("Controlled later factory filter fatal.")),
        };
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer => runtime.Invoke(pointer, 10, 0),
            WindowCount = 1,
            WindowOperations = windows,
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(fatal, actual);
        Assert.Equal(new[] { WindowEffects.Filter }, windows.ReleaseAttempts);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(2, windows.WindowReferences);
        Assert.Equal(2, effects.PoolPopAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Verifies original source-retain fatal survives a later notification fatal without guessed rollback.")]
    public async Task SourceFactoryFailureSinkCannotReplaceOriginalRetainFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("Controlled earlier source retain fatal.");
        var sink = new ThrowingFailureSink(new IOException("Controlled later factory sink wrapper.",
            new OutOfMemoryException("Controlled later factory sink fatal.")));
        Assert.True(pool.TryReserveCatalog(sink, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var windows = new WindowEffects
        {
            WindowRetainFailure = new IOException("Controlled source retain wrapper.", fatal),
        };
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer => runtime.Invoke(pointer, 10, 0),
            WindowCount = 1,
            WindowOperations = windows,
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(fatal, actual);
        Assert.Equal(new[] { WindowEffects.Filter }, windows.ReleaseAttempts);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(2, windows.WindowReferences);
        Assert.Equal(1, runtime.ReleaseAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(new[] { WindowEffects.Filter }, windows.ReleaseAttempts);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects an earlier filter-release fatal and a later sink fatal through actual source cleanup.")]
    public async Task SourceFailureSinkCannotSkipWindowCleanupOrReplaceEarlierFilterFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("Controlled earlier source filter fatal.");
        var sink = new ThrowingFailureSink(new IOException("Controlled later source sink wrapper.",
            new OutOfMemoryException("Controlled later source sink fatal.")));
        Assert.True(pool.TryReserveCatalog(sink, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var windows = new WindowEffects
        {
            FilterReleaseFailure = new IOException("Controlled filter release wrapper.", fatal),
        };
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer => runtime.Invoke(pointer, 10, 0),
            WindowCount = 2,
            WindowOperations = windows,
            SecondWindowFailure = new IOException("Controlled ordinary second-window fault."),
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(fatal, actual);
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(1, windows.WindowReferences);
        Assert.Equal(2, effects.PoolPopAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Verifies earlier content cleanup fatal survives a later failure sink fatal without skipping other owners.")]
    public async Task ThrowingFailureSinkCannotSkipPoolAndBlockCleanupOrReplaceEarlierFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("Controlled earlier content cleanup fatal.");
        var sink = new ThrowingFailureSink(new IOException("Controlled later sink wrapper.",
            new OutOfMemoryException("Controlled later sink fatal.")));
        Assert.True(pool.TryReserveCatalog(sink, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer => runtime.Invoke(pointer, 10, 0),
            ContentReleaseFailure = new IOException("Controlled content release wrapper.", fatal),
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(fatal, actual);
        Assert.True(context.IsClosed);
        Assert.Equal(1, effects.ContentReleaseAttempts);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(2, effects.PoolPopAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        Assert.Equal(1, runtime.RootFreeAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a fatal from a fallible failure sink after an ordinary unknown retain effect.")]
    public async Task ThrowingFailureSinkCannotStrandRetainFaultBeforeResultCompletion()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("Controlled failure sink fatal.");
        var sink = new ThrowingFailureSink(new IOException("Controlled sink wrapper.", fatal));
        Assert.True(pool.TryReserveCatalog(sink, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer => runtime.Invoke(pointer, 10, 0),
            RetainFailure = new IOException("Controlled ordinary retained-content fault."),
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(fatal, actual);
        Assert.True(context.IsClosed);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(2, effects.ContentReferences);
        Assert.Equal(0, effects.ContentReleaseAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        Assert.Equal(1, runtime.RootFreeAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    public async Task RevokedAccessSettlesUndispatchedRealCompletionWithoutCallbackWait()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var effects = new EnumerationEffects(runtime) { RevokeBeforeDispatch = true };

        Assert.Empty(await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, effects.DispatchAttempts);
        Assert.Equal(0, effects.RetainAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task UnknownDispatchReturnsWithoutWaitingAndRejectsLateRealCallback()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        nint extra = 0;
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                throw new IOException("Controlled copied-then-failed dispatch.");
            },
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal("macos_source_producer_unavailable", actual.Message);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(1, runtime.ReleaseAttempts);
        Assert.Equal(0, runtime.RootFreeAttempts);
        Assert.Equal(0, effects.RetainAttempts);

        runtime.Invoke(extra, 10, 0);
        runtime.ReleaseBlock(extra);

        Assert.Equal(0, effects.RetainAttempts);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    public async Task HealthyNonemptyResultTransfersOnlyOriginalSourceChargeAfterNativeRetirement()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var windows = new WindowEffects();
        nint extra = 0;
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                runtime.Invoke(pointer, 10, 0);
            },
            WindowCount = 1,
            WindowOperations = windows,
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        Assert.False(operation.IsCompleted);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        runtime.ReleaseBlock(extra);
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Single(sources);
            Assert.Equal((1, 1, 0), pool.GetUsage());
            Assert.Equal(1, runtime.RootFreeAttempts);
            Assert.Equal(1, windows.FilterReferences);
            Assert.Equal(2, windows.WindowReferences);
            Assert.Empty(windows.ReleaseAttempts);
        }
        finally
        {
            foreach (IMacOSRemoteWindowNativeSource source in sources) { source.Dispose(); }
        }
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
    }

    [Fact]
    public async Task ReentrantSettlementDuringOwnedReleaseKeepsOriginalBatchCharged()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        nint extra = 0;
        runtime.BeforeRelease = () =>
        {
            Task.Run(() => pool.CompleteBatch(batch!, context))
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.Equal((1, 128, 1), pool.GetUsage());
        };
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                runtime.Invoke(pointer, 10, 0);
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects).AsTask();

        Assert.False(operation.IsCompleted);
        Assert.True(context.IsClosed);
        runtime.ReleaseBlock(extra);
        Assert.Empty(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(1, runtime.RootFreeAttempts);
    }

    [Fact]
    public async Task KnownSourceCleanupRunsBeforeWaitingForDelayedNativeRetirement()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var windows = new WindowEffects();
        nint extra = 0;
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                runtime.Invoke(pointer, 10, 0);
            },
            WindowCount = 2,
            WindowOperations = windows,
            SecondWindowFailure = new IOException("Controlled ordinary window query failure."),
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        try
        {
            Assert.False(operation.IsCompleted);
            Assert.Equal(0, windows.FilterReferences);
            Assert.Equal(1, windows.WindowReferences);
            Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
            Assert.Equal((1, 127, 1), pool.GetUsage());
        }
        finally
        {
            if (extra != 0) { runtime.ReleaseBlock(extra); }
        }

        Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
    }

    [Fact]
    public async Task NativeRetirementDoesNotReturnBatchWhileLaterManagedObserverRuns()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var completedEntered = new ManualResetEventSlim();
        using var allowCompletedExit = new ManualResetEventSlim();
        int completedCount = 0;
        nint extra = 0;
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                runtime.Invoke(pointer, 10, 0);
            },
            BeforeCompleted = () =>
            {
                if (Interlocked.Increment(ref completedCount) != 2) { return; }
                completedEntered.Set();
                if (!allowCompletedExit.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Controlled completed observer exit timed out.");
                }
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        Assert.False(operation.IsCompleted);
        Task invocation = Task.Factory.StartNew(() => runtime.Invoke(extra, 10, 0),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(completedEntered.Wait(TimeSpan.FromSeconds(5)));
            runtime.ReleaseBlock(extra);
            var owner = Assert.IsType<MacOSRemoteWindowEnumerationCompletion>(effects.Completion!.Target);
            Assert.True(owner.NativeCaptureRetirement.IsCompletedSuccessfully);
            Assert.False(owner.ManagedInvocationDrain.IsCompleted);
            Assert.False(operation.IsCompleted);
            Assert.Equal((1, 128, 1), pool.GetUsage());
            Assert.Equal(1, effects.RetainAttempts);
            Assert.Equal(1, effects.ContentReferences);
            Assert.Equal(1, runtime.InvokeReturns);
        }
        finally
        {
            allowCompletedExit.Set();
            await invocation.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Empty(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the nested fatal from the actual last physical capture root-free helper.")]
    public async Task LateLastCopyRootFreeFaultEndsWithoutGuessingRetirement()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        nint extra = 0;
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                runtime.Invoke(pointer, 10, 0);
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        Assert.False(operation.IsCompleted);
        Assert.Equal(1, runtime.ReleaseAttempts);
        var fatal = new OutOfMemoryException("Controlled late native retirement fatal.");
        runtime.RootFreeFailure = new IOException("Controlled root-free wrapper.", fatal);

        runtime.ReleaseBlock(extra);

        var actual = await Record.ExceptionAsync(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(fatal, actual);
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        var owner = Assert.IsType<MacOSRemoteWindowEnumerationCompletion>(effects.Completion!.Target);
        Assert.True(owner.IsReleased);
        Assert.False(owner.NativeCaptureRetirement.IsCompleted);
        Assert.False(owner.ManagedInvocationDrain.IsCompleted);
        owner.Dispose();
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
    }

    [Fact]
    public async Task ExtraNativeCopyKeepsHealthyEnumerationChargedUntilLastRelease()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        nint extra = 0;
        var effects = new EnumerationEffects(runtime)
        {
            DispatchCallback = pointer =>
            {
                extra = runtime.CopyBlock(pointer);
                runtime.Invoke(pointer, 10, 0);
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();

        try
        {
            Assert.Equal(1, runtime.ReleaseAttempts);
            Assert.Equal(0, runtime.RootFreeAttempts);
            Assert.False(operation.IsCompleted);
            Assert.Equal((1, 128, 1), pool.GetUsage());
            Assert.Equal(1, runtime.LiveRoots);
            Assert.Equal(1, runtime.LiveBlocks);
            Assert.Equal(1, effects.ContentReferences);
        }
        finally
        {
            if (extra != 0) { runtime.ReleaseBlock(extra); }
        }

        Assert.Empty(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public void RootAfterEffectFaultKeepsPrimitiveOwnerInOriginalBatchGraph()
    {
        AssertUnknownGraph(CreateUnknownAcquisition(copyFault: false));
    }

    [Fact]
    public void CopyAfterEffectFaultKeepsPrimitiveOwnerInOriginalBatchGraph()
    {
        AssertUnknownGraph(CreateUnknownAcquisition(copyFault: true));
    }

    private static void AssertUnknownGraph((MacOSRemoteWindowSourceOwnershipPool Pool,
        WeakReference Completion, WeakReference Effects, WeakReference Context, WeakReference Catalog) graph)
    {
        var (pool, completion, effects, context, catalog) = graph;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.True(completion.IsAlive);
        Assert.True(effects.IsAlive);
        Assert.True(context.IsAlive);
        Assert.True(catalog.IsAlive);
        GC.KeepAlive(pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the original nested fatal after the actual controlled root side effect.")]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Completion,
        WeakReference Effects, WeakReference Context, WeakReference Catalog) CreateUnknownAcquisition(bool copyFault)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("Controlled acquisition after-effect fatal.");
        var fault = new IOException("Controlled acquisition wrapper.", fatal);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime
        {
            RootAllocationFailure = copyFault ? null : fault,
            CopyFailure = copyFault ? fault : null,
        };
        var effects = new EnumerationEffects(runtime);

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Same(fatal, actual);
        Assert.True(context.IsClosed);
        Assert.Equal(1, runtime.RootAllocationAttempts);
        Assert.Equal(1, runtime.LiveRoots);
        Assert.Equal(copyFault ? 1 : 0, runtime.CopyAttempts);
        Assert.Equal(copyFault ? 1 : 0, runtime.LiveBlocks);
        Assert.Equal(0, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.ReleaseAttempts);
        Assert.Equal(0, effects.DispatchAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        // Fixture-only raw cleanup removes its GCHandle root before the probe;
        // it neither calls primitive cleanup nor confirms any unknown debt.
        return (pool, effects.Completion!, new(effects), new(context), new(catalog));
    }

    private sealed class EnumerationEffects(IMacOSRemoteWindowBlockOperations runtime)
        : IMacOSRemoteWindowEnumerationOperations
    {
        internal WeakReference? Completion;
        internal int DispatchAttempts;
        internal int ContentReferences = 1;
        internal int RetainAttempts;
        internal int ContentReleaseAttempts;
        internal Exception? RetainFailure { get; init; }
        internal Exception? ContentReleaseFailure { get; init; }
        internal int PoolPushAttempts;
        internal int PoolPopAttempts;
        internal Action<nint>? DispatchCallback { get; init; }
        internal Action? BeforeCompleted { get; init; }
        internal nuint WindowCount { get; init; }
        internal WindowEffects? WindowOperations { get; init; }
        internal Exception? SecondWindowFailure { get; init; }
        internal bool RevokeBeforeDispatch { get; init; }
        private int preflights;
        public bool PreflightCaptureAccess() => !(++preflights == 2 && RevokeBeforeDispatch);
        public bool HasExistingApplication() => true;
        public void InitializeRuntime() { }
        public IMacOSRemoteWindowEnumerationCompletion PrepareCompletion(
            Action<nint, nint> action, Action<Exception> failure, Action completed)
        {
            var owner = MacOSRemoteWindowEnumerationCompletion.PrepareWithOperations(
                action, failure, () => { BeforeCompleted?.Invoke(); completed(); }, runtime);
            Completion = new(owner);
            return owner;
        }
        public nint PushAutoreleasePool() => ++PoolPushAttempts;
        public void PopAutoreleasePool(nint pool) { PoolPopAttempts++; }
        public void Dispatch(nint completion)
        {
            DispatchAttempts++;
            if (DispatchCallback is not null) { DispatchCallback(completion); return; }
            throw new InvalidOperationException("Unexpected dispatch.");
        }
        public nint RetainContent(nint content)
        {
            RetainAttempts++;
            ContentReferences++;
            if (RetainFailure is not null) { throw RetainFailure; }
            return content;
        }
        public void ReleaseContent(nint content)
        {
            ContentReleaseAttempts++;
            ContentReferences--;
            if (ContentReleaseFailure is not null) { throw ContentReleaseFailure; }
        }
        public nint GetWindows(nint content) => 0;
        public nuint GetWindowCount(nint windows) => WindowCount;
        public nint GetWindow(nint windows, nuint index)
        {
            if (index == 1 && SecondWindowFailure is not null) { throw SecondWindowFailure; }
            return WindowEffects.Window;
        }
        public IMacOSRemoteWindowSourceCreationOperations SourceCreationOperations =>
            WindowOperations ?? throw new InvalidOperationException("Unexpected source creation.");
    }

    private sealed class WindowEffects : IMacOSRemoteWindowSourceCreationOperations
    {
        internal static readonly nint Window = 40;
        internal static readonly nint Filter = 50;
        internal int WindowReferences = 1;
        internal int FilterReferences;
        internal Exception? FilterReleaseFailure { get; init; }
        internal Exception? WindowRetainFailure { get; init; }
        internal List<nint> ReleaseAttempts { get; } = [];
        public bool TryGetIdentity(nint window, out MacOSRemoteWindowNativeIdentity identity)
        {
            identity = new(1, 2, 3, 4);
            return true;
        }
        public nint AllocateFilter() { FilterReferences++; return Filter; }
        public nint InitializeFilter(nint allocatedFilter, nint window) => allocatedFilter;
        public (double X, double Y, double Width, double Height) GetFrame(nint window) => (0, 0, 16, 16);
        public float GetScale(nint filter) => 1;
        public bool ValidateWindow(MacOSRemoteWindowNativeIdentity identity, NativeRemoteWindowGeometry geometry) => true;
        public nint Retain(nint owner)
        {
            WindowReferences++;
            if (WindowRetainFailure is not null) { throw WindowRetainFailure; }
            return owner;
        }
        public void Release(nint owner)
        {
            ReleaseAttempts.Add(owner);
            if (owner == Filter)
            {
                FilterReferences--;
                if (FilterReleaseFailure is not null) { throw FilterReleaseFailure; }
            }
            else if (owner == Window) { WindowReferences--; }
            else { throw new InvalidOperationException("Unexpected controlled source owner."); }
        }
        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => true;
    }

    private sealed class ThrowingFailureSink(Exception failure) : IMacOSRemoteWindowSourceCreationFailureSink
    {
        public void RecordProducerFailure(Exception exception) => throw failure;
    }
}
