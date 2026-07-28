using System.Buffers.Binary;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class DeterministicArchiveTests
{
    [Fact]
    public void ZipUsesCanonicalUnixCreatorPlatform()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        byte[] archive = File.ReadAllBytes(Assert.Single(
            Directory.GetFiles(output, "*.zip")));
        ReadOnlySpan<byte> end = archive.AsSpan(archive.Length - 22);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(end[10..]);
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> header = archive.AsSpan(offset, 46);
            Assert.Equal(0x02014b50u,
                BinaryPrimitives.ReadUInt32LittleEndian(header));
            Assert.Equal(3, header[5]);
            offset += header.Length
                + BinaryPrimitives.ReadUInt16LittleEndian(header[28..])
                + BinaryPrimitives.ReadUInt16LittleEndian(header[30..])
                + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
        }

        Assert.Equal(archive.Length - 22, offset);
    }

    [Theory]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    public void TarGzipUsesCanonicalUnixOperatingSystem(string rid)
    {
        using var fixture = new ReleaseTestFixture(rid);
        fixture.Prepare();
        string output = fixture.Seal("release");
        byte[] archive = File.ReadAllBytes(Assert.Single(
            Directory.GetFiles(output, "*.tar.gz")));

        Assert.Equal(3, archive[9]);
    }
}
