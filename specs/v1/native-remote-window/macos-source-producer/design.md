# macOS initial source-producer design

## Production path and first tracer

Extract only system effects from the initial CreateSource path, keeping one
production orchestration and real NativeSource. Preserve eligibility/identity,
geometry and pixel limits. Allocation and initialization are separate effects;
retain/release and frame/scale/current queries are injectable system boundaries.
A test-only alternative owner state machine is not sufficient.

First tracer: `InitialRetainAfterEffectFaultKeepsDebt`. A valid source candidate
gets a confirmed filter, then the initial window retain changes ownership and
throws an original nested OutOfMemoryException before assignment. Verify the
fatal instance, one independent confirmed-filter cleanup, no guessed release of
uncertain window ownership, retained slot/batch and full graph after settlement
and GC. First save a seam-only baseline that actually fails the ownership
assertion; do not label compiler/missing-type failures or predicted behavior RED.

## One obligation, one bounded record

Catalog already reserves a batch and128 source slots before enumeration. Pass
an explicit internal production context into the real API; do not rely on an
ambient AsyncLocal or a replaceable last-failure field. Claim one of those slots
and attach a pre-effect token containing the borrowed window, native effect
owner, filter/window attempted/confirmed facts and original failure identities.
All token allocation and linkage precedes its first native ownership effect.

The token remains rooted by its original SourceRecord throughout allocation,
initialization, validation, retain and real NativeSource construction. Transfer
that same record into the returned source and then entry; PrepareEntry recognizes
that record rather than selecting another empty slot. Uncertain producer debt
marks the original record and batch failed before CompleteBatch may return them.
It also closes the exact producing context's admission before propagating the
failure, without a native effect or external callback under a pool/state gate.
No separate producer pool or second charge for the same base ownership.

A published base source must not keep the mutable BatchRecord as its late-failure
routing authority: a healthy batch may settle and that record can be reused by
another owner. Routing and return must prove the exact source record/token and
its current handoff phase; stale contexts must not poison or return a replacement
batch/record. Root the real returned NativeSource before dropping producer-local
roots, not only a ledger holding its raw addresses.

Bind each reservation to its exact creation context, close it at settlement,
and capture the original catalog owner independently of mutable batch/owner
records. Return requires confirmed terminal cleanup; NativeSource not yet
constructed is an in-flight state, not proof of no debt. NativeSource attachment
requires its context to remain the exact active reservation. After reentrant
settlement/reuse before attachment, creation fails closed and independently
cleans known owners rather than letting a late source cross into replacement
slots. Published NativeSource cleanup confirms only native debt; its entry/
registry charge remains until complete entry cleanup.

The existing no-context internal API is used by native tools. It must obtain a
finite envelope from this same pool and retain it until every produced owner is
confirmed cleaned. No public user API is added. Portable old API fixtures can
use a default overload, but production must not bypass its context. Define the
direct-envelope lifetime and return contract before its first native effect.

The direct envelope uses the existing owner-record capacity (the internal
CatalogRecord also represents this non-catalog lifetime), one existing batch
and its128 source slots. A tiny internal failure sink is implemented by real
Catalog and direct lifetime; Context captures the sink object, never a mutable
owner-record lookup. Reservation failure returns only confirmed empty capacity
and rejects before source effects. After enumeration settles, CompleteBatch
returns unused slots but keeps live/uncertain source records; the direct owner
is then marked closed. Its last confirmed source cleanup returns the owner
record. Unknown live-batch debt keeps batch/source/owner; late source debt after
healthy batch settlement keeps original source/owner without affecting a reused
batch. There is no finalizer, lease timeout, independent pool or synthetic
Catalog. The explicit Catalog overload does not enter the direct wrapper.

Catalog exposes its existing bounded failure result. The direct adapter envelope
preallocates a bounded `macos_source_producer_unavailable` diagnosis before
effects; ordinary body failures leave no inner exception or native metadata at
that boundary. Original nested fatal identity still propagates unchanged.
The internal effects seam/ledger retains original failure facts for ownership
accounting, not for serialization or logging.

Capture PrepareOwner/AcquireOwner still represents a distinct retained copy;
it must not consume the base source's catalog/producer record. State gates only
select/link facts; no native effect, external callback or wait under those gates.

## Effect and cleanup rules

Allocation returning nil confirms no allocated owner for that effect. A begun
initialization consumes/changes an allocated receiver according to its explicit
native contract: nil or changed self is not permission to release the old
receiver blindly. Exceptions leave uncertainty charged. A retain exception may
have taken an extra reference without yielding its returned address; do not
release the borrowed window to guess a rollback.

The operations boundary has one fixed Objective-C init-family contract: a
nonzero receiver carries one allocation +1; normal return confirms consumption
of that ownership, and only the returned nonzero self owns a new +1. Nil has no
returned owner. A nil allocation never enters initialization. Initialization
throw confirms neither consumption nor output, regardless of a fake's known
internal result, so neither old nor guessed replacement addresses are released.
This follows [Clang init semantics](https://clang.llvm.org/docs/AutomaticReferenceCounting.html#semantics-of-init)
and [Apple initialization guidance](https://developer.apple.com/library/archive/documentation/General/Conceptual/CocoaEncyclopedia/Initialization/Initialization.html).
The installed SCStream.h declares a normal init-family method without an
ownership override (SDK header SHA-256
`11633abf2df86bd6c92345a9c4804a0746e18d4db39994ae29243671f332825b`).
Controlled nil/changed-self/throw tests do not prove these behaviors occur in
ScreenCaptureKit, or that an Objective-C exception safely crosses P/Invoke.

After successful ownership effects, construction/list/entry failures still have
the preattached token. Independently confirmed filter/window cleanup is attempted
once each, even when one fails; no exception/fatal may skip the other. Only
fully confirmed effect cleanup allows pool return. Failed records retain their
complete graph; no eviction, retry, timeout or GC observation authorizes return.

Transient resources inside the native identity/current query implementation,
enumeration content/Block/callback ownership and complete list-level settlement
remain explicitly separate; this slice must not claim those faults contained.

## Verification

One real behavior RED→GREEN at a time. Then check allocation/init nil or changed
self, fatal and independent cleanup, construction/handoff faults, fresh-pool GC,
capacity-before-effect and healthy exact-once return. Bind new frozen code and
runtime to complete D/R tests and fresh exact-new-SHA hosted inventories. Actual
healthy task-owned native execution is separate from portable fault injection.
Preserve superseded candidates and all nonzero evidence; keep MSC9/v1 open.
