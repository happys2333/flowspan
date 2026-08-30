# Late cleanup-failure ledger checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`4daf82ce2eaeaba582eaf541fdf643daa4f7b73b`

Local environment: macOS 26.6.2 arm64, build 25G83, .NET SDK 10.0.301

Evidence classification: exact-commit local and hosted managed Desktop contract
evidence. This checkpoint adds no production-composed tracer row and contains no
native or physical-device evidence.

## Scope

This checkpoint implements Task 5.5a.3c, the two-owner late non-fatal failure
extension of
[ADR 0028](../adr/0028-bounded-remote-window-cleanup-confirmation.md). Its scope
is deliberately limited to one stable already-active generation, an
uncontended coordinator lifecycle gate, a healthy manual cleanup-confirmation
timer, external Dispose-first initiation, and two ordered cleanup-owner
failures.

Formal Emergency Stop registration disposal injects owner failure A at its
existing cleanup-step position. The later authenticated host Connection
disposal enters a test-owned barrier, then injects owner failure B when released.
That barrier keeps real cleanup pending through deadline minus one tick and
exact equality, allowing the shared public Dispose task to publish one stable
`host_cleanup_timeout` before either cleanup failure reaches the terminal
ledger.

When the Connection is released, every remaining independently safe cleanup
step runs. Terminal-ledger append recursively traverses non-fatal aggregate
inner exceptions in stored depth-first order. The resulting diagnostic is one
flat aggregate with the exact leaf sequence `[timeout, A, B]`; every original
exception instance is preserved, no nested aggregate remains, and the
generation's real cleanup result is recorded once. Late diagnostic settlement
does not mutate the already-completed public Dispose task or its timeout.

## TDD and review history

The focused test was first run against the preceding implementation tree
`3c409c5`. The old append path produced direct terminal children
`[timeout, Aggregate(A, B)]`, so the no-nested-aggregate assertion failed. That
was the behavioral RED.

A deliberate breadth-first/built-in-flatten mutation then produced
`[timeout, B, A]`; the exact semantic-order assertion failed. The final GREEN
uses a deeply nested A and direct B to prove recursive depth-first traversal in
stored inner-exception order rather than merely removing one aggregate layer.

The production change is limited to non-fatal terminal-ledger append. It
recursively collects leaves, returns the original leaf when only one exists,
creates one aggregate for multiple leaves, preserves the existing empty
aggregate fallback, and leaves the separate fatal OOM-dominance path unchanged.
Strict code/state, test-design, and specification reviews reported zero P0, P1,
or P2 findings for this bounded slice.

## Deterministic coordinator contract

`DisposeFirstTimeoutKeepsPublicFailureStableAndFlattensLateOwnerFailures` uses
`ReadyHostHarness`, a manual `TimeProvider`, a deeply nested Emergency Stop
registration-disposal failure A, and a host Connection whose disposal blocks
before throwing failure B. It proves:

- the first and concurrent external Dispose calls return the same public Task;
- formal Emergency Stop registration disposal runs once and becomes non-current
  before the later Connection owner blocks;
- Snapshot and active media authority are removed while the exact generation
  remains retiring with one active watchdog and no published terminal failure;
- at deadline minus one tick, both public Dispose observations, real cleanup,
  the Connection owner, retiring ownership, and the timer remain pending;
- exact equality publishes the stable `host_cleanup_timeout`, while concurrent
  and at-timeout Dispose observations expose that exact exception instance;
- releasing the Connection records A before B in cleanup-step order and lets
  every remaining safe owner release execute;
- the late terminal diagnostic is precisely one flat aggregate with the exact
  original instances `[timeout, A, B]` and no aggregate child;
- concurrent, at-timeout, post-drain, and repeated external Dispose observations
  all retain the same public Task and timeout instance after the diagnostic
  becomes the late aggregate;
- real cleanup is appended once while multiple external Dispose observations
  share the same public Task;
  and
- capture/input Stop, fail-close, Connection disposal, Authorization,
  Permission, Protection, Emergency Stop, control, Admission, media budget,
  watchdog, and retiring-generation ownership all drain exactly as asserted.

The test's `finally` path advances a still-pending watchdog when necessary,
releases the Connection barrier, creates the disposal operation if an earlier
assertion prevented it, and boundedly observes public disposal, Connection
disposal, and retiring-generation drain.

## Reproduction commands and local results

