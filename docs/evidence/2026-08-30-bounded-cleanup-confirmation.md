# Bounded Remote Window cleanup-confirmation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`685225ed92b76ee2e6f4800b9c97f8baf2af378d`

Local environment: macOS 26.6.2 arm64, build 25G83, .NET SDK 10.0.301

Evidence classification: exact-commit local and hosted managed contract plus
same-host production-composed loopback evidence. Native and physical evidence
remains explicitly out of scope.

## Scope

This checkpoint implements the first narrow vertical from
[ADR 0028](../adr/0028-bounded-remote-window-cleanup-confirmation.md): one active
Remote Window host receives an independent authenticated terminal disconnect
while one real cleanup owner remains blocked. The checkpoint separates the
generation's complete real cleanup from the bounded confirmation awaited by
the coordinator and external callers.

The cleanup policy is process-local configuration:

- the default confirmation duration is 10 seconds;
- a configured duration must be positive and no greater than the fixed
  30-second maximum;
- one injected `TimeProvider` owns the watchdog, with
  `TimeProvider.System` as the default;
- one generation owns one real cleanup task, one confirmation completion, one
  stable `host_cleanup_timeout` instance, and at most one timer creation-and-arm
  attempt; and
- the watchdog never cancels the real cleanup task or supplies a cancellation
  token to an owner.

For an already-active generation, the terminal callback closes frame and media
admission, moves the exact generation from `active` to `retiring`, creates and
arms its cleanup-confirmation operation, and only then publishes deferred
worker work. The timer is therefore armed on the revocation callback thread,
before a queued cleanup worker can race the transition.

The real cleanup task continues to own callback retirement, Preparation
reservation release, controller Stop, Emergency Stop owner release, shared
connection fail-close, media/control/protection/connection disposal, final
Admission release, and every later independently safe cleanup step. A separate
confirmation task lets the lifecycle gate stop waiting at the deadline without
pretending those owners are released.

`CleanupConfirmationOperation` uses a private commit gate to serialize real
completion against timeout. The winning path commits its coordinator state and
diagnostic result before completing the shared confirmation. Timeout commits
the monotonic cleanup-unconfirmed latch and stable timeout failure together.
Real completion records any cleanup failure before clearing `retiring`. Every
Start, and Stop or Dispose that finds an already-retiring terminal generation,
joins or observes that same bounded confirmation rather than manufacturing a
second deadline.

When timeout wins, Start is rejected with `host_cleanup_unconfirmed` before
coordinator host-fact validation, fact reservation, route selection, Prepare,
capture, Admission publication, media, rendering, or input. Late successful
real cleanup releases its remaining owners and clears `retiring`, but never clears the latch
or replaces the already-published timeout instance. V1 has no reset transition
for that coordinator.

The terminal projection also detects a direct or nested
`OutOfMemoryException` and retains the first original fatal instance instead of
wrapping it as timeout or an ordinary aggregate. Exhaustive OOM, provider,
timer-release, and combined-failure evidence remains part of Task 5.5a.3 rather
than this checkpoint.

## TDD and review history

The implementation followed two observable RED stages.

1. The first test-only state did not compile. The new constructor call produced
   `CS1729` because `DesktopRemoteWindowHostCoordinator` did not yet accept the
   cleanup `TimeProvider` and confirmation duration.
2. After adding only that constructor policy, the behavior test compiled and
   reached the blocked cleanup owner, but its five-second observation bound
   expired while `TerminalFailure` remained null. This was the expected
   behavioral RED: real cleanup still held the lifecycle gate indefinitely and
   no production confirmation watchdog existed.

The first GREEN was not accepted as final. Strict read-only review identified
and the implementation repaired the following real defects:

- `active` to `retiring` initially occurred only after a queued worker acquired
  the lifecycle gate; it now occurs synchronously for an active terminal
  generation before worker publication;
- the real cleanup factory could run before the timer was armed; the operation
  now arms first and then starts cleanup without flowing callback
  `ExecutionContext`;
- ignored queue-return values could leave the real task unstarted; both worker
  publications now have owned fallbacks;
- timeout, real completion, retiring release, failure storage, and confirmation
  publication initially had visibility gaps; the commit gate and terminal-state
  gate now give them one linear order;
- Stop or Dispose could skip a pending confirmation after observing the latch;
  any existing retiring generation is now always joined; and
- arrival-ordered aggregation and empty observer catches could hide fatal
  exhaustion; terminal projection now gives the first nested OOM original
  identity priority and observes real completion independently.

