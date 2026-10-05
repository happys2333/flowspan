# Task-owned native Block lifetime probe

This independent, explicit-opt-in executable exercises the actual
`MacOSRemoteWindowBlock.Prepare` → `AcquireCopy` primitive and real macOS
`_Block_copy` / `_Block_release` calls. It requires macOS ordinary arm64.
Default/help do not initialize native code. Unsupported `--run` exits 3 with
`native_pass=false`; a skip is not a passing native result.

Build the project separately with isolated artifacts, then execute the DLL in a
fresh process supervised by an external deadline (GNU `timeout` shown):

```sh
dotnet restore tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj --locked-mode --artifacts-path /tmp/flowspan-enumeration-native-block-20261005/artifacts
dotnet build tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj -c Debug --no-restore --artifacts-path /tmp/flowspan-enumeration-native-block-20261005/artifacts
timeout --signal=TERM --kill-after=5s 45s dotnet /tmp/flowspan-enumeration-native-block-20261005/artifacts/bin/Flowspan.MacOS.BlockProbe/debug/Flowspan.MacOS.BlockProbe.dll --run
```

Run the same frozen source in Release as a separate actual execution. Save raw
stdout/stderr and exit status, command/cwd, source and runtime manifests before
and after each run. An unchanged copied log is not a new execution. Require the
fixed `block_probe=pass` line, exit 0 and empty stderr; managed failure diagnosis
is bounded and does not expose pointers or exception text.

The one lifetime scenario verifies an inert managed owner before acquisition,
then one real stack-to-heap capture copy. An extra heap-to-heap `_Block_copy`
returns the same heap address without another real `CopyCapture` helper entry.
That counter is observation only and never authorizes cleanup or retirement.
The probe calls the published actual two-argument unmanaged invoke function on
a dedicated thread with two nil, unused object arguments; its managed callback
waits at a controlled gate. Caller-owned `Dispose` confirms its own native
release while the extra copy keeps both retirement observations incomplete.
Releasing that later last copy makes the actual capture disposer run and
`GCHandle.Free` return normally. Native retirement then completes while the
admitted managed invocation remains active and managed drain is still pending.
Only callback and completion-observer exit allows managed drain to complete;
the thread is additionally joined to observe actual ABI return. Cleanup is in
`finally`, releases owned references at most once and joins the dedicated thread.

The function pointer is read before release. Once the controlled callback has
entered, the real thunk holds its captured state as a managed local, so this
deliberate interleaving never rereads the freed heap block. It prevents new
invocations after the last release. `ManagedInvocationDrain` by itself still
precedes the native ABI return; the extra thread join is probe evidence, not a
new production drain promise. Damaged native pointers, Objective-C/ABI faults
or process termination remain outside managed containment; an external deadline
must bound the process. A deadline is not cleanup or lifetime proof.

No AppKit, ScreenCaptureKit, TCC, screen pixels, secure input, permission prompt,
third-party window or network API is called. The primitive's library and Block
metadata remain process-lifetime as before. This proves a task-owned actual
Block ABI lifetime interleaving only, not SCK dispatch/capture drain, all
enumeration ownership, protection, physical devices, production sharing,
minimum-OS/Intel/arm64e support, release readiness or v1 acceptance.
