# Host native Protection Preparation reservation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Protection implementation commit:
`c987ca84e1f9f867f0edef3222a94dc8d25a2583`

Exact evidence/test-stabilization commit:
`457a2c4b9e3d6905218e826cedd60029bbd1b35e`

Previous authenticated Connection checkpoint:
`259c3bbda4648bc6c45b71d78fbc7a34feb4de71`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint replaces the host's point-in-time Safe-protection checks with
one exact native Protection Preparation registration. The registration binds the
complete accepted observation: owner, session, and source generations; revision;
protection kind; observation time; and source identifier. The host owns that
registration before route selection, retains its temporary phase through
Prepare, Ready, and verified media attachment, promotes the same registration to
formal pre-start immediately before host-reservation promotion, and then carries
it through capture-start admission into live session ownership.

The protection observation's complete freshness interval is also part of the
host reservation. Every time-bearing transition from Arm through route and
actual Prepare send admission, Ready matching, and host promotion rechecks that
interval. After the controller has entered `Starting`, a fresh clock read and the
same source mutation gate perform one final capture-start admission immediately
before source use and native capture. Protection mutation therefore wins either
before capture, or after admission against a controller that is already
`Starting` and whose live protection gates can be closed synchronously.

No aggregate status changes are made in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
The production-composed tracer covers only the successful path and Protection
`R < M < S` for `SecureInput` and `Unknown`. It does not prove production-
composed `M < R` or `S < M`, nor the complete per-boundary
reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix. Tasks 5,
5.5a, and 5.5; aggregate H0/H1 acceptance; every native, physical, signing,
notarization, and release gate; and the long-term Goal remain open.
`CreateProduction()` must continue to report Remote Window unavailable.

## Exact observation and freshness contract

`InMemoryNativeProtectionSource.TryReservePreparation` accepts only a current,
fresh `Safe` observation whose complete identity and payload equal the expected
observation. Only one active registration can occupy the source slot. The
registration has a monotonic ID and is current only while it remains the exact
active object in that slot and its expected observation is still current. The
invalidation sink claims ownership synchronously under the source mutation gate;
if that claim throws, the slot is rolled back and the registration is
deactivated before the failure escapes. A late Dispose from an old registration
cannot remove a replacement.

Freshness accepts a `Safe` observation from
`ObservedAt - MaximumFutureClockSkew` through
`ObservedAt + MaximumProtectionAge`, including both endpoints. The host binds
that exact interval while its reservation is still `Collecting`. Arm, route
admission, actual Prepare send admission, Ready matching, and host promotion all
fail with fact Protection and `native_protection_not_safe` outside it. Request
deadline equality remains expired and takes priority over Protection freshness.
The protection source independently rechecks exact observation identity and
freshness at formal promotion and capture-start admission.

Mutation or source loss while the registration is temporary removes and
deactivates it under the source gate, then invokes the bounded host invalidation
sink before ordinary `Changed` observers. `Safe → unsafe → Safe` cannot revive
the old owner. Non-fatal reservation/currentness/promotion failures are reduced
to `native_protection_not_safe`; exact caller cancellation retains its token;
and nested or direct `OutOfMemoryException` escapes raw after applicable cleanup.

## Temporary, formal pre-start, and live ownership

The exact registration has three active phases:

1. `Temporary` covers host fact collection, route admission, Prepare, Ready,
   verified `FSM1` attachment, and the final host revalidations before protection
   promotion. A mutation invalidates the exact host Preparation reservation
   before ordinary observers.
2. `FormalPreStart` begins when the coordinator promotes the same registration
   after Ready and immediately before the separate host-reservation promotion.
   Mutation or source loss synchronously closes controller Protection admission
   and invalidates the host reservation. The registration remains owned through
   controller start rather than leaving a point-read gap.
3. `Live` begins only when the controller is already `Starting` and the
   registration admits capture with a fresh clock, the exact observation still
   current, and `Safe` still fresh under the source gate. Only then may source
   use and native capture begin. Subsequent observations use the formal live
   path; source loss requests terminal shutdown.

The controller's capture-start gate runs after its state lock has published
`Starting` and immediately before any source-use/native capture boundary. A
false or non-fatal throwing gate produces `native_protection_not_safe` and zero
capture. Fatal exhaustion performs failed-start cleanup and escapes by exact
instance. A concurrent mutation that wins the source gate prevents capture; a
mutation after admission observes `Starting` and closes the live gates rather
than racing an Idle point read.

## Live notification and exact Protection admission use

The source latches each live observation under its mutation gate before invoking
the formal Notify callback outside that gate and before ordinary observers. The
host closes Protection admission synchronously at latch time and retains a
bounded FIFO of exact observation plus admission-epoch entries. Non-reentrant
Notify callers join the sequence they observed; active callback ancestry avoids
self- and cross-boundary deadlock while the active outer drainer remains
responsible for queued work. Source loss is terminal and cannot be overwritten
by a later Safe observation. Queue pressure, callback failure, and source loss
all fail closed; fatal failure identity is retained rather than redacted.

