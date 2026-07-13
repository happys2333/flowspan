using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class CapabilityGrantTests
{
    [Fact]
    public void EmptyGrantDeniesEveryCapability()
    {
        foreach (Capability capability in Enum.GetValues<Capability>())
        {
            Assert.False(CapabilityGrant.None.Allows(capability));
        }
    }

    [Fact]
    public void GrantDoesNotImplyAdjacentCapability()
    {
        CapabilityGrant grant = CapabilityGrant.Of(Capability.MirrorView);

        Assert.True(grant.Allows(Capability.MirrorView));
        Assert.False(grant.Allows(Capability.MirrorDrive));
    }
}
