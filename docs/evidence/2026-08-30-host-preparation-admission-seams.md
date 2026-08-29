# Host Preparation source, route, and send admission seams checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Baseline: `fa70e63e2dc20f2d617897f5540fc6617e10d4f0`

Implementation and hosted evidence commit:
`3d27389de16bcdc43722ac3a94220511f563edb1`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint adds the narrow production-component admission seams required
to connect the standalone
[`RemoteWindowHostPreparationReservation`](2026-08-30-host-preparation-reservation-core.md)
to source invalidation, authenticated responder-route selection, and the actual
Transport Prepare send-admission point. It does not wire those seams through
`DesktopRemoteWindowHostCoordinator` at this exact commit.

No coverage state changes in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
H0 and H1 remain partial or missing, Task 5.5a remains unchecked, and
`CreateProduction()` must continue to report Remote Window unavailable.

## Platform source invalidation seam

`NativeRemoteWindowSourceRegistry` admits at most one internal Preparation
reservation for one exact source state. Source unregister, security-binding
change, and registry disposal close current source use admission, synchronously
invalidate and remove that Preparation slot under the source-state mutation
gate, and only then drain ordinary source callbacks outside the gate. An active
native-use scope does not delay this bounded Preparation invalidation; it still
delays the ordinary callback drain and source retirement it already owned.

The source tests prove single-slot reuse, invalidation-before-ordinary-callback
ordering, all three invalidation paths, invalidation while a use scope is active,
and late-registration rejection. They also inject a Preparation sink failure.
Unregister and security-binding mutation finish slot removal and ordinary
callback drain before rethrowing that failure. Registry disposal begins every
source invalidation in stable Activity order, clears the registry, drains all
callbacks, and then retains and rethrows the same raw or deterministically
ordered aggregate failure on repeated disposal without rerunning callbacks.

These tests prove the process-local source-registry boundary. They do not prove
that a Windows, macOS, Wayland, or X11 native source reported a real lifetime or
protection change.

## Authenticated route and Transport send seams

An authenticated connection generation now admits one bounded responder-route
operation. If revocation or owner cleanup wins first, route admission fails
before entering the media registry. If route admission wins, owner cleanup
waits the peer-connect and responder-route operations before disposing media
registration. Route side-effect-then-throw conservatively retains route
ownership and the original local failure, and a public or reserved operation
cannot claim the same route twice.

The internal reserved Prepare path invokes one synchronous
`IRemoteWindowHostPreparationAdmission` hook under the existing send-admission
and Preparation gates, after Stop, cancellation, and deadline checks and
immediately before the real wire send is counted and invoked. Hook rejection or
throw therefore produces zero wire call and zero active send. Once the hook
commits, a subsequent wire throw cannot roll back that admission.

The lease and session tests also freeze cancellation identity across the real
lease-to-session-to-wire token links: an exact caller cancellation is reissued
with the caller's original token, while a foreign-token cancellation remains
the same exception and token. The public Preparation-channel contract and the
protocol-1.7 wire schema do not change.

## TDD and review evidence

The Platform REDs first exposed the missing single source reservation slot and
mutation-gate callback. Failure injection then exposed a source-retirement path
that could remain installed when the synchronous sink threw; the repair always
removes and drains the source before retaining the failure.

The Transport REDs first exposed the missing generation-bound route operation
and send-admission hook. A later real wire test exposed nested linked-token
classification that could return an intermediate token instead of the original
caller token; normalization at both session and lease boundaries repaired it.

Independent strict review initially found one P1 in Platform source retirement
and one P1 in nested Transport cancellation. After both repairs, final review
returned APPROVE with 0 P0, 0 P1, and 0 P2 findings for these seam scopes.

## Local verification

