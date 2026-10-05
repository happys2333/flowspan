# Same-Capture constructor zero-pool checkpoint — 2026-10-05

Status: one finite MCC task4 acquisition outcome is accepted locally. Task4,
complete MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`ConstructorZeroPoolPushRejectsScopedBodyAndRetainsShellBeyondFactorySlotReplacement`
returns an actual zero token from constructor pool push. No fatal is injected.
The test performs real safe Stop and both Dispose calls even if incorrect
construction succeeds, then genuinely replaces the factory failure slot and
executes outer GC before its refusal/effect/weak-graph assertions.

Compiled `constructor-zero-pool-red01` actually fails line24: expected
factory-return/body/push/pop/configure/object-release/queue-release/removal/
barrier/source-release tuple is `(false,0,1,0,0,0,0,0,0,1)`; actual is
`(true,7,2,1,1,3,1,1,1,1)`. The zero token still permits all seven scoped body
effects and bad factory success. RED push2/pop1 includes its real healthy
removal teardown, not an erroneous ordinal/equality assertion that aborts cleanup.

Minimal GREEN changes only the API with a four-line constructor guard:
immediately reject unconfirmed zero acquisition with ordinary
InvalidOperationException before allocation/configuration/native body. The
known source retain releases once. Target push is exactly ordinal1/token0 on
the creator thread; pop effects and attempts are0, scoped body is0, and all
completion allocation/copy/release/root-free/invocation effects are0.
Factory and Dispose failures are ordinary InvalidOperationException; repeated
Dispose preserves the first Dispose failure instance. No guessed pop or retry
is attempted, and unknown acquisition does not authorize shell/count return.

The failed shell, operations/source and callback markers remain alive after
the actual factory-slot replacement and GC. Retained count is `before+2`:
the target unknown-pool shell plus the separate replacement shell's existing
unknown cleanup debt, not two charges attributed to the zero-pool target.

RED01→GREEN01 uses identical complete test bytes. Single Debug/Release each1
and focused Debug/Release each128 pass, preserving the exact127 accepted
qualified identities plus this1 Fact with no removals. All five locked restore/
build stages exit0 with0 warnings/errors; GREEN test exits0 with no skipped or
nonterminal results.503 selected inputs, complete138-file runtime inventories,
raw commands and qualified TRX/DLL bindings are saved; receipts verify.
No new project/quality/full-solution/native or hosted gate was run. The last
actually executed project Debug/Release remain390.

- Test SHA256: `39b7be972c0527b66e9d2cba74860922db30ddb7156a81f654302e7f596a1b21`.
- GREEN API.

  SHA-256: `c2312a7db65c34d41dfc0b4af0cc804274a0b85854234bf8bcaaf4f063dae243`.

- Auditor: `7ef1a1f45dcb076f0565ecbe2ffd3ccaacaaf43f7cab7a2038ef9747894b6414`.
- Replay: `54e44557683b1623928dad528158c2e33292af64245a8e3c80bfcf801dcb574c`.
- Saved report: `f4339f60a479d3e92784de96cdcd10abc2be4de65e58345377b7263102097bb4`.
- Selected-current report: `a29ea3d33de01d6792598c1b3ca847b9c58732a46378807c9af925cb654f7c60`.

Standards:0 concrete findings. Spec:0 concrete findings. Root accepts this
finite behavior after actually executing `constructor-zero-pool-root-saved01`
and `constructor-zero-pool-root-current01`: both raw exits0, empty violations
and reports byte-identical to their worker counterparts. Both replay receipts
verify. Replay executes no tests. Selected-current equality covers503 inputs
at the recorded freeze, not the whole tree, and becomes historical after edits.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/constructor-zero-pool-replay.sh constructor-zero-pool-next-saved01 saved
```

Use a fresh label. The other three scope guards, unknown push outcomes,
Stop-unissued behavior, observers/resource-use joins, races and outer-drain
fallback remain open. This is portable controlled-effect evidence, not native-
fault containment, global Capture admission, production sharing or complete
task4/MCC/v1 acceptance.