The production-composed tracer review then removed three possible false
positives: it proves timer creation and revocation callback use the same thread,
pins protocol `1.7` rather than relying only on a moving minimum-version alias,
and gives every failure path bounded joins for real connection disposal and the
retiring generation before test teardown.

Review disposition also separated real defects from checkpoint-scope false
positives. Requiring this first row to complete Stop-first, Dispose-first,
pre-generation cleanup, arbitrary timer-provider failure, blocking timer
release, every completion/expiry combination, or the complete semantic failure
ledger would contradict ADR 0028's staged Task 5.5a.2/5.5a.3 split. Those items
were retained explicitly as open 5.5a.3 work rather than reported as evidence
from this row. A transient complaint that the unit checkpoint lacked a real
protocol/FSM1 trace was likewise not treated as production evidence; the
separate production-composed row described below was added before this
checkpoint was recorded.

Final strict review of the exact active-disconnect, blocked-connection-dispose,
healthy manual/System timer slice reported zero P0, P1, and P2 findings. Broader
termination and fault combinations map to the explicitly open 5.5a.3 scope and
are not counted as closed behavior.

## Deterministic coordinator contract

`TerminalCleanupWatchdogTimeoutReleasesGateAndPermanentlyBlocksRestartUntilTrueDrain`
uses a manual `TimeProvider` and a host connection whose disposal stops at a
test-owned barrier. It proves:

- the active Snapshot disappears, `retiring` is present, and exactly one timer
  is created and active before the revocation call returns;
- timer creation occurs on the revocation callback thread;
- frame admission is closed and the media budget is empty while connection
  disposal remains physically incomplete;
- at deadline minus one tick, confirmation, terminal failure, and replacement
  Start remain pending;
- at exact equality, the stable `host_cleanup_timeout` instance is published
  and replacement Start returns `host_cleanup_unconfirmed`;
- route, Prepare, Admission publication, capture, renderer preparation,
  permission/authorization/Emergency reservation, media, and input baselines do
  not advance for the rejected replacement;
- releasing the owner completes real cleanup, removes the sole timer, and
  clears `retiring` without changing the timeout instance or latch; and
- a second replacement Start after real drain is rejected before authority.

The test's `finally` path advances a still-pending manual watchdog, releases the
blocked owner, joins the replacement task, performs a bounded coordinator
Dispose, and waits for physical connection disposal plus `retiring` drain so a
failed assertion cannot leave the test-owned cleanup operation blocked.

## Production-composed managed tracer: row 42

`AuthenticatedControlDisconnectBlockedHostDisposeTimesOutAndPermanentlyBlocksRestart`
is the 42nd same-host managed production-composed tracer execution. It uses real
loopback TCP, authenticated protocol 1.7, and bilateral `FSM1` media attachment.
Before the terminal fault, the host reaches
final Admission, capture emits a frame, the frame traverses encrypted media,
and the participant decodes and renders it once.

The participant then independently disposes its authenticated control
connection. The host revocation callback synchronously closes authority and
arms the production cleanup watchdog. The host wrapper enters
`DisposeAsync` but waits on a test-owned barrier **before** it calls the inner
authenticated connection's `DisposeAsync`. This is direct evidence that one
real generation owner remains unsettled; cancellation or wrapper entry is not
misreported as physical cleanup.

Before disconnect, the test acquires a second host connection lease and proves
that its generation equals the original authenticated generation. That
same-generation lease is retained as a tripwire for the replacement Start. It
cannot manufacture a replacement authenticated generation or bypass the
coordinator latch. The rejected attempt disposes its supplied connection and
protection owners without selecting a route, sending Prepare, publishing
Admission, starting capture, preparing another renderer, creating new
Permission/Authorization/Emergency reservations, sending media, or injecting
input.

The manual provider advances first to deadline minus one tick. Confirmation and
replacement Start remain pending, the original inner connection remains
undisposed, `retiring` remains present, and the single timer remains active. At
exact equality, the timer publishes the exact stable timeout failure and wakes
the replacement waiter. The real task remains pending and the timer remains
owned until the blocked connection owner is released.

After release, the wrapper calls the real inner disposal and the same cleanup
task drains the remaining host and participant owner graph. The test observes
empty media budgets and directories, zero routes, no connected or reacquirable
authenticated peer generation, no control generation, disposed renderer and
protection, no permission observer or Preparation reservation, and no Emergency
readiness or registration. The participant session and listener are joined.
`retiring` and the timer then clear, but the timeout instance and sticky latch
remain. A second Start is rejected again, and coordinator Dispose returns the
same timeout instance.

This row proves one production-composed timeout order only. It does not prove
an arbitrary native or third-party owner can be forcibly terminated.

