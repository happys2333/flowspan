# Pending renderer exact-deadline checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Timeout implementation commit:
`40d4f78f32bb9958c1e7fbc075b6743620d1f0de`

Final CI-stabilized evidence tree:
`de4009aae9b7e5822983e13e70909b7deb8c2b64`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 32nd production-composed managed tracer execution. It
crosses TX, P0, P2, and CL, but the injected fault originates at the P2 renderer
worker's exact participant deadline. It therefore advances only P2 Timeout from
Missing to Partial. CL Timeout remains Missing because cleanup itself neither
times out nor injects a cleanup-timeout policy. Every other matrix cell is
unchanged.

`TxP0P2ExactDeadlineWhileRendererPreparationIsBlockedFailsClosedAndDrains`
uses real authenticated protocol-1.7 loopback, exact Prepare send admission,
and bilateral verified `FSM1` attachment. The host and participant use separate
manual clocks. Only the participant clock advances to exact request-deadline
equality while its renderer factory is deliberately non-cooperatively blocked.
The host clock remains strictly before the deadline, and authenticated peer
disconnect has not entered when the renderer lifetime token is cancelled.

Before renderer release, no participant Ready outcome exists, the host
Preparation remains in `PrepareSending`, and the host has no Ready authority.
After release, the participant produces its one terminal `Rejected` response
with bounded reason `preparation_expired`; authenticated disconnect follows.
The host accepts only the two bounded causally related terminal tuples permitted
by that disconnect race:

- Connection / `authenticated_connection_stale` with the corresponding bounded
  host start failure; or
- disposed host Preparation / `host_preparation_disposed` with bounded
  `remote_window_prepare_not_acknowledged`.

Both tuples consume the connection and grant no Ready authority, Admission,
capture, media send, render, or input. The late renderer is disposed, and the
controller, capture/input/session, protection, permission observer, Emergency
Stop, renderer, media session, route, directory, handler, channel, connection,
control, and host generation owners drain on both managed nodes.

## TDD and stabilization evidence

The deadline tracer was first implemented at `40d4f78`. Its split clocks prove
participant deadline equality directly rather than inferring timeout from a
generic cancellation or advancing host time. The authenticated-disconnect
companion row remains separate and still produces `preparation_cancelled`.

An earlier documentation tree, `c761acf`, received CI run `33296383742`. That
run is **not successful evidence**: Windows job `99216650548` failed because the
AD shutdown observer did not recognize the exact legitimate stale-generation
aggregate produced by its fail-close race and because `Changed` publication
depended on a starved `Task.Run` worker. CodeQL run `33296383740` succeeded, but
static analysis does not convert
the failed CI matrix into a successful checkpoint.

Final evidence tree `de4009a` fixes the exact retired-generation shutdown
classifier and replaces the local-pairing publication `Task.Run` dependency with
one bounded `LongRunning | DenyChildAttach` worker started under suppressed
`ExecutionContext`. Its tests cover single-consumer coalescing, external drain,
callback self-dispose, fatal worker failure, stable failure replay, primary-
before-cleanup aggregation, reference deduplication, and complete cleanup.
Three strict review rounds report zero P0, P1, or P2 findings after those
repairs.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~TxP0P2ExactDeadlineWhileRendererPreparationIsBlockedFailsClosedAndDrains|FullyQualifiedName~TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TxP0P2ExactDeadlineWhileRendererPreparationIsBlockedFailsClosedAndDrains|FullyQualifiedName~TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains'
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

Final local results at `de4009a`:

- focused deadline plus authenticated-disconnect rows: `2/2` in Debug and
  Release;
- fresh-process deadline row: `10/10` in Debug and `10/10` in Release;
- managed tracer class: `32/32` in Debug and Release;
- Desktop: `707/707` in Debug and Release;
- solution tests: `2571/2571` in Debug and Release;
- solution builds: zero warnings, zero errors; and
- format verification and `git diff --check`: passed.

## Hosted run history

