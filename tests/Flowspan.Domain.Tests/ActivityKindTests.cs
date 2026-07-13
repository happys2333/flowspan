using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class ActivityKindTests
{
    [Theory]
    [InlineData("workspace.note/v1")]
    [InlineData("web.page/v2")]
    [InlineData("file-reference/v10")]
    public void ValidKindIsAccepted(string value)
    {
        ActivityKind kind = ActivityKind.Parse(value);

        Assert.Equal(value, kind.Value);
    }

    [Theory]
    [InlineData("Workspace.Note/v1")]
    [InlineData("workspace_note/v1")]
    [InlineData("workspace.note/1")]
    [InlineData("workspace.note/v0")]
    [InlineData("workspace.note/v01")]
    [InlineData("workspace.note/v+1")]
    [InlineData("workspace.note/v10000")]
    public void MalformedKindIsRejected(string value)
    {
        Assert.Throws<FormatException>(() => ActivityKind.Parse(value));
    }

    [Fact]
    public void EmptyKindIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ActivityKind.Parse(string.Empty));
    }
}
