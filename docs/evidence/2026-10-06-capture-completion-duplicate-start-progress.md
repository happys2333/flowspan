# Duplicate Start error after admitted success — 2026-10-06

Status: the finite
`DuplicateStartErrorWhileFirstCompletedIsActivePreservesSuccessAndDelivery`
slice is accepted locally after actual RED/GREEN, root receipt replay and both
review axes. Task4 retains only root's local finite coverage label; full
task5/MCC/MSC/native/hosted/CI/v1 and Goal remain open. Base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a dirty working-tree overlay,
not a pristine commit. Evidence root:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Scope is actual managed Capture/Block orchestration and shared typed callback
ABI with controlled native effects, not native ScreenCaptureKit or three-platform
acceptance. The Fact/helper/record are in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete frozen RED/candidate/final Debug/Release test bytes share SHA256
`db0b685aa506167307e1891ff2353e5488ecaa65752f326ad880bc6fbd656afa`.

## Actual RED, superseded GREEN and final narrow repair

`duplicate-start-debug01` actually builds with0 warnings/0 errors and executes
1 Failed/0 Passed. Frozen line30 expects `(SourceUnavailable,deliveryClosed,
unavailableNotified)` to be `Tuple (0, 0, 0)`; actual is `Tuple (1, 1, 1)`.
Duplicate error notifies source loss and closes delivery despite an unchanged
original successful Start TCS/task. This is behavioral RED, not compiler/setup
failure. Later assertions are not claimed to pass in RED.

`duplicate-start-green-debug01` actually passes this single tracer with candidate
API SHA256 `07d2b03ab1a98c58ac121c9540562bb7e00ff747b6ff00d88e775e9b6b4a9813`.
Static inspection of the existing late-callback settlement contract led to a
narrower helper before final Release/focused runs. The candidate is preserved
but superseded, not final acceptance; no old late-callback test runtime failure
is claimed or invented.

The only RED-to-final production change is in
`src/Flowspan.Platform.MacOS/MacOSRemoteWindowScreenCaptureKitApi.cs`.
RED API.

SHA-256: `d4ae1dfe3325cccbf1e710423b77527a2fe3f843acf18c906b2deb49ee08d7c8`.

Final API.

SHA-256: `43a0017a5520a2f2e12da5b3125a31839d5372db9226fe2a67f4d87c2b877c51`.

Ordinary native Start error notifies only when `SetStartResult` returns the first
TCS TrySet winner. The bool helper still sets real `startSettled=true` under
`gate`, including a real late callback after a prior issued-handoff task fault;
only result admission's bool is used to suppress duplicate ordinary notification.
Fatal/failure-observer/catch diagnosis and notification remain independent.
Existing asynchronous continuations are preserved; no new field/state/seam,
native effect, observer or wait is added under the gate.

`duplicate-start-green-debug02` / `duplicate-start-green-release01` each actually
pass1 with the same full test bytes. Frozen production diff, source hashes,
stdout and qualified TRX were read directly. This does not execute an inverse-
order or closed-admission/late-callback targeted tracer.

## Held original Start and actual duplicate ABI

Default synchronous Start runs on a dedicated first thread. Existing
StartCompletedHook holds only first local ordinal after actual completed
notification; StartCompletedRelease remains null. A second dedicated thread
invokes confirmed StartBorrowedPointer with error1 through the real one-argument
ABI and actually joins before the first hook opens. No new seam, extra retain,
synthetic result TCS or field reset is used.

Final GREEN preserves the same first true task/original owner, settled=true,
unsafe=false and FirstFailure null. SourceUnavailable/deliveryClosed/
unavailableNotified stay0. Original resource-use is `(false,1,false)` and native
handoff remains active. Runtime1 root/1 copy/no release/free moves from ABI1/0
to2/1; native Start1 and no Stop/removal/barrier, pool2 push/1 pop, charge
`before+1`. Public cached StartAsync returns the same true task with no additional
invocation. These are strongly held snapshots, not pending cross-GC retention
proof; one duplicate ABI return does not release the original native borrow.

## Real resume and known cleanup

Finally opens the first hook and actually joins both dedicated threads. The
returned original Start task stays identical/true; real first ABI and native
handoff return, with borrow finally-exit confirmed. Runtime2/2 is observed before
the actual normal StopAndDrain, without a ThreadPool join or result-only substitute.
Normal Stop/drain, Dispose and repeat succeed:2 roots/2 copies/2 releases/
2 root frees, all3 ABI returns,0 live roots/blocks; all four original-thread pool
pops confirm. Stream/output/configuration release in order, queue/source release
once; both native-retirement/managed-drain lifetimes terminal, charge baseline.
Repeat adds no effects. Raw teardown confirms
`(roots0,blocks0,releases2,rootFrees2,invokes3,returns3)`, not substitute cleanup.
Raw teardown/NoInlining helper exit then forced GC collect the full original
weak Capture/both owners/primitives/operations/source/runtime/callback-marker graph.

## Regression and independently checked replay binding

`focused-debug-duplicate-start01` / `focused-release-duplicate-start01` each
actually pass159: exact accepted158 qualified identities plus this Fact,
removed[],0 skipped/nonterminal. Six recorded stages (RED1/superseded GREEN1/
final GREEN4) bind503 selected inputs and complete138-file runtime inventories
before/after; all builds have0 warnings/0 errors. Archive-first recorder remains
`de801da994b16693b356a50e59af1880bfb267d81c0761ba3b7845f177f4f957`;
independent tar-create/extract exits0. Historical recorder
`13e28b6d141b2bc579a62eb6c02eba6ae00bee18a900f122f3bc7b2ef963d565`
and exclusions stay unchanged; no old pipe-tar cause is established.
Standards:0 hard/0 judgment. Spec:0 finite findings. Both are read-only, untested.
Worker/root fresh saved/current wrapper/audit exits0, passed=true/violations=[],
root reports byte-identical to worker reports. Documentation verifies10 receipts
(six stage/two worker/two root), actual wrapper argv/cwd/frozen script bindings,
SHA256s and cmp; reads auditor/wrapper but executes no build/test/audit/replay.

- Auditor: `d8801a60098391404378f36e9b270f379e6052470ca0c6f79f26baa6fa652a2c`.
- Replay: `80d949a5ca9b6baf9f63f7c2542df68cfb2196c4c4314dea8b6df5bfe196565a`.
- Saved report: `7d7a8d7c6428dc6c6ab8ed9ee3f08a6b7304191f95f6e4874040ca8b9f1917fa`.
- Selected-current report: `2713583b019cbf165146879093c17994753a366f4f3930d40591177b7fdfd3ce`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/duplicate-start-replay.sh duplicate-start-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
inputs at the159 freeze, not whole-tree/tools or a pristine commit; subsequent
source edits make it historical. Saved evidence stays unchanged. Inverse order,
closed late admission, terminal publication/outer composition and full task5
remain outside this slice. Project D/R390 is historical, not rerun; no new full/
quality/security/native/CI gate. Only this file is edited, no new agents/commit/
push; full MCC/native/hosted/v1/Goal open, delegate0/global limits unchanged.
