# Host Permission Preparation reservation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Previous exact Trust/Capability checkpoint:
`635dc23ec0c8f2812d527e16135b3d9c40885788`

Permission implementation and hosted evidence commit:
`d607ed1c3217c9c4102c4b893d20da9a6845f02d`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint replaces the host's point-in-time pre-Prepare permission read
with one synchronous, prompt-free Permission Preparation reservation. The
reservation binds the exact permission owner generation, revision, capture and
input facts, and frozen participant role under the permission boundary's
authoritative observation-commit gate. A later committed permission revision
makes the old host Preparation generation terminal before ordinary permission
observers run.

The production-composed managed loopback tracer proves one Permission
`R < M < S` order. A real authenticated protocol-1.7 responder route is
selected; a managed permission revision from Granted to Revoked invalidates the
exact host reservation; and the actual Transport Prepare send-admission hook
admits no Prepare bytes. Participant policy, media attachment wait, capture,
media, renderer preparation, final Admission, and input authority remain
closed, the old permission reservation cannot revive after regrant, and both
nodes' managed owner graphs drain.

This is not native permission-revocation proof. The macOS adapter now provides
a testable reservation and observation-commit gate over prompt-free
CoreGraphics screen-capture facts, but this checkpoint does not observe or
force a real TCC revoke on a packaged machine. macOS input remains
`Unsupported`; Windows and Linux native permission implementations do not
exist. The production-composed tracer uses the managed permission boundary, not
the macOS CoreGraphics boundary.

No aggregate status changes in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
Connection and Protection reservations, the remaining production-composed
Permission orders and fault intersections, and the complete per-boundary matrix
remain open. Tasks 5, 5.5a, and 5.5; aggregate H0/H1 acceptance; every native,
physical, signing, notarization, and release gate; and the long-term Goal remain
open. `CreateProduction()` must continue to report Remote Window unavailable.

## Exact Permission reservation contract

`INativeRemoteWindowPermissionPreparationBoundary` exposes one internal,
synchronous reservation operation. It accepts the exact previously observed
`NativeRemoteWindowPermissionSnapshot`, the frozen participant role, and one
bounded invalidation sink. It is prompt-free: it cannot request TCC,
Accessibility, portal, desktop, or any other user-facing permission.

Reservation succeeds only when all of these facts are current under the
permission boundary gate:

- owner generation, revision, capture state, and input state exactly match the
  expected snapshot;
- capture is `Granted`; and
- DriverEligible additionally has input `Granted`.

ViewOnly therefore requires only Granted capture. The present macOS adapter
cannot reserve DriverEligible because its input fact is truthfully
`Unsupported`. Snapshot replacement or required-role denial is classified as
`native_permission_denied`; an unsupported, unavailable, disposed, or missing
reservation boundary is `native_permission_unavailable`. Unexpected non-fatal
reservation or currentness failures are redacted to the same unavailable
reason. Exact caller cancellation keeps its exception and token, while
`OutOfMemoryException` remains a fatal runtime condition and escapes unchanged.

The invalidation sink first receives ownership of the registration while the
permission gate is still held. This synchronous ownership transfer means a
later throw, cancellation, or fatal failure cannot hide a committed owner from
coordinator cleanup. If ownership transfer itself fails, the boundary removes
and deactivates the new registration before propagating the failure.

## macOS observation-commit gate

`MacOSNativeRemoteWindowPermissionBoundary` assigns a monotonically increasing
operation sequence before each CoreGraphics preflight or explicit request. An
older operation completion cannot overwrite a newer committed result. An
observation that changes the accepted capture fact advances the exact revision,
deactivates every current Preparation registration under the boundary gate,
and invokes their bounded invalidation sinks before ordinary `Changed`
observers run. Repeating the same accepted fact does not advance the revision or
invalidate a reservation.

All registrations are made inactive before sink delivery begins. A non-fatal
sink failure cannot keep an old registration current, block a later sink, skip
ordinary observers, or undo the permission commit; multiple failures retain
registration order. A fatal `OutOfMemoryException` escapes raw after all
registrations have been deactivated. Boundary disposal deactivates remaining
registrations and retains the same terminal invalidation failure for repeat
disposal without invoking a sink twice.

Revision and registration identities prevent a Revoked-then-Granted sequence,
late old disposal, or a reused visible permission value from reviving or
removing a replacement reservation. The commit gate orders only Flowspan's
accepted observations. External TCC can change outside this process; until a
fresh prompt-free preflight commits that observation, this managed gate cannot
claim to have observed the native change.

## Desktop composition, promotion, and cleanup

