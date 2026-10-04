using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowSourceCatalogTests
{
    [Fact]
    public async Task InvalidationCallbackCanDisposeCatalogWithoutSelfWaiting()
    {
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        await catalog.RefreshAsync();
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquire(snapshot.Token,
            snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
        bool callbackReturned = false;
        using (lease)
        {
            Assert.True(lease!.TryRegisterInvalidationCallback(() =>
            {
                catalog.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter().GetResult();
                callbackReturned = true;
            }, out var callback));
            using (callback)
            {
                api.Sources = [];
                LocalBoundaryResult refresh = await catalog.RefreshAsync();
                Assert.True(refresh.Succeeded);
                Assert.True(callbackReturned);
            }
        }

        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task NativeSourceReleaseCallbackCanDisposeCatalogWithoutSelfWaiting()
    {
        MacOSRemoteWindowSourceCatalog? catalog = null;
        bool callbackReturned = false;
        var nativeSource = new MacOSRemoteWindowTestSource
        {
            DisposeCallback = () =>
            {
                catalog!.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter().GetResult();
                callbackReturned = true;
            },
        };
        var api = new MacOSRemoteWindowTestApi { Sources = [nativeSource] };
        catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        try
        {
            Assert.True((await catalog.RefreshAsync()).Succeeded);
            api.Sources = [];

            LocalBoundaryResult refresh = await catalog.RefreshAsync();

            Assert.True(refresh.Succeeded);
            Assert.True(callbackReturned);
            Assert.Equal(1, nativeSource.DisposeCalls);
            Assert.True(nativeSource.Disposed);
        }
        finally
        {
            await catalog.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, nativeSource.DisposeCalls);
    }

    [Fact]
    public async Task DisposalFailureStillReleasesEverySourceAndReportsBoundedFailure()
    {
        var failure = new InvalidOperationException("private native source details");
        var first = new MacOSRemoteWindowTestSource
        {
            DisposeFailure = failure,
        };
        var second = new MacOSRemoteWindowTestSource
        {
            Identity = new(24, 43, 1_700_000_001, 124),
        };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [first, second],
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);

        InvalidOperationException reported = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.DisposeAsync());

        Assert.Equal("macos_source_cleanup_failed", reported.Message);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        InvalidOperationException repeated = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.DisposeAsync());
        Assert.Same(reported, repeated);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Injected fatal verifies original allocation failure identity and safe independent cleanup, not actual runtime exhaustion.")]
    public async Task FatalDisposalFailurePreservesOriginalAndAttemptsEveryOwner(
        bool nestedFailure)
    {
        var fatal = new OutOfMemoryException("native source release fatal");
        var first = new MacOSRemoteWindowTestSource
        {
            DisposeFailure = nestedFailure
                ? new AggregateException(new InvalidOperationException("ordinary"),
                    new AggregateException(fatal))
                : fatal,
        };
        var second = new MacOSRemoteWindowTestSource
        {
            Identity = new(24, 43, 1_700_000_001, 124),
        };
        var api = new MacOSRemoteWindowTestApi { Sources = [first, second] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);

        Task disposal = catalog.DisposeAsync().AsTask();
        OutOfMemoryException reported = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await disposal);

        Assert.Same(fatal, reported);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
        Assert.Same(disposal, catalog.DisposeAsync().AsTask());
        OutOfMemoryException repeated = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await catalog.DisposeAsync());
        Assert.Same(fatal, repeated);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshRetirementFailureStillReleasesEveryOwnerAndClosesAdmission(
        bool hasReplacementCandidates)
    {
        var first = new MacOSRemoteWindowTestSource
        {
            DisposeFailure = new InvalidOperationException("private native source details"),
        };
        var second = new MacOSRemoteWindowTestSource
        {
            Identity = new(24, 43, 1_700_000_001, 124),
        };
        var firstCandidate = new MacOSRemoteWindowTestSource
        {
            Identity = first.Identity,
            Geometry = NativeRemoteWindowGeometry.Create(1, 0, 64, 64, 1),
        };
        var secondCandidate = new MacOSRemoteWindowTestSource
        {
            Identity = second.Identity,
            Geometry = NativeRemoteWindowGeometry.Create(1, 0, 64, 64, 1),
        };
        var api = new MacOSRemoteWindowTestApi { Sources = [first, second] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        IReadOnlyList<NativeRemoteWindowSourceSnapshot> before = catalog.GetSnapshot();
        api.Sources = hasReplacementCandidates
            ? [firstCandidate, secondCandidate] : [];

        LocalBoundaryResult refresh = await catalog.RefreshAsync();

        Assert.False(refresh.Succeeded);
        Assert.Equal("macos_source_catalog_unavailable", refresh.ReasonCode);
        Assert.Empty(catalog.GetSnapshot());
        foreach (NativeRemoteWindowSourceSnapshot snapshot in before)
        {
            Assert.False(catalog.TryAcquire(snapshot.Token,
                snapshot.Source.SourceGeneration, out _));
        }

        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
        Assert.Equal(hasReplacementCandidates ? 1 : 0, firstCandidate.DisposeCalls);
        Assert.Equal(hasReplacementCandidates ? 1 : 0, secondCandidate.DisposeCalls);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.DisposeAsync());
        Assert.Equal("macos_source_cleanup_failed", failure.Message);
        InvalidOperationException repeated = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.DisposeAsync());
        Assert.Same(failure, repeated);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
        Assert.Equal(hasReplacementCandidates ? 1 : 0, firstCandidate.DisposeCalls);
        Assert.Equal(hasReplacementCandidates ? 1 : 0, secondCandidate.DisposeCalls);
    }

    [Fact]
    public async Task CancelledLateEnumerationReleasesOwnersWithoutPublishing()
    {
        var completion = new TaskCompletionSource<
            IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new MacOSRemoteWindowTestApi
        {
            EnumerationTask = completion.Task,
            EnumerationEntered = entered,
        };
        var nativeSource = new MacOSRemoteWindowTestSource();
        var drainCompletion = new TaskCompletionSource<
            IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var drainEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        using var cancellation = new CancellationTokenSource();
        Task<LocalBoundaryResult>? drain = null;
        try
        {
            Task<LocalBoundaryResult> refresh = catalog.RefreshAsync(
                cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(nativeSource.Disposed);

            api.EnumerationTask = drainCompletion.Task;
            api.EnumerationEntered = drainEntered;
            completion.SetResult([nativeSource]);
            drain = catalog.RefreshAsync().AsTask();
            await drainEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The second operation owns the gate but cannot retire a wrongly
            // published first result while its own enumeration is pending.
            Assert.Empty(catalog.GetSnapshot());
            Assert.True(nativeSource.Disposed);
            Assert.Equal(1, nativeSource.DisposeCalls);
        }
        finally
        {
            completion.TrySetResult([nativeSource]);
            drainCompletion.TrySetResult([]);
            if (drain is not null)
            {
                await drain.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StableRefreshPreservesBindingAndReleasesOnlyFreshCandidate(
        bool reuseNativeOwner)
    {
        var original = new MacOSRemoteWindowTestSource();
        var candidate = reuseNativeOwner
            ? original : new MacOSRemoteWindowTestSource();
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        try
        {
            Assert.True((await catalog.RefreshAsync()).Succeeded);
            NativeRemoteWindowSourceSnapshot before = Assert.Single(
                catalog.GetSnapshot());
            Assert.True(catalog.TryAcquire(before.Token,
                before.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
            using (lease)
            {
                api.Sources = [candidate];

                Assert.True((await catalog.RefreshAsync()).Succeeded);

                NativeRemoteWindowSourceSnapshot after = Assert.Single(
                    catalog.GetSnapshot());
                Assert.Equal(before.Token, after.Token);
                Assert.Equal(before.Source.SourceGeneration,
                    after.Source.SourceGeneration);
                Assert.Equal(before.Source.ActivityId, after.Source.ActivityId);
                Assert.Equal(before.GeometryRevision, after.GeometryRevision);
                Assert.True(lease!.IsCurrent);
                Assert.True(catalog.TryAcquireNativeBinding(
                    NativeRemoteWindowSourceUse.Create(after, 1, 1),
                    out MacOSRemoteWindowSourceCatalog.NativeBinding? binding));
                using (binding)
                {
                    Assert.Same(original, binding!.Native);
                    Assert.True(binding.IsCurrent);
                }

                Assert.Equal(0, original.DisposeCalls);
                Assert.Equal(reuseNativeOwner ? 0 : 1, candidate.DisposeCalls);
            }
        }
        finally
        {
            await catalog.DisposeAsync();
        }

        Assert.Equal(1, original.DisposeCalls);
        Assert.Equal(1, candidate.DisposeCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ChangedWindowOrProcessInstanceRetiresExactSource(
        int changedIdentityField)
    {
        var original = new MacOSRemoteWindowTestSource();
        MacOSRemoteWindowNativeIdentity changedIdentity = changedIdentityField switch
        {
            0 => original.Identity with { WindowId = original.Identity.WindowId + 1 },
            1 => original.Identity with { ProcessId = original.Identity.ProcessId + 1 },
            2 => original.Identity with
            {
                ProcessStartSeconds = original.Identity.ProcessStartSeconds + 1,
            },
            3 => original.Identity with
            {
                ProcessStartMicroseconds = original.Identity.ProcessStartMicroseconds + 1,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(changedIdentityField)),
        };
        var replacement = new MacOSRemoteWindowTestSource
        {
            Identity = changedIdentity,
        };

        await AssertReplacementRetiresExactSourceAsync(original, replacement);
    }

    [Theory]
    [InlineData(1, 0, 64, 64, 1)]
    [InlineData(0, 1, 64, 64, 1)]
    [InlineData(0, 0, 65, 64, 1)]
    [InlineData(0, 0, 64, 65, 1)]
    [InlineData(0, 0, 64, 64, 2)]
    public async Task ChangedGeometryRetiresExactSource(
        double x,
        double y,
        double width,
        double height,
        double scaleFactor)
    {
        var original = new MacOSRemoteWindowTestSource();
        var replacement = new MacOSRemoteWindowTestSource
        {
            Geometry = NativeRemoteWindowGeometry.Create(
                x, y, width, height, scaleFactor),
        };

        await AssertReplacementRetiresExactSourceAsync(original, replacement);
    }

    [Fact]
    public async Task ReusedWindowAndProcessIdentityCannotReviveAnEarlierSelection()
    {
        var original = new MacOSRemoteWindowTestSource();
        var changedInstance = new MacOSRemoteWindowTestSource
        {
            Identity = original.Identity with
            {
                ProcessStartMicroseconds = original.Identity.ProcessStartMicroseconds + 1,
            },
        };
        var returnedIdentity = new MacOSRemoteWindowTestSource();
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        try
        {
            Assert.True((await catalog.RefreshAsync()).Succeeded);
            NativeRemoteWindowSourceSnapshot first = Assert.Single(catalog.GetSnapshot());
            Assert.True(catalog.TryAcquire(first.Token, first.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? firstLease));
            using (firstLease)
            {
                api.Sources = [changedInstance];
                Assert.True((await catalog.RefreshAsync()).Succeeded);
                NativeRemoteWindowSourceSnapshot second = Assert.Single(catalog.GetSnapshot());
                Assert.True(catalog.TryAcquire(second.Token, second.Source.SourceGeneration,
                    out NativeRemoteWindowSourceLease? secondLease));
                using (secondLease)
                {
                    api.Sources = [returnedIdentity];

                    Assert.True((await catalog.RefreshAsync()).Succeeded);

                    NativeRemoteWindowSourceSnapshot third = Assert.Single(catalog.GetSnapshot());
                    Assert.False(firstLease!.IsCurrent);
                    Assert.False(secondLease!.IsCurrent);
                    Assert.False(catalog.TryAcquire(first.Token,
                        first.Source.SourceGeneration, out _));
                    Assert.False(catalog.TryAcquire(second.Token,
                        second.Source.SourceGeneration, out _));
                    Assert.NotEqual(first.Token, third.Token);
                    Assert.NotEqual(second.Token, third.Token);
                    Assert.NotEqual(first.Source.ActivityId, third.Source.ActivityId);
                    Assert.True(third.Source.SourceGeneration > second.Source.SourceGeneration);
                    Assert.Equal(1, original.DisposeCalls);
                    Assert.Equal(1, changedInstance.DisposeCalls);
                    Assert.Equal(0, returnedIdentity.DisposeCalls);
                }
            }
        }
        finally
        {
            await catalog.DisposeAsync();
        }

        Assert.Equal(1, original.DisposeCalls);
        Assert.Equal(1, changedInstance.DisposeCalls);
        Assert.Equal(1, returnedIdentity.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableCaptureRetiresSourcesWithoutEnumerating(
        bool unsupported)
    {
        var original = new MacOSRemoteWindowTestSource();
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            catalog.GetSnapshot());
        Assert.True(catalog.TryAcquire(snapshot.Token,
            snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
        using (lease)
        {
            api.IsSupported = !unsupported;
            api.PermissionGranted = false;

            LocalBoundaryResult result = await catalog.RefreshAsync();

            Assert.False(result.Succeeded);
            Assert.Equal(unsupported ? "macos_capture_unsupported"
                : "macos_capture_permission_absent", result.ReasonCode);
            Assert.Equal(unsupported ? 1 : 2, api.PreflightCalls);
            Assert.Equal(1, api.EnumerationCalls);
            Assert.Empty(catalog.GetSnapshot());
            Assert.False(lease!.IsCurrent);
            Assert.False(catalog.TryAcquire(snapshot.Token,
                snapshot.Source.SourceGeneration, out _));
            Assert.Equal(1, original.DisposeCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Injected fatal verifies original allocation failure identity and stale source retirement, not actual runtime exhaustion.")]
    public async Task EnumerationFatalFailurePreservesOriginalAndClosesOldAdmission(
        bool nestedFailure)
    {
        var fatal = new OutOfMemoryException("native enumeration fatal");
        var original = new MacOSRemoteWindowTestSource();
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        try
        {
            Assert.True((await catalog.RefreshAsync()).Succeeded);
            NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
                catalog.GetSnapshot());
            Assert.True(catalog.TryAcquire(snapshot.Token,
                snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
            using (lease)
            {
                Exception thrown = nestedFailure
                    ? new AggregateException(new InvalidOperationException("ordinary"),
                        new AggregateException(fatal))
                    : fatal;
                api.EnumerationTask = Task.FromException<
                    IReadOnlyList<IMacOSRemoteWindowNativeSource>>(thrown);

                OutOfMemoryException reported = await Assert.ThrowsAsync<OutOfMemoryException>(
                    async () => await catalog.RefreshAsync());

                Assert.Same(fatal, reported);
                Assert.Empty(catalog.GetSnapshot());
                Assert.False(lease!.IsCurrent);
                Assert.False(catalog.TryAcquire(snapshot.Token,
                    snapshot.Source.SourceGeneration, out _));
                Assert.Equal(1, original.DisposeCalls);
            }
        }
        finally
        {
            Task disposal = catalog.DisposeAsync().AsTask();
            OutOfMemoryException reported = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await disposal);
            Assert.Same(fatal, reported);
            Assert.Same(disposal, catalog.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task OrdinaryEnumerationFailureClosesOldAdmissionWithBoundedReason()
    {
        var original = new MacOSRemoteWindowTestSource();
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            catalog.GetSnapshot());
        Assert.True(catalog.TryAcquire(snapshot.Token,
            snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
        using (lease)
        {
            api.EnumerationTask = Task.FromException<
                IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
                new InvalidOperationException("private native source details"));

            LocalBoundaryResult result = await catalog.RefreshAsync();

            Assert.False(result.Succeeded);
            Assert.Equal("macos_source_catalog_unavailable", result.ReasonCode);
            Assert.Empty(catalog.GetSnapshot());
            Assert.False(lease!.IsCurrent);
            Assert.False(catalog.TryAcquire(snapshot.Token,
                snapshot.Source.SourceGeneration, out _));
            Assert.Equal(1, original.DisposeCalls);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DuplicateIdentityRetiresSelectionAndReleasesEachOwnerOnce(
        int duplicateCase)
    {
        var original = new MacOSRemoteWindowTestSource();
        var firstCandidate = duplicateCase == 2
            ? original : new MacOSRemoteWindowTestSource();
        var secondCandidate = duplicateCase == 0
            ? new MacOSRemoteWindowTestSource() : original;
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        try
        {
            Assert.True((await catalog.RefreshAsync()).Succeeded);
            NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
                catalog.GetSnapshot());
            Assert.True(catalog.TryAcquire(snapshot.Token,
                snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
            using (lease)
            {
                api.Sources = [firstCandidate, secondCandidate];

                await catalog.RefreshAsync();

                Assert.Empty(catalog.GetSnapshot());
                Assert.False(lease!.IsCurrent);
                Assert.False(catalog.TryAcquire(snapshot.Token,
                    snapshot.Source.SourceGeneration, out _));
                Assert.Equal(1, original.DisposeCalls);
                Assert.Equal(1, firstCandidate.DisposeCalls);
                Assert.Equal(1, secondCandidate.DisposeCalls);
            }
        }
        finally
        {
            await catalog.DisposeAsync();
        }

        Assert.Equal(1, original.DisposeCalls);
        Assert.Equal(1, firstCandidate.DisposeCalls);
        Assert.Equal(1, secondCandidate.DisposeCalls);
    }

    [Fact]
    public async Task OverflowEnumerationReleasesEntireBatchAndRetiresOldSelection()
    {
        var original = new MacOSRemoteWindowTestSource();
        MacOSRemoteWindowTestSource[] overflow = Enumerable.Range(
            0, NativeRemoteWindowSourceRegistry.MaximumSources + 1)
            .Select(index => new MacOSRemoteWindowTestSource
            {
                Identity = new((uint)(100 + index), 1000 + index,
                    1_700_000_000, (ulong)index),
            }).ToArray();
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            catalog.GetSnapshot());
        Assert.True(catalog.TryAcquire(snapshot.Token,
            snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
        using (lease)
        {
            api.Sources = overflow;

            LocalBoundaryResult result = await catalog.RefreshAsync();

            Assert.False(result.Succeeded);
            Assert.Equal("macos_source_catalog_overflow", result.ReasonCode);
            Assert.Empty(catalog.GetSnapshot());
            Assert.False(lease!.IsCurrent);
            Assert.False(catalog.TryAcquire(snapshot.Token,
                snapshot.Source.SourceGeneration, out _));
            Assert.Equal(1, original.DisposeCalls);
            Assert.All(overflow, owner => Assert.Equal(1, owner.DisposeCalls));
        }
    }

    [Fact]
    public async Task DisposalJoinsPendingEnumerationAndDrainsLateOwners()
    {
        var original = new MacOSRemoteWindowTestSource();
        var lateDisposalEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLateDisposal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lateSource = new MacOSRemoteWindowTestSource
        {
            Identity = new(24, 43, 1_700_000_001, 124),
            DisposeCallback = () =>
            {
                lateDisposalEntered.TrySetResult();
                releaseLateDisposal.Task.GetAwaiter().GetResult();
            },
        };
        var completion = new TaskCompletionSource<
            IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            catalog.GetSnapshot());
        Assert.True(catalog.TryAcquire(snapshot.Token,
            snapshot.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
        using (lease)
        {
            api.EnumerationTask = completion.Task;
            api.EnumerationEntered = entered;
            Task<LocalBoundaryResult> refresh = catalog.RefreshAsync().AsTask();
            Task? firstDispose = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                firstDispose = catalog.DisposeAsync().AsTask();
                Task repeatedDispose = catalog.DisposeAsync().AsTask();
                Assert.Same(firstDispose, repeatedDispose);
                Assert.False(firstDispose.IsCompleted);
                Assert.Throws<ObjectDisposedException>(() => catalog.GetSnapshot());
                Assert.Throws<ObjectDisposedException>(() => catalog.TryAcquire(
                    snapshot.Token, snapshot.Source.SourceGeneration, out _));
                Assert.Equal(0, lateSource.DisposeCalls);
                completion.SetResult([lateSource]);
                await lateDisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(firstDispose.IsCompleted);
                Assert.False(lateSource.Disposed);
            }
            finally
            {
                completion.TrySetResult([lateSource]);
                releaseLateDisposal.TrySetResult();
                try
                {
                    await refresh.WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    await (firstDispose ?? catalog.DisposeAsync().AsTask())
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
            }

            Assert.False(lease!.IsCurrent);
            Assert.Equal(1, original.DisposeCalls);
            Assert.Equal(1, lateSource.DisposeCalls);
            await catalog.DisposeAsync();
            Assert.Equal(1, original.DisposeCalls);
            Assert.Equal(1, lateSource.DisposeCalls);
        }
    }

    [Fact]
    public async Task DisposalImmediatelyClosesExistingNativeBindingWhileEnumerationIsPending()
    {
        var original = new MacOSRemoteWindowTestSource();
        var lateSource = new MacOSRemoteWindowTestSource
        {
            Identity = new(24, 43, 1_700_000_001, 124),
        };
        var completion = new TaskCompletionSource<
            IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(catalog.GetSnapshot());
        Assert.True(catalog.TryAcquireNativeBinding(
            NativeRemoteWindowSourceUse.Create(snapshot, 1, 1),
            out MacOSRemoteWindowSourceCatalog.NativeBinding? binding));
        Assert.True(binding!.IsCurrent);
        api.EnumerationTask = completion.Task;
        api.EnumerationEntered = entered;
        Task<LocalBoundaryResult> refresh = catalog.RefreshAsync().AsTask();
        Task? disposal = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposal = catalog.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);
            Assert.False(binding.IsCurrent);
            Assert.Equal(0, original.DisposeCalls);
            Assert.Equal(0, lateSource.DisposeCalls);
            completion.SetResult([lateSource]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(lateSource.Disposed);
            Assert.Equal(1, lateSource.DisposeCalls);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(binding.IsCurrent);
            Assert.Equal(0, original.DisposeCalls);
        }
        finally
        {
            completion.TrySetResult([lateSource]);
            try
            {
                await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                binding.Dispose();
                await (disposal ?? catalog.DisposeAsync().AsTask())
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        Assert.Equal(1, original.DisposeCalls);
        Assert.Equal(1, lateSource.DisposeCalls);
    }

    private static async Task AssertReplacementRetiresExactSourceAsync(
        MacOSRemoteWindowTestSource original,
        MacOSRemoteWindowTestSource replacement)
    {
        var api = new MacOSRemoteWindowTestApi { Sources = [original] };
        var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api);
        try
        {
            Assert.True((await catalog.RefreshAsync()).Succeeded);
            NativeRemoteWindowSourceSnapshot before = Assert.Single(
                catalog.GetSnapshot());
            Assert.True(catalog.TryAcquire(before.Token,
                before.Source.SourceGeneration, out NativeRemoteWindowSourceLease? lease));
            using (lease)
            {
                int invalidations = 0;
                Assert.True(lease!.TryRegisterInvalidationCallback(
                    () => Interlocked.Increment(ref invalidations),
                    out NativeRemoteWindowSourceInvalidationRegistration? callback));
                using (callback)
                {
                    api.Sources = [replacement];
                    Assert.True((await catalog.RefreshAsync()).Succeeded);

                    NativeRemoteWindowSourceSnapshot after = Assert.Single(
                        catalog.GetSnapshot());
                    Assert.False(lease.IsCurrent);
                    Assert.Equal(1, invalidations);
                    Assert.False(catalog.TryAcquire(before.Token,
                        before.Source.SourceGeneration, out _));
                    Assert.NotEqual(before.Token, after.Token);
                    Assert.NotEqual(before.Source.ActivityId, after.Source.ActivityId);
                    Assert.True(after.Source.SourceGeneration > before.Source.SourceGeneration);
                    Assert.Equal(replacement.Geometry, after.Metadata.Geometry);
                    Assert.True(catalog.TryAcquireNativeBinding(
                        NativeRemoteWindowSourceUse.Create(after, 1, 1),
                        out MacOSRemoteWindowSourceCatalog.NativeBinding? binding));
                    using (binding)
                    {
                        Assert.Same(replacement, binding!.Native);
                        Assert.True(binding.IsCurrent);
                    }

                    Assert.Equal(1, original.DisposeCalls);
                    Assert.Equal(0, replacement.DisposeCalls);
                }
            }
        }
        finally
        {
            await catalog.DisposeAsync();
        }

        Assert.Equal(1, original.DisposeCalls);
        Assert.Equal(1, replacement.DisposeCalls);
    }

    [Fact]
    public async Task PromptFreeCatalogCreatesGenericSourceWithoutClaimingSafety()
    {
        var api = new MacOSRemoteWindowTestApi();
        var nativeSource = new MacOSRemoteWindowTestSource();
        api.Sources = [nativeSource];
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()),
            api);

        LocalBoundaryResult result = await catalog.RefreshAsync();

        Assert.True(result.Succeeded);
        NativeRemoteWindowSourceSnapshot source = Assert.Single(
            catalog.GetSnapshot());
        Assert.Equal("Remote Window", source.Metadata.DisplayName);
        Assert.Equal("Application", source.Metadata.OwningApplicationName);
        Assert.True(source.Metadata.SupportsCapture);
        Assert.False(source.Metadata.SupportsInput);
        Assert.Equal(ProtectionKind.Unknown, source.Metadata.Protection.Kind);
        Assert.False(source.Source.IsSemanticActivity);
        Assert.Equal(1, api.PreflightCalls);
        Assert.Equal(1, api.EnumerationCalls);
        Assert.False(nativeSource.Disposed);
    }
}

internal sealed class MacOSRemoteWindowTestApi : IMacOSRemoteWindowNativeApi
{
    public bool IsSupported { get; set; } = true;

    public bool PermissionGranted { get; set; } = true;

    public IReadOnlyList<IMacOSRemoteWindowNativeSource> Sources { get; set; } = [];

    public int PreflightCalls { get; private set; }

    public int EnumerationCalls { get; private set; }

    public Task<IReadOnlyList<IMacOSRemoteWindowNativeSource>>? EnumerationTask
    {
        get; set;
    }

    public TaskCompletionSource? EnumerationEntered { get; set; }

    public bool Current { get; set; } = true;

    public IMacOSRemoteWindowNativeCapture? Capture { get; set; }

    public int CaptureCalls { get; private set; }

    public Action<IMacOSRemoteWindowNativeSample>? SampleCallback { get; private set; }

    public Action? SourceUnavailableCallback { get; private set; }

    public bool PreflightCaptureAccess()
    {
        PreflightCalls++;
        return PermissionGranted;
    }

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync()
    {
        EnumerationCalls++;
        ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> completion =
            EnumerationTask is null
            ? ValueTask.FromResult(Sources)
            : new ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>>(
                EnumerationTask);
        EnumerationEntered?.TrySetResult();
        return completion;
    }

    public bool IsCurrent(IMacOSRemoteWindowNativeSource source) => Current;

    public IMacOSRemoteWindowNativeCapture CreateCapture(
        IMacOSRemoteWindowNativeSource source,
        Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
        Action sourceUnavailable)
    {
        CaptureCalls++;
        SampleCallback = takeSampleOwnership;
        SourceUnavailableCallback = sourceUnavailable;
        return Capture ?? throw new NotSupportedException();
    }
}

internal sealed class MacOSRemoteWindowTestSource : IMacOSRemoteWindowNativeSource
{
    private int disposeCalls;

    public MacOSRemoteWindowNativeIdentity Identity { get; init; } =
        new(23, 42, 1_700_000_000, 123);

    public NativeRemoteWindowGeometry Geometry { get; init; } =
        NativeRemoteWindowGeometry.Create(0, 0, 64, 64, 1);

    public bool Disposed { get; private set; }

    public Exception? DisposeFailure { get; init; }

    public Action? DisposeCallback { get; init; }

    public int DisposeCalls => Volatile.Read(ref disposeCalls);

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCalls);
        DisposeCallback?.Invoke();
        Disposed = true;
        if (DisposeFailure is not null)
        {
            throw DisposeFailure;
        }
    }
}
