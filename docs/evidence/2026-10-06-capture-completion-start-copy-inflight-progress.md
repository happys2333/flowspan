# Start copy-in-flight shutdown and exact published owner — 2026-10-06

Status: the finite
`StartCopyInFlightStopAndDisposePreserveExactOwnerAndOrderShutdown` slice is
accepted locally. Task4 constructor publication/pool-pop first-fatal coverage,
task5 and full MCC/native/hosted/CI/v1 gates remain open. Base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a dirty working-tree overlay,
not a pristine commit. Evidence root:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Scope is actual managed Capture/Block orchestration and the shared typed
callback ABI with controlled native effects. No native ScreenCaptureKit capture,
complete acquisition-race matrix or three-platform acceptance is established.

## Direct GREEN without production repair

Only one Fact/NoInlining helper/immutable observation and its narrow field-read
helper are added at the top of
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Existing `AfterCopyCapture` is reused; old test/helper/fixture bytes are preserved.
Complete frozen Debug/Release test bytes have SHA256
`3091d399a4591f0c8b362fcd2f5db4c1b1638198a046bc8c7a960b40416fd4f7`.
No production or shared-support-file change is introduced. API SHA256 remains
`05efad915e5644cb0f39714c5ba6df0da8672df9c9369feb78b17a16d47e014e`.
`start-copy-inflight-debug01` / `start-copy-inflight-release01` each actually
build and pass1, direct GREEN. No product RED or new production repair is claimed.
Frozen Fact/helper, raw stdout and qualified TRX were read directly.

## Held copy effect and real shutdown request

There is no fault injection. A real synchronous Start thread reaches the first
controlled heap/copy-helper effect; a real barrier holds it before CopyBlock
returns and AcquireCopy confirms. Actual root/copy attempts are1, root confirmed,
copy unconfirmed, physical copy/root/live block each1, and public Pointer0.
Capture's `startBlock` is reference-identical to the original completion owner.

Actual StopAndDrain remains pending. An external uncancelled watchdog observes
TimeoutException and the original drain's incomplete state; it neither cancels
that drain nor supplies a fake result. Actual Dispose reports
InvalidOperationException. Pending admission is requested=true/deliveryClosed1/
disposed=false; Start/Stop are unissued/unreturned/unsettled, results waiting and
exitsfalse. Caller release/resource release/selectors/ABI/removal/barrier counts
are0; constructor pool is1/1, retained source owners2 and charge `before+1`.
Start resource-use remains open/active0/join-incomplete, with no FirstFailure.
These are strongly held helper snapshots, not pending cross-GC retention proof.
Elapsed watchdog time does not authorize cleanup or imply cancellation.

## Ordered resume and terminal cleanup

Finally unconditionally opens the copy barrier and actually joins the Start
thread. The exact published owner stays current. Original Start legally succeeds;
the original drain then issues and confirms successful Stop, without delivery
resurrection: deliveryClosed stays1 and final disposed=true. Start/Stop issued/
normal-return/settled/result true/exit-success facts confirm, ResultFailure null.
Two copy hooks/two selectors/two callback ABI returns occur; SourceUnavailable0,
actual output removal/sample barrier each1. No fabricated cancelled-Start false
result is required by this accepted existing shutdown order.

Known cleanup completes:2 roots/2 copies,2 caller releases/2 root frees,0 live
roots/blocks; all four original synchronous pool scopes confirm, pool4/4.
Stream/output/configuration release in order; queue/source release once; both
primitive native-retirement and managed-drain facts are terminal without failure.
Charge returns to `before`; repeated Dispose adds no failure or effects. Raw
teardown confirms `(roots0,blocks0,releases2,rootFrees2,invokes2,returns2)`.

Raw controlled-runtime teardown and NoInlining helper exit precede outer forced
GC. Weak Capture/completion/operations/source/sample/unavailable marker graphs
are unreachable. This does not prove every in-flight copy/fault/observer race.

## Regression and root receipt binding

`focused-debug-start-copy-inflight01` /
`focused-release-start-copy-inflight01` each actually pass156: exact accepted
prior155 qualified identities plus this Fact, removed[],0 skipped/nonterminal.
Four stages bind503 selected source inputs and complete138-file runtime
inventories before/after. Archive-first recorder stays
`de801da994b16693b356a50e59af1880bfb267d81c0761ba3b7845f177f4f957`;
tar-create/extract raw exits0 are independently recorded. Historical recorder
`13e28b6d141b2bc579a62eb6c02eba6ae00bee18a900f122f3bc7b2ef963d565`
and earlier exclusions stay unchanged; no old pipe-tar cause is established.
Standards:0 hard/0 judgment. Spec:0 missing/out-of-scope/incorrect implementation.
Both reviews are read-only and execute no tests.

Root's fixed replay wrapper records actual argv/cwd/stdout/stderr/exit/times,
copied auditor and receipt. `start-copy-inflight-root-saved01` /
`start-copy-inflight-root-current01` raw exits0, passed=true/violations=[];
reports are byte-identical to corresponding worker reports. Workers originally
produced report-only directories, with no worker raw receipts. Documentation
independently verifies exactly6 receipts—four stage plus two root—SHA256s and
cmp, reads auditor/wrapper, and executes no build/test/audit/replay or new agents.

- Auditor: `530a1ab8b906ef672b63ecc3f13b5affe60257cac81d31e73a06a3f458ea92ef`.
- Replay: `607800fbaaa63dac9cbc2f91f49a0e40c192f3b4593b43b42eda97ced75b6ba1`.
- Saved report: `892b26103722f66eb93bdddaa4718ac3e2ff3d10d9d068bb968a178fd2f8af1d`.
- Selected-current report: `bf5f64f5dfbe2f5b70b94c8a418ff2d6c68848f9f83b6121dcdf751056632396`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/start-copy-inflight-replay.sh start-copy-inflight-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at the156 freeze, not whole-tree/tools or a pristine commit;
next-tracer source changes make it historical. Frozen saved evidence is unchanged.
Last project D/R remains390, not rerun; no new full/quality/security/native/CI gate.
This step edits only this evidence file, without commit/push. Task4/task5/MCC/
native/CI/v1/Goal remain open; delegate0/global Capture-admission limits unchanged.
