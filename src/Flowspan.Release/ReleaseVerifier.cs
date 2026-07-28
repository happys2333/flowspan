using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public static partial class ReleaseVerifier
{
    public static void VerifyDirectory(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string outputPath = Path.GetFullPath(outputDirectory);
        IReadOnlyList<ReleaseTreeFile> files = ReleaseTree.EnumerateFiles(outputPath);
        if (files.Any(static file => file.RelativePath.Contains('/')))
        {
            throw new ReleaseInputException(
                "Release companion records must be top-level files.");
        }

        ReleaseTreeFile checksumsFile = files.SingleOrDefault(file =>
            StringComparer.Ordinal.Equals(file.RelativePath, "SHA256SUMS"))
            ?? throw new ReleaseInputException(
                "The release checksum file is missing.");
        IReadOnlyDictionary<string, string> checksums = ParseChecksums(
            File.ReadAllBytes(checksumsFile.FullPath));
        string[] expectedNames = checksums.Keys
            .Append("SHA256SUMS")
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualNames = files.Select(static file => file.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new ReleaseInputException(
                "The release directory contains missing, unlisted, or extra files.");
        }

        foreach ((string name, string expectedHash) in checksums)
        {
            if (!StringComparer.Ordinal.Equals(
                ReleaseHash.Sha256File(Path.Combine(outputPath, name)),
                expectedHash))
            {
                throw new ReleaseInputException(
                    "A release companion checksum does not match.");
            }
        }

        ReleaseTreeFile archive = files.SingleOrDefault(static file =>
            file.RelativePath.EndsWith(".zip", StringComparison.Ordinal)
            || file.RelativePath.EndsWith(".tar.gz", StringComparison.Ordinal))
            ?? throw new ReleaseInputException(
                "The release directory must contain one archive.");
        JsonObject update = ReadJsonBySuffix(files, ".update.json");
        UpdateEvidence expected = ReadUpdate(update);
        if (!StringComparer.Ordinal.Equals(expected.ArchiveName, archive.RelativePath)
            || expected.ArchiveLength != archive.Length
            || !StringComparer.Ordinal.Equals(
                expected.ArchiveSha256,
                ReleaseHash.Sha256File(archive.FullPath)))
        {
            throw new ReleaseInputException(
                "The update record does not bind the release archive.");
        }

        ArchiveEvidence actual = ArchiveVerifier.Verify(
            archive.FullPath,
            expected.SignatureState);
        if (!StringComparer.Ordinal.Equals(actual.Context.Version, expected.Version)
            || !StringComparer.Ordinal.Equals(actual.Context.Commit, expected.Commit)
            || !StringComparer.Ordinal.Equals(actual.Context.Target.Rid, expected.Rid)
            || !StringComparer.Ordinal.Equals(
                actual.SignatureState,
                expected.SignatureState)
            || !MatchesUpdateContext(expected, actual.Context))
        {
            throw new ReleaseInputException(
                "The update record and package manifest disagree.");
        }

        List<LicenseEvidence> licenses = VerifyLicenseRecord(
            ReadJsonBySuffix(files, ".licenses.json"),
            expected);
        VerifySpdxRecord(
            ReadJsonBySuffix(files, ".spdx.json"),
            expected,
            actual.Context,
            licenses);
        VerifyProvenanceRecord(
            ReadJsonBySuffix(files, ".provenance.json"),
            expected,
            actual.Context);
    }

    private static SortedDictionary<string, string> ParseChecksums(
        ReadOnlySpan<byte> encoded)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(encoded);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ReleaseInputException(
                "The release checksum file is not UTF-8.",
                exception);
        }

        if (!text.EndsWith('\n') || text.Contains('\r'))
        {
            throw new ReleaseInputException(
                "The release checksum file is not canonical LF text.");
        }

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 5)
        {
            throw new ReleaseInputException(
                "The release checksum file must cover five files.");
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            if (line.Length < 67 || line[64..66] != "  ")
            {
                throw new ReleaseInputException(
                    "A release checksum line is malformed.");
            }

            string hash = line[..64];
            string name = line[66..];
            if (!ReleaseHash.IsLowerSha256(hash)
                || name.Length is 0 or > ReleaseBounds.MaximumRelativePathLength
                || name.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not '.' and not '-' and not '_')
                || !result.TryAdd(name, hash))
            {
                throw new ReleaseInputException(
                    "A release checksum name or digest is invalid.");
            }
        }

        string canonical = string.Concat(result.Select(static pair =>
            $"{pair.Value}  {pair.Key}\n"));
        if (!StringComparer.Ordinal.Equals(canonical, text))
        {
            throw new ReleaseInputException(
                "The release checksum lines are not in canonical order.");
        }

        return result;
    }

    private static JsonObject ReadJsonBySuffix(
        IReadOnlyList<ReleaseTreeFile> files,
        string suffix)
    {
        ReleaseTreeFile file = files.SingleOrDefault(candidate =>
            candidate.RelativePath.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new ReleaseInputException(
                "A required release JSON companion record is missing.");
        return CanonicalJson.DecodeObject(File.ReadAllBytes(file.FullPath));
    }

    private static UpdateEvidence ReadUpdate(JsonObject value)
    {
        CanonicalJson.RequireProperties(
            value,
            "schema",
            "channel",
            "version",
            "minimumSupportedVersion",
            "publishedAt",
            "commit",
            "packages");
        if (!StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "schema"),
            "flowspan.update/v1"))
        {
            throw new ReleaseInputException(
                "The release update schema is unsupported.");
        }

        JsonArray packages = CanonicalJson.ReadArray(value, "packages");
        if (packages.Count != 1 || packages[0] is not JsonObject package)
        {
            throw new ReleaseInputException(
                "The release update record must contain one package.");
        }

        CanonicalJson.RequireProperties(
            package,
            "rid",
            "url",
            "size",
            "sha256",
            "signatureState");
        string urlValue = CanonicalJson.ReadString(package, "url");
        if (!Uri.TryCreate(urlValue, UriKind.Absolute, out Uri? url)
            || url.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Fragment))
        {
            throw new ReleaseInputException(
                "The release update URL is invalid.");
        }

        string archiveName = Uri.UnescapeDataString(url.Segments[^1]);
        string digest = CanonicalJson.ReadString(package, "sha256");
        string signatureState = CanonicalJson.ReadString(
            package,
            "signatureState");
        long archiveLength = CanonicalJson.ReadInt64(package, "size");
        string publishedAt = CanonicalJson.ReadString(value, "publishedAt");
        if (!ReleaseHash.IsLowerSha256(digest)
            || archiveLength is <= 0 or > ReleaseBounds.MaximumPackageBytes
            || signatureState is not SignatureStates.UnsignedTestArtifact
                and not SignatureStates.Verified
            || !TryReadTimestamp(publishedAt, out _))
        {
            throw new ReleaseInputException(
                "The release update digest or signature state is invalid.");
        }

        return new UpdateEvidence(
            archiveName,
            archiveLength,
            digest,
            CanonicalJson.ReadString(value, "version"),
            CanonicalJson.ReadString(value, "commit"),
            CanonicalJson.ReadString(package, "rid"),
            signatureState,
            CanonicalJson.ReadString(value, "channel"),
            CanonicalJson.ReadString(value, "minimumSupportedVersion"),
            publishedAt,
            url.AbsoluteUri);
    }

    private static List<LicenseEvidence> VerifyLicenseRecord(
        JsonObject value,
        UpdateEvidence expected)
    {
        CanonicalJson.RequireProperties(
            value,
            "schema",
            "archive",
            "archiveSha256",
            "version",
            "commit",
            "rid",
            "application",
            "packageCount",
            "reviewRequired",
            "packages");
        JsonArray packages = CanonicalJson.ReadArray(value, "packages");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "schema"),
                "flowspan.licenses/v1")
            || !MatchesCommonArchiveFields(value, expected)
            || CanonicalJson.ReadInt64(value, "packageCount") != packages.Count + 1)
        {
            throw new ReleaseInputException(
                "The release license record does not bind the package.");
        }

        VerifyApplicationLicense(
            CanonicalJson.ReadObject(value, "application"),
            expected);

        string? previousId = null;
        string? previousVersion = null;
        bool reviewRequired = true;
        var evidence = new List<LicenseEvidence>(packages.Count);
        foreach (JsonNode? node in packages)
        {
            if (node is not JsonObject package)
            {
                throw new ReleaseInputException(
                    "A release license package is not an object.");
            }

            CanonicalJson.RequireProperties(
                package,
                "id",
                "version",
                "direct",
                "lockContentHash",
                "archiveSha256",
                "licenseKind",
                "licenseExpression",
                "licenseUrl",
                "licenseFile",
                "licenseFileSha256",
                "reviewStatus");
            string id = CanonicalJson.ReadString(package, "id");
            string version = CanonicalJson.ReadString(package, "version");
            int idComparison = previousId is null
                ? -1
                : StringComparer.OrdinalIgnoreCase.Compare(previousId, id);
            if (idComparison > 0
                || idComparison == 0
                    && StringComparer.Ordinal.Compare(previousVersion, version) >= 0)
            {
                throw new ReleaseInputException(
                    "Release license packages are duplicated or unordered.");
            }

            previousId = id;
            previousVersion = version;
            bool direct = CanonicalJson.ReadBoolean(package, "direct");
            string lockContentHash = CanonicalJson.ReadString(
                package,
                "lockContentHash");
            string archiveSha256 = CanonicalJson.ReadString(
                package,
                "archiveSha256");
            string licenseKind = CanonicalJson.ReadString(package, "licenseKind");
            string? licenseExpression = ReadOptionalString(
                package,
                "licenseExpression");
            string? licenseUrl = ReadOptionalString(package, "licenseUrl");
            string? licenseFile = ReadOptionalString(package, "licenseFile");
            string? licenseFileSha256 = ReadOptionalString(
                package,
                "licenseFileSha256");
            string reviewStatus = CanonicalJson.ReadString(package, "reviewStatus");
            if (!IsBase64Sha512(lockContentHash)
                || !ReleaseHash.IsLowerSha256(archiveSha256)
                || !IsValidLicenseDeclaration(
                    licenseKind,
                    licenseExpression,
                    licenseUrl,
                    licenseFile,
                    licenseFileSha256,
                    reviewStatus))
            {
                throw new ReleaseInputException(
                    "A release license package declaration is invalid.");
            }

            reviewRequired |= reviewStatus
                .EndsWith("review-required", StringComparison.Ordinal);
            evidence.Add(new LicenseEvidence(
                id,
                version,
                direct,
                archiveSha256,
                licenseKind,
                licenseExpression));
        }

        if (CanonicalJson.ReadBoolean(value, "reviewRequired") != reviewRequired)
        {
            throw new ReleaseInputException(
                "The release license review summary is inconsistent.");
        }

        return evidence;
    }

    private static bool IsValidLicenseDeclaration(
        string kind,
        string? expression,
        string? url,
        string? file,
        string? fileSha256,
        string reviewStatus)
    {
        bool noFile = file is null && fileSha256 is null;
        return kind switch
        {
            "expression" => expression is not null
                && SpdxExpressionSyntax.IsValid(expression)
                && noFile
                && reviewStatus == "declared-expression",
            "file" => expression is null
                && file is not null
                && StringComparer.Ordinal.Equals(
                    ReleaseTree.NormalizeRelativePath(file),
                    file)
                && fileSha256 is not null
                && ReleaseHash.IsLowerSha256(fileSha256)
                && reviewStatus == "declared-file-review-required",
            "missing" => expression is null
                && noFile
                && (url is null
                    ? reviewStatus == "missing-review-required"
                    : reviewStatus == "legacy-url-review-required"),
            "unknown" => expression is null
                && noFile
                && reviewStatus == "unknown-declaration-review-required",
            "invalid-expression" => expression is null
                && noFile
                && reviewStatus == "invalid-expression-review-required",
            _ => false,
        };
    }

    private static void VerifyApplicationLicense(
        JsonObject value,
        UpdateEvidence expected)
    {
        CanonicalJson.RequireProperties(
            value,
            "id",
            "version",
            "archiveSha256",
            "licenseExpression",
            "reviewStatus");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "id"),
                "Flowspan")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "version"),
                expected.Version)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "archiveSha256"),
                expected.ArchiveSha256)
            || ReadOptionalString(value, "licenseExpression") is not null
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "reviewStatus"),
                "application-license-review-required"))
        {
            throw new ReleaseInputException(
                "The application license record is inconsistent.");
        }
    }

    private static void VerifySpdxRecord(
        JsonObject value,
        UpdateEvidence expected,
        ReleaseContext context,
        IReadOnlyList<LicenseEvidence> licenses)
    {
        CanonicalJson.RequireProperties(
            value,
            "spdxVersion",
            "dataLicense",
            "SPDXID",
            "name",
            "documentNamespace",
            "creationInfo",
            "packages",
            "relationships");
        string expectedNamespace = context.Repository.AbsoluteUri.TrimEnd('/')
            + $"/spdx/{context.Commit}/{context.Target.Rid}/{context.Version}";
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "spdxVersion"),
                "SPDX-2.3")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "dataLicense"),
                "CC0-1.0")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "SPDXID"),
                "SPDXRef-DOCUMENT")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "name"),
                context.GetPackageStem(expected.SignatureState))
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "documentNamespace"),
                expectedNamespace))
        {
            throw new ReleaseInputException(
                "The release SPDX header is invalid.");
        }

        VerifySpdxCreationInfo(
            CanonicalJson.ReadObject(value, "creationInfo"),
            context);
        JsonArray packages = CanonicalJson.ReadArray(value, "packages");
        if (packages.Count != licenses.Count + 1
            || packages[0] is not JsonObject application)
        {
            throw new ReleaseInputException(
                "The release SPDX document does not describe Flowspan.");
        }

        VerifySpdxApplication(application, expected, context);
        for (int index = 0; index < licenses.Count; index++)
        {
            if (packages[index + 1] is not JsonObject package)
            {
                throw new ReleaseInputException(
                    "A release SPDX package is not an object.");
            }

            VerifySpdxDependency(package, licenses[index], index + 1);
        }

        VerifySpdxRelationships(
            CanonicalJson.ReadArray(value, "relationships"),
            licenses.Count);
    }

    private static void VerifyProvenanceRecord(
        JsonObject value,
        UpdateEvidence expected,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(
            value,
            "_type",
            "subject",
            "predicateType",
            "predicate");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "_type"),
                "https://in-toto.io/Statement/v1")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "predicateType"),
                "https://slsa.dev/provenance/v1"))
        {
            throw new ReleaseInputException(
                "The release provenance statement type is invalid.");
        }

        JsonArray subjects = CanonicalJson.ReadArray(value, "subject");
        if (subjects.Count != 1 || subjects[0] is not JsonObject subject)
        {
            throw new ReleaseInputException(
                "The release provenance must contain one subject.");
        }

        VerifyProvenanceSubject(subject, expected);
        VerifyProvenancePredicate(
            CanonicalJson.ReadObject(value, "predicate"),
            expected,
            context);
    }

    private static bool MatchesCommonArchiveFields(
        JsonObject value,
        UpdateEvidence expected) =>
        StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "archive"),
            expected.ArchiveName)
        && StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "archiveSha256"),
            expected.ArchiveSha256)
        && StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "version"),
            expected.Version)
        && StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "commit"),
            expected.Commit)
        && StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "rid"),
            expected.Rid);

    private static bool MatchesUpdateContext(
        UpdateEvidence value,
        ReleaseContext context)
    {
        string expectedUrl = context.DownloadBase.AbsoluteUri.TrimEnd('/')
            + '/'
            + Uri.EscapeDataString(value.ArchiveName);
        string expectedTimestamp = context.SourceTimestamp.ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        return StringComparer.Ordinal.Equals(value.Channel, context.Channel)
            && StringComparer.Ordinal.Equals(
                value.MinimumVersion,
                context.MinimumVersion)
            && StringComparer.Ordinal.Equals(value.PublishedAt, expectedTimestamp)
            && StringComparer.Ordinal.Equals(value.Url, expectedUrl);
    }

    private static bool TryReadTimestamp(
        string value,
        out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out timestamp)
        && timestamp.Offset == TimeSpan.Zero
        && StringComparer.Ordinal.Equals(
            timestamp.ToString("O", CultureInfo.InvariantCulture),
            value);

    private static string? ReadOptionalString(JsonObject value, string property)
    {
        if (value[property] is null)
        {
            return null;
        }

        try
        {
            string result = value[property]!.GetValue<string>();
            if (string.IsNullOrWhiteSpace(result)
                || result.Length > ReleaseBounds.MaximumTextLength
                || result.Any(char.IsControl))
            {
                throw new ReleaseInputException(
                    $"The release JSON {property} is invalid.");
            }

            return result;
        }
        catch (InvalidOperationException exception)
        {
            throw new ReleaseInputException(
                $"The release JSON {property} is not a string or null.",
                exception);
        }
    }

    private static bool IsBase64Sha512(string value)
    {
        Span<byte> decoded = stackalloc byte[64];
        return value.Length <= 128
            && Convert.TryFromBase64String(value, decoded, out int bytesWritten)
            && bytesWritten == decoded.Length;
    }

    private sealed record LicenseEvidence(
        string Id,
        string Version,
        bool Direct,
        string ArchiveSha256,
        string LicenseKind,
        string? LicenseExpression);

    private sealed record UpdateEvidence(
        string ArchiveName,
        long ArchiveLength,
        string ArchiveSha256,
        string Version,
        string Commit,
        string Rid,
        string SignatureState,
        string Channel,
        string MinimumVersion,
        string PublishedAt,
        string Url);
}
