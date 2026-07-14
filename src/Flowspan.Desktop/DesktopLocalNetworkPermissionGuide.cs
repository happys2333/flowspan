namespace Flowspan.Desktop;

public enum DesktopPlatformFamily
{
    Windows,
    MacOS,
    Linux,
}

public sealed record DesktopLocalNetworkPermissionGuide
{
    private const string SharedPurpose =
        "Flowspan uses the local network only after you enable it to discover, pair, and authenticate nearby Flowspan computers.";

    private const string SharedDataExposure =
        "Visible on this local network: device name, Device ID, identity fingerprint, TCP listener port, supported protocol versions, issue and expiry times, a short-lived nonce, and an identity signature. Activity content and Capability grants are not advertised.";

    private DesktopLocalNetworkPermissionGuide(
        DesktopPlatformFamily platform,
        string platformName,
        string promptExpectation,
        string revocationAction)
    {
        Platform = platform;
        PlatformName = platformName;
        Purpose = SharedPurpose;
        DataExposure = SharedDataExposure;
        PromptExpectation = promptExpectation;
        RevocationAction = revocationAction;
    }

    public string DataExposure { get; }

    public DesktopPlatformFamily Platform { get; }

    public string PlatformName { get; }

    public string PromptExpectation { get; }

    public string Purpose { get; }

    public string RevocationAction { get; }

    public static DesktopLocalNetworkPermissionGuide ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return ForPlatform(DesktopPlatformFamily.Windows);
        }

        if (OperatingSystem.IsMacOS())
        {
            return ForPlatform(DesktopPlatformFamily.MacOS);
        }

        if (OperatingSystem.IsLinux())
        {
            return ForPlatform(DesktopPlatformFamily.Linux);
        }

        throw new PlatformNotSupportedException(
            "The desktop shell supports Windows, macOS, and Linux only.");
    }

    public static DesktopLocalNetworkPermissionGuide ForPlatform(
        DesktopPlatformFamily platform) => platform switch
        {
            DesktopPlatformFamily.Windows => new(
                platform,
                "Windows",
                "Windows Firewall may ask whether Flowspan can communicate on private networks. Do not allow public networks unless you intentionally accept that exposure.",
                "Disable local networking in Flowspan first. Then remove Flowspan in Windows Security > Firewall & network protection > Allow an app through firewall."),
            DesktopPlatformFamily.MacOS => new(
                platform,
                "macOS",
                "macOS may ask for Local Network access when discovery starts. Denying it keeps local discovery and trusted reconnect unavailable.",
                "Disable local networking in Flowspan first. Then turn off Flowspan in System Settings > Privacy & Security > Local Network."),
            DesktopPlatformFamily.Linux => new(
                platform,
                "Linux",
                "Linux has no single standard application prompt. A firewall, sandbox, or network policy may block multicast DNS or the local TCP listener.",
                "Disable local networking in Flowspan first. Then remove any Flowspan firewall rule or sandbox network grant using your distribution's settings."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(platform),
                platform,
                "The desktop platform family is not supported."),
        };
}