## Reproduction commands and local results

The following commands define the recorded local verification surface:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AuthenticatedControlDisconnectBlockedHostDisposeTimesOutAndPermanentlyBlocksRestart'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AuthenticatedControlDisconnectBlockedHostDisposeTimesOutAndPermanentlyBlocksRestart'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
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

Recorded results at exact implementation commit `685225e`:

- locked-mode solution restore: passed;
- focused row: `1/1` in Debug and `1/1` in Release;
- coordinator contract class: `116/116` in Debug and Release;
- production-composed managed tracer: `42/42` in Debug and Release;
- complete Desktop project: `720/720` in Debug and Release;
- complete solution: `2584/2584` in Debug and Release;
- warning-as-error solution builds: zero warnings and zero errors in Debug and
  Release;
- `dotnet format --verify-no-changes` and `git diff --check`: passed;
- explicit TEST MODE Desktop composition validation: passed;
- deterministic simulator: passed; and
- direct and transitive NuGet vulnerability audit: every project reported no
  known vulnerable package.

`gitleaks` is not installed on this local host. No local Secret Scan result is
claimed.

## Hosted status

[CI run `33311180093`](https://github.com/happys2333/flowspan/actions/runs/33311180093)
and [CodeQL run `33311180128`](https://github.com/happys2333/flowspan/actions/runs/33311180128)
both completed successfully for exact implementation
`685225ed92b76ee2e6f4800b9c97f8baf2af378d`.

Each downloaded platform test artifact contains 12 TRX files. Aggregating every
`Counters` element gives `2584` total, executed, and passed tests with every
failed, error, timeout, aborted, inconclusive, not-runnable, not-executed,
disconnected, warning, completed, in-progress, and pending counter equal to
zero:

| Hosted OS | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99256390781` | `9732055680` | `f66d090fc31c1c0eb3fc89d243031673b4ba919cb37f073087b6eb0ab8b84ce9` |
| Windows | `99256390796` | `9732075760` | `408b363ff73ed94190ae192689f5384f9dc10fc564fad872ac950ddcc44d5ae5` |
| Linux | `99256390787` | `9732058646` | `500fbcb560996a616eaa1c08ddb4e7e7cebea02c19cec0d60213af94114fb5c3` |

Secret Scan job `99256390658` succeeded. Artifact `9732018175`, digest
`3d63b7ec9097678f227f0084237a0fa537436ded3f13c02ea4b720fa08f40fd4`,
contains SARIF 2.1.0 with 208 Gitleaks rules and zero results.

All reproducible unsigned-package jobs succeeded:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `osx-arm64` | `99256953596` | `9732092145` | `61412736e7b7fb41fe67f4756f38f53a8b14812cd6e98935ca4847922ac7fda1` |
| `win-x64` | `99256953607` | `9732104131` | `9d0508df7fc3a3b646bfa986ed344224831be4c206c356ec56840565927a7539` |
| `linux-x64` | `99256953658` | `9732096968` | `6076442754bfc4beae90c44200e7c5dc0373e41e4ae3bbf1d0f6db3aeaae7e51` |

CodeQL job `99256390416` succeeded. Exact-SHA analysis `1694054320`
evaluated 52 rules with zero results, and the exact-commit branch query returned
zero open alerts.

These hosted results remain managed runner and contract evidence. They do not
prove native capture/input/protection/permission/Emergency Stop behavior or a
physical two-Device path.

## Acceptance impact and explicit non-evidence

This exact checkpoint moves only production-boundary matrix cell **CL Timeout**
from Missing to Partial and completes only Task 5.5a.2. It does not complete CL,
Task 5.5a, Task 5.5, or the accepted ADR 0028 contract as a whole.

Task 5.5a.3 remains open for explicit Stop-first and Dispose-first initiation,
concurrent terminators, cleanup-completion winner and equality races, timer
creation/arm/disposal failure, blocking timer release, late non-fatal cleanup
failure, fatal OOM combinations, every other active or pending owner, the
deterministic full failure ledger, and equivalent bounded pre-generation
cleanup.

This evidence is same-host managed loopback on macOS. It proves no Windows or
Linux real-machine behavior, no Windows/macOS/Linux native capture/input/
protection/Emergency API, no physical two-Device operation, no signed package,
no macOS notarization, and no release acceptance. The native, physical,
packaging, signing, notarization, accessibility, and release gates remain open.

`CreateProduction()` must continue to report Remote Window unavailable. Tasks
5, 5.5a, 5.5, the later native/physical/release tasks, and the long-term
Flowspan Goal remain open.
