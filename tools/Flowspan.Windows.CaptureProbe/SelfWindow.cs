using System.Runtime.InteropServices;

namespace Flowspan.Windows.CaptureProbe;

// ABI provenance: win32metadata SHA 5c5efbc01d4c87f6830ec304d42777991d533154,
// um/WinUser.h, wingdi.h, dwmapi.h, libloaderapi.h, shared/windef.h, minwindef.h.
// Only this probe's own HWND is ever created or used. WM_CLOSE is an admission
// stop request, not DestroyWindow: native capture owners must drain first.
internal sealed unsafe partial class SelfWindow
{
    private const uint WmPaint = 0x000F;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmFinish = 0x8001;
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim nativeCleanup = new();
    private readonly WgcProbeAdmission admission = new();
    private readonly Thread thread;
    private Exception? failure;
    private nint hwnd;
    private bool destroyed;
    private bool paintCleanupUnconfirmed;

    public SelfWindow()
    {
        thread = new Thread(UiThread) { IsBackground = true, Name = "Flowspan WGC owned HWND" };
    }

    public nint Handle => hwnd;

    public bool AdmissionOpen => admission.IsOpen;

    public void CommitFrame(Action commit) => admission.Commit(commit);

    public bool CleanupConfirmed { get; private set; }

    public void Start()
    {
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5))) { throw new ProbeFailureException("window_start_timeout"); }
        if (failure is not null) { throw failure; }
        WgcNative.RequirePointer(hwnd);
    }

    // Called only after confirmed capture cleanup. The UI owner destroys HWND.
    public void Finish()
    {
        admission.Stop();
        nativeCleanup.Set();
        if (hwnd != 0 && !destroyed && PostMessageW(hwnd, WmFinish, 0, 0) == 0)
        {
            throw new ProbeFailureException("window_finish_post_failed");
        }

        if (!thread.Join(TimeSpan.FromSeconds(5)) || !CleanupConfirmed)
        {
            throw new ProbeFailureException("window_cleanup_unconfirmed");
        }

        if (failure is not null) { throw failure; }
        ready.Dispose();
        nativeCleanup.Dispose();
    }

    public static bool HasInteractiveDesktop()
    {
        if (!Environment.UserInteractive) { return false; }
        nint station = GetProcessWindowStation();
        uint returned = 0;
        UserObjectFlags actual = default;
        if (station == 0 || GetUserObjectInformationW(station, 1, &actual, 12, &returned) == 0
            || (actual.Flags & 1) == 0) { return false; }
        nint desktop = OpenInputDesktop(0, 0, 1); // DESKTOP_READOBJECTS only
        if (desktop == 0) { return false; }
        if (CloseDesktop(desktop) == 0) { throw new ProbeFailureException("native_cleanup_unconfirmed"); }
        int composition = 0;
        WgcNative.Require(DwmIsCompositionEnabled(&composition), "dwm_composition_check_failed");
        return composition != 0;
    }

    private void UiThread()
    {
        nint instance = 0;
        nint previousDpi = 0;
        ushort atom = 0;
        bool admitted = false;
        const string className = "Flowspan.WgcProbe.OwnWindow";
        WindowProcedure callback = WindowProc;
        try
        {
            instance = GetModuleHandleW(0);
            WgcNative.RequirePointer(instance);
            previousDpi = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
            WgcNative.RequirePointer(previousDpi);
            fixed (char* name = className)
            {
                WindowClass definition = new()
                {
                    Procedure = Marshal.GetFunctionPointerForDelegate(callback),
                    Instance = instance,
                    ClassName = (nint)name,
                };
                atom = RegisterClassW(&definition);
                if (atom == 0) { throw new ProbeFailureException("window_class_register_failed"); }
                hwnd = CreateWindowExW(0, name, name, 0x80000000, 80, 80, 64, 64, 0, 0, instance, 0);
                WgcNative.RequirePointer(hwnd);
                ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE; result is old visibility, not success.
                if (UpdateWindow(hwnd) == 0) { throw new ProbeFailureException("window_update_failed"); }
                if (failure is not null) { throw failure; }
            }

            admitted = true;
            ready.Set();
            WindowMessage message = default;
            while (true)
            {
                int next = GetMessageW(&message, 0, 0, 0);
                if (next < 0) { throw new ProbeFailureException("window_message_failed"); }
                if (next == 0) { break; }
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            admission.Stop();
            ready.Set();
        }
        finally
        {
            // Even a failed message loop cannot destroy an admitted source
            // while a capture worker may still hold native borrowers.
            if (admitted) { nativeCleanup.Wait(); }
            if (hwnd != 0 && !destroyed) { destroyed = DestroyWindow(hwnd) != 0; }
            bool unregistered = atom == 0 || UnregisterClassW((nint)atom, instance) != 0;
            bool dpiRestored = previousDpi == 0 || SetThreadDpiAwarenessContext(previousDpi) != 0;
            CleanupConfirmed = (hwnd == 0 || destroyed) && unregistered && dpiRestored
                && !paintCleanupUnconfirmed;
            GC.KeepAlive(callback);
        }
    }

    private nint WindowProc(nint window, uint message, nuint wparam, nint lparam)
    {
        try
        {
            if (message == WmClose) { admission.Stop(); return 0; }
            if (message == WmFinish)
            {
                admission.Stop();
                if (!nativeCleanup.IsSet) { return 0; }
                destroyed = DestroyWindow(window) != 0;
                if (!destroyed) { failure = new ProbeFailureException("window_destroy_failed"); PostQuitMessage(1); }
                return 0;
            }

            if (message == WmDestroy) { PostQuitMessage(0); return 0; }
            if (message == WmPaint) { Paint(window); return 0; }
            return DefWindowProcW(window, message, wparam, lparam);
        }
        catch (Exception exception)
        {
            failure = exception;
            admission.Stop();
            // No managed exception is permitted to cross the native callback.
            return 0;
        }
    }

    private void Paint(nint window)
    {
        PaintStruct paint = default;
        nint dc = BeginPaint(window, &paint);
        if (dc == 0)
        {
            paintCleanupUnconfirmed = true;
            throw new ProbeFailureException("window_begin_paint_failed");
        }
        try
        {
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                uint color = quadrant switch { 0 => 0x0000FF, 1 => 0x00FF00, 2 => 0xFF0000, _ => 0xFFFFFF };
                nint brush = CreateSolidBrush(color);
                WgcNative.RequirePointer(brush);
                try
                {
                    int x = quadrant % 2 * 32;
                    int y = quadrant / 2 * 32;
                    WindowRect rect = new() { Left = x, Top = y, Right = x + 32, Bottom = y + 32 };
                    if (FillRect(dc, &rect, brush) == 0) { throw new ProbeFailureException("window_paint_failed"); }
                }
                finally
                {
                    if (DeleteObject(brush) == 0)
                    {
                        paintCleanupUnconfirmed = true;
                        throw new ProbeFailureException("gdi_brush_delete_failed");
                    }
                }
            }
        }
        finally
        {
            if (EndPaint(window, &paint) == 0)
            {
                paintCleanupUnconfirmed = true;
                throw new ProbeFailureException("window_end_paint_failed");
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wparam, nint lparam);

    [LibraryImport("user32.dll")] private static partial ushort RegisterClassW(WindowClass* value);
    [LibraryImport("user32.dll")] private static partial int UnregisterClassW(nint name, nint instance);
    [LibraryImport("user32.dll")] private static partial nint CreateWindowExW(uint ex, char* cls, char* title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")] private static partial int ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")] private static partial int UpdateWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int DestroyWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int PostMessageW(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")] private static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(WindowMessage* message, nint window, uint minimum, uint maximum);
    [LibraryImport("user32.dll")] private static partial int TranslateMessage(WindowMessage* message);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(WindowMessage* message);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")] private static partial nint BeginPaint(nint window, PaintStruct* paint);
    [LibraryImport("user32.dll")] private static partial int EndPaint(nint window, PaintStruct* paint);
    [LibraryImport("user32.dll")] private static partial int FillRect(nint dc, WindowRect* rect, nint brush);
    [LibraryImport("user32.dll")] private static partial nint SetThreadDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll")] private static partial nint GetProcessWindowStation();
    [LibraryImport("user32.dll")] private static partial int GetUserObjectInformationW(nint value, int index, void* info, uint length, uint* needed);
    [LibraryImport("user32.dll")] private static partial nint OpenInputDesktop(uint flags, int inherit, uint desiredAccess);
    [LibraryImport("user32.dll")] private static partial int CloseDesktop(nint desktop);
    [LibraryImport("gdi32.dll")] private static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint value);
    [LibraryImport("dwmapi.dll")] private static partial int DwmIsCompositionEnabled(int* enabled);
    [LibraryImport("kernel32.dll")] private static partial nint GetModuleHandleW(nint name);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UserObjectFlags { public int Inherit; public int Reserved; public uint Flags; }

[StructLayout(LayoutKind.Sequential)]
internal struct WindowClass
{
    public uint Style;
    public nint Procedure;
    public int ClassExtra;
    public int WindowExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;
    public nint MenuName;
    public nint ClassName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowMessage
{
    public nint Window;
    public uint Message;
    public nuint Wparam;
    public nint Lparam;
    public uint Time;
    public int X;
    public int Y;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PaintStruct
{
    public nint DeviceContext;
    public int Erase;
    public WindowRect Paint;
    public int Restore;
    public int IncrementalUpdate;
    public fixed byte Reserved[32];
}
