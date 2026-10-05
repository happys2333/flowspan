# Returned callback versus synchronous native handoff borrow — 2026-10-06

Status: the finite
`ReturnedStartCallbackDoesNotReleaseBorrowedOwnersBeforeNativeHandoffExit` slice
is accepted locally. Full task4/task5/MCC/native/hosted/three-platform/v1 gates
remain open. Base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a dirty
working-tree overlay, not a pristine commit. Evidence root:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Scope is the actual managed Capture/Block orchestration and shared typed callback
ABI with controlled native effects on this host. This does not execute native
ScreenCaptureKit capture or prove the complete acquisition-race/observer matrix.
The Fact is in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete frozen RED07/Debug GREEN/Release GREEN test bytes share SHA256
`a713341ebc68eb9c5d6e9244107bf6592464c9d40eb0668e13b233d109a137f9`.

## Adopted RED and finite production repair

`native-start-borrow-red-debug07` actually builds and executes1 Failed/0 Passed.
Frozen test line34 expects the four Start-specific Capture/primitive caller
release attempt/confirmed flags `(false,false,false,false)`; actual is alltrue.
Earlier assertions already confirm actual Start callback result/exit, pending
synchronous invocation return, actual Stop/drain success and joined Dispose.
Later assertions are not claimed to pass in RED. This is a precise Start-owner
borrow violation, not an aggregate release-count assertion.

The only RED-to-GREEN production change is in
`src/Flowspan.Platform.MacOS/MacOSRemoteWindowScreenCaptureKitApi.cs`:
SHA256 `05efad915e5644cb0f39714c5ba6df0da8672df9c9369feb78b17a16d47e014e`.
Two fixed booleans, `startInvocationBorrowExited` and
`stopInvocationBorrowExited`, are published from the synchronous invocation's
outermost finally after push/selector/Invoke/pop exits normally or exceptionally.
Dependency release uses `HasActiveNativeInvocationBorrowCore()`; `ReleaseBlock`
checks corresponding borrow eligibility under `gate` before recording a caller
release attempt. Native effects, waits and observers stay outside `gate`; there
is no new await of `ManagedInvocationDrain`. Pool-debt/root-qualification policy
is unchanged. This is not complete acquisition or native exception containment.

`native-start-borrow-green-debug01` and
`native-start-borrow-green-release01` actually pass1 with identical full test
bytes. Frozen source diff, qualified TRX and stdout were read directly.

## Pending and terminal observations

A real Start thread executes the one-argument callback ABI through
`runtime.Invoke`; that call actually returns. An after-callback hook then holds
the synchronous native handoff frame using a real barrier. Start is issued,
settled/result/exit true, but `InvocationReturned` false. Separate external
Stop/removal/sample-barrier/drain completes; Stop issued/returned/settled/result/
exit are true. A real Dispose thread joins within its bound and reports the
fail-fast `InvalidOperationException`, without releasing Start's borrowed caller
or Stream/source dependencies. Elapsed time does not authorize cleanup.

GREEN's four Start caller flags remainfalse. Its resource-use region is already
closed, active0 and join-complete: that fact does not prove the synchronous
handoff borrow has exited. Stop has exited its own handoff and may independently
release its caller0 or1; aggregate runtime release0 is not required. Pending
cleanup is `(push4,pop3,remove1,barrier1,objects0,queue0,source0,sourceOwners2,
physicalDrained=true)`, with capture charge `before+1`. These are strongly held
helper snapshots, not pending cross-GC weak-retention proof.

Finally opens the barrier and actually joins both Start and Dispose threads.
Normal drain and repeated Dispose then complete with one terminal cleanup:
2 roots/2 copies,2 releases,2 root frees,0 live roots/blocks; pool4/4, independent
object/queue/source cleanup, both primitive lifetimes terminal and charge back
to `before`. Raw runtime teardown and NoInlining helper exit precede the outer
forced GC; weak Capture/completion/operations/source/callback-marker graphs are
unreachable. This does not generalize to every native return/observer placement.

## Snapshot history, regression and audit binding

Archive-first recorder SHA256:
`de801da994b16693b356a50e59af1880bfb267d81c0761ba3b7845f177f4f957`.
All five adopted stages record tar-create and tar-extract independently, both
raw exits0. Historical recorder remains
`13e28b6d141b2bc579a62eb6c02eba6ae00bee18a900f122f3bc7b2ef963d565`.
Attempts01,03–06 are incomplete snapshots without build/test/TRX and excluded,
not product RED. Attempt02 actually executes an aggregate RED but is superseded
for contract precision. The original pipe-tar failure mechanism is unestablished;
archive-first success does not diagnose that historical cause.

`focused-debug-native-start-borrow01` /
`focused-release-native-start-borrow01` each actually pass151: exact prior150
qualified identities plus this Fact, removed[],0 skipped/nonterminal. The five
adopted stages bind503 selected source inputs and complete138-file runtime
inventories before/after. Standards:0 hard/0 judgment findings, read-only and
untested by that reviewer. Spec:0 finite-scope findings.
Root `native-start-borrow-root-saved01` / `native-start-borrow-root-current01`
raw audit exits are0, reports passed=true/violations=[] and byte-identical to
corresponding worker reports. Documentation independently verifies all five
stage and four worker/root receipts, report/script/recorder SHA256s and cmp;
it runs no build/test/audit/replay.

- Auditor: `aa882cfc92c2d1db48e764b1a70826a4c94969408c446346fea361c25b3a20de`.
- Replay: `8edb3bdd630c7cff3d284d2218fd33f325a7d51133c8df8adcc743597d2c3299`.
- Saved report: `748df1c83f884dd193bb8f7bf2008a01f1d2179269bda2ba964ee67bfcbad942`.
- Selected-current report: `b3bee3d6a68dd34011e89f91ac5a1d4ee682125cb0b237a76d5c09c53578d195`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/native-start-borrow-replay.sh native-start-borrow-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
inputs at its freeze, not whole-tree/tools or a pristine commit. Subsequent test
source edits already make that current equality historical; saved evidence is
unchanged. Last project D/R remains390, not rerun; no new full/quality/security/
native/CI gate. Existing delegate0/global Capture-admission limits stay unchanged.
This step edits only this evidence file; task4/task5/MCC/v1 and Goal stay open.
