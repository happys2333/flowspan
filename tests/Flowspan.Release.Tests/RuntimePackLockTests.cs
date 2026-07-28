using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class RuntimePackLockTests
{
    [Fact]
    public void CommittedRuntimePackLockIsCanonicalJson()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "runtime-packages.lock.json");
        byte[] actual = File.ReadAllBytes(path);
        JsonNode node = JsonNode.Parse(actual)!;
        byte[] expected = CanonicalJson.Encode(node);

        Assert.Equal(
            Encoding.UTF8.GetString(expected),
            Encoding.UTF8.GetString(actual));
    }

    [Fact]
    public void CommittedRuntimePackLockCoversHostAndRuntimeForEveryTarget()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "runtime-packages.lock.json");
        JsonObject value = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        JsonObject[] packages = CanonicalJson.ReadArray(value, "packages")
            .OfType<JsonObject>()
            .ToArray();
        string version = XDocument.Load(Path.Combine(
                AppContext.BaseDirectory,
                "Source.Directory.Build.props"))
            .Descendants("FlowspanRuntimePackVersion")
            .Single()
            .Value;
        Assert.Equal(6, packages.Length);
        foreach (string rid in new[] { "linux-x64", "osx-arm64", "win-x64" })
        {
            JsonObject[] selected = packages.Where(package =>
                CanonicalJson.ReadString(package, "rid") == rid).ToArray();
            Assert.Equal(2, selected.Length);
            Assert.Contains(selected, package => CanonicalJson.ReadString(
                package,
                "id") == $"Microsoft.NETCore.App.Host.{rid}");
            Assert.Contains(selected, package => CanonicalJson.ReadString(
                package,
                "id") == $"Microsoft.NETCore.App.Runtime.{rid}");
            Assert.All(selected, package =>
            {
                Assert.Equal(version, CanonicalJson.ReadString(package, "version"));
                Assert.NotEqual(
                    Convert.ToBase64String(new byte[64]),
                    CanonicalJson.ReadString(package, "contentHash"));
            });
        }
    }
}