```bash
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~NativeRemoteWindowSourceRegistryTests'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~NativeRemoteWindowSourceRegistryTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AuthenticatedRemoteWindowConnectionLeaseTests|FullyQualifiedName~RemoteWindowControlSessionConcurrencyTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AuthenticatedRemoteWindowConnectionLeaseTests|FullyQualifiedName~RemoteWindowControlSessionConcurrencyTests'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore
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

- focused Platform source-registry Debug and Release: `10/10` each;
- focused Transport lease/session Debug and Release: `32/32` each;
- Platform Debug and Release: `230/230` each;
- Transport Debug and Release: `718/718` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2328/2328` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed; and
- deterministic protocol-1.7 simulator: passed.

## Hosted exact-SHA evidence

[CI run `33280551919`](https://github.com/happys2333/flowspan/actions/runs/33280551919)
completed successfully at exact SHA
`3d27389de16bcdc43722ac3a94220511f563edb1`. Downloaded artifacts contain
exactly 12 TRX files per platform. Structured XML aggregation reports
`2328/2328` total, executed, and passed on each platform; failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable,
not-executed, disconnected, warning, completed, in-progress, and pending are all
zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99174884660` | `9722899747` | `f0d5d48b287115e7544fdb3451026de1389aa1af836738476cf8bec7d832e9c6` |
| Windows | `99174884761` | `9722896372` | `ca453eb0ba642b11075ebef3d0d1a4e84b41288b50d37009586d7a783d66888e` |
| Linux | `99174884531` | `9722885849` | `eab8012afe55e4cb36eac83b250a1ab1e350bb18b03cd4f5ec5f073cc6665a4d` |

Secret Scan job `99174884736` passed. Artifact `9722849555`, digest
`d77cbf81f735f93b4761f02c98d01ec4e4162772d0ba1e69a44b42428600db8e`,
contains SARIF 2.1.0 with one Gitleaks run, 208 rules, and 0 results.

Every reproducible unsigned package job passed its content lock, explicit TEST
MODE composition, seal verification, direct/transitive dependency audit, and
artifact upload:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `win-x64` | `99175363874` | `9722923945` | `d72368d668dc2ff3c1771650075bfb8c2187d5cb2de5f52a55711b4935721da9` |
| `osx-arm64` | `99175363890` | `9722920399` | `05f9ba36760f17b35cb807de0e7255cc75075e209db1265eb38481cb5c17edd4` |
| `linux-x64` | `99175363875` | `9722916546` | `d7e98745f2bcfdd5930805c97a3f4c6d9dfd2ff7e57a0ecbb704fbc802be1454` |

[CodeQL run `33280551900`](https://github.com/happys2333/flowspan/actions/runs/33280551900),
job `99174884585`, completed successfully. Exact-SHA analysis `1692761755`
evaluated 52 rules with 0 results, and the exact-commit branch query returned 0
open alerts.

The downloaded TRX and Secret Scan artifacts were parsed from a temporary local
verification directory; that directory is not durable project evidence. The
artifact IDs and GitHub digests above bind this record to the retained hosted
artifacts.

## Explicit limitations and next slice

At exact commit `3d27389`, the Desktop reservation core does not implement the
new Platform or Transport interfaces, and the coordinator does not register a
source Preparation guard or pass the reservation through route and Prepare.
The production-component tests therefore prove each seam independently, not one
source mutation ordered against one production-composed host route and wire
send. They do not prove complete `M < R`, `R < M < S`, and `S < M` orders across
Desktop, Platform, and Transport.

Permission revision, Trust/Capability mutation, authenticated-connection fact
invalidation beyond the route-operation gate, Emergency Stop readiness
reserve/promote, and exact protection epochs remain unimplemented. The complete
per-boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix
also remains open.

These hosted results are managed contract/build and reproducible unsigned-
package evidence. They are not Windows, macOS, or Linux native API results;
physical two-Device evidence; packaged interactive accessibility evidence;
signed or notarized package evidence; or release acceptance. H0, H1, Task 5.5a,
Task 5.5, all native/physical/release gates, and the long-term Goal remain open.
`CreateProduction()` must continue to report Remote Window unavailable.