The following commands define the recorded local verification surface:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DisposeFirstTimeoutKeepsPublicFailureStableAndFlattensLateOwnerFailures'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DisposeFirstTimeoutKeepsPublicFailureStableAndFlattensLateOwnerFailures'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~DisposeFirstTimeoutKeepsPublicFailureStableAndFlattensLateOwnerFailures' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~DisposeFirstTimeoutKeepsPublicFailureStableAndFlattensLateOwnerFailures' || exit 1; done
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj --configuration Release --no-build --no-restore
```

Recorded results at exact implementation commit `4daf82c`:

- focused row: `1/1` in Debug and `1/1` in Release;
- twenty fresh focused processes: `20/20` in Debug and `20/20` in Release;
- coordinator contract class: `120/120` in Debug and Release;
- complete Desktop project: `724/724` in Debug and Release;
- complete solution: `2588/2588` in Debug and Release;
- warning-as-error solution builds: zero warnings and zero errors in Debug and
  Release;
- `dotnet format --verify-no-changes` and `git diff --check`: passed;
- explicit TEST MODE Desktop composition validation: passed;
- deterministic simulator: passed; and
- direct and transitive NuGet vulnerability audit: every project reported no
  known vulnerable package.

The production-composed managed tracer remains the existing 42-case class. This
checkpoint adds no 43rd tracer execution. `gitleaks` is not installed on this
local host, so no local Secret Scan result is claimed.

## Hosted status

[CI run `33318946768`](https://github.com/happys2333/flowspan/actions/runs/33318946768)
(run 226, attempt 1) and
[CodeQL run `33318946770`](https://github.com/happys2333/flowspan/actions/runs/33318946770)
(run 226, attempt 1) both completed successfully for exact implementation
`4daf82ce2eaeaba582eaf541fdf643daa4f7b73b`.

Each downloaded platform artifact contains 12 TRX files. Aggregating every
`Counters` element gives `2588` total, executed, and passed tests with every
failed, error, timeout, aborted, inconclusive, passed-but-run-aborted,
not-runnable, not-executed, disconnected, warning, completed, in-progress, and
pending counter equal to zero:

| Hosted OS | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `99277381938` | `9734360071` | `c3579554dc9d82bb05a7a4d69aed278c63c8b22064c163540f30c0cf34247c5c` |
| macOS | `99277381956` | `9734343341` | `b0a486dba3bb2b907d5b30bbfa0d0feae598fb3c966ee4dd359b0fedc2f27d1b` |
| Linux | `99277382070` | `9734341611` | `7859202c78374092efa2e7d39e1bd0279d7584951af2f55fc1d86575f4e87834` |

Secret Scan job `99277381822` succeeded. Artifact `9734308966`, digest
`899df8da6df239903e771aa2adeafb621b3f40dd2f722ef68fd54329d53b1cf0`,
contains SARIF 2.1.0 with 208 Gitleaks rules and zero results.

All three reproducible version-`0.1.226` unsigned-package jobs completed two
seal/verify passes and their reproducibility comparison successfully:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `99277905436` | `9734382664` | `89cbc1412d3aecfdf01c360153fe01da5f0dd3298b6ced55e3dad8d5fe16b95b` |
| `osx-arm64` | `99277905691` | `9734383181` | `8061944dc68182fd59a8d5ff1184176ca8954f875cb68074260321ad188ebab4` |
| `win-x64` | `99277905500` | `9734391617` | `abb888a698c167c35db5973723249e6dd7a74947f474e78bc0bb221296f54916` |

CodeQL job `99277381879` succeeded. Exact-SHA analysis `1694372215`
evaluated 52 rules with zero results, and the exact-commit branch query returned
zero open alerts.

## Acceptance impact and explicit non-evidence

This exact checkpoint completes only Task 5.5a.3c. It adds one direct managed
contract example within the already-Partial **CL Timeout** and **CL
Cleanup-fault** cells. Neither cell, nor any other matrix cell, changes status.
The checkpoint adds no 43rd production-composed tracer execution.

Task 5.5a.3 remains open for fatal OOM, other late-failure combinations,
ordinary Stop throw and `FullyStopped == false`, timer creation, arm, release,
and callback faults, cleanup-completion winner and equality races,
lifecycle-gate contention, pre-generation cleanup, every other active or
pending initiator and owner, and the complete deterministic failure ledger.

This local evidence uses a managed Desktop harness on macOS. The hosted results
are managed and contract evidence on GitHub runners. Neither proves Windows,
macOS, or Linux native capture/input/protection/permission/Emergency APIs; a
physical two-Device path; packaged accessibility; a signed package; macOS
notarization; or release acceptance.

`CreateProduction()` must continue to report Remote Window unavailable. Tasks
5, 5.5a.3, 5.5a, 5.5, every later native/physical/release task, and the
long-term Flowspan Goal remain open.
