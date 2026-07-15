# Evidence: desktop Semantic Handoff, 2026-07-15

Classification: **Local**, **headless UI/contract**, **same-host encrypted
loopback**, and **Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`c7cee092ed008b4829cd3fc74603601e8154be57`

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

## Hosted CI results

CI run
[29387850255](https://github.com/happys2333/flowspan/actions/runs/29387850255)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (windows-latest)` (`87264727819`);
- `Test (macos-latest)` (`87264727834`);
- `Test (ubuntu-latest)` (`87264727846`); and
- `Secret scan` (`87264727809`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
0 warnings and 0 errors, ran all 421 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. Every suite reported 95 desktop, 103
transport, 90 security, 24 integration, 33 domain, 17 protocol, 8
platform-contract, 12 Windows-adapter, 10 macOS-adapter, 15 Linux-adapter, and 14
DNS-SD passes. Every validator printed
`Flowspan desktop composition validation passed in explicit TEST MODE.`; every
simulator printed protocol 1.0, `Source preserved: True`, and
`Target resumed: True`.

CodeQL run
[29387850241](https://github.com/happys2333/flowspan/actions/runs/29387850241)
also completed successfully. Its `Analyze C#` job (`87264727692`) restored the
locked dependency graph, built the analyzed source, scanned 151/151 C# files,
successfully uploaded the SARIF result, and reported a successful job status.

Both runs were created at `2026-07-15T03:59:22Z`. CodeQL completed at
`2026-07-15T04:01:57Z`, and CI completed at `2026-07-15T04:02:24Z`. Run, job,
step, commit, timestamp, suite-count, validator-output, simulator-output, and
CodeQL coverage evidence was queried with `gh run view` on 2026-07-15.

The evidence-documentation commit
`4d49bd96436e472d52323704b01d3d5f59419d33` was then independently verified by
CI run
[29388968415](https://github.com/happys2333/flowspan/actions/runs/29388968415)
and CodeQL run
[29388968454](https://github.com/happys2333/flowspan/actions/runs/29388968454).
The second CI run again passed all 421 tests, locked restore, formatting,
warning-free builds, TEST MODE composition, deterministic simulator, artifact
upload, and Secret Scan in jobs `87267987685` (Windows), `87267987611` (macOS),
`87267987621` (Ubuntu), and `87267987613` (Secret Scan). The second CodeQL job
`87267987746` again scanned 151/151 C# files, uploaded results, and completed
successfully. Both runs were created at `2026-07-15T04:26:14Z`; CI completed at
`2026-07-15T04:29:10Z` and CodeQL at `2026-07-15T04:29:22Z`.

With the implementation and evidence commits independently green, the 7.3a
automated acceptance evidence is complete. The closure commit that records this
status remains subject to the same branch CI, Secret Scan, and CodeQL workflows;
the status is effective only after those final HEAD checks pass.

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
