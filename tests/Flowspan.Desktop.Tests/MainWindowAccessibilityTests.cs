using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

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

    [Fact]
    public async Task PairingConfirmationRequiresKeyboardCodeAcknowledgement()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        using var source = new DesktopPairingDecisionSource();
        using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            source,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        Task<PairingDecision> pending = source.DecideAsync(
            new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "123456",
                DateTimeOffset.UtcNow.AddMinutes(1))).AsTask();
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            CheckBox confirmation = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("PairingCodeConfirmation"));
            Button accept = Assert.IsType<Button>(
                window.FindControl<Button>("AcceptPairingButton"));
            Button reject = Assert.IsType<Button>(
                window.FindControl<Button>("RejectPairingButton"));
            TextBlock code = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("PairingCodeValue"));

            Assert.Equal("123 456", code.Text);
            Assert.Equal(
                "I compared the pairing code on both devices",
                confirmation.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Confirm pairing",
                accept.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Reject pairing",
                reject.GetValue(AutomationProperties.NameProperty));
            Assert.False(accept.IsEnabled);
            Assert.True(confirmation.Focus());

            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(accept.IsEnabled);
            Assert.True(accept.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.Close();
        }, CancellationToken.None);

        PairingDecision decision = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(decision.Accepted);
        Assert.Empty(decision.CapabilitiesGrantedToPeer.Capabilities);
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
