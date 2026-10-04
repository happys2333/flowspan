# Historical temporary-probe record

This file records the prior /tmp source and prior executions, not the modified
repository tool. Original text follows unchanged.

# ScreenCaptureKit direct C# interop findings, 2026-10-04

All work is in this temporary directory. No repository source, commit, push,
GitHub message, or permission request was made by this probe task.

## Verdict

There is no demonstrated technical need for a Swift shim on ordinary arm64.
Direct C# source-generated C interop, typed objc_msgSend calls, public runtime
dynamic classes/protocols, UnmanagedCallersOnly IMPs, and the documented Clang
Blocks ABI completed actual exact-window ScreenCaptureKit capture on this host.
Continue the direct C# route through a narrow owned interop layer and record its
compiler-ABI dependency in the ADR. This is evidence of feasibility, not a
production adapter or completion of Task 6.

## Environment and reproducible input

- macOS 27.0.1, build 26A434, ordinary arm64.
- Xcode 27.0, build 27A266a, macOS SDK 27.0.
- .NET SDK 10.0.301, runtime .NET 10.0.9.
- Program.cs: 725 lines, SHA-256
  `7b613b75985a0a715f31b424aa0b252b3c83c4271ec83f995e54dbc40b22f7bd`.
- Probe.csproj SHA-256:
  `ba1ddf0c5d42ee5c7a419a5e26c78a276819ace71949c950c981f6073822420c`.
- Exact final Debug/Release build-and-run commands are in README.md. Both
  completed with exit 0 and zero compiler warnings/errors.
- The observed stdout log files are copies of actual tool output, made with
  apply_patch. They do not substitute a new execution for the measured runs.

## Actual observations

Each final configuration completed:

1. SCStreamConfiguration round-trips of width, height, BGRA format, queueDepth 3,
   CMTime 1/30, and CGRect values. Measured C# sizes were BlockLiteral 40,
   BlockDescriptor 40, CGRect 32, CMTime 24, CMSampleTimingInfo 72 bytes.
2. 1,000 native heap Block copies on a suspended GCD serial queue; caller Block
   owners were released and GC was forced before resuming native invocation.
   All 1,000 callbacks ran, with 1,000 copy helpers, 1,000 dispose helpers, and
   zero live contexts after the queue barrier.
3. 1,000 dynamic Objective-C SCStreamOutput callbacks from a native GCD thread.
   Each received a synthetic 2x2 CoreMedia sample, retained it in the callback,
   released all producer ownership, locked the image read-only, copied exactly
   16 row-normalized bytes, verified the test pattern, unlocked and released it.
   No retained sample remained.
4. CGPreflightScreenCaptureAccess returned true. SCShareableContent enumeration
   completed and a desktop-independent exact-window filter was allocated.
   The reported source count is only a structural window/PID/geometry probe;
   it is not Flowspan protected-window policy eligibility. No titles were read.
5. A task-owned borderless NSWindow displayed four fixed sRGB quadrants.
   Catalog resolution matched both this process PID and the exact NSWindow
   windowNumber. Only that window was captured. One Complete BGRA 64x64 frame
   matched exact 4,096-byte interior markers, FNV-1a-64
   `e92fac80964c0b25`; reported whole-frame hash was `9e913b9daaa62825`.
   The full copy was bounded to 16,384 bytes. No pixel file was written.
6. Stop completion returned success; output removal returned success; the
   serial native sample queue barrier returned. The measured subsequent
   observation was 501 ms in Release and 503 ms in Debug with zero callbacks.
   Total native output callbacks were 1 in Release and 2 in Debug; only one
   Complete matching frame was consumed in each. Idle/status variation is
   expected and not treated as a deterministic callback-count contract.
7. Final global counter for every Block GCHandle context, including content,
   start, stop and dispatch completions, was zero. The registered ObjC
   class/protocol and three Block descriptors are intentional process-lifetime
   ABI metadata in this throwaway program.

## Failures retained as evidence

- Initial unsafe async C# context failed compilation with CS4004. Corrected by
  using a synchronous main thread and isolating unsafe methods; the main
  autorelease pool consequently stays on the thread that created it.
- objc_getProtocol("SCStreamOutput") returned nil even after SCStream class
  realization on this host. The probe allocates/registers that protocol using
  public runtime APIs and adds the SDK-declared optional instance method.
  Its encoding is `v40@0:8@16^{opaqueCMSampleBuffer=}24q32`. A read-only helper
  independently compiled this declaration with local clang to LLVM IR for
  ordinary arm64 and x86_64; both encodings agree. This is compile-time ABI
  evidence for x86_64, not x86_64 execution evidence.
  The primary probe agent then independently reproduced both compilations from
  `ProtocolEncoding.m`, which is never linked or loaded by the C# application:
  `xcrun clang -target arm64-apple-macos12.3 -isysroot <SDK-root> -x objective-c -S -emit-llvm ProtocolEncoding.m -o -`
  and the same command with `-target x86_64-apple-macos12.3`. Both emitted the
  documented encoding and target triple, with no compiler error. The filtered
  actual LLVM output is copied in `protocol-encoding.observed-stdout.log`.
- After synthetic ABI tests and Granted preflight, an initial console-only SCK
  enumeration aborted with exit 134:
  `Assertion failed: (did_initialize), function CGS_REQUIRE_INIT, file CGInitialization.c, line 44.`
  Successful execution uses public NSApplicationLoad, sharedApplication with
  prohibited activation policy, and a main-thread CFRunLoop pump. A product
  adapter must use the Desktop's existing AppKit owner/dispatcher and must not
  silently initialize a second UI owner from a worker thread.
