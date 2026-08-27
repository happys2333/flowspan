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

    [Fact]
    public void SceneApplyRequiresNegotiatedProtocolOnePointFour()
    {
        Assert.False(ProtocolFeatures.SupportsSceneApply(
            new ProtocolVersion(1, 3)));
        Assert.True(ProtocolFeatures.SupportsSceneApply(
            new ProtocolVersion(1, 4)));
        Assert.True(ProtocolFeatures.SupportsSceneApply(
            new ProtocolVersion(1, 5)));
        Assert.False(ProtocolFeatures.SupportsSceneApply(
            new ProtocolVersion(2, 0)));
    }

    [Fact]
    public void RemoteWindowRequiresNegotiatedProtocolOnePointFive()
    {
        Assert.False(ProtocolFeatures.SupportsRemoteWindow(
            new ProtocolVersion(1, 4)));
        Assert.True(ProtocolFeatures.SupportsRemoteWindow(
            new ProtocolVersion(1, 5)));
        Assert.True(ProtocolFeatures.SupportsRemoteWindow(
            new ProtocolVersion(1, 6)));
        Assert.False(ProtocolFeatures.SupportsRemoteWindow(
            new ProtocolVersion(2, 0)));
    }

    [Fact]
    public void RemoteWindowMediaRouteRequiresNegotiatedProtocolOnePointSix()
    {
        Assert.False(ProtocolFeatures.SupportsRemoteWindowMediaRoute(
            new ProtocolVersion(1, 5)));
        Assert.True(ProtocolFeatures.SupportsRemoteWindowMediaRoute(
            new ProtocolVersion(1, 6)));
        Assert.True(ProtocolFeatures.SupportsRemoteWindowMediaRoute(
            new ProtocolVersion(1, 7)));
        Assert.False(ProtocolFeatures.SupportsRemoteWindowMediaRoute(
            new ProtocolVersion(2, 0)));
    }

    [Fact]
    public void ProductionProfileAdvertisesAllV1MinorsAndPrefersRemoteWindow()
    {
        Assert.Equal(
            [
                new ProtocolVersion(1, 0),
                new ProtocolVersion(1, 1),
                new ProtocolVersion(1, 2),
                new ProtocolVersion(1, 3),
                new ProtocolVersion(1, 4),
                new ProtocolVersion(1, 5),
                new ProtocolVersion(1, 6),
            ],
            ProtocolFeatures.ProductionSupportedVersions.ToArray());

        ProtocolNegotiationResult result = ProtocolNegotiator.Negotiate(
            ProtocolFeatures.ProductionSupportedVersions,
            [new ProtocolVersion(1, 2), new ProtocolVersion(1, 3)]);

        Assert.True(result.Succeeded);
        Assert.Equal(new ProtocolVersion(1, 3), result.Version);
        Assert.True(ProtocolFeatures.SupportsLiveRekey(result.Version));
        Assert.False(ProtocolFeatures.SupportsSceneApply(result.Version));
    }

    [Fact]
    public void CurrentPeersNegotiateProtocolOnePointSix()
    {
        ProtocolNegotiationResult result = ProtocolNegotiator.Negotiate(
            ProtocolFeatures.ProductionSupportedVersions,
            ProtocolFeatures.ProductionSupportedVersions);

        Assert.True(result.Succeeded);
        Assert.Equal(new ProtocolVersion(1, 6), result.Version);
        Assert.True(ProtocolFeatures.SupportsSceneApply(result.Version));
        Assert.True(ProtocolFeatures.SupportsRemoteWindow(result.Version));
        Assert.True(ProtocolFeatures.SupportsRemoteWindowMediaRoute(result.Version));
    }
}
