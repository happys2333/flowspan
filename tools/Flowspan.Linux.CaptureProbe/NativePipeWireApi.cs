using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Flowspan.Linux.CaptureProbe;

internal sealed class NativePipeWireApi : IPipeWireApi, IDisposable
{
    internal const string LibraryName = "libpipewire-0.3.so.0";
    private static readonly string[] RequiredSymbols =
    [
        "pw_init",
        "pw_deinit",
        "pw_get_library_version",
        "pw_thread_loop_new",
        "pw_thread_loop_get_loop",
        "pw_thread_loop_start",
        "pw_thread_loop_lock",
        "pw_thread_loop_unlock",
        "pw_thread_loop_in_thread",
        "pw_thread_loop_get_time",
        "pw_thread_loop_stop",
        "pw_thread_loop_destroy",
    ];

    private static nint resolvedLibrary;
    private nint library;

    private NativePipeWireApi(nint library)
    {
        this.library = library;
        resolvedLibrary = library;
        NativeLibrary.SetDllImportResolver(
            typeof(NativePipeWireApi).Assembly,
            static (name, _, _) => name == LibraryName ? resolvedLibrary : 0);
    }

    public static NativePipeWireApi? TryCreate(out ProbeReason reason)
    {
        if (!NativeLibrary.TryLoad(LibraryName, out nint library))
        {
            reason = ProbeReason.LibraryMissing;
            return null;
        }

        bool transferred = false;
        try
        {
            foreach (string symbol in RequiredSymbols)
            {
                if (!NativeLibrary.TryGetExport(library, symbol, out _))
                {
                    reason = ProbeReason.SymbolMissing;
                    return null;
                }
            }

            NativePipeWireApi api = new(library);
            transferred = true;
            reason = ProbeReason.None;
            return api;
        }
        catch
        {
            resolvedLibrary = 0;
            throw;
        }
        finally
        {
            if (!transferred)
            {
                NativeLibrary.Free(library);
            }
        }
    }

    public void Initialize() => NativeImports.Initialize(0, 0);

    public string GetLibraryVersion()
    {
        nint text = NativeImports.GetLibraryVersion();
        if (text == 0)
        {
            return string.Empty;
        }

        Span<byte> bytes = stackalloc byte[ProbeOutput.MaximumVersionBytes];
        for (int index = 0; index <= bytes.Length; index++)
        {
            byte value = Marshal.ReadByte(text, index);
            if (value == 0)
            {
                return Encoding.ASCII.GetString(bytes[..index]);
            }

            if (index == bytes.Length || value > 127)
            {
                return string.Empty;
            }

            bytes[index] = value;
        }

        return string.Empty;
    }

    public nint CreateLoop() => NativeImports.CreateLoop("flowspan-abi", 0);
    public nint GetLoop(nint loop) => NativeImports.GetLoop(loop);
    public int Start(nint loop) => NativeImports.Start(loop);
    public void Lock(nint loop) => NativeImports.Lock(loop);
    public void Unlock(nint loop) => NativeImports.Unlock(loop);
    public byte InThread(nint loop) => NativeImports.InThread(loop);
    public int GetTime(nint loop, out LinuxTimespec time) => NativeImports.GetTime(loop, out time, 0);
    public void Stop(nint loop) => NativeImports.Stop(loop);
    public void Destroy(nint loop) => NativeImports.Destroy(loop);
    public void Deinitialize() => NativeImports.Deinitialize();

    public void Dispose()
    {
        nint owner = Interlocked.Exchange(ref library, 0);
        if (owner != 0)
        {
            resolvedLibrary = 0;
            NativeLibrary.Free(owner);
        }
    }
}

internal static partial class NativeImports
{
    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Initialize(nint argc, nint argv);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_deinit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Deinitialize();

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_get_library_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint GetLibraryVersion();

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_new", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint CreateLoop(string name, nint properties);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_get_loop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint GetLoop(nint loop);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Start(nint loop);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_lock")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Lock(nint loop);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_unlock")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Unlock(nint loop);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_in_thread")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte InThread(nint loop);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_get_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int GetTime(nint loop, out LinuxTimespec time, long timeout);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Stop(nint loop);

    [LibraryImport(NativePipeWireApi.LibraryName, EntryPoint = "pw_thread_loop_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Destroy(nint loop);
}
