# macOS source-entry ownership prerequisite

Status: approved v1 safety baseline; local and exact `3aa099b` hosted slice verified.
Requirements trace
to NR8/NR10 and MSC6/MSC9 ownership prerequisites; this does not close MSC9.

As a source host, I need a late catalog-binding cleanup failure to remain
observable and durably owned even after callers discard the catalog. A managed
object cycle, a raw pointer or an exception alone is not ownership retention.

- SCE1: When the last binding releases a retired SourceEntry and native cleanup
  is unconfirmed, the catalog shall record that fault before returning it,
  reject later enumeration and new source/binding admission, and preserve its
  stable bounded ordinary failure or the original nested fatal instance.
- SCE2: Before catalog-owned enumeration work, the implementation shall reserve
  independent process-bounded catalog, candidate-source and batch ownership.
  Exhaustion shall reject before the next enumeration effect. The engineering
  defaults are 8 catalog lifetimes, 1024 source obligations and 8 batch records;
  these are independent from all Capture/coordinator/tag permits. Each native
  producer batch is bounded to the existing maximum of 128 sources.
- SCE3: While any source/batch/binding obligation remains, its complete managed
  owner graph shall remain reachable through pre-reserved process ownership.
  Unconfirmed cleanup shall stay charged without guessing absence, retrying
  the effect, overwriting a prior failed batch or allocating a new fault ledger.
- SCE4: When cleanup is confirmed, the implementation shall return only the
  corresponding obligation. Catalog disposal alone shall not return records
  still held by bindings or unresolved batches. Independent known owners shall
  receive their cleanup attempts even if another cleanup fails; original fatal
  identity shall not be replaced by notification or a later cleanup exception.
- SCE5: While selecting, retiring or returning ownership, no catalog/pool state
  gate shall cross native effects, external invalidation callbacks or waits.
  Repeated/concurrent paths shall not double-link records, retry an uncertain
  native release or return a slot while a previous owner still has an obligation.
- SCE6: Before this slice closes, final Debug/Release behavior and complete
  regressions, exact identities, fault/GC/budget contracts, selected healthy
  native regression and fresh exact-commit all-OS hosted evidence shall pass.
  GC reachability and portable effects shall not be named native fault proof.

Non-goals: initial CreateSource alloc/init/retain uncertainty, ownership acquired
inside EnumerateAsync before it returns a batch, native Block/content failure,
nonzero delegate/global runtime admission, physical sharing or release acceptance.
The catalog pool cannot retroactively own an unreturned native allocation.
Delegate=0, 14.2/Arm64 candidate admission and production availability stay unchanged.
