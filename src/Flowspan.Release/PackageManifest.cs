using System.Text.Json.Nodes;

namespace Flowspan.Release;

public sealed record PackageFileRecord(
    string Path,
    long Length,
    int Mode,
    string Sha256);

public sealed record PackageManifestResult(
    byte[] Encoded,
    string SignedTreeSha256,
    IReadOnlyList<PackageFileRecord> Files);

public static class PackageManifest
{
    public const string FileName = "flowspan-package.json";
    public const string SignatureReportFileName = "flowspan-signature.json";
    private const string Schema = "flowspan.package/v1";

    public static string ComputeSignedTreeSha256(
        string packageRoot,
        ReleaseContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(context);
        string stageRoot = Path.GetDirectoryName(Path.GetFullPath(packageRoot))
            ?? throw new ReleaseInputException(
                "The release package root has no stage parent.");
        string reportPath = $"{context.Target.RootName}/{SignatureReportFileName}";
        IReadOnlyList<PackageFileRecord> files = ReleaseTree
            .EnumerateFiles(packageRoot)
            .Select(file => CreateFileRecord(
                file,
                ReleaseTree.NormalizeRelativePath(
                    Path.GetRelativePath(stageRoot, file.FullPath)),
                context))
            .Where(file => !StringComparer.Ordinal.Equals(file.Path, reportPath))
            .ToArray();
        if (files.Any(file => file.Path.EndsWith('/' + FileName, StringComparison.Ordinal)))
        {
            throw new ReleaseInputException(
                "The release package already contains a package manifest.");
        }

        return ReleaseHash.Sha256Bytes(CanonicalJson.Encode(CreateFilesArray(files)));
    }

    public static PackageManifestResult Create(
        string packageRoot,
        ReleaseContext context,
        string signatureState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(context);
        RequireSignatureState(signatureState);

        string stageRoot = Path.GetDirectoryName(Path.GetFullPath(packageRoot))
            ?? throw new ReleaseInputException(
                "The release package root has no stage parent.");
        IReadOnlyList<ReleaseTreeFile> treeFiles =
            ReleaseTree.EnumerateFiles(packageRoot);
        var payloadFiles = new List<PackageFileRecord>(treeFiles.Count);
        foreach (ReleaseTreeFile file in treeFiles)
        {
            string path = ReleaseTree.NormalizeRelativePath(
                Path.GetRelativePath(stageRoot, file.FullPath));
            if (path.EndsWith('/' + FileName, StringComparison.Ordinal))
            {
                throw new ReleaseInputException(
                    "The release package already contains a package manifest.");
            }

            payloadFiles.Add(CreateFileRecord(file, path, context));
        }

        string reportPath = $"{context.Target.RootName}/{SignatureReportFileName}";
        PackageFileRecord? report = payloadFiles.SingleOrDefault(file =>
            StringComparer.Ordinal.Equals(file.Path, reportPath));
        IReadOnlyList<PackageFileRecord> signedTreeFiles = payloadFiles
            .Where(file => !StringComparer.Ordinal.Equals(file.Path, reportPath))
            .ToArray();
        string signedTreeSha256 = ReleaseHash.Sha256Bytes(
            CanonicalJson.Encode(CreateFilesArray(signedTreeFiles)));

        if (signatureState == SignatureStates.UnsignedTestArtifact && report is not null)
        {
            throw new ReleaseInputException(
                "An unsigned test package cannot contain a signature report.");
        }

        if (signatureState == SignatureStates.Verified)
        {
            if (report is null)
            {
                throw new ReleaseInputException(
                    "A verified package requires a signature report.");
            }

            SignatureReport.Verify(
                Path.Combine(packageRoot, SignatureReportFileName),
                context,
                signedTreeSha256);
        }

        JsonArray files = CreateFilesArray(payloadFiles);
        byte[] encoded = CanonicalJson.Encode(new JsonObject
        {
            ["schema"] = Schema,
            ["product"] = "Flowspan",
            ["version"] = context.Version,
            ["buildVersion"] = context.BuildVersion,
            ["commit"] = context.Commit,
            ["repository"] = context.Repository.AbsoluteUri,
            ["rid"] = context.Target.Rid,
            ["sourceDateEpoch"] = context.SourceTimestamp.ToUnixTimeSeconds(),
            ["channel"] = context.Channel,
            ["minimumVersion"] = context.MinimumVersion,
            ["downloadBase"] = context.DownloadBase.AbsoluteUri,
            ["builderId"] = context.BuilderId,
            ["invocationId"] = context.InvocationId,
            ["signatureState"] = signatureState,
            ["entryPoint"] = context.Target.EntryPoint,
            ["signedTreeSha256"] = signedTreeSha256,
            ["files"] = files,
        });
        return new PackageManifestResult(
            encoded,
            signedTreeSha256,
            payloadFiles);
    }

    private static PackageFileRecord CreateFileRecord(
        ReleaseTreeFile file,
        string path,
        ReleaseContext context)
    {
        int mode = StringComparer.Ordinal.Equals(path, context.Target.EntryPoint)
            ? 493
            : 420;
        return new PackageFileRecord(
            path,
            file.Length,
            mode,
            ReleaseHash.Sha256File(file.FullPath));
    }

    private static JsonArray CreateFilesArray(
        IEnumerable<PackageFileRecord> files)
    {
        var result = new JsonArray();
        foreach (PackageFileRecord file in files.OrderBy(
            static file => file.Path,
            StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["path"] = file.Path,
                ["length"] = file.Length,
                ["mode"] = file.Mode,
                ["sha256"] = file.Sha256,
            });
        }

        return result;
    }

    private static void RequireSignatureState(string signatureState)
    {
        if (signatureState is not SignatureStates.UnsignedTestArtifact
            and not SignatureStates.Verified)
        {
            throw new ReleaseInputException(
                "The release signature state is unsupported.");
        }
    }
}
