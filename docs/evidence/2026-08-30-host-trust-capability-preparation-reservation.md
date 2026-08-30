# Host Trust and Capability Preparation reservation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Baseline: `8e349cc7d9f722caa7e6df404ec6a59117d7d588`

Authenticated-fingerprint prerequisite: `1c1999c`

Security reservation implementation: `7a1349b`

Fatal-failure preservation repair: `4138b03`

Desktop integration and hosted evidence commit:
`635dc23ec0c8f2812d527e16135b3d9c40885788`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint binds one host Remote Window Preparation generation to the
exact current Trust identity and all role-required Mirror Capabilities before
responder-route admission. The authenticated protocol connection now carries
the peer public-key fingerprint proved by its real handshake into its
generation-bound Remote Window lease. The Security coordinator reserves the
exact peer Device ID, that fingerprint, and an all-of Capability requirement
under the same gate used to commit Trust revocation and Capability update.

The production-composed managed loopback tracer proves one Authorization
`R < M < S` order. A real authenticated protocol-1.7 responder route is
selected; an Applied update that preserves the same visible `mirror.view` grant
still invalidates the exact Trust Preparation reservation; and the actual
Transport Prepare send-admission hook returns NotDelivered. No Prepare bytes,
participant policy, media attachment wait, capture, media, renderer, final
Admission, or input authority follows, and both nodes' managed owner graph
drains.

This does not complete H0 or H1. It proves neither every production-composed
Authorization `M < R` or `S < M` order nor the complete per-boundary reject,
throw, cancel, timeout, revoke, disconnect, and cleanup-fault matrix. Permission,
authenticated Connection mutation, Protection, remaining Source and Emergency
Stop orders, native adapters, and physical evidence remain open. Task 5, Task
5.5a, Task 5.5, every native/physical/signing/notarization/release gate, and the
long-term Goal remain open. `CreateProduction()` must continue to report Remote
Window unavailable.

## Authenticated fingerprint binding

The authenticated connection registration captures the peer fingerprint from
the verified handshake identity and exposes it only through the internal
generation-bound Remote Window lease. The host coordinator does not accept a
caller-supplied or discovery-only fingerprint as Preparation authority. A
missing or blank authenticated fingerprint fails before a Trust reservation or
route with `authenticated_connection_stale`.

Transport contract tests prove that a lease retains the actual handshake
fingerprint and that a replacement key for the same Device ID creates a
different fingerprint on the replacement connection generation. The old lease
remains bound to the old fingerprint and cannot be retargeted to the new
identity.

The fingerprint is a process-local authorization input. It is not added to the
protocol-1.7 Prepare/Ready frames, media locator, diagnostics, persistence, or
peer-visible state.

## Exact Security reservation

`TrustSessionCoordinator` owns monotonically increasing internal Preparation
registration identities. Reservation succeeds only when all of these facts are
current under its mutation gate:

- the exact peer Device ID exists;
- its current public-key fingerprint equals the authenticated connection
  fingerprint; and
- every required Capability is present.

The Desktop adapter maps ViewOnly to `mirror.view` and DriverEligible to the
all-of set `mirror.view` plus `mirror.drive`. `PeerNotFound` and
`CapabilityDenied` reduce to `mirror_capability_denied`; `IdentityChanged`
reduces to `authenticated_connection_stale`; and an unexpected non-fatal
reservation failure reduces to `mirror_authorization_unavailable`. Exact caller
cancellation retains its original token, while `OutOfMemoryException` remains a
fatal runtime condition and is not projected as a product rejection.

Every Applied revoke or Capability update invalidates all matching Preparation
registrations while the Trust mutation gate remains held, after the store
commit and before `Changed` observers or active-session Stop. This includes an
Applied update whose resulting Capability set equals the previous set: the old
operation reservation is never inferred current from value equality. A
rejected, thrown, or caller-cancelled store mutation does not invalidate a
reservation because no authoritative mutation committed.

Invalidation first deactivates and removes all matching registrations in
stable registration order. Non-fatal sink failures are retained after all
registrations have been deactivated; they do not undo the committed Trust
mutation or skip active-session Stop, and combined failures retain deterministic
order. A fatal `OutOfMemoryException` escapes as the same instance rather than
being wrapped; all registrations were already deactivated before sink delivery
started. Revoke/regrant, identity replacement, and late disposal of an old
registration cannot revive or remove a replacement reservation. Coordinator
disposal invalidates all remaining registrations in stable order.

## Desktop composition, promotion, and cleanup

