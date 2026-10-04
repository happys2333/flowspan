using System.Collections.Concurrent;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed partial class MacOSRemoteWindowScreenCaptureKitCaptureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void UnknownSourceRetainFatalKeepsOriginalIdentityOnRepeatedDispose()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
#pragma warning disable CA2201 // Intentional original-fatal identity injection at the source boundary.
        var fatal = new OutOfMemoryException("original opaque retain fatal");
#pragma warning restore CA2201
        source.State.RetainFailure = new InvalidOperationException("retain wrapper", fatal);
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;

        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { })));
        Assert.True(api.TryTakeFailedCapture(fatal, out IMacOSRemoteWindowNativeCapture? failed));
        Assert.NotNull(failed);
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(failed.Dispose));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(failed.Dispose));
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.True(source.IsCurrent());
        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(0, source.State.ReleaseCalls);
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.PoolsPushed);
    }

    [Fact]
    public async Task RejectedOutputAddressRegistrationDoesNotRemoveAnotherCapturesLookup()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var rejectedSource = new TestSource();
        var operations = new TestOperations();
        int delivered = 0;
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => { delivered++; sample.Dispose(); }, () => { });
        var rejectedOperations = new TestOperations { OutputAddressOverride = operations.Output };
        try
        {
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                api.CreateCaptureWithOperations(rejectedSource, rejectedOperations, sample => sample.Dispose(), () => { }));
            Assert.Equal("macOS callback registration unavailable.", failure.Message);
            Assert.False(api.TryTakeFailedCapture(failure, out _));
            operations.DeliverSample();
            Assert.Equal(1, delivered);
            Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
            Assert.Equal(1, rejectedSource.State.OwnerCount);
            Assert.Equal(new[] { rejectedOperations.Output, rejectedOperations.Configuration }, rejectedOperations.ObjectReleaseAttempts);
            Assert.Equal(1, rejectedOperations.ReleasedQueues);
        }
        finally
        {
            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
            capture.Dispose();
        }
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
    }

    [Fact]
    public async Task RepeatedUncertainOutputCleanupDoesNotRemoveReplacementCaptureAtReusedAddress()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var oldSource = new TestSource();
        using var replacementSource = new TestSource();
        var oldOperations = new TestOperations();
        oldOperations.AfterObjectRelease = owner =>
        {
            if (owner == oldOperations.Output) { throw new InvalidOperationException("consumed output release"); }
        };
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture oldCapture = api.CreateCaptureWithOperations(
            oldSource, oldOperations, sample => sample.Dispose(), () => { });
        Assert.True(await oldCapture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.True(await oldCapture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        InvalidOperationException disposal = Assert.Throws<InvalidOperationException>(oldCapture.Dispose);

        var replacementOperations = new TestOperations { OutputAddressOverride = oldOperations.Output };
        int delivered = 0;
        IMacOSRemoteWindowNativeCapture replacement = api.CreateCaptureWithOperations(
            replacementSource, replacementOperations, sample => { delivered++; sample.Dispose(); }, () => { });
        try
        {
            Assert.True(await replacement.StartAsync().AsTask().WaitAsync(Timeout));
            Assert.Same(disposal, Assert.Throws<InvalidOperationException>(oldCapture.Dispose));
            Assert.Same(disposal, Assert.Throws<InvalidOperationException>(oldCapture.Dispose));
            replacementOperations.DeliverSample();
            Assert.Equal(1, delivered);
            Assert.Equal(1, oldOperations.ObjectReleaseAttempts.Count(owner => owner == oldOperations.Output));
            Assert.Equal(before + 2, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        }
        finally
        {
            Assert.True(await replacement.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
            replacement.Dispose();
        }

        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, oldSource.State.OwnerCount);
        Assert.Equal(1, replacementSource.State.OwnerCount);
    }

    [Fact]
    public void ThrowingSourceRetainKeepsShellButNeverDisposesBorrowedSource()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var failure = new InvalidOperationException("opaque source retain failure");
        source.State.RetainFailure = failure;
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { })));
        Assert.True(api.TryTakeFailedCapture(failure, out IMacOSRemoteWindowNativeCapture? failed));
        Assert.NotNull(failed);
        InvalidOperationException disposal = Assert.Throws<InvalidOperationException>(failed.Dispose);
        Assert.Same(disposal, Assert.Throws<InvalidOperationException>(failed.Dispose));
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.True(source.IsCurrent());
        Assert.Equal(1, source.State.OwnerCount);
        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(0, source.State.ReleaseCalls);
        Assert.Equal(0, source.State.RetainedReleaseAttempts);
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.PoolsPushed);
    }

    [Fact]
    public void InvalidPixelGeometryReleasesKnownSourceAndRootWhenCleanupIsConfirmed()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        source.State.Geometry = NativeRemoteWindowGeometry.Create(0, 0, 16_384, 16_384, 1);
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { }));
        Assert.Equal("macOS window pixel bounds unavailable.", failure.Message);
        Assert.False(api.TryTakeFailedCapture(failure, out _));
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.Equal(1, source.State.OwnerCount);
        Assert.True(source.IsCurrent());
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.PoolsPushed);
    }

    [Fact]
    public async Task NestedFatalReleaseFailureKeepsOriginalIdentityAndIndependentCleanupSingleAttempt()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