- Generic-RGB layer colors were transformed in captured sRGB output. Explicit
  sRGB colors produced exact primary-color interior markers. The perfectly
  quadrant-filled whole-frame expected hash still differed; the probe therefore
  validates fixed interior markers exactly and reports the actual full hash
  separately. It does not claim that every rendered boundary pixel equals a
  software-filled ideal rectangle.

## API and ABI sources

Root: `/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk`.

- `System/Library/Frameworks/ScreenCaptureKit.framework/Headers/SCShareableContent.h`:
  SCWindow.windowID/frame/owningApplication/isOnScreen and SCShareableContent's
  completion-handler enumeration. Current-process redacted enumeration exists
  since macOS 14.4; it was not exercised because this host preflight was Granted.
- `System/Library/Frameworks/ScreenCaptureKit.framework/Headers/SCStream.h:614`:
  optional `stream:didOutputSampleBuffer:ofType:` method; SCStreamOutputType is
  NSInteger, hence nint, not int32. Same header defines desktop-independent
  SCContentFilter, stream creation/add/remove/start/stop, BGRA, queueDepth,
  minimumFrameInterval, no-audio and no-cursor configuration.
- `usr/include/objc/runtime.h:723,919,929`: class_addMethod and class allocation/
  registration. `:1279,1382,1392,1405,1419`: protocol method description and
  public allocate/register/add-method/adopt-protocol APIs. Protocols cannot be
  disposed and are immutable after registration; production must initialize
  exactly one process-level ABI singleton under a lock, resolve an existing
  registration first, and validate its documented method shape.
- `usr/include/Block.h:31,34`: _Block_copy/_Block_release. `:42` explicitly says
  `_NSConcrete*Block` are compiler-used variables and "Do not use these variables
  yourself". They are exported ABI symbols used here to implement a foreign
  compiler bridge, not ordinary high-level API. This exception must remain
  explicit in the ADR and be backed by architecture/OS probes.
- [Public Clang Apple Blocks ABI](https://clang.llvm.org/docs/Block-ABI-Apple.html):
  literal isa/flags/reserved/invoke/descriptor/captures, flag bit 25 for copy/
  dispose, bit 30 for signature, descriptor reserved/size/copy/dispose/signature.
  Heap recopy increments block ownership without necessarily rerunning the
  capture copy helper; the GCHandle owner is released only at final dispose.
- [Clang pointer-auth ABI](https://clang.llvm.org/docs/PointerAuthentication.html#blocks):
  arm64e block invoke/helper signing differs. This ordinary-arm64 probe does not
  support or prove arm64e.
- `System/Library/Frameworks/AppKit.framework/Headers/NSApplication.h:198,306,618`:
  sharedApplication, activation policy and NSApplicationLoad. The latter's
  documented use is initialization when loading Cocoa into a Carbon process;
  here it successfully bootstraps the console ABI test.
- `System/Library/Frameworks/CoreMedia.framework/Headers/CMSampleBuffer.h:44,684,829`:
  explicit CFRetain/CFRelease, synthetic ready sample creation, borrowed image
  buffer getter. `CoreMedia.framework/Headers/CMTime.h:37,89` gives 4-byte packed
  CMTime layout. `CoreVideo.framework/Headers/CVPixelBuffer.h:195,482,521` gives
  read-only lock flag 1, matching lock/unlock, and base-address-after-lock rule.

## Productization required before adapter readiness

No universal callback-stop guarantee is proved by a 501/503 ms observation.
There was no permission deny/revoke, secure input, source close during capture,
stale generation, blocked frame sink, native start/stop timeout, OOM/fatal,
packaged AppKit/TCC identity, leak detector, Intel execution, signing or notarized
runtime test. This probe's finally/timeout handling must not be transplanted as
production lifetime logic: uncertain native completion must retain retiring
owners, close frame admission and drain callbacks before releasing resources.
Completion cancellation must reject/release a late retained content result.
Native callback exceptions must stay inside the bridge and publish typed faults.

Suggested next low-level interfaces remain internal to the macOS project:

- prompt-free bounded shareable-window enumeration on the existing AppKit owner;
- exact source resolution behind the generation-bound portable registry;
- a single exact-window SCStream owner with Start, CloseFrameAdmission, Stop,
  output removal, native sample-queue drain and final resource release;
- a synchronous bounded read-only BGRA copy inside the native sample callback,
  then the portable owned-buffer/generation-aware sink; no raw native handles,
  titles or samples may reach Transport or Desktop diagnostics;
- process-level ObjC class/protocol and Block ABI registration, with per-owner
  strong callback roots, native-copy-aware GCHandle lifetimes and catch-all
  nonthrowing reverse entries.

## Minimal reproducible repository probe proposal

Root may choose a standalone `tools/Flowspan.MacOS.CaptureProbe` net10.0 project,
excluded from the solution and shipped product. Preserve this source hash as
prior local evidence, then reduce the reproducer intentionally rather than
copying the entire throwaway into production. Default mode should exercise ABI
and synthetic lifetimes; a named `--native-self-window` mode should additionally
require current Granted preflight and capture only its own test NSWindow. A
non-granted/headless host reports a structured Skip, which must not be counted
as native capture evidence. No permission-request API is linked. Document arm64
support now and add explicit x86_64 objc_msgSend_stret paths plus matching Intel
execution before claiming that architecture. Retain the native assertion and
public compiler-ABI dependency in the ADR and matching-host smoke record.
