# Final Admission authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`7be177bb010c55ba44c852a851b60c3ba843d9d7`

Final hosted evidence tree:
`629d1e5b1026367d34815f18f34ab8ff50c3db1c`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 35th production-composed managed tracer execution. Its
fault is independent authenticated transport loss at the exact final-Admission
side-effect window. It therefore advances only AD Disconnect from Missing to
Partial. The row also crosses host post-publication revalidation and terminal
cleanup, but it is not an HC-origin disconnect injection and does not complete
the CL disconnect matrix. HC Disconnect remains Missing, CL Disconnect remains
Partial, and every other cell is unchanged.

`AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes` shares
the real authenticated protocol-1.7 loopback, bilateral verified `FSM1`,
prepared renderer, controller, participant endpoint, and final-Admission hook
with the side-effect-then-throw and authority-revoke rows. The participant has
already committed and published its exact Admission as `Applied` or
`AlreadyApplied`, but the host has not yet performed post-publication fact/
protection revalidation or opened `Admission.TryOpen()`.

At the boundary, capture has started exactly once and its initial pre-Admission
frame has been disposed. The hook deliberately emits a second frame and proves
that owner is also disposed exactly once with zero media send or participant
render; input remains empty. The authenticated generation is current. The hook
then starts the real `participantConnection.DisposeAsync()` to inject transport
loss independently of Trust or Capability mutation.

The hook does not await full connection disposal. Instead, it waits for a
barrier published only after the real host revocation callback has returned.
That proves the invalidation callback has run before assertions without folding
the entire disconnect/session teardown into the hook. At the barrier, the old
host generation is non-current and cannot be reacquired. The host Trust record,
exact peer fingerprint, and sole `mirror.view` grant remain unchanged both at
the boundary and after cleanup.

Host Start fails closed with exact bounded
`authenticated_connection_stale`, no inner exception, and no fingerprint or
dependency payload. Capture and input receive local Emergency Stop; the frame
gate never opens; and media send, render, and input remain zero despite the
participant's exact Admission commit. Outside the hook, the test joins full
participant connection disposal and session completion, then proves the
controller, capture/input/session, renderer, protection, permission observer,
Emergency Stop, media sessions, route, directory, handler, channel, connection,
control, and host-generation owners drain across both managed nodes. The exact
source lease remains current.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AdFinalAdmission'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AdFinalAdmission'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact implementation commit `7be177b`:

- focused final-Admission rows: `3/3` in Debug and Release;
- ten fresh authenticated-disconnect processes per configuration: `10/10` in
  Debug and `10/10` in Release;
- production-composed managed tracer: `35/35` in Debug and Release;
- Desktop: `712/712` in Debug and Release;
- solution tests: `2576/2576` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

[CI run `33302056214`](https://github.com/happys2333/flowspan/actions/runs/33302056214)
completed with `success`, run number 211 attempt 1, for exact evidence tree
`629d1e5b1026367d34815f18f34ab8ff50c3db1c`. Each downloaded test artifact
contains exactly 12 TRX files. Structured aggregation reports `2576/2576`
total, executed, and passed on Linux, macOS, and Windows; every failed, error,
timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable, not-
executed, disconnected, warning, completed, in-progress, and pending counter is
zero. Downloaded bytes independently reproduce each service outer SHA-256:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99231928250` | `9729298466` | `25473684fbdb61cc045760f990f36f95ba1375594c8d4f2c8dab9b67f227fbd6` |
| macOS | `99231928457` | `9729303443` | `75bf4bd516d578e542be0e7882c378c5a9a5960bd526e772b82e618ee7f1bd2a` |
| Windows | `99231928274` | `9729314043` | `318bc177528e952267f41d1811db5eda41597810ee222cfe060db1273e851d67` |

Secret Scan job `99231928182` completed successfully. Artifact `9729255724`
has independently reproduced outer SHA-256
`5ac107d008fd9b28f4491d3eeda403661d724ddc8b02272d96c58f2a2a4329a6`.
Its 45,825-byte `results.sarif` payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
records SARIF 2.1.0 and Gitleaks semantic version v8.0.0, and contains 208
rules with 0 results.

[CodeQL run `33302056182`](https://github.com/happys2333/flowspan/actions/runs/33302056182),
run number 211 attempt 1, completed with `success`. Job `99231928086` produced
analysis ID `1693660011` and SARIF ID
`c5a33d32-a44e-11f1-94a0-457e82414681`; service warning/error text is empty
and the exact branch ref has 0 open alerts. The 230,952-byte SARIF payload has
SHA-256 `038e9829decfa29f034ad3934bd8f03da2b5490d5c94c8974fab2882d075e07a`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results.

All three reproducible packages report version `0.1.211`, exact evidence SHA
`629d1e5b1026367d34815f18f34ab8ff50c3db1c`, and
`unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes `5/5`, and
the repository `Flowspan.Release verify` command passes each artifact directory.
Downloaded bytes independently reproduce the service outer digests, inner
archive digests, and manifest-bound signed-tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99232469509` | `9729342404` | `96461a76d5e419c99c18b8972432b68e24e7fa435ba72970c48605c694c817af` | `7d7c7908b08bd7b30e432a257e5efa7c41a355bd812cac7cf0d75b33b9b49b47` | `b9ee2791c0a5fb92df640db1cc6e7e4f67076b8532c65a1e7cf990f25b5524b4` |
| `osx-arm64` | `99232469572` | `9729332616` | `56ded129481cf547a68703a799ccdb5e8aedcdb38d9b9d3edc31d0c25cf39df7` | `f74ae8b5932ff30c4b7de8ecc2547a732dee9479c64006fa49f9455079e28404` | `5d24eb30d6638138f85f4b6047eed83c2b1e0f39b560146923cc3b2d73615a6f` |
| `linux-x64` | `99232469518` | `9729333826` | `9c0af817eeb37ce57a8fb628e87db56f030468370785dbe742bfef7717156fee` | `27985ad1953dc96e6e562411d0c667186a04ccf73fae5799bb3b37ea418180c4` | `747300eba849eb4fcefb43729489d7df88cd6fe982d2ef38f75d801ec8fffffb` |

These hosted results prove managed build/test, static-analysis, content-lock,
and reproducible unsigned-package properties for exact evidence tree
`629d1e5`. They do not prove native APIs, physical two-device operation,
package signing, notarization, or release acceptance. Earlier CI `33301715578`
and CodeQL `33301715584` target `c13acc5`; they remain irrelevant to the exact
`7be177b` implementation checkpoint except as documentation-only history.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one authenticated disconnect after participant final-
Admission commit but before host frame-gate open. It does not complete other
Admission buffering/send/commit disconnect phases, disconnect-plus-cleanup
faults, every host post-Ready boundary, or native non-cooperative teardown. AD
Disconnect remains Partial; HC Disconnect remains Missing; CL Disconnect remains
Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
