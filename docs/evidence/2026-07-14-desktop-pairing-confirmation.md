# Evidence: desktop pairing confirmation bridge, 2026-07-14

Classification: **Local**, **headless UI**, **loopback integration**, and
**Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`592ebc088c0285f7af36b118082a6258e79db68c`

## Local environment and commands

The local environment remains the macOS 26.5.2 arm64, .NET SDK 10.0.301, .NET
runtime 10.0.9 environment recorded in
[desktop-composition evidence](2026-07-14-desktop-composition.md).

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --filter "FullyQualifiedName!~MacOSDeviceIdentityStoreTests.ProductionStoreUsesSecurityFrameworkOnMacOSAndRejectsOtherPlatforms&FullyQualifiedName!~MacOSDeviceIdentityStoreTests.ConcurrentFirstLaunchesConvergeInMacOSKeychain&FullyQualifiedName!~MacOSTrustPayloadStoreTests.ProductionStoreUsesSecurityFrameworkOnMacOSOnly"
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

Observed results:

- locked restore passed for all 24 projects after the desktop-test lock file
  recorded its new transport project reference;
- format verification and the patch whitespace check passed;
- Release build passed with 0 warnings and 0 errors;
- 303 tests passed after excluding exactly the same three locally unavailable
  production-Keychain tests; the complete repository contains 306 tests;
- all 19 desktop tests passed, including two complete pairing ceremonies over
  real same-process loopback TCP connections;
- explicit TEST MODE desktop composition and the deterministic handoff simulator
  exited successfully;
- NuGet reported no known vulnerable direct or transitive package.

The local Keychain exclusion is an unchanged host authentication boundary, not a
pairing failure or product-code bypass. The hosted macOS job below ran the full
unfiltered suite.

## Hosted CI results

CI run
[29298480369](https://github.com/happys2333/flowspan/actions/runs/29298480369)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (ubuntu-latest)` (`86977046510`);
- `Test (macos-latest)` (`86977046523`);
- `Test (windows-latest)` (`86977046537`);
- `Secret scan` (`86977046546`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
warnings as errors, ran all 306 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. Each desktop suite reported 19 passes.

CodeQL run
[29298480397](https://github.com/happys2333/flowspan/actions/runs/29298480397)
also completed successfully for the same commit. Its `Analyze C#` job
(`86977046600`) restored locked dependencies, built the analyzed source, and
completed analysis successfully.

Both runs were created at `2026-07-14T01:24:42Z`; CI completed at
`2026-07-14T01:26:50Z`, and CodeQL completed at `2026-07-14T01:27:26Z`.
Run, job, step, commit, timestamp, test-count, and validator-output evidence was
queried with `gh run view` on 2026-07-14.

## What this proves

- In the complete `PairingCeremony` flow used by the loopback tests, the security
  core calls the desktop decision port only after exchanging hellos and verifying
  the peer transcript signature.
- The desktop surface shows the full peer identity, protocol, expiry, and SAS;
  confirmation stays disabled until the local owner acknowledges comparing the
  code on both devices.
- Capability selection starts empty and the surface exposes only
  `ActivityOffer` and `ActivityReceive`. Reject and view disposal return no
  Capability grant.
- One prompt is visible at a time. A concurrent request cannot replace it;
  cancellation removes it; a stale prompt ID and deliberately reordered UI
  callbacks cannot accept or restore it over a later peer.
- In the two-node accept test, neither in-memory Trust Store contains the peer
  before both decisions. After two acceptances and signed completion proofs,
  each store contains only the Capability grant chosen locally for that peer.
- In the two-node reject test, one local rejection produces `Rejected` on both
  ceremonies and both in-memory Trust Stores remain empty.
- The prompt's checked production XAML, keyboard code acknowledgement, disabled
  accept state, and declared automation names pass headless tests on all three
  hosted OS images.
- The implementation commit passed the configured secret scan and CodeQL
  workflow.

## What this does not prove

- `Flowspan.Desktop` still does not start or own the production discovery,
  listener, advertisement, trust authority, or initiator workflow. The surface
  is an adapter for an injected verified request, not a usable LAN pairing entry.
- The loopback tests use two in-memory Trust Stores. They do not prove desktop
  composition with DPAPI, Keychain, or Secret Service trust persistence.
- No person compared codes on two physical devices. Headless keyboard routing
  and automation properties are not screen-reader, visual-focus, or accidental-
  approval evidence.
- The slice does not enumerate trusted peers, edit/revoke capabilities, surface
  identity-change outcomes, or request native capture/input permissions.
- No physical LAN, firewall, multicast discovery, packaging, signing, install,
  update, or long-duration behavior ran.

Those claims remain open under task 7.2, platform/native tasks, task 7.4, and the
v1 release criteria.
