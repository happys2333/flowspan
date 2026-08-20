# Native Remote Window Authenticated Control Session Evidence - 2026-08-20

## Evidence status and boundary

Classification: **local portable contract**, **hosted portable contract**,
**headless Desktop**, and **unsigned package**.

Branch: `codex/v1-foundation`

Implementation commit:
`2f52ae46b128bc65797f3f042e17be38b13fef81`, based on
`675959200e86c1b0acfda338a78102a8faba7f2a`.

This record is scoped to task 3 in
`specs/v1/native-remote-window/tasks.md`. It proves that the production
authenticated control registration owns one strict dispatcher for Activity,
Replace, Swap, Scene, and Remote Window control messages, exposes only
protocol-supported channels, rechecks current Capability grants at operation
boundaries, and drains both routes on revocation, reconnect, malformed routing,
or disposal.

This is control-channel evidence. It does not prove a production Remote Window
media route, codec, renderer, native source enumeration, native capture, native
input injection, protected-surface or secure-input detection, operating-system
permission prompts, physical Emergency Stop, signed or notarized packages, or
communication between two physical Devices.

## Implemented contract

- `AuthenticatedControlSessionDispatcher` is the only reader for a production
  authenticated control connection. It routes the frozen Activity, Replace,
  Swap, Scene, and Remote Window message families in receive order. Unknown or
  non-negotiated messages are fatal instead of being consumed by a competing
  handler.
- Activity and Remote Window outbound traffic shares one connection-owned send
  drain. Stop, cancellation, send failure, peer callback reentrancy, nested or
  copied `ExecutionContext`, and disposal cannot leave an admitted send or
  child session outside the terminal cleanup result.
- Protocol 1.0 through 1.4 registrations expose no Remote Window channel.
  Protocol 1.5 exposes the channel without changing any frozen protocol fixture.
- The production any-of admission profile includes Activity grants,
  `scene.apply`, `mirror.view`, and `mirror.drive`. Admission establishes only
  an authenticated idle channel. Activity, Scene, Remote Window viewing, and
  Remote Window driving retain their operation-specific current Trust and
  Capability checks.
- Desktop Remote Window target discovery requires both a live protocol-1.5
  channel and the current `mirror.view` grant. A protocol-1.4 peer and a peer
  whose grant was removed are excluded.
- Handler replacement and shutdown revoke both routes together. Pending
  Remote Window commands are bounded to 16, started sends and peer callbacks
  drain, and stale reconnect state cannot retain participant authority.
- Activity and Remote Window child cleanup failures are both observed. A
  primary handler failure and subsequent cleanup failures are aggregated rather
  than overwritten by a second disposal attempt. Presentation observers cannot
  terminate authenticated dispatch.

The requirements, architecture, ownership rules, and test traceability are in
`specs/v1/native-remote-window/`; the production trusted-reconnect admission
decision is ADR 0008. Task 4 still owns the authenticated media-route and codec
decisions, and task 5 still owns full Desktop host/participant composition.

## Local candidate gates

Environment:

```text
Host: macOS 26.6.1 (build 25G76), Apple Silicon, Asia/Hong_Kong
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Verification date: 2026-08-20
Committed implementation: 2f52ae46b128bc65797f3f042e17be38b13fef81
```

The final candidate gate ran against the exact tree content later committed as
the implementation commit:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=task3-local" \
  --results-directory TestResults/task3-local-20260820-final
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
- Structured XML parsing of 12 fresh TRX files reported `1703` total, `1703`
  executed, and `1703` passed. Failed, error, timeout, aborted, inconclusive,
  passed-but-aborted, not-runnable, not-executed, disconnected, warning,
  completed, in-progress, and pending counters were all zero.
- Per-project results were Desktop 405, Integration 338, Transport 333,
  Platform 219, Security 125, Release 71, Domain 60, Protocol 59,
  Platform.Linux 27, Platform.Windows 27, Platform.macOS 25, and mDNS 14.
- Explicit TEST MODE Desktop composition passed.
- The deterministic simulator reported protocol `1.5`, source preserved,
  target resumed, and atomic swap committed.
- The direct/transitive vulnerability audit covered all 26 projects and found
  no known vulnerable NuGet package. `git diff --check` passed.

Additional fresh-process contention evidence covered the complete Transport
suite. Three implementation-gate reruns and eight independent read-only audit
runs each passed `333/333`; the audit runs left no unfinished test or testhost
process. This repetition exercises the single dispatcher, the 16-slot pending
bound, send/stop/dispose drain, cancellation and peer-callback reentrancy,
nested and copied execution-context ancestry, child cleanup aggregation, and
observer isolation. It does not prove that every possible scheduler interleaving
has been exhausted.

The ignored local TRX directory is:

```text
TestResults/task3-local-20260820-final
```

These are same-host macOS and portable-contract results. The platform-named
test assemblies do not execute native Windows or Linux Remote Window adapters
on this host.

## Hosted exact-commit evidence

