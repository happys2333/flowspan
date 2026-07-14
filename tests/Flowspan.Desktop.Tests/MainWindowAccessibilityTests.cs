using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Flowspan.Desktop.Tests;

public sealed class MainWindowAccessibilityTests
{
    [Fact]
    public async Task ShellDeclaresTextStatesAndSupportsKeyboardDisclosure()
    {
        using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup());
        await viewModel.InitializeAsync();
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            ToggleButton toggle = Assert.IsType<ToggleButton>(
                window.FindControl<ToggleButton>("IdentityDetailsToggle"));
            Button emergencyStop = Assert.IsType<Button>(
                window.FindControl<Button>("EmergencyStopButton"));
            TextBlock sharingStatus = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Show identity details",
                toggle.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Emergency stop",
                emergencyStop.GetValue(AutomationProperties.NameProperty));
            Assert.Equal("NOT SHARING", sharingStatus.Text);
            Assert.False(emergencyStop.IsEnabled);
            Assert.True(toggle.Focus());

            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(viewModel.IsIdentityDetailsVisible);
            Assert.Equal("Hide identity details", toggle.Content);
            window.Close();
        }, CancellationToken.None);
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
