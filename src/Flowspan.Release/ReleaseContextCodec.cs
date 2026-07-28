using System.Text.Json.Nodes;

namespace Flowspan.Release;

public sealed record PreparedStage(
    ReleaseContext Context,
    IReadOnlyList<PackageFileRecord> Files);

public static class ReleaseContextCodec
{
    public const string StageMetadataFileName = ".flowspan-stage.json";
    private const string Schema = "flowspan.stage/v2";

    public static byte[] Encode(
        ReleaseContext context,
        string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        return CanonicalJson.Encode(new JsonObject
        {
            ["schema"] = Schema,
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
            ["files"] = EncodeFiles(context, packageRoot),
        });
    }

    public static PreparedStage Decode(ReadOnlySpan<byte> encoded)
    {
        JsonObject value = CanonicalJson.DecodeObject(encoded);
        CanonicalJson.RequireProperties(
            value,
            "schema",
            "version",
            "buildVersion",
            "commit",
            "repository",
            "rid",
            "sourceDateEpoch",
            "channel",
            "minimumVersion",
            "downloadBase",
            "builderId",
            "invocationId",
            "files");

        if (!StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "schema"),
            Schema))
        {
            throw new ReleaseInputException(
                "The release stage schema is unsupported.");
        }

        ReleaseContext context = ReleaseContext.Create(
            CanonicalJson.ReadString(value, "version"),
            CanonicalJson.ReadString(value, "buildVersion"),
            CanonicalJson.ReadString(value, "commit"),
            CanonicalJson.ReadString(value, "repository"),
            CanonicalJson.ReadString(value, "rid"),
            CanonicalJson.ReadInt64(value, "sourceDateEpoch"),
            CanonicalJson.ReadString(value, "channel"),
            CanonicalJson.ReadString(value, "minimumVersion"),
            CanonicalJson.ReadString(value, "downloadBase"),
            CanonicalJson.ReadString(value, "builderId"),
            CanonicalJson.ReadString(value, "invocationId"));
        return new PreparedStage(
            context,
            DecodeFiles(CanonicalJson.ReadArray(value, "files"), context));
    }

    private static JsonArray EncodeFiles(
        ReleaseContext context,
        string packageRoot)
    {
        string root = Path.GetFullPath(packageRoot);
        string stageRoot = Path.GetDirectoryName(root)
            ?? throw new ReleaseInputException(
                "The release package root has no stage parent.");
        var values = new JsonArray();
        foreach (ReleaseTreeFile file in ReleaseTree.EnumerateFiles(root))
        {
            string path = ReleaseTree.NormalizeRelativePath(
                Path.GetRelativePath(stageRoot, file.FullPath));
            values.Add(new JsonObject
            {
                ["path"] = path,
                ["length"] = file.Length,
                ["mode"] = ExpectedMode(path, context),
                ["sha256"] = ReleaseHash.Sha256File(file.FullPath),
            });
        }

        return values;
    }

    private static List<PackageFileRecord> DecodeFiles(
        JsonArray values,
        ReleaseContext context)
    {
        if (values.Count is 0 or > ReleaseBounds.MaximumFileCount)
        {
            throw new ReleaseInputException(
                "The prepared release file list is empty or oversized.");
        }

        var files = new List<PackageFileRecord>(values.Count);
        string? previousPath = null;
        foreach (JsonNode? node in values)
        {
            if (node is not JsonObject value)
            {
                throw new ReleaseInputException(
                    "A prepared release file record is not an object.");
            }

            CanonicalJson.RequireProperties(
                value,
                "path",
                "length",
                "mode",
                "sha256");
            string path = ReleaseTree.NormalizeRelativePath(
                CanonicalJson.ReadString(value, "path"));
            long length = CanonicalJson.ReadInt64(value, "length");
            long mode = CanonicalJson.ReadInt64(value, "mode");
            string sha256 = CanonicalJson.ReadString(value, "sha256");
            ValidateFileRecord(
                path,
                length,
                mode,
                sha256,
                previousPath,
                context);
            files.Add(new PackageFileRecord(path, length, (int)mode, sha256));
            previousPath = path;
        }

        if (!files.Any(file => StringComparer.Ordinal.Equals(
            file.Path,
            context.Target.EntryPoint)))
        {
            throw new ReleaseInputException(
                "The prepared release entry point is missing.");
        }

        return files;
    }

    private static void ValidateFileRecord(
        string path,
        long length,
        long mode,
        string sha256,
        string? previousPath,
        ReleaseContext context)
    {
        if (!path.StartsWith(
                context.Target.RootName + '/',
                StringComparison.Ordinal)
            || previousPath is not null
                && StringComparer.Ordinal.Compare(previousPath, path) >= 0
            || length is < 0 or > ReleaseBounds.MaximumFileBytes
            || mode != ExpectedMode(path, context)
            || !ReleaseHash.IsLowerSha256(sha256))
        {
            throw new ReleaseInputException(
                "A prepared release file record is invalid or unordered.");
        }
    }

    private static int ExpectedMode(
        string path,
        ReleaseContext context) =>
        StringComparer.Ordinal.Equals(path, context.Target.EntryPoint)
            ? 493
            : 420;
}
