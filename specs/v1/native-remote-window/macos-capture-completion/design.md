# Same-Capture staged one-argument completion

Status: implementation contract for MCC1-MCC8. Finite implementation/test
checkpoints are recorded in [tasks](tasks.md); full MCC acceptance is unverified.

## Source facts read

All repository paths below are under
`/Users/happys/Documents/GitHub/flowspan/` at
`1ab251135f3617bc18e39d5f67e38885b53e85a7`:

- `src/Flowspan.Platform.MacOS/MacOSRemoteWindowBlock.cs`: legacy one-argument
  Create selects `InvokeOne`, Cdecl `void(block, object)`, and signature
  `v16@?0@8`; the argument is borrowed. Staged Prepare/AcquireCopy currently
  selects only the two-argument invoke/descriptor. Shared copy/dispose helpers
  distinguish physical captures from heap retains. Terminal managed drain waits
  for confirmed root retirement and zero admitted primitive invocations; neither
  that task nor completed notification proves the final ABI return.
- `MacOSRemoteWindowCaptureOperations.cs` and `MacOSRemoteWindowScreenCaptureKitApi.cs`:
  `CreateCompletion` allocates the legacy primitive before returning. Start and
  Stop store the owner only after that call. Capture is already durably rooted
  before its per-Capture native allocation and staged-source retention. Known
  cleanup is single-attempt/outside gates, and sample/output drain is separate
  from cleanup success. Its `retainedOwners` count is diagnostic, not admission.
- `MacOSRemoteWindowCaptureBoundary.cs`: one operation per boundary; failed
  cleanup retains that operation. It is not the MSC process capacity gate.
- `MacOSRemoteWindowStreamDelegateRouter.cs`, association coordinator, MSC
  requirements and ADR0030: the separate max-16 reservation/runtime is not used
  by current Capture. This slice must not imply it is wired.
- `MacOSRemoteWindowEnumerationCompletion.cs`: already stages the actual
  two-argument primitive in its original BatchRecord before acquisition. Its
  repaired notification/independent-cleanup/fatal behavior is not an open finding.

The local Xcode SDK's actual
`/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk/System/Library/Frameworks/ScreenCaptureKit.framework/Versions/A/Headers/SCStream.h`
declares Start/Stop `void (^)(NSError *_Nullable error)`. Its comments describe
operation completion, not Block-copy disposal or no-future-callback/drain proof.

## One primitive, one Capture

Add only an inert **one-argument** staged overload to the existing Block primitive
and a thin completion owner exposing acquisition, caller release, failure,
native capture retirement and terminal managed drain. Parameterize the existing
prepared literal's invoke/descriptor by its fixed argument shape; do not wrap a
two-argument native entry in a one-argument managed lambda or migrate legacy
zero/two-argument Create callers. Native metadata remains process-lifetime;
per-operation root/copy effects occur only after owner attachment.

Change the Capture operations seam from acquire-and-return to prepare-and-return
for its Start/Stop owners. In the same Capture, attach `startBlock`/`stopBlock`
before AcquireCopy. Both production and portable tests execute this exact state
machine and the actual primitive entries; only root/Block/native system effects
are replaceable. No test-only alternate completion state machine establishes
acceptance. Retain the existing source, sample-output and factory cleanup paths.

Each shell has two fixed completion slots and fixed synchronous pool facts
(construction, Start invocation, Stop invocation, output removal). The existing
`RemoveNativeOutput` pool has its own fourth fixed acquisition/pop facts; it is
not covered by the Stop-invocation scope. Do not create a growing list
for callback occurrences, native copies or failed ownership. Repeat callbacks
use counters and closed/settled facts on the original owner. Global shell capacity
remains an explicit subsequent MSC obligation, not a newly claimed bound.

## First tracer, then staged coverage

