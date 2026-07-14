# Evidence: desktop composition root, 2026-07-14

Classification: **Local**, **headless UI/contract**, and **Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`3439e2d3e367a113f96f1af96216da4f535ced46`

## Local environment

```text
OS: macOS 26.5.2 (build 25F84), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
```

## Local commands and results

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-restore \
  --filter "FullyQualifiedName!~MacOSDeviceIdentityStoreTests.ProductionStoreUsesSecurityFrameworkOnMacOSAndRejectsOtherPlatforms&FullyQualifiedName!~MacOSDeviceIdentityStoreTests.ConcurrentFirstLaunchesConvergeInMacOSKeychain&FullyQualifiedName!~MacOSTrustPayloadStoreTests.ProductionStoreUsesSecurityFrameworkOnMacOSOnly"
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive
git diff --check
```

Observed results:

- locked restore passed for 24 projects;
- format verification passed;
- Release build passed with 0 warnings and 0 errors;
- 292 tests passed after excluding exactly three real macOS Keychain tests;
  the desktop suite contributed 8 passing tests;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.` and
  exited successfully;
- the simulator negotiated protocol 1.0, preserved the source, resumed the
  target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any of the
  24 projects;
- the patch whitespace check passed.

The unfiltered local suite executed all other tests successfully but the three
disposable production-Keychain create paths returned Security.framework status
`-25293` (`errSecAuthFailed`). `security add-generic-password` and
`security show-keychain-info` independently returned the same authentication
failure for the current login Keychain. No product code or test was weakened to
turn that host state into a pass.

A separate local production-start observation reached the Avalonia event loop
and created the protected identity item with service
`app.flowspan.device-identity` and account `primary-device`. The item was
retained. Because the unpackaged apphost could not be attached through an
observable LaunchServices window session, this is process/Keychain evidence,
not visual desktop or accessibility evidence.

## Hosted CI results

CI run
[29296981647](https://github.com/happys2333/flowspan/actions/runs/29296981647)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (windows-latest)` (`86972560577`);
- `Test (ubuntu-latest)` (`86972560594`);
- `Test (macos-latest)` (`86972560608`);
- `Secret scan` (`86972560580`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
warnings as errors, ran all 295 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. In particular, the macOS job completed
all 10 platform-macOS tests, including the three production Keychain paths that
the locked local login Keychain could not execute.

CodeQL run
[29296981677](https://github.com/happys2333/flowspan/actions/runs/29296981677)
also completed successfully for the same commit. Its `Analyze C#` job
(`86972560596`) restored locked dependencies, built the analyzed source, and
completed analysis successfully.

Both runs were created at `2026-07-14T00:49:56Z`; CI completed at
`2026-07-14T00:52:09Z`, and CodeQL completed at `2026-07-14T00:52:56Z`.
Run, job, step, commit, timestamp, test-count, and validator-output evidence was
queried with `gh run view` on 2026-07-14.

## What this proves

- Avalonia 12.1.0 and its reviewed transitive graph restore from committed lock
  files and compile in the repository's Windows, macOS, and Linux CI matrix.
- The desktop composition project stays outside the headless core and selects
  the existing Windows DPAPI, macOS Keychain, or Linux Secret Service identity
  store for production according to the current OS.
- Identity initialization caches one identity, blocks rather than silently
  degrading after protected-store failure, redacts exception details, supports
  retry, and cancels before disposing resources during window shutdown.
- The checked production XAML declares textual non-sharing and empty states,
  disabled emergency-stop explanation, programmatic names, visible focus
  styling, and a keyboard-operable identity disclosure. Avalonia headless input
  routing activates that disclosure on all three hosted OS images.
- CI's executable composition path can be injected only through an explicitly
  degraded in-memory identity, labels the result TEST MODE, and exits without
  requesting capture or remote-input permission.
- The implementation commit passed the configured secret scan and CodeQL
  workflow.

## What this does not prove

- The TEST MODE validator does not create a native window and does not use the
  production platform credential-store selector.
- Headless control construction and declared automation metadata do not prove
  screen-reader speech, focus visibility, high-contrast integration, text
  scaling, reduced motion, or keyboard behavior in a packaged native app.
- No visual Windows or Linux launch was observed, and the local macOS apphost
  process observation is not visual acceptance.
- No signed, notarized, installed, auto-starting, or updating package was built.
- No desktop pairing, capability editing, sharing, capture, remote input,
  emergency-stop service, physical LAN, or second-device flow ran.
- The hosted Ubuntu job does not prove a live unlocked desktop Secret Service
  collection, and hosted credential-store results do not cover locked profiles
  or packaged-app ACL/prompt behavior.

Those claims remain open in task 7.4, the platform/native tasks, and the v1
release criteria.
