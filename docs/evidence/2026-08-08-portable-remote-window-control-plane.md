# Portable Remote Window Control-Plane Evidence - 2026-08-08

## Evidence boundary

This record closes the local automated evidence for the portable portions of
Remote Window tasks 2 and 3: one-Activity live session control, current
Capability checks, monotonic Driver authority, bounded remote input, protection
pause/resume convergence, local Emergency Stop, and payload-free failure
results. It does not implement or prove an authenticated Remote Window wire
protocol, media transport, Desktop sharing workflow, native capture/input or
protection APIs, physical emergency action, permissions, accessibility, or
two-device behavior.

Branch: `codex/v1-foundation`

The local worktree was based on
`82f34cdbfe602efeffe4007ac38fb58db4b028b7`. The resulting implementation
commit is `d19cfea8a06dfec13d298ba2630916dc5e3bbf33` (`feat: add
fail-closed Remote Window control plane`). Its exact-commit hosted evidence is
recorded below.

## Implemented contract

- One controller owns one active local Activity and composes capture, remote
  input, current authorization, local sharing-session, `MirrorSession`, and
  `DriverLease` boundaries without persisting live authority.
- View admission requires current `mirror.view`; Driver admission, transfer,
  and input require one current immutable grant containing both `mirror.view`
  and `mirror.drive`. Input also requires the exact unexpired Driver epoch and
  a fresh Safe protection observation.
- Sessions admit at most 16 participants. Input batches contain 1-64 closed,
  defensively copied HID key, normalized pointer, button, or bounded scroll
  events. Results and display strings contain no raw input or Activity payload.
- Every protection observation advances an internal monotonic revision. Only a
  current revision may publish Active or confirmed Paused state. Re-entrant and
  concurrent supersession, late capture start, and local gate failures converge
  to the latest observation; eight unstable attempts fail closed. A partial
  resume failure re-pauses both gates before returning the original failure.
- Emergency Stop latches and revokes Driver authority before local boundaries,
  runs capture/input/session gates despite individual failures, has no peer ACK
  boundary, wins pending start/input/protection races, and is re-applied if a
  stale protection boundary ran after the stop.

## Local environment and commands

```text
Host: macOS 26.6.1 (build 25G76), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
Verification time: 2026-08-08T08:27:43Z
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=macOS-local" \
  --results-directory <fresh-directory>
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

## Local results

- Locked restore and format verification passed.
- Release build passed for all 26 projects with 0 warnings and 0 errors.
- Structured parsing of 12 fresh TRX files reported 1257 total, 1257 executed,
  1257 passed, 0 failed, 0 error, 0 timeout, 0 aborted, 0 inconclusive, and 0
  not executed.
- Per-project results were Desktop 201, Domain 60, Integration 338, shared
  Platform 69, Linux contracts 27, macOS contracts 25, Windows contracts 27,
  Protocol 47, Release 71, Security 123, mDNS Transport 14, and Transport 255.
- The controller suite includes deterministic public-interface tests for
  same-thread re-entry, a blocked cross-thread Safe/unsafe race, late Start
  convergence, bounded 64-transition protection churn, partial-resume
  reclosure, stale gate failure, and Emergency Stop after a stale resume.
  Sixteen fixed seeds still execute 48 mixed control transitions and retry
  every retired Device/epoch through the public input API.
- The five protection supersession/churn/Emergency regression tests passed in
  20/20 fresh test processes (100 focused test executions) without a failure.
- Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`
- The deterministic simulator printed protocol `1.4`, source preserved,
  target resumed, and atomic swap committed, with a redacted receipt.
- NuGet reported no known vulnerable direct or transitive package in any of the
  26 projects. `git diff --check` passed.
- Fresh TRX files were parsed from
  `/var/folders/rf/dgnm75850p1_wm033h90780c0000gn/T/flowspan-remote-window-trx-XXXXXX.U5fENsiaS1`.
  This volatile local path is diagnostic evidence, not a retained release
  artifact.
- No local `gitleaks` executable was installed. No local secret-scan result is
  claimed; the pinned hosted Secret Scan remains mandatory.

