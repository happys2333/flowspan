# macOS delegate ownership prerequisite — local and hosted evidence

Status: prerequisite complete at implementation
`c27532dd809cb9d29f5ec01b45e37bbca5fcddd3`, with final local, exact-commit
Windows/macOS/Linux CI and downloaded synthetic ABI evidence. This closes only
MDO task 4 and native task 6.1a; native Task 6, v1 and the long-term Goal remain
open.

Scope is the [MDO specification](../../specs/v1/native-remote-window/macos-delegate-ownership/requirements.md)
and [ADR 0029](../adr/0029-direct-csharp-screencapturekit-interop.md).
The owner is not wired into capture; current ScreenCaptureKit construction
still passes `delegate=0`. Production Remote Window host sharing remains
unavailable. No new NuGet dependencies or production-language toolchain were
introduced; this is an independent clean-room C# implementation.

## Executed environment and inputs

- Local macOS 27.0.1, build 26A434, ordinary arm64; .NET SDK 10.0.301 and
  executed runtime 10.0.9. Neither macOS 15.2 nor Intel/arm64e was executed.
- Baseline HEAD `eea18ad22e66e3b10aaf87af612e945ead35547e` plus the scoped
  uncommitted owner/tests/tool/IVT/workflow changes. Local results are source-
  hash bound, not an inherited hosted result from this baseline commit.
- Owner SHA-256:
  `6aa506c30880383e1c5a99e7898c7ac93e22e6e1a93ef6d32767c350b1cc75e8`.
- Tests SHA-256:
  `dd7f20854138f60a41d542707000ced55a643d724ce0dcc4a05365d0f1189849`.
- `NativeDelegateProbe.cs` SHA-256:
  `9b50d293c4dc02e79395171ba274a5c3341efcbb51983de32612fd31905262ba`.
- Complete tool/project/lock/IVT and isolated DLL hashes are preserved in
  `/tmp/flowspan-macos-delegate-probe-20261004/final-input-and-output-hashes.log`.

## Portable behavior and actual RED→GREEN

Twelve behavioral stages have executed failing TRX followed by passing TRX:
terminal without sample; early terminal; binding mismatch; blocked retirement;
immutable address/late isolation; direct self-join; Task.Run descendant
self-join; handler/OOM faults; wrong-stream before activation; invalid signal;
depth-65 original OOM; and published/unbound zero-stream initialization.
The handler/OOM stage has two failing cases; each other stage has one.
The root independently parsed these RED/GREEN records. Capacity and exited-
ExecutionContext cases are supplemental GREEN, not manufactured behavior RED.
Compilation failures and test-writing mistakes are preserved separately.

Standards review found that an old handler's `Assert.Fail` could be contained
by the owner, making a negative test ineffective. The final test observes zero
old notifications and null Failure instead. Spec review found that a published
owner's failed zero bind allowed later initialization. The executed stage-14
RED is repaired by permanently retiring that published owner; a pre-publication
zero attempt remains nonterminal. Final two-axis owner review and probe/CI review
must refer to these final hashes, not the older index contents.

Final focused Debug/Release each pass 18/18. Full MacOS project each passes
144/144. Owner lifecycle records and commands are in
`/tmp/flowspan-macos-delegate-20261004/OWNER-HANDOFF.md`.

## Complete local solution gate

