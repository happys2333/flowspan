using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class ReleaseContextTests
{
    [Theory]
    [InlineData("win-x64", ".zip", "Flowspan/Flowspan.Desktop.exe")]
    [InlineData("osx-arm64", ".tar.gz", "Flowspan.app/Contents/MacOS/Flowspan.Desktop")]
    [InlineData("linux-x64", ".tar.gz", "flowspan/Flowspan.Desktop")]
    public void ApprovedTargetsFreezeArchiveAndEntryPoint(
        string rid,
        string extension,
        string entryPoint)
    {
        ReleaseContext context = Create(rid);

        Assert.Equal(extension, context.Target.ArchiveExtension);
        Assert.Equal(entryPoint, context.Target.EntryPoint);
        Assert.Contains("unsigned-test", context.GetPackageStem(
            SignatureStates.UnsignedTestArtifact));
    }

    [Theory]
    [InlineData("linux-arm64")]
    [InlineData("win-x86")]
    [InlineData("osx-x64")]
    public void UndeclaredTargetsAreRejected(string rid)
    {
        Assert.Throws<ReleaseInputException>(() => Create(rid));
    }

    [Theory]
    [InlineData("../1.0")]
    [InlineData("1.0+secret")]
    [InlineData(".1.0")]
    public void UnsafeVersionsAreRejected(string version)
    {
        Assert.Throws<ReleaseInputException>(() => Create(
            "win-x64",
            version));
    }

    [Fact]
    public void MacPrereleaseUsesNumericDisplayVersion()
    {
        ReleaseContext context = Create(
            "osx-arm64",
            "1.0.0-preview.1");

        Assert.Equal("1.0.0-preview.1", context.Version);
        Assert.Equal("1.0.0", context.DisplayVersion);
    }

    [Theory]
    [InlineData("10000.0.0")]
    [InlineData("1.100.0")]
    [InlineData("1.0.100")]
    [InlineData("1.0.0.1")]
    public void MacBuildVersionMustFitAppleComponentBounds(string buildVersion)
    {
        Assert.Throws<ReleaseInputException>(() => Create(
            "osx-arm64",
            buildVersion: buildVersion));
    }

    [Fact]
    public void NonCanonicalCommitAndNonHttpsRepositoryAreRejected()
    {
        Assert.Throws<ReleaseInputException>(() => ReleaseContext.Create(
            "1.0.0",
            "1.0.0",
            new string('A', 40),
            "https://github.com/example/flowspan",
            "win-x64",
            1785196800,
            "stable",
            "1.0.0",
            "https://downloads.example.test/",
            "builder",
            "invocation"));
        Assert.Throws<ReleaseInputException>(() => ReleaseContext.Create(
            "1.0.0",
            "1.0.0",
            new string('a', 40),
            "http://github.com/example/flowspan",
            "win-x64",
            1785196800,
            "stable",
            "1.0.0",
            "https://downloads.example.test/",
            "builder",
            "invocation"));
    }

    private static ReleaseContext Create(
        string rid,
        string version = "1.0.0",
        string buildVersion = "1.0.0") => ReleaseContext.Create(
            version,
            buildVersion,
        new string('a', 40),
        "https://github.com/example/flowspan",
        rid,
        1785196800,
        "stable",
        "1.0.0",
        "https://downloads.example.test/",
        "builder",
        "invocation");
}
