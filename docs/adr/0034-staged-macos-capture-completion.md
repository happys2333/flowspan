# ADR 0034: Staged same-Capture completion ownership

- Status: accepted for staged implementation; acceptance unverified
- Date: 2026-10-05
- Requirements: MCC1-MCC8; NR8/NR10; MSC2/MSC6/MSC7 prerequisites

## Context

Enumeration already stages its two-argument Block owner before root/copy effects.
Capture still acquires a legacy one-argument Start/Stop completion inside a
factory, then attaches the returned owner. An after-effect failure can leave an
unreturned primitive outside that Capture's cleanup facts. A first callback exit
or sample queue barrier is not complete native-copy/managed lifetime proof.

## Decision

Extend the existing staged Block primitive with the exact one-object-argument
ABI and a thin inert completion owner. Attach it to the original rooted Capture
before acquisition. Keep fixed Start/Stop slots, attempted/confirmed ownership
facts, independent single-attempt cleanup and original fatal ordering. Unknown
acquisition authorizes no guessed release; unknown release/free is never retried.
Legacy zero/two-argument callers are not migrated by this prerequisite.

Separate resource-use admission/join from terminal primitive drain. Close and
join action/failure/completed resource users before dependent native release;
then release caller Block ownership and independently confirmed native owners.
Only afterward await physical copy retirement and terminal managed drain before
returning the shell root/accounting. SCStream may retain a completion copy until
its release; requiring that copy's retirement first would create a wait cycle.
No gate spans native effects, observers or joins. The outer boundary's physical
IsDrained fallback must not conceal completion debt or return source ownership.

Account four fixed synchronous autorelease-pool scopes: construction, Start,
Stop and output removal. Confirmed pools pop once on their creating thread.
Unsettled constructor pool ownership prevents shell-root return; choose the
original primary/fatal after pop accounting, establish the exact failed-shell
handoff, then attempt independent cleanup. A replaceable thread-local slot is
not durable rooting.

## Consequences and provenance

This is per-shell ownership, not a process-wide capacity claim. The separate
max-16 Capture permit is not yet composed into current Capture. Keep delegate=0,
macOS14.2/ordinary-arm64 candidate admission, Protection Unknown and production
sharing unavailable. Portable injected effects, opt-in one-argument Block proof
and healthy task-owned SCStream regressions remain separately labelled. Full
MSC/global admission, native fault containment, physical and v1 gates stay open.

Independent C# work reuses this repository's ownership contracts and Apple SDK
Start/Stop `void (^)(NSError *_Nullable)` declarations, not Deskflow code or a
new production dependency. Those declarations do not promise Block-copy
retirement or absence of future callbacks. See
[requirements](../../specs/v1/native-remote-window/macos-capture-completion/requirements.md),
[design](../../specs/v1/native-remote-window/macos-capture-completion/design.md) and
[tasks](../../specs/v1/native-remote-window/macos-capture-completion/tasks.md).

## Finite implementation checkpoints

The [Start checkpoint](../evidence/2026-10-05-capture-completion-start-progress.md)
accepts tasks1–2 locally: the actual same-Capture copy-after-effect tracer fails
before attachment-order repair and passes with unchanged test bytes afterward.
The [first Stop behavior](../evidence/2026-10-05-capture-completion-stop-progress.md)
then repairs independently known Start caller+1 cleanup without inventing Stop,
resource-use join, native-copy retirement or terminal managed drain. The
[late root-free checkpoint](../evidence/2026-10-05-capture-completion-root-free-progress.md)
then requires both staged lifetimes and freshly reads their failures after
independent cleanup, preventing early shell-root/count return. These
controlled-effects checkpoints do not close task3, the remaining lifetime/pool
work, the new native gates, MCC, MSC or v1. Later source changes supersede their
selected-current equality; their immutable saved evidence remains historical.

