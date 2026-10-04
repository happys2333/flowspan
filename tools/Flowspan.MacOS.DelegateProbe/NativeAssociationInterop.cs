using System.Runtime.InteropServices;

namespace Flowspan.MacOS.DelegateProbe;

internal static partial class NativeAssociationInterop
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    [LibraryImport(ObjC, EntryPoint = "class_addIvar", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial byte AddIvar(nint value, string name, nuint size, byte alignmentLog2, string encoding);

    [LibraryImport(ObjC, EntryPoint = "class_getInstanceVariable", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint InstanceVariable(nint value, string name);

    [LibraryImport(ObjC, EntryPoint = "ivar_getTypeEncoding")]
    internal static partial nint IvarEncoding(nint value);

    [LibraryImport(ObjC, EntryPoint = "ivar_getOffset")]
    internal static partial nint IvarOffset(nint value);

    [LibraryImport(ObjC, EntryPoint = "class_getInstanceSize")]
    internal static partial nuint InstanceSize(nint value);

    [LibraryImport(ObjC, EntryPoint = "class_getSuperclass")]
    internal static partial nint Superclass(nint value);

    [LibraryImport(ObjC, EntryPoint = "object_getClass")]
    internal static partial nint ObjectClass(nint value);

    [LibraryImport(ObjC, EntryPoint = "method_getImplementation")]
    internal static partial nint MethodImplementation(nint value);

    [LibraryImport(ObjC, EntryPoint = "objc_setAssociatedObject")]
    internal static partial void Associate(nint source, nint key, nint tag, nuint policy);

    [LibraryImport(ObjC, EntryPoint = "objc_getAssociatedObject")]
    internal static partial nint Association(nint source, nint key);

    [LibraryImport(ObjC, EntryPoint = "objc_retain")]
    internal static partial nint Retain(nint value);

    [LibraryImport(ObjC, EntryPoint = "objc_release")]
    internal static partial void Release(nint value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSendSuper")]
    internal static partial void SuperDealloc(ref ObjectiveCSuper context, nint selector);

    [LibraryImport(Foundation, EntryPoint = "NSGetSizeAndAlignment", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint TypeSizeAndAlignment(string encoding, out nuint size, out nuint alignment);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ObjectiveCSuper
    {
        internal nint Receiver;
        internal nint Superclass;
    }
}
