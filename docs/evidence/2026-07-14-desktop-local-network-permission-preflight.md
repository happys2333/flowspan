# Evidence: desktop local-network permission preflight, 2026-07-14

Classification: **Local**, **headless UI/contract**, and **Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`c44a36481b57f8614f4076ccc4e3ebd6fbcc2d5c`

## Local environment and commands

```text
OS: macOS 26.5.2 (build 25F84), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
```

```sh
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The two affected classes were also run in 20 fresh testhost processes each:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~MainWindowAccessibilityTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~LocalPairingViewModelTests'
```

Observed results:

- format verification and the patch whitespace check passed;
- Release build passed with 0 warnings and 0 errors;
- the complete unfiltered suite passed 389 tests with 0 failures and 0 skips,
  including 82 desktop, 90 transport, 86 security, and 22 integration tests;
- 20 independent Headless-window processes each passed 5/5, for 100 additional
  UI/lifecycle executions;
- 20 independent local-pairing view-model processes each passed 16/16, for 320
  additional preflight, platform-selection, retry, and cancellation executions;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0, preserved the source,
  resumed the target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

The TDD red gates first failed because the permission-guide/view-model API did
not exist, then because the required named Headless controls were absent. The
targeted suites passed after the state boundary and XAML were implemented.

## Hosted CI results

CI run
[29343694694](https://github.com/happys2333/flowspan/actions/runs/29343694694)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (macos-latest)` (`87121757419`);
- `Test (ubuntu-latest)` (`87121757476`);
- `Test (windows-latest)` (`87121757498`);
- `Secret scan` (`87121757382`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
warnings as errors, ran all 389 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. Each desktop suite reported 82 passes,
each transport suite 90, each security suite 86, and each integration suite 22.
The current-platform test selected Windows, macOS, or Linux from the actual
runner OS. Every validator printed its explicit TEST MODE success line; every
simulator printed protocol 1.0, `Source preserved: True`, and
`Target resumed: True`.

CodeQL run
[29343694005](https://github.com/happys2333/flowspan/actions/runs/29343694005)
also completed successfully. Its `Analyze C#` job (`87121755280`) restored
locked dependencies, built the analyzed source, scanned 142/142 C# files, and
successfully uploaded results.

Both runs were created at `2026-07-14T15:04:19Z`. CI completed at
`2026-07-14T15:06:46Z`, and CodeQL completed at `2026-07-14T15:07:33Z`. Run,
job, step, commit, timestamp, suite-count, validator-output, simulator-output,
and CodeQL coverage evidence was queried with `gh run view` on 2026-07-14.

## What this proves

- Desktop startup, direct enable without review, opening the review, and
  canceling the review do not call the local-network runtime factory.
- The closed platform selector supplies Windows, macOS, or Linux guidance, and
  production composition selects from the current supported OS rather than a
  user-controlled string.
- Every guide explains the feature purpose; enumerates the advertised device
  name, Device ID, fingerprint, listener port, protocol versions, issue/expiry
  times, nonce, and signature; explicitly excludes Activity content and
  Capability grants; describes likely platform/firewall behavior; and gives a
  platform-appropriate revocation path.
- `ENABLE ON LOCAL NETWORK` cannot execute until the visible review is
  affirmatively acknowledged. A successful enable hides the review while
  preserving the global `NOT SHARING` state.
- Explicit Disable clears the acknowledgement, so the next network lifetime
  requires a new review. Enable and background failure reopen the already
  reviewed recovery surface without claiming the OS granted permission.
- Review, acknowledgement, enable, candidate/trusted-peer state, and the
  identity warning remain keyboard operable with declared automation names in
  the production XAML.
- Cancellation and disposal still drain an enable already admitted across the
  reviewed boundary.

## What this does not prove

- The guide is static education, not a native permission probe. No system
  privacy setting or firewall state was read or changed.
- No real Windows Firewall dialog, macOS Local Network prompt, Linux firewall
  or sandbox control, denial, settings navigation, revocation, or recovery was
  exercised.
- No packet capture verified that a packaged build's network traffic exactly
  matches the disclosure, and no physical LAN or second device was used.
- Headless keyboard and automation contracts do not prove native screen-reader
  speech, focus rendering, text scaling, or a packaged window.
- Screen-capture and accessibility/remote-input preflights remain tied to the
  corresponding unimplemented platform feature-use paths. This slice does not
  satisfy those release gates, physical evidence, or v1 acceptance.
