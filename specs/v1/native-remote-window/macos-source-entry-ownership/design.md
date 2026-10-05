# macOS source-entry ownership design

## Small implementation boundary

Keep the real SourceCatalog, SourceEntry, NativeBinding and NativeSource.
Add only a thin internal factory returning the real native source for tests;
inject the existing retain/release/current-check effects, not a second source
state machine. An API fixture supplies bounded source lists and records
enumeration calls. No new native library, production dependency or UI is needed.

The first tracer is `LastBindingReleaseFaultClosesCatalogBeforeNextEnumeration`:
publish one real source, hold its binding, refresh to retire the entry, consume
filter release and throw at the last binding's cleanup, then attempt Refresh.
The failure must be recorded by the catalog before any further enumeration.
Save the actual baseline assertion failure before modifying owner behavior.

## Entry failure transfer

An entry carries its already allocated catalog cleanup owner/record. Its final
Release catches cleanup failure, marks the catalog closed, preserves the first
fatal, links the exact entry once and rethrows the native-source diagnosis.
RetireEntry uses that same transfer and must not duplicate a retained link.
The notification is state-only and allocation-free after pre-reservation; it
does not execute native calls or user invalidation handlers.

Enumeration admission linearizes at the final closed-state check after
permission/reservation work, immediately before the effect. Earlier preflight
checks are not admission. An enumeration already admitted before a concurrent
fault keeps its reserved batch through settlement; closure cannot discard its
returned owners. Publication and new binding acquisition recheck closed state,
so that admitted work cannot publish fresh authority after the fault.

Lease and entry cleanup are independent. Retire must not let registration
failure skip native cleanup or replace an earlier fatal; binding cleanup must
not let a lease failure skip its entry obligation. New catalog acquisition is
denied after a cleanup fault. This is not proof of the later global admission
closure for all existing Captures.

## Separate bounded process ownership

Use a fixed process-static pool, not GCHandle allocation after a failure or
the delegate coordinator's 16 records. Engineering defaults: 8 catalog owners,
1024 source obligations, 8 batch records. Portable tests can inject a fresh
small pool executing the same algorithm; production quarantine is never reset.
Reserve before enumeration, including candidate capacity for its maximum-128
producer result. Existing/retiring/failed entries keep their charges while a
new candidate batch is reserved. Saturation cannot evict a healthy obligation.

Root the complete cleanup owner and transfer existing reserved obligations from
batch to entry exactly once. Batch Count/index/group/registration failures must
retain the original list and reserved record; a single replaceable instance
field cannot be the sole durable root. Do not copy resources into an unbounded
general ledger. The real producer's 128 bound limits each retained batch; a
catalog record count alone does not prove an arbitrary injected object graph
has bounded bytes.

Healthy final source cleanup returns its record. DisposeAsync may finish while
a borrowed binding still owns an entry; the catalog lifetime stays charged
until that binding cleans up and every batch/source obligation is confirmed.
Failed cleanup stays process-rooted and charged. No timeout, GC observation,
cached disposal result or empty active dictionary authorizes release.

## Verification and scope

Test through public catalog refresh/acquisition plus the actual native binding.
Use real NativeSource with controlled external release effects for ordinary and
nested-fatal last-binding failures, independent cleanup and no retry. NoInlining
helpers discard all caller/API/list/binding/task references before forced GC;
the fixture ledger must not strongly root the graph being tested.

Then test failed entry/batch graphs, healthy disposal with held binding, final
healthy record return, two catalogs sharing a capacity-two pool, rejection before
enumeration, stable repeated failure and candidate/registration cleanup faults.
Add cases one behavior at a time, preserving actual RED/GREEN and direct GREEN.
Bind final source/runtime/command/exit/TRX inventories, complete solution D/R,
ordinary repeat tests, native healthy checks and exact-new-SHA hosted evidence.

Initial native CreateSource/content/callback acquisition cannot be repaired by
the catalog after the producer loses that owner. Those are subsequent entry
slices, as are nonzero delegate, global fault delivery, retirement and complete
Capture permit return. See [ADR 0031](../../../../docs/adr/0031-bounded-macos-source-entry-ownership.md).
