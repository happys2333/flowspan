# External Dispose-first bounded cleanup checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`ea984fb01cad46ab128c6d294835df59327aa8ac`

Local environment: macOS 26.6.2 arm64, build 25G83, .NET SDK 10.0.301

Evidence classification: exact-commit local and hosted managed Desktop contract
evidence. This checkpoint adds no production-composed tracer row and contains no
native or physical-device evidence.

## Scope

This checkpoint implements Task 5.5a.3a, the first explicit external
Dispose-first extension of
[ADR 0028](../adr/0028-bounded-remote-window-cleanup-confirmation.md). Its scope
is deliberately limited to a stable already-active generation, an uncontended
coordinator lifecycle gate, one blocked host Connection disposal owner, and a
healthy manual cleanup-confirmation timer.

The first external `DisposeAsync` call sets the disposed gate before returning.
After its worker obtains lifecycle ownership, it synchronously closes frame and
media admission, moves the published generation from `active` to `retiring`,
publishes that generation's single real cleanup task and bounded confirmation,
and creates and arms the watchdog before any potentially blocking controller or
owner call. The public Dispose task awaits only the shared bounded confirmation;
it does not falsely complete the blocked Connection owner.

A later authenticated-disconnect callback may still execute its existing
synchronous Emergency Stop safety prefix. It cannot create a second cleanup
operation, timer, or owner graph: its terminal cleanup path attaches to the
already-published retiring generation. Callback-origin Dispose keeps the
existing non-waiting recursion rule while initiating or joining the same public
operation for later external callers.

At timeout, all concurrent and later external Dispose callers observe the same
public Task and the same `host_cleanup_timeout` exception instance. Because the
coordinator is already explicitly disposed, Start preserves normal
`ObjectDisposedException` precedence before any authority rather than exposing
`host_cleanup_unconfirmed`. Late real cleanup drains the owner and releases the
timer but cannot mutate the completed public Dispose result.

## TDD and review history

The focused test was first run against the preceding implementation. It reached
the blocked Connection owner but failed because the old Dispose path awaited raw
cleanup: the generation was not published as `retiring` and no bounded timer
existed. That was the behavioral RED.

The GREEN routes external disposal through the same generation-owned terminal
cleanup operation used by terminal callbacks. Follow-up review removed a
test-only production callback hook and replaced caller-thread affinity as an
ordering proxy with observable production state. The final test instead proves
that the timer and retiring operation are published before the first external
Dispose call returns, and uses the capture Emergency Stop entry barrier to show
that the later revocation callback is active before it attaches.

Independent spec/standards, test-design, and state/lock reviews reported APPROVE
with no P0, P1, or P2 finding in this bounded slice. The reviewers retained
Stop-first, lifecycle-gate contention, timer faults, late cleanup fault/OOM,
pre-generation cleanup, other owners, and broader race combinations as explicit
later Task 5.5a.3 work.

## Deterministic coordinator contract

`DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain`
uses `ReadyHostHarness`, a manual `TimeProvider`, and a host Connection whose
`DisposeAsync` stops at a test-owned barrier. It proves:

- the disposal worker closes admission and publishes `active -> retiring`, the
  one real cleanup operation, and the sole watchdog before the first external
  Dispose API call returns;
- a cross-thread authenticated-disconnect callback enters its existing local
  Emergency Stop safety prefix and attaches exactly once to the same retiring
  operation;
- Snapshot and active media authority disappear, the control generation closes,
  the pending media budget drains, and a post-claim frame is not transmitted;
- Connection fail-close and disposal, controller Stop, capture/input Emergency
  Stop, Protection disposal, Permission observer removal, and Emergency Stop
  registration removal each begin or settle according to their existing owner
  contract without creating replacement authority;
- Start while disposal is pending returns a bounded
  `ObjectDisposedException`, and route, Prepare, Admission publication, capture,
  input, Authorization, Permission, and Emergency Stop reservation counts do not
  advance;
- at deadline minus one tick, both external Dispose callers and real cleanup
  remain pending with one active timer and no terminal failure;
- at exact equality, one stable `host_cleanup_timeout` instance completes the
  shared public Dispose Task;
- concurrent, later, and post-drain external Dispose calls return the same Task
  and throw that same exception instance; and
