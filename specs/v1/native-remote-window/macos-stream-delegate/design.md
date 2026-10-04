# Generation-routed macOS stream delegate design

## Portable first slice

Use an internal bounded router and registration API. Construction reserves a
Capture permit, an exclusive initializer and a monotonically increasing numeric
generation under one short managed gate. The native composition must root one
router for the process and must never replace/reset it after native publication;
fresh portable test instances do not publish native objects. Abandoning an
unpublished construction can release initializer exclusivity without poisoning
future initialization, but it cannot release the Capture permit: configuration,
output or queue resources may already exist. Permit return always requires
explicit complete cleanup (including an allocation-free reservation's absence
fact), never just absence of delegate publication. Once the delegate has been
published to native initialization, failure to prove immutable stream
association poisons future initialization.
For a published successful construction, only confirmed association releases
initializer exclusivity for later Captures; unpublished abandonment is the
explicit separate path above. Ambiguous published failure poisons the router
before relinquishing that initializer.

The native boundary, not the portable router, proves that a callback's numeric
generation came from a valid retained tag. The router admits exact-generation
terminal signals, stores at most one early terminal fact, activates one handler,
and closes delivery before once-only notification. Active cannot resurrect it.
Retirement joins managed invocations and detaches handlers. A separate explicit
complete-cleanup confirmation releases the Capture permit; no timeout, handler
exit or tag observation substitutes for that confirmation. Quarantine consumes
the permit. No native pointer is stored or read in this first portable slice.

External handlers run outside the routing gate. Use direct callback ancestry
and the existing active ExecutionContext drain scope to reject self-join,
including Task.Run descendants. Completion continuations run asynchronously and
outside the gate. Callback exceptions are observable and fail admission closed.
Generation exhaustion rejects rather than wrapping.

## Native association slice (Foundation local/hosted prerequisites verified)

The opt-in tool now implements and actually verifies the tag/reference/dealloc
subset below, without actual Capture. See
[Phase 2a evidence](../../../../docs/evidence/2026-10-05-macos-native-association.md).
The legacy Phase 2a nil-tag path still rejects unknown sources. The independent
early mode now executes the retained early-fact/publication protocol locally and
at exact `2c6f8fd` in hosted CI. Neither mode is actual Capture
composition or permission/pixel evidence.

One permanent NSObject bridge dispatches via immutable stream associations.
Use a small NSObject tag subclass with a numeric ivar, not NSNumber (which can
use tagged pointers). A separately bounded tag-allocation permit remains held
until the tag's actual nonthrowing `dealloc` completes successful superclass
deallocation, not merely dealloc entry; the ordinary-arm64 implementation
must invoke NSObject superclass deallocation with the verified ABI. The tag
contains no Capture, handler or GCHandle. Association is retained and never
removed at retirement. This avoids depending on undocumented SCStream delegate
retention or no-future-callback guarantees.
Validate the runtime tag class, method encoding, ivar type/size/alignment and
runtime-reported ivar offset before native generation reads. A compiler's
observed NSObject layout is shape evidence, not a hard-coded universal offset.
An explicit `objc_msgSendSuper` dealloc call supplies the verified NSObject
superclass, not TagClass (which would recursively re-enter the override).
Compiler-emitted `[super dealloc]` uses `objc_msgSendSuper2` with a different
starting-class convention; these entry points must not be interchanged.
Save any budget-return fact before superclass deallocation; after it returns,
do not read `self`/ivars or message the freed object. Budget bookkeeping uses
only process-rooted state and the saved value.

A callback synchronously retains its valid borrowed SCStream, reads and retains
the associated tag, extracts the generation, then routes without a native call
under the managed gate. Finally balances the acquired references after any
handler-driven stream release. Nil-tag early events retain the stream before
recording pointer identity, preventing pointer ABA. Snapshotting the initializer
and re-reading the association is required when publication races with the
nil-tag read. Clearing pending initialization must be serialized with recording
the exact early fact; no callback may fall through to a replacement initializer.
If nil re-read precedes publication but exact-token early recording loses to
publication/clear, the callback must read association again and route only the
verified immutable tag. Rejection of the early record is not permission to drop
the event or snapshot a later initializer. If no tag can be proved, preserve a
fail-closed ambiguity/poison outcome instead. A deterministic native-boundary
test must gate nil re-read, successful publication plus pending clear, and then
the losing early-record attempt in that order.
An ambiguous published nil/throwing initialization poisons later initialization
instead of guessing the generation of subsequent untagged callbacks.
Repeated early terminal events for one retained stream coalesce. A second
distinct untagged stream during the same initialization is ambiguity: poison
the construction, never overwrite the first identity or route either to a
later initializer. Exact association completion or rejection owns release of
all acquired pending-stream references; failed/uncertain release retains that
ownership within an independent charged process record, with related Capture
quarantine while that registration still owns resources. Pending-fact storage itself
must be bounded; rejecting an extra fact still balances its callback +1.

