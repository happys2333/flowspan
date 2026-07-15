# Evidence: desktop acknowledged Semantic Move, 2026-07-15

Classification: **Local**, **headless UI/contract**, **same-host encrypted
loopback**, and **Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`eb83801ff8b1f81cc327f66eb6d97ead2018a881`

## Local environment and commands

```text
OS: macOS 26.5.2 (build 25F84), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4.27133
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
  --filter 'FullyQualifiedName~DesktopActivityRuntimeTests.AuthenticatedRuntimesMoveOnlyAfterVerifiedTargetReceipt|FullyQualifiedName~DesktopActivityRuntimeTests.AuthenticatedTargetRejectionKeepsMoveSourceActive'
```

Observed results for the implementation working tree:

- locked restore passed for all 24 projects;
- format verification, patch whitespace check, and the desktop UI forbidden
  color/font/emoji audit passed;
- Release build passed with 0 warnings and 0 errors;
- the complete unfiltered suite passed 431 tests with 0 failures and 0 skips:
  105 desktop, 103 transport, 90 security, 24 integration, 33 domain, 17
  protocol, 8 platform-contract, 12 Windows-adapter, 10 macOS-adapter, 15
  Linux-adapter, and 14 DNS-SD tests;
- 20 independent Avalonia Headless accessibility processes passed;
- 20 independent Activity codec/session processes passed;
- 20 independent authenticated Move success/rejection loopback processes
  passed;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0, printed
  `Source preserved: True` and `Target resumed: True`, and returned a committed
  payload-free Handoff receipt; it remains a Handoff simulator and is not
  counted as Move evidence;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

Selected TDD red gates failed first because the production window had no Move
preview control, a committed Move reused Handoff's `NO UNDO REQUIRED` text,
uncertain/rejected Move summaries called the operation a semantic copy, and the
shared receipt's automation name was hard-coded as Handoff. The green behavior
adds a separate keyboard-operable Move preview, operation-aware receipt and undo
text, and operation-neutral shared target/receipt automation metadata.

## Hosted implementation-commit results

CI run
[29393014808](https://github.com/happys2333/flowspan/actions/runs/29393014808)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (windows-latest)` (`87280353308`);
- `Test (macos-latest)` (`87280353312`);
- `Test (ubuntu-latest)` (`87280353315`); and
- `Secret scan` (`87280353314`).

Each hosted OS restored the locked 24-project graph, verified formatting, built
with 0 warnings and 0 errors, ran all 431 tests with 0 failures and 0 skips, ran
the explicit TEST MODE desktop composition validator, ran the deterministic
Handoff simulator, and uploaded test evidence. Every suite reported 105 desktop,
103 transport, 90 security, 24 integration, 33 domain, 17 protocol, 8
platform-contract, 12 Windows-adapter, 10 macOS-adapter, 15 Linux-adapter, and 14
DNS-SD passes. Each validator printed
`Flowspan desktop composition validation passed in explicit TEST MODE.`; each
simulator printed `Source preserved: True` and `Target resumed: True`.

CodeQL run
[29393014850](https://github.com/happys2333/flowspan/actions/runs/29393014850)
also completed successfully. Its `Analyze C#` job (`87280353463`) restored the
locked dependency graph, built the analyzed source, scanned 151/151 C# files,
successfully uploaded the SARIF result, and reported a successful job status.

Both runs were created at `2026-07-15T06:01:29Z`. CI completed at
`2026-07-15T06:04:04Z`, and CodeQL completed at `2026-07-15T06:04:32Z`. Run,
job, step, commit, timestamp, suite-count, validator-output, simulator-output,
and CodeQL coverage evidence was queried with `gh run view` on 2026-07-15.

The implementation commit is green. This evidence-document commit and the
subsequent closure commit must independently pass the same workflows before task
7.3b is recorded complete.

The evidence-document commit
`3ea5e10f9a07f0b71fd40b000df975d8b22836bc` was then independently verified by
CI run
[29393553679](https://github.com/happys2333/flowspan/actions/runs/29393553679)
and CodeQL run
[29393553720](https://github.com/happys2333/flowspan/actions/runs/29393553720).
The second CI run again passed all 431 tests, locked restore, formatting,
warning-free builds, TEST MODE composition, deterministic Handoff simulator,
artifact upload, and Secret Scan in jobs `87281960616` (Windows), `87281960652`
(macOS), `87281960696` (Ubuntu), and `87281960612` (Secret Scan). The second
CodeQL job `87281960754` again scanned 151/151 C# files, uploaded results, and
completed successfully. Both runs were created at `2026-07-15T06:12:56Z`; CI
completed at `2026-07-15T06:15:26Z` and CodeQL at
`2026-07-15T06:16:01Z`.

With the implementation and evidence commits independently green, the 7.3b
automated acceptance evidence is complete. The closure commit that records this
status remains subject to the same branch CI, Secret Scan, and CodeQL workflows;
the status is effective only after those final-HEAD checks pass.

## What this proves

- The desktop offers a separate Move preview and keyboard-operable confirmation
  that states target resume precedes source close.
- A committed `workspace.note/v1` Move removes the closed source from the active
  desktop Activity list only after a precisely bound target receipt.
- Missing source-side `activity.receive`, no live authenticated peer, live
  target-side `activity.offer` rejection, delivery failure, and acknowledgement
  loss retain the source Activity.
- A source-close failure remains committed at the target, retains the source,
  and is projected as `COMMITTED WITH WARNING / SourceCleanupFailed` with a
  possible two-copy warning.
- Deterministic application tests prove target-first ordering, idempotent target
  delivery, and recovery after acknowledgement loss. Same-host TCP tests exercise
  production identity authentication, encrypted framing, desktop authorization,
  target acceptance, target rejection, and payload-free receipts.
- Handoff and Move retain distinct UI semantics while sharing one bounded
  control channel. Shared target and receipt metadata is operation-neutral, and
  the global state remains `NOT SHARING`.

## What this does not prove

- No physical LAN, second machine, Wi-Fi/Ethernet interface change, firewall,
  sleep/wake, or cross-machine packet-loss path was exercised.
- Hosted runner success is portable Windows/macOS/Linux contract evidence, not
  physical-device, native-permission, or packaged-app evidence.
- The deterministic simulator still demonstrates Handoff, not Move.
- `workspace.note/v1` and the desktop operation journal remain in memory;
  restart recovery, persistent history, and user-guided duplicate reconciliation
  are not implemented.
- No arbitrary third-party application process, unsaved internal state,
  credential, secure input, screen media, or remote input was transferred.
- Remote Window, replace, swap, Mirror, driver switching, Activity Groups,
  Scenes, and compensating undo remain later tasks.
- Headless automation contracts do not prove native screen-reader speech, focus
  rendering, contrast, scaling, reduced motion, localization, or packaged
  Windows/macOS/Linux accessibility.
- This evidence does not satisfy the physical-device, native-permission,
  packaging, independent security-review, or complete v1 acceptance gates.