The first tracer targets the actual Start completion copy **after effect**. Add
the minimal shared real one-argument completion adapter accepting the existing
root/Block effects seam. During RED setup, retain `CreateCompletion` as
`Prepare -> AcquireCopy -> return`; Capture still assigns `startBlock` only after
return. The controlled runtime executes the actual primitive copy helper, creates
the physical capture, then throws a wrapper containing the chosen fatal.

Drive actual Capture Stop/Dispose after this fault. Drop test strong references
to the completion adapter/primitive, operations and shell; use weak references,
forced GC and existing retained-owner accounting to inspect the original-shell
graph and premature root/count return. A test variable rooting the missing owner
or a fake completion state machine cannot prove containment. Necessary one-arg
plumbing is setup evidence, not a claimed behavioral RED. Preserve the actual
failed execution. The minimal GREEN changes only the ownership order to
`Prepare -> attach to Capture -> AcquireCopy`; the test bytes remain identical.
Then apply the same finite contract to Stop, one additional behavior at a time.

On uncertainty the pointer is not publishable, issued state must not be fabricated,
known independent owners can clean up once, and the original shell/root remains
charged. Record the original primary/nested fatal before any observer or cleanup.
Never retry an unconfirmed effect or rely on a staged owner's finalizer.

## Lifecycle and cleanup ordering

Use short gates only for selection/admission/facts. Preparation, acquisition,
native invoke/release, observers and joins execute outside them. Model acquisition
versus release explicitly so reentrant Stop/Dispose cannot lose or release a
not-yet-attached completion. Native invocation borrowing must be serialized with
caller release; an acquisition success alone does not prove successful invocation.
An actual callback can return and publish result/exit with resource-use active0
while the synchronous native handoff is still on its calling stack. Those facts
do not prove borrow exit. Dependent caller/stream/source release must separately
observe that handoff's finally-exited fact, including exceptional exit, without
moving its autorelease-pool unwind to another thread or waiting on post-release
Block retirement. The finite
[native borrow checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-native-borrow-progress.md)
uses two fixed finally-exited facts: scope entry is the existing fixed pool-push
attempt, and an outer synchronous finally publishes exit even after push,
selector, invocation or pop throws. Release selection checks these facts under
the short Capture gate; native work and the original-thread pop stay outside.
Borrow exit does not clear unknown pool effect debt. Its held Start behavioral
RED→GREEN is accepted locally; other acquisition/handoff races remain open.

Callback result/resource-use admission closes monotonically. The first accepted
Start/Stop result has its existing TCS semantics; later callbacks have no native
reads, repeated notification or state resurrection. An admitted callback that
already uses the shell must finish its managed resource use before dependent
release. This applies to **all** action, failure-observer and completed-notification
resource-use regions, not only the action or settled TCS. Close their resource-use
admission and join the admitted set before native release. First idle is only that
set, not terminal drain. Restore callback ancestry in finally and reject direct/
active-descendant joins.

The finite asynchronous join implementation obtains both fixed owners'
`CloseResourceUse` tasks after confirmed Stop/output-removal/sample-barrier
progress, before awaiting either outside the Capture gate. It then freshly reads
settled/unsafe state and reports the pending fatal in the existing finally.
Physical drain remains a separate fact; this step adds neither native release
nor a post-release lifetime wait. See the
[finite checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-async-resource-join-progress.md).

That pre-release resource-use join must be separate from the primitive's
`ManagedInvocationDrain`. The latter requires confirmed native root retirement
plus zero remaining primitive invocations, so it is a **post-release** lifetime
fact; waiting on it before releasing a stream-held native copy creates a cycle.
Late entries may still be tracked by the primitive, but after closed admission
their action/failure/completed paths must not use released Capture resources.
Closing admission must not strand a still-pending result TCS: require a real
settled result or confirmed unissued handoff, and do not continue waiting on a
completed notification whose resource-use admission has been suppressed.
After terminal native/managed lifetime is confirmed, freshly observe primitive
failure before shell-root return; reading failure only before lifetime checks
can miss a fault published between those observations.