Objective-C association publication can initialize/reenter runtime code. Do not
hold the routing gate across `objc_setAssociatedObject`, native retain/release,
superclass deallocation or handlers.

## Retained early-association coordinator (portable/local contracts verified)

The final 29-case coordinator and 33-case router focused suites pass in both
configurations; full local solutions pass 2775 cases each. See
[portable evidence](../../../../docs/evidence/2026-10-05-macos-early-association-coordinator.md).
Exact `9deed36` hosted CI failed four Windows fixture cases, while all 29
coordinator cases passed on all OSes. The repaired complete local solution now
passes 2777 cases per configuration; exact `2c6f8fd` now passes the complete
three-OS hosted inventory. The separate Foundation early-publication proof has
final local and hosted evidence. Native error/ownership and Capture composition
remain separate unverified gates.

An immutable Initializer object wraps one registration; it is never recycled.
The coordinator serializes exact-token/pending-fact state with a short gate,
but admits native calls and invokes handlers outside it. A valid borrowed
source is retained before any association read. After a first nil read, snapshot
the exact initializer and perform the second read. If that read is nil but
recording loses to publication/pending-clear, perform the third read and route
only a verified immutable tag. Never resnapshot a replacement initializer.
No proved tag after that race is ambiguity, not permission to drop the terminal
event. Same-source terminal facts can coalesce; Active cannot occupy the record.
Distinct early sources or a returned source differing from the retained fact
poison construction rather than overwriting identity. Association publication
retains its own source through native reentry and rechecks exact-token/poison
state afterward. Completion/rejection has one owner for pending-reference release.

Use a fixed maximum-16 array of ownership records distinct from both Capture
and tag-allocation budgets. Reserve before any native retain/read/publication;
this also bounds callbacks whose ownership outcome becomes unknowable. Each
record tracks source/tag acquisition attempted versus confirmed and release
attempted versus confirmed. An early fact transfers its existing charged record,
not an untracked pointer. Return a record only after all references are confirmed
released. A throwing retain/read may have changed native ownership; a throwing
release may have consumed its +1. Keep those records charged, do not infer
absence, and never retry an uncertain release. These are process-lifetime
quarantines, not native fault containment or a cleanup/drain guarantee.

Identity ambiguity only poisons later construction and quarantines related
exact tokens; it must not notify unrelated or replacement Captures. By contrast,
native ownership/resource faults and record exhaustion permanently close new
native-work admission and every registration's delivery admission. Preserve the
first primary exception and original nested fatal exception; expose runtime
Failure/NativeAdmissionClosed for later Capture composition. These state-only
router operations invoke neither native code nor handlers. A late unknown fault
cannot restore a registration whose complete-cleanup slot was already reused;
the separate process record owns the uncertainty.

Every non-cleanup native operation receives admission under a short state gate
and executes outside it. Work admitted before a fault may still finish; closure
does not claim native drain. Later phases require fresh admission. Independently
confirmed source/tag references still get their single cleanup attempts in nested
finally paths after a fault; failure of one release must not skip the other.
Portable boundary fixtures prove only these managed ownership and ordering
contracts. Separate Foundation early-publication execution verifies real native
reads/retains/associations in controlled interleavings, not actual SCStream
scheduling or native fault containment.
`CompleteAssociation` returning true confirms an association fact only: cleanup
in its finally path can still fail and close the runtime. Capture must consume
`Failure` and `NativeAdmissionClosed`; the Boolean cannot grant sharing admission.

## Opt-in Foundation early-association proof (local/hosted verified)

