# Host capture-start caller-cancellation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`0f26c26e93c0af6013372245ba448fd839037a1c`

Final hosted evidence tree:
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 38th production-composed managed tracer execution. Its
fault is exact caller cancellation while capture Start still owns HC. It
therefore advances only HC Cancel from Missing to Partial. CL Cancel remains
Partial, and every other matrix cell is unchanged.

`HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes`
shares the capture-start runner with the authenticated-disconnect and authority-
revoke rows. A dedicated caller `CancellationTokenSource` is passed only to
`DesktopRemoteWindowHostCoordinator.StartAsync`; a separate 20-second harness
token bounds network setup, observation, and cleanup joins. The two tokens are
never treated as interchangeable evidence.

Ready has arrived, exact bilateral `FSM1` sessions are attached, and capture has
emitted its pre-Admission frame. The frame owner is disposed exactly once and the
hook runs before capture Start returns. Admission publication, media send,
participant render, and input are all zero. The hook synchronously cancels the
dedicated caller token.

Cancellation is not authority revocation. At the hook boundary, the
authenticated connection remains current; an exact generation probe acquires
the same authenticated generation and is immediately disposed. Trust, the exact
peer fingerprint, and the sole `mirror.view` grant remain unchanged.

Before rethrowing an `OperationCanceledException` carrying the exact caller
token, Host Start awaits its owned ordinary Stop, fail-close, connection
disposal, and cleanup. The test then observes that exception, joins participant
session completion, and verifies bilateral drain. No Admission or frame
authority opens, and the controller, capture/input/session,
renderer, protection, permission observer, Emergency Stop registration, media
sessions, route, directory, handler, channel, connection, control, and host-
generation owners drain across both nodes. The exact source lease remains
current.

No production change was required for this row.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact commit `0f26c26`:

- focused HC caller-cancellation row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `38/38` in Debug and Release;
- Desktop: `715/715` in Debug and Release;
- solution tests: `2579/2579` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

[CI `33305006486`](https://github.com/happys2333/flowspan/actions/runs/33305006486)
and [CodeQL `33305006421`](https://github.com/happys2333/flowspan/actions/runs/33305006421)
completed with `success`, run 215 attempt 1, for exact tree
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`. Each platform artifact contains
12 TRX files with `2580/2580` passed and every non-success counter zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99239868630` | `9730209009` | `8ac31011acebfcd9584896c2f112ad3da4928f70771e0900d32702bb940f5b11` |
| macOS | `99239868647` | `9730192596` | `2ea7053d970975160446bc7e0581e2a9bfe96f5951e2fef3c684b9cc9e7e6a0d` |
| Windows | `99239868561` | `9730209505` | `519db834f49e509c0c3ceae82ec316569ef688051cd7ddfe1a02423cb3270652` |

Secret job `99239868650`, artifact `9730165279`, has outer SHA-256
`1f982b74073855a127aafe9ee79f07578f02554a7e549ccee607d16b9ace83e8`;
its 45,825-byte payload SHA-256 is
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
with Gitleaks 208/0. CodeQL job `99239868295` produced analysis `1693781026`,
SARIF `ef0849c4-a458-11f1-9e0f-7042e9d1722e`, and a 230,952-byte payload with
SHA-256 `1be9b39c8d54589351f58c10947d07819f025162d2d5361433467804d57669ba`;
CodeQL/C# queries report 52/0, empty warnings/errors, and 0 exact-ref alerts.

All packages report version `0.1.215`, exact SHA, and unsigned-test status;
checksums pass `5/5` and repository verification succeeds:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99240277448` | `9730233902` | `8e17d82473ceef22be28f85c86c658d3f2f13d5e92a5e09bae489e17f4293876` | `c2fc0c5e1a7133020e52a4ca0dbbf694717c3e41724092be7a0cd1102d75595f` | `431dddc211d44da6cb3b80665d753cf8c275894e421ab787e70ec036371b3f0e` |
| `osx-arm64` | `99240277437` | `9730225499` | `ae28e42945e84ac3aa9f7da31025fb4b62f24e36ca664067ca7218d541208b1d` | `cfe82ef137ac24e2a414a1b51cdb506b6aacff458873c51c035976970a96a9d3` | `c16b4630add2caa793b62669f3dfab333749a400f1709c780e74eeb5cafecfaf` |
| `linux-x64` | `99240277462` | `9730231245` | `c1786023eb3106871f0d52a4b7d0ca73b7257e06ae4eae977624c6660a384587` | `a7c2fe43d3504a080562975a04f80383317f306ca65cf62ec016837e0ca4f210` | `ccdf4dc4bb8936d16b0b3669815882b8195e16104427872d7de7f8ce6a3fb24e` |

These hosted results remain managed/unsigned evidence, not native, physical,
signed, notarized, or release proof. Earlier `9ca4b2c` runs precede this row.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one exact caller cancellation after Ready and attachment
while capture Start owns HC. It does not complete every HC boundary, other
cancellation phases, cancellation-plus-cleanup faults, foreign/tokenless
cancellation, or native non-cooperative teardown. HC Cancel remains Partial and
CL Cancel remains Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
