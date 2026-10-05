# Same-Capture output-removal zero-pool checkpoint — 2026-10-05

Status: one finite MCC task4 acquisition outcome is accepted locally. Task4,
complete MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`RemoveOutputZeroPoolPushRejectsNativeBodyAndRetainsDependentOwners` has
healthy construction and actual Start/Stop callbacks. Ordinal4 removal pool
push returns token0 without a fatal injection. Both Dispose attempts and raw
runtime teardown precede outer GC and the refusal/owner assertions.

Compiled `remove-zero-pool-red01` actually fails line23. Expected invocation
tuple is `(true,false,1,1,2,1,1,2,2,0)`; actual is
`(true,true,1,1,2,1,1,2,2,1)`. The zero token incorrectly permits RemoveOutput1
and successful Stop/drain return despite missing valid removal scope. This is
behavioral RED, not a setup failure or premature teardown assertion.

Minimal GREEN changes only `RemoveNativeOutput` with a four-line ordinary
InvalidOperationException guard before its body. RemoveOutput remains0; Stop
returns false without an outward exception, and both Dispose calls report
ordinary InvalidOperationException. Zero token is never popped or retried.
The three valid pool scopes each pop once on their creating threads; their
token/thread pairs match independently of push/pop ordinals.

Confirmed Start and Stop callers each release/free once, with both primitives
normally retired, no FirstFailure and zero live roots/blocks before raw teardown.
Removal is unknown: barrier/object/queue/source release effects stay0 and
`IsDrained=false`. No successful BOOL/error, physical drain, dependent native
cleanup or shell-root/accounting return is invented. Complete original shell/
two-completion/primitive/operations/source/callback-marker graph and `before+1`
charge remain after teardown and GC. This is not full cleanup acceptance.

RED01→GREEN01 uses identical complete test bytes. Single Debug/Release each1
and focused Debug/Release each131 pass, preserving the exact130 accepted
qualified identities plus this1 Fact with no removals. Previous tests/helpers/
system fixture bytes are preserved. All five locked restore/builds exit0 with0
warnings/errors; GREEN test exits0 with no skipped or nonterminal results.503
selected inputs, complete138-file runtime inventories, raw commands and
qualified TRX/DLL bindings are saved; receipts verify. No new project/quality/
full-solution/native or hosted gate was run; last actual project D/R remain390.

- Test SHA256: `afa1f611decf398430c55a354424c4a53c10f924a95583277901485821f9763e`.
- GREEN API.

  SHA-256: `122ef6529f39c175594352195dfa129adc2ae55f59cb492cc813fca96eb9a3c4`.

- Auditor: `e66c962374253c2d5f4e67168a204b0ed1711b8d5a63b5134b2be978842d1a94`.
- Replay: `0ede3cff0005f4ab063480cd678ad8286d3f4abd90084370d8dd0a5847c05ae8`.
- Saved report: `64b3655833e621d4ccb4215b93bfe8191752cfb5ba7e4cc388386def5e3e64c1`.
- Selected-current report: `ba55938f5184b132bf47891c06423b95017a6fb71f42ef7ad4453eee15c9b087`.

Standards:0 concrete findings. Spec:0 concrete findings. Root actually executes
`remove-zero-pool-root-saved01` / `remove-zero-pool-root-current01`: both raw
exits0, empty violations, verified receipts and reports byte-identical to their
worker counterparts. Replay executes no tests. Selected-current equality
covers503 inputs at that freeze, not the whole tree, and becomes historical
after edits. Future batches do not alter these frozen saved evidence bundles.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/remove-zero-pool-replay.sh remove-zero-pool-next-saved01 saved
```

Use a fresh label. Unknown push outcomes, unissued Stop caller cleanup,
observers/resource-use joins, races and outer-drain fallback remain open. This
is portable controlled-effect evidence, not native-fault containment, global
Capture admission, production sharing or complete task4/MCC/v1 acceptance.
