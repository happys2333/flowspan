namespace Flowspan.Desktop;

public enum DesktopPlatformFamily
{
    Windows,
    MacOS,
    Linux,
}

public sealed record DesktopLocalNetworkPermissionGuide
{
    private DesktopLocalNetworkPermissionGuide(
        DesktopPlatformFamily platform,
        string platformName,
        string promptExpectation,
        string revocationAction)
    {
        Platform = platform;
        PlatformName = platformName;
        Purpose = DesktopText.Get("LocalNetworkGuide_Purpose");
        DataExposure = DesktopText.Get("LocalNetworkGuide_DataExposure");
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
                DesktopText.Get("LocalNetworkGuide_WindowsName"),
                DesktopText.Get("LocalNetworkGuide_WindowsPrompt"),
                DesktopText.Get("LocalNetworkGuide_WindowsRevocation")),
            DesktopPlatformFamily.MacOS => new(
                platform,
                DesktopText.Get("LocalNetworkGuide_MacOSName"),
                DesktopText.Get("LocalNetworkGuide_MacOSPrompt"),
                DesktopText.Get("LocalNetworkGuide_MacOSRevocation")),
            DesktopPlatformFamily.Linux => new(
                platform,
                DesktopText.Get("LocalNetworkGuide_LinuxName"),
                DesktopText.Get("LocalNetworkGuide_LinuxPrompt"),
                DesktopText.Get("LocalNetworkGuide_LinuxRevocation")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(platform),
                platform,
                "The desktop platform family is not supported."),
        };
}
