using Avalonia;
using Avalonia.Headless;

namespace Flowspan.Desktop.Tests;

internal static class RemoteWindowViewerHeadlessApp
{
    // Pixel ownership needs the real Skia bitmap implementation. The ordinary
    // layout fixture deliberately uses Avalonia's headless drawing substitute.
    // Keep the dispatcher process-scoped, as with the assembly headless fixture,
    // and rebuild the application for each dispatched test.
    private static readonly Lazy<HeadlessUnitTestSession> SharedSession = new(
        () => HeadlessUnitTestSession.StartNew(
            typeof(RemoteWindowViewerHeadlessApp),
            AvaloniaTestIsolationLevel.PerTest));

    public static HeadlessUnitTestSession Session => SharedSession.Value;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false,
        });
}
