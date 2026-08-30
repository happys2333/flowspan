# Host capture-start authority-revoke checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`62e9372aef378e8c085ccf79502104f63ae8aa76`

First hosted documentation tree:
`9ca4b2c5665cc7ffd462a1a59b8314388f16bc58`

Final hosted evidence tree:
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 37th production-composed managed tracer execution. Its
fault is a real host authority revoke while capture Start still owns HC. It
therefore advances only HC Revoke from Missing to Partial. HC, AD, and CL
Disconnect remain Partial, and every other matrix cell is unchanged.

`HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes` shares the
capture-start runner with the authenticated-disconnect row. Ready has arrived,
exact bilateral `FSM1` media sessions are attached, and capture has emitted its
pre-Admission frame. That frame owner is disposed exactly once and the hook runs
before capture Start returns. Admission publication, media send, participant
render, and input are all zero.

The hook calls real fingerprint-bound `hostTrust.UpdateCapabilitiesAsync` with
`CapabilityGrant.None`. The mutation returns `Applied` and reaches the real host
revocation callback barrier. The current Trust record and exact peer fingerprint
remain present, while `mirror.view` is absent and the Capability set is empty.
The old authenticated generation is non-current and cannot be reacquired.

After the hook returns, the `fe0be79` post-Start host-fact revalidation preserves
the already-linearized Connection cause. Host Start exposes exact bounded
`authenticated_connection_stale` with no inner exception, fingerprint, or
dependency payload. No Admission is published and the frame gate never opens.
Capture/input Emergency Stop locally; full session completion is joined; and the
controller, capture/input/session, renderer, protection, permission observer,
Emergency Stop, media sessions, route, directory, handler, channel, connection,
control, and host-generation owners drain across both nodes. The exact source
lease remains current.

No additional production change was required. The one-line revalidation added
at `fe0be79` for capture-start authenticated disconnect is sufficient to retain
the causal result for this authority-revoke sibling.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact commit `62e9372`:

- focused HC authority-revoke row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `37/37` in Debug and Release;
- Desktop: `714/714` in Debug and Release;
- solution tests: `2578/2578` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

[CI run `33304022418`](https://github.com/happys2333/flowspan/actions/runs/33304022418),
run number 214 attempt 1, is **failure evidence**, not a successful hosted
checkpoint, for exact documentation tree `9ca4b2c` containing this row.

The authority-revoke tracer itself was not the reported failure. Only macOS job
`99237248671` failed, in the unrelated local-pairing test
`DisposeRejectsAndStopsCancellationIgnoringLateEnableSession`: it expected
`OperationCanceledException` but observed `ObjectDisposedException`. Ubuntu job
`99237248699`, Windows job `99237248779`, and Secret Scan job `99237248763`
succeeded. Package matrix job `99237778338` was skipped, so no package result is
claimed from this run.

[CodeQL run `33304022374`](https://github.com/happys2333/flowspan/actions/runs/33304022374),
run number 214 attempt 1, independently completed with `success`. Job
`99237248324` produced analysis ID `1693740950` and SARIF ID
`7eace976-a455-11f1-9f4a-a44bc3e1e004`; service warning/error text is empty and
the analysis reports 52 rules with 0 results. This does not convert failed CI
into successful evidence.

Production fix `72394484e9fd0fd556497641f1ac5d79afe80bce` restores
deterministic lifetime-cancellation precedence for that unrelated pairing race.
Final exact-tree evidence containing both fixes and these records follows.

### Final successful exact tree

[CI run `33305006486`](https://github.com/happys2333/flowspan/actions/runs/33305006486)
and [CodeQL run `33305006421`](https://github.com/happys2333/flowspan/actions/runs/33305006421)
completed with `success`, run number 215 attempt 1, for exact tree
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`.

Each downloaded test artifact contains exactly 12 TRX files and reports
`2580/2580` total, executed, and passed; every non-success counter is zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99239868630` | `9730209009` | `8ac31011acebfcd9584896c2f112ad3da4928f70771e0900d32702bb940f5b11` |
| macOS | `99239868647` | `9730192596` | `2ea7053d970975160446bc7e0581e2a9bfe96f5951e2fef3c684b9cc9e7e6a0d` |
| Windows | `99239868561` | `9730209505` | `519db834f49e509c0c3ceae82ec316569ef688051cd7ddfe1a02423cb3270652` |

Secret Scan job `99239868650`, artifact `9730165279`, has outer SHA-256
`1f982b74073855a127aafe9ee79f07578f02554a7e549ccee607d16b9ace83e8`.
Its 45,825-byte SARIF payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
and reports Gitleaks 208 rules with 0 results.

CodeQL job `99239868295` produced analysis ID `1693781026` and SARIF ID
`ef0849c4-a458-11f1-9e0f-7042e9d1722e`. Its 230,952-byte payload has SHA-256
`1be9b39c8d54589351f58c10947d07819f025162d2d5361433467804d57669ba`,
reports CodeQL 2.26.4 / C# queries 1.9.2, 52 rules, 0 results, empty warning/
error text, and 0 exact-ref open alerts.

All packages report version `0.1.215`, exact SHA, and
`unsigned-test-artifact`; every `SHA256SUMS` entry passes `5/5`, and repository
verification succeeds:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99240277448` | `9730233902` | `8e17d82473ceef22be28f85c86c658d3f2f13d5e92a5e09bae489e17f4293876` | `c2fc0c5e1a7133020e52a4ca0dbbf694717c3e41724092be7a0cd1102d75595f` | `431dddc211d44da6cb3b80665d753cf8c275894e421ab787e70ec036371b3f0e` |
| `osx-arm64` | `99240277437` | `9730225499` | `ae28e42945e84ac3aa9f7da31025fb4b62f24e36ca664067ca7218d541208b1d` | `cfe82ef137ac24e2a414a1b51cdb506b6aacff458873c51c035976970a96a9d3` | `c16b4630add2caa793b62669f3dfab333749a400f1709c780e74eeb5cafecfaf` |
| `linux-x64` | `99240277462` | `9730231245` | `c1786023eb3106871f0d52a4b7d0ca73b7257e06ae4eae977624c6660a384587` | `a7c2fe43d3504a080562975a04f80383317f306ca65cf62ec016837e0ca4f210` | `ccdf4dc4bb8936d16b0b3669815882b8195e16104427872d7de7f8ce6a3fb24e` |

These are managed and reproducible unsigned-package results, not native,
physical-device, signing, notarization, or release-acceptance evidence.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one authority revoke after Ready and attachment while
capture Start owns HC. It does not complete every HC boundary, other revoke
phases, revoke-plus-cleanup faults, or native non-cooperative teardown. HC Revoke
remains Partial. HC, AD, and CL Disconnect remain Partial. Tasks 5, 5.5a, and
5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
signing/notarization/release gate, and the long-term Goal remain open.
