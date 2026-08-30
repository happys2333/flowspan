# Explicit Stop-first bounded cleanup checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`681842290d44f9524eab33550b307bad76017fbc`

Local environment: macOS 26.6.2 arm64, build 25G83, .NET SDK 10.0.301

Evidence classification: exact-commit local and hosted managed Desktop contract
evidence. This checkpoint adds no production-composed tracer row and contains no
native or physical-device evidence.

## Scope

This checkpoint implements Task 5.5a.3b, the explicit Stop-first extension of
[ADR 0028](../adr/0028-bounded-remote-window-cleanup-confirmation.md). Its scope
is deliberately limited to one stable already-active generation, an
uncontended coordinator lifecycle gate, a healthy manual cleanup-confirmation
timer, and two separate deterministic controller Stop scenarios.

Explicit Stop closes frame and media admission, moves the generation from
`active` to `retiring`, and publishes that generation's single real cleanup
task, bounded confirmation, and watchdog before invoking controller Stop or any
other potentially blocking owner. The exact caller token applies only to the
first controller Stop attempt. It never cancels the confirmation operation,
fallback Stop, or later owner release.

If the first controller Stop throws, observes the exact caller cancellation, or
returns `FullyStopped == false`, the original outcome remains the terminal
primary and the same real task invokes exactly one fallback Stop with
`CancellationToken.None`. A first `FullyStopped == true` result performs no
fallback. Public Stop waits for bounded confirmation and then exposes the
generation-owned outcome rather than a caller-local result.

## TDD and review history

Both new rows were first run against the preceding `f65de5c` tree. They failed
with `HasRetiringGeneration == false`: the old explicit Stop path called the
controller before publishing the retiring generation, real cleanup operation,
and watchdog. Those early failures prevented the later token assertions from
running. After GREEN, two targeted mutation checks changed the first attempt to
`CancellationToken.None` and the fallback to the cancelled caller token; each
failed at its exact token assertion. Together these are the behavioral RED and
token-routing mutation evidence.

The GREEN moves the explicit initial Stop into the generation-owned cleanup
operation and introduces one internal controller-Stop boundary so the tests can
observe exact token and attempt ownership without replacing the production
controller. Strict spec/standards, test-design, and state/lock reviews reported
zero P0, P1, or P2 findings for this bounded slice.

The reviews retain concurrent Stop/Dispose/callback precedence, ordinary throw,
`FullyStopped == false`, lifecycle-gate contention, cleanup-completion winner
and equality races, timer creation/arm/release/callback faults, late cleanup
failure or OOM, pre-generation cleanup, and every other blocked owner as later
Task 5.5a.3 work.

## Caller-cancellation and fallback contract

`StopFirstCallerCancellationRunsOneFallbackAndPreservesTheExactToken` starts an
active DriverEligible generation and holds one admitted input operation so the
real cleanup task remains observable. It proves:

- the retiring generation, closed authority, empty active media budget, one
  real cleanup operation, and sole watchdog are published before the first
  controller Stop attempt;
- that first attempt receives the exact caller token, while a post-claim frame
  is disposed without a second media transmission;
- cancellation after publication produces exactly one second controller Stop
  attempt with `CancellationToken.None`;
- caller cancellation does not cancel the watchdog, fallback, pending input
  drain, or any later owner release;
- at deadline minus one tick, public Stop and the real task remain pending with
  the single timer still active;
- releasing the input lets the fallback and complete owner graph settle before
  expiry, releases the timer, and preserves the exact original
  `OperationCanceledException` and caller token as the terminal primary;
- repeated observation of public Stop and later coordinator Dispose exposes
  that same exception instance; and
- replacement Start is rejected with `host_cleanup_unconfirmed` before new
  authority, while the replacement-supplied owners are disposed.

The final state has one initial Stop and one fallback, one capture/input Stop,
one fail-close and Connection disposal, released Preparation registrations,
closed control authority, an empty media budget, no Permission observer or
Emergency Stop registration, and no retiring generation or live timer.

## Blocking Stop and timeout contract

`StopFirstCleanupTimeoutIsStableWhileControllerStopBlocksAndAfterLateDrain`
blocks the first controller Stop inside the capture boundary. It proves:

- the same admission close, `active -> retiring` transition, cleanup operation,
  confirmation, and sole watchdog are published before controller Stop can
  block;
- the first and only Stop attempt receives `CancellationToken.None`, control
  and media authority are closed, and a post-claim frame is not transmitted;
- at deadline minus one tick, public Stop, the real cleanup task, retiring
  generation, and timer all remain pending with no terminal failure;
- exact equality publishes one stable `host_cleanup_timeout`, releases the
  bounded public waiter, and leaves the real task and blocked owner live;
