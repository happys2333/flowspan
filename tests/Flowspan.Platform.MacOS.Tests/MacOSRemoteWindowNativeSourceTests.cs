using System.Collections.Concurrent;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed partial class MacOSRemoteWindowScreenCaptureKitCaptureTests
{
    [Fact]
    public void BaseSourceReleaseDoesNotBlockClosingObserversOrRepeatConcurrentCleanup()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var staged = Assert.IsAssignableFrom<IMacOSRemoteWindowStagedCaptureSource>(source);
        IMacOSRemoteWindowStagedCaptureSource prepared = staged.PrepareOwner();
        IMacOSRemoteWindowStagedCaptureSource? unexpectedNewOwner = null;
        using var releaseEntered = new ManualResetEventSlim();
        using var allowReleaseExit = new ManualResetEventSlim();
        using var disposerReturned = new ManualResetEventSlim();
        using var observerReturned = new ManualResetEventSlim();
        using var concurrentReturned = new ManualResetEventSlim();
        Exception? disposeFailure = null;
        Exception? observerFailure = null;
        Exception? concurrentFailure = null;
        Exception? prepareFailure = null;
        Exception? acquireFailure = null;
        bool currentResult = true;
        effects.BeforeRelease = address =>
        {
            if (address == SourceEffects.Filter)
            {
                releaseEntered.Set();
                allowReleaseExit.Wait();
            }
        };
        var disposer = new Thread(() =>
        {
            try { source.Dispose(); }
            catch (Exception failure) { disposeFailure = failure; }
            finally { disposerReturned.Set(); }
        })
        { IsBackground = true };
        var observer = new Thread(() =>
        {
            try
            {
                try { currentResult = source.IsCurrent(); }
                catch (Exception failure) { observerFailure = failure; }
                try { unexpectedNewOwner = staged.PrepareOwner(); }
                catch (Exception failure) { prepareFailure = failure; }
                try { prepared.AcquireOwner(); }
                catch (Exception failure) { acquireFailure = failure; }
            }
            finally { observerReturned.Set(); }
        })
        { IsBackground = true };
        var concurrentDisposer = new Thread(() =>
        {
            try { source.Dispose(); }
            catch (Exception failure) { concurrentFailure = failure; }
            finally { concurrentReturned.Set(); }
        })
        { IsBackground = true };
        bool disposerStarted = false;
        bool observerStarted = false;
        bool concurrentStarted = false;
        bool disposerJoined = true;
        bool observerJoined = true;
        bool concurrentJoined = true;
        try
        {
            disposer.Start();
            disposerStarted = true;
            Assert.True(releaseEntered.Wait(Timeout));
            observer.Start();
            observerStarted = true;
            concurrentDisposer.Start();
            concurrentStarted = true;
            Assert.True(observerReturned.Wait(Timeout), "Closing source observation waited behind a held release effect.");
            Assert.True(concurrentReturned.Wait(Timeout), "Concurrent source cleanup waited behind a held release effect.");
            Assert.Null(observerFailure);
            Assert.False(currentResult);
            Assert.IsType<ObjectDisposedException>(prepareFailure);
            Assert.IsType<ObjectDisposedException>(acquireFailure);
            Assert.Null(unexpectedNewOwner);
            Assert.IsType<InvalidOperationException>(concurrentFailure);
            Assert.False(disposerReturned.IsSet);
            Assert.Equal(new[] { SourceEffects.Filter }, effects.ReleaseAttempts);
            Assert.Empty(effects.RetainAttempts);
            Assert.Equal(0, effects.CurrentChecks);
            Assert.Equal(1, effects.References[SourceEffects.Filter]);
            Assert.Equal(1, effects.References[SourceEffects.Window]);
        }
        finally
        {
            allowReleaseExit.Set();
            if (disposerStarted) { disposerJoined = disposer.Join(Timeout); }
            if (observerStarted) { observerJoined = observer.Join(Timeout); }
            if (concurrentStarted) { concurrentJoined = concurrentDisposer.Join(Timeout); }
            effects.BeforeRelease = null;
            if (disposerJoined && observerJoined && concurrentJoined)
            {
                prepared.Dispose();
                unexpectedNewOwner?.Dispose();
            }
        }
        Assert.True(disposerJoined);
        Assert.True(observerJoined);
        Assert.True(concurrentJoined);
        Assert.Null(disposeFailure);
        source.Dispose();
        source.Dispose();
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.Empty(effects.RetainAttempts);
        Assert.Equal(0, effects.CurrentChecks);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public Task UnknownRealSourceGraphSurvivesWithoutTheFactoryMailbox(int failedRetain) =>
        Task.Factory.StartNew(() => VerifyUnknownRealSourceGraph(failedRetain),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void VerifyUnknownRealSourceGraph(int failedRetain)
    {
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        WeakNativeSourceGraph graph = CreateWeakUnknownRealSourceGraph(failedRetain);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.True(graph.Capture.TryGetTarget(out IMacOSRemoteWindowNativeCapture? capture));
        Assert.NotNull(capture);
        Assert.True(graph.SourceEffects.TryGetTarget(out SourceEffects? sourceEffects));
        Assert.NotNull(sourceEffects);
        Assert.True(graph.CaptureEffects.TryGetTarget(out TestOperations? captureEffects));
        Assert.NotNull(captureEffects);
        Assert.True(graph.CallbackOwner.TryGetTarget(out ConcurrentQueue<int>? callbackOwner));
        Assert.NotNull(callbackOwner);
        Assert.True(graph.Api.TryGetTarget(out _));
        Assert.True(graph.Fatal.TryGetTarget(out OutOfMemoryException? fatal));
        Assert.NotNull(fatal);
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(capture.Dispose));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(capture.Dispose));
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(failedRetain == 0 ? new[] { SourceEffects.Window }
            : new[] { SourceEffects.Window, SourceEffects.Filter }, sourceEffects.RetainAttempts);
        Assert.Equal(failedRetain == 0 ? new[] { SourceEffects.Filter, SourceEffects.Window }
            : new[] { SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, sourceEffects.ReleaseAttempts);
        Assert.Equal(failedRetain == 0 ? 1 : 0, sourceEffects.References[SourceEffects.Window]);
        Assert.Equal(failedRetain == 1 ? 1 : 0, sourceEffects.References[SourceEffects.Filter]);
        Assert.Empty(captureEffects.ObjectReleaseAttempts);
        Assert.Equal(0, captureEffects.PoolsPushed);
        Assert.Empty(callbackOwner);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakNativeSourceGraph CreateWeakUnknownRealSourceGraph(int failedRetain)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var operations = new TestOperations();
        var callbackOwner = new ConcurrentQueue<int>();
#pragma warning disable CA2201 // Original-fatal identity injection at the retain boundary.
        var fatal = new OutOfMemoryException("rooted real source retain fatal");
#pragma warning restore CA2201
        effects.AfterRetain = owner =>
        {
            if (owner == (failedRetain == 0 ? SourceEffects.Window : SourceEffects.Filter))
            {
                throw new InvalidOperationException("retain wrapper", fatal);
            }
        };
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            api.CreateCaptureWithOperations(source, operations,
                sample => { callbackOwner.Enqueue(1); sample.Dispose(); },
                () => callbackOwner.Enqueue(2))));
        Assert.True(api.TryTakeFailedCapture(fatal, out IMacOSRemoteWindowNativeCapture? capture));
        Assert.NotNull(capture);
        Assert.False(api.TryTakeFailedCapture(fatal, out _));
        source.Dispose();
        return new(new(capture), new(effects), new(operations), new(callbackOwner), new(api), new(fatal));
    }

    private sealed record WeakNativeSourceGraph(
        WeakReference<IMacOSRemoteWindowNativeCapture> Capture,
        WeakReference<SourceEffects> SourceEffects,
        WeakReference<TestOperations> CaptureEffects,
        WeakReference<ConcurrentQueue<int>> CallbackOwner,
        WeakReference<MacOSRemoteWindowScreenCaptureKitApi> Api,
        WeakReference<OutOfMemoryException> Fatal);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentCheckFailureDoesNotCreateNativeOwnershipDebt(bool nestedFatal)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        using IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
