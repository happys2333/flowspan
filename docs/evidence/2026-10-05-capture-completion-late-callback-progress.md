# Same-Capture pending-to-late-callback pair — 2026-10-05

Status: these two finite MCC task4 invocation paths are accepted locally.
Task4's observers, task5 joins/races, complete MCC/MSC, native and hosted gates
remain open. Recorded base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a
working-tree overlay. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

The two Facts are
`StartInvocationFatalWithoutCallbackWaitsForRealLateCallbackAndRetiresNativeHeldCopy`
and `StopInvocationFatalWithoutCallbackWaitsForRealLateCallbackAndRetiresNativeHeldCopy`.
Both are actual direct GREEN with unchanged production code and no new RED or
repair. The controlled invocation takes a real extra heap retain of the actual
one-argument completion before throwing a wrapped original OutOfMemoryException,
without invoking a callback. This is not merely a saved borrowed address.
The native-held copy is released only by the independent Stream release effect.

An external100ms watchdog observes pending drain; it does not manufacture result,
callback exit, cleanup or ownership return. Two actual pending Dispose attempts
preserve the original fatal. No removal/barrier/dependent native/source cleanup
occurs. Pending Start releases no caller; pending Stop releases only its already
confirmed completed Start caller. The target native-held copy remains live and
shell charge is `before+1`. Stop's read-only production-gate observation confirms
actual failure selection/pool unwind, not a synthesized callback fact.

| Target scope | Before real late callback | After real late callback |
| --- | --- | --- |
| Start | Issued=true, returned=false, settled=false; local result faulted with original fatal; exit unset | Settled/exit become true, but local result remains faulted with the same original fatal and returned stays false |
| Stop | Issued=true, returned=false, settled=false; result pending and exit unset | Actual callback settles result=true and exit/settled=true; returned remains false |

The test invokes the actual retained pointer through the real typed ABI on a
real thread and finally joins that thread, independently observing ABI return
before test teardown. A completed notification is not that join. Start's late
successful callback does not overwrite its earlier faulted local result or
retroactively invent a returned handoff. Stop's result/exit are set only by its
real late callback. Neither throwing invocation ever gains a returned fact.

After the late callback, actual Stop/removal/barrier and independent caller/
stream/output/configuration/queue/source cleanup complete. Both known callers
release once and Stream release retires the extra heap retain. Final observed
totals are2 roots,3 actual copies/retains,3 releases,2 root frees and zero live
roots/blocks; both primitives confirm native retirement and terminal managed
drain with no FirstFailure. This path does not wait for stream-held Block
retirement before releasing Stream. Confirmed complete lifetime/owner facts
permit shell/count return to `before`, which remains so after raw teardown/GC;
permanent retention would not be the accepted outcome.

Original fatal remains observable from Start's initial failure, pending Dispose
and final drain, or Stop's pending Dispose/final drain. Final Dispose is allowed
to return or throw that same fatal, never a replacement. Pending observations
are same-helper snapshots while Capture is strongly held: they do not prove a
pending cross-boundary weak-graph/GC retention scenario. Final count/retirement
proof is not a general pre-release action/failure/completed resource-use join.

Each Start/Stop single Debug/Release passes1; Start is verified before Stop is
added. Final focused D/R138 preserves exact136 accepted qualified identities
plus these2 with no removals. Existing effect defaults are preserved; shared
fixture changes are limited to opt-in retain-before-callback faults, ready
observation and stream-held copy release. All six locked restore/build/test stages exit0 with0
warnings/errors and no skipped/nonterminal results.503 selected inputs,
complete138-file runtime inventories, raw commands and qualified TRX/DLLs are
bound; receipts verify. Last actual project D/R remains390; no new project/
quality/full-solution/native or hosted gate was run.

- Start-only test SHA256: `ae28c82bddcd69a19703f8c8fd542a08f675068fe25d6e42c47651ba081fede9`.
- Final pair test: `1ff463f88341b57d1b91e2246c955ce8bbbd7fde876cc837ad30ab83d0ce9371`.
- Unchanged API.

  SHA-256: `7de3e8fe82b010bd6c74f561004dc6131ef20047959c24e43b8dfff5fea01eda`.

- Auditor: `8f900fff0655c053f6dd3359f7ef6c1fb50c45e16ed217cae807696ab40952c7`.
- Replay: `8ee946b68308bf186d6eea783334b016a2108fc930144619b7b3f1e22067677e`.
- Saved report: `5b87f5833d936460389d5e01797bcd086f74a8000aa66d9b29c56e63b5b7ba3e`.
- Selected-current report: `6d22a0bd168a2c1589bc6b7a53c76fe6c7ad6c00e4361f0ea069e60f29a063b5`.

Standards:0 concrete findings. Spec:0 concrete findings. Root actually executes
`late-callback-pair-root-saved01` / `late-callback-pair-root-current01`: both
raw exits0, empty violations, verified receipts and reports byte-identical to
their worker counterparts. Replay executes no tests. Selected-current equality
covers503 inputs at that freeze, not the whole tree or a pristine commit, and
becomes historical after edits. Frozen saved evidence is not rewritten.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/late-callback-pair-replay.sh late-callback-pair-next-saved01 saved
```

Use a fresh label. Observer/resource-use closure and joins, duplicates/races,
outer-drain fallback and aggregate/full/native/exact-SHA hosted gates remain
open. Portable controlled-effect evidence is not native-fault containment,
global Capture admission, production sharing or complete task4/MCC/v1 acceptance.
