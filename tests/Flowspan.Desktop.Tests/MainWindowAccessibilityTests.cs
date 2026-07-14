using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class MainWindowAccessibilityTests
{
    [Fact]
    public async Task ShellDeclaresTextStatesAndSupportsKeyboardDisclosure()
    {
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup());
        await viewModel.InitializeAsync();
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
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
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PairingConfirmationRequiresKeyboardCodeAcknowledgement()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        using var source = new DesktopPairingDecisionSource();
        await using var viewModel = new WorkspaceShellViewModel(
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
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
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
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        PairingDecision decision = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(decision.Accepted);
        Assert.Empty(decision.CapabilitiesGrantedToPeer.Capabilities);
    }

    [Fact]
    public async Task TrustedDeviceCapabilitiesAndRevokeAreKeyboardOperable()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None));
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            trustAuthority: new DesktopTrustAuthority(trustStore));
        await viewModel.InitializeAsync();
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            ListBox devices = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrustedDeviceList"));
            CheckBox mirrorView = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("GrantMirrorViewCheckBox"));
            Button save = Assert.IsType<Button>(
                window.FindControl<Button>("SaveCapabilitiesButton"));
            Button reviewRevoke = Assert.IsType<Button>(
                window.FindControl<Button>("ReviewRevokeButton"));
            Button confirmRevoke = Assert.IsType<Button>(
                window.FindControl<Button>("ConfirmRevokeButton"));
            TextBlock mutationStatus = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("TrustMutationStatusText"));

            Assert.Single(devices.Items);
            Assert.Equal(
                "Allow peer to view mirrored Activities",
                mirrorView.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Save peer capabilities",
                save.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Review device revocation",
                reviewRevoke.GetValue(AutomationProperties.NameProperty));
            Assert.True(mirrorView.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(save.IsEnabled);
            Assert.True(save.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(trustStore.Allows(peer.DeviceId, Capability.MirrorView));

            Assert.True(reviewRevoke.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(confirmRevoke.IsVisible);
            Assert.True(trustStore.TryGet(peer.DeviceId, out _));
            Assert.True(confirmRevoke.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.False(trustStore.TryGet(peer.DeviceId, out _));
            Assert.True(mutationStatus.IsVisible);
            Assert.Equal("DEVICE REVOKED", mutationStatus.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TrustedDeviceSelectionUpdatesCapabilityDraftByKeyboard()
    {
        using DeviceIdentity first = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "First desk");
        using DeviceIdentity second = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Second desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            first.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        trustStore.Register(new TrustRecord(
            second.PublicIdentity,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CapabilityGrant.Of(Capability.MirrorDrive)));
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            trustAuthority: new DesktopTrustAuthority(trustStore));
        await viewModel.InitializeAsync();
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            ListBox devices = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrustedDeviceList"));

            Assert.Equal("First desk", viewModel.TrustedDevices.SelectedDevice?.DisplayName);
            Assert.True(viewModel.TrustedDevices.GrantActivityOffer);
            Assert.False(viewModel.TrustedDevices.GrantMirrorDrive);
            Control firstItem = Assert.IsAssignableFrom<Control>(
                devices.ContainerFromIndex(0));
            Assert.True(firstItem.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

            Assert.Equal("Second desk", viewModel.TrustedDevices.SelectedDevice?.DisplayName);
            Assert.False(viewModel.TrustedDevices.GrantActivityOffer);
            Assert.True(viewModel.TrustedDevices.GrantMirrorDrive);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LocalPairingEnableAndCandidateActionsAreKeyboardReachable()
    {
        var runtime = new DesktopLocalPairingRuntime(
            new AccessibilityNetworkFactory());
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            trustAuthority: new DesktopTrustAuthority(new InMemoryTrustStore()),
            localPairingRuntime: runtime);
        await viewModel.InitializeAsync();
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            Button enable = Assert.IsType<Button>(
                window.FindControl<Button>("EnableLocalPairingButton"));
            ListBox candidates = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("LocalPairingCandidateList"));
            ListBox trustedConnections = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrustedPeerConnectionList"));
            TextBlock identityWarning = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("TrustedPeerIdentityWarningSummary"));
            Button pair = Assert.IsType<Button>(
                window.FindControl<Button>("PairDiscoveredDeviceButton"));
            Button cancel = Assert.IsType<Button>(
                window.FindControl<Button>("CancelOutboundPairingButton"));
            TextBlock sharing = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Enable local pairing",
                enable.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Discovered local pairing candidates",
                candidates.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Trusted peer local connection status",
                trustedConnections.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Pair selected local device",
                pair.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Cancel outbound pairing",
                cancel.GetValue(AutomationProperties.NameProperty));
            Assert.True(enable.IsEnabled);
            Assert.True(enable.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(viewModel.LocalPairing.IsEnabled);
            Assert.Single(trustedConnections.Items);
            Assert.True(identityWarning.IsVisible);
            Assert.Contains("IDENTITY CLAIM BLOCKED", identityWarning.Text);
            Assert.Equal(
                "Trusted peer identity warning",
                identityWarning.GetValue(AutomationProperties.NameProperty));
            Assert.Equal("NOT SHARING", sharing.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

    private sealed class AccessibilityNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(
                new AccessibilityNetworkSession());
    }

    private sealed class AccessibilityNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ImmutableArray<DesktopTrustedPeerConnectionSnapshot>
            GetTrustedPeerConnections() =>
        [
            new(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk",
                "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                DesktopTrustedPeerConnectionState.AuthenticatedIdle,
                null,
                null,
                "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"),
        ];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