The [one-argument native Block subgate](../evidence/2026-10-05-capture-completion-native-block-progress.md)
is now locally accepted with fresh Debug/Release native observations, strict
original-bundle validation,259 public CLI fixtures, closed two-axis findings
and actual root replay. Its opt-in/legacy-byte boundary is preserved. This is
not healthy Capture, native fault containment, hosted or full MCC acceptance.

The [eight-row uncertainty matrix](../evidence/2026-10-05-capture-completion-uncertainty-matrix.md)
now closes finite task3 coverage locally: attempted/confirmed allocation,
invalid-root and caller-release behavior retains the original graph without
guessed retries, including independently confirmed retirement with unconfirmed
caller release. Final focused D/R120/project D/R389, quality, both review axes
and actual root replay pass. Pool/resource-use/outer-drain work and later gates
remain open; no complete MCC or new hosted acceptance follows.

The first [constructor pool checkpoint](../evidence/2026-10-05-capture-completion-constructor-pool-progress.md)
now implements the fixed constructor facts and pop/failure-selection/handoff
ordering, retaining the first shell after a real slot replacement. Its unchanged
test-byte RED→GREEN, focused D/R121/project D/R390, both reviews and root replay
pass locally. Other constructor outcomes and the three other scopes, observers,
resource joins and full acceptance remain open.

The [Start pool checkpoint](../evidence/2026-10-05-capture-completion-start-pool-progress.md)
then adds fixed Start scope facts and its final shell-return gate. Actual
after-effect RED→GREEN, focused D/R122, both reviews and root replay pass; known
primitive/native cleanup is not confused with confirmed pool cleanup. Remaining
scopes, combined faults, resource joins and full acceptance remain open.

The [Stop pool checkpoint](../evidence/2026-10-05-capture-completion-stop-pool-progress.md)
also passes actual unchanged-test-byte RED→GREEN, focused D/R123, both reviews
and root replay. Confirmed primitive/native cleanup cannot authorize shell/count
return while its Stop pool pop remains unconfirmed. This closes one behavior,
not the remaining removal/combined/acquisition/observer or complete MCC gates.

The [output-removal pool checkpoint](../evidence/2026-10-05-capture-completion-remove-pool-progress.md)
preserves a confirmed removal result independently from pop confirmation, so
barrier/known-owner cleanup remains possible while pool debt retains the shell.
Canonical RED→GREEN, focused D/R124, both reviews and root replay pass locally.
Body/pop combinations, acquisition outcomes, observers and full MCC stay open.

The [Start body/pop checkpoint](../evidence/2026-10-05-capture-completion-start-body-pool-progress.md)
records an original body fatal before its synchronous pool unwind, preserving
that fatal when pop also throws. Actual RED→GREEN with unchanged test bytes,
focused D/R125, both reviews and root replay pass. This shared-method repair
does not itself verify Stop's corresponding injection or complete task4.

The [Stop body/pop checkpoint](../evidence/2026-10-05-capture-completion-stop-body-pool-progress.md)
subsequently verifies that injection as direct GREEN with unchanged production.
Focused D/R126, both reviews and actual root replay pass; no new behavioral RED,
production repair or complete-task4 acceptance is inferred.

The [unknown-removal body/pop checkpoint](../evidence/2026-10-05-capture-completion-remove-body-pool-progress.md)
preserves original body fatal before pop and separately records normal Stop
handoff return for its independent caller cleanup. Actual RED→GREEN, focused
D/R127, both reviews and root replay pass. Unknown removal still forbids
barrier/dependent release/root return; completed notification is not resource-use
join. Acquisition/observer outcomes and complete MCC stay open.

The [constructor zero-pool checkpoint](../evidence/2026-10-05-capture-completion-constructor-zero-pool-progress.md)
adds ordinary invalid-token rejection before scoped native work. Actual
RED→GREEN, focused D/R128, both reviews and root replay pass. No guessed pop
or retry is made; known source cleanup remains independent and unknown pool
debt retains the shell after genuine factory-slot replacement. Other scope
outcomes, observers and full MCC acceptance remain open.

