using System.Buffers.Binary;
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
        archive.Dispose();
        NormalizeZipCreatorPlatform(archivePath, files.Count);
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
        writer.Dispose();
        NormalizeGzipOperatingSystem(archivePath);
    }

    private static void NormalizeZipCreatorPlatform(
        string archivePath,
        int expectedEntryCount)
    {
        using FileStream stream = File.Open(
            archivePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Span<byte> end = stackalloc byte[22];
        stream.Position = stream.Length - end.Length;
        stream.ReadExactly(end);
        if (BinaryPrimitives.ReadUInt32LittleEndian(end) != 0x06054b50
            || BinaryPrimitives.ReadUInt16LittleEndian(end[10..]) != expectedEntryCount
            || BinaryPrimitives.ReadUInt16LittleEndian(end[20..]) != 0)
        {
            throw new ReleaseInputException(
                "The generated ZIP end record is inconsistent.");
        }

        long position = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
        Span<byte> header = stackalloc byte[46];
        for (int index = 0; index < expectedEntryCount; index++)
        {
            stream.Position = position;
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50)
            {
                throw new ReleaseInputException(
                    "The generated ZIP central directory is inconsistent.");
            }

            header[5] = 3;
            stream.Position = position;
            stream.Write(header);
            position += header.Length
                + BinaryPrimitives.ReadUInt16LittleEndian(header[28..])
                + BinaryPrimitives.ReadUInt16LittleEndian(header[30..])
                + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
        }
    }

    private static void NormalizeGzipOperatingSystem(string archivePath)
    {
        using FileStream stream = File.Open(
            archivePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None);
        stream.Position = 9;
        stream.WriteByte(3);
    }

    private static string GetArchivePath(
        string stageDirectory,
        string fullPath) =>
        ReleaseTree.NormalizeRelativePath(
            Path.GetRelativePath(
                Path.GetFullPath(stageDirectory),
                fullPath));
}
