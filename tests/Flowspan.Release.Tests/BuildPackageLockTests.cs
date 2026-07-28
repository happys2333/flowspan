using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class BuildPackageLockTests
{
    [Fact]
    public void CommittedBuildPackageLockIsCanonicalAndVersionBound()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "build-packages.lock.json");
        byte[] actual = File.ReadAllBytes(path);
        JsonObject value = CanonicalJson.DecodeObject(actual);
        Assert.Equal(
            Encoding.UTF8.GetString(CanonicalJson.Encode(value)),
            Encoding.UTF8.GetString(actual));
        JsonObject package = Assert.IsType<JsonObject>(Assert.Single(
            CanonicalJson.ReadArray(value, "packages")));
        string version = XDocument.Load(Path.Combine(
                AppContext.BaseDirectory,
                "Source.Directory.Build.props"))
            .Descendants("FlowspanRuntimePackVersion")
            .Single()
            .Value;
        Assert.Equal(version, CanonicalJson.ReadString(package, "version"));
        Assert.Equal(
            "Microsoft.NET.ILLink.Tasks",
            CanonicalJson.ReadString(package, "id"));
        Assert.NotEqual(
            Convert.ToBase64String(new byte[64]),
            CanonicalJson.ReadString(package, "contentHash"));
    }

    [Fact]
    public void UnexpectedBuildPackageIdentityIsRejectedBeforeCacheAccess()
    {
        string source = Path.Combine(
            AppContext.BaseDirectory,
            "build-packages.lock.json");
        JsonObject value = CanonicalJson.DecodeObject(File.ReadAllBytes(source));
        JsonObject package = Assert.IsType<JsonObject>(Assert.Single(
            CanonicalJson.ReadArray(value, "packages")));
        package["id"] = "Unexpected.Package";
        string path = Path.Combine(Path.GetTempPath(), $"build-lock-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(path, CanonicalJson.Encode(value));
        try
        {
            Assert.Throws<ReleaseInputException>(() =>
                BuildPackageLock.Verify(path, "/unused"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