The underlying in-memory source also serializes its bounded ordinary/formal
notification queue, commits the latest observation before callbacks, coalesces
overflow to `Unknown`, and joins in-flight formal notification work during
external Dispose. Self-disposal and nested/cross-source callback ancestry do not
wait on their own active callback. Reversed concurrent notifications preserve the
committed unsafe-before-Safe order, and an older captured callback context cannot
bypass a current drainer.

Closing controller Protection admission prevents every new
`ProtectionAdmissionUse`. Each native frame destination and each native or
semantic input boundary holds one exact use for the complete local call. After
an unsafe latch, reconciliation joins existing uses except for its own active
ancestry. A later current Safe result can reopen admission only after uses drain
and the same admission epoch, accepted observation, Active lifecycle, and
Capturing state still match; a stale Safe callback or a still-borrowed use
cannot reopen the gate.

## Deterministic and production-composed evidence

The Platform contract tests cover:

- exact full-observation matching, single-slot ownership, ownership-transfer
  rollback, monotonic replacement/late Dispose safety, conflict, and ABA;
- inclusive freshness boundaries, stale/future/unsafe rejection, revalidation
  at promotion and capture start, and mutation-first zero-capture admission;
- `Temporary → FormalPreStart → Live`, mutation and source-loss behavior in
  every phase, formal latch-before-notify ordering, and notification failure;
- reversed concurrent Notify ordering, bounded overflow, external drain,
  self-dispose, nested and cross-source ancestry, stable failure replay, and raw
  fatal failure; and
- the capture-start gate plus exact frame/input `ProtectionAdmissionUse` scopes,
  blocked reopening, safe reopening after drain, cancellation, and Stop races.

Focused Desktop coordinator tests cover all three abstract order shapes
`M < R`, `R < M < S`, and `S < M`; mutation immediately before and after the
capture-start gate; the formal FIFO and source-loss drain; live input closure
before Notify; stale ancestry; reserve conflict; claim-then-cancel; redacted
non-fatal reserve/promotion/currentness failures; raw fatal exhaustion; and
release/cleanup ownership.

`DriverEligibleWindowTraversesManagedTwoNodeProductionPathAndCleansUp` plus the
two executions of
`ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains` form the
focused three-case managed tracer set. They use real loopback TCP, authenticated
protocol 1.7, the production host route, and the actual Transport Prepare send-
admission hook. In each negative execution, `SecureInput` or `Unknown` commits
after the route is selected. The send hook is entered once and returns
`NotDelivered`; the host reservation never marks Prepare send admitted, zero
Prepare wire or later capture/render/input/Admission authority appears, a later
Safe publication cannot revive the terminal generation, and both managed nodes
drain. These two negative rows prove only Protection `R < M < S`.

## TDD and review evidence

The contract, controller, coordinator, and managed tracer tests were introduced
against the prior point-read path before exact source ownership, state promotion,
capture-start admission, live FIFO/drain, and per-boundary admission-use behavior
were implemented. Two independent final reviews reported no P0 or P1 finding
for this checkpoint. That result is scoped to this Protection slice and does not
review or close the remaining matrix, native adapters, physical-device gates, or
release acceptance.

## Local verification

```bash
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DriverEligibleWindowTraversesManagedTwoNodeProductionPathAndCleansUp|FullyQualifiedName~ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DriverEligibleWindowTraversesManagedTwoNodeProductionPathAndCleansUp|FullyQualifiedName~ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~FormalProtectionSourceLossWaitsForEarlierNotification'
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

Implementation-tree results at `c987ca8`:

- Platform Debug and Release: `289/289` each;
- Desktop Debug and Release: `700/700` each;
- focused managed success plus `SecureInput` and `Unknown` tracer: `3/3` in
  Debug and Release;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2564/2564` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed;
- deterministic protocol-1.7 simulator: passed; and
- two independent final reviews: zero P0/P1 findings.

After the test-only cleanup-barrier commit `457a2c4`, the focused Release
`FormalProtectionSourceLossWaitsForEarlierNotification` execution passed, and
50 consecutive repetitions also passed. The full Debug and Release builds and
solution tests were then repeated at that exact evidence tree: both builds had
zero warnings/errors and both test runs passed `2564/2564`.

The local host did not execute native capture, input, permission, secure-input,
protected-window, or packaged two-device behavior. The production-composed tests
used managed loopback endpoints on the same macOS machine. These local results
are not Windows or Linux execution evidence and are not physical macOS device-
pair evidence.

## Hosted exact-SHA evidence

The first exact-implementation CI attempt, run `33293828592` at `c987ca8`,
failed only the macOS execution of
`FormalProtectionSourceLossWaitsForEarlierNotification`: the test inspected the
coordinator snapshot immediately after synchronous route closure, before its
separate deterministic asynchronous terminal cleanup had completed. Its
production-safety assertions passed, but this run is **not** counted as
successful hosted evidence. Commit `457a2c4` changes only that test barrier to
join terminal cleanup; the targeted Release test and 50 repeated local
executions then passed.

