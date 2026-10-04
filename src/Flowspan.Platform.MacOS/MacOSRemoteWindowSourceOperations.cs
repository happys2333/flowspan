namespace Flowspan.Platform.MacOS;

// Only native effects are replaceable. NativeSource and NativeCaptureSource
// remain the production ownership and use-admission implementation in tests.
internal interface IMacOSRemoteWindowSourceOperations
{
    public nint Retain(nint owner);
    public void Release(nint owner);
    public bool CheckCurrent(nint filter, MacOSRemoteWindowNativeIdentity identity,
        NativeRemoteWindowGeometry geometry);
}
