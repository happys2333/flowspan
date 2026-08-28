# Native Remote Window Portable Contracts Evidence - 2026-08-20

## Evidence status and boundary

Classification: **local portable contract**, **hosted portable contract**,
**headless Desktop**, and **unsigned package**.

Branch: `codex/v1-foundation`

Feature implementation commit:
`ff8b7e45bbd708e936960fdbaf7db3fa2c64111b`, based on
`4fb5d61b6af9b8b1f33271cacb0ff13bc2d10f37`. Final verified commit:
`5cf76ff6d3621cd61f2f248ecc498e8941c73a74`.

The final commit for this evidence record changes only seven deliberately
blocking Desktop test paths. It moves their workers from the shared thread pool
to the repository's existing dedicated `LongRunning` test helper pattern. It
does not change production code or relax the two-second lifecycle assertions.
A later production scheduling correction is recorded below.

This record is scoped to task 2 in
`specs/v1/native-remote-window/tasks.md`. It proves portable contracts,
generation-safe registry and lease behavior, bounded frame ownership, controller
source composition, and deterministic disposal behavior on the named hosts. It
does not prove that Flowspan captured a native window, injected native input,
detected a protected surface or secure input, displayed an operating-system
permission prompt, executed a physical Emergency Stop, or connected two
physical Devices.

## Implemented contract

- `Flowspan.Platform` now defines bounded permission, source catalog, source
  lease, geometry, frame, protection-observation, input, and local Emergency
  Stop registration contracts. Native handles and platform callback objects do
  not cross the public boundary.
- A `RemoteWindowSourceReference` binds the Activity ID, optional semantic kind,
  display label, host Device, and exact source generation. The controller uses
  that reference directly. A compatibility factory can adapt an active semantic
  Activity, but there is no fabricated Activity Descriptor kind for a generic
  native source.
- The in-memory source registry admits at most 128 combined visible and retained
  invalidating generations. Catalog snapshots, lease acquisition, source use,
  invalidation, replacement, and disposal all re-check exact generation. A late
  callback cannot gain authority over a replacement source.
- Native frame payloads are bounded to 64 MiB and 16,384 pixels per dimension.
  The frame sink admits one pending frame, preserves exact session/source/geometry
  ownership, and releases reservations on success, rejection, cancellation,
  destination failure, close, and disposal.
- Protection observations are revision ordered and bounded to eight pending
  notifications. Overflow coalesces to `Unknown`; it cannot let a later safe
  observation erase an undelivered unsafe state.
- Local Emergency Stop registrations are one-shot and generation bound.
  Registration loss is fail closed, callbacks cannot be replaced while running,
  and trigger, loss, unregister, and disposal clear authority.
- Controller operations, source uses, invalidation callbacks, frame deliveries,
  protection callbacks, and Emergency Stop callbacks share process-wide drain
  ancestry. An active `(owner, token)` can defer a cyclic inner join, while a
  stale copied `ExecutionContext` loses that exemption after its original
  invocation exits. Top-level external disposal still joins complete cleanup.
- Protocol 1.5 fixtures remain unchanged. The existing Activity ID field is the
  wire binding; this slice adds no new protocol kind or native media transport.

The detailed requirements, architecture, ownership order, limits, and test
traceability are in `specs/v1/native-remote-window/`; the existing control-plane
decision remains ADR 0021.

## Local candidate gates

Environment:

```text
Host: macOS 26.6.1 (build 25G76), Apple Silicon, Asia/Hong_Kong
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Verification date: 2026-08-20
Feature implementation: ff8b7e45bbd708e936960fdbaf7db3fa2c64111b
Final verified commit: 5cf76ff6d3621cd61f2f248ecc498e8941c73a74
```

The final candidate gate ran the repository commands below against the exact
final commit:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=macOS-local" \
  --results-directory \
  artifacts/test-results/2026-08-20-native-contract-final-13
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Observed results:

- Locked restore and format verification passed.
- All 26 projects built in Release with 0 warnings and 0 errors.
- Structured parsing of 12 fresh TRX files reported `1667` total, `1667`
  executed, `1667` passed, and zero failed, error, timeout, aborted,
  inconclusive, passed-but-aborted, not-runnable, not-executed, disconnected,
  warning, completed, in-progress, or pending tests.
- Per-project results were Desktop 402, Integration 338, Transport 300,
  Platform 219, Security 125, Release 71, Domain 60, Protocol 59,
  Platform.Linux 27, Platform.Windows 27, Platform.macOS 25, and mDNS 14.
- Explicit TEST MODE Desktop composition passed.
- The deterministic simulator reported protocol `1.5`, source preserved,
  target resumed, and atomic swap committed.
- The direct/transitive vulnerability audit covered all 26 projects and found
  no known vulnerable NuGet package. `git diff --check` passed.

