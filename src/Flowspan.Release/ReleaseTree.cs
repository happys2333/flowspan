using System.Runtime.InteropServices;

namespace Flowspan.Release;

public sealed record ReleaseTreeFile(
    string RelativePath,
    string FullPath,
    long Length);

public static class ReleaseTree
{
    public static IReadOnlyList<ReleaseTreeFile> EnumerateFiles(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = new DirectoryInfo(Path.GetFullPath(rootPath));
        if (!root.Exists)
        {
            throw new ReleaseInputException(
                "The release input directory does not exist.");
        }

        RejectLink(root);
        var files = new List<ReleaseTreeFile>();
        long totalBytes = 0;
        Walk(root, root, files, ref totalBytes);
        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            left.RelativePath,
            right.RelativePath));
        return files;
    }

    public static string NormalizeRelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Replace(Path.DirectorySeparatorChar, '/');
        if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
        {
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, '/');
        }

        string[] segments = normalized.Split('/', StringSplitOptions.None);
        if (normalized.Length > ReleaseBounds.MaximumRelativePathLength
            || normalized[0] == '/'
            || segments.Any(static segment =>
                segment.Length == 0
                || segment is "." or ".."
                || segment.Any(static character =>
                    character is '\0' or ':' or '\\'
                    || char.IsControl(character))))
        {
            throw new ReleaseInputException(
                "A release relative path is unsafe or oversized.");
        }

        return string.Join('/', segments);
    }

    private static void Walk(
        DirectoryInfo root,
        DirectoryInfo directory,
        List<ReleaseTreeFile> files,
        ref long totalBytes)
    {
        FileSystemInfo[] children;
        try
        {
            children = directory.GetFileSystemInfos();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new ReleaseInputException(
                "A release directory could not be enumerated.",
                exception);
        }

        Array.Sort(
            children,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.Name,
                right.Name));
        foreach (FileSystemInfo child in children)
        {
            RejectLink(child);
            switch (child)
            {
                case DirectoryInfo childDirectory:
                    Walk(root, childDirectory, files, ref totalBytes);
                    break;
                case FileInfo file:
                    AddFile(root, file, files, ref totalBytes);
                    break;
                default:
                    throw new ReleaseInputException(
                        "The release tree contains an unsupported file kind.");
            }
        }
    }

    private static void AddFile(
        DirectoryInfo root,
        FileInfo file,
        List<ReleaseTreeFile> files,
        ref long totalBytes)
    {
        if (files.Count >= ReleaseBounds.MaximumFileCount
            || file.Length > ReleaseBounds.MaximumFileBytes
            || totalBytes > ReleaseBounds.MaximumPackageBytes - file.Length)
        {
            throw new ReleaseInputException(
                "The release tree exceeds its file or size bound.");
        }

        string relativePath = NormalizeRelativePath(
            Path.GetRelativePath(root.FullName, file.FullName));
        totalBytes += file.Length;
        files.Add(new ReleaseTreeFile(
            relativePath,
            file.FullName,
            file.Length));
    }

    private static void RejectLink(FileSystemInfo value)
    {
        try
        {
            if ((value.Attributes & FileAttributes.ReparsePoint) != 0
                || value.LinkTarget is not null
                || (value.Attributes & FileAttributes.Device) != 0)
            {
                throw new ReleaseInputException(
                    "The release tree contains a link or reparse point.");
            }

            RejectUnsupportedUnixKind(value);
        }
        catch (ReleaseInputException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new ReleaseInputException(
                "A release file kind could not be inspected.",
                exception);
        }
    }

    private static void RejectUnsupportedUnixKind(FileSystemInfo value)
    {
        uint mode;
        int result;
        if (OperatingSystem.IsLinux())
        {
            result = LinuxLStat(value.FullName, out UnixFileStatus status);
            mode = status.LinuxMode;
        }
        else if (OperatingSystem.IsMacOS())
        {
            result = MacLStat(value.FullName, out UnixFileStatus status);
            mode = status.MacMode;
        }
        else
        {
            return;
        }

        uint expectedKind = value is DirectoryInfo ? 0x4000u : 0x8000u;
        if (result != 0 || (mode & 0xF000u) != expectedKind)
        {
            throw new ReleaseInputException(
                "The release tree contains an unsupported file kind.");
        }
    }

#pragma warning disable SYSLIB1054, CA2101
    [DllImport("libc", EntryPoint = "lstat", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LinuxLStat(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out UnixFileStatus status);

    [DllImport("libc", EntryPoint = "lstat", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MacLStat(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out UnixFileStatus status);
#pragma warning restore SYSLIB1054, CA2101

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct UnixFileStatus
    {
        [FieldOffset(4)] public ushort MacMode;
        [FieldOffset(24)] public uint LinuxMode;
    }
}
