using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopIdentityStartupTests
{
    [Fact]
    public async Task InitializeAsyncCachesOneExplicitlyDegradedIdentity()
    {
        using var store = new InMemoryDeviceIdentityStore();
        using var startup = new DesktopIdentityStartup(store, "Test workstation");

        LocalIdentitySnapshot first = await startup.InitializeAsync();
        LocalIdentitySnapshot second = await startup.InitializeAsync();

        Assert.Equal("Test workstation", first.DisplayName);
        Assert.Equal(first.DeviceId, second.DeviceId);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.True(first.IsTestMode);
        Assert.Contains("TEST MODE", first.ProtectionLabel, StringComparison.Ordinal);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void DescribeFailureRedactsExceptionDetails()
    {
        const string canary = "CANARY_PRIVATE_IDENTITY_PAYLOAD";

        DesktopStartupFailure failure = DesktopIdentityStartup.DescribeFailure(
            new IOException(canary));

        Assert.Equal("identity.credential_store_unavailable", failure.ReasonCode);
        Assert.DoesNotContain(canary, failure.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, failure.RecoveryAction, StringComparison.Ordinal);
    }
}
