# Host capture-start rejection checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`858acb2c28321ed8603646227d8834eef318405a`

Immediate pairing-race fix dependency:
`72394484e9fd0fd556497641f1ac5d79afe80bce`

Final hosted evidence tree:
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 39th production-composed managed tracer execution. Its
fault is a bounded negative returned by capture Start while HC owns the host
commit boundary. It therefore advances only HC Reject from Missing to Partial.
HC Reject, Cancel, Revoke, and Disconnect are now all Partial. Every other matrix
cell is unchanged.

`HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes` reuses
the capture-start runner after Ready and exact bilateral `FSM1` attachment.
Capture emits its pre-Admission frame and observes that owner disposed exactly
once. The hook runs while capture Start is still current, probes the exact same
authenticated generation, and immediately disposes that probe. Trust, peer
fingerprint, sole `mirror.view`, and transport remain unchanged.

After the hook, capture returns
`LocalBoundaryResult.Failed("capture_start_failed")`. Host Start exposes the
exact bounded `capture_start_failed` reason with no inner exception, fingerprint,
or dependency payload. Admission publication, media send, participant render,
and input remain zero. The frame gate never opens.

Production performs owned fail-close and connection disposal. Capture and input
use their ordinary Stop boundaries; the exact test asserts both Emergency Stop
counts remain zero. The controller, capture/input/session, renderer, protection,
permission observer, Emergency Stop registration, media sessions, route,
directory, handler, channel, connection, control, and host-generation owners
drain across both nodes. The exact source lease remains current.

No production change was required for this row.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results through exact commit `858acb2`:

- focused HC rejection row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `39/39` in Debug and Release;
- Desktop: `716/716` in Debug and Release;
- combined solution tests: `2580/2580` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

[CI `33305006486`](https://github.com/happys2333/flowspan/actions/runs/33305006486)
and [CodeQL `33305006421`](https://github.com/happys2333/flowspan/actions/runs/33305006421)
completed with `success`, run 215 attempt 1, for exact tree
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`, containing `858acb2`,
`7239448`, and these records. All three test artifacts contain 12 TRX files,
`2580/2580` passed, and zero non-success counters:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99239868630` | `9730209009` | `8ac31011acebfcd9584896c2f112ad3da4928f70771e0900d32702bb940f5b11` |
| macOS | `99239868647` | `9730192596` | `2ea7053d970975160446bc7e0581e2a9bfe96f5951e2fef3c684b9cc9e7e6a0d` |
| Windows | `99239868561` | `9730209505` | `519db834f49e509c0c3ceae82ec316569ef688051cd7ddfe1a02423cb3270652` |

Secret job `99239868650`, artifact `9730165279`, has outer SHA-256
`1f982b74073855a127aafe9ee79f07578f02554a7e549ccee607d16b9ace83e8`
and payload SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
with Gitleaks 208/0. CodeQL job `99239868295` produced analysis `1693781026`,
SARIF `ef0849c4-a458-11f1-9e0f-7042e9d1722e`, payload SHA-256
`1be9b39c8d54589351f58c10947d07819f025162d2d5361433467804d57669ba`,
52/0 results, empty warnings/errors, and 0 exact-ref alerts.

All packages report version `0.1.215`, exact SHA, and unsigned-test state;
checksums pass `5/5` and repository verification succeeds:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99240277448` | `9730233902` | `8e17d82473ceef22be28f85c86c658d3f2f13d5e92a5e09bae489e17f4293876` | `c2fc0c5e1a7133020e52a4ca0dbbf694717c3e41724092be7a0cd1102d75595f` | `431dddc211d44da6cb3b80665d753cf8c275894e421ab787e70ec036371b3f0e` |
| `osx-arm64` | `99240277437` | `9730225499` | `ae28e42945e84ac3aa9f7da31025fb4b62f24e36ca664067ca7218d541208b1d` | `cfe82ef137ac24e2a414a1b51cdb506b6aacff458873c51c035976970a96a9d3` | `c16b4630add2caa793b62669f3dfab333749a400f1709c780e74eeb5cafecfaf` |
| `linux-x64` | `99240277462` | `9730231245` | `c1786023eb3106871f0d52a4b7d0ca73b7257e06ae4eae977624c6660a384587` | `a7c2fe43d3504a080562975a04f80383317f306ca65cf62ec016837e0ca4f210` | `ccdf4dc4bb8936d16b0b3669815882b8195e16104427872d7de7f8ce6a3fb24e` |

These hosted results prove the managed 39-case tree and reproducible unsigned
packages, not native, physical-device, signing, notarization, or release
acceptance.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one bounded capture Start rejection after Ready and
attachment. It does not complete every HC negative boundary, other rejection
phases, rejection-plus-cleanup faults, or native non-cooperative teardown. HC
Reject remains Partial; HC Cancel, Revoke, and Disconnect remain Partial. Tasks
5, 5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/
physical/signing/notarization/release gate, and the long-term Goal remain open.
