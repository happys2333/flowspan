using Flowspan.Protocol;

namespace Flowspan.Protocol.Tests;

public sealed class ProtocolNegotiatorTests
{
    [Fact]
    public void HighestCommonVersionIsSelected()
    {
        ProtocolNegotiationResult result = ProtocolNegotiator.Negotiate(
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 2), new ProtocolVersion(2, 0)],
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 2), new ProtocolVersion(3, 0)]);

        Assert.True(result.Succeeded);
        Assert.Equal(new ProtocolVersion(1, 2), result.Version);
        Assert.Equal(ProtocolNegotiationFailure.None, result.Failure);
    }

    [Fact]
    public void MissingCommonVersionIsStructuredFailure()
    {
        ProtocolNegotiationResult result = ProtocolNegotiator.Negotiate(
            [new ProtocolVersion(1, 0)],
            [new ProtocolVersion(2, 0)]);

        Assert.False(result.Succeeded);
        Assert.Equal(ProtocolNegotiationFailure.NoCommonVersion, result.Failure);
    }

    [Fact]
    public void EmptyVersionSetsAreIncompatible()
    {
        ProtocolNegotiationResult result = ProtocolNegotiator.Negotiate([], []);

        Assert.False(result.Succeeded);
    }
}
