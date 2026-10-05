# Same-Capture active Start action / duplicate completion — 2026-10-05

Status: the finite first active Start-action behavior is accepted locally.
The remaining task4/task5 cases and full MCC/MSC/native/hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay, not a
pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

The focused Fact is
`FirstStartActionStillActiveDoesNotReleaseCallerAfterDuplicateCompletion` in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Its frozen RED test SHA256 is
`8115c5b74f05d28c13a9529a24dc781b0622f2c64c9eaebf637f13524d18a89f`.

## Confirmed RED

`active-start-action-red01` actually restores and compiles the test before
execution. Restore/build exit0; build reports0 warnings/errors. Test and runner
exit1. Stdout and qualified TRX agree on1 executed,1 Failed,0 Passed,0 skipped,
with no nonterminal result. The failure is the caller-release assertion at
frozen test line23: expected0, actual1. This is a behavior assertion failure,
not a compile failure or a manufactured RED. Root independently checked the
stdout/TRX, receipt and frozen test SHA; this document's preparation also read
the raw result and verified that frozen test SHA.

Later assertions in the Fact are not reached after this first failure. The RED
does not establish their success, final reclamation correctness or an accepted
resource-use join.

## Test structure and intended boundary

The controlled Start invocation performs an actual extra heap retain of the
real one-argument completion and returns before invoking any callback. Two real
threads subsequently invoke the retained pointer through the typed ABI. The
first is an error callback whose first action pauses inside `SourceUnavailable`
after the local Start result has become false/settled. The second is a duplicate
successful callback; its real thread join is separately observed. Its completed
notification must not substitute for the still-active first action's return.

While that first action is paused, the test attempts Dispose and snapshots
caller, primitive, native-owner and dependent-resource effects. The intended
contract is no caller release and no removal/barrier/dependent native or source
release during this active action. The confirmed RED instead records one caller
release; the unchanged complete test passes after the production repair.

The helper's `finally` releases the barrier and joins both actual ABI threads
before raw controlled-runtime teardown. It then exercises final Stop/drain,
Dispose/repeated Dispose, removal/barrier and independent cleanup, records
terminal lifetime/counters, leaves the helper, and performs outer forced GC
before assertions. This is test structure, not proof that production resource
release occurs after every active action or ABI return. The pending snapshot is
collected while the helper strongly holds Capture; outer GC is after unblocking
and teardown, not a pending cross-boundary weak-graph retention proof.

## Accepted finite GREEN and production repair

`active-start-action-green01` is a superseded worker candidate following a
guard-position self-check; it is not the final accepted GREEN. Root confirms
actual execution of `active-start-action-green02` and
`active-start-action-green-release02`: each1 Passed and exit0. Actual
`focused-debug-active-start-action01` and
`focused-release-active-start-action01` each pass139 and exit0. Their stdout
and TRX were also read during documentation. The unchanged test bytes bind the
RED and final GREEN to the SHA above. The focused qualified set is the exact
previous138 identities plus this one Fact, with no removals. Locked receipts bind
503 selected source inputs and complete138-file runtime inventories before/after.

The repair changes three production files. Root confirms an actual callback
wrapper and a closed-admission/active0 join covering all three callback regions;
an incomplete join does not record a `ReleaseBlock` attempt. Caller
release qualification requires a settled result or a uniquely unissued path;
Dispose rejects owner ancestry, and final native/managed lifetime confirmation
is followed by a fresh `FirstFailure` observation. Accepted frozen SHA256s:

- `MacOSRemoteWindowCaptureCompletion.cs`: `2fb11b11a464c9dffe55383456132b35b7605429215a9a5b6732d6a4dc148e35`.
- `MacOSRemoteWindowCaptureOperations.cs`: `f194c4fca6d5bf1dd905d927e384c26b72f3e302cd66cf804a859d75e442dd53`.
- `MacOSRemoteWindowScreenCaptureKitApi.cs`: `1f02891b3ec1ba73b76c30b4b6fa119e90b9cf3b1f705de4fd4194d031937bf3`.

Standards:0 concrete findings. Spec:0 concrete findings. Root actually executes
`active-start-action-root-saved01` and `active-start-action-root-current01`:
both raw exits0, empty violations, verified receipts and reports byte-identical
to their worker counterparts. This document's update independently verifies
those receipt hashes, report/script SHA256s and report equality, without running
the auditor or replay. Bindings:

- Auditor: `12d2ed964cc90958c3de402299ffe95c41aa0e11ef358784cbd59e0b52385606`.
- Replay: `c7f698d68981402e7db0c144fcd43fd3d26e8347b9f4ba107b45b3a44267ee82`.
- Saved report: `1225384dd3cba88df60cc835976755229af4865dfc5349c224709d2d68376b37`.
- Selected-current report: `95dddfffc5e822696c59ed36d264d489f3b584f6ae9234907eb4b76293f53aaf`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/active-start-action-replay.sh active-start-action-next-saved01 saved
```

Use a fresh label. Replay audits evidence; it runs no tests. Selected-current
equality covers the503 inputs at this freeze, not the whole tree or a pristine
commit. Root has authorized the next three task4 observer-fault vertical cases;
subsequent source edits make that equality historical. Frozen saved evidence is
not rewritten.

Only this first active-action release gate is accepted. No acceptance is
inferred for all wrapper faults/duplicates, self-join, asynchronous joins,
StopAndDrain ancestry, outer `IsDrained`, other races/resource-use scenarios,
or pending cross-boundary weak-graph/GC retention.

No new project/full-solution/quality/native/hosted CI result is claimed here.
The last recorded project Debug/Release count remains390. This finite Start
duplicate/action case does not accept task5 as a whole, other resource-use
joins/races, complete MCC/MSC, native-fault containment or v1. This documentation
step runs no build, test or replay and does not alter production or tracking
files.
