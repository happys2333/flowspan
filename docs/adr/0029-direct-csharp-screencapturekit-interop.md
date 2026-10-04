# ADR 0029: Direct C# ScreenCaptureKit interop on ordinary arm64

- Status: Accepted for the macOS capture candidate; not production readiness
- Date: 2026-10-04
- Requirements: NR1, NR3, NR6, NR8, NR10

## Context

Flowspan prefers one implementation language when platform integration can be
owned and tested safely. ScreenCaptureKit exposes Objective-C objects, optional
protocol methods, and Blocks completion handlers rather than a C capture API.
We must prove those lifetimes before choosing a native-language shim.

A local feasibility probe on macOS 27.0.1 arm64, Xcode/SDK 27.0 and .NET SDK
10.0.301 actually exercised copied Blocks after caller release and forced GC,
dynamic SCStreamOutput callbacks, retained CoreMedia samples, and capture of
only a task-owned 64-by-64 window. Debug and Release each completed 1,000 Block
callbacks and 1,000 synthetic sample callbacks, with balanced owners and a
successful exact-window capture, Stop completion, output removal and serial
queue barrier. This proves feasibility on that host, not a supported-version,
architecture, permission or production-cleanup matrix.

## Decision

Continue with C# and a narrow macOS-local interop implementation. Do not add a
Swift shim without new evidence that the managed bridge cannot be maintained
safely. Initially enable the candidate only on macOS 14.2 or later and ordinary
arm64; 14.2 is required by the selected includeChildWindows configuration API.
The minimum-version choice is an API constraint, not evidence of execution on
14.2. Intel and arm64e remain unsupported by this candidate until separately
implemented and executed.

Use source-generated C interop and typed Objective-C message entry points.
Install the SCStreamOutput method with a nonthrowing reverse entry and the
SDK-declared method encoding. SCStreamOutputType is NSInteger (nint), not int32.
Resolve and validate existing runtime protocol metadata first; when unavailable,
register the documented optional method through public Objective-C runtime
APIs. Process-level classes, protocols, descriptors and function roots are
initialized once and stay alive for the process. Native identities, handles,
window titles and samples never enter Transport, persisted descriptors or
diagnostics.

The Blocks implementation is an explicit **compiler-ABI bridge**. It follows the
public Clang Apple Blocks ABI, including capture copy/dispose helpers and final
native ownership of GCHandle-backed state. `_NSConcreteStackBlock` is exported
compiler machinery: the SDK Block.h says these variables are compiler-only and
must not be treated as ordinary high-level application API. Accept this narrow,
documented dependency only with reproducible architecture-specific probes and
nonthrowing reverse entries. Ordinary arm64 results do not establish arm64e
pointer-authentication compatibility.

The production candidate requires the Desktop's already initialized AppKit
owner and main run loop. It must not initialize a second NSApplication, change
activation policy, or bootstrap AppKit on a worker thread. The standalone probe
may bootstrap its own console UI owner. An earlier console-only enumeration
actually aborted with `CGS_REQUIRE_INIT` (exit 134); that failure is part of the
evidence, not a permission denial to retry blindly.

## Ownership and safety boundaries

- Enumeration is prompt-free. Native window ID, owning PID, process-start
  identity and geometry resolve a host-local token; a fresh native check occurs
  immediately before capture. Titles are not required for generic labels.
- The source catalog reports capture capability but input unsupported and
  protection **Unknown**. Enumerability or a successful capture does not prove
  protected-content safety, and does not make the host production-ready.
- Callback samples are borrowed. Retain before callback return when deferring
  work, then balance release even when validation, copying or delivery fails.
  Retention is not a pixel lock or a guarantee against sample invalidation.
  Deferred work rechecks sample validity, data readiness and Complete frame
  status before accessing image data.
  CPU copying checks a successful read-only CVPixelBuffer lock and uses matching
  unlock flags, validated geometry/stride and existing frame-byte ceilings.
