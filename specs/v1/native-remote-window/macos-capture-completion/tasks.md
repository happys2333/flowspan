# MCC implementation plan

This finite prerequisite is adopted alongside the approved v1 baseline. The
first Start ownership tracer has executed; full MCC acceptance remains open.
Ordinary scoped execution needs no additional user product-choice gate.

- [x] 1. Execute the first same-Capture Start after-effect ownership RED.
  - Add only the minimal shared actual one-argument adapter accepting root/Block
    effects. Keep `CreateCompletion` doing Prepare/AcquireCopy before returning;
    Capture still assigns `startBlock` after return, preserving the ownership bug.
    Do not use the two-argument invoke or a fake completion state machine as proof.
  - Control actual physical capture copy, then throw the original nested fatal.
    Drive actual Stop/Dispose, drop test strong references, then inspect original
    shell/actual primitive-owner weak graph and retained-owner accounting through
    GC. Preserve the actual premature cleanup/graph RED and source/test manifest.
  - Add only this tracer first. Label compiler/setup/direct-GREEN results honestly.
  - _Requirements: MCC1-MCC2, MCC6, MCC8_
- [x] 2. Close the staged one-argument Start ownership path with minimal GREEN.
  - Reuse the shared primitive state/helpers with `InvokeOne` and `v16@?0@8`;
    change only inert prepare -> exact-Capture attachment -> root/copy acquisition.
  - Rerun unchanged first-tracer bytes and actual Stop/Dispose. Check healthy
    single Start as a separate direct-GREEN contract; preserve old legacy
    zero/two-argument behavior.
  - _Requirements: MCC1-MCC2, MCC4, MCC6, MCC8_
- [x] 3. Extend the same fixed-owner contract to Stop and uncertain cleanup.
  - One behavior at a time: Stop copy after-effect; root allocation unknown or
    invalid return; caller release after-effect; late last-copy root-free fault.
  - Give confirmed independent owners one attempt; no guessed retry/finalizer.
    Preserve original primary/fatal identity and full shell through GC/reentry.
  - _Requirements: MCC2, MCC4-MCC6_
- [x] 4. Close the existing factory/invocation pool and observer obligations.
  - Local finite implementation/test coverage only. Final frozen project/
    solution, quality/security, native and exact-SHA hosted acceptance remains
    open in tasks6–8; this check does not close complete MCC.
  - In finite tracers, verify all four fixed scopes: constructor, Start invocation,
    Stop invocation and `RemoveNativeOutput`. Include output-removal body/pop
    faults, attempted/confirmed facts, original-thread pop and independent cleanup.
  - Unsettled constructor pool scope must prohibit root/count return. Contain pop
    and select original primary/fatal before exact failed-shell handoff, then
    attempt independent cleanup/rollback. Do not rely only on the replaceable
    handoff slot; constructor finally cannot replace the original failure.
  - Reject the scoped native body when pool push returns no valid token or
    throws with unknown acquisition; record that attempted/unconfirmed debt,
    perform no guessed pop and do not retry the scope.
  - Start/Stop native invoke after-effect
    uncertainty must not synthesize a callback, successful stop or release.
  - Separately trace a confirmed acquired Stop caller whose native handoff is
    confirmed unissued: zero-token refusal alone does not establish its
    independent caller cleanup or fabricate native callback/exit facts.
  - Verify fallible action/failure/completed notifications cannot skip remaining
    independent cleanup or hide an earlier fatal. Include factory-slot replacement.
    Include the actual Capture `SourceUnavailable` user observer after an
    earlier fatal, not only a primitive wrapper fault. Independently exercise
    Stop action, failure-observer and completed notification faults through
    `ConfirmStopAsync`; Start's shared-wrapper evidence does not prove those
    distinct production Stop callbacks. Those finite regions now have separate
    accepted checkpoints through155. The coverage audit's one remaining
    constructor path—healthy body/successful AddOutput followed by the first
    pool-pop fatal—now has its separate157 checkpoint: actual async rollback,
    slot replacement and outer weak GC preserve unknown pool debt.
  - _Requirements: MCC2, MCC4-MCC6_
