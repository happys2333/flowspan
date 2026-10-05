# Same-Capture Stop zero-pool refusal checkpoint — 2026-10-05

Status: finite Stop refusal/retention is accepted locally, not complete cleanup.
Task4, complete MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`StopZeroPoolPushRejectsNativeBodyAndRetainsUnconfirmedCapture` is actual
direct-GREEN under the unchanged shared guard from the
[Start zero checkpoint](2026-10-05-capture-completion-start-zero-pool-progress.md).
No new RED or production repair is manufactured. Constructor and native Start
are healthy; actual ordinal3 Stop pool push returns token0. Stop selector,
native invocation and callback do not execute. Stop returns false without an
outward exception; both Dispose attempts report ordinary InvalidOperationException.
No fatal is injected and no zero-token pop or unknown-effect retry is guessed.

Local Stop issued/returned/result-completed/exit-completed remain false and
its result remains unset. No callback, completion notification, native handoff
return or primitive retirement is invented. The confirmed Start caller releases
once and retires normally. Stop's real caller has already acquired its root/
copy but is unissued and actually remains unreleased: this known caller-cleanup
debt is explicitly reserved for a subsequent independent tracer, not accepted
as complete cleanup by this refusal Fact.

Removal/barrier/object/queue/source release effects remain0 and `IsDrained`
stays false. Before raw teardown, one Stop block/root remains live and Stop
caller-release/native-retirement/managed-drain/root-free facts are unconfirmed.
Raw runtime teardown removes only fixture allocations; production release/free
counters remain1 and no Stop result/exit/retirement is fabricated. Outer GC
still preserves the complete original two-completion/primitive/shell/operations/
source/callback-marker graph and `before+1` charge. Valid constructor/Start
pool token/thread pairs match; zero Stop pool has no matching pop.

Only the new Fact/helper prefix is added. Previous tests, system fixture,
production and controlled-runtime implementation bytes remain unchanged.
Single `stop-zero-pool-direct-debug01` / `stop-zero-pool-direct-release01` each
pass1; focused Debug/Release each130 preserve the exact129 accepted qualified
identities plus this1 with no removals. All four locked restore/build/test
stages exit0 with0 warnings/errors and no skipped or nonterminal results.503
selected inputs, complete138-file runtime inventories, raw commands and
qualified TRX/DLL bindings are saved; receipts verify. No new project/quality/
full-solution/native or hosted gate was run; last actual project D/R remain390.

- Test SHA256: `7c3af83936fe7cce1d404069e14a11ed5c6e85b23047fba986314b5d83286b0f`.
- Unchanged API.

  SHA-256: `563a3f591ce2b62121ef4bf44939c71fe7222a198c27245e0299bfd66c51f117`.

- Auditor: `bc53d7a8e2bdc436a11efc642cb17a18decb932dff641c775c58b102e3cd7bcb`.
- Replay: `704833fcc6929abedfbef68fedee9bf8990bd2e4a553d4b0529f1d114e2fff9f`.
- Saved report: `f576a36def819c580c9312f6dba05e948ec98ef26da8cd6a676086faafcd3048`.
- Selected-current report: `e302449a8dd2a5ea0b38bb5d8547fcd5041bf83889b7f20d721a16bef07da63d`.

Standards:0 concrete findings. Spec:0 concrete findings. Root actually executes
`stop-zero-pool-root-saved01` / `stop-zero-pool-root-current01`: both raw exits0,
empty violations, verified receipts and reports byte-identical to their worker
counterparts. Replay executes no tests. Selected-current equality covers503
inputs at that freeze, not the whole tree, and becomes historical after edits.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/stop-zero-pool-replay.sh stop-zero-pool-next-saved01 saved
```

Use a fresh label. Unissued Stop caller cleanup, unknown push/removal outcomes,
observers/resource-use joins, races and outer-drain fallback remain open. This
is portable controlled-effect evidence, not native-fault containment, global
Capture admission, production sharing or complete task4/MCC/v1 acceptance.
