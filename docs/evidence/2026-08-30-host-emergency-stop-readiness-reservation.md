# Host Emergency Stop readiness reservation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Baseline: `54a84810c52080eff59b225a91fb52f2136c1952`

Implementation and hosted evidence commit:
`8e349cc7d9f722caa7e6df404ec6a59117d7d588`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Evidence status

This checkpoint replaces the host's point-in-time Emergency Stop readiness
probe with one managed, process-local readiness reservation. The exact owner
and Session generations are reserved before responder-route admission. The same
reservation invalidates the existing host Preparation generation if registrar
readiness is lost, and it can be promoted exactly once to the formal Emergency
Stop registration only after exact Ready, media attachment, host-fact
revalidation, and formal protection observation.

The production-composed managed loopback tracer proves the Emergency Stop
`R < M < S` order: a real authenticated responder route is selected, registrar
readiness loss invalidates the host reservation, and the actual Transport
Prepare send-admission hook returns NotDelivered. No Prepare bytes, capture,
media, renderer, final Admission, or input authority follows.

This is not an operating-system Emergency Stop implementation. The registrar in
this checkpoint is managed and process-local; it does not reserve or exercise a
Windows hotkey, macOS global action, Linux desktop/compositor action, native
permission, or physical input path. No aggregate status changes in the
[Remote Window production-boundary matrix](../testing/remote-window-production-boundary-matrix.md):
H0 and H1 remain partial or missing, Task 5.5a remains unchecked, and
`CreateProduction()` must continue to report Remote Window unavailable.

## Process-local reservation and promotion

`ILocalEmergencyStopRegistrar` now exposes a narrow readiness-reservation
contract. `InMemoryLocalEmergencyStopRegistrar` owns one exact slot in
`Reserved`, `Registered`, or inactive state. A reserved owner binds the host
owner generation, Session generation, and one bounded invalidation sink. It
installs no activation callback: an ordinary user-action trigger cannot fire
during Preparation, and another readiness reservation or formal registration
cannot take the slot.

Registrar loss and disposal linearize against promotion under the same registrar
gate. If loss wins while the owner is reserved, the registrar first makes that
owner inactive and releases the slot, then invokes the bounded Preparation
invalidation transition. If promotion wins, the same owner changes to
Registered and the Preparation sink is removed before the formal activation
callback becomes current. Concurrent promotion and loss therefore deliver
exactly one of the pre-Ready invalidation sink or the post-promotion formal
registration-loss callback, never both.

Releasing a reservation makes later promotion stale and cannot affect an ABA
replacement, even when owner and Session generation values are reused. A
readiness-sink failure cannot retain the registrar slot. Registrar disposal
retains and rethrows the same non-fatal invalidation failure on repeat disposal
without invoking the sink again. Promoted callback drain, self-disposal, and
external disposal retain the existing local Emergency Stop ownership rules.

This reservation protects only the managed registrar slot. It does not claim
that an operating system has reserved a hotkey, global shortcut, accessibility
permission, compositor action, or any other native resource.

## Desktop composition and cleanup

The host coordinator collects the exact readiness reservation after fresh
source protection and host-fact checks and before it arms the host Preparation
reservation or admits a responder route. Readiness loss calls the same
`RemoteWindowHostPreparationReservation` with the fixed Emergency Stop fact;
the terminal public reason is therefore
`emergency_stop_readiness_unavailable`, not registrar or injected exception
text.

After Ready and verified media attachment, the coordinator revalidates the
deadline and host facts, installs the formal protection observer, and reads a
fresh Safe observation. It then promotes the same readiness owner to a formal
Emergency Stop registration, rechecks cancellation and every host fact, and
only then promotes the host Preparation reservation. Capture and participant
authority remain closed throughout this transfer.

A promotion rejection, unexpected throw, side-effect-then-throw, immediate
registration-loss activation, registrar disposal, or exact caller cancellation
after promotion is pre-capture and terminal. If the registrar supplied a formal
owner before throwing, the coordinator retains that owner for cleanup. Cleanup
stops the controller's capture, input, and sharing boundaries before releasing
the formal Emergency Stop registration; a reserved-but-unpromoted owner is also
released exactly once. Post-route terminal outcomes share the existing
connection fail-close and complete owner-graph cleanup.

## Deterministic and production-composed evidence

The Platform contract tests prove:

- a readiness reservation installs no callback before promotion;
- conflict, release, exact owner/Session binding, and stale ABA behavior;
- loss-before-promotion invalidation and post-promotion registration-loss
  delivery;
- registrar disposal in reserved and promoted states;
- promotion-versus-loss produces exactly one owner path;
- invalidation failure releases the slot; and
- repeated registrar disposal retains one exact failure without repeated
  invalidation.

The focused host-coordinator tests cover readiness loss before route, after
route admission, and after Prepare send admission; cancellation before and
after promotion; promotion rejection and throw redaction; promotion
side-effect-then-throw owner retention; activation/disposal during promotion;
and formal-owner cleanup ordering. These coordinator rows use a faithful
registrar or connection double to freeze the exact admission points.

