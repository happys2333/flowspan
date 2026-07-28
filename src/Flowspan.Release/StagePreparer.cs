using System.Text;

namespace Flowspan.Release;

public static class StagePreparer
{
    public static string Prepare(
        string publishDirectory,
        string stageDirectory,
        ReleaseContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageDirectory);
        ArgumentNullException.ThrowIfNull(context);

        string publishPath = Path.GetFullPath(publishDirectory);
        string stagePath = Path.GetFullPath(stageDirectory);
        RejectOverlappingPaths(publishPath, stagePath);
        if (Directory.Exists(stagePath) || File.Exists(stagePath))
        {
            throw new ReleaseInputException(
                "The release stage already exists.");
        }

        IReadOnlyList<ReleaseTreeFile> sourceFiles =
            ReleaseTree.EnumerateFiles(publishPath);
        string sourceEntryPoint = context.Target.Rid == "win-x64"
            ? "Flowspan.Desktop.exe"
            : "Flowspan.Desktop";
        if (sourceFiles.Count != 1
            || !StringComparer.Ordinal.Equals(
                sourceFiles[0].RelativePath,
                sourceEntryPoint))
        {
            throw new ReleaseInputException(
                "The release publish must contain only its target entry point.");
        }

        string parent = Path.GetDirectoryName(stagePath)
            ?? throw new ReleaseInputException(
                "The release stage requires a parent directory.");
        Directory.CreateDirectory(parent);
        string temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(stagePath)}.tmp-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(temporaryPath);
            string contentPrefix = context.Target.IsMacOS
                ? Path.Combine("Flowspan.app", "Contents", "MacOS")
                : context.Target.RootName;
            foreach (ReleaseTreeFile sourceFile in sourceFiles)
            {
                string destination = Path.Combine(
                    temporaryPath,
                    contentPrefix,
                    sourceFile.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                CopyFile(
                    sourceFile.FullPath,
                    destination,
                    sourceFile.Length,
                    context);
            }

            if (context.Target.IsMacOS)
            {
                WriteInfoPlist(temporaryPath, context);
            }

            IReadOnlyList<ReleaseTreeFile> currentFiles =
                ReleaseTree.EnumerateFiles(publishPath);
            if (currentFiles.Count != sourceFiles.Count
                || !currentFiles.Select(static file => (
                    file.RelativePath,
                    file.Length)).SequenceEqual(sourceFiles.Select(static file => (
                        file.RelativePath,
                        file.Length))))
            {
                throw new ReleaseInputException(
                    "The release publish changed while it was being prepared.");
            }

            string metadataPath = Path.Combine(
                temporaryPath,
                ReleaseContextCodec.StageMetadataFileName);
            File.WriteAllBytes(
                metadataPath,
                ReleaseContextCodec.Encode(
                    context,
                    Path.Combine(temporaryPath, context.Target.RootName)));
            SetFileMetadata(metadataPath, executable: false, context);
            Directory.Move(temporaryPath, stagePath);
            return stagePath;
        }
        catch
        {
            if (Directory.Exists(temporaryPath))
            {
                Directory.Delete(temporaryPath, recursive: true);
            }

            throw;
        }
    }

    private static void CopyFile(
        string source,
        string destination,
        long expectedLength,
        ReleaseContext context)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (directory is null)
        {
            throw new ReleaseInputException(
                "A release destination path has no parent.");
        }

        Directory.CreateDirectory(directory);

        using (FileStream input = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan))
        using (FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan))
        {
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }

        if (new FileInfo(source).Length != expectedLength
            || new FileInfo(destination).Length != expectedLength
            || !StringComparer.Ordinal.Equals(
                ReleaseHash.Sha256File(source),
                ReleaseHash.Sha256File(destination)))
        {
            throw new ReleaseInputException(
                "The release publish changed while it was being prepared.");
        }

        bool executable = StringComparer.Ordinal.Equals(
            Path.GetFileName(destination),
            context.Target.Rid == "win-x64"
                ? "Flowspan.Desktop.exe"
                : "Flowspan.Desktop");
        SetFileMetadata(destination, executable, context);
    }

    private static void WriteInfoPlist(
        string temporaryPath,
        ReleaseContext context)
    {
        string contentsPath = Path.Combine(temporaryPath, "Flowspan.app", "Contents");
        Directory.CreateDirectory(contentsPath);
        string plistPath = Path.Combine(contentsPath, "Info.plist");
        string plist = string.Join('\n',
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" "
                + "\"https://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
            "<plist version=\"1.0\">",
            "<dict>",
            "  <key>CFBundleDisplayName</key><string>Flowspan</string>",
            "  <key>CFBundleExecutable</key><string>Flowspan.Desktop</string>",
            "  <key>CFBundleIdentifier</key><string>io.flowspan.desktop</string>",
            $"  <key>CFBundleShortVersionString</key><string>{context.DisplayVersion}</string>",
            $"  <key>CFBundleVersion</key><string>{context.BuildVersion}</string>",
            "  <key>CFBundlePackageType</key><string>APPL</string>",
            "  <key>LSMinimumSystemVersion</key><string>13.0</string>",
            "  <key>NSHighResolutionCapable</key><true/>",
            "</dict>",
            "</plist>",
            string.Empty);
        File.WriteAllText(plistPath, plist, new UTF8Encoding(false));
        SetFileMetadata(plistPath, executable: false, context);
    }

    private static void SetFileMetadata(
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

    private static void RejectOverlappingPaths(string publish, string stage)
    {
        if (ContainsPath(publish, stage) || ContainsPath(stage, publish))
        {
            throw new ReleaseInputException(
                "Release publish and stage directories must not overlap.");
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
}
