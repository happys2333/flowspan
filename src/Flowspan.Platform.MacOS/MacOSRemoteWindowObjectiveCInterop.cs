using System.Runtime.InteropServices;

namespace Flowspan.Platform.MacOS;

// Signatures returning aggregates use the ordinary arm64 ABI. The caller must
// establish its OS/architecture guard before initializing any native owner.
internal static partial class MacOSRemoteWindowObjectiveCInterop
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    internal const string ScreenCaptureKit = "/System/Library/Frameworks/ScreenCaptureKit.framework/ScreenCaptureKit";
    internal const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    internal const uint Bgra = 0x42475241;

    private static readonly Lazy<nint> FrameStatus = new(() => ExportedObject(ScreenCaptureKit, "SCStreamFrameInfoStatus"));
    internal static nint FrameStatusKey => FrameStatus.Value;
    internal static nint Sel(string name) => sel_registerName(name);
    internal static nint ExportedObject(string library, string name) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(library), name));

    [LibraryImport(ObjC)] internal static partial nint objc_autoreleasePoolPush();
    [LibraryImport(ObjC)] internal static partial void objc_autoreleasePoolPop(nint pool);
    [LibraryImport(ObjC)] internal static partial nint objc_retain(nint value);
    [LibraryImport(ObjC)] internal static partial void objc_release(nint value);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint objc_getClass(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint objc_getProtocol(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint objc_allocateProtocol(string name);
    [LibraryImport(ObjC)] internal static partial void objc_registerProtocol(nint protocol);
    [LibraryImport(ObjC)] internal static partial void protocol_addProtocol(nint protocol, nint adoptedProtocol);
    [LibraryImport(ObjC)] internal static partial void protocol_addMethodDescription(nint protocol, nint selector, nint types, byte required, byte instance);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] private static partial nint sel_registerName(string name);
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);
    [LibraryImport(ObjC)] internal static partial void objc_registerClassPair(nint cls);
    [LibraryImport(ObjC)] internal static partial byte class_addProtocol(nint cls, nint protocol);
    [LibraryImport(ObjC)] internal static partial byte class_addMethod(nint cls, nint selector, nint implementation, nint types);
    [LibraryImport(ObjC)] internal static partial MacOSObjectiveCMethodDescription protocol_getMethodDescription(nint protocol, nint selector, byte required, byte instance);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial nint Send0(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial nint Send1(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial nint SendIndex(nint receiver, nint selector, nuint index);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial void SendNUInt(nint receiver, nint selector, nuint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial void SendNInt(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial void SendUInt(nint receiver, nint selector, uint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial void SendByte(nint receiver, nint selector, byte value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial void SendTime(nint receiver, nint selector, MacOSCaptureTime value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial nuint GetNUInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial nint GetNInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial uint GetUInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial int GetInt(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial byte GetByte(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial float GetFloat(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial MacOSCaptureRect GetRect(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial void Enumerate(nint receiver, nint selector, byte excludeDesktop, byte onScreenOnly, nint block);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial nint InitStream(nint receiver, nint selector, nint filter, nint config, nint callbackDelegate);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial byte AddOutput(nint receiver, nint selector, nint output, nint type, nint queue, out nint error);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] internal static partial byte RemoveOutput(nint receiver, nint selector, nint output, nint type, out nint error);
    [LibraryImport(SystemLibrary, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint dispatch_queue_create(string label, nint attribute);
    [LibraryImport(SystemLibrary)] internal static partial void dispatch_sync_f(nint queue, nint context, nint function);
    [LibraryImport(SystemLibrary)] internal static partial void dispatch_release(nint queue);
    [LibraryImport("/usr/lib/libproc.dylib")] internal static partial int proc_pidinfo(int pid, int flavor, ulong argument, out MacOSProcessInstanceInfo buffer, int bufferSize);
    [LibraryImport(CoreGraphics)] internal static partial byte CGPreflightScreenCaptureAccess();
    [LibraryImport(CoreGraphics)] internal static partial nint CGWindowListCopyWindowInfo(uint option, uint relativeWindow);
    [LibraryImport(CoreGraphics)] internal static partial byte CGRectMakeWithDictionaryRepresentation(nint dictionary, out MacOSCaptureRect rectangle);
    [LibraryImport(CoreFoundation)] internal static partial nint CFRetain(nint value);
    [LibraryImport(CoreFoundation)] internal static partial void CFRelease(nint value);
    [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetCount(nint array);
    [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(CoreFoundation)] internal static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(CoreFoundation)] internal static partial byte CFBooleanGetValue(nint boolean);
    [LibraryImport(CoreFoundation)] internal static partial byte CFNumberGetValue(nint number, nint numberType, out long value);
    [LibraryImport(CoreVideo)] internal static partial int CVPixelBufferLockBaseAddress(nint pixel, nuint flags);
    [LibraryImport(CoreVideo)] internal static partial int CVPixelBufferUnlockBaseAddress(nint pixel, nuint flags);
    [LibraryImport(CoreVideo)] internal static partial nint CVPixelBufferGetBaseAddress(nint pixel);
    [LibraryImport(CoreVideo)] internal static partial nuint CVPixelBufferGetBytesPerRow(nint pixel);
    [LibraryImport(CoreVideo)] internal static partial nuint CVPixelBufferGetDataSize(nint pixel);
    [LibraryImport(CoreVideo)] internal static partial nuint CVPixelBufferGetWidth(nint pixel);
    [LibraryImport(CoreVideo)] internal static partial nuint CVPixelBufferGetHeight(nint pixel);
    [LibraryImport(CoreVideo)] internal static partial uint CVPixelBufferGetPixelFormatType(nint pixel);
    [LibraryImport(CoreVideo)] internal static partial byte CVPixelBufferIsPlanar(nint pixel);
    [LibraryImport(CoreMedia)] internal static partial nint CMSampleBufferGetImageBuffer(nint sample);
    [LibraryImport(CoreMedia)] internal static partial byte CMSampleBufferIsValid(nint sample);
    [LibraryImport(CoreMedia)] internal static partial byte CMSampleBufferDataIsReady(nint sample);
    [LibraryImport(CoreMedia)] internal static partial nint CMSampleBufferGetSampleAttachmentsArray(nint sample, byte createIfNecessary);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MacOSCaptureRect
{
    internal readonly double X;
    internal readonly double Y;
    internal readonly double Width;
    internal readonly double Height;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct MacOSCaptureTime
{
    internal long Value;
    internal int Timescale;
    internal uint Flags;
    internal long Epoch;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MacOSObjectiveCMethodDescription
{
    internal readonly nint Name;
    internal readonly nint Types;
}

// Darwin proc_bsdinfo is 136 bytes on ordinary 64-bit macOS. We read only PID
// and its birth timestamp, never the process-name fields in the native buffer.
[StructLayout(LayoutKind.Explicit, Size = 136)]
internal readonly struct MacOSProcessInstanceInfo
{
    [FieldOffset(12)] internal readonly uint ProcessId;
    [FieldOffset(120)] internal readonly ulong StartSeconds;
    [FieldOffset(128)] internal readonly ulong StartMicroseconds;
}
