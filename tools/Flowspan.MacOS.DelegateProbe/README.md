# Synthetic macOS delegate ABI probe

This independent .NET 10 tool is excluded from `Flowspan.slnx` and all product
composition. It references the internal, portable callback-owner and generation-router candidates in
`Flowspan.Platform.MacOS` through a narrow friend-assembly declaration. It adds
no NuGet packages and keeps the repository's recommended analyzers, code-style
checks, deterministic build and warnings-as-errors settings enabled.

This is a **no-capture** probe. It never creates SCStream, AppKit, windows or
samples; enumerates content; preflights/requests permission; reads titles or
pixels; or injects input. Its native surface contains only Foundation loading,
Objective-C class/method/ivar/message operations, retained associations, native
retain/release, superclass deallocation, autorelease pools and GCD dispatch.

## Explicit execution

Default and `--help` perform no native probe calls and print exactly:

```text
delegate_probe=skip; reason=explicit_run_required; native_calls=0; capture_executed=false
```

Unknown arguments return exit 2 before native initialization. Only
`--run-synthetic` or `--run-associations` may invoke native code, and only on ordinary-arm64 macOS 15.2
or later. Other hosts print an explicit unsupported-host Skip; it is not a
native pass. The minimum comes from the SDK's active/inactive delegate methods,
not a change to the existing capture candidate's 14.2 floor.

To isolate this probe and all referenced build outputs from concurrent solution
QA, use the following commands from the repository root:

```sh
dotnet build tools/Flowspan.MacOS.DelegateProbe/Flowspan.MacOS.DelegateProbe.csproj -c Debug -p:RestoreLockedMode=true --artifacts-path /tmp/flowspan-macos-delegate-probe-20261004/artifacts
dotnet /tmp/flowspan-macos-delegate-probe-20261004/artifacts/bin/Flowspan.MacOS.DelegateProbe/debug/Flowspan.MacOS.DelegateProbe.dll
dotnet /tmp/flowspan-macos-delegate-probe-20261004/artifacts/bin/Flowspan.MacOS.DelegateProbe/debug/Flowspan.MacOS.DelegateProbe.dll --help
dotnet /tmp/flowspan-macos-delegate-probe-20261004/artifacts/bin/Flowspan.MacOS.DelegateProbe/debug/Flowspan.MacOS.DelegateProbe.dll --unknown
dotnet /tmp/flowspan-macos-delegate-probe-20261004/artifacts/bin/Flowspan.MacOS.DelegateProbe/debug/Flowspan.MacOS.DelegateProbe.dll --run-synthetic
dotnet build tools/Flowspan.MacOS.DelegateProbe/Flowspan.MacOS.DelegateProbe.csproj -c Release -p:RestoreLockedMode=true --artifacts-path /tmp/flowspan-macos-delegate-probe-20261004/artifacts
dotnet /tmp/flowspan-macos-delegate-probe-20261004/artifacts/bin/Flowspan.MacOS.DelegateProbe/release/Flowspan.MacOS.DelegateProbe.dll --run-synthetic
ArtifactsPath=/tmp/flowspan-macos-delegate-probe-20261004/artifacts dotnet format tools/Flowspan.MacOS.DelegateProbe/Flowspan.MacOS.DelegateProbe.csproj --no-restore --verify-no-changes --include tools/Flowspan.MacOS.DelegateProbe/Program.cs tools/Flowspan.MacOS.DelegateProbe/Native.cs tools/Flowspan.MacOS.DelegateProbe/NativeDelegateProbe.cs
```

Standard non-isolated builds use `bin/<Configuration>/net10.0/` as usual. Build
and run the already-produced DLL separately: a no-native default is not a
successful native execution. Inspect both exit code and `delegate_probe=pass`.

## What the explicit synthetic run actually checks

It registers one NSObject subclass with three nonthrowing reverse entries and
checks its native method metadata against the SDK-declared encodings:

| Selector | Ordinary-arm64 encoding |
| --- | --- |
| `stream:didStopWithError:` | `v32@0:8@16@24` |
| `streamDidBecomeActive:` | `v24@0:8@16` |
| `streamDidBecomeInactive:` | `v24@0:8@16` |