#pragma warning disable CA2201 // Fatal injection at a non-ownership system boundary.
        var fatal = new OutOfMemoryException("current check fatal");
#pragma warning restore CA2201
        var failure = new InvalidOperationException("current check failure", nestedFatal ? fatal : null);
        effects.DuringCurrentCheck = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => source.IsCurrent()));
        effects.DuringCurrentCheck = null;
        source.Dispose();
        source.Dispose();
        Assert.False(source.IsCurrent());
        Assert.Equal(1, effects.CurrentChecks);
        Assert.Empty(effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedTokenClosingBeforeRetainHasKnownAbsence(bool closeParent)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        using IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var staged = Assert.IsAssignableFrom<IMacOSRemoteWindowStagedCaptureSource>(source);
        using IMacOSRemoteWindowStagedCaptureSource owner = staged.PrepareOwner();
        if (closeParent) { source.Dispose(); }
        else { owner.Dispose(); }
        Assert.Throws<ObjectDisposedException>(owner.AcquireOwner);
        owner.Dispose();
        owner.Dispose();
        source.Dispose();
        Assert.Empty(effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public void BaseSourceFatalReleaseStillAttemptsTheIndependentOwnerOnce()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
#pragma warning disable CA2201 // Fatal identity injection at the native release boundary.
        var fatal = new OutOfMemoryException("base source filter release fatal");
#pragma warning restore CA2201
        effects.AfterRelease = owner =>
        {
            if (owner == SourceEffects.Filter) { throw new InvalidOperationException("release wrapper", fatal); }
        };
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(source.Dispose));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(source.Dispose));
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public void PreparedOwnerClosingJoinsTheAdmittedRetainAndRejectsNewAcquisition()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var staged = Assert.IsAssignableFrom<IMacOSRemoteWindowStagedCaptureSource>(source);
        IMacOSRemoteWindowStagedCaptureSource owner = staged.PrepareOwner();
        using var retainEntered = new ManualResetEventSlim();
        using var allowRetainExit = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();
        using var observerReturned = new ManualResetEventSlim();
        Exception? acquireFailure = null;
        Exception? disposeFailure = null;
        Exception? observerFailure = null;
        bool closingObserved = false;
        effects.BeforeRetain = address =>
        {
            if (address == SourceEffects.Window) { retainEntered.Set(); allowRetainExit.Wait(); }
        };
        var acquirer = new Thread(() =>
        {
            try { owner.AcquireOwner(); }
            catch (Exception failure) { acquireFailure = failure; }
        })
        { IsBackground = true };
        var disposer = new Thread(() =>
        {
            try { owner.Dispose(); }
            catch (Exception failure) { disposeFailure = failure; }
            finally { disposeReturned.Set(); }
        })
        { IsBackground = true };
        var observer = new Thread(() =>
        {
            try
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (elapsed.Elapsed < Timeout)
                {
                    try { owner.AcquireOwner(); }
                    catch (ObjectDisposedException) { closingObserved = true; break; }
                    catch (InvalidOperationException) { }
                    Thread.Yield();
                }
            }
            catch (Exception failure) { observerFailure = failure; }
            finally { observerReturned.Set(); }
        })
        { IsBackground = true };
        bool acquireStarted = false;
        bool disposeStarted = false;
        bool observerStarted = false;
        try
        {
            acquirer.Start();
            acquireStarted = true;
            Assert.True(retainEntered.Wait(Timeout));
            disposer.Start();
            disposeStarted = true;
            observer.Start();
            observerStarted = true;
            Assert.True(observerReturned.Wait(Timeout));
            Assert.Null(observerFailure);
            Assert.True(closingObserved);
            Assert.False(disposeReturned.IsSet);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowRetainExit.Set();
            if (acquireStarted) { Assert.True(acquirer.Join(Timeout)); }
            if (disposeStarted) { Assert.True(disposer.Join(Timeout)); }
            if (observerStarted) { Assert.True(observer.Join(Timeout)); }
            effects.BeforeRetain = null;
            owner.Dispose();
            source.Dispose();
        }
        Assert.IsType<ObjectDisposedException>(acquireFailure);
        Assert.Null(disposeFailure);
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task ActiveRetainDescendantCannotJoinEitherPinnedSource()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var staged = Assert.IsAssignableFrom<IMacOSRemoteWindowStagedCaptureSource>(source);
        IMacOSRemoteWindowStagedCaptureSource owner = staged.PrepareOwner();
        using var retainEntered = new ManualResetEventSlim();
        using var allowRetainExit = new ManualResetEventSlim();
        using var childReturned = new ManualResetEventSlim();
        Task? child = null;
        Exception? parentFailure = null;
        Exception? ownerFailure = null;
        Exception? acquireFailure = null;
        effects.BeforeRetain = address =>
        {
            if (address != SourceEffects.Window) { return; }
            child = Task.Run(() =>
            {
                try { source.Dispose(); }
                catch (Exception failure) { parentFailure = failure; }
                try { owner.Dispose(); }
                catch (Exception failure) { ownerFailure = failure; }
                finally { childReturned.Set(); }
            });
            retainEntered.Set();
            allowRetainExit.Wait();
        };
        var acquirer = new Thread(() =>
        {
            try { owner.AcquireOwner(); }
            catch (Exception failure) { acquireFailure = failure; }
        })
        { IsBackground = true };
        bool started = false;
        try
        {
            acquirer.Start();
            started = true;
            Assert.True(retainEntered.Wait(Timeout));
            Assert.True(childReturned.Wait(Timeout));
            Assert.IsType<InvalidOperationException>(parentFailure);
            Assert.IsType<InvalidOperationException>(ownerFailure);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowRetainExit.Set();
            if (started) { Assert.True(acquirer.Join(Timeout)); }
            if (child is not null) { await child.WaitAsync(Timeout); }
            effects.BeforeRetain = null;
            owner.Dispose();
            source.Dispose();
        }
        Assert.IsType<ObjectDisposedException>(acquireFailure);
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Theory]
    [InlineData(0, "before")]
    [InlineData(0, "after")]
    [InlineData(0, "nil")]
    [InlineData(0, "foreign")]
    [InlineData(1, "before")]
    [InlineData(1, "after")]
    [InlineData(1, "nil")]
    [InlineData(1, "foreign")]
    public Task UnknownRetainEffectsKeepTheRootWithoutReleasingBorrowedAddresses(
        int failedRetain, string fault) => Task.Factory.StartNew(
            () => VerifyUnknownRetainEffects(failedRetain, fault), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void VerifyUnknownRetainEffects(int failedRetain, string fault)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var operations = new TestOperations();
        nint selected = failedRetain == 0 ? SourceEffects.Window : SourceEffects.Filter;
        var injected = new InvalidOperationException("unknown native retain effect");
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        int rootAtFirstRetain = -1;
        effects.BeforeRetain = owner =>
        {
            if (owner == SourceEffects.Window)
            {
                rootAtFirstRetain = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
            }
            if (owner == selected && fault == "before") { throw injected; }
        };
        effects.AfterRetain = owner =>
        {
            if (owner == selected && fault == "after") { throw injected; }
        };
        effects.RetainResult = owner => owner != selected ? owner
            : fault == "nil" ? 0 : fault == "foreign" ? (nint)99 : owner;
        try
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { }));
            if (fault is "before" or "after") { Assert.Same(injected, failure); }
            Assert.Equal(before + 1, rootAtFirstRetain);
            Assert.True(api.TryTakeFailedCapture(failure, out IMacOSRemoteWindowNativeCapture? failed));
            Assert.NotNull(failed);
            Assert.True(failed.IsDrained);
            InvalidOperationException disposal = Assert.Throws<InvalidOperationException>(failed.Dispose);
            Assert.Same(disposal, Assert.Throws<InvalidOperationException>(failed.Dispose));
            Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
            Assert.Equal(failedRetain == 0 ? new[] { SourceEffects.Window }
                : new[] { SourceEffects.Window, SourceEffects.Filter }, effects.RetainAttempts);
            Assert.Equal(failedRetain == 0 ? Array.Empty<nint>() : new[] { SourceEffects.Window }, effects.ReleaseAttempts);
            Assert.Equal(failedRetain == 0 && fault != "before" ? 2 : 1, effects.References[SourceEffects.Window]);
            Assert.Equal(failedRetain == 1 && fault != "before" ? 2 : 1, effects.References[SourceEffects.Filter]);
            Assert.True(source.IsCurrent());
            Assert.Empty(operations.ObjectReleaseAttempts);
            Assert.Equal(0, operations.PoolsPushed);
        }
        finally
        {
            effects.BeforeRetain = null;
            effects.AfterRetain = null;
            source.Dispose();
        }
        Assert.Equal(failedRetain == 0 && fault != "before" ? 1 : 0, effects.References[SourceEffects.Window]);
        Assert.Equal(failedRetain == 1 && fault != "before" ? 1 : 0, effects.References[SourceEffects.Filter]);
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
    }

    [Fact]
    public void BaseSourceFilterReleaseFaultDoesNotSkipWindowOrRetryUnknownRelease()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        effects.AfterRelease = owner =>
        {
            if (owner == SourceEffects.Filter) { throw new InvalidOperationException("consumed base source filter release"); }
        };

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(source.Dispose);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.False(source.IsCurrent());
        Assert.Same(first, Assert.Throws<InvalidOperationException>(source.Dispose));
        Assert.Same(first, Assert.Throws<InvalidOperationException>(source.Dispose));
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.Empty(effects.RetainAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task NativeSourceClosingRejectsAdmissionWhileJoiningAnAdmittedRetainUse()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var staged = Assert.IsAssignableFrom<IMacOSRemoteWindowStagedCaptureSource>(source);
        var operations = new TestOperations();
        using var retainEntered = new ManualResetEventSlim();
        using var allowRetainExit = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();
        using var observerReturned = new ManualResetEventSlim();
        effects.BeforeRetain = owner =>
        {
            if (owner == SourceEffects.Window) { retainEntered.Set(); allowRetainExit.Wait(); }
        };
        IMacOSRemoteWindowNativeCapture? capture = null;
        Exception? acquireFailure = null;
        Exception? disposeFailure = null;
        Exception? observerFailure = null;
        bool closingObserved = false;
        bool newCurrentRejected = false;
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        var acquirer = new Thread(() =>
        {
            try { capture = api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { }); }
            catch (Exception failure) { acquireFailure = failure; }
        })
        { IsBackground = true };
        var disposer = new Thread(() =>
        {
            disposeStarted.Set();
            try { source.Dispose(); }
            catch (Exception failure) { disposeFailure = failure; }
            finally { disposeReturned.Set(); }
        })
        { IsBackground = true };
        var observer = new Thread(() =>
        {
            try
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (elapsed.Elapsed < Timeout)
                {
                    try { staged.PrepareOwner().Dispose(); }
                    catch (ObjectDisposedException) { closingObserved = true; break; }
                    Thread.Yield();
                }
                if (closingObserved) { newCurrentRejected = !source.IsCurrent(); }
            }
            catch (Exception failure) { observerFailure = failure; }
            finally { observerReturned.Set(); }
        })
        { IsBackground = true };
        bool acquireStarted = false;
        bool disposerStarted = false;
        bool observerStarted = false;
        try
        {
            acquirer.Start();
            acquireStarted = true;
            Assert.True(retainEntered.Wait(Timeout));
            disposer.Start();
            disposerStarted = true;
            Assert.True(disposeStarted.Wait(Timeout));
            observer.Start();
            observerStarted = true;
            Assert.True(observerReturned.Wait(Timeout), "New admission waited behind an admitted retain effect.");
            Assert.Null(observerFailure);
            Assert.True(closingObserved);
            Assert.True(newCurrentRejected);
            Assert.False(disposeReturned.IsSet);
            Assert.Empty(effects.ReleaseAttempts);
            Assert.Equal(1, effects.CurrentChecks); // The factory's pre-acquisition check only.
        }
        finally
        {
            allowRetainExit.Set();
            if (acquireStarted) { Assert.True(acquirer.Join(Timeout)); }
            if (disposerStarted) { Assert.True(disposer.Join(Timeout)); }
            if (observerStarted) { Assert.True(observer.Join(Timeout)); }
            effects.BeforeRetain = null;
            if (capture is not null)
            {
                await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
                capture.Dispose();
            }
            source.Dispose();
        }
        Assert.Null(acquireFailure);
        Assert.Null(disposeFailure);
        Assert.NotNull(capture);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
        Assert.Equal(1, effects.CurrentChecks);
    }

    [Fact]
    public void DirectCurrentUseCannotJoinItsOwnSourceCleanup()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        using IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        Exception? selfJoinFailure = null;
        effects.DuringCurrentCheck = () =>
        {
            try { source.Dispose(); }
            catch (Exception failure) { selfJoinFailure = failure; }
        };

        Assert.False(source.IsCurrent());
        Assert.IsType<InvalidOperationException>(selfJoinFailure);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal(1, effects.CurrentChecks);
        effects.DuringCurrentCheck = null;
        source.Dispose();
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task StaleCurrentDescendantCanDisposeAfterTheUseExits()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        using IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        using var allowChild = new ManualResetEventSlim();
        Task? child = null;
        effects.DuringCurrentCheck = () => child = Task.Run(() =>
        {
            allowChild.Wait();
            source.Dispose();
        });
        try
        {
            Assert.True(source.IsCurrent());
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowChild.Set();
            if (child is not null) { await child.WaitAsync(Timeout); }
            effects.DuringCurrentCheck = null;
        }
        Assert.NotNull(child);
        Assert.Equal(1, effects.CurrentChecks);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public void CurrentUseAncestryDoesNotBlockAnUnrelatedSourceCleanup()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        var otherEffects = new SourceEffects();
        using IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        using IMacOSRemoteWindowCaptureSource other = CreateRealSource(api, otherEffects);
        Exception? otherFailure = null;
        effects.DuringCurrentCheck = () =>
        {
            try { other.Dispose(); }
            catch (Exception failure) { otherFailure = failure; }
        };
        Assert.True(source.IsCurrent());
        Assert.Null(otherFailure);
        Assert.Empty(effects.ReleaseAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, otherEffects.ReleaseAttempts);
        Assert.All(otherEffects.References.Values, count => Assert.Equal(0, count));
        effects.DuringCurrentCheck = null;
        source.Dispose();
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task ActiveCurrentDescendantCannotJoinItsOwnSourceUse()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        using var useEntered = new ManualResetEventSlim();
        using var allowUseExit = new ManualResetEventSlim();
        using var childReturned = new ManualResetEventSlim();
        Task? child = null;
        Exception? childFailure = null;
        Exception? useFailure = null;
        bool useResult = true;
        effects.DuringCurrentCheck = () =>
        {
            child = Task.Run(() =>
            {
                try { source.Dispose(); }
                catch (Exception failure) { childFailure = failure; }
                finally { childReturned.Set(); }
            });
            useEntered.Set();
            allowUseExit.Wait();
        };
        var user = new Thread(() =>
        {
            try { useResult = source.IsCurrent(); }
            catch (Exception failure) { useFailure = failure; }
        })
        { IsBackground = true };
        bool started = false;
        try
        {
            user.Start();
            started = true;
            Assert.True(useEntered.Wait(Timeout));
            Assert.True(childReturned.Wait(Timeout), "An active current-check descendant waited for its own source use.");
            Assert.IsType<InvalidOperationException>(childFailure);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowUseExit.Set();
            if (started) { Assert.True(user.Join(Timeout)); }
            if (child is not null) { await child.WaitAsync(Timeout); }
            effects.DuringCurrentCheck = null;
            source.Dispose();
        }
        Assert.Null(useFailure);
        Assert.False(useResult);
        Assert.Equal(1, effects.CurrentChecks);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public void NativeSourceClosingRejectsNewAdmissionWhileJoiningAnAdmittedCurrentUse()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var staged = Assert.IsAssignableFrom<IMacOSRemoteWindowStagedCaptureSource>(source);
        using var useEntered = new ManualResetEventSlim();
        using var allowUseExit = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();
        using var observerReturned = new ManualResetEventSlim();
        effects.DuringCurrentCheck = () => { useEntered.Set(); allowUseExit.Wait(); };
        Exception? useFailure = null;
        Exception? disposeFailure = null;
        Exception? observerFailure = null;
        bool useResult = true;
        bool closingObserved = false;
        bool newCurrentRejected = false;
        var user = new Thread(() =>
        {
            try { useResult = source.IsCurrent(); }
            catch (Exception failure) { useFailure = failure; }
        })
        { IsBackground = true };
        var disposer = new Thread(() =>
        {
            disposeStarted.Set();
            try { source.Dispose(); }
            catch (Exception failure) { disposeFailure = failure; }
            finally { disposeReturned.Set(); }
        })
        { IsBackground = true };
        var observer = new Thread(() =>
        {
            try
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (elapsed.Elapsed < Timeout)
                {
                    try { staged.PrepareOwner().Dispose(); }
                    catch (ObjectDisposedException) { closingObserved = true; break; }
                    Thread.Yield();
                }
                if (closingObserved) { newCurrentRejected = !source.IsCurrent(); }
            }
            catch (Exception failure) { observerFailure = failure; }
            finally { observerReturned.Set(); }
        })
        { IsBackground = true };
        bool userStarted = false;
        bool disposerStarted = false;
        bool observerStarted = false;
        try
        {
            user.Start();
            userStarted = true;
            Assert.True(useEntered.Wait(Timeout));
            disposer.Start();
            disposerStarted = true;
            Assert.True(disposeStarted.Wait(Timeout));
            observer.Start();
            observerStarted = true;
            Assert.True(observerReturned.Wait(Timeout), "New source admission did not return while an admitted current check remained held.");
            Assert.Null(observerFailure);
            Assert.True(closingObserved);
            Assert.True(newCurrentRejected);
            Assert.False(disposeReturned.IsSet);
            Assert.Empty(effects.ReleaseAttempts);
        }
        finally
        {
            allowUseExit.Set();
            if (userStarted) { Assert.True(user.Join(Timeout)); }
            if (disposerStarted) { Assert.True(disposer.Join(Timeout)); }
            if (observerStarted) { Assert.True(observer.Join(Timeout)); }
            effects.DuringCurrentCheck = null;
            source.Dispose();
        }
        Assert.Null(useFailure);
        Assert.Null(disposeFailure);
        Assert.False(useResult);
        Assert.Equal(1, effects.CurrentChecks);
        Assert.Empty(effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
    }

    [Fact]
    public Task PartialRetainCleanupFaultCannotReplaceOriginalFatal() =>
        Task.Factory.StartNew(VerifyPartialRetainCleanupFault, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void VerifyPartialRetainCleanupFault()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var operations = new TestOperations();
#pragma warning disable CA2201 // Original-fatal identity injection at the native retain boundary.
        var fatal = new OutOfMemoryException("original partial-retain fatal");
#pragma warning restore CA2201
        var cleanupFailure = new InvalidOperationException("consumed window cleanup failure");
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        int rootAtFirstRetain = -1;
        effects.BeforeRetain = owner =>
        {
            if (owner == SourceEffects.Window)
            {
                rootAtFirstRetain = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
            }
        };
        effects.AfterRetain = owner =>
        {
            if (owner == SourceEffects.Filter) { throw new InvalidOperationException("retain wrapper", fatal); }
        };
        effects.AfterRelease = owner =>
        {
            if (owner == SourceEffects.Window) { throw cleanupFailure; }
        };
        try
        {
            Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
                api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { })));
            Assert.Equal(before + 1, rootAtFirstRetain);
            Assert.True(api.TryTakeFailedCapture(fatal, out IMacOSRemoteWindowNativeCapture? failed));
            Assert.NotNull(failed);
            Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(failed.Dispose));
            Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(failed.Dispose));
            Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
            Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, effects.RetainAttempts);
            Assert.Equal(new[] { SourceEffects.Window }, effects.ReleaseAttempts);
            Assert.Equal(1, effects.References[SourceEffects.Window]);
            Assert.Equal(2, effects.References[SourceEffects.Filter]);
            Assert.Empty(operations.ObjectReleaseAttempts);
            Assert.Equal(0, operations.PoolsPushed);
        }
        finally
        {
            effects.AfterRetain = null;
            effects.AfterRelease = null;
            source.Dispose();
        }
        Assert.Equal(0, effects.References[SourceEffects.Window]);
        Assert.Equal(1, effects.References[SourceEffects.Filter]);
    }

    [Fact]
    public async Task RealNativeSourceUsesOnlyInjectedEffectsThroughHealthyCaptureLifecycle()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var effects = new SourceEffects();
        using IMacOSRemoteWindowCaptureSource source = CreateRealSource(api, effects);
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        try
        {
            Assert.Equal(2, effects.References[SourceEffects.Window]);
            Assert.Equal(2, effects.References[SourceEffects.Filter]);
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        }
        finally
        {
            await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            capture.Dispose();
        }

        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, effects.References[SourceEffects.Window]);
        Assert.Equal(1, effects.References[SourceEffects.Filter]);
        source.Dispose();
        Assert.Equal(new[] { SourceEffects.Window, SourceEffects.Filter }, effects.RetainAttempts);
        Assert.Equal(new[] { SourceEffects.Filter, SourceEffects.Window, SourceEffects.Filter, SourceEffects.Window }, effects.ReleaseAttempts);
        Assert.All(effects.References.Values, count => Assert.Equal(0, count));
    }

    private static IMacOSRemoteWindowCaptureSource CreateRealSource(
        MacOSRemoteWindowScreenCaptureKitApi api, SourceEffects effects) =>
        api.CreateCaptureSourceWithOperations(new(1, 123, 1, 0),
            NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2),
            SourceEffects.Window, SourceEffects.Filter, effects);

    private sealed class SourceEffects : IMacOSRemoteWindowSourceOperations
    {
        internal const nint Window = 41;
        internal const nint Filter = 42;
        internal ConcurrentDictionary<nint, int> References { get; } = new(new[]
        {
            new KeyValuePair<nint, int>(Window, 1), new KeyValuePair<nint, int>(Filter, 1),
        });
        internal ConcurrentQueue<nint> RetainAttempts { get; } = new();
        internal ConcurrentQueue<nint> ReleaseAttempts { get; } = new();
        internal Action<nint>? BeforeRetain { get; set; }
        internal Action<nint>? AfterRetain { get; set; }
        internal Func<nint, nint>? RetainResult { get; set; }
        internal Action<nint>? BeforeRelease { get; set; }
        internal Action<nint>? AfterRelease { get; set; }
        internal Action? DuringCurrentCheck { get; set; }
        internal int CurrentChecks;
        public nint Retain(nint owner)
        {
            RetainAttempts.Enqueue(owner);
            BeforeRetain?.Invoke(owner);
            Assert.True(References.AddOrUpdate(owner, 1, (_, count) => count + 1) >= 2);
            AfterRetain?.Invoke(owner);
            return RetainResult?.Invoke(owner) ?? owner;
        }

        public void Release(nint owner)
        {
            ReleaseAttempts.Enqueue(owner);
            BeforeRelease?.Invoke(owner);
            Assert.True(References.AddOrUpdate(owner, -1, (_, count) => count - 1) >= 0);
            AfterRelease?.Invoke(owner);
        }

        public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
            NativeRemoteWindowGeometry geometry)
        {
            Assert.Equal(Filter, filter);
            Assert.Equal(1u, identity.WindowId);
            Assert.Equal(80, geometry.Width);
            Interlocked.Increment(ref CurrentChecks);
            DuringCurrentCheck?.Invoke();
            return true;
        }
    }
}
