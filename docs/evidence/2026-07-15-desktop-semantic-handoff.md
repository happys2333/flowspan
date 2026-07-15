# Evidence: desktop Semantic Handoff, 2026-07-15

Classification: **Local**, **headless UI/contract**, and **same-host encrypted
loopback**. Hosted CI is pending for this candidate.

Branch: `codex/v1-foundation`

Candidate implementation commit: pending creation and hosted verification.

## Local environment and commands

```text
OS: macOS 26.5.2 (build 25F84), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The most stateful affected paths were also run in 20 fresh testhost processes
per group:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~MainWindowAccessibilityTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ActivityControlMessageCodecTests|FullyQualifiedName~ActivityControlSessionTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~DesktopTrustedPeerConnectionsTests.ProductionLoopAuthenticatesOneWayGrantInEitherDeviceIdOrdering'
```

Observed results for the final local working tree:

- locked restore passed for all 24 projects after the new project-reference graph
  was captured in every affected lockfile;
- format verification and the patch whitespace check passed;
- Release build passed with 0 warnings and 0 errors;
- the complete unfiltered suite passed 421 tests with 0 failures and 0 skips:
  95 desktop, 103 transport, 90 security, 24 integration, 33 domain, 17
  protocol, 8 platform-contract, 12 Windows-adapter, 10 macOS-adapter, 15
  Linux-adapter, and 14 DNS-SD tests;
- 20 independent Avalonia Headless accessibility processes passed;
- 20 independent Activity codec/session processes passed;
- 20 independent production-reconnect processes passed both complementary
  one-way Capability/Device-ID-ordering cases;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0, printed
  `Source preserved: True` and `Target resumed: True`, returned a committed
  payload-free receipt, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

The final TDD red gate for truthful recovery status failed because an
acknowledgement-lost result said the target `did not accept` the copy. The green
implementation now says the peer may have accepted it while the verified outcome
is unavailable. Earlier tracer tests drove the bounded codec, authenticated
session, directional authorization, desktop runtime, preview, receipt, and
lifecycle composition.

## Hosted CI status

Pending. Task 7.3a remains in progress until the implementation commit and the
final evidence-documentation HEAD both pass the configured Windows, macOS, and
Ubuntu matrix, Secret Scan, and CodeQL. Run IDs, job IDs, exact suite counts, and
timestamps will be added only after those hosted runs complete.

## What this proves

- Production desktop composition initializes protected identity, Trust, and the
  Activity runtime in order, and drains network, Activity, Trust, then identity.
- A live authenticated control session is admitted when either locally granted
  Activity direction is useful. Removing one any-of alternative preserves the
  session; removing the final alternative drains it. Existing all-of profiles
  remain strict.
- Outbound disclosure is denied before payload serialization unless the source's
  local Trust Record grants `activity.receive` for that target. The receiving
  runtime reloads current Trust and requires local `activity.offer` for the
  authenticated sender before adapter use.
- The target adapter independently rejects empty, extra-field, malformed-shape,
  and over-limit portable-note payloads before catalog mutation.
- The strict transfer codec verifies target, deadline, request, payload, and
  descriptor digests. The pending sender accepts only a payload-free receipt
  bound to the authenticated participants, correlation, Operation, Activity,
  kind, and descriptor digest.
- One `workspace.note/v1` Activity can be created and semantically resumed over
  the encrypted same-host control channel while the source remains active.
- The desktop preview names the data disclosure, semantic boundary, source
  preservation, and unavailable Remote Window fallback. The receipt projection
  contains no note payload and distinguishes rejection, failure, and uncertain
  acknowledgement.
- The global state remains `NOT SHARING`; a one-shot Handoff is not presented as
  Mirror, remote input, process migration, Move, replace, or swap.

## What this does not prove

- No physical LAN, second machine, Wi-Fi/Ethernet interface change, firewall,
  sleep/wake, or cross-machine packet-loss path was exercised.
- Hosted runner contracts, when added, will not be physical Windows, macOS, or
  Linux device evidence and will not prove packaged-app behavior.
- No arbitrary third-party application process, unsaved internal state,
  credential, secure input, screen media, or remote input was transferred.
- `workspace.note/v1` remains in-memory for this slice; restart persistence,
  recovery history, duplicate-outcome reconciliation UI, and user deletion are
  not implemented.
- The Remote Window fallback, Move, replace, swap, Mirror, driver switching,
  Activity Groups, Scenes, and undo/compensation remain later tasks.
- Headless automation contracts do not prove native screen-reader speech, focus
  rendering, contrast, scaling, reduced motion, localization, or packaged
  Windows/macOS/Linux accessibility.
- This evidence does not satisfy the physical-device, native-permission,
  packaging, independent security-review, or complete v1 acceptance gates.
