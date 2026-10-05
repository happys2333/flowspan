# macOS enumeration-producer design

## One production orchestration

Extract external effects only from EnumerateCoreAsync: prompt-free access/app
checks, completion construction/dispatch, autorelease push/pop, content
retain/release, window collection/count/index, and the existing source creation
effects. Production and controlled-effect tests invoke this same core with an
explicit real creation context. Direct internal tools keep the same bounded
direct envelope. Do not fake EnumerateAsync, Catalog, NativeSource or the pool.

The completion interface initially exposes Pointer/IsReleased/FirstFailure
and Dispose through the real Block wrapper. These describe its owned reference,
not all native copies or callback drain. No positive native drain is inferred
from that provisional interface; its proof is a later required task in this slice.

## First tracer and reservation

`EnumerationContentRetainAfterEffectFaultKeepsBatchDebt`: reserve a fresh real
catalog/batch/context, execute actual orchestration with controlled effects,
deliver nonzero content, increment its native reference then throw a specified
nested OOM. After callback exit and settlement, verify the original fatal, no
guessed content release/retry and the original charge. Discard caller/API/task/
fixture strong roots, force GC and prove the complete graph remains through
the actual pool. Save a seam-only actual behavior RED before repairing.

Attach one operation ledger to the existing BatchRecord before Block/dispatch
or any owned effect. The ledger contains the exact context and effects object,
borrowed content and attempted/confirmed acquisitions, independent cleanup
facts, callback state and original failure. Allocate callbacks/tasks and other
managed graph components before the effects they protect. Existing defaults
remain8 owners/1024 source records/8 maximum-128 batches; the ledger is not a
new pool. Failed operations retain their original batch and its reserved slots.

Pool methods prove exact ledger and context identity before state mutation.
CompleteBatch must not detach an in-flight or unconfirmed enumeration ledger,
including during reentrant settlement before NativeSource exists. Failure
notification captures the original stable sink, not a mutable batch owner
lookup. No native operation, fault observer, wait or user callback runs under
pool/state gates. Failure closes the producing context before outward escape;
global Capture admission is a separate future composition.

## Content and callback admission

Record the first callback admission and borrowed content before attempting
retain. Normal nonzero exact retain confirms its +1; nil/foreign returns do not
authorize publication or guessed rollback. A throw leaves uncertainty even if
a fixture knows its internal outcome. Repeated/late callbacks must reject
before another retain; do not retain-and-release every duplicate callback.

Track invocation entry/exit independently from result completion. Join admitted
managed invocations without holding gates or self-waiting through active
callback ancestry. One completed notification is not an all-callback join and
still occurs before return across the ABI. Retained content is not released
until all operations that may use it have stopped. Immutable old operation
callbacks retain their own ledger; reused BatchRecords are never their authority.

## Completion, dispatch and primitive Block lifetime

Root completion acquisition before native Block copy. A wrapper which only
assigns after MacOSRemoteWindowBlock.Create returns cannot contain its internal
partial allocation/copy/root debt. Required task4 must stage or otherwise prove
the actual primitive owner before those effects, using the same batch graph.
Unknown effects cannot rely on a finalizer or repeated Dispose to guess cleanup.
Preserve existing Capture callers' contract until explicitly migrated.

The required staged contract is Prepare → attach inert primitive owner to the
same batch graph → AcquireCopy. Prepare creates the managed shell/state without
an owned native copy or GC root; AcquireCopy records root/copy attempts before
their effects. A copy/root failure before the old Create would return still has
that staged owner. Initial content-only work may use the old factory seam but
must label this primitive debt unresolved.

Dispatch may copy the Block and throw before returning. That retains unknown
dispatch/native-copy debt; it is not permission to release/reuse the batch.
Autorelease pool push/pop are independent attempted/confirmed effects. Cleanup
must respect thread affinity rather than transferring a native pool across await.
An owned Block release confirmation and actual native capture/context retirement
need separate observations. Completion/Block native-copy retirement must be
proved by a suitable actual primitive/ABI contract, not by a fake IsReleased.
The candidate observation is the real BlockState's last physical capture
dispose/root-release event, independently paired with all admitted managed
invocation exits. Verify with an actual extra native copy: releasing only the
caller's reference cannot return the batch; last physical-copy retirement plus
managed exit and confirmed other cleanup can. Copy counts alone are not proof.
If retirement cannot be established, retain charge and report the named
unavailable boundary; timeout or GC cannot manufacture healthy-return proof.

Release the caller-owned Block +1 before awaiting retirement; holding it while
waiting for its own last-capture retirement would self-lock. A heap Block's extra
native copy can increment native reference count without invoking CopyCapture,
so actual last release and confirmed GCHandle.Free, not helper call counts or
handle==0 alone, must publish the monotonic retirement observation. Keep this
staged no-retry/no-finalizer path opt-in until existing Capture callers migrate.

## Settlement and diagnostics

On failure, independently select and execute each confirmed source/content/
completion/pool cleanup at most once. Preserve the earliest nested fatal in
preference to later ordinary/fatal cleanup faults; never let foreach's first
failure skip remaining known sources. Any unknown effect keeps the batch charge
and full graph with no timeout, eviction, retry or GC-triggered return.

Successful enumeration still creates sources using CreateSourceCore and original
source slots. Its ledger may settle only with complete enumeration-owner and
lifetime proof. Live source/entry charges transfer exactly as in ADR0032; empty
result alone does not prove enumeration cleanup. Preallocate an ordinary bounded
diagnosis before ownership effects; raw native pointers/window metadata are not
serialized or logged. Source and enumeration failure facts remain internal.

## Evidence

One behavior tracer at a time. Save raw commands/cwd/exits/TRX and immutable
source/runtime snapshots under a new `/tmp/flowspan-enumeration-producer-20261005/`.
Retain failures and superseded candidates. Cover unknown and confirmed content,
duplicate/overlap/late callbacks, reentrant settlement/reuse, dispatch and pool
faults, each independent cleanup and native Block factory/drain separately.
Then bind final D/R inventories, actual healthy native regression and fresh
exact-new-commit downloaded hosted artifacts. No content-only progress closes
the complete MEP acceptance or changes production sharing availability.
