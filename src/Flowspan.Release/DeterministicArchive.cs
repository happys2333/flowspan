using System.Formats.Tar;
using System.IO.Compression;

namespace Flowspan.Release;

public static class DeterministicArchive
{
    public static void Create(
        string stageDirectory,
        string archivePath,
        ReleaseContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(context);
        if (File.Exists(archivePath) || Directory.Exists(archivePath))
        {
            throw new ReleaseInputException(
                "The release archive output already exists.");
        }

        string packageRoot = Path.Combine(
            Path.GetFullPath(stageDirectory),
            context.Target.RootName);
        IReadOnlyList<ReleaseTreeFile> files =
            ReleaseTree.EnumerateFiles(packageRoot);
        if (context.Target.UsesZip)
        {
            CreateZip(stageDirectory, archivePath, files, context);
        }
        else
        {
            CreateTarGzip(stageDirectory, archivePath, files, context);
        }

        var archive = new FileInfo(archivePath);
        if (!archive.Exists || archive.Length is 0 or > ReleaseBounds.MaximumPackageBytes)
        {
            throw new ReleaseInputException(
                "The release archive is empty or exceeds its size bound.");
        }
    }

    private static void CreateZip(
        string stageDirectory,
        string archivePath,
        IReadOnlyList<ReleaseTreeFile> files,
        ReleaseContext context)
    {
        using FileStream output = new(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: false,
            entryNameEncoding: System.Text.Encoding.UTF8);
        foreach (ReleaseTreeFile file in files)
        {
            string path = GetArchivePath(stageDirectory, file.FullPath);
            ZipArchiveEntry entry = archive.CreateEntry(
                path,
                CompressionLevel.SmallestSize);
            entry.LastWriteTime = context.SourceTimestamp;
            int mode = StringComparer.Ordinal.Equals(path, context.Target.EntryPoint)
                ? 493
                : 420;
            entry.ExternalAttributes = (0x8000 | mode) << 16;
            using Stream destination = entry.Open();
            using FileStream source = File.OpenRead(file.FullPath);
            source.CopyTo(destination);
        }
    }

    private static void CreateTarGzip(
        string stageDirectory,
        string archivePath,
        IReadOnlyList<ReleaseTreeFile> files,
        ReleaseContext context)
    {
        using FileStream output = new(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var gzip = new GZipStream(
            output,
            CompressionLevel.SmallestSize,
            leaveOpen: false);
        using var writer = new TarWriter(
            gzip,
            TarEntryFormat.Ustar,
            leaveOpen: false);
        foreach (ReleaseTreeFile file in files)
        {
            string path = GetArchivePath(stageDirectory, file.FullPath);
            using FileStream source = File.OpenRead(file.FullPath);
            var entry = new UstarTarEntry(
                TarEntryType.RegularFile,
                path)
            {
                DataStream = source,
                Gid = 0,
                GroupName = string.Empty,
                ModificationTime = context.SourceTimestamp,
                Mode = StringComparer.Ordinal.Equals(path, context.Target.EntryPoint)
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                Uid = 0,
                UserName = string.Empty,
            };
            writer.WriteEntry(entry);
        }
    }

    private static string GetArchivePath(
        string stageDirectory,
        string fullPath) =>
        ReleaseTree.NormalizeRelativePath(
            Path.GetRelativePath(
                Path.GetFullPath(stageDirectory),
                fullPath));
}
