# Host initial Authorization authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`077c996e82dd4077d24a58957c37b86383479f6e`

Final hosted evidence tree:
`077c996e82dd4077d24a58957c37b86383479f6e`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 40th production-composed managed tracer execution. Its
fault is an independent authenticated transport loss while H0 still owns initial
host facts: the real fingerprint-bound Trust authorization reservation has been
acquired, but a deterministic barrier has not yet returned that reservation to
the host coordinator. It therefore advances only H0 Disconnect from Missing to
Partial. H1 Disconnect remains Missing, CL Disconnect remains Partial, and every
other matrix cell is unchanged.

`H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes`
uses real protocol-1.7 authenticated TCP over loopback. The blocking wrapper
first obtains a real reservation from `TrustMirrorAuthorizationSource`, retains
its registration, signals the barrier, and waits before handing ownership to the
coordinator. At that boundary, the exact Connection Preparation registration
and its live revocation callback are current. The Permission reservation exists,
but the H1 Protection reservation, Emergency Stop readiness and registration,
responder route, Prepare call and wire admission, media attachment, capture,
Admission publication, media send, participant render, and input have not
opened.

The participant then disposes its authenticated control connection. The test
waits for the real host revocation callback to return rather than treating the
Dispose call itself as the mutation barrier. Connection and its exact
Preparation registration become non-current, and the old generation cannot be
reacquired. The host Trust record, peer fingerprint, sole `mirror.view` grant,
and the still-wrapper-owned Authorization registration remain current. This
separates authenticated transport loss from Trust or Capability revocation.

After the wrapper barrier is released, the normal return is the sole ownership
handoff: the coordinator receives the Authorization registration, observes the
stale authenticated Connection, and disposes the registration during owned
cleanup. Pre-handoff exceptional paths remain owned by the wrapper: it disposes
any acquired registration, preserves an out-of-memory primary exactly, lets an
out-of-memory cleanup failure escape an ordinary primary, and aggregates an
ordinary primary with an ordinary cleanup failure.

Host Start exposes only bounded `authenticated_connection_stale`, with no inner
exception, fingerprint, or dependency payload. No route was selected, so there
is no fail-close side effect: `FailCloseCount == 0` and the host connection is
disposed exactly once. No protocol Prepare, capture, Admission, media route,
attachment, send, render, or input authority opens. The Authorization and
Connection registrations, Permission
observer/reservation, protection owner, capture/input/session owners, media
directories and routes, handlers, channels, connections, participant session,
control owner, and host generation drain across both nodes. The exact source
lease remains current, and the unchanged host Trust grant remains present.

No production change was required for this row.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results through exact commit `077c996`:

- focused H0 authenticated-disconnect row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `40/40` in Debug and Release;
- Desktop: `717/717` in Debug and Release;
- combined solution tests: `2581/2581` in Debug and Release;
- solution builds: zero warnings and zero errors; and
- format verification and `git diff --check`: passed.

## Hosted exact-SHA evidence

[CI `33305848081`](https://github.com/happys2333/flowspan/actions/runs/33305848081)
and [CodeQL `33305848085`](https://github.com/happys2333/flowspan/actions/runs/33305848085)
completed with `success`, run 216 attempt 1, for exact tree
`077c996e82dd4077d24a58957c37b86383479f6e`. All three test artifacts contain
12 TRX files, `2581/2581` total, executed, and passed. Failed, error, timeout,
aborted, inconclusive, passed-but-run-aborted, not-runnable, not-executed,
disconnected, warning, completed, in-progress, and pending counters are all zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99242163880` | `9730471145` | `9af1a6f850caa661c9a3283536ea6ed101653a8ec238932d40d717419f4a820c` |
| macOS | `99242163934` | `9730466266` | `fd2bdaa6169c25000a516d129743559d80312af9ab3830d197598121c0b82001` |
| Windows | `99242163831` | `9730484624` | `588c506fe77f431082c1295a4b591e0428463d55b5e426c970e62731a1958011` |

Secret Scan job `99242163794`, artifact `9730425997`, has outer SHA-256
`8cd99743ff6b515274800b9cb7b12591c75a42e09a2176a4824d7352c77c1fe2`
and payload SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
with Gitleaks 208 rules and 0 results. CodeQL job `99242163803` produced
analysis `1693814728`, SARIF `cbe3f2c4-a45b-11f1-8b50-620b1f97cfde`, payload
SHA-256
`2d0be55e721530eb23b5641a07ac1801bd1637dabd02209ecfdb6ea2717f89c0`,
52 rules and 0 results, empty warning/error fields, and 0 exact-ref open alerts.

All packages report version `0.1.216`, exact SHA, and
`unsigned-test-artifact`; all `5/5` `SHA256SUMS` entries pass per runtime and
the repository `Flowspan.Release verify` command passes each artifact directory:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99242677086` | `9730509246` | `f72a6012ac92ca106d23172d042365a0fd901dca2c0c9e85caee78ca303dab` | `a6282b44d237772227e6d5da9f87be4d4198e73f9be09505efee421ab64c149e` | `143e0115a02adc84258553e23bbcc206992ce79bd6a82ae86c07612a1bb8ead4` |
| `osx-arm64` | `99242677005` | `9730510863` | `5f95552031e811df89a72d7c03b2d6f5f44083e9076eba4e3c2fda6a3567a920` | `e16df762dff2c56dcc4dcd445d6eb220d9c2cdaddb962d5ff4b9efc4710d3e43` | `903c12cbbe9b4b0656c7fa8913d86b34826b807354b027a04ba06ed8df0c50ab` |
| `linux-x64` | `99242676964` | `9730506760` | `3b458a1402242c5dd65740547ba1093986f8bd2020fc7fd652b78564b5907176` | `7b9999f7a5852ccae81000201b826dd64ee3fd2734149174bccbc637d5198db1` | `753c9a53520131f93df929739de406a66a016ec41479923cb0a237f10ef19492` |

These hosted results prove the managed 40-case tree and reproducible unsigned
packages. They do not prove native APIs, physical Devices, signing,
notarization, or release acceptance.

## Explicit limitations

The focused local tracer is same-host managed loopback evidence on macOS. The
hosted matrix is portable managed/contract evidence. Neither form instantiates
native capture/input/protection/permission/Emergency Stop APIs, a physical
Device pair, packaged accessibility, signing, notarization, or release
acceptance.

The scenario covers one H0 authenticated disconnect after a real Authorization
reservation is acquired but before its ownership handoff. It does not complete
the other H0 disconnect phases, H1 Disconnect, disconnect-plus-cleanup faults,
or the aggregate H0/H1 matrix. H0 Disconnect remains Partial; H1 Disconnect
remains Missing; CL Disconnect remains Partial. Tasks 5, 5.5a, and 5.5,
aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/signing/
notarization/release gate, and the long-term Goal remain open.
