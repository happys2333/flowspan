# Host authenticated Connection Preparation reservation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Previous exact Permission checkpoint:
`d607ed1c3217c9c4102c4b893d20da9a6845f02d`

Connection implementation commit:
`259c3bbda4648bc6c45b71d78fbc7a34feb4de71`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint replaces the host's point-in-time authenticated-connection read
with one synchronous composite Connection Preparation reservation. The
reservation occupies two exact slots at once: the owning
`RemoteWindowConnectionGeneration` and that generation's exact
`AuthenticatedRemoteWindowMediaSession`. It is therefore invalidated by either
generation mutation or media-session mutation, rather than treating generation
currentness as a substitute for media currentness.

The Desktop coordinator carries that same registration through authenticated
responder-route selection and actual Transport Prepare send admission, rechecks
it before host-reservation promotion, and releases it only after promotion. A
separate exact-once live revocation registration already observes both the
generation and media control-stop paths, so the temporary reservation-to-live
session handoff has no unobserved media-mutation interval before capture.

Two managed production-composed tracer rows are added. One disconnects the real
authenticated control connection after route selection and proves terminal
Connection classification plus complete two-node drain. That disconnect cancels
the operation before it enters the actual Prepare send-admission hook, so its
`PrepareSendAdmissionCount == 0` is **not** evidence that the send gate rejected
the request. Actual send-gate ownership is instead proved by the Transport
`RemoteWindowControlSession` two-lease regression. The second tracer reaches
Ready and verified `FSM1` attachment, mutates the exact media session during the
promotion/release handoff, observes the live callback and Emergency Stop before
capture, and drains the complete managed graph.

No aggregate status changes are made in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
The production-composed Connection evidence is deliberately narrow, Protection
still has no exact Preparation reservation, and the complete per-boundary
reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix remains open.
Tasks 5, 5.5a, and 5.5; aggregate H0/H1 acceptance; every native, physical,
signing, notarization, and release gate; and the long-term Goal remain open.
`CreateProduction()` must continue to report Remote Window unavailable.

## Exact generation-and-media reservation contract

`AuthenticatedRemoteWindowConnectionLease.TryReservePreparation` creates one
monotonically identified registration while holding the exact generation gate,
then commits the same registration into the exact media-session slot while also
holding the media gate. Reservation rejects a revoked, fail-close-pending, or
owner-released generation; a disposing, route-invalidated, or control-stopped
media session; a previously claimed responder route; or an existing active
registration in either slot.

The invalidation sink synchronously receives the new registration before the
reservation call can return. If ownership transfer throws, both generation and
media slots are rolled back and the registration is deactivated before the
failure escapes. Registration IDs remain monotonic, so a replacement cannot be
removed by a late Dispose from an older failed or released registration. An
active registration is current only while it is active and is the exact object
present in both slots, with both the generation and media session still current.

The fixed lock order is generation then media for reservation, currentness,
route selection, send admission, and release. Invalidation removes and
deactivates the authoritative slot under its mutation gate. Its sink is bounded:
it may only latch the owning host Preparation reservation and must not await,
fail-close, dispose, perform wire I/O, or invoke native or UI work under either
gate.

## Mutation and operation admission

Generation owner release, explicit fail-close, and committed deferred fail-close
invalidate the temporary registration before ordinary generation callbacks or
connection cleanup. Media disposal, the first control-stop commit, and responder
route invalidation likewise invalidate under the media gate before signalling
the public control-stop token. Repeated or concurrent causes cannot invoke the
same Preparation sink twice.

Non-fatal invalidation, fail-close, timer, and registration-cleanup failures are
retained in deterministic order while later owners are still attempted. Shared
fail-close callers observe one completion Task. Fatal `OutOfMemoryException`
escapes by exact instance after applicable registrations, routes, timers, and
other owners have been attempted; it is never reduced to a stale product reason.

While a Connection Preparation registration is active, authenticated responder
route selection and the actual Prepare send-admission callback accept only that
exact registration. A public call, another lease over the same generation, a
foreign registration, a stale registration, or an omitted registration cannot
cross those gates. Once no active registration exists, the public path remains
usable only through a channel that exposes the same atomic wire-admission hook.
A channel without that hook returns `NotDelivered` without sending. This
checkpoint does not add or change protocol bytes.

## Desktop ordering, promotion, and live handoff

The host acquires the Connection registration after exact source and Permission
reservations and before ordinary permission/connection observers,
Trust/Capability reservation, protection, Emergency Stop readiness, route
selection, or Prepare. The host generation is the bounded invalidation sink and
claims ownership synchronously, including the side-effect-then-throw and exact
caller-cancellation paths.

