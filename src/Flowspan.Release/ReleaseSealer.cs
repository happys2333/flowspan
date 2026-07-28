namespace Flowspan.Release;

public static class ReleaseSealer
{
    public static PackageRecordSet Seal(
        string stageDirectory,
        string outputDirectory,
        string lockFilePath,
        string runtimeLockFilePath,
        string globalPackagesPath,
        string signatureState,
        string? signatureReportPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string stagePath = Path.GetFullPath(stageDirectory);
        string outputPath = Path.GetFullPath(outputDirectory);
        RejectOverlappingPaths(stagePath, outputPath);
        if (Directory.Exists(outputPath) || File.Exists(outputPath))
        {
            throw new ReleaseInputException(
                "The release output already exists.");
        }

        RequireSignatureInput(signatureState, signatureReportPath);
        PreparedStage prepared = ReadAndValidateStage(stagePath, signatureState);
        ReleaseContext context = prepared.Context;
        string parent = Path.GetDirectoryName(outputPath)
            ?? throw new ReleaseInputException(
                "The release output requires a parent directory.");
        Directory.CreateDirectory(parent);
        string token = Guid.NewGuid().ToString("N");
        string temporaryOutput = Path.Combine(
            parent,
            $".{Path.GetFileName(outputPath)}.tmp-{token}");
        string temporaryStage = Path.Combine(
            parent,
            $".{Path.GetFileName(outputPath)}.stage-{token}");

        try
        {
            Directory.CreateDirectory(temporaryOutput);
            Directory.CreateDirectory(temporaryStage);
            CopyPackageRoot(
                stagePath,
                temporaryStage,
                context,
                signatureState);
            string packageRoot = Path.Combine(
                temporaryStage,
                context.Target.RootName);
            ValidatePackageRoot(packageRoot, prepared, signatureState);
            string treeSha256 = PackageManifest.ComputeSignedTreeSha256(
                packageRoot,
                context);
            if (signatureState == SignatureStates.Verified)
            {
                string reportSource = Path.GetFullPath(signatureReportPath!);
                SignatureReport.Verify(reportSource, context, treeSha256);
                string reportDestination = Path.Combine(
                    packageRoot,
                    PackageManifest.SignatureReportFileName);
                File.Copy(reportSource, reportDestination, overwrite: false);
                SetDataMetadata(reportDestination, context);
            }

            PackageManifestResult manifest = PackageManifest.Create(
                packageRoot,
                context,
                signatureState);
            if (!StringComparer.Ordinal.Equals(
                    manifest.SignedTreeSha256,
                    treeSha256))
            {
                throw new ReleaseInputException(
                    "The release stage changed while it was being sealed.");
            }

            string manifestPath = Path.Combine(
                packageRoot,
                PackageManifest.FileName);
            WriteNew(manifestPath, manifest.Encoded);
            SetDataMetadata(manifestPath, context);

            string archiveName = context.GetPackageStem(signatureState)
                + context.Target.ArchiveExtension;
            string archivePath = Path.Combine(temporaryOutput, archiveName);
            DeterministicArchive.Create(
                temporaryStage,
                archivePath,
                context);
            IReadOnlyList<NuGetDependency> dependencies = NuGetGraph.Read(
                lockFilePath,
                runtimeLockFilePath,
                globalPackagesPath,
                context);
            PackageRecordSet records = PackageRecords.Write(
                temporaryOutput,
                archiveName,
                context,
                signatureState,
                dependencies);
            ReleaseVerifier.VerifyDirectory(temporaryOutput);
            Directory.Delete(temporaryStage, recursive: true);
            Directory.Move(temporaryOutput, outputPath);
            return records;
        }
        catch
        {
            if (Directory.Exists(temporaryStage))
            {
                Directory.Delete(temporaryStage, recursive: true);
            }

            if (Directory.Exists(temporaryOutput))
            {
                Directory.Delete(temporaryOutput, recursive: true);
            }

            throw;
        }
    }

    public static string ComputeSignedTreeSha256(string stageDirectory)
    {
        string stagePath = Path.GetFullPath(stageDirectory);
        PreparedStage prepared = ReadAndValidateStage(
            stagePath,
            SignatureStates.Verified);
        return PackageManifest.ComputeSignedTreeSha256(
            Path.Combine(stagePath, prepared.Context.Target.RootName),
            prepared.Context);
    }

    private static PreparedStage ReadAndValidateStage(
        string stagePath,
        string signatureState)
    {
        var stage = new DirectoryInfo(stagePath);
        if (!stage.Exists
            || (stage.Attributes & FileAttributes.ReparsePoint) != 0
            || stage.LinkTarget is not null)
        {
            throw new ReleaseInputException(
                "The release stage is missing or linked.");
        }

        FileSystemInfo[] children = stage.GetFileSystemInfos();
        Array.Sort(children, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Name, right.Name));
        FileInfo metadata = children.OfType<FileInfo>().SingleOrDefault(file =>
            StringComparer.Ordinal.Equals(
                file.Name,
                ReleaseContextCodec.StageMetadataFileName))
            ?? throw new ReleaseInputException(
                "The release stage metadata is missing.");
        if ((metadata.Attributes & FileAttributes.ReparsePoint) != 0
            || metadata.LinkTarget is not null)
        {
            throw new ReleaseInputException(
                "The release stage metadata is linked.");
        }

