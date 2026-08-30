# Participant current-lease acquisition ownership checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Ownership implementation commit:
`681c0f72b4f584aba8fa6bf7e915a27317636ff9`

First hosted candidate tree:
`213327c4373379f5f92457ab651741dd5bdd85c4`

Final hosted evidence tree:
`15aba95409c62d858669b740957da54a5bce6b95`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint closes one participant P0 ownership gap without changing a
matrix status. `DesktopRemoteWindowPreparationPeer` asks the authenticated
connection collaborator for a current, generation-bound lease before media
attachment. A collaborator can legitimately assign that real `out` lease and
then throw. Before `681c0f7`, that throw could leave the lease outside the
participant generation's owner graph.

The peer now initializes the lease locally and attaches every non-null lease to
the generation in a `finally` around the collaborator call. Ownership therefore
transfers before either a normal return or exception propagation. Later outcome
classification and terminal cleanup operate on the same owned lease instead of
depending on the collaborator to undo a completed side effect.

Two connected production-peer tests freeze the contract:

- `ConnectionAcquirerSideEffectThenThrowReleasesLease` assigns a real current
  lease and throws `IOException`. The peer returns the bounded
  `Rejected/media_unavailable` result, never invokes renderer Preparation,
  releases the side-effect lease, and leaves the host lease current. A
  replacement acquisition succeeds, and late/idempotent disposal of the old
  lease cannot remove that current replacement.
- `FatalConnectionAcquirerSideEffectRetainsLeaseForTerminalCleanup` assigns the
  same kind of lease and throws one exact `OutOfMemoryException`. That fatal
  instance escapes unchanged. The lease remains owned and current until peer
  terminal cleanup, then is released.

This is direct P0 Throw and owner-cleanup evidence. P0 Throw was already Partial
and remains Partial because other participant phases and fault intersections
remain open. The tests are connected-peer cases, not additions to
`DesktopRemoteWindowManagedTwoNodeTracerTests`; the production-composed managed
tracer therefore remains exactly `32/32`.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~ConnectionAcquirerSideEffect'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~ConnectionAcquirerSideEffect'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~ConnectionAcquirerSideEffect' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~ConnectionAcquirerSideEffect' || exit 1; done
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

Results at final local tree `213327c`:

- focused ownership rows: `2/2` in Debug and Release;
- twenty fresh focused processes per configuration: `20/20` process runs and
  `40/40` test invocations in each configuration, `80/80` test invocations
  total;
- Desktop: `709/709` in Debug and Release;
- production-composed managed tracer: unchanged at `32/32` in Debug and
  Release;
- solution tests: `2573/2573` in Debug and Release;
- solution builds: zero warnings and zero errors; and
- format verification and `git diff --check`: passed.

## Hosted exact-SHA evidence