Implementation commit `2f52ae46b128bc65797f3f042e17be38b13fef81`
passed
[CI run `32360067150`](https://github.com/happys2333/flowspan/actions/runs/32360067150),
attempt 1:

- Windows test job
  [`96397567152`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96397567152);
- Ubuntu test job
  [`96397567251`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96397567251);
- macOS test job
  [`96397567108`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96397567108);
- Secret Scan job
  [`96397566845`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96397566845);
- `win-x64` package job
  [`96398689367`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96398689367);
- `osx-arm64` package job
  [`96398689376`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96398689376);
- `linux-x64` package job
  [`96398689510`](https://github.com/happys2333/flowspan/actions/runs/32360067150/job/96398689510).

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
| Windows TRX | `9403293851` | `ac02e24471ad702e28281e8a3f0dc0087c447e37c247ba77dbcfe6928ae8303d` | `6afd28dc95796ceb4d8904f2de837f34fd9a417e742b19273455dbee87f99feb` | 12 files, 1703/1703 passed |
| macOS TRX | `9403238606` | `7a913c08059045fe90dd582d5ebf026d8122446db4c2b19ab55140d723d692d7` | `a5881c8d2f80357f6b938d04e2d3465fa337cbb3d99916f43ddeedc28eee7e02` | 12 files, 1703/1703 passed |
| Ubuntu TRX | `9403237143` | `9c67970509abbe8d2ff9bb0d2491bbcb7fafce0137ae4fccb8101238508b1fe2` | `d5e5e18c824b5e408cd2db0ab8e539e573b4fb1f778ff285671061e0faaa1c62` | 12 files, 1703/1703 passed |
| Gitleaks SARIF | `9403165144` | `8e2f022d931aa03da95ab2e9bd83a0e9e219b875a1a9714785a94142bfef0c4b` | `30b18e59f84d7bf1dc7e3eb60edbec4f2598eeea70e38fed7af10f1f47fb3c5c` | SARIF 2.1.0, 208 rules, 0 results |

All three TRX aggregates reported the same 12 assemblies and zero failed,
error, timeout, aborted, inconclusive, passed-but-aborted, not-runnable,
not-executed, disconnected, warning, completed, in-progress, or pending tests.
The 36 files total `5109/5109` passes. Gitleaks SARIF contains no invocation
record, so its execution success is established by the hosted Secret Scan job,
not inferred from the zero-result SARIF alone.

The three downloaded package directories independently passed
`Flowspan.Release verify`, and all 15 entries across their three `SHA256SUMS`
files passed. Their in-toto/SLSA v1 provenance binds version `0.1.136`, exact
commit `2f52ae46b128bc65797f3f042e17be38b13fef81`, CI run `32360067150`,
attempt 1, the expected RID, and the named hosted builder.

| Package | ID | Artifact digest | Tree SHA-256 | Inner archive bytes / SHA-256 |
| --- | ---: | --- | --- | --- |
| `win-x64` | `9403372693` | `b9debc51befb12b3b2b7578227762a84edac3af9fc56628af9a72d05a1f110fa` | `f9c22531fdbf932461caed21353a1bbbfd6c4bae79993b8cfdb5bc2393707dd1` | 43,888,856 / `17b5827650c1242513fbe0ace8303ebcf48feca45cb2f327fb87a989fda96640` |
| `osx-arm64` | `9403354002` | `85b9cb6f73990d333674b8b6dc9b76784cfaf31203427345fd1d66a0e9dabcf3` | `d22cff8886745cbd67fdd326caee154eabd0608b5ba21ebbbbe10c03867d7212` | 42,723,473 / `e9a5d9db97050643168b3f10133f38a4e5964dc74132d3c595c4a9ed15b9e1f6` |
| `linux-x64` | `9403342483` | `f70bf5c4f9bb19efe470d9597d48c2392d21417c03066529a3f844b1f8f0df6c` | `07e1c5a9c1ad5df3440313d41c77c91be259c5a1bd021779ff0638326f96a34b` | 41,903,439 / `635ebc1e491cc011e292607f81124268950580dfd4888eb4de7e03165063f705` |

Each SPDX 2.3 SBOM contains 38 packages and 38 relationships: one application,
three direct dependencies, and 34 transitive dependencies. Each license report
contains the application plus 37 dependency entries and remains
`reviewRequired=true`: the application license is undeclared and nine dependency
entries require human review. Every package is explicitly
`unsigned-test-artifact`; none is a signed or notarized release installer.

[CodeQL run `32360067140`](https://github.com/happys2333/flowspan/actions/runs/32360067140),
job
[`96397567116`](https://github.com/happys2333/flowspan/actions/runs/32360067140/job/96397567116),
also passed for the exact implementation commit. CodeQL 2.26.3 analysis
`1646580910` scanned 343/343 C# files, evaluated 52 rules, and reported 0
results; the branch had 0 open alerts at the time of verification.

These hosted results prove portable build, contract, headless composition, and
reproducible unsigned-package behavior on the named runner images. A
platform-named contract assembly executing on a hosted runner is not evidence
that a native capture, input, protection, permission, or physical-device path
ran.

## Independent implementation audit

A final read-only concurrency review reported no findings. It checked the
single-reader dispatcher, protocol gate, current-Capability rechecks,
Activity/Remote Window coexistence, malformed cross-routing, revoke/drain,
reconnect authority replacement, and send, disconnect, cancellation, cleanup,
and disposal reentrancy coverage. Hosted exact-commit evidence remained the
only Task 3 closure gate at the end of that review.

## Open evidence

- Task 4 must freeze the authenticated media-route purpose and binding, codec,
  quality ladder, decoder limits, hostile-media behavior, and frozen fixtures.
- Task 5 must compose the production Desktop Remote Window host and participant
  runtime. Production sharing remains unavailable until that complete path is
  ready.
- Tasks 6-8 must implement and prove native macOS, Windows, Wayland, and explicit
  X11 permission, capture, input, protection, and Emergency Stop paths on
  matching real machines.
- Task 9 must provide cross-platform fault/load measurements and physical
  two-Device evidence for exact signed or notarized package digests.
- Human license review, signed/notarized installation lifecycle, native
  accessibility, external security review, affected parent tasks, release
  criteria, task 9.4, and the long-term v1 Goal remain open.

This record supplies Task 3 implementation evidence. Task 3 remains open until
this evidence document itself passes exact-commit CI and CodeQL. It does not
close tasks 4-10, any native or physical release criterion, or Flowspan v1.