Do **not** impose `all native owners release -> only after Block retirement`.
SCStream may own a completion copy until stream release; the SDK does not prove
earlier disposal. After confirmed Stop/output removal/sample queue barrier and
closure/join of admitted resource users, give independently known native owners
their single cleanup attempt, including releases that may retire native Block
copies. Release the caller's Block +1 before awaiting that copy's final retirement.
Do not await under a managed gate. Only after all required owner facts and both
completion native-retirement/terminal-managed-drain facts are confirmed may the
shell root/accounting return. A late copy/root failure ends with unconfirmed
cleanup and durable graph retention, never an invented successful drain.

Keep separate internal sample/output-drained and complete-cleanup facts. Reconcile
the public `IsDrained`/`StopAndDrainAsync` contract with the existing outer boundary
explicitly: its `StopAndDrainAsync() || IsDrained` fallback must not conceal
completion debt, treat cached sample proof as complete cleanup, or authorize
shell/source-binding return. A cached barrier must never make Dispose/root return
succeed despite completion debt. Prefer asynchronous drain/lifetime orchestration and
synchronous Dispose that observes its immutable cleanup proof; no blocking native
callback self-join. No success timeout is permitted. An external test watchdog
may stop a hung test without establishing cleanup.

The finite [outer binding checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-binding-progress.md)
executes that actual fallback with the same Catalog NativeSource and an
independently held Stop heap copy. Native Dispose rejects pending lifetime before
binding disposal; Catalog retirement consequently keeps its source and capacity
charged. The added operations overload and optional test factory are thin setup
seams, not a replacement source or cleanup state machine. This negative contract
does not establish eventual recovery or healthy/late-fatal/GC composition.

The finite162 checkpoint combines fresh terminal failure observation with actual
outer contained-fatal cleanup only through the same controlled window. Its
test-only completed-exit observation runs after the real resource-use region
exits and before the original exception reaches primitive containment; a pure
forwarding completion observer returns the original lifetime Tasks and joins
the actual ABI producer. It creates no failure or lifetime state itself.

The tracer proves the outer layer skipped binding cleanup after a fully
confirmed native cleanup that still reported a contained fatal. The minimal
repair adds an optional/default-false complete-cleanup proof. Actual Capture
exposes its existing `disposed` fact, set only after all independent
owner/pool/completion cleanup and shell-root/accounting return succeed. This is
distinct from physical `IsDrained`. After a thrown native Dispose, the boundary
must preserve diagnosis and may continue independent binding cleanup only if
that complete proof is true. An unknown or pending effect remains unconfirmed;
neither exception category nor a cached physical drain authorizes return. No
new state machine, native effect retry or proof-by-test-flag is introduced.
Both existing publications use OR under their original gate so a delayed false
cannot erase an already confirmed proof. This is a static review correction,
not a separately executed concurrency RED. See the
[finite evidence](../../../../docs/evidence/2026-10-06-capture-completion-fresh-terminal-progress.md)
for same-test-byte binding RED→GREEN, final source-bound regression and the
remaining outer healthy/pending-GC/native/hosted/full acceptance boundaries.

The separate [healthy outer checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-healthy-progress.md)
now observes the actual Catalog/Boundary/Capture path returning its binding,
source references and capacity, with same-pool replacement and weak collection.
It changes no production interface or state and uses the already executed cached
drain task, not a new Stop request after disposal. Pending-GC/known recovery and
aggregate/native/hosted acceptance remain separate open obligations.