[CodeQL run `33299464246`](https://github.com/happys2333/flowspan/actions/runs/33299464246),
run number 208 attempt 1, completed with `success` for exact candidate tree
`213327c4373379f5f92457ab651741dd5bdd85c4`. Job `99224751599` produced
analysis ID `1693554724` and SARIF ID
`c0bac7da-a445-11f1-8d19-6686da05fe5e`; service warning/error text is empty
and the exact branch ref has 0 open alerts. The 230,952-byte SARIF payload has
SHA-256 `e0a3d4654f2d81020bbd4584545fec7918ca5214fb90c8b904620f2834481b5e`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results.

[CI run `33299464241`](https://github.com/happys2333/flowspan/actions/runs/33299464241)
for that same exact tree is **failure evidence, not successful hosted evidence**.
The Secret Scan, macOS, and Ubuntu jobs completed successfully, but Windows job
`99224733077` reached the three-minute inactivity watchdog after 343 passing
Desktop tests while
`DesktopRemoteWindowHostCoordinatorTests.StaleCapturedProtectionNotifyContextMustJoinCurrentDrainer`
was active. That fixture scheduled both deliberately blocked notification calls
through `Task.Run`; under Windows full-suite thread-pool starvation, neither the
released worker nor its timeout continuation could resume. Both new participant
lease tests had already completed successfully, but the test host was aborted,
so package jobs did not run. A subsequent exact-tree CI and CodeQL pair must both
succeed before this checkpoint can claim hosted matrix or package completion.

Test-only commit `d89758b257c74240fc9d59a2eb023f94f4278f07`
removes that fixture's pool dependency by using two dedicated `LongRunning` and
`DenyChildAttach` blocking collaborators plus a fail-safe release/join. Its four
related rows pass `4/4` in Debug and Release, and the previously starved fixture
passes twenty fresh processes per configuration. Subsequent tracer commit
`8413d06` and final-Admission commit `15aba95` preserve the ownership repair and
extend the tracer without changing its contract.

[Final exact-SHA CI run `33300966551`](https://github.com/happys2333/flowspan/actions/runs/33300966551)
completed with `success`, run number 209 attempt 1, for
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

[Final exact-SHA CodeQL run `33300966509`](https://github.com/happys2333/flowspan/actions/runs/33300966509),
run number 209 attempt 1, completed with `success`. Job `99228921811` produced
analysis ID `1693615018` and SARIF ID
`f304d2e4-a44a-11f1-8405-2648af22fa88`; service warning/error text is empty
and the exact branch ref has 0 open alerts. The 230,952-byte SARIF payload has
SHA-256 `214643932548a2bc13853c64062bb89f2a5438023f3006321e059586e0d5e2ee`,
records CodeQL 2.26.4 and `codeql/csharp-queries` 1.9.2, and contains 52 rules
with 0 results.

All three reproducible packages report version `0.1.209`, exact final evidence
SHA, and `unsigned-test-artifact`. Every downloaded `SHA256SUMS` entry passes
`5/5`, and the repository `Flowspan.Release verify` command passes each artifact
directory. Downloaded bytes independently reproduce the service outer digests,
inner archive digests, and manifest-bound signed-tree digests:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Inner archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99229439494` | `9728995733` | `9e0be92721daed3e81c0b2320ac58979f2b25ab8d60900b9b363efebca570e0e` | `ce33415e92eef2fc50f0ffcb9524e5e050330ee8c4e9e1241c620f5879cc8711` | `6ea9ab94258a17aebd5512fd79b5a6c14dad3518cd2145bb62498a46fb85ea49` |
| `osx-arm64` | `99229439508` | `9728994068` | `53419c26d084a9848a91a9f13bdf6ef21e912aaef86ba5b473f071641829ecc3` | `63c3bc2b5c59f02f5f724df3b91f75570befb4f5b221d8e8c7ab3705f772c99f` | `613bb36d7baf12cf54fcb6413528225c0d974a8612aca88724120cf5cc077301` |
| `linux-x64` | `99229439719` | `9728990542` | `5f184d3d4794eaf4a9757cebd67296b7dc074ab9b7380de59837e08ca49108fb` | `c4fe599cc674ae51bdc1a8773d4da9a8103ddac97a81c551d83e6a69f6db9a82` | `ea71ea0358e0320265c3f908ed27c66360e1e97885c276db9a6cd50a03d91540` |

These final hosted results supersede candidate-tree failure `33299464241` as
the successful checkpoint. They prove managed build/test, static-analysis,
content-lock, and reproducible unsigned-package properties for exact final tree
`15aba95`; they do not prove native, physical, signing, notarization, or release
behavior.

## Explicit limitations

This is local same-host macOS connected-peer evidence plus portable managed CI
matrix evidence. It does not instantiate a native renderer, capture, input,
permission, protection, or Emergency Stop API; a physical Device pair; signed
packages; macOS notarization; packaged accessibility; or release acceptance.

The checkpoint covers one current-lease acquisition side-effect followed by a
non-fatal or fatal throw. It does not complete current Trust/lease revocation,
the other authenticated-disconnect phases, every participant cleanup-fault
combination, or the complete reject/throw/cancel/timeout/revoke/disconnect/
cleanup-fault matrix. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
