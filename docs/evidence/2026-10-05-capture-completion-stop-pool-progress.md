# Same-Capture Stop pool checkpoint — 2026-10-05

Status: one additional finite MCC task4 behavior is accepted locally. Complete
task4/MCC, native and hosted gates remain open. The recorded base is
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`StopPoolPopAfterEffectFatalRetainsCaptureBeyondConfirmedNativeCleanup` uses
the actual one-argument completion. Construction and Start are healthy; the
Stop callback and native invocation return normally, then only ordinal3 pool
pop consumes its effect and throws an AggregateException/IOException wrapper
containing the original OutOfMemoryException. Actual output removal and queue
barrier remain healthy. Both caller references and stream/output/configuration/
queue/source clean up once; both primitives retire normally with no primitive
FirstFailure. Per-ordinal/token/creating-thread push/pop records match. Both
Dispose calls and raw fixture teardown precede outer GC and the final weak-graph/
charge/fatal assertion.

Compiled `stop-pool-red01` actually fails line23: expected count1 and12 true
facts; actual count0,9 weak-graph facts false, Stop original-fatal identity true,
and both Dispose original-fatal facts false. All earlier assertions already
confirm independent cleanup, retired primitive roots and zero live blocks/roots.
The prior code nevertheless returns the shell root despite unconfirmed Stop
pool ownership, losing the otherwise weak graph and repeated-Dispose fatal.

Minimal GREEN changes only the API: four fixed Stop scope attempted/confirmed
facts and its final shell-root/accounting return gate. Push/pop effects stay
outside gates; a known nonzero pool gets one pop on its creating synchronous
thread, and unknown pop is not retried. Known independent cleanup still runs
before the gate. Confirmed native/primitive retirement and `IsDrained` do not
authorize shell/count return while this Stop pool debt remains. Removal scope,
combined body/pop failures, zero/unknown acquisition, observers, resource-use
joins, races/reentry and outer-drain fallback are not repaired or accepted here.

Identical complete test bytes pass single Debug/Release each1 and focused
Debug/Release each123, exactly the accepted122 qualified identities plus this1
Fact with no removals. The five recorded stages are `stop-pool-red01`,
`stop-pool-green01`, `stop-pool-green-release01`, `focused-debug-stop-pool01`
and `focused-release-stop-pool01`. Locked restore/build exits0 with0 compiler
warnings/errors; GREEN test exits0 with no skipped or nonterminal results.503
selected source inputs and complete138-file runtime inventories are bound to
qualified TRX identities and actual test DLLs. All five stage receipts verify.
No new project/full-solution/quality or native gate was run for this behavior.
The last actually executed project suites remain constructor Debug/Release390;
392 is not an executed result. Final aggregate gates remain required.

- Test SHA256: `ffd66dbdc5dcc0abce3c4405293eee11c583fbdd4e37e385b8ebd8dd7114fc93`.
- GREEN API.

  SHA-256: `89e5c8302588f1e3901fa66e5e0d9343433931ce3b7348d11cf1dbb44804c374`.

- GREEN source503 manifest: `95475483aec92619b91c42c1e8fe11cde726af3a1e2ae520c5729f47d5a63686`.
- Single D/R runtime manifests: `23c606fb6f20f46f22d5d564e8ae65363dd3fd4fbe4927c9d0d31c51c0989c86` / `e48b1b58fbe07a961feb9fe1140978831a428ce8fa5e660b52f530de3a2f34a8`.
- Focused D/R runtime manifests: `43c5d4d6ccd52495819c43b2d4f1ccf1d8b021088463da749203ed82840fbc4f` / `7a73f7451d32e180538d29162c739761495c4973ff118212cb4ce4678969e82b`.
- Auditor: `239aa7a55bcc7dc85a07ae0c63f6c34ee1ce3a2cbb81deae5ccdaec3714b0af4`.
- Replay: `ca62018e0a78c82a2c45c1d256b3886130f08bd5285db653cd9218403d57acb9`.
- Saved report: `19d0072330afe37dd8c52e280a6b18cb6e3480daaebbab5d39cf9237d33b0732`.
- Selected-current report: `f3ad44d15c674c7264bce6bb2a69298d2f8e189c337a26dbb14629cf5d05275e`.

Root acceptance records Standards:0 concrete findings and Spec:0 concrete
findings. Root actually executes saved/current frozen replay under
`stop-pool-root-saved01` / `stop-pool-root-current01`: both raw exits0, empty
violations and byte-identical worker reports. Their receipt hashes also verify.
Replay checks complete receipts, source/runtime files, raw commands and
qualified TRX/DLL bindings; it does not execute tests. Selected-current equality
covered only503 inputs at that recorded freeze and becomes historical after
subsequent edits; it is not whole-tree equality or a current-source test run.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/stop-pool-replay.sh stop-pool-next-saved01 saved
```

Use a fresh label; existing replay labels are not overwritten. This is portable
controlled-effect evidence, not native pool-fault containment, process-wide
Capture admission, production sharing, complete MCC/MSC or v1. Output-removal
pool behavior is the next sequential tracer; task4 stays incomplete.
