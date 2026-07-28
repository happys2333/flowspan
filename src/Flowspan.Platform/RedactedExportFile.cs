namespace Flowspan.Platform;

public static class RedactedExportFile
{
    public const int MaximumContentBytes = 1 * 1024 * 1024;

    public static IReadOnlyList<string> ListFiles(
        string directory,
        string requiredPrefix)
    {
        string fullDirectory = ValidateDirectoryAndPrefix(
            directory,
            requiredPrefix);
        if (!Directory.Exists(fullDirectory))
        {
            return [];
        }

        RejectReparsePoint(fullDirectory);
        return Directory.EnumerateFiles(
                fullDirectory,
                $"{requiredPrefix}*.json",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .Where(name => name.StartsWith(requiredPrefix, StringComparison.Ordinal)
                && IsAllowedFileName(name))
            .Where(name =>
            {
                RejectReparsePoint(Path.Combine(fullDirectory, name));
                return true;
            })
            .OrderDescending(StringComparer.Ordinal)
            .ToArray();
    }

    public static ValueTask<bool> DeleteAsync(
        string directory,
        string requiredPrefix,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullDirectory = ValidateDirectoryAndPrefix(
            directory,
            requiredPrefix);
        ValidateManagedFileName(fileName, requiredPrefix);
        if (!Directory.Exists(fullDirectory))
        {
            return ValueTask.FromResult(false);
        }

        RejectReparsePoint(fullDirectory);
        string fullPath = Path.Combine(fullDirectory, fileName);
        RejectReparsePoint(fullPath);
        if (!File.Exists(fullPath))
        {
            return ValueTask.FromResult(false);
        }

        File.Delete(fullPath);
        return ValueTask.FromResult(true);
    }

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
        RejectReparsePoint(fullDirectory);
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

    private static string ValidateDirectoryAndPrefix(
        string directory,
        string requiredPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredPrefix);
        if (requiredPrefix.Length > 64
            || !requiredPrefix.EndsWith('-')
            || !IsAllowedFileName(requiredPrefix))
        {
            throw new ArgumentException(
                "A managed export prefix is invalid.",
                nameof(requiredPrefix));
        }

        return Path.GetFullPath(directory);
    }

    private static void ValidateManagedFileName(
        string fileName,
        string requiredPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName != Path.GetFileName(fileName)
            || Path.IsPathRooted(fileName)
            || fileName.Contains("..", StringComparison.Ordinal)
            || !fileName.StartsWith(requiredPrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(".json", StringComparison.Ordinal)
            || !IsAllowedFileName(fileName))
        {
            throw new ArgumentException(
                "A managed export file name is invalid.",
                nameof(fileName));
        }
    }

    private static void RejectReparsePoint(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is
            FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
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
