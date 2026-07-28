using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

internal sealed class ReleaseTestFixture : IDisposable
{
    private string packageContentHash = string.Empty;
    private string hostContentHash = string.Empty;
    private string runtimeContentHash = string.Empty;

    public ReleaseTestFixture(string rid, string licenseExpression = "MIT")
    {
        Target = ReleaseTarget.Parse(rid);
        Root = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-release-tests-{Guid.NewGuid():N}");
        PublishDirectory = Path.Combine(Root, "publish");
        StageDirectory = Path.Combine(Root, "stage");
        LockFilePath = Path.Combine(Root, "packages.lock.json");
        RuntimeLockFilePath = Path.Combine(Root, "runtime-packages.lock.json");
        GlobalPackagesPath = Path.Combine(Root, "packages");
        Directory.CreateDirectory(PublishDirectory);
        string entryPoint = rid == "win-x64"
            ? "Flowspan.Desktop.exe"
            : "Flowspan.Desktop";
        File.WriteAllText(
            Path.Combine(PublishDirectory, entryPoint),
            "flowspan-release-entry\nflowspan-release-payload\n",
            new UTF8Encoding(false));
        CreatePackageCache(licenseExpression);
        CreateRuntimeLockFile();
        CreateLockFile();
        Context = ReleaseContext.Create(
            "0.1.42",
            "1.42.0",
            new string('a', 40),
            "https://github.com/example/flowspan",
            rid,
            1785196800,
            "ci",
            "0.1.0",
            "https://downloads.example.test/flowspan/",
            "https://github.com/example/flowspan/.github/workflows/ci.yml@refs/heads/main",
            "https://github.com/example/flowspan/actions/runs/42");
    }

    public string Root { get; }

    public string PublishDirectory { get; }

    public string StageDirectory { get; }

    public string LockFilePath { get; }

    public string RuntimeLockFilePath { get; }

    public string GlobalPackagesPath { get; }

    public ReleaseTarget Target { get; }

    public ReleaseContext Context { get; }

    public void Prepare() => StagePreparer.Prepare(
        PublishDirectory,
        StageDirectory,
        Context);

    public string Seal(string name)
    {
        string output = Path.Combine(Root, name);
        _ = ReleaseSealer.Seal(
            StageDirectory,
            output,
            LockFilePath,
            RuntimeLockFilePath,
            GlobalPackagesPath,
            SignatureStates.UnsignedTestArtifact);
        return output;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private void CreatePackageCache(string licenseExpression)
    {
        string packageDirectory = Path.Combine(
            GlobalPackagesPath,
            "example.package",
            "1.0.0");
        Directory.CreateDirectory(packageDirectory);
        string nuspec = string.Join('\n',
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">",
            "  <metadata>",
            "    <id>Example.Package</id>",
            "    <version>1.0.0</version>",
            "    <authors>Example</authors>",
            "    <description>Release test package.</description>",
            $"    <license type=\"expression\">{licenseExpression}</license>",
            "  </metadata>",
            "</package>",
            string.Empty);
        string nuspecPath = Path.Combine(
            packageDirectory,
            "example.package.nuspec");
        File.WriteAllText(nuspecPath, nuspec, new UTF8Encoding(false));
        string archivePath = Path.Combine(
            packageDirectory,
            "example.package.1.0.0.nupkg");
        using (FileStream stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("Example.Package.nuspec");
            using StreamWriter writer = new(
                entry.Open(),
                new UTF8Encoding(false));
            writer.Write(nuspec);
        }

        using FileStream package = File.OpenRead(archivePath);
        packageContentHash = Convert.ToBase64String(SHA512.HashData(package));
        File.WriteAllText(
            archivePath + ".sha512",
            packageContentHash,
            new UTF8Encoding(false));
        hostContentHash = CreateFrameworkPackageCache("Host");
        runtimeContentHash = CreateFrameworkPackageCache("Runtime");
    }

    private string CreateFrameworkPackageCache(string kind)
    {
        string id = $"Microsoft.NETCore.App.{kind}.{Target.Rid}";
        const string version = "10.0.10";
        string normalizedId = id.ToLowerInvariant();
        string packageDirectory = Path.Combine(
            GlobalPackagesPath,
            normalizedId,
            version);
        Directory.CreateDirectory(packageDirectory);
        string nuspec = string.Join('\n',
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">",
            "  <metadata>",
            $"    <id>{id}</id>",
            $"    <version>{version}</version>",
            "    <authors>Microsoft</authors>",
            "    <description>Runtime test package.</description>",
            "    <license type=\"expression\">MIT</license>",
            "  </metadata>",
            "</package>",
            string.Empty);
        string archivePath = Path.Combine(
            packageDirectory,
            $"{normalizedId}.{version}.nupkg");
        using (FileStream stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry($"{id}.nuspec");
            using StreamWriter writer = new(
                entry.Open(),
                new UTF8Encoding(false));
            writer.Write(nuspec);
        }

        using FileStream package = File.OpenRead(archivePath);
        string contentHash = Convert.ToBase64String(SHA512.HashData(package));
        File.WriteAllText(
            archivePath + ".sha512",
            contentHash,
            new UTF8Encoding(false));
        return contentHash;
    }

    private void CreateRuntimeLockFile()
    {
        var packages = new JsonArray();
        foreach (string rid in new[] { "linux-x64", "osx-arm64", "win-x64" })
        {
            foreach (string kind in new[] { "Host", "Runtime" })
            {
                bool isSelected = StringComparer.Ordinal.Equals(rid, Target.Rid);
                packages.Add(new JsonObject
                {
                    ["rid"] = rid,
                    ["id"] = $"Microsoft.NETCore.App.{kind}.{rid}",
                    ["version"] = "10.0.10",
                    ["contentHash"] = isSelected
                        ? kind == "Host" ? hostContentHash : runtimeContentHash
                        : Convert.ToBase64String(new byte[64]),
                });
            }
        }

        File.WriteAllBytes(
            RuntimeLockFilePath,
            CanonicalJson.Encode(new JsonObject
            {
                ["schema"] = "flowspan.runtime-packages/v1",
                ["packages"] = packages,
            }));
    }

    private void CreateLockFile()
    {
        var dependencies = new JsonObject
        {
            ["net10.0"] = new JsonObject
            {
                ["flowspan.domain"] = new JsonObject
                {
                    ["type"] = "Project",
                },
                ["Example.Package"] = new JsonObject
                {
                    ["type"] = "Direct",
                    ["requested"] = "[1.0.0, )",
                    ["resolved"] = "1.0.0",
                    ["contentHash"] = packageContentHash,
                },
            },
            [$"net10.0/{Target.Rid}"] = new JsonObject(),
        };
        byte[] lockFile = CanonicalJson.Encode(new JsonObject
        {
            ["version"] = 2,
            ["dependencies"] = dependencies,
        });
        File.WriteAllBytes(LockFilePath, lockFile);
    }
}
