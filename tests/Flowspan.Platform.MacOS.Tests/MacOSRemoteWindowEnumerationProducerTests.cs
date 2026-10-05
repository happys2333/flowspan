using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowEnumerationProducerTests
{
    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects an earlier dispatch fatal followed by a later admitted callback-retain fatal.")]
    public async Task LaterAdmittedCallbackFatalCannotOverwriteEarlierDispatchFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var retainHeld = new ManualResetEventSlim();
        using var allowRetainReturn = new ManualResetEventSlim();
        var fatal = new OutOfMemoryException("private earlier dispatch fatal");
        Task? admitted = null;
        var effects = new EnumerationEffects(new IOException("private later callback wrapper",
            new OutOfMemoryException("private later callback fatal")))
        {
            RetainCallback = () =>
            {
                retainHeld.Set();
                if (!allowRetainReturn.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Controlled retain return timed out.");
                }
            },
            DispatchCallback = completion =>
            {
                admitted = Task.Factory.StartNew(
                    () => completion.Invoke(EnumerationEffects.Content, 0),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.True(retainHeld.Wait(TimeSpan.FromSeconds(5)));
                throw new IOException("private earlier dispatch wrapper", fatal);
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        try
        {
            Assert.False(operation.IsCompleted);
            Assert.Equal(2, effects.ContentReferences);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowRetainReturn.Set();
            if (admitted is not null) { await admitted.WaitAsync(TimeSpan.FromSeconds(5)); }
        }

        var actual = await Record.ExceptionAsync(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(fatal, actual);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(2, effects.ContentReferences);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal(0, effects.QueryAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a first-pool cleanup fatal after ordinary dispatch failure and a later content cleanup fatal.")]
    public void EarlierFirstPoolFatalCannotBeOverwrittenByLaterContentFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private earlier first-pool cleanup fatal");
        var effects = new EnumerationEffects(null)
        {
            DispatchFailure = new IOException("private ordinary dispatch failure"),
            FirstPoolPopFailure = new IOException("private first-pool wrapper", fatal),
            ReleaseFailure = new IOException("private later content wrapper",
                new OutOfMemoryException("private later content cleanup fatal")),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Same(fatal, actual);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.PoolPopAttempts);
        Assert.Equal(1, effects.Completion!.DisposeAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a contained completion-release observer fatal after a healthy source result.")]
    public void ContainedCompletionReleaseFaultRejectsSourcesWithoutHidingFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private contained completion release fatal");
        var windows = new WindowEffects();
        var effects = new EnumerationEffects(null)
        {
            WindowCount = 1,
            WindowOperations = windows,
            CompletionReportedFailure = new IOException("private completion observer wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Same(fatal, actual);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(1, windows.WindowReferences);
        Assert.True(effects.Completion!.IsReleased);
        Assert.Equal(1, effects.Completion.DisposeAttempts);
        Assert.Equal((1, 127, 1), pool.GetUsage());
    }

    [Fact]
    public async Task StaleContextAndLateOldCallbackCannotPoisonReusedBatch()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var originalBatch));
        var originalContext = new MacOSRemoteWindowSourceCreationContext(pool, originalBatch!);
        var original = new EnumerationEffects(null)
        {
            BeforeInvocationExit = sequence =>
            {
                if (sequence == 2) { throw new IOException("private old callback fault"); }
            },
        };
        Assert.Empty(await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(originalContext, original));
        pool.CompleteBatch(originalBatch!, originalContext);
        Assert.True(pool.TryReserveBatch(owner!, out var replacementBatch));
        Assert.Same(originalBatch, replacementBatch);
        var replacementContext = new MacOSRemoteWindowSourceCreationContext(pool, replacementBatch!);
        var rejected = new EnumerationEffects(null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(originalContext, rejected));
        Assert.Null(rejected.Completion);
        Assert.Equal(0, rejected.PoolPushAttempts);
        Assert.Equal(0, rejected.RetainAttempts);
        original.Completion!.Invoke(EnumerationEffects.Content, 0);
        Assert.Equal(1, original.RetainAttempts);
        Assert.Equal(1, original.ContentReferences);
        Assert.False(replacementContext.IsClosed);

        var replacement = new EnumerationEffects(null);
        Assert.Empty(await MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(replacementContext, replacement));
        pool.CompleteBatch(replacementBatch!, replacementContext);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task UnknownEnumerationDebtRejectsCapacityBeforeAnotherOwnedEffect()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var charged = new EnumerationEffects(null)
        {
            CallbackCount = 0,
            DispatchFailure = new IOException("private unknown dispatch effect"),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, charged));
        var rejected = new EnumerationEffects(null);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, rejected));

        Assert.Equal("macos_source_ownership_capacity_exhausted", actual.Message);
        Assert.Null(rejected.Completion);
        Assert.Equal(0, rejected.PoolPushAttempts);
        Assert.Equal(0, rejected.DispatchAttempts);
        Assert.Equal(0, rejected.RetainAttempts);
        Assert.Empty(rejected.ReleaseAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    public async Task HealthyNonemptyEnumerationTransfersOnlyTheOriginalSourceCharge()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var windows = new WindowEffects();
        var effects = new EnumerationEffects(null) { WindowCount = 1, WindowOperations = windows };
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources =
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects);
        try
        {
            Assert.Single(sources);
            Assert.Equal((1, 1, 0), pool.GetUsage());
            Assert.Equal(2, windows.WindowReferences);
            Assert.Equal(1, windows.FilterReferences);
            Assert.Empty(windows.ReleaseAttempts);
            Assert.Equal(1, effects.ContentReferences);
            Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
            Assert.True(effects.Completion!.IsReleased);
        }
        finally
        {
            foreach (IMacOSRemoteWindowNativeSource source in sources) { source.Dispose(); }
        }

        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(1, windows.WindowReferences);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
    }

    [Fact]
    public async Task DispatchFailureWithoutCallbackDoesNotWaitForMissingInvocationOrAdmitLateContent()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new EnumerationEffects(null)
        {
            CallbackCount = 0,
            DispatchFailure = new IOException("private dispatch failure without callback"),
        };

        var actual = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal("macos_source_producer_unavailable", actual.Message);
        Assert.Equal(1, effects.DispatchAttempts);
        Assert.Equal(1, effects.PoolPopAttempts);
        Assert.Equal(0, effects.RetainAttempts);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.True(effects.Completion!.IsReleased);
        effects.Completion.Invoke(EnumerationEffects.Content, 0);
        Assert.Equal(0, effects.RetainAttempts);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a duplicate invocation fault while the admitted content retain is still in flight.")]
    public async Task DuplicateInvocationFaultCannotDoubleReleaseLaterConfirmedContent()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var retainHeld = new ManualResetEventSlim();
        using var allowRetainReturn = new ManualResetEventSlim();
        var fatal = new OutOfMemoryException("private duplicate invocation fatal");
        Task? admitted = null;
        var effects = new EnumerationEffects(null)
        {
            RetainCallback = () =>
            {
                retainHeld.Set();
                if (!allowRetainReturn.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Controlled retain return timed out.");
                }
            },
            BeforeInvocationExit = sequence =>
            {
                if (sequence == 2) { throw new IOException("private duplicate wrapper", fatal); }
            },
            DispatchCallback = completion =>
            {
                admitted = Task.Factory.StartNew(
                    () => completion.Invoke(EnumerationEffects.Content, 0),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.True(retainHeld.Wait(TimeSpan.FromSeconds(5)));
                completion.Invoke(EnumerationEffects.Content, 0);
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        try
        {
            Assert.False(operation.IsCompleted);
            Assert.Equal(2, effects.ContentReferences);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowRetainReturn.Set();
            if (admitted is not null) { await admitted.WaitAsync(TimeSpan.FromSeconds(5)); }
        }

        var actual = await Record.ExceptionAsync(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(0, effects.QueryAttempts);
        Assert.Equal(2, effects.Completion!.InvocationExits);
        Assert.Same(fatal, actual);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects an earlier dispatch fatal and a later consumed completion-release fatal.")]
    public void CompletionCleanupFailureCannotHideEarlierDispatchFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private earlier dispatch fatal");
        var effects = new EnumerationEffects(null)
        {
            DispatchFailure = new IOException("private dispatch wrapper", fatal),
            CompletionReleaseFailure = new IOException("private completion release wrapper",
                new OutOfMemoryException("private later completion release fatal")),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Same(fatal, actual);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.PoolPopAttempts);
        Assert.True(effects.Completion!.IsReleased);
        Assert.Equal(1, effects.Completion.DisposeAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a nested fatal after the query autorelease-pool push effect.")]
    public void SecondPoolPushAfterEffectFaultCleansContentWithoutReturningUnknownPoolDebt()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private query pool push fatal");
        var effects = new EnumerationEffects(null)
        {
            SecondPoolPushFailure = new IOException("private query pool push wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(2, effects.PoolPushAttempts);
        Assert.Equal(1, effects.PoolOwners);
        Assert.Equal(1, effects.PoolPopAttempts);
        Assert.Equal(0, effects.QueryAttempts);
        Assert.Same(fatal, actual);
        Assert.True(effects.Completion!.IsReleased);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a nested fatal after the first autorelease-pool push effect.")]
    public void FirstPoolPushAfterEffectFaultClosesContextAndKeepsOriginalBatch()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private pool push fatal");
        var effects = new EnumerationEffects(null)
        {
            FirstPoolPushFailure = new IOException("private pool push wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.True(context.IsClosed);
        Assert.Same(fatal, actual);
        Assert.Equal(1, effects.PoolPushAttempts);
        Assert.Equal(1, effects.PoolOwners);
        Assert.Equal(0, effects.PoolPopAttempts);
        Assert.Equal(0, effects.DispatchAttempts);
        Assert.Equal(0, effects.RetainAttempts);
        Assert.True(effects.Completion!.IsReleased);
        effects.Completion.Invoke(EnumerationEffects.Content, 0);
        Assert.Equal(0, effects.RetainAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects an original dispatch fatal followed by an independent first-pool cleanup fault.")]
    public void FirstPoolCleanupFailureDoesNotSkipContentOrHideEarlierDispatchFatal(bool laterFatal)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private original dispatch fatal");
        Exception cleanupFailure = laterFatal
            ? new IOException("private pool wrapper", new OutOfMemoryException("private later pool fatal"))
            : new IOException("private first pool cleanup");
        var effects = new EnumerationEffects(null)
        {
            DispatchFailure = new IOException("private dispatch wrapper", fatal),
            FirstPoolPopFailure = cleanupFailure,
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.PoolPopAttempts);
        Assert.Equal(0, effects.QueryAttempts);
        Assert.Same(fatal, actual);
        Assert.True(context.IsClosed);
        Assert.True(effects.Completion!.IsReleased);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the original fatal after dispatch has delivered a retained content callback.")]
    public void DispatchAfterCallbackFaultStillReleasesConfirmedContent()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private dispatch after callback fatal");
        var effects = new EnumerationEffects(null)
        {
            DispatchFailure = new IOException("private dispatch wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.PoolPopAttempts);
        Assert.Equal(0, effects.QueryAttempts);
        Assert.Same(fatal, actual);
        Assert.True(context.IsClosed);
        Assert.True(effects.Completion!.IsReleased);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    public async Task DuplicateInvocationExitCannotReleaseContentBeforeAdmittedInvocationExits()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        using var callbackHeld = new ManualResetEventSlim();
        using var allowExit = new ManualResetEventSlim();
        Task? invocation = null;
        var effects = new EnumerationEffects(null)
        {
            BeforeFirstInvocationExit = () =>
            {
                callbackHeld.Set();
                if (!allowExit.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Controlled invocation exit timed out.");
                }
            },
            DispatchCallback = completion =>
            {
                invocation = Task.Run(() => completion.Invoke(EnumerationEffects.Content, 0));
                Assert.True(callbackHeld.Wait(TimeSpan.FromSeconds(5)));
                completion.Invoke(EnumerationEffects.Content, 0);
            },
        };
        Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>> operation =
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects).AsTask();
        try
        {
            Assert.False(operation.IsCompleted);
            Assert.Equal(2, effects.ContentReferences);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowExit.Set();
            if (invocation is not null) { await invocation.WaitAsync(TimeSpan.FromSeconds(5)); }
        }

        Assert.Empty(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(2, effects.Completion!.InvocationExits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects an earlier content fatal and a later independent pool cleanup fault.")]
    public void PoolCleanupFailureDoesNotSkipSourcesOrHideEarlierFatal(bool laterFatal)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private earlier content cleanup fatal");
        var windows = new WindowEffects();
        var effects = new EnumerationEffects(null)
        {
            WindowCount = 1,
            WindowOperations = windows,
            ReleaseFailure = new IOException("private earlier content wrapper", fatal),
            PoolPopFailure = laterFatal
                ? new IOException("private later pool wrapper", new OutOfMemoryException("private later pool fatal"))
                : new IOException("private later pool ordinary"),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal(2, effects.PoolPopAttempts);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(1, windows.WindowReferences);
        Assert.Equal((1, 127, 1), pool.GetUsage());
        Assert.Same(fatal, actual);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a content cleanup fatal after creating an actual source.")]
    public void ContentCleanupFailureStillCleansConfirmedSourceOwners()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private content cleanup fatal with source");
        var windows = new WindowEffects();
        var effects = new EnumerationEffects(null)
        {
            WindowCount = 1,
            WindowOperations = windows,
            ReleaseFailure = new IOException("private content cleanup wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal(1, windows.AllocationAttempts);
        Assert.Equal(1, windows.RetainAttempts);
        Assert.Equal(new[] { WindowEffects.Filter, WindowEffects.Window }, windows.ReleaseAttempts);
        Assert.Equal(0, windows.FilterReferences);
        Assert.Equal(1, windows.WindowReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal((1, 127, 1), pool.GetUsage());
        Assert.Same(fatal, actual);
    }

    [Fact]
    public void OrdinaryQueryFailureClosesExactContextBeforeOutwardFailure()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var effects = new EnumerationEffects(null) { QueryFailure = new IOException("private ordinary query") };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.ContentReferences);
        Assert.True(context.IsClosed);
        var rejected = new EnumerationEffects(null);
        Assert.Throws<InvalidOperationException>(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, rejected)
                .AsTask().GetAwaiter().GetResult());
        Assert.Null(rejected.Completion);
        Assert.Equal(0, rejected.RetainAttempts);
    }

    [Fact]
    public async Task DuplicateCompletionCannotAcquireAdditionalContentOwnership()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new EnumerationEffects(null) { CallbackCount = 2 };

        Assert.Empty(await MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects));

        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(2, effects.Completion!.InvocationExits);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a cleanup fatal after an ordinary window-query failure.")]
    public void OrdinaryQueryFailureCannotHideContentCleanupFatal()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private cleanup fatal after ordinary query");
        var effects = new EnumerationEffects(null)
        {
            QueryFailure = new IOException("private query failure"),
            ReleaseFailure = new IOException("private cleanup wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        Assert.Equal(2, effects.PoolPopAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Same(fatal, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void InvalidContentRetainReturnNeverPublishesOrGuessesRelease(int returned)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new EnumerationEffects(null) { RetainResult = returned };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateDirectWithOperations(pool, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(2, effects.ContentReferences);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Empty(effects.ReleaseAttempts);
        var diagnosis = Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal("macos_source_producer_unavailable", diagnosis.Message);
        Assert.Null(diagnosis.InnerException);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a retain fatal after reentrant original-batch settlement.")]
    public void EnumerationReentrantSettlementCannotReturnUnknownContentDebt()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private reentrant content-retain fatal");
        var effects = new EnumerationEffects(new IOException("private reentrant wrapper", fatal))
        {
            RetainCallback = () =>
            {
                pool.CompleteBatch(batch!, context);
                pool.CloseCatalog(owner!, registryConfirmed: true);
            },
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Same(fatal, actual);
        Assert.Equal(2, effects.ContentReferences);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.True(context.IsClosed);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the original fatal after a native content-release side effect.")]
    public void EnumerationContentReleaseAfterEffectFaultKeepsBatchDebt()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private content-release fatal");
        var effects = new EnumerationEffects(null)
        {
            ReleaseFailure = new IOException("private content-release wrapper", fatal),
        };

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.NotNull(actual);
        Assert.Equal(1, effects.ContentReferences);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(2, effects.PoolPopAttempts);
        Assert.Same(fatal, actual);
        Assert.True(context.IsClosed);
        pool.CompleteBatch(batch!, context);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(new[] { EnumerationEffects.Content }, effects.ReleaseAttempts);
    }

    [Fact]
    public void EnumerationContentRetainAfterEffectFaultKeepsBatchDebt()
    {
        var (pool, effects, context, catalog, completion) = CreateUnknownContentRetain();
        Assert.Equal((1, 128, 1), pool.GetUsage());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(effects.IsAlive);
        Assert.True(context.IsAlive);
        Assert.True(catalog.IsAlive);
        Assert.True(completion.IsAlive);
        GC.KeepAlive(pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the original nested fatal after a native content-retain side effect.")]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Effects,
        WeakReference Context, WeakReference Catalog, WeakReference Completion) CreateUnknownContentRetain()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private content-retain fatal");
        var effects = new EnumerationEffects(new IOException("private content-retain wrapper", fatal));

        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.EnumerateWithOperations(context, effects)
                .AsTask().GetAwaiter().GetResult());

        Assert.Same(fatal, actual);
        Assert.Equal(2, effects.ContentReferences);
        Assert.Equal(1, effects.RetainAttempts);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.NotNull(effects.Completion);
        Assert.Equal(1, effects.Completion.InvocationExits);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        return (pool, new(effects), new(context), new(catalog), new(effects.Completion));
    }

    [SuppressMessage("Design", "CA1001", Justification = "Observes the completion whose ownership is held and disposed by the real enumeration core.")]
    private sealed class EnumerationEffects(Exception? retainFailure) : IMacOSRemoteWindowEnumerationOperations
    {
        internal static readonly nint Content = 10;
        internal int ContentReferences = 1;
        internal int RetainAttempts;
        internal int PoolPopAttempts;
        internal int PoolPushAttempts;
        internal int PoolOwners;
        internal int DispatchAttempts;
        internal Exception? ReleaseFailure { get; init; }
        internal Exception? QueryFailure { get; init; }
        internal Exception? PoolPopFailure { get; init; }
        internal Exception? FirstPoolPopFailure { get; init; }
        internal Exception? FirstPoolPushFailure { get; init; }
        internal Exception? SecondPoolPushFailure { get; init; }
        internal Exception? DispatchFailure { get; init; }
        internal Exception? CompletionReleaseFailure { get; init; }
        internal Exception? CompletionReportedFailure { get; init; }
        internal int QueryAttempts;
        internal Action? RetainCallback { get; init; }
        internal nint? RetainResult { get; init; }
        internal int CallbackCount { get; init; } = 1;
        internal nuint WindowCount { get; init; }
        internal WindowEffects? WindowOperations { get; init; }
        internal Action? BeforeFirstInvocationExit { get; init; }
        internal Action<int>? BeforeInvocationExit { get; init; }
        internal Action<EnumerationCompletion>? DispatchCallback { get; init; }
        internal List<nint> ReleaseAttempts { get; } = [];
        internal EnumerationCompletion? Completion;
        public bool PreflightCaptureAccess() => true;
        public bool HasExistingApplication() => true;
        public void InitializeRuntime() { }
        public IMacOSRemoteWindowEnumerationCompletion PrepareCompletion(
            Action<nint, nint> action, Action<Exception> failure, Action completed)
        {
            Completion = new(action, failure, completed)
            {
                BeforeFirstExit = BeforeFirstInvocationExit,
                BeforeExit = BeforeInvocationExit,
                ReleaseFailure = CompletionReleaseFailure,
                ReportedReleaseFailure = CompletionReportedFailure,
            };
            return Completion;
        }
        public nint PushAutoreleasePool()
        {
            PoolPushAttempts++;
            PoolOwners++;
            if (PoolPushAttempts == 1 && FirstPoolPushFailure is not null) { throw FirstPoolPushFailure; }
            if (PoolPushAttempts == 2 && SecondPoolPushFailure is not null) { throw SecondPoolPushFailure; }
            return PoolPushAttempts;
        }
        public void PopAutoreleasePool(nint pool)
        {
            PoolPopAttempts++;
            PoolOwners--;
            if (PoolPopAttempts == 1 && FirstPoolPopFailure is not null) { throw FirstPoolPopFailure; }
            if (PoolPopAttempts == 2 && PoolPopFailure is not null) { throw PoolPopFailure; }
        }
        public void Dispatch(nint completion)
        {
            DispatchAttempts++;
            if (DispatchCallback is not null) { DispatchCallback(Completion!); return; }
            for (int index = 0; index < CallbackCount; index++)
            {
                Completion!.Invoke(Content, 0);
            }
            if (DispatchFailure is not null) { throw DispatchFailure; }
        }
        public nint RetainContent(nint content)
        {
            RetainAttempts++;
            ContentReferences++;
            RetainCallback?.Invoke();
            if (retainFailure is not null) { throw retainFailure; }
            return RetainResult ?? content;
        }
        public void ReleaseContent(nint content)
        {
            ReleaseAttempts.Add(content);
            ContentReferences--;
            if (ReleaseFailure is not null) { throw ReleaseFailure; }
        }
        public nint GetWindows(nint content)
        {
            QueryAttempts++;
            if (QueryFailure is not null) { throw QueryFailure; }
            return 20;
        }
        public nuint GetWindowCount(nint windows) => WindowCount;
        public nint GetWindow(nint windows, nuint index) => WindowEffects.Window;
        public IMacOSRemoteWindowSourceCreationOperations SourceCreationOperations =>
            WindowOperations ?? throw new InvalidOperationException("Unexpected source creation.");
    }

    private sealed class WindowEffects : IMacOSRemoteWindowSourceCreationOperations
    {
        internal static readonly nint Window = 40;
        internal static readonly nint Filter = 50;
        internal int WindowReferences = 1;
        internal int FilterReferences;
        internal int AllocationAttempts;
        internal int RetainAttempts;
        internal List<nint> ReleaseAttempts { get; } = [];
        public bool TryGetIdentity(nint window, out MacOSRemoteWindowNativeIdentity identity)
        {
            identity = new(1, 2, 3, 4);
            return true;
        }
        public nint AllocateFilter()
        {
            AllocationAttempts++;
            FilterReferences++;
            return Filter;
        }
        public nint InitializeFilter(nint allocatedFilter, nint window) => allocatedFilter;
        public (double X, double Y, double Width, double Height) GetFrame(nint window) => (0, 0, 16, 16);
        public float GetScale(nint filter) => 1;
        public bool ValidateWindow(MacOSRemoteWindowNativeIdentity identity, NativeRemoteWindowGeometry geometry) => true;
        public nint Retain(nint owner)
        {
            RetainAttempts++;
            WindowReferences++;
            return owner;
        }
        public void Release(nint owner)
        {
            ReleaseAttempts.Add(owner);
            if (owner == Filter) { FilterReferences--; }
            else if (owner == Window) { WindowReferences--; }
            else { throw new InvalidOperationException("Unexpected source owner."); }
        }
        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => true;
    }

    // Mimics only the external completion ABI's action/fault/exit ordering.
    // It provides no native-copy lifetime or primitive factory proof.
    private sealed class EnumerationCompletion(
        Action<nint, nint> action, Action<Exception> failure, Action completed)
        : IMacOSRemoteWindowEnumerationCompletion
    {
        internal int InvocationExits;
        internal int DisposeAttempts;
        private int invocationSequence;
        internal Action? BeforeFirstExit { get; init; }
        internal Action<int>? BeforeExit { get; init; }
        internal Exception? ReleaseFailure { get; init; }
        internal Exception? ReportedReleaseFailure { get; init; }
        public nint Pointer => 30;
        public bool IsReleased { get; private set; }
        public Exception? FirstFailure { get; private set; }
        public Task NativeCaptureRetirement => Task.CompletedTask;
        public Task ManagedInvocationDrain => Task.CompletedTask;
        public void AcquireCopy() { }

        internal void Invoke(nint content, nint error)
        {
            int sequence = Interlocked.Increment(ref invocationSequence);
            try
            {
                action(content, error);
                if (sequence == 1) { BeforeFirstExit?.Invoke(); }
                BeforeExit?.Invoke(sequence);
            }
            catch (Exception exception)
            {
                FirstFailure ??= exception;
                failure(exception);
            }
            finally
            {
                Interlocked.Increment(ref InvocationExits);
                completed();
            }
        }

        public void Dispose()
        {
            DisposeAttempts++;
            IsReleased = true;
            if (ReportedReleaseFailure is not null)
            {
                FirstFailure ??= ReportedReleaseFailure;
                failure(ReportedReleaseFailure);
            }
            if (ReleaseFailure is not null) { throw ReleaseFailure; }
        }
    }
}
