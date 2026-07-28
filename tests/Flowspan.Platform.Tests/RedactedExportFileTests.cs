using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class RedactedExportFileTests
{
    [Fact]
    public async Task WriteReturnsFullPathInsideDirectoryWithOwnerOnlyModes()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-{Guid.NewGuid():N}");
        byte[] content =
            "{\"exportKind\":\"flowspan.scene-export.redacted/v1\"}"u8.ToArray();
        try
        {
            string fullPath = await RedactedExportFile.WriteAsync(
                directory,
                "scene-export.json",
                content);

            Assert.Equal(
                Path.Combine(Path.GetFullPath(directory), "scene-export.json"),
                fullPath);
            Assert.Equal(content, await File.ReadAllBytesAsync(fullPath));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(Path.GetFullPath(directory)));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(fullPath));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PathBearingFileNamesAreRejectedWithoutCreatingStorage()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-names-{Guid.NewGuid():N}");
        byte[] content = "{\"exportKind\":\"redacted\"}"u8.ToArray();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await RedactedExportFile.WriteAsync(
                directory,
                Path.Combine("nested", "scene-export.json"),
                content));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await RedactedExportFile.WriteAsync(
                directory,
                Path.Combine(Path.GetTempPath(), "scene-export.json"),
                content));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await RedactedExportFile.WriteAsync(directory, "..", content));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task StreamAndUnsafeFileNameCharactersAreRejected()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-chars-{Guid.NewGuid():N}");
        byte[] content = "{\"exportKind\":\"redacted\"}"u8.ToArray();
        string[] rejected =
        [
            "scene-export.json:payload",
            "scene-export.json ",
            "scene-export*.json",
            ".scene-export.json",
            "scene export.json",
        ];

        foreach (string fileName in rejected)
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await RedactedExportFile.WriteAsync(
                    directory,
                    fileName,
                    content));
        }

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task EmptyAndOversizeContentAreRejectedWithoutCreatingStorage()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-bounds-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await RedactedExportFile.WriteAsync(
                directory,
                "scene-export.json",
                ReadOnlyMemory<byte>.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await RedactedExportFile.WriteAsync(
                directory,
                "scene-export.json",
                new byte[RedactedExportFile.MaximumContentBytes + 1]));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task SecondWriteWithSameNameFailsWithoutClobberingFirstExport()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-existing-{Guid.NewGuid():N}");
        byte[] firstContent = "{\"attempt\":1}"u8.ToArray();
        try
        {
            string fullPath = await RedactedExportFile.WriteAsync(
                directory,
                "scene-export.json",
                firstContent);

            await Assert.ThrowsAsync<IOException>(async () =>
                await RedactedExportFile.WriteAsync(
                    directory,
                    "scene-export.json",
                    "{\"attempt\":2}"u8.ToArray()));

            Assert.Equal(firstContent, await File.ReadAllBytesAsync(fullPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SymlinkedExportDirectoryIsRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string realDirectory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-target-{Guid.NewGuid():N}");
        string linkPath = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-redacted-export-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(realDirectory);
        try
        {
            Directory.CreateSymbolicLink(linkPath, realDirectory);

            await Assert.ThrowsAsync<IOException>(async () =>
                await RedactedExportFile.WriteAsync(
                    linkPath,
                    "scene-export.json",
                    "{\"attempt\":\"symlink\"}"u8.ToArray()));

            Assert.Empty(Directory.GetFiles(realDirectory));
        }
        finally
        {
            Directory.Delete(linkPath);
            Directory.Delete(realDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PreCancelledWriteCreatesNoDirectoryOrFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-cancelled-redacted-export-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RedactedExportFile.WriteAsync(
                directory,
                "scene-export.json",
                "{\"attempt\":\"cancelled\"}"u8.ToArray(),
                cancellation.Token));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ManagedListAndDeleteStayInsideDiagnosticPrefix()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-managed-export-{Guid.NewGuid():N}");
        try
        {
            await RedactedExportFile.WriteAsync(
                directory,
                "diagnostics-20260728-a.json",
                "{\"kind\":\"diagnostics\"}"u8.ToArray());
            await RedactedExportFile.WriteAsync(
                directory,
                "history-export-20260728-b.json",
                "{\"kind\":\"history\"}"u8.ToArray());

            string diagnostic = Assert.Single(
                RedactedExportFile.ListFiles(directory, "diagnostics-"));
            Assert.Equal("diagnostics-20260728-a.json", diagnostic);
            Assert.True(await RedactedExportFile.DeleteAsync(
                directory,
                "diagnostics-",
                diagnostic));
            Assert.False(await RedactedExportFile.DeleteAsync(
                directory,
                "diagnostics-",
                diagnostic));
            Assert.True(File.Exists(Path.Combine(
                directory,
                "history-export-20260728-b.json")));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await RedactedExportFile.DeleteAsync(
                    directory,
                    "diagnostics-",
                    "../history-export-20260728-b.json"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ManagedListAndDeleteRejectDanglingSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-managed-link-{Guid.NewGuid():N}");
        string fileName = "diagnostics-20260728-link.json";
        Directory.CreateDirectory(directory);
        string linkPath = Path.Combine(directory, fileName);
        File.CreateSymbolicLink(
            linkPath,
            Path.Combine(directory, "missing-target.json"));
        try
        {
            Assert.Throws<IOException>(() =>
                RedactedExportFile.ListFiles(directory, "diagnostics-"));
            await Assert.ThrowsAsync<IOException>(async () =>
                await RedactedExportFile.DeleteAsync(
                    directory,
                    "diagnostics-",
                    fileName));
            Assert.True(File.GetAttributes(linkPath)
                .HasFlag(FileAttributes.ReparsePoint));
        }
        finally
        {
            File.Delete(linkPath);
            Directory.Delete(directory);
        }
    }
}
