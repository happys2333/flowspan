# macOS capture ABI probe

Independent, clean-room C# feasibility tool; not in `Flowspan.slnx`, not referenced
by any product project, and not a production capture adapter. It has no NuGet
dependencies. .NET compiler warnings remain errors; recommended production
analyzers are disabled only for this raw ABI probe project.

## Run

Requires the repository's .NET SDK and macOS 14.4 or later on ordinary arm64.
Unsupported OS/architecture returns a structured `probe=skip`; this is not test
evidence. No Intel or arm64e execution support is claimed.

From the repository root:

```sh
dotnet build tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj -c Debug
dotnet run --project tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj -c Debug --no-build
dotnet build tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj -c Release
dotnet run --project tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj -c Release --no-build
```

The default is **synthetic only**. It does not check capture permission, initialize
AppKit, enumerate shareable content, create a window, or start ScreenCaptureKit
capture. It checks:

- typed Objective-C configuration and by-value CGRect/CMTime round-trips;
- 1,000 native Block copies, forced GC before GCD invocation, and balanced
  copy/dispose helpers with zero remaining GCHandle contexts;
- 1,000 dynamic SCStreamOutput callbacks on a native queue, retaining synthetic
  2×2 BGRA CoreMedia samples after producer release and copying 16 bounded bytes.

Real capture is available only with this explicit flag:

```sh
dotnet run --project tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj -c Release --no-build -- --native-self-window
```

This mode additionally requires **already-Granted** prompt-free
`CGPreflightScreenCaptureAccess`. Otherwise it emits `own_window_capture=skip`
and never calls AppKit/SCK capture. No permission-request API is linked or called.

It bootstraps this standalone process's AppKit owner on its synchronous main
thread, using prohibited activation policy and a pumped CFRunLoop. It creates its
own 64×64 borderless, mouse-ignoring window containing fixed sRGB quadrants. It
uses macOS 14.4's documented, redacted
`getCurrentProcessShareableContentWithCompletionHandler:` API (available without
TCC user consent), matches both this process PID and the exact NSWindow window
number, and constructs a desktop-independent filter only for that exact window.
It does not enumerate global user content, read titles, capture existing user
windows, capture audio/cursor/child windows, or save pixel files.

One Complete BGRA frame is bounded to 16,384 row-normalized bytes. Fixed interior
markers are verified exactly; native scaling/boundary interpolation means the
whole-frame hash is reported, not asserted equal to an ideal software raster.
Stop completion, successful output removal, and a serial-queue barrier precede
resource release. An additional ≥500 ms observation checks for late callbacks;
this finite observation is not an unlimited callback-stop guarantee.

Exit codes: `0` means executed checks passed, but may include explicit native
capture Skip; `1` means an observed failure; `2` means unsupported arguments.
Always inspect `own_window_capture=pass` before counting a native capture run.
`--help` performs no tests or native calls.

## Lifetime and failure boundaries

This repository tool intentionally fixes the prior temporary probe's obvious
failure paths:

- a suspended synthetic queue is resumed and drained in `finally`;
- timed-out enumeration is terminally canceled; a late retained content result
  is released by the callback, including the timeout/result race;
- Block literal/GCHandle and descriptor allocation failures release temporary
  allocations;
- native allocations are checked before queue/capture use;
- unconfirmed Start or initial Stop completion is sticky: a later successful
  Stop cannot make an earlier uncertain operation's owner releasable;
- failed/unconfirmed Start, Stop, output removal or cleanup closes frame
  admission and retains native owners plus managed callback roots until the
  nonzero process exit. This is deliberate process-lifetime quarantine, not a
  production service cleanup/recovery strategy;
- managed exceptions stay within reverse callbacks, using constant failure
  markers; a damaged callback-owner invariant terminates the standalone probe.

The synchronous GCD barriers cannot be canceled and have no internal deadline.
Run the tool under an external job/process watchdog in automated environments;
terminate a hung probe rather than attempting to release uncertain native
owners. Native Objective-C/framework assertions can terminate the process.
Successful ABI descriptors/classes/protocols and loaded framework handles are
intentionally process-lifetime metadata.

This is not evidence for permission denial/revocation races, native start/stop
fault injection, source disappearance, blocked media sinks, protected windows,
secure input, external emergency stop, a physical two-device session, packaged
TCC identity, leak detectors, signing/notarization, Intel, or arm64e. The repaired
exception/timeout paths were reviewed but are not claimed as native fault-
injection evidence.

## ABI dependency and provenance

The bridge uses source-generated C interop, typed `objc_msgSend`, public runtime
class/protocol APIs, `UnmanagedCallersOnly` callbacks, and the published Clang
Apple Blocks ABI. `_NSConcreteStackBlock` is a compiler-used SDK symbol whose
header says not to use it directly in normal application code. This tool is an
explicit foreign-compiler ABI experiment, not a claim that this is ordinary
high-level ScreenCaptureKit API usage. Ordinary arm64 is the only executed ABI;
arm64e pointer-authentication and Intel aggregate-return paths are not supported.

`ProtocolEncoding.m` is a read-only SDK declaration check, never linked or loaded
by the C# tool. With Xcode installed, compile to LLVM IR (no executable):

```sh
xcrun clang -target arm64-apple-macos12.3 -isysroot "$(xcrun --sdk macosx --show-sdk-path)" -x objective-c -S -emit-llvm tools/Flowspan.MacOS.CaptureProbe/ProtocolEncoding.m -o -
xcrun clang -target x86_64-apple-macos12.3 -isysroot "$(xcrun --sdk macosx --show-sdk-path)" -x objective-c -S -emit-llvm tools/Flowspan.MacOS.CaptureProbe/ProtocolEncoding.m -o -
```

The optional SCStreamOutput method uses NSInteger (`nint`) and encoding
`v40@0:8@16^{opaqueCMSampleBuffer=}24q32`. SDK compilation for Intel is not Intel
runtime evidence.

The source derives from this project's temporary feasibility probe at
`/tmp/flowspan-macos-capture-probe-20261004`. Historical Program.cs SHA-256 was
`7b613b75985a0a715f31b424aa0b252b3c83c4271ec83f995e54dbc40b22f7bd`.
That hash belongs to the **prior source**, not the intentionally modified current
tool. Historical notes/logs are preserved under `evidence/prior-spike/`; current
repository-tool executions are separately recorded in `evidence/2026-10-04.md`.
