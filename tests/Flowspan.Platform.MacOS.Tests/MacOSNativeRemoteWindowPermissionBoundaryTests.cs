using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSNativeRemoteWindowPermissionBoundaryTests
{
    [Fact]
    public void SnapshotPreflightsCaptureWithoutRequestingPermission()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, snapshot.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
        Assert.Equal(1, snapshot.OwnerGeneration);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void InitialAbsentPreflightRemainsNotDetermined()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        Assert.Equal(
            NativeRemoteWindowPermissionState.NotDetermined,
            snapshot.Capture);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Theory]
    [InlineData(true, NativeRemoteWindowPermissionState.Granted)]
    [InlineData(false, NativeRemoteWindowPermissionState.Denied)]
    public async Task ExplicitCaptureRequestMapsNativeDecision(
        bool nativeDecision,
        NativeRemoteWindowPermissionState expectedState)
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = nativeDecision,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        Assert.Equal(expectedState, snapshot.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(1, interop.RequestCalls);
    }

    [Fact]
    public async Task GrantedCaptureBecomesRevokedWhenPreflightLosesAccess()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        NativeRemoteWindowPermissionSnapshot revoked = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, revoked.Capture);
        Assert.True(revoked.Revision > granted.Revision);
        Assert.Equal(1, interop.RequestCalls);
        Assert.Equal(1, interop.PreflightCalls);
    }

    [Fact]
    public async Task RevokedCaptureStaysRevokedWhilePreflightRemainsAbsent()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        _ = await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        NativeRemoteWindowPermissionSnapshot first = boundary.GetSnapshot();

        NativeRemoteWindowPermissionSnapshot second = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, first.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, second.Capture);
        Assert.Equal(2, interop.PreflightCalls);
    }

    [Fact]
    public async Task RepeatedDeniedFactDoesNotAdvanceRevisionOrPublishChange()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = false,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        NativeRemoteWindowPermissionSnapshot denied =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        NativeRemoteWindowPermissionSnapshot repeated = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Denied, denied.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Denied, repeated.Capture);
        Assert.Same(denied, repeated);
        Assert.Equal(denied.Revision, repeated.Revision);
        Assert.Single(changes);
        Assert.Same(denied, changes[0]);
    }

    [Fact]
    public async Task TemporaryFailureDoesNotForgetPriorGrant()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        interop.PreflightFailure = new IOException("temporary-native-failure");
        NativeRemoteWindowPermissionSnapshot unavailable = boundary.GetSnapshot();
        interop.PreflightFailure = null;

        NativeRemoteWindowPermissionSnapshot recovered = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Unavailable,
            unavailable.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, recovered.Capture);
    }

    [Fact]
    public async Task OlderPreflightCannotOverwriteNewerDeniedRequest()
    {
        using var preflightEntered = new ManualResetEventSlim();
        using var releasePreflight = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = false,
            Preflight = () =>
            {
                preflightEntered.Set();
                if (!releasePreflight.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out releasing preflight.");
                }

                return true;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        Task<NativeRemoteWindowPermissionSnapshot> older = Task.Run(
            boundary.GetSnapshot);
        Assert.True(preflightEntered.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionSnapshot denied;
        try
        {
            denied = await boundary.RequestCapturePermissionAsync(
                CancellationToken.None);
        }
        finally
        {
            releasePreflight.Set();
        }

        NativeRemoteWindowPermissionSnapshot stale =
            await older.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeRemoteWindowPermissionState.Denied, denied.Capture);
        Assert.Same(denied, stale);
        Assert.Single(changes);
        Assert.Same(denied, changes[0]);
    }

    [Fact]
    public async Task OlderDeniedRequestCannotOverwriteNewerGrantedPreflight()
    {
        using var requestEntered = new ManualResetEventSlim();
        using var releaseRequest = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
            Request = () =>
            {
                requestEntered.Set();
                if (!releaseRequest.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out releasing request.");
                }

                return false;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        Task<NativeRemoteWindowPermissionSnapshot> older = Task.Run(
            async () => await boundary.RequestCapturePermissionAsync(
                CancellationToken.None));
        Assert.True(requestEntered.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionSnapshot granted;
        try
        {
            granted = boundary.GetSnapshot();
        }
        finally
        {
            releaseRequest.Set();
        }

        NativeRemoteWindowPermissionSnapshot stale =
            await older.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Same(granted, stale);
        Assert.Single(changes);
        Assert.Same(granted, changes[0]);
    }

    [Fact]
    public async Task OlderGrantCannotRestoreNewerRevokedPreflight()
    {
        using var requestEntered = new ManualResetEventSlim();
        using var releaseRequest = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        NativeRemoteWindowPermissionSnapshot granted =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        interop.Request = () =>
        {
            requestEntered.Set();
            if (!releaseRequest.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out releasing request.");
            }

            return true;
        };
        Task<NativeRemoteWindowPermissionSnapshot> older = Task.Run(
            async () => await boundary.RequestCapturePermissionAsync(
                CancellationToken.None));
        Assert.True(requestEntered.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionSnapshot revoked;
        try
        {
            revoked = boundary.GetSnapshot();
        }
        finally
        {
            releaseRequest.Set();
        }

        NativeRemoteWindowPermissionSnapshot stale =
            await older.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, revoked.Capture);
        Assert.Equal(1, granted.Revision);
        Assert.Equal(2, revoked.Revision);
        Assert.Same(revoked, stale);
        Assert.Equal([granted, revoked], changes);
    }

    [Fact]
    public void ThrowingObserverCannotBlockLaterSafetyObserver()
    {
        const string canary = "FLOWSPAN_PERMISSION_OBSERVER_CANARY";
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot? safetyObservation = null;
        boundary.Changed += _ => throw new InvalidOperationException(canary);
        boundary.Changed += snapshot => safetyObservation = snapshot;

        NativeRemoteWindowPermissionSnapshot published = boundary.GetSnapshot();

        Assert.Same(published, safetyObservation);
        Assert.Equal(1, published.Revision);
        Assert.Equal(NativeRemoteWindowPermissionState.Granted, published.Capture);
    }

    [Fact]
    public void ObserverCanReenterPermissionReadWithoutStateLockDeadlock()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot? reentered = null;
        boundary.Changed += snapshot =>
        {
            if (snapshot.Capture == NativeRemoteWindowPermissionState.Granted)
            {
                interop.PreflightResult = false;
                reentered = boundary.GetSnapshot();
            }
        };

        NativeRemoteWindowPermissionSnapshot initial = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, initial.Capture);
        Assert.Equal(1, initial.Revision);
        Assert.NotNull(reentered);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, reentered.Capture);
        Assert.Equal(2, reentered.Revision);
    }

    [Fact]
    public async Task DisposedBoundaryRejectsNewOperationsBeforeNativeCalls()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        await boundary.DisposeAsync();
        await boundary.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(boundary.GetSnapshot);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await boundary.RequestCapturePermissionAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await boundary.RequestInputPermissionAsync(CancellationToken.None));
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task DisposeRejectsBlockedNativeCompletionWithoutNotification()
    {
        using var preflightEntered = new ManualResetEventSlim();
        using var releasePreflight = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            Preflight = () =>
            {
                preflightEntered.Set();
                if (!releasePreflight.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out releasing preflight.");
                }

                return true;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        int notifications = 0;
        Action<NativeRemoteWindowPermissionSnapshot> observer =
            _ => notifications++;
        boundary.Changed += observer;
        Task<NativeRemoteWindowPermissionSnapshot> reading = Task.Run(
            boundary.GetSnapshot);
        Assert.True(preflightEntered.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            await boundary.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() =>
                boundary.Changed += _ => { });
            boundary.Changed -= observer;
        }
        finally
        {
            releasePreflight.Set();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await reading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, notifications);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task PreCancellationWinsAfterDisposeWithoutNativeCalls()
    {
        var interop = new RecordingScreenCapturePermissionInterop();
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        await boundary.DisposeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException capture =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await boundary.RequestCapturePermissionAsync(
                    cancellation.Token));
        OperationCanceledException input =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await boundary.RequestInputPermissionAsync(
                    cancellation.Token));

        Assert.Equal(cancellation.Token, capture.CancellationToken);
        Assert.Equal(cancellation.Token, input.CancellationToken);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task UnsupportedRuntimeDoesNotCrossNativeBoundary()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            IsSupported = false,
            PreflightResult = true,
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot preflight = boundary.GetSnapshot();
        NativeRemoteWindowPermissionSnapshot capture =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        NativeRemoteWindowPermissionSnapshot input =
            await boundary.RequestInputPermissionAsync(CancellationToken.None);

        Assert.Equal(
            NativeRemoteWindowPermissionState.Unsupported,
            preflight.Capture);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Unsupported,
            capture.Capture);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Unsupported,
            input.Input);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void PreflightFailureReturnsRedactedUnavailableFact()
    {
        const string canary = "FLOWSPAN_CAPTURE_PREFLIGHT_CANARY";
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightFailure = new IOException(canary),
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        Assert.Equal(
            NativeRemoteWindowPermissionState.Unavailable,
            snapshot.Capture);
        Assert.DoesNotContain(canary, snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task RequestFailureReturnsRedactedUnavailableFact()
    {
        const string canary = "FLOWSPAN_CAPTURE_REQUEST_CANARY";
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestFailure = new EntryPointNotFoundException(canary),
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        Assert.Equal(
            NativeRemoteWindowPermissionState.Unavailable,
            snapshot.Capture);
        Assert.DoesNotContain(canary, snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(1, interop.RequestCalls);
    }

    [Fact]
    public async Task PreCancelledCaptureRequestDoesNotCrossNativeBoundary()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException cancellationException =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await boundary.RequestCapturePermissionAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task InputRequestStaysUnsupportedWithoutScreenCaptureCall()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot =
            await boundary.RequestInputPermissionAsync(CancellationToken.None);

        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task ProductionBoundaryPreflightsCoreGraphicsOnMatchingHost()
    {
        await using var boundary =
            new MacOSNativeRemoteWindowPermissionBoundary();

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        if (OperatingSystem.IsMacOSVersionAtLeast(10, 15))
        {
            Assert.True(
                snapshot.Capture is NativeRemoteWindowPermissionState.Granted
                    or NativeRemoteWindowPermissionState.NotDetermined,
                $"Unexpected matching-host capture state: {snapshot.Capture}");
        }
        else
        {
            Assert.Equal(
                NativeRemoteWindowPermissionState.Unsupported,
                snapshot.Capture);
        }

        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
    }

    private sealed class RecordingScreenCapturePermissionInterop :
        IMacOSScreenCapturePermissionInterop
    {
        public bool IsSupported { get; init; } = true;

        public bool PreflightResult { get; set; }

        public Func<bool>? Preflight { get; init; }

        public Exception? PreflightFailure { get; set; }

        public bool RequestResult { get; init; }

        public Func<bool>? Request { get; set; }

        public Exception? RequestFailure { get; init; }

        public int PreflightCalls { get; private set; }

        public int RequestCalls { get; private set; }

        public bool PreflightScreenCaptureAccess()
        {
            PreflightCalls++;
            if (PreflightFailure is not null)
            {
                throw PreflightFailure;
            }

            return Preflight?.Invoke() ?? PreflightResult;
        }

        public bool RequestScreenCaptureAccess()
        {
            RequestCalls++;
            if (RequestFailure is not null)
            {
                throw RequestFailure;
            }

            return Request?.Invoke() ?? RequestResult;
        }
    }
}
