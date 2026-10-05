using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowCaptureBoundaryTests
{
    [Fact]
    public async Task StopClosesDeliveryAndWaitsForLateNativeStart()
    {
        var startCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeCapture = new MacOSRemoteWindowTestCapture
        {
            StartResult = startCompletion.Task,
        };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api, ownershipPool: new MacOSRemoteWindowSourceOwnershipPool());
        await catalog.RefreshAsync();
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        Task<LocalBoundaryResult> start = boundary.StartAsync(
            NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1),
            new MacOSRemoteWindowTestSink(), CancellationToken.None).AsTask();
        Assert.Equal(1, nativeCapture.StartCalls);

        Assert.Equal("macos_capture_delivery_closed", boundary.StopNow().ReasonCode);
        Task dispose = boundary.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        Assert.Equal(0, nativeCapture.StopCalls);
        Assert.Equal(0, nativeCapture.DisposeCalls);

        startCompletion.SetResult(true);
        Assert.False((await start).Succeeded);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }

    [Fact]
    public async Task UnconfirmedNativeCleanupRetainsBorrowAndBlocksReplacement()
    {
        var nativeSource = new MacOSRemoteWindowTestSource();
        var nativeCapture = new MacOSRemoteWindowTestCapture
        {
            StopResult = Task.FromResult(false),
        };
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [nativeSource],
            Capture = nativeCapture,
        };
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api, ownershipPool: new MacOSRemoteWindowSourceOwnershipPool());
        await catalog.RefreshAsync();
        var sourceUse = NativeRemoteWindowSourceUse.Create(Assert.Single(catalog.GetSnapshot()), 1, 1);
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        Assert.True((await boundary.StartAsync(sourceUse,
            new MacOSRemoteWindowTestSink(), CancellationToken.None)).Succeeded);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await boundary.DisposeAsync());
        Assert.Equal("macos_capture_cleanup_unconfirmed", failure.Message);
        Assert.Equal(0, nativeCapture.DisposeCalls);
        await catalog.DisposeAsync();
        Assert.False(nativeSource.Disposed);
        Assert.False((await boundary.StartAsync(sourceUse,
            new MacOSRemoteWindowTestSink(), CancellationToken.None)).Succeeded);
        InvalidOperationException repeated = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await boundary.DisposeAsync());
        Assert.Same(failure, repeated);
    }

    [Fact]
    public async Task ExactCurrentSourceCanStartAndDisposeJoinsNativeCleanup()
    {
        var nativeCapture = new MacOSRemoteWindowTestCapture();
        var api = new MacOSRemoteWindowTestApi
        {
            Sources = [new MacOSRemoteWindowTestSource()],
            Capture = nativeCapture,
        };
        await using var catalog = new MacOSRemoteWindowSourceCatalog(
            DeviceId.From(Guid.NewGuid()), api, ownershipPool: new MacOSRemoteWindowSourceOwnershipPool());
        await catalog.RefreshAsync();
        NativeRemoteWindowSourceSnapshot source = Assert.Single(
            catalog.GetSnapshot());
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        var sink = new MacOSRemoteWindowTestSink();

        LocalBoundaryResult started = await boundary.StartAsync(
            NativeRemoteWindowSourceUse.Create(source, 1, 1),
            sink,
            CancellationToken.None);
        Assert.True(started.Succeeded);
        Assert.Equal(1, api.CaptureCalls);
        await boundary.DisposeAsync();

        Assert.Equal(1, nativeCapture.StopCalls);
        Assert.Equal(1, nativeCapture.DisposeCalls);
    }
}

internal sealed class MacOSRemoteWindowTestCapture : IMacOSRemoteWindowNativeCapture
{
    public Task<bool> StartResult { get; set; } = Task.FromResult(true);

    public Task<bool> StopResult { get; set; } = Task.FromResult(true);

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public ValueTask<bool> StartAsync()
    {
        StartCalls++;
        return new ValueTask<bool>(StartResult);
    }

    public ValueTask<bool> StopAndDrainAsync()
    {
        StopCalls++;
        return new ValueTask<bool>(StopResult);
    }

    public void Dispose() => DisposeCalls++;
}

internal sealed class MacOSRemoteWindowTestSink : INativeRemoteWindowFrameSink
{
    public List<long> Sequences { get; } = [];

    public Action<NativeRemoteWindowFrame>? OnFrame { get; set; }

    public void TakeOwnership(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrame frame)
    {
        try
        {
            Sequences.Add(frame.Sequence);
            OnFrame?.Invoke(frame);
        }
        finally
        {
            frame.Dispose();
        }
    }
}
