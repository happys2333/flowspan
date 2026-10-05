# Duplicate Stop error during original completed region — 2026-10-06

Status: the finite first-success→duplicate-error slice is accepted locally after
actual RED/GREEN, root saved/current receipt replays and both review axes. Task4 has only root's
local finite implementation/test-coverage completion label; full aggregate/
native/hosted/task5/MCC/MSC/CI/v1 and Goal remain open. Base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a dirty working-tree overlay,
not a pristine commit. Evidence root:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Scope is actual managed Capture/Block orchestration and shared typed callback
ABI with controlled native effects, not native ScreenCaptureKit or cross-platform
acceptance. The Fact is
`DuplicateStopErrorWhileFirstCompletedIsActivePreservesConfirmedStopAndCleanup`
in `tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete frozen RED/Debug GREEN/Release GREEN test bytes have identical SHA256
`4da3767d5709034829774ea0ee3abb7a4b652453fc323653602b0418b0f70d4e`.

## Precise actual RED and first-result-winner repair

`duplicate-stop-debug01` actually builds with0 warnings/0 errors and executes
1 Failed/0 Passed. Frozen line34 compares AfterDuplicate.Stop.Facts:
expected `Tuple (True, False, True, RanToCompletion, True, True)`;
actual `Tuple (True, False, False, RanToCompletion, True, True)`.
The first successful result TCS stays completed/true, but duplicate error
regresses `stopSettled` true to false. This is behavioral RED, not compiler/setup
failure. Earlier assertions establish actual duplicate ABI join and cached drain;
later assertions are not claimed to pass in RED.

The sole RED-to-GREEN production change is in
`src/Flowspan.Platform.MacOS/MacOSRemoteWindowScreenCaptureKitApi.cs`.
RED API.

SHA-256: `05efad915e5644cb0f39714c5ba6df0da8672df9c9369feb78b17a16d47e014e`.

GREEN API.

SHA-256: `d4ae1dfe3325cccbf1e710423b77527a2fe3f843acf18c906b2deb49ee08d7c8`.

Inside the existing short `gate`, the Stop action now returns immediately unless
`stopCompletion.TrySetResult(error == 0)` wins; only that winner updates
`stopSettled` and `unsafeFailure`. The existing TCS uses
RunContinuationsAsynchronously. No new field/state, external effect, observer or
wait is introduced under the gate; fatal diagnosis remains independent.

`duplicate-stop-green-debug01` / `duplicate-stop-green-release01` actually pass1
with the same complete test bytes. Frozen API diff, source hashes, raw stdout
and qualified TRX were read directly. First-error→later-success is statically
guarded by this code but is not executed or claimed by this tracer.

## Held original frame and actual second Stop ABI

Healthy Start succeeds. The actual cached StopAndDrain begins real Stop; a
default-null StopCompletedHook runs after actual completed notification and
holds only first ordinal1 using a real barrier. Confirmed Stop caller Pointer
is then used by a second dedicated thread for the actual one-argument ABI with
error1. That thread actually joins before the first completed hook is released.
There is no extra copy, synthetic TCS, field reset or other injected fault.

GREEN preserves Stop result/settled=true and unsafe=false, the identical result
task/owner, original resource-use `(closed=false,active1,join=false)` and Stop
native borrow-exit=false. Runtime ABI counts move2 attempts/1 return to3/2;
selector/native Start/Stop effects do not change. Roots2/copies2 remain live,
release/root-free/removal/barrier/object/queue/source counts0, pool3 push/2 pop,
charge `before+1`. FirstFailure and SourceUnavailable remain absent; original
drain stays incomplete and reference-identical. These are held helper snapshots,
not pending cross-GC retention proof or authority for cleanup/resurrection.

## Actual drain join and terminal cleanup

Finally opens the first hook and joins the duplicate thread if required. The
original actual drain task is awaited, not replaced by another ABI return,
result-only TCS or a fictitious ThreadPool Thread.Join. Its completion confirms
all3 actual ABI returns, normal Stop handoff return/borrow finally-exit, drain
true, removal/sample barrier1 and resource closure/active0/join completion.
All four synchronous pool scopes pop with their matching token/creator thread.

Dispose and repeat succeed:2 caller releases/2 root frees,0 live roots/blocks;
both primitive native-retirement and terminal managed-drain facts confirm.
Stream/output/configuration release in order, queue/source release once and
charge returns to `before`; repeat adds no effects. Raw teardown is
`(roots0,blocks0,releases2,rootFrees2,invokes3,returns3)`, not substitute production
cleanup. Raw teardown/NoInlining helper exit then outer forced GC leave weak
Capture/both owners/primitives/operations/source/runtime/callback markers dead.

## Regression and independently checked replay binding

`focused-debug-duplicate-stop01` / `focused-release-duplicate-stop01` each actually
pass158: exact accepted157 qualified identities plus this Fact, removed[],
0 skipped/nonterminal. Five stages (one RED/four GREEN) bind503 selected source
inputs and complete138-file runtime inventories before/after. Archive-first
recorder stays `de801da994b16693b356a50e59af1880bfb267d81c0761ba3b7845f177f4f957`,
independent tar-create/extract exits0. Historical recorder
`13e28b6d141b2bc579a62eb6c02eba6ae00bee18a900f122f3bc7b2ef963d565`
and exclusions stay unchanged; old pipe-tar cause remains unestablished.
Standards:0 hard/0 judgment. Spec:0 missing/out-of-scope/incorrect implementation.
Both reviews are read-only and execute no tests.
Worker/root fresh saved/current wrapper/audit exits0, passed=true/violations=[],
root reports byte-identical to worker reports. Documentation verifies9 receipts
(five stage/two worker/two root), wrapper argv/cwd/frozen script bindings, hashes
and cmp; it reads both scripts but executes no build/test/audit/replay/new agents.

- Auditor: `911009065f3e4b0ac3df805e5a7e77adfdcfa2c76216f8b7bf78cdee953aec57`.
- Replay: `a39400f6cd3a76b366c0be7c8eb3958fc56e92a4bccb2f77aa7859d482173a73`.
- Saved report: `9ed58fd40ee7db62d26c07d54281319ed853c046977949dd0344e40b02147e94`.
- Selected-current report: `894c351c766354ec4050de0e9c6798bf2266a1868cf4a91fbfce4dde380b521a`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/duplicate-stop-replay.sh duplicate-stop-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
inputs at the158 freeze, not whole-tree/tools or a pristine commit; later source
edits make it historical. Saved evidence stays unchanged. Reverse-order duplicate,
closed-admission late callback, terminal publication/outer composition and full
task5 remain outside this slice. Last project D/R remains390, not rerun; no new
full/quality/security/native/CI gate. This step edits only this file, no commit/
push; full MCC/native/hosted/v1/Goal stay open. Delegate0/global admission limits
remain unchanged.
