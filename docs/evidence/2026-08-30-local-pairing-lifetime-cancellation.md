# Local pairing lifetime-cancellation precedence checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Production-fix commit:
`72394484e9fd0fd556497641f1ac5d79afe80bce`

Final hosted evidence tree:
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint repairs deterministic lifetime-cancellation precedence in
`DesktopLocalPairingRuntime.EnableAsync`. It is unrelated to a Remote Window
boundary-matrix status and changes no tracer count.

Dispose initiates the runtime lifetime token's asynchronous cancellation and
sets the runtime disposed state. A deliberately cancellation-ignoring session
start may return after disposal before asynchronous cancellation propagation to
the linked token has executed. In that late `startRejected` classification
window, the runtime previously checked the linked token and then the disposed
flag. It could therefore surface `ObjectDisposedException` even though lifetime
cancellation had already won the operation.

The one-line fix checks `lifetimeCancellation.Token` before the linked token and
disposed state. A lifetime cancellation now deterministically produces
`OperationCanceledException`; linked caller cancellation retains the next
precedence, while genuine disposal without either cancellation remains
`ObjectDisposedException`. Cleanup and late-session Stop behavior are unchanged.

## Hosted failure history

[CI run `33304022418`](https://github.com/happys2333/flowspan/actions/runs/33304022418),
run number 214 attempt 1, is **failure evidence**, not a successful checkpoint,
for exact documentation tree
`9ca4b2c5665cc7ffd462a1a59b8314388f16bc58`.

Only macOS job `99237248671` failed, in
`DesktopLocalPairingRuntimeTests.DisposeRejectsAndStopsCancellationIgnoringLateEnableSession`:

- expected: `OperationCanceledException`;
- actual: `ObjectDisposedException` from `DesktopLocalPairingRuntime.EnableAsync`.

Ubuntu job `99237248699`, Windows job `99237248779`, and Secret Scan job
`99237248763` succeeded. The package matrix job `99237778338` was skipped because
the required test matrix was not successful; no package result from this run is
claimed.

[CodeQL run `33304022374`](https://github.com/happys2333/flowspan/actions/runs/33304022374),
run number 214 attempt 1, independently completed with `success`. Job
`99237248324` produced analysis ID `1693740950` and SARIF ID
`7eace976-a455-11f1-9f4a-a44bc3e1e004`; service warning/error text is empty and
the analysis reports 52 rules with 0 results. Static analysis success does not
convert the failed CI run into successful evidence.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopLocalPairingRuntimeTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopLocalPairingRuntimeTests'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~DisposeRejectsAndStopsCancellationIgnoringLateEnableSession' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~DisposeRejectsAndStopsCancellationIgnoringLateEnableSession' || exit 1; done
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

Results:

- local-pairing runtime class: `29/29` in Debug and Release;
- exact cancellation-ignoring late-enable row: twenty fresh processes in Debug
  and twenty in Release, all passing;
- combined current tree through `858acb2`: solution `2580/2580` in Debug and
  Release;
- solution builds: zero warnings and zero errors; and
- format verification and `git diff --check`: passed.

## Hosted post-fix evidence

[CI `33305006486`](https://github.com/happys2333/flowspan/actions/runs/33305006486)
and [CodeQL `33305006421`](https://github.com/happys2333/flowspan/actions/runs/33305006421)
completed with `success`, run 215 attempt 1, for post-fix exact tree
`c4c02a360506e12147998cdc6f9a0a5ffa4e4ac0`. Each of the three platform
artifacts contains 12 TRX files with `2580/2580` passed and every non-success
counter zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99239868630` | `9730209009` | `8ac31011acebfcd9584896c2f112ad3da4928f70771e0900d32702bb940f5b11` |
| macOS | `99239868647` | `9730192596` | `2ea7053d970975160446bc7e0581e2a9bfe96f5951e2fef3c684b9cc9e7e6a0d` |
| Windows | `99239868561` | `9730209505` | `519db834f49e509c0c3ceae82ec316569ef688051cd7ddfe1a02423cb3270652` |

Secret job `99239868650`, artifact `9730165279`, has outer SHA-256
`1f982b74073855a127aafe9ee79f07578f02554a7e549ccee607d16b9ace83e8`
and payload SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`
with Gitleaks 208/0. CodeQL job `99239868295`, analysis `1693781026`, SARIF
`ef0849c4-a458-11f1-9e0f-7042e9d1722e`, has payload SHA-256
`1be9b39c8d54589351f58c10947d07819f025162d2d5361433467804d57669ba`,
52/0 results, empty warnings/errors, and 0 exact-ref alerts.

All version-0.1.215 packages bind the exact SHA and unsigned-test state; their
`5/5` checksums and repository verification pass:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99240277448` | `9730233902` | `8e17d82473ceef22be28f85c86c658d3f2f13d5e92a5e09bae489e17f4293876` | `c2fc0c5e1a7133020e52a4ca0dbbf694717c3e41724092be7a0cd1102d75595f` | `431dddc211d44da6cb3b80665d753cf8c275894e421ab787e70ec036371b3f0e` |
| `osx-arm64` | `99240277437` | `9730225499` | `ae28e42945e84ac3aa9f7da31025fb4b62f24e36ca664067ca7218d541208b1d` | `cfe82ef137ac24e2a414a1b51cdb506b6aacff458873c51c035976970a96a9d3` | `c16b4630add2caa793b62669f3dfab333749a400f1709c780e74eeb5cafecfaf` |
| `linux-x64` | `99240277462` | `9730231245` | `c1786023eb3106871f0d52a4b7d0ca73b7257e06ae4eae977624c6660a384587` | `a7c2fe43d3504a080562975a04f80383317f306ca65cf62ec016837e0ca4f210` | `ccdf4dc4bb8936d16b0b3669815882b8195e16104427872d7de7f8ce6a3fb24e` |

This successful exact tree supersedes `33304022418` as post-fix evidence. It
remains managed and unsigned, not physical/native/release proof.

## Explicit limitations

This is managed same-host cancellation/cleanup evidence. It does not prove
physical LAN pairing, native firewall behavior, packaged accessibility,
credential-store behavior, signing, notarization, or release acceptance. The
fix closes one late Start classification race; broader load, native networking,
and physical Device evidence remain open.
