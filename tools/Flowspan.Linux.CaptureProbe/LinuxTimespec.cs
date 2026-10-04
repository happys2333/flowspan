using System.Runtime.InteropServices;

namespace Flowspan.Linux.CaptureProbe;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LinuxTimespec(long seconds, long nanoseconds)
{
    public readonly long Seconds = seconds;
    public readonly long Nanoseconds = nanoseconds;
}
