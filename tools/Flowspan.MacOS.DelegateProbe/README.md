# Synthetic macOS delegate ABI probe

This independent .NET 10 tool is excluded from `Flowspan.slnx` and all product
composition. It references the internal, portable callback-owner candidate in
`Flowspan.Platform.MacOS` through a narrow friend-assembly declaration. It adds
no NuGet packages and keeps the repository's recommended analyzers, code-style
checks, deterministic build and warnings-as-errors settings enabled.

This is a **no-capture** probe. It never creates SCStream, AppKit, windows or
samples; enumerates content; preflights/requests permission; reads titles or
pixels; or injects input. Its native surface contains only Foundation loading,
Objective-C class/method/message operations, autorelease pools and GCD dispatch.

## Explicit execution

Default and `--help` perform no native probe calls and print exactly:

```text
delegate_probe=skip; reason=explicit_run_required; native_calls=0; capture_executed=false
```

Unknown arguments return exit 2 before native initialization. Only
`--run-synthetic` may invoke native code, and only on ordinary-arm64 macOS 15.2
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

See the scoped requirements/design/tasks in
`specs/v1/native-remote-window/macos-delegate-ownership/` and ADR 0029. Actual
local execution logs and input hashes are recorded separately under
`/tmp/flowspan-macos-delegate-probe-20261004/`; a record copied with `apply_patch`
is not a new test execution.
