using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Mdns.Tests;

internal sealed class DeviceIdentityFixture : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private DeviceIdentityFixture(DeviceIdentity identity, SignedDiscoveryOffer offer)
    {
        Identity = identity;
        Offer = offer;
    }

    public DeviceIdentity Identity { get; }

    public SignedDiscoveryOffer Offer { get; }

    public static DeviceIdentityFixture Create()
    {
        DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            Now,
            TimeSpan.FromSeconds(30),
            Enumerable.Repeat((byte)0x42, SignedDiscoveryOffer.NonceLength)
                .ToArray());
        return new DeviceIdentityFixture(identity, offer);
    }

    public void Dispose() => Identity.Dispose();
}