The host coordinator acquires the Trust Preparation registration after the
exact source guard and early safety observers and before responder-route
admission. The same Desktop host reservation is the bounded invalidation sink.
An authoritative Trust mutation therefore latches the fixed Authorization fact
and terminal reason `mirror_capability_denied` without performing cleanup under
the Security mutation gate.

Focused coordinator tests freeze Authorization invalidation before route,
after route admission but before Prepare send, and after Prepare send. The
first order selects no route. The second consumes the conservatively owned
connection and admits no Prepare wire. The third permits at most the already
admitted exact Prepare but prevents Ready authority, attachment wait, capture,
participant Admission, frames, and input. Reject, blank fingerprint, unexpected
throw, fatal exhaustion, exact caller cancellation, and success/cancellation
release counts are also direct rows.

After exact Ready and media attachment, the coordinator rechecks that the same
Trust registration remains current before host-reservation promotion. On
successful promotion it releases the temporary Trust registration before
crossing controller Start. Every terminal cleanup path also releases an
unpromoted registration through failure-accumulating cleanup, so the
implementation attempts later owners even if release fails. A direct
Authorization-registration release-failure intersection remains part of the
open cleanup-fault matrix.

The existing point-in-time Capability reads and active-session revocation
callbacks remain defense in depth and live sharing authority checks; neither is
used as a substitute for the pre-Prepare reservation.

## Deterministic and production-composed evidence

Security reservation tests cover:

- exact fingerprint and all-of Capability admission;
- exact rejection status and no empty-Capability reservation;
- Applied same-grant update invalidation;
- revoke/regrant, replacement identity, and late-old-dispose ABA resistance;
- mutation-versus-reservation serialization on both sides of the gate;
- invalidation before `Changed` and before a blocking active-session Stop;
- rejected, thrown, and caller-cancelled mutation behavior;
- exact reservation-gate caller cancellation;
- single and multiple sink/Stop failure identity and ordering;
- fatal invalidation and session-Stop exhaustion without wrapping; and
- coordinator disposal invalidation order and retained failure.

The focused Desktop tests cover the three host order shapes plus reservation
reject, missing fingerprint, throw/redaction, fatal exhaustion, exact caller
cancellation, and success/cancellation release counts. These tests use
controlled boundaries and do not by themselves prove a real authenticated
route or Prepare wire admission.

`AppliedSameMirrorGrantAfterReservedRoutePreventsPrepareWireAndDrains` supplies
the production-composed `R < M < S` row. It establishes real loopback TCP,
authenticated protocol 1.7, the handshake-derived peer fingerprint, production
connection lease, responder media route, real Trust coordinator and Desktop
adapter, host reservation, and actual Transport send-admission hook. While the
route is owned and the Prepare forward is blocked, an Applied same-grant update
invalidates Authorization. The source and authenticated connection are still
current at that instant, isolating the Trust fact. The later send hook admits no
Prepare wire. Fail-close and connection disposal each run once, while capture,
input, sharing, renderer, protection, permission observer, Emergency Stop,
media directories, routes, handlers, leases, channel, controller, and
coordinator generation drain without resurrection.

The production-composed `M < R` and `S < M` rows and all remaining
Authorization fault intersections remain open. Unit order shapes are not
promoted to those missing production rows by inference.

## TDD and review evidence

The fingerprint lease tests, exact Security reservation tests, focused Desktop
host tests, and managed tracer were added against the earlier point-read-only
authorization path. Final independent strict reviews covering the authenticated
fingerprint, Security core and fatal-failure repair, and Desktop integration
reported APPROVE with 0 P0, 0 P1, and 0 P2 findings for their reviewed scope.

## Local verification

```bash
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Security.Tests/Flowspan.Security.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Security.Tests/Flowspan.Security.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests|FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests|FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
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

- Transport Debug and Release: `719/719` each;
- Security Debug and Release: `144/144` each;
- focused host coordinator plus managed tracer Debug and Release: `87/87`
  each;
- Desktop Debug and Release: `616/616` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2377/2377` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed; and
- deterministic protocol-1.7 simulator: passed.

The current local host did not have Gitleaks installed. Secret Scan evidence is
therefore the exact hosted job below, not a claimed local scan.

## Hosted exact-SHA evidence

