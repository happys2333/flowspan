using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowSourceProducerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a query fatal and optional later cleanup failure at system boundaries.")]
    public async Task QueryFatalSurvivesKnownFilterCleanup(bool cleanupThrows)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private frame-query fatal");
        var effects = new CreationEffects
        {
            FrameFailure = new IOException("private frame-query wrapper", fatal),
            FilterReleaseFailure = cleanupThrows ? new IOException("private later filter cleanup failure") : null,
        };

        var failure = await Record.ExceptionAsync(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
                pool, CreationEffects.Window, effects));

        Assert.NotNull(failure);
        Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(failure));
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(new[] { CreationEffects.Filter }, effects.ReleaseAttempts);
        Assert.Equal(cleanupThrows ? (1, 128, 1) : (0, 0, 0), pool.GetUsage());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RejectedCandidateReturnsConfirmedFilterAndCapacity(int rejection)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new CreationEffects
        {
            Width = rejection == 0 ? 0 : rejection == 1 ? 16_385 : 16,
            WindowValid = rejection != 2,
        };

        Assert.Null(await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects));

        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(new[] { CreationEffects.Filter }, effects.ReleaseAttempts);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects cleanup fatal at a confirmed native owner boundary.")]
    public void FailedHandoffAttemptsEachConfirmedOwnerCleanupOnce(bool failWindow, bool failFilter)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private known-owner cleanup fatal");
        var effects = new CreationEffects
        {
            AllocationCallback = context.Close,
            WindowReleaseFailure = failWindow ? new IOException("private window cleanup wrapper", fatal) : null,
            FilterReleaseFailure = failFilter ? new IOException("private filter cleanup failure") : null,
        };

        var failure = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
                context, CreationEffects.Window, effects));

        Assert.NotNull(failure);
        if (failWindow) { Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(failure)); }
        else { Assert.IsType<IOException>(failure); }
        Assert.Equal(new[] { CreationEffects.Window, CreationEffects.Filter }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        var rejected = new CreationEffects();
        Assert.Throws<InvalidOperationException>(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
                context, CreationEffects.Window, rejected));
        Assert.Equal(0, rejected.AllocationAttempts);
        Assert.Equal(new[] { CreationEffects.Window, CreationEffects.Filter }, effects.ReleaseAttempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(21)]
    public void InitializationAfterEffectFailureNeverGuessesReceiverOrResultRelease(int initializerResult)
    {
        var (pool, effects) = CreateUnknownInitialization(initializerResult);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.True(effects.IsAlive);
        GC.KeepAlive(pool);
    }

    [Fact]
    public async Task ChangedSelfInitializationOwnsOnlyReturnedFilter()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new CreationEffects { InitializationResult = CreationEffects.ReplacementFilter };
        var source = await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects);
        Assert.NotNull(source);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(1, effects.ReplacementFilterReferences);
        Assert.Equal((1, 1, 0), pool.GetUsage());

        source.Dispose();

        Assert.Equal(new[] { CreationEffects.ReplacementFilter, CreationEffects.Window }, effects.ReleaseAttempts);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(0, effects.ReplacementFilterReferences);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task NilFilterInitializationConsumesReceiverWithoutProducerRelease()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new CreationEffects { InitializationResult = 0 };

        Assert.Null(await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects));

        Assert.Equal(1, effects.InitializationAttempts);
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(0, effects.ReplacementFilterReferences);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Empty(effects.ReleaseAttempts);
    }

    [Fact]
    public async Task NilFilterAllocationDoesNotEnterInitialization()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new CreationEffects { AllocationResult = 0 };

        Assert.Null(await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects));

        Assert.Equal(1, effects.AllocationAttempts);
        Assert.Equal(0, effects.InitializationAttempts);
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Empty(effects.ReleaseAttempts);
    }

    [Fact]
    public async Task DirectLateCleanupFailureDoesNotPoisonReusedBatch()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(2, 129, 1);
        var effects = new CreationEffects();
        var source = await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects);
        Assert.NotNull(source);
        var replacementCatalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(replacementCatalog, out var replacementOwner));
        Assert.True(pool.TryReserveBatch(replacementOwner!, out var replacementBatch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, replacementBatch!);
        effects.FilterReleaseFailure = new IOException("private late direct cleanup failure");

        var failure = Assert.Throws<InvalidOperationException>(source.Dispose);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(source.Dispose));

        Assert.False(context.IsClosed);
        Assert.Equal(new[] { CreationEffects.Filter, CreationEffects.Window }, effects.ReleaseAttempts);
        using (var replacement = MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
            context, CreationEffects.Window, new CreationEffects()))
        {
            Assert.NotNull(replacement);
        }
        pool.CompleteBatch(replacementBatch!, context);
        pool.CloseCatalog(replacementOwner!, registryConfirmed: true);
        Assert.Equal((1, 1, 0), pool.GetUsage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectUnknownAcquisitionRemainsChargedAndReachable(bool failRetain)
    {
        var (pool, effects) = CreateDirectUnknownAcquisition(failRetain);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.True(effects.IsAlive);
        GC.KeepAlive(pool);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectNonOwningOutcomeReturnsAllCapacity(bool identityThrows)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var identityFailure = new IOException("private identity query failure");
        var effects = new CreationEffects { IdentityFailure = identityThrows ? identityFailure : null };
        if (identityThrows)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
                    pool, CreationEffects.Window, effects));
            Assert.Equal("macos_source_producer_unavailable", failure.Message);
            Assert.Null(failure.InnerException);
        }
        else
        {
            Assert.Null(await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
                pool, window: 0, effects));
        }

        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(0, effects.AllocationAttempts);
        Assert.Empty(effects.ReleaseAttempts);
    }

    [Fact]
    public void DirectSourceRemainsReachableFromOnlyItsRealPool()
    {
        var (pool, source, effects) = CreateDirectSourceWithoutCaller();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal((1, 1, 0), pool.GetUsage());
        Assert.True(source.IsAlive);
        Assert.True(effects.IsAlive);
        Assert.IsAssignableFrom<IMacOSRemoteWindowNativeSource>(source.Target).Dispose();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        GC.KeepAlive(pool);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DirectCapacityRefusalDoesNotEnterCreationOrLeakEmptyOwner(int exhaustedCapacity)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(
            exhaustedCapacity == 0 ? 1 : 2, exhaustedCapacity == 2 ? 127 : 256, 1);
        MacOSRemoteWindowSourceOwnershipPool.CatalogRecord? heldOwner = null;
        MacOSRemoteWindowSourceOwnershipPool.BatchRecord? heldBatch = null;
        if (exhaustedCapacity != 2)
        {
            var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
                new MacOSRemoteWindowTestApi(), ownershipPool: pool);
            Assert.True(pool.TryReserveCatalog(catalog, out heldOwner));
            if (exhaustedCapacity == 1) { Assert.True(pool.TryReserveBatch(heldOwner!, out heldBatch)); }
        }
        var before = pool.GetUsage();
        var effects = new CreationEffects();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
                pool, CreationEffects.Window, effects));

        Assert.Equal("macos_source_ownership_capacity_exhausted", failure.Message);
        Assert.Equal(before, pool.GetUsage());
        Assert.Equal(0, effects.IdentityQueries);
        Assert.Equal(0, effects.AllocationAttempts);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Empty(effects.ReleaseAttempts);
        if (heldBatch is not null) { pool.CompleteBatch(heldBatch); }
        if (heldOwner is not null) { pool.CloseCatalog(heldOwner, registryConfirmed: true); }
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task DirectSourceUsesExistingPoolUntilCleanup()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new CreationEffects();
        var source = await MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects);
        Assert.NotNull(source);
        Assert.Equal((1, 1, 0), pool.GetUsage());

        source.Dispose();

        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(new[] { CreationEffects.Filter, CreationEffects.Window }, effects.ReleaseAttempts);
    }

    [Fact]
    public async Task CompletedBatchCannotPublishLateSourceIntoReplacement()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(2, 256, 1);
        var replacementCatalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(replacementCatalog, out var replacementOwner));
        MacOSRemoteWindowSourceCreationContext? replacementContext = null;
        var api = new CreationApi
        {
            AllocationCallback = context =>
            {
                pool.CompleteBatch(context.Batch, context);
                Assert.True(pool.TryReserveBatch(replacementOwner!, out var batch));
                replacementContext = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
            },
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            api, ownershipPool: pool);

        var result = await catalog.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("macos_source_catalog_unavailable", result.ReasonCode);
        Assert.Empty(catalog.GetSnapshot());
        var effects = Assert.Single(api.Effects);
        Assert.Equal(new[] { CreationEffects.Window, CreationEffects.Filter }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        Assert.NotNull(replacementContext);
        using (var replacementSource = MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
            replacementContext, CreationEffects.Window, new CreationEffects()))
        {
            Assert.NotNull(replacementSource);
        }
        pool.CompleteBatch(replacementContext.Batch, replacementContext);
        pool.CloseCatalog(replacementOwner!, registryConfirmed: true);
        await catalog.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task PublishedSourceCleanupDoesNotReturnEntryObligationEarly()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var api = new CreationApi();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            api, ownershipPool: pool);
        Assert.True((await catalog.RefreshAsync()).Succeeded);

        Assert.Single(api.Produced).Dispose();

        Assert.Equal((1, 1, 0), pool.GetUsage());
        Assert.Single(catalog.GetSnapshot());
        await catalog.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        var effects = Assert.Single(api.Effects);
        Assert.Equal(new[] { CreationEffects.Filter, CreationEffects.Window }, effects.ReleaseAttempts);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
    }

    [Fact]
    public void ReentrantBatchSettlementCannotEraseUnconfirmedAllocation()
    {
        var (pool, effects, context, catalog) = CreateReentrantUnknownAllocation();
        Assert.Equal((1, 1, 0), pool.GetUsage());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(effects.IsAlive);
        Assert.True(context.IsAlive);
        Assert.True(catalog.IsAlive);
        GC.KeepAlive(pool);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void InvalidInitialRetainResultDoesNotPublishOrGuessRelease(int retainResult)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var effects = new CreationEffects { RetainResult = retainResult };

        Assert.Throws<InvalidOperationException>(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
                context, CreationEffects.Window, effects));

        Assert.True(context.IsClosed);
        Assert.Equal(2, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(new[] { CreationEffects.Filter }, effects.ReleaseAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [Fact]
    public async Task LatePublishedCleanupFailureClosesOnlyOriginalCatalog()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(2, 129, 1);
        var api = new CreationApi();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            api, ownershipPool: pool);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        var source = Assert.Single(api.Produced);
        var effects = Assert.Single(api.Effects);
        effects.FilterReleaseFailure = new IOException("private late cleanup failure");
        var replacementCatalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(replacementCatalog, out var replacementOwner));
        Assert.True(pool.TryReserveBatch(replacementOwner!, out var replacementBatch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, replacementBatch!);

        Assert.Throws<InvalidOperationException>(source.Dispose);
        var failure = await catalog.RefreshAsync();
        Assert.False(failure.Succeeded);
        Assert.Equal("macos_source_catalog_unavailable", failure.ReasonCode);
        Assert.Equal(new[] { CreationEffects.Filter, CreationEffects.Window }, effects.ReleaseAttempts);
        using (var replacementSource = MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
            context, CreationEffects.Window, new CreationEffects()))
        {
            Assert.NotNull(replacementSource);
        }
        pool.CompleteBatch(replacementBatch!, context);
        pool.CloseCatalog(replacementOwner!, registryConfirmed: true);
        Assert.Equal((1, 1, 0), pool.GetUsage());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.DisposeAsync());
        Assert.Equal((1, 1, 0), pool.GetUsage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleCreationContextCannotUseReusedBatch(bool replaceCatalog)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var oldContext = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        pool.CompleteBatch(batch!);
        if (replaceCatalog)
        {
            pool.CloseCatalog(owner!, registryConfirmed: true);
            catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
                new MacOSRemoteWindowTestApi(), ownershipPool: pool);
            Assert.True(pool.TryReserveCatalog(catalog, out owner));
        }
        Assert.True(pool.TryReserveBatch(owner!, out var replacementBatch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, replacementBatch!);
        var rejectedEffects = new CreationEffects();

        Assert.Throws<InvalidOperationException>(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
                oldContext, CreationEffects.Window, rejectedEffects));

        Assert.Equal(0, rejectedEffects.FilterReferences);
        Assert.Equal(1, rejectedEffects.WindowReferences);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        pool.CompleteBatch(replacementBatch!, oldContext);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        using (var source = MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
            context, CreationEffects.Window, new CreationEffects()))
        {
            Assert.NotNull(source);
        }
        pool.CompleteBatch(replacementBatch!);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public void UnreleasedSourceRemainsChargedAfterBatchSettlement()
    {
        var (pool, source, effects) = CreateSuccessfulSourceWithoutCaller(settleBatch: true);
        Assert.Equal((1, 1, 0), pool.GetUsage());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(source.IsAlive);
        Assert.True(effects.IsAlive);
        Assert.IsAssignableFrom<IMacOSRemoteWindowNativeSource>(source.Target).Dispose();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        GC.KeepAlive(pool);
    }

    [Fact]
    public async Task FullProducerBatchPublishesWithoutAdditionalSlots()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var api = new CreationApi { Count = 128 };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            api, ownershipPool: pool);

        Assert.True((await catalog.RefreshAsync()).Succeeded);
        Assert.Equal(128, catalog.GetSnapshot().Count);
        Assert.Equal((1, 128, 0), pool.GetUsage());
        await catalog.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.All(api.Effects, effects =>
        {
            Assert.Equal(1, effects.WindowReferences);
            Assert.Equal(0, effects.FilterReferences);
        });
    }

    [Fact]
    public async Task CatalogPassesBoundedCreationContextToProducer()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var api = new CreationApi();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            api, ownershipPool: pool);

        Assert.True((await catalog.RefreshAsync()).Succeeded);
        Assert.Single(catalog.GetSnapshot());
        Assert.Equal((1, 1, 0), pool.GetUsage());
        await catalog.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(1, Assert.Single(api.Effects).WindowReferences);
        Assert.Equal(0, Assert.Single(api.Effects).FilterReferences);
    }

    [Fact]
    public void SuccessfulSourceRemainsReachableBeforeBatchSettlement()
    {
        var (pool, source, effects) = CreateSuccessfulSourceWithoutCaller();
        Assert.Equal((1, 128, 1), pool.GetUsage());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(source.IsAlive);
        Assert.True(effects.IsAlive);
        GC.KeepAlive(pool);
    }

    [Fact]
    public void InitialRetainAfterEffectFaultKeepsDebt()
    {
        var (pool, effects, context, catalog) = CreateUnknownInitialRetain();
        Assert.Equal((1, 128, 1), pool.GetUsage());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(effects.IsAlive);
        Assert.True(context.IsAlive);
        Assert.True(catalog.IsAlive);
        GC.KeepAlive(pool);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Preserves the original nested fatal through independent native cleanup failure.")]
    public void InitialRetainFatalSurvivesConfirmedFilterCleanupFailure()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private initial-retain fatal");
        var effects = new CreationEffects
        {
            RetainFailure = new InvalidOperationException("private retain wrapper", fatal),
            FilterReleaseFailure = new IOException("private filter cleanup failure"),
        };

        Exception? actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(context, CreationEffects.Window, effects));

        Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(actual!));
        Assert.Equal(new[] { CreationEffects.Filter }, effects.ReleaseAttempts);
        Assert.Equal(2, effects.WindowReferences);
        Assert.True(context.IsClosed);
        pool.CompleteBatch(batch!);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        Assert.Equal((1, 128, 1), pool.GetUsage());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested initialization fatal after receiver consumption.")]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Effects)
        CreateUnknownInitialization(int initializerResult)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private initialization fatal");
        var effects = new CreationEffects
        {
            InitializationResult = initializerResult,
            InitializationFailure = new IOException("private initialization wrapper", fatal),
        };
        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
                pool, CreationEffects.Window, effects).AsTask().GetAwaiter().GetResult());
        Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(actual!));
        Assert.Equal(1, effects.InitializationAttempts);
        Assert.Equal(1, effects.WindowReferences);
        Assert.Equal(initializerResult == (int)CreationEffects.Filter ? 1 : 0, effects.FilterReferences);
        Assert.Equal(initializerResult == (int)CreationEffects.ReplacementFilter ? 1 : 0, effects.ReplacementFilterReferences);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        return (pool, new(effects));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the original fatal after the direct acquisition effect.")]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Effects)
        CreateDirectUnknownAcquisition(bool failRetain)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var fatal = new OutOfMemoryException("private direct acquisition fatal");
        var failure = new InvalidOperationException("private direct acquisition wrapper", fatal);
        var effects = new CreationEffects
        {
            AllocationFailure = failRetain ? null : failure,
            RetainFailure = failRetain ? failure : null,
        };
        var actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
                pool, CreationEffects.Window, effects).AsTask().GetAwaiter().GetResult());
        Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(actual!));
        Assert.Equal(failRetain ? 2 : 1, effects.WindowReferences);
        Assert.Equal(failRetain ? 0 : 1, effects.FilterReferences);
        Assert.Equal(failRetain ? [CreationEffects.Filter] : Array.Empty<nint>(), effects.ReleaseAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        return (pool, new(effects));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Source,
        WeakReference Effects) CreateDirectSourceWithoutCaller()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(1, 128, 1);
        var effects = new CreationEffects();
        var source = MacOSRemoteWindowScreenCaptureKitApi.CreateDirectSourceWithOperations(
            pool, CreationEffects.Window, effects).AsTask().GetAwaiter().GetResult();
        Assert.NotNull(source);
        return (pool, new(source), new(effects));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original allocation fatal after reentrant settlement at the native boundary.")]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Effects,
        WeakReference Context, WeakReference Catalog) CreateReentrantUnknownAllocation()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private allocation fatal");
        var effects = new CreationEffects
        {
            AllocationCallback = () => pool.CompleteBatch(batch!, context),
            AllocationFailure = new InvalidOperationException("private allocation wrapper", fatal),
        };
        var failure = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
                context, CreationEffects.Window, effects));
        Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(failure!));
        Assert.True(context.IsClosed);
        Assert.Equal(1, effects.FilterReferences);
        Assert.Empty(effects.ReleaseAttempts);
        pool.CompleteBatch(batch!, context);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        return (pool, new(effects), new(context), new(catalog));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Source,
        WeakReference Effects) CreateSuccessfulSourceWithoutCaller(bool settleBatch = false)
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var effects = new CreationEffects();
        var source = MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
            context, CreationEffects.Window, effects);
        Assert.NotNull(source);
        Assert.Equal(2, effects.WindowReferences);
        Assert.Equal(1, effects.FilterReferences);
        if (settleBatch)
        {
            pool.CompleteBatch(batch!);
            pool.CloseCatalog(owner!, registryConfirmed: true);
        }
        return (pool, new(source), new(effects));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects the original nested fatal at a native system-effect boundary.")]
    private static (MacOSRemoteWindowSourceOwnershipPool Pool, WeakReference Effects,
        WeakReference Context, WeakReference Catalog) CreateUnknownInitialRetain()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            new MacOSRemoteWindowTestApi(), ownershipPool: pool);
        Assert.True(pool.TryReserveCatalog(catalog, out var owner));
        Assert.True(pool.TryReserveBatch(owner!, out var batch));
        var context = new MacOSRemoteWindowSourceCreationContext(pool, batch!);
        var fatal = new OutOfMemoryException("private initial-retain fault");
        var effects = new CreationEffects { RetainFailure = new InvalidOperationException("private wrapper", fatal) };
        Exception? actual = Record.Exception(() =>
            MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(context, CreationEffects.Window, effects));
        Assert.Same(fatal, MacOSRemoteWindowFailure.FindFatal(actual!));
        Assert.Equal(2, effects.WindowReferences);
        Assert.Equal(0, effects.FilterReferences);
        Assert.Equal(new[] { CreationEffects.Filter }, effects.ReleaseAttempts);
        pool.CompleteBatch(batch!);
        pool.CloseCatalog(owner!, registryConfirmed: true);
        return (pool, new(effects), new(context), new(catalog));
    }

    private sealed class CreationEffects : IMacOSRemoteWindowSourceCreationOperations
    {
        internal static readonly nint Window = 10;
        internal static readonly nint Filter = 20;
        internal static readonly nint ReplacementFilter = 21;
        internal Exception? RetainFailure;
        internal Exception? IdentityFailure;
        internal Action? AllocationCallback;
        internal Exception? AllocationFailure;
        internal nint AllocationResult = Filter;
        internal nint? InitializationResult;
        internal Exception? InitializationFailure;
        internal Exception? FrameFailure;
        internal nint? RetainResult;
        internal Exception? FilterReleaseFailure;
        internal Exception? WindowReleaseFailure;
        internal uint IdentityId = 1;
        internal double Width = 16;
        internal bool WindowValid = true;
        internal int WindowReferences = 1;
        internal int FilterReferences;
        internal int ReplacementFilterReferences;
        internal int IdentityQueries;
        internal int AllocationAttempts;
        internal int InitializationAttempts;
        internal List<nint> ReleaseAttempts { get; } = [];
        public bool TryGetIdentity(nint window, out MacOSRemoteWindowNativeIdentity identity)
        {
            IdentityQueries++;
            if (IdentityFailure is { } failure) { throw failure; }
            identity = new(IdentityId, 2, 3, 4);
            return window == Window;
        }
        public nint AllocateFilter()
        {
            AllocationAttempts++;
            if (AllocationResult == 0) { return 0; }
            FilterReferences++;
            AllocationCallback?.Invoke();
            if (AllocationFailure is { } failure) { throw failure; }
            return AllocationResult;
        }
        public nint InitializeFilter(nint allocatedFilter, nint window)
        {
            InitializationAttempts++;
            nint result = InitializationResult ?? allocatedFilter;
            if (result != allocatedFilter)
            {
                FilterReferences--;
                if (result != 0) { ReplacementFilterReferences++; }
            }
            if (InitializationFailure is { } failure) { throw failure; }
            return result;
        }
        public (double X, double Y, double Width, double Height) GetFrame(nint window)
        {
            if (FrameFailure is { } failure) { throw failure; }
            return (0, 0, Width, 16);
        }
        public float GetScale(nint filter) => 1;
        public bool ValidateWindow(MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => WindowValid;
        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => true;
        public nint Retain(nint owner)
        {
            WindowReferences++;
            if (RetainFailure is { } failure) { throw failure; }
            return RetainResult ?? owner;
        }
        public void Release(nint owner)
        {
            ReleaseAttempts.Add(owner);
            if (owner == Filter)
            {
                FilterReferences--;
                if (FilterReleaseFailure is { } failure) { throw failure; }
            }
            else if (owner == ReplacementFilter) { ReplacementFilterReferences--; }
            else if (owner == Window)
            {
                WindowReferences--;
                if (WindowReleaseFailure is { } failure) { throw failure; }
            }
        }
    }

    private sealed class CreationApi : IMacOSRemoteWindowNativeApi
    {
        internal List<CreationEffects> Effects { get; } = [];
        internal List<IMacOSRemoteWindowNativeSource> Produced { get; } = [];
        internal int Count = 1;
        internal Action<MacOSRemoteWindowSourceCreationContext>? AllocationCallback;
        public bool IsSupported => true;
        public bool PreflightCaptureAccess() => true;
        public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync() =>
            throw new InvalidOperationException("producer requires its bounded creation context");
        public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync(
            MacOSRemoteWindowSourceCreationContext context)
        {
            List<IMacOSRemoteWindowNativeSource> sources = [];
            for (int index = 0; index < Count; index++)
            {
                var effects = new CreationEffects { IdentityId = (uint)index + 1 };
                if (AllocationCallback is { } callback)
                {
                    effects.AllocationCallback = () => callback(context);
                }
                Effects.Add(effects);
                var source = MacOSRemoteWindowScreenCaptureKitApi.CreateSourceWithOperations(
                    context, CreationEffects.Window, effects);
                sources.Add(source!);
                Produced.Add(source!);
            }
            return ValueTask.FromResult<IReadOnlyList<IMacOSRemoteWindowNativeSource>>(sources);
        }
        public bool IsCurrent(IMacOSRemoteWindowNativeSource source) => true;
        public IMacOSRemoteWindowNativeCapture CreateCapture(
            IMacOSRemoteWindowNativeSource source,
            Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
            Action sourceUnavailable) => throw new NotSupportedException();
    }
}