The [Start zero-pool checkpoint](../evidence/2026-10-05-capture-completion-start-zero-pool-progress.md)
places the shared invocation guard before selector lookup, issued state and
native invocation. Actual RED→GREEN, focused D/R129, both reviews and root
replay pass. Local unissued settlement is not callback proof; known cleanup
does not return unknown pool debt. Stop-zero and remaining MCC gates stay open.

The [Stop zero-pool checkpoint](../evidence/2026-10-05-capture-completion-stop-zero-pool-progress.md)
then verifies refusal/retention independently as direct GREEN, focused D/R130,
both reviews and root replay, without production changes. The acquired-but-
unissued Stop caller remains a separate cleanup obligation; no successful Stop,
callback exit or complete task4 cleanup is inferred from refusal.

The [output-removal zero-pool checkpoint](../evidence/2026-10-05-capture-completion-remove-zero-pool-progress.md)
then rejects native RemoveOutput before its body on invalid acquisition. Actual
RED→GREEN, focused D/R131, both reviews and root replay pass. Confirmed caller
cleanup does not authorize barrier/dependent release or shell return without
removal proof. Unknown push, unissued caller and remaining MCC gates stay open.

The [four-scope unknown-push matrix](../evidence/2026-10-05-capture-completion-unknown-push-matrix.md)
subsequently passes sequential single D/R and final focused D/R135, both reviews
and root replay, with unchanged production. An effect followed by throw does
not confirm a returned token or authorize body/pop/retry. Original fatal and
graph/charge remain, with only known independent cleanup. Unissued Stop caller,
observer/resource-use/races and complete MCC acceptance remain open.

The [confirmed-unissued Stop caller checkpoint](../evidence/2026-10-05-capture-completion-stop-unissued-caller-progress.md)
adds qualification from normal acquisition and the unique setup flow's terminal
unissued fact, not Dispose's instantaneous issued snapshot. Actual RED→GREEN,
focused D/R136, both reviews and root replay pass. The caller releases once,
without inventing Stop/callback/dependent cleanup or returning unknown pool debt.
Native-invoke/observers, resource-use/races and full MCC gates remain open.

The [late-callback pair checkpoint](../evidence/2026-10-05-capture-completion-late-callback-progress.md)
then verifies actual retained native copies and delayed typed callbacks for
Start and Stop as direct GREEN with unchanged production. Focused D/R138, both
reviews and actual root replay pass. An invocation throw does not establish
handoff return or callback exit; only the later real callback/ABI Join and
independent caller/stream-copy cleanup establish the finite lifetime outcome.
Pending cross-boundary GC graph proof, observers, resource-use joins/races and
complete MCC acceptance are not inferred. Task5's first tracer is now underway.

The [first active-action checkpoint](../evidence/2026-10-05-capture-completion-active-action-progress.md)
now repairs that premature caller release with actual unchanged-test-byte
RED→GREEN. A fixed three-region admission owner publishes join only after
permanent closure and active0; no release attempt is recorded before join.
Focused D/R139, both reviews and actual root replay pass. Only the overlapping
Start-action behavior is accepted, not all observer regions, async orchestration,
duplicates/races/self-join/outer drain or complete task5/MCC.

The [terminal observer-fault matrix](../evidence/2026-10-05-capture-completion-observer-fault-progress.md)
then passes focused D/R142 (exact139+3), both reviews and actual root replay.
Action has two actual same-byte REDs; initial Start now surfaces the recorded
fatal without rolling back a settled result, and contained managed callback
failure alone does not retain ownership once independent cleanup and native/
managed terminal facts are confirmed. Fresh failure reporting remains intact;
unknown effect debt still retains the shell. Failure/completed cases are direct
GREEN. Cached repeated Start and active observer joins are not proved by this
finite matrix; task4/task5 and later aggregate/native/hosted gates stay open.

The [repeated-Start checkpoint](../evidence/2026-10-05-capture-completion-repeated-start-progress.md)
then closes that one cached-fatal behavior with actual unchanged-test-byte
RED→GREEN, focused D/R143, both reviews and root replay. A single fatal read/
rethrow precedes cached result return; it performs no native effect, observer or
wait under the short gate. Confirmed original success/settlement/handoff facts
remain unchanged, with one native Start/owner only. Active observer joins,
ancestry/races/outer drain and complete task4/task5/MCC remain open.

