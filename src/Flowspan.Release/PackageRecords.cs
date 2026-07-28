using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public sealed record PackageRecordSet(
    string ArchiveFileName,
    string SpdxFileName,
    string LicenseFileName,
    string ProvenanceFileName,
    string UpdateFileName,
    string ChecksumsFileName);

public static class PackageRecords
{
    public static PackageRecordSet Write(
        string outputDirectory,
        string archiveFileName,
        ReleaseContext context,
        string signatureState,
        IReadOnlyList<NuGetDependency> dependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFileName);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dependencies);
        string outputPath = Path.GetFullPath(outputDirectory);
        string archivePath = Path.Combine(outputPath, archiveFileName);
        var archive = new FileInfo(archivePath);
        if (!archive.Exists || archive.Length is 0 or > ReleaseBounds.MaximumPackageBytes)
        {
            throw new ReleaseInputException(
                "The release archive is absent, empty, or oversized.");
        }

        string stem = context.GetPackageStem(signatureState);
        string spdxName = stem + ".spdx.json";
        string licenseName = stem + ".licenses.json";
        string provenanceName = stem + ".provenance.json";
        string updateName = stem + ".update.json";
        string archiveSha256 = ReleaseHash.Sha256File(archivePath);
        WriteNew(Path.Combine(outputPath, spdxName), CreateSpdx(
            archiveFileName,
            archiveSha256,
            archive.Length,
            context,
            signatureState,
            dependencies));
        WriteNew(Path.Combine(outputPath, licenseName), CreateLicenses(
            archiveFileName, archiveSha256, context, dependencies));
        WriteNew(Path.Combine(outputPath, provenanceName), CreateProvenance(
            archiveFileName, archiveSha256, context, signatureState));
        WriteNew(Path.Combine(outputPath, updateName), CreateUpdate(
            archiveFileName,
            archiveSha256,
            archive.Length,
            context,
            signatureState));

        string[] coveredNames =
        [
            archiveFileName,
            spdxName,
            licenseName,
            provenanceName,
            updateName,
        ];
        var checksums = new StringBuilder();
        foreach (string name in coveredNames.Order(StringComparer.Ordinal))
        {
            checksums.Append(ReleaseHash.Sha256File(Path.Combine(outputPath, name)))
                .Append("  ")
                .Append(name)
                .Append('\n');
        }

        const string checksumsName = "SHA256SUMS";
        WriteNew(
            Path.Combine(outputPath, checksumsName),
            Encoding.UTF8.GetBytes(checksums.ToString()));
        return new PackageRecordSet(
            archiveFileName,
            spdxName,
            licenseName,
            provenanceName,
            updateName,
            checksumsName);
    }

    private static byte[] CreateSpdx(
        string archiveName,
        string archiveSha256,
        long archiveLength,
        ReleaseContext context,
        string signatureState,
        IReadOnlyList<NuGetDependency> dependencies)
    {
        var packages = new JsonArray(CreateApplicationPackage(
            archiveName,
            archiveSha256,
            archiveLength,
            context));
        var relationships = new JsonArray(new JsonObject
        {
            ["spdxElementId"] = "SPDXRef-DOCUMENT",
            ["relationshipType"] = "DESCRIBES",
            ["relatedSpdxElement"] = "SPDXRef-Package-Flowspan",
        });
        for (int index = 0; index < dependencies.Count; index++)
        {
            NuGetDependency package = dependencies[index];
            string spdxId = $"SPDXRef-Package-NuGet-{index + 1}";
            packages.Add(CreateDependencyPackage(package, spdxId));
            relationships.Add(new JsonObject
            {
                ["spdxElementId"] = "SPDXRef-Package-Flowspan",
                ["relationshipType"] = "DEPENDS_ON",
                ["relatedSpdxElement"] = spdxId,
            });
        }

        string namespaceValue = context.Repository.AbsoluteUri.TrimEnd('/')
            + $"/spdx/{context.Commit}/{context.Target.Rid}/{context.Version}";
        return CanonicalJson.Encode(new JsonObject
        {
            ["spdxVersion"] = "SPDX-2.3",
            ["dataLicense"] = "CC0-1.0",
            ["SPDXID"] = "SPDXRef-DOCUMENT",
            ["name"] = context.GetPackageStem(signatureState),
            ["documentNamespace"] = namespaceValue,
            ["creationInfo"] = new JsonObject
            {
                ["created"] = FormatTimestamp(context.SourceTimestamp),
                ["creators"] = new JsonArray("Tool: Flowspan.Release/1"),
            },
            ["packages"] = packages,
            ["relationships"] = relationships,
        });
    }

    private static JsonObject CreateApplicationPackage(
        string archiveName,
        string archiveSha256,
        long archiveLength,
        ReleaseContext context) => new()
        {
            ["name"] = "Flowspan",
            ["SPDXID"] = "SPDXRef-Package-Flowspan",
            ["versionInfo"] = context.Version,
            ["downloadLocation"] = GetDownloadUri(context, archiveName),
            ["filesAnalyzed"] = false,
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "NOASSERTION",
            ["copyrightText"] = "NOASSERTION",
            ["checksums"] = new JsonArray(new JsonObject
            {
                ["algorithm"] = "SHA256",
                ["checksumValue"] = archiveSha256,
            }),
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "PACKAGE-MANAGER",
                ["referenceType"] = "purl",
                ["referenceLocator"] = $"pkg:generic/flowspan@{context.Version}?rid={context.Target.Rid}",
            }),
            ["comment"] = $"archiveBytes={archiveLength}",
        };

    private static JsonObject CreateDependencyPackage(
        NuGetDependency package,
        string spdxId) => new()
        {
            ["name"] = package.Id,
            ["SPDXID"] = spdxId,
            ["versionInfo"] = package.Version,
            ["downloadLocation"] = $"https://www.nuget.org/packages/{Uri.EscapeDataString(package.Id)}/{Uri.EscapeDataString(package.Version)}",
            ["filesAnalyzed"] = false,
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = package.License.Kind == "expression"
                ? package.License.Expression
                : "NOASSERTION",
            ["copyrightText"] = "NOASSERTION",
            ["checksums"] = new JsonArray(new JsonObject
            {
                ["algorithm"] = "SHA256",
                ["checksumValue"] = package.ArchiveSha256,
            }),
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "PACKAGE-MANAGER",
                ["referenceType"] = "purl",
                ["referenceLocator"] = $"pkg:nuget/{Uri.EscapeDataString(package.Id)}@{Uri.EscapeDataString(package.Version)}",
            }),
            ["comment"] = package.IsDirect
                ? "NuGet direct dependency"
                : "NuGet transitive or RID-specific dependency",
        };

    private static byte[] CreateLicenses(
        string archiveName,
        string archiveSha256,
        ReleaseContext context,
        IReadOnlyList<NuGetDependency> dependencies)
    {
        var packages = new JsonArray();
        foreach (NuGetDependency package in dependencies)
        {
            packages.Add(new JsonObject
            {
                ["id"] = package.Id,
                ["version"] = package.Version,
                ["direct"] = package.IsDirect,
                ["lockContentHash"] = package.LockContentHash,
                ["archiveSha256"] = package.ArchiveSha256,
                ["licenseKind"] = package.License.Kind,
                ["licenseExpression"] = package.License.Expression,
                ["licenseUrl"] = package.License.Url,
                ["licenseFile"] = package.License.File,
                ["licenseFileSha256"] = package.License.FileSha256,
                ["reviewStatus"] = package.License.ReviewStatus,
            });
        }

        return CanonicalJson.Encode(new JsonObject
        {
            ["schema"] = "flowspan.licenses/v1",
            ["archive"] = archiveName,
            ["archiveSha256"] = archiveSha256,
            ["version"] = context.Version,
            ["commit"] = context.Commit,
            ["rid"] = context.Target.Rid,
            ["application"] = new JsonObject
            {
                ["id"] = "Flowspan",
                ["version"] = context.Version,
                ["archiveSha256"] = archiveSha256,
                ["licenseExpression"] = null,
                ["reviewStatus"] = "application-license-review-required",
            },
            ["packageCount"] = dependencies.Count + 1,
            ["reviewRequired"] = true,
            ["packages"] = packages,
        });
    }

    private static byte[] CreateProvenance(
        string archiveName,
        string archiveSha256,
        ReleaseContext context,
        string signatureState) => CanonicalJson.Encode(new JsonObject
        {
            ["_type"] = "https://in-toto.io/Statement/v1",
            ["subject"] = new JsonArray(new JsonObject
            {
                ["name"] = archiveName,
                ["digest"] = new JsonObject
                {
                    ["sha256"] = archiveSha256,
                },
            }),
            ["predicateType"] = "https://slsa.dev/provenance/v1",
            ["predicate"] = new JsonObject
            {
                ["buildDefinition"] = new JsonObject
                {
                    ["buildType"] = "https://flowspan.io/build-types/dotnet-desktop/v1",
                    ["externalParameters"] = new JsonObject
                    {
                        ["version"] = context.Version,
                        ["rid"] = context.Target.Rid,
                        ["channel"] = context.Channel,
                        ["signatureState"] = signatureState,
                        ["sourceDateEpoch"] = context.SourceTimestamp.ToUnixTimeSeconds(),
                    },
                    ["internalParameters"] = new JsonObject(),
                    ["resolvedDependencies"] = new JsonArray(new JsonObject
                    {
                        ["uri"] = $"git+{context.Repository.AbsoluteUri}@{context.Commit}",
                        ["digest"] = new JsonObject
                        {
                            ["gitCommit"] = context.Commit,
                        },
                    }),
                },
                ["runDetails"] = new JsonObject
                {
                    ["builder"] = new JsonObject
                    {
                        ["id"] = context.BuilderId,
                    },
                    ["metadata"] = new JsonObject
                    {
                        ["invocationId"] = context.InvocationId,
                        ["startedOn"] = FormatTimestamp(context.SourceTimestamp),
                        ["finishedOn"] = FormatTimestamp(context.SourceTimestamp),
                    },
                },
            },
        });

    private static byte[] CreateUpdate(
        string archiveName,
        string archiveSha256,
        long archiveLength,
        ReleaseContext context,
        string signatureState) => CanonicalJson.Encode(new JsonObject
        {
            ["schema"] = "flowspan.update/v1",
            ["channel"] = context.Channel,
            ["version"] = context.Version,
            ["minimumSupportedVersion"] = context.MinimumVersion,
            ["publishedAt"] = FormatTimestamp(context.SourceTimestamp),
            ["commit"] = context.Commit,
            ["packages"] = new JsonArray(new JsonObject
            {
                ["rid"] = context.Target.Rid,
                ["url"] = GetDownloadUri(context, archiveName),
                ["size"] = archiveLength,
                ["sha256"] = archiveSha256,
                ["signatureState"] = signatureState,
            }),
        });

    private static string GetDownloadUri(
        ReleaseContext context,
        string fileName) =>
        context.DownloadBase.AbsoluteUri.TrimEnd('/')
        + '/'
        + Uri.EscapeDataString(fileName);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void WriteNew(string path, byte[] content)
    {
        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }
}
