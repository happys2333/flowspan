# Task-owned native Block lifetime probe

This independent, explicit-opt-in executable exercises the actual
`MacOSRemoteWindowBlock.Prepare` → `AcquireCopy` primitive and real macOS
`_Block_copy` / `_Block_release` calls. It requires macOS ordinary arm64.
Default/help do not initialize native code. Unsupported `--run` exits 3 with
`native_pass=false`; a skip is not a passing native result.
The separate `--run-enumeration` mode uses the actual enumeration orchestration
and completion owner described below; the original `--run` behavior and result
line remain unchanged.
The additional `--run-capture-completion` mode uses the real one-argument
completion adapter and native Block primitive described below. The existing
default/help output and both previous native mode result lines remain unchanged.

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

## Enumeration composition mode

`--run-enumeration` calls the same production `EnumerateDirectWithOperations`
with a fresh real `MacOSRemoteWindowSourceOwnershipPool` (one owner, 128 source
slots, one batch) and the actual
`MacOSRemoteWindowEnumerationCompletion.Prepare` with its default native Block
operations. The completion preparation is observed inert before return; the
production core attaches it to the reserved batch before `AcquireCopy`.
No second enumeration, pool, source or retirement state machine is implemented.

The external enumeration effects alone are controlled: access/application checks
return true without querying TCC or AppKit, runtime initialization and pool effects
are synthetic, and a synthetic content owner yields an empty window collection.
The nonzero content argument is never dereferenced or sent to an Objective-C API.
Its retain/release accounting is a contract fixture, not actual native content
ownership evidence. The real Block stack-to-heap acquisition, extra heap copy,
unmanaged two-argument invocation, releases, root free and lifetime observations
are native.

The dedicated invoke thread calls the real enumeration callback and then holds
a completion observer after the core's first-idle notification. The core itself
must release its caller-owned Block reference while the extra native copy keeps
retirement pending. At that point the enumeration task is incomplete and the
same pool still charges all `(1, 128, 1)` records. Later last-copy release must
complete actual native capture retirement while managed drain, the enumeration
task and batch settlement remain pending. Only after the observer exits may
managed drain complete. An independent thread join observes ABI return; an empty
enumeration result and pool usage `(0, 0, 0)` then prove healthy settlement of this
one controlled scenario. The probe never disposes the completion behind the
production core's back, and its extra copy receives at most one release attempt.

Build frozen inputs separately, then run each configuration once under the
repository's task-process-group watchdog using new, nonexisting evidence paths:

```sh
dotnet restore tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj --locked-mode --artifacts-path /tmp/flowspan-enumeration-composition-native-20261005/artifacts
dotnet build tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj -c Debug --no-restore --artifacts-path /tmp/flowspan-enumeration-composition-native-20261005/artifacts
python3 .github/scripts/posix-watchdog.py /tmp/flowspan-enumeration-composition-native-20261005/debug-run01 45 2 -- dotnet /tmp/flowspan-enumeration-composition-native-20261005/artifacts/bin/Flowspan.MacOS.BlockProbe/debug/Flowspan.MacOS.BlockProbe.dll --run-enumeration
dotnet build tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj -c Release --no-restore --artifacts-path /tmp/flowspan-enumeration-composition-native-20261005/artifacts
python3 .github/scripts/posix-watchdog.py /tmp/flowspan-enumeration-composition-native-20261005/release-run01 45 2 -- dotnet /tmp/flowspan-enumeration-composition-native-20261005/artifacts/bin/Flowspan.MacOS.BlockProbe/release/Flowspan.MacOS.BlockProbe.dll --run-enumeration
```

These are invocation examples, not claimed execution results. Preserve actual
command/cwd/exit records and before/after source and runtime bindings. Require the
exact `enumeration_block_probe=pass` record, exit 0, empty stderr and successful
watchdog terminal facts. Unsupported hosts return an explicit skip with exit 3;
that is not a native pass. Managed observation deadlines do not return product
capacity or prove cleanup. The external watchdog bounds only its task-owned
process group, not arbitrary descendants or native cleanup.

