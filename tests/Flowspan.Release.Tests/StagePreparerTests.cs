using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class StagePreparerTests
{
    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    public void PrepareCreatesOnlyTargetLayoutAndCanonicalMetadata(string rid)
    {
        using var fixture = new ReleaseTestFixture(rid);

        fixture.Prepare();

        string packageRoot = Path.Combine(
            fixture.StageDirectory,
            fixture.Target.RootName);
        Assert.True(Directory.Exists(packageRoot));
        Assert.True(File.Exists(Path.Combine(
            fixture.StageDirectory,
            ReleaseContextCodec.StageMetadataFileName)));
        string entryPoint = Path.Combine(
            fixture.StageDirectory,
            fixture.Target.EntryPoint.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(entryPoint));
        Assert.Equal(
            "flowspan-release-entry\nflowspan-release-payload\n",
            File.ReadAllText(entryPoint));

        string plist = Path.Combine(
            packageRoot,
            "Contents",
            "Info.plist");
        Assert.Equal(rid == "osx-arm64", File.Exists(plist));
        if (rid == "osx-arm64")
        {
            string content = File.ReadAllText(plist);
            Assert.Contains("io.flowspan.desktop", content, StringComparison.Ordinal);
            Assert.Contains(fixture.Context.Version, content, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Root, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExistingStageIsRejectedWithoutMutation()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        Directory.CreateDirectory(fixture.StageDirectory);
        string canary = Path.Combine(fixture.StageDirectory, "canary.txt");
        File.WriteAllText(canary, "existing");

        Assert.Throws<ReleaseInputException>(fixture.Prepare);

        Assert.Equal("existing", File.ReadAllText(canary));
        Assert.Single(Directory.EnumerateFileSystemEntries(fixture.StageDirectory));
    }

    [Fact]
    public void AdditionalPublishMaterialIsRejectedBeforeStageCreation()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        File.WriteAllText(
            Path.Combine(fixture.PublishDirectory, "signing.key"),
            "sensitive");

        Assert.Throws<ReleaseInputException>(fixture.Prepare);

        Assert.False(Directory.Exists(fixture.StageDirectory));
    }

    [Fact]
    public void LinkedPublishEntryIsRejectedBeforeStageCreation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ReleaseTestFixture("linux-x64");
        string target = Path.Combine(fixture.Root, "outside.txt");
        File.WriteAllText(target, "outside");
        File.CreateSymbolicLink(
            Path.Combine(fixture.PublishDirectory, "linked.dat"),
            target);

        Assert.Throws<ReleaseInputException>(fixture.Prepare);

        Assert.False(Directory.Exists(fixture.StageDirectory));
    }

    [Fact]
    public void PublishAndStageOverlapIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        string nestedStage = Path.Combine(fixture.PublishDirectory, "stage");

        Assert.Throws<ReleaseInputException>(() => StagePreparer.Prepare(
            fixture.PublishDirectory,
            nestedStage,
            fixture.Context));
    }
}
