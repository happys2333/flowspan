using Avalonia;

namespace Flowspan.Desktop;

internal static class Program
{
    private const string ValidateCompositionArgument = "--validate-composition";

    [STAThread]
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 1
            && StringComparer.Ordinal.Equals(args[0], ValidateCompositionArgument))
        {
            return ValidateCompositionAsync().GetAwaiter().GetResult();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect();

    private static async Task<int> ValidateCompositionAsync()
    {
        await using WorkspaceShellViewModel viewModel =
            DesktopCompositionRoot.CreateValidation();
        await viewModel.InitializeAsync().ConfigureAwait(false);

        bool valid = viewModel.IsIdentityAvailable
            && viewModel.IsTestMode
            && !viewModel.IsStartupBlocked
            && !viewModel.IsEmergencyStopAvailable
            && !viewModel.Pairing.HasPendingPrompt
            && viewModel.TrustedDevices.IsTrustAvailable
            && viewModel.TrustedDevices.IsEmpty
            && viewModel.IdentityProtection.Contains(
                "TEST MODE",
                StringComparison.Ordinal)
            && viewModel.TrustedDevices.Protection.Contains(
                "TEST MODE",
                StringComparison.Ordinal);

        Console.WriteLine(valid
            ? "Flowspan desktop composition validation passed in explicit TEST MODE."
            : "Flowspan desktop composition validation failed.");
        return valid ? 0 : 1;
    }
}