The declarations are from local macOS SDK 27.0 `ScreenCaptureKit.framework/Headers/SCStream.h`
(terminal line 629, active line 658, inactive line 665). No SCStreamDelegate
protocol, SCStream instance, or ScreenCaptureKit session is created: method
metadata belongs to this synthetic NSObject subclass. Typed `objc_msgSend`
actually invokes all three selectors; stream/error arguments are opaque fake
identities, never messaged, retained or dereferenced.

One process-rooted pool reserves four slots and publishes four +1-owned native
bridges. Forced collections before and after callbacks verify managed roots
remain usable. The executable assertions cover:

- a native terminal-before-binding fact, exact one-time binding, one replayed
  notification and active observations that cannot reopen admission;
- a GCD-delivered terminal without any sample, direct and Task.Run descendant
  self-join rejection, and a handler deliberately blocked outside the state gate;
- 48 further GCD dispatches completing while that handler remains blocked,
  with measured simultaneous reverse entries, terminal-once delivery, admission
  closure before retirement joins and eventual `ManagedInvocationsExited`;
- late callbacks on a retired bridge, wrong-stream isolation from a different
  live owner, immutable published addresses and early stream-binding mismatch;
- fixed-slot exhaustion and fault checks independent of retirement completion.

The successful run prints one fixed-schema line. `native_callbacks` is the
actual counter incremented at the reverse entries, and `published_bridges` is
the actual publication count, not a claimed SCStream event count.

## Retention and evidence limits

Every published NSObject bridge, class and immutable owner mapping is retained
until process exit. There is no bridge release or address reuse path. Capacity
four is this probe's finite publication budget, not a production memory bound.
GCD work owns its managed context until its thunk exits; published bridge
retention is deliberately independent of that context lifetime.

`ManagedInvocationsExited` joins only handlers admitted by the managed owner.
It does **not** mean NativeDrained, native delegate unbinding, or no future native
callbacks. Late synthetic sends intentionally exercise retained tombstones.
Managed faults are contained and independently fail the probe; Objective-C
exceptions, damaged native pointers or ABI/process faults may terminate it.
Bounded managed waits do not turn an uncertain native operation into cleanup;
use an external process/job deadline in automation.

Synthetic NSObject messages are not real SCStream source-loss events. This tool
does not prove native delegate retention, unbinding, source disappearance,
capture drain, TCC/protection/input/emergency-stop behavior, physical devices,
packaged identity or releases. macOS 15.2 minimum, Intel and arm64e execution
remain separate unverified platform evidence; only ordinary arm64 is enabled.

## Opt-in Foundation association proof (MSC Phase 2a)

`--run-associations` is a separate no-capture mode. It uses real task-owned
NSObject sources, one permanent +1-owned stateless bridge, one process-rooted
portable generation router, and a registered NSObject tag subclass containing
only one signed 64-bit numeric generation. It creates no SCStream or AppKit
objects and uses no permission, sample, pixel or input APIs. It allocates no
GCHandles. Default, help, unknown-argument and `--run-synthetic` behavior remain
unchanged. An unsupported association host prints `association_probe=skip`;
that is not a native pass.

```sh
dotnet build tools/Flowspan.MacOS.DelegateProbe/Flowspan.MacOS.DelegateProbe.csproj -c Debug -p:RestoreLockedMode=true --artifacts-path /tmp/flowspan-msc-native-association-20261005/artifacts
dotnet /tmp/flowspan-msc-native-association-20261005/artifacts/bin/Flowspan.MacOS.DelegateProbe/debug/Flowspan.MacOS.DelegateProbe.dll --run-associations
dotnet build tools/Flowspan.MacOS.DelegateProbe/Flowspan.MacOS.DelegateProbe.csproj -c Release -p:RestoreLockedMode=true --artifacts-path /tmp/flowspan-msc-native-association-20261005/artifacts
dotnet /tmp/flowspan-msc-native-association-20261005/artifacts/bin/Flowspan.MacOS.DelegateProbe/release/Flowspan.MacOS.DelegateProbe.dll --run-associations
ArtifactsPath=/tmp/flowspan-msc-native-association-20261005/artifacts dotnet format tools/Flowspan.MacOS.DelegateProbe/Flowspan.MacOS.DelegateProbe.csproj --no-restore --verify-no-changes --include tools/Flowspan.MacOS.DelegateProbe/Program.cs tools/Flowspan.MacOS.DelegateProbe/NativeAssociationProbe.cs tools/Flowspan.MacOS.DelegateProbe/NativeAssociationInterop.cs
```

