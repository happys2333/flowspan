using System.Runtime.InteropServices;

namespace Flowspan.Windows.CaptureProbe;

internal interface IWgcComApi
{
    public int QueryClosable(nint instance, out nint closable);

    public int Close(nint closable);

    public uint Release(nint instance);
}

internal sealed class WgcComOwners(IWgcComApi api)
{
    private readonly List<nint> references = [];

    public int OwnedReferences { get; private set; }

    public int ReleasedReferences { get; private set; }

    public bool Quarantined { get; private set; }

    public void Quarantine() => Quarantined = true;

    public nint Own(nint instance)
    {
        if (instance != 0) { references.Add(instance); OwnedReferences++; }
        return instance;
    }

    public void Close(nint instance)
    {
        try
        {
            int query = api.QueryClosable(instance, out nint closable);
            Own(closable);
            if (query < 0 || closable == 0)
            {
                throw new ProbeFailureException("native_cleanup_unconfirmed", query);
            }

            int closed = api.Close(closable);
            if (closed < 0)
            {
                throw new ProbeFailureException("native_cleanup_unconfirmed", closed);
            }

            ReleaseOwned(closable);
        }
        catch
        {
            Quarantined = true;
            throw;
        }
    }

    public void ReleaseOwned(nint instance)
    {
        if (Quarantined) { return; }
        int index = references.LastIndexOf(instance);
        if (index < 0) { throw new ProbeFailureException("unowned_reference_release"); }
        api.Release(instance);
        references.RemoveAt(index);
        ReleasedReferences++;
    }

    public void ReleaseAll()
    {
        if (Quarantined) { return; }
        while (references.Count != 0)
        {
            ReleaseOwned(references[^1]);
        }
    }
}

internal sealed class NativeWgcComApi : IWgcComApi
{
    public int QueryClosable(nint instance, out nint closable) =>
        WgcNative.QueryInterface(instance, WgcNative.ClosableIid, out closable);

    public int Close(nint closable) => WgcNative.Close(closable);

    public uint Release(nint instance) => D3D11Native.Release(instance);
}

// Public Windows SDK declarations, fixed win32metadata SHA
// 5c5efbc01d4c87f6830ec304d42777991d533154: windows.graphics.capture.h,
// Windows.Graphics.Capture.Interop.h, windows.graphics.directx.direct3d11.idl,
// windows.graphics.directx.direct3d11.interop.h, inspectable.h, roapi.h, winstring.h.
// No RCW or CLR WinRT projection; ordinary Windows x64 only.
internal static unsafe partial class WgcNative
{
    public static readonly Guid SessionStaticsIid = new("2224A540-5974-49AA-B232-0882536F4CB5");
    public static readonly Guid ItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    public static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    public static readonly Guid PoolStatics2Iid = new("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
    public static readonly Guid Session2Iid = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");
    public static readonly Guid Session6Iid = new("D7419236-BE20-5E9F-BCD6-C4E98FD6AFDC");
    public static readonly Guid ClosableIid = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
    public static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    public static readonly Guid WinrtDeviceIid = new("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
    public static readonly Guid DxgiAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    public static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    public const int NoInterface = unchecked((int)0x80004002);
    public const int ClassNotRegistered = unchecked((int)0x80040154);

    [LibraryImport("combase.dll")]
    private static partial int WindowsCreateString(char* source, uint length, out nint value);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint value);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint name, Guid* iid, out nint factory);

    [LibraryImport("d3d11.dll")]
    public static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint inspectable);

    public static nint ActivationFactory(string className, Guid iid, WgcComOwners owners)
    {
        nint name = 0;
        try
        {
            fixed (char* text = className)
            {
                Require(WindowsCreateString(text, checked((uint)className.Length), out name), "hstring_create_failed");
            }

            int result = RoGetActivationFactory(name, &iid, out nint factory);
            owners.Own(factory);
            Require(result, "activation_factory_failed");
            RequirePointer(factory);
            return factory;
        }
        finally
        {
            if (name != 0)
            {
                int deleted = WindowsDeleteString(name);
                if (deleted < 0)
                {
                    owners.Quarantine();
                    throw new ProbeFailureException("native_cleanup_unconfirmed", deleted);
                }
            }
        }
    }

    public static int QueryInterface(nint instance, Guid iid, out nint result)
    {
        nint value = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(instance, 0))(
            instance, &iid, &value);
        result = value;
        return hr;
    }

    public static nint QueryOwned(nint instance, Guid iid, WgcComOwners owners)
    {
        int hr = QueryInterface(instance, iid, out nint value);
        owners.Own(value);
        Require(hr, "query_interface_failed");
        RequirePointer(value);
        return value;
    }

    public static int IsSupported(nint statics, byte* supported) =>
        ((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(statics, 6))(statics, supported);

    public static int CreateForWindow(nint interop, nint hwnd, nint* item)
    {
        Guid iid = ItemIid;
        return ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(interop, 3))(
            interop, hwnd, &iid, item);
    }

    public static int ItemSize(nint item, SizeInt32* size) =>
        ((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Slot(item, 7))(item, size);

    public static int CreatePool(nint statics, nint device, SizeInt32 size, nint* pool) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int, int, SizeInt32, nint*, int>)Slot(statics, 6))(
            statics, device, (int)D3D11Native.FormatBgra8Unorm, 2, size, pool);

    public static int CreateSession(nint pool, nint item, nint* session) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(pool, 10))(pool, item, session);

    public static int SetCursor(nint session2, byte enabled) =>
        ((delegate* unmanaged[Stdcall]<nint, byte, int>)Slot(session2, 7))(session2, enabled);

    public static int SetSecondaryWindows(nint session6, byte included) =>
        ((delegate* unmanaged[Stdcall]<nint, byte, int>)Slot(session6, 7))(session6, included);

    public static int Start(nint session) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Slot(session, 6))(session);

    public static int NextFrame(nint pool, nint* frame) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(pool, 7))(pool, frame);

    public static int FrameSurface(nint frame, nint* surface) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(frame, 6))(frame, surface);

    public static int FrameContentSize(nint frame, SizeInt32* size) =>
        ((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Slot(frame, 8))(frame, size);

    public static int NativeTexture(nint access, nint* texture)
    {
        Guid iid = Texture2DIid;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(access, 3))(
            access, &iid, texture);
    }

    public static int Close(nint closable) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Slot(closable, 6))(closable);

    public static void Require(int result, string reason)
    {
        if (result < 0) { throw new ProbeFailureException(reason, result); }
    }

    public static void RequirePointer(nint value)
    {
        if (value == 0) { throw new ProbeFailureException("missing_native_owner"); }
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];
}

[StructLayout(LayoutKind.Sequential)]
internal struct SizeInt32
{
    public int Width;
    public int Height;
}