[CI run `33284857461`](https://github.com/happys2333/flowspan/actions/runs/33284857461)
completed successfully at exact SHA
`635dc23ec0c8f2812d527e16135b3d9c40885788`. Its relevant jobs were Secret
Scan `99186166592`, Ubuntu `99186166681`, Windows `99186166712`, macOS
`99186166730`, `osx-arm64` package `99186527207`, `linux-x64` package
`99186527209`, and `win-x64` package `99186527289`.

Downloaded test artifacts contain exactly 12 TRX files per platform. Structured
XML aggregation reports `2377/2377` total, executed, and passed on each
platform; failed, error, timeout, aborted, inconclusive, passed-but-run-aborted,
not-runnable, not-executed, disconnected, warning, completed, in-progress, and
pending are all zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99186166730` | `9724108449` | `67b9715bd32fcf5c14c5c79e05e9ceb101a4b74925c64995567656c35d2cb330` |
| Linux | `99186166681` | `9724116702` | `6e9fa77f2860faed83b8c9fba974a4a203a38d3e9bd5f4057be1958e20b375ba` |
| Windows | `99186166712` | `9724118096` | `d174dd5a0390732c4405e90e4f8ca9d3af69699914f084be1d4f0703562cbceb` |

Secret Scan job `99186166592` passed. Artifact `9724079831` has GitHub digest
`101922ab25ed65950d222301c34bd8ce6508694d7493298904124ec5d699ff7c`.
Its SARIF records Gitleaks v8.0.0 with 208 rules and 0 results.

Every reproducible package job at this exact-SHA run produced version
`0.1.195`, reported `unsigned-test-artifact`, passed the Release verifier, and
matched all `5/5` entries in its downloaded `SHA256SUMS`. Its package, update,
and provenance manifests bind the target SHA, expected runtime identifier, and
unsigned signature state. `Artifact SHA-256` is GitHub's
service-computed outer artifact digest. `Inner archive SHA-256` identifies the
sealed application archive, and `Tree SHA-256` hashes the sorted extracted-file
manifest:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `linux-x64` | `99186527209` | `9724137007` | `e666e7ff43772315997a473b922bf2cb82e2ee7e98c9b7938dd34dd4ad398e53` | `7f99a230271fd3afdd8c849207301b80fa995923ce975543b564c2c8cced80dc` | `2b34bd6ae815772bef0bbf74b30384650bb59e914a0e83ddd3e12f0215a461ec` |
| `osx-arm64` | `99186527207` | `9724138947` | `2394849097128b469308d3500ac11fd83aecda98d1f33e481d5c2d19e15dd031` | `73aeede7abae2644ca90d849b21473dc485c3c0a02a279fc7d38b5b0da7a1c81` | `afd0559dec2e31bd600d6c1a9b70c6d3a6e6fa04038fa4d137933f8c7d328f5d` |
| `win-x64` | `99186527289` | `9724144322` | `ccfde28330b43cfb01d8b2627d52ae8b18fbe2cfddf33b4871c47b67a2a50819` | `e54aa5517fbfb72677536a35756e7118c8e50935211017c9dc72d02641658dcf` | `ee6a5ad154393cd042b89121dddbc5f3c4248f028cd675e02f193d4e89638415` |

[CodeQL run `33284857449`](https://github.com/happys2333/flowspan/actions/runs/33284857449),
job `99186166403`, completed successfully. Exact-SHA analysis `1692926633`
evaluated 52 rules with 0 results, and the exact-commit branch query returned 0
open alerts.

The downloaded TRX, Secret Scan, and package artifacts were parsed from a
temporary verification directory; that directory is not durable project
evidence. The retained artifact IDs and outer, inner, and tree digests above
bind this record to the hosted artifacts.

## Explicit limitations and next slices

This exact commit proves managed authenticated-fingerprint propagation, an
exact process-local Trust/Capability Preparation reservation, focused Desktop
order/fault rows, and one production-composed managed `R < M < S` loopback row.
Hosted Windows, Linux, and macOS test execution proves the same managed
contracts and test-mode composition on those runners. The packages prove
reproducible unsigned structure and verification only.

It does not prove a Windows, macOS, Wayland, or X11 native permission, source,
capture, input, protection, secure-input, or Emergency Stop API; a physical
two-Device path; signed packages; macOS notarization; packaged accessibility;
or release acceptance. It does not make the test-only managed coordinator a
shipped production runtime.

Permission, authenticated Connection mutation, Protection, remaining Source
and Emergency Stop orders, the remaining Authorization orders and fault
intersections, and the complete per-boundary matrix remain open. H0 and H1 stay
P or M. Tasks 5, 5.5a, and 5.5; `CreateProduction()`; every native/physical/
signing/notarization/release gate; and the long-term Goal remain open.