- replacement Start is rejected with `host_cleanup_unconfirmed` before new
  authority;
- releasing the blocked Stop returns `FullyStopped == true`, performs no
  fallback, and lets the same real task drain every remaining owner; and
- late true drain clears the retiring generation and timer without replacing
  the completed timeout result or allowing the original Stop to be observed as
  successful.

Both tests have defensive teardown that releases every test-owned barrier,
advances a still-pending watchdog when necessary, and boundedly observes Stop,
Connection disposal, coordinator disposal, and retiring-generation drain.

## Reproduction commands and local results

The following commands define the recorded local verification surface:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~StopFirst'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~StopFirst'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~StopFirst' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~StopFirst' || exit 1; done
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

Recorded results at exact implementation commit `6818422`:

- the two focused rows: `2/2` in Debug and `2/2` in Release;
- twenty fresh focused processes: `40/40` case executions in Debug and `40/40`
  in Release;
- coordinator contract class: `119/119` in Debug and Release;
- complete Desktop project: `723/723` in Debug and Release;
- complete solution: `2587/2587` in Debug and Release;
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

[CI run `33317026854`](https://github.com/happys2333/flowspan/actions/runs/33317026854)
(run 224, attempt 1) and
[CodeQL run `33317026837`](https://github.com/happys2333/flowspan/actions/runs/33317026837)
(run 224, attempt 1) both completed successfully for exact implementation
`681842290d44f9524eab33550b307bad76017fbc`.

Each downloaded platform artifact contains 12 TRX files. Aggregating every
`Counters` element gives `2587` total, executed, and passed tests with every
failed, error, timeout, aborted, inconclusive, passed-but-run-aborted,
not-runnable, not-executed, disconnected, warning, completed, in-progress, and
pending counter equal to zero:

| Hosted OS | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `99272246618` | `9733833303` | `c71f55823ec79c9fe549fa48c34d2d52b5cee45896cbc1f4cb3e33dc47d52d67` |
| macOS | `99272246628` | `9733812221` | `d61576aef1ca609d06fd5db231484a873a0bf553428a1849ece5f0f994ec7f56` |
| Linux | `99272246591` | `9733810185` | `fdc937ff0e2da812eedbfd6feb04299819989801b598dcf8597d84e0a03cea04` |

Secret Scan job `99272246479` succeeded. Artifact `9733761572`, digest
`c5bc97e19961ce3151973f61b634ba6822db86c5fcf407443356665ca2ea9458`,
contains SARIF 2.1.0 with 208 Gitleaks rules and zero results.

All three reproducible version-`0.1.224` unsigned-package jobs completed two
seal/verify passes and their reproducibility comparison successfully:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `99272944491` | `9733855103` | `30b6c183173e8d966fdd0b127fb871ef948c866235aa8104a46d527dc509b52c` |
| `osx-arm64` | `99272944497` | `9733853252` | `729ee21748cf14c73e5ad5055b1c6a9b03dc2ab230692d6e2721e1a43a7fbee6` |
| `win-x64` | `99272944518` | `9733861284` | `d29b0cecf229e3e90106e59c5999fa66670df4c685d98aaee1923fcf447435c5` |

CodeQL job `99272246351` succeeded. Exact-SHA analysis `1694297443`
evaluated 52 rules with zero results, and the exact-commit branch query returned
zero open alerts.

## Acceptance impact and explicit non-evidence

This exact checkpoint completes only Task 5.5a.3b. The caller-cancellation row
adds direct managed evidence within the already-Partial **CL Cancel** cell, and
the blocked-Stop row adds direct managed evidence within the already-Partial
**CL Timeout** cell. Neither cell, nor any other matrix cell, changes status.
The checkpoint adds no 43rd production-composed tracer execution.

Task 5.5a.3 remains open for concurrent Stop/Dispose/callback precedence,
ordinary throw and `FullyStopped == false` combinations, lifecycle-gate
contention, cleanup-completion winner and equality races, timer creation, arm,
release, and callback faults, late non-fatal cleanup failure, fatal OOM,
pre-generation cleanup, every other active or pending owner, and the complete
deterministic failure ledger.

This local evidence uses a managed Desktop harness on macOS. The hosted results
are managed and contract evidence on GitHub runners. Neither proves Windows,
macOS, or Linux native capture/input/protection/permission/Emergency APIs; a
physical two-Device path; packaged accessibility; a signed package; macOS
notarization; or release acceptance.

`CreateProduction()` must continue to report Remote Window unavailable. Tasks
5, 5.5a.3, 5.5a, 5.5, every later native/physical/release task, and the
long-term Flowspan Goal remain open.
