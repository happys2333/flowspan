using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class CapabilitySnapshotTests
{
    [Fact]
    public void SnapshotRequiresExplicitStatusForEveryCapability()
    {
        PlatformCapabilityStatus[] incomplete =
        [
            PlatformCapabilityStatus.Create(
                PlatformCapability.ScreenCapture,
                CapabilityAvailability.Available,
                "capture.available"),
        ];

        Assert.Throws<ArgumentException>(() => new PlatformCapabilitySnapshot(
            PlatformFamily.MacOS,
            DateTimeOffset.UtcNow,
            incomplete));
    }

    [Fact]
    public void PermissionAndDegradationRequireRecoveryAction()
    {
        Assert.Throws<ArgumentException>(() => PlatformCapabilityStatus.Create(
            PlatformCapability.ScreenCapture,
            CapabilityAvailability.PermissionRequired,
            "capture.permission_required"));
        Assert.Throws<ArgumentException>(() => PlatformCapabilityStatus.Create(
            PlatformCapability.RemoteInput,
            CapabilityAvailability.Degraded,
            "input.x11_degraded"));
    }

    [Fact]
    public void CompleteSnapshotPreservesNamedDegradation()
    {
        PlatformCapabilityStatus[] statuses = Enum.GetValues<PlatformCapability>()
            .Select(capability => capability == PlatformCapability.RemoteInput
                ? PlatformCapabilityStatus.Create(
                    capability,
                    CapabilityAvailability.Degraded,
                    "input.x11_degraded",
                    "Switch to a Wayland RemoteDesktop portal session.")
                : PlatformCapabilityStatus.Create(
                    capability,
                    CapabilityAvailability.Available,
                    "capability.available"))
            .ToArray();

        var snapshot = new PlatformCapabilitySnapshot(
            PlatformFamily.Linux,
            DateTimeOffset.UtcNow,
            statuses);

        PlatformCapabilityStatus input = snapshot.Get(PlatformCapability.RemoteInput);
        Assert.Equal(CapabilityAvailability.Degraded, input.Availability);
        Assert.Equal("input.x11_degraded", input.ReasonCode);
        Assert.NotNull(input.RecoveryAction);
    }
}
