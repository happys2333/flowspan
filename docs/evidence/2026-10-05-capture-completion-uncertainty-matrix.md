# Same-Capture completion uncertainty matrix — 2026-10-05

Status: MCC task3's finite ownership coverage is accepted locally. Tasks4–8,
complete MCC/MSC, production sharing and v1 remain open. Base HEAD is
`1ab251135f3617bc18e39d5f67e38885b53e85a7`; these are working-tree snapshots,
not pristine committed or hosted builds.

Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`, with the
first Start allocation row in
`/tmp/flowspan-capture-completion-allocation-20261005/`.

## Behavior and execution

Eight scenarios were added and actually executed one at a time: Start/Stop
root-allocation after effect, Start/Stop zero and foreign root returns, and
Start/Stop caller release consumed then throwing. Each uses the same production
Capture and actual one-argument primitive, not an alternate owner state machine.
Both allocation rows have individual Debug/Release runs; the remaining six have
individual Debug runs and are also included in the final Debug/Release gates.
All are direct GREEN with unchanged production/runtime/support code. No new
behavioral RED or production repair is claimed for these eight additions.

Invalid roots produce the primitive's ordinary InvalidOperationException, not
an injected fatal. Unknown allocation does not authorize copy, free or caller
release. Caller-release failures retain attempted/unconfirmed ownership even
when root free, native retirement and managed drain independently succeeded.
Repeated Dispose does not retry an unknown effect; known independent owners
receive their single cleanup attempt. Original nested fatal identity, complete
weak graph and retained-owner charge survive fixture raw teardown and outer GC.
Start/Stop invalid-return outcomes are recorded separately rather than forcing
identical exception semantics.

Final focused Debug/Release each pass120; project Debug/Release each pass389.
Complete qualified identity sets match across configurations and equal the
accepted381 baseline plus these8 Facts, with none removed. All restore/build/
test exits are0, with0 warnings/errors. The final test gates bind503 selected
inputs and138 complete runtime files per configuration. A separate565-input
quality snapshot passes serial locked full-solution restore and verify-only
format; its test-input projection matches the final gate. This is not a
full-solution test, native execution or new hosted result.

## Frozen bindings and root replay

- Ownership test SHA256: `ac64dff1cc8954aabc657084ac02b9b2e6a40e498446b38852ddbbe61f1e7d03`.
- API.

  SHA-256: `ef8abaa78f6dad313c07a31a993120682b931f3d17f77ce533b354d2a93ebf89`.

- Final503-input manifest: `6329cb732bfb4236bc106b38461269ddb3937dd0f57283b2f4583c9d7f02c172`.
- Runtime manifests D/R: `62fa8da36dd41f18ed980d917f1b6c664a842fba0bf308a1da3d22f2be0f6b1a` / `f6ae91687965934051be4491cfe8b01c0273bd74b29ab8b83ca731ea59bc6ffb`.
- Quality565-input manifest: `1aafd08ee7adebb91d2af6b270f4be75a33012b4a8d2e4169810ea0c0ac8ee52`.
- Auditor SHA256: `fcd710c94812fe224cf67a82130e668a76bd4cc1b0ec082b2e468cef108874d5`.
- Replay SHA256: `9918096170a8f81a4a485671f29a2274fe4fca6025540a5774e5ed6303807859`.
- Saved report: `f5943f50a2aee357769e86d0338a8b430b0f495a6251b2e90dc98c5efe622056`.
- Selected-current report: `8bb58ddfcea2ab92a7f09e08b72f83b686f10e326de34e0910309005d93b2c98`.

Independent Standards and Spec reviews each report0 concrete findings. Root
actually executes both frozen replay modes and compares each report with its
worker counterpart: all exits0, violations empty and reports byte-identical.
The replay checks raw commands, complete receipts, actual source/runtime
inventories, qualified TRX identities and DLL bindings; it does not rerun tests.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/replay.sh next-saved01 saved
```

Current equality is limited to the recorded503 test/565 quality inputs, excludes
GitHub scripts and documentation, and becomes historical after later edits.
Together with the accepted Start, Stop-copy and late root-free checkpoints this
closes task3 only. Four fixed pool scopes, resource-use closure/join, races,
outer IsDrained debt, healthy Capture, final solution and exact-new-SHA hosted
gates remain required. Portable injected faults are not actual native exception
containment or process-wide Capture admission.
