using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NuGet.Versioning;

namespace Flowspan.Release;

public static class BuildPackageLock
{
    private const string Schema = "flowspan.build-packages/v1";
    private const string RequiredPackage = "Microsoft.NET.ILLink.Tasks";

    public static void Verify(
        string lockFilePath,
        string globalPackagesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(globalPackagesPath);
        JsonObject value = CanonicalJson.DecodeObject(
            File.ReadAllBytes(lockFilePath));
        CanonicalJson.RequireProperties(value, "schema", "packages");
        JsonArray packages = CanonicalJson.ReadArray(value, "packages");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "schema"),
                Schema)
            || packages.Count != 1
            || packages[0] is not JsonObject package)
        {
            throw new ReleaseInputException(
                "The build package lock is incomplete.");
        }

        CanonicalJson.RequireProperties(
            package,
            "id",
            "version",
            "contentHash");
        string id = CanonicalJson.ReadString(package, "id");
        string version = CanonicalJson.ReadString(package, "version");
        string contentHash = CanonicalJson.ReadString(
            package,
            "contentHash");
        ValidateEntry(id, version, contentHash);
        _ = NuGetGraph.VerifyPackageArchive(
            id,
            version,
            contentHash,
            globalPackagesPath);
    }

    private static void ValidateEntry(
        string id,
        string version,
        string contentHash)
    {
        Span<byte> decoded = stackalloc byte[SHA512.HashSizeInBytes];
        if (!StringComparer.Ordinal.Equals(id, RequiredPackage)
            || !NuGetVersion.TryParse(
                version,
                out NuGetVersion? parsedVersion)
            || !StringComparer.Ordinal.Equals(
                parsedVersion.ToNormalizedString(),
                version)
            || contentHash.Length > 128
            || !Convert.TryFromBase64String(
                contentHash,
                decoded,
                out int bytesWritten)
            || bytesWritten != decoded.Length)
        {
            throw new ReleaseInputException(
                "The build package lock entry is invalid.");
        }
    }
}
