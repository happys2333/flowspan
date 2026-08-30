# Pending renderer authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Exact implementation/test commit:
`8d0831d0716bc68bc1d5dc0ff18c4efc033624b7`

Committed documentation baseline:
`4c13202a231ed4edf2780e941ede4f34c1c62bb4`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds one production-composed managed tracer execution spanning
TX, P0, P2, and CL. It advances only the P0 Disconnect matrix cell from Missing
to Partial. TX Disconnect, P2 Disconnect, and CL Disconnect remain Partial; no
other matrix status changes.

`TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains`
uses real loopback TCP, authenticated protocol 1.7, the production participant
Preparation peer, and bilateral verified `FSM1` attachment. The host has
admitted the exact Prepare send, both media sessions are attached, and the
participant renderer factory has entered a deliberately non-cooperative
Preparation call. No Ready outcome has yet been produced.

Disposing the participant's authenticated control connection enters owned peer-
disconnect cleanup and cancels the renderer Preparation lifetime. The renderer
observes cancellation but deliberately does not return. Before its release, the
participant disconnect, Preparation worker, and renderer factory all remain
incomplete; the renderer is not disposed, while the host Preparation is already
terminal with fact Connection, reason `authenticated_connection_stale`, and
`ConsumeConnection` cleanup scope. This proves cleanup admission and
cancellation without pretending that a non-cooperative renderer can be forcibly
completed.

After the test releases the renderer factory, the participant produces its one
local terminal `Rejected` result with the bounded reason
`preparation_cancelled`. The late renderer returned by the non-cooperative
factory is disposed. The disconnected generation never acknowledges Ready to
the host and opens no final Admission, capture, media send, render, or input
authority. Host fail-close and connection disposal each execute once, and both
nodes' controller, capture/input/session, protection, permission-observer,
Emergency Stop, renderer, media-session, route, directory, handler, channel,
connection, and control owners drain.

## TDD evidence

Before implementing the scenario, a deliberate RED sentinel in the focused row
failed `0/1`. The completed focused row then passed `1/1` in both Debug and
Release. Twenty fresh processes per configuration passed `20/20` in Debug and
`20/20` in Release. The sentinel is test-development evidence only; it is not
counted as a passing gate.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains' || exit 1; done
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

Results at exact commit `8d0831d`:

- deliberate focused RED sentinel: `0/1`;
- final focused Debug and Release: `1/1` each;
- fresh-process focused Debug and Release: `20/20` each;
- managed tracer class Debug and Release: `31/31` each;
- Desktop Debug and Release: `701/701` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2565/2565` each; and
- format verification and `git diff --check`: passed.

## Hosted exact-SHA evidence

[CI run `33295825931`](https://github.com/happys2333/flowspan/actions/runs/33295825931)
completed with `success`, run number 202 attempt 1, for exact commit
`8d0831d0716bc68bc1d5dc0ff18c4efc033624b7`. Each downloaded test artifact
contains exactly 12 TRX files. Structured aggregation reports `2565/2565`
total, executed, and passed on Linux, macOS, and Windows; every failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable, not-
executed, disconnected, warning, completed, in-progress, and pending counter is
zero. Downloaded bytes independently reproduce each service outer SHA-256:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99215187993` | `9727405012` | `61aea7ebef437f24f4f5e95a1579d692a2e6e9b20ef61d620a23334c4b92a3ad` |
| macOS | `99215188014` | `9727412330` | `2c5c0219ffcdc519cd5defb177319edaa5367d6f3f3f5bbdc65c074017bcc75c` |
| Windows | `99215188024` | `9727413993` | `25ce3be4f1122e9825d58343b0766a2c42aeb5495734dff23d7b080d99632995` |

Secret Scan job `99215187945` completed successfully. Artifact `9727370325`
has independently reproduced outer SHA-256
`2844c86011f1db12b0efcc578564dd16de67c4dd4accbf9daa9e136e75d48d21`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
records SARIF 2.1.0 and Gitleaks semantic version `v8.0.0`, and contains 208 rules
with 0 results.

[CodeQL run `33295825897`](https://github.com/happys2333/flowspan/actions/runs/33295825897),
run number 202 attempt 1, completed with `success` for the same exact commit.
Job `99215187802` produced analysis ID `1693397325` and SARIF ID
`7e6cb9ae-a438-11f1-8f4b-0fbb8d1bd0fa`; the exact branch ref has 0 open
alerts. The 230,952-byte SARIF payload has SHA-256
`bb03ef478f2f4fd1ecb598b5595d3eeafb646c5fea1bc2ed1f2e904f3256f636`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results. Hosted warning and error fields are both empty.

All three reproducible packages report version `0.1.202`, exact commit
`8d0831d0716bc68bc1d5dc0ff18c4efc033624b7`, and
`unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes `5/5`, and
the repository `Flowspan.Release verify` command passes each artifact directory.
Downloaded bytes independently reproduce the service outer digests and the
recorded inner archive and canonical tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99215628828` | `9727437036` | `fd555df4d590bacede321c79718682079476a07cdcd816e4aad96e2595f9624a` | `935026161db871b2f3d55df9436a94a8cd49cc46ace794e736561924ca5956e9` | `a43da38ffbbc0ee176d0990d1c5c90f8261ffa668ed3fe4dd6c53cee1c027c01` |
| `linux-x64` | `99215628844` | `9727432692` | `99df32e15e06ff876425d1171f47cb1ce395ea924f624c3df42e52d1806e274a` | `2170165f849790c26bc1b2b233631894b5038b010b292427b836d3ec65677ad5` | `fc2a4f2a778f638b487e070b2eb3aa494859010932e83c473489c9d8510c34b0` |
| `osx-arm64` | `99215628854` | `9727430501` | `2aa38ce7a172f39f137b32b330a8ecf7e92010dd11ce42590684c43f388db55c` | `274b35a03b1f54aa2168d1065cf94c02f68e9f45ef1fb795b3ba0a66ca2f222d` | `e90f4b93d52fe440dc3d1631e0a7b91e9dcb8aaa607097291388736eb324a96a` |

These hosted results prove managed build/test, static-analysis, content-lock,
and reproducible unsigned-package properties for the exact commit. They do not
prove native APIs, physical two-device operation, package signing,
notarization, or release acceptance.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
exercise a native renderer, native capture/input/protection, two physical
devices, Windows or Linux runtime behavior, signing, notarization, or release
acceptance. It covers one authenticated disconnect after Prepare send admission
and bilateral attachment while renderer Preparation is pending; it does not
cover every TX phase, participant Trust/lease revoke, renderer timeout, combined
cleanup failure, or non-cooperative native teardown.

TX, P0, P2, and CL remain Partial overall. Tasks 5, 5.5a, and 5.5, aggregate
H0/H1 acceptance, every native/physical/signing/notarization/release gate,
`CreateProduction()`, and the long-term Goal remain open.