The root actually executed locked restore, complete no-change format, Debug and
Release warning-as-error builds, and both complete solution test runs:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore --logger 'trx;LogFilePrefix=mdo-debug' --results-directory /tmp/flowspan-mdo-solution-debug-results-20261004 --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFilePrefix=mdo-release' --results-directory /tmp/flowspan-mdo-solution-release-results-20261004 --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
```

Both builds report zero warnings/errors. Each configuration has exactly 12 TRX,
2710 total/executed/passed, all non-success counters zero, every result Passed,
and matching per-assembly counts. Independent parser and result:
`/tmp/flowspan-mdo-qa-audit-20261004.py` / `.json`; JSON SHA-256
`0f05ef598228cd4e520691d6cf70511686b7ede43135e398ac09f0fda23954f6`.

Explicit TEST MODE composition and deterministic simulator return exit 0.
The 26-project NuGet including-transitive vulnerability query returns exit 0,
empty stderr, no query errors or reported vulnerable packages. This is a
point-in-time advisory query, not proof of general safety. Local checks are
macOS execution of portable contracts, not Windows/Linux native evidence.

## Actual no-capture Objective-C/GCD execution

The independent tool's locked Debug/Release builds and all four modes were
actually executed on the final owner: default/help return explicit Skip with
zero probe-native calls, unknown arguments return expected exit 2, and explicit
`--run-synthetic` returns exit 0 in both configurations. Full standalone format
also passes. See [reproduction commands and limits](../../tools/Flowspan.MacOS.DelegateProbe/README.md)
and `/tmp/flowspan-macos-delegate-probe-20261004/RESULTS.md`.

Both final configurations actually observed 61 native callbacks and four
published bridges. The root independently restored/built the standard Release
output, ran full tool format, and executed synthetic mode again. Its stdout is
277 bytes, exactly one LF record, SHA-256
`ca64ed8dd8591c786457c2f84ece3885b4245a0ef155d10f785fb06a7e19fdf5`;
stderr is zero bytes. Actual record:

```text
delegate_probe=pass; mode=synthetic; method_signatures=3; native_callbacks=61; managed_invocations_exited=true; published_bridges=4; published_bridges_retained=true; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0
```

The three ordinary-arm64 signatures derive from SDK SCStreamDelegate declarations,
but runtime metadata belongs to this synthetic NSObject subclass. Typed
Objective-C messages/GCD callbacks actually exercise early terminal, forced GC,
blocked-handler concurrency, direct/descendant self-join, retirement and late
tombstones. No ScreenCaptureKit session, SCStream, AppKit, window, permission
preflight/request, source enumeration, title, sample, pixel or input API runs.

`dotnet build --locked-mode` was rejected by MSBuild before compilation; logs
remain preserved. Reproduction docs now use the actually successful
`-p:RestoreLockedMode=true`. This command error is not counted as behavioral RED.

## Strict CI evidence gate and remaining work

CI builds/formats the tool on all three OSes and requires the no-native default.
Only matching macOS runs explicit synthetic mode, with a two-minute step limit
and raw stdout/stderr uploaded even when the step fails. Native Pass requires
exit 0, empty stderr, a bounded exact one-line schema and no NUL; Skip cannot pass.
CodeQL explicitly builds the standalone source for extraction.

An independently reproduced gate defect accepted embedded NUL because Bash
command substitution discarded it. Actual fixture RED incorrectly exited 0;
the raw-byte NUL check repairs this, and all 26 fixtures plus actionlint pass.
Files: `/tmp/flowspan-macos-delegate-ci-gate-tests.py`,
`/tmp/flowspan-mdo-ci-gate-nul-{red,green}-20261004.log`.

Published +1 NSObject bridges and immutable mappings are deliberately retained
until process exit, with a fixed four-slot probe budget. This is not cleanup,
production memory boundedness or native drain. `ManagedInvocationsExited` proves
only admitted managed handlers have exited; no-future-native-callback,
SCStream delegate retention/unbinding, real source loss, capture drain, TCC,
protection, input, independent Emergency Stop, physical devices, signing,
notarization, legal clearance and full v1 remain open. The separately audited
exact-SHA hosted checkpoint and downloaded raw evidence are recorded below.

## Exact hosted checkpoint: MDO delegate ownership

Audited on 2026-10-05 HKT; run timestamps below are UTC. The read-only hosted audit performed no workflow dispatch/rerun/cancel or GitHub communication. The preserved audit scripts were independently rerun for this repository closeout, all with exit 0.

### Source and completed runs

- Commit: `c27532dd809cb9d29f5ec01b45e37bbca5fcddd3`; ref `refs/heads/codex/v1-foundation`.
- [CI run 37214730077](https://github.com/happys2333/flowspan/actions/runs/37214730077), run number 242, attempt 1, push event: completed **success**, updated `2026-10-04T16:02:21Z`; all eight jobs succeeded.
- [CodeQL run 37214730078](https://github.com/happys2333/flowspan/actions/runs/37214730078), run number 242, attempt 1, push event: completed **success**; Analyze C# job `111472819681` succeeded.
- Repository/head-repository identity, full SHA, branch, run/attempt, workflow path, check-suite/job identity and exact committed workflow bytes were checked. Windows and Linux correctly skipped only the two macOS-specific synthetic delegate execution/upload steps; these skips are not native Pass results.

### Test result archives

All three downloaded archives matched their API byte count/digest and completed upload log ID/size/SHA256. Each contains **12 TRX, 2710 distinct cases, 2710 executed and Passed**; all other result counters are zero. The complete case-name inventories match across OS, SHA256 `b5b5a025d9b19bde23adc2d49663e2864db94b82a46761f3aade7fac4e1a2bc4`.

| OS | Test job | Artifact | ZIP bytes | ZIP SHA256 |
|---|---:|---:|---:|---|
| Windows | 111472820023 | 11307788720 | 631082 | `7a8743a3e135733208bd21bfc0830dfe9ca138bed18dfea97b4364fdc2917c85` |
| macOS | 111472820030 | 11307288683 | 632091 | `fb2e222dddaeee14f7fafb0542936a435f2c0f75993209d47d38ed4846ca8637` |
| Linux | 111472820054 | 11307454780 | 633373 | `7438dc17429563ab22d234e6037161fda14224efd6fc0ec4bcaed5095b8f001b` |

Per OS: macOS project **144 Passed**, new `MacOSRemoteWindowCallbackOwnerTests` **18 Passed**, prior candidate subset **62 Passed**, startup subset **4 Passed**, caller-cancellation contract **6 Passed**, plus named real-TCP and route-disposal regressions Passed. These are managed/contract tests, not OS window-capture verification.

### Hosted macOS synthetic delegate evidence

Artifact `macos-delegate-synthetic-c27532dd809cb9d29f5ec01b45e37bbca5fcddd3`, ID **11308205901**: downloaded ZIP **462 bytes**, SHA256 `3b0d26bbc7c84f2fae841530e9c15365b7d76e14623231ef86a05192ee9ece6c`; upload log and API match.

Raw members: `native.stdout.raw` **277 bytes**, exactly one trailing LF, no NUL; `native.stderr.raw` **0 bytes**. Anchored Pass schema succeeds, not Skip. stdout SHA256 `ca64ed8dd8591c786457c2f84ece3885b4245a0ef155d10f785fb06a7e19fdf5`; empty stderr SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`. Hosted stdout is byte-identical to the preserved local Debug and Release synthetic logs.