The [active completed/failure pair](../evidence/2026-10-06-capture-completion-active-observer-progress.md)
then passes direct GREEN with unchanged production, focused D/R145, both reviews
and actual root replay. Published completed/exit or a returned duplicate callback
does not retire another active resource-use region: caller release and attempt
facts stay zero/false until permanent closure plus active0. After real ABI joins,
independent caller/stream-copy cleanup and terminal lifetime permit graph return;
failure priority remains intact. This is not async join, ancestry/races/outer
boundary or complete task5/MCC acceptance.

The [completed ancestry pair](../evidence/2026-10-06-capture-completion-completed-ancestry-progress.md)
then passes direct Dispose and active inherited-EC async descendant Stop as
sequential direct GREEN, focused D/R147, both reviews and actual root replay.
The real guarded parent remains active while cleanup rejects before native
Stop, resource closure or release; actual ABI/child joins permit later external
known cleanup. Production is unchanged. Inactive descendants, other entry/
region combinations, async resource join, races and outer-boundary proof plus
complete task5/MCC acceptance remain open.

The [async resource-use join checkpoint](../evidence/2026-10-06-capture-completion-async-resource-join-progress.md)
then repairs actual premature StopAndDrain success while a completed observer
remains active. Both fixed resource-use admissions close before gate-free waits;
fresh state/fatal reporting follows. Same-test-byte single D/R1, focused D/R148,
both code review axes and actual root replay pass. This adds no native release or
terminal lifetime wait and does not close outer source-binding or full task5/MCC.

The [outer binding checkpoint](../evidence/2026-10-06-capture-completion-outer-binding-progress.md)
then verifies one real Catalog/Boundary/Capture negative contract as direct GREEN:
physical drain's fallback cannot bypass pending Stop-copy lifetime and return
the source binding or Catalog capacity. Thin default-off operations/test seams
use the same actual NativeSource; no cleanup behavior repair is claimed. Single
D/R1, focused D/R149, both code review axes and actual root replay pass. Healthy,
late-fatal, pending-GC/recovery and remaining task4/task5/MCC gates stay open.

The [pending healthy Start checkpoint](../evidence/2026-10-06-capture-completion-pending-start-progress.md)
then passes direct GREEN with unchanged production, focused D/R150, both review
axes and actual root replay. Dispose does not close admission or release owners
while the original issued/returned Start result awaits its real callback; later
ABI Join and external cleanup permit weak-graph collection. This does not prove
an active native handoff has stopped borrowing its caller/stream/source. That
separate task5 tracer has the checkpoint below; full MCC remains open.

The [native handoff borrow checkpoint](../evidence/2026-10-06-capture-completion-native-borrow-progress.md)
repairs actual Start-specific caller release before the held native frame exits.
Two fixed synchronous finally-exited facts protect corresponding caller and
dependent native/source release; normal return and pool confirmation are not
substitutes. Precise same-test-byte RED→GREEN, focused D/R151, both code review
axes and actual root replay pass. No gate spans effects/waits and pool pop stays
on the creating thread. This one held Start behavior does not accept all races,
task4/task5, MCC or native/hosted/v1 gates.

The actual [SourceUnavailable user observer](../evidence/2026-10-06-capture-completion-unavailable-observer-progress.md)
and [Stop completed notification](../evidence/2026-10-06-capture-completion-stop-completed-progress.md)
then pass sequential direct GREEN with unchanged production, culminating in
focused D/R153, both review axes and root replay. Earlier fatal A survives
user-observer B; unknown copy ownership stays rooted despite known independent
cleanup. Separately, a contained Stop notification fatal remains reportable
without overwriting successful Stop or retaining fully confirmed ownership.
Stop action/failure-wrapper evidence, task5 and complete acceptance stay open.