The host coordinator acquires the permission registration after the exact
source reservation and before early safety observers, Trust/Capability
reservation, Emergency Stop readiness, responder-route admission, or Prepare.
The same host Preparation reservation is its bounded invalidation sink. A
committed permission change therefore latches the fixed Permission fact and
terminal reason without running cleanup under the permission mutation gate.

Focused coordinator tests freeze permission mutation while reservation is
being acquired, after route selection but before Prepare send admission, and
after Prepare send. The first order selects no route. The second consumes the
conservatively owned connection but admits no Prepare wire. The third permits
at most the already admitted exact Prepare and prevents Ready authority, media
attachment wait, capture, participant Admission, frames, and input. Exact
snapshot replacement, role denial, missing reservation support, unexpected
throw, owner-claim-then-throw, caller cancellation, foreign cancellation, fatal
exhaustion, currentness failure, and cleanup ownership are direct rows.

After exact Ready and media attachment, the coordinator revalidates host facts
and checks that the same permission registration remains current before host
reservation promotion. It releases the temporary registration only after that
promotion. The existing permission observer and repeated permission reads
remain defense-in-depth and live-session authority checks; neither substitutes
for the pre-Prepare reservation. Every terminal path releases an unpromoted
registration through failure-accumulating cleanup.

## Deterministic and production-composed evidence

The macOS permission-boundary tests cover:

- exact ViewOnly admission without a prompt and truthful DriverEligible input
  unavailability;
- exact snapshot matching, required-role denial, and disposed-boundary
  rejection;
- ownership-transfer rollback and retry;
- mutation invalidation before ordinary observers;
- same-fact stability and Revoked/Granted ABA resistance;
- both deterministic reservation-versus-commit gate orders;
- one and multiple non-fatal sink failures, raw fatal exhaustion, stable
  disposal failure, and registration-order invalidation; and
- existing CoreGraphics preflight/request sequencing, stale completion,
  observer isolation, unsupported runtime, and disposal behavior.

The focused Desktop tests cover all three host order shapes and the failure,
cancellation, fatal, ownership-transfer, release, and currentness outcomes
listed above. These tests use controlled boundaries and do not by themselves
prove a real authenticated route or native TCC mutation.

`PermissionRevisionAfterReservedRoutePreventsPrepareWireAndDrains` supplies the
production-composed `R < M < S` row. It establishes real loopback TCP,
authenticated protocol 1.7, the production connection lease, responder media
route, Desktop host reservation, managed permission boundary, and actual
Transport send-admission hook. While the route is owned and the Prepare forward
is blocked, a committed Revoked revision invalidates Permission with
`ConsumeConnection` cleanup scope. A later Granted revision does not revive the
terminal generation. Send admission remains zero, fail-close and connection
disposal run once, and capture, input, sharing, renderer, protection, Emergency
Stop, media directories, routes, handlers, leases, channel, controller, and
coordinator generation drain without resurrection.

The production-composed Permission `M < R` and `S < M` rows and the remaining
reject, throw, cancel, timeout, revoke, disconnect, and cleanup-fault
intersections remain open. Unit order shapes are not promoted to those missing
production rows by inference.

## TDD and review evidence

The Platform permission contract, macOS commit-gate, focused Desktop, and
managed tracer tests were introduced against the earlier point-read-only host
path. Final independent review reported no P0 or P1 findings for this
checkpoint. This statement does not claim closure of the broader matrix or any
native release review.

## Local verification

```bash
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj --configuration Release --no-restore
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

- Platform Debug and Release: `240/240` each;
- macOS Platform Debug and Release: `64/64` each;
- Desktop Debug and Release: `639/639` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2418/2418` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed; and
- deterministic protocol-1.7 simulator: passed.

The local host did not execute a real TCC grant/revoke cycle. The matching-host
CoreGraphics test proves only that the prompt-free production preflight call is
reachable. The current local host also did not have Gitleaks installed; Secret
Scan evidence is the exact hosted job below.

## Hosted exact-SHA evidence