```text
delegate_probe=pass; mode=synthetic; method_signatures=3; native_callbacks=61; managed_invocations_exited=true; published_bridges=4; published_bridges_retained=true; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0
```

All three OS observed the no-native default exactly:

```text
delegate_probe=skip; reason=explicit_run_required; native_calls=0; capture_executed=false
```

Scope: synthetic NSObject messages/GCD only. Three native method signatures, 61 callbacks and four published +1 bridges were exercised; bridges and immutable mapping are deliberately retained until process exit. `ManagedInvocationsExited` means admitted managed handlers exited, **not** native cleanup/drain, unbind, or no-future-callback proof. No SCStream, AppKit, permission prompt, pixel capture or input injection. Production capture still passes `delegate=0`, production factory remains unavailable; Task 6 and v1 native/physical acceptance stay open. This does not establish minimum OS, Intel or arm64e behavior.

### Existing no-capture probes

- Three OS: explicit desktop TEST MODE and Protocol 1.7; standalone locked builds/format gates succeed; portable self-tests report 10/12/17 cases without native calls. Production macOS probe default remains explicit-run-required Skip with no AppKit/capture/permissions.
- Windows WARP synthetic texture readback Pass: four owned/released references and expected 24-byte digest; macOS/Linux correctly report requires-Windows Skip. No WGC self-window capture Pass is claimed.
- macOS copied-block and synthetic sample probes report 1000 cycles each with zero live contexts/retained samples; own-window capture is skipped.
- Linux thread-loop ABI job `111472820056` succeeded; log reports library 1.0.5, native timespec 16 bytes, owner-release/loop/lock round trips, zero daemon/portal/stream/hardware/capture/permissions/pixels. Artifact `11308305815`, **1875 bytes**, API/upload SHA256 `9149d89d6738bd77b0acbf96b57f9acbfe31bb534ea3ce58d3401faf724a37df`. Its ZIP was **not downloaded**, so no local raw-member/hash verification is claimed.

### Security evidence