[Replacement CI run `33294103546`](https://github.com/happys2333/flowspan/actions/runs/33294103546)
completed with `success`, run number 200 attempt 1, for exact evidence SHA
`457a2c4b9e3d6905218e826cedd60029bbd1b35e`. Secret Scan job
`99210704683`, macOS test job `99210704723`, Windows test job `99210704750`,
Ubuntu test job `99210704809`, `osx-arm64` package job `99211142222`,
`win-x64` package job `99211142260`, and `linux-x64` package job
`99211142286` all completed with `success`.

Downloaded test artifacts contain exactly 12 TRX files per platform. Structured
XML aggregation reports `2564/2564` total, executed, and passed on every
platform. Failed, error, timeout, aborted, inconclusive,
passed-but-run-aborted, not-runnable, not-executed, disconnected, warning,
completed, in-progress, and pending counters are all zero. The downloaded
artifact bytes independently reproduce each service-provided SHA-256:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99210704723` | `9726890617` | `7fc3476b31354fc78873830d594073e8aaa5899311678c68220bf6ea5d636d62` |
| Linux | `99210704809` | `9726893798` | `84991186642e30260111d5e57387e00ba899806ca71d2e2868d66cc762dec9f8` |
| Windows | `99210704750` | `9726904373` | `2a88dd0f7cf5b01f673a36ee230ced0a45a854f7ed8633adeae0cca53f7a384a` |

Secret Scan artifact `9726854706` has independently reproduced outer SHA-256
`2c5e83b1856233eb703be76f1ce3b91e72f5ae84f550071b12477f732141971f`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
records SARIF 2.1.0 and Gitleaks semantic version v8.0.0, and contains 208
rules with 0 results.

[CodeQL run `33294103609`](https://github.com/happys2333/flowspan/actions/runs/33294103609),
run number 200 attempt 1, completed with `success` for the same exact evidence
SHA. Job `99210704844` produced analysis ID `1693322232` and SARIF ID
`a0543bb6-a431-11f1-8f35-8902a0865a48`; the exact branch ref has 0 open
alerts. The 230,952-byte SARIF API payload has SHA-256
`c58714701169f0fe565ca683d6ba9f95b91f72ae950ac8879695ceaa5b33b94b`,
records SARIF 2.1.0, CodeQL semantic version 2.26.4,
`codeql/csharp-queries` 1.9.2 plus its recorded build metadata, 52 extension
rules, and 0 results. The hosted warning and error fields are both empty.

Every reproducible package is version `0.1.200` and reports
`unsigned-test-artifact`. All `5/5` downloaded `SHA256SUMS` entries passed for
each runtime, the repository `Flowspan.Release verify` command passed all three
artifact directories, and independent archive extraction matched the exact
commit, RID, unsigned state, every manifest length/digest/mode, and the
canonical signed-tree digest. Downloaded artifact bytes also independently
reproduce the service outer digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99211142260` | `9726931645` | `292376f931be01b44d575d84f4e84d3e1ddc719691b70b70c3983345a379bc4a` | `ad4f986f1acb3efca69170e2b83c6734aa67e29019ea60cfd770aeccba1db0db` | `f0c6b36d0322819565d8c01efa4ce0cf7342042b6b6aab92da4ae2f3f94425ac` |
| `linux-x64` | `99211142286` | `9726920405` | `130872faaed321dbbf0dba986beef5f2c76a5e7252be666a0633064242b8b2ac` | `c3d526e6853350a77b51440e4703a83d32bfb82c1b0f4aaa5099ef67dcce3b3a` | `e5e2962b1fc70a78546f263099ec77670f95bd03647414e638870184b884d8ff` |
| `osx-arm64` | `99211142222` | `9726928606` | `9534974714a58db9c9dc04c3b3a1294f5884752ea7fa95d994db226da2a38733` | `183806ee9ade03452bafa755961a6cb6dfaf750ec22687eae1019b95800f2d73` | `8432d3b21568312f89b8e34cb0ba5e9c4807c1613289f9d77ff85c5aa9315edc` |

These successful hosted jobs prove managed build, test, static-analysis,
content-lock, and reproducible unsigned-package properties at the exact
evidence SHA. They do not prove native APIs, physical two-device operation,
signing, notarization, or release acceptance.

## Remaining limits

This checkpoint is managed contract and same-host loopback evidence. It does not
instantiate a Windows secure-desktop/protected-content probe, a macOS secure-
input/protected-window probe, a Wayland portal/PipeWire protection boundary, or
an X11 degradation path. It does not execute native capture or input, physical
two-device networking, packaged accessibility, signing, notarization, or release
lifecycle behavior.

Only the successful tracer and Protection `R < M < S` for `SecureInput` and
`Unknown` are production-composed here. Protection `M < R`, `S < M`, native
source-specific revocation, and the complete per-boundary
reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix remain open.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance, every native/physical/
release gate, `CreateProduction()`, and the long-term Goal remain open.