- releasing the blocked Connection lets the one real cleanup task clear
  `retiring` and release the timer without changing the public result or allowing
  a later Start.

The test's `finally` path advances a still-pending watchdog when needed, releases
the Connection barrier, creates the disposal operation if an earlier assertion
prevented it, and boundedly observes public disposal, revocation, Connection
disposal, and retiring-generation drain.

## Reproduction commands and local results

The following commands define the recorded local verification surface:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain' || exit 1; done
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

Recorded results at exact implementation commit `ea984fb`:

- focused row: `1/1` in Debug and `1/1` in Release;
- twenty fresh focused processes: `20/20` in Debug and `20/20` in Release;
- coordinator contract class: `117/117` in Debug and Release;
- complete Desktop project: `721/721` in Debug and Release;
- complete solution: `2585/2585` in Debug and Release;
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

[CI run `33314229467`](https://github.com/happys2333/flowspan/actions/runs/33314229467)
(run 222, attempt 1) and
[CodeQL run `33314229459`](https://github.com/happys2333/flowspan/actions/runs/33314229459)
both completed successfully for exact implementation
`ea984fb01cad46ab128c6d294835df59327aa8ac`.

Each downloaded platform artifact contains 12 TRX files. Aggregating every
`Counters` element gives `2585` total, executed, and passed tests with every
failed, error, timeout, aborted, inconclusive, passed-but-run-aborted,
not-runnable, not-executed, disconnected, warning, completed, in-progress, and
pending counter equal to zero:

| Hosted OS | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `99264566335` | `9732971936` | `25ec806dbe7bf0f49c1314767ad7101d1fe00d321a125a333a72dc3da5d4d36f` |
| Linux | `99264566364` | `9732957815` | `391ab26587132d546d0c5dc8c61d093c6cd892c64ffe3ad960143970b5b5a7ce` |
| macOS | `99264566406` | `9732960864` | `a42c1929301a98720b3e26e6be0927f9967667403ee2ce5ac5b62ebde3e002b1` |

Secret Scan job `99264566292` succeeded. Artifact `9732929375`, digest
`bfd71a3d5fe1a9b156a0052e5b8f87d0f3b51cc850048cb6709e75b8b20f2723`,
contains SARIF 2.1.0 with 208 Gitleaks rules and zero results.

All three reproducible version-`0.1.222` unsigned-package jobs completed two
seal/verify passes and their reproducibility comparison successfully:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `99264998683` | `9732994336` | `105c6b90d5681820991fad25d65fe0be6ec2bc3bc323c5d576da8eeb2ef43e28` |
| `osx-arm64` | `99264998721` | `9732992369` | `3e63837b48d234cc9b788c2c74307ba4f0b95762f9bebcc7f6467cfedc4bab40` |
| `win-x64` | `99264998722` | `9732998610` | `6d9abbeb731398057e3bca6c853107a14992e5d62b685f36571f40ef82382053` |

CodeQL job `99264566252` succeeded. Exact-SHA analysis `1694176205`
evaluated 52 rules with zero results, and the exact-commit branch query returned
zero open alerts.

## Acceptance impact and explicit non-evidence

This exact checkpoint completes only Task 5.5a.3a. It adds another direct
managed contract example within the already-Partial **CL Timeout** cell; it does
not promote that cell, any other matrix cell, or the 42-case production-composed
tracer count.

Task 5.5a.3 remains open for explicit Stop-first semantics, lifecycle-gate
contention, cleanup-completion winner and equality races, timer creation/arm/
disposal failure, blocking timer release, late non-fatal cleanup failure, fatal
OOM combinations, every other active or pending owner, the deterministic full
failure ledger, and equivalent bounded pre-generation cleanup.

This local evidence uses a managed Desktop harness on macOS. The hosted results
are managed and contract evidence on GitHub runners. Neither proves Windows,
macOS, or Linux native capture/input/protection/permission/Emergency APIs; a
physical two-Device path; packaged accessibility; a signed package; macOS
notarization; or release acceptance.

`CreateProduction()` must continue to report Remote Window unavailable. Tasks
5, 5.5a.3, 5.5a, 5.5, every later native/physical/release task, and the
long-term Flowspan Goal remain open.
