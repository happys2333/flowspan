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

    [Fact]
    public void ActivitySwapRequiresNegotiatedProtocolOnePointOne()
    {
        Assert.False(ProtocolFeatures.SupportsActivitySwap(new ProtocolVersion(1, 0)));
        Assert.True(ProtocolFeatures.SupportsActivitySwap(new ProtocolVersion(1, 1)));
        Assert.True(ProtocolFeatures.SupportsActivitySwap(new ProtocolVersion(1, 2)));
        Assert.False(ProtocolFeatures.SupportsActivitySwap(new ProtocolVersion(2, 0)));

        ProtocolNegotiationResult result = ProtocolNegotiator.Negotiate(
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)],
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);

        Assert.Equal(ProtocolFeatures.ActivitySwapMinimumVersion, result.Version);
    }

    [Fact]
    public void EncryptedFinishedRequiresNegotiatedProtocolOnePointTwo()
    {
        Assert.False(ProtocolFeatures.RequiresSecureSessionFinished(
            new ProtocolVersion(1, 1)));
        Assert.True(ProtocolFeatures.RequiresSecureSessionFinished(
            new ProtocolVersion(1, 2)));
        Assert.True(ProtocolFeatures.RequiresSecureSessionFinished(
            new ProtocolVersion(1, 3)));
        Assert.False(ProtocolFeatures.RequiresSecureSessionFinished(
            new ProtocolVersion(2, 0)));
    }

    [Fact]
    public void LiveRekeyRequiresNegotiatedProtocolOnePointThree()
    {
        Assert.False(ProtocolFeatures.SupportsLiveRekey(
            new ProtocolVersion(1, 2)));
        Assert.True(ProtocolFeatures.SupportsLiveRekey(
            new ProtocolVersion(1, 3)));
        Assert.True(ProtocolFeatures.SupportsLiveRekey(
            new ProtocolVersion(1, 4)));
        Assert.False(ProtocolFeatures.SupportsLiveRekey(
            new ProtocolVersion(2, 0)));
    }
}
