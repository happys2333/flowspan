using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopPairingIntegrationTests
{
    private static readonly ProtocolVersion Version = new(1, 0);

    [Fact]
    public async Task TwoDesktopDecisionsCreateOnlyTheirLocalCapabilityGrants()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Initiator");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Responder");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        using var initiatorDecisions = new DesktopPairingDecisionSource();
        using var responderDecisions = new DesktopPairingDecisionSource();
        (DirectTcpPairingChannel initiatorChannel,
            DirectTcpPairingChannel responderChannel) = await ConnectPairingChannelsAsync();
        var profile = new PairingCeremonyProfile([Version]);
        var initiator = new PairingCeremony(
            profile,
            initiatorDecisions,
            initiatorTrust);
        var responder = new PairingCeremony(
            profile,
            responderDecisions,
            responderTrust);

        Task<PairingCeremonyResult> initiatorRun = initiator.RunInitiatorAsync(
            initiatorChannel,
            initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responderRun = responder.RunResponderAsync(
            responderChannel,
            responderIdentity).AsTask();
        DesktopPairingPrompt initiatorPrompt = await WaitForPromptAsync(
            initiatorDecisions);
        DesktopPairingPrompt responderPrompt = await WaitForPromptAsync(
            responderDecisions);

        Assert.Equal(
            initiatorPrompt.ShortAuthenticationString,
            responderPrompt.ShortAuthenticationString);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));

        Assert.True(initiatorDecisions.TryAccept(
            initiatorPrompt.PromptId,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        Assert.True(responderDecisions.TryAccept(
            responderPrompt.PromptId,
            CapabilityGrant.Of(Capability.ActivityReceive)));

        PairingCeremonyResult[] results = await Task.WhenAll(
            initiatorRun,
            responderRun).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, static result => Assert.True(result.Succeeded));
        Assert.True(initiatorTrust.Allows(
            responderIdentity.DeviceId,
            Capability.ActivityOffer));
        Assert.False(initiatorTrust.Allows(
            responderIdentity.DeviceId,
            Capability.ActivityReceive));
        Assert.True(responderTrust.Allows(
            initiatorIdentity.DeviceId,
            Capability.ActivityReceive));
        Assert.False(responderTrust.Allows(
            initiatorIdentity.DeviceId,
            Capability.ActivityOffer));
    }

    [Fact]
    public async Task OneDesktopRejectionLeavesBothTrustStoresEmpty()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Initiator");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Responder");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        using var initiatorDecisions = new DesktopPairingDecisionSource();
        using var responderDecisions = new DesktopPairingDecisionSource();
        (DirectTcpPairingChannel initiatorChannel,
            DirectTcpPairingChannel responderChannel) = await ConnectPairingChannelsAsync();
        var profile = new PairingCeremonyProfile([Version]);
        var initiator = new PairingCeremony(
            profile,
            initiatorDecisions,
            initiatorTrust);
        var responder = new PairingCeremony(
            profile,
            responderDecisions,
            responderTrust);

        Task<PairingCeremonyResult> initiatorRun = initiator.RunInitiatorAsync(
            initiatorChannel,
            initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responderRun = responder.RunResponderAsync(
            responderChannel,
            responderIdentity).AsTask();
        DesktopPairingPrompt initiatorPrompt = await WaitForPromptAsync(
            initiatorDecisions);
        await WaitForPromptAsync(responderDecisions);

        Assert.True(initiatorDecisions.TryReject(initiatorPrompt.PromptId));

        PairingCeremonyResult[] results = await Task.WhenAll(
            initiatorRun,
            responderRun).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, static result =>
        {
            Assert.False(result.Succeeded);
            Assert.Equal(PairingFailure.Rejected, result.Failure);
        });
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
        Assert.Null(responderDecisions.CurrentPrompt);
    }

    private static async Task<(
        DirectTcpPairingChannel Initiator,
        DirectTcpPairingChannel Responder)> ConnectPairingChannelsAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            Task<DirectTcpPairingChannel> accept =
                DirectTcpPairingChannel.AcceptAsync(listener).AsTask();
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            DirectTcpPairingChannel connect =
                await DirectTcpPairingChannel.ConnectAsync(endpoint);
            return (connect, await accept);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static DeviceIdentity CreateIdentity(string id, string name) =>
        DeviceIdentity.Generate(DeviceId.Parse(id), name);

    private static async Task<DesktopPairingPrompt> WaitForPromptAsync(
        DesktopPairingDecisionSource source)
    {
        if (source.CurrentPrompt is { } current)
        {
            return current;
        }

        var completion = new TaskCompletionSource<DesktopPairingPrompt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Opened
                && source.CurrentPrompt is { } prompt)
            {
                completion.TrySetResult(prompt);
            }
        }

        source.PromptChanged += OnChanged;
        try
        {
            if (source.CurrentPrompt is { } raced)
            {
                return raced;
            }

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            source.PromptChanged -= OnChanged;
        }
    }
}
