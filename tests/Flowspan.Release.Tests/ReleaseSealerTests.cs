using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class ReleaseSealerTests
{
    [Theory]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    public void TarPackagesUseOnlyUstarEntries(string rid)
    {
        using var fixture = new ReleaseTestFixture(rid);
        fixture.Prepare();
        string output = fixture.Seal("release");
        string archivePath = Assert.Single(
            Directory.GetFiles(output, "*.tar.gz"));

        using FileStream source = File.OpenRead(archivePath);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            Assert.Equal(TarEntryFormat.Ustar, entry.Format);
        }
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    public void RepeatedUnsignedSealIsByteIdenticalAndVerifiable(string rid)
    {
        using var fixture = new ReleaseTestFixture(rid);
        fixture.Prepare();

        string first = fixture.Seal("first");
        if (rid == "linux-x64")
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(1100));
        }

        string second = fixture.Seal("second");

        ReleaseVerifier.VerifyDirectory(first);
        ReleaseVerifier.VerifyDirectory(second);
        string[] firstNames = Directory.GetFiles(first)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        string[] secondNames = Directory.GetFiles(second)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(firstNames, secondNames);
        foreach (string name in firstNames)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first, name)),
                File.ReadAllBytes(Path.Combine(second, name)));
        }

        Assert.Contains(
            firstNames,
            name => name.EndsWith(
                fixture.Target.ArchiveExtension,
                StringComparison.Ordinal));
    }

    [Fact]
    public void LicenseRecordIncludesEveryLockedPackageAndDeclaredExpression()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string licensePath = Assert.Single(
            Directory.GetFiles(output, "*.licenses.json"));

        JsonObject record = CanonicalJson.DecodeObject(
            File.ReadAllBytes(licensePath));
        Assert.Equal(4, CanonicalJson.ReadInt64(record, "packageCount"));
        Assert.True(CanonicalJson.ReadBoolean(record, "reviewRequired"));
        JsonObject application = CanonicalJson.ReadObject(record, "application");
        Assert.Equal("Flowspan", CanonicalJson.ReadString(application, "id"));
        JsonObject[] packages = CanonicalJson.ReadArray(record, "packages")
            .OfType<JsonObject>()
            .ToArray();
        Assert.Contains(packages, value =>
            CanonicalJson.ReadString(value, "id") ==
            "Microsoft.NETCore.App.Host.linux-x64");
        Assert.Contains(packages, value =>
            CanonicalJson.ReadString(value, "id") ==
            "Microsoft.NETCore.App.Runtime.linux-x64");
        JsonObject package = packages
            .Single(value => CanonicalJson.ReadString(value, "id") == "Example.Package");
        Assert.Equal("Example.Package", CanonicalJson.ReadString(package, "id"));
        Assert.Equal(
            "declared-expression",
            CanonicalJson.ReadString(package, "reviewStatus"));
        Assert.Equal("MIT", package["licenseExpression"]!.GetValue<string>());
    }

    [Fact]
    public void InvalidLicenseExpressionBecomesNoAssertionReviewFinding()
    {
        using var fixture = new ReleaseTestFixture("linux-x64", "MIT OR");
        fixture.Prepare();
        string output = fixture.Seal("release");
        JsonObject licenses = CanonicalJson.DecodeObject(File.ReadAllBytes(
            Assert.Single(Directory.GetFiles(output, "*.licenses.json"))));
        JsonObject package = CanonicalJson.ReadArray(licenses, "packages")
            .OfType<JsonObject>()
            .Single(value => CanonicalJson.ReadString(value, "id") == "Example.Package");
        Assert.Equal(
            "invalid-expression",
            CanonicalJson.ReadString(package, "licenseKind"));
        Assert.True(CanonicalJson.ReadBoolean(licenses, "reviewRequired"));
        JsonObject spdx = CanonicalJson.DecodeObject(File.ReadAllBytes(
            Assert.Single(Directory.GetFiles(output, "*.spdx.json"))));
        JsonObject dependency = Assert.IsType<JsonObject>(
            CanonicalJson.ReadArray(spdx, "packages")[1]);
        Assert.Equal(
            "NOASSERTION",
            CanonicalJson.ReadString(dependency, "licenseDeclared"));
    }

    [Fact]
    public void LockContentHashMismatchIsRejectedWithoutOutput()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        JsonObject lockFile = CanonicalJson.DecodeObject(
            File.ReadAllBytes(fixture.LockFilePath));
        JsonObject dependencies = CanonicalJson.ReadObject(lockFile, "dependencies");
        JsonObject framework = CanonicalJson.ReadObject(dependencies, "net10.0");
        JsonObject package = CanonicalJson.ReadObject(framework, "Example.Package");
        package["contentHash"] = Convert.ToBase64String(new byte[64]);
        File.WriteAllBytes(fixture.LockFilePath, CanonicalJson.Encode(lockFile));

        Assert.Throws<ReleaseInputException>(() => fixture.Seal("release"));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "release")));
    }

    [Fact]
    public void UnapprovedPostSigningPathIsRejectedWithoutOutput()
    {
        using var fixture = new ReleaseTestFixture("osx-arm64");
        fixture.Prepare();
        File.WriteAllText(
            Path.Combine(fixture.StageDirectory, "Flowspan.app", "signing.key"),
            "sensitive");

        Assert.Throws<ReleaseInputException>(() => fixture.Seal("release"));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "release")));
    }

    [Fact]
    public void UnsignedSealRejectsPreparedEntryPointMutation()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string entryPoint = Path.Combine(
            fixture.StageDirectory,
            fixture.Target.EntryPoint.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(entryPoint, "mutated");

        Assert.Throws<ReleaseInputException>(() => fixture.Seal("release"));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "release")));
    }

    [Fact]
    public void SignedTreeRejectsPreparedBundleMetadataMutation()
    {
        using var fixture = new ReleaseTestFixture("osx-arm64");
        fixture.Prepare();
        string plist = Path.Combine(
            fixture.StageDirectory,
            "Flowspan.app",
            "Contents",
            "Info.plist");
        File.AppendAllText(plist, "mutated");

        Assert.Throws<ReleaseInputException>(() =>
            ReleaseSealer.ComputeSignedTreeSha256(fixture.StageDirectory));
    }

    [Fact]
    public void StageAndOutputOverlapIsRejectedWithoutStageMutation()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = Path.Combine(fixture.StageDirectory, "output");

        Assert.Throws<ReleaseInputException>(() => ReleaseSealer.Seal(
            fixture.StageDirectory,
            output,
            fixture.LockFilePath,
            fixture.RuntimeLockFilePath,
            fixture.GlobalPackagesPath,
            SignatureStates.UnsignedTestArtifact));

        Assert.False(Directory.Exists(output));
        Assert.True(File.Exists(Path.Combine(
            fixture.StageDirectory,
            ReleaseContextCodec.StageMetadataFileName)));
    }

    [Fact]
    public void CompanionTamperIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string updatePath = Assert.Single(Directory.GetFiles(output, "*.update.json"));
        File.AppendAllText(updatePath, " ");

        Assert.Throws<ReleaseInputException>(() =>
            ReleaseVerifier.VerifyDirectory(output));
    }

    [Fact]
    public void RehashedProvenanceSourceTamperIsRejected()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(
            Directory.GetFiles(output, "*.provenance.json"));
        JsonObject record = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        JsonObject predicate = CanonicalJson.ReadObject(record, "predicate");
        JsonObject definition = CanonicalJson.ReadObject(
            predicate,
            "buildDefinition");
        JsonArray dependencies = CanonicalJson.ReadArray(
            definition,
            "resolvedDependencies");
        JsonObject dependency = Assert.IsType<JsonObject>(Assert.Single(dependencies));
        JsonObject digest = CanonicalJson.ReadObject(dependency, "digest");
        digest["gitCommit"] = new string('f', 40);
        File.WriteAllBytes(path, CanonicalJson.Encode(record));
        RewriteChecksums(output);

        Assert.Throws<ReleaseInputException>(() =>
            ReleaseVerifier.VerifyDirectory(output));
    }

    [Fact]
    public void RehashedSpdxDependencyTamperIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.spdx.json"));
        JsonObject record = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        JsonArray packages = CanonicalJson.ReadArray(record, "packages");
        JsonObject dependency = Assert.IsType<JsonObject>(packages[1]);
        JsonArray checksums = CanonicalJson.ReadArray(dependency, "checksums");
        JsonObject checksum = Assert.IsType<JsonObject>(Assert.Single(checksums));
        checksum["checksumValue"] = new string('e', 64);
        File.WriteAllBytes(path, CanonicalJson.Encode(record));
        RewriteChecksums(output);

        Assert.Throws<ReleaseInputException>(() =>
            ReleaseVerifier.VerifyDirectory(output));
    }

    [Fact]
    public void RehashedInvalidDeclaredLicenseExpressionIsRejected()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.licenses.json"));
        JsonObject record = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        JsonObject package = CanonicalJson.ReadArray(record, "packages")
            .OfType<JsonObject>()
            .Single(value => CanonicalJson.ReadString(value, "id") == "Example.Package");
        package["licenseExpression"] = "MIT OR";
        File.WriteAllBytes(path, CanonicalJson.Encode(record));
        RewriteChecksums(output);

        Assert.Throws<ReleaseInputException>(() =>
            ReleaseVerifier.VerifyDirectory(output));
    }

    [Fact]
    public void RehashedLicenseReviewStatusMismatchIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.licenses.json"));
        JsonObject record = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        JsonObject package = CanonicalJson.ReadArray(record, "packages")
            .OfType<JsonObject>()
            .Single(value => CanonicalJson.ReadString(value, "id") == "Example.Package");
        package["reviewStatus"] = "missing-review-required";
        File.WriteAllBytes(path, CanonicalJson.Encode(record));
        RewriteChecksums(output);

        Assert.Throws<ReleaseInputException>(() =>
            ReleaseVerifier.VerifyDirectory(output));
    }

    [Fact]
    public void ArchiveByteTamperIsRejectedByBoundedVerifier()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string archivePath = Assert.Single(Directory.GetFiles(output, "*.tar.gz"));
        _ = ArchiveVerifier.Verify(
            archivePath,
            SignatureStates.UnsignedTestArtifact);
        TamperGzipPayload(archivePath);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            archivePath,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void ZipSymlinkTypeIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string archivePath = Assert.Single(Directory.GetFiles(output, "*.zip"));
        using (FileStream stream = File.Open(archivePath, FileMode.Open))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.Entries[0].ExternalAttributes = (0xA000 | 420) << 16;
        }

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            archivePath,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void ZipTimestampDriftIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string archivePath = Assert.Single(Directory.GetFiles(output, "*.zip"));
        using (FileStream stream = File.Open(archivePath, FileMode.Open))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.Entries[0].LastWriteTime =
                archive.Entries[0].LastWriteTime.AddSeconds(2);
        }

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            archivePath,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void ZipFileModeDriftIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string archivePath = Assert.Single(Directory.GetFiles(output, "*.zip"));
        using (FileStream stream = File.Open(archivePath, FileMode.Open))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.Entries[0].ExternalAttributes = (0x8000 | 384) << 16;
        }

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            archivePath,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void ZipArchiveCommentIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.zip"));
        byte[] original = File.ReadAllBytes(path);
        ReadOnlySpan<byte> signature = [0x50, 0x4b, 0x05, 0x06];
        int eocd = original.AsSpan().LastIndexOf(signature);
        Assert.Equal(original.Length - 22, eocd);
        Array.Resize(ref original, original.Length + 1);
        original[eocd + 20] = 1;
        original[^1] = (byte)'x';
        File.WriteAllBytes(path, original);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void GzipHeaderTimestampIsRejected()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.tar.gz"));
        byte[] archive = File.ReadAllBytes(path);
        archive[4] = 1;
        File.WriteAllBytes(path, archive);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void TarOwnerDriftIsRejected()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.tar.gz"));
        RepackAsUstar(path, changeOwner: true, changeTime: false);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void TarTimestampDriftIsRejected()
    {
        using var fixture = new ReleaseTestFixture("osx-arm64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.tar.gz"));
        RepackAsUstar(path, changeOwner: false, changeTime: true);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void PaxTarFormatIsRejected()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string archivePath = Assert.Single(Directory.GetFiles(output, "*.tar.gz"));
        RepackAsPax(archivePath);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            archivePath,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void MalformedArchiveIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        string path = Path.Combine(fixture.Root, "malformed.zip");
        File.WriteAllBytes(path, [0x50, 0x4b, 0x03, 0x04]);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void OversizedDeclaredTarEntryIsRejectedBeforeExtraction()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        string path = Path.Combine(fixture.Root, "oversized.tar.gz");
        CreateOversizedTarGzip(path);

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void DuplicateArchivePathIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        string path = Path.Combine(fixture.Root, "duplicate.zip");
        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            AddDuplicateEntry(archive, fixture.Context, "Flowspan/payload");
            AddDuplicateEntry(archive, fixture.Context, "Flowspan/payload");
        }

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void ExtraArchiveFileIsRejected()
    {
        using var fixture = new ReleaseTestFixture("win-x64");
        fixture.Prepare();
        string output = fixture.Seal("release");
        string path = Assert.Single(Directory.GetFiles(output, "*.zip"));
        using (FileStream stream = File.Open(path, FileMode.Open))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            AddDuplicateEntry(archive, fixture.Context, "Flowspan/extra.dat");
        }

        Assert.Throws<ReleaseInputException>(() => ArchiveVerifier.Verify(
            path,
            SignatureStates.UnsignedTestArtifact));
    }

    [Fact]
    public void VerifiedSealRequiresTreeBoundSignatureReport()
    {
        using var fixture = new ReleaseTestFixture("osx-arm64");
        fixture.Prepare();
        string codeSignature = Path.Combine(
            fixture.StageDirectory,
            "Flowspan.app",
            "Contents",
            "_CodeSignature");
        Directory.CreateDirectory(codeSignature);
        File.WriteAllText(Path.Combine(codeSignature, "CodeResources"), "signed");
        string entryPoint = Path.Combine(
            fixture.StageDirectory,
            fixture.Target.EntryPoint.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(entryPoint, "signed-entry-point");
        string treeSha256 = ReleaseSealer.ComputeSignedTreeSha256(
            fixture.StageDirectory);
        string reportPath = Path.Combine(fixture.Root, "signature.json");
        File.WriteAllBytes(reportPath, SignatureReport.Create(
            fixture.Context,
            treeSha256,
            "test-provider",
            "test-signer",
            "test-verifier/1",
            fixture.Context.SourceTimestamp,
            new string('b', 64)));
        string output = Path.Combine(fixture.Root, "signed");

        PackageRecordSet records = ReleaseSealer.Seal(
            fixture.StageDirectory,
            output,
            fixture.LockFilePath,
            fixture.RuntimeLockFilePath,
            fixture.GlobalPackagesPath,
            SignatureStates.Verified,
            reportPath);

        Assert.Contains("signed", records.ArchiveFileName, StringComparison.Ordinal);
        ReleaseVerifier.VerifyDirectory(output);
    }

    [Fact]
    public void MismatchedSignatureReportIsRejectedWithoutOutput()
    {
        using var fixture = new ReleaseTestFixture("linux-x64");
        fixture.Prepare();
        string reportPath = Path.Combine(fixture.Root, "signature.json");
        File.WriteAllBytes(reportPath, SignatureReport.Create(
            fixture.Context,
            new string('c', 64),
            "test-provider",
            "test-signer",
            "test-verifier/1",
            fixture.Context.SourceTimestamp,
            new string('d', 64)));
        string output = Path.Combine(fixture.Root, "signed");

        Assert.Throws<ReleaseInputException>(() => ReleaseSealer.Seal(
            fixture.StageDirectory,
            output,
            fixture.LockFilePath,
            fixture.RuntimeLockFilePath,
            fixture.GlobalPackagesPath,
            SignatureStates.Verified,
            reportPath));
        Assert.False(Directory.Exists(output));
    }

    private static void CreateOversizedTarGzip(string path)
    {
        byte[] header = new byte[512];
        Encoding.ASCII.GetBytes("flowspan/oversized.bin").CopyTo(header, 0);
        WriteTarOctal(header, 100, 8, 420);
        WriteTarOctal(header, 108, 8, 0);
        WriteTarOctal(header, 116, 8, 0);
        WriteTarOctal(
            header,
            124,
            12,
            ReleaseBounds.MaximumFileBytes + 1);
        WriteTarOctal(header, 136, 12, 1785196800);
        header.AsSpan(148, 8).Fill((byte)' ');
        header[156] = (byte)'0';
        Encoding.ASCII.GetBytes("ustar\0").CopyTo(header, 257);
        Encoding.ASCII.GetBytes("00").CopyTo(header, 263);
        int checksum = header.Sum(static value => value);
        string encodedChecksum = Convert.ToString(checksum, 8)!.PadLeft(6, '0');
        Encoding.ASCII.GetBytes(encodedChecksum).CopyTo(header, 148);
        header[154] = 0;
        header[155] = (byte)' ';

        using FileStream output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize);
        gzip.Write(header);
        gzip.Write(new byte[1024]);
    }

    private static void WriteTarOctal(
        byte[] header,
        int offset,
        int length,
        long value)
    {
        string encoded = Convert.ToString(value, 8)!.PadLeft(length - 1, '0');
        Encoding.ASCII.GetBytes(encoded).CopyTo(header, offset);
        header[offset + length - 1] = 0;
    }

    private static void RepackAsUstar(
        string archivePath,
        bool changeOwner,
        bool changeTime)
    {
        var entries = new List<(
            string Name,
            byte[] Data,
            UnixFileMode Mode,
            DateTimeOffset Modified)>();
        using (FileStream source = File.OpenRead(archivePath))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var reader = new TarReader(gzip))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                using var content = new MemoryStream();
                entry.DataStream!.CopyTo(content);
                entries.Add((entry.Name, content.ToArray(), entry.Mode,
                    entry.ModificationTime));
            }
        }

        string replacement = archivePath + ".ustar";
        using (FileStream output = File.Create(replacement))
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar))
        {
            for (int index = 0; index < entries.Count; index++)
            {
                var value = entries[index];
                using var content = new MemoryStream(value.Data);
                var entry = new UstarTarEntry(TarEntryType.RegularFile, value.Name)
                {
                    DataStream = content,
                    Mode = value.Mode,
                    Uid = index == 0 && changeOwner ? 1 : 0,
                    Gid = 0,
                    UserName = string.Empty,
                    GroupName = string.Empty,
                    ModificationTime = index == 0 && changeTime
                        ? value.Modified.AddSeconds(1)
                        : value.Modified,
                };
                writer.WriteEntry(entry);
            }
        }

        File.Move(replacement, archivePath, overwrite: true);
    }

    private static void RepackAsPax(string archivePath)
    {
        var entries = new List<(
            string Name,
            byte[] Data,
            UnixFileMode Mode,
            DateTimeOffset Modified)>();
        using (FileStream source = File.OpenRead(archivePath))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var reader = new TarReader(gzip))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                using var content = new MemoryStream();
                entry.DataStream!.CopyTo(content);
                entries.Add((
                    entry.Name,
                    content.ToArray(),
                    entry.Mode,
                    entry.ModificationTime));
            }
        }

        string replacement = archivePath + ".pax";
        using (FileStream output = File.Create(replacement))
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            foreach ((string name, byte[] data, UnixFileMode mode,
                DateTimeOffset modified) in entries)
            {
                using var content = new MemoryStream(data);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = content,
                    Mode = mode,
                    ModificationTime = modified,
                };
                writer.WriteEntry(entry);
            }
        }

        File.Move(replacement, archivePath, overwrite: true);
    }

    private static void AddDuplicateEntry(
        ZipArchive archive,
        ReleaseContext context,
        string path)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        entry.ExternalAttributes = (0x8000 | 420) << 16;
        entry.LastWriteTime = context.SourceTimestamp;
        using Stream content = entry.Open();
        content.WriteByte(1);
    }

    private static void RewriteChecksums(string output)
    {
        var content = new StringBuilder();
        foreach (string path in Directory.GetFiles(output)
            .Where(path => Path.GetFileName(path) != "SHA256SUMS")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            content.Append(ReleaseHash.Sha256File(path))
                .Append("  ")
                .Append(Path.GetFileName(path))
                .Append('\n');
        }

        File.WriteAllText(
            Path.Combine(output, "SHA256SUMS"),
            content.ToString(),
            new UTF8Encoding(false));
    }

    private static void TamperGzipPayload(string archivePath)
    {
        byte[] uncompressed;
        using (FileStream source = File.OpenRead(archivePath))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var content = new MemoryStream())
        {
            gzip.CopyTo(content);
            uncompressed = content.ToArray();
        }

        byte[] canary = Encoding.UTF8.GetBytes("flowspan-release-payload");
        int index = uncompressed.AsSpan().IndexOf(canary);
        Assert.True(index >= 0);
        uncompressed[index] ^= 0x20;
        using FileStream destination = new(
            archivePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using var output = new GZipStream(
            destination,
            CompressionLevel.SmallestSize);
        output.Write(uncompressed);
    }
}
