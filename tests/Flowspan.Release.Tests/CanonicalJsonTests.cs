using System.Text;
using System.Text.Json.Nodes;
using Flowspan.Release;

namespace Flowspan.Release.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void EncodeUsesLfLineEndings()
    {
        byte[] encoded = CanonicalJson.Encode(new JsonObject
        {
            ["value"] = "example",
        });

        Assert.Equal(
            "{\n  \"value\": \"example\"\n}\n",
            Encoding.UTF8.GetString(encoded));
    }
}
