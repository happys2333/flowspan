# SourceUnavailable observer fatal priority with unknown copy — 2026-10-06

Status: the finite
`SourceUnavailableObserverFatalPreservesEarlierCopyFatalAndUnknownOwnership`
slice is accepted locally; full observer/task4/task5/MCC/native/hosted/CI/v1
gates remain open. Base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a
dirty working-tree overlay, not a pristine commit. Evidence root:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

This executes the actual managed Capture/Block orchestration and real user
SourceUnavailable delegate with controlled native effects. The acquisition-
failure path performs no native Start/Stop invocation or callback ABI execution;
it is not a completed-callback observer or native ScreenCaptureKit test.

## Direct GREEN characterization

The only code additions for this slice are the Fact and its helper in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete frozen Debug/Release test bytes have SHA256
`6d0f185086b21c2d0a4833fbdee640e9a7296d188ba6226f3008f709eae759e4`.
No production or supporting fixture repair is introduced. API SHA256 stays
`05efad915e5644cb0f39714c5ba6df0da8672df9c9369feb78b17a16d47e014e`.
`unavailable-observer-debug01` / `unavailable-observer-release01` each actually
build and pass1, direct GREEN. There is no adopted RED or claim of a new repair.
Frozen Fact/helper, raw stdout and qualified TRX were read directly.

The real Start copy helper makes its physical after-effect, then throws an
AggregateException/IOException wrapper containing fatal A. The actual
`CreateCaptureWithOperations` SourceUnavailable user delegate subsequently calls
and throws exactly once with a distinct wrapped fatal B. This is not injection
by replacing a primitive observer wrapper. B and A are not the same object.
Actual Start, StopAndDrain, first Dispose and repeated Dispose outer exceptions,
plus the completion primitive's FirstFailure, remain reference-identical A.
The user notification really executes, while native/callback invoke counts0
remain a separate fact; observer call1 does not mean callback ABI invocation1.

## Known cleanup and retained unknown ownership

Known output removal/sample barrier each occur once. Stream/output/configuration
release in that order, queue release and Capture-owned source release each occur
once. Physical `IsDrained` is true despite the thrown logical drain. First and
repeated cleanup observations agree, including the original fatal identity;
repeated Dispose creates no new release or retirement attempt.

Start root/copy attempts are1, root confirmed and copy unconfirmed. The actual
unknown physical copy remains represented by live root/block1, but public
completion Pointer is0 in both observations. Caller release and root-free
attempts0 remain0; native-retirement/managed-drain terminal facts stay pending.
Stop primitive is absent, and capture charge stays `before+1`. Physical drain
does not authorize guessing an unknown pointer or retiring its ownership debt.

Raw fixture teardown has live roots/blocks0 but release/root-free/invoke/return
counts0: controlled-runtime disposal is not production release or retirement.
NoInlining helper exit and this raw teardown precede outer forced GC. The full
weak graph—Capture/completion/primitive/operations/source and sample/unavailable
markers—remains alive, with the original capture charge retained. This is actual
cross-helper retention evidence, not merely strong pending-phase snapshots;
it does not prove cleanup of unknown ownership or all user observer placements.

## Regression and independently checked audit binding

`focused-debug-unavailable-observer01` /
`focused-release-unavailable-observer01` each actually pass152: exact accepted
prior151 qualified identities plus this Fact, removed[],0 skipped/nonterminal.
All four stages bind503 selected source inputs and complete138-file runtime
inventories before/after. Archive-first recorder remains
`de801da994b16693b356a50e59af1880bfb267d81c0761ba3b7845f177f4f957`;
separately recorded tar-create/extract raw exits0. Historical recorder remains
`13e28b6d141b2bc579a62eb6c02eba6ae00bee18a900f122f3bc7b2ef963d565`;
this does not revise prior exclusions or diagnose the old pipe-tar mechanism.
Standards/spec reviews report0 finite-scope findings, read-only and untested.
Root `unavailable-observer-root-saved01` /
`unavailable-observer-root-current01` raw audit exits0, passed=true/violations=[],
and reports byte-identical to corresponding worker reports. Documentation
independently verifies four stage/four worker-root receipts, SHA256s and cmp;
it does not execute an audit/replay or recursively audit earlier accepted slices.

- Auditor: `c3bb6342e918369ac32ee53559b15bf4a8d156c42704df0f6c083ad09625753e`.
- Replay: `d2896264310aa98f90ceab37b5cada5476c82bd8373631b0a3f178f90efc37b4`.
- Saved report: `f2c83bbb0ff245e6ea264db343287e6ba931f7d5765ad615929fe1f8b9394d31`.
- Selected-current report: `5627ba9cbcdc1f70ddde09f7fd269ffbaecbffd1f7ae25b42c85161b02e966c8`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/unavailable-observer-replay.sh unavailable-observer-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at its freeze, not whole-tree/tools or a pristine commit.
Subsequent source edits make it historical; frozen saved evidence is unchanged.
Last project D/R remains390, not rerun; no new full/quality/security/native/CI
gate. Existing delegate0/global Capture-admission limits remain unchanged.
This step edits only this evidence file and runs no build/test/replay/new agents;
task4/task5/MCC/v1 and Goal remain open.