`--run-early-associations` is an independent no-capture mode, with its own
process-rooted coordinator/router/bridge and validated numeric tag class. Keep
default/help/unknown arguments and the existing synthetic/Phase 2a output
contracts unchanged. Only ordinary-arm64 macOS 15.2+ can execute it; unsupported
hosts return explicit Skip, which a native CI gate must reject. No SCStream,
AppKit/window, content enumeration, permission, sample/pixel or input API enters
this mode. A fresh process and external deadline contain a stalled probe; they
do not establish native exception containment or cleanup.

The first vertical slice sends an actual typed terminal callback on a real
NSObject with a nil association before initialization completes. Verify retained
same-source identity, one pending replay after actual association publication,
terminal-once delivery, complete reference accounting and successful NSObject
superclass tag deallocation. A first tracer result cannot claim the publication
race: its separate race/ambiguity fields remain false until exercised.

Then gate the callback immediately after its actual second native nil read has
completed, publish/clear the exact initializer, admit a replacement initializer,
and resume the losing early-record attempt. Verify the third actual native
association read routes only the original immutable generation and never the
replacement. A separate nil-third-read/distinct-source scenario must preserve
poison/quarantine without guessed replacement notification. Controlled hooks
follow real native operations; this is deterministic native-boundary interleaving,
not proof of SCStream/OS scheduler behavior. No production coordinator test hook
or Capture modification is required.

Every reverse entry has a local autorelease pool, managed exception containment
and an outer failure gate. Count actual source/tag owners, reference acquisition
and confirmed release, native reads/callbacks, pending/race notifications and
final charged/uncertain ownership separately. Successful output requires bounded
one-LF/no-NUL bytes, empty stderr, exact Pass facts and exit zero; uploads retain
raw failure/Skip bytes. Unknown native outcomes never become a balanced Pass.
Final Debug/Release and 24 independent early-mode processes pass with identical
1057-byte stdout and empty stderr. Only stages 13/14/15 bind the final source;
one actual native RED→GREEN pair (02→03) is recorded separately from direct
GREEN additions and compiler exclusions. The strict gate is byte-exact and
rejects Skip/nonzero/stderr/timeout. A Python3-stdlib POSIX process watchdog is
CI infrastructure, not a product language split or native cleanup proof.
Only complete local and exact-commit hosted evidence can close the corresponding
native subitem; actual Capture and remaining MSC gates stay independent. See
[native evidence](../../../../docs/evidence/2026-10-05-macos-foundation-early-associations.md)
and [root gates](../../../../docs/evidence/2026-10-05-early-checkpoint-local-gates.md).

## Capture composition (not yet implemented)

The first composition preparation extracts only the Capture's native system
boundary, not another Capture state machine. An internal operations interface
owns allocation/configuration, output and sample queue operations, stream
initialization, completion invocation, release, pool and sample reads. A source
owner interface retains/checks/releases the exact source; a small completion
owner wraps the unchanged copied-Block implementation. Allocate and record each
+1 owner before later configuration can fail. Keep stream alloc/init ownership
semantics explicit. The unmanaged sample trampoline and portable tests invoke
one shared managed sample core on the actual Capture. Enumeration, TCC/window
system and permission APIs remain outside this seam.

This initial refactor deliberately keeps production `delegate=0`, the existing
14.2 candidate floor and availability unchanged. Tests drive healthy lifecycle,
completion action versus callback exit, factory rollback/handoff, sample queue
barrier, independent owner quarantine and sample ownership transfer. Such tests
prove the existing same-state-machine composition with fake native boundaries;
they do not prove SCStream execution or the later nonzero-delegate lifetime.

`MacOSRemoteWindowScreenCaptureKitApi.Capture` currently publishes only sample
output and passes `delegate=0`. Nonzero delegate publication occurs before
entering InitStream and must be tracked independently of AddOutput publication.
Its construction catch, successful Start completion, once-only SourceUnavailable,
drain and GCHandle/native release paths must use the new registration. Terminal
events close Capture delivery first. Existing copied-Block/sample queue proofs
do not establish delegate drain or become obsolete through generation routing.

## Verification

Deliver vertical RED→GREEN tests through the real portable registration API:
terminal without sample; early terminal; wrong/retired generation; initializer
contention; ambiguous publication poison; blocked retirement; direct/descendant
self-join; successful permit reuse without generation reuse; quarantine and
exhaustion; handler/fatal fault containment. Then execute the no-capture native
association/dealloc proof. Only afterward integrate and test the actual Capture,
including source-close with separate native sample and sink counters. Record
actual local and exact-commit all-OS hosted results without inferring physical,
production or release acceptance.
