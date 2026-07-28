using System.Globalization;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public static partial class ReleaseVerifier
{
    private static void VerifyProvenanceSubject(
        JsonObject value,
        UpdateEvidence expected)
    {
        CanonicalJson.RequireProperties(value, "name", "digest");
        JsonObject digest = CanonicalJson.ReadObject(value, "digest");
        CanonicalJson.RequireProperties(digest, "sha256");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "name"),
                expected.ArchiveName)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(digest, "sha256"),
                expected.ArchiveSha256))
        {
            throw new ReleaseInputException(
                "The release provenance subject is inconsistent.");
        }
    }

    private static void VerifyProvenancePredicate(
        JsonObject value,
        UpdateEvidence expected,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(value, "buildDefinition", "runDetails");
        VerifyProvenanceBuildDefinition(
            CanonicalJson.ReadObject(value, "buildDefinition"),
            expected,
            context);
        VerifyProvenanceRunDetails(
            CanonicalJson.ReadObject(value, "runDetails"),
            context);
    }

    private static void VerifyProvenanceBuildDefinition(
        JsonObject value,
        UpdateEvidence expected,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(
            value,
            "buildType",
            "externalParameters",
            "internalParameters",
            "resolvedDependencies");
        if (!StringComparer.Ordinal.Equals(
            CanonicalJson.ReadString(value, "buildType"),
            "https://flowspan.io/build-types/dotnet-desktop/v1"))
        {
            throw new ReleaseInputException(
                "The release provenance build type is invalid.");
        }

        JsonObject internalParameters = CanonicalJson.ReadObject(
            value,
            "internalParameters");
        if (internalParameters.Count != 0)
        {
            throw new ReleaseInputException(
                "The release provenance internal parameters are not empty.");
        }

        VerifyProvenanceParameters(
            CanonicalJson.ReadObject(value, "externalParameters"),
            expected,
            context);
        VerifyProvenanceDependencies(
            CanonicalJson.ReadArray(value, "resolvedDependencies"),
            context);
    }

    private static void VerifyProvenanceParameters(
        JsonObject value,
        UpdateEvidence expected,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(
            value,
            "version",
            "rid",
            "channel",
            "signatureState",
            "sourceDateEpoch");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "version"),
                expected.Version)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "rid"),
                expected.Rid)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "channel"),
                context.Channel)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "signatureState"),
                expected.SignatureState)
            || CanonicalJson.ReadInt64(value, "sourceDateEpoch")
                != context.SourceTimestamp.ToUnixTimeSeconds())
        {
            throw new ReleaseInputException(
                "The release provenance parameters are inconsistent.");
        }
    }

    private static void VerifyProvenanceDependencies(
        JsonArray values,
        ReleaseContext context)
    {
        if (values.Count != 1 || values[0] is not JsonObject dependency)
        {
            throw new ReleaseInputException(
                "The release provenance source dependency is missing.");
        }

        CanonicalJson.RequireProperties(dependency, "uri", "digest");
        JsonObject digest = CanonicalJson.ReadObject(dependency, "digest");
        CanonicalJson.RequireProperties(digest, "gitCommit");
        string expectedUri = $"git+{context.Repository.AbsoluteUri}@{context.Commit}";
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(dependency, "uri"),
                expectedUri)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(digest, "gitCommit"),
                context.Commit))
        {
            throw new ReleaseInputException(
                "The release provenance source dependency is inconsistent.");
        }
    }

    private static void VerifyProvenanceRunDetails(
        JsonObject value,
        ReleaseContext context)
    {
        CanonicalJson.RequireProperties(value, "builder", "metadata");
        JsonObject builder = CanonicalJson.ReadObject(value, "builder");
        CanonicalJson.RequireProperties(builder, "id");
        JsonObject metadata = CanonicalJson.ReadObject(value, "metadata");
        CanonicalJson.RequireProperties(
            metadata,
            "invocationId",
            "startedOn",
            "finishedOn");
        string expectedTime = context.SourceTimestamp.ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(builder, "id"),
                context.BuilderId)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(metadata, "invocationId"),
                context.InvocationId)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(metadata, "startedOn"),
                expectedTime)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(metadata, "finishedOn"),
                expectedTime))
        {
            throw new ReleaseInputException(
                "The release provenance run details are inconsistent.");
        }
    }
}
