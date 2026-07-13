using System.Net;
using System.Net.Sockets;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class DirectTcpPairingChannelTests
{
    [Fact]
    public async Task RealLoopbackCeremonyPersistsTrustOnlyAfterMatchingSasAcceptance()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        var initiatorDecision = new AcceptingDecisionSource(
            CapabilityGrant.Of(Capability.ActivityReceive));
        var responderDecision = new AcceptingDecisionSource(
            CapabilityGrant.Of(Capability.MirrorView));
        PairingCeremonyProfile profile = new(
            [new ProtocolVersion(1, 0)],
            timeout: TimeSpan.FromSeconds(2));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPairingChannel> accepting =
            DirectTcpPairingChannel.AcceptAsync(listener).AsTask();
        DirectTcpPairingChannel initiatorChannel =
            await DirectTcpPairingChannel.ConnectAsync(endpoint);
        DirectTcpPairingChannel responderChannel = await accepting;

        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            profile,
            initiatorDecision,
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            profile,
            responderDecision,
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();
        PairingCeremonyResult[] results = await Task.WhenAll(initiating, responding)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.All(results, static result => Assert.True(result.Succeeded));
        Assert.All(
            results,
            static result => Assert.Equal(
                TrustRegistrationResult.Added,
                result.TrustRegistration));
        Assert.Equal(
            initiatorDecision.Request!.ShortAuthenticationString,
            responderDecision.Request!.ShortAuthenticationString);
        Assert.Equal(endpoint, initiatorChannel.RemoteEndPoint);
        Assert.True(initiatorTrust.Allows(
            responderIdentity.DeviceId,
            Capability.ActivityReceive));
        Assert.True(responderTrust.Allows(
            initiatorIdentity.DeviceId,
            Capability.MirrorView));
    }

    private sealed class AcceptingDecisionSource(CapabilityGrant capabilities) :
        IPairingDecisionSource
    {
        public PairingConfirmationRequest? Request { get; private set; }

        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return ValueTask.FromResult(new PairingDecision(
                accepted: true,
                capabilities));
        }
    }
}
