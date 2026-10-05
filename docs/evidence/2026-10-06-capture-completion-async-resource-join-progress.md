# Same-Capture asynchronous pre-release resource-use join — 2026-10-06

Status: the finite `StopAndDrainWaitsForActiveCompletedResourceUse` slice is
accepted locally; full task4/task5/MCC/MSC/outer/native/hosted/v1 gates stay open.
Recorded base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree
overlay, not a pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Execution scope: the actual managed Capture/Block orchestration with controlled
native effects on this host. This does not execute ScreenCaptureKit capture or
prove native exception containment or Windows/Linux platform behavior.

The Fact is in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete RED/Debug GREEN/Release GREEN test bytes are unchanged:
`5c2a3402118475c1801213e27fae1eaf1c442b70e040a11aef311a922ca26f9a`.

## Actual RED and finite repair

`async-resource-join-red-debug01` actually compiles and executes1 Failed/0 Passed.
Frozen test line32 is Assert.False: expectedFalse, actualTrue for drain-task
completion while the real Start completed resource-use region remains active.
Earlier assertions already confirm actual Stop result/exit, output removal and
sample barrier physical progress. Later assertions are not claimed to pass in
RED. This is premature logical drain completion, not failed physical Stop.

The only production repair is in `MacOSRemoteWindowScreenCaptureKitApi.cs`:
SHA256 `46b9200ae11c18306221e17efd13a1916212bd5c87356162834a66acb779d013`.
After physical facts, DrainAsync obtains both actual owners' `CloseResourceUse`
tasks before any await, then awaits their fixed joins outside `gate`. It freshly
reads settled/unsafe state under the lock, preserving the existing finally
`ThrowPendingFatal`. No Block/native release or `ManagedInvocationDrain` await
is added: this is a pre-release resource-use join, not post-release lifetime
retirement. The fixture tests one active Start completed region, not every
combination of simultaneously active Start/Stop/action/failure observers.

Actual `async-resource-join-green-debug01` and
`async-resource-join-green-release01` each pass1 with the same complete test.
The source diff, frozen test hashes, stdout and qualified TRX were read directly.

## Pending and terminal observations

Start takes an actual extra heap retain and returns before callback. A real
thread invokes the retained one-argument pointer; actual Capture completed
publishes exit and its guarded wrapper pauses. External Stop/drain performs real
Stop/removal/barrier. A bounded SpinWait observes actual premature task return
or owner closure; no elapsed delay authorizes cleanup or manufactures a join.

GREEN confirms physical `IsDrained` alreadytrue but drain task incomplete:
Start owner closed/active1/join-pending, all four caller release flagsfalse,
actual releases0, no dependent object/queue/source release. Both Start/Stop
result/issued/returned/settled/exit facts are true, yet the retained copy remains
held and charge is `before+1`. These are same-helper strongly held snapshots,
not pending cross-boundary weak-graph/GC retention proof. Physical `IsDrained`
and outer fallback binding remain separate from this accepted logical join.

Finally opens the barrier and independently joins the actual ABI thread before
awaiting the original drain task and both Dispose attempts. Drain succeeds;
known caller/Stream-held copy/independent native/source cleanup release once.
Final totals2 roots,3 copies/retains,3 releases,2 root frees,0 live roots/blocks;
both native-retirement/managed-drain facts are terminal, with no FirstFailure.
Shell/accounting returns to `before`. Raw teardown, NoInlining helper exit and
outer forced GC precede assertions; weak Capture/completion/operations/source/
callback-marker graphs are unreachable. Thread join does not generalize the
production resource-use join into a guarantee about every reverse-ABI return.

## Regression and root audit

Actual `focused-debug-async-resource-join01` /
`focused-release-async-resource-join01` each pass148: exact147 prior qualified
identities plus this Fact, no removals. Stdout/TRX agree,0 skipped/nonterminal.
All five stages bind503 selected inputs and complete138-file runtime inventories
before/after; all stage/root receipts verify. Standards:0 hard/0 judgment code
findings. Spec:0 finite-scope findings.
Root actually executes `async-resource-join-root-saved01` /
`async-resource-join-root-current01`: both raw exits0, passed/empty violations,
verified receipts and reports byte-identical to corresponding worker reports.
Documentation independently reads reports, verifies receipts/SHA256s and compares
worker/root bytes; it does not execute audit/replay.

- Auditor: `2024e4ce85da471a85cb79d8aca2759d0efeeda5ffa3a19ee0fff0d48ff47b71`.
- Replay: `41629476423aa492ee6ca1e7902483c5d4981ac2ec38b57640b663008e55c6fd`.
- Saved report: `b4242e95a0027b6c4b10c0022daa49a29791126a47d62eb5e275e70e3ef50f60`.
- Selected-current report: `023c210d1334111db3984a84f38cf7db38a28d60fb7b89abd5f19c37719b739c`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/async-resource-join-replay.sh async-resource-join-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at this freeze, not the whole tree/tools or a pristine commit;
next-tracer source edits make it historical. Frozen saved evidence is unchanged.
Other callback placements, unknown effects, self/opposite/inactive ancestry,
races and outer fallback/full cleanup remain outside this finite acceptance.
Existing delegate0/global Capture-admission limits are unchanged. Last project
D/R remains390, not rerun; no new quality/full/native/CI gate. This step edits
only this evidence file and runs no build/test/replay or new agents.
