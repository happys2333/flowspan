# Host Preparation source linearization checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Baseline: `e862091517e551edba04423a87950b5bde07ede5`

Implementation and hosted evidence commit:
`ec63942296175f63964d8f463335d6b621e22042`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint connects the existing Desktop
`RemoteWindowHostPreparationReservation` to the Platform source invalidation
slot, authenticated responder-route operation, actual Transport Prepare
send-admission hook, Ready match, and host promotion path. The same reservation
instance now follows one coordinator generation through those production
components.

This is a source-fact vertical slice. It proves the production-composed managed
success path and one exact `R < M < S` source-mutation path. It does not prove
the complete production-composed `M < R` or `S < M` source paths, and it does
not implement the exact Permission, Trust/Capability, authenticated Connection
mutation, Emergency Stop reserve/promote, or Protection fact reservations.

No aggregate status changes in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
H0 and H1 remain partial or missing, Task 5.5a remains unchecked, and
`CreateProduction()` must continue to report Remote Window unavailable.

## Production-composed source reservation

The host coordinator creates the exact protocol-1.7 Preparation request and one
monotonic host Preparation generation before arming the reservation. It
registers that same reservation in the exact source lease's single atomic
Preparation slot before the early safety observers and point-in-time preflight
checks run. After those checks it arms the reservation, passes it through the
generation-bound authenticated responder-route operation, and passes it again
to the actual Transport Prepare send-admission hook.

The Platform source registry invokes the reservation's bounded source
invalidation while it owns the source-state mutation gate. The reservation does
not call outward while holding its own gate. The authenticated route boundary
orders the connection generation before invoking the media-route registry, and
the Transport hook runs under the send-admission and Preparation gates
immediately before the real wire send is counted and invoked. This composes the
lock order without holding a source-use scope across network Preparation.

After one exact Ready response, the coordinator asks the same reservation to
match the request binding. It retains the source Preparation registration
through the post-Ready source/protection revalidation and formal safety-owner
installation, then promotes the reservation before releasing the temporary
source guard. Cleanup derives its fail-close decision from the reservation's
conservative `RouteMayBeOwned` snapshot. A source invalidation at or after
route admission therefore consumes and closes the owning authenticated
connection even when no Prepare is delivered.

Source terminal failures use only `native_source_stale`. A route or wire
failure racing a source invalidation cannot replace that stable source reason
with injected exception text. An exact caller-token cancellation still retains
its original exception and token when it wins the same boundary.

## Deterministic source-order evidence

The focused host-coordinator class covers source invalidation before route,
after route admission, during a route side-effect failure, after Prepare send
admission, during a Prepare wire failure, and concurrently with exact caller
cancellation. The post-route cases require zero capture and complete one
connection fail-close and disposal. These coordinator tests use a faithful
connection double for exact admission-point injection; they do not by
themselves prove the real authenticated route and wire components.

`SourceInvalidationAfterReservedRoutePreventsPrepareWireAndDrains` supplies the
production-composed `R < M < S` row. It establishes real loopback TCP,
authenticated protocol 1.7, the production connection lease, and a responder
media route. The test pauses after the reserved route is selected and before
the real Prepare forward, unregisters the exact source through the production
source registry, and observes one terminal Source fact with
`native_source_stale` and `ConsumeConnection` cleanup scope.

The later real Transport Prepare send-admission attempt runs once and returns
NotDelivered without admitting a Prepare wire send. Participant receive policy,
media attachment wait, capture, encrypted media send, renderer preparation,
render, and final Admission all remain zero. The old source generation cannot
be reacquired. Host fail-close and connection disposal each run once, and both
control handlers, media directories, routes, leases, channel, controller, and
coordinator state drain without resurrection.

The existing
`DriverEligibleWindowTraversesManagedTwoNodeProductionPathAndCleansUp` success
row now traverses the same reservation, reserved authenticated route, real
Prepare send-admission hook, Ready match, source-guard transfer, and promotion
path before encrypted media, input, Emergency Stop, and complete cleanup.

## TDD and review evidence

