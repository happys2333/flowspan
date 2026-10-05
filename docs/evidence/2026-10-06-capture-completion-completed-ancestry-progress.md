# Same-Capture completed ancestry self-join rejection — 2026-10-06

Status: this finite completed-ancestry pair is accepted locally. Both slices are
direct GREEN without a production change/new RED; remaining gates stay open.
Recorded base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree
overlay, not a pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Facts: `StartCompletedDirectDisposeRejectsSelfJoin` and
`StartCompletedAsyncDescendantStopRejectsSelfJoin`, in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Actual sequential stages99/100 are `99-start-completed-direct-dispose-debug` /
`100-start-completed-direct-dispose-release`; then101/102 are
`101-start-completed-descendant-stop-debug` /
`102-start-completed-descendant-stop-release`. Each executes1 Passed,0 Failed,
skipped or nonterminal. Frozen stdout/qualified TRX agree and all four receipts
verify. Documentation reads frozen test/helper/operation sources, not live edits.

## Actual completed callback and active ancestry

Native Start takes an actual extra heap retain of its real one-argument owner
and returns before callback. A real thread invokes that retained pointer through
the typed ABI. Inside the actual guarded completed wrapper, real Capture
completed first publishes exit=true; its hook then calls the same Capture.
No callback-side xUnit assertion is used as containment evidence.

Direct hook calls `Capture.Dispose` and records InvalidOperationException with
message `macOS native callback cannot dispose its own cleanup owner.` It returns
while the actual parent completed wrapper remains paused at a test barrier.

Descendant hook uses normal inherited ExecutionContext: `Task.Run`, then
`await Task.Yield`, then `await Capture.StopAndDrainAsync` on a different managed
thread. The child enters once and faults with InvalidOperationException:
`macOS native callback cannot join its own cleanup.` The hook observes the
actual child's completed fault while its parent remains active, then reaches the
same barrier. This is active same-owner ancestry across an await, not an inactive
historical descendant or an asynchronous resource-use join.

Pending observations confirm resource admission remains open/active1/join
incomplete, and all four Capture/primitive caller release-attempt/confirmation
flags arefalse. Actual releases0, no native Stop selector/invocation and no
removal/barrier/dependent native/source cleanup occur. Start issued/returned/
settled/result=true and exit publication remain; Stop is unissued/pending.
The actual extra copy stays held, release attempts0 and charge `before+1`.
The descendant rejection is a Stop call, not a second pending Dispose attempt.
Snapshots strongly hold Capture; they are not pending cross-boundary GC proof.

## Real return and external cleanup

Finally releases the parent barrier and uses actual ABI `Thread.Join`; the
descendant helper also observes `GetAwaiter().GetResult` on the real child task,
confirming the same rejection before external cleanup. Task fault observation
is not an OS-thread join or a completed notification substitute.
External actual Start result/drain succeed, and both Dispose attempts return
without failure. Known callers release once and independent Stream release
retires its held copy; totals2 roots,3 copies/retains,3 releases,2 root frees,
0 live roots/blocks. Both native-retirement/managed-drain facts are terminal;
removal/barrier and independent object/queue/source cleanup are confirmed.
Final resource admission is closed/active0/join-complete; shell/accounting
returns to `before`. Raw runtime teardown, NoInlining helper exit and outer
forced GC precede assertions; weak Capture/completion/operations/source/markers
are unreachable, and both primitive FirstFailure observations are null.

Frozen SHA256s:

- Direct-only test: `d3f22853d785212b2b5ba2cc46dbc85b965d44b7718b76166e58ae6127aa2b17`.
- Final pair test: `c209f627bafd190d10705a6fa3704ad7eceff553d91399abe6acb81fb3c74a45`.
- Unchanged API.

  SHA-256: `fca3d0c49f980bd4d2185c78993a1aea5570016298af21a50f37b56e66546084`.

## Finite matrix and audit bindings

Actual `focused-debug-start-completed-ancestry-pair01` /
`focused-release-start-completed-ancestry-pair01` each pass147,0 Failed/skipped/
nonterminal. Exact145 prior qualified identities plus these2, with no removals.
All six stages bind503 selected inputs and complete138-file runtime inventories
before/after. Standards:0 hard/0 judgment findings. Spec:0 concrete findings.
Root actually executes `start-completed-ancestry-pair-root-saved01` /
`start-completed-ancestry-pair-root-current01`: both raw exits0, empty violations,
verified receipts and reports byte-identical to their worker counterparts.
Documentation reads raw reports/stdout/TRX, verifies root receipts/report/script
SHA256s and compares worker/root bytes without executing audit/replay.

- Auditor: `b83c7b2e9d87d36bfce42621c04b1afb69bab44ab330af0312590e0097c731c1`.
- Replay: `6e0e861cdfde4f928a50c1e2b82420acfc0ff6491ae50109b89a1369ac72149f`.
- Saved report: `501b256cc78606b095a28de8ddd929690983b43134a14cbb8ac1257adf48bee4`.
- Selected-current report: `709e7b41f4f90589ff0468deb301006dcbacf1c1abf23b374e2a0f3f475641cd`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/start-completed-ancestry-pair-replay.sh start-completed-ancestry-pair-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at this freeze, not the whole tree/tools or a pristine commit;
later source edits make it historical. Frozen saved evidence remains unchanged.
No acceptance is inferred for async resource-use joins, inactive old descendants,
opposite entry points, other callback/Stop equivalents, races, outer `IsDrained`, complete task4/task5/
MCC/MSC, native containment, hosted CI or v1. Last recorded project D/R remains390;
no new aggregate/quality/full/native gate. This step edits only this evidence
file and runs no build/test/replay.
