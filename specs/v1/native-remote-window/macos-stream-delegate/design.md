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

## Native association slice (Foundation Phase 2a verified; early protocol open)

The opt-in tool now implements and actually verifies the tag/reference/dealloc
subset below, without actual Capture. See
[Phase 2a evidence](../../../../docs/evidence/2026-10-05-macos-native-association.md).
Its nil-tag path rejects unknown sources; it does not yet implement the retained
early-fact/publication protocol described here. That protocol remains required
before replacing Capture's `delegate=0`.

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
Exact new-SHA hosted and actual Foundation early-publication proof remain open.

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
contracts. Actual Foundation early-publication execution remains a separate
required gate before any Capture composition.
`CompleteAssociation` returning true confirms an association fact only: cleanup
in its finally path can still fail and close the runtime. Capture must consume
`Failure` and `NativeAdmissionClosed`; the Boolean cannot grant sharing admission.

## Capture composition (not yet implemented)

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