The focused coordinator and production-composed tracer rows were introduced
against the previously unwired Desktop interfaces and then passed after the
same reservation was threaded through the source, route, send, Ready, promotion,
and cleanup boundaries. The source mutation remains synchronous and bounded;
the reservation never executes external cleanup or callbacks under its state
gate.

Final independent strict review reported APPROVE with 0 P0, 0 P1, and 0 P2
findings for this source-linearization scope.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~SourceInvalidationAfterReservedRoutePreventsPrepareWireAndDrains'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~SourceInvalidationAfterReservedRoutePreventsPrepareWireAndDrains'
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

- focused host-coordinator Debug and Release: `44/44` each;
- focused new production-composed tracer Debug and Release: `1/1` each;
- Desktop Debug and Release: `596/596` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2334/2334` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed; and
- deterministic protocol-1.7 simulator: passed.

## Hosted exact-SHA evidence

[CI run `33281547016`](https://github.com/happys2333/flowspan/actions/runs/33281547016)
completed successfully at exact SHA
`ec63942296175f63964d8f463335d6b621e22042`. Downloaded artifacts contain
exactly 12 TRX files per platform. Structured XML aggregation reports
`2334/2334` total, executed, and passed on each platform; failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable,
not-executed, disconnected, warning, completed, in-progress, and pending are all
zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99177440677` | `9723159266` | `17a84797bef6d804c0d94b477868138cdf65ea3cbc9500f52d9a3a7488683082` |
| Windows | `99177440651` | `9723173567` | `5285107e018b1100afab953abaa6130405c91860f982846898a667f2b14bbf00` |
| Linux | `99177440621` | `9723165713` | `11a9d06ba444f764e66f85142f9c75a3af74422f65db7475bdd08ed2ce53a96a` |

Secret Scan job `99177440547` passed. Artifact `9723129418` has GitHub digest
`971d7a0e725b90cb3ff8a143294d9776fd39d24fb58a0440971816fbce8c8ea7`.
Its `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
and contains SARIF 2.1.0 with one Gitleaks run, 208 rules, and 0 results.

Every reproducible unsigned package job passed its content lock, explicit TEST
MODE composition, seal verification, direct/transitive dependency audit, and
artifact upload:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `win-x64` | `99177871763` | `9723194111` | `406653da63b9be6453d84e7751133b385cc6c76f95f308975f615be64622fc79` |
| `osx-arm64` | `99177871791` | `9723188244` | `201e9692095af309aae554e80f637b7c9adfb19694eea8a2371a08f51bc3d9f7` |
| `linux-x64` | `99177871769` | `9723190048` | `ee3179cdad603dab92ead1e0b2907b7f3eb6db31a662dac2159533b4894693cb` |

[CodeQL run `33281546949`](https://github.com/happys2333/flowspan/actions/runs/33281546949),
job `99177440348`, completed successfully. Exact-SHA analysis `1692797187`
evaluated 52 rules with 0 results, and the exact-commit branch query returned 0
open alerts.

The downloaded TRX and Secret Scan artifacts were parsed from temporary local
verification directories; those directories are not durable project evidence.
The artifact IDs, GitHub digests, and payload digest above bind this record to
the retained hosted artifacts.

## Explicit limitations and next slices

This exact commit proves Source `R < M < S` through the production-composed
managed source registry, coordinator, authenticated route, and Prepare
send-admission path. It also proves the success path uses the same reservation.
It does not yet provide a production-composed tracer for Source `M < R` or
`S < M`; the complete reject/throw/cancel/timeout/revoke/disconnect/
cleanup-fault matrix at every boundary remains open.

Permission revision, exact Trust/Capability mutation, authenticated Connection
fact invalidation at its mutation gate, Emergency Stop readiness reserve/promote,
and exact Protection epochs remain unimplemented. Their standalone point reads
or callbacks cannot be promoted to reservation evidence by inference.

These hosted results are managed contract/build and reproducible unsigned-
package evidence. They are not Windows, macOS, or Linux native API results;
physical two-Device evidence; packaged interactive accessibility evidence;
signed or notarized package evidence; or release acceptance. Task 5, Task 5.5a,
Task 5.5, all native/physical/signing/notarization/release gates, and the
long-term Goal remain open. `CreateProduction()` must continue to report Remote
Window unavailable.
