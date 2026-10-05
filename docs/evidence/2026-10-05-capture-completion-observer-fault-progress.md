# Same-Capture Start observer-fault slices — 2026-10-05

Status: this finite three-slice matrix is accepted locally. Other task4/task5
cases and full MCC/MSC/native/hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay, not a
pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

The Facts are:

- `StartActionFaultAfterActualCaptureResultPreservesFatalAndReturnsKnownOwnership`.
- `StartFailureObserverFaultDoesNotReplaceEarlierActionFatalOrRetainKnownOwnership`.
- `StartCompletedNotificationFaultPreservesFatalAndReturnsKnownOwnership`.

They live in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Documentation reads the relevant frozen test/helper/operation sources and the
frozen API, not an in-progress live-tree implementation.

## Actual faults and evidence sequence

The opt-in controlled operations create the actual production completion and
wrap its real Capture action, failure observer and completed notification.
Every selected wrapper first calls its actual Capture callback, then throws a
nested wrapper containing the selected original OutOfMemoryException through
the same typed one-argument ABI. An exception is not merely assigned to a mock
result or primitive field.

| Slice | Actual callback order | Failure identity |
| --- | --- | --- |
| Action | Capture action settles successful Start result=true; action wrapper then throws A | A remains externally reportable without rolling back the settled result |
| Failure observer | Action wrapper throws A after Capture action; real Capture failure observer handles A, then its wrapper throws B | Earlier A remains first failure; later B does not replace it |
| Completed-only | No action/failure fault injected; real Capture completed/exit notification publishes before its wrapper throws C | C becomes first failure; actual throw counters are0/0/1 |

Action stages are `observer-action-red01`, `observer-action-red02`,
`observer-action-green01` and `observer-action-green-release01`. The complete
action test bytes remain identical throughout:
`66fbe70e18182c3bba9b43f9ba5c6a5b94e72ff4ac1236225197227c49ac402e`.

Both REDs are compiled behavioral failures, each1 Failed/0 Passed, not compile
failures. Root checked stdout/TRX and receipts; documentation also reads their
raw failure results and verifies frozen test SHA. RED01 fails at frozen test
line23: expected the same original A, actual null from initial Start. After
initial Start fatal surfacing is repaired, RED02 reaches line43: expected
baseline charged-owner count0, actual1. Its preceding assertions already
confirm2 independent releases,2 root frees and both terminal lifetime facts;
the remaining shell/accounting retention is the second defect. Assertions after
each failing line are not claimed as passing in that RED.

The complete action test then actually passes1 in each Debug/Release GREEN.
Failure-observer stages `observer-failure-direct-debug01` /
`observer-failure-direct-release01` and completed-only stages
`observer-completed-direct-debug01` /
`observer-completed-direct-release01` likewise each pass1. Stdout and qualified
TRX agree on all six single passes, with0 skipped/nonterminal results. Failure
and completed-only are direct GREEN on the action repair: no new RED or extra
production repair is invented for them.

## Repair and bounded lifetime proof

The API repair checks `ThrowPendingFatal` after the initial Start invocation
normally returns and records its true returned fact. A synchronous managed
callback fault can therefore be surfaced even when the actual Capture action
has already settled success. The successful result and returned fact are not
rolled back. This change is not an acceptance of the `startRequested` repeated
Start cached-result branch.

Final completion lifetime checking samples `FirstFailure` freshly after
independent native-retirement and managed-drain terminal observations and keeps
the original fatal reportable. A contained managed callback failure alone is
not unknown ownership debt after all independent releases and both terminal
facts are confirmed. Those confirmed facts permit shell/root/accounting return;
unknown release effects or lifetime debt remain disqualifying.

Each helper joins its real Start/ABI thread, then exercises actual Stop/drain,
both Dispose attempts, removal/barrier and independent native/source cleanup,
records terminal state, performs raw controlled-runtime teardown and exits its
NoInlining helper. Outer forced GC and all assertions occur afterward. The
single GREENs confirm2 roots/copies,2 releases,2 root frees,0 live roots/blocks,
baseline charged-owner return and weak Capture/completion/operations/source/
callback-marker graphs becoming unreachable. Initial Start, drain and both
Dispose attempts preserve the selected original fatal. Test-thread join is
independent of completed notification, but these sequential terminal slices do
not prove resource-use joins while failure/completed observers are active.

Frozen bindings:

- Action-only test: `66fbe70e18182c3bba9b43f9ba5c6a5b94e72ff4ac1236225197227c49ac402e`.
- Failure-added test: `b897ff20ff874cfca2232a2d161ff32b06e1f33d851640b78df2bf3acc65c0e9`.
- Final three-slice test: `5369bc2b8c956659a96dab554bab825642960f0413a9e85268e212fe46bf73d7`.
- Final API.

  SHA-256: `c7333c640de0c986e642a12090c61e50eacd0a38ea1f069b2ab9c4de5a7b9c20`.

- Unchanged completion owner: `2fb11b11a464c9dffe55383456132b35b7605429215a9a5b6732d6a4dc148e35`.
- Unchanged Capture operations: `f194c4fca6d5bf1dd905d927e384c26b72f3e302cd66cf804a859d75e442dd53`.

## Finite matrix acceptance and replay bindings

Actual `focused-debug-observer-matrix01` and
`focused-release-observer-matrix01` each pass142. Stdout and qualified TRX agree
on142 executed/Passed,0 Failed/skipped/nonterminal results. The exact previous
139 qualified identities remain, plus only the three Facts above, with no
removals. All ten stage records bind503 selected source inputs and complete
138-file runtime inventories, before/after, with raw commands/results and
receipts. Standards:0 concrete findings. Spec:0 concrete findings.

Root actually executes `observer-matrix-root-saved01` /
`observer-matrix-root-current01`: both raw exits0, empty violations, verified
receipts and reports byte-identical to their worker counterparts. Documentation
independently reads the raw reports and focused stdout/TRX, verifies both root
receipts and report/script SHA256s, and compares both worker/root reports. It
does not execute the auditor or replay. Bindings:

- Auditor: `113eb888f07be08872dfcb8b98b1772e955074d791c4712c42baead5aba9491a`.
- Replay: `09776a1099758da27995e21b01c5a9dfd63a58d0dc14fd3b2d0d241fd7629bb7`.
- Saved report: `302fd2626ff4a48b671f8131a777ff5ef07ec4cde17feadb670976b6faf4e946`.
- Selected-current report: `d577b990d8cc13cf856a5f05cd0425959e51845ee7036fa87bf0f698c8b26aef`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/observer-matrix-replay.sh observer-matrix-next-saved01 saved
```

Use a fresh label. Replay audits evidence and runs no tests. Selected-current
equality covers503 selected test/build inputs at this freeze, not the whole
tree/tools or a pristine commit. New repeated-Start tracer work will make that
equality historical; frozen saved evidence remains unchanged.

No acceptance is inferred for repeated Start caching, active failure/
completed resource-use joins, self-join, races, outer-drain behavior, complete
task4/task5/MCC/MSC, native-fault containment, hosted CI or v1. Last recorded
project Debug/Release remains390; no new aggregate/quality/full/native gate is
claimed. This documentation step edits only this file and runs no build, test
or replay.
