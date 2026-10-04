# Historical temporary-probe record

This file records the prior /tmp source and prior executions, not the modified
repository tool. Original text follows unchanged.

# Throwaway ScreenCaptureKit C# ABI probe

Question: can direct C# interop own documented Objective-C protocol callbacks,
copied Blocks, and retained CoreMedia samples without Swift glue on this host?

Run: `dotnet run --project /tmp/flowspan-macos-capture-probe-20261004/Probe.csproj -c Release`

The probe never requests screen capture permission or reads window titles. It
invokes SCShareableContent enumeration only if the prompt-free CoreGraphics
preflight is already Granted. It then creates a task-owned borderless 64x64
NSWindow with fixed four-quadrant sRGB colors, matches both its native window
number and this process PID, captures only that window, verifies its bounded
BGRA frame and exact interior-marker hash, stops, removes the output, and drains
the serial sample queue. It writes no pixel file and captures no existing user
window. The test window is closed before exit.

Other output callback samples are 2x2 synthetic BGRA buffers created by this
process. Temporary descriptors stay alive for the process lifetime; production
needs a process-owned interop ABI singleton and drained operation owners. This
is a technical probe, not a production adapter. Ordinary arm64 is the only
executed architecture; x86_64 aggregate-return ABI and arm64e are unproven.

Known first failure: a console process without AppKit bootstrap reaches a native
CGS_REQUIRE_INIT assertion during SCShareableContent enumeration. The successful
path calls NSApplicationLoad, obtains sharedApplication with prohibited
activation policy, and pumps the main CFRunLoop. In this process,
objc_getProtocol("SCStreamOutput") returned nil even after realizing SCStream.
The probe registers the SDK-declared optional method with public Objective-C
runtime APIs when that lookup returns nil. This is an observed consumer setup
detail, not a claim that every macOS framework/version omits that metadata.

Observed final commands, both exit 0:

```sh
dotnet build /tmp/flowspan-macos-capture-probe-20261004/Probe.csproj -c Release && dotnet run --project /tmp/flowspan-macos-capture-probe-20261004/Probe.csproj -c Release --no-build
dotnet build /tmp/flowspan-macos-capture-probe-20261004/Probe.csproj -c Debug && dotnet run --project /tmp/flowspan-macos-capture-probe-20261004/Probe.csproj -c Debug --no-build
```

See `NOTES.md`, `release.observed-stdout.log`, and `debug.observed-stdout.log` for
the measured results and limits. Logs are copies of the observed tool output,
written using apply_patch; they are not new test executions.
