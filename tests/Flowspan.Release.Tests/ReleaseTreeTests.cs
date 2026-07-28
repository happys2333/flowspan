using System.Diagnostics;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class ReleaseTreeTests
{
    [Theory]
    [InlineData("../payload")]
    [InlineData("root//payload")]
    [InlineData("root/./payload")]
    [InlineData("C:/payload")]
    public void UnsafeRelativePathsAreRejected(string path)
    {
        Assert.Throws<ReleaseInputException>(() =>
            ReleaseTree.NormalizeRelativePath(path));
    }

    [Fact]
    public void OversizedRelativePathIsRejected()
    {
        WithRoot(root =>
        {
            string name = new('a', ReleaseBounds.MaximumRelativePathLength + 1);
            File.WriteAllText(Path.Combine(root, name), "bounded");

            Assert.Throws<ReleaseInputException>(() =>
                ReleaseTree.EnumerateFiles(root));
        });
    }

    [Fact]
    public void OversizedSparseFileIsRejected()
    {
        WithRoot(root =>
        {
            string path = Path.Combine(root, "oversized.bin");
            using (FileStream stream = File.Create(path))
            {
                stream.SetLength(ReleaseBounds.MaximumFileBytes + 1);
            }

            Assert.Throws<ReleaseInputException>(() =>
                ReleaseTree.EnumerateFiles(root));
        });
    }

    [Fact]
    public void ExcessiveFileCountIsRejected()
    {
        WithRoot(root =>
        {
            for (int index = 0; index <= ReleaseBounds.MaximumFileCount; index++)
            {
                File.WriteAllBytes(
                    Path.Combine(root, $"{index:D5}.bin"),
                    []);
            }

            Assert.Throws<ReleaseInputException>(() =>
                ReleaseTree.EnumerateFiles(root));
        });
    }

    [Fact]
    public void UnixFifoIsRejectedAsUnsupportedFileKind()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithRoot(root =>
        {
            string path = Path.Combine(root, "payload.fifo");
            var start = new ProcessStartInfo("mkfifo")
            {
                UseShellExecute = false,
            };
            start.ArgumentList.Add(path);
            using Process process = Process.Start(start)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);

            Assert.Throws<ReleaseInputException>(() =>
                ReleaseTree.EnumerateFiles(root));
        });
    }

    private static void WithRoot(Action<string> action)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-tree-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