The platform-named contract assemblies compiling and passing on this macOS host
do not prove Windows, macOS, or Linux native APIs.

## Hosted exact-commit evidence

Implementation commit `d19cfea8a06dfec13d298ba2630916dc5e3bbf33`
passed [CI run `31249203563`](https://github.com/happys2333/flowspan/actions/runs/31249203563):

- Ubuntu test job [`93082747709`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93082747709);
- macOS test job [`93082747710`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93082747710);
- Windows test job [`93082747713`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93082747713);
- Secret Scan job [`93082747664`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93082747664);
- linux-x64 package job [`93083075259`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93083075259);
- osx-arm64 package job [`93083075260`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93083075260);
- win-x64 package job [`93083075267`](https://github.com/happys2333/flowspan/actions/runs/31249203563/job/93083075267).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the deterministic simulator, and uploaded TRX evidence. Every
package job verified content-locked tooling, published its self-contained
target, validated packaged composition, sealed and verified two reproducible
unsigned outputs, recursively compared them, audited direct/transitive
dependencies, and uploaded the resulting test package.

Downloaded test and Secret Scan artifacts were parsed using XML and JSON
parsers. `Artifact digest` is the SHA-256 reported by the GitHub artifact API;
`tree SHA-256` independently hashes each extracted relative path and file
digest in sorted order.

| Artifact | ID | Artifact digest | Tree SHA-256 | Parsed result |
| --- | ---: | --- | --- | --- |
| Windows TRX | `9019492185` | `5ede4472d2f9b489b68a3db8382c7430a85ecd1d8769a48687338f505401460c` | `d07430d7440e54409e34045bf12864d5174df45589e9ddc9b86ecb9373b3df1f` | 12 files, 1257/1257 passed |
| macOS TRX | `9019474640` | `2732696b33c542ac950da835edcbb548e1e618fe10bbac6ca55a7fa3b3ba8aec` | `0fcf2a97a8cb7511b45458f7ac17b1536d28045a1f395f4a80b8b1879d96b089` | 12 files, 1257/1257 passed |
| Ubuntu TRX | `9019484482` | `c57ab1341998443aa9a55ee0e50c467341fad15ce91298128b50ed48aa988784` | `ce3bd341bda139380286f0eeb7c9bb036f60962a91fd83cd53330b6bd354d207` | 12 files, 1257/1257 passed |
| Gitleaks SARIF | `9019457952` | `1f56bf8a000159c3a05a6cef68d1240bff457bfad07fd4109df095877430ed5b` | `7e69a8c7dbdfd4285adff52c59544be5344adc0e1721b007859738e615735728` | 208 rules, 0 results |

Every platform TRX aggregate also reported 0 failed, error, timeout, aborted,
inconclusive, and not-executed tests.

Package artifact metadata is bound to the same workflow SHA:

| Artifact | ID | Artifact digest |
| --- | ---: | --- |
| win-x64 unsigned test package | `9019515040` | `319c8445656259bd201c86e430351a4253177ddabc5be7c638a35b6ac24b0bed` |
| linux-x64 unsigned test package | `9019510356` | `f44b835879d5862e08c53ca890b257c8a3417b3716b237ffb7a0fb64baeb103e` |
| osx-arm64 unsigned test package | `9019506915` | `c03d6cf5b7197b81e90bcead7c19a430cac70062e536fb0ce231d42dc71afe13` |

[CodeQL run `31249203570`](https://github.com/happys2333/flowspan/actions/runs/31249203570),
job [`93082747557`](https://github.com/happys2333/flowspan/actions/runs/31249203570/job/93082747557),
also passed for the exact implementation SHA. Analysis `1589487041`
evaluated 52 rules and reported 0 results and 0 open branch alerts.

These hosted results prove portable build, control-contract behavior, and
reproducible unsigned packaging on the named runner images. They do not prove
native capture/input/protection, permissions, physical emergency action,
physical two-device networking, or packaged accessibility. Tasks 4-9 in the
Remote Window plan, parent tasks 6.3-6.6, physical task 5.4, accessibility task
7.4, and release tasks 9.3-9.4 remain open.
