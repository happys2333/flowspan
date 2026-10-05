# Initial source-producer — staged behavioral evidence

Status: first test-entry tracer is RED→GREEN. Implementation is incomplete and
uncommitted; production enumeration still enters CreateSource without a bounded
context. Same-record source/entry handoff and finite direct-call envelopes are
not implemented. No MSP acceptance, native fault containment or v1 completion.

Base checkpoint: `3aa099becb579395ee150eac65fbe4d8c7838c5c`.
Specification: `specs/v1/native-remote-window/macos-source-producer/`;
[ADR0032](../adr/0032-pre-reserved-macos-source-producer.md).
Raw snapshots/commands/cwd/exits/TRX/source and runtime inventories:
`/tmp/flowspan-source-producer-20261005/`.

Stages01–03 are compilation/analyzer correction candidates, not product REDs.
Stage04 `InitialRetainAfterEffectFaultKeepsDebt` actually runs and fails with
exit1,0Passed/1Failed. Before the failing usage assertion, it verifies the
original nested fatal, window reference count2, one confirmed-filter cleanup
and no guessed window release. After batch/catalog settlement it expects
`(Catalogs, Sources, Batches)=(1,128,1)` but observes `(0,0,0)`.

Stage05 actually exits0,1Passed/0Failed after pre-effect acquisition-ledger
linkage and marking the exact source/batch failed before context notification.
The same real creation core is used; controlled effects replace native calls.
The entire ProducerTests file is byte-identical between04/05; each stage's
saved input-before/after manifest is equal. Root actually inspects both exit
receipts/TRX and executes these comparisons, rather than inferring a result
from the stage directory name. Forced GC intentionally preserves only the
fresh real pool; effects/context/catalog observations remain weak. This proves
managed graph retention for that test entry, not native reference survival.

The implementation agent stopped on a reported usage-limit error after those
records were saved. Root preserves its partial edits and continues from current
files. Independent final review, complete D/R and new hosted gates have not run
for this producer implementation. SourceEntry/Catalog's successful `3aa099b`
CI remains a separate checkpoint and is not inherited here.

Next behaviors include original-fatal preservation when confirmed-filter
cleanup fails, independent confirmed owner cleanup, allocation/init uncertainty,
nil/changed-self semantics, same-record successful handoff, complete healthy
return, exact-context admission and finite direct-call envelopes. Content/Block/
callback/dispatch/query-internal resources and whole-list settlement remain
separate. No production sharing or platform floor change is made.