        PreparedStage prepared = ReleaseContextCodec.Decode(
            File.ReadAllBytes(metadata.FullName));
        ReleaseContext context = prepared.Context;
        string[] actualNames = children.Select(static child => child.Name).ToArray();
        string[] expectedNames =
        [
            ReleaseContextCodec.StageMetadataFileName,
            context.Target.RootName,
        ];
        Array.Sort(expectedNames, StringComparer.Ordinal);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || children.Single(child => StringComparer.Ordinal.Equals(
                child.Name,
                context.Target.RootName)) is not DirectoryInfo root
            || (root.Attributes & FileAttributes.ReparsePoint) != 0
            || root.LinkTarget is not null)
        {
            throw new ReleaseInputException(
                "The release stage contains unexpected or linked entries.");
        }

        ValidatePackageRoot(root.FullName, prepared, signatureState);
        return prepared;
    }

    private static void ValidatePackageRoot(
        string packageRoot,
        PreparedStage prepared,
        string signatureState)
    {
        ReleaseContext context = prepared.Context;
        var expected = prepared.Files.ToDictionary(
            static file => file.Path,
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReleaseTreeFile file in ReleaseTree.EnumerateFiles(packageRoot))
        {
            string path = $"{context.Target.RootName}/{file.RelativePath}";
            if (!IsAllowedStagedPath(file.RelativePath, context, signatureState)
                || !seen.Add(path))
            {
                throw new ReleaseInputException(
                    "The release stage contains an unapproved package path.");
            }

            bool isPrepared = expected.TryGetValue(
                path,
                out PackageFileRecord? record);
            bool mayChange = signatureState == SignatureStates.Verified
                && StringComparer.Ordinal.Equals(path, context.Target.EntryPoint);
            if (!isPrepared && signatureState != SignatureStates.Verified
                || isPrepared && !mayChange
                    && (file.Length != record!.Length
                        || !StringComparer.Ordinal.Equals(
                            ReleaseHash.Sha256File(file.FullPath),
                            record.Sha256)))
            {
                throw new ReleaseInputException(
                    "The release stage differs from its prepared file manifest.");
            }
        }

        if (prepared.Files.Any(file =>
                !seen.Contains(file.Path)
                || !IsPreparedPath(file.Path, context)))
        {
            throw new ReleaseInputException(
                "The release stage is missing a prepared package path.");
        }
    }

    private static bool IsPreparedPath(
        string path,
        ReleaseContext context) =>
        path.StartsWith(context.Target.RootName + '/', StringComparison.Ordinal)
        && IsAllowedStagedPath(
            path[(context.Target.RootName.Length + 1)..],
            context,
            SignatureStates.UnsignedTestArtifact);

    private static void RequireSignatureInput(
        string signatureState,
        string? signatureReportPath)
    {
        if (signatureState == SignatureStates.UnsignedTestArtifact)
        {
            if (signatureReportPath is not null)
            {
                throw new ReleaseInputException(
                    "Unsigned test sealing cannot accept a signature report.");
            }

            return;
        }

        if (signatureState != SignatureStates.Verified
            || string.IsNullOrWhiteSpace(signatureReportPath)
            || !File.Exists(signatureReportPath))
        {
            throw new ReleaseInputException(
                "Verified sealing requires an existing signature report.");
        }
    }

    private static void CopyPackageRoot(
        string stagePath,
        string destinationStage,
        ReleaseContext context,
        string signatureState)
    {
        string sourceRoot = Path.Combine(stagePath, context.Target.RootName);
        string destinationRoot = Path.Combine(
            destinationStage,
            context.Target.RootName);
        Directory.CreateDirectory(destinationRoot);
        foreach (ReleaseTreeFile source in ReleaseTree.EnumerateFiles(sourceRoot))
        {
            if (!IsAllowedStagedPath(
                source.RelativePath,
                context,
                signatureState))
            {
                throw new ReleaseInputException(
                    "The release stage contains an unapproved package path.");
            }

            string destination = Path.Combine(
                destinationRoot,
                source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source.FullPath, destination, overwrite: false);
            bool executable = StringComparer.Ordinal.Equals(
                $"{context.Target.RootName}/{source.RelativePath}",
                context.Target.EntryPoint);
            SetMetadata(destination, executable, context);
        }
    }

    private static bool IsAllowedStagedPath(
        string path,
        ReleaseContext context,
        string signatureState)
    {
        string entryPoint = context.Target.EntryPoint[
            (context.Target.RootName.Length + 1)..];
        if (StringComparer.Ordinal.Equals(path, entryPoint))
        {
            return true;
        }

        return context.Target.IsMacOS
            && (StringComparer.Ordinal.Equals(path, "Contents/Info.plist")
                || signatureState == SignatureStates.Verified
                    && StringComparer.Ordinal.Equals(
                        path,
                        "Contents/_CodeSignature/CodeResources"));
    }

    private static void RejectOverlappingPaths(string stage, string output)
    {
        if (ContainsPath(stage, output) || ContainsPath(output, stage))
        {
            throw new ReleaseInputException(
                "Release stage and output directories must not overlap.");
        }
    }

    private static bool ContainsPath(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        string parentPrefix = $"..{Path.DirectorySeparatorChar}";
        return relative == "."
            || relative != ".."
                && !relative.StartsWith(parentPrefix, StringComparison.Ordinal);
    }

    private static void SetDataMetadata(string path, ReleaseContext context) =>
        SetMetadata(path, executable: false, context);

    private static void SetMetadata(
        string path,
        bool executable,
        ReleaseContext context)
    {
        File.SetLastWriteTimeUtc(path, context.SourceTimestamp.UtcDateTime);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                executable
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

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