Focused coordinator tests freeze all three order shapes:

- `M < R`: Connection mutation during reservation makes the operation terminal
  before observer registration or route selection;
- `R < M < S`: mutation after the route is selected prevents Prepare wire
  admission and every later authority; and
- `S < M`: mutation after Prepare send prevents Ready from granting host
  authority.

Reservation conflict, unexpected throw, foreign cancellation, owner-claim-
then-throw, exact caller cancellation, initial and promotion currentness faults,
fatal exhaustion, release, and cleanup ownership have direct rows. Non-fatal
failures expose only `authenticated_connection_stale`; exact caller cancellation
retains its token; `OutOfMemoryException` remains raw.

The ordinary authenticated-connection revocation callback remains the formal
live-session owner. Its composite registration observes both generation
revocation and media control stop through one exact-once invocation, rolls back
partial setup, releases media then generation registrations, and preserves
cleanup failure order. It is installed while the temporary Preparation
registration is still current. After Ready, verified media attachment, final
host-fact checks, and host-reservation promotion, the temporary registration is
released. A media mutation in that overlap therefore reaches the live callback
and Emergency Stop before capture even when the generation itself has not yet
been revoked.

## Deterministic and production-composed evidence

The Transport connection-generation tests cover:

- exact generation-and-media reservation, conflict, release, and both-slot
  currentness;
- ownership-transfer rollback, monotonic registration replacement, ABA, and
  late old disposal;
- generation revoke, explicit and deferred fail-close invalidation before
  ordinary callbacks or cleanup;
- non-fatal sink plus cleanup failure order, shared completion, and fatal
  exhaustion after cleanup;
- exact responder-route and Prepare send owners, including public, foreign,
  stale, and missing-registration rejection; and
- composite generation/media live callback setup, rollback, concurrent
  exact-once invocation, self-dispose/fail-close re-entry, reverse-order release,
  stable failure replay, and fatal cleanup.

The focused media-session class covers invalidation on Dispose, control-stop,
and responder-route loss before public control-stop observation; post-promotion
live callback ordering; concurrent stop/dispose exact-once delivery; replacement
and late-dispose safety; cleanup ordering; and raw fatal invalidation after route
cleanup.

`ActiveConnectionPreparationBlocksPublicPrepareSendAtWireAdmission` uses two
leases over one real `RemoteWindowControlSession`: one owns the exact composite
registration while the other attempts the public Prepare path. The public call
reaches the actual wire-admission boundary but writes zero Prepare frames. The
companion exact-owner rows prove that only the reserved lease and exact
registration can cross route and send admission.

`AuthenticatedControlDisconnectAfterReservedRoutePreventsPrepareWireAndDrains`
uses real loopback TCP, authenticated protocol 1.7, the production connection
lease and media session, a real responder route, and the Desktop coordinator.
After the route is selected, disposing the participant control connection makes
the host reservation terminal with fact Connection, reason
`authenticated_connection_stale`, and cleanup scope `ConsumeConnection`.
Participant policy,
attachment wait, capture, media, renderer, final Admission, and input remain
closed, and both nodes' routes, directories, handlers, leases, controller, and
host generation drain. Because disconnect cancellation wins before the send
hook, this row makes no claim about send-gate rejection.

`MediaMutationAfterPreparationPromotionTriggersLiveCallbackBeforeCapture`
reuses the authenticated protocol-1.7 tracer through Ready and verified
bilateral `FSM1` attachment. The host media control-stop is committed during the
exact promotion-to-release window. The live composite callback invokes Emergency
Stop synchronously before the mutation returns and before capture starts; no
Admission, frame, input, or render authority opens, and the same two-node owner
graph drains.

These are managed same-host loopback and contract tests on macOS. They are not
native API, physical two-device, Windows/Linux/macOS packaged-runtime, signing,
notarization, or release evidence. They also do not fill the remaining
production-composed Connection orders or every Connection fault intersection.

## TDD and review evidence

The Transport and Desktop tests were introduced against the earlier
point-read-only connection path, then the exact generation/media slots, route
and send admission, coordinator ownership, and live handoff were implemented.
Two independent final reviews reported no P0 or P1 finding for this checkpoint.
That result is scoped to this Connection slice and does not review or close the
remaining matrix, Protection reservation, native adapters, or release gates.

## Local verification

```bash
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AuthenticatedRemoteWindowMediaSessionsTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AuthenticatedRemoteWindowMediaSessionsTests'
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

- Transport Debug and Release: `755/755` each;
- focused media-session class Debug and Release: `41/41` each;
- Desktop Debug and Release: `654/654` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2469/2469` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed;
- deterministic protocol-1.7 simulator: passed; and
- two independent final reviews: zero P0/P1 findings.