- [x] 5. Close finite same-Capture lifetime, handoff and reentry implementation.
  - Finite implementation/test coverage and final source review only. Task7's
    aggregate, task6's exact-source native and task8's new hosted gates remain
    mandatory before complete MCC acceptance; this check closes none of them.
  - Trace pre-release resource-use closure/join for action, failure and completed
    regions, separately from caller +1 release/extra native copy and post-release
    `ManagedInvocationDrain` (which requires native root retirement). A callback
    may remain active after its completed notification; first idle is not terminal
    drain. Actual primitive and Capture orchestration stay shared.
  - Verify duplicate/overlapping/late result closure, no Start resurrection,
    acquisition/Stop/Dispose race, and direct/active-descendant self-join rejection.
  - Prove independently confirmed Stop/output/sample cleanup can release native
    owners without a stream/Block retirement cycle; shell accounting cannot return
    until full completion/native/managed cleanup is confirmed. Verify the outer
    boundary's `IsDrained` fallback cannot mask completion debt or authorize
    shell/source-binding return, not only an internal fake owner's flags.
  - Reread staged failure after observing confirmed terminal native/managed
    lifetime before shell-root return; an earlier failure sample followed by
    later lifetime checks is not one synchronized no-debt proof.
  - _Requirements: MCC3-MCC4, MCC6-MCC7_
  - First resource-use tracer (finite RED→GREEN accepted; task5 remains open):
    `FirstStartActionStillActiveDoesNotReleaseCallerAfterDuplicateCompletion`.
    Native Start handoff
    returns holding only the actual pointer; a real one-argument error callback
    pauses in SourceUnavailable after result settlement. A second real callback
    returns and publishes completed while the first action is still active.
    Synchronous Dispose must not release the Start caller+1 on that first-exit
    latch; it may reject unconfirmed cleanup while the admitted action is active.
    Use barriers and join the real invocation thread before teardown,
    not sleeps or terminal ManagedInvocationDrain as a pre-release join.
    `active-start-action-red01` actually fails the caller-release assertion:
    expected0, actual1, after both real ABI threads, teardown and outer GC.
    Locked restore/build succeed with zero warnings/errors; the single compiled
    Fact fails at outer line23. Its immutable evidence is under
    `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.
    This RED establishes the defect, not resource-use closure acceptance.
    Its subsequent [finite repair checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-active-action-progress.md)
    passes unchanged-test-byte final GREEN02 single D/R1 and focused D/R139,
    both review axes and actual root replay. Only the first active-action caller
    release behavior is accepted; other regions, reentry and races remain open.
- [x] 6. Execute new one-argument native lifetime and healthy Capture gates.
  - Limited local native coverage only: final-source Block and task-owned healthy
    Capture D/R are recorded in the [final local progress](../../../../docs/evidence/2026-10-06-capture-completion-final-local-progress.md).
    No native-fault, all-OS, production-sharing or complete MCC acceptance.
  - Separate opt-in native Block mode: real one-argument ABI, extra heap retain,
    caller release, actual last-copy retirement and terminal managed drain.
    Preserve old non-native defaults/modes; strict exit/stdout/stderr/watchdog
    evidence must distinguish skip/failure and final ABI return observations.
  - New exact-source task-owned healthy SCStream Capture Debug/Release executions;
    retain sample/frame distinctions and limitations. Do not claim native faults.
  - _Requirements: MCC1, MCC3, MCC7-MCC8_
- [x] 7. Freeze and audit the finite slice, then update formal traceability.
  - Limited local scope: fresh candidate03 six stages, D/R3007,20 independent
    164-case repeats, default security scan, actual worker/root saved/current
    replays/cmp/receipts pass in [final local progress](../../../../docs/evidence/2026-10-06-capture-completion-final-local-progress.md).
    Preserved candidate01/02 failures are not relabeled; task8/full MCC stay open.
  - Focused/project/full solution D/R; locked restore/format/analyzers/security;
    complete qualified identities and before/after source/runtime inventories.
  - Fault/concurrency/reentry repeat contracts; independent Standards/Spec review;
    root replay of immutable evidence. Keep failures/superseded candidates.
  - Update MCC/MSC task links, ADR/evidence/threat model only for proved scope;
    leave max-16 Capture admission/nonzero delegate/global runtime explicitly open.
  - _Requirements: MCC1-MCC8_
- [ ] 8. Verify the new exact-SHA all-OS hosted checkpoint.
  - Commit/push implementation branch, never main. Download complete new CI/
    CodeQL/qualified inventories and native/security evidence, independently
    audit bindings/digests and root replay. Old or pending MEP CI cannot substitute.
  - No GitHub issue/PR/comment/review/discussion publication without exact-text
    per-target approval. Close only MCC after its gates, not MSC/v1/active Goal.
  - _Requirements: MCC8_

## Start checkpoint progress

Raw stages under `/tmp/flowspan-capture-completion-start-20261005/` preserve
`red-setup01` compiler failure separately from actual compiled `red01`/`red02`.
The final first-tracer `red02` loses the original charged weak graph after actual
Stop/Dispose and fixture-root removal: expected `(1,T,T,T,T,T)`, actual
`(0,F,F,F,F,F)`. `green01` passes the same Fact with byte-identical complete test
file SHA256 `a002a99c0b03d94cbd02b78eca16a99357a05e99098bd58c39ead5bd3ee9e163`.
The minimal repair moves acquisition from the shared factory to after exact
Capture attachment. Necessary healthy Stop acquisition plumbing is not Stop
fault/lifetime acceptance. `healthy01` separately passes the actual typed
one-argument Start/Stop contract as direct GREEN.

Expanded final fixtures add only those two Facts and isolate the existing partial
Capture class from parallel global retained-owner observations. Focused02 D/R
each pass110 and project01 D/R each pass379. Both static review axes have0
concrete findings. Quality02 locked serial restore/format passes, preserving the
failed quality01 candidate. Worker and actual root saved/current replays each
exit0 with no violations and byte-identical corresponding reports. Tasks1–2
close at this finite [Start checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-start-progress.md).
Task3 begins with the separate Stop-copy after-effect tracer. This is
controlled-effects portable evidence only; no full
solution, new one-argument native, hosted, MCC/MSC, production or v1 acceptance.

## First Stop behavior within task3

The [Stop-copy checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-stop-progress.md)
preserves original shell/Stop primitive graph and fatal as direct GREEN, then
actually reproduces missed independent Start caller cleanup as RED→GREEN with
unchanged test bytes. Only a normally returned Start handoff plus observed
notification permits that single caller+1 cleanup; it confirms no Stop/drain or
complete lifetime and releases no dependent native owner. Final focused D/R111,
project D/R380 (original377+Start2+Stop1), quality, both review axes and actual
root saved/current replays pass. This one behavior is accepted; task3 remains
in progress for root/invalid-return/release/late-free uncertainty, and tasks4–8
remain open. Current/live equality is superseded by any next implementation.

## Late last-reference root-free behavior within task3

The [late root-free checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-root-free-progress.md)
executes the actual last heap-reference helper fault before repeated Dispose,
fixture teardown and outer GC. Its real ownership/charge RED passes afterward
with unchanged test bytes. Only API lifetime confirmation is added after caller
and independent owner releases: freshly observe both failure slots and require
native retirement plus terminal managed drain before root/count return. No wait,
retry, pool/resource-use/race or outer drain expansion. Focused D/R112 and project
D/R381, both review axes and actual root saved/current replay pass. This behavior
is accepted locally; root allocation/invalid-return/caller-release cases and
tasks4–8 remain open. Later test edits supersede selected-current equality.

## First root-allocation case within task3

The [allocation checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-allocation-progress.md)
adds only the actual Start root-allocation-after-effect Fact/helper. The same
Capture/one-argument primitive preserves attempted/unconfirmed acquisition,
original fatal, complete weak graph and charge; no guessed free/copy/release or
retry, while independent native owners clean up once. Single qualified Debug/
Release executions and actual root saved/current replay pass. This is direct
GREEN with unchanged production code, not a new behavioral RED or project gate.
Stop allocation, invalid returns and caller-release cases still remain open.

## Completed finite uncertainty coverage within task3

The [eight-row uncertainty matrix](../../../../docs/evidence/2026-10-05-capture-completion-uncertainty-matrix.md)
now executes those remaining cases sequentially as direct GREEN, with no new
production/runtime/support change. Final focused D/R120 and project D/R389
preserve the exact381 baseline plus8 identities. Separate locked solution
restore/verify-format, both review axes and actual root saved/current replay
pass. Combined with the accepted Stop-copy and late root-free checkpoints,
task3 closes locally. Task4 starts with one constructor-body-fatal/pool-pop
after-effect tracer; its implementation and acceptance remain open. This is
not complete MCC, native fault containment or new hosted/v1 proof. Subsequent
edits supersede this checkpoint's selected-current equality.

## First constructor pool behavior within task4

The [constructor pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-constructor-pool-progress.md)
has an actual final weak-graph/charge/fatal RED after body-fatal, pool-pop
after-effect and genuine failed-slot replacement. Unchanged test bytes pass
after the fixed constructor facts and pop/selection/handoff/cleanup ordering
repair. Focused D/R121/project D/R390, quality, both review axes and actual root
saved/current replay pass. Only this behavior is accepted; task4 remains in
progress for other constructor outcomes, Start/Stop/removal scopes and observers.
Resource-use/race/outer-drain and later complete MCC gates remain open. The next
tracer is Start invocation pool-pop debt; no full native or hosted result is claimed.

The [Start pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-start-pool-progress.md)
now accepts that single behavior after actual RED→GREEN with unchanged test
bytes. Single D/R1 and focused D/R122, both review axes and actual root
saved/current replay pass. Known native/primitive cleanup cannot return the
shell while its Start pool pop remains unconfirmed. The compiler-only red01
candidate is preserved and excluded. Stop/removal scopes, other pool outcomes
and observers remain task4 work; project/quality/solution gates are deferred to
the final aggregate checkpoint, not assumed from this focused run.

The [Stop pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-stop-pool-progress.md)
likewise accepts only healthy-body pop-after-effect RED→GREEN: single D/R1,
focused D/R123, both review axes and actual root saved/current replay pass.
Independent native/primitive cleanup completes without retry, but unconfirmed
Stop pool debt prevents shell/root accounting return. Output-removal scope,
combined body/pop, zero/unknown acquisition and observer behaviors remain open
in task4. The last actual project gate remains D/R390; final aggregate gates
are still pending.

The [output-removal pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-remove-pool-progress.md)
then preserves the actual successful BOOL/error removal despite pool-pop failure,
allowing the queue barrier and independent cleanup to continue. Canonical
unchanged-test-byte RED→GREEN, single D/R1, focused D/R124, both reviews and
actual root saved/current replay pass. Unknown removal-pool debt still blocks
shell/root accounting return; combined body/pop, invalid/unknown push and
observer/native-invoke outcomes remain task4 work, followed by task5 joins/races.

The [Start body/pop checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-start-body-pool-progress.md)
then reproduces original body fatal A being replaced by later pop fatal B.
The minimal shared body catch records A before unwind; unchanged test bytes
pass single D/R1, focused D/R125, both reviews and actual root replay.
Start/Stop/two-Dispose retain A while known cleanup runs and pool debt retains
the shell. Stop's corresponding injection is not yet accepted merely because
the method is shared; removal body/pop and other task4 outcomes remain open.

The [Stop body/pop checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-stop-body-pool-progress.md)
now executes that corresponding injection separately as direct GREEN with no
production changes: single D/R1, focused D/R126, both reviews and actual root
saved/current replay pass. Exact125 identities are retained plus its1 Fact.
This validates the shared fatal-order repair for Stop, not a new RED or complete
task4; unknown-removal combinations and acquisition/observer outcomes stay open.

The [unknown-removal body/pop checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-remove-body-pool-progress.md)
then has actual unchanged-test-byte RED→GREEN: preserve body A before pop B and
give the normally returned/completed staged Stop caller its independent single
release. Unknown removal authorizes no barrier/dependent-native/source cleanup,
physical drain or shell return. Single D/R1, focused D/R127, both reviews and
actual root replay pass; resource-use join is not inferred from this finite
notification proof. Zero/unknown push and observer/invoke outcomes remain task4.

The [constructor zero-pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-constructor-zero-pool-progress.md)
rejects the scoped native body after an actual zero-token return. Its unchanged-
test-byte RED→GREEN, single D/R1, focused D/R128, both reviews and actual root
saved/current replay pass. No guessed pop/retry occurs; known source cleanup
still runs once, while unknown pool debt retains the original shell beyond
genuine failed-slot replacement. The other scope outcomes and observers remain
open. The last actual project result stays D/R390; aggregate gates are pending.

The [Start zero-pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-start-zero-pool-progress.md)
then rejects selector/native invocation before issued state on a zero token.
Actual unchanged-test-byte RED→GREEN, single D/R1, focused D/R129, both reviews
and actual root saved/current replay pass. Known cleanup runs once; local
unissued result/exit settlement is explicitly not a native callback. Unknown
pool debt retains the full shell graph/accounting. The shared guard does not
accept Stop-zero; remaining acquisition/observer/resource-use gates stay open.

The [Stop zero-pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-stop-zero-pool-progress.md)
separately passes direct GREEN with unchanged production: single D/R1, focused
D/R130, both reviews and actual root saved/current replay. No Stop selector,
native callback/result/exit or guessed pop is fabricated. It accepts refusal
and retained debt only: the confirmed Start caller cleans up once, while the
acquired-but-unissued Stop caller remains an explicit later cleanup obligation.
No barrier/dependent cleanup, complete task4 or new aggregate gate is accepted.

The [output-removal zero-pool checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-remove-zero-pool-progress.md)
then passes actual unchanged-test-byte RED→GREEN: single D/R1, focused D/R131,
both reviews and actual root replay. A zero token no longer permits RemoveOutput
or physical drain. Both confirmed callers clean up independently once, while
barrier/dependent native/source cleanup and shell/count return remain forbidden.
Unknown push outcomes, unissued Stop caller cleanup and observers remain task4
work; resource-use/races and final aggregate/native/hosted gates remain open.

The [four-scope unknown-push matrix](../../../../docs/evidence/2026-10-05-capture-completion-unknown-push-matrix.md)
then executes each Fact's single D/R before adding the next, all direct GREEN
with unchanged production; final focused D/R135 preserves all131 prior identities
plus4. Both reviews and actual root saved/current replay pass. Nonzero simulated
effects followed by a throw provide no returned token, so no body/pop/retry is
authorized. Original fatal and full graph/charge remain; only independently
known cleanup runs. Unissued Stop caller cleanup and observers remain task4
work, followed by resource-use/races and final aggregate/native/hosted gates.

The [confirmed-unissued Stop caller checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-stop-unissued-caller-progress.md)
then repairs that finite missing cleanup with actual unchanged-test-byte
RED→GREEN: single D/R1, focused D/R136, both reviews and actual root replay.
Only normal acquisition plus the unique setup's terminal unissued fact permits
the independent caller release. Unknown acquisition and issued Stop do not gain
new eligibility; no Stop callback/result/exit/settled or dependent cleanup/root
return is fabricated. Native-invoke/observer cases, resource-use/races and final
aggregate/native/hosted gates remain open; task4 is not complete.

The [late-callback pair checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-late-callback-progress.md)
then executes Start and Stop sequentially as direct GREEN without production
changes: each single D/R1, final focused D/R138 (exact136+2), both review axes
and actual root saved/current replay pass. Actual extra heap retain followed by
an invocation throw leaves callback exit/drain unconfirmed; a real typed late
callback and ABI thread Join then permit independently confirmed caller and
stream-held copy cleanup before retirement/accounting return. No handoff return
or successful replacement of Start's faulted local result is invented. This
does not accept pending cross-boundary GC graph proof or resource-use join.
Task4 observers remain open; task5 now starts with the single overlapping-action
tracer above. Project D/R390 remains the last actual project result, and final
aggregate/native/new exact-SHA hosted gates remain open.

The [first active-action checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-active-action-progress.md)
then has actual unchanged-test-byte RED→GREEN: while first Start action remains
active after duplicate completed notification, caller releases change from1
to0. One fixed adapter owner closes and joins resource use independently of
native retirement; an incomplete join records no release attempt. Result/
confirmed-unissued qualification avoids suppressing a needed result, and final
lifetime precedes fresh failure observation. Final single D/R1, focused D/R139
(exact138+1), both reviews and actual root saved/current replay pass. GREEN01
is retained as superseded. Only this first behavior closes; task4 observers,
other resource regions, duplicates/races/reentry/outer drain and final gates
remain open. Project D/R390 is still the last actual project result.

## Finite observer-fault matrix within task4

The [three-region observer-fault checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-observer-fault-progress.md)
now passes final single Debug/Release1 for action, failure observer and completed
notification, plus focused D/R142 (exact139+3). The action case has two actual
unchanged-test-byte REDs: initial Start hides a recorded fatal behind its settled
successful result, then known terminal ownership remains charged. Two minimal
API repairs preserve the actual result/handoff facts and first fatal while
returning fully confirmed ownership. Failure/completed cases are direct GREEN,
not invented REDs. Both review axes and actual root saved/current replay pass.
This accepts only those terminal observer-fault behaviors; task4/task5 stay open.
Repeated Start caching, active failure/completed joins, ancestry, races and outer
drain still need independent proof. Project D/R390 remains the last actual
project gate; no new aggregate/native/hosted or complete MCC proof is inferred.

The [repeated-Start checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-repeated-start-progress.md)
now has actual same-byte RED→GREEN: the cached branch initially returns true
without the already recorded fatal; one `ThrowPendingFatal` before its return
reports the original failure without rewriting the settled result/handoff facts
or issuing another native Start. Single D/R1 and focused D/R143 (exact142+1),
both reviews and actual root saved/current replay/cmp/receipts pass. Only this
MCC4 cache behavior closes; task4/task5 and final gates remain open.

The [active-observer pair](../../../../docs/evidence/2026-10-06-capture-completion-active-observer-progress.md)
now passes sequential completed/failure single D/R1 as direct GREEN with no
production change. Real guarded notifications remain active behind barriers;
external Dispose releases0 callers and records no release attempt even after
completed publication or a real duplicate callback return. Real ABI threads
join before independent caller/stream-copy cleanup, terminal lifetime and outer
GC. Focused D/R145 (exact143+2), both reviews and actual root saved/current
replay/cmp/receipts pass. Only these two pending-region observations close;
task5 stays open for async join, ancestry, races and outer boundary proof.

The [completed ancestry pair](../../../../docs/evidence/2026-10-06-capture-completion-completed-ancestry-progress.md)
now passes sequential direct Dispose then inherited-EC async descendant Stop
single D/R1 as direct GREEN with unchanged production. The real guarded
completed parent remains active while each same-Capture cleanup call rejects;
no resource closure, release attempt or native Stop is fabricated. Actual ABI
and child task joins precede external known cleanup/lifetime/outer GC. Focused
D/R147 (exact145+2), both reviews and actual root saved/current replay/cmp/
receipts pass. Only these two self-join entry/region combinations close.

The [async resource-use join checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-async-resource-join-progress.md)
now accepts `StopAndDrainWaitsForActiveCompletedResourceUse`: actual Stop,
removal and sample-barrier progress precede the premature-success RED. The
same-test-byte repair selects both owners' closed resource-use joins before any
await, joins outside gates and freshly reads the return state. Single D/R1,
focused D/R148 (exact147+1), both code review axes and actual root saved/current
replay/cmp/receipts pass. No release or terminal lifetime wait was added; this
closes only that active completed region's asynchronous join, not full task5.

The [outer binding checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-binding-progress.md)
now accepts `PhysicalDrainWithHeldStopCopyDoesNotReturnCatalogBinding` as direct
GREEN: the same actual Catalog NativeSource enters the real Boundary/Capture;
physical `IsDrained=true` and cached drain=false trigger the real fallback, but
pending Stop-copy lifetime makes native Dispose fail before binding return.
Catalog retirement retains entry/source/pool ownership and refuses replacement
enumeration. Single D/R1, focused D/R149 (exact148+1), both code review axes and
actual root replay/cmp/receipts pass. Earlier compiler/recording/test-contract
failures remain classified, not product RED. Thin default-off setup seams add
no cleanup-state-machine repair; fixture teardown does not restore the outer
owner or return its binding/charge. Healthy/late-fatal/GC/recovery proof is open.

The [pending healthy Start checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-pending-start-progress.md)
now accepts `PendingSuccessfulStartDisposeLeavesAdmissionOpenForLateCallback`
as direct GREEN with unchanged production: single D/R1, focused D/R150
(exact149+1), both code review axes and actual root replay/cmp/receipts pass.
With native handoff already returned and the real ABI thread held before
invocation, external Dispose leaves result/admission pending and attempts no
release. Finally releases and joins that thread before original Start success,
normal external cleanup and outer weak GC. This is neither pending-GC nor
active native-handoff borrowing proof; task4/task5 remain open.

The [native handoff borrow checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-native-borrow-progress.md)
now accepts `ReturnedStartCallbackDoesNotReleaseBorrowedOwnersBeforeNativeHandoffExit`.
The precise RED07 fails Start-specific caller-attempt/confirmation facts:
expected four false, actual four true. Its unchanged full test bytes then pass
single D/R1, focused D/R151 (exact150+1), both code review axes and actual root
replay/cmp/receipts. Fixed Start/Stop synchronous finally-exited facts guard
dependent native/source and corresponding caller release; exceptional exit is
separate from normal handoff return and unknown pool effects remain charged.
The real callback ABI has already returned in the held phase; physical Stop/
removal/barrier can progress without authorizing borrowed-owner release. Finally
releases and joins the original Start/Dispose threads before terminal cleanup
and outer weak GC. Independent Stop caller0/1 is permitted, not banned by an
aggregate release assertion. Original pool-thread/lifetime boundaries persist.
Superseded contract and tar-only failures are retained; a separately pinned
archive-first recorder keeps the historical recorder intact. This accepts only
this held Start frame, not acquisition races or full task5/MCC.

The [actual SourceUnavailable observer checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-unavailable-observer-progress.md)
now accepts `SourceUnavailableObserverFatalPreservesEarlierCopyFatalAndUnknownOwnership`
as direct GREEN with unchanged production: single D/R1, focused D/R152
(exact151+1), both code review axes and actual root replay/cmp/receipts pass.
The actual Capture user observer throws fatal B after copy-after-effect fatal A;
the four external operations and primitive FirstFailure preserve A. Known
independent cleanup proceeds once, while unconfirmed copy ownership retains
pointer0, pending lifetime and its original weak graph/charge through outer GC.
No callback ABI or native Start/Stop is executed on this acquisition-failure
path; raw fixture teardown is not production retirement. Task4/task5 stay open.

The [Stop completed checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-stop-completed-progress.md)
now accepts `StopCompletedNotificationFatalPreservesSuccessfulStopAndCleansKnownOwnership`
as direct GREEN with unchanged production: single D/R1, focused D/R153
(exact152+1), both code review axes and actual root replay/cmp/receipts pass.
After actual Stop success and completed exit publication, a contained first
fatal remains reportable without rewriting Stop result. ConfirmStopAsync still
performs removal/barrier; all known ownership and native/managed lifetime facts
confirm cleanup and shell-root return before outer weak GC. The compiler-only
CA1861 candidate is preserved, not a product RED. Task4 remains open because
Stop action and Stop failure-wrapper faults require their own actual evidence.

The [Stop action checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-stop-action-progress.md)
now accepts `StopActionFatalPreservesSuccessfulStopAndCleansKnownOwnership`
as direct GREEN with unchanged production: single D/R1, focused D/R154
(exact153+1), both code review axes and actual root replay/cmp/receipts pass.
Actual Stop action publishes success then throws A; the real failure/completed
path and ConfirmStopAsync preserve success and diagnosis while known ownership
cleans up once, lifetimes confirm and outer weak GC collects the full graph.
Stop completed fault is0; this is a separate actual action entry, not inherited
Start evidence. Task4 still needs its last fixed Stop failure-wrapper region.

The [Stop failure-observer checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-stop-failure-observer-progress.md)
now accepts `StopFailureObserverFatalPreservesEarlierActionFatalAndCleansKnownOwnership`
as direct GREEN with unchanged production: single D/R1, focused D/R155
(exact154+1), both code review axes and actual root saved/current replay/cmp/
receipts pass. After action fatal A reaches the actual Stop failure callback,
the guarded observer throws distinct B; primitive containment preserves A and
successful Stop while removal/barrier, known single cleanup, terminal lifetimes
and outer weak collection still complete. The root task4 finite coverage gap
audit identifies one outstanding constructor path: normal body/successful
publication followed by the first pool-pop fatal, which takes actual async
rollback rather than unpublished synchronous cleanup. It needs independent
rollback/barrier/known cleanup, retained pool debt, exact failed-slot replacement
and outer weak GC evidence. Task4 and full MCC gates remain open.

The [Start copy-in-flight checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-start-copy-inflight-progress.md)
now accepts `StartCopyInFlightStopAndDisposePreserveExactOwnerAndOrderShutdown`
as direct GREEN with unchanged production: single D/R1, focused D/R156
(exact155+1), both review axes and actual root saved/current replay/cmp/receipts.
Only the first actual physical copy is held after its helper/effect but before
AcquireCopy confirmation. External Stop waits and Dispose rejects unconfirmed
cleanup without closing pending result/resource admission or issuing guessed
native work. Finally release and join the original Start thread, then its real
success precedes ordered Stop/cleanup/weak collection. Delivery remains closed;
the earlier admitted Start is not a newly resurrected request.
Other ancestry/stale descendants, closed-admission late callbacks, terminal
failure publication and healthy/late-fatal/GC/recovery composition remain open.

The [published constructor pop checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-published-constructor-pop-progress.md)
now accepts `ConstructorSuccessfulPublicationThenPoolPopFatalRetainsExactShellAfterRollbackAndSlotReplacement`
as direct GREEN with unchanged production: single D/R1, focused D/R157
(exact156+1), both review axes and actual root saved/current replay/cmp/receipts.
Healthy body/successful AddOutput precede only ordinal1 pop after-effect fatal A.
The actual cached rollback Task is joined: normal completion false/no exception,
separate physical drain true. Known owners each clean up once, but unknown
constructor pool debt retains original A and shell charge; repeated Dispose
adds no effects. Real same-thread slot replacement precedes outer weak GC of
the retained original full graph. This closes the independent coverage audit's
sole task4 gap. Task4 is now locally finite-coverage complete, not aggregate,
native/hosted or complete MCC acceptance; tasks5–8 and MSC/v1 remain open.

The [duplicate Stop checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-duplicate-stop-progress.md)
now accepts `DuplicateStopErrorWhileFirstCompletedIsActivePreservesConfirmedStopAndCleanup`:
actual single Debug RED regresses confirmed Stop, followed by unchanged-test-byte
single D/R GREEN and focused D/R158 (exact157+1). The minimal first-result-winner
guard preserves settled success during a real duplicate error ABI while the
first completed region/native handoff remains active. Both reviews and fresh
root saved/current replay/cmp/receipts pass. Finally release and actual drain
join precede single known cleanup, terminal lifetimes and weak collection.
First-error reverse order is not exercised; task5 and final gates remain open.

The [duplicate Start checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-duplicate-start-progress.md)
now accepts `DuplicateStartErrorWhileFirstCompletedIsActivePreservesSuccessAndDelivery`:
actual Debug RED repeats SourceUnavailable and closes delivery after the first
success, then unchanged-test-byte final single D/R GREEN and focused D/R159
(exact158+1) pass. Ordinary native error notification uses the first TCS winner;
real late-callback settlement and independent fatal/failure/catch notification
remain intact. The narrower final helper supersedes a preserved single GREEN
candidate, with no invented old-test runtime failure. Both reviews and actual
root saved/current replay/cmp/receipts pass. No new seam/extra retain is added.
Only this finite duplicate scenario closes; task5 and final gates stay open.

The [closed Start late-ABI checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-closed-start-late-progress.md)
accepts `ClosedStartAdmissionLateCallbackDoesNotTouchReleasedCaptureAndRetiresKnownCopy`
as direct GREEN: final single D/R1, focused D/R160 (exact159+1), both reviews
and actual root saved/current replay/cmp/receipts. Production/support/seams are
unchanged; the passed initial candidate is separate from final test bytes after
readonly Stop closure observations were added. Both admissions are closed and
known native owners released before actual late ABI through a valid extra heap
retain. Only ABI accounting increases; completed does not reenter. This direct
witness plus shared-guard review is not an independent action/failure-hook
experiment. Finally actual Join precedes one known-copy retirement, terminal
facts and normal cleanup/full weak collection without native effects replay.
This is standalone known-pending recovery, not outer pending-GC/recovery.

The [exited completed ancestry checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-exited-ancestry-progress.md)
accepts `ExitedStartCompletedAncestryAllowsInheritedDescendantStopAndDispose`
as direct GREEN with production/support/seams unchanged: single D/R1, focused
D/R161 (exact160+1), both reviews and actual root saved/current replay/cmp/
receipts pass. A normal Task.Run child inherits the real guarded completed
scope while active; actual parent ABI Join and confirmed borrow exit precede
child release. The same inherited scope is then inactive and permits actual
Stop/Dispose/repeat. Full weak collection includes the actual scope; no synthetic
ancestry or strong Task/ExecutionContext/scope is returned. Equivalent action/
failure matrices, fresh terminal failure publication and outer composition
remain unproved; task5 and final gates stay open.

The [fresh terminal/outer contained-fatal checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-fresh-terminal-progress.md)
accepts `FreshCompletedFailureAtTerminalObservationPreservesFatalAndReturnsOuterBinding`:
canonical actual Debug03 binding RED `(0,2)` versus `(1,1)` passes afterward
with unchanged complete test bytes. The fresh-failure core itself is direct
GREEN: a default-off observer pauses after actual completed resource-use exit,
then the forwarding getter joins real fatal publication/ABI return before
actual managed terminal and the production fresh failure read. Native cleanup
can confirm while the original fatal remains reportable; its readonly complete
proof, default-false for other owners, permits independent outer binding cleanup.
The two existing proof publications use OR under their original gate after a
static concurrency finding; no new runtime RED is claimed for that refinement.
Initial passed candidates remain separate from final frozen source. Final single
D/R1, focused D/R162 (exact161+1), both reviews and actual root saved/current
replay/cmp/receipts pass. Source references/capacity return and full outer weak
collection are confirmed in this same contained-fatal window. Task5 and all
final gates remain open for outer healthy and pending-GC/known recovery.

The [outer healthy checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-healthy-progress.md)
accepts that actual Catalog NativeSource/Boundary/Capture lifecycle as direct
GREEN without production/support/seam changes. Single D/R1, focused D/R163
(exact162+1), both reviews and actual root saved/current replay/cmp/receipts pass.
Two compiled test-observation errors remain separately classified, not product
REDs. Binding/source/capacity return, same-pool replacement enumeration and the
20-object weak graph are observed after actual cached cleanup joins. Task5 stays
open; pending-GC/known lifetime recovery has the separate checkpoint below.

The [outer pending-GC checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-pending-gc-progress.md)
accepts `ActualOuterPendingLifetimeSurvivesBoundaryGcAndReturnsBindingAfterKnownRetirement`:
final complete test bytes execute a binding RED `(0,1)` versus `(1,0)` under
accepted163 production, then pass single D/R1, focused D/R164 (exact163+1) and
full macOS project D/R433 after the narrow six-production-file repair. Boundary
collects while the exact pending Capture/Operation/binding graph stays rooted
and charged. Only known fixture retirement plus actual native/managed joins and
fresh complete proof authorize eventual binding/source/capacity return, same-
pool reuse and full weak collection. The original failed Stop Task is unchanged;
one isolated recovery first joins it, never replays native effects. Already-
terminal lifetime Tasks remain eligible to avoid a qualification observation
race; recorded disposal failure conservatively rejects recovery. Both reviews
and actual root saved/current replay/cmp/receipts pass. Setup/compiler/historical
candidates remain excluded from canonical same-test-byte RED→GREEN. Only ordinary
known-held recovery closes, not all contained-diagnostic-plus-pending combinations.
Task5, final source/aggregate/native/hosted MCC gates and MSC/v1/Goal stay open.

### Finite task5 coverage ledger

The read-only accepted158 coverage audit originally identified the paths below.
Separate finite checkpoints now cover them; the checked items do not substitute
for final source review, aggregate regression or native/hosted acceptance.

- [x] Closed-admission late Start ABI: no released-Capture access, while the
  primitive still accounts for the entry and retires its known copy. (MCC3/7)
  Finite checkpoint160 only; no independent action/failure-hook claim.
- [x] Exited completed ancestry: an inherited but inactive descendant may
  Stop/Dispose; do not expand equivalent action/failure matrices. (MCC3/7)
  Finite checkpoint161 only; no synthetic ancestry or native/outer proof.
- [x] Fresh terminal failure publication: control the actual old-failure-read,
  terminal-lifetime observation and new-failure publication ordering. Existing
  stable fatal snapshots do not prove this interleaving. (MCC6/7)
  Publish through the actual primitive before managed terminal drain, and read
  failure freshly after both real terminal observations; do not inject failure
  by reflection or invoke a callback after retirement.
  Checkpoint162 direct-GREEN core, not a new publication repair.
- [x] Actual outer healthy lifecycle: Catalog NativeSource, Boundary and Capture
  return confirmed binding/source/capacity and the full weak graph. (MCC6/7)
  Finite checkpoint163 only; no pending-GC, native SCK or hosted proof.
- [x] Actual outer contained-fatal lifecycle: report the fatal without confusing
  already confirmed native ownership/lifetimes with unknown binding debt.
  Combine with fresh publication only if the same actual tracer controls its
  required window. (MCC5/6/7)
  Checkpoint162 actual binding RED→GREEN in the same publication window;
  readonly complete proof and monotonic publication retain original diagnosis.
- [x] Actual outer pending-GC/known recovery: drop test strong references, retain
  the exact operation graph while lifetime is pending, then release only the
  known extra retain and reobserve confirmed facts for binding/capacity return.
  Include the distinct Stop acquisition/Dispose branch if needed here, rather
  than adding a standalone mirrored matrix. Never retry unknown acquisition,
  release or free, or replay native effects. (MCC4/6/7)
  Checkpoint164 proves the durable Capture graph owns Operation/binding/entry/
  catalog/source/sink but not Boundary. Optional default-false known-lifetime
  qualification requires all caller, independent-owner, pool, borrow and
  resource-use facts, with no unknown primitive effect or recorded disposal
  failure. The existing Operation owns at most one isolated continuation that
  first joins the original failed Stop Task, then real lifetime Tasks after
  stream release, and reobserves actual Dispose/complete proof. No native replay,
  failed-receipt replacement or guessed cleanup. Task149 preserves every held-
  copy premature-return rejection; its finally now joins actual recovery after
  known fixture retirement before raw teardown. This finite ordinary recovery
  does not establish contained-diagnostic-plus-pending or unknown-effect recovery.

Shared three-region join, equivalent active markers, Stop's first-error winner
guard, fixed borrow/pool facts and legacy ABI preservation require final source
review and aggregate regression, not recursively expanded combinations. Static
guard verification is not a claimed new concurrent behavior execution. Task149
remains the held-copy premature-return rejection; checkpoint164 separately proves
pending-GC/ordinary known recovery. Final shared-source review now closes task5
only as finite implementation/test coverage; aggregate acceptance belongs to
task7 and remains pending, separately from task6/8 native/hosted evidence.

## Final source readiness — 2026-10-06

The [readiness record](../../../../docs/evidence/2026-10-06-capture-completion-final-local-readiness.md)
has final Standards0/Spec0 over actual base-to-working-tree changes and new
files. Shared three-region joins, active/inactive ancestry/self-join rejection,
first-result winners, borrow/pool facts, unknown-effect single attempts and old
ABI preservation are reviewed without recursively expanding equivalent matrices.
The sole final Standards probe finding is statically fixed: unconfirmed Join
cannot authorize caller/extra Block release or gate disposal. No runtime RED
or timeout-cleanup proof is claimed. Production and the accepted164 selected503
inputs remain unchanged. Root confirms finite source readiness for a fresh
`final164-01` local campaign; no aggregate/native/hosted outcome is inherited.
Full MCC/MSC/v1/Goal acceptance remains open.

## Native Block subgate within task6

The [one-argument native checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-native-block-progress.md)
actually passes fresh Debug/Release on ordinary-arm64 macOS using the exact
completion owner/primitive, real extra heap retain/last-copy retirement, held
completed observer, terminal managed drain and independent ABI-return join.
Old mode/default/help bytes remain exact. Root native saved/current replay,
final strict gate over those original bundles, both review axes and259 public
CLI fixtures pass; the parser review finding has actual RED→GREEN proof.
All-OS fixture/new macOS native CI steps are wired but not yet hosted. Only
this local Block subgate closes; task6 stays in progress for healthy Capture,
and task8/new exact-SHA hosted plus complete MCC/MSC/v1 remain open.

The [final local progress](../../../../docs/evidence/2026-10-06-capture-completion-final-local-progress.md)
subsequently closes only local task6 with new final-source Block and healthy
Capture D/R. Candidate01 retains Debug build failure and default-scan failure;
its Release3007 result is component evidence, not aggregate acceptance. Document
checksum presentation is repaired without scanner exemptions. Task7 requires
fresh serial candidate02 and actual worker/root replays; task8 stays open.

Fresh candidate03 subsequently passes all six stages, actual D/R3007,
macOS project433,20×164 repeats and default security without exemptions.
Worker/root saved/current replays, corresponding cmp and all four receipts pass;
only finite local task7 closes. Candidate01/02 failures remain preserved. New
committed-SHA hosted task8 and full MCC/MSC/v1/Goal remain open.
