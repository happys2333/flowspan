using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

[Collection("macOS Capture ownership")]
public sealed partial class MacOSRemoteWindowScreenCaptureKitCaptureTests
{
    [Fact]
    public void ActualOuterPendingLifetimeSurvivesBoundaryGcAndReturnsBindingAfterKnownRetirement()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        OuterPendingGraph graph = CreateActualOuterPendingLifetime();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        bool boundaryAlive = graph.Boundary.IsAlive;
        bool[] pendingGraphAlive = graph.RootedGraph.Select(reference => reference.IsAlive).ToArray();
        int pendingChargeAfterGc = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        OuterPendingFinal final = CompleteActualOuterPendingLifetime(graph);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(graph.StartResult.Succeeded);
        Assert.True(graph.SameCatalogNativeSource);
        Assert.Equal(1, graph.CaptureCalls);
        Assert.Equal("macos_capture_cleanup_unconfirmed", graph.StopFailure);
        Assert.True(graph.SameStopTask);
        Assert.True(graph.PhysicalDrain);
        Assert.False(graph.CachedDrainResult);
        Assert.False(boundaryAlive);
        Assert.All(pendingGraphAlive, Assert.True);
        Assert.Equal(before + 1, pendingChargeAfterGc);
        Assert.Equal((2, 3, 2, 1, 1, 1, 2, 2), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Pending.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1), graph.Pending.CleanupEffects);
        Assert.Equal((0, 1), graph.Pending.Binding);
        Assert.Equal((1, 1), graph.Pending.SourceReferences);
        Assert.Equal((1, 1, 0), graph.Pending.Usage);
        Assert.False(graph.Pending.FullCleanupProof);
        Assert.Equal(before + 1, graph.Pending.ChargedOwners);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, false, false, false, false, 1L), graph.Pending.StopPrimitiveFacts);
        Assert.Equal((true, 0, true), graph.Pending.StartResourceUse);
        Assert.Equal(graph.Pending.StartResourceUse, graph.Pending.StopResourceUse);
        Assert.Equal((1, 0), (graph.Pending.ExternalCopyHeld, graph.Pending.ExternalCopyReleaseAttempts));
        Assert.False(graph.RefusedReplacement.Succeeded);
        Assert.Equal("macos_source_ownership_capacity_exhausted", graph.RefusedReplacement.ReasonCode);
        Assert.Equal(0, graph.RefusedEnumerations);
        Assert.Equal((1, 1, 0), graph.RefusedReplacementUsage);
        Assert.True(final.NativeRetirementJoined);
        Assert.True(final.ManagedInvocationJoined);
        Assert.Equal((1, 0), final.Final.Binding);
        Assert.True(final.RecoveryExisted);
        Assert.True(final.RecoveryJoined);
        Assert.Null(final.RecoveryFailure);
        Assert.True(final.SameRecoveryTask);
        Assert.True(final.SameOriginalStopTask);
        Assert.Equal(TaskStatus.Faulted, final.OriginalStopStatus);
        Assert.Equal("macos_capture_cleanup_unconfirmed", final.OriginalStopFailure);
        Assert.True(final.Final.FullCleanupProof);
        Assert.Equal(before, final.Final.ChargedOwners);
        Assert.Equal((0, 0), final.Final.SourceReferences);
        Assert.Equal((0, 0, 0), final.Final.Usage);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), final.Final.RuntimeEffects);
        Assert.Equal(graph.Pending.NativeEffects, final.Final.NativeEffects);
        Assert.Equal(graph.Pending.CleanupEffects, final.Final.CleanupEffects);
        Assert.Equal((0, 1), (final.Final.ExternalCopyHeld, final.Final.ExternalCopyReleaseAttempts));
        Assert.Equal(graph.Pending.StartPrimitiveFacts, final.Final.StartPrimitiveFacts);
        Assert.Equal(final.Final.StartPrimitiveFacts, final.Final.StopPrimitiveFacts);
        Assert.Null(final.RepeatedCaptureFailure);
        Assert.Equal(final.Final, final.Repeated);
        Assert.True(final.ReplacementResult.Succeeded);
        Assert.Equal(1, final.ReplacementEnumerations);
        Assert.Equal((0, 0, 0), final.ReplacementUsage);
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, graph.SourceRetains);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, final.SourceReleases);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, final.ObjectReleases);
        Assert.Equal(4, final.PushEffects.Length);
        Assert.Equal(final.PushEffects, final.PopEffects);
        Assert.Equal((0, 0, 3, 2, 2, 2), final.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.All(graph.RootedGraph, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OuterPendingGraph CreateActualOuterPendingLifetime()
    {
        var captureApi = new MacOSRemoteWindowScreenCaptureKitApi();
        var sourceEffects = new SourceEffects();
        IMacOSRemoteWindowNativeSource nativeSource = MacOSRemoteWindowScreenCaptureKitApi.CreateNativeSourceWithOperations(
            new(1, 123, 1, 0), NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2),
            SourceEffects.Window, SourceEffects.Filter, sourceEffects);
        var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime)
        {
            HoldExternalStopCopy = true,
            StopInvocationFailureAfterEffect = new IOException("Actual Stop returned callback before ordinary invocation failure."),
        };
        IMacOSRemoteWindowNativeCapture? capture = null;
        bool sameCatalogNative = false;
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            CaptureFactory = (source, takeSampleOwnership, sourceUnavailable) =>
            {
                sameCatalogNative = ReferenceEquals(nativeSource, source);
                capture = captureApi.CreateCaptureWithOperations(source, operations, takeSampleOwnership, sourceUnavailable);
                return capture;
            },
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool(catalogCapacity: 1, batchCapacity: 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api, ownershipPool: pool);
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink();
        Task<LocalBoundaryResult>? refresh = null;
        Task<LocalBoundaryResult>? start = null;
        Task? stop = null;
        Task<bool>? actualDrain = null;
        Task? catalogDisposal = null;
        MacOSRemoteWindowSourceCatalog? refused = null;
        Task<LocalBoundaryResult>? refusedRefresh = null;
        Task? refusedDisposal = null;
        static void JoinActual(Task? work)
        {
            if (work is null) { return; }
            try { work.GetAwaiter().GetResult(); } catch (Exception) { }
        }
        try
        {
            refresh = catalog.RefreshAsync().AsTask();
            refresh.WaitAsync(Timeout).GetAwaiter().GetResult();
            NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
            start = boundary.StartAsync(NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), sink, CancellationToken.None).AsTask();
            LocalBoundaryResult startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult();
            WeakReference startOwner = operations.Completion!;
            WeakReference startPrimitive = operations.Primitive!;
            stop = boundary.DisposeAsync().AsTask();
            string? stopFailure = null;
            try { stop.WaitAsync(Timeout).GetAwaiter().GetResult(); }
            catch (Exception exception) { stopFailure = exception.Message; }
            bool sameStopTask = ReferenceEquals(stop, boundary.StopCompletion);
            actualDrain = Assert.IsAssignableFrom<Task<bool>>(capture!.GetType()
                .GetField("drain", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
            bool cachedDrainResult = actualDrain.WaitAsync(Timeout).GetAwaiter().GetResult();
            bool physicalDrain = capture.IsDrained;
            object operation = boundary.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            var binding = Assert.IsType<MacOSRemoteWindowSourceCatalog.NativeBinding>(operation.GetType()
                .GetField("binding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(operation));
            var entry = Assert.IsType<MacOSRemoteWindowSourceCatalog.SourceEntry>(binding.GetType()
                .GetField("entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding));
            catalogDisposal = catalog.DisposeAsync().AsTask();
            catalogDisposal.WaitAsync(Timeout).GetAwaiter().GetResult();
            OuterPendingPhase pending = ReadOuterPendingPhase(capture, operations, runtime, sourceEffects, pool, binding, entry, startOwner);
            var refusedApi = new MacOSRemoteWindowTestApi();
            refused = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), refusedApi, ownershipPool: pool);
            refusedRefresh = refused.RefreshAsync().AsTask();
            LocalBoundaryResult refusedResult = refusedRefresh.WaitAsync(Timeout).GetAwaiter().GetResult();
            refusedDisposal = refused.DisposeAsync().AsTask();
            refusedDisposal.WaitAsync(Timeout).GetAwaiter().GetResult();
            object sourceOwner = capture.GetType().GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
            object retainedNativeSource = sourceOwner.GetType().GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sourceOwner)!;
            return new(startResult, sameCatalogNative, api.CaptureCalls, stopFailure, sameStopTask, physicalDrain, cachedDrainResult,
                pending, refusedResult, refusedApi.EnumerationCalls, pool.GetUsage(), sourceEffects.RetainAttempts.ToArray(),
                operations.Stream, operations.Output, operations.Configuration, new(boundary), new(capture), new(operation),
                new(operations), new(runtime), new(sourceEffects), new(pool), new(binding), new(entry), startOwner,
                [new(capture), new(captureApi), new(operation), new(catalog), new(pool), new(binding), new(entry), new(nativeSource),
                    new(sourceOwner), new(retainedNativeSource), new(sink), new(api), new(sourceEffects), new(operations), new(runtime),
                    startOwner, startPrimitive, operations.Completion!, operations.Primitive!]);
        }
        catch
        {
            // A watchdog is only an observation. Join original work and the
            // independently owned recovery before tearing down raw effects.
            operations.ReleaseExternalStopCopy();
            try
            {
                JoinActual(refresh);
                JoinActual(start);
                stop ??= boundary.DisposeAsync().AsTask();
                JoinActual(stop);
                // Stop may have published the same known fixture retain after
                // the first cleanup observation; this releases no guessed owner.
                operations.ReleaseExternalStopCopy();
                actualDrain ??= capture?.GetType().GetField("drain", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(capture) as Task<bool>;
                JoinActual(actualDrain);
                object? operation = boundary.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary);
                JoinActual(operation?.GetType().GetField("knownPendingCleanupRecovery", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(operation) as Task);
                catalogDisposal ??= catalog.DisposeAsync().AsTask();
                JoinActual(catalogDisposal);
                JoinActual(refusedRefresh);
                if (refused is not null)
                {
                    refusedDisposal ??= refused.DisposeAsync().AsTask();
                    JoinActual(refusedDisposal);
                }
            }
            finally { runtime.Dispose(); }
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OuterPendingFinal CompleteActualOuterPendingLifetime(OuterPendingGraph graph)
    {
        var capture = Assert.IsAssignableFrom<IMacOSRemoteWindowNativeCapture>(graph.Capture.Target);
        object operation = graph.Operation.Target!;
        var operations = Assert.IsType<CompletionOwnershipOperations>(graph.Operations.Target);
        var runtime = Assert.IsType<MacOSRemoteWindowControlledBlockRuntime>(graph.Runtime.Target);
        var sourceEffects = Assert.IsType<SourceEffects>(graph.SourceEffects.Target);
        var pool = Assert.IsType<MacOSRemoteWindowSourceOwnershipPool>(graph.Pool.Target);
        var binding = Assert.IsType<MacOSRemoteWindowSourceCatalog.NativeBinding>(graph.Binding.Target);
        var entry = Assert.IsType<MacOSRemoteWindowSourceCatalog.SourceEntry>(graph.Entry.Target);
        var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
        var stop = Assert.IsAssignableFrom<Task>(operation.GetType().GetField("stopCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(operation));
        FieldInfo? recoveryField = operation.GetType().GetField("knownPendingCleanupRecovery", BindingFlags.Instance | BindingFlags.NonPublic);
        Task? recovery = recoveryField?.GetValue(operation) as Task;
        bool nativeJoined = false;
        bool managedJoined = false;
        bool recoveryJoined = false;
        Exception? recoveryFailure = null;
        Exception? repeatedFailure = null;
        LocalBoundaryResult replacementResult = LocalBoundaryResult.Failed("test_replacement_not_observed");
        int replacementEnumerations = 0;
        (int, int, int) replacementUsage = default;
        OuterPendingPhase? final = null;
        OuterPendingPhase? repeated = null;
        MacOSRemoteWindowSourceCatalog? replacement = null;
        Task? replacementDisposal = null;
        try
        {
            // Only the confirmed fixture-owned +1 is released. Production owns
            // the independent recovery; this helper never calls it or sets facts.
            operations.ReleaseExternalStopCopy();
            stopOwner.NativeCaptureRetirement.WaitAsync(Timeout).GetAwaiter().GetResult();
            nativeJoined = stopOwner.NativeCaptureRetirement.IsCompletedSuccessfully;
            stopOwner.ManagedInvocationDrain.WaitAsync(Timeout).GetAwaiter().GetResult();
            managedJoined = stopOwner.ManagedInvocationDrain.IsCompletedSuccessfully;
            if (recovery is not null)
            {
                try { recovery.WaitAsync(Timeout).GetAwaiter().GetResult(); recoveryJoined = true; }
                catch (Exception exception) { recoveryFailure = exception; }
            }
            final = ReadOuterPendingPhase(capture, operations, runtime, sourceEffects, pool, binding, entry, graph.StartOwner);
            if (recovery is not null && recoveryJoined && capture.IsCleanupConfirmed)
            {
                try { capture.Dispose(); }
                catch (Exception exception) { repeatedFailure = exception; }
            }
            repeated = ReadOuterPendingPhase(capture, operations, runtime, sourceEffects, pool, binding, entry, graph.StartOwner);
            var replacementApi = new MacOSRemoteWindowTestApi();
            replacement = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), replacementApi, ownershipPool: pool);
            replacementResult = replacement.RefreshAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
            replacementEnumerations = replacementApi.EnumerationCalls;
            replacementDisposal = replacement.DisposeAsync().AsTask();
            replacementDisposal.WaitAsync(Timeout).GetAwaiter().GetResult();
            replacementUsage = pool.GetUsage();
        }
        finally
        {
            operations.ReleaseExternalStopCopy();
            try { stop.GetAwaiter().GetResult(); } catch (Exception) { }
            stopOwner.NativeCaptureRetirement.GetAwaiter().GetResult();
            stopOwner.ManagedInvocationDrain.GetAwaiter().GetResult();
            if (recovery is not null) { try { recovery.GetAwaiter().GetResult(); } catch (Exception) { } }
            if (replacement is not null)
            {
                replacementDisposal ??= replacement.DisposeAsync().AsTask();
                replacementDisposal.GetAwaiter().GetResult();
            }
            runtime.Dispose();
        }
        Task originalStop = (Task)operation.GetType().GetField("stopCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(operation)!;
        string? stopFailure = stop.Exception?.InnerException?.Message;
        return new(nativeJoined, managedJoined, recovery is not null, recoveryJoined, recoveryFailure,
            recovery is not null && ReferenceEquals(recovery, recoveryField!.GetValue(operation)), ReferenceEquals(stop, originalStop),
            originalStop.Status, stopFailure, final!, repeated!, repeatedFailure, replacementResult, replacementEnumerations, replacementUsage,
            sourceEffects.ReleaseAttempts.ToArray(), operations.ObjectReleaseAttempts.ToArray(), operations.PoolPushEffects.ToArray(),
            operations.PoolPopEffects.ToArray(), (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                runtime.InvokeAttempts, runtime.InvokeReturns));
    }

    private static OuterPendingPhase ReadOuterPendingPhase(IMacOSRemoteWindowNativeCapture capture, CompletionOwnershipOperations operations,
        MacOSRemoteWindowControlledBlockRuntime runtime, SourceEffects sourceEffects, MacOSRemoteWindowSourceOwnershipPool pool,
        MacOSRemoteWindowSourceCatalog.NativeBinding binding, MacOSRemoteWindowSourceCatalog.SourceEntry entry, WeakReference startOwner)
    {
        return new((runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns),
            (operations.StartSelectorAttempts, operations.StopSelectorAttempts, operations.NativeInvocationAttempts,
                operations.StartInvocations, operations.StopInvocations),
            (operations.PoolsPushed, operations.PoolsPopped, operations.RemoveCalls, operations.DrainCalls,
                operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts),
            ((int)binding.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding)!,
                (int)entry.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!),
            (sourceEffects.References[SourceEffects.Window], sourceEffects.References[SourceEffects.Filter]), pool.GetUsage(),
            capture.IsCleanupConfirmed, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
            ReadZeroPoolPrimitiveFacts(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwner.Target)),
            ReadZeroPoolPrimitiveFacts(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target)),
            ReadCompletionResourceUseFacts(startOwner), ReadCompletionResourceUseFacts(operations.Completion!),
            Volatile.Read(ref operations.ExternallyHeldStopCopy) == 0 ? 0 : 1, operations.ExternalStopCopyReleaseAttempts);
    }

    private sealed record OuterPendingGraph(LocalBoundaryResult StartResult, bool SameCatalogNativeSource, int CaptureCalls,
        string? StopFailure, bool SameStopTask, bool PhysicalDrain, bool CachedDrainResult, OuterPendingPhase Pending,
        LocalBoundaryResult RefusedReplacement, int RefusedEnumerations, (int Catalogs, int Sources, int Batches) RefusedReplacementUsage,
        nint[] SourceRetains, nint Stream, nint Output, nint Configuration, WeakReference Boundary, WeakReference Capture,
        WeakReference Operation, WeakReference Operations, WeakReference Runtime, WeakReference SourceEffects, WeakReference Pool,
        WeakReference Binding, WeakReference Entry, WeakReference StartOwner, WeakReference[] RootedGraph);

    private sealed record OuterPendingPhase(
        (int Roots, int Copies, int Releases, int Frees, int LiveRoots, int LiveBlocks, int Invokes, int Returns) RuntimeEffects,
        (int StartSelectors, int StopSelectors, int NativeAttempts, int StartInvocations, int StopInvocations) NativeEffects,
        (int Pushes, int Pops, int Removes, int Barriers, int Objects, int Queues) CleanupEffects,
        (int Disposed, int References) Binding, (int Window, int Filter) SourceReferences, (int Catalogs, int Sources, int Batches) Usage,
        bool FullCleanupProof, int ChargedOwners,
        (bool AllocationAttempted, bool AllocationConfirmed, bool CopyAttempted, bool CopyConfirmed, bool ReleaseAttempted, bool ReleaseConfirmed,
            bool RootFreeAttempted, bool RootFreeConfirmed, bool NativeRetired, bool ManagedDrained, long PhysicalCopies) StartPrimitiveFacts,
        (bool AllocationAttempted, bool AllocationConfirmed, bool CopyAttempted, bool CopyConfirmed, bool ReleaseAttempted, bool ReleaseConfirmed,
            bool RootFreeAttempted, bool RootFreeConfirmed, bool NativeRetired, bool ManagedDrained, long PhysicalCopies) StopPrimitiveFacts,
        (bool Closed, int ActiveUsers, bool JoinCompleted) StartResourceUse, (bool Closed, int ActiveUsers, bool JoinCompleted) StopResourceUse,
        int ExternalCopyHeld, int ExternalCopyReleaseAttempts);

    private sealed record OuterPendingFinal(bool NativeRetirementJoined, bool ManagedInvocationJoined, bool RecoveryExisted,
        bool RecoveryJoined, Exception? RecoveryFailure, bool SameRecoveryTask, bool SameOriginalStopTask, TaskStatus OriginalStopStatus,
        string? OriginalStopFailure, OuterPendingPhase Final, OuterPendingPhase Repeated, Exception? RepeatedCaptureFailure,
        LocalBoundaryResult ReplacementResult, int ReplacementEnumerations, (int Catalogs, int Sources, int Batches) ReplacementUsage,
        nint[] SourceReleases, nint[] ObjectReleases, (int Ordinal, nint Token, int Thread)[] PushEffects,
        (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int Frees, int Invokes, int Returns) RawTeardownEffects);

    [Fact]
    public void ActualOuterHealthyCaptureReturnsCatalogBindingCapacityAndWeakGraph()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        OuterHealthyGraph graph = CreateActualOuterHealthyCapture();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.True(graph.StartResult.Succeeded);
        Assert.True(graph.SameCatalogNativeSource);
        Assert.Equal(1, graph.CaptureCalls);
        Assert.Equal((0, 2), graph.InitialBinding);
        Assert.Equal((2, 2), graph.InitialSourceReferences);
        Assert.Equal((1, 1, 0), graph.InitialUsage);
        Assert.Equal((1, 1, 0, 0, 1, 1, 1, 1), graph.StartedRuntimeEffects);
        Assert.Equal(before + 1, graph.StartedChargedOwners);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Started.Facts);
        Assert.Null(graph.Started.ResultFailure);
        Assert.Null(graph.BoundaryDisposeFailure);
        Assert.Null(graph.RepeatedBoundaryDisposeFailure);
        Assert.Null(graph.RepeatedCaptureDisposeFailure);
        Assert.True(graph.SameBoundaryTask);
        Assert.True(graph.DeliveryJoined);
        Assert.True(graph.DrainResult);
        Assert.True(graph.FullCleanupProof);
        Assert.Equal((1, 1), graph.FinalBinding);
        Assert.Equal((true, 0, true), graph.StartResourceUse);
        Assert.Equal((true, 0, true), graph.StopResourceUse);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal(graph.Final.StartPrimitiveFacts, graph.Final.StopPrimitiveFacts);
        Assert.Null(graph.Final.StartFailure);
        Assert.True(graph.Final.NativeDisposed);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Null(graph.CatalogDisposeFailure);
        Assert.Equal((1, 1, 0), graph.BeforeCatalogUsage);
        Assert.Equal((0, 0, 0), graph.AfterCatalogUsage);
        Assert.Equal(0, graph.FinalEntryReferences);
        Assert.Equal((0, 0), graph.FinalSourceReferences);
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, graph.SourceRetains);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, graph.SourceReleases);
        Assert.True(graph.ReplacementSucceeded);
        Assert.Equal(1, graph.ReplacementEnumerations);
        Assert.Equal((0, 0, 0), graph.AfterReplacementUsage);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal((0, 0), (graph.NativeHeldCopy, graph.NativeHeldCopyReleases));
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.All(graph.CompleteGraph, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OuterHealthyGraph CreateActualOuterHealthyCapture()
    {
        var captureApi = new MacOSRemoteWindowScreenCaptureKitApi();
        var sourceEffects = new SourceEffects();
        IMacOSRemoteWindowNativeSource nativeSource = MacOSRemoteWindowScreenCaptureKitApi.CreateNativeSourceWithOperations(
            new(1, 123, 1, 0), NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2),
            SourceEffects.Window, SourceEffects.Filter, sourceEffects);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture? capture = null;
        bool sameCatalogNative = false;
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            CaptureFactory = (source, takeSampleOwnership, sourceUnavailable) =>
            {
                sameCatalogNative = ReferenceEquals(nativeSource, source);
                capture = captureApi.CreateCaptureWithOperations(source, operations, takeSampleOwnership, sourceUnavailable);
                return capture;
            },
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool(catalogCapacity: 1, batchCapacity: 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api, ownershipPool: pool);
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink();
        WeakReference? startOwner = null;
        WeakReference? startPrimitive = null;

        TerminalPublicationPhase ReadPhase()
        {
            var first = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwner!.Target);
            var last = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
            return new((runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                    runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns),
                (operations.StartSelectorAttempts, operations.StopSelectorAttempts, operations.NativeInvocationAttempts,
                    operations.StartInvocations, operations.StopInvocations),
                (operations.PoolsPushed, operations.PoolsPopped, operations.RemoveCalls, operations.DrainCalls,
                    operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts),
                ReadZeroPoolPrimitiveFacts(first), ReadZeroPoolPrimitiveFacts(last), first.FirstFailure,
                operations.ObjectReleaseAttempts.ToArray(), capture!.IsCleanupConfirmed,
                MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        }

        Exception? entryFailure = null;
        LocalBoundaryResult startResult = LocalBoundaryResult.Failed("test_start_not_observed");
        CaptureCompletionPhase? started = null;
        (int, int, int, int, int, int, int, int) startedRuntime = default;
        int startedCharge = 0;
        object? outerOperation = null;
        MacOSRemoteWindowSourceCatalog.NativeBinding? binding = null;
        MacOSRemoteWindowSourceCatalog.SourceEntry? entry = null;
        (int, int) initialBinding = default;
        (int, int) initialSourceReferences = default;
        (int, int, int) initialUsage = default;
        (int, int) finalBinding = default;
        Exception? boundaryFailure = null;
        Exception? repeatedBoundaryFailure = null;
        Exception? repeatedCaptureFailure = null;
        Exception? catalogFailure = null;
        bool sameBoundaryTask = false;
        bool deliveryJoined = false;
        bool drainResult = false;
        bool fullProof = false;
        (bool, int, bool) startResource = default;
        (bool, int, bool) stopResource = default;
        TerminalPublicationPhase? final = null;
        TerminalPublicationPhase? repeated = null;
        (int, int, int) beforeCatalogUsage = default;
        (int, int, int) afterCatalogUsage = default;
        (int, int, int) afterReplacementUsage = default;
        bool replacementSucceeded = false;
        int replacementEnumerations = 0;
        Task? boundaryDisposal = null;
        Task? catalogDisposal = null;
        MacOSRemoteWindowSourceCatalog? replacement = null;
        Task<LocalBoundaryResult>? replacementRefresh = null;
        Task? replacementDisposal = null;
        try
        {
            entryFailure = Record.Exception(() =>
            {
                catalog.RefreshAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
                NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
                startResult = boundary.StartAsync(NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), sink, CancellationToken.None)
                    .AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
            });
            startOwner = operations.Completion!;
            startPrimitive = operations.Primitive!;
            started = ReadCaptureCompletionPhase(capture!, "start");
            startedRuntime = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns);
            startedCharge = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
            outerOperation = boundary.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            binding = Assert.IsType<MacOSRemoteWindowSourceCatalog.NativeBinding>(outerOperation.GetType()
                .GetField("binding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outerOperation));
            entry = Assert.IsType<MacOSRemoteWindowSourceCatalog.SourceEntry>(binding.GetType()
                .GetField("entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding));
            (int, int) BindingFacts() => ((int)binding.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding)!,
                (int)entry.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!);
            initialBinding = BindingFacts();
            initialSourceReferences = (sourceEffects.References[SourceEffects.Window], sourceEffects.References[SourceEffects.Filter]);
            initialUsage = pool.GetUsage();
            boundaryDisposal = boundary.DisposeAsync().AsTask();
            boundaryFailure = Record.Exception(() => boundaryDisposal.WaitAsync(Timeout).GetAwaiter().GetResult());
            sameBoundaryTask = ReferenceEquals(boundaryDisposal, boundary.StopCompletion);
            deliveryJoined = boundary.DeliveryCompletion.IsCompletedSuccessfully;
            var actualDrain = Assert.IsAssignableFrom<Task<bool>>(capture!.GetType()
                .GetField("drain", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
            drainResult = actualDrain.WaitAsync(Timeout).GetAwaiter().GetResult();
            fullProof = capture.IsCleanupConfirmed;
            finalBinding = BindingFacts();
            startResource = ReadCompletionResourceUseFacts(startOwner);
            stopResource = ReadCompletionResourceUseFacts(operations.Completion!);
            final = ReadPhase();
            repeatedBoundaryFailure = Record.Exception(() => boundary.DisposeAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
            repeatedCaptureFailure = Record.Exception(capture.Dispose);
            repeated = ReadPhase();
            beforeCatalogUsage = pool.GetUsage();
            catalogDisposal = catalog.DisposeAsync().AsTask();
            catalogFailure = Record.Exception(() => catalogDisposal.WaitAsync(Timeout).GetAwaiter().GetResult());
            afterCatalogUsage = pool.GetUsage();
            var replacementApi = new MacOSRemoteWindowTestApi();
            replacement = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), replacementApi, ownershipPool: pool);
            replacementRefresh = replacement.RefreshAsync().AsTask();
            replacementSucceeded = replacementRefresh.WaitAsync(Timeout).GetAwaiter().GetResult().Succeeded;
            replacementEnumerations = replacementApi.EnumerationCalls;
            replacementDisposal = replacement.DisposeAsync().AsTask();
            replacementDisposal.WaitAsync(Timeout).GetAwaiter().GetResult();
            afterReplacementUsage = pool.GetUsage();
        }
        finally
        {
            // Join actual cached work even if an observation watchdog failed.
            // These joins do not retry effects or substitute for proof above.
            boundaryDisposal ??= boundary.DisposeAsync().AsTask();
            _ = Record.Exception(() => boundaryDisposal.GetAwaiter().GetResult());
            catalogDisposal ??= catalog.DisposeAsync().AsTask();
            _ = Record.Exception(() => catalogDisposal.GetAwaiter().GetResult());
            if (replacementRefresh is not null) { _ = Record.Exception(() => replacementRefresh.GetAwaiter().GetResult()); }
            if (replacement is not null)
            {
                replacementDisposal ??= replacement.DisposeAsync().AsTask();
                _ = Record.Exception(() => replacementDisposal.GetAwaiter().GetResult());
            }
            runtime.Dispose();
        }

        object sourceOwner = capture!.GetType().GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        object retainedNativeSource = sourceOwner.GetType().GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sourceOwner)!;
        int finalEntryReferences = (int)entry!.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(entryFailure, startResult, sameCatalogNative, api.CaptureCalls, initialBinding, initialSourceReferences, initialUsage,
            startedRuntime, startedCharge, started!, boundaryFailure, repeatedBoundaryFailure, repeatedCaptureFailure,
            sameBoundaryTask, deliveryJoined, drainResult, fullProof, finalBinding, startResource, stopResource,
            final!, repeated!, catalogFailure, beforeCatalogUsage, afterCatalogUsage, finalEntryReferences,
            (sourceEffects.References[SourceEffects.Window], sourceEffects.References[SourceEffects.Filter]),
            sourceEffects.RetainAttempts.ToArray(), sourceEffects.ReleaseAttempts.ToArray(), replacementSucceeded, replacementEnumerations,
            afterReplacementUsage, operations.Stream, operations.Output, operations.Configuration,
            operations.PoolPushEffects.ToArray(), operations.PoolPopEffects.ToArray(), Volatile.Read(ref operations.NativeHeldCompletion),
            operations.NativeHeldReleaseAttempts, raw,
            [new(capture), new(captureApi), new(boundary), new(catalog), new(pool), new(outerOperation), new(binding), new(entry),
                new(nativeSource), new(sourceOwner), new(retainedNativeSource), new(sink), new(api), new(sourceEffects), new(operations),
                new(runtime), startOwner!, startPrimitive!, operations.Completion!, operations.Primitive!]);
    }

    private sealed record OuterHealthyGraph(Exception? EntryFailure, LocalBoundaryResult StartResult, bool SameCatalogNativeSource,
        int CaptureCalls, (int Disposed, int References) InitialBinding, (int Window, int Filter) InitialSourceReferences,
        (int Catalogs, int Sources, int Batches) InitialUsage,
        (int Roots, int Copies, int Releases, int RootFrees, int LiveRoots, int LiveBlocks, int Invokes, int Returns) StartedRuntimeEffects,
        int StartedChargedOwners, CaptureCompletionPhase Started, Exception? BoundaryDisposeFailure,
        Exception? RepeatedBoundaryDisposeFailure, Exception? RepeatedCaptureDisposeFailure,
        bool SameBoundaryTask, bool DeliveryJoined, bool DrainResult, bool FullCleanupProof,
        (int Disposed, int References) FinalBinding, (bool Closed, int ActiveUsers, bool JoinCompleted) StartResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) StopResourceUse,
        TerminalPublicationPhase Final, TerminalPublicationPhase Repeated, Exception? CatalogDisposeFailure,
        (int Catalogs, int Sources, int Batches) BeforeCatalogUsage, (int Catalogs, int Sources, int Batches) AfterCatalogUsage,
        int FinalEntryReferences, (int Window, int Filter) FinalSourceReferences, nint[] SourceRetains, nint[] SourceReleases,
        bool ReplacementSucceeded, int ReplacementEnumerations, (int Catalogs, int Sources, int Batches) AfterReplacementUsage,
        nint Stream, nint Output, nint Configuration, (int Ordinal, nint Token, int Thread)[] PushEffects,
        (int Ordinal, nint Token, int Thread)[] PopEffects, nint NativeHeldCopy, int NativeHeldCopyReleases,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference[] CompleteGraph);

    [Fact]
    public void FreshCompletedFailureAtTerminalObservationPreservesFatalAndReturnsOuterBinding()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        TerminalPublicationGraph graph = CreateFreshCompletedFailureWithOuterBinding();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.True(graph.StartResult.Succeeded);
        Assert.True(graph.SameCatalogNativeSource);
        Assert.Equal(1, graph.CaptureCalls);
        Assert.True(graph.ProducerJoined);
        Assert.Null(graph.ProducerFailure);
        Assert.Equal(new Exception?[] { null, null, graph.Fatal }, graph.ProductionFailureReads);
        string[] expectedOrder = ["failure:null", "failure:null", "native:retired-managed:active",
            "producer:ABI-returned", "managed:terminal", "failure:fatal"];
        Assert.Equal(expectedOrder, graph.ProductionReadOrder);
        Assert.Equal((true, false, 1, true), graph.NativeObservation);
        Assert.Equal((true, 0, true), graph.NativeResourceUse);
        Assert.True(graph.SameNativeTask);
        Assert.True(graph.SameManagedTask);
        Assert.Null(graph.FailureAtNativeObservation);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 1), graph.BeforePublication.RuntimeEffects);
        Assert.Equal(before + 1, graph.BeforePublication.ChargedOwners);
        Assert.Equal((true, true, true, true, true, true, true, false, true, true, 1L), graph.BeforePublication.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.BeforePublication.StopPrimitiveFacts);
        Assert.Null(graph.BeforePublication.StartFailure);
        Assert.Same(graph.Fatal, graph.Final.StartFailure);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal(graph.Final.StartPrimitiveFacts, graph.Final.StopPrimitiveFacts);
        Assert.True(graph.Final.NativeDisposed);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1), graph.Final.CleanupEffects);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Same(graph.Fatal, graph.BoundaryFailure);
        Assert.Same(graph.Fatal, graph.RepeatedBoundaryFailure);
        Assert.Same(graph.Fatal, graph.RepeatedCaptureFailure);
        Assert.True(graph.SameBoundaryTask);
        Assert.True(graph.DeliveryJoined);
        // Core fresh publication assertions above establish the controlled
        // window before this separate real outer binding cleanup assertion.
        Assert.Equal((0, 2), graph.InitialBinding);
        Assert.Equal((1, 1), graph.FinalBinding);
        Assert.True(graph.FullCleanupProof);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Null(graph.CatalogDisposeFailure);
        Assert.Equal((1, 1, 0), graph.BeforeCatalogUsage);
        Assert.Equal((0, 0, 0), graph.AfterCatalogUsage);
        Assert.Equal((0, 0), graph.FinalSourceReferences);
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, graph.SourceRetains);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, graph.SourceReleases);
        Assert.True(graph.ReplacementSucceeded);
        Assert.Equal(1, graph.ReplacementEnumerations);
        Assert.Equal((0, 0, 0), graph.AfterReplacementUsage);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.All(graph.CompleteGraph, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests the actual completed fatal propagation window and independently confirmed native versus outer binding cleanup.")]
    private static TerminalPublicationGraph CreateFreshCompletedFailureWithOuterBinding()
    {
        var captureApi = new MacOSRemoteWindowScreenCaptureKitApi();
        var sourceEffects = new SourceEffects();
        IMacOSRemoteWindowNativeSource nativeSource = MacOSRemoteWindowScreenCaptureKitApi.CreateNativeSourceWithOperations(
            new(1, 123, 1, 0), NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2),
            SourceEffects.Window, SourceEffects.Filter, sourceEffects);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowCompletedBodyReturn = new ManualResetEventSlim();
        using var allowFailurePublication = new ManualResetEventSlim();
        var completedResourceExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fatal = new OutOfMemoryException("Original actual completed fatal published during terminal observation.");
        var readOrder = new ConcurrentQueue<string>();
        var failureReads = new ConcurrentQueue<Exception?>();
        IMacOSRemoteWindowNativeCapture? capture = null;
        CompletionOwnershipOperations? operations = null;
        MacOSRemoteWindowCaptureCompletion? startOwner = null;
        TerminalPublicationCompletionForwarder? facade = null;
        Thread? producer = null;
        bool producerJoined = false;
        Exception? producerFailure = null;
        bool sameNativeTask = false;
        bool sameManagedTask = false;
        Exception? failureAtNativeObservation = null;
        (bool, bool, int, bool) nativeObservation = default;
        (bool, int, bool) nativeResourceUse = default;
        TerminalPublicationPhase? beforePublication = null;

        TerminalPublicationPhase ReadPhase()
        {
            var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations!.Completion!.Target);
            return new((runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                    runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns),
                (operations.StartSelectorAttempts, operations.StopSelectorAttempts, operations.NativeInvocationAttempts,
                    operations.StartInvocations, operations.StopInvocations),
                (operations.PoolsPushed, operations.PoolsPopped, operations.RemoveCalls, operations.DrainCalls,
                    operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts),
                ReadZeroPoolPrimitiveFacts(startOwner!), ReadZeroPoolPrimitiveFacts(stopOwner),
                startOwner!.FirstFailure, operations.ObjectReleaseAttempts.ToArray(),
                (bool)capture!.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!,
                MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        }

        operations = new(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartCompletedRelease = allowCompletedBodyReturn,
            StartCompletedFailureAfterEffect = new AggregateException(new IOException("Actual completed fatal wrapper.", fatal)),
            StartCompletedResourceUseExited = () =>
            {
                completedResourceExited.TrySetResult();
                allowFailurePublication.Wait();
            },
            StartCompletionForwarder = owner =>
            {
                startOwner = owner;
                facade = new(owner, failure =>
                {
                    failureReads.Enqueue(failure);
                    readOrder.Enqueue(failure is null ? "failure:null" : "failure:fatal");
                }, actualNativeTask =>
                {
                    sameNativeTask = ReferenceEquals(actualNativeTask, owner.NativeCaptureRetirement);
                    failureAtNativeObservation = owner.FirstFailure;
                    nativeObservation = (actualNativeTask.IsCompletedSuccessfully, owner.ManagedInvocationDrain.IsCompletedSuccessfully,
                        owner.Primitive.ActiveManagedInvocations, failureAtNativeObservation is null);
                    nativeResourceUse = ReadCompletionResourceUseFacts(new(owner));
                    // Read actual tasks/facts before releasing the real producer;
                    // no primitive failure or lifetime state is manufactured.
                    beforePublication = ReadPhase();
                    readOrder.Enqueue("native:retired-managed:active");
                    allowFailurePublication.Set();
                    producer!.Join();
                    producerJoined = true;
                }, actualManagedTask =>
                {
                    sameManagedTask = ReferenceEquals(actualManagedTask, owner.ManagedInvocationDrain);
                    readOrder.Enqueue(actualManagedTask.IsCompletedSuccessfully ? "managed:terminal" : "managed:pending");
                });
                return facade;
            },
        };
        bool sameCatalogNative = false;
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            CaptureFactory = (source, takeSampleOwnership, sourceUnavailable) =>
            {
                sameCatalogNative = ReferenceEquals(nativeSource, source);
                capture = captureApi.CreateCaptureWithOperations(source, operations, takeSampleOwnership, sourceUnavailable);
                return capture;
            },
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool(catalogCapacity: 1, batchCapacity: 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api, ownershipPool: pool);
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink();
        Exception? entryFailure = null;
        LocalBoundaryResult startResult = LocalBoundaryResult.Failed("test_start_not_observed");
        Exception? boundaryFailure = null;
        Exception? repeatedBoundaryFailure = null;
        Exception? repeatedCaptureFailure = null;
        Exception? catalogDisposeFailure = null;
        bool sameBoundaryTask = false;
        bool deliveryJoined = false;
        bool producerStarted = false;
        object? outerOperation = null;
        MacOSRemoteWindowSourceCatalog.NativeBinding? binding = null;
        MacOSRemoteWindowSourceCatalog.SourceEntry? entry = null;
        (int, int) initialBinding = default;
        (int, int) finalBinding = default;
        (int, int, int) beforeCatalogUsage = default;
        (int, int, int) afterCatalogUsage = default;
        (int, int, int) afterReplacementUsage = default;
        bool replacementSucceeded = false;
        int replacementEnumerations = 0;
        Task? firstBoundaryTask = null;
        Task? catalogDisposal = null;
        MacOSRemoteWindowSourceCatalog? replacement = null;
        Task<LocalBoundaryResult>? replacementRefresh = null;
        Task? replacementDisposal = null;
        TerminalPublicationPhase? final = null;
        TerminalPublicationPhase? repeated = null;
        try
        {
            entryFailure = Record.Exception(() =>
            {
                catalog.RefreshAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
                NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
                Task<LocalBoundaryResult> started = boundary.StartAsync(NativeRemoteWindowSourceUse.Create(snapshot, 1, 1), sink,
                    CancellationToken.None).AsTask();
                nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
                producer = new Thread(() =>
                {
                    producerFailure = Record.Exception(() => runtime.Invoke(nativeOwnedPointer, 0));
                    readOrder.Enqueue("producer:ABI-returned");
                })
                { IsBackground = true, Name = "Flowspan actual fresh completed terminal publication ABI" };
                producer.Start();
                producerStarted = true;
                operations.StartCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
                startResult = started.WaitAsync(Timeout).GetAwaiter().GetResult();
                allowCompletedBodyReturn.Set();
                completedResourceExited.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            });
            outerOperation = boundary.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            binding = Assert.IsType<MacOSRemoteWindowSourceCatalog.NativeBinding>(outerOperation.GetType()
                .GetField("binding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outerOperation));
            entry = Assert.IsType<MacOSRemoteWindowSourceCatalog.SourceEntry>(binding.GetType()
                .GetField("entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding));
            (int, int) BindingFacts() => ((int)binding.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding)!,
                (int)entry.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!);
            initialBinding = BindingFacts();
            firstBoundaryTask = boundary.DisposeAsync().AsTask();
            boundaryFailure = Record.Exception(() => firstBoundaryTask.WaitAsync(Timeout).GetAwaiter().GetResult());
            sameBoundaryTask = ReferenceEquals(firstBoundaryTask, boundary.StopCompletion);
            deliveryJoined = boundary.DeliveryCompletion.IsCompletedSuccessfully;
            final = ReadPhase();
            finalBinding = BindingFacts();
            repeatedBoundaryFailure = Record.Exception(() => boundary.DisposeAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
            repeatedCaptureFailure = Record.Exception(capture!.Dispose);
            repeated = ReadPhase();
            beforeCatalogUsage = pool.GetUsage();
            catalogDisposal = catalog.DisposeAsync().AsTask();
            catalogDisposeFailure = Record.Exception(() => catalogDisposal.WaitAsync(Timeout).GetAwaiter().GetResult());
            afterCatalogUsage = pool.GetUsage();
            var replacementApi = new MacOSRemoteWindowTestApi();
            replacement = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), replacementApi, ownershipPool: pool);
            replacementRefresh = replacement.RefreshAsync().AsTask();
            replacementSucceeded = replacementRefresh.WaitAsync(Timeout).GetAwaiter().GetResult().Succeeded;
            replacementEnumerations = replacementApi.EnumerationCalls;
            replacementDisposal = replacement.DisposeAsync().AsTask();
            replacementDisposal.WaitAsync(Timeout).GetAwaiter().GetResult();
            afterReplacementUsage = pool.GetUsage();
        }
        finally
        {
            allowCompletedBodyReturn.Set();
            allowFailurePublication.Set();
            if (producerStarted && !producerJoined) { producer!.Join(); producerJoined = true; }
            // Watchdog observations above do not authorize raw teardown while
            // any actual cleanup task is active. Join original cached work;
            // these joins never retry native effects or establish test proof.
            if (firstBoundaryTask is null) { firstBoundaryTask = boundary.DisposeAsync().AsTask(); }
            _ = Record.Exception(() => firstBoundaryTask.GetAwaiter().GetResult());
            catalogDisposal ??= catalog.DisposeAsync().AsTask();
            _ = Record.Exception(() => catalogDisposal.GetAwaiter().GetResult());
            if (replacementRefresh is not null) { _ = Record.Exception(() => replacementRefresh.GetAwaiter().GetResult()); }
            if (replacement is not null)
            {
                replacementDisposal ??= replacement.DisposeAsync().AsTask();
                _ = Record.Exception(() => replacementDisposal.GetAwaiter().GetResult());
            }
            runtime.Dispose();
        }

        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        var sourceOwner = capture!.GetType().GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        return new(fatal, entryFailure, startResult, sameCatalogNative, api.CaptureCalls, producerJoined, producerFailure,
            failureReads.ToArray(), readOrder.ToArray(), nativeObservation, nativeResourceUse,
            sameNativeTask, sameManagedTask, failureAtNativeObservation, beforePublication!, final!, repeated!,
            boundaryFailure, repeatedBoundaryFailure, repeatedCaptureFailure, sameBoundaryTask, deliveryJoined,
            initialBinding, finalBinding, capture.IsCleanupConfirmed, catalogDisposeFailure, beforeCatalogUsage, afterCatalogUsage,
            (sourceEffects.References[SourceEffects.Window], sourceEffects.References[SourceEffects.Filter]),
            sourceEffects.RetainAttempts.ToArray(), sourceEffects.ReleaseAttempts.ToArray(), replacementSucceeded, replacementEnumerations,
            afterReplacementUsage, operations.Stream, operations.Output, operations.Configuration,
            operations.PoolPushEffects.ToArray(), operations.PoolPopEffects.ToArray(), raw,
            [new(capture), new(boundary), new(catalog), new(pool), new(outerOperation), new(binding), new(entry), new(nativeSource),
                new(sourceOwner), new(sink), new(api), new(sourceEffects), new(operations), new(runtime), new(facade),
                new(startOwner), new(startOwner!.Primitive), operations.Completion!, operations.Primitive!]);
    }

    private sealed class TerminalPublicationCompletionForwarder(MacOSRemoteWindowCaptureCompletion owner,
        Action<Exception?> failureObserved, Action<Task> nativeRetirementObserved, Action<Task> managedDrainObserved)
        : IMacOSRemoteWindowStagedCaptureCompletion
    {
        private int nativeObservations;
        public void AcquireCopy() => owner.AcquireCopy();
        public Task CloseResourceUse() => owner.CloseResourceUse();
        public bool HasActiveResourceUseAncestry => owner.HasActiveResourceUseAncestry;
        public nint Pointer => owner.Pointer;
        public bool IsReleased => owner.IsReleased;
        public Exception? FirstFailure
        {
            get { Exception? actual = owner.FirstFailure; failureObserved(actual); return actual; }
        }
        public Task NativeCaptureRetirement
        {
            get
            {
                Task actual = owner.NativeCaptureRetirement;
                if (Interlocked.Increment(ref nativeObservations) == 1) { nativeRetirementObserved(actual); }
                return actual;
            }
        }
        public Task ManagedInvocationDrain
        {
            get { Task actual = owner.ManagedInvocationDrain; managedDrainObserved(actual); return actual; }
        }
        public void Dispose() => owner.Dispose();
    }

    private sealed record TerminalPublicationPhase(
        (int Roots, int Copies, int Releases, int RootFrees, int LiveRoots, int LiveBlocks, int Invokes, int Returns) RuntimeEffects,
        (int StartSelectors, int StopSelectors, int NativeAttempts, int Starts, int Stops) NativeEffects,
        (int Pushes, int Pops, int Removes, int Barriers, int ObjectsReleased, int QueuesReleased) CleanupEffects,
        (bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, long) StartPrimitiveFacts,
        (bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, long) StopPrimitiveFacts,
        Exception? StartFailure, nint[] ObjectReleases, bool NativeDisposed, int ChargedOwners);

    private sealed record TerminalPublicationGraph(OutOfMemoryException Fatal, Exception? EntryFailure,
        LocalBoundaryResult StartResult, bool SameCatalogNativeSource, int CaptureCalls, bool ProducerJoined, Exception? ProducerFailure,
        Exception?[] ProductionFailureReads, string[] ProductionReadOrder,
        (bool NativeRetired, bool ManagedTerminal, int ActivePrimitiveInvocations, bool FailureNull) NativeObservation,
        (bool Closed, int ActiveUsers, bool JoinCompleted) NativeResourceUse,
        bool SameNativeTask, bool SameManagedTask, Exception? FailureAtNativeObservation,
        TerminalPublicationPhase BeforePublication, TerminalPublicationPhase Final, TerminalPublicationPhase Repeated,
        Exception? BoundaryFailure, Exception? RepeatedBoundaryFailure, Exception? RepeatedCaptureFailure,
        bool SameBoundaryTask, bool DeliveryJoined, (int Disposed, int References) InitialBinding, (int Disposed, int References) FinalBinding,
        bool FullCleanupProof,
        Exception? CatalogDisposeFailure, (int Catalogs, int Sources, int Batches) BeforeCatalogUsage,
        (int Catalogs, int Sources, int Batches) AfterCatalogUsage, (int Window, int Filter) FinalSourceReferences,
        nint[] SourceRetains, nint[] SourceReleases, bool ReplacementSucceeded, int ReplacementEnumerations,
        (int Catalogs, int Sources, int Batches) AfterReplacementUsage, nint Stream, nint Output, nint Configuration,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference[] CompleteGraph);

    [Fact]
    public void ExitedStartCompletedAncestryAllowsInheritedDescendantStopAndDispose()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ExitedCompletedDescendantGraph graph = CreateExitedStartCompletedInheritedDescendant();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.Equal((true, true), graph.BeforeParentBorrow);
        Assert.True(graph.HookHasActualScope);
        Assert.True(graph.HookScopeMatchesOwner);
        Assert.Equal((true, true), graph.ChildReadyAncestry);
        Assert.Equal((false, 1, false), graph.ParentActiveResourceUse);
        Assert.Equal((false, false, false, false), graph.ParentActiveCallerRelease);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.ParentActive.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.ParentActive.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 1, 0), graph.ParentActive.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.ParentActive.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.ParentActive.CleanupEffects);
        Assert.Equal(before + 1, graph.ParentActive.ChargedOwners);
        Assert.True(graph.ParentJoined);
        Assert.Null(graph.ParentFailure);
        Assert.True(graph.ParentScopeRestored);
        Assert.Equal((true, true), graph.AfterParentBorrow);
        Assert.Equal((false, 0, false), graph.AfterParentResourceUse);
        Assert.Equal((1, 2, 0, 0, 1, 1, 1, 1), graph.AfterParent.RuntimeEffects);
        Assert.Equal(graph.ParentActive.NativeEffects, graph.AfterParent.NativeEffects);
        Assert.Equal(graph.ParentActive.CleanupEffects, graph.AfterParent.CleanupEffects);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        // The child still has the exact scope inherited inside guardedCompleted;
        // only its shared active marker changed after actual parent ABI return.
        Assert.Equal((true, false), graph.ChildCleanupAncestry);
        Assert.Equal(1, graph.ChildEntries);
        Assert.NotEqual(graph.ParentThread, graph.ChildReadyThread);
        Assert.True(graph.ChildJoined);
        Assert.Null(graph.ChildJoinFailure);
        Assert.Equal(TaskStatus.RanToCompletion, graph.ChildStatus);
        Assert.True(graph.ChildDrainResult);
        Assert.Null(graph.ChildDrainFailure);
        Assert.Null(graph.ChildDisposeFailure);
        Assert.Null(graph.ChildRepeatedDisposeFailure);
        Assert.Equal((true, 0, true), graph.FinalResourceUse);
        Assert.Equal((true, 0, true), graph.FinalStopResourceUse);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal(graph.Final.StartPrimitiveFacts, graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((before, true, false, 1), (graph.Final.ChargedOwners, graph.Final.NativeCopyReady,
            graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.ActualScope.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.SourceOwner.IsAlive);
        Assert.False(graph.SourceState.IsAlive);
        Assert.False(graph.Runtime.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ExitedCompletedDescendantGraph CreateExitedStartCompletedInheritedDescendant()
    {
        static object? ReadCurrentScope()
        {
            object current = typeof(Flowspan.Platform.NativeRemoteWindowDrainActivityScope)
                .GetField("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            return current.GetType().GetProperty("Value")!.GetValue(current);
        }

        static (bool Returned, bool BorrowExited) ReadBorrow(IMacOSRemoteWindowNativeCapture value)
        {
            object Field(string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
            lock (Field("gate"))
            {
                return ((bool)Field("startInvocationReturned"), (bool)Field("startInvocationBorrowExited"));
            }
        }

        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowParentReturn = new ManualResetEventSlim();
        var childReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowChildCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IMacOSRemoteWindowNativeCapture? capture = null;
        WeakReference? startCompletion = null;
        WeakReference? actualScope = null;
        Task? child = null;
        bool hookHasScope = false;
        bool hookMatchesOwner = false;
        (bool, bool) childReadyAncestry = default;
        (bool, bool) childCleanupAncestry = default;
        int childEntries = 0;
        int parentThread = 0;
        int childReadyThread = 0;
        bool childDrainResult = false;
        Exception? childDrainFailure = null;
        Exception? childDisposeFailure = null;
        Exception? childRepeatedDisposeFailure = null;
        LateCallbackPhase? final = null;
        LateCallbackPhase? repeated = null;
        WeakReference? stopCompletion = null;
        WeakReference? stopPrimitive = null;
        (bool, int, bool) finalResourceUse = default;
        (bool, int, bool) finalStopResourceUse = default;
        CompletionOwnershipOperations? operations = null;
        operations = new(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartCompletedHook = () =>
            {
                object? scope = ReadCurrentScope();
                hookHasScope = scope is not null;
                actualScope = new(scope);
                hookMatchesOwner = ReferenceEquals(scope?.GetType()
                    .GetField("owner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope), startCompletion!.Target);
                // Normal Task.Run inheritance from the actual guarded region.
                // The hook does not wait for the child or manufacture a scope.
                child = Task.Run(async () =>
                {
                    childReadyThread = Environment.CurrentManagedThreadId;
                    childReadyAncestry = (ReferenceEquals(ReadCurrentScope(), actualScope.Target),
                        ((IMacOSRemoteWindowStagedCaptureCompletion)startCompletion!.Target!).HasActiveResourceUseAncestry);
                    childReady.TrySetResult();
                    await allowChildCleanup.Task;
                    childCleanupAncestry = (ReferenceEquals(ReadCurrentScope(), actualScope.Target),
                        ((IMacOSRemoteWindowStagedCaptureCompletion)startCompletion.Target!).HasActiveResourceUseAncestry);
                    Interlocked.Increment(ref childEntries);
                    childDrainFailure = await Record.ExceptionAsync(async () => childDrainResult = await capture!.StopAndDrainAsync());
                    stopCompletion = operations!.Completion!;
                    stopPrimitive = operations.Primitive!;
                    childDisposeFailure = Record.Exception(capture!.Dispose);
                    final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
                    finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
                    finalStopResourceUse = ReadCompletionResourceUseFacts(stopCompletion);
                    childRepeatedDisposeFailure = Record.Exception(capture.Dispose);
                    repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
                });
            },
            StartCompletedRelease = allowParentReturn,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Task<bool> start = capture.StartAsync().AsTask();
        startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        var sourceOwner = new WeakReference(capture.GetType()
            .GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        var beforeParentBorrow = ReadBorrow(capture);
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? parentFailure = null;
        bool parentScopeRestored = false;
        var parent = new Thread(() =>
        {
            parentThread = Environment.CurrentManagedThreadId;
            parentFailure = Record.Exception(() => runtime.Invoke(nativeOwnedPointer, 0));
            parentScopeRestored = ReadCurrentScope() is null;
        })
        { IsBackground = true, Name = "Flowspan exited Start completed inherited descendant ABI" };
        bool parentStarted = false;
        bool parentJoined = false;
        bool childJoined = false;
        Exception? childJoinFailure = null;
        Exception? entryFailure = null;
        LateCallbackPhase? parentActive = null;
        LateCallbackPhase? afterParent = null;
        (bool, int, bool) parentActiveResourceUse = default;
        (bool, int, bool) afterParentResourceUse = default;
        (bool, bool, bool, bool) parentActiveCallerRelease = default;
        (bool, bool) afterParentBorrow = default;
        bool startResult = false;
        Exception? startFailure = null;
        try
        {
            parent.Start();
            parentStarted = true;
            entryFailure = Record.Exception(() =>
            {
                operations.StartCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
                childReady.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            });
            parentActive = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            parentActiveResourceUse = ReadCompletionResourceUseFacts(startCompletion);
            parentActiveCallerRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            allowParentReturn.Set();
            parent.Join();
            parentJoined = true;
            afterParent = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            afterParentResourceUse = ReadCompletionResourceUseFacts(startCompletion);
            afterParentBorrow = ReadBorrow(capture);
            startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
            // Release only after actual ABI return and the post-Join snapshot.
            allowChildCleanup.TrySetResult();
            childJoinFailure = Record.Exception(() => child!.GetAwaiter().GetResult());
            childJoined = true;
        }
        finally
        {
            allowParentReturn.Set();
            if (parentStarted && !parentJoined) { parent.Join(); parentJoined = true; }
            allowChildCleanup.TrySetResult();
            if (child is not null && !childJoined)
            {
                childJoinFailure = Record.Exception(() => child.GetAwaiter().GetResult());
                childJoined = true;
            }
            // Raw teardown follows both real joins and never proves cleanup.
            runtime.Dispose();
        }

        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(entryFailure, beforeParentBorrow, hookHasScope, hookMatchesOwner,
            childReadyAncestry, parentActive!, parentActiveResourceUse, parentActiveCallerRelease,
            parentJoined, parentFailure, parentScopeRestored, afterParentBorrow, afterParent!, afterParentResourceUse,
            startResult, startFailure, childCleanupAncestry, childEntries, parentThread, childReadyThread,
            childJoined, childJoinFailure, child?.Status ?? default, childDrainResult, childDrainFailure,
            childDisposeFailure, childRepeatedDisposeFailure, final!, repeated!, finalResourceUse, finalStopResourceUse,
            operations.Stream, operations.Output, operations.Configuration,
            operations.PoolPushEffects.ToArray(), operations.PoolPopEffects.ToArray(), raw,
            new(capture), startCompletion, startPrimitive, stopCompletion!, stopPrimitive!, actualScope!, new(operations),
            sourceOwner, new(source.State), new(runtime), new(sampleMarker), new(unavailableMarker));
    }

    private sealed record ExitedCompletedDescendantGraph(Exception? EntryFailure,
        (bool Returned, bool BorrowExited) BeforeParentBorrow, bool HookHasActualScope, bool HookScopeMatchesOwner,
        (bool SameScope, bool Active) ChildReadyAncestry, LateCallbackPhase ParentActive,
        (bool Closed, int ActiveUsers, bool JoinCompleted) ParentActiveResourceUse,
        (bool, bool, bool, bool) ParentActiveCallerRelease, bool ParentJoined, Exception? ParentFailure,
        bool ParentScopeRestored, (bool Returned, bool BorrowExited) AfterParentBorrow,
        LateCallbackPhase AfterParent, (bool Closed, int ActiveUsers, bool JoinCompleted) AfterParentResourceUse,
        bool StartResult, Exception? StartFailure, (bool SameScope, bool Active) ChildCleanupAncestry,
        int ChildEntries, int ParentThread, int ChildReadyThread, bool ChildJoined, Exception? ChildJoinFailure,
        TaskStatus ChildStatus, bool ChildDrainResult, Exception? ChildDrainFailure,
        Exception? ChildDisposeFailure, Exception? ChildRepeatedDisposeFailure,
        LateCallbackPhase Final, LateCallbackPhase Repeated,
        (bool Closed, int ActiveUsers, bool JoinCompleted) FinalResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) FinalStopResourceUse,
        nint Stream, nint Output, nint Configuration,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference ActualScope,
        WeakReference Operations, WeakReference SourceOwner, WeakReference SourceState, WeakReference Runtime,
        WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void ClosedStartAdmissionLateCallbackDoesNotTouchReleasedCaptureAndRetiresKnownCopy()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ClosedStartLateGraph graph = CreateClosedStartAdmissionLateCallback();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.Null(graph.ReadyFailure);
        Assert.True(graph.LateJoined);
        Assert.Null(graph.LateFailure);
        Assert.True(graph.ValidKnownPointer);
        Assert.Equal((2, 3, 2, 1, 1, 1, 2, 2), graph.BeforeLate.RuntimeEffects);
        Assert.Equal(graph.BeforeLate.RuntimeEffects, graph.Ready.RuntimeEffects);
        Assert.Equal((2, 3, 2, 1, 1, 1, 3, 3), graph.AfterLate.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.BeforeLate.CleanupEffects);
        Assert.Equal(graph.BeforeLate.CleanupEffects, graph.Ready.CleanupEffects);
        Assert.Equal(graph.BeforeLate.CleanupEffects, graph.AfterLate.CleanupEffects);
        Assert.Equal(graph.BeforeLate.NativeEffects, graph.AfterLate.NativeEffects);
        Assert.Equal((0, 0, 0, 0), graph.ReadyNativePointers);
        Assert.Equal((true, 0, true), graph.ReadyResourceUse);
        Assert.Equal(graph.ReadyResourceUse, graph.LateResourceUse);
        Assert.Equal((true, 0, true), graph.ReadyStopResourceUse);
        Assert.Equal(graph.ReadyStopResourceUse, graph.LateStopResourceUse);
        Assert.Equal((true, false, true, true, true, 1, 0), graph.ReadyState);
        Assert.Equal(graph.ReadyState, graph.LateState);
        Assert.Equal((1, 1, 0), (graph.BeforeCompletedCalls, graph.AfterCompletedCalls, graph.UnavailableCalls));
        // Unlike SourceUnavailable0 alone (also protected by result winner),
        // the existing completed hook does not reenter after use admission closes.
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterLate.Start.Facts);
        Assert.Equal(graph.BeforeLate.Start.Facts, graph.AfterLate.Start.Facts);
        Assert.Equal((true, true, true, true, true, true, false, false, false, false, 1L), graph.AfterLate.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.AfterLate.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.AfterLate.StartPrimitiveFailure);
        Assert.Null(graph.AfterLate.StopPrimitiveFailure);
        Assert.Equal(before + 1, graph.AfterLate.ChargedOwners);
        Assert.Equal(1, graph.KnownCopyRetirementAttempts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 3, 3), graph.AfterRetirement.RuntimeEffects);
        Assert.Equal(before + 1, graph.AfterRetirement.ChargedOwners);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.AfterRetirement.StartPrimitiveFacts);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.RepeatedDisposeFailure);
        Assert.Equal(graph.AfterRetirement.RuntimeEffects, graph.Final.RuntimeEffects);
        Assert.Equal(graph.BeforeLate.NativeEffects, graph.Final.NativeEffects);
        Assert.Equal(graph.BeforeLate.CleanupEffects, graph.Final.CleanupEffects);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal((0, 0, 3, 2, 3, 3), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.SourceOwner.IsAlive);
        Assert.False(graph.SourceState.IsAlive);
        Assert.False(graph.Runtime.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ClosedStartLateGraph CreateClosedStartAdmissionLateCallback()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowLateEntry = new ManualResetEventSlim();
        var lateReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCalls = 0;
        int unavailableCalls = 0;
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartCompletedHook = () => Interlocked.Increment(ref completedCalls),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        Task<bool>? firstTask = null;
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
        {
            firstTask = capture.StartAsync().AsTask();
            startResult = firstTask.WaitAsync(Timeout).GetAwaiter().GetResult();
        });
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        nint knownExtra = runtime.CopyBlock(operations.StartBorrowedPointer);
        bool validKnownPointer = knownExtra != 0;
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() =>
            drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase beforeLate = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        int beforeCompleted = completedCalls;
        nint entryPointer = knownExtra;
        Exception? lateFailure = null;
        var late = new Thread(() =>
        {
            lateReady.TrySetResult();
            allowLateEntry.Wait();
            lateFailure = Record.Exception(() => runtime.Invoke(entryPointer, 1));
        })
        { IsBackground = true, Name = "Flowspan closed Start admission late ABI on known valid retain" };
        bool lateStarted = false;
        bool lateJoined = false;
        Exception? readyFailure = null;
        LateCallbackPhase? ready = null;
        LateCallbackPhase? afterLate = null;
        (bool, int, bool) readyResource = default;
        (bool, int, bool) lateResource = default;
        (bool, int, bool) readyStopResource = default;
        (bool, int, bool) lateStopResource = default;
        (bool, bool, bool, bool, bool, int, int) readyState = default;
        (bool, bool, bool, bool, bool, int, int) lateState = default;
        (nint, nint, nint, nint) readyPointers = default;
        int afterCompleted = 0;
        int retirementAttempts = 0;
        try
        {
            late.Start();
            lateStarted = true;
            readyFailure = Record.Exception(() => lateReady.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            ready = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            readyResource = ReadCompletionResourceUseFacts(startCompletion);
            readyStopResource = ReadCompletionResourceUseFacts(stopCompletion);
            readyState = ReadDuplicateStartState(capture, startCompletion, firstTask!);
            object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
            readyPointers = ((nint)Field("stream"), (nint)Field("output"), (nint)Field("queue"), (nint)Field("configuration"));
            allowLateEntry.Set();
            late.Join();
            lateJoined = true;
            afterLate = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            lateResource = ReadCompletionResourceUseFacts(startCompletion);
            lateStopResource = ReadCompletionResourceUseFacts(stopCompletion);
            lateState = ReadDuplicateStartState(capture, startCompletion, firstTask!);
            afterCompleted = completedCalls;
        }
        finally
        {
            // The known extra copy stays valid until this actual ABI joins.
            // Only that confirmed obligation gets one explicit retirement.
            allowLateEntry.Set();
            if (lateStarted && !lateJoined) { late.Join(); lateJoined = true; }
            nint retained = Interlocked.Exchange(ref knownExtra, 0);
            if (retained != 0)
            {
                retirementAttempts++;
                runtime.ReleaseBlock(retained);
            }
        }

        LateCallbackPhase afterRetirement = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var sourceOwner = new WeakReference(capture.GetType()
            .GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(startResult, startFailure, drainResult, drainFailure, firstDisposeFailure,
            readyFailure, lateJoined, lateFailure, validKnownPointer, beforeLate, ready!, afterLate!,
            readyResource, lateResource, readyStopResource, lateStopResource, readyState, lateState, readyPointers, beforeCompleted, afterCompleted,
            unavailableCalls, retirementAttempts, afterRetirement, finalDisposeFailure, repeatedDisposeFailure,
            final, repeated, operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, startPrimitive, stopCompletion, stopPrimitive, new(operations),
            sourceOwner, new(source.State), new(runtime), new(sampleMarker), new(unavailableMarker));
    }

    private sealed record ClosedStartLateGraph(bool StartResult, Exception? StartFailure, bool DrainResult,
        Exception? DrainFailure, Exception? FirstDisposeFailure, Exception? ReadyFailure,
        bool LateJoined, Exception? LateFailure, bool ValidKnownPointer,
        LateCallbackPhase BeforeLate, LateCallbackPhase Ready, LateCallbackPhase AfterLate,
        (bool Closed, int ActiveUsers, bool JoinCompleted) ReadyResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) LateResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) ReadyStopResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) LateStopResourceUse,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner, int DeliveryClosed, int UnavailableNotified) ReadyState,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner, int DeliveryClosed, int UnavailableNotified) LateState,
        (nint Stream, nint Output, nint Queue, nint Configuration) ReadyNativePointers,
        int BeforeCompletedCalls, int AfterCompletedCalls, int UnavailableCalls, int KnownCopyRetirementAttempts,
        LateCallbackPhase AfterRetirement, Exception? FinalDisposeFailure, Exception? RepeatedDisposeFailure,
        LateCallbackPhase Final, LateCallbackPhase Repeated, nint Stream, nint Output, nint Configuration,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference Operations,
        WeakReference SourceOwner, WeakReference SourceState, WeakReference Runtime,
        WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void DuplicateStartErrorWhileFirstCompletedIsActivePreservesSuccessAndDelivery()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        DuplicateStartGraph graph = CreateDuplicateStartErrorDuringFirstCompleted();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.True(graph.DuplicateJoined);
        Assert.Null(graph.DuplicateFailure);
        Assert.Equal((0, 0, 0), (graph.BeforeUnavailableCalls, graph.BeforeState.DeliveryClosed, graph.BeforeState.UnavailableNotified));
        // A later error on the same real ABI must not notify source loss or
        // close delivery after the first admitted successful Start result.
        Assert.Equal((0, 0, 0), (graph.AfterUnavailableCalls, graph.AfterState.DeliveryClosed, graph.AfterState.UnavailableNotified));
        Assert.Equal((true, false, false, true, true, 0, 0), graph.BeforeState);
        Assert.Equal(graph.BeforeState, graph.AfterState);
        Assert.True(graph.SameCachedTask);
        Assert.True(graph.CachedResult);
        Assert.Null(graph.CachedFailure);
        Assert.Equal((true, false, true, TaskStatus.RanToCompletion, true, true), graph.BeforeDuplicate.Start.Facts);
        Assert.Equal(graph.BeforeDuplicate.Start.Facts, graph.AfterDuplicate.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.AfterDuplicate.Stop.Facts);
        Assert.Equal((false, 1, false), graph.BeforeResourceUse);
        Assert.Equal(graph.BeforeResourceUse, graph.AfterResourceUse);
        Assert.Equal((1, 1, 0, 0, 1, 1, 1, 0), graph.BeforeDuplicate.RuntimeEffects);
        Assert.Equal((1, 1, 0, 0, 1, 1, 2, 1), graph.AfterDuplicate.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.AfterDuplicate.NativeEffects);
        Assert.Equal((2, 1, 0, 0, 0, 0, 0, 2, false), graph.AfterDuplicate.CleanupEffects);
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.AfterDuplicate.StartPrimitiveFacts);
        Assert.Null(graph.AfterDuplicate.StartPrimitiveFailure);
        Assert.Null(graph.AfterDuplicate.StopPrimitiveFacts);
        Assert.Empty(graph.AfterDuplicate.ObjectReleases);
        Assert.Equal(before + 1, graph.AfterDuplicate.ChargedOwners);
        Assert.True(graph.FirstStartJoined);
        Assert.True(graph.SameReturnedTask);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterStart.Start.Facts);
        Assert.Equal((true, false, true, true, true, 0, 0), graph.StartedState);
        Assert.Equal((1, 1, 0, 0, 1, 1, 2, 2), graph.AfterStart.RuntimeEffects);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.RepeatedDisposeFailure);
        Assert.Equal((2, 2, 2, 2, 0, 0, 3, 3), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((2, 0), (graph.CompletedHookCalls, graph.FinalUnavailableCalls));
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal((0, 0, 2, 2, 3, 3), graph.RawTeardownEffects);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.Runtime.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DuplicateStartGraph CreateDuplicateStartErrorDuringFirstCompleted()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowFirstCompletedReturn = new ManualResetEventSlim();
        var firstCompletedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedHookCalls = 0;
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartCompletedHook = () =>
            {
                if (Interlocked.Increment(ref completedHookCalls) != 1) { return; }
                firstCompletedEntered.TrySetResult();
                allowFirstCompletedReturn.Wait();
            },
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        Task<bool>? returnedTask = null;
        bool startResult = false;
        Exception? startFailure = null;
        var firstStart = new Thread(() => startFailure = Record.Exception(() =>
        {
            returnedTask = capture.StartAsync().AsTask();
            startResult = returnedTask.WaitAsync(Timeout).GetAwaiter().GetResult();
        }))
        { IsBackground = true, Name = "Flowspan first Start actual native handoff with completed held" };
        bool firstStarted = false;
        bool firstJoined = false;
        Thread? duplicate = null;
        bool duplicateStarted = false;
        bool duplicateJoined = false;
        Exception? entryFailure = null;
        Exception? duplicateFailure = null;
        Exception? cachedFailure = null;
        bool cachedResult = false;
        bool sameCachedTask = false;
        Task<bool>? originalResult = null;
        WeakReference? startCompletion = null;
        WeakReference? startPrimitive = null;
        LateCallbackPhase? beforeDuplicate = null;
        LateCallbackPhase? afterDuplicate = null;
        (bool, bool, bool, bool, bool, int, int) beforeState = default;
        (bool, bool, bool, bool, bool, int, int) afterState = default;
        (bool, int, bool) beforeResource = default;
        (bool, int, bool) afterResource = default;
        int beforeUnavailable = 0;
        int afterUnavailable = 0;
        try
        {
            firstStart.Start();
            firstStarted = true;
            entryFailure = Record.Exception(() => firstCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            startCompletion = operations.Completion!;
            startPrimitive = operations.Primitive!;
            originalResult = Assert.IsType<TaskCompletionSource<bool>>(capture.GetType()
                .GetField("startCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)).Task;
            beforeDuplicate = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            beforeState = ReadDuplicateStartState(capture, startCompletion, originalResult);
            beforeResource = ReadCompletionResourceUseFacts(startCompletion);
            beforeUnavailable = unavailableCalls;
            nint confirmedCallerPointer = operations.StartBorrowedPointer;
            duplicate = new Thread(() => duplicateFailure = Record.Exception(() => runtime.Invoke(confirmedCallerPointer, 1)))
            { IsBackground = true, Name = "Flowspan duplicate Start error actual one-argument ABI" };
            duplicate.Start();
            duplicateStarted = true;
            duplicateJoined = duplicate.Join(Timeout);
            cachedFailure = Record.Exception(() =>
            {
                Task<bool> cached = capture.StartAsync().AsTask();
                sameCachedTask = ReferenceEquals(originalResult, cached);
                cachedResult = cached.WaitAsync(Timeout).GetAwaiter().GetResult();
            });
            afterDuplicate = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            afterState = ReadDuplicateStartState(capture, startCompletion, originalResult);
            afterResource = ReadCompletionResourceUseFacts(startCompletion);
            afterUnavailable = unavailableCalls;
        }
        finally
        {
            allowFirstCompletedReturn.Set();
            if (duplicateStarted && !duplicateJoined) { duplicate!.Join(); }
            if (firstStarted) { firstStart.Join(); firstJoined = true; }
        }

        bool sameReturnedTask = ReferenceEquals(originalResult, returnedTask);
        LateCallbackPhase afterStart = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion!);
        var startedState = ReadDuplicateStartState(capture, startCompletion!, originalResult!);
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() =>
            drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion!);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion!);
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(entryFailure, duplicateJoined, duplicateFailure, beforeUnavailable, afterUnavailable,
            beforeDuplicate!, afterDuplicate!, beforeState, afterState, beforeResource, afterResource,
            sameCachedTask, cachedResult, cachedFailure, firstJoined, sameReturnedTask, startResult, startFailure,
            afterStart, startedState, drainResult, drainFailure, finalDisposeFailure, repeatedDisposeFailure,
            final, repeated, completedHookCalls, unavailableCalls, operations.Stream, operations.Output, operations.Configuration,
            pushes, pops, raw, new(capture), startCompletion!, startPrimitive!, stopCompletion, stopPrimitive,
            new(operations), new(source.State), new(runtime), new(sampleMarker), new(unavailableMarker));
    }

    private static (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner,
        int DeliveryClosed, int UnavailableNotified) ReadDuplicateStartState(
        IMacOSRemoteWindowNativeCapture capture, WeakReference completion, Task<bool> result)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        lock (Field("gate"))
        {
            return ((bool)Field("startSettled"), (bool)Field("unsafeFailure"), (bool)Field("startInvocationBorrowExited"),
                ReferenceEquals(result, Assert.IsType<TaskCompletionSource<bool>>(Field("startCompletion")).Task),
                ReferenceEquals(completion.Target, Field("startBlock")), (int)Field("deliveryClosed"), (int)Field("unavailableNotified"));
        }
    }

    private sealed record DuplicateStartGraph(Exception? EntryFailure, bool DuplicateJoined, Exception? DuplicateFailure,
        int BeforeUnavailableCalls, int AfterUnavailableCalls, LateCallbackPhase BeforeDuplicate, LateCallbackPhase AfterDuplicate,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner, int DeliveryClosed, int UnavailableNotified) BeforeState,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner, int DeliveryClosed, int UnavailableNotified) AfterState,
        (bool Closed, int ActiveUsers, bool JoinCompleted) BeforeResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) AfterResourceUse,
        bool SameCachedTask, bool CachedResult, Exception? CachedFailure, bool FirstStartJoined, bool SameReturnedTask,
        bool StartResult, Exception? StartFailure, LateCallbackPhase AfterStart,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner, int DeliveryClosed, int UnavailableNotified) StartedState,
        bool DrainResult, Exception? DrainFailure, Exception? FinalDisposeFailure, Exception? RepeatedDisposeFailure,
        LateCallbackPhase Final, LateCallbackPhase Repeated, int CompletedHookCalls, int FinalUnavailableCalls,
        nint Stream, nint Output, nint Configuration,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference Operations, WeakReference Source,
        WeakReference Runtime, WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void DuplicateStopErrorWhileFirstCompletedIsActivePreservesConfirmedStopAndCleanup()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        DuplicateStopGraph graph = CreateDuplicateStopErrorDuringFirstCompleted();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.Null(graph.EntryFailure);
        Assert.True(graph.DuplicateJoined);
        Assert.Null(graph.DuplicateFailure);
        Assert.True(graph.SameDrainTask);
        Assert.False(graph.HeldDrainCompleted);
        Assert.Equal((true, false, true, TaskStatus.RanToCompletion, true, true), graph.BeforeDuplicate.Stop.Facts);
        // A duplicate error must not regress the first admitted successful
        // result's independently confirmed Stop facts or poison cleanup.
        Assert.Equal((true, false, true, TaskStatus.RanToCompletion, true, true), graph.AfterDuplicate.Stop.Facts);
        Assert.Equal((true, false, false, true, true), graph.BeforeStopState);
        Assert.Equal(graph.BeforeStopState, graph.AfterStopState);
        Assert.Equal((false, 1, false), graph.BeforeResourceUse);
        Assert.Equal(graph.BeforeResourceUse, graph.AfterResourceUse);
        Assert.Equal((2, 2, 0, 0, 2, 2, 2, 1), graph.BeforeDuplicate.RuntimeEffects);
        Assert.Equal((2, 2, 0, 0, 2, 2, 3, 2), graph.AfterDuplicate.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.BeforeDuplicate.NativeEffects);
        Assert.Equal(graph.BeforeDuplicate.NativeEffects, graph.AfterDuplicate.NativeEffects);
        Assert.Equal((3, 2, 0, 0, 0, 0, 0, 2, false), graph.BeforeDuplicate.CleanupEffects);
        Assert.Equal(graph.BeforeDuplicate.CleanupEffects, graph.AfterDuplicate.CleanupEffects);
        Assert.Null(graph.BeforeDuplicate.StopPrimitiveFailure);
        Assert.Null(graph.AfterDuplicate.StopPrimitiveFailure);
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.AfterDuplicate.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Empty(graph.AfterDuplicate.ObjectReleases);
        Assert.Equal(before + 1, graph.AfterDuplicate.ChargedOwners);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Stop.Facts);
        Assert.Equal((true, false, true, true, true), graph.DrainedStopState);
        Assert.Equal((true, 0, true), graph.DrainedResourceUse);
        Assert.Equal((2, 2, 0, 0, 2, 2, 3, 3), graph.AfterDrain.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 0, 0, 0, 2, true), graph.AfterDrain.CleanupEffects);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.RepeatedDisposeFailure);
        Assert.Equal((2, 2, 2, 2, 0, 0, 3, 3), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal((2, 0), (graph.CompletedHookCalls, graph.UnavailableCalls));
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal((0, 0, 2, 2, 3, 3), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.Runtime.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DuplicateStopGraph CreateDuplicateStopErrorDuringFirstCompleted()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowFirstCompletedReturn = new ManualResetEventSlim();
        var firstCompletedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedHookCalls = 0;
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StopCompletedHook = () =>
            {
                if (Interlocked.Increment(ref completedHookCalls) != 1) { return; }
                firstCompletedEntered.TrySetResult();
                allowFirstCompletedReturn.Wait();
            },
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Task<bool> drain = capture.StopAndDrainAsync().AsTask();
        Exception? entryFailure = null;
        Exception? duplicateFailure = null;
        Thread? duplicate = null;
        bool duplicateStarted = false;
        bool duplicateJoined = false;
        bool sameDrainTask = false;
        bool heldDrainCompleted = false;
        WeakReference? stopCompletion = null;
        WeakReference? stopPrimitive = null;
        Task<bool>? stopResult = null;
        LateCallbackPhase? beforeDuplicate = null;
        LateCallbackPhase? afterDuplicate = null;
        (bool, bool, bool, bool, bool) beforeStopState = default;
        (bool, bool, bool, bool, bool) afterStopState = default;
        (bool, int, bool) beforeResourceUse = default;
        (bool, int, bool) afterResourceUse = default;
        try
        {
            entryFailure = Record.Exception(() => firstCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            stopCompletion = operations.Completion!;
            stopPrimitive = operations.Primitive!;
            stopResult = Assert.IsType<TaskCompletionSource<bool>>(capture.GetType()
                .GetField("stopCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)).Task;
            nint confirmedCallerPointer = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopCompletion.Target).Pointer;
            beforeDuplicate = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            beforeStopState = ReadDuplicateStopState(capture, stopCompletion, stopResult);
            beforeResourceUse = ReadCompletionResourceUseFacts(stopCompletion);
            // The known caller +1 remains borrowed by the held first native
            // handoff. No extra copy, fake completion or raw field reset.
            duplicate = new Thread(() => duplicateFailure = Record.Exception(() => runtime.Invoke(confirmedCallerPointer, 1)))
            { IsBackground = true, Name = "Flowspan duplicate Stop error actual one-argument ABI" };
            duplicate.Start();
            duplicateStarted = true;
            duplicateJoined = duplicate.Join(Timeout);
            afterDuplicate = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            afterStopState = ReadDuplicateStopState(capture, stopCompletion, stopResult);
            afterResourceUse = ReadCompletionResourceUseFacts(stopCompletion);
            sameDrainTask = ReferenceEquals(drain, capture.StopAndDrainAsync().AsTask());
            heldDrainCompleted = drain.IsCompleted;
        }
        finally
        {
            allowFirstCompletedReturn.Set();
            if (duplicateStarted && !duplicateJoined) { duplicate!.Join(); }
        }

        bool drainResult = false;
        // Awaiting this original actual drain is the first thread-pool native
        // invocation's join; a completed TCS or another ABI return cannot replace it.
        Exception? drainFailure = Record.Exception(() => drainResult = drain.WaitAsync(Timeout).GetAwaiter().GetResult());
        LateCallbackPhase afterDrain = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var drainedState = ReadDuplicateStopState(capture, stopCompletion!, stopResult!);
        var drainedResourceUse = ReadCompletionResourceUseFacts(stopCompletion!);
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(startResult, startFailure, entryFailure, duplicateJoined, duplicateFailure,
            sameDrainTask, heldDrainCompleted, beforeDuplicate!, afterDuplicate!, beforeStopState, afterStopState,
            beforeResourceUse, afterResourceUse, drainResult, drainFailure, afterDrain, drainedState, drainedResourceUse,
            finalDisposeFailure, repeatedDisposeFailure, final, repeated, completedHookCalls, unavailableCalls,
            operations.Stream, operations.Output, operations.Configuration, pushes, pops, raw,
            new(capture), startCompletion, startPrimitive, stopCompletion!, stopPrimitive!,
            new(operations), new(source.State), new(runtime), new(sampleMarker), new(unavailableMarker));
    }

    private static (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner)
        ReadDuplicateStopState(IMacOSRemoteWindowNativeCapture capture, WeakReference completion, Task<bool> result)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        lock (Field("gate"))
        {
            return ((bool)Field("stopSettled"), (bool)Field("unsafeFailure"), (bool)Field("stopInvocationBorrowExited"),
                ReferenceEquals(result, Assert.IsType<TaskCompletionSource<bool>>(Field("stopCompletion")).Task),
                ReferenceEquals(completion.Target, Field("stopBlock")));
        }
    }

    private sealed record DuplicateStopGraph(bool StartResult, Exception? StartFailure, Exception? EntryFailure,
        bool DuplicateJoined, Exception? DuplicateFailure, bool SameDrainTask, bool HeldDrainCompleted,
        LateCallbackPhase BeforeDuplicate, LateCallbackPhase AfterDuplicate,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner) BeforeStopState,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner) AfterStopState,
        (bool Closed, int ActiveUsers, bool JoinCompleted) BeforeResourceUse,
        (bool Closed, int ActiveUsers, bool JoinCompleted) AfterResourceUse,
        bool DrainResult, Exception? DrainFailure, LateCallbackPhase AfterDrain,
        (bool Settled, bool Unsafe, bool BorrowExited, bool SameResultTask, bool SameOwner) DrainedStopState,
        (bool Closed, int ActiveUsers, bool JoinCompleted) DrainedResourceUse,
        Exception? FinalDisposeFailure, Exception? RepeatedDisposeFailure, LateCallbackPhase Final, LateCallbackPhase Repeated,
        int CompletedHookCalls, int UnavailableCalls, nint Stream, nint Output, nint Configuration,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference Operations, WeakReference Source,
        WeakReference Runtime, WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void ConstructorSuccessfulPublicationThenPoolPopFatalRetainsExactShellAfterRollbackAndSlotReplacement()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        PublishedConstructorPoolGraph graph = CreatePublishedConstructorPoolPopFailure();
        // The helper has left the actual joined rollback's entire strong graph.
        // Replace the real ThreadStatic handoff slot on this same test thread.
        bool replaced = ReplaceConstructorFailureSlot();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.False(graph.FactoryReturned);
        Assert.Same(graph.Fatal, graph.FactoryFailure);
        Assert.True(graph.SameRollbackTask);
        Assert.True(graph.SameRepeatedRollbackTask);
        Assert.Equal(TaskStatus.RanToCompletion, graph.RollbackStatus);
        Assert.False(graph.RollbackResult);
        Assert.Null(graph.RollbackFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, false), graph.ConstructorPoolFacts);
        Assert.Equal((true, true, true, true), graph.RemovalPoolFacts);
        Assert.Equal((false, false, false, false), graph.StartPoolFacts);
        Assert.Equal((false, false, false, false), graph.StopPoolFacts);
        Assert.Equal((false, false, true, TaskStatus.WaitingForActivation, null, false), graph.AfterRollback.Start.Facts);
        Assert.Equal((false, false, true, TaskStatus.WaitingForActivation, null, false), graph.AfterRollback.Stop.Facts);
        Assert.Null(graph.AfterRollback.Start.ResultFailure);
        Assert.Null(graph.AfterRollback.Stop.ResultFailure);
        Assert.Equal((7, 1, 1, 2, 2, 2, 1, 1, 3, 1, 1, 1, true), graph.AfterRollback.Effects);
        Assert.Equal((0, 0, 0, 0, 0), graph.AfterRollback.NativeEffects);
        Assert.Equal((0, 0, 0, 0, 0, 0, 0, 0), graph.AfterRollback.RuntimeEffects);
        Assert.Equal((true, true, true, true, true, true), graph.AfterRollback.KnownCleanupFacts);
        Assert.Equal((0, 0, 0, 0), graph.AfterRollback.NativePointers);
        Assert.Equal((true, true, true, true, true, true, true, false, 1, true, false, true), graph.AfterRollback.ShellFacts);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.AfterRollback.ObjectReleases);
        Assert.Equal(before + 1, graph.AfterRollback.ChargedOwners);
        Assert.Equal(graph.AfterRollback with { ObjectReleases = graph.AfterFirstDispose.ObjectReleases }, graph.AfterFirstDispose);
        Assert.Equal(graph.AfterRollback.ObjectReleases, graph.AfterFirstDispose.ObjectReleases);
        Assert.Equal(graph.AfterRollback with { ObjectReleases = graph.AfterRepeatedDispose.ObjectReleases }, graph.AfterRepeatedDispose);
        Assert.Equal(graph.AfterRollback.ObjectReleases, graph.AfterRepeatedDispose.ObjectReleases);
        Assert.Equal(2, graph.PushEffects.Length);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal((1, (nint)1, graph.CreatorThread), graph.PushEffects[0]);
        Assert.Equal(2, graph.PushEffects[1].Ordinal);
        Assert.Equal(1, graph.PushEffects[1].Token);
        Assert.Equal((0, 0, 0, 0, 0, 0), graph.RawTeardownEffects);
        Assert.True(replaced);
        Assert.Equal(before + 2, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.True(graph.Capture.IsAlive);
        Assert.True(graph.Operations.IsAlive);
        Assert.True(graph.SourceOwner.IsAlive);
        Assert.True(graph.SourceState.IsAlive);
        Assert.True(graph.Runtime.IsAlive);
        Assert.True(graph.SampleMarker.IsAlive);
        Assert.True(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests a first nested fatal after successful constructor publication and its actual async rollback.")]
    private static PublishedConstructorPoolGraph CreatePublishedConstructorPoolPopFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("First constructor pool pop fatal after successful publication.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            PopFailureOrdinal = 1,
            PopFailureAfterEffect = new AggregateException(new IOException("Published constructor pop wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture? returned = null;
        Exception? factoryFailure = Record.Exception(() => returned = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker)));
        WeakReference observation = Assert.IsType<WeakReference>(ObserveFactorySlot(operations));
        var capture = Assert.IsAssignableFrom<IMacOSRemoteWindowNativeCapture>(observation.Target);
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        var originalRollback = Assert.IsAssignableFrom<Task<bool>>(Field("drain"));
        Task<bool> drain = capture.StopAndDrainAsync().AsTask();
        bool sameRollbackTask = ReferenceEquals(originalRollback, drain);
        bool rollbackResult = true;
        // This joins the real cached rollback; physical IsDrained is observed
        // afterward, never used as a substitute for awaiting this task.
        Exception? rollbackFailure = Record.Exception(() =>
            rollbackResult = drain.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool sameRepeatedRollbackTask = ReferenceEquals(originalRollback, capture.StopAndDrainAsync().AsTask());
        PublishedConstructorPhase afterRollback = ReadPublishedConstructorPhase(capture, operations, runtime, source.State, fatal);
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        PublishedConstructorPhase afterFirst = ReadPublishedConstructorPhase(capture, operations, runtime, source.State, fatal);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        PublishedConstructorPhase afterRepeated = ReadPublishedConstructorPhase(capture, operations, runtime, source.State, fatal);
        var constructorFacts = ReadUnknownPoolFacts(capture, "constructor");
        var removalFacts = ReadUnknownPoolFacts(capture, "remove");
        var startFacts = ReadUnknownPoolFacts(capture, "start");
        var stopFacts = ReadUnknownPoolFacts(capture, "stop");
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        var sourceOwner = new WeakReference(Field("source"));
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(observation, new(operations), sourceOwner, new(source.State), new(runtime),
            new(sampleMarker), new(unavailableMarker), fatal, returned is not null, factoryFailure,
            sameRollbackTask, sameRepeatedRollbackTask, originalRollback.Status, rollbackResult, rollbackFailure,
            firstDisposeFailure, repeatedDisposeFailure, afterRollback, afterFirst, afterRepeated,
            constructorFacts, removalFacts, startFacts, stopFacts, operations.Stream, operations.Output,
            operations.Configuration, Environment.CurrentManagedThreadId, pushes, pops, raw);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PublishedConstructorPhase ReadPublishedConstructorPhase(IMacOSRemoteWindowNativeCapture capture,
        CompletionOwnershipOperations operations, MacOSRemoteWindowControlledBlockRuntime runtime,
        SourceState source, OutOfMemoryException fatal)
    {
        object? Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture);
        lock (Field("gate")!)
        {
            return new(ReadCaptureCompletionPhase(capture, "start"), ReadCaptureCompletionPhase(capture, "stop"),
                MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                (operations.ConstructorScopedBodyCalls, operations.ConfigureCalls, operations.AddOutputCalls,
                    operations.PoolsPushed, operations.PoolsPopped, operations.PoolPopAttempts,
                    operations.RemoveCalls, operations.DrainCalls, operations.ObjectReleaseAttempts.Count,
                    operations.QueueReleaseAttempts, source.RetainedReleaseAttempts, source.OwnerCount, capture.IsDrained),
                (operations.StartSelectorAttempts, operations.StopSelectorAttempts, operations.NativeInvocationAttempts,
                    operations.StartInvocations, operations.StopInvocations),
                (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                    runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns),
                ((bool)Field("streamReleaseAttempted")!, (bool)Field("outputReleaseAttempted")!,
                    (bool)Field("configurationReleaseAttempted")!, (bool)Field("queueReleaseAttempted")!,
                    (bool)Field("sourceReleaseAttempted")!, (bool)Field("sourceReleaseConfirmed")!),
                ((nint)Field("stream")!, (nint)Field("output")!, (nint)Field("queue")!, (nint)Field("configuration")!),
                (ReferenceEquals(operations, Field("operations")), ReferenceEquals(fatal, Field("fatalFailure")),
                    ReferenceEquals(fatal, Field("factoryFailure")), (bool)Field("rootCounted")!,
                    ((GCHandle)Field("callbackRoot")!).IsAllocated, (bool)Field("outputRemoved")!,
                    (bool)Field("queueDrained")!, (bool)Field("startRequested")!, (int)Field("deliveryClosed")!,
                    (bool)Field("unsafeFailure")!, (bool)Field("disposed")!,
                    Field("startBlock") is null && Field("stopBlock") is null && operations.Completion is null && operations.Primitive is null),
                operations.ObjectReleaseAttempts.ToArray());
        }
    }

    private sealed record PublishedConstructorPhase(CaptureCompletionPhase Start, CaptureCompletionPhase Stop,
        int ChargedOwners,
        (int ScopedBody, int Configure, int Adds, int Pushes, int Pops, int PopAttempts, int Removes, int Barriers,
            int ObjectsReleased, int QueuesReleased, int SourceReleased, int SourceOwners, bool PhysicalDrained) Effects,
        (int StartSelectors, int StopSelectors, int NativeAttempts, int Starts, int Stops) NativeEffects,
        (int Roots, int Copies, int Releases, int RootFrees, int LiveRoots, int LiveBlocks, int Invokes, int Returns) RuntimeEffects,
        (bool Stream, bool Output, bool Configuration, bool Queue, bool SourceAttempted, bool SourceConfirmed) KnownCleanupFacts,
        (nint Stream, nint Output, nint Queue, nint Configuration) NativePointers,
        (bool SameOperations, bool SameFatal, bool SameFactoryFatal, bool RootCounted, bool RootAllocated,
            bool OutputRemoved, bool QueueDrained, bool StartRequested, int DeliveryClosed, bool Unsafe,
            bool Disposed, bool NoCompletionOwners) ShellFacts, nint[] ObjectReleases);

    private sealed record PublishedConstructorPoolGraph(WeakReference Capture, WeakReference Operations,
        WeakReference SourceOwner, WeakReference SourceState, WeakReference Runtime,
        WeakReference SampleMarker, WeakReference UnavailableMarker, OutOfMemoryException Fatal,
        bool FactoryReturned, Exception? FactoryFailure, bool SameRollbackTask, bool SameRepeatedRollbackTask,
        TaskStatus RollbackStatus, bool RollbackResult, Exception? RollbackFailure,
        Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure,
        PublishedConstructorPhase AfterRollback, PublishedConstructorPhase AfterFirstDispose, PublishedConstructorPhase AfterRepeatedDispose,
        (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed) ConstructorPoolFacts,
        (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed) RemovalPoolFacts,
        (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed) StartPoolFacts,
        (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed) StopPoolFacts,
        nint Stream, nint Output, nint Configuration, int CreatorThread,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects);

    [Fact]
    public void StartCopyInFlightStopAndDisposePreserveExactOwnerAndOrderShutdown()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        CopyInFlightGraph observation = CreateStartCopyInFlightWithShutdownRequested();
        ActiveCompletedGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.IsType<TimeoutException>(observation.PendingDrainWatchdogFailure);
        Assert.False(observation.PendingDrainCompleted);
        Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.True(observation.PendingSameOwner);
        Assert.Equal(0, observation.PendingPointer);
        Assert.Equal((true, 1, false), observation.PendingAdmissionFacts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((false, 0, false), graph.PendingResourceUseFacts);
        Assert.Equal((true, true, true, true, false, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Null(graph.Pending.StartPrimitiveFailure);
        Assert.Null(graph.Pending.StopPrimitiveFacts);
        Assert.Equal((1, 1, 0, 0, 1, 1, 0, 0), graph.Pending.RuntimeEffects);
        Assert.Equal((0, 0, 0, 0, 0), graph.Pending.NativeEffects);
        Assert.Equal((1, 1, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal(before + 1, graph.Pending.ChargedOwners);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.True(observation.FinalSameOwner);
        Assert.Equal((true, 1, true), observation.FinalAdmissionFacts);
        Assert.Equal((2, 0), (observation.CopyHookCalls, observation.UnavailableCalls));
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Null(graph.Final.Start.ResultFailure);
        Assert.Null(graph.Final.Stop.ResultFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CopyInFlightGraph CreateStartCopyInFlightWithShutdownRequested()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowCopyReturn = new ManualResetEventSlim();
        var copyEffectReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int copyHookCalls = 0;
        runtime.AfterCopyCapture = () =>
        {
            // The actual heap and production copy-helper effects exist, but
            // CopyBlock has not returned or authorized a publishable pointer.
            if (Interlocked.Increment(ref copyHookCalls) != 1) { return; }
            copyEffectReached.TrySetResult();
            allowCopyReturn.Wait();
        };
        var operations = new CompletionOwnershipOperations(runtime);
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        bool startResult = false;
        Exception? startFailure = null;
        var startThread = new Thread(() => startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()))
        { IsBackground = true, Name = "Flowspan Start actual copy effect held before acquisition confirmation" };
        bool startThreadStarted = false;
        bool startThreadJoined = false;
        Exception? entryFailure = null;
        Exception? pendingDisposeFailure = null;
        Exception? pendingDrainWatchdogFailure = null;
        bool pendingDrainCompleted = false;
        Task<bool>? drain = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        (bool, int, bool) pendingAdmission = default;
        bool pendingSameOwner = false;
        nint pendingPointer = 0;
        WeakReference? startCompletion = null;
        try
        {
            startThread.Start();
            startThreadStarted = true;
            entryFailure = Record.Exception(() => copyEffectReached.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            startCompletion = operations.Completion!;
            drain = capture.StopAndDrainAsync().AsTask();
            // An external watchdog observes pending only; it neither cancels
            // the original drain nor supplies a fake successful completion.
            pendingDrainWatchdogFailure = Record.Exception(() => drain.WaitAsync(Timeout).GetAwaiter().GetResult());
            pendingDrainCompleted = drain.IsCompleted;
            pendingDisposeFailure = Record.Exception(capture.Dispose);
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
            (pendingSameOwner, pendingAdmission) = ReadCopyInFlightCaptureFacts(capture, startCompletion);
            pendingPointer = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target).Pointer;
        }
        finally
        {
            allowCopyReturn.Set();
            if (startThreadStarted) { startThread.Join(); startThreadJoined = true; }
        }

        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = drain!.WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion!);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion!);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion!);
        var (finalSameOwner, finalAdmission) = ReadCopyInFlightCaptureFacts(capture, startCompletion!);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(entryFailure, pendingDisposeFailure, pending!, pendingRelease, pendingResourceUse,
            startThreadJoined, null, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion!, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker)),
            pendingDrainWatchdogFailure, pendingDrainCompleted, pendingSameOwner, pendingPointer, pendingAdmission,
            finalSameOwner, finalAdmission, copyHookCalls, unavailableCalls);
    }

    private static (bool SameOwner, (bool Requested, int DeliveryClosed, bool Disposed) Admission)
        ReadCopyInFlightCaptureFacts(IMacOSRemoteWindowNativeCapture capture, WeakReference completion)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        lock (Field("gate"))
        {
            return (ReferenceEquals(Field("startBlock"), completion.Target),
                ((bool)Field("startRequested"), (int)Field("deliveryClosed"), (bool)Field("disposed")));
        }
    }

    private sealed record CopyInFlightGraph(ActiveCompletedGraph Graph, Exception? PendingDrainWatchdogFailure,
        bool PendingDrainCompleted, bool PendingSameOwner, nint PendingPointer,
        (bool Requested, int DeliveryClosed, bool Disposed) PendingAdmissionFacts,
        bool FinalSameOwner, (bool Requested, int DeliveryClosed, bool Disposed) FinalAdmissionFacts,
        int CopyHookCalls, int UnavailableCalls);

    [Fact]
    public void StopFailureObserverFatalPreservesEarlierActionFatalAndCleansKnownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopFailureFaultGraph observation = CreateStopFailureObserverFatalAfterActionFailure();
        StopCompletedFaultGraph graph = observation.Action.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.NotSame(graph.Fatal, observation.LaterFatal);
        Assert.Equal((1, 1, 0, 0), (observation.Action.ActionThrowAttempts, observation.FailureThrowAttempts,
            graph.CompletedThrowAttempts, graph.UnavailableCalls));
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Stop.Facts);
        Assert.Null(graph.AfterDrain.Start.ResultFailure);
        Assert.Null(graph.AfterDrain.Stop.ResultFailure);
        Assert.Null(graph.AfterDrain.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.AfterDrain.StopPrimitiveFailure);
        Assert.Equal((2, 2, 0, 0, 2, 2, 2, 2), graph.AfterDrain.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.AfterDrain.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 0, 0, 0, 2, true), graph.AfterDrain.CleanupEffects);
        Assert.Equal(before + 1, graph.AfterDrain.ChargedOwners);
        Assert.All(graph.PoolFacts, facts => Assert.Equal((true, true, true, true), facts));
        Assert.Equal(4, graph.PoolFacts.Length);
        Assert.Equal(graph.AfterDrain.Start.Facts, graph.Final.Start.Facts);
        Assert.Equal(graph.AfterDrain.Stop.Facts, graph.Final.Stop.Facts);
        Assert.Null(graph.Final.Start.ResultFailure);
        Assert.Null(graph.Final.Stop.ResultFailure);
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.Final.StopPrimitiveFailure);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(graph.Final.Start.Facts, graph.Repeated.Start.Facts);
        Assert.Equal(graph.Final.Stop.Facts, graph.Repeated.Stop.Facts);
        Assert.Null(graph.Repeated.Start.ResultFailure);
        Assert.Null(graph.Repeated.Stop.ResultFailure);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal(graph.Final.StartPrimitiveFacts, graph.Repeated.StartPrimitiveFacts);
        Assert.Equal(graph.Final.StopPrimitiveFacts, graph.Repeated.StopPrimitiveFacts);
        Assert.Equal(graph.Final.ObjectReleases, graph.Repeated.ObjectReleases);
        Assert.Null(graph.Repeated.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.Repeated.StopPrimitiveFailure);
        Assert.Equal(before, graph.Repeated.ChargedOwners);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        // A contained managed fatal stays reportable without inventing unknown
        // ownership debt after known cleanup and both terminal joins confirm.
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests a later guarded Stop failure-wrapper fatal after the actual Capture failure callback has recorded the earlier action fatal.")]
    private static StopFailureFaultGraph CreateStopFailureObserverFatalAfterActionFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original actual Stop action fatal A after successful Capture result.");
        var laterFatal = new OutOfMemoryException("Later actual Stop failure-observer fatal B.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StopActionFailureAfterEffect = new AggregateException(new IOException("Actual Stop action wrapper.", fatal)),
            StopFailureObserverFailureAfterEffect = new AggregateException(new IOException("Actual Stop failure-observer wrapper.", laterFatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? drainFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        LateCallbackPhase afterDrain = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        (bool, bool, bool, bool)[] poolFacts =
        [
            ReadUnknownPoolFacts(capture, "constructor"), ReadUnknownPoolFacts(capture, "start"),
            ReadUnknownPoolFacts(capture, "stop"), ReadUnknownPoolFacts(capture, "remove"),
        ];
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(new(fatal, startResult, startFailure, drainFailure, firstDisposeFailure, repeatedDisposeFailure,
            operations.StopCompletedThrowAttempts, unavailableCalls, afterDrain, final, repeated, poolFacts,
            operations.Stream, operations.Output, operations.Configuration, raw, new(capture), startCompletion,
            startPrimitive, stopCompletion, stopPrimitive, new(operations), new(source.State),
            new(sampleMarker), new(unavailableMarker)), operations.StopActionThrowAttempts), laterFatal,
            operations.StopFailureObserverThrowAttempts);
    }

    private sealed record StopFailureFaultGraph(StopActionFaultGraph Action, OutOfMemoryException LaterFatal,
        int FailureThrowAttempts);

    [Fact]
    public void StopActionFatalPreservesSuccessfulStopAndCleansKnownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopActionFaultGraph observation = CreateStopActionFatalAfterSuccessfulStop();
        StopCompletedFaultGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.Equal((1, 0, 0), (observation.ActionThrowAttempts, graph.CompletedThrowAttempts, graph.UnavailableCalls));
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Stop.Facts);
        Assert.Null(graph.AfterDrain.Start.ResultFailure);
        Assert.Null(graph.AfterDrain.Stop.ResultFailure);
        Assert.Null(graph.AfterDrain.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.AfterDrain.StopPrimitiveFailure);
        Assert.Equal((2, 2, 0, 0, 2, 2, 2, 2), graph.AfterDrain.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.AfterDrain.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 0, 0, 0, 2, true), graph.AfterDrain.CleanupEffects);
        Assert.Equal(before + 1, graph.AfterDrain.ChargedOwners);
        Assert.All(graph.PoolFacts, facts => Assert.Equal((true, true, true, true), facts));
        Assert.Equal(4, graph.PoolFacts.Length);
        Assert.Equal(graph.AfterDrain.Start.Facts, graph.Final.Start.Facts);
        Assert.Equal(graph.AfterDrain.Stop.Facts, graph.Final.Stop.Facts);
        Assert.Null(graph.Final.Start.ResultFailure);
        Assert.Null(graph.Final.Stop.ResultFailure);
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.Final.StopPrimitiveFailure);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(graph.Final.Start.Facts, graph.Repeated.Start.Facts);
        Assert.Equal(graph.Final.Stop.Facts, graph.Repeated.Stop.Facts);
        Assert.Null(graph.Repeated.Start.ResultFailure);
        Assert.Null(graph.Repeated.Stop.ResultFailure);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal(graph.Final.StartPrimitiveFacts, graph.Repeated.StartPrimitiveFacts);
        Assert.Equal(graph.Final.StopPrimitiveFacts, graph.Repeated.StopPrimitiveFacts);
        Assert.Equal(graph.Final.ObjectReleases, graph.Repeated.ObjectReleases);
        Assert.Null(graph.Repeated.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.Repeated.StopPrimitiveFailure);
        Assert.Equal(before, graph.Repeated.ChargedOwners);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        // A contained managed fatal stays reportable without inventing unknown
        // ownership debt after known cleanup and both terminal joins confirm.
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a nested fatal after the actual successful Stop action within the guarded action region.")]
    private static StopActionFaultGraph CreateStopActionFatalAfterSuccessfulStop()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original actual Stop action fatal after successful Capture result.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StopActionFailureAfterEffect = new AggregateException(new IOException("Actual Stop action wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? drainFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        LateCallbackPhase afterDrain = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        (bool, bool, bool, bool)[] poolFacts =
        [
            ReadUnknownPoolFacts(capture, "constructor"), ReadUnknownPoolFacts(capture, "start"),
            ReadUnknownPoolFacts(capture, "stop"), ReadUnknownPoolFacts(capture, "remove"),
        ];
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(fatal, startResult, startFailure, drainFailure, firstDisposeFailure, repeatedDisposeFailure,
            operations.StopCompletedThrowAttempts, unavailableCalls, afterDrain, final, repeated, poolFacts,
            operations.Stream, operations.Output, operations.Configuration, raw, new(capture), startCompletion,
            startPrimitive, stopCompletion, stopPrimitive, new(operations), new(source.State),
            new(sampleMarker), new(unavailableMarker)), operations.StopActionThrowAttempts);
    }

    private sealed record StopActionFaultGraph(StopCompletedFaultGraph Graph, int ActionThrowAttempts);

    [Fact]
    public void StopCompletedNotificationFatalPreservesSuccessfulStopAndCleansKnownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopCompletedFaultGraph graph = CreateStopCompletedFatalAfterSuccessfulStop();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.Equal((1, 0), (graph.CompletedThrowAttempts, graph.UnavailableCalls));
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.AfterDrain.Stop.Facts);
        Assert.Null(graph.AfterDrain.Start.ResultFailure);
        Assert.Null(graph.AfterDrain.Stop.ResultFailure);
        Assert.Null(graph.AfterDrain.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.AfterDrain.StopPrimitiveFailure);
        Assert.Equal((2, 2, 0, 0, 2, 2, 2, 2), graph.AfterDrain.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.AfterDrain.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 0, 0, 0, 2, true), graph.AfterDrain.CleanupEffects);
        Assert.Equal(before + 1, graph.AfterDrain.ChargedOwners);
        Assert.All(graph.PoolFacts, facts => Assert.Equal((true, true, true, true), facts));
        Assert.Equal(4, graph.PoolFacts.Length);
        Assert.Equal(graph.AfterDrain.Start.Facts, graph.Final.Start.Facts);
        Assert.Equal(graph.AfterDrain.Stop.Facts, graph.Final.Stop.Facts);
        Assert.Null(graph.Final.Start.ResultFailure);
        Assert.Null(graph.Final.Stop.ResultFailure);
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.Final.StopPrimitiveFailure);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(graph.Final.Start.Facts, graph.Repeated.Start.Facts);
        Assert.Equal(graph.Final.Stop.Facts, graph.Repeated.Stop.Facts);
        Assert.Null(graph.Repeated.Start.ResultFailure);
        Assert.Null(graph.Repeated.Stop.ResultFailure);
        Assert.Equal(graph.Final.RuntimeEffects, graph.Repeated.RuntimeEffects);
        Assert.Equal(graph.Final.NativeEffects, graph.Repeated.NativeEffects);
        Assert.Equal(graph.Final.CleanupEffects, graph.Repeated.CleanupEffects);
        Assert.Equal(graph.Final.StartPrimitiveFacts, graph.Repeated.StartPrimitiveFacts);
        Assert.Equal(graph.Final.StopPrimitiveFacts, graph.Repeated.StopPrimitiveFacts);
        Assert.Equal(graph.Final.ObjectReleases, graph.Repeated.ObjectReleases);
        Assert.Null(graph.Repeated.StartPrimitiveFailure);
        Assert.Same(graph.Fatal, graph.Repeated.StopPrimitiveFailure);
        Assert.Equal(before, graph.Repeated.ChargedOwners);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        // A contained managed fatal stays reportable without inventing unknown
        // ownership debt after known cleanup and both terminal joins confirm.
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.StartPrimitive.IsAlive);
        Assert.False(graph.StopCompletion.IsAlive);
        Assert.False(graph.StopPrimitive.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects a nested fatal after actual Stop completed/exit notification within the guarded observer region.")]
    private static StopCompletedFaultGraph CreateStopCompletedFatalAfterSuccessfulStop()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original actual Stop completed-notification fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StopCompletedFailureAfterEffect = new AggregateException(new IOException("Actual Stop completed-notification wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? drainFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        LateCallbackPhase afterDrain = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        (bool, bool, bool, bool)[] poolFacts =
        [
            ReadUnknownPoolFacts(capture, "constructor"), ReadUnknownPoolFacts(capture, "start"),
            ReadUnknownPoolFacts(capture, "stop"), ReadUnknownPoolFacts(capture, "remove"),
        ];
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(fatal, startResult, startFailure, drainFailure, firstDisposeFailure, repeatedDisposeFailure,
            operations.StopCompletedThrowAttempts, unavailableCalls, afterDrain, final, repeated, poolFacts,
            operations.Stream, operations.Output, operations.Configuration, raw, new(capture), startCompletion,
            startPrimitive, stopCompletion, stopPrimitive, new(operations), new(source.State),
            new(sampleMarker), new(unavailableMarker));
    }

    private sealed record StopCompletedFaultGraph(OutOfMemoryException Fatal, bool StartResult,
        Exception? StartFailure, Exception? DrainFailure, Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure,
        int CompletedThrowAttempts, int UnavailableCalls, LateCallbackPhase AfterDrain,
        LateCallbackPhase Final, LateCallbackPhase Repeated, (bool, bool, bool, bool)[] PoolFacts,
        nint Stream, nint Output, nint Configuration,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void SourceUnavailableObserverFatalPreservesEarlierCopyFatalAndUnknownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        UnavailableObserverGraph graph = CreateUnavailableObserverFatalAfterCopyFailure();
        CompletionOwnershipGraph ownership = graph.Ownership;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.NotSame(ownership.Fatal, graph.ObserverFatal);
        Assert.Same(ownership.Fatal, graph.StartFailure);
        Assert.Same(ownership.Fatal, graph.DrainFailure);
        Assert.Same(ownership.Fatal, ownership.FirstDisposeFailure);
        Assert.Same(ownership.Fatal, ownership.RepeatedDisposeFailure);
        Assert.Equal((1, 1), graph.ObserverEffects);
        Assert.Equal((1, 1, 0, 0, 1, 1, 0, 0), graph.FirstCleanup.RuntimeEffects);
        Assert.Equal((0, 0, 0, 0, 0), graph.FirstCleanup.NativeEffects);
        Assert.Equal((2, 2, 1, 1, 3, 1, 1, 1, true), graph.FirstCleanup.CleanupEffects);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.FirstCleanup.ObjectReleases);
        Assert.Equal((true, true, true, true, false, false, false, false, false, false, 1L),
            graph.FirstCleanup.StartPrimitiveFacts);
        Assert.Null(graph.FirstCleanup.StopPrimitiveFacts);
        Assert.Same(ownership.Fatal, graph.FirstCleanup.StartPrimitiveFailure);
        Assert.Equal(before + 1, graph.FirstCleanup.ChargedOwners);
        Assert.Equal(graph.FirstCleanup.RuntimeEffects, graph.RepeatedCleanup.RuntimeEffects);
        Assert.Equal(graph.FirstCleanup.NativeEffects, graph.RepeatedCleanup.NativeEffects);
        Assert.Equal(graph.FirstCleanup.CleanupEffects, graph.RepeatedCleanup.CleanupEffects);
        Assert.Equal(graph.FirstCleanup.ObjectReleases, graph.RepeatedCleanup.ObjectReleases);
        Assert.Equal(graph.FirstCleanup.StartPrimitiveFacts, graph.RepeatedCleanup.StartPrimitiveFacts);
        Assert.Same(ownership.Fatal, graph.RepeatedCleanup.StartPrimitiveFailure);
        Assert.Equal(before + 1, graph.RepeatedCleanup.ChargedOwners);
        Assert.Equal((0, 0), graph.PointerObservations);
        // Raw fixture teardown is not production release/root retirement.
        // No callback ABI was invoked on this acquisition-failure path.
        Assert.Equal((0, 0, 0, 0, 0, 0), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                ownership.Capture.IsAlive, ownership.Completion.IsAlive, ownership.Primitive.IsAlive,
                ownership.Operations.IsAlive, ownership.Source.IsAlive,
                graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects distinct nested fatal identities at actual copy and user observer boundaries.")]
    private static UnavailableObserverGraph CreateUnavailableObserverFatalAfterCopyFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Original Start copy after-effect fatal A.");
        var observerFatal = new OutOfMemoryException("SourceUnavailable user observer fatal B.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime
        {
            CopyFailure = new AggregateException(new IOException("Original Start copy wrapper.", fatal)),
        };
        var operations = new CompletionOwnershipOperations(runtime);
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int observerCalls = 0;
        int observerThrows = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                Interlocked.Increment(ref observerCalls);
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref observerThrows);
                throw new AggregateException(new IOException("SourceUnavailable user observer wrapper.", observerFatal));
            });

        Exception? startFailure = Record.Exception(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? drainFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase first = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        nint firstPointer = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target).Pointer;
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase repeated = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        nint repeatedPointer = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target).Pointer;
        var ownership = new CompletionOwnershipGraph(new(capture), startCompletion, operations.Primitive!,
            new(operations), new(source.State), fatal, firstDisposeFailure, repeatedDisposeFailure);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(ownership, observerFatal, startFailure, drainFailure, (observerCalls, observerThrows),
            first, repeated, (firstPointer, repeatedPointer), operations.Stream, operations.Output,
            operations.Configuration, raw, new(sampleMarker), new(unavailableMarker));
    }

    private sealed record UnavailableObserverGraph(CompletionOwnershipGraph Ownership,
        OutOfMemoryException ObserverFatal, Exception? StartFailure, Exception? DrainFailure,
        (int Calls, int Throws) ObserverEffects, LateCallbackPhase FirstCleanup, LateCallbackPhase RepeatedCleanup,
        (nint First, nint Repeated) PointerObservations, nint Stream, nint Output, nint Configuration,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void ReturnedStartCallbackDoesNotReleaseBorrowedOwnersBeforeNativeHandoffExit()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        BorrowedHandoffGraph observation = CreateReturnedStartCallbackWithNativeHandoffHeld();
        ActiveCompletedGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.Null(observation.PendingDrainFailure);
        Assert.True(observation.PendingDrainResult);
        Assert.True(observation.DisposeThreadJoined);
        Assert.Equal((true, false, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Stop.Facts);
        Assert.Equal((1, 1, 2, 1, 1), graph.Pending.NativeEffects);
        // The actual callback ABI has returned, but its caller pointer and the
        // stream/source are still borrowed by the blocked native handoff frame.
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Equal((true, 0, true), graph.PendingResourceUseFacts);
        Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.True(graph.Pending.StopPrimitiveFacts.HasValue);
        var pendingStop = graph.Pending.StopPrimitiveFacts.GetValueOrDefault();
        Assert.Equal((true, true, true, true, true, 1L),
            (pendingStop.AcquisitionAttempted, pendingStop.RootAttempted, pendingStop.RootConfirmed,
                pendingStop.CopyAttempted, pendingStop.CopyConfirmed, pendingStop.PhysicalCopies));
        // Stop has left its own native handoff and may independently release
        // its caller. Do not mistake aggregate runtime release counts for the
        // Start-specific borrowed caller's ownership proof above.
        int independentStopReleases = pendingStop.CallerReleased ? 1 : 0;
        Assert.Equal((pendingStop.CallerReleased, pendingStop.CallerReleased,
                pendingStop.CallerReleased, pendingStop.CallerReleased),
            (pendingStop.NativeRetired, pendingStop.ManagedDrained,
                pendingStop.RootFreeAttempted, pendingStop.RootFreeConfirmed));
        Assert.Equal((2, 2, independentStopReleases, independentStopReleases,
            2 - independentStopReleases, 2 - independentStopReleases, 2, 2), graph.Pending.RuntimeEffects);
        Assert.Equal((4, 3, 1, 1, 0, 0, 0, 2, true), graph.Pending.CleanupEffects);
        Assert.Equal(before + 1, graph.Pending.ChargedOwners);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BorrowedHandoffGraph CreateReturnedStartCallbackWithNativeHandoffHeld()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowHandoffReturn = new ManualResetEventSlim();
        var nativeFrameHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationAfterCallbackHook = () =>
            {
                // No assertion here: runtime.Invoke has actually returned,
                // while this synchronous native handoff stack remains active.
                nativeFrameHeld.TrySetResult();
                allowHandoffReturn.Wait();
            },
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        bool startResult = false;
        Exception? startFailure = null;
        var startThread = new Thread(() => startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()))
        { IsBackground = true, Name = "Flowspan Start native handoff held after actual callback return" };
        Exception? pendingDisposeFailure = null;
        var disposer = new Thread(() => pendingDisposeFailure = Record.Exception(capture.Dispose))
        { IsBackground = true, Name = "Flowspan external Dispose during native Start borrow" };
        bool startThreadStarted = false;
        bool startThreadJoined = false;
        bool disposerStarted = false;
        bool disposerJoined = false;
        bool pendingDrainResult = false;
        Exception? pendingDrainFailure = null;
        Task<bool>? drain = null;
        Exception? entryFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        WeakReference? startCompletion = null;
        try
        {
            startThread.Start();
            startThreadStarted = true;
            entryFailure = Record.Exception(() => nativeFrameHeld.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            startCompletion = operations.Completion!;
            drain = capture.StopAndDrainAsync().AsTask();
            pendingDrainFailure = Record.Exception(() => pendingDrainResult = drain.WaitAsync(Timeout).GetAwaiter().GetResult());
            disposer.Start();
            disposerStarted = true;
            disposerJoined = disposer.Join(Timeout);
            if (disposerJoined)
            {
                pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
                pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
                pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
            }
        }
        finally
        {
            allowHandoffReturn.Set();
            if (startThreadStarted) { startThread.Join(); startThreadJoined = true; }
            if (disposerStarted) { disposer.Join(); }
        }

        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = drain!.WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion!);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion!);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion!);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(entryFailure, pendingDisposeFailure, pending!, pendingRelease, pendingResourceUse,
            startThreadJoined, null, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion!, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker)),
            pendingDrainResult, pendingDrainFailure, disposerJoined);
    }

    private sealed record BorrowedHandoffGraph(ActiveCompletedGraph Graph, bool PendingDrainResult,
        Exception? PendingDrainFailure, bool DisposeThreadJoined);

    [Fact]
    public void PendingSuccessfulStartDisposeLeavesAdmissionOpenForLateCallback()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ActiveCompletedGraph graph = CreatePendingSuccessfulStartBeforeActualCallback();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        InvalidOperationException pending = Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.Equal("macOS capture cleanup remains unconfirmed.", pending.Message);
        Assert.Equal((true, true, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((false, 0, false), graph.PendingResourceUseFacts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 0, 0), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Null(graph.Pending.StartPrimitiveFailure);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ActiveCompletedGraph CreatePendingSuccessfulStartBeforeActualCallback()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowInvoke = new ManualResetEventSlim();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new CompletionOwnershipOperations(runtime) { StartInvocationReturnsBeforeCallback = true };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            ready.TrySetResult();
            allowInvoke.Wait();
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan pending healthy Start late callback ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        Exception? entryFailure = null;
        Exception? pendingDisposeFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        try
        {
            callback.Start();
            callbackStarted = true;
            entryFailure = Record.Exception(() => ready.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            pendingDisposeFailure = Record.Exception(capture.Dispose);
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        }
        finally
        {
            allowInvoke.Set();
            if (callbackStarted) { callback.Join(); callbackJoined = true; }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(entryFailure, pendingDisposeFailure, pending!, pendingRelease, pendingResourceUse,
            callbackJoined, callbackFailure, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker));
    }

    [Fact]
    public async Task PhysicalDrainWithHeldStopCopyDoesNotReturnCatalogBinding()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        var captureApi = new MacOSRemoteWindowScreenCaptureKitApi();
        var sourceEffects = new SourceEffects();
        IMacOSRemoteWindowNativeSource nativeSource = MacOSRemoteWindowScreenCaptureKitApi.CreateNativeSourceWithOperations(
            new(1, 123, 1, 0), NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2),
            SourceEffects.Window, SourceEffects.Filter, sourceEffects);
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime)
        {
            HoldExternalStopCopy = true,
            StopInvocationFailureAfterEffect = new IOException("Actual Stop returned callback before ordinary invocation failure."),
        };
        IMacOSRemoteWindowNativeCapture? capture = null;
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            CaptureFactory = (source, takeSampleOwnership, sourceUnavailable) =>
            {
                // This is the actual catalog binding's Native, not a second
                // independently constructed Capture source or alternate owner.
                Assert.Same(nativeSource, source);
                capture = captureApi.CreateCaptureWithOperations(source, operations, takeSampleOwnership, sourceUnavailable);
                return capture;
            },
        };
        var pool = new MacOSRemoteWindowSourceOwnershipPool(catalogCapacity: 1, batchCapacity: 1);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api, ownershipPool: pool);
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        try
        {
            Assert.True((await catalog.RefreshAsync().AsTask().WaitAsync(Timeout)).Succeeded);
            NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
            var sourceUse = NativeRemoteWindowSourceUse.Create(snapshot, 1, 1);
            Assert.True((await boundary.StartAsync(sourceUse, new MacOSRemoteWindowTestSink(), CancellationToken.None)
                .AsTask().WaitAsync(Timeout)).Succeeded);
            Assert.Equal(1, api.CaptureCalls);
            Assert.NotNull(capture);
            Assert.Equal((1, 1, 0), pool.GetUsage());
            Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, sourceEffects.RetainAttempts);

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await boundary.DisposeAsync().AsTask().WaitAsync(Timeout));
            Assert.Equal("macos_capture_cleanup_unconfirmed", failure.Message);
            Task<bool> cachedDrain = Assert.IsAssignableFrom<Task<bool>>(capture.GetType()
                .GetField("drain", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
            Assert.True(cachedDrain.IsCompletedSuccessfully);
            Assert.False(await cachedDrain);
            Assert.Same(cachedDrain, capture.StopAndDrainAsync().AsTask());
            Assert.True(capture.IsDrained);
            Assert.Equal((true, false, true, TaskStatus.RanToCompletion, true, true), ReadCaptureCompletionPhase(capture, "stop").Facts);
            Assert.Equal((1, 1, 2, 1, 1),
                (operations.StartSelectorAttempts, operations.StopSelectorAttempts, operations.NativeInvocationAttempts,
                    operations.StartInvocations, operations.StopInvocations));
            Assert.Equal((4, 4, 1, 1, 3, 1),
                (operations.PoolsPushed, operations.PoolsPopped, operations.RemoveCalls, operations.DrainCalls,
                    operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts));
            Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
            Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, sourceEffects.ReleaseAttempts);
            Assert.Equal(1, sourceEffects.References[SourceEffects.Window]);
            Assert.Equal(1, sourceEffects.References[SourceEffects.Filter]);
            Assert.Equal((2, 3, 2, 1, 1, 1, 2, 2),
                (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                    runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns));
            var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
            Assert.Equal((true, true, true, true, true, true, false, false, false, false, 1L), ReadZeroPoolPrimitiveFacts(stopOwner));
            Assert.Null(stopOwner.FirstFailure);
            Assert.False(stopOwner.NativeCaptureRetirement.IsCompleted);
            Assert.False(stopOwner.ManagedInvocationDrain.IsCompleted);
            Assert.NotEqual(0, Volatile.Read(ref operations.ExternallyHeldStopCopy));
            Assert.Equal(0, Volatile.Read(ref operations.NativeHeldCompletion));
            Assert.Equal(0, operations.NativeHeldReleaseAttempts);
            Assert.Equal(0, operations.ExternalStopCopyReleaseAttempts);
            Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
            object operation = boundary.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            var binding = Assert.IsType<MacOSRemoteWindowSourceCatalog.NativeBinding>(operation.GetType()
                .GetField("binding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(operation));
            var entry = Assert.IsType<MacOSRemoteWindowSourceCatalog.SourceEntry>(binding.GetType()
                .GetField("entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding));
            Assert.Same(nativeSource, binding.Native);
            Assert.Equal(0, binding.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding));
            Assert.Equal(2, entry.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry));

            await catalog.DisposeAsync().AsTask().WaitAsync(Timeout);
            Assert.Throws<ObjectDisposedException>(() => catalog.GetSnapshot());
            Assert.Equal(0, binding.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding));
            Assert.Equal(1, entry.GetType().GetField("references", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry));
            Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, sourceEffects.ReleaseAttempts);
            Assert.Equal(1, sourceEffects.References[SourceEffects.Window]);
            Assert.Equal(1, sourceEffects.References[SourceEffects.Filter]);
            Assert.Equal((1, 1, 0), pool.GetUsage());
            var replacementApi = new MacOSRemoteWindowTestApi();
            var replacement = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), replacementApi, ownershipPool: pool);
            LocalBoundaryResult refused = await replacement.RefreshAsync().AsTask().WaitAsync(Timeout);
            Assert.False(refused.Succeeded);
            Assert.Equal("macos_source_ownership_capacity_exhausted", refused.ReasonCode);
            Assert.Equal(0, replacementApi.EnumerationCalls);
            await replacement.DisposeAsync().AsTask().WaitAsync(Timeout);
            Assert.Equal((1, 1, 0), pool.GetUsage());
        }
        finally
        {
            // Release only this confirmed fixture-owned extra retain once.
            // Join production's actual recovery without replaying native work,
            // disposing the binding here or resetting owner accounting.
            operations.ReleaseExternalStopCopy();
            try
            {
                try { await boundary.StopCompletion; } catch (Exception) { }
                object? operation = boundary.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary);
                if (operation?.GetType().GetField("knownPendingCleanupRecovery", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(operation) is Task recovery)
                {
                    await recovery;
                }
            }
            finally { runtime.Dispose(); }
        }

        Assert.Equal(1, operations.ExternalStopCopyReleaseAttempts);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
    }

    [Fact]
    public void StopAndDrainWaitsForActiveCompletedResourceUse()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ActiveCompletedDrainGraph observation = CreateStopDrainWithActiveStartCompletedNotification();
        ActiveCompletedGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.True(observation.ProgressObserved);
        // Actual Stop completion, output removal and sample barrier may progress
        // independently of the still-active Start completed resource-use region.
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Stop.Facts);
        Assert.Equal((1, 1, 2, 1, 1), graph.Pending.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 0, 0, 0, 2, true), graph.Pending.CleanupEffects);
        Assert.False(observation.DrainCompletedWhileCallbackActive);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((true, 1, false), graph.PendingResourceUseFacts);
        Assert.Equal((2, 3, 0, 0, 2, 2, 2, 1), graph.Pending.RuntimeEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Pending.StartPrimitiveFailure);
        Assert.Null(graph.Pending.StopPrimitiveFailure);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ActiveCompletedDrainGraph CreateStopDrainWithActiveStartCompletedNotification()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowCompletedReturn = new ManualResetEventSlim();
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartCompletedRelease = allowCompletedReturn,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan Stop drain active completed resource-use ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        bool progressObserved = false;
        bool drainCompletedWhileCallbackActive = false;
        Task<bool>? drain = null;
        Exception? entryFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        try
        {
            callback.Start();
            callbackStarted = true;
            entryFailure = Record.Exception(() => operations.StartCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            drain = capture.StopAndDrainAsync().AsTask();
            // Observe a concrete terminal decision: old premature task return
            // or closure of the actual owner before its active resource join.
            // This is a bounded observation, not a delay authorizing cleanup.
            progressObserved = SpinWait.SpinUntil(() => drain.IsCompleted
                || ReadCompletionResourceUseFacts(startCompletion).Closed, Timeout);
            drainCompletedWhileCallbackActive = drain.IsCompleted;
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        }
        finally
        {
            allowCompletedReturn.Set();
            if (callbackStarted) { callback.Join(); callbackJoined = true; }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = drain!.WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(entryFailure, null, pending!, pendingRelease, pendingResourceUse,
            callbackJoined, callbackFailure, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker)),
            progressObserved, drainCompletedWhileCallbackActive);
    }

    private sealed record ActiveCompletedDrainGraph(ActiveCompletedGraph Graph, bool ProgressObserved,
        bool DrainCompletedWhileCallbackActive);

    [Fact]
    public void StartCompletedAsyncDescendantStopRejectsSelfJoin()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        CompletedDescendantGraph observation = CreateStartCompletedAsyncDescendantStopSelfJoin();
        ActiveCompletedGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        InvalidOperationException rejection = Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.Equal("macOS native callback cannot join its own cleanup.", rejection.Message);
        Assert.Equal(1, observation.DescendantEntries);
        Assert.Equal(TaskStatus.Faulted, observation.ObservedDescendantStatus);
        Assert.True(observation.DescendantJoined);
        Assert.Same(rejection, observation.DescendantJoinFailure);
        Assert.NotEqual(observation.CallbackThread, observation.DescendantThread);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((false, 1, false), graph.PendingResourceUseFacts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 1, 0), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Null(graph.Pending.StartPrimitiveFailure);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CompletedDescendantGraph CreateStartCompletedAsyncDescendantStopSelfJoin()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowCompletedReturn = new ManualResetEventSlim();
        IMacOSRemoteWindowNativeCapture? capture = null;
        Exception? selfJoinFailure = null;
        Task? descendant = null;
        TaskStatus observedDescendantStatus = default;
        int descendantEntries = 0;
        int callbackThread = 0;
        int descendantThread = 0;
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartCompletedHook = () =>
            {
                callbackThread = Environment.CurrentManagedThreadId;
                // Normal ExecutionContext inheritance, including the actual
                // still-active completed owner's ancestry across an await.
                descendant = Task.Run(async () =>
                {
                    await Task.Yield();
                    descendantThread = Environment.CurrentManagedThreadId;
                    Interlocked.Increment(ref descendantEntries);
                    await capture!.StopAndDrainAsync();
                });
                try { descendant.WaitAsync(Timeout).GetAwaiter().GetResult(); }
                catch (Exception exception) { selfJoinFailure = exception; }
                observedDescendantStatus = descendant.Status;
            },
            StartCompletedRelease = allowCompletedReturn,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan completed async Stop descendant ancestry ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        bool descendantJoined = false;
        Exception? descendantJoinFailure = null;
        Exception? entryFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        try
        {
            callback.Start();
            callbackStarted = true;
            entryFailure = Record.Exception(() => operations.StartCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            // Child observation has finished while its parent remains active;
            // no callback-side xUnit assertion can be swallowed by containment.
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        }
        finally
        {
            allowCompletedReturn.Set();
            if (callbackStarted) { callback.Join(); callbackJoined = true; }
            if (descendant is not null)
            {
                descendantJoinFailure = Record.Exception(() => descendant.GetAwaiter().GetResult());
                descendantJoined = true;
            }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(entryFailure, selfJoinFailure, pending!, pendingRelease, pendingResourceUse,
            callbackJoined, callbackFailure, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker)),
            descendantEntries, observedDescendantStatus, descendantJoined, descendantJoinFailure, callbackThread, descendantThread);
    }

    private sealed record CompletedDescendantGraph(ActiveCompletedGraph Graph, int DescendantEntries,
        TaskStatus ObservedDescendantStatus, bool DescendantJoined, Exception? DescendantJoinFailure,
        int CallbackThread, int DescendantThread);

    [Fact]
    public void StartCompletedDirectDisposeRejectsSelfJoin()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ActiveCompletedGraph graph = CreateStartCompletedDirectDisposeSelfJoin();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        InvalidOperationException rejection = Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.Equal("macOS native callback cannot dispose its own cleanup owner.", rejection.Message);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((false, 1, false), graph.PendingResourceUseFacts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 1, 0), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Null(graph.Pending.StartPrimitiveFailure);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ActiveCompletedGraph CreateStartCompletedDirectDisposeSelfJoin()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowCompletedReturn = new ManualResetEventSlim();
        IMacOSRemoteWindowNativeCapture? capture = null;
        Exception? selfJoinFailure = null;
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartCompletedHook = () =>
            {
                // Record only: assertions belong outside this guarded ABI path.
                try { capture!.Dispose(); }
                catch (Exception exception) { selfJoinFailure = exception; }
            },
            StartCompletedRelease = allowCompletedReturn,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan completed direct Dispose ancestry ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        Exception? entryFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        try
        {
            callback.Start();
            callbackStarted = true;
            entryFailure = Record.Exception(() => operations.StartCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            // The hook returned, but its actual guarded completed wrapper is
            // still active and the one-argument reverse ABI has not returned.
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        }
        finally
        {
            allowCompletedReturn.Set();
            if (callbackStarted) { callback.Join(); callbackJoined = true; }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(entryFailure, selfJoinFailure, pending!, pendingRelease, pendingResourceUse,
            callbackJoined, callbackFailure, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker));
    }

    [Fact]
    public void FirstStartFailureObserverStillActiveDoesNotReleaseCallerAfterDuplicateCompletion()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ActiveFailureObserverGraph observation = CreateActiveStartFailureObserverAfterDuplicateCompletion();
        ActiveCompletedGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.Equal(0, graph.Pending.RuntimeEffects.Releases);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((true, 1, false), graph.PendingResourceUseFacts);
        Assert.Same(observation.Fatal, graph.PendingDisposeFailure);
        Assert.True(observation.SecondCallbackJoined);
        Assert.Null(observation.SecondCallbackFailure);
        Assert.Equal((1, 1), (observation.ActionThrowAttempts, observation.UnavailableCalls));
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 2, 1), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Same(observation.Fatal, graph.Pending.StartPrimitiveFailure);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        // The original Start call returned its pending task before callbacks;
        // its first real success stays true rather than being retroactively
        // rewritten. Subsequent cleanup must still report the original fatal.
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.Same(observation.Fatal, graph.DrainFailure);
        Assert.Same(observation.Fatal, graph.FinalDisposeFailure);
        Assert.Same(observation.Fatal, graph.FinalRepeatedDisposeFailure);
        Assert.Same(observation.Fatal, graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 3, 3), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 3, 3), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Actual first action throws its chosen nested fatal before the actual Capture failure observer is paused.")]
    private static ActiveFailureObserverGraph CreateActiveStartFailureObserverAfterDuplicateCompletion()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowFailureReturn = new ManualResetEventSlim();
        var fatal = new OutOfMemoryException("Original active first Start failure-observer action fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartActionFailureAfterEffect = new AggregateException(new IOException("First actual Start action wrapper.", fatal)),
            StartActionFailureOnce = true,
            StartFailureObserverRelease = allowFailureReturn,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? firstCallbackFailure = null;
        Exception? secondCallbackFailure = null;
        var first = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { firstCallbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan active failure observer ABI" };
        var second = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { secondCallbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan duplicate after active failure observer ABI" };
        bool firstStarted = false;
        bool secondStarted = false;
        bool firstJoined = false;
        bool secondJoined = false;
        Exception? entryFailure = null;
        Exception? pendingDisposeFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        try
        {
            first.Start();
            firstStarted = true;
            entryFailure = Record.Exception(() => operations.StartFailureObserverEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            second.Start();
            secondStarted = true;
            secondJoined = second.Join(Timeout);
            pendingDisposeFailure = Record.Exception(capture.Dispose);
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        }
        finally
        {
            allowFailureReturn.Set();
            if (firstStarted) { first.Join(); firstJoined = true; }
            if (secondStarted) { second.Join(); }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(entryFailure, pendingDisposeFailure, pending!, pendingRelease, pendingResourceUse,
            firstJoined, firstCallbackFailure, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker)),
            fatal, secondJoined, secondCallbackFailure, operations.StartActionThrowAttempts, unavailableCalls);
    }

    private sealed record ActiveFailureObserverGraph(ActiveCompletedGraph Graph, OutOfMemoryException Fatal,
        bool SecondCallbackJoined, Exception? SecondCallbackFailure, int ActionThrowAttempts, int UnavailableCalls);

    [Fact]
    public void StartCompletedStillActiveDoesNotReleaseCaller()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ActiveCompletedGraph graph = CreateActiveStartCompletedNotification();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.EntryFailure);
        Assert.Equal(0, graph.Pending.RuntimeEffects.Releases);
        Assert.Equal((false, false, false, false), graph.PendingCallerReleaseFacts);
        Assert.Equal((true, 1, false), graph.PendingResourceUseFacts);
        Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 1, 0), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.True(graph.StartResult);
        Assert.Null(graph.StartFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.DrainFailure);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal((true, true, true, true), graph.FinalCallerReleaseFacts);
        Assert.Equal((true, 0, true), graph.FinalResourceUseFacts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ActiveCompletedGraph CreateActiveStartCompletedNotification()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var allowCompletedReturn = new ManualResetEventSlim();
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationReturnsBeforeCallback = true,
            StartCompletedRelease = allowCompletedReturn,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan active completed notification ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        Exception? entryFailure = null;
        Exception? pendingDisposeFailure = null;
        LateCallbackPhase? pending = null;
        (bool, bool, bool, bool) pendingRelease = default;
        (bool, int, bool) pendingResourceUse = default;
        try
        {
            callback.Start();
            callbackStarted = true;
            entryFailure = Record.Exception(() => operations.StartCompletedEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
            pendingDisposeFailure = Record.Exception(capture.Dispose);
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
            pendingRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
            pendingResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        }
        finally
        {
            allowCompletedReturn.Set();
            if (callbackStarted) { callback.Join(); callbackJoined = true; }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var finalRelease = ReadStartCallerReleaseFacts(capture, startCompletion);
        var finalResourceUse = ReadCompletionResourceUseFacts(startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(entryFailure, pendingDisposeFailure, pending!, pendingRelease, pendingResourceUse,
            callbackJoined, callbackFailure, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, final, finalRelease, finalResourceUse,
            operations.Stream, operations.Output, operations.Configuration, raw,
            new(capture), startCompletion, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker));
    }

    private static (bool CaptureAttempted, bool CaptureConfirmed, bool PrimitiveAttempted, bool PrimitiveConfirmed)
        ReadStartCallerReleaseFacts(IMacOSRemoteWindowNativeCapture capture, WeakReference completion)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(completion.Target);
        lock (Field("gate"))
        {
            return ((bool)Field("startBlockReleaseAttempted"), (bool)Field("startBlockReleaseConfirmed"),
                owner.Primitive.OwnedReleaseAttempted, owner.Primitive.OwnedReleaseConfirmed);
        }
    }

    private static (bool Closed, int ActiveUsers, bool JoinCompleted) ReadCompletionResourceUseFacts(WeakReference completion)
    {
        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(completion.Target);
        object Field(string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        lock (Field("resourceUseGate"))
        {
            return ((bool)Field("resourceUseClosed"), (int)Field("activeResourceUsers"),
                Assert.IsType<TaskCompletionSource>(Field("resourceUseJoin")).Task.IsCompletedSuccessfully);
        }
    }

    private sealed record ActiveCompletedGraph(Exception? EntryFailure, Exception? PendingDisposeFailure,
        LateCallbackPhase Pending, (bool, bool, bool, bool) PendingCallerReleaseFacts, (bool, int, bool) PendingResourceUseFacts,
        bool CallbackJoined, Exception? CallbackFailure, bool StartResult, Exception? StartFailure, bool DrainResult, Exception? DrainFailure,
        Exception? FinalDisposeFailure, Exception? FinalRepeatedDisposeFailure, LateCallbackPhase Final,
        (bool, bool, bool, bool) FinalCallerReleaseFacts, (bool, int, bool) FinalResourceUseFacts,
        nint Stream, nint Output, nint Configuration, (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker);

    [Fact]
    public void RepeatedStartAfterRecordedCompletionFatalDoesNotReturnCachedSuccess()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StartActionFaultGraph graph = CreateStartActionFaultAfterCaptureResult(repeatStartBeforeDrain: true);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Same(graph.Fatal, graph.RepeatedStartFailure);
        Assert.False(graph.RepeatedStartResult);
        Assert.Same(graph.Fatal, graph.StartFailure);
        Assert.True(graph.StartThreadJoined);
        Assert.Equal((1, 0, 0, 1),
            (graph.ActionThrowAttempts, graph.FailureObserverThrowAttempts, graph.CompletedThrowAttempts, graph.UnavailableCalls));
        LateCallbackPhase beforeDrain = Assert.IsType<LateCallbackPhase>(graph.BeforeDrain);
        // The initial real success/settlement/return remains confirmed. The
        // repeated API call must report the recorded fatal, not rewrite facts
        // or issue another native Start/acquire another completion owner.
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), beforeDrain.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), beforeDrain.Stop.Facts);
        Assert.Equal((1, 1, 0, 0, 1, 1, 1, 1), beforeDrain.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), beforeDrain.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), beforeDrain.CleanupEffects);
        Assert.Equal(before + 1, beforeDrain.ChargedOwners);
        Assert.Same(graph.Fatal, beforeDrain.StartPrimitiveFailure);
        Assert.Empty(beforeDrain.ObjectReleases);
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Actual completed notification throws a wrapper containing the chosen fatal after the real Capture exit notification.")]
    public void StartCompletedNotificationFaultPreservesFatalAndReturnsKnownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        var fatal = new OutOfMemoryException("Original actual Start completed-notification fatal.");
        StartActionFaultGraph graph = CreateStartActionFaultAfterCaptureResult(completedFatal: fatal);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((0, 0, 1), (graph.ActionThrowAttempts, graph.FailureObserverThrowAttempts, graph.CompletedThrowAttempts));
        Assert.True(graph.StartThreadJoined);
        Assert.Same(fatal, graph.Fatal);
        Assert.Same(fatal, graph.StartFailure);
        Assert.Same(fatal, graph.DrainFailure);
        Assert.Same(fatal, graph.FirstDisposeFailure);
        Assert.Same(fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Same(fatal, graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(1, graph.UnavailableCalls);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Actual Capture failure callback handles the original action fatal before its primitive observer throws a different nested fatal.")]
    public void StartFailureObserverFaultDoesNotReplaceEarlierActionFatalOrRetainKnownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        var laterFatal = new OutOfMemoryException("Later actual Start failure-observer fatal.");
        StartActionFaultGraph graph = CreateStartActionFaultAfterCaptureResult(
            new AggregateException(new IOException("Actual Start failure-observer wrapper.", laterFatal)));

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((1, 1), (graph.ActionThrowAttempts, graph.FailureObserverThrowAttempts));
        Assert.True(graph.StartThreadJoined);
        Assert.NotSame(laterFatal, graph.Fatal);
        Assert.Same(graph.Fatal, graph.StartFailure);
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Same(graph.Fatal, graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(1, graph.UnavailableCalls);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
        GC.KeepAlive(laterFatal);
    }

    [Fact]
    public void StartActionFaultAfterActualCaptureResultPreservesFatalAndReturnsKnownOwnership()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StartActionFaultGraph graph = CreateStartActionFaultAfterCaptureResult();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Same(graph.Fatal, graph.StartFailure);
        Assert.True(graph.StartThreadJoined);
        Assert.Equal(1, graph.ActionThrowAttempts);
        Assert.Equal(1, graph.UnavailableCalls);
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Same(graph.Fatal, graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        // A managed callback failure remains reportable, but is not unknown
        // ownership after every release and both terminal facts are confirmed.
        Assert.Equal(before, graph.Final.ChargedOwners);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(graph.Capture.IsAlive);
        Assert.False(graph.StartCompletion.IsAlive);
        Assert.False(graph.Operations.IsAlive);
        Assert.False(graph.Source.IsAlive);
        Assert.False(graph.SampleMarker.IsAlive);
        Assert.False(graph.UnavailableMarker.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Actual one-argument action throws a wrapper containing the chosen original fatal after the real Capture action has run.")]
    private static StartActionFaultGraph CreateStartActionFaultAfterCaptureResult(Exception? failureObserverFailure = null,
        OutOfMemoryException? completedFatal = null, bool repeatStartBeforeDrain = false)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = completedFatal ?? new OutOfMemoryException("Original actual Start action fatal after Capture result.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartActionFailureAfterEffect = completedFatal is null ? new AggregateException(new IOException("Actual Start action wrapper.", fatal)) : null,
            StartFailureObserverFailureAfterEffect = failureObserverFailure,
            StartCompletedFailureAfterEffect = completedFatal is null ? null : new AggregateException(new IOException("Actual Start completed-notification wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () =>
            {
                GC.KeepAlive(unavailableMarker);
                Interlocked.Increment(ref unavailableCalls);
            });
        Exception? startFailure = null;
        var startThread = new Thread(() => startFailure = Record.Exception(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()))
        { IsBackground = true, Name = "Flowspan actual Start action fault ABI" };
        bool startThreadStarted = false;
        bool startThreadJoined = false;
        try
        {
            startThread.Start();
            startThreadStarted = true;
            startThreadJoined = startThread.Join(Timeout);
        }
        finally
        {
            if (startThreadStarted) { startThread.Join(); }
        }

        WeakReference startCompletion = operations.Completion!;
        bool repeatedStartResult = false;
        Exception? repeatedStartFailure = null;
        LateCallbackPhase? beforeDrain = null;
        if (repeatStartBeforeDrain)
        {
            repeatedStartFailure = Record.Exception(() => repeatedStartResult =
                capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
            beforeDrain = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        }
        Exception? drainFailure = Record.Exception(() => capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var captureReference = new WeakReference(capture);
        var operationsReference = new WeakReference(operations);
        var sourceReference = new WeakReference(source.State);
        var sampleReference = new WeakReference(sampleMarker);
        var unavailableReference = new WeakReference(unavailableMarker);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(fatal, startFailure, startThreadJoined, operations.StartActionThrowAttempts, operations.StartFailureObserverThrowAttempts,
            operations.StartCompletedThrowAttempts, unavailableCalls,
            drainFailure, firstDisposeFailure, repeatedDisposeFailure, final,
            operations.Stream, operations.Output, operations.Configuration, raw,
            captureReference, startCompletion, operationsReference, sourceReference, sampleReference, unavailableReference)
        {
            RepeatedStartResult = repeatedStartResult,
            RepeatedStartFailure = repeatedStartFailure,
            BeforeDrain = beforeDrain,
        };
    }

    private sealed record StartActionFaultGraph(OutOfMemoryException Fatal, Exception? StartFailure, bool StartThreadJoined,
        int ActionThrowAttempts, int FailureObserverThrowAttempts, int CompletedThrowAttempts, int UnavailableCalls, Exception? DrainFailure, Exception? FirstDisposeFailure,
        Exception? RepeatedDisposeFailure, LateCallbackPhase Final, nint Stream, nint Output, nint Configuration,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects,
        WeakReference Capture, WeakReference StartCompletion, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker)
    {
        internal bool RepeatedStartResult { get; init; }
        internal Exception? RepeatedStartFailure { get; init; }
        internal LateCallbackPhase? BeforeDrain { get; init; }
    }

    [Fact]
    public void FirstStartActionStillActiveDoesNotReleaseCallerAfterDuplicateCompletion()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ActiveStartActionGraph graph = CreateActiveStartActionAfterDuplicateCompletion();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal(0, graph.Pending.RuntimeEffects.Releases);
        Assert.True(graph.FirstActionEntered);
        Assert.True(graph.SecondCallbackJoined);
        Assert.True(graph.FirstCallbackJoined);
        Assert.Null(graph.FirstCallbackFailure);
        Assert.Null(graph.SecondCallbackFailure);
        Assert.IsType<InvalidOperationException>(graph.PendingDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, false, true), graph.Pending.Start.Facts);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 2, 1), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.Null(graph.StartFailure);
        Assert.False(graph.StartResult);
        Assert.Null(graph.DrainFailure);
        Assert.True(graph.DrainResult);
        Assert.Null(graph.FinalDisposeFailure);
        Assert.Null(graph.FinalRepeatedDisposeFailure);
        Assert.Equal(1, graph.UnavailableCalls);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, false, true), graph.Final.Start.Facts);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 3, 3), graph.Final.RuntimeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal((0, 0, 3, 2, 3, 3), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ActiveStartActionGraph CreateActiveStartActionAfterDuplicateCompletion()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        using var actionEntered = new ManualResetEventSlim();
        using var allowActionReturn = new ManualResetEventSlim();
        var operations = new CompletionOwnershipOperations(runtime) { StartInvocationReturnsBeforeCallback = true };
        int unavailableCalls = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => sample.Dispose(), () =>
            {
                Interlocked.Increment(ref unavailableCalls);
                actionEntered.Set();
                allowActionReturn.Wait();
            });
        Task<bool> start = capture.StartAsync().AsTask();
        WeakReference startCompletion = operations.Completion!;
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? firstCallbackFailure = null;
        Exception? secondCallbackFailure = null;
        var first = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 1); }
            catch (Exception exception) { firstCallbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan active first Start action ABI" };
        var second = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { secondCallbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan duplicate Start completion ABI" };
        bool firstStarted = false;
        bool secondStarted = false;
        bool firstActionEntered = false;
        bool secondCallbackJoined = false;
        bool firstCallbackJoined = false;
        Exception? pendingDisposeFailure = null;
        LateCallbackPhase? pending = null;
        try
        {
            first.Start();
            firstStarted = true;
            firstActionEntered = actionEntered.Wait(Timeout);
            second.Start();
            secondStarted = true;
            secondCallbackJoined = second.Join(Timeout);
            pendingDisposeFailure = Record.Exception(capture.Dispose);
            pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        }
        finally
        {
            // Every test barrier is released before the real ABI threads join.
            // Neither completed nor a terminal retirement task substitutes for
            // actual thread return before the raw test-runtime teardown.
            allowActionReturn.Set();
            if (firstStarted) { first.Join(); firstCallbackJoined = true; }
            if (secondStarted) { second.Join(); }
        }

        bool startResult = false;
        Exception? startFailure = Record.Exception(() => startResult = start.WaitAsync(Timeout).GetAwaiter().GetResult());
        bool drainResult = false;
        Exception? drainFailure = Record.Exception(() => drainResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalDisposeFailure = Record.Exception(capture.Dispose);
        Exception? finalRepeatedDisposeFailure = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(firstActionEntered, secondCallbackJoined, firstCallbackJoined, firstCallbackFailure, secondCallbackFailure,
            pendingDisposeFailure, pending!, startResult, startFailure, drainResult, drainFailure,
            finalDisposeFailure, finalRepeatedDisposeFailure, unavailableCalls, final,
            operations.Stream, operations.Output, operations.Configuration, raw);
    }

    private sealed record ActiveStartActionGraph(bool FirstActionEntered, bool SecondCallbackJoined, bool FirstCallbackJoined,
        Exception? FirstCallbackFailure, Exception? SecondCallbackFailure, Exception? PendingDisposeFailure,
        LateCallbackPhase Pending, bool StartResult, Exception? StartFailure, bool DrainResult, Exception? DrainFailure,
        Exception? FinalDisposeFailure, Exception? FinalRepeatedDisposeFailure, int UnavailableCalls, LateCallbackPhase Final,
        nint Stream, nint Output, nint Configuration,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects);

    [Fact]
    public void StartInvocationFatalWithoutCallbackWaitsForRealLateCallbackAndRetiresNativeHeldCopy()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        LateCallbackGraph graph = CreateStartInvokeWithoutCallbackFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Same(graph.Fatal, graph.StartFailure);
        Assert.IsType<TimeoutException>(graph.PendingWatchdogFailure);
        Assert.Same(graph.Fatal, graph.PendingFirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.PendingRepeatedDisposeFailure);
        Assert.Equal((true, false, false, TaskStatus.Faulted, null, false), graph.Pending.Start.Facts);
        Assert.Same(graph.Fatal, graph.Pending.Start.ResultFailure);
        Assert.Equal((false, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Equal((1, 2, 0, 0, 1, 1, 0, 0), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 0, 1, 1, 0), graph.Pending.NativeEffects);
        Assert.Equal((2, 2, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.Null(graph.Pending.StopPrimitiveFacts);
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.True(graph.FinalFirstDisposeFailure is null || ReferenceEquals(graph.Fatal, graph.FinalFirstDisposeFailure));
        Assert.True(graph.FinalRepeatedDisposeFailure is null || ReferenceEquals(graph.Fatal, graph.FinalRepeatedDisposeFailure));
        // A faulted local result remains faulted after a real successful native
        // callback. Native handoff return is never retroactively invented.
        Assert.Equal((true, false, true, TaskStatus.Faulted, null, true), graph.Final.Start.Facts);
        Assert.Same(graph.Fatal, graph.Final.Start.ResultFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.True(graph.Final.StopPrimitiveFacts.HasValue);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        // All independent ownership and lifetime facts are confirmed here;
        // retaining the original shell forever would be a different defect.
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests actual extra heap retain followed by wrapped Start invocation fatal before a real late one-argument ABI callback.")]
    private static LateCallbackGraph CreateStartInvokeWithoutCallbackFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original Start native handoff after-effect fatal before callback.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationFailureBeforeCallbackAfterEffect = new AggregateException(new IOException("Start handoff wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Exception? startFailure = Record.Exception(() => capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        Task<bool> drain = capture.StopAndDrainAsync().AsTask();
        // This external watchdog observes pending only. It never confirms
        // callback exit, drain, cleanup or ownership return.
        Exception? pendingWatchdogFailure = Record.Exception(() => drain.WaitAsync(TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult());
        Exception? pendingFirst = Record.Exception(capture.Dispose);
        Exception? pendingRepeated = Record.Exception(capture.Dispose);
        LateCallbackPhase pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan actual late Start completion ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        try
        {
            callback.Start();
            callbackStarted = true;
            callbackJoined = callback.Join(Timeout);
        }
        finally
        {
            // Join actual ABI return before any caller/native owner release
            // or raw teardown; a completed notification alone is not this join.
            if (callbackStarted) { callback.Join(); }
        }

        Exception? drainFailure = Record.Exception(() => drain.WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalFirst = Record.Exception(capture.Dispose);
        Exception? finalRepeated = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(fatal, startFailure, pendingWatchdogFailure, pendingFirst, pendingRepeated,
            callbackJoined, callbackFailure, drainFailure, finalFirst, finalRepeated, pending, final,
            operations.Stream, operations.Output, operations.Configuration, pushes, pops, raw);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LateCallbackPhase ReadLateCallbackPhase(IMacOSRemoteWindowNativeCapture capture,
        CompletionOwnershipOperations operations, MacOSRemoteWindowControlledBlockRuntime runtime,
        SourceState source, WeakReference startCompletion)
    {
        var startOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target);
        var stopOwner = ReferenceEquals(operations.Completion, startCompletion) ? null
            : Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
        var stopFacts = stopOwner is null ? ((bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, long)?)null : ReadZeroPoolPrimitiveFacts(stopOwner);
        return new(ReadCaptureCompletionPhase(capture, "start"), ReadCaptureCompletionPhase(capture, "stop"),
            MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
            (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
                runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns),
            (operations.StartSelectorAttempts, operations.StopSelectorAttempts, operations.NativeInvocationAttempts,
                operations.StartInvocations, operations.StopInvocations),
            (operations.PoolsPushed, operations.PoolsPopped, operations.RemoveCalls, operations.DrainCalls,
                operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts, source.RetainedReleaseAttempts,
                source.OwnerCount, capture.IsDrained),
            operations.NativeHeldCopyReady.Task.IsCompletedSuccessfully, Volatile.Read(ref operations.NativeHeldCompletion) != 0,
            operations.NativeHeldReleaseAttempts, ReadZeroPoolPrimitiveFacts(startOwner), stopFacts,
            startOwner.FirstFailure, stopOwner?.FirstFailure, operations.ObjectReleaseAttempts.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CaptureCompletionPhase ReadCaptureCompletionPhase(IMacOSRemoteWindowNativeCapture capture, string scope)
    {
        object Field(string name) => capture.GetType().GetField(scope + name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        var result = Assert.IsType<TaskCompletionSource<bool>>(Field("Completion")).Task;
        var exit = Assert.IsType<TaskCompletionSource<bool>>(Field("CallbackExited")).Task;
        return new(((bool)Field("Issued"), (bool)Field("InvocationReturned"), (bool)Field("Settled"), result.Status,
            result.IsCompletedSuccessfully ? result.Result : null, exit.IsCompletedSuccessfully), result.Exception?.GetBaseException());
    }

    private sealed record CaptureCompletionPhase(
        (bool Issued, bool InvocationReturned, bool Settled, TaskStatus ResultStatus, bool? Result, bool ExitCompleted) Facts,
        Exception? ResultFailure);

    private sealed record LateCallbackPhase(CaptureCompletionPhase Start, CaptureCompletionPhase Stop, int ChargedOwners,
        (int Roots, int Copies, int Releases, int RootFrees, int LiveRoots, int LiveBlocks, int Invokes, int Returns) RuntimeEffects,
        (int StartSelectors, int StopSelectors, int NativeAttempts, int Starts, int Stops) NativeEffects,
        (int Pushes, int Pops, int Removes, int Barriers, int ObjectsReleased, int QueuesReleased, int SourceReleased,
            int SourceOwnersBeforeBorrowedTeardown, bool PhysicalDrained) CleanupEffects,
        bool NativeCopyReady, bool NativeCopyHeld, int NativeCopyReleaseAttempts,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) StartPrimitiveFacts,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies)? StopPrimitiveFacts,
        Exception? StartPrimitiveFailure, Exception? StopPrimitiveFailure, nint[] ObjectReleases);

    private sealed record LateCallbackGraph(OutOfMemoryException Fatal, Exception? StartFailure, Exception? PendingWatchdogFailure,
        Exception? PendingFirstDisposeFailure, Exception? PendingRepeatedDisposeFailure, bool CallbackJoined,
        Exception? CallbackFailure, Exception? DrainFailure, Exception? FinalFirstDisposeFailure,
        Exception? FinalRepeatedDisposeFailure, LateCallbackPhase Pending, LateCallbackPhase Final,
        nint Stream, nint Output, nint Configuration, (int Ordinal, nint Token, int Thread)[] PushEffects,
        (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int Invokes, int Returns) RawTeardownEffects);

    [Fact]
    public void StopInvocationFatalWithoutCallbackWaitsForRealLateCallbackAndRetiresNativeHeldCopy()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopLateCallbackGraph observation = CreateStopInvokeWithoutCallbackFailure();
        LateCallbackGraph graph = observation.Graph;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Null(graph.StartFailure);
        Assert.Null(observation.NativeCopyReadyFailure);
        Assert.True(observation.StopFailureEntered);
        Assert.IsType<TimeoutException>(graph.PendingWatchdogFailure);
        Assert.Same(graph.Fatal, graph.PendingFirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.PendingRepeatedDisposeFailure);
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Pending.Start.Facts);
        Assert.Null(graph.Pending.Start.ResultFailure);
        Assert.Equal((true, false, false, TaskStatus.WaitingForActivation, null, false), graph.Pending.Stop.Facts);
        Assert.Null(graph.Pending.Stop.ResultFailure);
        Assert.Equal((2, 3, 1, 1, 1, 1, 1, 1), graph.Pending.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Pending.NativeEffects);
        Assert.Equal((3, 3, 0, 0, 0, 0, 0, 2, false), graph.Pending.CleanupEffects);
        Assert.Equal((before + 1, true, true, 0),
            (graph.Pending.ChargedOwners, graph.Pending.NativeCopyReady, graph.Pending.NativeCopyHeld, graph.Pending.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Pending.StartPrimitiveFacts);
        Assert.True(graph.Pending.StopPrimitiveFacts.HasValue);
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, 1L), graph.Pending.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Empty(graph.Pending.ObjectReleases);
        Assert.True(graph.CallbackJoined);
        Assert.Null(graph.CallbackFailure);
        Assert.Same(graph.Fatal, graph.DrainFailure);
        Assert.True(graph.FinalFirstDisposeFailure is null || ReferenceEquals(graph.Fatal, graph.FinalFirstDisposeFailure));
        Assert.True(graph.FinalRepeatedDisposeFailure is null || ReferenceEquals(graph.Fatal, graph.FinalRepeatedDisposeFailure));
        Assert.Equal((true, true, true, TaskStatus.RanToCompletion, true, true), graph.Final.Start.Facts);
        // Only the real late callback settles Stop and confirms callback exit.
        // A throwing native invocation never gains a returned-handoff fact.
        Assert.Equal((true, false, true, TaskStatus.RanToCompletion, true, true), graph.Final.Stop.Facts);
        Assert.Null(graph.Final.Stop.ResultFailure);
        Assert.Equal((2, 3, 3, 2, 0, 0, 2, 2), graph.Final.RuntimeEffects);
        Assert.Equal((1, 1, 2, 1, 1), graph.Final.NativeEffects);
        Assert.Equal((4, 4, 1, 1, 3, 1, 1, 1, true), graph.Final.CleanupEffects);
        Assert.Equal((before, true, false, 1),
            (graph.Final.ChargedOwners, graph.Final.NativeCopyReady, graph.Final.NativeCopyHeld, graph.Final.NativeCopyReleaseAttempts));
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StartPrimitiveFacts);
        Assert.True(graph.Final.StopPrimitiveFacts.HasValue);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.Final.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.Final.StartPrimitiveFailure);
        Assert.Null(graph.Final.StopPrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.Final.ObjectReleases);
        Assert.Equal(graph.PushEffects, graph.PopEffects);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal((0, 0, 3, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests actual extra heap retain followed by wrapped Stop invocation fatal before a real late one-argument ABI callback.")]
    private static StopLateCallbackGraph CreateStopInvokeWithoutCallbackFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original Stop native handoff after-effect fatal before callback.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StopInvocationFailureBeforeCallbackAfterEffect = new AggregateException(new IOException("Stop handoff wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Exception? startFailure = Record.Exception(() => capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        Task<bool> drain = capture.StopAndDrainAsync().AsTask();
        Exception? nativeCopyReadyFailure = Record.Exception(() => operations.NativeHeldCopyReady.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
        // The retain signal precedes the throw. Observe the real catch and
        // pool unwind under the production gate before testing pending Dispose.
        // This read-only observation creates no result/exit or ownership fact.
        bool stopFailureEntered = SpinWait.SpinUntil(() => ReadStopInvocationFailureEntered(capture, fatal), Timeout);
        Exception? pendingWatchdogFailure = Record.Exception(() => drain.WaitAsync(TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult());
        Exception? pendingFirst = Record.Exception(capture.Dispose);
        Exception? pendingRepeated = Record.Exception(capture.Dispose);
        LateCallbackPhase pending = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        nint nativeOwnedPointer = Volatile.Read(ref operations.NativeHeldCompletion);
        Exception? callbackFailure = null;
        var callback = new Thread(() =>
        {
            try { runtime.Invoke(nativeOwnedPointer, 0); }
            catch (Exception exception) { callbackFailure = exception; }
        })
        { IsBackground = true, Name = "Flowspan actual late Stop completion ABI" };
        bool callbackStarted = false;
        bool callbackJoined = false;
        try
        {
            callback.Start();
            callbackStarted = true;
            callbackJoined = callback.Join(Timeout);
        }
        finally
        {
            if (callbackStarted) { callback.Join(); }
        }

        Exception? drainFailure = Record.Exception(() => drain.WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? finalFirst = Record.Exception(capture.Dispose);
        Exception? finalRepeated = Record.Exception(capture.Dispose);
        LateCallbackPhase final = ReadLateCallbackPhase(capture, operations, runtime, source.State, startCompletion);
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        runtime.Dispose();
        var raw = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(fatal, startFailure, pendingWatchdogFailure, pendingFirst, pendingRepeated,
            callbackJoined, callbackFailure, drainFailure, finalFirst, finalRepeated, pending, final,
            operations.Stream, operations.Output, operations.Configuration, pushes, pops, raw), nativeCopyReadyFailure, stopFailureEntered);
    }

    private static bool ReadStopInvocationFailureEntered(IMacOSRemoteWindowNativeCapture capture, OutOfMemoryException fatal)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        lock (Field("gate"))
        {
            return ReferenceEquals(Field("fatalFailure"), fatal)
                && (bool)Field("stopPoolPopConfirmed") && (bool)Field("unsafeFailure");
        }
    }

    private sealed record StopLateCallbackGraph(LateCallbackGraph Graph, Exception? NativeCopyReadyFailure, bool StopFailureEntered);

    [Fact]
    public void StopConfirmedAcquiredButUnissuedCallerIsReleasedOnceWithoutInventingStopProof()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopZeroPoolGraph graph = CreateStopZeroPoolPushFailure(repeatStop: true);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((2, 2, 0, 0), (graph.CleanupEffects.CallerReleases, graph.CleanupEffects.RootFrees,
            graph.CleanupEffects.LiveRoots, graph.CleanupEffects.LiveBlocks));
        Assert.Equal((true, false, 1, 0, 1, 1, 0, 1, 1), graph.InvocationEffects);
        Assert.Null(graph.StartFailure);
        Assert.Null(graph.StopFailure);
        Assert.False(graph.RepeatedStopResult);
        Assert.Null(graph.RepeatedStopFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.IsType<InvalidOperationException>(graph.RepeatedDisposeFailure);
        Assert.Equal((false, false, false, null, false), graph.LocalStopFacts);
        Assert.False(graph.StopSettled);
        Assert.Equal((2, 2, 2, 2, 0, 0, 0, 0, 0, 0, 3, 2, 2, false), graph.CleanupEffects);
        Assert.Equal((1, 0, 2), graph.SourceEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StopPrimitiveFacts);
        Assert.Null(graph.StartPrimitiveFailure);
        Assert.Null(graph.StopPrimitiveFailure);
        Assert.Empty(graph.ObjectReleases);
        Assert.Equal(3, graph.PushEffects.Length);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)1, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)0), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Token != 0)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, 2, 2, 1, 1), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.StartCompletion.IsAlive, graph.StartPrimitive.IsAlive,
                graph.StopCompletion.IsAlive, graph.StopPrimitive.IsAlive, graph.Operations.IsAlive,
                graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [Fact]
    public void RemoveOutputUnknownPoolPushRetainsOriginalFatalAndDependentOwners()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        InvocationUnknownPoolGraph graph = CreateInvocationUnknownPoolPushFailure(4, "remove");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((true, false, 1, 1, 2, 1, 1, 2, 2), graph.InvocationEffects);
        Assert.Null(graph.StartFailure);
        Assert.Same(graph.Fatal, graph.StopFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, false, false, false), graph.PoolFacts);
        Assert.Equal((2, 2, 2, 2, 0, 0, 0, 0, 0, 0, 4, 3, 3, false), graph.CleanupEffects);
        Assert.Equal((1, 0, 2), graph.SourceEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StartPrimitiveFacts);
        Assert.True(graph.StopPrimitiveFacts.HasValue);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StopPrimitiveFacts.GetValueOrDefault());
        Assert.Null(graph.StartPrimitiveFailure);
        Assert.Null(graph.StopPrimitiveFailure);
        Assert.Empty(graph.ObjectReleases);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)1, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)1), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        Assert.Equal((4, (nint)1), (graph.PushEffects[3].Ordinal, graph.PushEffects[3].Token));
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Ordinal != 4)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.StartCompletion.IsAlive, graph.StartPrimitive.IsAlive,
                graph.StopCompletion?.IsAlive ?? false, graph.StopPrimitive?.IsAlive ?? false,
                graph.Operations.IsAlive, graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [Fact]
    public void StopUnknownPoolPushRetainsOriginalFatalWithoutInventingStopProof()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        InvocationUnknownPoolGraph graph = CreateInvocationUnknownPoolPushFailure(3, "stop");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((true, false, 1, 0, 1, 1, 0, 1, 1), graph.InvocationEffects);
        Assert.Null(graph.StartFailure);
        Assert.Same(graph.Fatal, graph.StopFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, false, false, false), graph.PoolFacts);
        Assert.Equal((2, 2, 0, 0, 0, 0, 3, 2, 2, false),
            (graph.CleanupEffects.Roots, graph.CleanupEffects.Copies, graph.CleanupEffects.Removes,
                graph.CleanupEffects.Barriers, graph.CleanupEffects.ObjectsReleased, graph.CleanupEffects.QueuesReleased,
                graph.CleanupEffects.Pushes, graph.CleanupEffects.Pops, graph.CleanupEffects.PopAttempts, graph.CleanupEffects.PhysicalDrained));
        Assert.Equal((1, 0, 2), graph.SourceEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StartPrimitiveFacts);
        Assert.True(graph.StopPrimitiveFacts.HasValue);
        var stop = graph.StopPrimitiveFacts.GetValueOrDefault();
        Assert.Equal((true, true, true, true, true, 1L),
            (stop.AcquisitionAttempted, stop.RootAttempted, stop.RootConfirmed, stop.CopyAttempted, stop.CopyConfirmed, stop.PhysicalCopies));
        // Observe the unissued known caller without requiring its existing debt
        // to remain forever. Cleanup acceptance belongs to a separate tracer.
        int confirmedCallers = 1 + (stop.CallerReleased ? 1 : 0);
        Assert.Equal((stop.CallerReleased, stop.CallerReleased, stop.CallerReleased, stop.CallerReleased),
            (stop.NativeRetired, stop.ManagedDrained, stop.RootFreeAttempted, stop.RootFreeConfirmed));
        Assert.Equal((confirmedCallers, confirmedCallers, 2 - confirmedCallers, 2 - confirmedCallers),
            (graph.CleanupEffects.CallerReleases, graph.CleanupEffects.RootFrees, graph.CleanupEffects.LiveRoots, graph.CleanupEffects.LiveBlocks));
        Assert.Null(graph.StartPrimitiveFailure);
        Assert.Null(graph.StopPrimitiveFailure);
        Assert.Empty(graph.ObjectReleases);
        Assert.Equal(3, graph.PushEffects.Length);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)1, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)1), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Ordinal != 3)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, confirmedCallers, confirmedCallers, 1, 1), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.StartCompletion.IsAlive, graph.StartPrimitive.IsAlive,
                graph.StopCompletion?.IsAlive ?? false, graph.StopPrimitive?.IsAlive ?? false,
                graph.Operations.IsAlive, graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
        Assert.Equal((false, false, false, null, false),
            ReadUnissuedStopFacts(Assert.IsAssignableFrom<IMacOSRemoteWindowNativeCapture>(graph.Capture.Target)));
    }

    [Fact]
    public void StartUnknownPoolPushRetainsOriginalFatalAndCleansKnownOwnersOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        InvocationUnknownPoolGraph graph = CreateInvocationUnknownPoolPushFailure(2, "start");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((false, false, 0, 0, 0, 0, 0, 0, 0), graph.InvocationEffects);
        Assert.Same(graph.Fatal, graph.StartFailure);
        Assert.Same(graph.Fatal, graph.StopFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, false, false, false), graph.PoolFacts);
        Assert.Equal((1, 1, 1, 1, 0, 0, 1, 1, 3, 1, 3, 2, 2, true), graph.CleanupEffects);
        Assert.Equal((1, 1, 1), graph.SourceEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StartPrimitiveFacts);
        Assert.Null(graph.StartPrimitiveFailure);
        Assert.Null(graph.StopCompletion);
        Assert.Null(graph.StopPrimitive);
        Assert.Null(graph.StopPrimitiveFacts);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.ObjectReleases);
        Assert.Equal(3, graph.PushEffects.Length);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)1, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)1), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        // The fixture observed a nonzero effect for ordinal2, but Capture
        // received no token. Match only independently returned pool scopes.
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Ordinal != 2)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, 1, 1, 0, 0), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.StartCompletion.IsAlive, graph.StartPrimitive.IsAlive,
                graph.Operations.IsAlive, graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests wrapped fatal after a simulated nonzero invocation pool acquisition effect without a returned token.")]
    private static InvocationUnknownPoolGraph CreateInvocationUnknownPoolPushFailure(int ordinal, string scope)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original " + scope + " pool push after-effect fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            PushFailureOrdinal = ordinal,
            PushFailureAfterEffect = new AggregateException(new IOException(scope + " pool push wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        bool stopResult = false;
        Exception? stopFailure = Record.Exception(() =>
            stopResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference? stopCompletion = ReferenceEquals(operations.Completion, startCompletion) ? null : operations.Completion;
        WeakReference? stopPrimitive = stopCompletion is null ? null : operations.Primitive;
        Exception? first = Record.Exception(capture.Dispose);
        Exception? repeated = Record.Exception(capture.Dispose);

        var startOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target);
        var stopOwner = stopCompletion is null ? null : Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopCompletion.Target);
        var startFacts = ReadZeroPoolPrimitiveFacts(startOwner);
        var stopFacts = stopOwner is null ? ((bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, long)?)null : ReadZeroPoolPrimitiveFacts(stopOwner);
        var poolFacts = ReadUnknownPoolFacts(capture, scope);
        var invocationEffects = (startResult, stopResult, operations.StartSelectorAttempts, operations.StopSelectorAttempts,
            operations.NativeInvocationAttempts, operations.StartInvocations, operations.StopInvocations,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        var cleanupEffects = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.LiveRoots, runtime.LiveBlocks, operations.RemoveCalls, operations.DrainCalls,
            operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts,
            operations.PoolsPushed, operations.PoolsPopped, operations.PoolPopAttempts, capture.IsDrained);
        var sourceEffects = (source.State.RetainCalls, source.State.RetainedReleaseAttempts, source.State.OwnerCount);
        nint[] objectReleases = operations.ObjectReleaseAttempts.ToArray();
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        Exception? startPrimitiveFailure = startOwner.FirstFailure;
        Exception? stopPrimitiveFailure = stopOwner?.FirstFailure;
        runtime.Dispose();
        var rawTeardownEffects = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(capture), startCompletion, startPrimitive, stopCompletion, stopPrimitive,
            new(operations), new(source.State), new(sampleMarker), new(unavailableMarker), fatal,
            startFailure, stopFailure, first, repeated, Environment.CurrentManagedThreadId,
            operations.Stream, operations.Output, operations.Configuration, invocationEffects, cleanupEffects,
            sourceEffects, poolFacts, startFacts, stopFacts, startPrimitiveFailure, stopPrimitiveFailure,
            objectReleases, pushes, pops, rawTeardownEffects);
    }

    private sealed record InvocationUnknownPoolGraph(WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference? StopCompletion, WeakReference? StopPrimitive, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker, OutOfMemoryException Fatal,
        Exception? StartFailure, Exception? StopFailure, Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure,
        int CreatorThread, nint Stream, nint Output, nint Configuration,
        (bool StartResult, bool StopResult, int StartSelectors, int StopSelectors, int NativeAttempts,
            int Starts, int Stops, int NativeCallbacks, int NativeCallbackReturns) InvocationEffects,
        (int Roots, int Copies, int CallerReleases, int RootFrees, int LiveRoots, int LiveBlocks,
            int Removes, int Barriers, int ObjectsReleased, int QueuesReleased, int Pushes, int Pops,
            int PopAttempts, bool PhysicalDrained) CleanupEffects,
        (int Retains, int RetainedReleases, int OwnersBeforeBorrowedTeardown) SourceEffects,
        (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed) PoolFacts,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) StartPrimitiveFacts,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies)? StopPrimitiveFacts,
        Exception? StartPrimitiveFailure, Exception? StopPrimitiveFailure, nint[] ObjectReleases,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int InvokeAttempts, int InvokeReturns) RawTeardownEffects);

    [Fact]
    public void ConstructorUnknownPoolPushRetainsOriginalFatalAndShellBeyondFactorySlotReplacement()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ConstructorUnknownPoolGraph graph = CreateConstructorUnknownPoolPushFailure();
        bool replaced = ReplaceConstructorFailureSlot();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((false, true, 0, 1, 0, 0, 0, 0, 0, 0, 1), graph.Effects);
        Assert.Same(graph.Fatal, graph.FactoryFailure);
        Assert.Null(graph.StopFailure);
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
        Assert.Equal((true, false, false, false), graph.PoolFacts);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread) }, graph.PushEffects);
        Assert.Empty(graph.PopEffects);
        Assert.Equal((0, 0, 0, 0, 0, 0, 0), graph.RuntimeEffects);
        Assert.Equal((before + 2, true, true, true, true, true, replaced),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount, graph.Capture?.IsAlive ?? false,
                graph.Operations.IsAlive, graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive, true));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests wrapped fatal after a simulated nonzero pool acquisition effect without a returned token.")]
    private static ConstructorUnknownPoolGraph CreateConstructorUnknownPoolPushFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original constructor pool push after-effect fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            PushFailureOrdinal = 1,
            PushFailureAfterEffect = new AggregateException(new IOException("Constructor pool push wrapper.", fatal)),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture? returned = null;
        Exception? factoryFailure = Record.Exception(() => returned = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker)));
        WeakReference? observation = returned is not null ? new(returned) : ObserveFactorySlot(operations);
        IMacOSRemoteWindowNativeCapture? capture = returned ?? observation?.Target as IMacOSRemoteWindowNativeCapture;
        bool stopped = false;
        Exception? stopFailure = null;
        Exception? first = null;
        Exception? repeated = null;
        var poolFacts = (false, false, false, false);
        if (capture is not null)
        {
            stopFailure = Record.Exception(() => stopped = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
            first = Record.Exception(capture.Dispose);
            repeated = Record.Exception(capture.Dispose);
            poolFacts = ReadUnknownPoolFacts(capture, "constructor");
        }

        var effects = (returned is not null, stopped, operations.ConstructorScopedBodyCalls,
            operations.PoolsPushed, operations.PoolsPopped, operations.PoolPopAttempts,
            operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts,
            operations.RemoveCalls, operations.DrainCalls, source.State.RetainedReleaseAttempts);
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        runtime.Dispose();
        var runtimeEffects = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.LiveRoots, runtime.LiveBlocks);
        return new(observation, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker),
            fatal, factoryFailure, stopFailure, first, repeated, Environment.CurrentManagedThreadId,
            effects, poolFacts, pushes, pops, runtimeEffects);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed)
        ReadUnknownPoolFacts(IMacOSRemoteWindowNativeCapture capture, string scope)
    {
        bool Field(string suffix) => (bool)capture.GetType()
            .GetField(scope + "Pool" + suffix, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        return (Field("PushAttempted"), Field("PushConfirmed"), Field("PopAttempted"), Field("PopConfirmed"));
    }

    private sealed record ConstructorUnknownPoolGraph(WeakReference? Capture, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker, OutOfMemoryException Fatal,
        Exception? FactoryFailure, Exception? StopFailure, Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure,
        int CreatorThread,
        (bool FactoryReturned, bool StopResult, int ScopedBody, int Pushes, int Pops, int PopAttempts,
            int ObjectsReleased, int QueuesReleased, int Removes, int Barriers, int SourceReleased) Effects,
        (bool PushAttempted, bool PushConfirmed, bool PopAttempted, bool PopConfirmed) PoolFacts,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Copies, int CallerReleases, int RootFrees, int NativeCallbacks, int LiveRoots, int LiveBlocks) RuntimeEffects);

    [Fact]
    public void RemoveOutputZeroPoolPushRejectsNativeBodyAndRetainsDependentOwners()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        RemoveZeroPoolGraph graph = CreateRemoveOutputZeroPoolPushFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((true, false, 1, 1, 2, 1, 1, 2, 2, 0), graph.InvocationEffects);
        Assert.Null(graph.StartFailure);
        Assert.Null(graph.StopFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.IsType<InvalidOperationException>(graph.RepeatedDisposeFailure);
        Assert.Equal((2, 2, 2, 2, 0, 0, 0, 0, 0, 4, 3, 3, false), graph.CleanupEffects);
        Assert.Equal((1, 0, 2), graph.SourceEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StopPrimitiveFacts);
        Assert.Null(graph.StartPrimitiveFailure);
        Assert.Null(graph.StopPrimitiveFailure);
        Assert.Empty(graph.ObjectReleases);
        Assert.Equal(4, graph.PushEffects.Length);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)1, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)1), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        Assert.Equal((4, (nint)0), (graph.PushEffects[3].Ordinal, graph.PushEffects[3].Token));
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Token != 0)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, 2, 2, 2, 2), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.StartCompletion.IsAlive, graph.StartPrimitive.IsAlive,
                graph.StopCompletion.IsAlive, graph.StopPrimitive.IsAlive, graph.Operations.IsAlive,
                graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RemoveZeroPoolGraph CreateRemoveOutputZeroPoolPushFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime) { ZeroPoolPushOrdinal = 4 };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        bool stopResult = false;
        // RED's incorrect removal success receives both actual Dispose attempts
        // and raw teardown before the outer refusal/owner/GC assertions.
        Exception? stopFailure = Record.Exception(() =>
            stopResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? first = Record.Exception(capture.Dispose);
        Exception? repeated = Record.Exception(capture.Dispose);

        var startOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target);
        var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopCompletion.Target);
        var startFacts = ReadZeroPoolPrimitiveFacts(startOwner);
        var stopFacts = ReadZeroPoolPrimitiveFacts(stopOwner);
        var invocationEffects = (startResult, stopResult, operations.StartSelectorAttempts, operations.StopSelectorAttempts,
            operations.NativeInvocationAttempts, operations.StartInvocations, operations.StopInvocations,
            runtime.InvokeAttempts, runtime.InvokeReturns, operations.RemoveCalls);
        var cleanupEffects = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.LiveRoots, runtime.LiveBlocks, operations.DrainCalls,
            operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts,
            operations.PoolsPushed, operations.PoolsPopped, operations.PoolPopAttempts, capture.IsDrained);
        var sourceEffects = (source.State.RetainCalls, source.State.RetainedReleaseAttempts, source.State.OwnerCount);
        nint[] objectReleases = operations.ObjectReleaseAttempts.ToArray();
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        Exception? startPrimitiveFailure = startOwner.FirstFailure;
        Exception? stopPrimitiveFailure = stopOwner.FirstFailure;
        runtime.Dispose();
        var rawTeardownEffects = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(capture), startCompletion, startPrimitive, stopCompletion, stopPrimitive,
            new(operations), new(source.State), new(sampleMarker), new(unavailableMarker),
            startFailure, stopFailure, first, repeated, Environment.CurrentManagedThreadId,
            invocationEffects, cleanupEffects, sourceEffects, startFacts, stopFacts,
            startPrimitiveFailure, stopPrimitiveFailure, objectReleases, pushes, pops, rawTeardownEffects);
    }

    private sealed record RemoveZeroPoolGraph(WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StartFailure, Exception? StopFailure,
        Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure, int CreatorThread,
        (bool StartResult, bool StopResult, int StartSelectors, int StopSelectors, int NativeAttempts,
            int Starts, int Stops, int NativeCallbacks, int NativeCallbackReturns, int Removes) InvocationEffects,
        (int Roots, int Copies, int CallerReleases, int RootFrees, int LiveRoots, int LiveBlocks,
            int Barriers, int ObjectsReleased, int QueuesReleased, int Pushes, int Pops,
            int PopAttempts, bool PhysicalDrained) CleanupEffects,
        (int Retains, int RetainedReleases, int OwnersBeforeBorrowedTeardown) SourceEffects,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) StartPrimitiveFacts,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) StopPrimitiveFacts,
        Exception? StartPrimitiveFailure, Exception? StopPrimitiveFailure, nint[] ObjectReleases,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int InvokeAttempts, int InvokeReturns) RawTeardownEffects);

    [Fact]
    public void StopZeroPoolPushRejectsNativeBodyAndRetainsUnconfirmedCapture()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopZeroPoolGraph graph = CreateStopZeroPoolPushFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((true, false, 1, 0, 1, 1, 0, 1, 1), graph.InvocationEffects);
        Assert.Null(graph.StartFailure);
        Assert.Null(graph.StopFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.IsType<InvalidOperationException>(graph.RepeatedDisposeFailure);
        Assert.Equal((false, false, false, null, false), graph.LocalStopFacts);
        Assert.Equal((2, 2, 0, 0, 0, 0, 3, 2, 2, false),
            (graph.CleanupEffects.Roots, graph.CleanupEffects.Copies, graph.CleanupEffects.Removes,
                graph.CleanupEffects.Barriers, graph.CleanupEffects.ObjectsReleased, graph.CleanupEffects.QueuesReleased,
                graph.CleanupEffects.Pushes, graph.CleanupEffects.Pops, graph.CleanupEffects.PopAttempts, graph.CleanupEffects.PhysicalDrained));
        Assert.Equal((1, 0, 2), graph.SourceEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.StartPrimitiveFacts);
        var stop = graph.StopPrimitiveFacts;
        Assert.Equal((true, true, true, true, true, 1L),
            (stop.AcquisitionAttempted, stop.RootAttempted, stop.RootConfirmed, stop.CopyAttempted, stop.CopyConfirmed, stop.PhysicalCopies));
        // This Fact is refusal/graph coverage, not a requirement that a known
        // unissued caller remain unreleased. Its cleanup has a separate tracer.
        int confirmedCallers = 1 + (stop.CallerReleased ? 1 : 0);
        Assert.Equal((stop.CallerReleased, stop.CallerReleased, stop.CallerReleased, stop.CallerReleased),
            (stop.NativeRetired, stop.ManagedDrained, stop.RootFreeAttempted, stop.RootFreeConfirmed));
        Assert.Equal((confirmedCallers, confirmedCallers, 2 - confirmedCallers, 2 - confirmedCallers),
            (graph.CleanupEffects.CallerReleases, graph.CleanupEffects.RootFrees, graph.CleanupEffects.LiveRoots, graph.CleanupEffects.LiveBlocks));
        Assert.Null(graph.StartPrimitiveFailure);
        Assert.Null(graph.StopPrimitiveFailure);
        Assert.Empty(graph.ObjectReleases);
        Assert.Equal(3, graph.PushEffects.Length);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)1, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)0), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Token != 0)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, confirmedCallers, confirmedCallers, 1, 1), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.StartCompletion.IsAlive, graph.StartPrimitive.IsAlive,
                graph.StopCompletion.IsAlive, graph.StopPrimitive.IsAlive, graph.Operations.IsAlive,
                graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StopZeroPoolGraph CreateStopZeroPoolPushFailure(bool repeatStop = false)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime) { ZeroPoolPushOrdinal = 3 };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        bool stopResult = false;
        Exception? stopFailure = Record.Exception(() =>
            stopResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopCompletion = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        bool repeatedStopResult = false;
        Exception? repeatedStopFailure = null;
        if (repeatStop)
        {
            repeatedStopFailure = Record.Exception(() =>
                repeatedStopResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        }
        Exception? first = Record.Exception(capture.Dispose);
        Exception? repeated = Record.Exception(capture.Dispose);

        var startOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target);
        var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopCompletion.Target);
        var startFacts = ReadZeroPoolPrimitiveFacts(startOwner);
        var stopFacts = ReadZeroPoolPrimitiveFacts(stopOwner);
        var localStopFacts = ReadUnissuedStopFacts(capture);
        bool stopSettled = (bool)capture.GetType().GetField("stopSettled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        var invocationEffects = (startResult, stopResult, operations.StartSelectorAttempts, operations.StopSelectorAttempts,
            operations.NativeInvocationAttempts, operations.StartInvocations, operations.StopInvocations,
            runtime.InvokeAttempts, runtime.InvokeReturns);
        var cleanupEffects = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.LiveRoots, runtime.LiveBlocks, operations.RemoveCalls, operations.DrainCalls,
            operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts,
            operations.PoolsPushed, operations.PoolsPopped, operations.PoolPopAttempts, capture.IsDrained);
        var sourceEffects = (source.State.RetainCalls, source.State.RetainedReleaseAttempts, source.State.OwnerCount);
        nint[] objectReleases = operations.ObjectReleaseAttempts.ToArray();
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        Exception? startPrimitiveFailure = startOwner.FirstFailure;
        Exception? stopPrimitiveFailure = stopOwner.FirstFailure;
        // Remove only fixture-owned raw allocations. Do not invent Stop result,
        // callback exit, native handoff return or primitive retirement.
        runtime.Dispose();
        var rawTeardownEffects = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(capture), startCompletion, startPrimitive, stopCompletion, stopPrimitive,
            new(operations), new(source.State), new(sampleMarker), new(unavailableMarker),
            startFailure, stopFailure, first, repeated, repeatedStopResult, repeatedStopFailure, stopSettled, Environment.CurrentManagedThreadId,
            invocationEffects, localStopFacts, cleanupEffects, sourceEffects, startFacts, stopFacts,
            startPrimitiveFailure, stopPrimitiveFailure, objectReleases, pushes, pops, rawTeardownEffects);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
        bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted, bool RootFreeConfirmed, long PhysicalCopies)
        ReadZeroPoolPrimitiveFacts(MacOSRemoteWindowCaptureCompletion owner) =>
        (owner.Primitive.AcquisitionAttempted, owner.Primitive.RootAllocationAttempted, owner.Primitive.RootAllocationConfirmed,
            owner.Primitive.CopyAttempted, owner.Primitive.CopyConfirmed, owner.IsReleased,
            owner.NativeCaptureRetirement.IsCompletedSuccessfully, owner.ManagedInvocationDrain.IsCompletedSuccessfully,
            owner.Primitive.RootReleaseAttempted, owner.Primitive.RootReleaseConfirmed, owner.Primitive.PhysicalCaptureCopyCount);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool Issued, bool InvocationReturned, bool ResultCompleted, bool? Result, bool ExitCompleted)
        ReadUnissuedStopFacts(IMacOSRemoteWindowNativeCapture capture)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        var result = Assert.IsType<TaskCompletionSource<bool>>(Field("stopCompletion")).Task;
        var exit = Assert.IsType<TaskCompletionSource<bool>>(Field("stopCallbackExited")).Task;
        return ((bool)Field("stopIssued"), (bool)Field("stopInvocationReturned"),
            result.IsCompletedSuccessfully, result.IsCompletedSuccessfully ? result.Result : null, exit.IsCompletedSuccessfully);
    }

    private sealed record StopZeroPoolGraph(WeakReference Capture, WeakReference StartCompletion, WeakReference StartPrimitive,
        WeakReference StopCompletion, WeakReference StopPrimitive, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StartFailure, Exception? StopFailure,
        Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure,
        bool RepeatedStopResult, Exception? RepeatedStopFailure, bool StopSettled, int CreatorThread,
        (bool StartResult, bool StopResult, int StartSelectors, int StopSelectors, int NativeAttempts,
            int Starts, int Stops, int NativeCallbacks, int NativeCallbackReturns) InvocationEffects,
        (bool Issued, bool InvocationReturned, bool ResultCompleted, bool? Result, bool ExitCompleted) LocalStopFacts,
        (int Roots, int Copies, int CallerReleases, int RootFrees, int LiveRoots, int LiveBlocks,
            int Removes, int Barriers, int ObjectsReleased, int QueuesReleased, int Pushes, int Pops,
            int PopAttempts, bool PhysicalDrained) CleanupEffects,
        (int Retains, int RetainedReleases, int OwnersBeforeBorrowedTeardown) SourceEffects,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) StartPrimitiveFacts,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) StopPrimitiveFacts,
        Exception? StartPrimitiveFailure, Exception? StopPrimitiveFailure, nint[] ObjectReleases,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int InvokeAttempts, int InvokeReturns) RawTeardownEffects);

    [Fact]
    public void StartZeroPoolPushRejectsNativeBodyAndRetainsCaptureAfterKnownCleanup()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StartZeroPoolGraph graph = CreateStartZeroPoolPushFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((false, false, 0, 0, 0, 0, 0, 0), graph.InvocationEffects);
        Assert.IsType<InvalidOperationException>(graph.StartFailure);
        Assert.Null(graph.StopFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.Same(graph.FirstDisposeFailure, graph.RepeatedDisposeFailure);
        Assert.Equal((false, false, true, false, true), graph.LocalStartFacts);
        Assert.Equal((1, 1, 1, 1, 1, 1, 3, 2, 2, true), graph.CleanupEffects);
        Assert.Equal((true, true, true, true, true, true, true, true, true, true, 1L), graph.PrimitiveFacts);
        Assert.Null(graph.PrimitiveFailure);
        Assert.Equal(new[] { graph.Stream, graph.Output, graph.Configuration }, graph.ObjectReleases);
        Assert.Equal(new[] { (1, (nint)1, graph.CreatorThread), (2, (nint)0, graph.CreatorThread) }, graph.PushEffects[..2]);
        Assert.Equal((3, (nint)1), (graph.PushEffects[2].Ordinal, graph.PushEffects[2].Token));
        // A zero push has no matching pop. Match only the two valid scopes,
        // using actual token/thread pairs rather than push/pop ordinals.
        Assert.Equal(graph.PushEffects.Where(static effect => effect.Token != 0)
            .Select(static effect => (effect.Token, effect.Thread)),
            graph.PopEffects.Select(static effect => (effect.Token, effect.Thread)));
        Assert.Equal((0, 0, 1, 1, 0, 0), graph.RawTeardownEffects);
        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.Completion.IsAlive, graph.Primitive.IsAlive,
                graph.Operations.IsAlive, graph.Source.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartZeroPoolGraph CreateStartZeroPoolPushFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime) { ZeroPoolPushOrdinal = 2 };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        bool startResult = false;
        Exception? startFailure = Record.Exception(() =>
            startResult = capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startCompletion = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        bool stopResult = false;
        // RED's incorrect native Start success still receives real Stop and
        // both Dispose attempts before any behavior/counter/outer GC assertion.
        Exception? stopFailure = Record.Exception(() =>
            stopResult = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? first = Record.Exception(capture.Dispose);
        Exception? repeated = Record.Exception(capture.Dispose);

        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startCompletion.Target);
        var primitiveFacts = (owner.Primitive.AcquisitionAttempted, owner.Primitive.RootAllocationAttempted,
            owner.Primitive.RootAllocationConfirmed, owner.Primitive.CopyAttempted, owner.Primitive.CopyConfirmed,
            owner.IsReleased, owner.NativeCaptureRetirement.IsCompletedSuccessfully,
            owner.ManagedInvocationDrain.IsCompletedSuccessfully, owner.Primitive.RootReleaseAttempted,
            owner.Primitive.RootReleaseConfirmed, owner.Primitive.PhysicalCaptureCopyCount);
        var localStartFacts = ReadUnissuedStartFacts(capture);
        var invocationEffects = (startResult, stopResult, operations.StartSelectorAttempts, operations.StopSelectorAttempts,
            operations.NativeInvocationAttempts, operations.StartInvocations, operations.StopInvocations, runtime.InvokeAttempts);
        var cleanupEffects = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, operations.RemoveCalls, operations.DrainCalls, operations.PoolsPushed,
            operations.PoolsPopped, operations.PoolPopAttempts, capture.IsDrained);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.Equal(1, source.State.OwnerCount);
        nint[] objectReleases = operations.ObjectReleaseAttempts.ToArray();
        var pushes = operations.PoolPushEffects.ToArray();
        var pops = operations.PoolPopEffects.ToArray();
        Exception? primitiveFailure = owner.FirstFailure;
        runtime.Dispose();
        var rawTeardownEffects = (runtime.LiveRoots, runtime.LiveBlocks, runtime.ReleaseAttempts,
            runtime.RootFreeAttempts, runtime.InvokeAttempts, runtime.InvokeReturns);
        return new(new(capture), startCompletion, startPrimitive, new(operations), new(source.State),
            new(sampleMarker), new(unavailableMarker), startFailure, stopFailure, first, repeated,
            Environment.CurrentManagedThreadId, operations.Stream, operations.Output, operations.Configuration,
            invocationEffects, localStartFacts, cleanupEffects, primitiveFacts, primitiveFailure, objectReleases,
            pushes, pops, rawTeardownEffects);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool Issued, bool InvocationReturned, bool ResultCompleted, bool? Result, bool ExitCompleted)
        ReadUnissuedStartFacts(IMacOSRemoteWindowNativeCapture capture)
    {
        object Field(string name) => capture.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        var result = Assert.IsType<TaskCompletionSource<bool>>(Field("startCompletion")).Task;
        var exit = Assert.IsType<TaskCompletionSource<bool>>(Field("startCallbackExited")).Task;
        // These are local unissued settlement facts, not evidence that any
        // native callback or completed notification actually executed.
        return ((bool)Field("startIssued"), (bool)Field("startInvocationReturned"),
            result.IsCompletedSuccessfully, result.IsCompletedSuccessfully ? result.Result : null, exit.IsCompletedSuccessfully);
    }

    private sealed record StartZeroPoolGraph(WeakReference Capture, WeakReference Completion, WeakReference Primitive,
        WeakReference Operations, WeakReference Source, WeakReference SampleMarker, WeakReference UnavailableMarker,
        Exception? StartFailure, Exception? StopFailure, Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure,
        int CreatorThread, nint Stream, nint Output, nint Configuration,
        (bool StartResult, bool StopResult, int StartSelectors, int StopSelectors, int NativeAttempts,
            int Starts, int Stops, int NativeCallbacks) InvocationEffects,
        (bool Issued, bool InvocationReturned, bool ResultCompleted, bool? Result, bool ExitCompleted) LocalStartFacts,
        (int Roots, int Copies, int CallerReleases, int RootFrees, int Removes, int Barriers,
            int Pushes, int Pops, int PopAttempts, bool PhysicalDrained) CleanupEffects,
        (bool AcquisitionAttempted, bool RootAttempted, bool RootConfirmed, bool CopyAttempted, bool CopyConfirmed,
            bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, long PhysicalCopies) PrimitiveFacts,
        Exception? PrimitiveFailure, nint[] ObjectReleases,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects,
        (int Roots, int Blocks, int Releases, int RootFrees, int InvokeAttempts, int InvokeReturns) RawTeardownEffects);

    [Fact]
    public void ConstructorZeroPoolPushRejectsScopedBodyAndRetainsShellBeyondFactorySlotReplacement()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ConstructorZeroPoolGraph graph = CreateConstructorZeroPoolPushFailure();
        bool replaced = ReplaceConstructorFailureSlot();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((false, 0, 1, 0, 0, 0, 0, 0, 0, 1), graph.Effects);
        Assert.Equal((before + 2, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture?.IsAlive ?? false, graph.Operations.IsAlive, graph.Source.IsAlive,
                graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive, replaced));
        Assert.IsType<InvalidOperationException>(graph.FactoryFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.Same(graph.FirstDisposeFailure, graph.RepeatedDisposeFailure);
        Assert.Equal(new[] { (1, (nint)0, graph.CreatorThread) }, graph.PushEffects);
        Assert.Empty(graph.PopEffects);
        Assert.Equal(0, graph.PopAttempts);
        Assert.True(graph.TeardownStopSucceeded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ConstructorZeroPoolGraph CreateConstructorZeroPoolPushFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime) { ZeroPoolPushOrdinal = 1 };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture? returned = null;
        Exception? factoryFailure = Record.Exception(() => returned = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker)));
        bool factoryReturned = returned is not null;
        WeakReference? observation = returned is not null ? new(returned) : ObserveFactorySlot(operations);
        IMacOSRemoteWindowNativeCapture? capture = returned ?? observation?.Target as IMacOSRemoteWindowNativeCapture;
        bool stopped = false;
        Exception? first = null;
        Exception? repeated = null;
        if (capture is not null)
        {
            // RED's incorrect successful construction still receives actual
            // safe Stop/Dispose before any refusal/counter/GC assertion.
            stopped = capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
            first = Record.Exception(capture.Dispose);
            repeated = Record.Exception(capture.Dispose);
        }

        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(1, source.State.OwnerCount);
        Assert.Equal(0, runtime.RootAllocationAttempts);
        Assert.Equal(0, runtime.CopyAttempts);
        Assert.Equal(0, runtime.ReleaseAttempts);
        Assert.Equal(0, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.InvokeAttempts);
        Assert.Equal(0, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        var effects = (factoryReturned, operations.ConstructorScopedBodyCalls, operations.PoolsPushed, operations.PoolsPopped,
            operations.ConfigureCalls, operations.ObjectReleaseAttempts.Count, operations.QueueReleaseAttempts,
            operations.RemoveCalls, operations.DrainCalls, source.State.RetainedReleaseAttempts);
        return new(observation, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker),
            factoryFailure, first, repeated, stopped, Environment.CurrentManagedThreadId, effects,
            operations.PoolPushEffects.ToArray(), operations.PoolPopEffects.ToArray(), operations.PoolPopAttempts);
    }

    private sealed record ConstructorZeroPoolGraph(WeakReference? Capture, WeakReference Operations, WeakReference Source,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? FactoryFailure,
        Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure, bool TeardownStopSucceeded, int CreatorThread,
        (bool FactoryReturned, int ScopedBody, int Pushes, int Pops, int Configure, int ObjectsReleased,
            int QueuesReleased, int Removes, int Barriers, int SourceReleased) Effects,
        (int Ordinal, nint Token, int Thread)[] PushEffects, (int Ordinal, nint Token, int Thread)[] PopEffects, int PopAttempts);

    [Fact]
    public void RemoveOutputBodyFatalThenPoolPopFatalPreservesOriginalFailureAndBothKnownCallers()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        RemoveBodyAndPoolOwnershipGraph graph = CreateRemoveOutputBodyAndPoolPopFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((2, 2, 0, 0, false, true, true, true),
            (graph.Effects.CallerReleases, graph.Effects.RootFrees, graph.Effects.LiveRoots, graph.Effects.LiveBlocks, graph.Effects.Drained,
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.StopFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.FirstDisposeFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.RepeatedDisposeFailure)));
        Assert.Equal((true, true, true, true, true, false, 1L), graph.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, false, 1L), graph.StopPrimitiveFacts);
        Assert.NotSame(graph.Completions.Ownership.Fatal, graph.PoolFatal);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Completions.Ownership.Capture.IsAlive, graph.Completions.Ownership.Completion.IsAlive,
                graph.Completions.Ownership.Primitive.IsAlive, graph.Completions.Ownership.Operations.IsAlive,
                graph.Completions.Ownership.Source.IsAlive, graph.Completions.OtherCompletion.IsAlive,
                graph.Completions.OtherPrimitive.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests unknown removal body fatal A before distinct pop fatal B without fabricating a BOOL/error result.")]
    private static RemoveBodyAndPoolOwnershipGraph CreateRemoveOutputBodyAndPoolPopFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original output removal body fatal A.");
        var poolFatal = new OutOfMemoryException("Subsequent output removal pool pop fatal B.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            RemoveOutputFailureAfterEffect = new AggregateException(new IOException("Output removal body wrapper.", fatal)),
            PopFailureAfterEffect = new AggregateException(new IOException("Output removal pool pop wrapper.", poolFatal)),
            PopFailureOrdinal = 4,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? stopFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target).FirstFailure);
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target).FirstFailure);
        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal((4, 4), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Equal(4, operations.PoolPushEffects.Count);
        Assert.Equal(operations.PoolPushEffects, operations.PoolPopEffects);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(0, operations.DrainCalls);
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.QueueReleaseAttempts);
        Assert.Equal(0, source.State.RetainedReleaseAttempts);
        Assert.False(capture.IsDrained);

        var effects = (runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.LiveRoots, runtime.LiveBlocks, capture.IsDrained);
        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        // No BOOL/error returned from RemoveOutput. Only caller +1 ownership
        // is independent; native/sample owners remain untouched and uncertain.
        return new(new(ownership, stopOwnerObservation, stopPrimitive), new(sampleMarker), new(unavailableMarker), stopFailure, poolFatal,
            effects, ReadCompletionPrimitiveFacts(startOwnerObservation, fatal), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
    }

    private sealed record RemoveBodyAndPoolOwnershipGraph(CompletionOwnershipPairGraph Completions,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StopFailure, OutOfMemoryException PoolFatal,
        (int CallerReleases, int RootFrees, int LiveRoots, int LiveBlocks, bool Drained) Effects,
        (bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, bool OriginalFatal, long PhysicalCopies) StartPrimitiveFacts,
        (bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, bool OriginalFatal, long PhysicalCopies) StopPrimitiveFacts);

    [Fact]
    public void StopInvocationBodyFatalThenPoolPopFatalPreservesOriginalFatalAndCaptureGraph()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopBodyAndPoolOwnershipGraph graph = CreateStopInvocationBodyAndPoolPopFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal((true, true, true, true, true, true, true, true, true),
            (graph.Completions.Ownership.Capture.IsAlive, graph.Completions.Ownership.Completion.IsAlive,
                graph.Completions.Ownership.Primitive.IsAlive, graph.Completions.Ownership.Operations.IsAlive,
                graph.Completions.Ownership.Source.IsAlive, graph.Completions.OtherCompletion.IsAlive,
                graph.Completions.OtherPrimitive.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
        Assert.NotSame(graph.Completions.Ownership.Fatal, graph.PoolFatal);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.StopFailure);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests distinct original Stop body and subsequent after-effect pool pop nested fatal identities.")]
    private static StopBodyAndPoolOwnershipGraph CreateStopInvocationBodyAndPoolPopFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original Stop invocation body fatal A.");
        var poolFatal = new OutOfMemoryException("Subsequent Stop pool pop fatal B.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StopInvocationFailureAfterEffect = new AggregateException(new IOException("Stop invocation body wrapper.", fatal)),
            PopFailureAfterEffect = new AggregateException(new IOException("Stop pool pop wrapper.", poolFatal)),
            PopFailureOrdinal = 3,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Assert.Equal((2, 2), (operations.PoolsPushed, operations.PoolsPopped));
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? stopFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(startOwnerObservation, fatal));
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target).FirstFailure);
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target).FirstFailure);
        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal((4, 4), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Equal(4, operations.PoolPushEffects.Count);
        Assert.Equal(operations.PoolPushEffects, operations.PoolPopEffects);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.True(capture.IsDrained);

        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(new(ownership, stopOwnerObservation, stopPrimitive), new(sampleMarker), new(unavailableMarker), stopFailure, poolFatal);
    }

    private sealed record StopBodyAndPoolOwnershipGraph(CompletionOwnershipPairGraph Completions,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StopFailure, OutOfMemoryException PoolFatal);

    [Fact]
    public void StartInvocationBodyFatalThenPoolPopFatalPreservesOriginalFatalAndCaptureGraph()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StartBodyAndPoolOwnershipGraph graph = CreateStartInvocationBodyAndPoolPopFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal((true, true, true, true, true, true, true, true, true),
            (graph.Completions.Ownership.Capture.IsAlive, graph.Completions.Ownership.Completion.IsAlive,
                graph.Completions.Ownership.Primitive.IsAlive, graph.Completions.Ownership.Operations.IsAlive,
                graph.Completions.Ownership.Source.IsAlive, graph.Completions.OtherCompletion.IsAlive,
                graph.Completions.OtherPrimitive.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive));
        Assert.NotSame(graph.Completions.Ownership.Fatal, graph.PoolFatal);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.StartFailure);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.StopFailure);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests distinct original Start body and subsequent after-effect pool pop nested fatal identities.")]
    private static StartBodyAndPoolOwnershipGraph CreateStartInvocationBodyAndPoolPopFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original Start invocation body fatal A.");
        var poolFatal = new OutOfMemoryException("Subsequent Start pool pop fatal B.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            StartInvocationFailureAfterEffect = new AggregateException(new IOException("Start invocation body wrapper.", fatal)),
            PopFailureAfterEffect = new AggregateException(new IOException("Start pool pop wrapper.", poolFatal)),
            PopFailureOrdinal = 2,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Assert.Equal((1, 1), (operations.PoolsPushed, operations.PoolsPopped));
        Exception? startFailure = Record.Exception(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? stopFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(startOwnerObservation, fatal));
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target).FirstFailure);
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target).FirstFailure);
        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal((4, 4), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Equal(4, operations.PoolPushEffects.Count);
        Assert.Equal(operations.PoolPushEffects, operations.PoolPopEffects);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.True(capture.IsDrained);

        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(new(ownership, stopOwnerObservation, stopPrimitive), new(sampleMarker), new(unavailableMarker),
            startFailure, stopFailure, poolFatal);
    }

    private sealed record StartBodyAndPoolOwnershipGraph(CompletionOwnershipPairGraph Completions,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StartFailure,
        Exception? StopFailure, OutOfMemoryException PoolFatal);

    [Fact]
    public void RemoveOutputPoolPopAfterEffectFatalPreservesConfirmedRemovalAndIndependentCleanup()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        RemovePoolOwnershipGraph graph = CreateRemoveOutputPoolPopAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((2, 2, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1, 1, 1, true), graph.Effects);
        Assert.Equal(graph.ExpectedObjectReleases, graph.ActualObjectReleases);
        Assert.Equal((true, true, true, true, true, false, 1L), graph.StartPrimitiveFacts);
        Assert.Equal((true, true, true, true, true, false, 1L), graph.StopPrimitiveFacts);
        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Completions.Ownership.Capture.IsAlive, graph.Completions.Ownership.Completion.IsAlive,
                graph.Completions.Ownership.Primitive.IsAlive, graph.Completions.Ownership.Operations.IsAlive,
                graph.Completions.Ownership.Source.IsAlive, graph.Completions.OtherCompletion.IsAlive,
                graph.Completions.OtherPrimitive.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive,
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.StopFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.FirstDisposeFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.RepeatedDisposeFailure)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests original nested fatal after actual successful output removal and its pool pop effect.")]
    private static RemovePoolOwnershipGraph CreateRemoveOutputPoolPopAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Output removal pool pop after-effect fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            PopFailureAfterEffect = new AggregateException(new IOException("Output removal pool pop wrapper.", fatal)),
            PopFailureOrdinal = 4,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? stopFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target).FirstFailure);
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target).FirstFailure);
        Assert.Equal((4, 4), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Equal(4, operations.PoolPushEffects.Count);
        Assert.Equal(operations.PoolPushEffects, operations.PoolPopEffects);
        var effects = (runtime.RootAllocationAttempts, runtime.CopyAttempts, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.LiveRoots, runtime.LiveBlocks, runtime.InvokeAttempts, runtime.InvokeReturns,
            operations.StartInvocations, operations.StopInvocations, operations.RemoveCalls, operations.DrainCalls,
            operations.QueueReleaseAttempts, source.State.RetainedReleaseAttempts, capture.IsDrained);
        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        // Cleanup effects are recorded before raw fixture teardown. The outer
        // assertion cannot confuse that teardown with production retirement.
        return new(new(ownership, stopOwnerObservation, stopPrimitive), new(sampleMarker), new(unavailableMarker), stopFailure,
            effects, [operations.Stream, operations.Output, operations.Configuration], operations.ObjectReleaseAttempts.ToArray(),
            ReadCompletionPrimitiveFacts(startOwnerObservation, fatal), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
    }

    private sealed record RemovePoolOwnershipGraph(CompletionOwnershipPairGraph Completions,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StopFailure,
        (int Roots, int Copies, int CallerReleases, int RootFrees, int LiveRoots, int LiveBlocks,
            int Invocations, int Returns, int Start, int Stop, int Remove, int Barrier, int QueueRelease, int SourceRelease, bool Drained) Effects,
        nint[] ExpectedObjectReleases, nint[] ActualObjectReleases,
        (bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, bool OriginalFatal, long PhysicalCopies) StartPrimitiveFacts,
        (bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, bool OriginalFatal, long PhysicalCopies) StopPrimitiveFacts);

    [Fact]
    public void StopPoolPopAfterEffectFatalRetainsCaptureBeyondConfirmedNativeCleanup()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopPoolOwnershipGraph graph = CreateStopPoolPopAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Completions.Ownership.Capture.IsAlive, graph.Completions.Ownership.Completion.IsAlive,
                graph.Completions.Ownership.Primitive.IsAlive, graph.Completions.Ownership.Operations.IsAlive,
                graph.Completions.Ownership.Source.IsAlive, graph.Completions.OtherCompletion.IsAlive,
                graph.Completions.OtherPrimitive.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive,
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.StopFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.FirstDisposeFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.RepeatedDisposeFailure)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests original nested fatal after the actual Stop pool pop effect and healthy completion invocation.")]
    private static StopPoolOwnershipGraph CreateStopPoolPopAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Stop pool pop after-effect fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            PopFailureAfterEffect = new AggregateException(new IOException("Stop pool pop wrapper.", fatal)),
            PopFailureOrdinal = 3,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Assert.Equal((2, 2), (operations.PoolsPushed, operations.PoolsPopped));
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Exception? stopFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(startOwnerObservation, fatal));
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target).FirstFailure);
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target).FirstFailure);
        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal((4, 4), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Equal(4, operations.PoolPushEffects.Count);
        Assert.Equal(operations.PoolPushEffects, operations.PoolPopEffects);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.True(capture.IsDrained);

        // Both primitive roots retired normally. Only the shell's unsettled
        // Stop pool scope can durably retain this otherwise weak graph.
        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(new(ownership, stopOwnerObservation, stopPrimitive), new(sampleMarker), new(unavailableMarker), stopFailure);
    }

    private sealed record StopPoolOwnershipGraph(CompletionOwnershipPairGraph Completions,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StopFailure);

    [Fact]
    public void StartPoolPopAfterEffectFatalRetainsCaptureBeyondConfirmedNativeCleanup()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StartPoolOwnershipGraph graph = CreateStartPoolPopAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Completions.Ownership.Capture.IsAlive, graph.Completions.Ownership.Completion.IsAlive,
                graph.Completions.Ownership.Primitive.IsAlive, graph.Completions.Ownership.Operations.IsAlive,
                graph.Completions.Ownership.Source.IsAlive, graph.Completions.OtherCompletion.IsAlive,
                graph.Completions.OtherPrimitive.IsAlive, graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive,
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.StopFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.FirstDisposeFailure),
                ReferenceEquals(graph.Completions.Ownership.Fatal, graph.Completions.Ownership.RepeatedDisposeFailure)));
        Assert.Equal((2, 2, graph.CreatorThread, graph.CreatorThread), graph.StartPoolEffects);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests original nested fatal after the actual Start pool pop effect and healthy completion invocation.")]
    private static StartPoolOwnershipGraph CreateStartPoolPopAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Start pool pop after-effect fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            PopFailureAfterEffect = new AggregateException(new IOException("Start pool pop wrapper.", fatal)),
            PopFailureOrdinal = 2,
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker));
        Assert.Equal((1, 1), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        var startPoolEffects = (operations.PoolsPushed, operations.PoolsPopped,
            operations.PoolPushThread, operations.PoolPopThread);
        Exception? stopFailure = Record.Exception(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(startOwnerObservation, fatal));
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target).FirstFailure);
        Assert.Null(Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target).FirstFailure);
        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal((4, 4), (operations.PoolsPushed, operations.PoolsPopped));
        Assert.Equal(4, operations.PoolPushEffects.Count);
        Assert.Equal(operations.PoolPushEffects, operations.PoolPopEffects);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.True(capture.IsDrained);

        // Both primitive roots retired normally. Only the shell's unsettled
        // Start pool scope can durably retain this otherwise weak graph.
        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(new(ownership, stopOwnerObservation, stopPrimitive), new(sampleMarker), new(unavailableMarker),
            stopFailure, Environment.CurrentManagedThreadId, startPoolEffects);
    }

    private sealed record StartPoolOwnershipGraph(CompletionOwnershipPairGraph Completions,
        WeakReference SampleMarker, WeakReference UnavailableMarker, Exception? StopFailure,
        int CreatorThread, (int Pushes, int Pops, int PushThread, int PopThread) StartPoolEffects);

    [Fact]
    public void ConstructorBodyFatalThenPoolPopAfterEffectRetainsExactShellBeyondSlotReplacement()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        ConstructorPoolGraph graph = CreateConstructorBodyAndPoolPopFailure();
        bool replaced = ReplaceConstructorFailureSlot();
        (Exception? first, Exception? repeated, bool unchangedEffects) = DisposeConstructorPoolGraph(graph);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 2, true, true, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture?.IsAlive ?? false, graph.Operations.IsAlive, graph.Source.IsAlive,
                graph.SampleMarker.IsAlive, graph.UnavailableMarker.IsAlive,
                ReferenceEquals(graph.Fatal, graph.OutwardFailure), ReferenceEquals(graph.Fatal, first),
                ReferenceEquals(graph.Fatal, repeated), replaced && unchangedEffects));
        Assert.Equal((1, 1, graph.CreatorThread, graph.CreatorThread, 1, 1, 1, 0, 0, 0, 0), graph.Effects);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Original constructor body fatal must survive a later after-effect pool pop failure.")]
    private static ConstructorPoolGraph CreateConstructorBodyAndPoolPopFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var fatal = new OutOfMemoryException("Original constructor body fatal.");
        var operations = new CompletionOwnershipOperations(runtime)
        {
            ConfigureFailure = new AggregateException(new IOException("Constructor body wrapper.", fatal)),
            PopFailureAfterEffect = new IOException("Constructor pool pop after-effect failure."),
        };
        var sampleMarker = new object();
        var unavailableMarker = new object();
        Exception? outward = Record.Exception(() => api.CreateCaptureWithOperations(source, operations,
            sample => { GC.KeepAlive(sampleMarker); sample.Dispose(); }, () => GC.KeepAlive(unavailableMarker)));
        WeakReference? capture = ObserveFactorySlot(operations);
        var effects = (operations.PoolsPushed, operations.PoolsPopped,
            operations.PoolPushThread, operations.PoolPopThread, operations.ConfigureCalls,
            operations.QueueReleaseAttempts, source.State.RetainedReleaseAttempts,
            operations.StartInvocations, operations.StopInvocations, operations.RemoveCalls, operations.DrainCalls);
        Assert.Equal(new[] { operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(0, runtime.RootAllocationAttempts);
        Assert.Equal(0, runtime.CopyAttempts);
        return new(capture, new(operations), new(source.State), new(sampleMarker), new(unavailableMarker),
            fatal, outward, Environment.CurrentManagedThreadId, effects);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReplaceConstructorFailureSlot()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var bodyFailure = new IOException("Independent replacement constructor failure.");
        var releaseFailure = new IOException("Independent replacement output consumed-then-throws.");
        var operations = new TestOperations { ConfigureFailure = bodyFailure };
        operations.AfterObjectRelease = owner =>
        {
            if (owner == operations.Output) { throw releaseFailure; }
        };
        Exception? outward = Record.Exception(() => api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { }));
        WeakReference? replacement = ObserveFactorySlot(operations);
        return ReferenceEquals(outward, bodyFailure) && replacement?.IsAlive == true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? ObserveFactorySlot(IMacOSRemoteWindowCaptureOperations operations)
    {
        object? capture = typeof(MacOSRemoteWindowScreenCaptureKitApi)
            .GetField("failedFactoryCapture", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        return capture is not null && ReferenceEquals(operations, capture.GetType()
            .GetField("operations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture))
            ? new(capture) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Exception? First, Exception? Repeated, bool UnchangedEffects) DisposeConstructorPoolGraph(ConstructorPoolGraph graph)
    {
        if (graph.Capture?.Target is not IMacOSRemoteWindowNativeCapture capture) { return (null, null, false); }
        Exception? first = Record.Exception(capture.Dispose);
        Exception? repeated = Record.Exception(capture.Dispose);
        var operations = (CompletionOwnershipOperations)graph.Operations.Target!;
        var source = (SourceState)graph.Source.Target!;
        bool unchanged = operations.PoolsPushed == 1 && operations.PoolsPopped == 1
            && operations.ConfigureCalls == 1 && operations.QueueReleaseAttempts == 1
            && source.RetainedReleaseAttempts == 1 && operations.ObjectReleaseAttempts.Count == 2
            && operations.StartInvocations == 0 && operations.StopInvocations == 0
            && operations.RemoveCalls == 0 && operations.DrainCalls == 0;
        return (first, repeated, unchanged);
    }

    private sealed record ConstructorPoolGraph(WeakReference? Capture, WeakReference Operations,
        WeakReference Source, WeakReference SampleMarker, WeakReference UnavailableMarker,
        OutOfMemoryException Fatal, Exception? OutwardFailure, int CreatorThread,
        (int Pushes, int Pops, int PushThread, int PopThread, int Configure, int QueueRelease,
            int SourceRelease, int Start, int Stop, int Remove, int Barrier) Effects);

    [Fact]
    public void StopCallerReleaseAfterEffectFatalRetainsCaptureAndCleansOtherOwnersOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        CompletionOwnershipPairGraph graph = CreateStopCallerReleaseAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive, graph.OtherCompletion.IsAlive,
                graph.OtherPrimitive.IsAlive));
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested fatal after actual Stop caller reference consumption.")]
    private static CompletionOwnershipPairGraph CreateStopCallerReleaseAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Stop caller release after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Assert.True(capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;

        runtime.BeforeRelease = () => runtime.ReleaseFailure = runtime.ReleaseAttempts == 2
            ? new AggregateException(new IOException("Stop caller release wrapper.", fatal)) : null;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(stopOwnerObservation.Target);
        Assert.True(owner.Primitive.CopyConfirmed);
        Assert.True(owner.Primitive.OwnedReleaseAttempted);
        Assert.False(owner.Primitive.OwnedReleaseConfirmed);
        Assert.Equal(0, owner.Pointer);
        Assert.Equal((false, true, true, true, true, true, 1L), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(startOwnerObservation, fatal));

        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.True(capture.IsDrained);

        var ownership = new CompletionOwnershipGraph(new(capture), stopOwnerObservation,
            stopPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(ownership, startOwnerObservation, startPrimitive);
    }

    [Fact]
    public void StartCallerReleaseAfterEffectFatalRetainsCaptureAndCleansOtherOwnersOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        CompletionOwnershipPairGraph graph = CreateStartCallerReleaseAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive, graph.OtherCompletion.IsAlive,
                graph.OtherPrimitive.IsAlive));
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested fatal after actual Start caller reference consumption.")]
    private static CompletionOwnershipPairGraph CreateStartCallerReleaseAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Start caller release after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        Assert.True(capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference stopOwnerObservation = operations.Completion!;
        WeakReference stopPrimitive = operations.Primitive!;

        runtime.BeforeRelease = () => runtime.ReleaseFailure = runtime.ReleaseAttempts == 1
            ? new AggregateException(new IOException("Start caller release wrapper.", fatal)) : null;
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target);
        Assert.True(owner.Primitive.CopyConfirmed);
        Assert.True(owner.Primitive.OwnedReleaseAttempted);
        Assert.False(owner.Primitive.OwnedReleaseConfirmed);
        Assert.Equal(0, owner.Pointer);
        // Caller release returned failure after consumption. Root free did
        // return normally, so native/managed retirement is independently true.
        Assert.Equal((false, true, true, true, true, true, 1L), ReadCompletionPrimitiveFacts(startOwnerObservation, fatal));
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(stopOwnerObservation, fatal));

        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.True(capture.IsDrained);

        var ownership = new CompletionOwnershipGraph(new(capture), startOwnerObservation,
            startPrimitive, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(ownership, stopOwnerObservation, stopPrimitive);
    }

    private sealed record CompletionOwnershipPairGraph(
        CompletionOwnershipGraph Ownership, WeakReference OtherCompletion, WeakReference OtherPrimitive);

    [Fact]
    public void StopForeignRootReturnRetainsCaptureAndCleansKnownStartCallerOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        UnknownStopRootReturnOwnershipGraph graph = CreateStopInvalidRootReturnFailure("foreign");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive, graph.StartCompletion.IsAlive,
                graph.StartPrimitive.IsAlive));
        Assert.IsType<InvalidOperationException>(graph.Ownership.OriginalFailure);
        Assert.IsType<InvalidOperationException>(graph.Ownership.FirstDisposeFailure);
        Assert.IsType<InvalidOperationException>(graph.Ownership.RepeatedDisposeFailure);
    }

    [Fact]
    public void StopZeroRootReturnRetainsCaptureAndCleansKnownStartCallerOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        UnknownStopRootReturnOwnershipGraph graph = CreateStopInvalidRootReturnFailure("zero");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive, graph.StartCompletion.IsAlive,
                graph.StartPrimitive.IsAlive));
        Assert.IsType<InvalidOperationException>(graph.Ownership.OriginalFailure);
        Assert.IsType<InvalidOperationException>(graph.Ownership.FirstDisposeFailure);
        Assert.IsType<InvalidOperationException>(graph.Ownership.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UnknownStopRootReturnOwnershipGraph CreateStopInvalidRootReturnFailure(string invalidReturn)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwnerObservation = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;

        runtime.InvalidRootReturn = invalidReturn;
        Assert.False(capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
        Exception originalFailure = Assert.IsType<InvalidOperationException>(stopOwner.FirstFailure);
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.True(stopOwner.Primitive.AcquisitionAttempted);
        Assert.True(stopOwner.Primitive.RootAllocationAttempted);
        Assert.False(stopOwner.Primitive.RootAllocationConfirmed);
        Assert.False(stopOwner.Primitive.CopyAttempted);
        Assert.False(stopOwner.Primitive.CopyConfirmed);
        Assert.False(stopOwner.Primitive.OwnedReleaseAttempted);
        Assert.False(stopOwner.Primitive.OwnedReleaseConfirmed);
        Assert.False(stopOwner.Primitive.RootReleaseAttempted);
        Assert.False(stopOwner.Primitive.RootReleaseConfirmed);
        Assert.False(stopOwner.NativeCaptureRetirement.IsCompleted);
        Assert.False(stopOwner.ManagedInvocationDrain.IsCompleted);
        Assert.Equal(0, stopOwner.Pointer);
        Assert.Same(originalFailure, stopOwner.FirstFailure);
        Assert.Equal(0, stopOwner.Primitive.PhysicalCaptureCopyCount);
        var startOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(startOwnerObservation.Target);
        Assert.True(startOwner.IsReleased);
        Assert.True(startOwner.NativeCaptureRetirement.IsCompletedSuccessfully);
        Assert.True(startOwner.ManagedInvocationDrain.IsCompletedSuccessfully);
        Assert.Null(startOwner.FirstFailure);

        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(1, runtime.CopyAttempts);
        Assert.Equal(1, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        Assert.Equal(1, runtime.InvokeAttempts);
        Assert.Equal(1, runtime.InvokeReturns);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.Equal(0, operations.RemoveCalls);
        Assert.Equal(0, operations.DrainCalls);
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.QueueReleaseAttempts);
        Assert.Equal(0, source.State.RetainedReleaseAttempts);
        Assert.False(capture.IsDrained);

        var ownership = new UnknownRootReturnOwnershipGraph(new(capture), operations.Completion!,
            operations.Primitive!, new(operations), new(source.State), originalFailure,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(ownership, startOwnerObservation, startPrimitive);
    }

    private sealed record UnknownStopRootReturnOwnershipGraph(
        UnknownRootReturnOwnershipGraph Ownership, WeakReference StartCompletion, WeakReference StartPrimitive);

    [Fact]
    public void StartForeignRootReturnRetainsCaptureAndCleansKnownNativeOwnersOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        UnknownRootReturnOwnershipGraph graph = CreateStartInvalidRootReturnFailure("foreign");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.Completion.IsAlive, graph.Primitive.IsAlive,
                graph.Operations.IsAlive, graph.Source.IsAlive));
        Assert.IsType<InvalidOperationException>(graph.OriginalFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.Same(graph.FirstDisposeFailure, graph.RepeatedDisposeFailure);
    }

    [Fact]
    public void StartZeroRootReturnRetainsCaptureAndCleansKnownNativeOwnersOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        UnknownRootReturnOwnershipGraph graph = CreateStartInvalidRootReturnFailure("zero");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.Completion.IsAlive, graph.Primitive.IsAlive,
                graph.Operations.IsAlive, graph.Source.IsAlive));
        Assert.IsType<InvalidOperationException>(graph.OriginalFailure);
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.Same(graph.FirstDisposeFailure, graph.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UnknownRootReturnOwnershipGraph CreateStartInvalidRootReturnFailure(string invalidReturn)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime { InvalidRootReturn = invalidReturn };
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });

        Exception originalFailure = Assert.Throws<InvalidOperationException>(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Assert.False(capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
        Assert.True(owner.Primitive.AcquisitionAttempted);
        Assert.True(owner.Primitive.RootAllocationAttempted);
        Assert.False(owner.Primitive.RootAllocationConfirmed);
        Assert.False(owner.Primitive.CopyAttempted);
        Assert.False(owner.Primitive.CopyConfirmed);
        Assert.False(owner.Primitive.OwnedReleaseAttempted);
        Assert.False(owner.Primitive.OwnedReleaseConfirmed);
        Assert.False(owner.Primitive.RootReleaseAttempted);
        Assert.False(owner.Primitive.RootReleaseConfirmed);
        Assert.False(owner.NativeCaptureRetirement.IsCompleted);
        Assert.False(owner.ManagedInvocationDrain.IsCompleted);
        Assert.Equal(0, owner.Pointer);
        Assert.Same(originalFailure, owner.FirstFailure);
        Assert.Equal(0, owner.Primitive.PhysicalCaptureCopyCount);

        Assert.Equal(1, runtime.RootAllocationAttempts);
        Assert.Equal(0, runtime.CopyAttempts);
        Assert.Equal(1, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(0, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.ReleaseAttempts);
        Assert.Equal(0, runtime.InvokeAttempts);
        Assert.Equal(0, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        // Physical sample/output drain is not full completion/shell cleanup.
        Assert.True(capture.IsDrained);

        return new(new(capture), operations.Completion!, operations.Primitive!, new(operations),
            new(source.State), originalFailure, firstDisposeFailure, repeatedDisposeFailure);
    }

    private sealed record UnknownRootReturnOwnershipGraph(
        WeakReference Capture, WeakReference Completion, WeakReference Primitive,
        WeakReference Operations, WeakReference Source, Exception OriginalFailure,
        Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure);

    [Fact]
    public void StopRootAllocationAfterEffectFatalRetainsCaptureAndCleansKnownStartCallerOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopRootAllocationOwnershipGraph graph = CreateStopRootAllocationAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive, graph.StartCompletion.IsAlive,
                graph.StartPrimitive.IsAlive));
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested fatal after actual Stop root allocation.")]
    private static StopRootAllocationOwnershipGraph CreateStopRootAllocationAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Stop root allocation after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwner = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;

        runtime.RootAllocationFailure = new AggregateException(new IOException("Stop root allocation wrapper.", fatal));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        var stopOwner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
        Assert.True(stopOwner.Primitive.AcquisitionAttempted);
        Assert.True(stopOwner.Primitive.RootAllocationAttempted);
        Assert.False(stopOwner.Primitive.RootAllocationConfirmed);
        Assert.False(stopOwner.Primitive.CopyAttempted);
        Assert.False(stopOwner.Primitive.CopyConfirmed);
        Assert.False(stopOwner.Primitive.OwnedReleaseAttempted);
        Assert.False(stopOwner.Primitive.OwnedReleaseConfirmed);
        Assert.False(stopOwner.Primitive.RootReleaseAttempted);
        Assert.False(stopOwner.Primitive.RootReleaseConfirmed);
        Assert.False(stopOwner.NativeCaptureRetirement.IsCompleted);
        Assert.False(stopOwner.ManagedInvocationDrain.IsCompleted);
        Assert.Equal(0, stopOwner.Pointer);
        Assert.Same(fatal, stopOwner.FirstFailure);
        Assert.Equal(0, stopOwner.Primitive.PhysicalCaptureCopyCount);
        Assert.Equal((true, true, true, true, true, false, 1L), ReadCompletionPrimitiveFacts(startOwner, fatal));

        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(1, runtime.CopyAttempts);
        Assert.Equal(1, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(1, runtime.RootFreeAttempts);
        Assert.Equal(1, runtime.ReleaseAttempts);
        Assert.Equal(1, runtime.InvokeAttempts);
        Assert.Equal(1, runtime.InvokeReturns);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.Equal(0, operations.RemoveCalls);
        Assert.Equal(0, operations.DrainCalls);
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.QueueReleaseAttempts);
        Assert.Equal(0, source.State.RetainedReleaseAttempts);
        Assert.False(capture.IsDrained);

        // Stop's root allocation never returned ownership and no Stop was
        // issued. Only Start's confirmed caller +1 is independently released;
        // no unknown root or dependent native owner is guessed or retried.
        var ownership = new CompletionOwnershipGraph(new(capture), operations.Completion!,
            operations.Primitive!, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(ownership, startOwner, startPrimitive);
    }

    private sealed record StopRootAllocationOwnershipGraph(
        CompletionOwnershipGraph Ownership, WeakReference StartCompletion, WeakReference StartPrimitive);

    [Fact]
    public void StartRootAllocationAfterEffectFatalRetainsCaptureAndCleansKnownNativeOwnersOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        CompletionOwnershipGraph graph = CreateStartRootAllocationAfterEffectFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.Completion.IsAlive, graph.Primitive.IsAlive,
                graph.Operations.IsAlive, graph.Source.IsAlive));
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested fatal after actual Start root allocation.")]
    private static CompletionOwnershipGraph CreateStartRootAllocationAfterEffectFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Start root allocation after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime
        {
            RootAllocationFailure = new AggregateException(new IOException("Start root allocation wrapper.", fatal)),
        };
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });

        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(operations.Completion!.Target);
        Assert.True(owner.Primitive.AcquisitionAttempted);
        Assert.True(owner.Primitive.RootAllocationAttempted);
        Assert.False(owner.Primitive.RootAllocationConfirmed);
        Assert.False(owner.Primitive.CopyAttempted);
        Assert.False(owner.Primitive.CopyConfirmed);
        Assert.False(owner.Primitive.OwnedReleaseAttempted);
        Assert.False(owner.Primitive.OwnedReleaseConfirmed);
        Assert.False(owner.Primitive.RootReleaseAttempted);
        Assert.False(owner.Primitive.RootReleaseConfirmed);
        Assert.False(owner.NativeCaptureRetirement.IsCompleted);
        Assert.False(owner.ManagedInvocationDrain.IsCompleted);
        Assert.Equal(0, owner.Pointer);
        Assert.Same(fatal, owner.FirstFailure);
        Assert.Equal(0, owner.Primitive.PhysicalCaptureCopyCount);

        Assert.Equal(1, runtime.RootAllocationAttempts);
        Assert.Equal(0, runtime.CopyAttempts);
        Assert.Equal(1, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(0, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.ReleaseAttempts);
        Assert.Equal(0, runtime.InvokeAttempts);
        Assert.Equal(0, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);

        // The allocation effect happened, but no root pointer was returned.
        // No guessed free/caller release is permitted. The fixture raw-frees
        // its root before outer GC without confirming production retirement.
        return new(new(capture), operations.Completion!, operations.Primitive!, new(operations),
            new(source.State), fatal, firstDisposeFailure, repeatedDisposeFailure);
    }

    [Fact]
    public void LateLastHeapReferenceRootFreeAfterEffectFatalKeepsCaptureGraphAndCharge()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        LateRootFreeOwnershipGraph graph = CreateLateLastHeapReferenceRootFreeFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // The late helper fault, repeat Dispose and fixture teardown have all
        // actually run before checking an earlier premature-charge snapshot.
        Assert.Equal((before + 1, before + 1, true, true, true, true, true),
            (graph.ChargeBeforeLateFault, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive));
        Assert.IsType<InvalidOperationException>(graph.FirstDisposeFailure);
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.RepeatedDisposeFailure);
        Assert.Equal((true, false, false, true, false, true, 1L), graph.FinalPrimitiveFacts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects original nested fatal after actual last heap-reference root free.")]
    private static LateRootFreeOwnershipGraph CreateLateLastHeapReferenceRootFreeFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Late last heap-reference root-free after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        WeakReference startOwner = operations.Completion!;
        WeakReference startPrimitive = operations.Primitive!;
        nint extraReference = runtime.CopyBlock(operations.StartBorrowedPointer);
        Assert.Equal(operations.StartBorrowedPointer, extraReference);
        Assert.True(capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());

        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        int chargeBeforeLateFault = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        // Do not assert this charge/result yet: even a premature successful
        // Dispose must proceed through the actual late fault and full teardown.
        Assert.Equal((true, false, false, false, false, false, 1L), ReadCompletionPrimitiveFacts(startOwner, fatal));
        Assert.Equal((2, 1, 1, 1),
            (runtime.ReleaseAttempts, runtime.RootFreeAttempts, runtime.LiveRoots, runtime.LiveBlocks));

        runtime.RootFreeFailure = new AggregateException(new IOException("Late free wrapper.", fatal));
        Assert.Null(Record.Exception(() => runtime.ReleaseBlock(extraReference)));
        Exception? afterLateDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);
        var finalFacts = ReadCompletionPrimitiveFacts(startOwner, fatal);

        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(3, runtime.CopyAttempts);
        Assert.Equal(3, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);

        var ownership = new CompletionOwnershipGraph(new(capture), startOwner, startPrimitive,
            new(operations), new(source.State), fatal, afterLateDisposeFailure, repeatedDisposeFailure);
        return new(ownership, firstDisposeFailure, chargeBeforeLateFault, finalFacts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool CallerReleased, bool NativeRetired, bool ManagedDrained,
        bool RootFreeAttempted, bool RootFreeConfirmed, bool OriginalFatal, long PhysicalCopies)
        ReadCompletionPrimitiveFacts(WeakReference observation, OutOfMemoryException fatal)
    {
        var owner = Assert.IsType<MacOSRemoteWindowCaptureCompletion>(observation.Target);
        return (owner.IsReleased, owner.NativeCaptureRetirement.IsCompletedSuccessfully,
            owner.ManagedInvocationDrain.IsCompletedSuccessfully, owner.Primitive.RootReleaseAttempted,
            owner.Primitive.RootReleaseConfirmed, ReferenceEquals(owner.FirstFailure, fatal),
            owner.Primitive.PhysicalCaptureCopyCount);
    }

    private sealed record LateRootFreeOwnershipGraph(CompletionOwnershipGraph Ownership,
        Exception? FirstDisposeFailure, int ChargeBeforeLateFault,
        (bool CallerReleased, bool NativeRetired, bool ManagedDrained, bool RootFreeAttempted,
            bool RootFreeConfirmed, bool OriginalFatal, long PhysicalCopies) FinalPrimitiveFacts);

    [Fact]
    public void StopCompletionCopyAfterEffectFatalRetainsCaptureAndCleansKnownStartCallerOnce()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        StopCompletionOwnershipGraph graph = CreateStopCompletionCopyFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Ownership.Capture.IsAlive, graph.Ownership.Completion.IsAlive,
                graph.Ownership.Primitive.IsAlive, graph.Ownership.Operations.IsAlive,
                graph.Ownership.Source.IsAlive));
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.FirstDisposeFailure);
        Assert.Same(graph.Ownership.Fatal, graph.Ownership.RepeatedDisposeFailure);
        Assert.Equal((1, 1, 1, 1),
            (graph.CallerReleaseAttempts, graph.RootFreeAttempts, graph.LiveRoots, graph.LiveBlocks));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests original nested fatal after the second actual physical capture copy.")]
    private static StopCompletionOwnershipGraph CreateStopCompletionCopyFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Stop completion copy after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });

        Assert.True(capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult());
        Assert.Equal(1, runtime.CopyAttempts);
        runtime.CopyFailure = new AggregateException(new IOException("Stop copy wrapper.", fatal));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(1, runtime.InvokeAttempts);
        Assert.Equal(1, runtime.InvokeReturns);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.Equal(0, operations.RemoveCalls);
        Assert.Equal(0, operations.DrainCalls);
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.QueueReleaseAttempts);
        Assert.Equal(0, source.State.RetainedReleaseAttempts);
        Assert.False(capture.IsDrained);

        // Stop never acquired a publishable pointer and never issued native
        // Stop. Dependent native owners cannot clean up. Only the confirmed
        // Start caller +1 is independent after handoff returns and completion
        // notification is observed, not a terminal drain/final ABI return proof.
        // Runtime raw teardown removes fixture roots before the caller's GC.
        var ownership = new CompletionOwnershipGraph(new(capture), operations.Completion!,
            operations.Primitive!, new(operations), new(source.State), fatal,
            firstDisposeFailure, repeatedDisposeFailure);
        return new(ownership, runtime.ReleaseAttempts, runtime.RootFreeAttempts,
            runtime.LiveRoots, runtime.LiveBlocks);
    }

    private sealed record StopCompletionOwnershipGraph(
        CompletionOwnershipGraph Ownership, int CallerReleaseAttempts,
        int RootFreeAttempts, int LiveRoots, int LiveBlocks);

    [Fact]
    public async Task HealthyStagedOneArgumentStartAndStopUseActualCaptureAndRetireCallerCopies()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime();
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        try
        {
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        }
        finally
        {
            capture.Dispose();
        }

        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(2, runtime.RootAllocationAttempts);
        Assert.Equal(2, runtime.CopyAttempts);
        Assert.Equal(2, runtime.ReleaseAttempts);
        Assert.Equal(2, runtime.RootFreeAttempts);
        Assert.Equal(2, runtime.InvokeAttempts);
        Assert.Equal(2, runtime.InvokeReturns);
        Assert.Equal(0, runtime.LiveRoots);
        Assert.Equal(0, runtime.LiveBlocks);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
    }

    [Fact]
    public void StartCompletionCopyAfterEffectFatalRetainsOriginalCaptureAndPrimitiveOwner()
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        CompletionOwnershipGraph graph = CreateStartCompletionCopyFailure();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.Equal((before + 1, true, true, true, true, true),
            (MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount,
                graph.Capture.IsAlive, graph.Completion.IsAlive, graph.Primitive.IsAlive,
                graph.Operations.IsAlive, graph.Source.IsAlive));
        Assert.Same(graph.Fatal, graph.FirstDisposeFailure);
        Assert.Same(graph.Fatal, graph.RepeatedDisposeFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Usage", "CA2201", Justification = "Tests original nested fatal identity after the actual copy helper effect.")]
    private static CompletionOwnershipGraph CreateStartCompletionCopyFailure()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var fatal = new OutOfMemoryException("Start completion copy after-effect fatal.");
        using var runtime = new MacOSRemoteWindowControlledBlockRuntime
        {
            CopyFailure = new AggregateException(new IOException("Start copy wrapper.", fatal)),
        };
        var operations = new CompletionOwnershipOperations(runtime);
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });

        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StartAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult()));
        Exception? firstDisposeFailure = Record.Exception(capture.Dispose);
        Exception? repeatedDisposeFailure = Record.Exception(capture.Dispose);

        Assert.Equal(1, runtime.RootAllocationAttempts);
        Assert.Equal(1, runtime.CopyAttempts);
        Assert.Equal(1, runtime.LiveRoots);
        Assert.Equal(1, runtime.LiveBlocks);
        Assert.Equal(0, runtime.RootFreeAttempts);
        Assert.Equal(0, runtime.ReleaseAttempts);
        Assert.Equal(0, runtime.InvokeAttempts);
        Assert.Equal(0, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(1, operations.DrainCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.QueueReleaseAttempts);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);

        // The only retained test observations are weak. Runtime Dispose below
        // raw-frees fixture memory/GCHandles without production helper calls,
        // root retirement, retries or a reset of Capture's durable accounting.
        return new(new(capture), operations.Completion!, operations.Primitive!, new(operations),
            new(source.State), fatal, firstDisposeFailure, repeatedDisposeFailure);
    }

    private sealed record CompletionOwnershipGraph(
        WeakReference Capture, WeakReference Completion, WeakReference Primitive,
        WeakReference Operations, WeakReference Source, OutOfMemoryException Fatal,
        Exception? FirstDisposeFailure, Exception? RepeatedDisposeFailure);

    // Only system effects are controlled; Capture and completion use the actual
    // production state machines and typed one-argument copy/invoke/dispose ABI.
    private sealed class CompletionOwnershipOperations(MacOSRemoteWindowControlledBlockRuntime runtime)
        : IMacOSRemoteWindowCaptureOperations
    {
        private static long nextAddress = 1_000_000;
        private readonly nint addressBase = (nint)Interlocked.Add(ref nextAddress, 10);
        internal nint Configuration => addressBase;
        internal nint Output => addressBase + 1;
        internal nint Queue => addressBase + 2;
        internal nint Stream => addressBase + 3;
        internal WeakReference? Completion;
        internal WeakReference? Primitive;
        internal ConcurrentQueue<nint> ObjectReleaseAttempts { get; } = new();
        internal int QueueReleaseAttempts;
        internal int StartInvocations;
        internal int StopInvocations;
        internal int StartSelectorAttempts;
        internal int StopSelectorAttempts;
        internal int NativeInvocationAttempts;
        internal int RemoveCalls;
        internal int DrainCalls;
        internal nint StartBorrowedPointer;
        internal Action? StartInvocationAfterCallbackHook { get; init; }
        internal Exception? StartActionFailureAfterEffect { get; init; }
        internal int StartActionThrowAttempts;
        internal Exception? StopActionFailureAfterEffect { get; init; }
        internal int StopActionThrowAttempts;
        internal bool StartActionFailureOnce { get; init; }
        internal Exception? StartFailureObserverFailureAfterEffect { get; init; }
        internal int StartFailureObserverThrowAttempts;
        internal Exception? StopFailureObserverFailureAfterEffect { get; init; }
        internal int StopFailureObserverThrowAttempts;
        internal ManualResetEventSlim? StartFailureObserverRelease { get; init; }
        internal TaskCompletionSource StartFailureObserverEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? StartCompletedFailureAfterEffect { get; init; }
        internal int StartCompletedThrowAttempts;
        internal Exception? StopCompletedFailureAfterEffect { get; init; }
        internal int StopCompletedThrowAttempts;
        internal Action? StartCompletedHook { get; init; }
        internal Action? StartCompletedResourceUseExited { get; init; }
        internal Func<MacOSRemoteWindowCaptureCompletion, IMacOSRemoteWindowCaptureCompletion>? StartCompletionForwarder { get; init; }
        internal Action? StopCompletedHook { get; init; }
        internal ManualResetEventSlim? StartCompletedRelease { get; init; }
        internal TaskCompletionSource StartCompletedEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int completionPreparations;
        internal bool StartInvocationReturnsBeforeCallback { get; init; }
        internal Exception? StartInvocationFailureAfterEffect { get; init; }
        internal Exception? StartInvocationFailureBeforeCallbackAfterEffect { get; init; }
        internal Exception? StopInvocationFailureAfterEffect { get; init; }
        internal bool HoldExternalStopCopy { get; init; }
        internal nint ExternallyHeldStopCopy;
        internal int ExternalStopCopyReleaseAttempts;
        internal Exception? StopInvocationFailureBeforeCallbackAfterEffect { get; init; }
        internal Exception? RemoveOutputFailureAfterEffect { get; init; }
        internal nint NativeHeldCompletion;
        internal int NativeHeldReleaseAttempts;
        internal TaskCompletionSource NativeHeldCopyReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Exception? ConfigureFailure { get; init; }
        internal Exception? PopFailureAfterEffect { get; init; }
        internal int? PopFailureOrdinal { get; init; }
        internal int? ZeroPoolPushOrdinal { get; init; }
        internal Exception? PushFailureAfterEffect { get; init; }
        internal int? PushFailureOrdinal { get; init; }
        internal int PoolsPushed;
        internal int PoolsPopped;
        internal int PoolPopAttempts;
        internal int PoolPushThread;
        internal int PoolPopThread;
        internal int ConfigureCalls;
        internal int AddOutputCalls;
        internal int ConstructorScopedBodyCalls;
        internal ConcurrentQueue<(int Ordinal, nint Token, int Thread)> PoolPushEffects { get; } = new();
        internal ConcurrentQueue<(int Ordinal, nint Token, int Thread)> PoolPopEffects { get; } = new();

        public nint PushAutoreleasePool()
        {
            int ordinal = Interlocked.Increment(ref PoolsPushed);
            PoolPushThread = Environment.CurrentManagedThreadId;
            nint token = ZeroPoolPushOrdinal == ordinal ? 0 : 1;
            PoolPushEffects.Enqueue((ordinal, token, PoolPushThread));
            if (PushFailureAfterEffect is { } failure && (PushFailureOrdinal is null || PushFailureOrdinal == ordinal))
            {
                throw failure;
            }
            return token;
        }
        public void PopAutoreleasePool(nint pool)
        {
            Interlocked.Increment(ref PoolPopAttempts);
            Assert.Equal(1, pool);
            int ordinal = Interlocked.Increment(ref PoolsPopped);
            PoolPopThread = Environment.CurrentManagedThreadId;
            PoolPopEffects.Enqueue((ordinal, pool, PoolPopThread));
            if (PopFailureAfterEffect is { } failure && (PopFailureOrdinal is null || PopFailureOrdinal == ordinal))
            {
                throw failure;
            }
        }
        public nint AllocateConfiguration()
        {
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            return Configuration;
        }
        public void Configure(nint configuration, int width, int height)
        {
            Assert.Equal(Configuration, configuration);
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            Interlocked.Increment(ref ConfigureCalls);
            if (ConfigureFailure is { } failure) { throw failure; }
        }
        public nint AllocateOutput()
        {
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            return Output;
        }
        public nint CreateSampleQueue()
        {
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            return Queue;
        }
        public nint AllocateStream()
        {
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            return Stream;
        }
        public nint InitializeStream(nint allocatedStream, nint filter, nint configuration, nint streamDelegate)
        {
            Assert.Equal(Stream, allocatedStream);
            Assert.Equal(42, filter);
            Assert.Equal(Configuration, configuration);
            Assert.Equal(0, streamDelegate);
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            return Stream;
        }

        public byte AddOutput(nint stream, nint output, nint queue, out nint error)
        {
            Assert.Equal(Stream, stream);
            Assert.Equal(Output, output);
            Assert.Equal(Queue, queue);
            Interlocked.Increment(ref ConstructorScopedBodyCalls);
            Interlocked.Increment(ref AddOutputCalls);
            error = 0;
            return 1;
        }

        public byte RemoveOutput(nint stream, nint output, out nint error)
        {
            Assert.Equal(Stream, stream);
            Assert.Equal(Output, output);
            Interlocked.Increment(ref RemoveCalls);
            if (RemoveOutputFailureAfterEffect is { } failure) { throw failure; }
            error = 0;
            return 1;
        }

        public IMacOSRemoteWindowCaptureCompletion CreateCompletion(
            Action<nint> action, Action<Exception> failure, Action completed)
        {
            bool isStart = Interlocked.Increment(ref completionPreparations) == 1;
            MacOSRemoteWindowCaptureCompletion owner = MacOSRemoteWindowCaptureCompletion.CreateWithOperations(error =>
            {
                action(error);
                if (isStart && StartActionFailureAfterEffect is { } actionFailure)
                {
                    if (!StartActionFailureOnce || Interlocked.CompareExchange(ref StartActionThrowAttempts, 1, 0) == 0)
                    {
                        if (!StartActionFailureOnce) { Interlocked.Increment(ref StartActionThrowAttempts); }
                        throw actionFailure;
                    }
                }
                if (!isStart && StopActionFailureAfterEffect is { } stopActionFailure)
                {
                    Interlocked.Increment(ref StopActionThrowAttempts);
                    throw stopActionFailure;
                }
            }, exception =>
            {
                failure(exception);
                if (isStart && StartFailureObserverRelease is { } release && StartFailureObserverEntered.TrySetResult())
                {
                    release.Wait();
                }
                if (isStart && StartFailureObserverFailureAfterEffect is { } observerFailure)
                {
                    Interlocked.Increment(ref StartFailureObserverThrowAttempts);
                    throw observerFailure;
                }
                if (!isStart && StopFailureObserverFailureAfterEffect is { } stopObserverFailure)
                {
                    Interlocked.Increment(ref StopFailureObserverThrowAttempts);
                    throw stopObserverFailure;
                }
            }, () =>
            {
                completed();
                if (isStart) { StartCompletedHook?.Invoke(); }
                else { StopCompletedHook?.Invoke(); }
                if (isStart && StartCompletedRelease is { } release)
                {
                    StartCompletedEntered.TrySetResult();
                    release.Wait();
                }
                if (isStart && StartCompletedFailureAfterEffect is { } completedFailure)
                {
                    Interlocked.Increment(ref StartCompletedThrowAttempts);
                    throw completedFailure;
                }
                if (!isStart && StopCompletedFailureAfterEffect is { } stopCompletedFailure)
                {
                    Interlocked.Increment(ref StopCompletedThrowAttempts);
                    throw stopCompletedFailure;
                }
            }, runtime, prepared =>
            {
                Completion = new(prepared);
                Primitive = new(prepared.Primitive);
            }, isStart ? StartCompletedResourceUseExited : null);
            return isStart && StartCompletionForwarder is { } forwarder ? forwarder(owner) : owner;
        }

        public nint GetCompletionSelector(bool isStart)
        {
            if (isStart) { Interlocked.Increment(ref StartSelectorAttempts); }
            else { Interlocked.Increment(ref StopSelectorAttempts); }
            return isStart ? 1 : 2;
        }
        public void InvokeCompletion(nint stream, nint selector, nint completion)
        {
            Interlocked.Increment(ref NativeInvocationAttempts);
            Assert.Equal(Stream, stream);
            nint descriptor = Marshal.ReadIntPtr(completion, 24);
            Assert.Equal("v16@?0@8", Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(descriptor, 32)));
            if (selector == 1)
            {
                StartBorrowedPointer = completion;
                Interlocked.Increment(ref StartInvocations);
            }
            else { Assert.Equal(2, selector); Interlocked.Increment(ref StopInvocations); }
            if (selector == 1 && StartInvocationFailureBeforeCallbackAfterEffect is { } beforeCallbackFailure)
            {
                // Actual extra heap retain, not merely a saved borrowed address.
                Volatile.Write(ref NativeHeldCompletion, runtime.CopyBlock(completion));
                NativeHeldCopyReady.TrySetResult();
                throw beforeCallbackFailure;
            }
            if (selector == 2 && StopInvocationFailureBeforeCallbackAfterEffect is { } stopBeforeCallbackFailure)
            {
                Volatile.Write(ref NativeHeldCompletion, runtime.CopyBlock(completion));
                NativeHeldCopyReady.TrySetResult();
                throw stopBeforeCallbackFailure;
            }
            if (selector == 1 && StartInvocationReturnsBeforeCallback)
            {
                Volatile.Write(ref NativeHeldCompletion, runtime.CopyBlock(completion));
                NativeHeldCopyReady.TrySetResult();
                return;
            }
            if (selector == 2 && HoldExternalStopCopy)
            {
                // A confirmed extra heap retain independent of the copy slot
                // that the simulated stream automatically releases below.
                Volatile.Write(ref ExternallyHeldStopCopy, runtime.CopyBlock(completion));
            }
            runtime.Invoke(completion, 0);
            if (selector == 1) { StartInvocationAfterCallbackHook?.Invoke(); }
            if (selector == 1 && StartInvocationFailureAfterEffect is { } failure) { throw failure; }
            if (selector == 2 && StopInvocationFailureAfterEffect is { } stopFailure) { throw stopFailure; }
        }

        internal void ReleaseExternalStopCopy()
        {
            nint held = Interlocked.Exchange(ref ExternallyHeldStopCopy, 0);
            if (held != 0)
            {
                Interlocked.Increment(ref ExternalStopCopyReleaseAttempts);
                runtime.ReleaseBlock(held);
            }
        }

        public void DrainSampleQueue(nint queue)
        {
            Assert.Equal(Queue, queue);
            Interlocked.Increment(ref DrainCalls);
        }

        public void ReleaseObject(nint owner)
        {
            ObjectReleaseAttempts.Enqueue(owner);
            if (owner == Stream)
            {
                nint held = Interlocked.Exchange(ref NativeHeldCompletion, 0);
                if (held != 0)
                {
                    Interlocked.Increment(ref NativeHeldReleaseAttempts);
                    runtime.ReleaseBlock(held);
                }
            }
        }
        public void ReleaseQueue(nint queue)
        {
            Assert.Equal(Queue, queue);
            Interlocked.Increment(ref QueueReleaseAttempts);
        }

        public nint? GetSampleFrameStatus(nint sample) => throw new NotSupportedException();
        public bool IsSampleReady(nint sample) => throw new NotSupportedException();
        public IMacOSRemoteWindowNativeSample RetainSample(nint sample, int width, int height) =>
            throw new NotSupportedException();
    }
}

[CollectionDefinition("macOS Capture ownership", DisableParallelization = true)]
public sealed class MacOSCaptureCompletionOwnershipCollectionDefinition
{
}
