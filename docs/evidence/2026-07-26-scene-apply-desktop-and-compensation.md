# Scene Apply Desktop and Remote Compensation Evidence — 2026-07-26

## Evidence boundary

This record closes the automated evidence for task 8.2: deterministic Scene
apply preview/approval, durable journaled execution through production
boundaries, explicit safe remote Replace compensation over protocol 1.4, and
the Desktop preview, confirmation, partial-result, and compensation
presentation with keyboard and accessible-name coverage. It does not implement
or validate the private Scene repository and inspect/delete/export UI
lifecycle (task 8.3), native application migration, packaged three-OS
distribution, or physical two-device behavior. All hosted and same-host
results below are runner-image and same-host evidence, not physical or
native-device evidence.

Branch: `codex/v1-foundation`

Implementation commits:
`ad4cc02` (`feat: compensate remote Scene Replace explicitly`) and
`26ff858c71ff76790329154bfe437c3492f1a804`
(`feat: present Scene apply preview, results, and compensation`).

The task-status commit containing this record is effective only after that
exact commit passes the same CI, Secret Scan, and CodeQL workflows.

## Implemented contract

- Remote Replace compensation sends a payload-free authenticated
  `SceneUndoReplaceInstruction` whose operation/correlation IDs are frozen
  digests of the parent operation, Scene index, and capsule ID, so repeated
  compensation is idempotent by construction. The operation deadline is the
  Undo Capsule expiry; the control-envelope TTL stays canonically capped at
  five minutes and can only shorten, never extend, capsule validity.
- Undo results are rebuilt from the locally pending instruction; only status,
  failure code, and occurrence time are read from the wire, and forged, stale,
  expired, consumed, failed, cancelled, unsupported-protocol, undelivered,
  and lost-acknowledgement outcomes all degrade to explicit non-success
  statuses. No path synthesizes `Committed`.
- The Desktop Scene panel presents ordered actions and blockers, source
  disposition, exact Replace targets, stale-Group and expiry state, explicit
  per-Replace and whole-preview confirmations that independently gate Apply,
  truthful per-item results, and explicit Replace-only compensation, while
  keeping protected and ambiguous occupants redacted and the global status at
  `NOT SHARING`.
- Dual review closed with two hardening fixes, both regression-tested:
  a null-Replace-endpoint undo now reports `UndoUnavailable` with a current
  timestamp instead of a future one that a remote peer could use to tear down
  the authenticated session, and a non-UTC Undo Capsule expiry from a replace
  target is rejected at decode instead of aborting a Scene apply mid-run.
  `UndoCapsuleReference` additionally canonicalizes its expiry at
  construction.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

## Local results and review

- Locked restore and format verification passed.
- The Release build passed with 0 warnings and 0 errors.
- All 1047 tests passed with 0 failed and 0 skipped:
  - Desktop 171, Transport 255, Integration 300, Security 123;
  - Domain 60, Protocol 47, shared platform 19;
  - Windows platform 20, macOS platform 18, Linux platform 20;
  - mDNS transport 14.
- Desktop composition printed its explicit TEST MODE success line.
- The deterministic simulator result is recorded below; it remains a
  same-host deterministic simulator, not Scene-apply or physical-device
  evidence.
- The direct/transitive vulnerability query reported no known vulnerable
  package.
- The Scene accessibility workflow test and the compensation/routing suites
  each passed in 20/20 fresh `dotnet test` processes.
- Functional review and independent security review each closed with the two
  hardening findings described above; both fixes landed with regression tests
  (`InboundRemoteSceneUndoWithoutReplaceEndpointStaysDeliverable`,
  `ReplaceResultRejectsNonUtcUndoCapsuleExpiry`) before the implementation
  commits were created.
- The headless Scene accessibility workflow proves keyboard operability for
  preview, per-Replace confirmation, whole-preview acknowledgement, Apply,
  compensation confirmation, and compensation activation inside one
  application instance, with accessible names, blocked-item redaction,
  truthful partial results, and persistent `NOT SHARING`.

These are local macOS and portable same-host results. Platform-named contract
projects do not prove native Windows or Linux APIs.

## Hosted exact-commit evidence

Implementation commit `26ff858c71ff76790329154bfe437c3492f1a804`
passed [CI run `30206950890`](https://github.com/happys2333/flowspan/actions/runs/30206950890):

- macOS job [`89806581692`](https://github.com/happys2333/flowspan/actions/runs/30206950890/job/89806581692);
- Ubuntu job [`89806581672`](https://github.com/happys2333/flowspan/actions/runs/30206950890/job/89806581672);
- Windows job [`89806581678`](https://github.com/happys2333/flowspan/actions/runs/30206950890/job/89806581678);
- Secret Scan job [`89806581648`](https://github.com/happys2333/flowspan/actions/runs/30206950890/job/89806581648).

Every OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the simulator, and uploaded test evidence. Hosted runners
prove portable build and contract behavior on those runner images, not
physical two-device networking or native permission behavior.

Downloaded artifacts were independently hashed and parsed:

| Artifact | ID | SHA-256 | Files/results |
| --- | ---: | --- | ---: |
| Windows TRX | `8633371074` | `2b75c3f4d057eaeaf21d1a967403b26318404e94398e18e2f43939206ee667ab` | 11 TRX |
| macOS TRX | `8633353052` | `7f8a6141d501f418535e733bd6c9bb8b7ef756da661d2c4a171dbee76612093b` | 11 TRX |
| Linux TRX | `8633359672` | `23e8801d2fa7c04dcb101b23956a4ea30e8de48300a6cb8d010f0c482b310b49` | 11 TRX |
| Gitleaks SARIF | `8633338919` | `6d5f5dfbf5d8455c456e670617bab3566cc00ccd35a8245866d66942790e20df` | 208 rules, 0 results |

Summing every downloaded TRX `Counters` element independently produced the
same result on Windows, macOS, and Ubuntu:

```text
files=11 total=1047 executed=1047 passed=1047 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 completed=0 inProgress=0 pending=0
```

[CodeQL run `30206950861`](https://github.com/happys2333/flowspan/actions/runs/30206950861),
job [`89806581478`](https://github.com/happys2333/flowspan/actions/runs/30206950861/job/89806581478),
also passed for the exact implementation commit. CodeQL 2.26.1 evaluated 52
rules in analysis `1528724207` and reported 0 results and 0 open alerts for
the branch.

## Remaining gates

- Task 8.3 must add a private atomic Scene repository and inspect/delete/
  export behavior with filesystem protection and redaction; the Desktop
  Scene panel intentionally stays inert until that repository workflow
  provides `SelectScene`.
- Native Adapter, packaged three-OS, physical two-device, independent
  external security-review, cryptographic-review, and release-wide v1 gates
  remain open. This task does not claim arbitrary application process-state
  migration, and no hosted or same-host result above is physical or
  native-device evidence.
