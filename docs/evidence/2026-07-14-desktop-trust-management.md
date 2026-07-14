# Evidence: desktop trusted-device management, 2026-07-14

Classification: **Local**, **headless UI/contract**,
**protected-store integration**, and **Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`f6bbbf0333df58ef64388368c5e2aeaa67bf8024`

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
dotnet build Flowspan.slnx --no-restore --configuration Release -warnaserror
dotnet test Flowspan.slnx --no-build --no-restore --configuration Release
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

Observed results:

- locked restore passed for all 24 projects;
- format verification and the patch whitespace check passed;
- Release build passed with 0 warnings and 0 errors;
- the complete unfiltered suite passed 330 tests with 0 failures and 0 skips;
  this included 39 desktop tests, 86 security tests, and all 10 macOS platform
  tests on this host;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0, preserved the source,
  resumed the target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

The local disposable Keychain paths happened to pass in this run. That is useful
macOS-host evidence, but it is not a claim about packaged-app ACLs, locked
Keychains, other user profiles, or Windows/Linux credential stores.

## Hosted CI results

CI run
[29304377905](https://github.com/happys2333/flowspan/actions/runs/29304377905)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (windows-latest)` (`86994638075`);
- `Test (macos-latest)` (`86994638082`);
- `Test (ubuntu-latest)` (`86994638083`);
- `Secret scan` (`86994638072`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
warnings as errors, ran all 330 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. Every desktop suite reported 39 passes
and every security suite reported 86 passes. The validator printed its explicit
TEST MODE success line on all three jobs; each simulator printed protocol 1.0,
`Source preserved: True`, and `Target resumed: True`.

CodeQL run
[29304377902](https://github.com/happys2333/flowspan/actions/runs/29304377902)
also completed successfully for the same commit. Its `Analyze C#` job
(`86994637820`) restored locked dependencies, built the analyzed source, and
completed analysis successfully.

Both runs were created at `2026-07-14T03:47:59Z`; CI completed at
`2026-07-14T03:50:07Z`, and CodeQL completed at `2026-07-14T03:50:59Z`.
Run, job, step, commit, timestamp, suite-count, validator-output, and simulator
output evidence was queried with `gh run view` on 2026-07-14.

## What this proves

- Production desktop composition selects the existing DPAPI, Keychain, or
  Secret Service Trust payload adapter for the current OS, while validation
  composition remains explicitly in-memory and labelled TEST MODE.
- The desktop reads immutable trusted-peer projections in canonical Device ID
  order and displays the persisted display name, Device ID, fingerprint,
  verification time, and all seven independent v1 Capability grants.
- Saving sends the complete grant plus the displayed Device ID and fingerprint.
  Conditional in-memory and persistent mutations reject a stale fingerprint, so
  an old UI snapshot cannot edit or revoke a replacement identity.
- Capability downgrade and revocation commit the authoritative Trust change
  before asking affected sessions to stop. A typed post-commit stop failure is
  surfaced as authorization removed with shutdown unconfirmed; storage and
  other aggregate failures cannot be misreported as that partial success.
- Protected-store load/save/revoke failures fail closed, do not expose injected
  exception canaries, and refresh presentation state from the authority rather
  than applying an optimistic local mutation.
- Mutation serialization, cancellation, concurrent disposal, persisted restart,
  seven-Capability round-trip, identity replacement, and two-step revocation
  have deterministic tests.
- Checked production XAML exposes keyboard-operable selection, Capability save,
  revoke review/cancel/confirm controls, declared automation names, and a
  mutation result that remains visible after the last peer is revoked. These
  paths pass the Avalonia headless suite on all three hosted OS images.
- Window close cancels admitted Trust work and asynchronously waits for Trust,
  pairing, and identity resources instead of synchronously blocking the UI
  thread.
- The implementation commit passed the configured secret scan and CodeQL
  workflow.

## What this does not prove

- The production desktop still does not compose discovery, advertisement, the
  inbound listener, or an outbound pairing initiator. An empty Trust list does
  not mean a peer scan ran, and the UI does not claim a connection.
- Hosted matrix results and platform-contract fakes are not physical-device or
  physical-LAN evidence. No Windows or Linux desktop with an unlocked real user
  credential store was operated, and no person paired two machines.
- Session-stop behavior uses deterministic revocable-session doubles; no real
  mirrored, remote-window, file, or Activity session was active during revoke.
- Headless keyboard routing and automation metadata do not prove screen-reader
  speech, native focus appearance, high contrast, scaling, or reduced motion.
- No native capture/input permission was requested, and no identity-change
  discovery warning, emergency-stop service, packaging, signing, install,
  update, or long-duration behavior ran.

Those claims remain open under task 7.2, task 7.4, the platform/native tasks,
packaging tasks, and the v1 release criteria.
