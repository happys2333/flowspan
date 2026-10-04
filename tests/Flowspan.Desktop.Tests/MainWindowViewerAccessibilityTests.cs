using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;

namespace Flowspan.Desktop.Tests;

public sealed class MainWindowViewerAccessibilityTests
{
    private static HeadlessUnitTestSession HeadlessSession =>
        HeadlessUnitTestSession.GetOrStartForAssembly(
            typeof(MainWindowViewerAccessibilityTests).Assembly);

    [Fact]
    public async Task ReceivingControlsExplainViewOnlyExecutionAndAreUnavailableByDefault()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            await using var shell = new WorkspaceShellViewModel(new ReadyStartup());
            await shell.InitializeAsync();
            var window = new MainWindow { DataContext = shell };
            var closed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            try
            {
                DrainPendingUiJobs();
                TextBlock status = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("RemoteWindowViewerStatusText"));
                TextBlock explanation = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("RemoteWindowViewerExplanationText"));
                Button enable = Assert.IsType<Button>(
                    window.FindControl<Button>("EnableRemoteWindowReceivingButton"));
                Button stop = Assert.IsType<Button>(
                    window.FindControl<Button>("StopRemoteWindowReceivingButton"));
                Image image = Assert.IsType<Image>(
                    window.FindControl<Image>("RemoteWindowViewerImage"));

                Assert.Equal("REMOTE WINDOW RECEIVING UNAVAILABLE", status.Text);
                Assert.Contains("source device", explanation.Text);
                Assert.Contains("cannot send", explanation.Text);
                Assert.Equal(TextWrapping.Wrap, explanation.TextWrapping);
                Assert.Equal("Remote Window receiving status",
                    status.GetValue(AutomationProperties.NameProperty));
                Assert.False(enable.IsEffectivelyEnabled);
                Assert.False(stop.IsEffectivelyEnabled);
                Assert.Null(image.Source);
                Assert.False(image.IsVisible);
                Assert.True(enable.MinHeight >= 44);
                Assert.True(stop.MinHeight >= 44);
                Assert.Equal("Enable view-only Remote Window receiving",
                    enable.GetValue(AutomationProperties.NameProperty));
                Assert.Equal("Stop Remote Window receiving",
                    stop.GetValue(AutomationProperties.NameProperty));
                Assert.False(string.IsNullOrWhiteSpace(
                    enable.GetValue(AutomationProperties.HelpTextProperty)));
                Assert.Contains("closes this local view",
                    stop.GetValue(AutomationProperties.HelpTextProperty));
            }
            finally
            {
                window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReceivingCanBeEnabledAndStoppedWithTheKeyboard()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var stopRequested = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var stopReleased = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                () =>
                {
                    stopRequested.TrySetResult();
                    return new ValueTask(stopReleased.Task);
                });
            await using var shell = new WorkspaceShellViewModel(
                new ReadyStartup(),
                remoteWindowViewer: viewer);
            await shell.InitializeAsync();
            var window = new MainWindow { DataContext = shell };
            var closed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            try
            {
                DrainPendingUiJobs();
                Button enable = Assert.IsType<Button>(
                    window.FindControl<Button>("EnableRemoteWindowReceivingButton"));
                Button stop = Assert.IsType<Button>(
                    window.FindControl<Button>("StopRemoteWindowReceivingButton"));
                TextBlock status = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("RemoteWindowViewerStatusText"));
                Assert.Same(viewer, shell.RemoteWindowViewer);
                Assert.False(viewer.IsReceivingEnabled);
                Assert.Equal("REMOTE WINDOW RECEIVING OFF", status.Text);
                Assert.True(enable.IsEffectivelyEnabled);
                Assert.False(stop.IsEffectivelyEnabled);
                Assert.True(enable.Focus());

                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

                DrainPendingUiJobs();
                Assert.True(viewer.IsReceivingEnabled);
                Assert.Equal("WAITING FOR A VIEW-ONLY REMOTE WINDOW", status.Text);
                Assert.False(enable.IsEffectivelyEnabled);
                Assert.True(stop.IsEffectivelyEnabled);
                Assert.True(stop.Focus());

                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

                await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();
                Assert.False(viewer.IsReceivingEnabled);
                Assert.Equal("STOPPING REMOTE WINDOW RECEIVING", status.Text);
                Assert.False(enable.IsEffectivelyEnabled);
                Assert.False(stop.IsEffectivelyEnabled);
                stopReleased.TrySetResult();
                await viewer.StopReceivingAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();
                Assert.Equal("REMOTE WINDOW RECEIVING OFF", status.Text);
                Assert.True(enable.IsEffectivelyEnabled);
                Assert.False(stop.IsEffectivelyEnabled);
            }
            finally
            {
                stopReleased.TrySetResult();
                window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClosingFromAViewerNotificationStillWaitsForReceivingCleanup()
    {
        await HeadlessSession.Dispatch<int>(async () =>
        {
            var stopRequested = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var stopReleased = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                () =>
                {
                    stopRequested.TrySetResult();
                    return new ValueTask(stopReleased.Task);
                });
            await using var shell = new WorkspaceShellViewModel(
                new ReadyStartup(),
                remoteWindowViewer: viewer);
            await shell.InitializeAsync();
            var window = new MainWindow { DataContext = shell };
            var closed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            bool notificationClosedWindow = false;
            bool reentrantDisposalCompleted = false;
            viewer.PropertyChanged += (_, _) =>
            {
                if (notificationClosedWindow || !viewer.IsReceivingEnabled)
                {
                    return;
                }

                notificationClosedWindow = true;
                window.Close();
                Task reentrantDisposal = shell.DisposeAsync().AsTask();
                reentrantDisposalCompleted = reentrantDisposal.IsCompletedSuccessfully;
                if (reentrantDisposalCompleted)
                {
                    reentrantDisposal.GetAwaiter().GetResult();
                }
            };

            try
            {
                DrainPendingUiJobs();
                viewer.EnableReceiving();
                await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Task externalDisposal = shell.DisposeAsync().AsTask();

                Assert.True(notificationClosedWindow);
                Assert.True(reentrantDisposalCompleted);
                Assert.False(viewer.IsReceivingEnabled);
                Assert.False(externalDisposal.IsCompleted);
                Assert.False(closed.Task.IsCompleted);
                stopReleased.TrySetResult();
                await externalDisposal.WaitAsync(TimeSpan.FromSeconds(5));
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(viewer.EnableReceivingCommand.CanExecute(null));
            }
            finally
            {
                stopReleased.TrySetResult();
                window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            return 0;
        }, CancellationToken.None);
    }

    private static void DrainPendingUiJobs()
    {
        for (int index = 0; index < 10; index++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private sealed class ReadyStartup : IDesktopIdentityStartup
    {
        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose()
        {
        }
    }
}
