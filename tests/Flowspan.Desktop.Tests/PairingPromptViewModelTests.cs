using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class PairingPromptViewModelTests
{
    [Fact]
    public async Task AcceptRequiresCodeComparisonAndStartsWithNoCapabilities()
    {
        using DeviceIdentity peer = CreatePeer();
        using var source = new DesktopPairingDecisionSource();
        using var viewModel = new PairingPromptViewModel(
            source,
            InlineDesktopUiDispatcher.Instance);
        Task<PairingDecision> pending = source.DecideAsync(
            CreateRequest(peer)).AsTask();

        Assert.True(viewModel.HasPendingPrompt);
        Assert.Equal("654 321", viewModel.PairingCode);
        Assert.Equal("No capabilities selected.", viewModel.CapabilitySummary);
        Assert.False(viewModel.AcceptPairingCommand.CanExecute(null));

        viewModel.GrantActivityOffer = true;
        Assert.False(viewModel.AcceptPairingCommand.CanExecute(null));
        viewModel.IsCodeConfirmed = true;
        Assert.True(viewModel.AcceptPairingCommand.CanExecute(null));
        viewModel.AcceptPairingCommand.Execute(null);

        PairingDecision decision = await pending;
        Assert.True(decision.Accepted);
        Assert.True(decision.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityOffer));
        Assert.False(decision.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityReceive));
        Assert.False(viewModel.HasPendingPrompt);
        Assert.Contains("Waiting for the peer", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectNeverReturnsSelectedCapabilities()
    {
        using DeviceIdentity peer = CreatePeer();
        using var source = new DesktopPairingDecisionSource();
        using var viewModel = new PairingPromptViewModel(
            source,
            InlineDesktopUiDispatcher.Instance);
        Task<PairingDecision> pending = source.DecideAsync(
            CreateRequest(peer)).AsTask();
        viewModel.GrantActivityOffer = true;
        viewModel.GrantActivityReceive = true;

        viewModel.RejectPairingCommand.Execute(null);

        PairingDecision decision = await pending;
        Assert.False(decision.Accepted);
        Assert.Empty(decision.CapabilitiesGrantedToPeer.Capabilities);
        Assert.False(viewModel.HasPendingPrompt);
        Assert.Contains("No capabilities", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeRejectsVisiblePromptWithoutGrantingCapabilities()
    {
        using DeviceIdentity peer = CreatePeer();
        using var source = new DesktopPairingDecisionSource();
        var viewModel = new PairingPromptViewModel(
            source,
            InlineDesktopUiDispatcher.Instance);
        Task<PairingDecision> pending = source.DecideAsync(
            CreateRequest(peer)).AsTask();
        viewModel.GrantActivityOffer = true;
        viewModel.IsCodeConfirmed = true;

        viewModel.Dispose();

        PairingDecision decision = await pending;
        Assert.False(decision.Accepted);
        Assert.Empty(decision.CapabilitiesGrantedToPeer.Capabilities);
    }

    [Fact]
    public async Task OutOfOrderUiCallbacksCannotRestoreCanceledPeerOverNewPrompt()
    {
        using DeviceIdentity firstPeer = CreatePeer();
        using DeviceIdentity nextPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Next peer");
        using var source = new DesktopPairingDecisionSource();
        var dispatcher = new QueuedDispatcher();
        using var viewModel = new PairingPromptViewModel(source, dispatcher);
        using var cancellation = new CancellationTokenSource();
        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer),
            cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Task<PairingDecision> next = source.DecideAsync(
            new PairingConfirmationRequest(
                nextPeer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "222222",
                DateTimeOffset.UtcNow.AddMinutes(1))).AsTask();
        DesktopPairingPrompt nextPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        dispatcher.RunNewestFirst();

        Assert.True(viewModel.HasPendingPrompt);
        Assert.Equal("Next peer", viewModel.PeerDisplayName);
        Assert.Equal("222 222", viewModel.PairingCode);
        Assert.True(source.TryReject(nextPrompt.PromptId));
        Assert.False((await next).Accepted);
    }

    private static PairingConfirmationRequest CreateRequest(DeviceIdentity peer) => new(
        peer.PublicIdentity,
        new ProtocolVersion(1, 0),
        "654321",
        DateTimeOffset.UtcNow.AddMinutes(1));

    private static DeviceIdentity CreatePeer() => DeviceIdentity.Generate(
        DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
        "Peer desk");

    private sealed class QueuedDispatcher : IDesktopUiDispatcher
    {
        private readonly List<Action> callbacks = [];

        public void Post(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            callbacks.Add(callback);
        }

        public void RunNewestFirst()
        {
            for (int index = callbacks.Count - 1; index >= 0; index--)
            {
                callbacks[index]();
            }

            callbacks.Clear();
        }
    }
}
