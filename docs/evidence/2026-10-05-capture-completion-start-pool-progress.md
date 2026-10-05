# Same-Capture Start pool checkpoint — 2026-10-05

Status: one additional finite MCC task4 behavior is accepted locally. Complete
task4/MCC, native and hosted gates remain open. HEAD is still
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`StartPoolPopAfterEffectFatalRetainsCaptureBeyondConfirmedNativeCleanup` uses
the actual one-argument completion. Construction is healthy; Start callback and
native invocation return normally, then only ordinal2 pool pop consumes its
effect and throws a wrapper containing the original fatal. Actual Stop,
output-removal and queue barrier remain healthy. Both caller references and
stream/output/configuration/queue/source clean up once; both primitives retire
normally. Repeated Dispose, raw fixture teardown and outer GC precede the final
weak-graph/charge/fatal assertion. Per-ordinal/token/thread push/pop records match.

`start-pool-red01` is a preserved compiler/setup failure, not behavioral RED.
Compiled `start-pool-red02` actually fails line23: expected count1 and12 true
facts; actual count0,9 weak-graph facts false, Stop original-fatal identity true,
and both Dispose original-fatal facts false. The prior code returns the shell
root despite unconfirmed Start pool ownership.

Minimal GREEN adds only four fixed Start scope attempted/confirmed facts and
its final root-return gate. Push/pop effects stay outside gates, a known pool
gets one pop on its creating synchronous thread, and unknown pop is not retried.
Known independent cleanup still runs before the gate; both primitive lifetimes
alone cannot authorize shell/accounting return while this pool debt remains.
Other pool scopes, combined body/pop failures, observers and resource-use joins
are not repaired or accepted by this checkpoint.

Identical complete test bytes pass single Debug/Release each1 and focused
Debug/Release each122, exactly the accepted121 identities plus this1 Fact.
Locked restore/build/test exits0 with0 warnings/errors.503 selected inputs and
complete runtime inventories are bound. No new project/full-solution/quality or
native gate was run for this behavior; those final aggregate gates remain required.

- Test SHA256: `20502d76030e4907490419827c56c547079f4548e1a6e6729b58a4a37050d213`.
- GREEN API.

  SHA-256: `a35d570afd732d8b8d9f06e90e2dd8cdc2f0f1c3566491e0a082cb091234928f`.

- Source503 manifest: `7cd8eb0d6575f7731e2ffe3fb05cb138f31534143f109df44614a22db2b24121`.
- Single D/R runtime manifests: `870ee5c034c43f001b8af2ed895254a4c24910502242327c88f62e8de93288ad` / `68f017e9e19d14ccacdf08ca672ab2c0195ca772937ff7bd1b7a48d931efca14`.
- Auditor: `e9ce112bb4ef3a09a6699a8cbfb34b0b5c0782dd3087eb9f5ab1935a55dec1a2`.
- Replay: `e19873bcfb91177e1200292026800492304d7c0e81375717de614329a8e373c7`.
- Saved report: `6b93e2d7a3599f0cf47050cbd4c1c4fe61d05c2463eedff5a2a7f7d91b5d484b`.
- Selected-current report: `f97a532b2b8f189b0d6c6db91a10b5f9a4766976745d8d3ad07f07578ab463b2`.

Standards:0 concrete findings. Spec:0 concrete findings. Root actually executes
saved/current frozen replay and compares each worker report: all exits0, empty
violations and byte-identical reports. Replay checks complete receipts, source/
runtime files, raw commands and qualified TRX/DLL bindings; it does not execute
tests. Current equality covers only the503 selected inputs and becomes historical
after subsequent edits.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/start-pool-replay.sh start-pool-next-saved01 saved
```

This is portable controlled-effect evidence, not native pool-fault containment,
process-wide Capture admission, production sharing, complete MCC/MSC or v1.
Stop and output-removal pool behaviors are the next sequential tracers.