Additional contention gates ran in separate testhost processes:

- At the feature implementation commit, the complete Platform suite passed
  `219/219` with `DOTNET_PROCESSOR_COUNT=1`.
- Twelve high-risk source, frame, protection, Emergency Stop, and controller
  cross-disposal scenarios passed `240/240` across 20 independent single-core
  testhosts. This includes registry-capacity pressure and stale-context drain
  regressions, not only happy-path disposal.
- At the final commit, the Windows-exposed blocking Shell lifecycle scenario
  passed `30/30` across 30 independent testhosts.

The fresh ignored TRX directories are:

```text
artifacts/test-results/2026-08-20-native-contract-final-12-single-core
artifacts/test-results/2026-08-20-native-contract-final-12-risk
artifacts/test-results/2026-08-20-native-contract-final-13
artifacts/test-results/2026-08-20-native-contract-final-13-shell-risk
```

These are local diagnostic evidence and do not stand in for another operating
system or a native adapter.

## Windows scheduling finding

The first Windows attempt for feature run
[`32316907265`](https://github.com/happys2333/flowspan/actions/runs/32316907265),
job
[`96271017976`](https://github.com/happys2333/flowspan/actions/runs/32316907265/job/96271017976),
timed out in
`DisposeStartsPairingTeardownWhenRemoteWindowDisposeBlocksSynchronously`.
Ubuntu and macOS passed, and a retry on the same source commit passed, but the
transient retry was not treated as sufficient evidence.

The initial finding was that the test intentionally blocked thread-pool work
while waiting for another thread-pool continuation on the constrained Windows
runner. Commit `5cf76ff6d3621cd61f2f248ecc498e8941c73a74` moved that path and
the six related blocking Shell lifecycle paths to dedicated test threads. The
production implementation and two-second assertions were unchanged. The fresh
30-process local stress gate and the all-new hosted exact-commit run below then
passed.

### 2026-08-28 correction

The narrower causal conclusion above was incomplete. Docs-only branch head
`d38d6833f6e45171928158cf49eb39d1d1bc09c3` left production behavior unchanged,
but Windows job
[`98738334203`](https://github.com/happys2333/flowspan/actions/runs/33136757827/job/98738334203)
in CI run
[`33136757827`](https://github.com/happys2333/flowspan/actions/runs/33136757827)
again timed out before
`DisposeStartsPairingTeardownWhenRemoteWindowDisposeBlocksSynchronously`
observed the Remote Window disposal boundary. macOS and Ubuntu passed that run,
and exact-head CodeQL run
[`33136757788`](https://github.com/happys2333/flowspan/actions/runs/33136757788)
passed.

Commit `5cf76ff6d3621cd61f2f248ecc498e8941c73a74` isolated the blocking
test callers, but the production Shell still scheduled both one-time Remote
Window and local-pairing safety disposal delegates through shared-pool
`Task.Run`. The corrective candidate moves those two potentially synchronously
blocking external prefixes to dedicated `LongRunning | DenyChildAttach` workers
and adds deterministic assertions that both safety paths start outside the
shared pool. Local focused tests passed `3/3`, Desktop passed `449/449`, and the
Release solution passed `2096/2096`. Hosted exact-commit evidence for the
correction remains pending.

## Hosted exact-commit evidence

Final commit `5cf76ff6d3621cd61f2f248ecc498e8941c73a74` passed
[CI run `32318034236`](https://github.com/happys2333/flowspan/actions/runs/32318034236),
attempt 1:

- Windows test job
  [`96274390540`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96274390540);
- Ubuntu test job
  [`96274390565`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96274390565);
- macOS test job
  [`96274390624`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96274390624);
- Secret Scan job
  [`96274390454`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96274390454);
- `win-x64` package job
  [`96275342351`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96275342351);
- `osx-arm64` package job
  [`96275342365`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96275342365);
- `linux-x64` package job
  [`96275342430`](https://github.com/happys2333/flowspan/actions/runs/32318034236/job/96275342430).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.5 simulator, and uploaded TRX evidence. Every
package job verified content-locked tooling, published and smoke-tested a
self-contained target, sealed and compared two reproducible unsigned outputs,
audited direct/transitive dependencies, and uploaded one test package.

Downloaded TRX and Secret Scan artifacts were parsed with XML and JSON parsers.
`Artifact digest` is GitHub's service-computed SHA-256. `Tree SHA-256` hashes a
sorted manifest of every extracted relative path and file SHA-256.

| Artifact | ID | Artifact digest | Tree SHA-256 | Parsed result |
| --- | ---: | --- | --- | --- |
| Windows TRX | `9388891276` | `1ee95dad7eb7791b5aefa50cc5990f1581777e4460ea5dffcae4ac300d36429b` | `76bc7dd1492622d1ff46145b50cbf0dfe69f5a3c1cfba1ef446c43ab8c90477c` | 12 files, 1667/1667 passed |
| macOS TRX | `9388845998` | `c51f092c29c8aca6e1c8c65029df5f424000416c53db65d3fb05b9c30d15f910` | `b5581fe125cf68f6c5f2404dc199a666a42f6ea3c7ca13e4caa0c42576a3e007` | 12 files, 1667/1667 passed |
| Ubuntu TRX | `9388844465` | `fab889a9687df381e574a0a9043062210f3321bec722d0e5bb8b33f6f18d4fc7` | `d1421f5c99a0f1e3bb35d88e6e3a7155667c5bea0e153790053ac14991bde15b` | 12 files, 1667/1667 passed |
| Gitleaks SARIF | `9388788131` | `318b124b7977127deb91b7ac333fd2ecec1b297369b7b6e7c3e604e1cc59aaea` | `30b18e59f84d7bf1dc7e3eb60edbec4f2598eeea70e38fed7af10f1f47fb3c5c` | SARIF 2.1.0, 208 rules, 0 results |

All three TRX aggregates reported the same 12 assemblies and zero unsuccessful
or indeterminate counters; the 36 files total `5001/5001` passes. Gitleaks SARIF
contains no invocation record, so its execution success is established by the
hosted Secret Scan job, not inferred from the zero-result SARIF alone.

All three downloaded package directories independently passed
`Flowspan.Release verify`, and all 15 entries across their three `SHA256SUMS`
files passed. Their in-toto/SLSA v1 provenance binds version `0.1.133`, exact
commit `5cf76ff6d3621cd61f2f248ecc498e8941c73a74`, CI run `32318034236`,
attempt 1, the expected RID, and the named hosted builder.

| Package | ID | Artifact digest | Tree SHA-256 | Inner archive bytes / SHA-256 |
| --- | ---: | --- | --- | --- |
| `win-x64` | `9388944739` | `b42c66a02a809b4b31e28eec25a0428ac37a1547770c5c964b5c45fef3dae5f3` | `f9bd8eb32b183ea47c5e0e12dc31a968d46c9164722cfc9d34a0b01231e8706f` | 43,878,638 / `75744b6114acf91b47ce96351ab9bde88bbb7bcbb73b4fbe3e82e639e9fc04e6` |
| `osx-arm64` | `9388922397` | `ab0d0adfad9623eb7db861d918f01398e0dc804d8ae0a28f4eee8e31d2a45b97` | `8e467c3152f6a8959e0d237d8971558de8ff333a821d64fd92d86b1e134dbc40` | 42,709,993 / `82a4fafaf7a7b06c7c363503dbadad3afd442f5f881051458cc08dc587a8aff3` |
| `linux-x64` | `9388922896` | `632e2043bcdd5e46bc3df4d019f0f58bd51cb8209c776a0b91ee16fa392d604b` | `a59cbf495a06b93e3fe94769418c5ea1caee2e8ba3ffb6e5a0de2969dcd70911` | 41,893,553 / `f42f2fabae1b6e49e66d4f53e480c75c256bc581dfe96d72a49edf92ce07658f` |

Each SPDX 2.3 SBOM contains 38 packages and 38 relationships: one application,
three direct dependencies, and 34 transitive dependencies. Each license report
contains the application plus 37 dependency entries and remains
`reviewRequired=true`: the application license is undeclared and nine dependency
entries require human review. Every package is explicitly
`unsigned-test-artifact`; none is a signed or notarized release installer.

[CodeQL run `32318034225`](https://github.com/happys2333/flowspan/actions/runs/32318034225),
job
[`96274390449`](https://github.com/happys2333/flowspan/actions/runs/32318034225/job/96274390449),
also passed for the exact final commit. CodeQL 2.26.3 analysis `1644520381`
evaluated 52 rules and reported 0 results; the branch had 0 open alerts at the
time of verification.

These hosted results prove portable build, contract, headless composition, and
reproducible unsigned-package behavior on the named runner images. A
platform-named contract assembly executing on a hosted runner is not evidence
that a native capture, input, protection, permission, or physical-device path
ran.

## Open evidence

- Task 3 must compose Remote Window into the production authenticated control
  session without competing read loops.
- Tasks 4 and 5 must freeze production media/codec decisions and compose the
  production Desktop runtime.
- Tasks 6-8 must implement and prove native Windows, macOS, Wayland, and explicit
  X11 slices on matching real machines.
- Task 9 must provide cross-platform fault/load measurements and physical
  two-Device evidence for exact signed or notarized package digests.
- Human license review, signed/notarized installation lifecycle, native
  accessibility, external security review, affected parent tasks, release
  criteria, task 9.4, and the long-term v1 Goal remain open.

This record supports completion of native Remote Window task 2 only. It does
not close tasks 3-10, any native/physical release criterion, or Flowspan v1.
