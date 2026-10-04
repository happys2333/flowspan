using System.Runtime.InteropServices;

namespace Flowspan.MacOS.DelegateProbe;

internal static partial class Native
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";

    [LibraryImport(ObjC, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial nint PushAutoreleasePool();

    [LibraryImport(ObjC, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void PopAutoreleasePool(nint pool);

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint AllocateClass(nint superclass, string name, nuint extraBytes);

    [LibraryImport(ObjC, EntryPoint = "objc_registerClassPair")]
    internal static partial void RegisterClass(nint value);

    [LibraryImport(ObjC, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial byte AddMethod(nint value, nint selector, nint implementation, string encoding);

    [LibraryImport(ObjC, EntryPoint = "class_getInstanceMethod")]
    internal static partial nint InstanceMethod(nint value, nint selector);

    [LibraryImport(ObjC, EntryPoint = "method_getTypeEncoding")]
    internal static partial nint MethodEncoding(nint value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendObject(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendTerminal(nint receiver, nint selector, nint stream, nint error);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendObservation(nint receiver, nint selector, nint stream);

    [LibraryImport(SystemLibrary, EntryPoint = "dispatch_get_global_queue")]
    internal static partial nint GlobalQueue(nint identifier, nuint flags);

    [LibraryImport(SystemLibrary, EntryPoint = "dispatch_async_f")]
    internal static partial void DispatchAsync(nint queue, nint context, nint callback);
}
