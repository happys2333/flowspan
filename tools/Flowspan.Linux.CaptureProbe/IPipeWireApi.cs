namespace Flowspan.Linux.CaptureProbe;

internal interface IPipeWireApi
{
    public void Initialize();
    public string GetLibraryVersion();
    public nint CreateLoop();
    public nint GetLoop(nint loop);
    public int Start(nint loop);
    public void Lock(nint loop);
    public void Unlock(nint loop);
    public byte InThread(nint loop);
    public int GetTime(nint loop, out LinuxTimespec time);
    public void Stop(nint loop);
    public void Destroy(nint loop);
    public void Deinitialize();
}
