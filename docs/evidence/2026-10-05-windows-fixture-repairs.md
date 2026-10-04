# Windows checkpoint fixture repairs — local evidence

Status: three test-only repairs locally verified after the failed exact
`9deed368f2fe9697e5dd9e9d02ef0409701f2a3b` checkpoint. Production code is
unchanged by these repairs. [Root-wide combined gates](2026-10-05-early-checkpoint-local-gates.md)
pass Debug/Release 2777 each; exact `2c6f8fd` now also passes the separately
[audited hosted checkpoint](2026-10-05-early-association-hosted-checkpoint.md).

The [original hosted record](2026-10-05-early-coordinator-hosted-failure.md)
remains failed: CI `37228426977` has Windows 2771 Passed / 4 Failed,
macOS/Linux 2775 Passed each, and skipped packaging. CodeQL `37228426955`
passed. No local result below rewrites that run or proves post-fix Windows or
Linux execution. Local execution used macOS 27.0.1 ordinary arm64 and .NET SDK
10.0.301.

## Repairs and retained regression proof

| Test file | Frozen final SHA-256 |
| --- | --- |
| `tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowStreamDelegateRouterTests.cs` | `27de6e30ea88860cebfdb904fd5880845485ab36d8531c33e35e221a7ce60ab1` |
| `tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs` | `93b1d3364c71e418eae8909ae7ca0bd96d06cf83869da3f7e401dc74f5b279a6` |
| `tests/Flowspan.Desktop.Tests/DesktopPairingDecisionSourceTests.cs` | `d6389f58901f871f7089ba129a2699a277a49cd56ff20500a9b1c295c966a362` |

### Router retirement observer

The admitted callback's automatic five-second gate expiry could release its
invocation while a scheduler-starved observer had not resumed. Retirement then
legitimately completed before the old `Assert.False` at line 140. Two isolated
executions of the original fixture reproduce that exact assertion failure;
this is not a production early-retirement defect.

Two blocking fixtures now use dedicated background threads and owner-finally
release, with bounded joins before gate disposal. The final Debug/Release
focused suites each pass 62/62 (33 router + 29 coordinator), preserving the
exact downloaded Windows case inventory. A controlled final observer pause
lasts over six seconds, beyond the old expiry, and still passes pending-handler
and retirement assertions before explicit release. Final build/format checks
pass. The production router remains SHA-256
`ed4975c2440124218ba9e6c3117669912e8061842c346e169a2cc1a6c02ddde4`.

### Prepare status after internal generation revocation

The denied real Prepare send cancels its control session. Cleanup can revoke
the connection generation before the post-cancel lease-linked token check,
throwing `OperationCanceledException` instead of normally returning
`NotDelivered`; the old wrapper consequently left its result property null.
The host coordinator directly awaits the wrapper. This is not an early-return
or result-property visibility defect.

Two deterministic revocation-first rows first reproduce the old ND/null
assertion (RED: 2 Passed / 2 Failed), then pass strict outcome discrimination:
normal `NotDelivered` with no exception, or exact OCE with a canceled noncaller
token, original caller still uncanceled, and completed actual generation
revocation. Neither arbitrary exceptions nor null alone are accepted. All
zero-Prepare-wire, capture/input/media/render/authority and both-node drain
assertions remain. Debug/Release each pass focused 4/4 and tracer class 45/45;
40 fresh processes pass 160 focused executions.

Inventory change is explicit: adding the Boolean parameter renames the two
existing natural rows as `revokeBeforePreparationResult: False`; two new
`True` rows are added. This is not a claim of two additions with no name removals.
The new forced rows are RED-to-GREEN; the revised natural rows are not new TDD
cases. Historical ADR 0027 NotDelivered observations remain historical.

### Pairing publication scheduling

The original `before-publication` row and Dispose case failed at old lines 209
and 25. Controlled one-worker probes using the original test assembly reproduce
both locations. An async controller scheduled with `LongRunning` plus `Unwrap`
could still need a shared-pool continuation while cancellation-registration
cleanup occupied that pool. The Dispose publisher itself also depended on it.
These controlled reproductions do not prove that the historical Windows runner
had a one-worker pool or that exact blocked stack.

Three concurrency fixtures now use synchronous dedicated controllers and
dedicated publication/disposal workers with unconditional finally release and
drain. Blocking barriers no longer self-expire. Existing five-second observation
and join bounds are not widened. All ten cases (seven Facts, three theory rows),
exact event ordering, cancellation, exception-type and invocation-count
assertions remain. Debug/Release each pass 10/10; fourteen fresh one-worker CLR
processes pass (seven per configuration). Production pairing source remains
SHA-256 `68d87b2f4e8663ccf757395760f4d8b7eb614c644423d931e03babcb14b5aa9c`.

## Saved records and audit recipes

The original Windows archive `11313371560` is retained with SHA-256
`913a5d76afec2cca96bc24416ddc986e2fedd064ceb8b485a89ce0402bdcf29d`.
Detailed commands, source/runtime manifests, raw failures, final outputs and
limitations are retained in:

- `/tmp/flowspan-router-windows-9deed36/HANDOFF.md`; final records are stages
  `08-final-debug`, `09-final-release`, `10-final-scheduler-regression`.
- `/tmp/flowspan-prepare-status-windows-9deed36/findings.md`; RED/GREEN inputs,
  binaries, TRX and separate stress records are preserved.
- `/tmp/flowspan-pairing-scheduling-9deed36/diagnosis.md` and `stages.json`;
  final source snapshot is `green-source.cs`. `stages.json` hashes 34 artifacts
  and has SHA-256 `62ec083dee9b0f217dc34f603c372541eb5cd0f4145b01f3ace115cd861a8458`.

```sh
python3 /tmp/flowspan-router-windows-9deed36/audit.py
bash /tmp/flowspan-prepare-status-windows-9deed36/audit.sh
```

Both saved-evidence audits passed. Router audit script SHA-256 is
`b3bda01df8bdc9668dd59086cfe5cef98ba3725587c9b85ba35d751c10d8ed36`;
its final serialized result is **`audit-final.json`**, SHA-256
`c73daed47a4af633f2f67c9d053b19450709a4c9fecdad05e528a89d38eebe5e`.
The differently named `audit.json` is a superseded source/stage record and is
not final proof. Prepare audit script SHA-256 is
`f444fa15e8817f65e63b38762ef16ecfd012f117c8c2ff1ffe6dc8b0cf1e4a57`.
Pairing replay commands are in its diagnosis record; no local RED TRX is
fabricated for its controlled probe failures.

Compile/build/harness setup failures that did not execute the target are not
behavioral RED. Router stage 04's cloud-conflict copy blockage and pairing's
whole-VSTest one-worker initialization stall are excluded; conflict files were
preserved. Superseded router stages are not final-source evidence. Original
router harness runtime is preserved, but its initial harness source was not
separately snapshotted and is not retroactively claimed.

Independent final read-only Standards/Spec reviews report no actionable findings
for each frozen repair. These prove bounded local managed/loopback regressions,
not native/physical pairing or Capture, minimum-OS, production availability,
full matrix, release, v1, or active Goal completion.