[CI run `33286525528`](https://github.com/happys2333/flowspan/actions/runs/33286525528)
completed successfully for push run 196, attempt 1, at exact SHA
`d607ed1c3217c9c4102c4b893d20da9a6845f02d`. Its jobs were Secret Scan
`99190549630`, macOS `99190549712`, Windows `99190549748`, Ubuntu
`99190550100`, `osx-arm64` package `99191064115`, `linux-x64` package
`99191064116`, and `win-x64` package `99191064190`; every job completed with
`success`.

Downloaded test artifacts contain exactly 12 TRX files per platform. Structured
XML aggregation reports `2418/2418` total, executed, and passed on each
platform. Failed, error, timeout, aborted, inconclusive,
passed-but-run-aborted, not-runnable, not-executed, disconnected, warning,
completed, in-progress, and pending counters are all zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99190549712` | `9724635943` | `ec6cdd953731f01bf0b3b7dd2efd1850095c279d2db2b3b1934d89d07a6945e3` |
| Linux | `99190550100` | `9724629420` | `043f9cdc6deefa62fed0be26293fec228f5d143582f0e660c9585705e6c47c22` |
| Windows | `99190549748` | `9724655039` | `30cdc44264c1a91e5442540f85c645a9f4dc4cf61d8f9a9a3fdb3292dc9874c5` |

Secret Scan job `99190549630` passed. Artifact `9724602079` has GitHub outer
digest
`30377b3f4a3b3d9f4c74878de24056f4a82f5aa5086c144145fe8b42649d01c5`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
and records SARIF 2.1.0, Gitleaks v8.0.0, 208 rules, and 0 results.

Every reproducible package is version `0.1.196` and reports
`unsigned-test-artifact`. The service-computed outer digest was independently
recomputed after download. Every downloaded `SHA256SUMS` entry passed, the
repository Release verifier passed each package, and independent checks matched
the update/provenance/manifest commit, runtime, unsigned state, archive size and
digest, and every manifest file's length and digest:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `linux-x64` | `99191064116` | `9724671699` | `8d8eb444643e343fc32e77a2433d6a510a8150e7cb32d986f3a31c87e2124d1a` | `0de0d22112390d3c75e2c1b03795ff7ca71f5e095aa129783f5c98e3028d3f90` | `e1047bbb391164f2f14bb367e7b03d5f15764b8dd8c4605cbe9ec60914a8fca3` |
| `osx-arm64` | `99191064115` | `9724673990` | `f2643236335bfff3e068437fb70ff0d9ca75b61c1bd10c7da25d57e51be41114` | `beeaf3e32f4224fcfa233217154318ffd0d5a40bacfb098bcda652c230696979` | `1daf78edaef5a2248eebd3f4f4428fc5f8676da9b5226dda31aff5f916f6bd9b` |
| `win-x64` | `99191064190` | `9724676320` | `da8428a1bee1b61df89d73288942a58c63118776261322b3a016791deb71b6a0` | `261dc2e4deb5da9fddece3b52ca143891857d1ce8d5c889384b2dd75dbf0b12b` | `7e2c697c2256f2b80261729ecccae7ca04189f30d02506d1cf8edd6c4241c1b4` |

[CodeQL run `33286525529`](https://github.com/happys2333/flowspan/actions/runs/33286525529),
job `99190549552`, completed successfully. Exact-SHA analysis `1693001765`
with SARIF ID `a4bb6056-a415-11f1-897f-1c7065eb399c` evaluated 52 rules with
0 results, no warning or error, and the exact-ref query returned 0 open alerts.
The downloaded 230,952-byte CodeQL SARIF 2.1.0 payload has SHA-256
`e698638e193d22bdb14c05172e7c9a5578fa9e3e4d37d20b264ce4c572f86329`,
identifies CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, contains 52 extension
rules, and contains 0 results.

The downloaded TRX, SARIF, and package artifacts were parsed from a temporary
verification directory; that directory is not durable project evidence. The
retained run, job, analysis, artifact, outer, archive, payload, and tree
identifiers above bind this record to the hosted result.

## Explicit limitations and next slices

This exact commit proves the shared Permission reservation contract, the macOS
adapter's deterministic observation-commit gate with fake interop, focused
Desktop order/fault rows, and one production-composed managed `R < M < S`
loopback row. Hosted Windows, Linux, and macOS test execution proves the same
managed contracts and TEST MODE composition on those runners. The packages
prove reproducible unsigned structure and verification only.

It does not prove real macOS TCC grant, denial, revocation, or recovery;
Accessibility/input permission; Windows Graphics Capture permission; Wayland
portal/PipeWire/RemoteDesktop or X11 behavior; physical two-Device operation;
signed packages; notarization; packaged accessibility; or release acceptance.
The matching-host macOS test does not manufacture a TCC transition, and the
managed tracer does not instantiate the macOS native boundary.

Connection and Protection reservations; production-composed Permission
`M < R` and `S < M`; the remaining Source, Authorization, and Emergency Stop
orders and fault intersections; and the complete per-boundary
reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix remain open.
Tasks 5, 5.5a, and 5.5; aggregate H0/H1 acceptance; all native/physical/release
gates; `CreateProduction()`; and the long-term Goal remain open.
