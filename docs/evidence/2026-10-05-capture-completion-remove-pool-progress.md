# Same-Capture output-removal pool checkpoint — 2026-10-05

Status: one finite MCC task4 behavior is accepted locally. Task4, complete
MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`RemoveOutputPoolPopAfterEffectFatalPreservesConfirmedRemovalAndIndependentCleanup`
uses the actual one-argument completion with healthy construction, Start and
Stop. In the fourth pool scope, controlled `RemoveOutput` actually returns
BOOL1/error0; only that pool pop consumes its effect and throws a wrapper
containing the original OutOfMemoryException. Push/pop token, ordinal and
creating-thread records match. Cleanup observations are captured before raw
fixture teardown; teardown and outer GC precede the final ownership assertion.

Canonical `remove-pool-red02` compiles and actually fails line23. Both completion
invocations return and removal is called once, but pool unwinding erases its
successful return: barrier/queue/source cleanup remain0, only1 caller release
and primitive root free occur,1 block/root remains live, and `IsDrained` is false.
This is a behavioral RED, not a setup failure or teardown-generated observation.

Minimal GREEN changes only the API. It adds four fixed removal-scope attempted/
confirmed facts, preserves the independently confirmed BOOL/error result and
records the pop failure without replacing that result. The barrier and known
independent caller/stream/output/configuration/queue/source cleanup then execute
once. Both primitives retire with no FirstFailure and zero live blocks/roots.
The unconfirmed pool debt blocks only final shell-root/accounting return; the
full weak graph and charge remain, and Stop plus both Dispose calls preserve
the original fatal. A known pool is popped once on its creating synchronous
thread, outside gates; unknown pop is not retried.

Canonical RED02→GREEN02 uses identical complete test bytes. Single Debug/Release
each1 and focused Debug/Release each124 pass, preserving the exact123 accepted
qualified identities plus this1 Fact with no removals. All five canonical
locked restore/builds exit0 with0 warnings/errors; GREEN test exits0 with no
skipped or nonterminal results.503 selected source inputs, complete138-file
runtime inventories, raw commands and qualified TRX/DLL bindings are saved.
Their complete receipts verify. No new project/quality/full-solution/native or
hosted gate was run; last actually executed project Debug/Release remain390.

Historical `remove-pool-red01` / `remove-pool-green01` are preserved actual
RED/GREEN, not the canonical pair. Their test differs only in two tuple field
labels corrected from `InvocationEntries` to `PhysicalCopies`; assertions and
behavior are unchanged. The replay explicitly classifies these superseded runs
and verifies the label-only transition rather than treating them as canonical.

- Canonical test SHA256: `fa7a15c12e6248bd3d6c93d498e268d3a3622be011ef9d99f23dcf4b6a894aa0`.
- GREEN API.

  SHA-256: `7a67895a1ef75183f615bb6299688bc177a79ec54f518784ea6fa698c662cd16`.

- Auditor: `b7d099b542cc57c10f94c14de6863c4363378b233b6bc3f33a5d7fb137c101d9`.
- Replay: `f69b8ca94b208fb0a2e96f67fc0a6c73dd51ed20aa7994435b3b86387ae96f8e`.
- Saved report: `49bab7204747dadc29c6ed028b945b08370f6c7b2438a3dae0dfaed2a74ed81b`.
- Selected-current report: `4fa949ddec9494ec52a33588b8ed79f1ae294916338481ee98944c50fef5bc57`.

Standards:0 concrete findings. Spec:0 concrete findings. Root accepts this
finite behavior after actually executing `remove-pool-root-saved01` and
`remove-pool-root-current01`: both raw exits0, empty violations and reports
byte-identical to their worker counterparts. Both replay receipts also verify.
Replay reads existing evidence; it executes no tests. Selected-current equality
covers503 inputs at the recorded freeze, not the whole tree, and becomes
historical after later source edits.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/remove-pool-replay.sh remove-pool-next-saved01 saved
```

Use a fresh label. This healthy-body/pop-after-effect result does not accept
combined body/pop faults, zero/unknown pool acquisition, observer/resource-use
joins, races/reentry or outer-drain fallback. It is portable controlled-effect
evidence, not native-fault containment, global Capture admission, production
sharing or complete task4/MCC/v1 acceptance.
