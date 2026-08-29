# Host Preparation reservation core checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit: `294042fdfcc346e3eade3551d57cc7ccba95c601`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint implements and verifies only the process-local
`RemoteWindowHostPreparationReservation` core in `Flowspan.Desktop`. It is not
wired into `DesktopRemoteWindowHostCoordinator`, `Flowspan.Platform`,
`Flowspan.Security`, `Flowspan.Transport`, or `CreateProduction()`.

The core therefore changes no status in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
In particular, H0 and H1 remain partial or missing, Task 5.5a remains unchecked,
and production Remote Window remains unavailable.

No hosted exact-SHA run has been started for `294042f`. Every result in this
record is local macOS managed-contract evidence only.

## Implemented core contract

The internal Desktop state machine has one monotonic path:

```text
Collecting
  -> Armed
  -> RouteAdmitted
  -> RouteSelected
  -> PrepareSending
  -> ReadyMatched
  -> Promoted
```

Any pre-promotion phase can instead enter the single irreversible `Terminal`
phase. `TryArm`, route admission, Prepare send admission, Ready matching, and
promotion each reject deadline equality. Before `RouteAdmitted`, terminal work
has `PreRoute` cleanup scope. Route admission conservatively changes the scope
to `ConsumeConnection`, so a route operation that performs a side effect and
then throws cannot be mistaken for an unpublished pre-route failure.

One epoch bundle contains six independent opaque process-local fact epochs:

- Source;
- Permission;
- Authorization;
- authenticated Connection;
- Emergency Stop readiness; and
- Protection.

The bundle can be claimed by exactly one host reservation. A second reservation
cannot reuse it. A callback must match both the host generation and the exact
fact epoch; an old generation or old epoch cannot invalidate a replacement.
Regrant or replacement cannot re-arm a terminal reservation.

The core derives invalidation reasons from a closed fact enum instead of taking
caller text:

| Fact or terminal boundary | Stable reason |
| --- | --- |
| Source | `native_source_stale` |
| Permission | `native_permission_denied` |
| Authorization | `mirror_capability_denied` |
| Connection | `authenticated_connection_stale` |
| Emergency Stop | `emergency_stop_readiness_unavailable` |
| Protection | `native_protection_not_safe` |
| route failure | `responder_route_failed` |
| deadline equality/expiry | `preparation_expired` |
| foreign Ready binding | `remote_window_ready_mismatch` |
| unpromoted disposal | `host_preparation_disposed` |

A well-formed protocol Ready rejection retains its already validated protocol
reason. Arbitrary exception or callback text is not an input to the fact
invalidation surface. The terminal completion uses
`RunContinuationsAsynchronously`; the state gate does not invoke external
callbacks, cancellation, disposal, native work, or wire work.

## Deterministic ordering coverage

`RemoteWindowHostPreparationReservationTests` contains nine tests. They use
bounded waits, `LongRunning` workers, `Barrier`, and asynchronous completion
sources without `Thread.Sleep` correctness assumptions. The suite proves:

- `M < R`: fact invalidation before route admission prevents route selection;
- `R < M < S`: invalidation while an admitted route operation is blocked makes
  cleanup consume the connection and prevents Prepare send admission;
- `S < M`: Prepare send admission is irreversible, while the later invalidation
  still makes all later work terminal;
- route side-effect-then-throw retains `ConsumeConnection` cleanup scope;
- deadline equality fails at Arm, route admission, Prepare send admission,
  Ready matching, and promotion with the correct pre-route or post-route scope;
- a stale host generation or stale fact epoch cannot invalidate a replacement,
  and one epoch bundle cannot be claimed twice;
- all six simultaneous fact invalidations publish one terminal result and one
  winning fact/reason;
- Ready and promotion require their exact phase and exact request binding; and
- promotion is terminal-success ownership transfer: later fact invalidation or
  reservation disposal cannot turn it into a failure.

This proves the standalone state machine's ordering. It does not prove that a
real source invalidation, Trust mutation, permission revision, authenticated
connection revocation, Emergency Stop registrar, or protection observation
currently reaches the matching fact epoch at that subsystem's mutation
linearization point.

## TDD and review evidence

The implementation followed independent RED-to-GREEN slices. The recorded REDs
included:

- the missing reservation core and transition interface;
- Prepare deadline equality rejecting send without completing terminal state;
- a foreign Ready binding returning false without terminal fail-close;
- reuse of one epoch bundle by a second reservation;
- a late attacker/canary reason reaching validation after another terminal
  transition, creating a throw/leak surface; and
- construction beginning in `Armed`, which allowed route work before an
  explicit fact-collection commit.

Repairs added explicit `Collecting`/`TryArm`, single-claim bundles, deadline
checks at every authority-relevant transition, enum-derived fact reasons,
early terminal/generation checks, and exact Ready matching.

Strict review initially returned **BLOCK** with one P1 and two P2 findings. The
bundle-reuse, Collecting/arming, deadline, and late reason-validation/redaction
repairs were reviewed again. Final review returned **APPROVE** with 0 P0, 0 P1,
and 0 P2 findings for this Desktop-only core scope.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~RemoteWindowHostPreparationReservationTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~RemoteWindowHostPreparationReservationTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj --configuration Release --no-build --no-restore
git diff --check
```

Results:

- focused reservation core Debug and Release: `9/9` each;
- Desktop Debug and Release: `590/590` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2304/2304` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed; and
- deterministic protocol-1.7 simulator: passed.

## Explicit limitations and next slice

Commit `294042f` adds no source-registry reservation, permission reservation,
Trust/Capability generation, authenticated route operation, Emergency Stop
readiness promotion, protection epoch adapter, or Transport send-admission hook.
The coordinator still uses its existing point reads and callbacks. The core has
therefore not crossed a real H0/H1 production boundary and cannot change a
matrix cell by inference.

The next implementation must be a real vertical connection of source
invalidation to a generation-bound authenticated route operation and the actual
Transport Prepare send-admission hook. It must prove both orders at the real
Desktop/Platform/Transport seams before the other fact adapters are added.

Task 5.5a, Task 5.5, native adapters, physical two-device evidence, packaged
accessibility, signing, notarization, release criteria, and the long-term Goal
remain open. `CreateProduction()` must continue to report Remote Window
unavailable.
