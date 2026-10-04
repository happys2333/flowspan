using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Sources;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowCaptureOwnershipTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StopCompletionJoinsPendingSampleReleaseAfterActiveDeliveryReturns()
    {
        var sinkEntered = NewSignal();
        var releaseSink = NewSignal();
        var pendingDisposeEntered = NewSignal();
        var releasePendingDispose = NewSignal();
        var activeSampleReleased = NewSignal();
        var controlledStop = new ControlledStop();
        var nativeCapture = new OwnershipTestCapture { StopController = controlledStop };
        var nativeSource = new MacOSRemoteWindowTestSource();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink
        {
            OnFrame = _ =>
            {
                sinkEntered.SetResult();
                AwaitSignal(releaseSink);
            },
        };
        var active = new OwnershipTestSample(1)
        {
            OnDispose = () => activeSampleReleased.SetResult(),
        };
        var pending = new OwnershipTestSample(2)
        {
            OnDispose = () =>
            {
                pendingDisposeEntered.SetResult();
                AwaitSignal(releasePendingDispose);
            },
        };
        var late = new OwnershipTestSample(3);
        bool nativeOwnerReleasedBeforePendingSample = false;
        nativeCapture.OnDispose = () =>
            nativeOwnerReleasedBeforePendingSample = !pending.DisposeReturned;
        Task<LocalBoundaryResult>? stopCall = null;
        Task? disposal = null;
        try
        {
            Assert.True((await boundary.StartAsync(
                NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                sink, CancellationToken.None)).Succeeded);
            api.SampleCallback!(active);
            await sinkEntered.Task.WaitAsync(TestTimeout);
            api.SampleCallback!(pending);

            stopCall = Task.Run(() => boundary.StopNow());
            await pendingDisposeEntered.Task.WaitAsync(TestTimeout);
            LocalBoundaryResult stopResult = await stopCall.WaitAsync(TestTimeout);
            Assert.True(stopResult.Succeeded);
            Assert.Equal("macos_capture_delivery_closed", stopResult.ReasonCode);
            disposal = boundary.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            api.SampleCallback!(late);
            Assert.Equal(1, late.DisposeCalls);
            Assert.Equal(0, late.CopyCalls);

            await controlledStop.AwaitRegistered.Task.WaitAsync(TestTimeout);
            releaseSink.SetResult();
            await activeSampleReleased.Task.WaitAsync(TestTimeout);
            await boundary.DeliveryCompletion.WaitAsync(TestTimeout);
            // Native Stop is resumed inline only after the real worker drained.
            // Its cleanup continuation must return at the pending release await;
            // omitting that await releases native owners before this call returns.
            Assert.True(controlledStop.TryComplete(true));
            Assert.Equal(1, controlledStop.ContinuationReturns);
            Assert.Equal(Environment.CurrentManagedThreadId,
                controlledStop.ContinuationThreadId);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, nativeCapture.DisposeCalls);
        }
        finally
        {
            releaseSink.TrySetResult();
            releasePendingDispose.TrySetResult();
            controlledStop.TryComplete(true);
            if (stopCall is not null)
            {
                await stopCall.WaitAsync(TestTimeout);
            }

            await (disposal ?? boundary.DisposeAsync().AsTask()).WaitAsync(TestTimeout);
        }

        Assert.False(nativeOwnerReleasedBeforePendingSample);
        Assert.Equal(new long[] { 1 }, sink.Sequences);
        Assert.Equal(1, active.DisposeCalls);
        Assert.Equal(1, active.Pixels.DisposeCalls);
        Assert.Equal(1, pending.DisposeCalls);
        Assert.True(pending.DisposeReturned);
        Assert.Equal(0, pending.CopyCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatestSlotReleasesReplacedSampleAndDeliversActiveThenNewest(
        bool blockPixelCopy)
    {
        var activeEntered = NewSignal();
        var releaseActive = NewSignal();
        var newestDelivered = NewSignal();
        var receivedMarkers = new ConcurrentQueue<byte>();
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink
        {
            OnFrame = frame =>
            {
                byte marker = frame.Pixels.Span[0];
                receivedMarkers.Enqueue(marker);
                if (marker == 1 && !blockPixelCopy)
                {
                    activeEntered.SetResult();
                    AwaitSignal(releaseActive);
                }
                else if (marker == 3)
                {
                    newestDelivered.SetResult();
                }
            },
        };
        var active = new OwnershipTestSample(1)
        {
            OnCopy = () =>
            {
                if (blockPixelCopy)
                {
                    activeEntered.SetResult();
                    AwaitSignal(releaseActive);
                }
            },
        };
        var replaced = new OwnershipTestSample(2);
        var newest = new OwnershipTestSample(3);
        try
        {
            Assert.True((await boundary.StartAsync(
                NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                sink, CancellationToken.None)).Succeeded);
            api.SampleCallback!(active);
            await activeEntered.Task.WaitAsync(TestTimeout);
            api.SampleCallback!(replaced);
            api.SampleCallback!(newest);

            Assert.Equal(1, replaced.DisposeCalls);
            Assert.Equal(0, replaced.CopyCalls);
            Assert.Equal(0, replaced.Pixels.DisposeCalls);
            releaseActive.SetResult();
            await newestDelivered.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            releaseActive.TrySetResult();
            await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }

        Assert.Equal(new byte[] { 1, 3 }, receivedMarkers.ToArray());
        Assert.Equal(new long[] { 1, 2 }, sink.Sequences);
        Assert.Equal(1, active.CopyCalls);
        Assert.Equal(1, active.DisposeCalls);
        Assert.Equal(1, active.Pixels.DisposeCalls);
        Assert.Equal(1, newest.CopyCalls);
        Assert.Equal(1, newest.DisposeCalls);
        Assert.Equal(1, newest.Pixels.DisposeCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Fact]
    public async Task SinkCallbackDisposalDefersSelfJoinAndExternalDisposerWaitsForReturn()
    {
        var callbackDisposeReturned = NewSignal();
        var releaseCallback = NewSignal();
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink
        {
            OnFrame = _ =>
            {
                boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout)
                    .GetAwaiter().GetResult();
                callbackDisposeReturned.SetResult();
                AwaitSignal(releaseCallback);
            },
        };
        var sample = new OwnershipTestSample(1);
        Task? externalDisposal = null;
        try
        {
            Assert.True((await boundary.StartAsync(
                NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                sink, CancellationToken.None)).Succeeded);
            api.SampleCallback!(sample);
            await callbackDisposeReturned.Task.WaitAsync(TestTimeout);
            await nativeCapture.StopEntered.Task.WaitAsync(TestTimeout);

            externalDisposal = boundary.DisposeAsync().AsTask();
            Assert.False(externalDisposal.IsCompleted);
            Assert.Equal(0, sample.DisposeCalls);
            Assert.Equal(0, sample.Pixels.DisposeCalls);
            Assert.Equal(0, nativeCapture.DisposeCalls);
        }
        finally
        {
            releaseCallback.TrySetResult();
            await (externalDisposal ?? boundary.DisposeAsync().AsTask())
                .WaitAsync(TestTimeout);
        }

        Assert.Equal(1, sample.DisposeCalls);
        Assert.Equal(1, sample.Pixels.DisposeCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Fact]
    public async Task PendingSampleDisposerCanReenterBoundaryWithoutJoiningItsOwnRelease()
    {
        var sinkEntered = NewSignal();
        var releaseSink = NewSignal();
        var callbackDisposeReturned = NewSignal();
        var releasePendingCallback = NewSignal();
        var activeSampleReleased = NewSignal();
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink
        {
            OnFrame = _ =>
            {
                sinkEntered.SetResult();
                AwaitSignal(releaseSink);
            },
        };
        var active = new OwnershipTestSample(1)
        {
            OnDispose = () => activeSampleReleased.SetResult(),
        };
        var pending = new OwnershipTestSample(2)
        {
            OnDispose = () =>
            {
                boundary.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter().GetResult();
                callbackDisposeReturned.SetResult();
                AwaitSignal(releasePendingCallback);
            },
        };
        Task? externalDisposal = null;
        try
        {
            Assert.True((await boundary.StartAsync(
                NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                sink, CancellationToken.None)).Succeeded);
            api.SampleCallback!(active);
            await sinkEntered.Task.WaitAsync(TestTimeout);
            api.SampleCallback!(pending);
            Assert.True(boundary.StopNow().Succeeded);
            await callbackDisposeReturned.Task.WaitAsync(TestTimeout);

            externalDisposal = boundary.DisposeAsync().AsTask();
            Assert.False(externalDisposal.IsCompleted);
            Assert.Equal(0, nativeCapture.DisposeCalls);
            releaseSink.SetResult();
            await activeSampleReleased.Task.WaitAsync(TestTimeout);
            Assert.False(externalDisposal.IsCompleted);
            Assert.False(pending.DisposeReturned);
        }
        finally
        {
            releaseSink.TrySetResult();
            releasePendingCallback.TrySetResult();
            await (externalDisposal ?? boundary.DisposeAsync().AsTask())
                .WaitAsync(TestTimeout);
        }

        Assert.Equal(1, active.DisposeCalls);
        Assert.Equal(1, active.Pixels.DisposeCalls);
        Assert.Equal(1, pending.DisposeCalls);
        Assert.True(pending.DisposeReturned);
        Assert.Equal(0, pending.CopyCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Fact]
    public async Task SourceInvalidationClosesDeliveryAndRetainsNativeBorrowUntilSinkSettles()
    {
        var sinkEntered = NewSignal();
        var releaseSink = NewSignal();
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink
        {
            OnFrame = _ =>
            {
                sinkEntered.SetResult();
                AwaitSignal(releaseSink);
            },
        };
        var active = new OwnershipTestSample(1);
        var late = new OwnershipTestSample(2);
        try
        {
            Assert.True((await boundary.StartAsync(
                NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                sink, CancellationToken.None)).Succeeded);
            api.SampleCallback!(active);
            await sinkEntered.Task.WaitAsync(TestTimeout);
            api.Current = false;
            api.SourceUnavailableCallback!();
            await nativeCapture.StopEntered.Task.WaitAsync(TestTimeout);

            Assert.False(boundary.StopCompletion.IsCompleted);
            Assert.False(nativeSource.Disposed);
            Assert.Equal(0, nativeCapture.DisposeCalls);
            api.SampleCallback!(late);
            Assert.Equal(1, late.DisposeCalls);
            Assert.Equal(0, late.CopyCalls);
        }
        finally
        {
            releaseSink.TrySetResult();
            await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }

        Assert.Empty(catalog.GetSnapshot());
        Assert.Equal(new long[] { 1 }, sink.Sequences);
        Assert.Equal(1, active.DisposeCalls);
        Assert.Equal(1, active.Pixels.DisposeCalls);
        Assert.Equal(1, nativeSource.DisposeCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("activity")]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("geometry")]
    [InlineData("foreign_token")]
    [InlineData("native_identity_not_current")]
    public async Task WrongOwnerOrExactBindingCannotCreateNativeCapture(string mismatch)
    {
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        var otherApi = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
        };
        await using var otherCatalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), otherApi);
        await otherCatalog.RefreshAsync();
        NativeRemoteWindowSourceSnapshot other = Assert.Single(otherCatalog.GetSnapshot());
        var sourceUse = new NativeRemoteWindowSourceUse(
            mismatch == "foreign_token" ? other.Token : snapshot.Token,
            mismatch == "activity" ? ActivityId.From(Guid.NewGuid()) : snapshot.Source.ActivityId,
            mismatch == "host" ? DeviceId.From(Guid.NewGuid()) : snapshot.Source.HostDeviceId,
            mismatch == "owner" ? 2 : 1,
            1,
            mismatch == "source" ? snapshot.Source.SourceGeneration + 1 : snapshot.Source.SourceGeneration,
            mismatch == "geometry" ? snapshot.GeometryRevision + 1 : snapshot.GeometryRevision);
        api.Current = mismatch != "native_identity_not_current";
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);

        LocalBoundaryResult result = await boundary.StartAsync(
            sourceUse, new MacOSRemoteWindowTestSink(), CancellationToken.None);
        await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.False(result.Succeeded);
        Assert.Equal(0, api.CaptureCalls);
        Assert.Equal(0, nativeCapture.StartCalls);
        Assert.Equal(0, nativeCapture.StopCalls);
        Assert.Equal(0, nativeCapture.DisposeCalls);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies the original native allocation exception survives cleanup.")]
    public async Task NestedStartOutOfMemoryKeepsOriginalFatalAndDrainsCreatedCapture()
    {
        var fatal = new OutOfMemoryException("original private start fatal");
        var nativeCapture = new OwnershipTestCapture
        {
            StartFailure = WrapFatal(fatal),
        };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);

        OutOfMemoryException startFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.StartAsync(
                NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                new MacOSRemoteWindowTestSink(), CancellationToken.None));
        OutOfMemoryException disposalFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));

        Assert.Same(fatal, startFailure);
        Assert.Same(fatal, disposalFailure);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies the original native allocation exception survives cleanup.")]
    public async Task NestedStopOutOfMemoryKeepsOriginalFatalAndRetainsUnconfirmedNativeBorrow()
    {
        var fatal = new OutOfMemoryException("original private stop fatal");
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new OwnershipTestCapture
        {
            StopFailure = WrapFatal(fatal),
        };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        Assert.True((await boundary.StartAsync(
            NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
            new MacOSRemoteWindowTestSink(), CancellationToken.None)).Succeeded);

        OutOfMemoryException disposalFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        OutOfMemoryException repeatedFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        await catalog.DisposeAsync();

        Assert.Same(fatal, disposalFailure);
        Assert.Same(fatal, repeatedFailure);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(0, nativeCapture.DisposeCalls);
        Assert.False(nativeSource.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies the first original fatal survives independently confirmed native drain.")]
    public async Task DrainedStopFaultKeepsFirstOriginalFatalAndReleasesNativeOwners(
        bool earlierStartFatal)
    {
        var startFatal = new OutOfMemoryException("original private earlier start fatal");
        var stopFatal = new OutOfMemoryException("original private drained stop fatal");
        OutOfMemoryException expectedFatal = earlierStartFatal ? startFatal : stopFatal;
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new OwnershipTestCapture
        {
            StartFailure = earlierStartFatal ? WrapFatal(startFatal) : null,
            StopFailure = WrapFatal(stopFatal),
            DrainConfirmed = true,
        };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        NativeRemoteWindowSourceUse sourceUse = NativeRemoteWindowSourceUse.Create(
            Assert.Single(catalog.GetSnapshot()), 1, 1);
        if (earlierStartFatal)
        {
            OutOfMemoryException startFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await boundary.StartAsync(sourceUse,
                    new MacOSRemoteWindowTestSink(), CancellationToken.None));
            Assert.Same(startFatal, startFailure);
        }
        else
        {
            Assert.True((await boundary.StartAsync(sourceUse,
                new MacOSRemoteWindowTestSink(), CancellationToken.None)).Succeeded);
        }

        OutOfMemoryException disposalFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        OutOfMemoryException repeatedFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        await catalog.DisposeAsync();

        Assert.Same(expectedFatal, disposalFailure);
        Assert.Same(expectedFatal, repeatedFailure);
        Assert.True(nativeCapture.IsDrained);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
        Assert.Equal(1, nativeSource.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies failed sample release preserves original fatal diagnostics and native owners.")]
    public async Task SampleDisposalFailureRetainsNativeBorrowAndStableFailure(bool fatalFailure)
    {
        var fatal = new OutOfMemoryException("original private sample dispose fatal");
        Exception injected = fatalFailure
            ? WrapFatal(fatal)
            : new InvalidOperationException("private sample owner release details");
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new OwnershipTestCapture { DrainConfirmed = true };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink();
        var sample = new OwnershipTestSample(1) { DisposeFailure = injected };
        Assert.True((await boundary.StartAsync(
            NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
            sink, CancellationToken.None)).Succeeded);

        api.SampleCallback!(sample);
        await nativeCapture.StopEntered.Task.WaitAsync(TestTimeout);
        Exception disposalFailure = await Assert.ThrowsAnyAsync<Exception>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        Exception repeatedFailure = await Assert.ThrowsAnyAsync<Exception>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));
        await catalog.DisposeAsync();

        if (fatalFailure)
        {
            Assert.Same(fatal, disposalFailure);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(disposalFailure);
            Assert.Equal("macos_capture_sample_cleanup_unconfirmed", disposalFailure.Message);
            Assert.DoesNotContain("private sample owner", disposalFailure.Message,
                StringComparison.Ordinal);
        }

        Assert.Same(disposalFailure, repeatedFailure);
        Assert.Equal(new long[] { 1 }, sink.Sequences);
        Assert.Equal(1, sample.CopyCalls);
        Assert.Equal(1, sample.DisposeCalls);
        Assert.False(sample.DisposeReturned);
        Assert.Equal(1, sample.Pixels.DisposeCalls);
        Assert.True(nativeCapture.IsDrained);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(0, nativeCapture.DisposeCalls);
        Assert.Equal(0, nativeSource.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies fatal construction failures retain and drain their returned native owner.")]
    public async Task FailedCaptureConstructionRetainsOwnerUntilRealRollbackDrainAndKeepsFatal(
        bool nestedFatal)
    {
        var fatal = new OutOfMemoryException("original private construction fatal");
        Exception injected = nestedFatal ? WrapFatal(fatal) : fatal;
        var stopResult = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new OwnershipTestCapture
        {
            StopResult = stopResult.Task,
            DrainConfirmed = true,
        };
        var innerApi = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        var api = new FailedConstructionTestApi(innerApi, injected, nativeCapture);
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        Task? disposal = null;
        try
        {
            OutOfMemoryException startFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await boundary.StartAsync(
                    NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
                    new MacOSRemoteWindowTestSink(), CancellationToken.None)
                    .AsTask().WaitAsync(TestTimeout));
            Assert.Same(fatal, startFailure);
            await nativeCapture.StopEntered.Task.WaitAsync(TestTimeout);

            disposal = boundary.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.False(nativeCapture.IsDrained);
            Assert.Equal(0, nativeCapture.DisposeCalls);
            await catalog.DisposeAsync();
            Assert.False(nativeSource.Disposed);
        }
        finally
        {
            stopResult.TrySetResult(true);
            OutOfMemoryException disposalFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await (disposal ?? boundary.DisposeAsync().AsTask())
                    .WaitAsync(TestTimeout));
            Assert.Same(fatal, disposalFailure);
            await catalog.DisposeAsync();
        }

        Assert.Equal(1, api.FailedCaptureOwnerClaims);
        Assert.Equal(1, innerApi.CaptureCalls);
        Assert.Equal(0, nativeCapture.StartCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.True(nativeCapture.IsDrained);
        Assert.Equal(1, nativeCapture.DisposeCalls);
        Assert.Equal(1, nativeSource.DisposeCalls);
    }

    [Fact]
    public async Task NativeCaptureDisposerCanReenterBoundaryWithoutJoiningOwnCleanup()
    {
        var callbackDisposeReturned = NewSignal();
        var releaseCallback = NewSignal();
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        nativeCapture.OnDispose = () =>
        {
            boundary.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))
                .GetAwaiter().GetResult();
            callbackDisposeReturned.SetResult();
            AwaitSignal(releaseCallback);
        };
        Assert.True((await boundary.StartAsync(
            NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
            new MacOSRemoteWindowTestSink(), CancellationToken.None)).Succeeded);
        Task disposal = boundary.DisposeAsync().AsTask();
        try
        {
            await callbackDisposeReturned.Task.WaitAsync(TestTimeout);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(1, nativeCapture.DisposeCalls);
            await catalog.DisposeAsync();
            Assert.False(nativeSource.Disposed);
        }
        finally
        {
            releaseCallback.TrySetResult();
            await disposal.WaitAsync(TestTimeout);
            await catalog.DisposeAsync();
        }

        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
        Assert.Equal(1, nativeSource.DisposeCalls);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies the original native allocation exception survives cleanup.")]
    public async Task NestedPixelCopyOutOfMemoryKeepsOriginalFatalAndReleasesNativeSample()
    {
        var fatal = new OutOfMemoryException("original private pixel fatal");
        var nativeCapture = new OwnershipTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink();
        var sample = new OwnershipTestSample(1) { CopyFailure = WrapFatal(fatal) };
        Assert.True((await boundary.StartAsync(
            NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
            sink, CancellationToken.None)).Succeeded);

        api.SampleCallback!(sample);
        await nativeCapture.StopEntered.Task.WaitAsync(TestTimeout);
        OutOfMemoryException disposalFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync().AsTask().WaitAsync(TestTimeout));

        Assert.Same(fatal, disposalFailure);
        Assert.Empty(sink.Sequences);
        Assert.Equal(1, sample.CopyCalls);
        Assert.Equal(1, sample.DisposeCalls);
        Assert.Equal(0, sample.Pixels.DisposeCalls);
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    private static AggregateException WrapFatal(OutOfMemoryException fatal) =>
        new("private native aggregate",
            new InvalidOperationException("private native inner", fatal));

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AwaitSignal(TaskCompletionSource signal) =>
        signal.Task.WaitAsync(TestTimeout).GetAwaiter().GetResult();

    private sealed class ControlledStop : IValueTaskSource<bool>
    {
        private ManualResetValueTaskSourceCore<bool> completion = new()
        {
            RunContinuationsAsynchronously = false,
        };
        private int completed;
        private int continuationReturns;
        private int continuationThreadId;

        public TaskCompletionSource AwaitRegistered { get; } = NewSignal();

        public ValueTask<bool> Completion => new(this, completion.Version);

        public bool IsCompleted => Volatile.Read(ref completed) != 0;

        public int ContinuationReturns => Volatile.Read(ref continuationReturns);

        public int ContinuationThreadId => Volatile.Read(ref continuationThreadId);

        public bool TryComplete(bool result)
        {
            if (Interlocked.CompareExchange(ref completed, 1, 0) != 0)
            {
                return false;
            }

            completion.SetResult(result);
            return true;
        }

        public bool GetResult(short token) => completion.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => completion.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            completion.OnCompleted(_ =>
            {
                Volatile.Write(ref continuationThreadId, Environment.CurrentManagedThreadId);
                continuation(state);
                Interlocked.Increment(ref continuationReturns);
            }, null, token, flags);
            AwaitRegistered.TrySetResult();
        }
    }

    private sealed class FailedConstructionTestApi(
        MacOSRemoteWindowTestApi inner,
        Exception constructionFailure,
        IMacOSRemoteWindowNativeCapture failedCapture) : IMacOSRemoteWindowNativeApi
    {
        private int failedCaptureOwnerClaims;

        public bool IsSupported => inner.IsSupported;

        public int FailedCaptureOwnerClaims => Volatile.Read(ref failedCaptureOwnerClaims);

        public bool PreflightCaptureAccess() => inner.PreflightCaptureAccess();

        public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync() =>
            inner.EnumerateAsync();

        public bool IsCurrent(IMacOSRemoteWindowNativeSource source) => inner.IsCurrent(source);

        public IMacOSRemoteWindowNativeCapture CreateCapture(
            IMacOSRemoteWindowNativeSource source,
            Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
            Action sourceUnavailable)
        {
            inner.CreateCapture(source, takeSampleOwnership, sourceUnavailable);
            throw constructionFailure;
        }

        public bool TryTakeFailedCapture(
            Exception failure,
            out IMacOSRemoteWindowNativeCapture? capture)
        {
            if (ReferenceEquals(constructionFailure, failure)
                && Interlocked.CompareExchange(ref failedCaptureOwnerClaims, 1, 0) == 0)
            {
                capture = failedCapture;
                return true;
            }

            capture = null;
            return false;
        }
    }

    private sealed class OwnershipTestCapture : IMacOSRemoteWindowNativeCapture
    {
        private int startCalls;
        private int stopCalls;
        private int disposeCalls;

        public Exception? StartFailure { get; init; }

        public Exception? StopFailure { get; init; }

        public Task<bool>? StopResult { get; init; }

        public ControlledStop? StopController { get; init; }

        public bool DrainConfirmed { get; init; }

        public bool IsDrained => DrainConfirmed && StopCalls > 0
            && (StopResult is null || StopResult.IsCompleted)
            && (StopController is null || StopController.IsCompleted);

        public Action? OnDispose { get; set; }

        public TaskCompletionSource StopEntered { get; } = NewSignal();

        public int StartCalls => Volatile.Read(ref startCalls);

        public int StopCalls => Volatile.Read(ref stopCalls);

        public int DisposeCalls => Volatile.Read(ref disposeCalls);

        public ValueTask<bool> StartAsync()
        {
            Interlocked.Increment(ref startCalls);
            return StartFailure is null
                ? ValueTask.FromResult(true)
                : ValueTask.FromException<bool>(StartFailure);
        }

        public ValueTask<bool> StopAndDrainAsync()
        {
            Interlocked.Increment(ref stopCalls);
            StopEntered.TrySetResult();
            if (StopController is not null)
            {
                return StopController.Completion;
            }

            if (StopResult is not null)
            {
                return new ValueTask<bool>(StopResult);
            }

            return StopFailure is null
                ? ValueTask.FromResult(true)
                : ValueTask.FromException<bool>(StopFailure);
        }

        public void Dispose()
        {
            Interlocked.Increment(ref disposeCalls);
            OnDispose?.Invoke();
        }
    }

    private sealed class OwnershipTestSample(byte marker) : IMacOSRemoteWindowNativeSample
    {
        private int copyCalls;
        private int disposeCalls;
        private int disposeReturned;

        public MacOSRemoteWindowSampleRetention Retention { get; } = new();

        public Action? OnCopy { get; init; }

        public Action? OnDispose { get; init; }

        public Exception? CopyFailure { get; init; }

        public Exception? DisposeFailure { get; init; }

        public OwnershipTestPixelOwner Pixels { get; } = new(marker);

        public int CopyCalls => Volatile.Read(ref copyCalls);

        public int DisposeCalls => Volatile.Read(ref disposeCalls);

        public bool DisposeReturned => Volatile.Read(ref disposeReturned) != 0;

        public bool TryCopyPixels(out MacOSRemoteWindowPixelBuffer? pixels)
        {
            Interlocked.Increment(ref copyCalls);
            OnCopy?.Invoke();
            if (CopyFailure is not null)
            {
                throw CopyFailure;
            }

            pixels = new MacOSRemoteWindowPixelBuffer(Pixels, 4, 1, 1, 4);
            return true;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref disposeCalls);
            OnDispose?.Invoke();
            if (DisposeFailure is not null)
            {
                throw DisposeFailure;
            }

            Volatile.Write(ref disposeReturned, 1);
        }
    }

    private sealed class OwnershipTestPixelOwner(byte marker) : IMemoryOwner<byte>
    {
        private readonly byte[] bytes = [marker, 0, 0, 0];
        private int disposeCalls;

        public Memory<byte> Memory => bytes;

        public int DisposeCalls => Volatile.Read(ref disposeCalls);

        public void Dispose()
        {
            Interlocked.Increment(ref disposeCalls);
            bytes.AsSpan().Clear();
        }
    }
}
