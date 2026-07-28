using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public sealed record ArchiveEvidence(
    ReleaseContext Context,
    string SignatureState);

public static class ArchiveVerifier
{
    public static ArchiveEvidence Verify(
        string archivePath,
        string expectedSignatureState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var archiveFile = new FileInfo(Path.GetFullPath(archivePath));
        if (!archiveFile.Exists
            || archiveFile.Length is 0 or > ReleaseBounds.MaximumPackageBytes)
        {
            throw new ReleaseInputException(
                "The release archive is absent, empty, or oversized.");
        }

        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryPath);
        try
        {
            IReadOnlyDictionary<string, ArchiveEntryMetadata> metadata;
            try
            {
                metadata = archivePath.EndsWith(".zip", StringComparison.Ordinal)
                    ? ExtractZip(archivePath, temporaryPath)
                    : archivePath.EndsWith(".tar.gz", StringComparison.Ordinal)
                        ? ExtractTarGzip(archivePath, temporaryPath)
                        : throw new ReleaseInputException(
                            "The release archive extension is unsupported.");
            }
            catch (InvalidDataException exception)
            {
                throw new ReleaseInputException(
                    "The release archive compression is malformed.",
                    exception);
            }
            IReadOnlyList<ReleaseTreeFile> files =
                ReleaseTree.EnumerateFiles(temporaryPath);
            ReleaseTreeFile manifestFile = files.SingleOrDefault(file =>
                file.RelativePath.EndsWith(
                    '/' + PackageManifest.FileName,
                    StringComparison.Ordinal))
                ?? throw new ReleaseInputException(
                    "The release archive package manifest is missing.");
            ArchiveEvidence evidence = VerifyManifest(
                temporaryPath,
                manifestFile,
                files,
                metadata,
                expectedSignatureState);
            VerifyCanonicalArchive(
                archiveFile.FullName,
                temporaryPath,
                evidence.Context);
            return evidence;
        }
        finally
        {
            Directory.Delete(temporaryPath, recursive: true);
        }
    }

    private static Dictionary<string, ArchiveEntryMetadata> ExtractZip(
        string archivePath,
        string destinationRoot)
    {
        using FileStream stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: false);
        if (archive.Entries.Count is 0 or > ReleaseBounds.MaximumFileCount + 1)
        {
            throw new ReleaseInputException(
                "The release ZIP entry count is invalid.");
        }

        var metadata = new Dictionary<string, ArchiveEntryMetadata>(
            StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Name.Length == 0
                || entry.Length > ReleaseBounds.MaximumFileBytes
                || totalBytes > ReleaseBounds.MaximumPackageBytes - entry.Length)
            {
                throw new ReleaseInputException(
                    "The release ZIP contains an unsupported or oversized entry.");
            }

            string path = ReleaseTree.NormalizeRelativePath(entry.FullName);
            int unixAttributes = (entry.ExternalAttributes >> 16) & 0xFFFF;
            if (!StringComparer.Ordinal.Equals(path, entry.FullName)
                || (unixAttributes & 0xF000) != 0x8000
                || !metadata.TryAdd(path, new ArchiveEntryMetadata(
                    unixAttributes & 0x1FF,
                    entry.LastWriteTime)))
            {
                throw new ReleaseInputException(
                    "The release ZIP contains a non-regular or non-canonical entry.");
            }

            string destination = GetExtractionPath(destinationRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream input = entry.Open();
            WriteBounded(input, destination, entry.Length);
            totalBytes += entry.Length;
        }

        return metadata;
    }

    private static Dictionary<string, ArchiveEntryMetadata> ExtractTarGzip(
        string archivePath,
        string destinationRoot)
    {
        using FileStream stream = File.OpenRead(archivePath);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new TarReader(gzip, leaveOpen: false);
        var metadata = new Dictionary<string, ArchiveEntryMetadata>(
            StringComparer.Ordinal);
        long totalBytes = 0;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            if (entry is not UstarTarEntry ustar
                || entry.EntryType != TarEntryType.RegularFile
                || entry.Format != TarEntryFormat.Ustar
                || entry.DataStream is null
                || entry.Uid != 0
                || entry.Gid != 0
                || !string.IsNullOrEmpty(ustar.UserName)
                || !string.IsNullOrEmpty(ustar.GroupName)
                || entry.Length > ReleaseBounds.MaximumFileBytes
                || totalBytes > ReleaseBounds.MaximumPackageBytes - entry.Length
                || metadata.Count >= ReleaseBounds.MaximumFileCount + 1)
            {
                throw new ReleaseInputException(
                    "The release tar contains an unsupported or oversized entry.");
            }

            string path = ReleaseTree.NormalizeRelativePath(entry.Name);
            if (!StringComparer.Ordinal.Equals(path, entry.Name)
                || !metadata.TryAdd(path, new ArchiveEntryMetadata(
                    (int)entry.Mode,
                    entry.ModificationTime)))
            {
                throw new ReleaseInputException(
                    "The release tar contains a non-canonical or duplicate path.");
            }

            string destination = GetExtractionPath(destinationRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            WriteBounded(entry.DataStream, destination, entry.Length);
            totalBytes += entry.Length;
        }

        if (metadata.Count == 0)
        {
            throw new ReleaseInputException(
                "The release tar contains no files.");
        }

        return metadata;
    }

    private static ArchiveEvidence VerifyManifest(
        string extractionRoot,
        ReleaseTreeFile manifestFile,
        IReadOnlyList<ReleaseTreeFile> actualFiles,
        IReadOnlyDictionary<string, ArchiveEntryMetadata> metadata,
        string expectedSignatureState)
    {
        JsonObject value = CanonicalJson.DecodeObject(
            File.ReadAllBytes(manifestFile.FullPath));
        CanonicalJson.RequireProperties(
            value,
            "schema",
            "product",
            "version",
            "buildVersion",
            "commit",
            "repository",
            "rid",
            "sourceDateEpoch",
            "channel",
            "minimumVersion",
            "downloadBase",
            "builderId",
            "invocationId",
            "signatureState",
            "entryPoint",
            "signedTreeSha256",
            "files");
        string rid = CanonicalJson.ReadString(value, "rid");
        ReleaseTarget target = ReleaseTarget.Parse(rid);
        string signatureState = CanonicalJson.ReadString(
            value,
            "signatureState");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "schema"),
                "flowspan.package/v1")
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "product"),
                "Flowspan")
            || !StringComparer.Ordinal.Equals(signatureState, expectedSignatureState)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "entryPoint"),
                target.EntryPoint)
            || !StringComparer.Ordinal.Equals(
                manifestFile.RelativePath,
                $"{target.RootName}/{PackageManifest.FileName}"))
        {
            throw new ReleaseInputException(
                "The release package manifest header is inconsistent.");
        }

        JsonArray fileValues = CanonicalJson.ReadArray(value, "files");
        var expectedFiles = new List<PackageFileRecord>(fileValues.Count);
        string? previousPath = null;
        foreach (JsonNode? node in fileValues)
        {
            if (node is not JsonObject file)
            {
                throw new ReleaseInputException(
                    "A release package file record is not an object.");
            }

            CanonicalJson.RequireProperties(
                file,
                "path",
                "length",
                "mode",
                "sha256");
            string path = ReleaseTree.NormalizeRelativePath(
                CanonicalJson.ReadString(file, "path"));
            long length = CanonicalJson.ReadInt64(file, "length");
            long modeValue = CanonicalJson.ReadInt64(file, "mode");
            string sha256 = CanonicalJson.ReadString(file, "sha256");
            if (!path.StartsWith(target.RootName + '/', StringComparison.Ordinal)
                || previousPath is not null
                    && StringComparer.Ordinal.Compare(previousPath, path) >= 0
                || length is < 0 or > ReleaseBounds.MaximumFileBytes
                || modeValue is not 420 and not 493
                || !ReleaseHash.IsLowerSha256(sha256))
            {
                throw new ReleaseInputException(
                    "A release package file record is invalid or unordered.");
            }

            int expectedMode = StringComparer.Ordinal.Equals(path, target.EntryPoint)
                ? 493
                : 420;
            if (modeValue != expectedMode)
            {
                throw new ReleaseInputException(
                    "A release package file mode does not match its role.");
            }

            previousPath = path;
            expectedFiles.Add(new PackageFileRecord(
                path,
                length,
                expectedMode,
                sha256));
        }

        if (!expectedFiles.Any(file => StringComparer.Ordinal.Equals(
            file.Path,
            target.EntryPoint)))
        {
            throw new ReleaseInputException(
                "The release package entry point is not listed.");
        }

        string[] expectedPaths = expectedFiles.Select(static file => file.Path)
            .Append(manifestFile.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualPaths = actualFiles.Select(static file => file.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal)
            || metadata.Count != actualPaths.Length
            || metadata[manifestFile.RelativePath].Mode != 420)
        {
            throw new ReleaseInputException(
                "The release archive contains missing, extra, or mis-moded files.");
        }

        var actualByPath = actualFiles.ToDictionary(
            static file => file.RelativePath,
            StringComparer.Ordinal);
        foreach (PackageFileRecord expected in expectedFiles)
        {
            ReleaseTreeFile actual = actualByPath[expected.Path];
            if (actual.Length != expected.Length
                || metadata[expected.Path].Mode != expected.Mode
                || !StringComparer.Ordinal.Equals(
                    ReleaseHash.Sha256File(actual.FullPath),
                    expected.Sha256))
            {
                throw new ReleaseInputException(
                    "A release archive file does not match its manifest.");
            }
        }

        string reportPath = $"{target.RootName}/{PackageManifest.SignatureReportFileName}";
        JsonArray signedFiles = CreateFilesArray(expectedFiles.Where(file =>
            !StringComparer.Ordinal.Equals(file.Path, reportPath)));
        string treeSha256 = ReleaseHash.Sha256Bytes(CanonicalJson.Encode(signedFiles));
        if (!StringComparer.Ordinal.Equals(
            treeSha256,
            CanonicalJson.ReadString(value, "signedTreeSha256")))
        {
            throw new ReleaseInputException(
                "The release signed-tree digest does not match the manifest.");
        }

        ReleaseContext context = ReleaseContext.Create(
            CanonicalJson.ReadString(value, "version"),
            CanonicalJson.ReadString(value, "buildVersion"),
            CanonicalJson.ReadString(value, "commit"),
            CanonicalJson.ReadString(value, "repository"),
            rid,
            CanonicalJson.ReadInt64(value, "sourceDateEpoch"),
            CanonicalJson.ReadString(value, "channel"),
            CanonicalJson.ReadString(value, "minimumVersion"),
            CanonicalJson.ReadString(value, "downloadBase"),
            CanonicalJson.ReadString(value, "builderId"),
            CanonicalJson.ReadString(value, "invocationId"));
        if (metadata.Values.Any(entry => !HasExpectedTimestamp(
            entry.ModificationTime,
            context.SourceTimestamp,
            target.UsesZip)))
        {
            throw new ReleaseInputException(
                "A release archive timestamp does not match its manifest.");
        }

        bool hasReport = actualByPath.ContainsKey(reportPath);
        if (signatureState == SignatureStates.Verified)
        {
            if (!hasReport)
            {
                throw new ReleaseInputException(
                    "A verified release archive has no signature report.");
            }

            SignatureReport.Verify(
                actualByPath[reportPath].FullPath,
                context,
                treeSha256);
        }
        else if (signatureState != SignatureStates.UnsignedTestArtifact || hasReport)
        {
            throw new ReleaseInputException(
                "The release archive signature state is inconsistent.");
        }

        return new ArchiveEvidence(context, signatureState);
    }

    private static JsonArray CreateFilesArray(
        IEnumerable<PackageFileRecord> files)
    {
        var result = new JsonArray();
        foreach (PackageFileRecord file in files.OrderBy(
            static file => file.Path,
            StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["path"] = file.Path,
                ["length"] = file.Length,
                ["mode"] = file.Mode,
                ["sha256"] = file.Sha256,
            });
        }

        return result;
    }

    private static void VerifyCanonicalArchive(
        string archivePath,
        string extractionRoot,
        ReleaseContext context)
    {
        string canonicalPath = Path.Combine(
            extractionRoot,
            "canonical" + context.Target.ArchiveExtension);
        DeterministicArchive.Create(extractionRoot, canonicalPath, context);
        using FileStream actual = File.OpenRead(archivePath);
        using FileStream expected = File.OpenRead(canonicalPath);
        if (actual.Length != expected.Length)
        {
            throw new ReleaseInputException(
                "The release archive container is not canonical.");
        }

        Span<byte> actualBuffer = stackalloc byte[8192];
        Span<byte> expectedBuffer = stackalloc byte[8192];
        int actualRead;
        do
        {
            actualRead = actual.Read(actualBuffer);
            int expectedRead = expected.Read(expectedBuffer);
            if (actualRead != expectedRead
                || !actualBuffer[..actualRead].SequenceEqual(
                    expectedBuffer[..expectedRead]))
            {
                throw new ReleaseInputException(
                    "The release archive container is not canonical.");
            }
        }
        while (actualRead != 0);
    }

    private static string GetExtractionPath(
        string root,
        string relativePath)
    {
        string destination = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(root, destination);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new ReleaseInputException(
                "A release archive path escapes the extraction root.");
        }

        return destination;
    }

    private static void WriteBounded(
        Stream input,
        string destination,
        long expectedLength)
    {
        using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        byte[] buffer = new byte[64 * 1024];
        long written = 0;
        while (written < expectedLength)
        {
            int requested = (int)Math.Min(buffer.Length, expectedLength - written);
            int read = input.Read(buffer, 0, requested);
            if (read == 0)
            {
                throw new ReleaseInputException(
                    "A release archive entry ended before its declared length.");
            }

            output.Write(buffer, 0, read);
            written += read;
        }

        if (input.ReadByte() != -1)
        {
            throw new ReleaseInputException(
                "A release archive entry exceeds its declared length.");
        }

        output.Flush(flushToDisk: true);
    }

    private static bool HasExpectedTimestamp(
        DateTimeOffset actual,
        DateTimeOffset expected,
        bool usesZip)
    {
        if (!usesZip)
        {
            return actual.ToUniversalTime() == expected.ToUniversalTime();
        }

        DateTime expectedDosTime = expected.UtcDateTime.AddSeconds(
            -(expected.Second % 2));
        return actual.DateTime == expectedDosTime;
    }

    private sealed record ArchiveEntryMetadata(
        int Mode,
        DateTimeOffset ModificationTime);
}
