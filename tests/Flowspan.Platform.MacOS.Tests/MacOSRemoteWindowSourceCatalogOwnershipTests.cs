using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowSourceCatalogOwnershipTests
{
    [Fact]
    public async Task CancelledBatchCleansKnownOwnerWithoutRereadingPriorIndex()
    {
        var first = new SourceEffects();
        var second = new SourceEffects();
        var batch = new RereadFailureBatch([CreateRealSource(first), CreateRealSource(second)]);
        var completion = new TaskCompletionSource<IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new MacOSRemoteWindowTestApi
        {
            EnumerationTask = completion.Task,
            EnumerationEntered = entered,
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        using var cancellation = new CancellationTokenSource();
        Task<LocalBoundaryResult> refresh = catalog.RefreshAsync(cancellation.Token).AsTask();
        Exception? cleanupFailure = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            completion.TrySetResult(batch);
            // This joins the owned refresh even though its caller stopped waiting.
            try { await catalog.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception exception) { cleanupFailure = exception; }
        }

        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, second.ReleaseAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, first.ReleaseAttempts);
        Assert.Equal(1, batch.FirstIndexReads);
        Assert.Equal(1, batch.SecondIndexReads);
        Assert.Equal(1, batch.CountReads);
        Assert.Null(cleanupFailure);
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    private sealed class RereadFailureBatch(IMacOSRemoteWindowNativeSource[] sources)
        : IReadOnlyList<IMacOSRemoteWindowNativeSource>
    {
        internal int FirstIndexReads;
        internal int SecondIndexReads;
        internal int CountReads;
        public int Count
        {
            get { Interlocked.Increment(ref CountReads); return sources.Length; }
        }
        public IMacOSRemoteWindowNativeSource this[int index]
        {
            get
            {
                if (index == 0 && Interlocked.Increment(ref FirstIndexReads) != 1)
                {
                    throw new InvalidOperationException("private prior-index reread failure");
                }
                if (index == 1) { Interlocked.Increment(ref SecondIndexReads); }
                return sources[index];
            }
        }
        public IEnumerator<IMacOSRemoteWindowNativeSource> GetEnumerator() =>
            ((IEnumerable<IMacOSRemoteWindowNativeSource>)sources).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task LateFaultClosesExistingBindingAndRejectsBothNewAcquisitionPaths()
    {
        var failedEffects = new SourceEffects
        {
            FilterFailure = new InvalidOperationException("private late failure"),
        };
        var healthyEffects = new SourceEffects();
        IMacOSRemoteWindowNativeSource failed = CreateRealSource(failedEffects);
        IMacOSRemoteWindowNativeSource healthy = CreateRealSource(healthyEffects, 2);
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var api = new MacOSRemoteWindowTestApi { Sources = [failed, healthy] };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        MacOSRemoteWindowSourceCatalog.NativeBinding? failedBinding = null, healthyBinding = null;
        NativeRemoteWindowSourceSnapshot? healthySnapshot = null;
        foreach (NativeRemoteWindowSourceSnapshot snapshot in catalog.GetSnapshot())
        {
            Assert.True(catalog.TryAcquireNativeBinding(
                NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), out var binding));
            if (ReferenceEquals(binding!.Native, failed)) { failedBinding = binding; }
            else { healthyBinding = binding; healthySnapshot = snapshot; }
        }
        Assert.NotNull(failedBinding);
        Assert.NotNull(healthyBinding);
        Assert.NotNull(healthySnapshot);
        api.Sources = [healthy];
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        Assert.True(healthyBinding.IsCurrent);

        Assert.Throws<InvalidOperationException>(() => failedBinding.Dispose());

        Assert.False(healthyBinding.IsCurrent);
        Assert.False(catalog.TryAcquire(healthySnapshot.Token,
            healthySnapshot.Source.SourceGeneration, out _));
        Assert.False(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(healthySnapshot, 1, 1), out _));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.DisposeAsync());
        healthyBinding.Dispose();
        Assert.Equal((1, 1, 0), pool.GetUsage());
        Assert.Equal(2, failedEffects.ReleaseAttempts.Count);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, healthyEffects.ReleaseAttempts);
    }

    [Fact]
    public async Task FailedBatchIndexIsNotReadAgainWhileKnownSourceDrains()
    {
        var known = new SourceEffects();
        var unknown = new SourceEffects();
        var batch = new IndexFailureBatch([CreateRealSource(known), CreateRealSource(unknown)]);
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var api = new MacOSRemoteWindowTestApi { Sources = batch };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);

        Assert.False((await catalog.RefreshAsync()).Succeeded);

        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, known.ReleaseAttempts);
        Assert.Empty(unknown.ReleaseAttempts);
        Assert.Equal(1, batch.FailedIndexAttempts);
        Assert.Equal((1, 128, 1), pool.GetUsage());
        api.Sources = [];
        Assert.False((await catalog.RefreshAsync()).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.DisposeAsync());
        Assert.Equal(1, batch.FailedIndexAttempts);
        Assert.Equal(2, known.ReleaseAttempts.Count);
        Assert.Equal(1, api.EnumerationCalls);
    }

    private sealed class IndexFailureBatch(IMacOSRemoteWindowNativeSource[] sources)
        : IReadOnlyList<IMacOSRemoteWindowNativeSource>
    {
        internal int FailedIndexAttempts;
        public int Count => sources.Length;
        public IMacOSRemoteWindowNativeSource this[int index]
        {
            get
            {
                if (index == 1)
                {
                    Interlocked.Increment(ref FailedIndexAttempts);
                    throw new InvalidOperationException("private index failure");
                }
                return sources[index];
            }
        }
        public IEnumerator<IMacOSRemoteWindowNativeSource> GetEnumerator()
        {
            for (int index = 0; index < Count; index++) { yield return this[index]; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task FailedCandidateBatchStaysChargedWhileIndependentKnownSourceDrainsOnce()
    {
        var firstEffects = new SourceEffects
        {
            FilterFailure = new InvalidOperationException("private candidate failure"),
        };
        var secondEffects = new SourceEffects();
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        // Distinct native owners with the same semantic identity are rejected
        // as candidates; their independent ownership is still cleaned once.
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [CreateRealSource(firstEffects), CreateRealSource(secondEffects)],
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);

        Assert.False((await catalog.RefreshAsync()).Succeeded);

        Assert.Empty(catalog.GetSnapshot());
        Assert.Equal((1, 128, 1), pool.GetUsage());
        api.Sources = [];
        Assert.False((await catalog.RefreshAsync()).Succeeded);
        Assert.Equal(1, api.EnumerationCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.DisposeAsync());
        Assert.Equal((1, 128, 1), pool.GetUsage());
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, firstEffects.ReleaseAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, secondEffects.ReleaseAttempts);
        Assert.All(firstEffects.References.Values, count => Assert.Equal(0, count));
        Assert.All(secondEffects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Injected registration fatal verifies original identity across independent native cleanup.")]
    public async Task RegistrationFatalIsRecordedBeforeIndependentNativeCleanupFailure()
    {
        var fatal = new OutOfMemoryException("private registration fatal");
        var effects = new SourceEffects
        {
            FilterFailure = new InvalidOperationException("private later native failure"),
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var api = new MacOSRemoteWindowTestApi { Sources = [CreateRealSource(effects)] };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquire(snapshot.Token, snapshot.Source.SourceGeneration, out var lease));
        using (lease)
        {
            Assert.True(lease!.TryRegisterPreparationReservation(
                new FailingReservation(new AggregateException(fatal)), out var preparation));
            using (preparation)
            {
                api.Sources = [];
                Assert.Same(fatal, await Assert.ThrowsAsync<OutOfMemoryException>(
                    async () => await catalog.RefreshAsync()));
                Assert.Same(fatal, await Assert.ThrowsAsync<OutOfMemoryException>(
                    async () => await catalog.DisposeAsync()));
            }
        }
        Assert.Equal((1, 1, 0), pool.GetUsage());
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    private sealed class FailingReservation(Exception failure)
        : INativeRemoteWindowSourcePreparationReservation
    {
        public void InvalidateSourcePreparationNow() => throw failure;
    }

    [Fact]
    public async Task PendingBatchKeepsSharedBatchCapacityUntilItSettles()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(sourceCapacity: 256, batchCapacity: 1);
        var completion = new TaskCompletionSource<IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstApi = new MacOSRemoteWindowTestApi
        {
            EnumerationTask = completion.Task,
            EnumerationEntered = entered,
        };
        var secondApi = new MacOSRemoteWindowTestApi();
        await using var first = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            firstApi, ownershipPool: pool);
        await using var second = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            secondApi, ownershipPool: pool);
        Task<LocalBoundaryResult> pending = first.RefreshAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            LocalBoundaryResult rejected = await second.RefreshAsync();
            Assert.Equal("macos_source_ownership_capacity_exhausted", rejected.ReasonCode);
            Assert.Equal(0, secondApi.EnumerationCalls);
            Assert.Equal((2, 128, 1), pool.GetUsage());
        }
        finally
        {
            completion.TrySetResult([]);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal((2, 0, 0), pool.GetUsage());
        Assert.True((await second.RefreshAsync()).Succeeded);
        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task SourceBudgetRejectsBeforeEnumeratingWithoutEvictingHealthyOwner()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(sourceCapacity: 128);
        var effects = new SourceEffects();
        var firstApi = new MacOSRemoteWindowTestApi { Sources = [CreateRealSource(effects)] };
        var secondApi = new MacOSRemoteWindowTestApi();
        await using var first = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            firstApi, ownershipPool: pool);
        await using var second = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            secondApi, ownershipPool: pool);
        Assert.True((await first.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(first.GetSnapshot());

        LocalBoundaryResult result = await second.RefreshAsync();

        Assert.Equal("macos_source_ownership_capacity_exhausted", result.ReasonCode);
        Assert.Equal(0, secondApi.EnumerationCalls);
        Assert.Equal((2, 1, 0), pool.GetUsage());
        Assert.True(first.TryAcquire(snapshot.Token, snapshot.Source.SourceGeneration, out var lease));
        lease!.Dispose();
        Assert.Empty(effects.ReleaseAttempts);
        await first.DisposeAsync();
        Assert.True((await second.RefreshAsync()).Succeeded);
        await second.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public void UnknownBatchGraphSurvivesCallerCollectionWithoutRetry()
    {
        WeakBatchGraph graph = CreateUnknownBatchGraph();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(graph.Catalog.IsAlive);
        Assert.True(graph.Batch.IsAlive);
        Assert.True(graph.Source.IsAlive);
        Assert.Equal(1, graph.Ledger.CountAttempts);
        Assert.Empty(graph.Effects.ReleaseAttempts);
        Assert.Equal((1, 128, 1), graph.Pool.GetUsage());
        GC.KeepAlive(graph.Pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakBatchGraph CreateUnknownBatchGraph()
    {
        var effects = new SourceEffects();
        IMacOSRemoteWindowNativeSource source = CreateRealSource(effects);
        var ledger = new BatchLedger();
        var batch = new CountFailureBatch(source, ledger);
        var api = new MacOSRemoteWindowTestApi { Sources = batch };
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        Assert.False(catalog.RefreshAsync().AsTask().GetAwaiter().GetResult().Succeeded);
        api.Sources = [];
        Assert.False(catalog.RefreshAsync().AsTask().GetAwaiter().GetResult().Succeeded);
        Assert.Throws<InvalidOperationException>(() => catalog.DisposeAsync().AsTask().GetAwaiter().GetResult());
        return new(new WeakReference(catalog), new WeakReference(batch), new WeakReference(source),
            effects, ledger, pool);
    }

    private sealed record WeakBatchGraph(WeakReference Catalog, WeakReference Batch,
        WeakReference Source, SourceEffects Effects, BatchLedger Ledger,
        MacOSRemoteWindowSourceOwnershipPool Pool);

    private sealed class BatchLedger
    {
        internal int CountAttempts;
    }

    private sealed class CountFailureBatch(IMacOSRemoteWindowNativeSource source, BatchLedger ledger)
        : IReadOnlyList<IMacOSRemoteWindowNativeSource>
    {
        public int Count
        {
            get
            {
                Interlocked.Increment(ref ledger.CountAttempts);
                throw new InvalidOperationException("private batch access details");
            }
        }
        public IMacOSRemoteWindowNativeSource this[int index] => source;
        public IEnumerator<IMacOSRemoteWindowNativeSource> GetEnumerator() =>
            ((IEnumerable<IMacOSRemoteWindowNativeSource>)new[] { source }).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task TwoCatalogsShareCapacityAndThirdIsRejectedBeforeEnumeration()
    {
        var pool = new MacOSRemoteWindowSourceOwnershipPool(catalogCapacity: 2);
        var firstApi = new MacOSRemoteWindowTestApi();
        var secondApi = new MacOSRemoteWindowTestApi();
        var thirdApi = new MacOSRemoteWindowTestApi();
        await using var first = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            firstApi, ownershipPool: pool);
        await using var second = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            secondApi, ownershipPool: pool);
        await using var third = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            thirdApi, ownershipPool: pool);
        Assert.True((await first.RefreshAsync()).Succeeded);
        Assert.True((await second.RefreshAsync()).Succeeded);

        LocalBoundaryResult result = await third.RefreshAsync();

        Assert.Equal("macos_source_ownership_capacity_exhausted", result.ReasonCode);
        Assert.Equal(0, thirdApi.EnumerationCalls);
        Assert.Equal((2, 0, 0), pool.GetUsage());
        await first.DisposeAsync();
        Assert.True((await third.RefreshAsync()).Succeeded);
        await second.DisposeAsync();
        await third.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
    }

    [Fact]
    public async Task FaultDuringPreflightRejectsEnumerationAdmission()
    {
        var effects = new SourceEffects
        {
            FilterFailure = new InvalidOperationException("private release details"),
        };
        var inner = new MacOSRemoteWindowTestApi { Sources = [CreateRealSource(effects)] };
        var api = new PreflightCallbackApi(inner);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: new MacOSRemoteWindowSourceOwnershipPool());
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), out var binding));
        inner.Sources = [];
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        Exception? releaseFailure = null;
        api.AfterPreflight = () =>
        {
            try { binding!.Dispose(); }
            catch (Exception exception) { releaseFailure = exception; }
        };
        int before = inner.EnumerationCalls;

        LocalBoundaryResult result = await catalog.RefreshAsync();

        Assert.IsType<InvalidOperationException>(releaseFailure);
        Assert.False(result.Succeeded);
        Assert.Equal(before, inner.EnumerationCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.DisposeAsync());
        Assert.Equal(2, effects.ReleaseAttempts.Count);
    }

    [Fact]
    public async Task HeldBindingKeepsCatalogAndSourceChargedUntilConfirmedFinalRelease()
    {
        var effects = new SourceEffects();
        var pool = new MacOSRemoteWindowSourceOwnershipPool(catalogCapacity: 1);
        var api = new MacOSRemoteWindowTestApi { Sources = [CreateRealSource(effects)] };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), out var binding));

        await catalog.DisposeAsync();

        Assert.False(binding!.IsCurrent);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal((1, 1, 0), pool.GetUsage());
        var nextApi = new MacOSRemoteWindowTestApi();
        await using var next = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()),
            nextApi, ownershipPool: pool);
        LocalBoundaryResult rejected = await next.RefreshAsync();
        Assert.False(rejected.Succeeded);
        Assert.Equal("macos_source_ownership_capacity_exhausted", rejected.ReasonCode);
        Assert.Equal(0, nextApi.EnumerationCalls);

        binding.Dispose();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
        Assert.True((await next.RefreshAsync()).Succeeded);
        await next.DisposeAsync();
        Assert.Equal((0, 0, 0), pool.GetUsage());
        binding.Dispose();
        Assert.Equal(2, effects.ReleaseAttempts.Count);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Injected nested fatal verifies identity and independent ownership cleanup, not actual runtime exhaustion.")]
    public async Task LastBindingNestedFatalPreservesOriginalWithoutRetry()
    {
        var fatal = new OutOfMemoryException("private original fatal");
        var effects = new SourceEffects
        {
            FilterFailure = new AggregateException(new InvalidOperationException("ordinary"),
                new AggregateException(fatal)),
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var api = new MacOSRemoteWindowTestApi { Sources = [CreateRealSource(effects)] };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), out var binding));
        api.Sources = [];
        Assert.True((await catalog.RefreshAsync()).Succeeded);

        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() => binding!.Dispose()));
        int before = api.EnumerationCalls;
        Assert.Same(fatal, await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await catalog.RefreshAsync()));
        Assert.Same(fatal, await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await catalog.DisposeAsync()));
        Assert.Equal(before, api.EnumerationCalls);
        Assert.Equal((1, 1, 0), pool.GetUsage());
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
        binding!.Dispose();
        Assert.Equal(2, effects.ReleaseAttempts.Count);
    }

    [Fact]
    public void FailedLastBindingOwnerGraphSurvivesCallerCollection()
    {
        WeakOwnerGraph graph = CreateFailedLastBindingGraph();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(graph.Catalog.IsAlive);
        Assert.True(graph.Source.IsAlive);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, graph.Effects.ReleaseAttempts);
        Assert.Equal((1, 1, 0), graph.Pool.GetUsage());
        GC.KeepAlive(graph.Pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakOwnerGraph CreateFailedLastBindingGraph()
    {
        var effects = new SourceEffects
        {
            FilterFailure = new InvalidOperationException("private release details"),
        };
        IMacOSRemoteWindowNativeSource source = CreateRealSource(effects);
        var api = new MacOSRemoteWindowTestApi { Sources = [source] };
        var pool = new MacOSRemoteWindowSourceOwnershipPool();
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: pool);
        Assert.True(catalog.RefreshAsync().AsTask().GetAwaiter().GetResult().Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), out var binding));
        api.Sources = [];
        Assert.True(catalog.RefreshAsync().AsTask().GetAwaiter().GetResult().Succeeded);
        Assert.Throws<InvalidOperationException>(() => binding!.Dispose());
        return new(new WeakReference(catalog), new WeakReference(source), effects, pool);
    }

    private sealed record WeakOwnerGraph(WeakReference Catalog, WeakReference Source,
        SourceEffects Effects, MacOSRemoteWindowSourceOwnershipPool Pool);

    [Fact]
    public async Task LastBindingReleaseFaultClosesCatalogBeforeNextEnumeration()
    {
        var effects = new SourceEffects
        {
            FilterFailure = new InvalidOperationException("private release details"),
        };
        IMacOSRemoteWindowNativeSource source = CreateRealSource(effects);
        var api = new MacOSRemoteWindowTestApi { Sources = [source] };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api,
            ownershipPool: new MacOSRemoteWindowSourceOwnershipPool());
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), out var binding));
        api.Sources = [];
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        Assert.Empty(effects.ReleaseAttempts);

        InvalidOperationException reported = Assert.Throws<InvalidOperationException>(
            () => binding!.Dispose());

        Assert.Equal("macOS native source release unconfirmed.", reported.Message);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
        int before = api.EnumerationCalls;
        LocalBoundaryResult refresh = await catalog.RefreshAsync();
        Assert.False(refresh.Succeeded);
        Assert.Equal(before, api.EnumerationCalls);
        InvalidOperationException cleanup = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.DisposeAsync());
        Assert.Equal("macos_source_cleanup_failed", cleanup.Message);
        binding!.Dispose();
        Assert.Equal(2, effects.ReleaseAttempts.Count);
    }

    private static IMacOSRemoteWindowNativeSource CreateRealSource(SourceEffects effects,
        uint windowId = 1) =>
        MacOSRemoteWindowScreenCaptureKitApi.CreateNativeSourceWithOperations(
            new(windowId, 123, 1, 0), NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2),
            SourceEffects.Window, SourceEffects.Filter, effects);

    private sealed class SourceEffects : IMacOSRemoteWindowSourceOperations
    {
        internal const nint Window = 41;
        internal const nint Filter = 42;
        internal ConcurrentDictionary<nint, int> References { get; } = new(new[]
        {
            new KeyValuePair<nint, int>(Window, 1), new KeyValuePair<nint, int>(Filter, 1),
        });
        internal ConcurrentQueue<nint> ReleaseAttempts { get; } = new();
        internal Exception? FilterFailure { get; init; }

        public nint Retain(nint owner)
        {
            References.AddOrUpdate(owner, 1, (_, count) => count + 1);
            return owner;
        }

        public void Release(nint owner)
        {
            ReleaseAttempts.Enqueue(owner);
            References.AddOrUpdate(owner, -1, (_, count) => count - 1);
            if (owner == Filter && FilterFailure is not null) { throw FilterFailure; }
        }

        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry) => true;
    }

    private sealed class PreflightCallbackApi(MacOSRemoteWindowTestApi inner)
        : IMacOSRemoteWindowNativeApi
    {
        internal Action? AfterPreflight { get; set; }
        public bool IsSupported => inner.IsSupported;
        public bool PreflightCaptureAccess()
        {
            bool allowed = inner.PreflightCaptureAccess();
            AfterPreflight?.Invoke();
            return allowed;
        }
        public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync() =>
            inner.EnumerateAsync();
        public bool IsCurrent(IMacOSRemoteWindowNativeSource source) => inner.IsCurrent(source);
        public IMacOSRemoteWindowNativeCapture CreateCapture(IMacOSRemoteWindowNativeSource source,
            Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership, Action sourceUnavailable) =>
            inner.CreateCapture(source, takeSampleOwnership, sourceUnavailable);
    }
}
