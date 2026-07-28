# Deterministic Distribution Evidence — 2026-07-28

## Evidence boundary

This record closes the automated engineering and hosted-artifact portions of
tasks 9.1 and 9.2. It covers deterministic unsigned test archives, locked
self-contained publishing, package verification, supply-chain records, named
artifacts, and exact-commit hosted results for `win-x64`, `osx-arm64`, and
`linux-x64`.

All local results are macOS-host same-host evidence. Hosted results are
GitHub-hosted runner-image evidence. Neither is physical two-device evidence,
native installer evidence, real signing or notarization evidence, native
accessibility evidence, nor a production release endorsement.

Branch: `codex/v1-foundation`

Final implementation commit:
`6ceb7e128b33898cc5e258dc2c11d88e92ab2ff2`
(`fix: enforce archive extraction confinement`).

The closure recorded by the task-status commit containing this record is
effective only after that commit passes CI, Secret Scan, and CodeQL.

## Implemented contract

- Matching hosted runners publish one self-contained single-file target for
  `win-x64`, `osx-arm64`, and `linux-x64`; package smoke runs with shared .NET
  runtime discovery disabled.
- Host/runtime 10.0.10 packages and the ILLink build package are independently
  content-locked before publish or metadata generation.
- Stage v2 binds every file, rejects unsupported file kinds and links, and
  confines extraction before every archive write.
- ZIP and USTAR/gzip output fixes timestamps, permissions, paths, owner fields,
  line endings, creator platform, and gzip operating-system bytes.
- Unsigned seals permit no stage drift. Verified seals admit only the declared
  signing boundary and require a tree-bound structured verification report.
- SPDX 2.3, license inventory, SLSA provenance, inert update data, and
  `SHA256SUMS` cross-bind the archive, source commit, RID, and package graph.

## Local environment and gates

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
RID: osx-arm64
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger 'trx;LogFilePrefix=Local' --results-directory <temporary-directory>
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The final local gate used `/tmp/flowspan-final-trx-QzG6OS` and passed locked
restore, formatting, a Release build with 0 warnings and 0 errors, explicit
TEST MODE Desktop composition, the deterministic simulator, the dependency
vulnerability query, and `git diff --check`.

Independent summation of all local TRX `Counters` elements produced:

```text
files=12 total=1225 executed=1225 passed=1225 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 completed=0 inProgress=0 pending=0
```

The 71 Release tests include canonical LF JSON, fixed ZIP/gzip platform bytes,
repeated byte-identical seals, hostile archive metadata, traversal confinement,
links, devices, sockets, FIFOs, bounds, tamper, and companion-record binding.

## Hosted exact-commit evidence