The actual runtime checks the tag class and NSObject superclass, own dealloc
implementation and `v16@0:8` encoding, `q` ivar encoding, runtime-reported
64-bit size/alignment, and runtime ivar offset within subclass storage. The
offset is not a hard-coded universal NSObject layout. `class_addIvar` receives
alignment **log2 = 3**. The ordinary-arm64 `objc_super` layout is checked against
independent compiler shape evidence. `objc_msgSendSuper` receives the verified
NSObject superclass, not TagClass; it is not confused with
`objc_msgSendSuper2`'s starting-class convention.

Each source receives its retained association once. No managed retirement
removes or replaces it. The separate native tag budget is at most 16, and is
returned only after actual NSObject superclass dealloc returns successfully.
The dealloc thunk saves its numeric fact beforehand and never reads or messages
the freed object afterward. Sixteen retained, retired sources exhaust the tag
budget despite reuse of managed Capture permits. A seventeenth tag is refused
before allocation; actual source deallocation returns one permit for a new,
never-reused generation.

Each typed native callback establishes an autorelease pool, retains the valid
borrowed source, reads and retains its immutable tag, verifies the exact tag
class, and reads the numeric generation before routing. No native operation
runs under the router gate. Its handler actually releases the caller's source
+1 owner; the callback's independently acquired references still permit native
association/tag reads afterward. Nested finally paths release tag and source
references and the callback pool. Every admitted registration failure and every
contained reverse-entry failure is an outer execution gate. A later fatal
OutOfMemoryException is retained independently of an earlier ordinary failure;
the original exception is rethrown outside the native boundary.

The run also checks actual typed Active/Inactive/Stopped messages, forced GC,
late old-generation isolation after managed permit reuse, 64 healthy lifecycle
iterations and two unknown-tag callbacks that are rejected without guessing a
generation. Successful execution prints one bounded fixed-schema line after
all pools and transient native owners have been released. Counters distinguish
explicit source/tag owners and callback-acquired retain/release pairs from
association lifetime and actual tag dealloc entries/superclass returns. The
one permanent bridge and registered classes intentionally remain until process
exit. Failed or uncertain tag allocation/publication/release retains a charged
quarantine; a failed run never claims balanced cleanup or a native pass.
Objective-C exceptions, invalid native pointers and ABI/process faults can
still terminate the process; use an external job deadline.

This is **only MSC Phase 2a**: unknown/nil tags are rejected, not recorded as
retained early facts. It does not prove exact-initializer pending ownership,
nil-read/publication/pending-clear races, third association re-read, ambiguity
or poison behavior. The output therefore keeps
`early_publication_proved=false`. MSC task 2 remains open until Phase 2b proves
those obligations. No capture composition, native delegate drain, actual
source-loss, TCC/protection/input, minimum-OS, Intel/arm64e, physical-device or
production/release acceptance follows from this Foundation proof.

The contract is in `specs/v1/native-remote-window/macos-stream-delegate/` and
ADR 0030. Actual local runs, raw stdout/stderr and input hashes are retained
under `/tmp/flowspan-msc-native-association-20261005/`; copied records are not new
executions.

See the scoped requirements/design/tasks in
`specs/v1/native-remote-window/macos-delegate-ownership/` and ADR 0029. Actual
local execution logs and input hashes are recorded separately under
`/tmp/flowspan-macos-delegate-probe-20261004/`; a record copied with `apply_patch`
is not a new test execution.