`EmergencyStopReadinessLossAfterReservedRoutePreventsPrepareWireAndDrains`
provides the production-composed `R < M < S` row. It establishes real loopback
TCP, authenticated protocol 1.7, the production connection lease, a responder
media route, the managed process-local registrar, the Desktop reservation, and
the actual Transport send-admission hook. After route selection and before the
Prepare forward, `LoseRegistration()` makes the Emergency Stop fact terminal
with `ConsumeConnection` cleanup scope. The subsequent real send admission
runs once but admits no Prepare wire. Participant policy, attachment wait,
capture, media, renderer preparation, rendering, and final Admission remain
zero. Fail-close and connection disposal each run once, while both handlers,
media directories, routes, leases, channel, controller, protection owner, and
coordinator generation drain; the source remains current and the registrar slot
is reusable.

The other production-composed Emergency Stop `M < R` and `S < M` orders and the
complete reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix
remain open. Unit-level order shapes do not promote those missing production
rows by inference.

## TDD and review evidence

The readiness reservation, coordinator transfer, and managed tracer rows were
introduced against the earlier point-read-only host path. Independent strict
review first reported two P1 findings. After those repairs, a subsequent strict
review reported one additional P1. That finding was also repaired, and the
final review returned APPROVE with 0 P0, 0 P1, and 0 P2 findings for this
checkpoint.

## Local verification

```bash
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Release --no-restore
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

- Platform Debug and Release: `239/239` each;
- Desktop Debug and Release: `608/608` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2355/2355` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit TEST MODE Desktop composition validation: passed; and
- deterministic protocol-1.7 simulator: passed.

## Hosted exact-SHA evidence

[CI run `33283264188`](https://github.com/happys2333/flowspan/actions/runs/33283264188)
completed successfully at exact SHA
`8e349cc7d9f722caa7e6df404ec6a59117d7d588`. Downloaded artifacts contain
exactly 12 TRX files per platform. Structured XML aggregation reports
`2355/2355` total, executed, and passed on each platform; failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable,
not-executed, disconnected, warning, completed, in-progress, and pending are all
zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `99181927620` | `9723664618` | `35ba2d1683d6382ea3992477aae645b514060a387a9422659fd54957558eb868` |
| Linux | `99181927652` | `9723642438` | `e0be3749777619ed9a981b56e58ee3b2971376d3c660e0de8d6d6fb218fd9208` |
| macOS | `99181927700` | `9723643614` | `0433f0497f90bbb01bdf1191387920187e699049d44ed0b32c3520a6619f773f` |

Secret Scan job `99181927544` passed. Artifact `9723613917` has GitHub digest
`1dc3a689012265d5559b3e0b72af8fdc4a2c3f3e0ec41d90e174dedee2155e14`.
Its `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
and contains SARIF 2.1.0 with one Gitleaks run, 208 rules, and 0 results.

Every reproducible unsigned package job passed its content lock, explicit TEST
MODE composition, seal verification, direct/transitive dependency audit, and
artifact upload:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 |
| --- | ---: | ---: | --- | --- |
| `win-x64` | `99182405111` | `9723691547` | `c9c33ea7cfa0f66d656a760d8d76dc626228e7c2fc0bd919da5272f01c72f660` | `01cdbf810ae886709537e8f919de26b5b1d8a832d9861e48cbee96e95a14dd4c` |
| `linux-x64` | `99182405123` | `9723682085` | `16b0a16d1493475b7ff0b71ffdd3fe757e0e33b702c1f6009c7b946584549533` | `fae7b850b20f776d36765dcc4823d4c1c98acc674ae097e8a36eada131849ca3` |
| `osx-arm64` | `99182405198` | `9723687927` | `c6730670b425f79b281bd916be1bfdb6cd522ddf08abab20ea9889a6911cf556` | `d675d33303fba16db32d6e75f741b88ceda94fd62e82b5bd5527454c2e221d03` |

[CodeQL run `33283264254`](https://github.com/happys2333/flowspan/actions/runs/33283264254),
job `99181927596`, completed successfully. Exact-SHA analysis `1692863507`
used SARIF ID `e5d8c774-a409-11f1-94bc-0c09bed1f1fd`, evaluated 52 rules with
0 results, and the exact-commit branch query returned 0 open alerts.

The downloaded TRX, Secret Scan, and package artifacts were parsed from a
temporary local verification directory; that directory is not durable project
evidence. The artifact IDs, GitHub digests, and payload/archive digests above
bind this record to the retained hosted artifacts.

## Explicit limitations and next slices

This exact commit proves one managed, process-local Emergency Stop registrar
reservation and promotion contract and one production-composed managed
`R < M < S` loopback row. It does not prove a Windows, macOS, Wayland, X11, or
desktop-environment hotkey/action; native registration or registration loss;
physical Emergency Stop latency; blocked-UI/network behavior; secure input; or
packaged accessibility.

The other Emergency Stop `M/R/S` orders, complete Emergency Stop fault matrix,
production-composed Source `M < R` and `S < M`, Permission, Trust/Capability,
authenticated Connection mutation, exact Protection epochs, and the complete
per-boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix
remain open.

Tasks 5, 5.5a, and 5.5; `CreateProduction()`; every native/physical/signing/
notarization/release gate; and the long-term Goal remain open. These hosted
results are managed contract/build and reproducible unsigned-package evidence,
not native API, physical two-Device, signed-package, notarization, or release
acceptance evidence.