#pragma warning disable CA2201 // Intentional original-fatal identity injection at the native boundary.
        var fatal = new OutOfMemoryException("original release fatal");
#pragma warning restore CA2201
        operations.AfterObjectRelease = owner =>
        {
            if (owner == operations.Stream)
            {
                throw new AggregateException(new InvalidOperationException("release wrapper", fatal));
            }
        };
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));

        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(capture.Dispose));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(capture.Dispose));
        Assert.False(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.True(capture.IsDrained);
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.ReleasedQueues);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.All(operations.Completions.Values, completion => Assert.Equal(1, completion.ReleaseAttempts));
    }

    [Fact]
    public async Task ConfirmedCompletionReleasePreservesDiagnosisWithoutInventingUnknownOwnership()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        operations.StartCompletion!.FailureAfterRelease = new InvalidOperationException("contained release observer fault");

        InvalidOperationException disposal = Assert.Throws<InvalidOperationException>(capture.Dispose);
        Assert.Same(disposal, Assert.Throws<InvalidOperationException>(capture.Dispose));
        Assert.True(operations.StartCompletion.IsReleased);
        Assert.Same(operations.StartCompletion.FailureAfterRelease, operations.StartCompletion.FirstFailure);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.All(operations.Completions.Values, completion => Assert.Equal(1, completion.ReleaseAttempts));
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
        Assert.Equal(1, operations.ReleasedQueues);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => capture.StartAsync().AsTask());
    }

    [Fact]
    public void InvalidPixelGeometryQuarantinesUnconfirmedRetainedSourceWithoutReleasingBorrowedSource()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        source.State.Geometry = NativeRemoteWindowGeometry.Create(0, 0, 16_384, 16_384, 1);
        var releaseFailure = new InvalidOperationException("consumed retained source release");
        source.State.AfterRetainedRelease = () => throw releaseFailure;
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { }));
        Assert.NotSame(releaseFailure, failure);
        Assert.Equal("macOS window pixel bounds unavailable.", failure.Message);
        Assert.True(api.TryTakeFailedCapture(failure, out IMacOSRemoteWindowNativeCapture? failed));
        Assert.NotNull(failed);
        Assert.True(failed.IsDrained);
        InvalidOperationException disposal = Assert.Throws<InvalidOperationException>(failed.Dispose);
        Assert.Same(disposal, Assert.Throws<InvalidOperationException>(failed.Dispose));
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(1, source.State.RetainedReleaseAttempts);
        Assert.Equal(1, source.State.OwnerCount);
        Assert.True(source.IsCurrent());
        Assert.Empty(operations.ObjectReleaseAttempts);
        Assert.Equal(0, operations.ReleasedQueues);
        Assert.Equal(0, operations.PoolsPushed);
    }

    [Fact]
    public void EarlyConfigurationReleaseUncertaintyKeepsDurableOwnersBeyondFactorySlotReplacement()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var firstSource = new TestSource();
        using var secondSource = new TestSource();
        var firstFailure = new InvalidOperationException("first configuration failure");
        var secondFailure = new InvalidOperationException("second configuration failure");
        var releaseFailure = new InvalidOperationException("consumed unpublished output release");
        var firstOperations = new TestOperations { ConfigureFailure = firstFailure };
        var secondOperations = new TestOperations { ConfigureFailure = secondFailure };
        firstOperations.AfterObjectRelease = owner =>
        {
            if (owner == firstOperations.Output) { throw releaseFailure; }
        };
        secondOperations.AfterObjectRelease = owner =>
        {
            if (owner == secondOperations.Output) { throw releaseFailure; }
        };
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;

        Assert.Same(firstFailure, Assert.Throws<InvalidOperationException>(() =>
            api.CreateCaptureWithOperations(firstSource, firstOperations, sample => sample.Dispose(), () => { })));
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        WeakReference<IMacOSRemoteWindowNativeCapture> firstOwner = TakeWeakFailedCapture(api, firstFailure);
        Assert.Same(secondFailure, Assert.Throws<InvalidOperationException>(() =>
            api.CreateCaptureWithOperations(secondSource, secondOperations, sample => sample.Dispose(), () => { })));
        Assert.Equal(before + 2, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.False(api.TryTakeFailedCapture(firstFailure, out _));
        Assert.True(api.TryTakeFailedCapture(secondFailure, out IMacOSRemoteWindowNativeCapture? failed));
        Assert.NotNull(failed);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.True(firstOwner.TryGetTarget(out IMacOSRemoteWindowNativeCapture? firstCapture));
        Assert.NotNull(firstCapture);
        InvalidOperationException firstDisposal = Assert.Throws<InvalidOperationException>(firstCapture.Dispose);
        Assert.Same(firstDisposal, Assert.Throws<InvalidOperationException>(firstCapture.Dispose));
        Assert.True(failed.IsDrained);
        InvalidOperationException disposal = Assert.Throws<InvalidOperationException>(failed.Dispose);
        Assert.Same(disposal, Assert.Throws<InvalidOperationException>(failed.Dispose));
        Assert.Equal(before + 2, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        foreach (TestOperations operations in new[] { firstOperations, secondOperations })
        {
            Assert.Equal(new[] { operations.Output, operations.Configuration }, operations.ObjectReleaseAttempts);
            Assert.Equal(1, operations.ReleasedQueues);
            Assert.Equal(0, operations.StartInvocations);
            Assert.Equal(0, operations.StopInvocations);
            Assert.Equal(0, operations.RemoveCalls);
            Assert.Equal(operations.PoolsPushed, operations.PoolsPopped);
        }
        Assert.Equal(1, firstSource.State.RetainedReleaseAttempts);
        Assert.Equal(1, secondSource.State.RetainedReleaseAttempts);
        Assert.Equal(1, firstSource.State.OwnerCount);
        Assert.Equal(1, secondSource.State.OwnerCount);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("queue")]
    [InlineData("completion")]
    [InlineData("source")]
    public async Task ConsumedReleaseThenThrowsIsNotRetriedByRepeatedDispose(string resource)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
        var failure = new InvalidOperationException("injected after-effect release failure");
        if (resource == "object")
        {
            operations.AfterObjectRelease = owner =>
            {
                if (owner == operations.Stream) { throw failure; }
            };
        }
        else if (resource == "queue")
        {
            operations.AfterQueueRelease = () => throw failure;
        }
        else if (resource == "source")
        {
            source.State.AfterRetainedRelease = () => throw failure;
        }
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        if (resource == "completion")
        {
            operations.StartCompletion!.AfterRelease = () => throw failure;
        }

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(capture.Dispose);
        Assert.Same(first, Assert.Throws<InvalidOperationException>(capture.Dispose));
        Assert.Same(first, Assert.Throws<InvalidOperationException>(capture.Dispose));
        Assert.True(capture.IsDrained);
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, operations.ObjectReleaseAttempts.Count(owner => owner == operations.Stream));
        Assert.Equal(1, operations.ReleasedObjects.Count(owner => owner == operations.Stream));
        Assert.Equal(1, operations.ReleasedObjects.Count(owner => owner == operations.Output));
        Assert.Equal(1, operations.ReleasedObjects.Count(owner => owner == operations.Configuration));
        Assert.Equal(1, operations.ReleasedQueues);
        Assert.Equal(1, source.State.ReleaseCalls);
        Assert.Equal(1, source.State.OwnerCount);
        if (resource == "completion")
        {
            Assert.Equal(1, operations.StartCompletion!.ReleaseAttempts);
            Assert.Equal(1, operations.StartCompletion.ReleaseEffects);
            Assert.False(operations.StartCompletion.IsReleased);
        }
        if (resource == "source")
        {
            Assert.Equal(1, source.State.RetainedReleaseAttempts);
        }
        Assert.False(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal(1, operations.StartInvocations);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("source")]
    public async Task NativeReleaseDoesNotHoldCaptureStateGateAgainstIndependentSameOwnerObserver(string resource)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
        using var releaseEntered = new ManualResetEventSlim();
        using var allowRelease = new ManualResetEventSlim();
        using var observerEntered = new ManualResetEventSlim();
        using var observerReturned = new ManualResetEventSlim();
        Action holdRelease = () =>
        {
            releaseEntered.Set();
            allowRelease.Wait();
        };
        if (resource == "object")
        {
            operations.BeforeObjectRelease = owner =>
            {
                if (owner == operations.Stream) { holdRelease(); }
            };
        }
        else
        {
            source.State.BeforeRetainedRelease = holdRelease;
        }

        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        Exception? disposeFailure = null;
        Exception? observerFailure = null;
        var disposer = new Thread(() =>
        {
            try { capture.Dispose(); }
            catch (Exception failure) { disposeFailure = failure; }
        })
        { IsBackground = true };
        var observer = new Thread(() =>
        {
            observerEntered.Set();
            try { _ = capture.StopAndDrainAsync().AsTask(); }
            catch (Exception failure) { observerFailure = failure; }
            finally { observerReturned.Set(); }
        })
        { IsBackground = true };
        bool disposerStarted = false;
        bool observerStarted = false;
        try
        {
            disposer.Start();
            disposerStarted = true;
            Assert.True(releaseEntered.Wait(Timeout));
            observer.Start();
            observerStarted = true;
            Assert.True(observerEntered.Wait(Timeout));
            Assert.True(observerReturned.Wait(Timeout), "Capture observer did not return while native release remained held.");
            Assert.Null(observerFailure);
        }
        finally
        {
            allowRelease.Set();
            if (disposerStarted) { Assert.True(disposer.Join(Timeout)); }
            if (observerStarted) { Assert.True(observer.Join(Timeout)); }
        }

        Assert.Null(disposeFailure);
        Assert.Equal(1, source.State.OwnerCount);
    }

    [Fact]
    public async Task HealthyCaptureStartsDrainsAndReleasesItsIndependentOwners()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        int unavailable = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => unavailable++);
        try
        {
            Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
            Assert.Equal(2, source.State.OwnerCount);
            Assert.Equal(0, operations.StreamDelegate);
            Assert.Equal((160, 90), operations.ConfiguredDimensions);
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
            Assert.True(capture.IsDrained);
            Assert.Empty(operations.ReleasedObjects);
            Assert.Equal(0, operations.ReleasedQueues);
        }
        finally
        {
            await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            capture.Dispose();
        }

        capture.Dispose();
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, source.State.OwnerCount);
        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(1, source.State.ReleaseCalls);
        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(new[] { operations.Stream, operations.Output, operations.Configuration }, operations.ReleasedObjects);
        Assert.Equal(1, operations.ReleasedQueues);
        Assert.All(operations.Completions.Values, completion => Assert.True(completion.IsReleased));
        Assert.Equal(operations.PoolsPushed, operations.PoolsPopped);
        Assert.Equal(0, unavailable);
        AssertBefore(operations, "queue-barrier", "release-stream");
        AssertBefore(operations, "queue-barrier", "release-output");
        AssertBefore(operations, "queue-barrier", "release-queue");
    }

    [Fact]
    public void ConfigurationSetterFailureKeepsAllocatedOwnersVisibleForUnpublishedCleanup()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var failure = new InvalidOperationException("injected configuration setter fault");
        var operations = new TestOperations { ConfigureFailure = failure };
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { })));

        Assert.False(api.TryTakeFailedCapture(failure, out _));
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, source.State.OwnerCount);
        Assert.Equal(1, source.State.RetainCalls);
        Assert.Equal(1, source.State.ReleaseCalls);
        Assert.Equal(new[] { operations.Output, operations.Configuration }, operations.ReleasedObjects);
        Assert.Equal(1, operations.ReleasedQueues);
        Assert.Equal(0, operations.RemoveCalls);
        Assert.Equal(0, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        Assert.DoesNotContain("add-output", operations.Events);
        Assert.DoesNotContain("queue-barrier", operations.Events);
        Assert.Equal(operations.PoolsPushed, operations.PoolsPopped);
    }

    [Fact]
    public async Task StopErrorQuarantinesCompleteCaptureRatherThanReleasingCallbackOwners()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations { StopError = 7 };
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
        Assert.False(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));

        Assert.False(capture.IsDrained);
        Assert.Throws<InvalidOperationException>(capture.Dispose);
        Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(2, source.State.OwnerCount);
        Assert.Equal(0, source.State.ReleaseCalls);
        Assert.Empty(operations.ReleasedObjects);
        Assert.Equal(0, operations.ReleasedQueues);
        Assert.Equal(0, operations.RemoveCalls);
        Assert.DoesNotContain("queue-barrier", operations.Events);
        Assert.All(operations.Completions.Values, completion => Assert.False(completion.IsReleased));
        Assert.False(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal(1, operations.StopInvocations);
        // The process root intentionally remains charged. Do not reset global
        // state or fake a successful native stop to clean this uncertainty.
    }

    [Fact]
    public async Task CompleteSampleTransfersOneIndependentOwnerOnlyWhileStartedAndDeliveryOpen()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
        var received = new List<IMacOSRemoteWindowNativeSample>();
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample =>
            {
                received.Add(sample);
                sample.Dispose();
            }, () => { });
        try
        {
            operations.DeliverSample();
            Assert.Equal(0, operations.SampleStatusReads);
            Assert.Empty(received);

            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            operations.DeliverSample();
            TestSample sample = Assert.Single(operations.RetainedSamples);
            Assert.Same(sample, Assert.Single(received));
            Assert.Equal((160, 90), sample.Dimensions);
            Assert.Equal(1, sample.ReleaseCalls);
            Assert.Equal(1, operations.SampleStatusReads);
            Assert.Equal(1, operations.SampleReadyReads);

            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
            operations.DeliverSample();
            Assert.Single(received);
            Assert.Single(operations.RetainedSamples);
            Assert.Equal(1, operations.SampleStatusReads);
        }
        finally
        {
            await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            capture.Dispose();
        }

        operations.DeliverSample();
        Assert.Single(received);
        Assert.Equal(1, source.State.OwnerCount);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("idle", false)]
    [InlineData("not-ready", false)]
    [InlineData("terminal", true)]
    [InlineData("stale", true)]
    [InlineData("wrong-stream", true)]
    [InlineData("zero-sample", true)]
    [InlineData("non-screen", true)]
    public async Task SampleBoundaryDropsNonFramesAndClosesUnavailableDeliveryOnce(string condition, bool terminal)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations
        {
            SampleFrameStatus = condition switch { "missing" => null, "idle" => 1, "terminal" => 5, _ => 0 },
            SampleReady = condition != "not-ready",
        };
        int unavailable = 0;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => unavailable++);
        try
        {
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            source.State.Current = condition != "stale";
            nint stream = condition == "wrong-stream" ? operations.Stream + 1 : operations.Stream;
            nint sample = condition == "zero-sample" ? 0 : 7;
            nint kind = condition == "non-screen" ? 1 : 0;
            operations.DeliverSample(stream, sample, kind);
            operations.DeliverSample(stream, sample, kind);

            Assert.Empty(operations.RetainedSamples);
            Assert.Equal(terminal ? 1 : 0, unavailable);
            int expectedStatusReads = condition switch
            {
                "terminal" => 1,
                "missing" or "idle" or "not-ready" => 2,
                _ => 0,
            };
            Assert.Equal(expectedStatusReads, operations.SampleStatusReads);
            Assert.Equal(condition == "not-ready" ? 2 : 0, operations.SampleReadyReads);
            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
        }
        finally
        {
            await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            capture.Dispose();
        }

        Assert.Equal(1, source.State.OwnerCount);
    }

    [Fact]
    public async Task OwnerCleanupReleaseAlsoExitsHeldCompletionsPublishedAfterThatRelease()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations { HoldStartExit = true, HoldStopExit = true };
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        try
        {
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            Assert.Null(operations.StopCompletion);
            operations.ReleaseHeldCompletionExits();
            Assert.True(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
            Assert.NotNull(operations.StopCompletion);
            Assert.True(capture.IsDrained);
        }
        finally
        {
            operations.ReleaseHeldCompletionExits();
            await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            capture.Dispose();
        }

        Assert.Equal(1, source.State.OwnerCount);
    }

    [Fact]
    public async Task ThrowingSampleConsumerCannotCrossCallbackBoundaryAndKeepsPhysicalDrainIndependent()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations();
        int unavailable = 0;
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(source, operations,
            sample =>
            {
                try
                {
                    throw new InvalidOperationException("injected consumer fault");
                }
                finally
                {
                    sample.Dispose();
                }
            }, () => unavailable++);
        try
        {
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            operations.DeliverSample();
            operations.DeliverSample();
            Assert.Equal(1, unavailable);
            Assert.Equal(1, Assert.Single(operations.RetainedSamples).ReleaseCalls);
            Assert.False(await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout));
            Assert.True(capture.IsDrained);
        }
        finally
        {
            await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            capture.Dispose();
        }

        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, source.State.OwnerCount);
    }

    [Fact]
    public async Task DrainJoinsStartAndStopCallbackExitRatherThanOnlyTheirSettledResults()
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        var operations = new TestOperations { HoldStartExit = true, HoldStopExit = true };
        IMacOSRemoteWindowNativeCapture capture = api.CreateCaptureWithOperations(
            source, operations, sample => sample.Dispose(), () => { });
        Task<bool>? drain = null;
        try
        {
            Assert.True(await capture.StartAsync().AsTask().WaitAsync(Timeout));
            await operations.StartEntered.Task.WaitAsync(Timeout);
            drain = capture.StopAndDrainAsync().AsTask();
            Assert.False(capture.IsDrained);
            Assert.False(drain.IsCompleted);
            Assert.Equal(0, operations.StopInvocations);
            Assert.Throws<InvalidOperationException>(capture.Dispose);

            operations.StartCompletion!.Exit();
            await operations.StopEntered.Task.WaitAsync(Timeout);
            Assert.False(capture.IsDrained);
            Assert.False(drain.IsCompleted);
            Assert.Equal(0, operations.RemoveCalls);
            Assert.Empty(operations.ReleasedObjects);

            operations.StopCompletion!.Exit();
            Assert.True(await drain.WaitAsync(Timeout));
            Assert.True(capture.IsDrained);
        }
        finally
        {
            operations.ReleaseHeldCompletionExits();
            if (drain is not null)
            {
                await drain.WaitAsync(Timeout);
            }
            else
            {
                await capture.StopAndDrainAsync().AsTask().WaitAsync(Timeout);
            }

            capture.Dispose();
        }

        Assert.Equal(1, operations.StartInvocations);
        Assert.Equal(1, operations.StopInvocations);
        Assert.Equal(1, source.State.OwnerCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedOutputPublicationTransfersExactFactoryOwnerUntilQueueBarrierCompletes(bool throwing) =>
        Task.Factory.StartNew(() => VerifyFailedOutputPublication(throwing), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void VerifyFailedOutputPublication(bool throwing)
    {
        var api = new MacOSRemoteWindowScreenCaptureKitApi();
        var otherApi = new MacOSRemoteWindowScreenCaptureKitApi();
        using var source = new TestSource();
        using var releaseBarrier = new ManualResetEventSlim();
        var operations = new TestOperations
        {
            RejectOutput = !throwing,
            AddFailure = throwing ? new InvalidOperationException("injected AddOutput fault") : null,
            BarrierRelease = releaseBarrier,
        };
        int before = MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount;
        IMacOSRemoteWindowNativeCapture? failed = null;
        Task<bool>? rollback = null;
        try
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                api.CreateCaptureWithOperations(source, operations, sample => sample.Dispose(), () => { }));
            if (throwing)
            {
                Assert.Same(operations.AddFailure, failure);
            }

            Assert.False(otherApi.TryTakeFailedCapture(failure, out _));
            Assert.False(api.TryTakeFailedCapture(new InvalidOperationException(), out _));
            Assert.True(api.TryTakeFailedCapture(failure, out failed));
            Assert.NotNull(failed);
            Assert.False(api.TryTakeFailedCapture(failure, out _));
            operations.BarrierEntered.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            rollback = failed.StopAndDrainAsync().AsTask();
            Assert.False(rollback.IsCompleted);
            Assert.False(failed.IsDrained);
            Assert.Equal(before + 1, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
            Assert.Equal(2, source.State.OwnerCount);
            Assert.Empty(operations.ReleasedObjects);
            Assert.Equal(throwing ? 1 : 0, operations.RemoveCalls);
        }
        finally
        {
            releaseBarrier.Set();
            if (failed is not null)
            {
                rollback ??= failed.StopAndDrainAsync().AsTask();
                Assert.True(rollback.WaitAsync(Timeout).GetAwaiter().GetResult());
                failed.Dispose();
            }
        }

        Assert.NotNull(failed);
        Assert.True(failed.IsDrained);
        Assert.Equal(before, MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount);
        Assert.Equal(1, source.State.OwnerCount);
        Assert.Equal(0, operations.StartInvocations);
        Assert.Equal(0, operations.StopInvocations);
        AssertBefore(operations, "queue-barrier", "release-stream");
        AssertBefore(operations, "queue-barrier", "release-output");
        AssertBefore(operations, "queue-barrier", "release-queue");
    }

    private static void AssertBefore(TestOperations operations, string first, string second)
    {
        string[] events = operations.Events.ToArray();
        Assert.True(Array.IndexOf(events, first) >= 0);
        Assert.True(Array.IndexOf(events, second) > Array.IndexOf(events, first));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<IMacOSRemoteWindowNativeCapture> TakeWeakFailedCapture(
        MacOSRemoteWindowScreenCaptureKitApi api, Exception failure)
    {
        Assert.True(api.TryTakeFailedCapture(failure, out IMacOSRemoteWindowNativeCapture? capture));
        Assert.NotNull(capture);
        return new(capture);
    }

    private sealed class TestSource : IMacOSRemoteWindowCaptureSource
    {
        private int disposed;
        private readonly bool retained;
        internal SourceState State { get; }
        internal TestSource() : this(new SourceState()) { }
        private TestSource(SourceState state, bool retained = false)
        {
            State = state;
            this.retained = retained;
        }
        public NativeRemoteWindowGeometry Geometry => State.Geometry;
        public nint Filter => 42;
        public bool IsCurrent() => State.Current && Volatile.Read(ref disposed) == 0;
        public IMacOSRemoteWindowCaptureSource RetainOwner()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            Interlocked.Increment(ref State.RetainCalls);
            if (State.RetainFailure is { } failure) { throw failure; }
            Interlocked.Increment(ref State.OwnerCount);
            return new TestSource(State, retained: true);
        }

        public void Dispose()
        {
            if (retained) { Interlocked.Increment(ref State.RetainedReleaseAttempts); }
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                if (retained) { State.BeforeRetainedRelease?.Invoke(); }
                Interlocked.Decrement(ref State.OwnerCount);
                Interlocked.Increment(ref State.ReleaseCalls);
                if (retained) { State.AfterRetainedRelease?.Invoke(); }
            }
        }
    }

    private sealed class SourceState
    {
        internal NativeRemoteWindowGeometry Geometry = NativeRemoteWindowGeometry.Create(0, 0, 80, 45, 2);
        internal int OwnerCount = 1;
        internal int RetainCalls;
        internal int ReleaseCalls;
        internal volatile bool Current = true;
        internal Action? BeforeRetainedRelease;
        internal Action? AfterRetainedRelease;
        internal int RetainedReleaseAttempts;
        internal Exception? RetainFailure;
    }

    private sealed class TestOperations : IMacOSRemoteWindowCaptureOperations
    {
        private static long nextAddress = 100;
        private readonly nint addressBase = (nint)Interlocked.Add(ref nextAddress, 10);
        internal nint Configuration => addressBase;
        internal nint Output => OutputAddressOverride ?? addressBase + 1;
        internal nint? OutputAddressOverride { get; init; }
        internal nint Queue => addressBase + 2;
        internal nint Stream => addressBase + 3;
        internal nint StreamDelegate { get; private set; }
        internal (int Width, int Height) ConfiguredDimensions { get; private set; }
        internal ConcurrentQueue<string> Events { get; } = new();
        internal ConcurrentQueue<nint> ReleasedObjects { get; } = new();
        internal ConcurrentQueue<nint> ObjectReleaseAttempts { get; } = new();
        internal ConcurrentDictionary<nint, TestCompletion> Completions { get; } = new();
        internal int PoolsPushed;
        internal int PoolsPopped;
        internal int StartInvocations;
        internal int StopInvocations;
        internal int RemoveCalls;
        internal int ReleasedQueues;
        internal Action<nint>? BeforeObjectRelease { get; set; }
        internal Action<nint>? AfterObjectRelease { get; set; }
        internal Action? AfterQueueRelease { get; set; }
        private readonly object completionGate = new();
        private bool completionExitsReleased;
        private TestCompletion? startCompletion;
        private TestCompletion? stopCompletion;
        internal bool HoldStartExit { get; init; }
        internal bool HoldStopExit { get; init; }
        internal nint StopError { get; init; }
        internal TestCompletion? StartCompletion { get { lock (completionGate) { return startCompletion; } } }
        internal TestCompletion? StopCompletion { get { lock (completionGate) { return stopCompletion; } } }
        internal TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool RejectOutput { get; init; }
        internal Exception? AddFailure { get; init; }
        internal Exception? ConfigureFailure { get; init; }
        internal ManualResetEventSlim? BarrierRelease { get; init; }
        internal TaskCompletionSource BarrierEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SampleStatusReads;
        internal int SampleReadyReads;
        internal nint? SampleFrameStatus { get; init; } = 0;
        internal bool SampleReady { get; init; } = true;
        internal ConcurrentQueue<TestSample> RetainedSamples { get; } = new();

        internal void ReleaseHeldCompletionExits()
        {
            TestCompletion? start;
            TestCompletion? stop;
            lock (completionGate)
            {
                completionExitsReleased = true;
                start = startCompletion;
                stop = stopCompletion;
            }

            start?.Exit();
            stop?.Exit();
        }

        public nint PushAutoreleasePool()
        {
            Interlocked.Increment(ref PoolsPushed);
            return 1;
        }

        public void PopAutoreleasePool(nint pool)
        {
            Assert.Equal(1, pool);
            Interlocked.Increment(ref PoolsPopped);
        }

        public nint AllocateConfiguration() => Configuration;
        public void Configure(nint configuration, int width, int height)
        {
            Assert.Equal(Configuration, configuration);
            ConfiguredDimensions = (width, height);
            if (ConfigureFailure is not null)
            {
                throw ConfigureFailure;
            }
        }

        public nint AllocateOutput() => Output;
        public nint CreateSampleQueue() => Queue;
        public nint AllocateStream() => Stream;
        public nint InitializeStream(nint allocatedStream, nint filter, nint configuration, nint streamDelegate)
        {
            Assert.Equal(Stream, allocatedStream);
            Assert.Equal(42, filter);
            Assert.Equal(Configuration, configuration);
            StreamDelegate = streamDelegate;
            return Stream;
        }

        public byte AddOutput(nint stream, nint output, nint queue, out nint error)
        {
            Assert.Equal(Stream, stream);
            Assert.Equal(Output, output);
            Assert.Equal(Queue, queue);
            Events.Enqueue("add-output");
            if (AddFailure is not null)
            {
                throw AddFailure;
            }

            error = 0;
            return RejectOutput ? (byte)0 : (byte)1;
        }

        public byte RemoveOutput(nint stream, nint output, out nint error)
        {
            Assert.Equal(Stream, stream);
            Assert.Equal(Output, output);
            Interlocked.Increment(ref RemoveCalls);
            Events.Enqueue("remove-output");
            error = 0;
            return 1;
        }

        public IMacOSRemoteWindowCaptureCompletion CreateCompletion(
            Action<nint> action, Action<Exception> failure, Action completed)
        {
            nint pointer = (nint)Interlocked.Add(ref nextAddress, 10);
            var completion = new TestCompletion(pointer, action, failure, completed);
            Assert.True(Completions.TryAdd(pointer, completion));
            return completion;
        }

        public nint GetCompletionSelector(bool isStart) => isStart ? 1 : 2;
        public void InvokeCompletion(nint stream, nint selector, nint completion)
        {
            Assert.Equal(Stream, stream);
            TestCompletion owner = Completions[completion];
            bool exit;
            if (selector == 1)
            {
                Interlocked.Increment(ref StartInvocations);
                lock (completionGate)
                {
                    startCompletion = owner;
                    exit = !HoldStartExit || completionExitsReleased;
                }

                owner.Invoke(0, exit);
                StartEntered.TrySetResult();
            }
            else
            {
                Assert.Equal(2, selector);
                Interlocked.Increment(ref StopInvocations);
                lock (completionGate)
                {
                    stopCompletion = owner;
                    exit = !HoldStopExit || completionExitsReleased;
                }

                owner.Invoke(StopError, exit);
                StopEntered.TrySetResult();
            }
        }

        public void DrainSampleQueue(nint queue)
        {
            Assert.Equal(Queue, queue);
            BarrierEntered.TrySetResult();
            BarrierRelease?.Wait();
            Events.Enqueue("queue-barrier");
        }

        public void ReleaseObject(nint owner)
        {
            ObjectReleaseAttempts.Enqueue(owner);
            BeforeObjectRelease?.Invoke(owner);
            ReleasedObjects.Enqueue(owner);
            Events.Enqueue(owner == Stream ? "release-stream" : owner == Output ? "release-output" : "release-configuration");
            AfterObjectRelease?.Invoke(owner);
        }

        public void ReleaseQueue(nint queue)
        {
            Assert.Equal(Queue, queue);
            Interlocked.Increment(ref ReleasedQueues);
            Events.Enqueue("release-queue");
            AfterQueueRelease?.Invoke();
        }

        internal void DeliverSample(nint? stream = null, nint sample = 7, nint kind = 0) =>
            MacOSRemoteWindowScreenCaptureKitApi.DeliverCaptureSample(Output, stream ?? Stream, sample, kind);

        public nint? GetSampleFrameStatus(nint sample)
        {
            Assert.Equal(7, sample);
            Interlocked.Increment(ref SampleStatusReads);
            return SampleFrameStatus;
        }

        public bool IsSampleReady(nint sample)
        {
            Assert.Equal(7, sample);
            Interlocked.Increment(ref SampleReadyReads);
            return SampleReady;
        }

        public IMacOSRemoteWindowNativeSample RetainSample(nint sample, int width, int height)
        {
            Assert.Equal(7, sample);
            var owner = new TestSample(width, height);
            RetainedSamples.Enqueue(owner);
            return owner;
        }
    }

    private sealed class TestCompletion(
        nint pointer, Action<nint> action, Action<Exception> failure, Action completed) : IMacOSRemoteWindowCaptureCompletion
    {
        private int released;
        private readonly object exitGate = new();
        private bool actionFinished;
        private bool exitRequested;
        private bool exitNotified;
        private int consumed;
        internal int ReleaseAttempts;
        internal int ReleaseEffects;
        internal Action? AfterRelease { get; set; }
        internal Exception? FailureAfterRelease { get; set; }
        public nint Pointer => pointer;
        public bool IsReleased => Volatile.Read(ref released) != 0;
        public Exception? FirstFailure { get; private set; }
        internal void Invoke(nint error, bool exit = true)
        {
            try
            {
                action(error);
            }
            catch (Exception exception)
            {
                FirstFailure ??= exception;
                failure(exception);
            }
            finally
            {
                bool notify;
                lock (exitGate)
                {
                    actionFinished = true;
                    notify = (exit || exitRequested) && !exitNotified;
                    exitNotified |= notify;
                }

                if (notify)
                {
                    completed();
                }
            }
        }

        internal void Exit()
        {
            bool notify;
            lock (exitGate)
            {
                exitRequested = true;
                notify = actionFinished && !exitNotified;
                exitNotified |= notify;
            }

            if (notify)
            {
                completed();
            }
        }

        public void Dispose()
        {
            Interlocked.Increment(ref ReleaseAttempts);
            if (Interlocked.Exchange(ref consumed, 1) == 0)
            {
                Interlocked.Increment(ref ReleaseEffects);
            }

            AfterRelease?.Invoke();
            Volatile.Write(ref released, 1);
            FirstFailure ??= FailureAfterRelease;
        }
    }

    private sealed class TestSample(int width, int height) : IMacOSRemoteWindowNativeSample
    {
        private int released;
        internal int ReleaseCalls;
        internal (int Width, int Height) Dimensions => (width, height);
        public MacOSRemoteWindowSampleRetention Retention { get; } = new();
        public bool TryCopyPixels(out MacOSRemoteWindowPixelBuffer? pixels)
        {
            pixels = null;
            return false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                Interlocked.Increment(ref ReleaseCalls);
            }
        }
    }
}
