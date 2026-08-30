# Pending renderer participant-Trust-revoke checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`8413d065ba7f9d2d2b05e8b52d9c97eace768cf9`

Immediate test-fixture dependency:
`d89758b257c74240fc9d59a2eb023f94f4278f07`

Final hosted evidence tree:
`15aba95409c62d858669b740957da54a5bce6b95`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 33rd production-composed managed tracer execution. It
crosses TX, P0, P2, and CL, but the injected fault is the participant's
authoritative Trust revocation while its current generation-bound lease and
renderer Preparation are live. It therefore advances P0 Revoke and P2 Revoke
from Missing to Partial. TX Revoke and CL Revoke already remain Partial; their
owner paths are strengthened but not promoted. Every other matrix cell is
unchanged.

`P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains`
uses a real loopback listener, authenticated protocol 1.7, the signed discovery
candidate verified for the exact host identity, the production
`AuthenticatedTcpPeerSessionAttempt`, and `SystemAuthenticatedTcpConnector`.
The participant session is therefore owned by the real reconnect/Trust path,
not by a test-only direct connection shortcut.

The scenario waits for exact Prepare send admission, bilateral verified `FSM1`
attachment, a current participant connection lease, and entry into a
deliberately non-cooperative renderer Preparation call. No Ready result or host
Ready authority exists. It then calls the real
`participantTrust.RevokePeerAsync` for the host Device.

Before the renderer is released:

- Trust is absent, the acquired participant lease is no longer current, and a
  replacement lease cannot be acquired;
- authenticated peer-disconnect cleanup has entered and renderer cancellation
  is observed;
- the host reservation is terminal as Connection /
  `authenticated_connection_stale` with connection-consuming cleanup;
- Trust revocation, the authenticated session attempt, peer-disconnect cleanup,
  and participant Preparation all remain incomplete while the renderer still
  owns its call; and
- there is no Ready acknowledgement, final Admission, capture, media send,
  render, or input authority.

This is intentional ownership, not a fabricated cleanup completion: production
cannot forcibly complete arbitrary non-cooperative renderer code. After the
test releases that code, the participant emits exactly one bounded
`Rejected/preparation_cancelled`, disposes the late renderer, and finishes
disconnect cleanup. Trust revocation returns `true`; the production session
attempt completes as `PermanentRejection/PeerNotTrusted`; and the host failure
remains bounded to `authenticated_connection_stale`. The controller,
capture/input/session, renderer, protection, permission observer, Emergency
Stop, media sessions, route, directory, handler, channel, connection, control,
and host-generation owners drain across both managed nodes.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains' || exit 1; done
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

Final local results at exact implementation tree `8413d06`:

- focused Trust-revoke row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `33/33` in Debug and Release;
- Desktop: `710/710` in Debug and Release;
- solution tests: `2574/2574` in Debug and Release;
- solution builds: zero warnings and zero errors; and
- format verification and `git diff --check`: passed.

No production source change was required for this row. Its immediate parent
`d89758b` hardens only the Windows protection-notification test fixture with
dedicated blocking collaborators and fail-safe release; the related four rows
pass `4/4` in Debug and Release, and the previously starved fixture passes twenty
fresh processes in each configuration.

## Hosted exact-SHA evidence

[CI run `33300966551`](https://github.com/happys2333/flowspan/actions/runs/33300966551)
completed with `success`, run number 209 attempt 1, for final evidence tree
`15aba95409c62d858669b740957da54a5bce6b95`. Each downloaded test artifact
contains exactly 12 TRX files. Structured aggregation reports `2575/2575`
total, executed, and passed on Linux, macOS, and Windows; every failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable, not-
executed, disconnected, warning, completed, in-progress, and pending counter is
zero. Downloaded bytes independently reproduce each service outer SHA-256:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99228921969` | `9728955433` | `d1e05d797b0a7894ebc420bdbed732192f5c7800fcbcd3bd1f7aa39c247a51bb` |
| macOS | `99228921910` | `9728958360` | `356854ac8ba25eb7fe589311e4754938e0c4f8043f088b9dae265e4aec4fce0e` |
| Windows | `99228921953` | `9728969785` | `1cf2f39731fdf5fb9bc41e8a7049c554625dace583c506fc9b1f2f097c23018a` |

Secret Scan job `99228921844` completed successfully. Artifact `9728916252`
has independently reproduced outer SHA-256
`d11b4d6156506ee0fc03701b7063f43c89faee92100b43ef29862d39e6ff4882`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
records SARIF 2.1.0 and Gitleaks semantic version v8.0.0, and contains 208
rules with 0 results.

[CodeQL run `33300966509`](https://github.com/happys2333/flowspan/actions/runs/33300966509),
run number 209 attempt 1, completed with `success`. Job `99228921811` produced
analysis ID `1693615018` and SARIF ID
`f304d2e4-a44a-11f1-8405-2648af22fa88`; service warning/error text is empty
and the exact branch ref has 0 open alerts. The 230,952-byte SARIF payload has
SHA-256 `214643932548a2bc13853c64062bb89f2a5438023f3006321e059586e0d5e2ee`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results.

All three reproducible packages report version `0.1.209`, exact evidence SHA
`15aba95409c62d858669b740957da54a5bce6b95`, and
`unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes `5/5`, and
the repository `Flowspan.Release verify` command passes each artifact directory.
Downloaded bytes independently reproduce the service outer digests, inner
archive digests, and manifest-bound signed-tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99229439494` | `9728995733` | `9e0be92721daed3e81c0b2320ac58979f2b25ab8d60900b9b363efebca570e0e` | `ce33415e92eef2fc50f0ffcb9524e5e050330ee8c4e9e1241c620f5879cc8711` | `6ea9ab94258a17aebd5512fd79b5a6c14dad3518cd2145bb62498a46fb85ea49` |
| `osx-arm64` | `99229439508` | `9728994068` | `53419c26d084a9848a91a9f13bdf6ef21e912aaef86ba5b473f071641829ecc3` | `63c3bc2b5c59f02f5f724df3b91f75570befb4f5b221d8e8c7ab3705f772c99f` | `613bb36d7baf12cf54fcb6413528225c0d974a8612aca88724120cf5cc077301` |
| `linux-x64` | `99229439719` | `9728990542` | `5f184d3d4794eaf4a9757cebd67296b7dc074ab9b7380de59837e08ca49108fb` | `c4fe599cc674ae51bdc1a8773d4da9a8103ddac97a81c551d83e6a69f6db9a82` | `ea71ea0358e0320265c3f908ed27c66360e1e97885c276db9a6cd50a03d91540` |

These hosted results prove managed build/test, static-analysis, content-lock,
and reproducible unsigned-package properties for final evidence tree
`15aba95`. They do not prove native APIs, physical two-device operation,
package signing, notarization, or release acceptance.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate a native renderer, capture/input/protection/permission API, physical
Devices, packaged accessibility, signing, notarization, or release acceptance.
Hosted portable execution will remain managed evidence when it exists; it will
not convert this row into native or physical proof.

The scenario covers one participant Trust revoke after Prepare send admission
and bilateral attachment while renderer Preparation is pending. It does not
complete every P0 or P2 revoke phase, a direct generation-only revoke, revoke
plus cleanup fault, the remaining disconnect phases, or non-cooperative native
teardown. P0 Revoke and P2 Revoke remain Partial, while TX and CL remain Partial
overall. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
