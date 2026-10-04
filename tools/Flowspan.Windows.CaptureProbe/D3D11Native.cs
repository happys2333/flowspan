using System.Runtime.InteropServices;

namespace Flowspan.Windows.CaptureProbe;

// ABI provenance: microsoft/win32metadata, commit
// 5c5efbc01d4c87f6830ec304d42777991d533154,
// generation/WinSDK/RecompiledIdlHeaders/um/d3d11.h and d3dcommon.h.
// Ordinary Windows x64 only. No COM pointer is projected as a CLR RCW.
internal static unsafe partial class D3D11Native
{
    public const int DriverTypeWarp = 5;
    public const uint CreateDeviceBgraSupport = 0x20;
    public const uint SdkVersion = 7;
    public const uint FormatBgra8Unorm = 87;
    public const uint UsageDefault = 0;
    public const uint UsageStaging = 3;
    public const uint CpuAccessRead = 0x20000;
    public const uint MapRead = 1;
    public const uint MapDoNotWait = 0x100000;
    public const int WasStillDrawing = unchecked((int)0x887A000A);

    [LibraryImport("combase.dll")]
    public static partial int RoInitialize(uint initializationType);

    [LibraryImport("combase.dll")]
    public static partial void RoUninitialize();

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(
        nint adapter,
        int driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out nint device,
        out uint selectedFeatureLevel,
        out nint immediateContext);

    public static int CreateTexture2D(
        nint device,
        Texture2DDescription* description,
        SubresourceData* initialData,
        nint* texture) =>
        ((delegate* unmanaged[Stdcall]<
            nint, Texture2DDescription*, SubresourceData*, nint*, int>)
            Slot(device, 5))(device, description, initialData, texture);

    public static void GetDescription(
        nint texture,
        Texture2DDescription* description) =>
        ((delegate* unmanaged[Stdcall]<nint, Texture2DDescription*, void>)
            Slot(texture, 10))(texture, description);

    public static void CopySubresourceRegion(
        nint context,
        nint destination,
        nint source,
        TextureBox* sourceBox) =>
        ((delegate* unmanaged[Stdcall]<
            nint, nint, uint, uint, uint, uint, nint, uint, TextureBox*, void>)
            Slot(context, 46))(
                context, destination, 0, 0, 0, 0, source, 0, sourceBox);

    public static int Map(
        nint context,
        nint resource,
        MappedSubresource* mapped) =>
        ((delegate* unmanaged[Stdcall]<
            nint, nint, uint, uint, uint, MappedSubresource*, int>)
            Slot(context, 14))(
                context, resource, 0, MapRead, MapDoNotWait, mapped);

    public static void Unmap(nint context, nint resource) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)
            Slot(context, 15))(context, resource, 0);

    public static void Flush(nint context) =>
        ((delegate* unmanaged[Stdcall]<nint, void>)
            Slot(context, 111))(context);

    public static uint Release(nint instance) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);

    private static nint Slot(nint instance, int index) =>
        (*(nint**)instance)[index];
}

[StructLayout(LayoutKind.Sequential)]
internal struct Texture2DDescription
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SubresourceData
{
    public nint Data;
    public uint RowPitch;
    public uint SlicePitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MappedSubresource
{
    public nint Data;
    public uint RowPitch;
    public uint DepthPitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TextureBox
{
    public uint Left;
    public uint Top;
    public uint Front;
    public uint Right;
    public uint Bottom;
    public uint Back;
}
