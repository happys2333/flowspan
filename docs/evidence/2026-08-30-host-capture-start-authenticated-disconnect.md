# Host capture-start authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation and production-fix commit:
`fe0be79e0accbbb0cd4eef27b62e12620a18eccf`

Final hosted evidence tree:
`a0c964802eb75e58a5f9b1b276c172090331e123`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 36th production-composed managed tracer execution. Its
fault is authenticated transport loss while the host capture `StartAsync` call
still owns the HC boundary. It therefore advances only HC Disconnect from
Missing to Partial. AD Disconnect and CL Disconnect remain Partial, and every
other matrix cell is unchanged.

`HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes`
uses real authenticated protocol-1.7 loopback. The participant has returned
Ready, and exact bilateral `FSM1` sessions are attached with the same binding.
The host begins capture before any final Admission publication. Capture emits
its required pre-Admission frame, waits until that owner is disposed exactly
once, and enters the test hook without returning from capture Start.

At that boundary, capture Start count is one, the authenticated host generation
is current, and Admission publication, media send, participant render, and input
are all zero. The hook starts real `participantConnection.DisposeAsync()` and
waits for a barrier published after the real host revocation callback returns.
It does not await full connection disposal. The old generation is then non-
current and cannot be reacquired, while Trust, the exact peer fingerprint, and
the sole `mirror.view` grant remain unchanged.

After the hook returns, capture Start finishes. Host Start must preserve the
causal loss of current authenticated Connection authority rather than allowing
a later local controller outcome to overwrite it. The exact public result is
`authenticated_connection_stale`, with no inner exception, peer fingerprint, or
dependency payload. No Admission is published, the frame gate never opens, and
capture/input receive local Emergency Stop. Host Start awaits its owned cleanup
before returning the bounded failure. The test then joins participant disconnect
and session completion outside the hook and verifies that the controller,
capture/input/session, renderer, protection, permission observer, Emergency
Stop, media sessions, route, directory, handler, channel, connection, control,
and host-generation owners are drained across both managed nodes. The exact
source lease remains current.

## TDD and production fix

The first exact production-composed row was RED:

- expected: `authenticated_connection_stale`;
- actual: `emergency_stop_won_start_race`.

The Preparation reservation was already promoted and its temporary Connection
registration released. The disconnect instead made the authenticated Connection
non-current and invoked its retained live revocation callback, but after
controller/capture Start returned the coordinator projected the later local
Start result first. The minimal production change adds one
`ValidateCurrentHostFacts` call immediately after controller `StartAsync`
returns and before the returned Start reason is projected. That revalidation
preserves the already-linearized authenticated disconnect as the causal failure.
The same row is GREEN after the change.

The existing post-promotion media-mutation tracer expectation changes from
`session_not_idle` to `authenticated_connection_stale` for the same reason: its
same-generation `RequestControlStop` makes the Connection non-current and
invokes the retained live callback. Post-Start revalidation now reports that
causal fact instead of the later controller surface state.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

Final local results at exact commit `fe0be79`:

- focused HC disconnect row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `36/36` in Debug and Release;
- Desktop: `713/713` in Debug and Release;
- solution tests: `2577/2577` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus two independent strict reviews: 0 P0, 0 P1, and 0 P2
  findings.

## Hosted exact-SHA evidence

[CI run `33303210427`](https://github.com/happys2333/flowspan/actions/runs/33303210427)
completed with `success`, run number 213 attempt 1, for exact evidence tree
`a0c964802eb75e58a5f9b1b276c172090331e123`. Each downloaded test artifact
contains exactly 12 TRX files. Structured aggregation reports `2577/2577`
total, executed, and passed on Linux, macOS, and Windows; every failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable, not-
executed, disconnected, warning, completed, in-progress, and pending counter is
zero. Downloaded bytes independently reproduce each service outer SHA-256:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99235034230` | `9729646799` | `1a1a8573f54e2def86994b1bfe241224763b6db3f5e3b2cd086703d9b9357de2` |
| macOS | `99235034090` | `9729647216` | `0027be3cd3d315877dfbe6496637cb363efd9fd59f1e749105ce54df46fe7f64` |
| Windows | `99235034224` | `9729659650` | `d7aa809f19726836b32dd4541b56fc7bd4688e1a9cc109f74193e6fbd1c9f4d1` |

Secret Scan job `99235034306` completed successfully. Artifact `9729604531`
has independently reproduced outer SHA-256
`c711280c1b135f65c0459fe5432cce76887c64b2007eb72b9c582e9036160447`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
records SARIF 2.1.0 and Gitleaks semantic version v8.0.0, and contains 208
rules with 0 results.

[CodeQL run `33303210391`](https://github.com/happys2333/flowspan/actions/runs/33303210391),
run number 213 attempt 1, completed with `success`. Job `99235034007` produced
analysis ID `1693707908` and SARIF ID
`c459fdae-a452-11f1-8433-e6e74c3efa79`; service warning/error text is empty
and the exact branch ref has 0 open alerts. The 230,952-byte SARIF payload has
SHA-256 `cc4798b5d05e291920f1519cd6b859f3812651f23ba1c7cb31ecfb430548092e`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results.

All three reproducible packages report version `0.1.213`, exact evidence SHA
`a0c964802eb75e58a5f9b1b276c172090331e123`, and
`unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes `5/5`, and
the repository `Flowspan.Release verify` command passes each artifact directory.
Downloaded bytes independently reproduce the service outer digests, inner
archive digests, and manifest-bound signed-tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99235540873` | `9729692315` | `84c6576b787949a212e319675fac741a448c659e21b2772298a2eec5a139d123` | `681401feecc1811c9105e697a447c9770bf569184edeff43109a1bdeeb57f1f1` | `383efc55e3f7feafa7bd0a83e5b81a70f20f5ffc8c2446825962a2d9e87d9901` |
| `osx-arm64` | `99235540856` | `9729682041` | `7e1de9431d431c4c2d29f167280f2136c1b7f3e248e51e7a47f4cb8e1a04113b` | `0e8f9e54594f51edb28a591165c755aabe431a1edfcbaee40fae357b3b04b083` | `117c22264c9363ed67f4f55d976e3ce04da843f5178363589cf10992f7df2a11` |
| `linux-x64` | `99235540891` | `9729678963` | `bd8f843669737b88eaf4dd353038333c27608ff3d2e917d9471d2a94a42e5199` | `86ba1a91a24c7f5d0cfeb0fad914f2c0f2256971068516a7bdd9a1c21568f494` | `5f5aa42abefaffbd1f918b66ecd7791a689336b23f8d5e8359046b69c71c73bb` |

These hosted results prove managed build/test, static-analysis, content-lock,
and reproducible unsigned-package properties for exact evidence tree
`a0c9648`. They do not prove native APIs, physical two-device operation,
package signing, notarization, or release acceptance. Earlier CI `33302708813`
and CodeQL `33302708801` target `17a3401`; they remain documentation-only
history rather than evidence for the `fe0be79` implementation.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one authenticated disconnect after Ready and attachment
while capture Start owns the host commit boundary. It does not complete every HC
boundary, other disconnect phases, disconnect-plus-cleanup faults, or native
non-cooperative teardown. HC Disconnect remains Partial; AD Disconnect and CL
Disconnect remain Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
