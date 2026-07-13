using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class ActivityDescriptorTests
{
    private static readonly DeviceId Origin =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly ActivityId Activity =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void SamePayloadHasStableDigest()
    {
        string payload = JsonSerializer.Serialize(new { text = "hello" });

        ActivityDescriptor first = Create(payload);
        ActivityDescriptor second = Create(payload);

        Assert.Equal(first.PayloadDigest, second.PayloadDigest);
        Assert.Equal(64, first.PayloadDigest.Length);
        Assert.Equal(first.DescriptorDigest, second.DescriptorDigest);
        Assert.Equal(64, first.DescriptorDigest.Length);
    }

    [Fact]
    public void DescriptorDigestCoversMetadata()
    {
        string payload = JsonSerializer.Serialize(new { text = "hello" });
        ActivityDescriptor first = Create(payload);
        ActivityDescriptor renamed = ActivityDescriptor.Create(
            Activity,
            ActivityKind.Parse("workspace.note/v1"),
            Origin,
            "Different title",
            payload);

        Assert.Equal(first.PayloadDigest, renamed.PayloadDigest);
        Assert.NotEqual(first.DescriptorDigest, renamed.DescriptorDigest);
    }

    [Fact]
    public void StringRepresentationRedactsPayloadAndTitle()
    {
        const string canary = "FLOWSPAN_SECRET_CANARY";
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            Activity,
            ActivityKind.Parse("workspace.note/v1"),
            Origin,
            $"Title {canary}",
            JsonSerializer.Serialize(new { text = canary }));

        string representation = descriptor.ToString();

        Assert.DoesNotContain(canary, representation, StringComparison.Ordinal);
        Assert.Contains(descriptor.DescriptorDigest, representation, StringComparison.Ordinal);
    }

    [Fact]
    public void NonObjectPayloadIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Create("[]"));
    }

    [Fact]
    public void ControlCharactersInTitleAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ActivityDescriptor.Create(
            Activity,
            ActivityKind.Parse("workspace.note/v1"),
            Origin,
            "Unsafe\nTitle",
            "{\"text\":\"hello\"}"));
    }

    [Fact]
    public void OversizedPayloadIsRejectedBeforeUse()
    {
        string payload = JsonSerializer.Serialize(new
        {
            text = new string('a', ActivityDescriptor.MaximumPayloadBytes),
        });

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(payload));
    }

    private static ActivityDescriptor Create(string payload) =>
        ActivityDescriptor.Create(
            Activity,
            ActivityKind.Parse("workspace.note/v1"),
            Origin,
            "Test note",
            payload);
}
