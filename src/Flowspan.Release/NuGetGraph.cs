using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Flowspan.Release;

public sealed record NuGetLicense(
    string Kind,
    string? Expression,
    string? Url,
    string? File,
    string? FileSha256,
    string ReviewStatus);

public sealed record NuGetDependency(
    string Id,
    string Version,
    bool IsDirect,
    string LockContentHash,
    string ArchiveSha256,
    NuGetLicense License);

public static class NuGetGraph
{
    public static IReadOnlyList<NuGetDependency> Read(
        string lockFilePath,
        string runtimeLockFilePath,
        string globalPackagesPath,
        ReleaseContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeLockFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(globalPackagesPath);
        ArgumentNullException.ThrowIfNull(context);
        using FileStream stream = File.OpenRead(lockFilePath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        JsonElement dependencies = document.RootElement.GetProperty("dependencies");
        var graph = new Dictionary<string, LockDependency>(StringComparer.OrdinalIgnoreCase);
        AddSection(dependencies, "net10.0", graph);
        AddSection(dependencies, $"net10.0/{context.Target.Rid}", graph);
        AddRuntimePackage(runtimeLockFilePath, context, graph);

        return graph.Values
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .Select(package => ReadPackage(package, globalPackagesPath))
            .ToArray();
    }

    private static void AddSection(
        JsonElement dependencies,
        string sectionName,
        Dictionary<string, LockDependency> graph)
    {
        if (!dependencies.TryGetProperty(sectionName, out JsonElement section))
        {
            throw new ReleaseInputException(
                $"The Desktop lock file is missing {sectionName}.");
        }

        foreach (JsonProperty property in section.EnumerateObject())
        {
            JsonElement value = property.Value;
            string type = value.GetProperty("type").GetString()
                ?? throw new ReleaseInputException(
                    "A locked package type is missing.");
            if (StringComparer.Ordinal.Equals(type, "Project"))
            {
                continue;
            }

            if (type is not "Direct" and not "Transitive" and not "CentralTransitive")
            {
                throw new ReleaseInputException(
                    "A locked dependency has an unsupported type.");
            }

            string version = (value.TryGetProperty("resolved", out JsonElement resolved)
                ? resolved.GetString()
                : null)
                ?? throw new ReleaseInputException(
                    "A locked package version is missing.");
            string contentHash = (value.TryGetProperty("contentHash", out JsonElement hash)
                ? hash.GetString()
                : null)
                ?? throw new ReleaseInputException(
                    "A locked package content hash is missing.");
            if (!IsCanonicalNuGetVersion(version))
            {
                throw new ReleaseInputException(
                    "A locked package version is invalid.");
            }

            string key = $"{property.Name}/{version}";
            var candidate = new LockDependency(
                property.Name,
                version,
                StringComparer.Ordinal.Equals(type, "Direct"),
                contentHash);
            if (graph.TryGetValue(key, out LockDependency? existing))
            {
                if (!StringComparer.Ordinal.Equals(
                        existing.LockContentHash,
                        candidate.LockContentHash))
                {
                    throw new ReleaseInputException(
                        "A locked package has conflicting content hashes.");
                }

                graph[key] = existing with
                {
                    IsDirect = existing.IsDirect || candidate.IsDirect,
                };
            }
            else
            {
                graph.Add(key, candidate);
            }
        }
    }

    private static void AddRuntimePackage(
        string path,
        ReleaseContext context,
        Dictionary<string, LockDependency> graph)
    {
        JsonObject value = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        CanonicalJson.RequireProperties(value, "schema", "packages");
        JsonArray packages = CanonicalJson.ReadArray(value, "packages");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "schema"),
                "flowspan.runtime-packages/v1")
            || packages.Count != 6)
        {
            throw new ReleaseInputException(
                "The runtime package lock is incomplete.");
        }

        var selected = new List<LockDependency>(2);
        string? previousKey = null;
        foreach (JsonNode? node in packages)
        {
            if (node is not JsonObject package)
            {
                throw new ReleaseInputException(
                    "A runtime package lock entry is not an object.");
            }

            CanonicalJson.RequireProperties(
                package,
                "rid",
                "id",
                "version",
                "contentHash");
            string rid = CanonicalJson.ReadString(package, "rid");
            _ = ReleaseTarget.Parse(rid);
            string id = CanonicalJson.ReadString(package, "id");
            string version = CanonicalJson.ReadString(package, "version");
            string hash = CanonicalJson.ReadString(package, "contentHash");
            string key = $"{rid}/{id}";
            bool approvedId = StringComparer.Ordinal.Equals(
                    id,
                    $"Microsoft.NETCore.App.Host.{rid}")
                || StringComparer.Ordinal.Equals(
                    id,
                    $"Microsoft.NETCore.App.Runtime.{rid}");
            if (previousKey is not null
                    && StringComparer.Ordinal.Compare(previousKey, key) >= 0
                || !approvedId
                || !IsCanonicalNuGetVersion(version)
                || !IsEncodedSha512(hash))
            {
                throw new ReleaseInputException(
                    "A runtime package lock entry is invalid or unordered.");
            }

            previousKey = key;
            if (StringComparer.Ordinal.Equals(rid, context.Target.Rid))
            {
                selected.Add(new LockDependency(
                    id,
                    version,
                    IsDirect: false,
                    hash));
            }
        }

        if (selected.Count != 2)
        {
            throw new ReleaseInputException(
                "The selected runtime package lock entry is missing or duplicated.");
        }

        foreach (LockDependency package in selected)
        {
            if (!graph.TryAdd($"{package.Id}/{package.Version}", package))
            {
                throw new ReleaseInputException(
                    "A runtime package lock entry duplicates the application graph.");
            }
        }
    }

    private static NuGetDependency ReadPackage(
        LockDependency package,
        string globalPackagesPath)
    {
        string archivePath = VerifyPackageArchive(
            package.Id,
            package.Version,
            package.LockContentHash,
            globalPackagesPath);

        return new NuGetDependency(
            package.Id,
            package.Version,
            package.IsDirect,
            package.LockContentHash,
            ReleaseHash.Sha256File(archivePath),
            ReadLicense(archivePath, package.Id, package.Version));
    }

    internal static string VerifyPackageArchive(
        string id,
        string version,
        string lockContentHash,
        string globalPackagesPath)
    {
        string normalizedId = id.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();
        string packageDirectory = Path.Combine(
            Path.GetFullPath(globalPackagesPath),
            normalizedId,
            normalizedVersion);
        string archivePath = Path.Combine(
            packageDirectory,
            $"{normalizedId}.{normalizedVersion}.nupkg");
        string sha512Path = archivePath + ".sha512";
        if (!File.Exists(archivePath)
            || !File.Exists(sha512Path))
        {
            throw new ReleaseInputException(
                "A locked NuGet package is absent from the restored global cache.");
        }

        string expectedSha512 = File.ReadAllText(sha512Path).Trim();
        byte[] actualSha512;
        using (FileStream archive = File.OpenRead(archivePath))
        {
            actualSha512 = SHA512.HashData(archive);
        }

        if (!HashesEqual(expectedSha512, actualSha512))
        {
            throw new ReleaseInputException(
                "A restored NuGet archive does not match its cache SHA-512.");
        }

        using var packageReader = new PackageArchiveReader(archivePath);
        string contentHash = packageReader.GetContentHash(
            CancellationToken.None,
            () => expectedSha512);
        if (!HashesEqual(lockContentHash, contentHash))
        {
            throw new ReleaseInputException(
                $"A restored NuGet archive does not match its lock content hash: {id}.");
        }

        return archivePath;
    }

    private static bool IsEncodedSha512(string value)
    {
        Span<byte> decoded = stackalloc byte[SHA512.HashSizeInBytes];
        return value.Length <= 128
            && Convert.TryFromBase64String(value, decoded, out int bytesWritten)
            && bytesWritten == decoded.Length;
    }

    private static bool IsCanonicalNuGetVersion(string value) =>
        NuGetVersion.TryParse(value, out NuGetVersion? version)
        && StringComparer.Ordinal.Equals(
            version.ToNormalizedString(),
            value);

    private static bool HashesEqual(string expected, string actual)
    {
        try
        {
            return HashesEqual(expected, Convert.FromBase64String(actual));
        }
        catch (FormatException exception)
        {
            throw new ReleaseInputException(
                "A NuGet SHA-512 value is malformed.",
                exception);
        }
    }

    private static bool HashesEqual(string encoded, ReadOnlySpan<byte> actual)
    {
        try
        {
            byte[] expected = Convert.FromBase64String(encoded);
            return expected.Length == SHA512.HashSizeInBytes
                && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException exception)
        {
            throw new ReleaseInputException(
                "A NuGet SHA-512 value is malformed.",
                exception);
        }
    }

    private static NuGetLicense ReadLicense(
        string archivePath,
        string expectedId,
        string expectedVersion)
    {
        using FileStream package = File.OpenRead(archivePath);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        ZipArchiveEntry[] nuspecEntries = archive.Entries.Where(static entry =>
            entry.Name.Length > 0
            && !entry.FullName.Contains('/')
            && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nuspecEntries.Length != 1
            || nuspecEntries[0].Length is 0 or > ReleaseBounds.MaximumJsonBytes)
        {
            throw new ReleaseInputException(
                "A restored NuGet archive has no bounded root nuspec.");
        }

        using Stream nuspecStream = nuspecEntries[0].Open();
        using XmlReader reader = XmlReader.Create(nuspecStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = ReleaseBounds.MaximumJsonBytes,
            XmlResolver = null,
        });
        XDocument nuspec = XDocument.Load(reader, LoadOptions.None);
        XElement metadata = nuspec.Root?.Elements().SingleOrDefault(
            static element => element.Name.LocalName == "metadata")
            ?? throw new ReleaseInputException(
                "A restored NuGet nuspec has no metadata element.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                ReadMetadataValue(metadata, "id"),
                expectedId)
            || !StringComparer.Ordinal.Equals(
                ReadMetadataValue(metadata, "version"),
                expectedVersion))
        {
            throw new ReleaseInputException(
                "A restored NuGet nuspec identity does not match its lock entry.");
        }
        XElement? license = metadata.Elements().SingleOrDefault(
            static element => element.Name.LocalName == "license");
        string? licenseUrl = metadata.Elements().SingleOrDefault(
            static element => element.Name.LocalName == "licenseUrl")?.Value;

        if (license is null)
        {
            return new NuGetLicense(
                "missing",
                null,
                EmptyToNull(licenseUrl),
                null,
                null,
                licenseUrl is null
                    ? "missing-review-required"
                    : "legacy-url-review-required");
        }

        string type = license.Attribute("type")?.Value ?? string.Empty;
        string declaration = license.Value.Trim();
        if (StringComparer.OrdinalIgnoreCase.Equals(type, "expression"))
        {
            if (!SpdxExpressionSyntax.IsValid(declaration))
            {
                return new NuGetLicense(
                    "invalid-expression",
                    null,
                    EmptyToNull(licenseUrl),
                    null,
                    null,
                    "invalid-expression-review-required");
            }

            return new NuGetLicense(
                "expression",
                declaration,
                EmptyToNull(licenseUrl),
                null,
                null,
                "declared-expression");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(type, "file"))
        {
            return new NuGetLicense(
                "unknown",
                null,
                EmptyToNull(licenseUrl),
                null,
                null,
                "unknown-declaration-review-required");
        }

        string normalizedFile = ReleaseTree.NormalizeRelativePath(declaration);
        ZipArchiveEntry[] licenseEntries = archive.Entries.Where(entry =>
            StringComparer.Ordinal.Equals(entry.FullName, normalizedFile))
            .ToArray();
        if (licenseEntries.Length != 1
            || licenseEntries[0].Name.Length == 0
            || licenseEntries[0].Length is 0 or > ReleaseBounds.MaximumJsonBytes)
        {
            throw new ReleaseInputException(
                "A NuGet file license is missing or escapes its package.");
        }

        using Stream licenseContent = licenseEntries[0].Open();
        string licenseSha256 = Convert.ToHexStringLower(
            SHA256.HashData(licenseContent));

        return new NuGetLicense(
            "file",
            null,
            EmptyToNull(licenseUrl),
            normalizedFile,
            licenseSha256,
            "declared-file-review-required");
    }

    private static string ReadMetadataValue(XElement metadata, string name)
    {
        string[] values = metadata.Elements()
            .Where(element => element.Name.LocalName == name)
            .Select(static element => element.Value.Trim())
            .ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new ReleaseInputException(
                "A restored NuGet nuspec identity field is missing or duplicated.");
        }

        return values[0];
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record LockDependency(
        string Id,
        string Version,
        bool IsDirect,
        string LockContentHash);
}