CodeQL analysis **1889105944**, SARIF ID `a0868174-c00c-11f1-9ed5-073f7269c51e`: exact SHA/ref, analysis key `.github/workflows/codeql.yml:analyze`, empty environment; analysis created within specified job. CodeQL **2.27.1**, **431/431 C#** reported; independently `git ls-tree` counts 431 C# at exact commit. **52 rules / 0 results**, **3 raw diagnostics / 0 removed / 0 summary diagnostics**; API error/warning empty and SARIF processing complete. One check notice announces upcoming ubuntu-latest runner migration; not zero annotations. Downloaded analysis API SARIF is **231091 bytes**, SHA256 `aefe134201a2852dd99d67ff386bf5b040620008358a7a4237440756332f90dd`; it is reconstructed API representation, not byte-identical original upload. Count equality is not per-file TRAP audit or a universal security proof.

Secret scan job **111472819885**: actual Gitleaks **8.24.3**, **4 commits / approximately 88614 bytes**, `no leaks found`, **208 SARIF rules / 0 results**. Actual Git range/options:

```text
--no-merges --first-parent 16b1c124c9656e2f9f88e88077e15e01693d8417^..c27532dd809cb9d29f5ec01b45e37bbca5fcddd3
```

Artifact **11307584085**, **6764 bytes**, API/upload/local ZIP SHA256 `4e9b9e5186e5cf3fdd50f2d9787401fcfdb6e21463fdbc05b6d09593b30cca39`. SARIF member **45825 bytes**, SHA256 `f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`. SARIF semanticVersion field is `v8.0.0`, not actual execution version; DEP0040/DEP0169 warnings are present. This is only the observed pushed first-parent/no-merges range, **not** full-history or entire-working-tree scanning.

### Unsigned packages: hosted evidence only

All three package jobs succeeded, each with explicit TEST MODE, two seals/two verifier Pass outputs, successful same-stage recursive package diff, and 26 project dependency queries without reported vulnerable packages. API/upload ID/bytes/digest match; **these ZIPs were not downloaded or independently internally verified locally**.

| RID | Job | Artifact | ZIP bytes | API/upload SHA256 |
|---|---:|---:|---:|---|
| win-x64 | 111473941895 | 11307739309 | 44099145 | `30ef34593a1c9924dfe645db5f563ff67f5cd9511e05ce2f6023af8fbfa7ddb2` |
| osx-arm64 | 111473941865 | 11307744164 | 42926472 | `fafe62845dcb600116df75f5119d80c2a6f9a06c64b804535d9a7001c00de947` |
| linux-x64 | 111473941889 | 11307888523 | 42095443 | `d52fd789774c23220db325583f5fcfcda9d9264ad474db2fd443c8d57d6d64d9` |

Unsigned test artifacts, same-stage determinism and companion-record consistency do not prove native install/permissions/input/capture, physical two-device continuity, licensing clearance, signing/notarization, trusted attestation or production release acceptance. No release/v1 completion claim.

### Reproduction and preserved audit outputs

```sh
python3 /tmp/flowspan-mdo-hosted-c27532d/audit_tests.py
python3 /tmp/flowspan-mdo-hosted-c27532d/audit_checkpoint.py
```

- `/tmp/flowspan-mdo-hosted-c27532d/trx-audit.json`: per-project counters and every selected regression case, zero violations.
- `/tmp/flowspan-mdo-hosted-c27532d/checkpoint-audit.json`: completed exact-run audit; preserved API/workflow/log hashes, artifact/upload/local checks, raw delegate bytes, tests, probes, package and security scope.
- Inputs preserved under `api/`, `logs/` and `archives/` (five small downloaded artifacts only: three TRX ZIPs, delegate raw ZIP, Secret SARIF ZIP; Linux ABI and three package ZIPs deliberately absent).

### Independent closeout verification and acceptance boundary

The closeout independently reran all three preserved scripts, each with exit 0:

```sh
python3 /tmp/flowspan-mdo-hosted-c27532d/audit_tests.py
python3 /tmp/flowspan-mdo-hosted-c27532d/audit_checkpoint.py
python3 /tmp/flowspan-mdo-independent-hosted-audit-20261005.py
```

The independent four-archive audit confirms the three TRX archives match local
per-assembly counts and the hosted delegate raw record exactly matches the
root's locally executed raw stdout. Final standards/spec/probe/CI and
evidence-integrity reviews have no unresolved findings. This closes only MDO
task 4 and native task 6.1a at the exact implementation checkpoint above. It
does not promote a synthetic callback bridge into production delegate
composition, native drain, real source loss, physical two-Device continuity,
package/release acceptance or full v1 completion. Task 6 and the active
long-term Goal remain open.