[CI run `33297152942`](https://github.com/happys2333/flowspan/actions/runs/33297152942)
and [CodeQL run `33297152906`](https://github.com/happys2333/flowspan/actions/runs/33297152906)
completed with `success` for timeout implementation commit `40d4f78`. These
hosted managed results do not supersede the final local stabilization and do not
prove native or physical behavior.

[Final exact-SHA CI run `33298564630`](https://github.com/happys2333/flowspan/actions/runs/33298564630)
completed with `success`, run number 205 attempt 1, for evidence tree
`de4009aae9b7e5822983e13e70909b7deb8c2b64`. Each downloaded test
artifact contains exactly 12 TRX files. Structured aggregation reports
`2571/2571` total, executed, and passed on Linux, macOS, and Windows; every
failed, error, timeout, aborted, inconclusive, passed-but-run-aborted, not-
runnable, not-executed, disconnected, warning, completed, in-progress, and
pending counter is zero. Downloaded bytes independently reproduce each service
outer SHA-256:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99222349067` | `9728212727` | `ed7d45cf9bdbcb6d5efc434f56345277243fb8a1a1cb90c26b11b9ce2bc83c8c` |
| macOS | `99222349073` | `9728207051` | `2bb431a2aaceff30d4d2a742768f55f6412df22e2221b29547ccd1c3d7c1995e` |
| Windows | `99222349139` | `9728227540` | `6d8dbc085cddb2f91c521cb8f130b12d494d35265600d58d1e790075bbf21de9` |

Secret Scan job `99222349012` completed successfully. Artifact `9728178902`
has independently reproduced outer SHA-256
`e0cab50f452d61744efc2f0e27d148de0a1ba46aa895588d942b7f408eac4bf6`.
Its 45,825-byte SARIF payload has SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
records SARIF 2.1.0 and Gitleaks semantic version v8.0.0, and contains 208
rules with 0 results.

[Final exact-SHA CodeQL run `33298564676`](https://github.com/happys2333/flowspan/actions/runs/33298564676),
run number 205 attempt 1, completed with `success`. Job `99222349047` produced
analysis ID `1693513451` and SARIF ID
`5c7674de-a442-11f1-9aba-9e5f5432a4a2`; processing completed, the service
error field is null, warning/error text is empty, and the exact branch ref has 0
open alerts. The 230,952-byte SARIF payload has SHA-256
`09cc1a7bcb3aae24ae9209076f5b363cfa0b38211472b5fcfe140ab2c52329c4`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results.

All three reproducible packages report version `0.1.205`, exact evidence SHA
`de4009aae9b7e5822983e13e70909b7deb8c2b64`, and
`unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes `5/5`, and
the repository `Flowspan.Release verify` command passes each artifact directory.
Downloaded bytes independently reproduce the service outer digests and the
recorded inner archive and canonical tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99222814770` | `9728253597` | `3c827346355836931e8929e2b1c943761cf7a5a29b652b8ff85d17fad3fcd685` | `0e13caa93e23de814f756e49436f61007ce26e38b392ed7d68336c27b4a3d4fd` | `8e592427ae79386857b4b508bdc217acc6182c538818d198c9e474a3b344e40e` |
| `linux-x64` | `99222814766` | `9728246716` | `a716ac3ce1309d952e5f7f70a9b9876f7b4a50abf2a01d7337b90ad3a289a26a` | `15dcd6e2b0b3f79f95ed675dd1fb89248e2c2d04d29dacac11d4e64ad0ad96fe` | `2fe3105e7929121d9ede6bbfbd64808cdb658ed5846a4c33ea279b17b73641b1` |
| `osx-arm64` | `99222814765` | `9728245802` | `60a4a33ea214be2067a643c4a4a0190557dd6dfc16c615ef2bd27e67ac67007a` | `12cc9ba3fa23b311f280aee76fe9435dbee74db24455283713f85528850910cf` | `07c0b3ef06b5bd5017c06406fffdecd5b040f61266426ce618fab8b4f595433c` |

These final hosted results prove managed build/test, static-analysis, content-
lock, and reproducible unsigned-package properties for exact evidence tree
`de4009a`. They do not prove native APIs, physical two-device operation, package
signing, notarization, or release acceptance.

## Explicit limitations

This is same-host managed loopback and contract evidence. It does not instantiate
a native renderer, capture/input/protection API, two physical devices, native
Windows/macOS/Linux runtime behavior, signing, notarization, or release
acceptance. It covers one exact participant deadline while renderer Preparation
is pending; it does not cover every P2 timeout phase, timeout plus cleanup fault,
non-cooperative native teardown, or define a CL cleanup-timeout contract.

P2 Timeout remains Partial and CL Timeout remains Missing. Tasks 5, 5.5a, and
5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
signing/notarization/release gate, and the long-term Goal remain open.
