namespace Flowspan.Platform;

public static class RedactedExportFile
{
    public const int MaximumContentBytes = 1 * 1024 * 1024;

    public static async ValueTask<string> WriteAsync(
        string directory,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName != Path.GetFileName(fileName)
            || Path.IsPathRooted(fileName)
            || fileName.Contains("..", StringComparison.Ordinal)
            || !IsAllowedFileName(fileName))
        {
            throw new ArgumentException(
                "An export file name cannot contain a path.",
                nameof(fileName));
        }

        if (content.IsEmpty || content.Length > MaximumContentBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(content),
                $"An export must contain 1 to {MaximumContentBytes} bytes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string fullDirectory = Path.GetFullPath(directory);
        RejectReparsePoint(fullDirectory);
        Directory.CreateDirectory(fullDirectory);
        SetOwnerOnlyDirectoryMode(fullDirectory);
        string fullPath = Path.Combine(fullDirectory, fileName);
        RejectReparsePoint(fullPath);
        await using (var stream = new FileStream(
            fullPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            }))
        {
            SetOwnerOnlyFileMode(fullPath);
            await stream.WriteAsync(content, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        return fullPath;
    }

    private static bool IsAllowedFileName(string fileName)
    {
        foreach (char character in fileName)
        {
            bool allowed = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-'
                or '_'
                or '.';
            if (!allowed)
            {
                return false;
            }
        }

        return fileName[0] != '.';
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path))
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "An export path cannot be a reparse point.");
        }
    }

    private static void SetOwnerOnlyDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static void SetOwnerOnlyFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