Final commit `6ceb7e128b33898cc5e258dc2c11d88e92ab2ff2` passed
[CI run `30337625335`](https://github.com/happys2333/flowspan/actions/runs/30337625335):

- [macOS test job `90205877402`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90205877402);
- [Ubuntu test job `90205877418`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90205877418);
- [Windows test job `90205877457`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90205877457);
- [Secret Scan job `90205877359`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90205877359);
- [Linux package job `90206561691`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90206561691);
- [macOS ARM64 package job `90206561762`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90206561762);
- [Windows package job `90206561772`](https://github.com/happys2333/flowspan/actions/runs/30337625335/job/90206561772).

Each package job restored locked dependencies, verified content-locked build
tooling, published one PDB-free self-contained file, prepared a bounded stage,
smoke-ran the packaged entry point with empty `DOTNET_ROOT` values, sealed and
verified two byte-identical outputs, audited dependencies, and uploaded a named
`unsigned-test-artifact`.

[CodeQL run `30337625422`](https://github.com/happys2333/flowspan/actions/runs/30337625422),
job [`90205877675`](https://github.com/happys2333/flowspan/actions/runs/30337625422/job/90205877675),
also passed. Analysis `1536294070` evaluated 52 rules and reported 0 results
and 0 open branch alerts. The downloaded Gitleaks SARIF contained 208 rules and
0 results.

## Downloaded artifacts

GitHub reported these artifact IDs and service-computed SHA-256 digests:

| Artifact | ID | SHA-256 |
| --- | ---: | --- |
| Windows package | `8679904700` | `24467e77ca749c752fb0339191b0809720f4dea54059a2559b887a9358b52f98` |
| macOS package | `8679877381` | `ab5240694d067c7e8a5a3ab0198568982e32a920b624d8610e07c6047863f6f9` |
| Linux package | `8679876740` | `c6685c862f727b45983bc1cdbbab15f4d5000bc9cbe9de0a0898d878425563aa` |
| Windows TRX | `8679841698` | `51f974a5b6ee3fe0aa3d977df306bf7fdc4eba4748cf0b85a80075a2e872c9e7` |
| macOS TRX | `8679820588` | `d93c54d7e8c992db8ebb93fe4f54894f5cf3b0eeefc8c1156a7865bd0d155b86` |
| Linux TRX | `8679809086` | `bcecb1e7dd07d89a7acc84c0b2e151db2077550f70daed88f7346a560f42a35c` |
| Gitleaks SARIF | `8679766562` | `221e996611159e887b8f74ffae4de5c481b84eeb3b33df7c405c663826092c02` |

The artifacts were downloaded under
`/tmp/flowspan-ci-30337625335-PSF6wr/download`. The repository's final
`Flowspan.Release verify` command independently accepted all three extracted
package directories on the macOS host.

| RID | Inner archive bytes | Inner archive SHA-256 | Builder |
| --- | ---: | --- | --- |
| `linux-x64` | 41,733,518 | `b7c1869c94a229ccd5a5d6d3ae58075f012836c5988d76ffab60c8acda501b88` | `github-hosted:ubuntu-latest` |
| `osx-arm64` | 42,552,468 | `f16464057da6b1f3ca12b21b1fcfde0c893e7d4bc188e77325f8de9cc157f35a` | `github-hosted:macos-15` |
| `win-x64` | 43,722,126 | `095e7714f18d90a4e2dd846c1250aa78d4d99afabf4539a0971625d44885b7ed` | `github-hosted:windows-latest` |

Every package is version `0.1.120`, binds final commit `6ceb7e1`, uses minimum
supported version `0.1.0`, and is explicitly marked
`unsigned-test-artifact`. Each license record reports `packageCount=38`: the
Flowspan application plus 37 dependency entries. Each selected Host and Runtime
package is version 10.0.10.

Independent summation of every downloaded TRX `Counters` element produced the
same result on Windows, macOS, and Ubuntu:

```text
files=12 total=1225 executed=1225 passed=1225 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 completed=0 inProgress=0 pending=0
```

## Red-to-green findings

- CI run `30333736075` exposed platform-dependent canonical JSON newlines on
  Windows. The attribute-only repair at `794467e` did not fix that runtime
  serialization behavior. Commit `910ce05` fixed `JsonSerializerOptions.NewLine`
  to LF and added a direct regression test.
- Independent verification of run `30334496588` artifacts then exposed one-byte
  host defaults in ZIP creator-platform and gzip operating-system fields.
  Commit `f6ee267` normalizes both fields and locks them with container-header
  tests; downloaded Windows, Linux, and macOS artifacts now verify on the macOS
  host without mutation.
- `macos-15-arm64` never acquired a runner because it is an image-document name,
  not the current standard runner label. Commit `a5b0daa` selects `macos-15`,
  whose package provenance confirms `github-hosted:macos-15` for `osx-arm64`.
- CodeQL analysis of `a5b0daa` found one High `cs/zipslip` result at the final
  extraction write. Commit `6ceb7e1` adds a full canonical destination-root
  prefix check immediately before file creation plus a no-outside-write
  regression. Final analysis `1536294070` reports 0 results.

## Remaining gates

- These are unsigned archives, not signed, notarized, or installer packages.
- Real-machine install, launch, permission, accessibility, LAN, failure,
  lifecycle, upgrade, and uninstall evidence remains open under task 9.3.
- Physical two-device behavior and native packaged-window behavior remain open.
- Repository license policy remains unresolved, so the generated license report
  honestly keeps `reviewRequired=true` for the application entry.
- Task 9.4 and the long-term Goal remain open until every v1 release criterion,
  known limitation, and real-machine gate is closed.
