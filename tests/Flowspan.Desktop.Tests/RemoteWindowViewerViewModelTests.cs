using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Flowspan.Domain;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class RemoteWindowViewerViewModelTests
{
    private static HeadlessUnitTestSession HeadlessSession =>
        RemoteWindowViewerHeadlessApp.Session;

    [Fact]
    public async Task ReceivingIsExplicitViewOnlyAndPreparationShowsNoPixels()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            await using var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                static () => ValueTask.CompletedTask);
            RemoteWindowPreparationRequest request = CreateRequest();

            Assert.False(viewer.IsReceivingEnabled);
            Assert.Equal("renderer_unavailable", viewer.GetRejectionReason(request));
            viewer.EnableReceiving();
            Assert.Null(viewer.GetRejectionReason(request));
            await using IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, CancellationToken.None));

            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsViewing);
            byte[] pixels = [0, 0, 255, 255, 0, 255, 0, 255];
            using var frame = new DesktopRemoteWindowBgraFrame(pixels, 2, 1);
            await renderer.RenderAsync(frame, CancellationToken.None);
            WriteableBitmap displayed = Assert.IsType<WriteableBitmap>(viewer.ImageSource);
            frame.Dispose();

            Assert.All(pixels, value => Assert.Equal(0, value));
            using (var locked = displayed.Lock())
            {
                var displayedPixels = new byte[8];
                Marshal.Copy(locked.Address, displayedPixels, 0, displayedPixels.Length);
                Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, displayedPixels);
            }

            Assert.True(viewer.IsViewing);
            await viewer.StopReceivingAsync();
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsViewing);
            Assert.False(viewer.IsReceivingEnabled);
            return 0;
        }, CancellationToken.None);
    }

    private static RemoteWindowPreparationRequest CreateRequest(
        MirrorParticipantRole role = MirrorParticipantRole.ViewOnly) =>
        RemoteWindowPreparationRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            RemoteWindowSessionId.From(Guid.NewGuid()),
            ActivityId.From(Guid.NewGuid()),
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            role,
            DateTimeOffset.FromUnixTimeMilliseconds(
                DateTimeOffset.UtcNow.AddSeconds(20).ToUnixTimeMilliseconds()));

    [Fact]
    public async Task DrivingAndARequestFromAnEarlierEnableEpochAreRejected()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            await using var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                static () => ValueTask.CompletedTask);
            viewer.EnableReceiving();
            Assert.Equal("role_unsupported", viewer.GetRejectionReason(
                CreateRequest(MirrorParticipantRole.DriverEligible)));
            RemoteWindowPreparationRequest oldRequest = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(oldRequest));
            await viewer.StopReceivingAsync();
            viewer.EnableReceiving();

            Assert.Null(await viewer.PrepareAsync(oldRequest, CancellationToken.None));
            RemoteWindowPreparationRequest freshRequest = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(freshRequest));
            await using IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(freshRequest, CancellationToken.None));
            Assert.Null(viewer.ImageSource);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AFailedRenderStillClearsThePreviouslyDisplayedPixelsOnDisposal()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                static () => ValueTask.CompletedTask);
            viewer.EnableReceiving();
            RemoteWindowPreparationRequest request = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(request));
            IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, CancellationToken.None));
            using var first = new DesktopRemoteWindowBgraFrame([0, 0, 255, 255], 1, 1);
            await renderer.RenderAsync(first, CancellationToken.None);
            var invalid = new DesktopRemoteWindowBgraFrame([0, 255, 0, 255], 1, 1);
            invalid.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => renderer.RenderAsync(invalid, CancellationToken.None).AsTask());

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => renderer.DisposeAsync().AsTask());

            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsViewing);
            await viewer.DisposeAsync();
            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANotificationFailureCannotPreventRealStopOrLeaveItsTaskPending(
        bool executeBeforeThrowing)
    {
        var failure = new InvalidOperationException("test dispatcher failure");
        var dispatcher = new FailingDispatcher(failure, executeBeforeThrowing);
        int stops = 0;
        var viewer = new RemoteWindowViewerViewModel(
            dispatcher,
            () =>
            {
                Interlocked.Increment(ref stops);
                return ValueTask.CompletedTask;
            });
        viewer.EnableReceiving();
        dispatcher.Fails = true;

        await viewer.StopReceivingAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, stops);
        Assert.False(viewer.IsReceivingEnabled);
        Assert.Same(failure, viewer.NotificationFailure);
        dispatcher.Fails = false;
        await viewer.DisposeAsync();
    }

    private sealed class FailingDispatcher(
        Exception failure,
        bool executeBeforeThrowing) : IDesktopUiDispatcher
    {
        public bool Fails { get; set; }

        public void Post(Action callback)
        {
            if (!Fails || executeBeforeThrowing)
            {
                callback();
            }

            if (Fails)
            {
                throw failure;
            }
        }
    }

    [Fact]
    public async Task StoppingAnUnstartedUiCopyReleasesItsBorrowWithoutPresentingItLater()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var dispatcher = new OneQueuedDispatcher();
            await using var viewer = new RemoteWindowViewerViewModel(
                dispatcher,
                static () => ValueTask.CompletedTask);
            viewer.EnableReceiving();
            RemoteWindowPreparationRequest request = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(request));
            await using IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, CancellationToken.None));
            byte[] pixels = [0, 0, 255, 255];
            using var frame = new DesktopRemoteWindowBgraFrame(pixels, 1, 1);
            dispatcher.QueueOne = true;
            Task render = renderer.RenderAsync(frame, CancellationToken.None).AsTask();
            Assert.False(render.IsCompleted);
            Assert.NotNull(dispatcher.Pending);

            await viewer.StopReceivingAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
            frame.Dispose();
            dispatcher.RunQueued();

            Assert.All(pixels, value => Assert.Equal(0, value));
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsViewing);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AnActiveRenderNotificationCanStopItsOwnerAndExternalStopStillJoins()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            int stops = 0;
            await using var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                () =>
                {
                    Interlocked.Increment(ref stops);
                    return ValueTask.CompletedTask;
                });
            viewer.EnableReceiving();
            RemoteWindowPreparationRequest request = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(request));
            await using IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, CancellationToken.None));
            bool callbackStopped = false;
            viewer.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(viewer.ImageSource) && viewer.IsViewing)
                {
                    Task.Run(() => viewer.StopReceivingAsync().AsTask())
                        .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    callbackStopped = true;
                }
            };
            using var frame = new DesktopRemoteWindowBgraFrame([0, 0, 255, 255], 1, 1);

            await renderer.RenderAsync(frame, CancellationToken.None);
            await viewer.StopReceivingAsync();

            Assert.True(callbackStopped);
            Assert.Equal(1, stops);
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsReceivingEnabled);
            return 0;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private sealed class OneQueuedDispatcher : IDesktopUiDispatcher
    {
        public bool QueueOne { get; set; }

        public Action? Pending { get; private set; }

        public void Post(Action callback)
        {
            if (QueueOne)
            {
                QueueOne = false;
                Pending = callback;
                return;
            }

            AvaloniaDesktopUiDispatcher.Instance.Post(callback);
        }

        public void RunQueued()
        {
            Action callback = Assert.IsType<Action>(Pending);
            Pending = null;
            callback();
        }
    }

    [Fact]
    public async Task OneFailedUiClearStillRetriesClearingPixelsAndPreservesTheFailure()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var failure = new InvalidOperationException("test one-shot UI dispatch failure");
            var dispatcher = new OneShotFailureDispatcher(failure);
            var viewer = new RemoteWindowViewerViewModel(
                dispatcher,
                static () => ValueTask.CompletedTask);
            viewer.EnableReceiving();
            RemoteWindowPreparationRequest request = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(request));
            IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, CancellationToken.None));
            using var frame = new DesktopRemoteWindowBgraFrame([0, 0, 255, 255], 1, 1);
            await renderer.RenderAsync(frame, CancellationToken.None);
            Assert.True(viewer.IsViewing);
            dispatcher.SuccessfulPostsBeforeFailure = 1;

            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => viewer.StopReceivingAsync().AsTask());

            Assert.Same(failure, actual);
            Assert.False(viewer.IsViewing);
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsReceivingEnabled);
            Assert.Contains("CLEANUP FAILED", viewer.Status);
            viewer.EnableReceiving();
            Assert.False(viewer.IsReceivingEnabled);
            Assert.False(viewer.EnableReceivingCommand.CanExecute(null));
            Assert.True(viewer.StopReceivingCommand.CanExecute(null));
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                () => viewer.DisposeAsync().AsTask()));
            return 0;
        }, CancellationToken.None);
    }

    private sealed class OneShotFailureDispatcher(Exception failure) : IDesktopUiDispatcher
    {
        private readonly Lock gate = new();
        private int successfulPostsBeforeFailure = -1;

        public int SuccessfulPostsBeforeFailure
        {
            set
            {
                lock (gate)
                {
                    successfulPostsBeforeFailure = value;
                }
            }
        }

        public void Post(Action callback)
        {
            lock (gate)
            {
                if (successfulPostsBeforeFailure == 0)
                {
                    successfulPostsBeforeFailure = -1;
                    throw failure;
                }

                if (successfulPostsBeforeFailure > 0)
                {
                    successfulPostsBeforeFailure--;
                }
            }

            AvaloniaDesktopUiDispatcher.Instance.Post(callback);
        }
    }

    [Fact]
    public async Task PreparationWaitsForUiReadinessAndStopCancelsTheHiddenProbe()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var dispatcher = new OneQueuedDispatcher();
            await using var viewer = new RemoteWindowViewerViewModel(
                dispatcher,
                static () => ValueTask.CompletedTask);
            viewer.EnableReceiving();
            RemoteWindowPreparationRequest request = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(request));
            dispatcher.QueueOne = true;

            Task<IDesktopRemoteWindowParticipantRenderer?> preparing =
                viewer.PrepareAsync(request, CancellationToken.None).AsTask();

            Assert.False(preparing.IsCompleted);
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsViewing);
            await viewer.StopReceivingAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
            dispatcher.RunQueued();
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsReceivingEnabled);
            return 0;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Deterministic injection verifies the original fatal allocation exception survives cleanup.")]
    public async Task FatalStopFailureEscapesAsTheOriginalOutOfMemoryAfterOtherCleanupFails(
        bool nested)
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var fatal = new OutOfMemoryException("test original stop OOM");
            Exception stoppingFailure = nested
                ? new AggregateException(new InvalidOperationException(), fatal)
                : fatal;
            var dispatcher = new OneShotFailureDispatcher(
                new InvalidOperationException("test nonfatal renderer cleanup failure"));
            var viewer = new RemoteWindowViewerViewModel(
                dispatcher,
                () => ValueTask.FromException(stoppingFailure));
            viewer.EnableReceiving();
            RemoteWindowPreparationRequest request = CreateRequest();
            Assert.Null(viewer.GetRejectionReason(request));
            IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, CancellationToken.None));
            using var frame = new DesktopRemoteWindowBgraFrame([0, 0, 255, 255], 1, 1);
            await renderer.RenderAsync(frame, CancellationToken.None);
            dispatcher.SuccessfulPostsBeforeFailure = 1;

            OutOfMemoryException actual = await Assert.ThrowsAsync<OutOfMemoryException>(
                () => viewer.StopReceivingAsync().AsTask());

            Assert.Same(fatal, actual);
            Assert.Null(viewer.ImageSource);
            Assert.False(viewer.IsReceivingEnabled);
            Assert.Same(fatal, await Assert.ThrowsAsync<OutOfMemoryException>(
                () => viewer.DisposeAsync().AsTask()));
            return 0;
        }, CancellationToken.None);
    }
}
