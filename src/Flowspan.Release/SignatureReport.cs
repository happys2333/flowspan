using System.Globalization;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public static class SignatureReport
{
    private const string Schema = "flowspan.signature-verification/v1";

    public static byte[] Create(
        ReleaseContext context,
        string signedTreeSha256,
        string provider,
        string signerIdentity,
        string verificationTool,
        DateTimeOffset verificationTime,
        string evidenceSha256)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireDigest(signedTreeSha256, "signed tree");
        RequireText(provider, "signature provider");
        RequireText(signerIdentity, "signer identity");
        RequireText(verificationTool, "signature verification tool");
        RequireDigest(evidenceSha256, "signature evidence");
        if (verificationTime.Offset != TimeSpan.Zero)
        {
            throw new ReleaseInputException(
                "Signature verification time must be UTC.");
        }

        return CanonicalJson.Encode(new JsonObject
        {
            ["schema"] = Schema,
            ["rid"] = context.Target.Rid,
            ["signedTreeSha256"] = signedTreeSha256,
            ["provider"] = provider,
            ["signerIdentity"] = signerIdentity,
            ["verificationTool"] = verificationTool,
            ["verificationTime"] = verificationTime.ToString("O", CultureInfo.InvariantCulture),
            ["evidenceSha256"] = evidenceSha256,
        });
    }

    public static void Verify(
        string path,
        ReleaseContext context,
        string expectedTreeSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        JsonObject value = CanonicalJson.DecodeObject(File.ReadAllBytes(path));
        CanonicalJson.RequireProperties(
            value,
            "schema",
            "rid",
            "signedTreeSha256",
            "provider",
            "signerIdentity",
            "verificationTool",
            "verificationTime",
            "evidenceSha256");
        if (!StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "schema"),
                Schema)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "rid"),
                context.Target.Rid)
            || !StringComparer.Ordinal.Equals(
                CanonicalJson.ReadString(value, "signedTreeSha256"),
                expectedTreeSha256))
        {
            throw new ReleaseInputException(
                "The signature report does not bind the release stage.");
        }

        RequireText(CanonicalJson.ReadString(value, "provider"), "signature provider");
        RequireText(
            CanonicalJson.ReadString(value, "signerIdentity"),
            "signer identity");
        RequireText(
            CanonicalJson.ReadString(value, "verificationTool"),
            "signature verification tool");
        RequireDigest(
            CanonicalJson.ReadString(value, "evidenceSha256"),
            "signature evidence");

        string timestamp = CanonicalJson.ReadString(value, "verificationTime");
        if (!DateTimeOffset.TryParseExact(
                timestamp,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset parsed)
            || parsed.Offset != TimeSpan.Zero
            || !StringComparer.Ordinal.Equals(
                parsed.ToString("O", CultureInfo.InvariantCulture),
                timestamp))
        {
            throw new ReleaseInputException(
                "The signature verification time is not canonical UTC.");
        }
    }

    private static void RequireDigest(string value, string field)
    {
        if (!ReleaseHash.IsLowerSha256(value))
        {
            throw new ReleaseInputException(
                $"The {field} SHA-256 is invalid.");
        }
    }

    private static void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > ReleaseBounds.MaximumTextLength
            || value.Any(static character => char.IsControl(character)))
        {
            throw new ReleaseInputException(
                $"The {field} is empty, oversized, or contains controls.");
        }
    }
}
