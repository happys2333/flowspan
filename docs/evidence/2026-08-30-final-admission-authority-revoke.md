# Final Admission authority-revoke checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`15aba95409c62d858669b740957da54a5bce6b95`

Final hosted evidence tree:
`15aba95409c62d858669b740957da54a5bce6b95`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 34th production-composed managed tracer execution. Its
fault is one real host authority revoke at the exact final-Admission side-effect
window. It therefore advances only AD Revoke from Missing to Partial. The row
also crosses host post-publication revalidation and terminal cleanup, but that
does not constitute an HC-origin revoke injection or complete the CL revoke
matrix. HC Revoke remains Missing, CL Revoke remains Partial, and every other
cell is unchanged.

`AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes` shares the real
authenticated protocol-1.7 loopback, bilateral verified `FSM1`, prepared
renderer, host controller, and participant endpoint used by the existing final-
Admission side-effect-then-throw row. Its observation hook runs only after the
inner Admission publication has completed and the participant's exact
`StateChanged` publication confirms `Applied` or `AlreadyApplied`, but before the
host performs its post-publication fact/protection revalidation and before
`Admission.TryOpen()` can authorize frames.

At that boundary, capture has started exactly once and its initial pre-Admission
frame has been disposed. The hook deliberately emits a second boundary frame;
that owner is also disposed exactly once while media send and participant render
remain zero. Input is still empty and the authenticated generation is current.
The hook calls the real
`hostTrust.UpdateCapabilitiesAsync` with the exact participant fingerprint,
changing the grant from `mirror.view` to empty. The applied mutation
removes the required Mirror authority and drains the matching current inbound
session. When the mutation returns, the old connection is no longer current and
cannot be reacquired; the reduced Trust record has no Capabilities.

The host's post-publication revalidation fails closed with the exact bounded
`authenticated_connection_stale` result. It exposes neither the participant
fingerprint nor dependency text. The final frame gate never opens despite the
participant having committed the precise Admission state. Capture and input
receive local Emergency Stop, media send/render/input stay zero, and the
controller, capture/input/session, renderer, protection, permission observer,
Emergency Stop, media sessions, route, directory, handler, channel, connection,
control, and host-generation owners drain across both managed nodes. The exact
source lease remains current, avoiding an unrelated source mutation claim.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AdFinalAdmission'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AdFinalAdmission'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact implementation tree `15aba95`:

- focused final-Admission pair: `2/2` in Debug and Release;
- ten fresh authority-revoke processes per configuration: `10/10` in Debug and
  `10/10` in Release;
- production-composed managed tracer: `34/34` in Debug and Release;
- Desktop: `711/711` in Debug and Release;
- solution tests: `2575/2575` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- independent strict review: APPROVE with 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

[CI run `33300966551`](https://github.com/happys2333/flowspan/actions/runs/33300966551)
completed with `success`, run number 209 attempt 1, for exact commit
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

All three reproducible packages report version `0.1.209`, the exact evidence
SHA, and `unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes
`5/5`, and the repository `Flowspan.Release verify` command passes each artifact
directory. Downloaded bytes independently reproduce the service outer digests,
inner archive digests, and manifest-bound signed-tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99229439494` | `9728995733` | `9e0be92721daed3e81c0b2320ac58979f2b25ab8d60900b9b363efebca570e0e` | `ce33415e92eef2fc50f0ffcb9524e5e050330ee8c4e9e1241c620f5879cc8711` | `6ea9ab94258a17aebd5512fd79b5a6c14dad3518cd2145bb62498a46fb85ea49` |
| `osx-arm64` | `99229439508` | `9728994068` | `53419c26d084a9848a91a9f13bdf6ef21e912aaef86ba5b473f071641829ecc3` | `63c3bc2b5c59f02f5f724df3b91f75570befb4f5b221d8e8c7ab3705f772c99f` | `613bb36d7baf12cf54fcb6413528225c0d974a8612aca88724120cf5cc077301` |
| `linux-x64` | `99229439719` | `9728990542` | `5f184d3d4794eaf4a9757cebd67296b7dc074ab9b7380de59837e08ca49108fb` | `c4fe599cc674ae51bdc1a8773d4da9a8103ddac97a81c551d83e6a69f6db9a82` | `ea71ea0358e0320265c3f908ed27c66360e1e97885c276db9a6cd50a03d91540` |

These hosted results prove managed build/test, static-analysis, content-lock,
and reproducible unsigned-package properties for exact evidence tree
`15aba95`. They do not prove native APIs, physical two-device operation,
package signing, notarization, or release acceptance.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. Hosted portable execution will remain managed evidence when it
exists; it will not become native or physical proof.

The scenario covers one authority revoke after participant final-Admission
commit but before host frame-gate open. It does not complete revocation at other
Admission buffering/send/commit phases, an AD disconnect, revoke-plus-cleanup
faults, every host post-Ready boundary, or native non-cooperative teardown. AD
Revoke remains Partial; HC Revoke remains Missing; CL Revoke remains Partial.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every
native/physical/signing/notarization/release gate, and the long-term Goal remain
open.