The accepted [pending-GC checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-pending-gc-progress.md)
uses an optional, default-unsupported known-pending cleanup join on the original
Capture. Qualification requires
physical drain, all independent native owners and both caller releases confirmed,
all attempted pools confirmed, borrow exit and resource-use joins. Actual staged
primitive allocation/copy/release facts must exclude unknown acquisition or root
free; incomplete Tasks alone are not qualification. Only real post-release native
retirement/managed-drain Tasks may be joined. The original Operation can own at
most one isolated recovery continuation, which first observes the immutable
failed StopCompletion, then joins lifetime, reobserves Dispose and complete proof,
and only then returns its binding. It must not replace the old failure receipt,
await itself or replay Stop/removal/barrier/release effects. Any unknown effect
keeps the original graph charged. Lifetime may have become terminal between the
first failed Dispose observation and qualification; those same actual completed
Tasks must still permit reobservation, rather than requiring one to remain pending
and losing recovery to an observation race. Conservatively decline any previously
recorded disposal failure: the shell GCHandle free has no independent attempted
fact, so allocated/counting observations cannot exclude an earlier free exception.
This also excludes contained-diagnostic-plus-pending recovery from this finite
slice; already confirmed cleanup with reportable diagnosis remains covered
by checkpoint162's independent complete proof. Checkpoint164 has actual same-
complete-test-byte binding RED→GREEN, final single D/R1, focused D/R164 and
full macOS project D/R433, both review axes and actual root saved/current
replay/cmp/receipts. Boundary collects while the pending original graph remains
rooted/charged; known fixture retirement and actual joins permit binding/source/
capacity return, same-pool reuse and full weak collection. This accepts only
ordinary known-held recovery, not contained-diagnostic-plus-pending combinations,
unknown-effect recovery, full task5, native SCK or aggregate/hosted/v1 acceptance.

Pool scopes remain synchronous and thread-affine. Mark acquisition before push;
confirm only the returned valid token. If push returns zero or throws before
confirmation, reject that scope's native body and preserve the acquisition debt;
do not invent a token or pop it. Attempt each known pop once before leaving
its creating thread, including on body failure. Do not blindly pop an unknown
token or retry an uncertain pop. Apply those fixed facts to construction, Start,
Stop and `RemoveNativeOutput`, including output-removal body/pop failures.

Before constructor pool entry, attach its scope facts to the original shell;
an unsettled scope prevents root/count return even if other owners have cleaned
up. Construction body failure records its original primary/fatal without releasing
the shell root. Pop that known pool on the original synchronous thread, contain
its failure and select the original primary/fatal first. Then establish exact
failed-shell handoff using that selected exception before subsequent independent
cleanup/rollback. A finally fault cannot escape after root return, replace the
original fatal or rely solely on the replaceable thread-local handoff slot.
Pool failure cannot skip independently known owners. These four scopes are the
finite pool scope of this slice; enumeration/source producers remain unchanged.

## Evidence and adoption risks

Portable tests need a typed one-argument invoke path in the controlled runtime;
the current two-argument test invoke does not prove this ABI. Add a separate opt-in
one-argument native Block proof with extra native retain/last-copy retirement and
an invocation kept active past the completed notification. Preserve all old
BlockProbe CLI/output schemas; its existing two-argument result is not new proof.
Also rerun healthy task-owned SCStream Capture Debug/Release separately. Neither
proves production fault containment or arbitrary copy retirement scheduling.

Execution must resolve these constraints through targeted tests/code:
(1) shared adapter RED setup must keep Prepare/Acquire inside CreateCompletion
and owner assignment after return; (2) exact resource-use join/admission covering
action/failure/completed before release, separate from terminal managed drain;
(3) internal drain/cleanup fact placement without the stream/Block dependency
cycle; (4) constructor pool-finally failure rooting and exact outward exception;
(5) test quarantine cleanup must not reset global production ownership state.

The [final local progress](../../../../docs/evidence/2026-10-06-capture-completion-final-local-progress.md)
records new final-source local Block and task-owned healthy Capture D/R, closing
only native task6. Failed aggregate candidate01 is preserved; fresh serial
candidate02 must verify all local regression/security/repeat contracts without
inheriting its Release result. Hosted, full MCC/MSC and production limits remain.

Candidate02's format failure is also preserved. Mechanical two-test-file
whitespace correction changes no non-whitespace bytes. Fresh candidate03 passes
all six local stages, exact D/R3007/20×164 identities, default security and actual
worker/root replay/cmp/receipts. Only local task7 closes; new committed-SHA hosted
task8 and the original native/global-admission/production limits remain.
