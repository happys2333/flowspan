using System.Runtime.InteropServices;
using N = Flowspan.Platform.MacOS.MacOSRemoteWindowObjectiveCInterop;

namespace Flowspan.MacOS.NativeCaptureProbe;

// AppKit bootstrap belongs only to this standalone process. The platform
// library must never create NSApplication or run the host application's loop.
internal sealed partial class TaskWindow : IDisposable
{
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private nint window;

    internal TaskWindow()
    {
        _ = NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
        Program.Require(NSApplicationLoad() != 0, "tool_appkit_bootstrap");
        nint application = N.Send0(N.objc_getClass("NSApplication"), N.Sel("sharedApplication"));
        Program.Require(application != 0, "tool_application_created");
        N.SendNInt(application, N.Sel("setActivationPolicy:"), 2);
        window = InitWindow(N.Send0(N.objc_getClass("NSWindow"), N.Sel("alloc")),
            N.Sel("initWithContentRect:styleMask:backing:defer:"),
            new ProbeRect(100, 100, 64, 64), 0, 2, 0);
        Program.Require(window != 0, "task_window_created");
        N.SendByte(window, N.Sel("setReleasedWhenClosed:"), 0);
        N.SendByte(window, N.Sel("setIgnoresMouseEvents:"), 1);
        nint view = 0;
        nint space = 0;
        try
        {
            view = InitView(N.Send0(N.objc_getClass("NSView"), N.Sel("alloc")),
                N.Sel("initWithFrame:"), new ProbeRect(0, 0, 64, 64));
            Program.Require(view != 0, "task_view_created");
            N.SendByte(view, N.Sel("setWantsLayer:"), 1);
            nint rootLayer = N.Send0(view, N.Sel("layer"));
            space = CGColorSpaceCreateWithName(N.ExportedObject(CoreGraphics, "kCGColorSpaceSRGB"));
            Program.Require(rootLayer != 0 && space != 0, "task_color_layer_created");
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                nint layer = N.Send0(N.objc_getClass("CALayer"), N.Sel("new"));
                var components = new ProbeColor(
                    quadrant is 0 or 3 ? 1 : 0,
                    quadrant is 1 or 3 ? 1 : 0,
                    quadrant is 2 or 3 ? 1 : 0,
                    1);
                nint color = CGColorCreate(space, in components);
                try
                {
                    Program.Require(layer != 0 && color != 0, "task_marker_created");
                    SendRect(layer, N.Sel("setFrame:"),
                        new ProbeRect(quadrant % 2 * 32, quadrant / 2 * 32, 32, 32));
                    _ = N.Send1(layer, N.Sel("setBackgroundColor:"), color);
                    _ = N.Send1(rootLayer, N.Sel("addSublayer:"), layer);
                }
                finally
                {
                    if (color != 0) N.CFRelease(color);
                    if (layer != 0) N.objc_release(layer);
                }
            }

            _ = N.Send1(window, N.Sel("setContentView:"), view);
            _ = N.Send1(window, N.Sel("orderFront:"), 0);
            _ = N.Send0(window, N.Sel("displayIfNeeded"));
            Pump(TimeSpan.FromMilliseconds(500));
            WindowId = checked((uint)N.GetNInt(window, N.Sel("windowNumber")));
            Program.Require(WindowId != 0, "task_window_identity_created");
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (view != 0) N.objc_release(view);
            if (space != 0) N.CFRelease(space);
        }
    }

    internal uint WindowId { get; }

    internal static nint ApplicationPointer => Marshal.ReadIntPtr(
        NativeLibrary.GetExport(NativeLibrary.Load(AppKit), "NSApp"));

    internal void Hide() => _ = N.Send1(window, N.Sel("orderOut:"), 0);

    internal static void Pump(TimeSpan duration)
    {
        nint mode = N.ExportedObject(CoreFoundation, "kCFRunLoopDefaultMode");
        long deadline = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            _ = CFRunLoopRunInMode(mode, 0.01, 0);
        }
    }

    public void Dispose()
    {
        nint owned = Interlocked.Exchange(ref window, 0);
        if (owned != 0)
        {
            _ = N.Send0(owned, N.Sel("close"));
            N.objc_release(owned);
        }
    }

    [LibraryImport(AppKit)] private static partial byte NSApplicationLoad();
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint InitWindow(nint receiver, nint selector,
        ProbeRect rectangle, nuint style, nuint backing, byte defer);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint InitView(nint receiver, nint selector, ProbeRect rectangle);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SendRect(nint receiver, nint selector, ProbeRect rectangle);
    [LibraryImport(CoreGraphics)] private static partial nint CGColorSpaceCreateWithName(nint name);
    [LibraryImport(CoreGraphics)] private static partial nint CGColorCreate(nint space, in ProbeColor components);
    [LibraryImport(CoreFoundation)] private static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSource);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ProbeRect(double x, double y, double width, double height)
{
    internal readonly double X = x;
    internal readonly double Y = y;
    internal readonly double Width = width;
    internal readonly double Height = height;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ProbeColor(double red, double green, double blue, double alpha)
{
    internal readonly double Red = red;
    internal readonly double Green = green;
    internal readonly double Blue = blue;
    internal readonly double Alpha = alpha;
}
