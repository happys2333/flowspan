# Stop completed-notification fatal after successful Stop — 2026-10-06

Status: the finite
`StopCompletedNotificationFatalPreservesSuccessfulStopAndCleansKnownOwnership`
slice is accepted locally. Full task4/task5/MCC/native/hosted/CI/v1 gates remain
open. Base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a dirty working-
tree overlay, not a pristine commit. Evidence root:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

This exercises actual managed Capture/Block orchestration and the shared typed
callback ABI with controlled native effects. It is not native ScreenCaptureKit,
all observer placements, or a Windows/Linux/macOS acceptance result.

## Direct GREEN and excluded compile attempt

The Fact/helper and default-off Stop-only completed observer seam are in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete frozen Debug/Release test bytes have SHA256
`65997edfb1f09f8fa13e5f80d00203e3a2d6291021268f4bc91fa1eb92f96c60`.
There is no production or shared-support-file change. API SHA256 stays
`05efad915e5644cb0f39714c5ba6df0da8672df9c9369feb78b17a16d47e014e`.
`stop-completed-observer-debug02` / `stop-completed-observer-release01` each
actually build and pass1, direct GREEN; no product RED is fabricated.

`stop-completed-observer-debug01` is an actual CA1861 compile failure with no
test/TRX, excluded from behavioral evidence. The constant string-array pool
observation was replaced by four explicit pool reads; this is test observation
syntax, not a production repair. Its raw diagnostic and receipt remain saved.
Frozen Fact/helper/seam, actual stdout and qualified TRX were read directly.

## Successful Stop remains separate from contained fatal reporting

All other fault seams are off and no extra copy is retained. Actual Start
succeeds. The guarded Stop completed wrapper first calls actual `completed()`
and publishes the real Capture exit, then throws one nested fatal A. The
primitive contains it and invokes the actual Stop failure callback. Source-
unavailable notifications0 and Stop completed throws1 are independently checked.

After fatal A, Stop remains issued, normally returned from its handoff, settled,
result true and exit-success true. Start/Stop ResultFailure remain null; Start
primitive FirstFailure is null, while Stop primitive FirstFailure is the same A.
Actual StopAndDrain and both Dispose calls report reference-identical A without
rewriting the already successful Stop result or inventing unknown ownership.

Actual ConfirmStopAsync removal/sample barrier each occur once and physical
`IsDrained` is true. All four original synchronous constructor/start/stop/remove
pool push/pop attempted/confirmed facts are true. Two actual one-argument ABI
invocations return normally. At the after-drain observation, object/queue/source
cleanup is not yet complete and capture charge remains `before+1`; this physical
Stop progress is not itself the final ownership cleanup claim.

## Known terminal cleanup and weak graph

First Dispose completes known cleanup before reporting A. Start/Stop callers
and native roots are released/retired, both managed lifetimes terminal. Final
totals are2 roots/2 copies,2 releases,2 root frees,0 live roots/blocks, pool4/4.
Stream/output/configuration release in order, queue/source release each occur
once; capture charge returns to `before`. Repeated Dispose reports the same A
and adds no effects or lifetime transitions. Raw teardown confirms
`(roots0,blocks0,releases2,rootFrees2,invokes2,returns2)`.

NoInlining helper exit and raw fixture teardown precede outer forced GC. Weak
Capture/Start and Stop completions/primitives/operations/source/sample and
observer-marker graphs are unreachable. A contained managed fatal can remain
reportable while known cleanup and both terminal joins confirm; it is not an
unknown-ownership debt. This does not prove every action/failure observer path.

## Regression and independently checked audit binding

`focused-debug-stop-completed-observer01` /
`focused-release-stop-completed-observer01` each actually pass153: exact accepted
prior152 qualified identities plus this Fact, removed[],0 skipped/nonterminal.
The four adopted stages bind503 selected source inputs and complete138-file
runtime inventories before/after. Archive-first recorder stays
`de801da994b16693b356a50e59af1880bfb267d81c0761ba3b7845f177f4f957`;
tar-create/extract are independently recorded, raw exits0. Historical recorder
`13e28b6d141b2bc579a62eb6c02eba6ae00bee18a900f122f3bc7b2ef963d565`
and its prior exclusions remain unchanged; no old pipe-tar cause is asserted.
Standards/spec reviews report0 finite-scope findings, read-only and untested.
Root `stop-completed-observer-root-saved01` /
`stop-completed-observer-root-current01` raw audit exits0, passed=true/violations=[],
and reports byte-identical to corresponding worker reports. Documentation
independently verifies four adopted stage/four worker-root/one excluded-compile
receipts, SHA256s and cmp, without executing audit/replay or recursive review.

- Auditor: `bb86a1913788047cd983bc9d3f5ced383ba6b42ecc9884514553998bfbde4fb7`.
- Replay: `d59ef8b4b69897f4df8c00b4b0433af9c3b710cf8b4a974ac9af72a0f7cc7198`.
- Saved report: `7455df54d56fa4d2d5a649822f56d25a4ad84437677f91ac7b90e7a818465238`.
- Selected-current report: `603317bebb1e4a2e834c105e46e509e0b01fab0e0b0ab1f3636efa67be991e64`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/stop-completed-observer-replay.sh stop-completed-observer-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at its freeze, not whole-tree/tools or a pristine commit;
subsequent source edits make it historical, saved evidence unchanged. Task4
still lacks independent actual Stop action/failure-observer evidence. Last
project D/R remains390, not rerun; no new full/quality/security/native/CI gate.
This step edits only this file, runs no build/test/replay/new agents and leaves
task4/task5/MCC/v1 and Goal open; delegate0/global Capture-admission limits stay.