- Capture callbacks admit at most one pending sample alongside one active
  worker. Replaced samples are released. The worker re-establishes the exact
  source-use scope in its own ExecutionContext; callback lexical/AsyncLocal
  authority must not be assumed to flow through a native GCD queue.
- Synchronous Pause/Stop/Emergency Stop first close delivery admission. A
  confirmed local latch is not confirmation of native SCStream disposal.
  Native Stop completion, output removal, queue barrier, worker drain and final
  native release form a separate owned asynchronous operation. Uncertain stop
  retains retiring owners; it never releases callback roots speculatively.
- The controller borrows native boundaries. Desktop composition must explicitly
  join capture-owner disposal/stop completion in real generation cleanup before
  reporting complete host cleanup. Controller `FullyStopped` alone is not that
  native ownership evidence. Pause/resume cannot silently claim a confirmed
  native restart; the initial candidate may require a new session.
- Managed exceptions never escape reverse callbacks. NSError and native return
  failures become bounded typed faults and close admission. This is not a claim
  that C# catch can contain Objective-C NSException, invalid native pointers or
  process-level native faults; those and fatal allocation/lifetime paths require
  their own execution evidence.

## Reproducible evidence

Keep a standalone, non-product probe in
`tools/Flowspan.MacOS.CaptureProbe`, excluded from the solution. Default mode
exercises synthetic ABI/lifetime tests. Only explicit `--native-self-window`
may capture, only after Granted preflight, and only the tool's own window
matched by PID and window number. Nonmatching architecture, headless context or
absent permission must report a Skip rather than a native pass. It must neither
request permission nor save pixels or inspect existing user-window titles.
The reproducer uses the macOS 14.4+ current-process-only content enumeration
API, so its minimum version is 14.4 rather than the adapter candidate's 14.2.
This narrows the probe's source visibility; it does not change the product's
permission or protection policy.

The original temporary probe source SHA-256 was
`7b613b75985a0a715f31b424aa0b252b3c83c4271ec83f995e54dbc40b22f7bd`.
Its exact interior marker hash was `e92fac80964c0b25`; the copied frame was at
most 16,384 bytes. After the successful queue barrier, Release observed 501 ms
and Debug 503 ms with zero late callbacks. A finite observation does not prove
that callbacks can never arrive later. The repository reproducer is a new input
and must record its own execution rather than inherit this source hash.

## Alternatives and consequences

A minimal C-callable Swift helper remains a fallback if actual ABI, support,
crash/leak or maintenance evidence warrants it. It would need a versioned
contract, deterministic build inputs, packaged native assets, signing and
callback-lifetime tests. It is not justified merely by Objective-C syntax.

Direct interop avoids a second production toolchain but makes the small unsafe
bridge a maintained security boundary. Portable fault contracts, matching-host
native smoke and signed-package tests are all necessary; none substitutes for
the others. Permission revoke, source loss, blocked delivery, failed native
start/stop, supported older macOS versions, Intel, packaged TCC identity,
protection, independent Emergency Stop, input, leak detection, signing and
notarization remain open release gates. This ADR closes none of Tasks 5, 6 or
the v1 Goal.

## Sources

- [Clang Apple Blocks ABI](https://clang.llvm.org/docs/Block-ABI-Apple.html)
- [Clang pointer authentication, Blocks](https://clang.llvm.org/docs/PointerAuthentication.html#blocks)
- Local macOS SDK 27.0: `usr/include/Block.h` (copy/release and compiler-only
  symbols), `usr/include/objc/runtime.h` (method/protocol registration),
  ScreenCaptureKit `SCStream.h` and `SCShareableContent.h`, CoreMedia
  `CMSampleBuffer.h`, CoreVideo `CVPixelBuffer.h`, AppKit `NSApplication.h`.
- The local protocol declaration compiled to
  `v40@0:8@16^{opaqueCMSampleBuffer=}24q32` for arm64 and x86_64. The latter is
  compile-time shape evidence only, not Intel execution or aggregate-return
  validation.
