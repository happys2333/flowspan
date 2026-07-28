using System.Globalization;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public static partial class ReleaseVerifier
{
    private static void VerifySpdxCreationInfo(
        JsonObject value,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(value, "created", "creators");
        JsonArray creators = CanonicalJson.ReadArray(value, "creators");
        string expectedTime = context.SourceTimestamp.ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "created"),
                expectedTime)
            || creators.Count != 1
            || creators[0]?.GetValue<string>() != "Tool: Flowspan.Release/1")
        {
            throw new ReleaseInputException(
                "The release SPDX creation information is inconsistent.");
        }
    }

    private static void VerifySpdxApplication(
        JsonObject value,
        UpdateEvidence expected,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(
            value,
            "name",
            "SPDXID",
            "versionInfo",
            "downloadLocation",
            "filesAnalyzed",
            "licenseConcluded",
            "licenseDeclared",
            "copyrightText",
            "checksums",
            "externalRefs",
            "comment");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "name"),
                "Flowspan")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "SPDXID"),
                "SPDXRef-Package-Flowspan")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "versionInfo"),
                context.Version)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "downloadLocation"),
                expected.Url)
            || CanonicalJson.ReadBoolean(value, "filesAnalyzed")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "licenseConcluded"),
                "NOASSERTION")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "licenseDeclared"),
                "NOASSERTION")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "copyrightText"),
                "NOASSERTION")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "comment"),
                $"archiveBytes={expected.ArchiveLength}"))
        {
            throw new ReleaseInputException(
                "The release SPDX application package is inconsistent.");
        }

        VerifySpdxChecksum(value, expected.ArchiveSha256);
        VerifySpdxPurl(
            value,
            $"pkg:generic/flowspan@{context.Version}?rid={context.Target.Rid}");
    }

    private static void VerifySpdxDependency(
        JsonObject value,
        LicenseEvidence expected,
        int index)
    {
        CanonicalJson.RequireProperties(
            value,
            "name",
            "SPDXID",
            "versionInfo",
            "downloadLocation",
            "filesAnalyzed",
            "licenseConcluded",
            "licenseDeclared",
            "copyrightText",
            "checksums",
            "externalRefs",
            "comment");
        string expectedLicense = expected.LicenseKind == "expression"
            ? expected.LicenseExpression!
            : "NOASSERTION";
        string expectedComment = expected.Direct
            ? "NuGet direct dependency"
            : "NuGet transitive or RID-specific dependency";
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "name"),
                expected.Id)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "SPDXID"),
                $"SPDXRef-Package-NuGet-{index}")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "versionInfo"),
                expected.Version)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "downloadLocation"),
                $"https://www.nuget.org/packages/{Uri.EscapeDataString(expected.Id)}/{Uri.EscapeDataString(expected.Version)}")
            || CanonicalJson.ReadBoolean(value, "filesAnalyzed")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "licenseConcluded"),
                "NOASSERTION")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "licenseDeclared"),
                expectedLicense)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "copyrightText"),
                "NOASSERTION")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "comment"),
                expectedComment))
        {
            throw new ReleaseInputException(
                "A release SPDX dependency package is inconsistent.");
        }

        VerifySpdxChecksum(value, expected.ArchiveSha256);
        VerifySpdxPurl(
            value,
            $"pkg:nuget/{Uri.EscapeDataString(expected.Id)}@{Uri.EscapeDataString(expected.Version)}");
    }

    private static void VerifySpdxChecksum(JsonObject value, string expected)
    {
        JsonArray checksums = CanonicalJson.ReadArray(value, "checksums");
        if (checksums.Count != 1 || checksums[0] is not JsonObject checksum)
        {
            throw new ReleaseInputException(
                "A release SPDX package checksum is missing.");
        }

        CanonicalJson.RequireProperties(
            checksum,
            "algorithm",
            "checksumValue");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(checksum, "algorithm"),
                "SHA256")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(checksum, "checksumValue"),
                expected))
        {
            throw new ReleaseInputException(
                "A release SPDX package checksum is inconsistent.");
        }
    }

    private static void VerifySpdxPurl(JsonObject value, string expected)
    {
        JsonArray references = CanonicalJson.ReadArray(value, "externalRefs");
        if (references.Count != 1 || references[0] is not JsonObject reference)
        {
            throw new ReleaseInputException(
                "A release SPDX package purl is missing.");
        }

        CanonicalJson.RequireProperties(
            reference,
            "referenceCategory",
            "referenceType",
            "referenceLocator");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(reference, "referenceCategory"),
                "PACKAGE-MANAGER")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(reference, "referenceType"),
                "purl")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(reference, "referenceLocator"),
                expected))
        {
            throw new ReleaseInputException(
                "A release SPDX package purl is inconsistent.");
        }
    }

    private static void VerifySpdxRelationships(
        JsonArray values,
        int dependencyCount)
    {
        if (values.Count != dependencyCount + 1)
        {
            throw new ReleaseInputException(
                "The release SPDX relationship count is inconsistent.");
        }

        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] is not JsonObject relationship)
            {
                throw new ReleaseInputException(
                    "A release SPDX relationship is not an object.");
            }

            VerifySpdxRelationship(relationship, index);
        }
    }

    private static void VerifySpdxRelationship(JsonObject value, int index)
    {
        CanonicalJson.RequireProperties(
            value,
            "spdxElementId",
            "relationshipType",
            "relatedSpdxElement");
        string expectedSource = index == 0
            ? "SPDXRef-DOCUMENT"
            : "SPDXRef-Package-Flowspan";
        string expectedType = index == 0 ? "DESCRIBES" : "DEPENDS_ON";
        string expectedTarget = index == 0
            ? "SPDXRef-Package-Flowspan"
            : $"SPDXRef-Package-NuGet-{index}";
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "spdxElementId"),
                expectedSource)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "relationshipType"),
                expectedType)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "relatedSpdxElement"),
                expectedTarget))
        {
            throw new ReleaseInputException(
                "A release SPDX relationship is inconsistent.");
        }
    }
}
