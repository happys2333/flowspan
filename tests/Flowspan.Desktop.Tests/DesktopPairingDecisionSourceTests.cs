using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopPairingDecisionSourceTests
{
    [Fact]
    public async Task VerifiedRequestWaitsForAndReturnsExplicitLocalGrant()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();
        DateTimeOffset expiry = DateTimeOffset.Parse(
            "2026-07-14T02:00:00+00:00",
            System.Globalization.CultureInfo.InvariantCulture);

        Task<PairingDecision> decision = source.DecideAsync(
            new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "123456",
                expiry)).AsTask();
        DesktopPairingPrompt prompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        Assert.False(decision.IsCompleted);
        Assert.Equal("Peer desk", prompt.PeerDisplayName);
        Assert.Equal(peer.DeviceId.ToString(), prompt.PeerDeviceId);
        Assert.Equal(peer.PublicIdentity.Fingerprint, prompt.PeerFingerprint);
        Assert.Equal("123456", prompt.ShortAuthenticationString);
        Assert.Equal("1.0", prompt.ProtocolVersion);
        Assert.Equal(expiry, prompt.ExpiresAt);

        Assert.True(source.TryAccept(
            prompt.PromptId,
            CapabilityGrant.Of(Capability.ActivityReceive)));

        PairingDecision accepted = await decision;
        Assert.True(accepted.Accepted);
        Assert.True(accepted.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityReceive));
        Assert.False(accepted.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityOffer));
        Assert.Null(source.CurrentPrompt);
    }

    [Fact]
    public async Task ConcurrentRequestIsRejectedWithoutReplacingVisiblePeer()
    {
        using DeviceIdentity firstPeer = CreatePeer("First peer");
        using DeviceIdentity secondPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Second peer");
        using var source = new DesktopPairingDecisionSource();

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer, "111111")).AsTask();
        DesktopPairingPrompt firstPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        PairingDecision second = await source.DecideAsync(
            CreateRequest(secondPeer, "222222"));

        Assert.False(second.Accepted);
        Assert.Equal(firstPrompt.PromptId, source.CurrentPrompt?.PromptId);
        Assert.Equal("First peer", source.CurrentPrompt?.PeerDisplayName);
        Assert.True(source.TryReject(firstPrompt.PromptId));
        Assert.False((await first).Accepted);
    }

    [Fact]
    public async Task CancellationClearsPromptAndStaleCommandCannotResolveNextRequest()
    {
        using DeviceIdentity firstPeer = CreatePeer("First peer");
        using DeviceIdentity nextPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Next peer");
        using var source = new DesktopPairingDecisionSource();
        using var cancellation = new CancellationTokenSource();
        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer, "111111"),
            cancellation.Token).AsTask();
        Guid stalePromptId = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt).PromptId;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Null(source.CurrentPrompt);

        Task<PairingDecision> next = source.DecideAsync(
            CreateRequest(nextPeer, "222222")).AsTask();
        DesktopPairingPrompt nextPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);
        Assert.False(source.TryAccept(stalePromptId, CapabilityGrant.None));
        Assert.False(next.IsCompleted);
        Assert.True(source.TryReject(nextPrompt.PromptId));
        Assert.False((await next).Accepted);
    }

    [Fact]
    public async Task InvalidAuthenticationStringCannotOpenPrompt()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.DecideAsync(new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "12A456",
                DateTimeOffset.UtcNow.AddMinutes(1))));

        Assert.Null(source.CurrentPrompt);
    }

    private static PairingConfirmationRequest CreateRequest(
        DeviceIdentity peer,
        string code) => new(
        peer.PublicIdentity,
        new ProtocolVersion(1, 0),
        code,
        DateTimeOffset.UtcNow.AddMinutes(1));

    private static DeviceIdentity CreatePeer(string displayName) =>
        DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            displayName);
}