The separate [Stop action](../evidence/2026-10-06-capture-completion-stop-action-progress.md)
and [Stop failure-observer](../evidence/2026-10-06-capture-completion-stop-failure-observer-progress.md)
checkpoints now pass direct GREEN without production changes, culminating in
focused D/R155, both review axes and root saved/current replay/cmp/receipts.
Actual successful Stop, contained action fatal A and later guarded observer
fatal B retain separate facts: B does not replace A or suppress known cleanup.
Both terminal lifetimes and weak collection confirm ownership return, without
treating contained fatal reporting as unknown debt. Task4's finite coverage
audit leaves the published-constructor first pool-pop-fatal rollback trace open;
task5 races/reentry/outer composition and final aggregate,
healthy Capture/new hosted/full MCC/MSC/v1 acceptance remain open.

The [Start copy-in-flight checkpoint](../evidence/2026-10-06-capture-completion-start-copy-inflight-progress.md)
then passes direct GREEN, focused D/R156, both review axes and root replay/cmp/
receipts, with production unchanged. After actual copy-helper effects but before
confirmation, the exact attached owner/pending admission survives external Stop
and rejected Dispose with no guessed invoke/release. Finally release/Join permits
the original Start success then ordered Stop/known cleanup, with delivery still
closed. This finite acquisition race does not accept all task5 composition.

The [published constructor pop checkpoint](../evidence/2026-10-06-capture-completion-published-constructor-pop-progress.md)
then passes direct GREEN, focused D/R157, both reviews and root replay/cmp/
receipts with production unchanged. Actual cached async rollback returns false
while physical drain is true and known owners clean up once; first pool-pop
fatal/unknown debt retain the original shell through real slot replacement and
weak GC. The independent task4 coverage audit's sole gap is closed locally.
Only finite task4 implementation/test coverage closes; task5 and all final
aggregate/native/new hosted/MCC/MSC/v1 gates remain open.

The [duplicate Stop checkpoint](../evidence/2026-10-06-capture-completion-duplicate-stop-progress.md)
then proves actual unchanged-test-byte RED→GREEN: a duplicate error previously
regressed confirmed Stop despite the first successful TCS. The existing short
gate now admits the TCS result and its Stop facts through the same first winner;
no external call/wait or new state is added. Single D/R and focused D/R158,
both reviews and root saved/current replay/cmp/receipts pass. The first completed
region/native handoff remains active until finally release and actual drain
join; known cleanup/terminal lifetimes/weak collection then confirm return.
Reverse first-error behavior remains unexecuted; task5 and all final gates stay open.

The [duplicate Start checkpoint](../evidence/2026-10-06-capture-completion-duplicate-start-progress.md)
then passes actual unchanged-test-byte RED→final GREEN, focused D/R159, both
reviews and root saved/current replay/cmp/receipts. Only ordinary native error
notification is guarded by the first TCS result winner. Start's real settlement
fact still confirms after a prior issued-handoff fault; independent fatal,
failure-observer and catch notification remains unchanged. The preserved narrow
candidate single GREEN is superseded, not a fabricated old-test failure.
No new field/seam or ownership machine is added. Closed-late/outer composition,
task5 and final MCC/native/hosted/v1 acceptance remain open.

The [closed Start late-ABI checkpoint](../evidence/2026-10-06-capture-completion-closed-start-late-progress.md)
then passes direct GREEN, focused D/R160, both reviews and root replay/cmp/
receipts without production/seam changes. A valid known extra retain carries
the actual late ABI after both resource admissions close and native owners
release; completed does not reenter and only ABI accounting increases. Known
copy retirement then permits terminal cleanup/weak collection without effect
replay. Shared-guard review complements that direct witness, not invented
action/failure-hook experiments. Outer recovery, task5 and final gates stay open.