The local host did not execute native capture, input, permission, secure-input,
or packaged two-device behavior. The production-composed tests used managed
loopback endpoints on the same macOS machine.

## Hosted exact-SHA evidence

[CI run `33289550263`](https://github.com/happys2333/flowspan/actions/runs/33289550263)
completed with `success`, run number 197 attempt 1, for exact implementation SHA
`259c3bbda4648bc6c45b71d78fbc7a34feb4de71`. Its Secret Scan
`99198644781`, macOS `99198644809`, Windows `99198644815`, Ubuntu
`99198644950`, `win-x64` package `99199050305`, `linux-x64` package
`99199050317`, and `osx-arm64` package `99199050361` jobs all completed with
`success`.

Downloaded test artifacts contain exactly 12 TRX files per platform. Structured
XML aggregation reports `2469/2469` total, executed, and passed on each
platform. Failed, error, timeout, aborted, inconclusive,
passed-but-run-aborted, not-runnable, not-executed, disconnected, warning,
completed, in-progress, and pending counters are all zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99198644809` | `9725562738` | `94fd3c5ff9c5d15134548e08855916445630f132cc47a75ec443bca346f5a362` |
| Linux | `99198644950` | `9725559853` | `c886b2e84819599ffd0848ec9ed0b9e6d5b1620b513e6bd01d5e43a0c1f619e7` |
| Windows | `99198644815` | `9725558990` | `812fcb3820e94ea6f3393c30e15d352dd8e9b0b38e817737ec473776dfdb1fc6` |

Secret Scan job `99198644781` completed with `success`. Artifact `9725519905`
has GitHub outer digest
`31b2f53cf94c550e1e15ee6b5ec1be3a4a41cbd224e8b4857b42dacb71f56108`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
and records SARIF 2.1.0, Gitleaks semantic version v8.0.0, 208 rules, and 0
results.

[CodeQL run `33289550265`](https://github.com/happys2333/flowspan/actions/runs/33289550265),
run number 197 attempt 1, completed with `success` for the exact implementation
SHA. Job `99198644598` produced analysis ID `1693121127` and SARIF ID
`aebcfd8e-a420-11f1-8a5b-9cb215b81fb3`; the exact ref has 0 open alerts. The
230,952-byte SARIF API payload has SHA-256
`f0b8bb0cbbb7f03841c77a23ee5aea8c81fed3bb27c503bac3070faeed75be7f`,
records SARIF 2.1.0, CodeQL semantic version 2.26.4,
`codeql/csharp-queries` 1.9.2 plus its recorded build metadata, 52 extension
rules, and 0 results. The hosted warning and error fields are both empty.

Every reproducible package is version `0.1.197` and reports
`unsigned-test-artifact`. The service-computed outer artifact digests were
independently recomputed after download. Every downloaded `SHA256SUMS` entry
passed `5/5`, the repository Release verifier passed each package, and
independent checks matched the exact commit, runtime, unsigned state, archive
size and digest, every manifest file's length and digest, and the canonical tree
digest:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99199050305` | `9725585224` | `d02cdd4262ac2abf96b95aebedc997d714693addd41872439d4da9a1f3f2352d` | `ba0d5ea186b1ff755a2fb9584ce84f3ee09fe479e9cf0a91852cb7b879805797` | `9f1fe428e89f103c235648e7e477a33bc4e19184fc1e1848b4618f11b235861d` |
| `linux-x64` | `99199050317` | `9725578754` | `2e468f061ed7a65fb7c34e5bf7f0bc54c37004d1d5bd530439b14b952bb75c93` | `00799ac892f8a8466faad06fe71d6d5852481103236edf449e5c2751ad7405cc` | `d1a1c537221d4107bd0ac5624d086bffd4955bd733d527297ba8e36f6fde9ed6` |
| `osx-arm64` | `99199050361` | `9725581271` | `d80bfc8839552974ed3ece8f5ed0167bbd092d1425128165e89547f69226e664` | `56dfe02bf163e87ff31760b04c31278a27c2d36b0368083d6c6d0f77fcc80077` | `2f4d97708f386bc1634d0c8d03ef859585822e02d5079fca4c288676375d911a` |

The successful hosted tests, Secret Scan, CodeQL, and unsigned-package checks
are managed contract/static-analysis/supply-chain evidence only. They do not
prove native APIs, a physical two-device path, signing, notarization, or release
acceptance.