This mode proves the actual Block lifetime composition through one controlled
empty-result enumeration on the host tested. It does not call or verify real
SCK dispatch/content/list ownership, TCC preflight, AppKit, window enumeration,
pixels, input or network APIs. It does not provide native fault injection or
prove all enumeration obligations. A fresh task-owned `NativeCaptureProbe --run`
is still required as separate healthy SCK regression evidence. Production sharing,
protection/secure input, global Capture admission, minimum-platform support,
release readiness and v1 acceptance remain unchanged and unverified.

## One-argument completion lifetime mode

`--run-capture-completion` uses the actual inert
`MacOSRemoteWindowCaptureCompletion.Create` adapter and its same staged
`MacOSRemoteWindowBlock` with default native operations. It verifies preparation
has not attempted any per-operation root/copy effect, then acquires a real
stack-to-heap Block and checks its exact one-object-argument `v16@?0@8` descriptor.
A real extra heap-to-heap `_Block_copy` must retain the same physical capture
without another capture-copy helper entry. The dedicated invoke thread calls
the saved real one-argument function pointer with one nil, unused object argument.

The callback returns normally and records its completed notification once, but
the completed observer stays active at a controlled managed gate. Releasing the
caller's own +1 must leave native retirement and managed drain pending while the
extra heap retain remains. Releasing the actual last native copy must confirm
the original root free and native retirement, while the admitted completed
observer still keeps terminal managed drain pending. Only after the observer
exits may managed drain complete. The separate dedicated-thread join, not that
managed task, observes final ABI return. The saved function and the thunk's
strong managed state local avoid rereading a freed Block; no invocation starts
after the last release.

Cleanup in `finally` opens the observer gate, joins the task-owned thread and
attempts each still-owned caller/extra release at most once only after a confirmed
join (or when no invocation thread was created). If join fails or throws, it
retains those known references and reports failure; an observation timeout does
not authorize release of a pointer a late thread may still enter. It does not
dispose the managed waiting gates without a confirmed join. The helper count is
observation only; it cannot authorize cleanup or prove retirement.

Run both configurations from the same frozen inputs in separate fresh processes
with new evidence paths and the external task-process-group watchdog:

```sh
dotnet restore tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj --locked-mode --artifacts-path /tmp/flowspan-capture-completion-native/artifacts
dotnet build tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj -c Debug --no-restore --artifacts-path /tmp/flowspan-capture-completion-native/artifacts
python3 .github/scripts/posix-watchdog.py /tmp/flowspan-capture-completion-native/debug-run01 45 2 -- dotnet /tmp/flowspan-capture-completion-native/artifacts/bin/Flowspan.MacOS.BlockProbe/debug/Flowspan.MacOS.BlockProbe.dll --run-capture-completion
dotnet build tools/Flowspan.MacOS.BlockProbe/Flowspan.MacOS.BlockProbe.csproj -c Release --no-restore --artifacts-path /tmp/flowspan-capture-completion-native/artifacts
python3 .github/scripts/posix-watchdog.py /tmp/flowspan-capture-completion-native/release-run01 45 2 -- dotnet /tmp/flowspan-capture-completion-native/artifacts/bin/Flowspan.MacOS.BlockProbe/release/Flowspan.MacOS.BlockProbe.dll --run-capture-completion
```

These are invocation examples, not claimed results. Preserve command/cwd,
stdout/stderr/exit and before/after source/runtime inventories. Require the
fixed `capture_completion_block_probe=pass` record, exit 0, empty stderr and
successful watchdog terminal facts. Unsupported hosts return a separate skip
with exit 3 and `native_pass=false`; a skip or deadline is not a native pass.

This mode is a healthy task-owned one-argument Block lifetime observation only.
It does not create a Capture, call SCK/AppKit/TCC, inspect screen pixels, prompt
for permissions, use input/network APIs or inject native faults. The adapter
name does not imply actual SCStream Start/Stop orchestration or Capture cleanup
acceptance. A separately frozen `NativeCaptureProbe --run` Debug/Release remains
required for healthy task-owned SCK regression. Neither mode proves arbitrary
future native-copy scheduling, native fault containment, protection/secure input,
global Capture admission, physical LAN, minimum-OS/Intel/arm64e support,
production sharing, release readiness, full MCC/MSC or v1 acceptance.