The [exited completed ancestry checkpoint](../evidence/2026-10-06-capture-completion-exited-ancestry-progress.md)
then passes direct GREEN, focused D/R161, both reviews and actual root replay/
cmp/receipts with production/support/seams unchanged. A normal Task.Run child
inherits the actual active completed scope, waits until parent ABI Join and
confirmed borrow exit, then observes that same scope inactive and performs
normal Stop/Dispose/repeat. Full weak collection includes the actual scope.
No synthetic ancestry or equivalent action/failure matrix is added. Fresh
terminal failure publication, outer composition, task5 and final gates remain open.

The [fresh terminal/outer contained-fatal checkpoint](../evidence/2026-10-06-capture-completion-fresh-terminal-progress.md)
then has actual same-test-byte binding RED→GREEN; the fresh-failure observation
core is direct GREEN. The same real primitive publishes the fatal before its
actual managed terminal, and Capture's fresh read preserves it after both
terminal observations. A default-false readonly complete-cleanup proof permits
independent binding return despite that contained diagnosis; physical drain
cannot substitute. Static review makes the two existing disposed publications
monotonic with OR, without a new state or claimed runtime RED. Final single
D/R1, focused D/R162, both reviews and root replay/cmp/receipts pass. Source/
capacity return and full weak collection are confirmed in this actual window.
Outer healthy, pending-GC/known recovery, task5 and final gates remain open.

The [outer healthy checkpoint](../evidence/2026-10-06-capture-completion-outer-healthy-progress.md)
subsequently passes direct GREEN, focused D/R163, both reviews and actual root
replay/cmp/receipts with production unchanged. Actual binding/source/capacity
return, same-pool replacement and weak collection confirm that finite normal
lifecycle. Pending-GC/known recovery, task5 and all final gates remain open.

The [outer pending-GC checkpoint](../evidence/2026-10-06-capture-completion-outer-pending-gc-progress.md)
then has final same-complete-test-byte binding RED→GREEN, single D/R1, focused
D/R164 and full macOS project D/R433, both reviews and actual root replay/cmp/
receipts. Boundary can collect while the exact pending graph remains rooted and
charged. Optional default-false qualification reads actual primitive/resource/
owner facts; one isolated Operation recovery first joins the original failed
Stop Task, then original native/managed lifetime Tasks, and reobserves Dispose
and complete proof before binding return. Native effects and the old Stop result
are never replayed/replaced. Already-terminal Tasks remain eligible to avoid
TOCTOU loss; any recorded disposal failure conservatively rejects recovery.
Known fixture retirement permits source/capacity return, same-pool reuse and
full weak collection. Only ordinary known-held recovery is accepted, not all
contained-diagnostic-plus-pending combinations or unknown-effect recovery.
Final source/aggregate/native/hosted, complete task5/MCC/MSC/v1 remain open.

The [final source readiness review](../evidence/2026-10-06-capture-completion-final-local-readiness.md)
subsequently closes finite task5 implementation/test/source coverage only.
Standards0/Spec0 confirm shared guards and legacy preservation. Its one static
BlockProbe repair forbids finally caller/extra release when ABI-thread Join is
unconfirmed; known references and waiting gates remain retained and the probe
fails. No runtime RED or timeout cleanup is claimed. Root may now freeze a fresh
aggregate campaign; task6/7/8 and complete MCC/MSC/v1 acceptance remain open.

The [final local progress](../evidence/2026-10-06-capture-completion-final-local-progress.md)
then covers local task6 with new final-source Block and task-owned healthy
Capture D/R. Aggregate candidate01 remains failed despite its Release3007
component pass. Serial diagnostic success is not precise MSBuild root-cause
proof; document checksum presentation repair does not exempt any scanner rule.
Fresh candidate02, task7/8 and full MCC/MSC/v1 acceptance remain mandatory.

The same progress record then preserves candidate02's format failure and accepts
only candidate03 finite local task7: actual D/R3007,20×164 repeats, quality/default
security, two format-delta reviews0 and actual worker/root replay/cmp/receipts.
Both corrections are presentation/whitespace only; production pins stay fixed.
Whole-tree current equality becomes historical after evidence edits. Task8's
new committed-SHA hosted gates and complete MCC/MSC/v1 acceptance remain open.
